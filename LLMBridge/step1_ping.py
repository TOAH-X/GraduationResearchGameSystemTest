# -*- coding: utf-8 -*-
"""
【このファイルの役割】
段階1（APIの疎通確認）で実際に実行するスクリプト。LLM に短い文を1回送り、応答を画面に表示する。
llm フォルダの部品（クライアント、回数上限、送信部分）と、envutil.py（設定の読み込み）、
logging_util.py（ログと伏せ字）を組み合わせて動かす「入口」にあたる。

3つの動かし方がある:
- オプションなし : モック（mock_client.py）。APIもキーも使わない
- --simulate 名前 : 本物のクライアント（openai_client.py）を使い、送信だけ偽物（transport.py）にする。
                    エラー処理の確認用。APIは使わない
- --real          : 本物のクライアントで、実際に OpenAI を呼ぶ（費用が発生する）

使い方（LLMBridge フォルダで実行）:
  モック（APIを使わない）   : python step1_ping.py
  疑似エラー（APIを使わない）: python step1_ping.py --simulate rate_limit --max-calls 3
  実API（1回だけ）          : python step1_ping.py --real

終了コード: 0=成功 / 2=設定エラー / 3=回数上限 / 4=APIエラー
"""
from __future__ import print_function

import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

# Windows のコンソール（cp932）でも日本語が化けないようにする
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8")  # Python 3.7+
    except (AttributeError, ValueError):
        pass

from envutil import get_env, load_dotenv, require_env  # noqa: E402
from llm.base import BudgetExceededError, ConfigError, LLMError  # noqa: E402
from llm.budget import CallBudget  # noqa: E402
from llm.mock_client import MockClient  # noqa: E402
from llm.openai_client import OpenAIClient  # noqa: E402
from llm.transport import SCENARIOS, make_scenario  # noqa: E402
from logging_util import JsonlLogger, mask_secrets  # noqa: E402

DEFAULT_MODEL = "gpt-6-luna"
TAG = "[step1]"


def parse_args(argv=None):
    """コマンドラインのオプション（--real、--simulate、--max-calls など）を読み取る。"""
    p = argparse.ArgumentParser(description="段階1：LLM API の疎通確認")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--real", action="store_true", help="実APIを呼ぶ（費用が発生する）")
    g.add_argument("--simulate", choices=SCENARIOS, help="偽の通信で、本番と同じクライアントのエラー処理を確かめる")
    p.add_argument("--max-calls", type=int, default=1, help="1回の実行あたりのHTTPリクエスト上限（リトライ含む）。既定1")
    p.add_argument("--prompt", default="Reply with exactly one word: pong", help="送る文")
    p.add_argument("--max-tokens", type=int, default=16, help="出力の上限トークン数。既定16")
    p.add_argument("--timeout", type=float, default=60.0, help="応答待ちのタイムアウト（秒）。既定60")
    p.add_argument("--env-file", default=os.path.join(HERE, ".env"), help="読み込む .env のパス")
    return p.parse_args(argv)


def main(argv=None):
    """全体の流れ：設定を読む → 動かし方に合ったクライアントを用意する → 1回呼ぶ → 結果かエラーを表示する。

    戻り値は終了コード（0=成功 / 2=設定エラー / 3=回数上限 / 4=APIエラー）。
    """
    args = parse_args(argv)
    load_dotenv(args.env_file)

    budget = CallBudget(args.max_calls)
    logger = JsonlLogger(os.path.join(HERE, "logs"), stage="step1")
    messages = [{"role": "user", "content": args.prompt}]

    try:
        if args.real:
            mode = "real"
            api_key = require_env("OPENAI_API_KEY")
            model = require_env("OPENAI_MODEL")
            client = OpenAIClient(
                api_key=api_key,
                model=model,
                budget=budget,
                logger=logger,
                base_url=get_env("OPENAI_BASE_URL"),
                read_timeout=args.timeout,
                mode=mode,
            )
        elif args.simulate:
            mode = "simulate:" + args.simulate
            model = get_env("OPENAI_MODEL", DEFAULT_MODEL)
            client = OpenAIClient(
                api_key="sk-simulated-not-a-real-key",
                model=model,
                budget=budget,
                logger=logger,
                transport=make_scenario(args.simulate, model),
                read_timeout=args.timeout,
                mode=mode,
            )
        else:
            mode = "mock"
            model = "mock"
            client = MockClient(budget=budget, logger=logger)

        # 再試行やエラーの表示（stderr）より先に出るよう、すぐ flush する
        print("{} mode={} model={} max_calls={}".format(TAG, mode, model, args.max_calls), flush=True)
        resp = client.complete(messages, max_tokens=args.max_tokens)

    except ConfigError as e:
        _print_error(e)
        return 2
    except BudgetExceededError as e:
        _print_error(e, budget)
        return 3
    except LLMError as e:
        _print_error(e, budget)
        return 4

    if resp.text:
        print("{} response: {}".format(TAG, mask_secrets(resp.text)))
    else:
        print("{} response: （空）".format(TAG))
    print(
        "{} latency={:.2f}s tokens(in/out)={}/{} attempts={} calls_used={} finish_reason={}".format(
            TAG,
            resp.latency_s,
            _fmt(resp.input_tokens),
            _fmt(resp.output_tokens),
            resp.attempts,
            budget.summary(),
            resp.finish_reason,
        )
    )
    if resp.truncated:
        print(
            "{} 注意: 出力上限（--max-tokens={}）で打ち切られました。推論の設定か --max-tokens を見直してください。".format(
                TAG, args.max_tokens
            )
        )
    print("{} log saved: {}".format(TAG, os.path.relpath(logger.path, HERE)))
    return 0


def _fmt(v):
    """数値を表示用の文字列にする。値が無ければ「-」にする（トークン数が返ってこなかったときのため）。"""
    return "-" if v is None else str(v)


def _print_error(e, budget=None):
    """エラーの種類・内容・対処法を、キーを伏せ字にしてから画面に表示する。"""
    msg = mask_secrets(e.message)
    sys.stderr.write("{} 失敗 [{}] {}\n".format(TAG, e.kind, msg))
    if e.hint:
        sys.stderr.write("{} 対処: {}\n".format(TAG, e.hint))
    if budget is not None:
        sys.stderr.write("{} calls_used={}\n".format(TAG, budget.summary()))


if __name__ == "__main__":
    sys.exit(main())
