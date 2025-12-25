/*
using UnityEngine;

public class EnemyBase : MonoBehaviour
{
    [Header("エネミーID")]
    [SerializeField] int enemyID = 0;
    [Header("最大HP")]
    [SerializeField] int maxHp = 1;
    [Header("現在HP")]
    [SerializeField] int currentHp;
    [Header("アタックID")]  // 最新の攻撃者(プレイヤー)のID
    [SerializeField] int attackID;

    [Header("弾幕プレハブ")]
    [SerializeField] GameObject bulletPrefab;
    [Header("弾幕格納先(親)")]
    [SerializeField] GameObject bulletParentObj;
    private float timer = 0;

    private float lifeTimer = 0;

    // 画面外の範囲
    float minX = -10.0f;
    float maxX = 10.0f;
    float minY = -6.0f;
    float maxY = 6.0f;

    // Start is called before the first frame update
    void Start()
    {
        // 無くて良い
        // ResetEnemy();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        lifeTimer += Time.fixedDeltaTime;

        // 移動       //他スクリプトに分けること
        Move();

        timer += Time.fixedDeltaTime;
        if (timer >= 1.0f) 
        {
            float num = Random.Range(0.0f, 360.0f);
            for (int i = 0; i < 4; i++) 
            {

                //弾幕発射      //他スクリプトに分けること
                LaunchBullet(new Vector3(0f, 0f), 2f, i * 360 / 4f + num);      // ランダム弾
                //LaunchBullet(new Vector3(0f, 0f), 2f, i * 36f);               // 放射弾
            }
            timer = 0;
        }

        // 画面外処理
        Vector3 pos = transform.position;
        if (pos.x < minX || pos.x > maxX || pos.y < minY || pos.y > maxY)
        {
            // ★ Death() に理由を渡す
            //Death("Screen Boundary");
        }
    }

    /// <summary>
    /// 被ダメージ処理
    /// </summary>
    /// <param name="damage">ダメージ量</param>
    /// <param name="id">自身(敵)にダメージを与えたプレイヤーID</param>/// <summary>
    /// <returns>死亡したら1を返す</returns>
    public int Damage(int damage, int id)
    {
        currentHp -= damage;
        attackID = id;
        // 敵の被弾、スコアを加算
        //ScoreSystemController.Instance.UpdateScore(1, attackID);
        if (currentHp <= 0)
        {
            // 敵の死亡
            ScoreSystemController.Instance.UpdateScore(2, attackID);

            // 死亡エフェクト再生
            EnemyDeathEffect.Instance.PlayEffect(this.transform.position);
            // 死亡音再生
            SoundManager.PlaySE(SEType.EnemyDeath);

            // ★ Death() に理由を渡す
            Death("HP Depleted (Current HP: " + currentHp + ")");
            return 1;
        }
        return 0;
    }

    // 死亡処理
    public void Death(string reason = "Unknown")
    {
        // ★★★ 修正箇所：死亡時のHPと理由をログに出力 ★★★
        Debug.LogError($"[ENEMY DEATH] ID: {enemyID}. Reason: {reason}. Current HP at Death: {currentHp}.");

        EnemySpawnerController.Instance.ReturnEnemyToPool(gameObject);
    }

    // 移動(継承して別スクリプトにすること)
    public void Move()
    {
        if (enemyID == 1)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(0.5f, -0.25f);      //右下
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
        }
        else if (enemyID == 2)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(-0.5f, -0.25f);     //左下
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
        }
        else if (enemyID == 3)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(0.5f, 0.25f);       //右上
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
        }
        else if (enemyID == 4)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(-0.5f, 0.25f);       //左上
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
        }
        else if (enemyID == 5)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(0, -0.25f);       //下
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
        }
        else if (enemyID == 6)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(0f, -0.25f);       //下
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
        }
        else if (enemyID == 7)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(0.2f, -0.4f);       //右下
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
        }
        else if (enemyID == 8)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(-0.2f, -0.4f);       //左下
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
        }

        //
        Vector3 pos = transform.position;
        if (pos.x < minX || pos.x > maxX || pos.y < minY || pos.y > maxY)
        {
            //Debug.LogError($"Enemy {enemyID} out of range at {pos}");
        }
    }

    public void ResetEnemy()
    {
        currentHp = maxHp;
        timer = 0;

        lifeTimer = 0;

        //transform.localPosition = Vector3.zero;
    }

    /// <summary>
    /// 弾幕発射(継承して別スクリプトにすること)
    /// </summary>
    /// <param name="pos">弾幕の生成位置</param>
    /// <param name="moveSpeed">弾幕の測度</param>
    /// <param name="angleDegree">発射角度</param>
    public void LaunchBullet(Vector3 pos, float moveSpeed, float angleDegree)
    {
        // BulletParent の存在確認
        if (bulletParentObj == null)
        {
            bulletParentObj = GameObject.Find("BulletParent");
            if (bulletParentObj == null)
            {
                Debug.LogError("BulletParent not found in scene!");
                return;
            }
        }

        // プール取得
        var pooler = BulletPooler.Instance;
        if (pooler == null)
        {
            Debug.LogError("BulletPooler.Instance is null! Make sure BulletPooler exists in scene.");
            return;
        }

        var bulletObj = pooler.GetBullet();
        if (bulletObj == null)
        {
            Debug.LogWarning("No available bullet in pool!");
            return;
        }

        bulletObj.transform.SetParent(bulletParentObj.transform);
        bulletObj.transform.position = transform.position + pos;
        bulletObj.transform.rotation = Quaternion.Euler(0, 0, angleDegree);

        var bulletControllerScript = bulletObj.GetComponent<BulletController>();
        if (bulletControllerScript == null)
        {
            Debug.LogError("BulletController component missing on bullet prefab!");
            return;
        }

        bulletControllerScript.MoveSpeed = moveSpeed;
        bulletControllerScript.AngleDegree = angleDegree;
    }

    // playerID参照(getset)
    public int EnemyID
    {
        get { return enemyID; }
        set { enemyID = value; }
    }

    private void OnDisable()
    {
        // lifeTimer は FixedUpdate で増える、この敵の生存時間
        float survivalTime = lifeTimer;

        // 生存時間が非常に短い場合のみログを出す (即時死亡の追跡)
        if (survivalTime < 0.5f)
        {
            // ★ ログを強化：誰が呼んだかを追跡するためにStackTraceを追加
            Debug.LogError($"[EXTERNAL DISABLE] ID: {enemyID}. " +
                           $"Active(false) called by external source. " +
                           $"Survival Time: {survivalTime:F3}s (Frame: {Time.frameCount})\n" +
                           $"CALL STACK TRACE: {System.Environment.StackTrace}");
        }
    }
}
*/

