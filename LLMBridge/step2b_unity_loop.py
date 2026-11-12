# -*- coding: utf-8 -*-
"""
【このファイルの役割】
段階2b（Unity との配線：間接操作と耐久）で実行するスクリプト。OpenAI の API は使わない。
LLMTestScene の Player を、テスト用の間接操作（止まる・左・右・上・下）で動かしながら1ゲーム（60秒）を超えて回し、
次の3つに耐えることを確かめる。
- 長い停止（LLM の応答がとても遅い場合を想定して、1回だけわざと長く止まる）
- 60秒でゲームが終わったときの、シーンのやり直し（Player エージェントが増えないか）
- MaxStep による20秒ごとのエピソードの区切り

部品の分担:
- unity/env_wrapper.py      : Unity との接続、フレームの数え方、区切りの見分け、エージェント数の見張り
- command/test_commands.py  : テスト用の間接操作と、その移動の値（対応表）
- command/selector.py       : 間接操作を選ぶ係（今回はランダム版）

使い方（LLMBridge フォルダで実行し、表示が出たら Unity の再生ボタンを押す）:
  python step2b_unity_loop.py
  python step2b_unity_loop.py --sec 20 --long-pause-sec 5     （短く試す。シーンのやり直しは確かめられない）

終了コード: 0=成功 / 2=設定エラー / 5=Unity との接続・通信のエラー / 7=Player エージェントが増えた（要相談）
"""
from __future__ import print_function

import argparse
import os
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
from command.selector import RandomSelector  # noqa: E402
from command.test_commands import command_names, movement_of  # noqa: E402
from logging_util import JsonlLogger  # noqa: E402
from unity.env_wrapper import KIND_GAME_END, KIND_LABELS, KIND_MAX_STEP, UnityConnectError, UnityConnection  # noqa: E402

TAG = "[step2b]"
EXIT_OK = 0
EXIT_CONFIG = 2
EXIT_UNITY = 5
EXIT_DUPLICATE = 7


class ConfigProblem(Exception):
    """オプションの指定がおかしいときに使う（終了コード2）。"""


def parse_args(argv=None):
    """コマンドラインのオプションを読み取る。長さ・間隔・位置は、フレームでも秒でも指定できる（標準はフレーム）。"""
    p = argparse.ArgumentParser(description="段階2b：間接操作の切り替えと耐久の確認（API は使わない）")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--frames", type=int, help="動かす長さ（フレーム）。既定4500（90秒）")
    g.add_argument("--sec", type=float, help="動かす長さ（ゲーム内秒）")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--command-interval-frames", type=int, help="間接操作を選び直す間隔（フレーム）。既定150（3秒）")
    g.add_argument("--command-interval-sec", type=float, help="間接操作を選び直す間隔（ゲーム内秒）")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--long-pause-at-frames", type=int, help="長い停止の位置（フレーム）。既定1500（30秒）。0なら止まらない")
    g.add_argument("--long-pause-at-sec", type=float, help="長い停止の位置（ゲーム内秒）")
    p.add_argument("--long-pause-sec", type=float, default=65.0, help="長い停止の長さ（実時間の秒）。既定65")
    p.add_argument("--seed", type=int, default=None, help="ランダムな選び方を再現したいときの乱数の種")
    p.add_argument("--wait-sec", type=int, default=120, help="Unity からの接続や応答を待つ上限（秒）。既定120")
    p.add_argument("--behavior", default="Player", help="操作する behavior 名。既定 Player")
    return p.parse_args(argv)


