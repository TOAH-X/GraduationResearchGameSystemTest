# -*- coding: utf-8 -*-
"""
【このファイルの役割】
本物の API の代わりに、決まった応答（既定は "pong"）を返す「モック（ダミー）」のクライアント。
API キーも費用も要らないので、全体の流れを気軽に試すのに使う。

- 呼び出し方は本物（openai_client.py）と同じ（base.py の LLMClient の決まりに従っている）
- 回数の上限とログも本物と同じように扱う
- 段階1では最小限。段階2・3で「意図をJSONで返す」「わざと不正な応答を返す」「60秒待つ」などに広げる予定

【どこから使われるか】
step1_ping.py をオプションなしで実行したとき（mode=mock）に使う。
"""
import time
from typing import Any, Dict, List, Optional

from logging_util import JsonlLogger

from .base import LLMClient, LLMResponse
from .budget import CallBudget


class MockClient(LLMClient):
    """API を使わずに決まった応答を返すクライアント。"""

    def __init__(self, budget, logger=None, replies=None, delay_s=0.0):
        # type: (CallBudget, Optional[JsonlLogger], Optional[List[str]], float) -> None
        """返す応答のリスト（順番に使い回す）と、応答までにわざと待つ秒数を受け取って保存する。"""
        self.model = "mock"
        self.budget = budget
        self.logger = logger
        self._replies = list(replies) if replies else ["pong"]
        self._index = 0
        self.delay_s = delay_s

    def complete(self, messages, max_tokens=None, json_mode=False):
        # type: (List[Dict[str, Any]], Optional[int], bool) -> LLMResponse
        """本物と同じ形で呼ばれ、用意した応答を1つ返す。回数の消費とログの記録も本物と同じように行う。"""
        self.budget.consume()  # モックでも回数は数える（本番と同じ動きにするため）
        start = time.time()
        if self.delay_s > 0:
            time.sleep(self.delay_s)
        text = self._replies[self._index % len(self._replies)]
        self._index += 1
        latency = time.time() - start
        response = LLMResponse(
            text=text,
            model=self.model,
            finish_reason="stop",
            input_tokens=0,
            output_tokens=0,
            latency_s=latency,
            attempts=1,
        )
        if self.logger is not None:
            self.logger.log(
                {
                    "mode": "mock",
                    "model": self.model,
                    "attempt": 1,
                    "calls_used": self.budget.summary(),
                    "request": {"messages": messages, "params": {"max_tokens": max_tokens, "json_mode": json_mode}},
                    "status": None,
                    "latency_s": round(latency, 3),
                    "response": {"text": text, "model": self.model, "finish_reason": "stop"},
                }
            )
        return response
