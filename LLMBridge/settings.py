# -*- coding: utf-8 -*-
"""
【このファイルの役割】
時間に関わる設定値を1か所にまとめ、「秒」と「フレーム」を換算する。
Unity 側の設定（FixedUpdate の間隔、Player の判断の間隔）を変えたら、ここだけを直せばよい。

言葉の決まり（PLAN.md §5.5）:
- フレーム   : FixedUpdate 1回。内部の基準。ML-Agents の1歩と同じ
- 判断       : Player が行動を決める1回。DECISION_PERIOD フレームごと。Python が操作を送れるのはこのときだけ
- ゲーム内秒 : フレーム数 × FIXED_DT。人が読むための表示

段階3以降は、接続時に Unity から実際の値を受け取り、ここの値と照合する予定。

【どこから使われるか】
step2a_connect.py（と、以降の段階のスクリプト）。
"""
from typing import Tuple

# FixedUpdate 1回の長さ（秒）。Unity の Project Settings > Time > Fixed Timestep と同じ値にする
FIXED_DT = 0.02

# Player が何フレームごとに判断するか。LLMTestScene の Player の Decision Requester > Decision Period と同じ値にする
DECISION_PERIOD = 3

# Unity Editor と通信するポート（ML-Agents の Editor 用の既定値）
EDITOR_PORT = 5004


def frames_to_sec(frames):
    # type: (int) -> float
    """フレーム数をゲーム内の秒に直す（表示用）。"""
    return frames * FIXED_DT


def sec_to_frames(sec):
    # type: (float) -> int
    """ゲーム内の秒をフレーム数に直す（秒で指定されたときに使う）。端数は四捨五入する。"""
    return int(round(sec / FIXED_DT))


def align_to_decision(frames):
    # type: (int) -> Tuple[int, bool]
    """フレーム数を、判断のタイミング（DECISION_PERIOD の倍数）に切り上げてそろえる。

    Python が操作を送れるのは判断のときだけなので、たとえば 100 は 102 になる。
    戻り値は（そろえたフレーム数, 値を変えたかどうか）。
    """
    if frames <= 0:
        return 0, frames != 0
    aligned = ((frames + DECISION_PERIOD - 1) // DECISION_PERIOD) * DECISION_PERIOD
    return aligned, aligned != frames


def frames_to_decisions(frames):
    # type: (int) -> int
    """フレーム数が判断何回分にあたるかを返す（判断のタイミングにそろえた値を渡す前提）。"""
    return frames // DECISION_PERIOD


def describe(frames):
    # type: (int) -> str
    """フレーム数を「600フレーム（12.00秒、200判断）」の形の文字列にする（画面表示用）。"""
    return "{}フレーム（{:.2f}秒、{}判断）".format(frames, frames_to_sec(frames), frames_to_decisions(frames))
