# -*- coding: utf-8 -*-
"""
【このファイルの役割】
LLM クライアント全体で使う「共通の約束ごと」をまとめたファイル。処理の本体は無い。
1. エラーの種類（キー未設定、認証失敗、残高不足、レート制限 など）
2. LLM の応答を入れる入れ物（LLMResponse）
3. クライアントの呼び出し方の決まり（LLMClient）

【どこから使われるか】
openai_client.py（実API版）と mock_client.py（モック版）が、ここの決まりに従って作られている。
step1_ping.py は、ここのエラーの種類を見て、表示する内容と終了コードを決める。

※ Python 3.8 で動く書き方にしている（list[str] や X | Y の型注釈は使わない）。
"""
from typing import Any, Dict, List, Optional


# ---------------------------------------------------------------------------
# エラーの種類
#   C# の「Exception を継承した独自の例外クラス」と同じ考え方。
#   それぞれのクラスに「再試行してよいか（retryable）」と「利用者向けの対処（hint）」を持たせ、
#   呼び出し側がエラーの種類ごとに動きを変えられるようにしている。
# ---------------------------------------------------------------------------
class LLMError(Exception):
    """LLM 呼び出しに関するエラーの親クラス。

    kind      : エラーの種類（ログ・表示用の短い名前）
    retryable : 再試行してよいか
    status    : HTTP ステータス（無ければ None）
    hint      : 利用者向けの「次にすること」
    """

    kind = "llm_error"
    retryable = False
    hint = ""

    def __init__(self, message, status=None, retry_after=None):
        # type: (str, Optional[int], Optional[float]) -> None
        """エラーの内容（メッセージ、HTTPステータス、待つべき秒数）を受け取って保存する。"""
        super().__init__(message)
        self.message = message
        self.status = status
        self.retry_after = retry_after


class ConfigError(LLMError):
    """設定の不足（.env にキーやモデル名が無い など）。"""

    kind = "config"
    hint = "LLMBridge/.env.example をコピーして LLMBridge/.env を作り、値を設定してください。"


class BudgetExceededError(LLMError):
    """呼び出し回数の上限（--max-calls）に達した。"""

    kind = "budget_exceeded"
    hint = "呼び出し回数の上限に達しました。必要なら --max-calls を増やしてください。"


class AuthError(LLMError):
    """認証の失敗（キーが間違っている、無効化されている など。HTTP 401/403）。"""

    kind = "auth"
    hint = "APIキーが正しいか、無効化（Revoked）されていないかを確認してください。"


class QuotaError(LLMError):
    """残高・利用上限の不足（HTTP 429 の insufficient_quota）。待っても直らないので再試行しない。"""

    kind = "insufficient_quota"
    hint = "残高・利用上限が不足しています。platform.openai.com の Billing / Limits を確認してください（再試行しません）。"


class RateLimitError(LLMError):
    """短時間に呼びすぎた（HTTP 429）。少し待てば通るので再試行する。"""

    kind = "rate_limit"
    retryable = True
    hint = "レート制限です。時間をおいて再実行してください。"


class LLMTimeoutError(LLMError):
    """応答が時間内に返ってこなかった。再試行する。"""

    kind = "timeout"
    retryable = True
    hint = "応答がタイムアウトしました。ネットワークを確認するか --timeout を延ばしてください。"


class ConnectionFailedError(LLMError):
    """サーバーに接続できなかった（ネットワークの問題）。再試行する。"""

    kind = "connection"
    retryable = True
    hint = "接続できませんでした。ネットワーク（プロキシ等）を確認してください。"


class ServerError(LLMError):
    """OpenAI 側のエラー（HTTP 5xx など）。一時的なことが多いので再試行する。"""

    kind = "server_error"
    retryable = True
    hint = "API 側のエラーです。時間をおいて再実行してください。"


class BadRequestError(LLMError):
    """送った内容が不正（モデル名やパラメータ名の間違い など。HTTP 400/404）。直さない限り通らないので再試行しない。"""

    kind = "bad_request"
    hint = "リクエストの内容（モデル名・パラメータ名など）を確認してください。"


class InvalidResponseError(LLMError):
    """応答は返ってきたが、想定した形になっていなかった。"""

    kind = "invalid_response"
    hint = "API の応答を解釈できませんでした。logs/ の記録を確認してください。"


# ---------------------------------------------------------------------------
# 応答の入れ物
# ---------------------------------------------------------------------------
class LLMResponse(object):
    """LLM を1回呼んだ結果をまとめて入れておく入れ物。"""

    def __init__(
        self,
        text,  # type: str
        model,  # type: str
        finish_reason=None,  # type: Optional[str]
        input_tokens=None,  # type: Optional[int]
        output_tokens=None,  # type: Optional[int]
        latency_s=0.0,  # type: float
        attempts=1,  # type: int
    ):
        # type: (...) -> None
        """応答文、モデル名、終わった理由、トークン数、所要時間、試行回数を保存する。"""
        self.text = text
        self.model = model
        self.finish_reason = finish_reason
        self.input_tokens = input_tokens
        self.output_tokens = output_tokens
        self.latency_s = latency_s
        self.attempts = attempts

    @property
    def truncated(self):
        # type: () -> bool
        """出力上限で途中で打ち切られたかどうかを返す（空の応答の原因を知らせるため）。"""
        return self.finish_reason == "length"


# ---------------------------------------------------------------------------
# クライアントの呼び出し方の決まり
#   C# の interface（または abstract class）にあたる。
#   実API版とモック版がこれを継承するので、呼び出す側はどちらでも同じ書き方で使える。
# ---------------------------------------------------------------------------
class LLMClient(object):
    """LLM クライアントの共通の形（実API版・モック版で共通）。"""

    model = "unknown"

    def complete(self, messages, max_tokens=None, json_mode=False):
        # type: (List[Dict[str, Any]], Optional[int], bool) -> LLMResponse
        """メッセージを LLM に送って応答を受け取る。中身は継承したクラスが書く（ここでは未実装）。"""
        raise NotImplementedError