def resolve(args):
    """オプションから、長さ・切り替え間隔・長い停止の位置をフレームで決め、判断のタイミングにそろえる。"""
    plan = {}
    for key, frames, sec, default in (
        ("total", args.frames, args.sec, 4500),
        ("interval", args.command_interval_frames, args.command_interval_sec, 150),
        ("long_pause_at", args.long_pause_at_frames, args.long_pause_at_sec, 1500),
    ):
        value = settings.frames_from_options(frames, sec, default)
        if value > 0:
            aligned, changed = settings.align_to_decision(value)
            if changed:
                print("{} {} を判断のタイミングに合わせて {}フレームにしました（指定 {}フレーム）".format(
                    TAG, key, aligned, value), flush=True)
            value = aligned
        plan[key] = value

    if plan["total"] <= 0:
        raise ConfigProblem("長さは1フレーム以上にしてください。")
    if plan["interval"] <= 0:
        raise ConfigProblem("切り替えの間隔は1フレーム以上にしてください。")
    if plan["long_pause_at"] >= plan["total"]:
        raise ConfigProblem("長い停止の位置（{}フレーム）は長さ（{}フレーム）より前にしてください。".format(
            plan["long_pause_at"], plan["total"]))
    if args.long_pause_sec < 0 or args.wait_sec <= 0:
        raise ConfigProblem("--long-pause-sec は0以上、--wait-sec は1以上にしてください。")
    return plan


def main(argv=None):
    """全体の流れ：設定を決める → 接続する → 判断ごとに間接操作を送る（切り替え・区切り・長い停止）→ まとめを表示。

    戻り値は終了コード（0=成功 / 2=設定エラー / 5=Unity との接続・通信のエラー / 7=Player エージェントが増えた）。
    """
    args = parse_args(argv)
    try:
        plan = resolve(args)
        selector = RandomSelector(command_names(), seed=args.seed)
    except (ConfigProblem, ValueError) as e:
        sys.stderr.write("{} 設定エラー: {}\n".format(TAG, e))
        return EXIT_CONFIG

    logger = JsonlLogger(os.path.join(HERE, "logs"), stage="step2b", prefix="unity")
    print("{} 長さ={} 切り替え={}フレームごと FIXED_DT={} DECISION_PERIOD={} seed={}".format(
        TAG, settings.describe(plan["total"]), plan["interval"], settings.FIXED_DT, settings.DECISION_PERIOD,
        args.seed), flush=True)
    if plan["long_pause_at"] > 0:
        print("{} {}フレーム目（{:.2f}秒）で {:.1f}秒止まります（LLM の遅い返事を想定）".format(
            TAG, plan["long_pause_at"], settings.frames_to_sec(plan["long_pause_at"]), args.long_pause_sec), flush=True)
    print("{} Unity の再生ボタンを押してください（ポート{}、最大{}秒待ちます）".format(
        TAG, settings.EDITOR_PORT, args.wait_sec), flush=True)

    conn = UnityConnection(behavior=args.behavior, wait_sec=args.wait_sec, logger=logger)
    try:
        info = conn.connect()
        print("{} 接続しました。behavior: {} 観察={} 操作=連続{}".format(
            TAG, info["behavior"], info["obs_sizes"], info["continuous"]), flush=True)
        logger.log({"event": "plan", "total_frames": plan["total"], "interval_frames": plan["interval"],
                    "long_pause_at_frame": plan["long_pause_at"], "long_pause_sec": args.long_pause_sec,
                    "seed": args.seed, "selector": selector.name})
        return run_loop(conn, selector, plan, args.long_pause_sec, logger)
    except UnityConnectError as e:
        sys.stderr.write("{} 失敗: {}\n".format(TAG, e))
        logger.log({"event": "connect_failed", "message": str(e)})
        return EXIT_UNITY
    except KeyboardInterrupt:
        sys.stderr.write("{} 中断しました（Ctrl+C）\n".format(TAG))
        logger.log(dict({"event": "interrupted"}, **conn.timeline()))
        return EXIT_UNITY
    except UnityException as e:
        sys.stderr.write("{} 失敗: Unity との通信が切れました（{}: {}）。再生が止められた可能性があります。\n".format(
            TAG, type(e).__name__, e))
        logger.log(dict({"event": "unity_error", "error": type(e).__name__, "message": str(e)}, **conn.timeline()))
        return EXIT_UNITY
    finally:
        conn.close()
        print("{} log saved: {}".format(TAG, os.path.relpath(logger.path, HERE)))


