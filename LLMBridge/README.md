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

## 段階2a：Unity との接続と同期

OpenAI の API は使わない。Unity Editor で `Assets/Scenes/LLM/LLMTestScene` を開いておく。

1. LLMBridge フォルダで実行する（接続待ちになる）
   ```powershell
   ..\..\helloworld\env\Scripts\python.exe step2a_connect.py
   ```
2. 「Unity の再生ボタンを押してください」と出たら、Editor の再生ボタンを押す
3. 途中で Editor 全体が数秒固まる（わざと止めて、ゲームも止まるかを確かめている）
4. 終わったら Editor の再生を止める（接続を閉じたあとは、Player が学習済みモデルで動き出すことがある）

### オプション
長さと停止位置は、フレームでも秒（ゲーム内秒）でも指定できる。1フレーム＝FixedUpdate 1回（0.02秒）。値は `settings.py` で一括して変えられる。

| オプション | 既定 | 内容 |
|---|---|---|
| `--frames N` / `--sec S` | 600フレーム | 動かす長さ |
| `--pause-at-frames N` / `--pause-at-sec S` | 300フレーム | 何フレーム目で止まるか（0なら止まらない） |
| `--pause-sec S` | 5 | Python が止まる実時間（秒） |
| `--wait-sec S` | 120 | Unity からの接続や応答を待つ上限（秒） |
| `--behavior NAME` | Player | 操作する behavior 名 |

Player は3フレームごとに判断するので、3で割り切れない値は直後の判断に合わせる（例：100 → 102。画面に表示される）。

### 期待する出力
```
[step2a] 接続しました。behavior: Player?team=0 観察=[142] 操作=連続3 離散[]
[step2a] frame=300（6.00秒）で 5.0秒止まります…
[step2a] 停止をはさんだ変化量=0.1261（ふだんの1判断: 中央値0.0399、95%値0.5897／5秒ぶん（83判断）進んだ場合の目安: 7.2666）→ ロックステップ OK
[step2a] 600フレーム（12.00秒、200判断）完了。平均 64.6ms/判断 エピソードの区切り=0回
[step2a] 終了しました
```

| 終了コード | 意味 |
|---|---|
| 0 | 成功 |
| 2 | 設定エラー（オプションの誤り） |
| 5 | Unity との接続・通信のエラー（再生していない、ポートが使用中、途中で再生を止めた など） |
| 6 | 同期の判定が NG |

記録は `logs/unity_YYYYMMDD.jsonl` に残る（接続情報、判断ごとのフレーム番号・判断番号・ゲーム内秒・Player の位置、同期の判定結果）。

## 段階2b：間接操作と耐久

OpenAI の API は使わない。Unity Editor で `Assets/Scenes/LLM/LLMTestScene` を開いておく。
テスト用の間接操作（止まる・左・右・上・下）を一定の間隔でランダムに切り替えながら、1ゲーム（60秒）を超えて回す。

1. LLMBridge フォルダで実行する（接続待ちになる。再生ボタンを押すまでに時間がかかりそうなら `--wait-sec 300`）
   ```powershell
   ..\..\helloworld\env\Scripts\python.exe step2b_unity_loop.py --wait-sec 300
   ```
2. 表示が出たら Editor の再生ボタンを押す
3. 30秒目ごろに Editor 全体が65秒固まる（LLM の遅い返事を真似している。操作しない）
4. 約2分半で終わる。終わったら Editor の再生を止める

### オプション（長さ・間隔・位置は、フレームでも秒でも指定できる）
| オプション | 既定 | 内容 |
|---|---|---|
| `--frames N` / `--sec S` | 4500フレーム（90秒） | 動かす長さ。シーンのやり直しを確かめるには60秒より長くする |
| `--command-interval-frames N` / `--command-interval-sec S` | 150フレーム（3秒） | 間接操作を選び直す間隔 |
| `--long-pause-at-frames N` / `--long-pause-at-sec S` | 1500フレーム（30秒） | 長い停止の位置（0なら止まらない） |
| `--long-pause-sec S` | 65 | 長い停止の長さ（実時間の秒） |
| `--seed N` | なし | ランダムな選び方を再現したいときに指定する |
| `--wait-sec S` | 120 | Unity からの接続や応答を待つ上限（秒） |
| `--behavior NAME` | Player | 操作する behavior 名 |

