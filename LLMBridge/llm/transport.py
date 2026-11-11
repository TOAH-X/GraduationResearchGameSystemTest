# -*- coding: utf-8 -*-
"""
【このファイルの役割】
OpenAI へ HTTP リクエストを「実際に送る部分」だけを切り出したファイル。2種類ある。
- RequestsTransport : 本物。requests ライブラリでインターネット越しに送る
- FakeTransport     : 偽物。通信はせず、あらかじめ決めた応答（429、タイムアウトなど）を順番に返す

【なぜ分けたか（当初の計画には無く、段階1の課題3への対策で追加したファイル）】
リトライやエラー分類の処理は openai_client.py にある。
送信部分だけを偽物に差し替えれば、その本番のコードをそのまま通したまま、
実APIを1回も使わずに「429のあと待って再試行する」「残高不足ならすぐ止まる」などを確かめられる。
（モック版の mock_client.py を使うと、openai_client.py の処理自体が通らないので確認にならない）

【どこから使われるか】
- openai_client.py : 送信に使う（指定が無ければ本物の RequestsTransport）
- step1_ping.py    : --simulate のときに make_scenario() で偽物を作って差し込む
"""
import json
from typing import Any, Dict, List, Optional, Tuple, Union


class TransportTimeout(Exception):
    """送信がタイムアウトしたことを表す例外。"""


class TransportConnectionError(Exception):
    """サーバーに接続できなかったことを表す例外。"""


class HttpResult(object):
    """HTTP の応答（ステータス、本文、ヘッダー）を入れておく入れ物。"""

    def __init__(self, status, body, headers=None):
        # type: (int, str, Optional[Dict[str, str]]) -> None
        """応答の中身を保存する。ヘッダー名は大文字・小文字の違いで探し損ねないよう小文字にそろえる。"""
        self.status = status
        self.body = body
        self.headers = dict((k.lower(), v) for k, v in (headers or {}).items())


class Transport(object):
    """送信部分の共通の形（本物・偽物で共通。C# の interface にあたる）。"""

    def post(self, url, headers, json_body, timeout):
        # type: (str, Dict[str, str], Dict[str, Any], Tuple[float, float]) -> HttpResult
        """URL に JSON を POST して応答を返す。中身は継承したクラスが書く（ここでは未実装）。"""
        raise NotImplementedError


class RequestsTransport(Transport):
    """本物の送信。requests ライブラリで実際に OpenAI へ送る。"""

    def __init__(self):
        # type: () -> None
        """requests を読み込み、接続を使い回すためのセッションを用意する。"""
        import requests  # 既存 venv に導入済み（2.32.4）

        self._requests = requests
        self._session = requests.Session()

    def post(self, url, headers, json_body, timeout):
        # type: (str, Dict[str, str], Dict[str, Any], Tuple[float, float]) -> HttpResult
        """実際に送信する。タイムアウトと接続失敗は、このファイルの例外に置き換えて呼び出し元に伝える。"""
        req = self._requests
        try:
            resp = self._session.post(url, headers=headers, json=json_body, timeout=timeout)
        except req.exceptions.Timeout as e:
            # 例外名だけを渡す（キーを含むヘッダーは渡さない）
            raise TransportTimeout(type(e).__name__)
        except req.exceptions.ConnectionError as e:
            raise TransportConnectionError(type(e).__name__)
        resp.encoding = "utf-8"
        return HttpResult(resp.status_code, resp.text, dict(resp.headers))


# ---------------------------------------------------------------------------
# 偽物の送信（テスト用）
# ---------------------------------------------------------------------------
FakeItem = Union[HttpResult, Exception]  # 偽物が返すもの：HTTP 応答か、例外（タイムアウトなど）


