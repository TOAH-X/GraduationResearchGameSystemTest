using UnityEngine;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

public class PlayerAgent : Agent
{
    [SerializeField] PlayerBase playerBaseScript;                           // PlayerBaseスクリプト
    [SerializeField] PlayerMoveController playerMoveControllerScript;       // PlayerMoveControllerスクリプト
    [SerializeField] PlayerShotController playerShotControllerScript;       // PlayerShotControllerスクリプト
    [SerializeField] GameObject playerParentObj;

    [SerializeField] Transform bulletParent;                                // BulletParentのTransform
    [SerializeField] Transform enemyParent;                                 // EnemyParentのTransform 

    [SerializeField] bool isLimitedCollectObservations = true;              // 観察数の制限(弾幕を全て把握から一部把握へ変更)

    [SerializeField] RewardManager rewardManagerScript;                     // rewardManagerスクリプト

    [SerializeField] float gameTimer = 0;                                   // ゲームタイマー
    public int currentStep = 0;                                             // 出力用ステップ数管理(やり直しても管理できるようにするため)
    [SerializeField] private LearningLogCSV learningLogCSVScript;           // LearningLogCSVスクリプト

    [SerializeField] RewardManager rewardManager;
    [SerializeField] GameManager gameManager;

    [SerializeField] BehaviorParameters behaviorParameters;

    private bool isExported = false;                                        // CSVを出力済みか(EndGameが呼ばれたかどうかで判別するので厳密ではない)

    private int targetPlayerID = -1;

    public string BehaviorTypeName { get; set; } = "none";

    // 追加
    //[Header("基準となるプレイヤー")]
    //[SerializeField] private Transform player;

    private int bulletTrackCount = 15;                  // 追跡する弾の最大数
    private int enemyTrackCount = 20;                   // 追跡する敵の最大数

    [Header("非アクティブ時のダミー座標")]
    [SerializeField] private Vector2 bulletInactivePos = new Vector2(0f, 15f);

    // 弾の履歴データ（現在・1F前・2F前）
    private readonly Dictionary<Transform, Vector2[]> bulletHistory = new Dictionary<Transform, Vector2[]>();

    // ML用など他スクリプトで参照したい場合に使う
    public List<(Vector2 now, Vector2 prev1, Vector2 prev2)> bulletTrackedPositions = new List<(Vector2, Vector2, Vector2)>();

    [Header("非アクティブ時のダミー座標")]
    [SerializeField] private Vector2 enemyInactivePos = new Vector2(0f, 15f);

    // 弾の履歴データ（現在・1F前・2F前）
    private readonly Dictionary<Transform, Vector2[]> enemyHistory = new Dictionary<Transform, Vector2[]>();

    // ML用など他スクリプトで参照したい場合に使う
    public List<(Vector2 now, Vector2 prev1, Vector2 prev2)> enemyTrackedPositions = new List<(Vector2, Vector2, Vector2)>();

    // 追加


    // 位置情報ログ(機能を移すこと)
    [Header("学習用ログ設定")]
    private float statsBorderX = 2.0f;                 // 左右の境界線
    private float statsBorderY = 1.666f;               // 上下の境界線
    [SerializeField] private string statsNamePrefix = "Player";         // ログの頭につく名前（PlayerやSubPlayerに変更）
    private int[,] _gridCounts = new int[3, 3];                         // 9分割カウント用 [x, y]
    private int _totalStatFrames = 0;                                   // 9分割カウント用 [x, y]


