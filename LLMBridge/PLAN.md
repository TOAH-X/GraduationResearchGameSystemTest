# LLMBridge 実装計画

Python を橋渡しにして、LLM（OpenAI API）が選んだ「意図」を Unity ML-Agents のゲームに送る仕組みを作る。

- 作成日：2026-11-11（同日更新：モデルの決定、課題への対策を反映）
- 状態：**計画中（実装はまだ）**

---

## 1. 目的

- LLM は毎フレームの操作をしない。数秒ごとに、あらかじめ定義した意図（3〜5個）から1つを選ぶだけ
- 低レベルの操作は、エージェント側（学習済みモデルなど）が担当する

## 2. 使う場面

| 場面 | LLM | 支援エージェント | ゲームの進み方 |
|---|---|---|---|
| 配線確認（この計画の段階1〜3） | 使う（モックが標準） | 推論のみ | ロックステップ |
| **LLMプレイ時** | LLM が**被支援プレイヤー**を操作する | 推論のみ（**学習しない**） | ロックステップ |
| 再学習時 | 使わない（報酬は固定済み） | `mlagents-learn` で学習 | 通常の学習 |
| 人間がプレイする本番 | 使わない | 推論のみ | リアルタイム |

→ LLM を使う場面はすべてロックステップでよい。**非同期方式は不要**。

## 3. 前提と環境

| 項目 | バージョン・場所 |
|---|---|
| Unity | 2021.3.29f1 |
| ML-Agents（Unityパッケージ） | 2.2.1-exp.1（release_19）。`C:\Users\ikeda\helloworld\ml-agents-release_19` をローカル参照 |
| mlagents / mlagents_envs | 0.28.0 |
| Python | 3.8.10（既存の venv `C:\Users\ikeda\helloworld\env`） |
| 主な依存パッケージ | numpy 1.23.5 / protobuf 3.20.3 / grpcio 1.70.0 / torch 1.7.1 / requests 2.32.4 |
| LLM | OpenAI の有料API（Chat Completions API を `requests` で直接呼ぶ） |
| モデル | `gpt-6-luna`、推論の強さ（reasoning effort）は `none`（§5.4） |

- Python が Unity と LLM API の間を取り持つ。Unity は LLM と直接通信しない
- ゲームは Python の `env.step()` のときだけ進む。LLM の応答を待つあいだは止まる

## 4. ルール

**APIキー**
- コードに書かない。環境変数か `LLMBridge/.env` から読む（`.env` は `.gitignore` に入れる）
- ログ、標準出力、エラーメッセージ、コミット、チャットに出さない。`sk-` で始まる文字列は `sk-***` に置き換えてから出力する
- 値を空にした `.env.example` を用意する
- **`.env` を作るより先に `.gitignore` に追記する**（`.env` が誤ってコミットされないようにするため）

**APIの使い方**（大切に使う）
- モックを標準にする。実APIは `--real` を付けたときだけ呼ぶ
- `--max-calls` で1回の実行あたりの呼び出し回数に上限を設ける。リトライも1回として数え、上限に達したら**終了**する

**Python**
- Python 3.8 で動く書き方にする（`typing.List` / `Optional` を使い、`list[str]` や `X | Y` は使わない）
- 既存 venv のパッケージのバージョンは変えない。新しくインストールするものもない（`.env` の読み込みは自作する）
- Windows での文字化け対策：起動時に標準出力を UTF-8 に切り替え、ファイルの読み書きはすべて `encoding="utf-8"` を明示する（何も指定しないと cp932 になるため）

**記録と変更**
- 使ったモデル名、プロンプト、応答はファイルに保存する（キーは保存しない）
- 既存の Unity プロジェクトの変更は最小限にし、一覧（§7）で管理して元に戻せるようにする
- 削除や上書きなどの破壊的な操作は、事前にユーザーに確認する
- 不明な点は推測で進めず、質問する
- 各段階で動作を確認できてから次へ進む。実装はユーザーの明示的な指示があってから始める

## 5. 技術的な判断

### 5.1 同期用のダミーエージェントが必要
Unity が Python と同期して止まるのは、`BehaviorType = Default` のエージェントが観察を送ったときだけ（`RpcCommunicator.DecideBatch` / `PutObservations` で確認）。LLMプレイ時は全エージェントが Heuristic か Inference なので、**同期用のダミーエージェントを足す**。

