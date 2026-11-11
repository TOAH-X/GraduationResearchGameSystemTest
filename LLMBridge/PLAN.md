# LLMBridge 実装計画

Python を橋渡しにして、LLM（OpenAI API）が選んだ「意図」を Unity ML-Agents のゲームに送る仕組みを作る。

- 作成日：2026-11-11（同日更新：モデルの決定、課題への対策を反映）
- 状態：**段階1 完了**（§9）。段階2aは未着手（2026-11-11 に段階を7つに並べ直した。§6）

---

## 1. 目的

- LLM は毎フレームの操作をしない。数秒ごとに、あらかじめ定義した意図（3〜5個）から1つを選ぶだけ
- 低レベルの操作は、エージェント側（学習済みモデルなど）が担当する

## 2. 使う場面

| 場面 | LLM | 支援エージェント | ゲームの進み方 |
|---|---|---|---|
| 配線確認（この計画の段階1〜6） | 使う（モックが標準） | 推論のみ | ロックステップ |
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

### 5.3 状況の送り方（段階3で実装）

| | ダミーエージェントの観察 | 自作サイドチャンネル（JSON） |
|---|---|---|
| 実装量 | 少ない | Unity側・Python側に1クラスずつ |
| ステップとの同期 | 厳密 | ステップ単位 |
| 項目の追加 | 固定長の float。観察数の設定も変える必要がある | キーを足すだけ |
| 数が変わるデータ（敵や弾の数） | 上限の数だけ枠を取り、空きを埋める必要がある | リストのまま送れる |
| LLMプロンプトへの変換 | 添字の対応表が必要 | ほぼそのまま使える |