using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EnemyBase：既存の実装を尊重しつつ、
/// - ResetEnemy_InternalOnly() を追加（プールから取り出された時に呼ぶ軽い初期化）
/// - Death() はプールへの戻しを行う（Destroyしない）
/// - OnDisable に短寿命ログを残してトレース可能にする
/// </summary>
public class EnemyBase : MonoBehaviour
{
    [Header("エネミーID")]
    [SerializeField] int enemyID = 0;
    [Header("最大HP")]
    [SerializeField] int maxHp = 1;
    [Header("現在HP")]
    [SerializeField] int currentHp;

    [Header("弾幕プレハブ")]
    [SerializeField] GameObject bulletPrefab;
    [Header("弾幕格納先(親)")]
    [SerializeField] GameObject bulletParentObj;

    private Vector2  initialScale;

    private float timer = 0f;

    private float lifeTimer = 0f;

    // ログ出力用
    List<PlayerBase> activePlayersList = new List<PlayerBase>();    // プレイヤーの位置
    private int taskAssigneeID_Local = 0;           // 責任者ID(エネミー側で扱う便宜的なID)
    private int taskAssigneeID_Player = 0;          // 責任者ID(プレイヤーのプレイヤーID)
    private int taskAssigneeCountFrame = 0;         // 責任者IDを付与する際のフレームのカウンター(最初のカウントは最後の座標が記録されないなどの理由)
    private bool isEscape = false;                  // 撃ち漏らし
    Vector2 currentPos;                             // 現在の位置
    Vector2 lastPos;                                // 1フレーム前の位置

