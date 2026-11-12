# -*- coding: utf-8 -*-
"""
【このファイルの役割】
段階3c（LLM との仮接続）で実行するスクリプト。Unity と LLM を同時につなぐ。
21フレーム（0.42秒）ごとに、状況の要約を LLM に渡して、テスト用の間接操作（止まる＋8方向の9つ）から1つを選ばせ、
ゲームの中の Player を動かす。LLM の返事を待つあいだ、ゲームは止まって待つ（ロックステップ）。

流れ・状況の受け取り・3a の確認（1〜5）は step3a_state.py の run_loop をそのまま使い、
3b の確認（6〜9、step3b_state.py）と、9つの操作の向きの確認（10）を追加の確認係として差し込む。

3つの動かし方:
- オプションなし : モック。API を使わない。モックがランダムに正しい返事を返す（流れの確認と、9つの操作の確認）
- --simulate 名前 : 本物のクライアントを使い、送信だけ偽物にする。ゲームの途中でエラーを起こす。API を使わない
      transient … タイムアウトが続く（前の操作を続けるはず）、サーバーのエラー（再試行で直るはず）
      invalid   … 変な返事が続く（聞き直してもだめなら前の操作を続けるはず）、1回だけ変な返事（聞き直しで直るはず）
      auth      … キーが無効（そこで終了するはず）
- --real          : 実際に OpenAI を呼ぶ（費用が発生する。1ゲームで約143回、数円程度の見込み）

使い方（LLMBridge フォルダで実行し、表示が出たら Unity の再生ボタンを押す）:
  python step3c_llm_play.py --wait-sec 300                 （モック）
  python step3c_llm_play.py --simulate transient --wait-sec 300
  python step3c_llm_play.py --real --wait-sec 300

終了コード: 0=すべて OK / 2=設定エラー / 3=回数上限 / 4=API のエラーで終了 / 5=Unity との接続・通信のエラー / 8=確認項目に NG がある
"""
from __future__ import print_function

import argparse
import json
import logging
import os
import math
import random
import statistics
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

# Windows のコンソール（cp932）でも日本語が化けないようにする
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8")  # Python 3.7+
    except (AttributeError, ValueError):
        pass

from mlagents_envs.exception import UnityException  # noqa: E402

import settings  # noqa: E402
import step3a_state  # noqa: E402
from command.llm_planner import LLMPlanRunner  # noqa: E402
from command.llm_selector import LLMSelector  # noqa: E402
from command.test_commands import command_names, movement_of  # noqa: E402
from envutil import get_env, load_dotenv, require_env  # noqa: E402
from llm.base import BudgetExceededError, ConfigError, LLMError  # noqa: E402
from llm.budget import CallBudget  # noqa: E402
from llm.mock_client import MockClient  # noqa: E402
from llm.openai_client import OpenAIClient  # noqa: E402
from llm.transport import HttpResult, Transport, TransportTimeout, _err_body, _ok_body  # noqa: E402
from logging_util import JsonlLogger, mask_secrets, register_secret  # noqa: E402
from step3b_state import StateChecker  # noqa: E402
from unity.env_wrapper import UnityConnectError, UnityConnection  # noqa: E402

TAG = "[step3c]"
DEFAULT_MODEL = "gpt-6-luna"
SIM_SCENARIOS = ("transient", "invalid", "auth")
EXIT_OK, EXIT_CONFIG, EXIT_BUDGET, EXIT_LLM, EXIT_UNITY, EXIT_CHECK_NG = 0, 2, 3, 4, 5, 8


STEP_REASONS = [False]  # --step-reasons のとき True にする（モック・疑似エラーの返事の形を合わせるため）


def reply_obj(moves, reason):
    """モック・疑似エラーの返事の中身を作る。1手なら 3c の形 {"command"}、数手なら 3d の形 {"commands": [...]}。"""
    if STEP_REASONS[0]:
        return {"steps": [{"command": m, "why": reason} for m in moves]}
    if len(moves) == 1:
        return {"command": moves[0], "reason": reason}
    return {"commands": list(moves), "reason": reason}


