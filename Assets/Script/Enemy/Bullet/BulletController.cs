using UnityEngine;

public class BulletController : MonoBehaviour
{
    [Header("移動速度")]
    [SerializeField] float moveSpeed = 10.0f;
    [Header("角度")]
    [SerializeField] float angleDegree = 30.0f;

    float timer = 0;

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

    // 移動
    public void Move()
    {
        Vector2 moveDirection;          // 向き
        // Y軸（上方向）を基準として角度を反映
        float angleRad = -angleDegree * Mathf.Deg2Rad;
        moveDirection = new Vector2(Mathf.Sin(angleRad), Mathf.Cos(angleRad)).normalized;

        // Z軸回転（見た目の向き）も合わせる
        transform.rotation = Quaternion.Euler(0f, 0f, angleDegree);

        transform.position += (Vector3)(moveDirection * moveSpeed * Time.fixedDeltaTime);
    }

    // 敵にぶつかったとき
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            // ダメージを与える
            other.GetComponent<PlayerBase>()?.Damage(1);         // ダメージを1と仮定
            // 貫通しない場合
            Death();
        }

        // 試験的(必ずリファクタリングを行うこと)
        if (other.gameObject.name=="Graze")
        {
            Debug.Log("Graze");
            other.GetComponent<GrazeController>().AddReward();
        }
    }

    // 消滅
    public void Death()
    {
        BulletPooler.Instance.ReturnBullet(gameObject);
    }

    // player1Score参照(getset)
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
}
