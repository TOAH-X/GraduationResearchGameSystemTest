using UnityEngine;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine.SceneManagement;
using System.Threading;
using Unity.MLAgents.Policies;
using System.Data;
using DG.Tweening.Core.Easing;

public class SubPlayerAgent : Agent
{
    Rigidbody2D rb2D;

    [SerializeField] PlayerBase playerBaseScript;                           // PlayerBaseスクリプト
    [SerializeField] PlayerMoveController playerMoveControllerScript;       // PlayerMoveControllerスクリプト
    [SerializeField] PlayerShotController playerShotControllerScript;       // PlayerShotControllerスクリプト

    [SerializeField] Transform bulletParent;                                // BulletParentのTransform
    [SerializeField] Transform enemyParent;                                 // EnemyParentのTransform

    [SerializeField] bool isLimitedCollectObservations = true;              // 観察数の制限(弾幕を全て把握から一部把握へ変更)

    [SerializeField] RewardManager rewardManagerScript;                     // rewardManagerスクリプト

    [SerializeField] float gameTimer = 0;                                   // ゲームタイマー
    public int currentStep = 0;                                             // 出力用ステップ数管理(やり直しても管理できるようにするため)
    [SerializeField] private LearningLogCSV learningLogCSVScript;           // LearningLogスクリプト

    [SerializeField] RewardManager rewardManager;
    [SerializeField] GameManager gameManager;

    [SerializeField] BehaviorParameters behaviorParameters;

    private bool isExported = false;                                        // CSVを出力済みか(EndGameが呼ばれたかどうかで判別するので厳密ではない)

    // 追加
    //[Header("基準となるプレイヤー")]
    //[SerializeField] private Transform player;

    [Header("追跡する弾の最大数")]
    [SerializeField] private int bulletTrackCount = 30;

    [Header("非アクティブ時のダミー座標")]
    [SerializeField] private Vector2 bulletInactivePos = new Vector2(-100f, -100f);

    // 弾の履歴データ（現在・1F前・2F前）
    private readonly Dictionary<Transform, Vector2[]> bulletHistory = new Dictionary<Transform, Vector2[]>();

    // ML用など他スクリプトで参照したい場合に使う
    public List<(Vector2 now, Vector2 prev1, Vector2 prev2)> bulletTrackedPositions = new List<(Vector2, Vector2, Vector2)>();

    // 追加

    // 追加
    [Header("追跡する敵の最大数")]
    [SerializeField] private int enemyTrackCount = 25;

    [Header("非アクティブ時のダミー座標")]
    [SerializeField] private Vector2 enemyInactivePos = new Vector2(-100f, -100f);

    // 弾の履歴データ（現在・1F前・2F前）
    private readonly Dictionary<Transform, Vector2[]> enemyHistory = new Dictionary<Transform, Vector2[]>();

    // ML用など他スクリプトで参照したい場合に使う
    public List<(Vector2 now, Vector2 prev1, Vector2 prev2)> enemyTrackedPositions = new List<(Vector2, Vector2, Vector2)>();
    // 追加

    // SubPlayer用
    // Player位置座標
    [SerializeField] GameObject partnerPlayerObj;           // 僚機
    [SerializeField] GameObject playerParentObj;

    // 位置情報ログ(機能を移すこと)
    [Header("学習用ログ設定")]
    [SerializeField] private float statsBorderX = 6.0f;                 // 左右の境界線(12なので)
    [SerializeField] private float statsBorderY = 5.0f;                 // 上下の境界線(10なので)
    [SerializeField] private string statsNamePrefix = "SubPlayer";      // ログの頭につく名前（PlayerやSubPlayerに変更）
    private int[,] _gridCounts = new int[3, 3];                         // 9分割カウント用 [x, y]
    private int _totalStatFrames = 0;                                   // 9分割カウント用 [x, y]

    // 僚機との位置関係
    private float _cumulativeDistance = 0f;    // 距離の合計
    private int _spreadFrames = 0;             // 展開（左右分散）できていたフレーム数
    [SerializeField] private float spreadThreshold = 2.0f;              // 展開判定の境界線(12なので)