- ダミーは `Default` 型のエージェントがシーンに1体もないときだけ実行時に作る（再学習時に `mlagents-learn` がダミーを学習対象にしないため）
- `DontDestroyOnLoad` にして、60秒ごとのシーン再読み込みでも消えないようにする

### 5.2 意図の送り方
`EnvironmentParametersChannel` でキー `intent` に意図の番号（float）を送る。Unity 側は `Academy.Instance.EnvironmentParameters` で受け取る。

### 5.3 状況の送り方（将来の拡張。今回は実装しない）

| | ダミーエージェントの観察 | 自作サイドチャンネル（JSON） |
|---|---|---|
| 実装量 | 少ない | Unity側・Python側に1クラスずつ |
| ステップとの同期 | 厳密 | ステップ単位 |
| 項目の追加 | 固定長の float。観察数の設定も変える必要がある | キーを足すだけ |
| 数が変わるデータ（敵や弾の数） | 上限の数だけ枠を取り、空きを埋める必要がある | リストのまま送れる |
| LLMプロンプトへの変換 | 添字の対応表が必要 | ほぼそのまま使える |

**推奨：自作サイドチャンネル（仮称 `GameStateChannel`）**。LLM に渡すのは意味のついた可変長の情報だから。毎ステップではなく、要求時かNステップごとに送る。
今回は Python 側に `StateProvider` インターフェースだけを用意し、中身はダミーにしておく。

### 5.4 モデルの選定（2026-11-11 決定）

**`gpt-6-luna`（reasoning effort = `none`）** にする。

| | `gpt-6-luna`（採用） | `gpt-4o-mini` | `gpt-5-nano` | `gpt-6-astra` |
|---|---|---|---|---|
| 料金（100万トークンあたり、入力/出力） | $0.10 / $0.50 | $0.15 / $0.60 | $0.05 / $0.40 | 高い（最上位モデル） |
| 状態 | 現行 | 非推奨。2027-01-14 に廃止予定（後継が `gpt-6-luna`） | 非推奨 | 現行 |
| 見送った理由 | — | 高く、まもなく廃止 | 非推奨 | 用途に対して高すぎる |

