using UnityEngine;

public class ShotController : MonoBehaviour
{
    [Header("移動速度")]
    [SerializeField] float moveSpeed = 20.0f;
    [Header("角度")]
    [SerializeField] float angleDegree = 30.0f;
    float timer = 0;

    [SerializeField] GameObject shotEffectObj;
    [SerializeField] GameObject shotEffectParentObj;

    [Header("ショットのプレイヤーID")]
    [SerializeField] int playerID = 0;
    RewardManager rewardManager;

    // 画面外の範囲
    float minX = -6.5f;
    float maxX = 6.5f;
    float minY = -5.0f;
    float maxY = 5.0f;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // 移動
        Move();

        // 画面外処理
        Vector3 pos = transform.position;
        if (pos.x < minX || pos.x > maxX || pos.y < minY || pos.y > maxY)
        {
            Death();
        }
    }

    public void Initialize(RewardManager rewardManager)
    {
        this.rewardManager = rewardManager;
    }

    // 移動
    public void Move()
    {
        Vector2 moveDirection;          // 向き
        // Y軸（上方向）を基準として角度を反映
        float angleRad = -angleDegree * Mathf.Deg2Rad;
        moveDirection = new Vector2(Mathf.Sin(angleRad), Mathf.Cos(angleRad)).normalized;

        // Z軸回転（見た目の向き）も合わせる
        transform.rotation = Quaternion.Euler(0f, 0f, angleDegree);

        transform.position += (Vector3)(moveDirection * moveSpeed * Time.deltaTime);
    }

    // 敵に当たったとき
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.CompareTag("Enemy"))
        {
            // 敵のHP判定(オーバーキル防止)
            EnemyBase enemyBase = other.GetComponent<EnemyBase>();
            if (enemyBase.CurrentHP <= 0) 
            {
                return;
            }

            // ショットエフェクトの生成
            var shotEffectObjs = Instantiate(shotEffectObj);
            //shotEffectObjs.transform.SetParent(shotEffectParentObj.transform);
            shotEffectObjs.transform.position = transform.position;

            // ショットの音の再生
            SoundManager.PlaySE(SEType.Shot1);

            // ダメージを与える
            //int statusNum = (int)(other.GetComponent<EnemyBase>()?.Damage(1, playerID));       //ダメージを1と仮定
            int statusNum = (int)(enemyBase?.Damage(1, playerID));

            // 報酬
            if (statusNum == 0)
            {
                rewardManager.AddReward(RewardType.AttackReward);
            }
            // 敵の死亡
            if (statusNum == 1)
            {
                rewardManager.AddReward(RewardType.KillReward);
            }

            // 貫通しない場合
            Death();
        }
    }

    // 消滅
    public void Death() 
    {
        ShotPooler.Instance.ReturnShot(gameObject);
    }

    // moveSpeed参照(getset)
    public float MoveSpeed
    {
        get { return moveSpeed; }
        set { moveSpeed = value; }
    }
    // angleDegree参照(getset)
    public float AngleDegree
    {
        get { return angleDegree; }
        set { angleDegree = value; }
    }
    // playerID参照(getset)
    public int PlayerID
    {
        get { return playerID; }
        set { playerID = value; }
    }
}