class GameSimTransport(Transport):
    """ゲーム中の疑似エラー用の偽の送信。ふだんは正しい返事（ランダムな操作）を返し、決まった回にエラーを起こす。"""

    def __init__(self, scenario, names, model, seed=0, steps=1):
        """シナリオ名、選択肢、モデル名、乱数の種、1回で何手返すか（段階3d）を受け取る。何回目の送信で何を起こすかを決める。"""
        self.steps = steps
        self.scenario = scenario
        self.names = list(names)
        self.model = model
        self.rng = random.Random(seed)
        self.calls = 0
        # 送信の回数（1から）→ 起こすこと
        if scenario == "transient":
            # 10〜13回目：タイムアウトが4回続く（最初の1回＋再試行3回がすべて失敗 → 前の操作を続ける）
            # 30回目：サーバーのエラー（次の再試行で直る）
            self.plan = {10: "timeout", 11: "timeout", 12: "timeout", 13: "timeout", 30: "server_error"}
        elif scenario == "invalid":
            # 10・11回目：変な返事が2回（最初＋聞き直しが失敗 → 前の操作を続ける）
            # 20回目：選択肢に無い返事（聞き直しの21回目で直る）
            self.plan = {10: "not_json", 11: "not_json", 20: "bad_command"}
        else:  # auth
            # 15回目：キーが無効（そこで終了する）
            self.plan = {15: "auth"}

    def post(self, url, headers, json_body, timeout):
        """送信のふりをして、決まった回ならエラーを、それ以外は正しい返事を返す。"""
        self.calls += 1
        what = self.plan.get(self.calls, "ok")
        if what == "timeout":
            raise TransportTimeout("ReadTimeout")
        if what == "server_error":
            return HttpResult(500, _err_body("The server had an error", "server_error", None))
        if what == "auth":
            return HttpResult(401, _err_body("Incorrect API key provided: sk-sim***", "invalid_request_error",
                                             "invalid_api_key"))
        if what == "not_json":
            return HttpResult(200, _ok_body(self.model, "左に動きます"))
        if what == "bad_command":
            return HttpResult(200, _ok_body(self.model, json.dumps(reply_obj(["斜め"] * self.steps, "テスト"),
                                                                   ensure_ascii=False)))
        reply = json.dumps(reply_obj([self.rng.choice(self.names) for _ in range(self.steps)], "疑似の返事"),
                           ensure_ascii=False)
        return HttpResult(200, _ok_body(self.model, reply))


class DirectionChecker(object):
    """9つの操作のそれぞれで、自分が対応表どおりの向きに動いているかを数える係（確認10）。"""

    LAG_FRAMES = 3          # 切り替えの直後は、まだ前の操作の動きが残るので見ない
    RESPAWN_SKIP = 60       # 被弾してから復活し終わるまで（約1秒）は見ない
    MIN_SPEED = 0.03        # 動いているとみなす1フレームの移動量（ふつうは 0.1 前後。斜めは各軸 0.07 前後）
    STILL_SPEED = 0.01      # 止まっているとみなす1フレームの移動量

    def __init__(self, names):
        """選択肢ごとの数え上げを用意する。"""
        self.names = list(names)
        self.current = None
        self.switch_frame = 0
        self.last_hit = -10 ** 9
        self.checked = dict((n, 0) for n in self.names)
        self.matched = dict((n, 0) for n in self.names)
        self.bad_examples = []

    def on_scene(self, ev):
        """シーンの読み込みを受け取る。ゲーム開始時も、自機は画面下の外から登場する動き（復活と同じ。約1秒、
        そのあいだ操作を受け付けない）をするので、被弾と同じ扱いでしばらく見ない。"""
        self.last_hit = max(self.last_hit, int(ev.get("frame", 0)))

    def on_switch(self, frame, command):
        """操作が切り替わったことを受け取る。"""
        self.current = command
        self.switch_frame = frame

    def on_frame(self, fr):
        """記録1つを受け取り、今の操作の向きと、自分の移動量の向きが合っているかを数える。"""
        f = int(fr["frame"])
        for ev in fr.get("events") or []:
            if ev.get("kind") == "hit" and ev.get("who") == "self":
                self.last_hit = int(ev.get("frame", f))
        me = fr.get("self") or []
        if self.current is None or len(me) < 4:
            return
        if f < self.switch_frame + self.LAG_FRAMES or f < self.last_hit + self.RESPAWN_SKIP:
            return
        h, v = movement_of(self.current)
        x, y = me[0], me[1]
        mx, my = me[2] / settings.MOVE_SCALE, me[3] / settings.MOVE_SCALE
        ok = True
        judged = False
        for want, got, pos, lo, hi in ((h, mx, x, -5.5, 5.5), (v, my, y, -4.0, 4.0)):
            if want == 0:
                judged = True
                ok = ok and abs(got) <= self.STILL_SPEED
            else:
                at_wall = (want > 0 and pos >= hi - 0.05) or (want < 0 and pos <= lo + 0.05)
                if at_wall:
                    continue  # 壁に当たっていてそれ以上動けない向きは見ない
                judged = True
                ok = ok and (got * want > 0) and abs(got) >= self.MIN_SPEED
        if not judged:
            return
        self.checked[self.current] += 1
        if ok:
            self.matched[self.current] += 1
        elif len(self.bad_examples) < 5:
            self.bad_examples.append("frame={} {}: 移動量({:.3f}, {:.3f})".format(f, self.current, mx, my))

    def results(self):
        """確認10の結果を返す。選ばれた操作がすべて9割以上で合っていれば OK（選ばれなかった操作は表示だけ）。"""
        parts, ok = [], True
        unused = []
        for n in self.names:
            c, m = self.checked[n], self.matched[n]
            if c == 0:
                unused.append(n)
                continue
            rate = float(m) / c
            parts.append("{} {}/{}".format(n, m, c))
            if c >= 3 and rate < 0.9:
                ok = False
        detail = "、".join(parts)
        if unused:
            detail += "。確かめられなかった操作: " + "・".join(unused)
        if self.bad_examples and not ok:
            detail += "（例: " + " / ".join(self.bad_examples) + "）"
        # 選ばれなかった操作は NG にしない（LLM がすべてを選ぶとは限らない。9つすべての確認はモックで行う）
        return [("9つの操作で対応表どおりの向きに動く", ok and len(unused) < len(self.names), detail)]


