# -*- coding: utf-8 -*-
"""
【このファイルの役割】
段階3d（実験）：LLM に「次の N 手」をまとめて考えさせ、1手ずつ実行する係。
2b・3c の選ぶ係と同じ形（select）なので、step3a_state.run_loop にそのまま差し込める（1手ごとに select が呼ばれる）。

2つの動き方（PLAN.md 段階3d）:
- overlap なし：手が尽きたら、ゲームを止めて次の計画を頼み、届いてから続ける
- overlap あり（考えながら進める）：計画の1手目を始めると同時に、次の計画を別の流れ（スレッド）で頼む。
  今の計画を実行し終えたとき、次の計画が届いていればそのまま続け、まだなら届くまでゲームを止めて待つ
割り込みは入れない（ユーザー判断）。

LLM に渡す文章は「手」と「座標の距離」だけで書く（unity/state_summary.py の summarize_for_plan）。
計画が始まる時点の自分の位置は、今の計画の残りの手から予測して伝える。

返事の扱い（3c と同じ考え方）:
- 正しい返事（commands が N 個、すべて選択肢の中）→ 計画として使う
- 形がおかしい → 1回だけ聞き直す。それでもだめなら、計画なし（手が尽きていれば最後の手を続ける）
- 一時的なエラー → 計画なし（同上）
- 続けても意味のないエラー（キーが無効、残高不足、回数上限など）→ 次の select で呼び出し元へ投げる（終了する）

【どこから使われるか】
step3c_llm_play.py（--plan-steps / --overlap を付けたとき）。
"""
import json
import time
from concurrent.futures import ThreadPoolExecutor
from typing import Any, Dict, List, Optional

import settings
from command.selector import CommandSelector
from command.llm_selector import TRANSIENT_KINDS
from command.test_commands import movement_of
from llm.base import LLMClient, LLMError
from unity.state_summary import _pos, predict_self, step_distance, summarize_for_plan


def build_plan_system_prompt(names, steps, step_frames, with_reason, step_reasons=False):
    # type: (List[str], int, int, bool, bool) -> str
    """数手まとめて考えさせるときの「固定の説明」（ルール、目的、手の単位、選択肢、返す形）を作る。秒は使わず「手」で書く。

    step_reasons が True なら、手ごとに理由（why）を書かせる形にする（2026-11-12 ユーザー案。理由が1つだけだと
    考えが大まかになり、同じ手が続きやすかったため）。このとき返す形の例には具体的な手を入れない
    （例の手の並びが答え方に影響しないように）。
    """
    d = step_distance(step_frames)
    if step_reasons:
        one = '{"command": "選択肢のどれか", "why": "その手を選んだ理由（15文字程度）"}'
        form = '{{"steps": [{}]}}'.format(", ".join([one] * steps))
    else:
        example = ", ".join('"{}"'.format(n) for n in (names[3], names[3], names[5], names[0], names[4])[:steps])
        if with_reason:
            form = '{{"commands": [{}], "reason": "20文字程度の理由"}}'.format(example)
        else:
            form = '{{"commands": [{}]}}'.format(example)
    count_note = "steps" if step_reasons else "commands"
    return "\n".join([
        "あなたは縦スクロールのシューティングゲームをプレイしています。あなたは画面下側の自機を操作します。",
        "- 自機は前方（上）へショットを撃ち続けています。",
        "- 敵の弾に当たると、画面下の外から復活します。",
        "- 座標は右が x のプラス、上が y のプラスです。自機は壁の外へは出られません（壁で止まります）。",
        "目的（優先順）：",
        "1. 敵の弾に当たらないように避ける",
        "2. 敵を倒す（ショットは上にしか出ないので、敵の真下あたりに位置取ると倒しやすい）",
        "操作のしかた：",
        "- 時間は「手」で数えます。自機は1手で約{:.1f}進みます（斜めは横・縦それぞれ約{:.2f}）。".format(d, d / 2 ** 0.5),
        "- あなたは次の{}手を一度に決めます。決めた手は、状況に書かれた「次の計画の開始位置」から、1手ずつ順に実行されます。".format(steps),
        "- 計画を実行しているあいだは、新しい状況を見られません。状況に書かれた予測（弾が1手で進む向きと距離、近づく手）を使って、先を見越して選んでください。",
        "選択肢：" + "／".join(names),
        "日本語で、次の形の JSON だけを返してください（{} はちょうど{}個）：".format(count_note, steps) + form,
    ])


