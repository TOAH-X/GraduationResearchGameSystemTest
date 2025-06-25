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

    // 画面外の範囲
    float minX = -10f;
    float maxX = 10f;
    float minY = -6f;
    float maxY = 6f;

    // Start is called before the first frame update
    void Start()
    {
        currentHp = maxHp;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // 移動       //他スクリプトに分けること
        Move();

        // 死亡処理
        if (currentHp <= 0)
        {
            Death();
            ScoreSystemController.Instance.UpdateScore(100, attackID);
        }

        timer += Time.deltaTime;
        if (timer >= 1.0f) 
        {
            float num = Random.Range(0.0f, 360.0f);
            for (int i = 0; i < 5; i++) 
            {

                //弾幕発射      //他スクリプトに分けること
                LaunchBullet(new Vector3(0f, 0f), 2f, i * 360 / 5f + num);
                //LaunchBullet(new Vector3(0f, 0f), 2f, i * 36f);
            }
            timer = 0;
        }

        // 画面外処理
        Vector3 pos = transform.position;
        if (pos.x < minX || pos.x > maxX || pos.y < minY || pos.y > maxY)
        {
            Death();
        }
    }

    /// <summary>
    /// 被ダメージ処理
    /// </summary>
    /// <param name="damage">ダメージ量</param>
    /// <param name="id">自身(敵)にダメージを与えたプレイヤーID</param>
    public void Damage(int damage, int id)
    {
        currentHp -= damage;
        attackID = id;
        // スコアを加算
        //ScoreSystemController.Instance.UpdateScore(1, attackID);
    }

    // 死亡処理
    public void Death()
    {
        Destroy(gameObject);
    }

    // 移動(継承して別スクリプトにすること)
    public void Move()
    {
        if (enemyID == 1)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(0.5f, -0.25f);      //右下
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.deltaTime);
        }
        else if (enemyID == 2)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(-0.5f, -0.25f);     //左下
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.deltaTime);
        }
        else if (enemyID == 3)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(0.5f, 0.25f);       //右上
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.deltaTime);
        }
        else if (enemyID == 4)
        {
            float moveSpeed = 10.0f;     // 移動速度
            Vector3 moveDirection = new Vector3(-0.5f, 0.25f);       //左上
            transform.position += (Vector3)(moveDirection * moveSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// 弾幕発射(継承して別スクリプトにすること)
    /// </summary>
    /// <param name="pos">弾幕の生成位置</param>
    /// <param name="moveSpeed">弾幕の測度</param>
    /// <param name="angleDegree">発射角度</param>
    public void LaunchBullet(Vector3 pos, float moveSpeed, float angleDegree) 
    {
        // 弾幕生成
        if (bulletParentObj == null) 
        {
            bulletParentObj = GameObject.Find("BulletParent");      //重いので余裕があればインスタンス化
        }
        // プールから取得して設定
        var bulletObj = BulletPooler.Instance.GetBullet();
        bulletObj.transform.SetParent(bulletParentObj.transform);
        bulletObj.transform.position = transform.position + pos;
        bulletObj.transform.rotation = Quaternion.Euler(0, 0, angleDegree);

        // 弾幕の設定
        BulletController bulletControllerScript = bulletObj.GetComponent<BulletController>();
        bulletControllerScript.MoveSpeed = moveSpeed;
        bulletControllerScript.AngleDegree = angleDegree;
    }

    // playerID参照(getset)
    public int EnemyID
    {
        get { return enemyID; }
        set { enemyID = value; }
    }
}
