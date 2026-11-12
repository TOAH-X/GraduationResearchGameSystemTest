# -*- coding: utf-8 -*-
"""
【このファイルの役割】
Unity の LLMBridgeManager が自作サイドチャンネル（GameStateChannel.cs）で送ってくる情報を受け取る係。
届いた JSON を種類ごとにためておき、判断のたびに env_wrapper.py が取り出す。

届く種類:
- "scene" : シーンを読み込むたび。何回目か、設定値（fixedDeltaTime、DecisionPeriod、MaxStep）、PlayerAgent の一覧
- "frame" : 毎フレーム。通しのフレーム番号、Academy のステップ数、ゲーム内時刻、自分と相方の位置

チャンネルの ID は Unity 側（GameStateChannel.cs の ChannelGuid）と同じ値にしてある。変えるときは両方を変える。

【どこから使われるか】
unity/env_wrapper.py。
"""
import json
import time
import uuid
from typing import Any, Dict, List

from mlagents_envs.side_channel.incoming_message import IncomingMessage
from mlagents_envs.side_channel.side_channel import SideChannel

# Unity 側（GameStateChannel.cs）と同じ ID
CHANNEL_ID = uuid.UUID("7a3c1e52-4b8d-4f2a-9c61-2d5e8b0f4a17")


class StateChannel(SideChannel):
    """Unity から届く JSON を受け取り、「frame」と「それ以外（scene など）」に分けてためておく係。"""

    def __init__(self):
        # type: () -> None
        """チャンネルの ID を登録し、受け取ったものをためる入れ物と、数え上げを用意する。"""
        super(StateChannel, self).__init__(CHANNEL_ID)
        self._frames = []   # type: List[Dict[str, Any]]
        self._events = []   # type: List[Dict[str, Any]]
        self.total_messages = 0     # 受け取った件数
        self.total_bytes = 0        # 受け取った文字列の大きさの合計（バイト）
        self.parse_sec = 0.0        # JSON の読み取りにかかった時間の合計（秒）
        self.bad_messages = 0       # 読めなかった件数

    def on_message_received(self, msg):
        # type: (IncomingMessage) -> None
        """Unity からメッセージが1件届くたびに呼ばれる。JSON として読み、種類ごとにためる。"""
        t0 = time.perf_counter()
        text = msg.read_string()
        self.total_messages += 1
        self.total_bytes += len(text.encode("utf-8"))
        try:
            data = json.loads(text)
        except ValueError:
            self.bad_messages += 1
            return
        finally:
            self.parse_sec += time.perf_counter() - t0
        if data.get("type") == "frame":
            self._frames.append(data)
        else:
            self._events.append(data)

    def pop_frames(self):
        # type: () -> List[Dict[str, Any]]
        """前回取り出してから届いた「frame」を、届いた順に全部取り出す。"""
        out, self._frames = self._frames, []
        return out

    def pop_events(self):
        # type: () -> List[Dict[str, Any]]
        """前回取り出してから届いた「frame 以外」（scene など）を、届いた順に全部取り出す。"""
        out, self._events = self._events, []
        return out
