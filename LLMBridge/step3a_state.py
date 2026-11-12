# -*- coding: utf-8 -*-
"""
【このファイルの役割】
段階3a（状況の送信：通り道）で実行するスクリプト。OpenAI の API は使わない。
LLMTestScene に置いた LLMBridgeManager が自作サイドチャンネルで送ってくる情報を受け取り、
抜けなく届くかを確かめる。Player の動かし方は 2b と同じ（テスト用の間接操作をランダムに切り替える）。

確かめること（PLAN.md 段階3a）:
1. 接続時（シーン読み込み時）に設定値が届き、settings.py と一致する
2. フレームごとの情報が抜けなく届く（フレーム番号に抜けや重複が無い。シーンのやり直しの前後も含む）
   （段階3b から、記録は settings.STATE_INTERVAL_FRAMES フレームおきに届くので、その間隔で抜けが無いかを見る）
3. シーンを読み込むたびに PlayerAgent の数が届く（古いエージェントが残るか）
4. ゲーム終了時の統計が届き、「Unknown side channel」の警告が出ない
5. 送ることで余計にかかる時間とデータの量

全フレームの情報は logs/state_YYYYMMDD_HHMMSS.jsonl に、1回の実行で1ファイルとして保存する（小数は4桁に丸める）。

段階3b からは、step3b_state.py がこのファイルの main() を「追加の確認係（extra）」つきで呼び出して使う。
extra は on_frame(フレームの情報) で毎フレームの情報を、on_scene(シーン読み込みの情報) でシーンの読み込みを受け取り、
results() で追加の確認結果を返す。
それ以外の記録（接続、区切り、統計、判定結果など）は、これまでどおり logs/unity_YYYYMMDD.jsonl に残す。

使い方（LLMBridge フォルダで実行し、表示が出たら Unity の再生ボタンを押す）:
  python step3a_state.py --wait-sec 300
  python step3a_state.py --sec 20      （短く試す。シーンのやり直しは確かめられない）

終了コード: 0=すべて OK / 2=設定エラー / 5=Unity との接続・通信のエラー / 8=確認項目に NG がある
"""
from __future__ import print_function

import argparse
import datetime
import io
import json
import logging
import os
import sys
import time
from collections import Counter

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
from command.selector import RandomSelector  # noqa: E402
from command.test_commands import command_names, movement_of  # noqa: E402
from logging_util import JsonlLogger  # noqa: E402
from unity.env_wrapper import KIND_GAME_END, KIND_LABELS, UnityConnectError, UnityConnection  # noqa: E402

TAG = "[step3a]"
EXIT_OK = 0
EXIT_CONFIG = 2
EXIT_UNITY = 5
EXIT_CHECK_NG = 8


class WarningCounter(logging.Handler):
    """ML-Agents（mlagents_envs）が出す警告のうち、「Unknown side channel」を数える係（確認4のため）。"""

    def __init__(self):
        """数を0にして、警告以上だけを受け取るようにする。"""
        super(WarningCounter, self).__init__(level=logging.WARNING)
        self.unknown_side_channel = 0

    def emit(self, record):
        """警告が出るたびに呼ばれる。「Unknown side channel」なら数える。"""
        if "Unknown side channel" in record.getMessage():
            self.unknown_side_channel += 1


def parse_args(argv=None):
    """コマンドラインのオプションを読み取る。長さと間隔は、フレームでも秒でも指定できる（標準はフレーム）。"""
    p = argparse.ArgumentParser(description="段階3a：状況の送信（通り道）の確認（API は使わない）")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--frames", type=int, help="動かす長さ（フレーム）。既定4500（90秒）")
    g.add_argument("--sec", type=float, help="動かす長さ（ゲーム内秒）")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--command-interval-frames", type=int, help="間接操作を選び直す間隔（フレーム）。既定150（3秒）")
    g.add_argument("--command-interval-sec", type=float, help="間接操作を選び直す間隔（ゲーム内秒）")
    p.add_argument("--seed", type=int, default=None, help="ランダムな選び方を再現したいときの乱数の種")
    p.add_argument("--wait-sec", type=int, default=120, help="Unity からの接続や応答を待つ上限（秒）。既定120")
    p.add_argument("--behavior", default="Player", help="操作する behavior 名。既定 Player")
    return p.parse_args(argv)


