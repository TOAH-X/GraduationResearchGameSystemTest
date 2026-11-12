# -*- coding: utf-8 -*-
"""
【このファイルの役割】
間接操作を「選ぶ係」の LLM 版（段階3c）。2b のランダム版（selector.py）と同じ形（select）で差し込める。
状況の要約（unity/state_summary.py）をプロンプトに入れて LLM に送り、返事の JSON を確かめて、
選ばれた間接操作の名前を返す。

プロンプトの決まり（PLAN.md 段階3c）:
- LLM は「人間のプレイヤー役」。協力や相方への配慮は書かない
- 目的は「敵の弾を避ける」「敵を倒す」の2つ（優先順）
- 返す形：{"command": "選択肢のどれか", "reason": "20文字程度の理由"}（reason は外せる）

返事やエラーの扱い:
- 正しい返事 → その名前を返す
- JSON でない・選択肢に無い → 1回だけ聞き直す（回数上限に数える）。それでもだめなら None（前の操作を続ける）
- 一時的なエラー（混雑、タイムアウト、接続失敗、サーバーのエラー、応答の形がおかしい）→ None（前の操作を続ける）
- 続けても意味のないエラー（キーが無効、残高不足、回数上限、リクエストの誤り、設定の誤り）→ そのまま呼び出し元へ投げる（終了する）

【どこから使われるか】
step3c_llm_play.py。段階5 で、プロンプトを本物（意図・結果・感情・分析）に置き換える。
"""
import json
from typing import Any, Dict, List, Optional

import settings
from command.selector import CommandSelector
from llm.base import LLMClient, LLMError
from unity.state_summary import summarize

# 前の操作を続けてよい（一時的な）エラーの種類。llm/base.py の各エラーの kind と同じ名前
TRANSIENT_KINDS = ("rate_limit", "timeout", "connection", "server_error", "invalid_response")


def build_system_prompt(names, with_reason, interval_frames):
    # type: (List[str], bool, int) -> str
    """毎回同じ「固定の説明」（ゲームのルール、目的、選択肢、返す形）を作る。"""
    sec = settings.frames_to_sec(interval_frames)
    move = 5.0 * sec  # 自機の速さは毎秒5（PlayerMoveController の moveSpeed）
    if with_reason:
        form = '{"command": "選択肢のどれか", "reason": "20文字程度の理由"}'
    else:
        form = '{"command": "選択肢のどれか"}'
    return "\n".join([
        "あなたは縦スクロールのシューティングゲームをプレイしています。あなたは画面下側の自機を操作します。",
        "- 自機は前方（上）へショットを撃ち続けています。",
        "- 敵の弾に当たると、画面下の外から復活します。",
        "- 座標は右が x のプラス、上が y のプラスです。",
        "目的（優先順）：",
        "1. 敵の弾に当たらないように避ける",
        "2. 敵を倒す（ショットは上にしか出ないので、敵の真下あたりに位置取ると倒しやすい）",
        "{:.2f}秒ごとに、次の動きを選択肢から1つ選んでください。1回の選択で自機は約{:.1f}進みます。".format(sec, move),
        "選択肢：" + "／".join(names),
        "次の形の JSON だけを返してください：" + form,
    ])


