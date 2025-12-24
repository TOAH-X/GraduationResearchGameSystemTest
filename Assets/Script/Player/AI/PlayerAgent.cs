using UnityEngine;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine.SceneManagement;
using System.Threading;
using Unity.MLAgents.Policies;

public class PlayerAgent : Agent
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
    [SerializeField] private LearningLogCSV learningLogCSVScript;           // LearningLogCSVスクリプト

    [SerializeField] RewardManager rewardManager;
    [SerializeField] GameManager gameManager;

    BehaviorParameters behaviorParameters;

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

    private void Awake()
    {
        behaviorParameters = GetComponent<BehaviorParameters>();

        // 学習中のみ(利き手現象の対策)
        if (behaviorParameters.BehaviorType == BehaviorType.Default)
        {
            float noisePosX = UnityEngine.Random.Range(-4.0f, 4.0f);
            this.transform.position = new Vector2(noisePosX, 0);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    
    // ゲームオブジェクト生成時に呼ばれる
    public override void Initialize() 
    {
        this.rb2D = GetComponent<Rigidbody2D>();
    }

    // エピソード開始時に呼ばれる
    public override void OnEpisodeBegin() 
    {
        
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

        // 1*2(プレイヤー)+25*2*2(敵)+30*2*2(弾)=222
        if (bulletParent == null)
        {
            isBulletMissing = true;
        }
        if (enemyParent == null)
        {
            isEnemyMissing = true;
        }

        // 自身の位置を取得
        vectorSensor.AddObservation(transform.position.x);
        vectorSensor.AddObservation(transform.position.y);

        /*
        // 敵取得
        // 近い順
        if (enemyParent == null || player == null) return;

        // --- 敵リストを更新 ---
        List<Transform> enemys = new List<Transform>();
        for (int i = 0; i < enemyParent.childCount; i++)
        {
            enemys.Add(enemyParent.GetChild(i));
        }

        // --- アクティブな敵を抽出 ---
        List<Transform> activeEnemys = new List<Transform>();
        foreach (var b in enemys)
        {
            if (b != null && b.gameObject.activeSelf)
                activeEnemys.Add(b);
        }

        // --- プレイヤーに近い順に並び替え ---
        activeEnemys.Sort((a, b) =>
        {
            float da = (a.position - player.position).sqrMagnitude;
            float db = (b.position - player.position).sqrMagnitude;
            return da.CompareTo(db);
        });

        // --- trackCount件に制限し、不足分をnullで埋める ---
        List<Transform> trackedEnemys = new List<Transform>();
        for (int i = 0; i < enemyTrackCount; i++)
        {
            trackedEnemys.Add(i < activeEnemys.Count ? activeEnemys[i] : null);
        }

        // --- 敵の履歴更新 ---
        foreach (var enemy in trackedEnemys)
        {
            if (enemy == null) continue;

            if (!enemyHistory.ContainsKey(enemy))
            {
                enemyHistory[enemy] = new Vector2[3]
                {
                    enemyInactivePos,
                    enemyInactivePos,
                    enemyInactivePos
                };
            }

            var h = enemyHistory[enemy];
            if (enemy.gameObject.activeSelf)
            {
                h[2] = h[1];
                h[1] = h[0];
                h[0] = enemy.position;
            }
            else
            {
                h[2] = h[1];
                h[1] = h[0];
                h[0] = enemyInactivePos;
            }
        }

        // --- 削除された敵の履歴をクリア ---
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

        // --- 最終的なリストを作成 ---
        enemyTrackedPositions.Clear();
        int enemyCount = 0;
        foreach (var kvp in enemyHistory)
        {
            if (enemyCount >= enemyTrackCount) break;
            var h = kvp.Value;
            enemyTrackedPositions.Add((h[0], h[1], h[2]));
            enemyCount++;
        }

        // 敵が足りない場合はダミー座標で埋める
        while (enemyTrackedPositions.Count < enemyTrackCount)
        {
            enemyTrackedPositions.Add((enemyInactivePos, enemyInactivePos, enemyInactivePos));
        }

        // --- 観測を登録 ---
        foreach (var (now, prev1, prev2) in enemyTrackedPositions)
        {
            // 相対座標
            // 現在
            Vector2 relNow = now - (Vector2)player.position;
            vectorSensor.AddObservation(relNow.x);
            vectorSensor.AddObservation(relNow.y);

            // 1フレーム前
            Vector2 relPrev1 = prev1 - (Vector2)player.position;
            vectorSensor.AddObservation(relPrev1.x);
            vectorSensor.AddObservation(relPrev1.y);

            // 2フレーム前
            Vector2 relPrev2 = prev2 - (Vector2)player.position;
            vectorSensor.AddObservation(relPrev2.x);
            vectorSensor.AddObservation(relPrev2.y);
        }

        // 弾取得
        // 近い順
        if (bulletParent == null || player == null) return;

        // --- 弾リストを更新 ---
        List<Transform> bullets = new List<Transform>();
        for (int i = 0; i < bulletParent.childCount; i++)
        {
            bullets.Add(bulletParent.GetChild(i));
        }

        // --- アクティブな弾を抽出 ---
        List<Transform> activeBullets = new List<Transform>();
        foreach (var b in bullets)
        {
            if (b != null && b.gameObject.activeSelf)
                activeBullets.Add(b);
        }

        // --- プレイヤーに近い順に並び替え ---
        activeBullets.Sort((a, b) =>
        {
            float da = (a.position - player.position).sqrMagnitude;
            float db = (b.position - player.position).sqrMagnitude;
            return da.CompareTo(db);
        });

        // --- trackCount件に制限し、不足分をnullで埋める ---
        List<Transform> trackedBullets = new List<Transform>();
        for (int i = 0; i < bulletTrackCount; i++)
        {
            trackedBullets.Add(i < activeBullets.Count ? activeBullets[i] : null);
        }

        // --- 弾の履歴更新 ---
        foreach (var bullet in trackedBullets)
        {
            if (bullet == null) continue;

            if (!bulletHistory.ContainsKey(bullet))
            {
                bulletHistory[bullet] = new Vector2[3]
                {
                    bulletInactivePos,
                    bulletInactivePos,
                    bulletInactivePos
                };
            }

            var h = bulletHistory[bullet];
            if (bullet.gameObject.activeSelf)
            {
                h[2] = h[1];
                h[1] = h[0];
                h[0] = bullet.position;
            }
            else
            {
                h[2] = h[1];
                h[1] = h[0];
                h[0] = bulletInactivePos;
            }
        }

        // --- 削除された弾の履歴をクリア ---
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

        // --- 最終的なリストを作成 ---
        bulletTrackedPositions.Clear();
        int bulletCount = 0;
        foreach (var kvp in bulletHistory)
        {
            if (bulletCount >= bulletTrackCount) break;
            var h = kvp.Value;
            bulletTrackedPositions.Add((h[0], h[1], h[2]));
            bulletCount++;
        }

        // 弾が足りない場合はダミー座標で埋める
        while (bulletTrackedPositions.Count < bulletTrackCount)
        {
            bulletTrackedPositions.Add((bulletInactivePos, bulletInactivePos, bulletInactivePos));
        }
        */
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

        /*
        // --- 観測を登録 ---
        foreach (var (now, prev1, prev2) in bulletTrackedPositions)
        {
            // 相対座標
            // 現在
            Vector2 relNow = now - (Vector2)player.position;
            vectorSensor.AddObservation(relNow.x);
            vectorSensor.AddObservation(relNow.y);

            // 1フレーム前
            Vector2 relPrev1 = prev1 - (Vector2)player.position;
            vectorSensor.AddObservation(relPrev1.x);
            vectorSensor.AddObservation(relPrev1.y);

            // 2フレーム前
            Vector2 relPrev2 = prev2 - (Vector2)player.position;
            vectorSensor.AddObservation(relPrev2.x);
            vectorSensor.AddObservation(relPrev2.y);
        }

        // --- 観測を登録 ---
        foreach (var (now, prev1, prev2) in enemyTrackedPositions)
        {
            // 相対座標
            // 現在
            Vector2 relNow = now - (Vector2)player.position;
            vectorSensor.AddObservation(relNow.x);
            vectorSensor.AddObservation(relNow.y);

            // 1フレーム前
            Vector2 relPrev1 = prev1 - (Vector2)player.position;
            vectorSensor.AddObservation(relPrev1.x);
            vectorSensor.AddObservation(relPrev1.y);

            // 2フレーム前
            Vector2 relPrev2 = prev2 - (Vector2)player.position;
            vectorSensor.AddObservation(relPrev2.x);
            vectorSensor.AddObservation(relPrev2.y);
        }
        */
        /*
        // ID順

        if (bulletParent == null || enemyParent == null) return;
        // 自身の位置を取得
        vectorSensor.AddObservation(transform.position);
        //vectorSensor.AddObservation(transform.position.y);

        // 弾幕を全て取得
        if (isLimitedCollectObservations == false)
        {
            // 1フレーム前の弾幕の座標の取得
            for (int i = 0; i < bulletParent.childCount; i++)
            {
                Vector2 lastPos = (i < lastFrameBulletPositions.Count) ? lastFrameBulletPositions[i] : new Vector2(100f, 100f);

                vectorSensor.AddObservation(lastPos);
                //vectorSensor.AddObservation(lastPos.y);
            }

            // 現在の弾幕の座標の取得
            lastFrameBulletPositions.Clear();

            for (int i = 0; i < bulletParent.childCount; i++)
            {
                Transform bullet = bulletParent.GetChild(i);


                Vector2 relativePos;

                if (bullet.gameObject.activeSelf)
                {
                    relativePos = bullet.position - transform.position;
                }
                else
                {
                    relativePos = new Vector2(100f, 100f);      // 居ないものとして扱う
                }

                lastFrameBulletPositions.Add(relativePos);      // 弾幕の1フレーム前の座標リストに現在座標を登録

                vectorSensor.AddObservation(relativePos);
                //vectorSensor.AddObservation(relativePos.y);
            }
        }
        // 弾幕を近くのものだけ取得
        else 
        { 
            // 最も近いn個の弾幕を取得
            // 順番は守る
            // 最も近いn個に入ると最も遠い弾幕と交換してリストに入れる
        }



        // 敵の座標の取得(のちのち1フレーム前の座標も取得しておくと精度が上がるかもしれない)
        int maxEnemysToObserve = 100;     // エネミープーラから取得すること
        int count2 = 0;

        foreach (Transform enemy in enemyParent)
        {
            if (enemy.gameObject.activeSelf)
            {
                Vector2 relativePos = enemy.position - transform.position; // 相対位置
                vectorSensor.AddObservation(relativePos);
                //vectorSensor.AddObservation(relativePos.y);
                count2++;

                if (count2 >= maxEnemysToObserve)
                    break;
            }
        }

        // 観測数が max に満たない場合、座標を(100,100)にしておく。
        for (int i = count2; i < maxEnemysToObserve; i++)
        {
            vectorSensor.AddObservation(new Vector2(100f, 100f)); // x
            //vectorSensor.AddObservation(100f); // y
        }

        */
        /*
        // 弾幕の座標の取得(のちのち1フレーム前の座標も取得しておくと精度が上がるかもしれない)
        int maxBulletsToObserve = 1000;     // バレットプーラから取得すること
        int count = 0;

        foreach (Transform bullet in bulletParent)
        {
            if (bullet.gameObject.activeSelf)
            {
                Vector2 relativePos = bullet.position - transform.position; // 相対位置でもOK
                vectorSensor.AddObservation(relativePos.x);
                vectorSensor.AddObservation(relativePos.y);
                count++;

                if (count >= maxBulletsToObserve)
                    break;
            }
        }

        // 観測数が max に満たない場合、ゼロ埋め
        for (int i = count; i < maxBulletsToObserve; i++)
        {
            vectorSensor.AddObservation(0f); // x
            vectorSensor.AddObservation(0f); // y
        }

        */
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
        if (gameTimer >= 30.0f) 
        {
            // ゲーム終了を通知
            gameManager.EndGame();

            // エピソードの終了
            EndEpisode();

            gameTimer = 0;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
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
        currentStep += 1000;

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
        }

        // アクションログに出力
        ActionLogController.Instance.ExportData();
    }
}