class PlayQuality(object):
    """プレイの質を集計する係（参考。3c と 3d を比べるため。良し悪しの判定はしない）。

    ゲーム開始直後と被弾後の復活中（60フレーム）は、位置の集計から除く。60秒（3000フレーム）を超えた分も除く。
    """

    SKIP = 60

    def __init__(self):
        """数え上げの入れ物を用意する。"""
        self.last_hit = 0
        self.hits = {"self": 0, "partner": 0}
        self.kills = {"self": 0, "partner": 0}
        self.nearest = []       # 一番近い弾までの距離
        self.within1 = []       # 半径1以内の弾の数
        self.wall = 0           # 壁際にいた記録の数
        self.under = 0          # 敵の真下（横のずれ0.5以内、自分より上）にいた記録の数
        self.total = 0

    def on_scene(self, ev):
        """シーンの読み込み（開始直後の登場の動きは除くため、被弾と同じ扱いにする）。"""
        self.last_hit = max(self.last_hit, int(ev.get("frame", 0)))

    def on_frame(self, fr):
        """記録1つを受け取り、出来事と位置を数える。"""
        f = int(fr["frame"])
        if f >= 3000:
            return
        for ev in fr.get("events") or []:
            if ev.get("kind") == "hit":
                self.hits[ev.get("who", "self")] = self.hits.get(ev.get("who", "self"), 0) + 1
                if ev.get("who") == "self":
                    self.last_hit = int(ev.get("frame", f))
            elif ev.get("kind") == "kill":
                self.kills[ev.get("who", "self")] = self.kills.get(ev.get("who", "self"), 0) + 1
        me = fr.get("self") or []
        if len(me) < 2 or f - self.last_hit < self.SKIP:
            return
        x, y = me[0], me[1]
        self.total += 1
        if abs(x) >= 5.45 or y >= 3.95 or y <= -3.95:
            self.wall += 1
        d = [math.hypot(b[1] - x, b[2] - y) for b in fr.get("bullets") or []]
        if d:
            self.nearest.append(min(d))
            self.within1.append(sum(1 for v in d if v <= 1.0))
        if any(abs(e[1] - x) < 0.5 and e[2] > y for e in fr.get("enemies") or []):
            self.under += 1

    def results(self):
        """確認の結果としては出さない（集計は summary_line で表示する）。"""
        return []

    def summary_line(self):
        """集計を1行の文字列にする。"""
        n = max(1, self.total)
        med = statistics.median(self.nearest) if self.nearest else 0.0
        w1 = statistics.mean(self.within1) if self.within1 else 0.0
        return ("被弾 自分{}・相方{}、撃破 自分{}・相方{}、一番近い弾までの距離（中央値）{:.2f}、半径1以内の弾 平均{:.2f}発、"
                "壁際にいた時間{:.0f}%、敵の真下にいた時間{:.0f}%".format(
                    self.hits.get("self", 0), self.hits.get("partner", 0), self.kills.get("self", 0),
                    self.kills.get("partner", 0), med, w1, 100.0 * self.wall / n, 100.0 * self.under / n))


