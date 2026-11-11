# -*- coding: utf-8 -*-
"""
【このファイルの役割】
設定ファイル LLMBridge/.env を読み込み、APIキーやモデル名を取り出せるようにする。
（python-dotenv というライブラリと同じことを、パッケージを追加せずに自作したもの）

.env の書き方:
- 1行に1つ、KEY=VALUE の形で書く。空行と # で始まる行は無視する
- 値を " か ' で囲んでもよい（取り除いて読む）
- パソコンの環境変数に同じ名前がすでにあれば、そちらを優先する（.env では上書きしない）
- 値（キーなど）は画面に出さない。エラーのときも変数名だけを表示する

【どこから使われるか】
step1_ping.py が、起動直後に .env を読み込むのに使う。
"""
import io
import os
from typing import Dict, Optional

from llm.base import ConfigError


def load_dotenv(path):
    # type: (str) -> Dict[str, str]
    """.env ファイルを読んで、まだ設定されていない環境変数だけを設定する。ファイルが無ければ何もしない。"""
    loaded = {}  # type: Dict[str, str]
    if not os.path.isfile(path):
        return loaded
    with io.open(path, "r", encoding="utf-8-sig") as f:
        for raw in f:
            line = raw.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            key = key.strip()
            if key.startswith("export "):
                key = key[len("export "):].strip()
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
                value = value[1:-1]
            if not key:
                continue
            loaded[key] = value
            if key not in os.environ:
                os.environ[key] = value
    return loaded


def get_env(name, default=None):
    # type: (str, Optional[str]) -> Optional[str]
    """設定値を1つ取り出す。未設定または空なら default を返す（省略できる設定に使う）。"""
    value = os.environ.get(name)
    if value is None or value.strip() == "":
        return default
    return value.strip()


def require_env(name):
    # type: (str) -> str
    """必須の設定値を1つ取り出す。無ければ、変数名だけを示した ConfigError で止める（キーなど必須の設定に使う）。"""
    value = get_env(name)
    if value is None:
        raise ConfigError("{} が未設定です。".format(name))
    return value