短く試すとき：`--sec 20 --long-pause-sec 5`（シーンのやり直しは確かめられない）

### 期待する出力（抜粋）
```
[step2b] 接続しました。behavior: Player?team=0 観察=[142] 操作=連続3
[step2b] frame=0（0.00秒） game=1 間接操作: 右
[step2b] frame=1002（20.04秒） 区切り: 20秒の区切り（MaxStep）（interrupted=True）
[step2b] frame=1500（30.00秒）で 65.0秒止まります…（LLM の遅い返事を想定）
[step2b] 再開しました
[step2b] frame=3003（60.06秒） 区切り: ゲーム終了（interrupted=False） → game=2
[step2b] エージェント数: 最大1体、これまでに見た ID: [1, 3]（増えていません）
[step2b] 4500フレーム（90.00秒、1500判断）完了。ゲーム終了1回、20秒の区切り4回、間接操作の切り替え30回、平均 60.1ms/判断
```
ゲーム終了の瞬間に `[WARNING] Unknown side channel data received.` が多数出るが、既存コードが送る統計を受け取る口がまだ無いため（段階3で対応）。動作には影響しない。

| 終了コード | 意味 |
|---|---|
| 0 | 成功 |
| 2 | 設定エラー（オプションの誤り） |
| 5 | Unity との接続・通信のエラー |
| 7 | Player エージェントが増えた（作業を止めて相談する） |

記録は `logs/unity_YYYYMMDD.jsonl` に残る。判断ごとに、通しのフレーム番号・判断番号・何ゲーム目か・そのゲームの中のフレーム番号・ゲーム内秒・今の間接操作・Player の位置・エージェントの ID と数。ほかに、間接操作の切り替え、区切り（種類つき）、長い停止の開始と終了。

## 段階3a：状況の送信（通り道）

OpenAI の API は使わない。LLMTestScene に置いた `LLMBridgeManager`（Unity 側）が、Python とつながっているときだけ、
シーン読み込みの情報と毎フレームの情報（フレーム番号、時刻、自分と相方の位置）を自作サイドチャンネルで送る。
Python なしで再生したときは何もしない。

```powershell
..\..\helloworld\env\Scripts\python.exe step3a_state.py --wait-sec 300
```
表示が出たら Editor の再生ボタンを押す。約90秒で終わる（長い停止はない）。

| オプション | 既定 | 内容 |
|---|---|---|
| `--frames N` / `--sec S` | 4500フレーム（90秒） | 動かす長さ |
| `--command-interval-frames N` / `--command-interval-sec S` | 150フレーム（3秒） | 間接操作を選び直す間隔 |
| `--seed N` | なし | ランダムな選び方を再現したいとき |
| `--wait-sec S` | 120 | Unity からの接続や応答を待つ上限（秒） |

最後に5項目の確認結果を表示する（設定値の一致、フレームの抜け・重複、PlayerAgent の数、統計と警告、時間とデータ量）。

| 終了コード | 意味 |
|---|---|
| 0 | すべて OK |
| 2 | 設定エラー |
| 5 | Unity との接続・通信のエラー |
| 8 | 確認項目に NG がある |

記録：
- `logs/state_YYYYMMDD_HHMMSS.jsonl`：全フレームの情報（1回の実行で1ファイル。`type` が `frame`／`scene`／`stats`）
- `logs/unity_YYYYMMDD.jsonl`：接続、区切り、シーン読み込み、統計、確認結果など

## 段階3b：状況の送信（中身）

Unity の `LLMBridgeManager` が10フレームに1回、敵・敵の弾・自分・相方の位置と移動量、出来事を送る。
`step3a_state.py` の流れに、確認6〜9を足したもの。

```powershell
..\..\helloworld\env\Scripts\python.exe step3b_state.py --wait-sec 300
```

記録（`logs/state_*.jsonl` の `frame`）の形：
| 項目 | 形 |
|---|---|
| `self` / `partner` | `[x, y, 移動量x×係数, 移動量y×係数]`（移動量が無ければ `[x, y]`、見つからなければ `[]`） |
| `enemies` / `bullets` | `[["E10", x, y, 移動量x×係数, 移動量y×係数], ...]`（敵は E、弾は B。現れた最初のフレームは `[ID, x, y]`） |
| `events` | `{"kind": "hit/kill/escape", "who": "self/partner", "enemy": "E10", "frame": 起きたフレーム}` |

