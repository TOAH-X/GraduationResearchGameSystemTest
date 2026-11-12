# -*- coding: utf-8 -*-
"""
【このファイルの役割】
段階2a（Unity との配線：接続と同期）で実行するスクリプト。OpenAI の API は使わない。
Unity Editor で再生した LLMTestScene に接続し、Player（Behavior Type = Default）を Python から操作する。
途中でわざと数秒止まり、そのあいだゲームも止まっている（ロックステップ）ことを確かめる。

流れ:
1. 「再生ボタンを押してください」と表示し、Unity からの接続を待つ
2. つながったら、Player の behavior 名・観察の数・操作の数を表示する
3. 指定の長さ（既定600フレーム＝12秒）のあいだ、判断のたびに「止まる」操作を送る
4. 指定の位置（既定300フレーム目）で、指定の秒数（既定5秒）だけ Python が止まる
5. 止まる前後の観察（敵や弾の位置など）の変化量を、ふだんの1判断分と比べて同期を判定する
6. 接続を閉じ、結果を表示する。記録は logs/unity_YYYYMMDD.jsonl に残す

使い方（LLMBridge フォルダで実行し、表示が出たら Unity の再生ボタンを押す）:
  python step2a_connect.py
  python step2a_connect.py --sec 12 --pause-at-sec 6 --pause-sec 5

終了コード: 0=成功 / 2=設定エラー / 5=Unity との接続・通信のエラー / 6=同期の判定が NG
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

import numpy as np  # noqa: E402
from mlagents_envs.base_env import ActionTuple  # noqa: E402
from mlagents_envs.environment import UnityEnvironment  # noqa: E402
from mlagents_envs.exception import (  # noqa: E402
    UnityException,
    UnityTimeOutException,
    UnityWorkerInUseException,
)

import settings  # noqa: E402
from logging_util import JsonlLogger  # noqa: E402

TAG = "[step2a]"
EXIT_OK = 0
EXIT_CONFIG = 2
EXIT_UNITY = 5
EXIT_SYNC_NG = 6


class ConfigProblem(Exception):
    """オプションの指定がおかしいときに使う（終了コード2）。"""


def parse_args(argv=None):
    """コマンドラインのオプションを読み取る。長さと停止位置は、フレームでも秒でも指定できる（標準はフレーム）。"""
    p = argparse.ArgumentParser(description="段階2a：Unity との接続と同期の確認（API は使わない）")
    g_len = p.add_mutually_exclusive_group()
    g_len.add_argument("--frames", type=int, help="動かす長さ（フレーム）。既定600")
    g_len.add_argument("--sec", type=float, help="動かす長さ（ゲーム内秒）")
    g_pause = p.add_mutually_exclusive_group()
    g_pause.add_argument("--pause-at-frames", type=int, help="何フレーム目で止まるか。既定300。0なら止まらない")
    g_pause.add_argument("--pause-at-sec", type=float, help="何秒目で止まるか（ゲーム内秒）")
    p.add_argument("--pause-sec", type=float, default=5.0, help="Python が止まる実時間（秒）。既定5")
    p.add_argument("--wait-sec", type=int, default=120, help="Unity からの接続や応答を待つ上限（秒）。既定120")
    p.add_argument("--behavior", default="Player", help="操作する behavior 名。既定 Player")
    return p.parse_args(argv)


def resolve_frames(args):
    """オプションから「動かす長さ」と「止まる位置」をフレームで決める。判断のタイミングにそろえ、変えたら知らせる。"""
    if args.sec is not None:
        total = settings.sec_to_frames(args.sec)
    else:
        total = 600 if args.frames is None else args.frames
    if args.pause_at_sec is not None:
        pause_at = settings.sec_to_frames(args.pause_at_sec)
    else:
        pause_at = 300 if args.pause_at_frames is None else args.pause_at_frames

    total, changed = settings.align_to_decision(total)
    if changed:
        print("{} 長さを判断のタイミングに合わせて {}フレームにしました".format(TAG, total), flush=True)
    if pause_at > 0:
        pause_at, changed = settings.align_to_decision(pause_at)
        if changed:
            print("{} 止まる位置を判断のタイミングに合わせて {}フレームにしました".format(TAG, pause_at), flush=True)

    if total <= 0:
        raise ConfigProblem("長さは1フレーム以上にしてください。")
    if pause_at >= total:
        raise ConfigProblem("止まる位置（{}フレーム）は長さ（{}フレーム）より前にしてください。".format(pause_at, total))
    if args.pause_sec < 0 or args.wait_sec <= 0:
        raise ConfigProblem("--pause-sec は0以上、--wait-sec は1以上にしてください。")
    return total, pause_at


def find_behavior(env, wanted):
    """接続した Unity の behavior 一覧から、操作する behavior（例：Player?team=0）を探す。"""
    names = list(env.behavior_specs.keys())
    for name in names:
        if name.split("?")[0] == wanted:
            return name, names
    return None, names


def describe_spec(spec):
    """behavior の観察と操作の形を、表示と記録のための辞書にまとめる。"""
    obs_sizes = [int(np.prod(o.shape)) for o in spec.observation_specs]
    return {
        "obs_sizes": obs_sizes,
        "continuous": spec.action_spec.continuous_size,
        "discrete_branches": list(spec.action_spec.discrete_branches),
    }


def make_stop_action(spec, n_agents):
    """「その場に止まる」操作を作る。Player の連続値は（横, 縦, ショット）なので (0, 0, -1)。それ以外の形なら全部0。"""
    size = spec.action_spec.continuous_size
    one = np.zeros(size, dtype=np.float32)
    if size >= 3:
        one[2] = -1.0  # ショットしない（ただし既存コードでは常にショットが出る設定）
    continuous = np.tile(one, (n_agents, 1))
    discrete = np.zeros((n_agents, len(spec.action_spec.discrete_branches)), dtype=np.int32)
    return ActionTuple(continuous=continuous, discrete=discrete)


def obs_vector(decision_steps):
    """判断を求めてきたエージェント（先頭の1体）の観察を、1本の数値の並びにして返す。"""
    parts = [np.asarray(o[0], dtype=np.float64).ravel() for o in decision_steps.obs]
    return np.concatenate(parts)


def change_amount(a, b):
    """2つの観察のあいだの変化量（各値の差の絶対値の平均）を返す。"""
    return float(np.mean(np.abs(a - b)))


def judge_lockstep(history, before, after, pause_sec):
    """止まる前後の変化量を、ふだんの1判断分の変化量、そして「本当に止まった秒数ぶん進んだ場合」の変化量と比べる。

    戻り値は（判定 "OK" / "NG" / "判定不能", 表示用の数値をまとめた辞書）。
    """
    steps = [change_amount(history[i], history[i + 1]) for i in range(len(history) - 1)]
    paused = change_amount(before, after)
    result = {"paused_change": paused}
    if len(steps) < 5 or max(steps) == 0.0:
        result["reason"] = "停止前の観察がほとんど変化していないため、数値では比べられない"
        return "判定不能", result

    normal_median = float(np.median(steps))
    normal_p95 = float(np.percentile(steps, 95))
    result.update({"normal_median": normal_median, "normal_p95": normal_p95})

    # 実時間 pause_sec 秒のあいだゲームが進んでいたら、何判断分になるか（例：5秒→約83判断）
    lag = int(round(pause_sec / (settings.FIXED_DT * settings.DECISION_PERIOD)))
    if 0 < lag < len(history):
        lag_change = change_amount(history[-1 - lag], history[-1])
        result.update({"lag_decisions": lag, "lag_change": lag_change})
    else:
        lag_change = None

    ok = paused <= max(normal_p95 * 1.5, normal_median * 3.0)
    if lag_change is not None and lag_change > 0:
        ok = ok and paused < lag_change * 0.5
    return ("OK" if ok else "NG"), result


def main(argv=None):
    """全体の流れ：設定を決める → 接続を待つ → 判断ごとに操作を送る（途中で止まる）→ 同期を判定 → 閉じる。

    戻り値は終了コード（0=成功 / 2=設定エラー / 5=Unity との接続・通信のエラー / 6=同期の判定が NG）。
    """
    args = parse_args(argv)
    try:
        total_frames, pause_at = resolve_frames(args)
    except ConfigProblem as e:
        sys.stderr.write("{} 設定エラー: {}\n".format(TAG, e))
        return EXIT_CONFIG

    logger = JsonlLogger(os.path.join(HERE, "logs"), stage="step2a", prefix="unity")
    print("{} 長さ={} FIXED_DT={} DECISION_PERIOD={}".format(
        TAG, settings.describe(total_frames), settings.FIXED_DT, settings.DECISION_PERIOD))
    if pause_at > 0:
        print("{} {}フレーム目（{:.2f}秒）で {:.1f}秒止まります".format(
            TAG, pause_at, settings.frames_to_sec(pause_at), args.pause_sec))
    print("{} Unity の再生ボタンを押してください（ポート{}、最大{}秒待ちます）".format(
        TAG, settings.EDITOR_PORT, args.wait_sec), flush=True)

    env = None
    try:
        try:
            env = UnityEnvironment(file_name=None, base_port=settings.EDITOR_PORT, timeout_wait=args.wait_sec)
        except UnityWorkerInUseException:
            sys.stderr.write("{} 失敗: ポート{}がほかのプログラム（mlagents-learn など）に使われています。\n".format(
                TAG, settings.EDITOR_PORT))
            return EXIT_UNITY
        except UnityTimeOutException:
            sys.stderr.write("{} 失敗: {}秒以内に Unity から接続がありませんでした。再生ボタンを押したか確認してください。\n".format(
                TAG, args.wait_sec))
            logger.log({"event": "connect_timeout", "wait_sec": args.wait_sec})
            return EXIT_UNITY

        env.reset()
        bname, all_names = find_behavior(env, args.behavior)
        if bname is None:
            sys.stderr.write("{} 失敗: behavior「{}」が見つかりません。見つかったもの: {}\n".format(
                TAG, args.behavior, all_names))
            sys.stderr.write("{} 対処: LLMTestScene を再生しているか、Player の Behavior Type が Default かを確認してください。\n".format(TAG))
            logger.log({"event": "behavior_not_found", "behaviors": all_names})
            return EXIT_UNITY
        spec = env.behavior_specs[bname]
        spec_info = describe_spec(spec)
        print("{} 接続しました。behavior: {} 観察={} 操作=連続{} 離散{}".format(
            TAG, bname, spec_info["obs_sizes"], spec_info["continuous"], spec_info["discrete_branches"]), flush=True)
        logger.log({"event": "connected", "behavior": bname, "all_behaviors": all_names, "spec": spec_info,
                    "fixed_dt": settings.FIXED_DT, "decision_period": settings.DECISION_PERIOD,
                    "total_frames": total_frames, "pause_at_frame": pause_at, "pause_sec": args.pause_sec})

        return run_loop(env, bname, spec, total_frames, pause_at, args.pause_sec, logger)

    except KeyboardInterrupt:
        sys.stderr.write("{} 中断しました（Ctrl+C）\n".format(TAG))
        logger.log({"event": "interrupted"})
        return EXIT_UNITY
    except UnityException as e:
        sys.stderr.write("{} 失敗: Unity との通信が切れました（{}: {}）。再生が止められた可能性があります。\n".format(
            TAG, type(e).__name__, e))
        logger.log({"event": "unity_error", "error": type(e).__name__, "message": str(e)})
        return EXIT_UNITY
    finally:
        if env is not None:
            try:
                env.close()
            except Exception:  # 閉じるときの失敗は結果に影響しないので無視する
                pass
        print("{} log saved: {}".format(TAG, os.path.relpath(logger.path, HERE)))


def run_loop(env, bname, spec, total_frames, pause_at, pause_sec, logger):
    """判断のたびに観察を受け取り、「止まる」操作を送って1判断ぶん進める。止まる位置では Python がわざと待つ。"""
    decision = 0          # 何回目の判断か（0から数える）
    history = []          # 停止前の観察（同期の判定に使う）
    before = None         # 止まる直前の観察
    verdict = None
    detail = None
    step_times = []       # 1判断ぶん進めるのにかかった実時間（停止を除く）
    episode_ends = 0

    while True:
        decision_steps, terminal_steps = env.get_steps(bname)

        # エピソードの区切り（MaxStep など）が来たら記録だけする（2a では起きない想定。2b で扱う）
        if len(terminal_steps) > 0:
            episode_ends += 1
            interrupted = bool(terminal_steps.interrupted[0])
            print("{} エピソードの区切りを受け取りました（interrupted={}）".format(TAG, interrupted))
            logger.log({"event": "episode_end", "decision": decision, "interrupted": interrupted})

        if len(decision_steps) == 0:
            # 判断を求めていない（区切りの直後など）。操作は送らずに進める
            env.step()
            continue

        frame = decision * settings.DECISION_PERIOD  # 判断の回数から求めたフレーム番号（段階3で Unity の実測値に置き換える）
        obs = obs_vector(decision_steps)
        logger.log({"event": "decision", "frame": frame, "decision": decision,
                    "game_sec": round(settings.frames_to_sec(frame), 4),
                    "player_x": round(float(obs[0]) * 6.0, 4), "player_y": round(float(obs[1]) * 5.0, 4)})

        # 止まった直後の判断なら、止まる前と比べて同期を判定する
        if before is not None and verdict is None:
            verdict, detail = judge_lockstep(history, before, obs, pause_sec)
            show_verdict(verdict, detail, pause_sec)
            logger.log(dict({"event": "lockstep_check", "verdict": verdict, "frame": frame}, **detail))

        if frame >= total_frames:
            break

        if before is None:
            history.append(obs)

        # 止まる位置に来たら、Python がわざと待つ（このあいだ Unity も止まっているはず）
        if pause_at > 0 and frame == pause_at and before is None:
            print("{} frame={}（{:.2f}秒）で {:.1f}秒止まります…".format(
                TAG, frame, settings.frames_to_sec(frame), pause_sec), flush=True)
            before = obs
            logger.log({"event": "pause_start", "frame": frame, "pause_sec": pause_sec})
            time.sleep(pause_sec)
            logger.log({"event": "pause_end", "frame": frame})

        env.set_actions(bname, make_stop_action(spec, len(decision_steps)))
        t0 = time.perf_counter()
        env.step()
        step_times.append(time.perf_counter() - t0)
        decision += 1

        if decision % 50 == 0:
            print("{} frame={}（{:.2f}秒） player=({:.2f}, {:.2f})".format(
                TAG, decision * settings.DECISION_PERIOD, settings.frames_to_sec(decision * settings.DECISION_PERIOD),
                float(obs[0]) * 6.0, float(obs[1]) * 5.0), flush=True)

    avg_ms = 1000.0 * sum(step_times) / max(1, len(step_times))
    print("{} {}完了。平均 {:.1f}ms/判断 エピソードの区切り={}回".format(
        TAG, settings.describe(total_frames), avg_ms, episode_ends))
    logger.log({"event": "finished", "frames": total_frames, "decisions": decision,
                "avg_ms_per_decision": round(avg_ms, 3), "episode_ends": episode_ends, "verdict": verdict})

    if verdict == "NG":
        return EXIT_SYNC_NG
    print("{} 終了しました".format(TAG))
    return EXIT_OK


def show_verdict(verdict, detail, pause_sec):
    """同期の判定結果を、比べた数値と一緒に画面に表示する。"""
    if verdict == "判定不能":
        print("{} 同期の判定: 判定不能（{}）。停止中に Editor が固まっていたかを目視で確認してください".format(
            TAG, detail.get("reason")))
        return
    line = "{} 停止をはさんだ変化量={:.4f}（ふだんの1判断: 中央値{:.4f}、95%値{:.4f}".format(
        TAG, detail["paused_change"], detail["normal_median"], detail["normal_p95"])
    if "lag_change" in detail:
        line += "／{:.0f}秒ぶん（{}判断）進んだ場合の目安: {:.4f}".format(
            pause_sec, detail["lag_decisions"], detail["lag_change"])
    line += "）→ ロックステップ {}".format(verdict)
    print(line, flush=True)


if __name__ == "__main__":
    sys.exit(main())
