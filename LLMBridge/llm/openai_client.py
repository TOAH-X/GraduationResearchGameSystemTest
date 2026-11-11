# -*- coding: utf-8 -*-
"""
【このファイルの役割】
OpenAI の API（Chat Completions）を呼び出す本体。段階1の中心となるファイル。
1回の呼び出しで、次の流れを受け持つ。
  1. 送る内容（モデル名、メッセージ、推論の強さ、出力上限）を組み立てる
  2. 回数の上限（budget.py）を1回分消費してから送信する（送信そのものは transport.py に任せる）
  3. 応答を読み取る。エラーなら種類を判定する（base.py のエラーの種類）
  4. 再試行してよいエラーなら、決まった秒数だけ待ってもう一度送る
  5. 1回の送信ごとにログを残す（logging_util.py。キーは伏せ字にし、ヘッダーは保存しない）

【どこから使われるか】
step1_ping.py が --real（本物の送信）と --simulate（偽物の送信）のときに使う。
段階2以降の意図選択でも、このクラスをそのまま使う予定。
"""
import json
import sys
import time
from typing import Any, Callable, Dict, List, Optional

from logging_util import JsonlLogger, mask_secrets, register_secret

from .base import (
    AuthError,
    BadRequestError,
    ConnectionFailedError,
    InvalidResponseError,
    LLMClient,
    LLMError,
    LLMResponse,
    LLMTimeoutError,
    QuotaError,
    RateLimitError,
    ServerError,
)
from .budget import CallBudget
from .transport import HttpResult, RequestsTransport, Transport, TransportConnectionError, TransportTimeout

DEFAULT_BASE_URL = "https://api.openai.com/v1"  # .env の OPENAI_BASE_URL が空のときの接続先