def resolve(args):
    """オプションから、長さと切り替え間隔をフレームで決め、判断のタイミングにそろえる。"""
    plan = {}
    for key, frames, sec, default in (
        ("total", args.frames, args.sec, 4500),
        ("interval", args.command_interval_frames, args.command_interval_sec, 150),
    ):
        value = settings.frames_from_options(frames, sec, default)
        if value <= 0:
            raise ValueError("{} は1フレーム以上にしてください。".format(key))
        aligned, changed = settings.align_to_decision(value)
        if changed:
            print("{} {} を判断のタイミングに合わせて {}フレームにしました（指定 {}フレーム）".format(
                TAG, key, aligned, value), flush=True)
        plan[key] = aligned
    return plan


def round_floats(value, digits=4):
    """辞書や並びの中の小数を、すべて digits 桁に丸めた写しを返す（状況ログを小さくするため）。"""
    if isinstance(value, float):
        return round(value, digits)
    if isinstance(value, dict):
        return {k: round_floats(v, digits) for k, v in value.items()}
    if isinstance(value, list):
        return [round_floats(v, digits) for v in value]
    return value


class StateRecorder(object):
    """全フレームの情報を、1回の実行につき1ファイルの JSONL に書き出す係。"""

    def __init__(self, log_dir):
        # type: (str) -> None
        """保存先のフォルダを受け取り、実行開始の日時を名前にしたファイルを決める。"""
        if not os.path.isdir(log_dir):
            os.makedirs(log_dir)
        stamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        self.path = os.path.join(log_dir, "state_{}.jsonl".format(stamp))
        self._f = io.open(self.path, "a", encoding="utf-8")

    def write(self, record):
        """1件を1行として書き出す（小数は4桁に丸めて、ファイルを小さくする）。"""
        # 区切りの後ろの空白を省いて、ファイルを小さくする（段階3c で変更。ユーザー了承）
        self._f.write(json.dumps(round_floats(record), ensure_ascii=False, separators=(",", ":")) + "\n")

    def close(self):
        """ファイルを閉じる。"""
        self._f.close()