class Extras(object):
    """追加の確認係を2つ以上まとめて、step3a_state.run_loop に1つとして渡すための係。"""

    def __init__(self, *items):
        """まとめる確認係を受け取る。"""
        self.items = items

    def on_scene(self, ev):
        """シーンの読み込みを、それぞれの確認係に渡す。"""
        for it in self.items:
            it.on_scene(ev)

    def on_frame(self, fr):
        """記録を、それぞれの確認係に渡す。"""
        for it in self.items:
            it.on_frame(fr)

    def on_switch(self, frame, command):
        """操作の切り替えを、受け取れる確認係に渡す。"""
        for it in self.items:
            if hasattr(it, "on_switch"):
                it.on_switch(frame, command)

    def results(self):
        """それぞれの確認係の結果をつなげて返す。"""
        out = []
        for it in self.items:
            out.extend(it.results())
        return out


def parse_args(argv=None):
    """コマンドラインのオプションを読み取る。"""
    p = argparse.ArgumentParser(description="段階3c：LLM との仮接続（LLM が間接操作を選んで Player を動かす）")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--real", action="store_true", help="実際に OpenAI を呼ぶ（費用が発生する）")
    g.add_argument("--simulate", choices=SIM_SCENARIOS, help="ゲームの途中で疑似エラーを起こす（API は使わない）")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--frames", type=int, help="動かす長さ（フレーム）。既定3000（60秒＝1ゲーム）")
    g.add_argument("--sec", type=float, help="動かす長さ（ゲーム内秒）")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--command-interval-frames", type=int, help="LLM に選ばせる間隔（フレーム）。既定20（判断に合わせて21）")
    g.add_argument("--command-interval-sec", type=float, help="LLM に選ばせる間隔（ゲーム内秒）")
    p.add_argument("--max-calls", type=int, default=350, help="1回の実行あたりの HTTP リクエストの上限（再試行も数える）。既定350")
    p.add_argument("--no-reason", action="store_true", help="理由（reason）を返させない")
    p.add_argument("--plan-steps", type=int, default=1,
                   help="段階3d（実験）：1回の思考で何手まとめて決めさせるか。既定1（3c と同じ）。1手の長さは --command-interval-frames")
    p.add_argument("--step-reasons", action="store_true",
                   help="段階3d（実験）：数手まとめるとき、手ごとに理由を書かせる")
    p.add_argument("--overlap", action="store_true",
                   help="段階3d（実験）：LLM が考えているあいだもゲームを進める（次の計画が間に合わなければ止まって待つ）")
    p.add_argument("--seed", type=int, default=0, help="モック・疑似エラーの乱数の種")
    p.add_argument("--mock-delay", type=float, default=0.0, help="モックの返事をわざと遅らせる秒数（考えながら進める動きの確認用）")
    p.add_argument("--wait-sec", type=int, default=120, help="Unity からの接続や応答を待つ上限（秒）。既定120")
    p.add_argument("--behavior", default="Player", help="操作する behavior 名。既定 Player")
    p.add_argument("--env-file", default=os.path.join(HERE, ".env"), help="読み込む .env のパス")
    return p.parse_args(argv)