    private void Awake()
    {
        behaviorParameters = GetComponent<BehaviorParameters>();

        // 学習中のみ(利き手現象の対策)
        if (behaviorParameters.BehaviorType == BehaviorType.Default)
        {
            playerMoveControllerScript.isDefaultBehavior = true;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        // 僚機を取得
        GetPartnerPlayer();

        // PlayerIDの合計が偶数の場合はバグが起きるので防止
        if ((this.gameObject.GetComponent<PlayerBase>().PlayerID + partnerPlayerObj.GetComponentInParent<PlayerBase>().PlayerID) % 2 == 0)
        {
            Debug.LogError("プレイヤーとサブプレイヤーの組み合わせが無効");
        }
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void FixedUpdate()
    {
        // 自分の位置を取得
        Vector3 pos = transform.position;

        // どのマスにいるか判定 (0:左/下, 1:中, 2:右/上)
        int xIndex = (pos.x < -statsBorderX) ? 0 : (pos.x > statsBorderX ? 2 : 1);
        int yIndex = (pos.y < -statsBorderY) ? 0 : (pos.y > statsBorderY ? 2 : 1);

        // カウントアップ
        _gridCounts[xIndex, yIndex]++;
        _totalStatFrames++;

        // 学習モード（Default）かつ、僚機がいるときだけ計測する
        if (behaviorParameters.BehaviorType == BehaviorType.Default)
        {
            // 僚機がまだ取得できていない、または死んでいる場合は計測しない
            if (partnerPlayerObj == null) return;
            if (partnerPlayerObj.activeSelf == false) return;

            // --- 計測ロジック ---
            _totalStatFrames++;

            // 1. 位置情報のグリッド集計
            // 座標を 0.0~1.0 に正規化 (-statsBorderX ~ +statsBorderX を想定)
            float normX = Mathf.InverseLerp(-statsBorderX, statsBorderX, transform.position.x);
            float normY = Mathf.InverseLerp(-statsBorderY, statsBorderY, transform.position.y);

            // 0~2 のインデックスに変換
            int gridX = Mathf.FloorToInt(Mathf.Clamp(normX * 3f, 0, 2.99f));
            int gridY = Mathf.FloorToInt(Mathf.Clamp(normY * 3f, 0, 2.99f));

            _gridCounts[gridX, gridY]++;

            // 2. 僚機との関係性集計
            // A. 距離の積算
            float dist = Vector3.Distance(this.transform.position, partnerPlayerObj.transform.position);
            _cumulativeDistance += dist;

            // B. 展開率 (Spread) の判定
            // 自分のエリア判定 (-1:左, 0:中, 1:右)
            int myArea = 0;
            if (transform.position.x < -spreadThreshold) myArea = -1;
            else if (transform.position.x > spreadThreshold) myArea = 1;

            // 僚機のエリア判定
            int subArea = 0;
            float subX = partnerPlayerObj.transform.position.x;
            if (subX < -spreadThreshold) subArea = -1;
            else if (subX > spreadThreshold) subArea = 1;

            // 「僚機が左で、自分が左以外」 または 「僚機が右で、自分が右以外」なら展開成功
            bool isSpread = (subArea == -1 && myArea > -1) || (subArea == 1 && myArea < 1);

            if (isSpread)
            {
                _spreadFrames++;
            }
        }
    }

    // ゲームオブジェクト生成時に呼ばれる
    public override void Initialize()
    {
        base.Initialize();
        behaviorParameters = GetComponent<BehaviorParameters>();

        this.rb2D = GetComponent<Rigidbody2D>();
    }

    // エピソード開始時に呼ばれる
    public override void OnEpisodeBegin()
    {
        // リロード
        //SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // 観察取得時に呼ばれる
    public override void CollectObservations(VectorSensor vectorSensor)//posかも？
    {
        if (this == null || this.transform.position == null)  
        {
            return;
        }

        // 情報欠落(エラー。埋める用)
        bool isBulletMissing = false;
        bool isEnemyMissing = false;
        bool isPartnerMissing = false;

        // 僚機の取得
        if (partnerPlayerObj == null)
        {
            GetPartnerPlayer();
        }

        // 1*2(サブプレイヤー)+1*2(プレイヤー)+25*2*2(敵)+30*2*2(弾)=224
        if (bulletParent == null) 
        {
            isBulletMissing = true;
        }
        if (enemyParent == null) 
        {
            isEnemyMissing = true;
        }
        if (partnerPlayerObj == null) 
        { 
            isPartnerMissing = true;
        }

        // 自身の位置を取得
        vectorSensor.AddObservation(transform.position.x);
        vectorSensor.AddObservation(transform.position.y);


        // パートナープレイヤー(僚機)の位置の取得
        if (isPartnerMissing == false)
        {
            vectorSensor.AddObservation(partnerPlayerObj.transform.position.x - transform.position.x);
            vectorSensor.AddObservation(partnerPlayerObj.transform.position.y - transform.position.y);
        }
        else 
        {
            vectorSensor.AddObservation(0f);
            vectorSensor.AddObservation(0f);
        }

        // --- アクティブな敵を取得 ---
        List<Transform> activeEnemies = new List<Transform>();
        if (isBulletMissing == false) 
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
            if (kvp.Key == null)
                enemyRemoveList.Add(kvp.Key);
        }
        foreach (var key in enemyRemoveList)
        {
            enemyHistory.Remove(key);
        }

        // --- プレイヤーに近い順に並び替え（観測専用） ---
        activeEnemies.Sort((a, b) =>
        {
            float da = (a.position - this.transform.position).sqrMagnitude;
            float db = (b.position - this.transform.position).sqrMagnitude;
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
                Vector2 relNow = now - (Vector2)this.transform.position;
                Vector2 relPrev1 = prev1 - (Vector2)this.transform.position;
                Vector2 relPrev2 = prev2 - (Vector2)this.transform.position;

                vectorSensor.AddObservation(relNow.x);
                vectorSensor.AddObservation(relNow.y);
                vectorSensor.AddObservation(relPrev1.x);
                vectorSensor.AddObservation(relPrev1.y);
                // vectorSensor.AddObservation(relPrev2.x);
                // vectorSensor.AddObservation(relPrev2.y);
            }
        }
        else 
        {
            foreach (var (now, prev1, prev2) in enemyTrackedPositions)
            {
                Vector2 relNow = (Vector2)this.transform.position;
                Vector2 relPrev1 = (Vector2)this.transform.position;
                Vector2 relPrev2 = (Vector2)this.transform.position;

                vectorSensor.AddObservation(0f);
                vectorSensor.AddObservation(0f);
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
            if (kvp.Key == null)
                bulletRemoveList.Add(kvp.Key);
        }
        foreach (var key in bulletRemoveList)
        {
            bulletHistory.Remove(key);
        }

        // --- プレイヤーに近い順 ---
        activeBullets.Sort((a, b) =>
        {
            float da = (a.position - this.transform.position).sqrMagnitude;
            float db = (b.position - this.transform.position).sqrMagnitude;
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
                Vector2 relNow = now - (Vector2)this.transform.position;
                Vector2 relPrev1 = prev1 - (Vector2)this.transform.position;
                Vector2 relPrev2 = prev2 - (Vector2)this.transform.position;

                vectorSensor.AddObservation(relNow.x);
                vectorSensor.AddObservation(relNow.y);
                vectorSensor.AddObservation(relPrev1.x);
                vectorSensor.AddObservation(relPrev1.y);
                // vectorSensor.AddObservation(relPrev2.x);
                // vectorSensor.AddObservation(relPrev2.y);
            }
        }
        else 
        {
            foreach (var (now, prev1, prev2) in bulletTrackedPositions)
            {
                Vector2 relNow = (Vector2)this.transform.position;
                Vector2 relPrev1 = (Vector2)this.transform.position;
                Vector2 relPrev2 = (Vector2)this.transform.position;

                vectorSensor.AddObservation(0f);
                vectorSensor.AddObservation(0f);
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
        // if (gameTimer >= 30.0f)
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
                Academy.Instance.StatsRecorder.Add("SubPlayer/GameTimer", gameTimer);
                Academy.Instance.StatsRecorder.Add("SubPlayer/CurrentHP", playerBaseScript.CurrentHP);
                Academy.Instance.StatsRecorder.Add("SubPlayer/RewardSum", rewardManager.RewardSum);
                Academy.Instance.StatsRecorder.Add("SubPlayer/AttackReward", rewardManager.GetRewardCount(RewardType.AttackReward));
                Academy.Instance.StatsRecorder.Add("SubPlayer/SurvivalReward", rewardManager.GetRewardCount(RewardType.KillReward));
                Academy.Instance.StatsRecorder.Add("SubPlayer/GrazePenalty", rewardManager.GetRewardCount(RewardType.SurvivalReward));
                Academy.Instance.StatsRecorder.Add("SubPlayer/GrazePenalty", rewardManager.GetRewardCount(RewardType.GrazePenalty));
                Academy.Instance.StatsRecorder.Add("SubPlayer/HitPenalty", rewardManager.GetRewardCount(RewardType.HitPenalty));
                Academy.Instance.StatsRecorder.Add("SubPlayer/PunishPenalty", rewardManager.GetRewardCount(RewardType.PunishPenalty));
                Academy.Instance.StatsRecorder.Add("SubPlayer/GraceReward", rewardManager.GetRewardCount(RewardType.GraceReward));

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

                    if (partnerPlayerObj != null)
                    {
                        // 1. 平均距離
                        float avgDist = _cumulativeDistance / _totalStatFrames;
                        stats.Add($"{statsNamePrefix}/Relationship_Avg_Distance", avgDist);

                        // 2. 展開率 (Spread Rate)
                        float spreadRate = (float)_spreadFrames / _totalStatFrames;
                        stats.Add($"{statsNamePrefix}/Relationship_Spread_Rate", spreadRate);
                    }
                }
                // --- カウンターのリセット ---
                _gridCounts = new int[3, 3];
                _totalStatFrames = 0;

                // 僚機との位置関係のリセット
                _cumulativeDistance = 0f;
                _spreadFrames = 0;

                // 僚機
                Academy.Instance.StatsRecorder.Add("SubPlayer/PartnerCurrentHP", partnerPlayerObj.GetComponent<PlayerBase>().CurrentHP);
            }

            isExported = true;
        }
    }

    // 僚機の取得
    public void GetPartnerPlayer() 
    {
        foreach (Transform child in playerParentObj.transform)
        {
            if (child.gameObject.activeSelf)
            {
                if (child.gameObject.GetComponent<PlayerBase>().PlayerID != playerBaseScript.PlayerID)
                {
                    partnerPlayerObj = child.gameObject;
                    Debug.Log("アクティブな子オブジェクト: " + child.name);
                }
            }
        }
    }
}