    private bool isCount = false;                   // 生成して最初のフレームでtrue、敵の生成数をカウントするため

    // 画面外の範囲
    float minX = -10.0f;
    float maxX = 10.0f;
    float minY = -6.0f;
    float maxY = 6.0f;

    void Start()
    {
        // 既存 Start 処理（必要なら残す）
        initialScale = transform.localScale;
    }

    void FixedUpdate()
    {
        lifeTimer += Time.fixedDeltaTime;
        timer += Time.fixedDeltaTime;

        // 生成されたかどうか
        if (isCount == false) 
        {
            // 生成された数のカウント
            ActionLogController.Instance.CountEnemy();

            isCount = true;
        } 

        // 責任者ID取得
        if (taskAssigneeID_Local == 0) 
        {
            GetTaskAssigneeID();
            // 責任者のカウント
            ActionLogController.Instance.AddTaskAssignee(taskAssigneeID_Player);
        }

        // 撃ち漏らし取得
        if (taskAssigneeCountFrame > 1) 
        {
            if (taskAssigneeID_Local == 1 || taskAssigneeID_Local == 2)
            {
                GetIsEscape();
            }
        }
        taskAssigneeCountFrame++;

        // 移動処理
        Move();
        
        if (timer >= 1.0f)
        {
            float num = Random.Range(0.0f, 360.0f);
            for (int i = 0; i < 4; i++)
            {
                LaunchBullet(Vector3.zero, 2f, i * 360f / 4f + num);
            }
            timer = 0f;
        }

        // 画面外チェック（コメントアウト可）
        Vector3 pos = transform.position;
        if (pos.x < minX || pos.x > maxX || pos.y < minY || pos.y > maxY)
        {
            // 画面外で即死させたくない場合は何もしない
            Death(false, -1);
        }
    }

    /// <summary>
    /// 被ダメージ処理（HP <= 0 のときは Death を呼ぶ）
    /// </summary>
    public int Damage(int damage, int attackID)
    {
        currentHp -= damage;

        // スコア等の処理、攻撃
        ScoreSystemController.Instance.UpdateScore(1, attackID);

        if (currentHp <= 0)
        {
            // スコア等の処理、撃破
            ScoreSystemController.Instance.UpdateScore(2, attackID);

            // エフェクト・音
            if (EnemyDeathEffect.Instance != null)
                EnemyDeathEffect.Instance.PlayEffect(transform.position);
            SoundManager.PlaySE(SEType.EnemyDeath);

            // 死亡
            Death(true, attackID);
            return 1;
        }
        return 0;
    }

    // Death: プールへ戻す（Destroyしない）
    public void Death(bool isKilled, int attackID)
    {
        // 撃ち漏らし補助判定(撃ち漏らしフラグがある敵が倒された)
        if (ActionLogController.IsAvailable())
        {
            // isEscape(撃ち漏らしが発生したか),taskAssigneeID_Player(責任者),attackID(攻撃者),true/false(協調成立か)
            ActionLogController.Instance.AddIsEscapeData(isKilled, isEscape, taskAssigneeID_Player, attackID);
        }

        // プールへ返却（EnemyPoolManager を使用）
        if (EnemyPoolManager.Instance != null)
        {
            EnemyPoolManager.Instance.ReleaseEnemy(this.gameObject);
        }
        else
        {
            // 予備: プールがないなら Destroy（安全策）
            Destroy(this.gameObject);
        }
    }