class LLMSelector(CommandSelector):
    """状況の要約を LLM に渡し、次の間接操作を選んでもらう係。"""

    name = "llm"

    def __init__(self, client, names, with_reason=True, interval_frames=21, max_reask=1, max_tokens=None):
        # type: (LLMClient, List[str], bool, int, int, Optional[int]) -> None
        """LLM のクライアント、選択肢の名前、理由を返させるか、選ぶ間隔、聞き直す回数などを受け取る。"""
        self.client = client
        self.names = list(names)
        self.with_reason = with_reason
        self.max_reask = max_reask
        self.max_tokens = max_tokens or (80 if with_reason else 24)
        self.system_prompt = build_system_prompt(self.names, with_reason, interval_frames)
        self.last = {}  # type: Dict[str, Any]  # 直前の選択の詳しい内容（画面表示と記録用）
        # 集計
        self.calls = 0          # select が呼ばれた回数
        self.ok = 0             # 正しい返事で選べた回数
        self.kept = 0           # 選べず前の操作を続けた回数
        self.reasks = 0         # 聞き直した回数
        self.errors = {}        # type: Dict[str, int]  # 一時的なエラーの種類ごとの回数
        self.latencies = []     # type: List[float]
        self.input_tokens = 0
        self.output_tokens = 0
        self.picked = {}        # type: Dict[str, int]  # 選ばれた操作ごとの回数

    def select(self, context=None):
        # type: (Optional[Dict[str, Any]]) -> Optional[str]
        """状況（context の record と events）から次の操作を選ぶ。選べなければ None（前の操作を続ける）。"""
        context = context or {}
        summary = summarize(context.get("record"), context.get("events"))
        messages = [
            {"role": "system", "content": self.system_prompt},
            {"role": "user", "content": "今の状況：\n" + summary},
        ]
        self.calls += 1
        self.last = {"summary": summary, "command": None, "reason": None, "error": None, "latency_s": 0.0, "tries": 0}
        for attempt in range(1 + self.max_reask):
            self.last["tries"] = attempt + 1
            try:
                resp = self.client.complete(messages, max_tokens=self.max_tokens, json_mode=True)
            except LLMError as e:
                if e.kind in TRANSIENT_KINDS:
                    self.errors[e.kind] = self.errors.get(e.kind, 0) + 1
                    self.kept += 1
                    self.last["error"] = e.kind
                    return None
                raise  # 続けても意味のないエラーは、呼び出し元で終了する
            self.latencies.append(resp.latency_s)
            self.last["latency_s"] += resp.latency_s
            self.input_tokens += resp.input_tokens or 0
            self.output_tokens += resp.output_tokens or 0
            command, reason, problem = self._parse(resp.text)
            if command is not None:
                self.ok += 1
                self.picked[command] = self.picked.get(command, 0) + 1
                self.last.update({"command": command, "reason": reason, "raw": resp.text})
                return command
            self.last["error"] = problem
            self.last["raw"] = resp.text
            if attempt < self.max_reask:
                self.reasks += 1
                # 聞き直し：前の返事と、何が悪かったかを伝える
                messages = messages + [
                    {"role": "assistant", "content": resp.text or ""},
                    {"role": "user", "content": "{}。選択肢：{} のどれかを、指定の JSON の形だけで返してください。".format(
                        problem, "／".join(self.names))},
                ]
        self.kept += 1
        return None

    def _parse(self, text):
        # type: (Optional[str]) -> Any
        """返事の文字列を確かめる。（操作の名前, 理由, 問題の説明）を返す。だめなら名前は None。"""
        if not text:
            return None, None, "返事が空でした"
        try:
            data = json.loads(text)
        except ValueError:
            return None, None, "JSON として読めませんでした"
        if not isinstance(data, dict):
            return None, None, "JSON の形が違いました"
        command = data.get("command")
        if command not in self.names:
            return None, None, "command が選択肢にありませんでした（{}）".format(command)
        reason = data.get("reason")
        return command, (str(reason) if reason is not None else None), None

    def summary_line(self):
        # type: () -> str
        """集計を1行の文字列にする（最後の表示用）。"""
        lat = self.latencies
        avg = sum(lat) / len(lat) if lat else 0.0
        return ("呼び出し {}回：正しい返事 {}回、聞き直し {}回、選べず前の操作を続けた {}回、一時的なエラー {}。"
                "待ち時間 平均{:.2f}秒・最大{:.2f}秒。トークン 入力{}・出力{}".format(
                    self.calls, self.ok, self.reasks, self.kept, self.errors or "なし",
                    avg, max(lat) if lat else 0.0, self.input_tokens, self.output_tokens))
