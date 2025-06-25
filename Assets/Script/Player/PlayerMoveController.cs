using UnityEngine;

public class PlayerMoveController : MonoBehaviour
{
    [Header("移動速度")]
    [SerializeField] float moveSpeed = 5.0f;
    [Header("低速モードの低速率")]
    [SerializeField] float slowSpeedRate = 0.5f;

    [SerializeField] PlayerSpriteController playerSpriteControllerScript;   // PlayerSpriteControllerのスクリプト

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // 入力取得（上下左右）
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        MovePlayer(h, v);
    }

    // 移動
    public void MovePlayer(float h, float v) 
    {
        // 移動ベクトルの作成
        Vector2 inputDir = new Vector2(h, v);

        // 正規化（斜め移動の速さを一定にする）
        if (inputDir.magnitude > 1f)
        {
            inputDir = inputDir.normalized;
        }

        // 移動
        if (Input.GetKey(KeyCode.LeftShift) == false)
        {
            transform.position += (Vector3)(inputDir * moveSpeed * Time.deltaTime);
        }
        else
        {
            transform.position += (Vector3)(inputDir * moveSpeed * Time.deltaTime * slowSpeedRate);
        }


        // 移動制限
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, -5.5f, 5.5f);
        pos.y = Mathf.Clamp(pos.y, -4.0f, 4.0f);
        transform.position = pos;

        // プレイヤーのスプライトの更新
        playerSpriteControllerScript.UpdateSprite(h);
    }
}