    // 移動（既存のロジックを保持）
    public void Move()
    {
        float moveSpeed = 7.5f;
        Vector3 moveDirection = Vector3.zero;
        if (enemyID == 1) moveDirection = new Vector3(0.5f, -0.25f);
        else if (enemyID == 2) moveDirection = new Vector3(-0.5f, -0.25f);
        else if (enemyID == 3) moveDirection = new Vector3(0.5f, 0.25f);
        else if (enemyID == 4) moveDirection = new Vector3(-0.5f, 0.25f);
        else if (enemyID == 5) moveDirection = new Vector3(0f, -0.25f);
        else if (enemyID == 6) moveDirection = new Vector3(0f, -0.25f);
        else if (enemyID == 7) moveDirection = new Vector3(0.2f, -0.4f);
        else if (enemyID == 8) moveDirection = new Vector3(-0.2f, -0.4f);
        else if (enemyID == 9) moveDirection = new Vector3(0f, -0.25f);
        else if (enemyID == 10) moveDirection = new Vector3(0f, -0.25f);

        if (moveDirection.x >= 0)
        {
            this.transform.localScale = new Vector2(initialScale.x, initialScale.y);
        }
        else 
        {
            this.transform.localScale = new Vector2(-1.0f * initialScale.x, initialScale.y);
        }

        transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
    }

    // 責任者ID取得
    public void GetTaskAssigneeID() 
    {
        // めちゃくちゃ重い処理なので後で書き直す
        PlayerBase[] activePlayersArray = FindObjectsOfType<PlayerBase>();
        activePlayersList = new List<PlayerBase>(activePlayersArray);

        if (activePlayersList.Count != 2)
        {
            Debug.Log("Playerが" + activePlayersList.Count + "体いる状態。");
            taskAssigneeID_Local = -1;
        }
        else
        {
            // 責任者ID
            Vector2 pos_0 = this.transform.position;
            Vector2 pos_1 = (Vector2)activePlayersList[0].transform.position - pos_0;
            Vector2 pos_2 = (Vector2)activePlayersList[1].transform.position - pos_0;

            // プレイヤーが自身より下にいる場合(ショットは上向きなので無視する必要がある)
            if (pos_1.y < 0)
            {
                pos_1.y = 0;
            }
            if (pos_2.y < 0)
            {
                pos_2.y = 0;
            }

            // 距離比較、ID取得。
            if (pos_1.sqrMagnitude <= pos_2.sqrMagnitude)
            {
                // ローカルID
                taskAssigneeID_Local = 1;
                Debug.Log("taskAssigneeID_Local==" + taskAssigneeID_Local);
                // プレイヤーID
                taskAssigneeID_Player = activePlayersList[0].PlayerID;
                Debug.Log("taskAssigneeID_Player==" + taskAssigneeID_Player);
            }
            else
            {
                // ローカルID
                taskAssigneeID_Local = 2;
                Debug.Log("taskAssigneeID_Local==" + taskAssigneeID_Local);
                // プレイヤーID
                taskAssigneeID_Player = activePlayersList[1].PlayerID;
                Debug.Log("taskAssigneeID_Player==" + taskAssigneeID_Player);
            }
        }
    }