class FakeTransport(Transport):
    """偽物の送信。通信はせず、用意した応答（または例外）を1つずつ順番に返す。"""

    def __init__(self, items):
        # type: (List[FakeItem]) -> None
        """返す応答のリストを受け取って保存する。呼ばれた回数も数えられるようにしておく。"""
        self._items = list(items)
        self.calls = 0
        self.last_request = None  # type: Optional[Dict[str, Any]]

    def post(self, url, headers, json_body, timeout):
        # type: (str, Dict[str, str], Dict[str, Any], Tuple[float, float]) -> HttpResult
        """送信のふりをして、用意した応答を先頭から1つ返す。例外が入っていればそれを投げる。"""
        self.calls += 1
        self.last_request = {"url": url, "json": json_body}  # ヘッダー（キー）は保持しない
        if not self._items:
            raise AssertionError("FakeTransport: 用意した応答を使い切りました")
        item = self._items.pop(0)
        if isinstance(item, Exception):
            raise item
        return item


def _ok_body(model, text="pong", finish_reason="stop"):
    # type: (str, str, str) -> str
    """OpenAI の「成功したとき」の応答本文（JSON）を、本物と同じ形で作る。"""
    return json.dumps(
        {
            "id": "chatcmpl-simulated",
            "object": "chat.completion",
            "model": model,
            "choices": [
                {
                    "index": 0,
                    "message": {"role": "assistant", "content": text},
                    "finish_reason": finish_reason,
                }
            ],
            "usage": {"prompt_tokens": 12, "completion_tokens": 2 if text else 16, "total_tokens": 14},
        }
    )


def _err_body(message, err_type, code):
    # type: (str, str, Optional[str]) -> str
    """OpenAI の「エラーのとき」の応答本文（JSON）を、本物と同じ形で作る。"""
    return json.dumps({"error": {"message": message, "type": err_type, "param": None, "code": code}})


# --simulate で選べるシナリオの名前の一覧
SCENARIOS = (
    "ok",            # 正常に応答する
    "rate_limit",    # 1回目は 429（1秒待てと言われる）、2回目で成功
    "quota",         # 残高不足（再試行しないことを確かめる）
    "timeout",       # 1回目はタイムアウト、2回目で成功
    "server_error",  # 1回目は 500、2回目で成功
    "auth",          # 401。メッセージ内のキーがマスクされることを確かめる
    "bad_request",   # 400（パラメータ名の間違い）。再試行しないことを確かめる
    "empty",         # 出力上限で打ち切られて空の応答
)


def make_scenario(name, model):
    # type: (str, str) -> FakeTransport
    """シナリオ名（上の一覧）から、その状況を再現する偽物の送信を作る。"""
    ok = HttpResult(200, _ok_body(model))
    if name == "ok":
        items = [ok]  # type: List[FakeItem]
    elif name == "rate_limit":
        # 1回目は 429（Retry-After: 1秒）、2回目で成功
        items = [
            HttpResult(
                429,
                _err_body("Rate limit reached (simulated).", "requests", "rate_limit_exceeded"),
                {"Retry-After": "1"},
            ),
            ok,
        ]
    elif name == "quota":
        # 残高不足：再試行せずに止まるはず（2つ目は使われないことを確かめる）
        items = [
            HttpResult(
                429,
                _err_body(
                    "You exceeded your current quota (simulated).", "insufficient_quota", "insufficient_quota"
                ),
            ),
            ok,
        ]
    elif name == "timeout":
        items = [TransportTimeout("ReadTimeout (simulated)"), ok]
    elif name == "server_error":
        items = [HttpResult(500, _err_body("Internal server error (simulated).", "server_error", None)), ok]
    elif name == "auth":
        # OpenAI の 401 はメッセージにキーの一部を含む（ここの値は偽物）。マスクされることを確かめる
        items = [
            HttpResult(
                401,
                _err_body(
                    "Incorrect API key provided: sk-proj-AbCdEf123456XyZ. You can find your API key at ...",
                    "invalid_request_error",
                    "invalid_api_key",
                ),
            )
        ]
    elif name == "bad_request":
        items = [
            HttpResult(
                400,
                _err_body(
                    "Unsupported parameter: 'max_tokens' (simulated).", "invalid_request_error", "unsupported_parameter"
                ),
            )
        ]
    elif name == "empty":
        # 出力上限で打ち切られて空の応答
        items = [HttpResult(200, _ok_body(model, text="", finish_reason="length"))]
    else:
        raise ValueError("unknown scenario: {}".format(name))
    return FakeTransport(items)
