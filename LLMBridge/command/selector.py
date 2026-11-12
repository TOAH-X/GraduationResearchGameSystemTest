# -*- coding: utf-8 -*-
"""
【このファイルの役割】
間接操作を「選ぶ係」の差し込み口。
- CommandSelector : 選ぶ係の共通の形（select を持つ）。段階5では、これと同じ形の LLM 版を作って差し替える
- RandomSelector  : 段階2b で使うランダム版。--seed を決めれば、毎回同じ順番で選ぶ（再現できる）

【どこから使われるか】
step2b_unity_loop.py。
"""
import random
from typing import Any, List, Optional


class CommandSelector(object):
    """選ぶ係の共通の形。中身はそれぞれの版（ランダム版、LLM 版など）が決める。"""

    name = "base"

    def select(self, context=None):
        # type: (Optional[Any]) -> str
        """次の間接操作の名前を1つ返す。context には、状況などの判断材料を渡す（段階2b では使わない）。"""
        raise NotImplementedError


class RandomSelector(CommandSelector):
    """候補の中からランダムに選ぶ版。切り替わったことが画面で分かるよう、直前と同じものは選ばない。"""

    name = "random"

    def __init__(self, names, seed=None):
        # type: (List[str], Optional[int]) -> None
        """候補の名前の一覧と、乱数の種（seed。省略すると毎回ちがう選び方になる）を受け取る。"""
        if len(names) < 2:
            raise ValueError("候補は2つ以上必要です。")
        self.names = list(names)
        self.seed = seed
        self._rng = random.Random(seed)
        self._last = None  # type: Optional[str]

    def select(self, context=None):
        # type: (Optional[Any]) -> str
        """直前と違う候補の中から1つをランダムに選んで返す。"""
        candidates = [n for n in self.names if n != self._last]
        choice = self._rng.choice(candidates)
        self._last = choice
        return choice