def make_client(args, budget, llm_logger):
    """動かし方（モック／疑似エラー／実API）に合った LLM クライアントを作る。（モード名, クライアント）を返す。"""
    names = command_names()
    STEP_REASONS[0] = bool(args.step_reasons)
    if args.real:
        api_key = require_env("OPENAI_API_KEY")
        register_secret(api_key)
        model = require_env("OPENAI_MODEL")
        return "real", OpenAIClient(api_key=api_key, model=model, budget=budget, logger=llm_logger,
                                    base_url=get_env("OPENAI_BASE_URL"), mode="real")
    if args.simulate:
        model = get_env("OPENAI_MODEL", DEFAULT_MODEL)
        transport = GameSimTransport(args.simulate, names, model, seed=args.seed, steps=args.plan_steps)
        # 疑似エラーでは、再試行の待ち時間を飛ばす（確認を早く終えるため）
        return "simulate:" + args.simulate, OpenAIClient(
            api_key="sk-simulated-not-a-real-key", model=model, budget=budget, logger=llm_logger,
            transport=transport, sleep=lambda s: None, mode="simulate:" + args.simulate)
    rng = random.Random(args.seed)
    replies = [json.dumps(reply_obj([rng.choice(names) for _ in range(args.plan_steps)], "モックの返事"), ensure_ascii=False)
               for _ in range(500)]
    return "mock", MockClient(budget=budget, logger=llm_logger, replies=replies, delay_s=args.mock_delay)