    private void Awake()
    {
        behaviorParameters = GetComponent<BehaviorParameters>();
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    private void FixedUpdate()
    {
        if (!TryAcquireReferences()) return;

        // 1. ワールド座標ではなく「ローカル座標（親から見た位置）」を直接使う
        // これにより、エリアがどこにあっても「エリアの中心からの距離」が取れます
        Vector3 relativePos = playerBaseScript.transform.localPosition;

        // 2. 判定を行う
        // statsBorderX が 2.0、statsBorderY が 1.666 なら
        // ローカル座標の Y が 1.666 を超えた瞬間に "Top" と判定されます
        int xIndex = (relativePos.x < -statsBorderX) ? 0 : (relativePos.x > statsBorderX ? 2 : 1);
        int yIndex = (relativePos.y < -statsBorderY) ? 0 : (relativePos.y > statsBorderY ? 2 : 1);

        // カウントアップ
        _gridCounts[xIndex, yIndex]++;
        _totalStatFrames++;

        // デバッグ表示（これでエディタ上で数値が正しく動くか確認できます）
        
        //string xName = (xIndex == 0) ? "Left" : (xIndex == 2 ? "Right" : "Center");
        //string yName = (yIndex == 0) ? "Bottom" : (yIndex == 2 ? "Top" : "Middle");
        //Debug.Log($"LocalPos: {relativePos.x:F2} | BorderX: {statsBorderX} | Result: {xName}");
        //Debug.Log($"LocalPos: {relativePos.y:F2} | BorderY: {statsBorderY} | Result: {yName}");

        /*
        if (!TryAcquireReferences()) return;

        // 現在のワールド座標
        Vector3 playerPos = playerBaseScript.transform.position;

        // ★修正点A：判定用の「相対座標」を作る
        Vector3 relativePos = playerPos;
        if (playerParentObj != null)
        {
            relativePos = playerPos - playerParentObj.transform.position;
        }

        // ★修正点B：判定式の中身を 'playerPos' から 'relativePos' に書き換える
        // (ここが playerPos のままだと、ログは正しくても判定だけズレて右偏重になります)
        int xIndex = (relativePos.x < -statsBorderX) ? 0 : (relativePos.x > statsBorderX ? 2 : 1);
        int yIndex = (relativePos.y < -statsBorderY) ? 0 : (relativePos.y > statsBorderY ? 2 : 1);

        _gridCounts[xIndex, yIndex]++;
        _totalStatFrames++;
        */
        /*
        // プレイヤーがいるか確認
        if (!TryAcquireReferences()) return;

        // プレイヤーの位置を変数に入れる
        Vector3 playerPos = playerBaseScript.transform.position;

        // どのマスにいるか判定 (0:左/下, 1:中, 2:右/上)
        int xIndex = (playerPos.x < -statsBorderX) ? 0 : (playerPos.x > statsBorderX ? 2 : 1);
        int yIndex = (playerPos.y < -statsBorderY) ? 0 : (playerPos.y > statsBorderY ? 2 : 1);

        Debug.Log($"Current Pos X: {playerPos.x}");

        // カウントアップ
        _gridCounts[xIndex, yIndex]++;
        _totalStatFrames++;
        */

        /*
        // デバッグ用: 現在の判定状況をわかりやすく表示
        string xName = (xIndex == 0) ? "<color=red>Left</color>" : (xIndex == 2 ? "<color=blue>Right</color>" : "Center");
        string yName = (yIndex == 0) ? "Bottom" : (yIndex == 2 ? "Top" : "Middle");

        // コンソール出力
        // RelX: 判定に使われた相対座標
        // Border: 境界線（これを超えると判定が変わる）
        // Result: 最終的な判定結果
        Debug.Log($"[Check] RelX: {relativePos.x:F2} | Border: {statsBorderX} | Result: {xName} ({xIndex})");
        */
    }


    // ゲームオブジェクト生成時に呼ばれる
    public override void Initialize()
    {
        base.Initialize();

        DontDestroyOnLoad(this.gameObject);

        behaviorParameters = GetComponent<BehaviorParameters>();

        // 状態取得
        if (behaviorParameters.BehaviorType == BehaviorType.Default)
        {
            BehaviorTypeName = "Default";
        }
        else if (behaviorParameters.BehaviorType == BehaviorType.HeuristicOnly)
        {
            BehaviorTypeName = "Heuristic";
        }
        else if (behaviorParameters.BehaviorType == BehaviorType.InferenceOnly)
        {
            BehaviorTypeName = "Inference";
        }
        else 
        {
            BehaviorTypeName = "none";
        }
    }

    // エピソード開始時に呼ばれる
    public override void OnEpisodeBegin()
    {
        isExported = false;
    }

    // 観察取得時に呼ばれる
    public override void CollectObservations(VectorSensor vectorSensor)//posかも？
    {
        const float velocityScale = 0.5f;                   // 観察用の係数、ノイズだと思われないように値を大きくする

        // プレイヤーがいるか確認
        if (!TryAcquireReferences()) return;

        // プレイヤーの位置を変数に入れる
        Vector3 playerPos = playerBaseScript.transform.position;

        // 情報欠落(エラー。埋める用)
        bool isBulletMissing = false;
        bool isEnemyMissing = false;

        // 1*2(プレイヤー)+20*2*2(敵)+15*2*2(弾)=142
        if (bulletParent == null)
        {
            isBulletMissing = true;
        }
        if (enemyParent == null)
        {
            isEnemyMissing = true;
        }

        // 自身の位置を取得
        vectorSensor.AddObservation(playerPos.x / 3.0f);
        vectorSensor.AddObservation(playerPos.y / 2.5f);

        // --- アクティブな敵を取得 ---
        List<Transform> activeEnemies = new List<Transform>();
        if (isEnemyMissing == false)
        {
            for (int i = 0; i < enemyParent.childCount; i++)
            {
                var t = enemyParent.GetChild(i);
                if (t != null && t.gameObject.activeSelf)
                    activeEnemies.Add(t);
            }
        }

        // --- 履歴更新（順序に依存しない） ---
        foreach (var enemy in activeEnemies)
        {
            if (!enemyHistory.TryGetValue(enemy, out var h))
            {
                Vector2 currentPos = enemy.position;
                h = new Vector2[3]
                {
                enemyInactivePos,
                enemyInactivePos,
                enemyInactivePos
                };
                enemyHistory.Add(enemy, h);
            }

            h[2] = h[1];
            h[1] = h[0];
            h[0] = enemy.position;
        }

        // --- 破棄された敵を履歴から削除 ---
        List<Transform> enemyRemoveList = new List<Transform>();
        foreach (var kvp in enemyHistory)
        {
            if (kvp.Key == null || !kvp.Key.gameObject.activeSelf)
            {
                enemyRemoveList.Add(kvp.Key);
            }
        }
        foreach (var key in enemyRemoveList)
        {
            enemyHistory.Remove(key);
        }

        // --- プレイヤーに近い順に並び替え（観測専用） ---
        activeEnemies.Sort((a, b) =>
        {
            float da = (a.position - playerPos).sqrMagnitude;
            float db = (b.position - playerPos).sqrMagnitude;
            return da.CompareTo(db);
        });

        // --- 観測用リスト作成（順序固定） ---
        enemyTrackedPositions.Clear();
        for (int i = 0; i < enemyTrackCount; i++)
        {
            if (i < activeEnemies.Count)
            {
                var e = activeEnemies[i];
                var h = enemyHistory[e];
                enemyTrackedPositions.Add((h[0], h[1], h[2]));
            }
            else
            {
                enemyTrackedPositions.Add(
                    (enemyInactivePos, enemyInactivePos, enemyInactivePos)
                );
            }
        }

        // --- 敵の観測登録 ---
        if (isEnemyMissing == false)
        {
            foreach (var (now, prev1, prev2) in enemyTrackedPositions)
            {
                Vector2 relNow = now - (Vector2)playerPos;
                Vector2 relPrev1 = (now - prev1) / Time.fixedDeltaTime;
                Vector2 relPrev2 = (prev1 - prev2) / Time.fixedDeltaTime;

                vectorSensor.AddObservation(relNow.x / 3.0f);
                vectorSensor.AddObservation(relNow.y / 2.5f);
                vectorSensor.AddObservation(Mathf.Clamp(relPrev1.x / 3.0f * velocityScale, -2f, 2f));
                vectorSensor.AddObservation(Mathf.Clamp(relPrev1.y / 2.5f * velocityScale, -2f, 2f));
                // vectorSensor.AddObservation(relPrev2.x);
                // vectorSensor.AddObservation(relPrev2.y);
            }
        }
        else
        {
            foreach (var (now, prev1, prev2) in enemyTrackedPositions)
            {
                Vector2 relNow = (Vector2)playerPos;
                Vector2 relPrev1 = (Vector2)playerPos;
                Vector2 relPrev2 = (Vector2)playerPos;

                vectorSensor.AddObservation(enemyInactivePos.x / 3.0f);
                vectorSensor.AddObservation(enemyInactivePos.y / 2.5f);
                vectorSensor.AddObservation(0f);
                vectorSensor.AddObservation(0f);
                // vectorSensor.AddObservation(relPrev2.x);
                // vectorSensor.AddObservation(relPrev2.y);
            }
        }

        // =========================
        // 弾処理（敵と完全対称）
        // =========================

        // --- アクティブな弾を取得 ---
        List<Transform> activeBullets = new List<Transform>();
        if (isBulletMissing == false)
        {
            for (int i = 0; i < bulletParent.childCount; i++)
            {
                var t = bulletParent.GetChild(i);
                if (t != null && t.gameObject.activeSelf)
                    activeBullets.Add(t);
            }
        }

        // --- 履歴更新 ---
        foreach (var bullet in activeBullets)
        {
            if (!bulletHistory.TryGetValue(bullet, out var h))
            {
                Vector2 currentPos = bullet.position;
                h = new Vector2[3]
                {
                bulletInactivePos,
                bulletInactivePos,
                bulletInactivePos
                };
                bulletHistory.Add(bullet, h);
            }

            h[2] = h[1];
            h[1] = h[0];
            h[0] = bullet.position;
        }

        // --- 破棄された弾を履歴から削除 ---
        List<Transform> bulletRemoveList = new List<Transform>();
        foreach (var kvp in bulletHistory)
        {
            if (kvp.Key == null || !kvp.Key.gameObject.activeSelf) 
            {
                bulletRemoveList.Add(kvp.Key);
            }
        }
        foreach (var key in bulletRemoveList)
        {
            bulletHistory.Remove(key);
        }

        // --- プレイヤーに近い順 ---
        activeBullets.Sort((a, b) =>
        {
            float da = (a.position - playerPos).sqrMagnitude;
            float db = (b.position - playerPos).sqrMagnitude;
            return da.CompareTo(db);
        });

        // --- 観測用リスト作成 ---
        bulletTrackedPositions.Clear();
        for (int i = 0; i < bulletTrackCount; i++)
        {
            if (i < activeBullets.Count)
            {
                var b = activeBullets[i];
                var h = bulletHistory[b];
                bulletTrackedPositions.Add((h[0], h[1], h[2]));
            }
            else
            {
                bulletTrackedPositions.Add(
                    (bulletInactivePos, bulletInactivePos, bulletInactivePos)
                );
            }
        }

        // --- 弾の観測登録 ---
        if (isBulletMissing == false)
        {
            foreach (var (now, prev1, prev2) in bulletTrackedPositions)
            {
                Vector2 relNow = now - (Vector2)playerPos;
                Vector2 relPrev1 = (now - prev1) / Time.fixedDeltaTime;
                Vector2 relPrev2 = (prev1 - prev2) / Time.fixedDeltaTime;

                vectorSensor.AddObservation(relNow.x / 3.0f);
                vectorSensor.AddObservation(relNow.y / 2.5f);
                vectorSensor.AddObservation(Mathf.Clamp(relPrev1.x / 3.0f * 10f, -2f, 2f));
                vectorSensor.AddObservation(Mathf.Clamp(relPrev1.y / 2.5f * 10f, -2f, 2f));
                // vectorSensor.AddObservation(relPrev2.x);
                // vectorSensor.AddObservation(relPrev2.y);
            }
        }
        else
        {
            foreach (var (now, prev1, prev2) in bulletTrackedPositions)
            {
                Vector2 relNow = (Vector2)playerPos;
                Vector2 relPrev1 = (Vector2)playerPos;
                Vector2 relPrev2 = (Vector2)playerPos;

                vectorSensor.AddObservation(bulletInactivePos.x / 3.0f);
                vectorSensor.AddObservation(bulletInactivePos.y / 2.5f);
                vectorSensor.AddObservation(0f);
                vectorSensor.AddObservation(0f);
                // vectorSensor.AddObservation(relPrev2.x);
                // vectorSensor.AddObservation(relPrev2.y);
            }
        }
    }

    // 行動決定時に呼ばれる
    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        // 低速モードも実装すること

        // 行動：連続値2つ（横・縦）を取得
        float h = Mathf.Clamp(actionBuffers.ContinuousActions[0], -1f, 1f);
        float v = Mathf.Clamp(actionBuffers.ContinuousActions[1], -1f, 1f);

        // 移動
        playerMoveControllerScript.MovePlayer(h, v);

        // ショットするかどうかの値を取得(正の値をショット可とする)
        float rawValue = Mathf.Clamp(actionBuffers.ContinuousActions[2], -1f, 1f);
        bool isShot = false;
        if (rawValue > 0)
        {
            isShot = true;
        }
        else
        {
            isShot = false;
        }
        isShot = true;                                          // 後で絶対消せ(ショット常に出る化モード)
        // ショット
        playerShotControllerScript.ShotTrigger(isShot);

        // 報酬設計
        // 生存報酬 +1.0
        rewardManagerScript.AddReward(RewardType.SurvivalReward);
        // 報酬…被弾(負)、敵に弾を当てる


        // エピソード終了
        /*
        // HPが0のとき
        if (playerBaseScript.CurrentHP <= 0)  
        {
            EndEpisode();

            playerBaseScript.CurrentHP = 3;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        */
        // 開始時間から30秒経ったとき
        //if (gameTimer >= 30.0f) 
        // 学習時
        if (gameManager.StageID == 0) 
        {
            // HPが0になったとき、もしくは60秒が経過したとき
            if (playerBaseScript.CurrentHP <= 0 || gameTimer >= 60.0f)
            {
                /*
                // ゲーム終了を通知
                gameManager.EndGame();

                // エピソードの終了
                EndEpisode();

                gameTimer = 0;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                */
                gameManager.RequestSafeDelayedReset();
            }
        }
        // プレイ時
        else 
        {
            // 60秒が経過したとき
            if (gameTimer >= 60.0f)
            {
                /*
                // ゲーム終了を通知
                gameManager.EndGame();

                // エピソードの終了
                EndEpisode();

                gameTimer = 0;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                */
                gameManager.RequestSafeDelayedReset();
            }
        }
        
        gameTimer += Time.fixedDeltaTime;

        // 手動評価ボタン(後で消すこと)
        if (Input.GetKeyDown(KeyCode.O) == true)
        {
            // 報酬・天祐
            rewardManagerScript.AddReward(RewardType.GraceReward);
        }
        if (Input.GetKeyDown(KeyCode.L) == true)
        {
            // 報酬・神罰
            rewardManagerScript.AddReward(RewardType.PunishPenalty);
        }
    }