- 選んだ理由：最新世代でいちばん安い。公式が「速さと費用を重視するならこれ」と案内している。Chat Completions と Structured Outputs に対応している
- `none` にする理由：推論の強さは `none / low / medium（標準）/ high / xhigh / max` から選べる。`none` なら思考を飛ばすので速く、思考に使うトークンの料金がかからない。さらに「出力の上限を思考で使い切って、応答が空になる」問題も起きない
- 費用の目安：疎通確認1回で 0.001円未満、意図の選択1回で 0.01円程度、LLMプレイ1ゲーム（約20回）で 0.3円程度
- **未確認**：公式ページは Responses API の書き方（`reasoning.effort`）でしか説明していない。Chat Completions でのパラメータ名（従来は `reasoning_effort`）と、出力上限のパラメータ名（`max_completion_tokens` か `max_tokens` か）は、段階1の実API呼び出しで確かめる。受け付けられなければ 400 で停止するので、費用は1回分で済む
- 出典：[GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna.md)、[Pricing](https://developers.openai.com/api/docs/pricing)、[Deprecations](https://developers.openai.com/api/docs/deprecations)、[GPT-4o mini](https://developers.openai.com/api/docs/models/gpt-4o-mini)、[GPT-5 nano](https://developers.openai.com/api/docs/models/gpt-5-nano)

## 6. 段階ごとの計画

### 全体
| 段階 | やること | Unity | 実API（`--max-calls` の既定値） |
|---|---|---|---|
| 1. APIの疎通 | OpenAI を1回呼んで応答を表示する | 不要 | `--real` のときだけ（1回） |
| 2. 意図の選択 | ダミーの状況JSONから、意図を1つJSONで選ばせて検証する | 不要 | モックが標準（3回） |
| 3. Unityとの配線 | `env.step()` のループで、ランダムな意図を Unity に送る | 新しいシーンと `IntentBridge.cs` | モックが標準（5回） |

### 段階1：APIの疎通

**目的**：API を最小構成で1回呼んで応答を表示する。あわせて、以降の段階でも使う土台（設定の読み込み、エラーの分類、リトライ、回数上限、ログ）を作る。

**作るファイル**
```
LLMBridge/
├─ .env.example        OPENAI_API_KEY= / OPENAI_MODEL=gpt-6-luna / OPENAI_BASE_URL=（省略可）
├─ envutil.py          .env の読み込み
├─ llm/
│  ├─ base.py          LLMClient インターフェース、応答クラス、エラー分類
│  ├─ budget.py        CallBudget（--max-calls の管理）
│  ├─ transport.py     HTTPを送る部分（本物：requests／偽物：FakeTransport）
│  ├─ openai_client.py 実API版（送信は transport に任せる）
│  └─ mock_client.py   APIの代わりに応答を返す（段階2・3で使う）
├─ logging_util.py     JSONL形式のログ出力とキーのマスク
├─ step1_ping.py
└─ logs/               gitignore 対象
```

**作業の順番**：最初に `.gitignore` へ `LLMBridge/.env` と `LLMBridge/logs/` を追記してから、ほかのファイルを作る。

**仕様**
- 設定の読み込み：`KEY=VALUE` 形式。空行と `#` で始まる行は無視する。OSの環境変数があればそちらを優先する。値が未設定なら変数名だけを表示する
- 接続先：`OPENAI_BASE_URL` を省略すれば標準のURL（`https://api.openai.com/v1`）を使う
- リクエストの中身：`model`（`.env` の値）、`messages`、推論の強さ `none`、出力上限。パラメータ名は実API呼び出しの1回目で確定させる（§5.4）
- 応答が空で、`finish_reason=length`（上限で打ち切られた）の場合は、そう分かるメッセージを出す
- 疑似エラーの注入：`--simulate` を付けると、`OpenAIClient` の送信部分だけを `FakeTransport` に差し替える。`FakeTransport` は「1回目は429、2回目は200」のように決められた応答を順に返し、通信はしない。これにより、**本番と同じリトライ・分類・上限のコードを、実APIを使わずに確かめられる**
- 呼び出し方：`complete(messages, max_tokens, json_mode=False)` で、応答文・モデル名・トークン数・所要時間が返る
- エラーの分類とリトライ（最大3回。タイムアウトは接続10秒、応答待ち60秒）

| 状況 | 再試行 | 動作 |
|---|---|---|
| 429 レート制限 | する | `Retry-After` があればその秒数、なければ 2→4→8秒（最大30秒）待つ |
| 429 `insufficient_quota`（残高不足） | **しない** | すぐに停止する |
| 5xx、接続失敗、タイムアウト | する | 上と同じ間隔で待つ |
| 400 / 401 / 403 / 404 | しない | 内容を表示して停止する |

- 回数上限：HTTPリクエストを**送る前**に1回分を消費する。既定値が1なので、既定のままではリトライは起きない
- ログ（`logs/llm_YYYYMMDD.jsonl`、1回の呼び出しにつき1行）：時刻、段階、モデル名、送ったメッセージ、設定値、応答文、トークン数、所要時間、HTTPステータス、エラーの種類、何回目の試行か
- キーのマスク：`sk-` で始まる文字列（`sk-proj-...` も含む）を `sk-***` に置き換えてから表示・保存する。OpenAI の401エラーはメッセージにキーの一部を含むため。リクエストヘッダーは表示もログ保存もしない

**実行方法**
```
cd LLMBridge
..\..\helloworld\env\Scripts\python.exe step1_ping.py                                      # モック
..\..\helloworld\env\Scripts\python.exe step1_ping.py --real                               # 実API（1回）
..\..\helloworld\env\Scripts\python.exe step1_ping.py --simulate rate_limit --max-calls 3   # 疑似エラーでリトライを確認
```
オプション：`--real` / `--max-calls`（既定1）/ `--prompt` / `--max-tokens`（既定16。推論を `none` にするので小さくてよい）/ `--timeout` / `--simulate {rate_limit, quota, timeout, server_error}`

Git Bash で実行する場合は、パスを `../../helloworld/env/Scripts/python.exe` と書く（README にも併記する）。

**期待する出力（成功時）**
```
[step1] mode=real model=gpt-6-luna max_calls=1
[step1] response: pong
[step1] latency=0.84s tokens(in/out)=12/2 calls_used=1/1
[step1] log saved: logs/llm_YYYYMMDD.jsonl
```
失敗したときは、エラーの種類と次に何をすればよいかを1〜2行で表示し、0以外の終了コードで終わる。

**動作確認の手順**（実APIを使うのは最後の1回だけ）
1. `.env` が無い状態で実行し、キー未設定のメッセージが出ることを確認する
2. モックで正常に動くことを確認する
3. `--simulate` で、レート制限は待って再試行し、残高不足はすぐ止まり、上限に達したら終了することを確認する（本番と同じ `OpenAIClient` を通す）
4. `--real` で1回だけ呼ぶ。推論の強さと出力上限のパラメータ名がそのまま通るかも、ここで確かめる
5. `logs/` と画面出力に `sk-` が含まれていないこと、`git status` に `.env` が表示されないことを確かめる

### 段階2：意図の選択（Unity 不要）

**作るファイル**：`intent/intents.py`（意図の定義。仮に5つ）、`intent/selector.py`、`step2_select_intent.py`

- ダミーの状況JSONと選択肢を渡し、`{"intent": "...", "reason": "..."}` の形で返させる
- `response_format` は `json_object`（JSONで返させるモード）を使う
- 選択肢に入っているかを検証し、不正なら再試行する（再試行も `--max-calls` に数える）
- モックに不正な出力を返させて、再試行が働くことも確認する
- 詳細は段階1の完了後にここへ追記する

### 段階3：Unity との配線

**作るファイル（Python）**：`intent/controller.py`（IntentController）、`unity/env_wrapper.py`、`unity/state_provider.py`（インターフェースとダミー実装）、`step3_unity_loop.py`

**作るファイル（Unity）**：`Assets/Script/LLM/IntentBridge.cs`、`Assets/Scenes/LLM/LLMTestScene.unity`

- `mlagents_envs` の `UnityEnvironment(file_name=None)` で Editor に接続し、`env.step()` のループを回す
- Nステップごとにランダムな意図を `EnvironmentParametersChannel` で送る
- Unity のコンソールに `[IntentBridge] intent=2 (step=...)` と出ることを確認する
- 途中で65秒待ってから再開しても、ループが続くことを確認する（Python 側の `timeout_wait` は待機時間より長く設定する）
- 詳細は段階2の完了後にここへ追記する

## 7. 既存プロジェクトへの変更一覧（戻し方）

| 種類 | 対象 | 内容 | 段階 | 戻し方 |
|---|---|---|---|---|
| 新規 | `LLMBridge/` 一式 | Python 側のコード、この計画書 | 1〜3 | フォルダを削除 |
| 追記 | `.gitignore` | `LLMBridge/.env` と `LLMBridge/logs/` を追加 | 1 | 追記した2行を削除 |
| 新規 | `Assets/Script/LLM/IntentBridge.cs`（＋.meta） | 同期用ダミーの生成と、意図の受信ログ。Python と接続しているときだけ動く | 3 | ファイルを削除 |
| 新規 | `Assets/Scenes/LLM/LLMTestScene.unity`（＋.meta） | B_Scene の複製。CSV出力をOFF（5つ）、`isReload` を true | 3 | ファイルを削除 |

- B_Scene を含む**既存のシーンとスクリプトは変更しない**
- 新しいシーンは Build Settings に追加しない（Editor で開いて再生するだけ）

## 8. 未決事項

| 項目 | 状況 |
|---|---|
| ~~OpenAI のモデル名~~ | **決定**：`gpt-6-luna`、reasoning effort は `none`（§5.4） |
| Chat Completions でのパラメータ名 | 推論の強さと出力上限の名前を、段階1の実API呼び出しで確定させる（§5.4） |
| 意図の中身（選択肢） | 段階3まではランダムでよい。最終的には協調に関わる意図を想定（例：左側を担当、僚機の撃ち漏らしを拾う、回避を優先） |
| 被支援プレイヤーが意図をどう実行するか | 未定（既存の Player モデルを使う／意図を観察に加えて再学習する／ルールで動かす など） |
| 状況の送り方 | 推奨は自作サイドチャンネル（§5.3）。実装は後日 |
| ~~作業ブランチ~~ | **作成済み**：`feature/llm-bridge`（2026-11-11） |

## 9. 進捗と結果

| 段階 | 状態 | 実施日 | 結果・ログ |
|---|---|---|---|
| 1. APIの疎通 | 未着手 | | |
| 2. 意図の選択 | 未着手 | | |
| 3. Unityとの配線 | 未着手 | | |
