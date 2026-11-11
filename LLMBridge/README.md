# LLMBridge

LLM（OpenAI API）が選んだ「意図」を、Python 経由で Unity ML-Agents のゲームに送るための仕組み。
計画・決定事項・進捗は [PLAN.md](PLAN.md) を参照。

## 準備

- Python：既存の venv `C:\Users\ikeda\helloworld\env`（Python 3.8.10）を使う。**追加でインストールするものはない**
- APIキー：`.env.example` をコピーして `.env` を作り、`OPENAI_API_KEY` を記入する
  - `.env` は `.gitignore` 済み。キーはチャットやコミットに貼らない
  - モック・疑似エラーでの実行には `.env` は不要

## 段階1：APIの疎通

LLMBridge フォルダで実行する。

PowerShell:
```powershell
cd LLMBridge
..\..\helloworld\env\Scripts\python.exe step1_ping.py                                      # モック（APIを使わない）
..\..\helloworld\env\Scripts\python.exe step1_ping.py --simulate rate_limit --max-calls 3   # 疑似エラー（APIを使わない）
..\..\helloworld\env\Scripts\python.exe step1_ping.py --real                               # 実API（1回だけ。費用が発生）
```

Git Bash:
```bash
cd LLMBridge
../../helloworld/env/Scripts/python.exe step1_ping.py
../../helloworld/env/Scripts/python.exe step1_ping.py --simulate rate_limit --max-calls 3
../../helloworld/env/Scripts/python.exe step1_ping.py --real
```

### オプション
| オプション | 既定 | 内容 |
|---|---|---|
| `--real` | なし | 実APIを呼ぶ |
| `--simulate NAME` | なし | 偽の通信で、本番と同じクライアントのエラー処理を確かめる。NAME：`ok` `rate_limit` `quota` `timeout` `server_error` `auth` `bad_request` `empty` |
| `--max-calls N` | 1 | 1回の実行あたりのHTTPリクエスト上限（リトライも数える）。上限に達したら終了 |
| `--prompt TEXT` | `Reply with exactly one word: pong` | 送る文 |
| `--max-tokens N` | 16 | 出力の上限トークン数 |
| `--timeout SEC` | 60 | 応答待ちのタイムアウト |
| `--env-file PATH` | `LLMBridge/.env` | 読み込む .env |

### 期待する出力（実API・成功時）
```
[step1] mode=real model=gpt-6-luna max_calls=1
[step1] response: pong
[step1] latency=0.84s tokens(in/out)=12/2 attempts=1 calls_used=1/1 finish_reason=stop
[step1] log saved: logs/llm_YYYYMMDD.jsonl
```

### 失敗したとき
`[step1] 失敗 [種類] 内容` と `[step1] 対処: ...` を表示し、0以外の終了コードで終わる。

| 終了コード | 意味 |
|---|---|
| 0 | 成功 |
| 2 | 設定エラー（キー未設定など） |
| 3 | 呼び出し回数の上限に達した |
| 4 | APIエラー（認証、残高不足、タイムアウトなど） |

### 再試行のルール
| 状況 | 再試行 |
|---|---|
| 429 レート制限 | する（`Retry-After` があればその秒数、なければ 2→4→8秒、最大30秒） |
| 429 `insufficient_quota`（残高不足） | しない |
| 5xx、接続失敗、タイムアウト | する |
| 400 / 401 / 403 / 404 | しない |

再試行は最大3回。リトライも `--max-calls` に数える。

## ログ

`logs/llm_YYYYMMDD.jsonl` に、HTTP の1試行につき1行を追記する（`logs/` は `.gitignore` 済み）。
保存する内容：時刻、段階、モード、モデル名、送ったメッセージと設定値、応答文、トークン数、所要時間、HTTPステータス、エラーの種類、何回目の試行か。
`sk-` で始まる文字列は `sk-***` に置き換えて保存する。リクエストヘッダーは保存しない。