class LLMPlanRunner(CommandSelector):
    """LLM に数手まとめて考えさせ、1手ずつ返す係（select は1手ごとに呼ばれる）。"""

    name = "llm_plan"

    def __init__(self, client, names, steps=5, step_frames=12, overlap=True, with_reason=True, max_reask=1,
                 step_reasons=False):
        # type: (LLMClient, List[str], int, int, bool, bool, int, bool) -> None
        """LLM のクライアント、選択肢、1回で何手か、1手のフレーム数、考えながら進めるか、手ごとに理由を書かせるか、などを受け取る。"""
        self.client = client
        self.names = list(names)
        self.steps = steps
        self.step_frames = step_frames
        self.overlap = overlap
        self.with_reason = with_reason
        self.step_reasons = step_reasons
        self.max_reask = max_reask
        if step_reasons:
            self.max_tokens = 30 + 40 * steps
        else:
            self.max_tokens = (40 + 12 * steps) if with_reason else (16 + 12 * steps)
        self.system_prompt = build_plan_system_prompt(self.names, steps, step_frames, with_reason, step_reasons)
        self.queue = []          # type: List[str]  # これから実行する手
        self.last_move = None    # type: Optional[str]
        self.pending = None      # 別の流れで頼んでいる計画（Future）
        self.pending_info = {}   # type: Dict[str, Any]
        self._pool = ThreadPoolExecutor(max_workers=1) if overlap else None
        self.last = {}           # type: Dict[str, Any]  # 直前の select の内容（画面表示と記録用）
        # 集計
        self.calls = 0           # 計画を頼んだ回数（聞き直しは含まない）
        self.ok = 0              # 正しい計画を受け取った回数
        self.failed = 0          # 計画を受け取れなかった回数（エラー・形の誤り）
        self.reasks = 0
        self.errors = {}         # type: Dict[str, int]
        self.latencies = []      # type: List[float]
        self.input_tokens = 0
        self.output_tokens = 0
        self.waits = 0           # 次の計画を待ってゲームを止めた回数
        self.wait_sec = 0.0      # 止めて待った合計の実時間
        self.picked = {}         # type: Dict[str, int]

    # ---- 1手ごとに呼ばれる ----
    def select(self, context=None):
        # type: (Optional[Dict[str, Any]]) -> Optional[str]
        """次の1手を返す。手が尽きていれば次の計画を受け取る（まだなら待つ）。計画の1手目なら、次の計画を頼み始める。"""
        context = context or {}
        self.last = {}
        new_plan = False
        if not self.queue:
            plan, info = self._get_next_plan(context)
            self.last.update(info)   # 受け取れなかったときも、エラーの種類を表示と記録に残す
            if plan:
                self.queue = list(plan)
                new_plan = True
        if not self.queue:
            # 計画が得られなかった：最後の手を続ける（最初から得られなければ呼び出し元が「止まる」にする）
            self.last["kept"] = True
            return None
        move = self.queue.pop(0)
        self.last_move = move
        self.picked[move] = self.picked.get(move, 0) + 1
        self.last["move"] = move
        self.last["new_plan"] = new_plan
        if new_plan and self.overlap and self.pending is None:
            # 1手目を始めると同時に、次の計画を頼む。開始位置は「今の位置＋この手を含む残りの手」から予測する
            remaining = [move] + list(self.queue)
            self._start_request(context, remaining)
        return move

    # ---- 計画の受け取り ----
    def _get_next_plan(self, context):
        """次の計画を受け取る。頼んでいれば届くまで待ち（ゲームは止まる）、頼んでいなければ今すぐ頼んで待つ。"""
        if self.pending is None:
            self._start_request(context, [])
        t0 = time.time()
        done = self.pending.done()
        result = self.pending.result()   # 届くまで待つ。続けても意味のないエラーはここで投げられる
        waited = time.time() - t0
        if not done:
            self.waits += 1
            self.wait_sec += waited
        info = dict(self.pending_info)
        info["waited_s"] = round(waited, 3)
        self.pending = None
        plan, extra = result
        info.update(extra)
        return plan, info

    def _start_request(self, context, remaining_moves):
        """次の計画を頼み始める。overlap なら別の流れで、なしならその場で（すぐ結果を受け取れる形にして）頼む。"""
        record = context.get("record")
        now = _pos((record or {}).get("self"))
        moves = [movement_of(m) for m in remaining_moves]
        start = predict_self(now, moves, self.step_frames) if now is not None else None
        steps_until = len(remaining_moves)
        events = list(context.get("events") or [])
        self.pending_info = {"requested_frame": context.get("frame"), "steps_until_start": steps_until,
                             "start_pos": [round(start[0], 2), round(start[1], 2)] if start else None}
        if self._pool is not None:
            self.pending = self._pool.submit(self._request, record, events, start, steps_until)
        else:
            self.pending = _Done(self._request(record, events, start, steps_until))

    def _request(self, record, events, start, steps_until):
        """LLM に計画を頼んで、（手の並び or None, 詳しい内容）を返す。別の流れから呼ばれることがある。"""
        summary = summarize_for_plan(record, events, start, steps_until, self.step_frames, self.steps)
        messages = [{"role": "system", "content": self.system_prompt},
                    {"role": "user", "content": "今の状況：\n" + summary}]
        self.calls += 1
        info = {"plan": None, "reason": None, "error": None, "latency_s": 0.0, "tries": 0}
        for attempt in range(1 + self.max_reask):
            info["tries"] = attempt + 1
            try:
                resp = self.client.complete(messages, max_tokens=self.max_tokens, json_mode=True)
            except LLMError as e:
                if e.kind in TRANSIENT_KINDS:
                    self.errors[e.kind] = self.errors.get(e.kind, 0) + 1
                    self.failed += 1
                    info["error"] = e.kind
                    return None, info
                raise
            self.latencies.append(resp.latency_s)
            info["latency_s"] += resp.latency_s
            self.input_tokens += resp.input_tokens or 0
            self.output_tokens += resp.output_tokens or 0
            plan, reason, problem = self._parse(resp.text)
            if plan is not None:
                self.ok += 1
                info.update({"plan": plan, "reason": reason})
                return plan, info
            info["error"] = problem
            if attempt < self.max_reask:
                self.reasks += 1
                messages = messages + [
                    {"role": "assistant", "content": resp.text or ""},
                    {"role": "user", "content": "{}。選択肢：{} から、ちょうど{}手分を、指定の JSON の形だけで返してください。".format(
                        problem, "／".join(self.names), self.steps)},
                ]
        self.failed += 1
        return None, info

    def _parse(self, text):
        """返事の文字列を確かめる。（手の並び, 理由, 問題の説明）を返す。だめなら手の並びは None。"""
        if not text:
            return None, None, "返事が空でした"
        try:
            data = json.loads(text)
        except ValueError:
            return None, None, "JSON として読めませんでした"
        if not isinstance(data, dict):
            return None, None, "JSON の形が違いました"
        if self.step_reasons:
            return self._parse_steps(data)
        cmds = data.get("commands")
        if not isinstance(cmds, list) or len(cmds) != self.steps:
            return None, None, "commands がちょうど{}個の並びではありませんでした".format(self.steps)
        bad = [c for c in cmds if c not in self.names]
        if bad:
            return None, None, "commands に選択肢にないものがありました（{}）".format(bad[0])
        reason = data.get("reason")
        return list(cmds), (str(reason) if reason is not None else None), None

    def _parse_steps(self, data):
        """手ごとに理由を書かせた返事 {"steps": [{"command", "why"}, ...]} を確かめる。理由は「 / 」でつないで返す。"""
        items = data.get("steps")
        if not isinstance(items, list) or len(items) != self.steps:
            return None, None, "steps がちょうど{}個の並びではありませんでした".format(self.steps)
        cmds, whys = [], []
        for it in items:
            if not isinstance(it, dict) or it.get("command") not in self.names:
                bad = it.get("command") if isinstance(it, dict) else it
                return None, None, "steps の command に選択肢にないものがありました（{}）".format(bad)
            cmds.append(it["command"])
            whys.append(str(it.get("why") or "-"))
        return cmds, " / ".join(whys), None

    def close(self):
        """別の流れを片付ける（頼んでいる途中の計画があれば、終わるのを待たずに捨てる）。"""
        if self._pool is not None:
            self._pool.shutdown(wait=False)

    def summary_line(self):
        # type: () -> str
        """集計を1行の文字列にする（最後の表示用）。"""
        lat = self.latencies
        avg = sum(lat) / len(lat) if lat else 0.0
        return ("計画の依頼 {}回：正しい計画 {}回、聞き直し {}回、受け取れず {}回、一時的なエラー {}。"
                "待ち時間 平均{:.2f}秒・最大{:.2f}秒。ゲームを止めて待った {}回・合計{:.1f}秒。トークン 入力{}・出力{}".format(
                    self.calls, self.ok, self.reasks, self.failed, self.errors or "なし", avg, max(lat) if lat else 0.0,
                    self.waits, self.wait_sec, self.input_tokens, self.output_tokens))


class _Done(object):
    """overlap なしのとき、その場で得た結果を Future と同じ形（done・result）で持つための入れ物。"""

    def __init__(self, value):
        """結果を保存する。"""
        self._value = value

    def done(self):
        """いつも届いている扱いにする（止めて待った回数には数えない。3c と同じく、待ちは呼び出しの中で起きる）。"""
        return True

    def result(self):
        """保存した結果を返す。"""
        return self._value
