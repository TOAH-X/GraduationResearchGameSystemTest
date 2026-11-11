# -*- coding: utf-8 -*-
"""
【このファイルの役割】
1回の実行で LLM API を呼べる回数に上限（--max-calls）をかけ、費用の使いすぎを防ぐ。

- HTTP リクエストを「送る前」に1回分を消費する
- リトライ（再試行）も1回として数える
- 上限に達していたら、送らずに BudgetExceededError を出して止める

【どこから使われるか】
openai_client.py と mock_client.py が、送信の直前に consume() を呼ぶ。
step1_ping.py が、結果表示で「何回使ったか（calls_used）」を表示するのに使う。
"""
from .base import BudgetExceededError


class CallBudget(object):
    """呼び出し回数の残りを数えるカウンター。"""

    def __init__(self, max_calls):
        # type: (int) -> None
        """上限回数を受け取り、使った回数を0から数え始める。"""
        if max_calls < 0:
            raise ValueError("max_calls must be >= 0")
        self.max_calls = max_calls
        self.used = 0

    @property
    def remaining(self):
        # type: () -> int
        """あと何回呼べるかを返す（再試行の前に、待つ意味があるかを判断するため）。"""
        return self.max_calls - self.used

    def consume(self):
        # type: () -> None
        """1回分を消費する。上限に達していれば、送らせずに BudgetExceededError を出す。"""
        if self.used >= self.max_calls:
            raise BudgetExceededError(
                "呼び出し回数の上限（--max-calls={}）に達したため、これ以上送信しません。".format(self.max_calls)
            )
        self.used += 1

    def summary(self):
        # type: () -> str
        """「使った回数/上限」の文字列を返す（表示とログ用。例：1/3）。"""
        return "{}/{}".format(self.used, self.max_calls)
