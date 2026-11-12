# -*- coding: utf-8 -*-
"""
【このファイルの役割】
Unity Editor（ML-Agents）とのやり取りをまとめた部品。段階2a の step2a_connect.py で確かめた処理を、
以降の段階でも使えるように整理したもの。

この部品がやること:
- 接続：再生ボタンが押されるのを待ってつなぎ、操作する behavior（既定 Player）を探す
- 判断の受け取り：Player が判断を求めてくるまで進め、観察を受け取る
- 操作の送信：（横, 縦）の移動を Player に送り、1判断ぶん進める
- フレームの数え方：通しのフレーム番号、判断番号、何ゲーム目か、そのゲームの中のフレーム番号
  （段階3a から：シーンに LLMBridgeManager があれば、Unity が数えた実際のフレーム番号を使う。
    無ければ、これまでどおり「判断の回数 × DECISION_PERIOD」で求めた推定値を使う）
- 区切りの見分け：ゲーム終了（interrupted=False）と、MaxStep による20秒ごとの区切り（interrupted=True）
- エージェント数の見張り：判断を求めてきたエージェントの数と ID を記録し、2体以上になったら知らせる
- 状況の受け取り（段階3a から）：自作サイドチャンネル（state_channel.py）で届くフレームごとの情報と
  シーン読み込みの情報、ML-Agents 標準の統計チャンネル（StatsSideChannel）で届くゲーム終了時の統計を受け取る

【どこから使われるか】
step2b_unity_loop.py、step3a_state.py（と、以降の段階のスクリプト）。
"""
from typing import Any, Dict, List, Optional, Set

import numpy as np
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.exception import UnityTimeOutException, UnityWorkerInUseException
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel

import settings
from unity.state_channel import StateChannel

KIND_GAME_END = "game_end"   # 60秒でゲームが終わった（このあとシーンがやり直される）
KIND_MAX_STEP = "max_step"   # MaxStep による20秒ごとの区切り

KIND_LABELS = {
    KIND_GAME_END: "ゲーム終了",
    KIND_MAX_STEP: "20秒の区切り（MaxStep）",
}


class UnityConnectError(Exception):
    """接続できなかった、または操作する behavior が見つからなかったときに使う。"""


class Decision(object):
    """1回の判断で受け取ったものをまとめた入れ物。"""

    def __init__(self, obs, agent_ids, episode_ends, frames=None, events=None, stats=None):
        # type: (np.ndarray, List[int], List[Dict[str, Any]], Optional[List[Dict[str, Any]]], Optional[List[Dict[str, Any]]], Optional[Dict[str, Any]]) -> None
        """この判断で受け取ったものを持つ。

        obs          : 観察（先頭のエージェントの分）
        agent_ids    : 判断を求めてきたエージェントの ID 一覧
        episode_ends : この判断までに来た区切り
        frames       : 前の判断から届いたフレームごとの情報（LLMBridgeManager が無ければ空）
        events       : 前の判断から届いたシーン読み込みなどの情報
        stats        : 前の判断から届いた統計（ゲーム終了時など。無ければ空）
        """
        self.obs = obs
        self.agent_ids = agent_ids
        self.episode_ends = episode_ends
        self.frames = frames or []
        self.events = events or []
        self.stats = stats or {}

    @property
    def n_agents(self):
        # type: () -> int
        """判断を求めてきたエージェントの数（ふつうは1。2以上なら重複している）。"""
        return len(self.agent_ids)

    @property
    def player_pos(self):
        # type: () -> List[float]
        """Player の位置（観察の先頭2つは、位置を x/6、y/5 にした値なので元に戻す）。"""
        return [round(float(self.obs[0]) * 6.0, 4), round(float(self.obs[1]) * 5.0, 4)]