def run_loop(conn, selector, plan, long_pause_sec, logger):
    """判断ごとに、切り替え・区切りの表示・長い停止をこなしながら、今の間接操作を Player に送り続ける。"""
    current = None          # 今の間接操作の名前
    next_switch = 0         # 次に選び直す通しのフレーム番号
    switches = 0            # 切り替えた回数
    long_paused = False     # 長い停止を済ませたか
    step_times = []         # 1判断ぶん進めるのにかかった実時間（長い停止を除く）

    while True:
        d = conn.next_decision()
        frame = conn.frame

        # 区切りが来ていたら表示する（記録は env_wrapper が済ませている）
        for ev in d.episode_ends:
            line = "{} frame={}（{:.2f}秒） 区切り: {}（interrupted={}）".format(
                TAG, ev["frame"], settings.frames_to_sec(ev["frame"]), KIND_LABELS[ev["kind"]], ev["interrupted"])
            if ev["kind"] == KIND_GAME_END:
                line += " → game={}".format(ev["next_game"])
            print(line, flush=True)

        if d.n_agents > 1 and len(conn.duplicate_frames) == 1:
            print("{} 注意: Player エージェントが{}体になりました（ID: {}）".format(TAG, d.n_agents, d.agent_ids), flush=True)

        if frame >= plan["total"]:
            break

        # 切り替えのタイミングなら、間接操作を選び直す（段階5以降は、ここで LLM に問い合わせる）
        if frame >= next_switch:
            current = selector.select()
            switches += 1
            next_switch = frame + plan["interval"]
            print("{} frame={}（{:.2f}秒） game={} 間接操作: {}".format(
                TAG, frame, settings.frames_to_sec(frame), conn.game, current), flush=True)
            logger.log(dict({"event": "command_switch", "command": current, "selector": selector.name},
                            **conn.timeline()))

        # 長い停止（LLM の返事がとても遅かった場合を真似する）
        if plan["long_pause_at"] > 0 and not long_paused and frame >= plan["long_pause_at"]:
            long_paused = True
            print("{} frame={}（{:.2f}秒）で {:.1f}秒止まります…（LLM の遅い返事を想定）".format(
                TAG, frame, settings.frames_to_sec(frame), long_pause_sec), flush=True)
            logger.log(dict({"event": "long_pause_start", "pause_sec": long_pause_sec}, **conn.timeline()))
            t0 = time.time()
            time.sleep(long_pause_sec)
            logger.log(dict({"event": "long_pause_end", "actual_sec": round(time.time() - t0, 3)}, **conn.timeline()))
            print("{} 再開しました".format(TAG), flush=True)

        logger.log(dict({"event": "decision", "command": current, "player": d.player_pos,
                         "agent_ids": d.agent_ids, "n_agents": d.n_agents}, **conn.timeline()))

        h, v = movement_of(current)
        t0 = time.perf_counter()
        conn.act(h, v, d.n_agents)
        step_times.append(time.perf_counter() - t0)

    avg_ms = 1000.0 * sum(step_times) / max(1, len(step_times))
    dup = len(conn.duplicate_frames) > 0
    print("{} エージェント数: 最大{}体、これまでに見た ID: {}{}".format(
        TAG, conn.max_agents, sorted(conn.seen_agent_ids), "（増えました）" if dup else "（増えていません）"), flush=True)
    print("{} {}完了。ゲーム終了{}回、20秒の区切り{}回、間接操作の切り替え{}回、平均 {:.1f}ms/判断".format(
        TAG, settings.describe(plan["total"]), conn.counts[KIND_GAME_END], conn.counts[KIND_MAX_STEP],
        switches, avg_ms), flush=True)
    logger.log(dict({"event": "finished", "game_end_count": conn.counts[KIND_GAME_END],
                     "max_step_count": conn.counts[KIND_MAX_STEP], "switches": switches,
                     "max_agents": conn.max_agents, "seen_agent_ids": sorted(conn.seen_agent_ids),
                     "duplicate_decisions": len(conn.duplicate_frames), "avg_ms_per_decision": round(avg_ms, 3)},
                    **conn.timeline()))
    if dup:
        sys.stderr.write("{} Player エージェントが増えました。作業を止めて相談してください。\n".format(TAG))
        return EXIT_DUPLICATE
    print("{} 終了しました".format(TAG))
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