class OpenAIClient(LLMClient):
    """OpenAI API のクライアント（base.py の LLMClient の実API版）。"""

    def __init__(
        self,
        api_key,  # type: str
        model,  # type: str
        budget,  # type: CallBudget
        logger=None,  # type: Optional[JsonlLogger]
        base_url=None,  # type: Optional[str]
        transport=None,  # type: Optional[Transport]
        connect_timeout=10.0,  # type: float
        read_timeout=60.0,  # type: float
        max_retries=3,  # type: int
        reasoning_effort="none",  # type: Optional[str]
        token_param="max_completion_tokens",  # type: str
        backoff_base=2.0,  # type: float
        backoff_max=30.0,  # type: float
        sleep=time.sleep,  # type: Callable[[float], None]
        mode="real",  # type: str
    ):
        # type: (...) -> None
        """接続に必要な設定（キー、モデル、上限、ログ、送信方法、タイムアウト、再試行のルール）を受け取って保存する。

        キーは register_secret() で登録し、以後どこに表示・保存されても伏せ字になるようにする。
        """
        register_secret(api_key)
        self._api_key = api_key
        self.model = model
        self.budget = budget
        self.logger = logger
        self.base_url = (base_url or DEFAULT_BASE_URL).rstrip("/")
        self.transport = transport if transport is not None else RequestsTransport()
        self.timeout = (connect_timeout, read_timeout)
        self.max_retries = max_retries
        self.reasoning_effort = reasoning_effort
        self.token_param = token_param
        self.backoff_base = backoff_base
        self.backoff_max = backoff_max
        self._sleep = sleep
        self.mode = mode

    # ------------------------------------------------------------------
    def _build_body(self, messages, max_tokens, json_mode):
        # type: (List[Dict[str, Any]], Optional[int], bool) -> Dict[str, Any]
        """OpenAI に送る JSON の中身（モデル名、メッセージ、出力上限、推論の強さ、JSONモード）を組み立てる。"""
        body = {"model": self.model, "messages": messages}  # type: Dict[str, Any]
        if max_tokens is not None:
            body[self.token_param] = max_tokens
        if self.reasoning_effort:
            body["reasoning_effort"] = self.reasoning_effort
        if json_mode:
            body["response_format"] = {"type": "json_object"}
        return body

    def _backoff(self, attempt, err):
        # type: (int, LLMError) -> float
        """再試行の前に何秒待つかを決める。

        API が待つ秒数を指定してきたらそれに従い、無ければ 2→4→8秒…と倍にしていく（最大30秒）。
        """
        if err.retry_after is not None:
            return min(max(err.retry_after, 0.0), self.backoff_max)
        return min(self.backoff_base * (2 ** (attempt - 1)), self.backoff_max)

    def complete(self, messages, max_tokens=None, json_mode=False):
        # type: (List[Dict[str, Any]], Optional[int], bool) -> LLMResponse
        """LLM にメッセージを送って応答を返す（このクラスの入口）。

        失敗したら、再試行してよいものだけ待ってから送り直す。再試行できない失敗は、エラーとして呼び出し元に返す。
        """
        url = self.base_url + "/chat/completions"
        headers = {
            "Authorization": "Bearer " + self._api_key,
            "Content-Type": "application/json",
        }
        body = self._build_body(messages, max_tokens, json_mode)
        params = dict((k, v) for k, v in body.items() if k not in ("messages",))  # ログ用（メッセージ以外の設定）

        attempt = 0
        total_start = time.time()
        while True:
            attempt += 1
            # 上限チェック（送る前に消費）。上限なら BudgetExceededError がそのまま呼び出し元へ上がる
            self.budget.consume()

            # --- 送信 ---
            start = time.time()
            err = None  # type: Optional[LLMError]
            result = None  # type: Optional[HttpResult]
            response = None  # type: Optional[LLMResponse]
            try:
                result = self.transport.post(url, headers, body, self.timeout)
            except TransportTimeout as e:
                err = LLMTimeoutError("タイムアウトしました（{}）".format(e))
            except TransportConnectionError as e:
                err = ConnectionFailedError("接続に失敗しました（{}）".format(e))
            latency = time.time() - start

            # --- 応答の読み取り、またはエラーの種類の判定 ---
            if result is not None:
                if result.status == 200:
                    try:
                        response = self._parse_ok(result, latency, attempt)
                    except LLMError as e:
                        err = e
                else:
                    err = self._classify_error(result)

            self._log(attempt, messages, params, result, response, err, latency)

            # 成功したら返して終わり
            if response is not None:
                response.latency_s = time.time() - total_start
                return response

            # --- 失敗したとき：再試行するかどうか ---
            assert err is not None
            if not err.retryable or attempt > self.max_retries:
                raise err
            if self.budget.remaining <= 0:
                # 上限に達していて待っても送れないので、待たずに上限エラーにする
                self.budget.consume()
            wait = self._backoff(attempt, err)
            sys.stderr.write(
                "[retry] {}（status={}）。{:.1f}秒待って再試行します（{}回目/最大{}回）\n".format(
                    err.kind, err.status, wait, attempt, self.max_retries
                )
            )
            sys.stderr.flush()
            self._sleep(wait)

    # ------------------------------------------------------------------
    def _parse_ok(self, result, latency, attempt):
        # type: (HttpResult, float, int) -> LLMResponse
        """成功（HTTP 200）の応答本文から、応答文・終わった理由・トークン数を取り出して LLMResponse にする。"""
        try:
            data = json.loads(result.body)
            choice = data["choices"][0]
            text = (choice.get("message") or {}).get("content") or ""
            usage = data.get("usage") or {}
            return LLMResponse(
                text=text,
                model=data.get("model", self.model),
                finish_reason=choice.get("finish_reason"),
                input_tokens=usage.get("prompt_tokens", usage.get("input_tokens")),
                output_tokens=usage.get("completion_tokens", usage.get("output_tokens")),
                latency_s=latency,
                attempts=attempt,
            )
        except (ValueError, KeyError, IndexError, TypeError, AttributeError) as e:
            raise InvalidResponseError("応答の形式が想定と異なります（{}）".format(type(e).__name__), status=200)

    def _classify_error(self, result):
        # type: (HttpResult) -> LLMError
        """エラー応答の HTTP ステータスと中身から、エラーの種類（再試行するか、しないか）を判定する。"""
        status = result.status
        message = ""
        err_type = ""
        code = ""
        try:
            err_obj = json.loads(result.body).get("error") or {}
            message = err_obj.get("message") or ""
            err_type = err_obj.get("type") or ""
            code = err_obj.get("code") or ""
        except (ValueError, AttributeError):
            message = result.body[:300]
        message = mask_secrets(message)  # 401 のメッセージにはキーの一部が入るので伏せ字にする
        detail = "HTTP {} {}: {}".format(status, code or err_type, message).strip()

        if status == 429:
            # 同じ 429 でも、残高不足（待っても直らない）とレート制限（待てば通る）を見分ける
            if code == "insufficient_quota" or err_type == "insufficient_quota":
                return QuotaError(detail, status=status)
            return RateLimitError(detail, status=status, retry_after=_parse_retry_after(result.headers))
        if status in (401, 403):
            return AuthError(detail, status=status)
        if status in (400, 404, 422):
            return BadRequestError(detail, status=status)
        if status in (408, 409) or status >= 500:
            return ServerError(detail, status=status, retry_after=_parse_retry_after(result.headers))
        return LLMError(detail, status=status)

    def _log(self, attempt, messages, params, result, response, err, latency):
        # type: (int, List[Dict[str, Any]], Dict[str, Any], Optional[HttpResult], Optional[LLMResponse], Optional[LLMError], float) -> None
        """1回の送信の記録（送った内容、応答またはエラー、所要時間など）をログファイルに1行書く。"""
        if self.logger is None:
            return
        record = {
            "mode": self.mode,
            "model": self.model,
            "attempt": attempt,
            "calls_used": self.budget.summary(),
            "request": {"messages": messages, "params": params},
            "status": result.status if result is not None else None,
            "latency_s": round(latency, 3),
        }  # type: Dict[str, Any]
        if response is not None:
            record["response"] = {
                "text": response.text,
                "model": response.model,
                "finish_reason": response.finish_reason,
                "input_tokens": response.input_tokens,
                "output_tokens": response.output_tokens,
            }
        if err is not None:
            record["error"] = {"kind": err.kind, "message": err.message, "retryable": err.retryable}
        self.logger.log(record)


def _parse_retry_after(headers):
    # type: (Dict[str, str]) -> Optional[float]
    """応答ヘッダーから「何秒待ってから再試行してほしいか」を読み取る。指定が無ければ None を返す。"""
    value = headers.get("retry-after-ms")
    if value:
        try:
            return float(value) / 1000.0
        except ValueError:
            pass
    value = headers.get("retry-after")
    if value:
        try:
            return float(value)
        except ValueError:
            return None  # 日付形式の指定は扱わず、通常の待ち時間にする
    return None