def main(argv=None):
    """全体の流れ：設定を決める → LLM のクライアントを用意する → Unity に接続する → LLM に選ばせながら回す → 結果を表示する。"""
    args = parse_args(argv)
    load_dotenv(args.env_file)
    try:
        args.frames = args.frames if (args.frames is not None or args.sec is not None) else 3000
        if args.command_interval_frames is None and args.command_interval_sec is None:
            args.command_interval_frames = 20
        step3a_state.TAG = TAG
        plan = step3a_state.resolve(args)
    except ValueError as e:
        sys.stderr.write("{} 設定エラー: {}\n".format(TAG, e))
        return EXIT_CONFIG

    budget = CallBudget(args.max_calls)
    llm_logger = JsonlLogger(os.path.join(HERE, "logs"), stage="step3c")
    try:
        mode, client = make_client(args, budget, llm_logger)
    except ConfigError as e:
        sys.stderr.write("{} 設定エラー: {}\n".format(TAG, mask_secrets(e.message)))
        return EXIT_CONFIG
    plan_mode = args.plan_steps > 1 or args.overlap
    if plan_mode:
        # 段階3d（実験）：数手まとめて考えさせる。1手の長さ＝切り替えの間隔
        selector = LLMPlanRunner(client, command_names(), steps=args.plan_steps, step_frames=plan["interval"],
                                 overlap=args.overlap, with_reason=not args.no_reason, step_reasons=args.step_reasons)
    else:
        selector = LLMSelector(client, command_names(), with_reason=not args.no_reason, interval_frames=plan["interval"])

    warn_counter = step3a_state.WarningCounter()
    logging.getLogger("mlagents_envs").addHandler(warn_counter)
    logger = JsonlLogger(os.path.join(HERE, "logs"), stage="step3c", prefix="unity")
    recorder = step3a_state.StateRecorder(os.path.join(HERE, "logs"))
    quality = PlayQuality()
    extras = Extras(StateChecker(), DirectionChecker(command_names()), quality)

    print("{} mode={} model={} max_calls={} reason={}".format(
        TAG, mode, getattr(client, "model", "?"), args.max_calls, "なし" if args.no_reason else "あり"), flush=True)
    print("{} 長さ={} {}={}フレーム（{:.2f}秒）".format(
        TAG, settings.describe(plan["total"]), "1手" if plan_mode else "LLM に選ばせる間隔",
        plan["interval"], settings.frames_to_sec(plan["interval"])), flush=True)
    if plan_mode:
        print("{} 1回の思考で{}手 考えながら進める={} 手ごとの理由={}（段階3d・実験）".format(
            TAG, args.plan_steps, "あり" if args.overlap else "なし", "あり" if args.step_reasons else "なし"), flush=True)
    print("{} Unity の再生ボタンを押してください（ポート{}、最大{}秒待ちます）".format(
        TAG, settings.EDITOR_PORT, args.wait_sec), flush=True)

    def show_switch(frame, command, sel):
        """切り替えのたびに、LLM が選んだ操作・理由・待ち時間を1行で表示する（数手まとめるときは、計画の始まりだけ）。"""
        last = sel.last or {}
        if plan_mode:
            if last.get("new_plan"):
                print("{} frame={}（{:.2f}秒） 計画: {}（{}） 考えた時間{:.2f}秒 止まって待った{:.2f}秒".format(
                    TAG, frame, settings.frames_to_sec(frame), "・".join(last.get("plan") or []),
                    mask_secrets(last.get("reason") or "-"), last.get("latency_s", 0.0), last.get("waited_s", 0.0)), flush=True)
            elif last.get("kept"):
                print("{} frame={}（{:.2f}秒） 計画を受け取れず {} を続ける（{}）".format(
                    TAG, frame, settings.frames_to_sec(frame), command, last.get("error") or "-"), flush=True)
            return
        if last.get("command"):
            note = "{}（{}）".format(command, last.get("reason") or "-")
        else:
            note = "{} を続ける（{}）".format(command, last.get("error") or "選べず")
        print("{} frame={}（{:.2f}秒） LLM: {} {:.2f}秒".format(
            TAG, frame, settings.frames_to_sec(frame), mask_secrets(note), last.get("latency_s", 0.0)), flush=True)

    conn = UnityConnection(behavior=args.behavior, wait_sec=args.wait_sec, logger=logger)
    code = EXIT_OK
    started = None
    try:
        info = conn.connect()
        started = time.time()
        print("{} 接続しました。behavior: {} 観察={} 操作=連続{}".format(
            TAG, info["behavior"], info["obs_sizes"], info["continuous"]), flush=True)
        code = step3a_state.run_loop(conn, selector, plan, logger, recorder, warn_counter, extras, on_switch=show_switch,
                                     strict_config=True)
    except UnityConnectError as e:
        sys.stderr.write("{} 失敗: {}\n".format(TAG, e))
        code = EXIT_UNITY
    except step3a_state.ConfigMismatch as e:
        sys.stderr.write("{} 停止: Unity の設定値が settings.py と食い違っています（{}）。LLM を呼ぶ前に止めました。\n".format(TAG, e))
        sys.stderr.write("{} 対処: LLMBridgeManager の Inspector の値か settings.py を直し、シーンを保存してください。\n".format(TAG))
        code = EXIT_CONFIG
    except BudgetExceededError as e:
        sys.stderr.write("{} 終了: {}（calls_used={}）\n".format(TAG, mask_secrets(e.message), budget.summary()))
        code = EXIT_BUDGET
    except LLMError as e:
        sys.stderr.write("{} 終了: LLM のエラー [{}] {}\n".format(TAG, e.kind, mask_secrets(e.message)))
        if e.hint:
            sys.stderr.write("{} 対処: {}\n".format(TAG, e.hint))
        code = EXIT_LLM
    except KeyboardInterrupt:
        sys.stderr.write("{} 中断しました（Ctrl+C）\n".format(TAG))
        code = EXIT_UNITY
    except UnityException as e:
        sys.stderr.write("{} 失敗: Unity との通信が切れました（{}: {}）。\n".format(TAG, type(e).__name__, e))
        code = EXIT_UNITY
    finally:
        if hasattr(selector, "close"):
            selector.close()
        conn.close()
        recorder.close()
        logging.getLogger("mlagents_envs").removeHandler(warn_counter)
        elapsed = (time.time() - started) if started else 0.0
        print("{} 実行全体の実時間（接続から終了まで）: {:.1f}秒".format(TAG, elapsed), flush=True)
        print("{} プレイの質: {}".format(TAG, quality.summary_line()), flush=True)
        print("{} LLM の集計: {}".format(TAG, selector.summary_line()), flush=True)
        print("{} 選ばれた操作: {}（calls_used={}）".format(TAG, selector.picked or "なし", budget.summary()), flush=True)
        logger.log({"event": "llm_summary", "mode": mode, "plan_steps": args.plan_steps, "overlap": args.overlap,
                    "summary": selector.summary_line(), "quality": quality.summary_line(), "elapsed_s": round(elapsed, 1),
                    "picked": selector.picked, "calls_used": budget.summary(), "exit_code": code})
        print("{} log saved: {} / {} / {}".format(TAG, os.path.relpath(llm_logger.path, HERE),
                                                 os.path.relpath(logger.path, HERE), os.path.relpath(recorder.path, HERE)))
    return code


if __name__ == "__main__":
    sys.exit(main())