def main(argv=None, tag=None, stage="step3a", extra=None):
    """全体の流れ：設定を決める → 接続する → 判断ごとに間接操作を送りつつ状況を受け取る → 5項目を判定して表示する。

    tag・stage は表示と記録に使う名前（step3b から呼ぶときに変える）。extra は追加の確認係（無ければ None）。
    戻り値は終了コード（0=すべて OK / 2=設定エラー / 5=Unity との接続・通信のエラー / 8=確認項目に NG がある）。
    """
    global TAG
    if tag:
        TAG = tag
    args = parse_args(argv)
    try:
        plan = resolve(args)
        selector = RandomSelector(command_names(), seed=args.seed)
    except ValueError as e:
        sys.stderr.write("{} 設定エラー: {}\n".format(TAG, e))
        return EXIT_CONFIG

    warn_counter = WarningCounter()
    logging.getLogger("mlagents_envs").addHandler(warn_counter)

    logger = JsonlLogger(os.path.join(HERE, "logs"), stage=stage, prefix="unity")
    recorder = StateRecorder(os.path.join(HERE, "logs"))
    print("{} 長さ={} 切り替え={}フレームごと FIXED_DT={} DECISION_PERIOD={}".format(
        TAG, settings.describe(plan["total"]), plan["interval"], settings.FIXED_DT, settings.DECISION_PERIOD), flush=True)
    print("{} Unity の再生ボタンを押してください（ポート{}、最大{}秒待ちます）".format(
        TAG, settings.EDITOR_PORT, args.wait_sec), flush=True)

    conn = UnityConnection(behavior=args.behavior, wait_sec=args.wait_sec, logger=logger)
    try:
        info = conn.connect()
        print("{} 接続しました。behavior: {} 観察={} 操作=連続{}".format(
            TAG, info["behavior"], info["obs_sizes"], info["continuous"]), flush=True)
        return run_loop(conn, selector, plan, logger, recorder, warn_counter, extra)
    except UnityConnectError as e:
        sys.stderr.write("{} 失敗: {}\n".format(TAG, e))
        logger.log({"event": "connect_failed", "message": str(e)})
        return EXIT_UNITY
    except KeyboardInterrupt:
        sys.stderr.write("{} 中断しました（Ctrl+C）\n".format(TAG))
        return EXIT_UNITY
    except UnityException as e:
        sys.stderr.write("{} 失敗: Unity との通信が切れました（{}: {}）。\n".format(TAG, type(e).__name__, e))
        logger.log(dict({"event": "unity_error", "error": type(e).__name__, "message": str(e)}, **conn.timeline()))
        return EXIT_UNITY
    finally:
        conn.close()
        recorder.close()
        logging.getLogger("mlagents_envs").removeHandler(warn_counter)
        print("{} log saved: {} / {}".format(
            TAG, os.path.relpath(logger.path, HERE), os.path.relpath(recorder.path, HERE)))


class ConfigMismatch(Exception):
    """Unity の設定値が settings.py と食い違っていて、strict_config のときに止めるための例外。"""