    // 撃ち漏らし取得
    public void GetIsEscape() 
    {
        // 移動方向記録
        lastPos = currentPos;                   // 1フレーム前
        currentPos = this.transform.position;   // 現在のフレーム

        // 責任者の位置
        //Vector2 playerPos = activePlayersList[taskAssigneeID - 1].transform.position;
        Vector2 playerPos = (Vector2)activePlayersList[taskAssigneeID_Local - 1].transform.position;

        // 責任者が未設定、または既に撃ち漏らし状態なら判定しない
        //if (activePlayersList[taskAssigneeID] == null || isBypassed)
        //    return;

        // 敵の「現在の移動方向」を示すベクトル
        Vector2 movementVector = currentPos - lastPos;

        // 敵が停止している場合は判定できないため、処理を中断
        // if (movementVector.sqrMagnitude < 0.001f)
        // return;

        // V_N (法線): 移動方向 V_E の逆向きを定義
        Vector2 normalVector = -movementVector.normalized;

        // V_EP (相対位置): 敵 E からプレイヤー P へ向かうベクトル
        // V_EP = PlayerPosition - EnemyPosition
        Vector2 vectorEnemyToPlayer = playerPos - currentPos;

        // ----------------------------------------------------
        // 3. ドット積による通過判定
        // ----------------------------------------------------

        float dotProduct = Vector2.Dot(vectorEnemyToPlayer, normalVector);


        float escapeBorder = 0.5f;      // 撃ち漏らし距離

        if (dotProduct >= escapeBorder) //どれだけ越えたか。
        {
            // ドット積が正 = 敵が判定ラインを越えてプレイヤーの後ろに回った
            isEscape = true;
            Debug.Log("【撃ち漏らし発生】: " + activePlayersList[taskAssigneeID_Local - 1].name + " を敵が通過。");
        }
    }


    /// <summary>
    /// ★ 新規：内部専用の軽い初期化（Active化前に呼ぶ）
    /// - Transform の初期化は呼び出し側（Spawn）でやる想定
    /// - ここでは HP・タイマーなど最低限をリセットする
    /// </summary>
    public void ResetEnemy_Internal()
    {
        currentHp = maxHp;
        timer = 0;
        lifeTimer = 0;

        // ★追加：位置リセット
        transform.position = new Vector3(9999, 9999, 0);

        taskAssigneeID_Local = 0;
        taskAssigneeID_Player = 0;
        taskAssigneeCountFrame = 0;
        isEscape = false;
        isCount = false;
    }

    // LaunchBullet は既存のものをそのまま利用
    public void LaunchBullet(Vector3 pos, float moveSpeed, float angleDegree)
    {
        if (bulletParentObj == null)
        {
            bulletParentObj = GameObject.Find("BulletParent");
            if (bulletParentObj == null)
            {
                Debug.LogError("BulletParent not found in scene!");
                return;
            }
        }

        var pooler = BulletPooler.Instance;
        if (pooler == null)
        {
            Debug.LogError("BulletPooler.Instance is null!");
            return;
        }

        var bulletObj = pooler.GetBullet();
        if (bulletObj == null)
        {
            Debug.LogWarning("No available bullet in pool!");
            return;
        }

        bulletObj.transform.SetParent(bulletParentObj.transform);
        bulletObj.transform.position = transform.position + pos;
        bulletObj.transform.rotation = Quaternion.Euler(0, 0, angleDegree);

        var bulletControllerScript = bulletObj.GetComponent<BulletController>();
        if (bulletControllerScript == null)
        {
            Debug.LogError("BulletController component missing on bullet prefab!");
            return;
        }

        bulletControllerScript.MoveSpeed = moveSpeed;
        bulletControllerScript.AngleDegree = angleDegree;
    }

    public int EnemyID
    {
        get { return enemyID; }
        set { enemyID = value; }
    }
    public int CurrentHP
    {
        get { return currentHp; }
        set { currentHp = value; }
    }

    // 短寿命で突然非アクティブになったときの追跡用ログを出す
    private void OnDisable()
    {
        float survivalTime = lifeTimer;
        if (survivalTime < 0.5f)
        {
            //Debug.LogWarning($"[EnemyBase] Disabled quickly. ID:{enemyID}, survivalTime:{survivalTime:F3}s, frame:{Time.frameCount}\nSTACK: {System.Environment.StackTrace}");
        }
    }
}