class UnityConnection(object):
    """Unity との接続を1つ受け持ち、判断の受け取り・操作の送信・数え上げをする係。"""

    def __init__(self, behavior="Player", wait_sec=120, logger=None):
        # type: (str, int, Any) -> None
        """操作する behavior 名、接続や応答を待つ上限（秒）、記録係（JsonlLogger。省略可）を受け取る。"""
        self.behavior = behavior
        self.wait_sec = wait_sec
        self.logger = logger
        self.env = None  # type: Optional[UnityEnvironment]
        self.behavior_name = None  # type: Optional[str]
        self.spec = None
        # 数え上げ
        self.decision = 0           # これまでに送った判断の回数
        self.game = 1               # 何ゲーム目か（1から数える）
        self.game_start_frame = 0   # 今のゲームが始まった通しのフレーム番号
        self.counts = {KIND_GAME_END: 0, KIND_MAX_STEP: 0}
        # エージェント数の見張り
        self.max_agents = 0
        self.seen_agent_ids = set()  # type: Set[int]
        self.duplicate_frames = []  # type: List[int]
        # 状況の受け取り（段階3a から）
        self.state_channel = StateChannel()
        self.stats_channel = StatsSideChannel()
        self.unity_frame = None  # type: Optional[int]  # Unity から届いた最新のフレーム番号（届いていなければ None）

    # ---- 数え方 ----
    @property
    def frame(self):
        # type: () -> int
        """通しのフレーム番号。Unity から届いていればその実測値、無ければ「判断の回数 × DECISION_PERIOD」の推定値。"""
        if self.unity_frame is not None:
            return self.unity_frame
        return self.decision * settings.DECISION_PERIOD

    @property
    def frame_source(self):
        # type: () -> str
        """フレーム番号がどちらから来ているか（"unity"＝実測値、"estimate"＝推定値）。"""
        return "unity" if self.unity_frame is not None else "estimate"

    @property
    def frame_in_game(self):
        # type: () -> int
        """今のゲームの中でのフレーム番号（ゲーム終了の区切りで0に戻る）。"""
        return self.frame - self.game_start_frame

    def timeline(self):
        # type: () -> Dict[str, Any]
        """記録に付ける時間の情報（通しのフレーム、判断番号、何ゲーム目か、ゲームの中のフレーム、ゲーム内秒）。"""
        return {
            "frame": self.frame,
            "decision": self.decision,
            "game": self.game,
            "frame_in_game": self.frame_in_game,
            "game_sec": round(settings.frames_to_sec(self.frame), 4),
        }

    # ---- 接続 ----
    def connect(self):
        # type: () -> Dict[str, Any]
        """再生ボタンが押されるのを待ってつなぎ、操作する behavior を探す。見つかった behavior の情報を返す。"""
        try:
            self.env = UnityEnvironment(file_name=None, base_port=settings.EDITOR_PORT, timeout_wait=self.wait_sec,
                                        side_channels=[self.state_channel, self.stats_channel])
        except UnityWorkerInUseException:
            raise UnityConnectError("ポート{}がほかのプログラム（mlagents-learn など）に使われています。".format(
                settings.EDITOR_PORT))
        except UnityTimeOutException:
            raise UnityConnectError("{}秒以内に Unity から接続がありませんでした。再生ボタンを押したか確認してください。".format(
                self.wait_sec))

        self.env.reset()
        names = list(self.env.behavior_specs.keys())
        for name in names:
            if name.split("?")[0] == self.behavior:
                self.behavior_name = name
                break
        if self.behavior_name is None:
            raise UnityConnectError(
                "behavior「{}」が見つかりません。見つかったもの: {}。LLMTestScene を再生しているか、"
                "Player の Behavior Type が Default かを確認してください。".format(self.behavior, names))
        self.spec = self.env.behavior_specs[self.behavior_name]
        info = {
            "behavior": self.behavior_name,
            "all_behaviors": names,
            "obs_sizes": [int(np.prod(o.shape)) for o in self.spec.observation_specs],
            "continuous": self.spec.action_spec.continuous_size,
            "discrete_branches": list(self.spec.action_spec.discrete_branches),
        }
        self._log(dict({"event": "connected", "fixed_dt": settings.FIXED_DT,
                        "decision_period": settings.DECISION_PERIOD}, **info))
        return info

    # ---- 判断の受け取り ----
    def next_decision(self):
        # type: () -> Decision
        """Player が判断を求めてくるまで進め、観察を返す。途中で来た区切り（ゲーム終了／20秒）も一緒に返す。"""
        episode_ends = []  # type: List[Dict[str, Any]]
        frames = []  # type: List[Dict[str, Any]]
        events = []  # type: List[Dict[str, Any]]
        stats = {}  # type: Dict[str, Any]
        while True:
            # 自作チャンネルと統計チャンネルに届いたものを先に取り出す（区切りの記録に実際のフレーム番号を使うため）
            got = self.state_channel.pop_frames()
            if got:
                frames.extend(got)
                self.unity_frame = int(got[-1]["frame"])
            for ev in self.state_channel.pop_events():
                events.append(ev)
                self._log(dict({"event": "unity_" + str(ev.get("type", "unknown")), "data": ev}, **self.timeline()))
            new_stats = self._pop_stats()
            if new_stats:
                stats.update(new_stats)
                self._log(dict({"event": "stats", "stats": new_stats}, **self.timeline()))

            decision_steps, terminal_steps = self.env.get_steps(self.behavior_name)

            if len(terminal_steps) > 0:
                episode_ends.append(self._handle_episode_end(terminal_steps))

            if len(decision_steps) > 0:
                agent_ids = [int(a) for a in decision_steps.agent_id]
                self._watch_agents(agent_ids)
                parts = [np.asarray(o[0], dtype=np.float64).ravel() for o in decision_steps.obs]
                return Decision(np.concatenate(parts), agent_ids, episode_ends, frames, events, stats)

            # 判断を求めていない（区切りの直後など）。操作は送らずに進める
            self.env.step()

    def _pop_stats(self):
        # type: () -> Dict[str, List[float]]
        """統計チャンネルに届いた統計を取り出し、「項目名 → 値の一覧」の形にして返す（無ければ空）。"""
        raw = self.stats_channel.get_and_reset_stats()
        return {key: [float(v) for v, _ in values] for key, values in raw.items()}

    def check_config(self, scene_event):
        # type: (Dict[str, Any]) -> List[str]
        """Unity から届いた設定値を settings.py と比べ、食い違いの説明を返す（一致すれば空）。"""
        problems = []
        dt = scene_event.get("fixedDeltaTime")
        if dt is not None and abs(float(dt) - settings.FIXED_DT) > 1e-6:
            problems.append("fixedDeltaTime: Unity={} / settings.py={}".format(dt, settings.FIXED_DT))
        dp = scene_event.get("decisionPeriod")
        if dp is not None and int(dp) != settings.DECISION_PERIOD:
            problems.append("DecisionPeriod: Unity={} / settings.py={}".format(dp, settings.DECISION_PERIOD))
        return problems

    def _handle_episode_end(self, terminal_steps):
        # type: (Any) -> Dict[str, Any]
        """区切りの種類を見分けて数え、記録する。ゲーム終了なら「何ゲーム目か」を1つ進める。"""
        interrupted = bool(terminal_steps.interrupted[0])
        kind = KIND_MAX_STEP if interrupted else KIND_GAME_END
        self.counts[kind] += 1
        event = dict({"event": "episode_end", "kind": kind, "interrupted": interrupted,
                      "agent_ids": [int(a) for a in terminal_steps.agent_id]}, **self.timeline())
        if kind == KIND_GAME_END:
            self.game += 1
            self.game_start_frame = self.frame
            event["next_game"] = self.game
        self._log(event)
        return event

    def _watch_agents(self, agent_ids):
        # type: (List[int]) -> None
        """判断を求めてきたエージェントの数と ID を記録する。2体以上なら、そのフレームを覚えておく。"""
        self.seen_agent_ids.update(agent_ids)
        if len(agent_ids) > self.max_agents:
            self.max_agents = len(agent_ids)
        if len(agent_ids) > 1:
            self.duplicate_frames.append(self.frame)
            if len(self.duplicate_frames) == 1:
                self._log(dict({"event": "duplicate_agents", "agent_ids": agent_ids}, **self.timeline()))

    # ---- 操作の送信 ----
    def act(self, horizontal, vertical, n_agents):
        # type: (float, float, int) -> None
        """（横, 縦）の移動を、判断を求めてきた全エージェントに送り、1判断ぶん進める。"""
        size = self.spec.action_spec.continuous_size
        one = np.zeros(size, dtype=np.float32)
        if size >= 2:
            one[0] = horizontal
            one[1] = vertical
        if size >= 3:
            one[2] = -1.0  # ショットしない（ただし既存コードでは常にショットが出る設定）
        continuous = np.tile(one, (n_agents, 1))
        discrete = np.zeros((n_agents, len(self.spec.action_spec.discrete_branches)), dtype=np.int32)
        self.env.set_actions(self.behavior_name, ActionTuple(continuous=continuous, discrete=discrete))
        self.env.step()
        self.decision += 1

    # ---- 後片付け ----
    def close(self):
        # type: () -> None
        """接続を閉じる。閉じるときの失敗は結果に影響しないので無視する。"""
        if self.env is not None:
            try:
                self.env.close()
            except Exception:
                pass
            self.env = None

    def _log(self, record):
        # type: (Dict[str, Any]) -> None
        """記録係があれば、1行記録する。"""
        if self.logger is not None:
            self.logger.log(record)