移動量は「1フレームの移動量 × `settings.MOVE_SCALE`」。段階3d から係数は12で、送られる移動量がそのまま「1手（12フレーム）で進む距離」になる（3b・3c のログは係数10）。数字は小数2桁。

## 段階3c：LLM との仮接続

LLM が0.42秒（21フレーム）ごとに状況の要約を見て、止まる＋8方向から1つを選び、Player を動かす。
LLM の返事を待つあいだ、ゲームは止まる（実時間で1ゲーム約4分）。

```powershell
..\..\helloworld\env\Scripts\python.exe step3c_llm_play.py --wait-sec 300                       # モック（API を使わない）
..\..\helloworld\env\Scripts\python.exe step3c_llm_play.py --simulate transient --wait-sec 300   # 疑似エラー（API を使わない）
..\..\helloworld\env\Scripts\python.exe step3c_llm_play.py --real --wait-sec 300                 # 実API（1ゲーム約143回、約1.5円）
```

| オプション | 既定 | 内容 |
|---|---|---|
| `--real` / `--simulate NAME` | モック | NAME：`transient`（タイムアウト・サーバーのエラー）、`invalid`（変な返事）、`auth`（キーが無効） |
| `--frames N` / `--sec S` | 3000フレーム（60秒） | 動かす長さ |
| `--command-interval-frames N` / `--command-interval-sec S` | 20フレーム（判断に合わせて21） | LLM に選ばせる間隔 |
| `--max-calls N` | 350 | HTTP リクエストの上限（再試行・聞き直しも数える） |
| `--no-reason` | 理由あり | 理由（reason）を返させない |
| `--seed N` | 0 | モック・疑似エラーの乱数の種 |

エラー時：変な返事は1回だけ聞き直し、だめなら前の操作を続ける。一時的なエラー（混雑・タイムアウト・サーバー）も前の操作を続ける。
キーが無効・残高不足・回数上限のときは終了する。

| 終了コード | 意味 |
|---|---|
| 0 | すべて OK |
| 2 | 設定エラー |
| 3 | 回数上限に達した |
| 4 | API のエラーで終了（キーが無効など） |
| 5 | Unity との接続・通信のエラー |
| 8 | 確認項目に NG がある |

最後に10項目の確認（3a の1〜5、3b の6〜9、9つの操作の向き）と、LLM の集計（正しい返事の割合、待ち時間、トークン数）を表示する。
LLM に送った文章と返事は `logs/llm_YYYYMMDD.jsonl`、選んだ操作と理由は `logs/unity_YYYYMMDD.jsonl` に残る。

### 段階3d（実験）：数手まとめて考えさせ、考えながら進める

`step3c_llm_play.py` にオプションを付けると動く（付けなければ 3c と同じ）。結果は PLAN.md 段階3d を参照。

```powershell
..\..\helloworld\env\Scripts\python.exe step3c_llm_play.py --real --plan-steps 3 --overlap --step-reasons --command-interval-frames 12 --wait-sec 300
```

| オプション | 内容 |
|---|---|
| `--plan-steps N` | 1回の思考で何手まとめて決めさせるか（1手の長さは `--command-interval-frames`。12＝0.24秒） |
| `--overlap` | LLM が考えているあいだもゲームを進める。次の計画が間に合わなければ止まって待つ |
| `--step-reasons` | 手ごとに理由を書かせる（理由が1つだと一直線の計画になりやすかった） |
| `--mock-delay S` | モックの返事をわざと遅らせる（考えながら進める動きの確認用） |

移動量の係数は12（送られる移動量＝1手で進む距離）。Unity の LLMBridgeManager の Move Scale も12にしてシーンを保存しておく。
設定値が `settings.py` と食い違っていると、LLM を呼ぶ前に止まる（終了コード2）。

## ログ

`logs/llm_YYYYMMDD.jsonl` に、HTTP の1試行につき1行を追記する（`logs/` は `.gitignore` 済み）。
保存する内容：時刻、段階、モード、モデル名、送ったメッセージと設定値、応答文、トークン数、所要時間、HTTPステータス、エラーの種類、何回目の試行か。
`sk-` で始まる文字列は `sk-***` に置き換えて保存する。リクエストヘッダーは保存しない。