def run_loop(conn, selector, plan, logger, recorder, warn_counter, extra=None, on_switch=None, strict_config=False):
    """判断ごとに状況を受け取って保存し、間接操作を送る。最後に5項目（と追加の確認）を判定して表示する。

    選ぶ係（selector）には、最新の状況の記録と、前回の選択からの出来事を渡す（段階3c から。ランダム版は使わない）。
    選ぶ係が None を返したら、前の操作を続ける（LLM が一時的に答えられなかったときなど）。
    on_switch があれば、選んだあとに on_switch(フレーム, 操作, 選ぶ係) を呼ぶ（画面表示など）。
    """
    current = None
    next_switch = 0
    latest_record = None        # 最新の状況の記録（選ぶ係に渡す）
    events_since_switch = []    # 前回の選択からの出来事（選ぶ係に渡す）
    frame_numbers = []          # 届いたフレーム番号（確認2）
    per_decision = Counter()    # 1回の判断で届いたフレームの数の分布（確認2）
    scenes = []                 # 届いたシーン読み込みの情報（確認1・3）
    stats_games = []            # 統計が届いたときの（フレーム番号, 項目数）（確認4）
    step_times = []             # 1判断ぶん進めるのにかかった実時間
    config_problems = None      # 設定値の食い違い（確認1）

    while True:
        d = conn.next_decision()
        frame = conn.frame

        # --- 届いた情報の処理 ---
        # シーン読み込みの知らせを先に処理する（やり直し直後のフレームを、確認係が見分けられるように）
        for ev in d.events:
            recorder.write(ev)
            if ev.get("type") == "scene":
                scenes.append(ev)
                if extra is not None:
                    extra.on_scene(ev)
                show_scene(ev)
                if config_problems is None:
                    config_problems = conn.check_config(ev)
                    show_config(ev, config_problems)
                    if config_problems and strict_config:
                        # 設定値が食い違ったまま続けると、状況の読み方がずれる（API の費用も無駄になる）ので、ここで止める
                        raise ConfigMismatch("、".join(config_problems))
        for fr in d.frames:
            recorder.write(fr)
            frame_numbers.append(int(fr["frame"]))
            if extra is not None:
                extra.on_frame(fr)
            latest_record = fr
            events_since_switch.extend(fr.get("events") or [])
        if conn.decision > 0:   # 接続直後（リセット時）の分は数えない
            per_decision[len(d.frames)] += 1
        if d.stats:
            stats_games.append((frame, len(d.stats)))
            recorder.write({"type": "stats", "frame": frame, "stats": d.stats})
            print("{} frame={}（{:.2f}秒） 統計を受け取りました（{}項目）".format(
                TAG, frame, settings.frames_to_sec(frame), len(d.stats)), flush=True)
        for ev in d.episode_ends:
            line = "{} frame={}（{:.2f}秒） 区切り: {}".format(
                TAG, ev["frame"], settings.frames_to_sec(ev["frame"]), KIND_LABELS[ev["kind"]])
            if ev["kind"] == KIND_GAME_END:
                line += " → game={}".format(ev["next_game"])
            print(line, flush=True)

        if frame >= plan["total"]:
            break

        # --- 間接操作（2b と同じ。段階3c から、選ぶ係に状況を渡す） ---
        if frame >= next_switch:
            chosen = selector.select({"record": latest_record, "events": events_since_switch, "frame": frame})
            events_since_switch = []
            kept = chosen is None
            if not kept:
                current = chosen
            elif current is None:
                current = "止まる"   # 最初の選択から失敗したときは止まっておく
            # 次の切り替えは「予定のフレーム＋間隔」にする（「今のフレーム＋間隔」だと、判断のタイミングとの
            # ずれが毎回たまって間隔が延びるため）。大きく遅れたときだけ、今のフレームから数え直す
            next_switch += plan["interval"]
            if next_switch <= frame:
                next_switch = frame + plan["interval"]
            info = dict(getattr(selector, "last", None) or {})
            info.pop("summary", None)   # 要約の全文は LLM の記録（llm_日付.jsonl）に残っているので、ここには入れない
            logger.log(dict({"event": "command_switch", "command": current, "selector": selector.name,
                             "kept_previous": kept, "detail": info}, **conn.timeline()))
            if extra is not None and hasattr(extra, "on_switch"):
                extra.on_switch(frame, current)
            if on_switch is not None:
                on_switch(frame, current, selector)
        h, v = movement_of(current)
        t0 = time.perf_counter()
        conn.act(h, v, d.n_agents)
        step_times.append(time.perf_counter() - t0)

    return report(conn, plan, frame_numbers, per_decision, scenes, stats_games, step_times,
                  config_problems, warn_counter, logger, recorder, extra)


def show_scene(ev):
    """シーン読み込みの情報（何回目か、PlayerAgent の数と内訳）を表示する。"""
    agents = ev.get("playerAgents", [])
    active = sum(1 for a in agents if a.get("activeInHierarchy") and a.get("enabled"))
    print("{} シーン読み込み {}回目（frame={}）: PlayerAgent {}体（有効{}・無効{}）".format(
        TAG, ev.get("loadCount"), ev.get("frame"), ev.get("playerAgentCount"), active,
        ev.get("playerAgentCount", 0) - active), flush=True)
    for a in agents:
        print("{}     - {}（シーン: {}、有効: {}、{}）".format(
            TAG, a.get("name"), a.get("scene"), a.get("activeInHierarchy") and a.get("enabled"),
            a.get("behaviorType")), flush=True)


def show_config(ev, problems):
    """Unity から届いた設定値と、settings.py との照合結果を表示する。"""
    head = "{} 設定値: fixedDeltaTime={} DecisionPeriod={} MaxStep={} timeScale={}".format(
        TAG, ev.get("fixedDeltaTime"), ev.get("decisionPeriod"), ev.get("maxStep"), ev.get("timeScale"))
    if problems:
        print(head + " → settings.py と食い違い: " + "、".join(problems), flush=True)
    else:
        print(head + " → settings.py と一致", flush=True)