**推奨：自作サイドチャンネル（仮称 `GameStateChannel`）**。LLM に渡すのは意味のついた可変長の情報だから。毎ステップではなく、要求時かNステップごとに送る。
段階2bで Python 側に `StateProvider` インターフェースだけを用意し、中身はダミーにしておく。段階3でサイドチャンネル版に差し替える。

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
- **確認済み（2026-11-11）**：公式ページは Responses API の書き方（`reasoning.effort`）でしか説明していなかったが、段階1の実API呼び出しで、Chat Completions では `reasoning_effort` と `max_completion_tokens` がそのまま通ることを確かめた
- 出典：[GPT-6 Luna](https://developers.openai.com/api/docs/models/gpt-6-luna.md)、[Pricing](https://developers.openai.com/api/docs/pricing)、[Deprecations](https://developers.openai.com/api/docs/deprecations)、[GPT-4o mini](https://developers.openai.com/api/docs/models/gpt-4o-mini)、[GPT-5 nano](https://developers.openai.com/api/docs/models/gpt-5-nano)

## 6. 段階ごとの計画

### 全体（2026-11-11 に並べ直した）

| # | 段階 | やること | 前提 | 不確実さ | API |
|---|---|---|---|---|---|
| 1 | APIの疎通 | ✅ 完了 | — | — | 使用済み |
| 2a | Unity との配線：接続と同期 | 新しいシーン、同期用ダミー、`env.step()` を回すだけのループ | なし | **高い** | 使わない |
| 2b | Unity との配線：意図の送信と耐久 | ランダムな意図を送って Unity のログで確認。65秒の待機、シーンの再読み込みに耐える | 2a | **高い** | 使わない |
| 3 | 状況の送信 | 自作サイドチャンネルで、距離・方向・被弾などを Python に送る | 2 | 中 | 使わない |
| 4 | 意図の実行 | 被支援プレイヤーが、受け取った意図で動きを変える。意図どおりにいったかを状況から判定する | 2、3 | **高い**（研究上の設計判断） | 使わない |
| 5 | 意図の選択 | 状況のJSONから LLM に意図を選ばせて検証する。モック→実API | 1、3 | 低い | 少し使う |
| 6 | 統合 | 2〜5をつなぎ、LLM が選んだ意図でゲームが動くことを確かめる | 2〜5 | 中 | 使う（回数を絞る） |
| 7 | 運用 | LLMプレイでデータを集める（ログ、ゲーム数、費用、所要時間） | 6 | 低い | 本格的に使う |

```
1 ──────────────────────────────────────┐
2a ─ 2b ─ 3（状況の送信）─┬─ 4（意図の実行）─┤
                          └─ 5（意図の選択）─┴─ 6（統合）─ 7（運用）
```

**この順番にした理由**
- 当初は、いただいた原案の順番（API → 意図の選択 → Unity）をそのまま使っていた。しかし、Unity との配線は「意図はランダムでよい」としたので、意図の選択には依存していない。そこで、依存関係と不確実さで並べ直した
- **2を最初に**：まだ分かっていないことが一番多く（ML-Agents の同期、長時間の停止、シーンの再読み込み）、ほかのすべての段階の土台になるため。さらに a/b の2回に分け、「つながる・同期する」を先に確かめる
- **3（状況の送信）を4より先に**（2026-11-11 に入れ替えた）：意図の実行では「意図どおりにいったか」を判定して疑似プレイヤーの気分とみなす構想で、その判定にも分析にもゲームの状況が必要になるため（§8）。LLM に渡す状況の形式もここで決まるので、5のプロンプトの作り直しも減る
- **4（意図の実行）を早めに**：意図を受け取っても動きが変わらなければ、LLM に選ばせる意味がない。実行方法は研究の中身に関わる判断で、再学習が必要になると時間がかかるため、3の直後に置く
- **5は後ろへ**：Python だけで完結し、つまずく要素が少ない

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
│  └─ mock_client.py   APIの代わりに応答を返す（段階5以降で使う）
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

### 段階2a：Unity との配線（接続と同期）

**目的**：Python から Unity Editor に接続し、`env.step()` のときだけゲームが進む（ロックステップ）ことを確かめる。意図はまだ送らない。

**作るファイル（Unity）**
- `Assets/Scenes/LLM/LLMTestScene.unity`：B_Scene の複製。CSV出力を5つともOFF、`GameManager` の `isReload` を true（60秒後に同じシーンを再読み込み）
- `Assets/Script/LLM/IntentBridge.cs`：Python と接続しているとき、かつ `Default` 型のエージェントが1体もないときだけ、同期用のダミーエージェントを作る（`DontDestroyOnLoad`）

**作るファイル（Python）**：`step2a_connect.py`（接続して N ステップ回し、閉じるだけ）

**確かめること**
- 接続できて、behavior の一覧（ダミー）が見える
- Python が `step()` を呼ばない間は、ゲームが止まっている（数秒止めて目視とログで確認）
- 正しく終了でき、Editor 側もエラーにならない
- 詳細は段階2aに入る前にここへ追記する

### 段階2b：Unity との配線（意図の送信と耐久）

**目的**：意図の番号が Unity に届くこと、長い待ちとシーンの再読み込みに耐えることを確かめる。

**作るファイル（Python）**：`intent/intents.py`（仮の意図5つ）、`intent/controller.py`（意図を選ぶ差し込み口。`select(状況) → 意図の番号`。今回はランダム版）、`unity/env_wrapper.py`、`unity/state_provider.py`（インターフェースとダミー実装）、`step2b_unity_loop.py`

**変更するファイル（Unity）**：`IntentBridge.cs` に、意図の受信と `Debug.Log` を追加する

- N ステップごとにランダムな意図を `EnvironmentParametersChannel`（キー `intent`）で送る
- Unity のコンソールに `[IntentBridge] intent=2 (step=...)` と出ることを確認する
- 途中で65秒待ってから再開しても、ループが続くことを確認する（Python 側の `timeout_wait` は待機時間より長く設定する）
- 60秒のゲーム終了でシーンが再読み込みされたあとも、ダミーと意図の受信が続くことを確認する
- 詳細は段階2aの完了後にここへ追記する

### 段階3：状況の送信（概要）

- 自作サイドチャンネル（仮称 `GameStateChannel`、§5.3）で、距離・方向・被弾などを JSON で Python に送る
- 段階2bの `StateProvider` を、ダミーからサイドチャンネル版に差し替える
- 段階4で「意図どおりにいったか」を判定するのに必要な項目も、ここで送れるようにしておく（項目は段階4の詳細が共有されてから決める）

### 段階4：意図の実行（概要）

- 被支援プレイヤーが、受け取った意図で実際に動きを変える
- 意図どおりにいったかを状況から判定し、それを疑似プレイヤーの気分とみなす構想（§8。詳細はユーザーから後で共有）
- 実行方法（ルール、既存モデルの切り替え、意図を観察に加えて再学習 など）と意図の中身は、共有を受けてから決める

### 段階5：意図の選択（概要。旧・段階2）

**作るファイル**：`intent/selector.py`（LLM 版の選び方）、`step5_select_intent.py`

- 状況のJSON（段階3の形式）と選択肢を渡し、`{"intent": "...", "reason": "..."}` の形で返させる
- `response_format` は `json_object`（JSONで返させるモード）を使う
- 選択肢に入っているかを検証し、不正なら再試行する（再試行も `--max-calls` に数える）
- モックに不正な出力を返させて、再試行が働くことも確認する
- 段階2bの差し込み口に、ランダム版の代わりとして差し込めるようにする

### 段階6：統合（概要）

- 2〜5をつなぎ、LLM が選んだ意図でゲームが動くことを確かめる。`--max-calls` で回数を絞る

### 段階7：運用（概要）

- LLMプレイでデータを集める。ゲーム数・費用・所要時間の見積もりと記録

## 7. 既存プロジェクトへの変更一覧（戻し方）

| 種類 | 対象 | 内容 | 段階 | 戻し方 |
|---|---|---|---|---|
| 新規 | `LLMBridge/` 一式 | Python 側のコード、この計画書 | 1〜 | フォルダを削除 |
| 追記 | `.gitignore` | `LLMBridge/.env`、`LLMBridge/logs/`、`LLMBridge/**/__pycache__/` を追加（コメント行を含む4行） | 1 | 末尾の LLMBridge のブロック（4行）を削除 |
| 新規 | `Assets/Script/LLM/IntentBridge.cs`（＋.meta） | 同期用ダミーの生成（2a）と、意図の受信ログ（2b）。Python と接続しているときだけ動く | 2a、2b | ファイルを削除 |
| 新規 | `Assets/Scenes/LLM/LLMTestScene.unity`（＋.meta） | B_Scene の複製。CSV出力をOFF（5つ）、`isReload` を true | 2a | ファイルを削除 |

- B_Scene を含む**既存のシーンとスクリプトは変更しない**
- 新しいシーンは Build Settings に追加しない（Editor で開いて再生するだけ）

## 8. 未決事項

| 項目 | 状況 |
|---|---|
| ~~OpenAI のモデル名~~ | **決定**：`gpt-6-luna`、reasoning effort は `none`（§5.4） |
| ~~Chat Completions でのパラメータ名~~ | **確定**：`max_completion_tokens` と `reasoning_effort` で通った（§9 段階1の結果） |
| 意図の実行と「気分」の考え方 | **ユーザーから後で共有**。現時点の概要：疑似プレイヤーに意図を持たせて意図どおりの行動をさせ、**その通りにいったかどうかを疑似プレイヤーの気分とみなす**。分析には状況も含める（2026-11-11 時点のざっくりした構想） |
| 意図の中身（選択肢） | 段階2まではランダムでよい。上の共有を受けて段階4で決める |
| 被支援プレイヤーが意図をどう実行するか | 上の共有を受けて段階4で決める。候補：ルールで動かす／既存の Player モデルを切り替える／意図を観察に加えて再学習する |
| 「意図どおりにいったか」の判定方法 | 上の共有を受けて段階4で決める。判定に使う状況の項目は段階3で送れるようにする |
| 状況の送り方 | 推奨は自作サイドチャンネル（§5.3）。段階3で実装 |
| ~~段階3と4の順番~~ | **決定**：状況の送信（3）→ 意図の実行（4）に入れ替えた（2026-11-11） |
| ~~作業ブランチ~~ | **作成済み**：`feature/llm-bridge`（2026-11-11） |

## 9. 進捗と結果

| 段階 | 状態 | 実施日 | 結果・ログ |
|---|---|---|---|
| 1. APIの疎通 | **完了** | 2026-11-11 | 下記 |
| 2a. Unity との配線（接続と同期） | 未着手 | | |
| 2b. Unity との配線（意図の送信と耐久） | 未着手 | | |
| 3. 状況の送信 | 未着手 | | |
| 4. 意図の実行 | 未着手 | | |
| 5. 意図の選択 | 未着手 | | |
| 6. 統合 | 未着手 | | |
| 7. 運用 | 未着手 | | |

### 段階1の結果（2026-11-11）

実行環境：`C:\Users\ikeda\helloworld\env`（Python 3.8.10）。パッケージの追加はなし。

| # | 確認内容 | コマンド | 結果 |
|---|---|---|---|
| 1 | `.env` が無いときにキー未設定と表示される | `--real` | 成功：`失敗 [config] OPENAI_API_KEY が未設定です。` exit=2 |
| 2 | モックで動く | （オプションなし） | 成功：`response: pong` exit=0 |
| 2b | 偽の通信で正常な応答を処理できる | `--simulate ok` | 成功：`response: pong tokens(in/out)=12/2` exit=0 |
| 3a | 上限1回では、429のあとに再試行せず終了する | `--simulate rate_limit` | 成功：`[budget_exceeded]` calls_used=1/1 exit=3 |
| 3b | 429のあと Retry-After（1秒）だけ待って再試行する | `--simulate rate_limit --max-calls 3` | 成功：1.0秒待って2回目で成功。calls_used=2/3 exit=0 |
| 3c | 残高不足はすぐに止まる | `--simulate quota --max-calls 3` | 成功：再試行なし。calls_used=1/3 exit=4 |
| 3d | タイムアウトは2秒待って再試行する | `--simulate timeout --max-calls 3` | 成功：2回目で成功 exit=0 |
| 3e | 500は2秒待って再試行する | `--simulate server_error --max-calls 3` | 成功：2回目で成功 exit=0 |
| 3f | 401は再試行せず、メッセージ内のキーをマスクする | `--simulate auth --max-calls 3` | 成功：`sk-***` と表示。exit=4 |
| 3g | 400は再試行しない | `--simulate bad_request --max-calls 3` | 成功：calls_used=1/3 exit=4 |
| 3h | 空の応答（`finish_reason=length`）を知らせる | `--simulate empty` | 成功：`response: （空）` と注意を表示 |
| 4 | ログと画面にキーが残らない | `grep` で確認 | 成功：ログ14行中、`sk-` は `sk-***` の1件だけ。ダミーキーの値、Authorization ヘッダーは0件 |
| 5 | `.env`・`logs/`・`__pycache__/` がコミット対象外になる | `git check-ignore` | 成功 |
| 6 | **実APIで1回呼ぶ** | `--real` | **成功**：`response: pong`、latency=1.96s、tokens(in/out)=13/4、attempts=1、calls_used=1/1、finish_reason=stop、exit=0。費用は約 $0.000003 |
| 6b | 実API呼び出しのあともキーが漏れていない | ログを照合 | 成功：ログ中のキーの値0件、伏せ字になっていない `sk-` 0件。`.env` と `logs/` は `git status` に出ない |

**パラメータ名の確定**（§5.4 の未確認事項）：`gpt-6-luna` の Chat Completions で、`max_completion_tokens` と `reasoning_effort: "none"` がそのまま受け付けられた（HTTP 200）。`none` のため出力4トークンで、思考に使うトークンは発生していない。

実装中に見つけて直した点：
- パイプでつないで実行すると、エラー行（stderr）が見出しの行より先に出ていた → 見出しの行を出した直後に flush するよう修正
- `__pycache__/` が未追跡ファイルとして表示された → `.gitignore` に追加