    // ヒューリスティックモード
    public override void Heuristic(in ActionBuffers actionBuffers)
    {
        var actionsOut = actionBuffers.ContinuousActions;
        // 移動
        actionsOut[0] = Input.GetAxis("Horizontal");
        actionsOut[1] = Input.GetAxis("Vertical");
        // ショット
        actionsOut[2] = Input.GetKey(KeyCode.Z) ? 1f : -1f; // trueは1、falseは-1を出力する。
        // 低速モード
        //actionsOut[3] = Input.GetKey(KeyCode.LeftShift) ? 1f : -1f; // trueは1、falseは-1を出力する。
    }

    // ゲーム終了時(ログなどを取る)
    public void EndGame()
    {
        if (isExported == false)
        {
            currentStep = Academy.Instance.StepCount;

            // CSVにHP・報酬・状態を出力
            learningLogCSVScript.UpdateData(currentStep, gameTimer, playerBaseScript.CurrentHP, "Training");

            // Tensorに出力(学習中のみ)
            if (behaviorParameters.BehaviorType == BehaviorType.Default)
            {
                Academy.Instance.StatsRecorder.Add("Player/GameTimer", gameTimer);
                Academy.Instance.StatsRecorder.Add("Player/CurrentHP", playerBaseScript.CurrentHP);
                Academy.Instance.StatsRecorder.Add("Player/RewardSum", rewardManager.RewardSum);
                Academy.Instance.StatsRecorder.Add("Player/AttackReward", rewardManager.GetRewardCount(RewardType.AttackReward));
                Academy.Instance.StatsRecorder.Add("Player/KillReward", rewardManager.GetRewardCount(RewardType.KillReward));
                Academy.Instance.StatsRecorder.Add("Player/SurvivalReward", rewardManager.GetRewardCount(RewardType.SurvivalReward));
                Academy.Instance.StatsRecorder.Add("Player/GrazePenalty", rewardManager.GetRewardCount(RewardType.GrazePenalty));
                Academy.Instance.StatsRecorder.Add("Player/HitPenalty", rewardManager.GetRewardCount(RewardType.HitPenalty));
                Academy.Instance.StatsRecorder.Add("Player/PunishPenalty", rewardManager.GetRewardCount(RewardType.PunishPenalty));
                Academy.Instance.StatsRecorder.Add("Player/GraceReward", rewardManager.GetRewardCount(RewardType.GraceReward));

                // ActionLog関係
                // System
                Academy.Instance.StatsRecorder.Add("Player/EnemyCount", ActionLogController.Instance.EnemyCount);
                // Total
                Academy.Instance.StatsRecorder.Add("Player/T_Score", ActionLogController.Instance.ActionLog_Score_Total);

                Academy.Instance.StatsRecorder.Add("Player/T_attackedCount", ActionLogController.Instance.StatsTotal.attackedCount);
                Academy.Instance.StatsRecorder.Add("Player/T_killedCount", ActionLogController.Instance.StatsTotal.killedCount);
                Academy.Instance.StatsRecorder.Add("Player/T_taskAssigneeCount", ActionLogController.Instance.StatsTotal.taskAssigneeCount);
                Academy.Instance.StatsRecorder.Add("Player/T_taskAssigneeKilldedCount", ActionLogController.Instance.StatsTotal.taskAssigneeKilldedCount);
                Academy.Instance.StatsRecorder.Add("Player/T_nonTaskAssigneeKilldedCount", ActionLogController.Instance.StatsTotal.nonTaskAssigneeKilldedCount);
                Academy.Instance.StatsRecorder.Add("Player/T_escapedCount", ActionLogController.Instance.StatsTotal.escapedCount);
                Academy.Instance.StatsRecorder.Add("Player/T_escapedPartnerKilledSelf", ActionLogController.Instance.StatsTotal.escapedPartnerKilledSelf);
                Academy.Instance.StatsRecorder.Add("Player/T_escapedSelfKilledSelf", ActionLogController.Instance.StatsTotal.escapedSelfKilledSelf);

                // 0除算防止
                if (_totalStatFrames > 0)
                {
                    var stats = Academy.Instance.StatsRecorder;

                    // A. 9項目の詳細出力 (CSV分析用)
                    string[] xNames = { "Left", "Center", "Right" };
                    string[] yNames = { "Bottom", "Middle", "Top" };

                    for (int x = 0; x < 3; x++)
                    {
                        for (int y = 0; y < 3; y++)
                        {
                            float rate = (float)_gridCounts[x, y] / _totalStatFrames;
                            // 例: Player/Grid_Left_Top
                            stats.Add($"{statsNamePrefix}/Grid_{xNames[x]}_{yNames[y]}", rate);
                        }
                    }

                    // B. 6項目のまとめ出力 (TensorBoard用)
                    //    9つのマスを足し算して算出する

                    // Y軸の集計（横一列を足す：Bottomなら (0,0)+(1,0)+(2,0) ）
                    float stayBottom = (float)(_gridCounts[0, 0] + _gridCounts[1, 0] + _gridCounts[2, 0]) / _totalStatFrames;
                    float stayMiddleY = (float)(_gridCounts[0, 1] + _gridCounts[1, 1] + _gridCounts[2, 1]) / _totalStatFrames;
                    float stayTop = (float)(_gridCounts[0, 2] + _gridCounts[1, 2] + _gridCounts[2, 2]) / _totalStatFrames;

                    stats.Add($"{statsNamePrefix}/Pos_Bottom", stayBottom);
                    stats.Add($"{statsNamePrefix}/Pos_Middle_Y", stayMiddleY);
                    stats.Add($"{statsNamePrefix}/Pos_Top", stayTop);

                    // X軸の集計（縦一列を足す）
                    float stayLeft = (float)(_gridCounts[0, 0] + _gridCounts[0, 1] + _gridCounts[0, 2]) / _totalStatFrames;
                    float stayCenterX = (float)(_gridCounts[1, 0] + _gridCounts[1, 1] + _gridCounts[1, 2]) / _totalStatFrames;
                    float stayRight = (float)(_gridCounts[2, 0] + _gridCounts[2, 1] + _gridCounts[2, 2]) / _totalStatFrames;

                    stats.Add($"{statsNamePrefix}/Pos_Left", stayLeft);
                    stats.Add($"{statsNamePrefix}/Pos_Center_X", stayCenterX);
                    stats.Add($"{statsNamePrefix}/Pos_Right", stayRight);
                }
                // --- カウンターのリセット ---
                _gridCounts = new int[3, 3];
                _totalStatFrames = 0;
            }

            isExported = true;
        }
    }