def report(conn, plan, frame_numbers, per_decision, scenes, stats_games, step_times,
           config_problems, warn_counter, logger, recorder, extra=None):
    """5項目（と追加の確認係があればその結果）を判定して表示し、記録に残す。すべて OK なら終了コード0、NG があれば8を返す。"""
    results = []

    # 1. 設定値
    results.append(("設定値が届き settings.py と一致", config_problems is not None and not config_problems,
                    "届かず" if config_problems is None else ("一致" if not config_problems else "、".join(config_problems))))

    # 2. フレームの抜けと重複（段階3b から、記録は STATE_INTERVAL_FRAMES フレームおきに届く）
    step = settings.STATE_INTERVAL_FRAMES
    gaps = [b - a for a, b in zip(frame_numbers, frame_numbers[1:]) if b - a != step]
    dup = len(frame_numbers) - len(set(frame_numbers))
    expected = ((frame_numbers[-1] - frame_numbers[0]) // step + 1) if frame_numbers else 0
    ok2 = bool(frame_numbers) and not gaps and dup == 0
    results.append(("記録の抜け・重複なし", ok2, "{}フレームおきの記録 {}/{}件（抜け{}・重複{}）、1判断あたりの件数の分布 {}".format(
        step, len(frame_numbers), expected, len(gaps), dup, dict(sorted(per_decision.items())))))

    # 3. シーン読み込みと PlayerAgent の数
    counts = ["{}回目={}体".format(s.get("loadCount"), s.get("playerAgentCount")) for s in scenes]
    ok3 = len(scenes) >= 1
    results.append(("シーン読み込みごとに PlayerAgent の数が届く", ok3, "、".join(counts) if counts else "届かず"))

    # 4. 統計と警告
    game_ends = conn.counts[KIND_GAME_END]
    ok4 = warn_counter.unknown_side_channel == 0 and (game_ends == 0 or len(stats_games) >= 1)
    results.append(("統計が届き、警告が出ない", ok4, "統計 {}回、Unknown side channel の警告 {}件（ゲーム終了 {}回）".format(
        len(stats_games), warn_counter.unknown_side_channel, game_ends)))

    # 5. 時間とデータ量
    ch = conn.state_channel
    avg_ms = 1000.0 * sum(step_times) / max(1, len(step_times))
    parse_ms = 1000.0 * ch.parse_sec / max(1, len(step_times))
    span = (frame_numbers[-1] - frame_numbers[0] + step) if frame_numbers else 0  # 受け取った記録が覆うフレーム数
    kb_per_game = (ch.total_bytes / 1024.0) * (3000.0 / max(1, span))
    results.append(("時間とデータ量（参考）", True, "平均 {:.1f}ms/判断（うち受け取りの読み取り {:.3f}ms）、"
                    "{}件 {:.0f}KB 受信、1ゲーム（3000フレーム）あたり約{:.0f}KB、読めなかった件数 {}".format(
                        avg_ms, parse_ms, ch.total_messages, ch.total_bytes / 1024.0, kb_per_game, ch.bad_messages)))

    # 追加の確認（段階3b など）
    if extra is not None:
        results.extend(extra.results())

    print("{} ---- 確認結果 ----".format(TAG), flush=True)
    for i, (name, ok, detail) in enumerate(results, 1):
        print("{} {}. {}: {} … {}".format(TAG, i, name, "OK" if ok else "NG", detail), flush=True)
    print("{} {}完了。フレーム番号の出どころ: {}".format(TAG, settings.describe(plan["total"]), conn.frame_source), flush=True)

    logger.log(dict({"event": "finished", "results": [
        {"item": n, "ok": ok, "detail": det} for n, ok, det in results],
        "state_log": os.path.basename(recorder.path)}, **conn.timeline()))
    return EXIT_OK if all(ok for _, ok, _ in results) else EXIT_CHECK_NG


if __name__ == "__main__":
    sys.exit(main())
