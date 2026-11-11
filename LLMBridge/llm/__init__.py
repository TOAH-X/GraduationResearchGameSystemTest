# -*- coding: utf-8 -*-
"""
【このファイルの役割】
llm フォルダを Python の「パッケージ」（C# の名前空間のようなまとまり）として扱うための目印。
中身は空でよい。このファイルがあると、ほかのファイルから `from llm.base import ...` のように読み込める。

【llm フォルダの中身】
- base.py          : 共通の約束ごと（エラーの種類、応答の形、クライアントの呼び出し方）
- budget.py        : 呼び出し回数の上限（--max-calls）を数える
- transport.py     : HTTP を実際に送る部分（本物と、テスト用の偽物）
- openai_client.py : OpenAI API を呼ぶ本体
- mock_client.py   : API の代わりに決まった応答を返す（APIを使わずに試すため）
"""