    // シーンを跨ぐ用：毎回呼ぶ「リンク再接続」処理
    private bool TryAcquireReferences()
    {
        // 1. プレイヤーの生存確認・ID整合性チェック
        if (playerBaseScript != null && playerBaseScript.gameObject != null)
        {
            // 違うIDのプレイヤーを操作してしまっていたらリセット
            if (targetPlayerID != -1 && playerBaseScript.PlayerID != targetPlayerID)
            {
                playerBaseScript = null;
            }
            else
            {
                return true; // 正常にリンクされているので終了
            }
        }

        // プレイヤーの特定（ID重視）
        GameObject parentObj = GameObject.Find("PlayerParent");
        if (parentObj == null) return false; // まだロードされていない

        // 親オブジェクトの参照も更新しておく（GetPartnerPlayerで使うため）
        playerParentObj = parentObj;

        // 親の子供の中からPlayerBaseをすべて取得（非アクティブ含む）
        PlayerBase foundPlayer = null;
        PlayerBase[] allPlayers = parentObj.GetComponentsInChildren<PlayerBase>(true);

        foreach (var p in allPlayers)
        {
            // 【初回】IDがまだ決まっていない時
            if (targetPlayerID == -1)
            {
                // 最初からアクティブになっている機体を「自分の機体」と決める
                if (p.gameObject.activeInHierarchy)
                {
                    targetPlayerID = p.PlayerID; // IDを登録！
                    foundPlayer = p;
                    break;
                }
            }
            // 【2回目以降】記憶したIDと同じ機体を探す
            else
            {
                if (p.PlayerID == targetPlayerID)
                {
                    foundPlayer = p;
                    break;
                }
            }
        }

        // 見つからなければ今回は見送り
        if (foundPlayer == null) return false;

        // 3. コンポーネントのリンクと初期化（ここが抜けていました！）
        playerBaseScript = foundPlayer;
        playerMoveControllerScript = foundPlayer.GetComponent<PlayerMoveController>();
        playerShotControllerScript = foundPlayer.GetComponent<PlayerShotController>();

        // 学習中のみ(利き手現象の対策)
        if (behaviorParameters.BehaviorType == BehaviorType.Default)
        {
            playerMoveControllerScript.isDefaultBehavior = true;
        }

        // 4. マネージャー類の再取得
        if (rewardManagerScript == null)
            rewardManagerScript = FindObjectOfType<RewardManager>();

        if (gameManager == null)
            gameManager = FindObjectOfType<GameManager>();

        if (learningLogCSVScript == null)
            learningLogCSVScript = FindObjectOfType<LearningLogCSV>();

        // 弾や敵の親
        if (bulletParent == null)
        {
            GameObject bp = GameObject.Find("BulletParent");
            if (bp != null) bulletParent = bp.transform;
        }
        if (enemyParent == null)
        {
            GameObject ep = GameObject.Find("EnemyParent");
            if (ep != null) enemyParent = ep.transform;
        }

        // 履歴のリセット（シーン遷移直後なのでクリア）
        bulletHistory.Clear();
        enemyHistory.Clear();

        return true; // 接続成功
    }
}