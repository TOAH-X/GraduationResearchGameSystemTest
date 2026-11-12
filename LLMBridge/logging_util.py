# -*- coding: utf-8 -*-
"""
【このファイルの役割】
LLM を呼んだ記録（や Unity との通信の記録）をファイルに残すことと、APIキーを伏せ字にすること。
- JsonlLogger    : 1回の送信につき1行、logs/llm_YYYYMMDD.jsonl に追記する（JSONL＝1行に1つのJSON）。
                   段階2a からは、Unity との通信の記録を logs/unity_YYYYMMDD.jsonl に書くのにも使う
- mask_secrets() : 文字列の中のキー（sk- で始まるもの、登録したキーの値）を伏せ字にする

ログは必ず mask_secrets() を通してから書き込む。画面に出すエラーメッセージも同様。

【どこから使われるか】
openai_client.py と mock_client.py がログを書くのに使う。
step1_ping.py が、画面に表示する前の伏せ字処理に使う。
"""
import datetime
import io
import json
import os
import re
from typing import Any, Dict, List, Optional

# sk- で始まる文字列（sk-proj-... を含む）。OpenAI の 401 メッセージは "sk-proj-abc***xyz" のように
# キーの一部を含むため、記号の * や . も含めて丸ごと置き換える。
_SK_PATTERN = re.compile(r"sk-[A-Za-z0-9_\-\*\.]+")

# 実行中に登録された秘密の値（.env から読んだキーそのもの）
_registered_secrets = []  # type: List[str]


def register_secret(value):
    # type: (Optional[str]) -> None
    """表示・保存から必ず消したい値（APIキーなど）を登録する。以後、mask_secrets() で伏せ字になる。"""
    if value and len(value) >= 8 and value not in _registered_secrets:
        _registered_secrets.append(value)


def mask_secrets(text):
    # type: (Any) -> str
    """文字列の中のキーを伏せ字にして返す（登録した値は ***、sk- で始まる文字列は sk-*** に置き換える）。"""
    s = text if isinstance(text, str) else str(text)
    for secret in _registered_secrets:
        s = s.replace(secret, "***")
    return _SK_PATTERN.sub("sk-***", s)


class JsonlLogger(object):
    """LLM 呼び出しの記録を、日付ごとのファイルに1行ずつ追記していく係。"""

    def __init__(self, log_dir, stage, prefix="llm"):
        # type: (str, str, str) -> None
        """保存先のフォルダと段階名（例：step1）を受け取り、今日の日付のファイル名を決める。

        prefix はファイル名の頭（既定 llm → llm_YYYYMMDD.jsonl。Unity との通信の記録は unity を使う）。
        """
        self.log_dir = log_dir
        self.stage = stage
        today = datetime.datetime.now().strftime("%Y%m%d")
        self.path = os.path.join(log_dir, "{}_{}.jsonl".format(prefix, today))

    def log(self, record):
        # type: (Dict[str, Any]) -> None
        """記録に時刻と段階名を付け、キーを伏せ字にしてから、ファイルに1行追記する。"""
        if not os.path.isdir(self.log_dir):
            os.makedirs(self.log_dir)
        full = {
            "ts": datetime.datetime.now().astimezone().isoformat(timespec="milliseconds"),
            "stage": self.stage,
        }
        full.update(record)
        line = mask_secrets(json.dumps(full, ensure_ascii=False))
        with io.open(self.path, "a", encoding="utf-8") as f:
            f.write(line + "\n")
