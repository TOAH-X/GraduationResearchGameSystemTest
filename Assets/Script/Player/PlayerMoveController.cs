using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

public class PlayerMoveController : MonoBehaviour
{
    [Header("移動速度")]
    [SerializeField] float moveSpeed = 5.0f;
    [Header("低速モードの低速率")]
    [SerializeField] float slowSpeedRate = 0.5f;

    [SerializeField] PlayerSpriteController playerSpriteControllerScript;   // PlayerSpriteControllerのスクリプト

    [SerializeField] PlayerBase playerBaseScript;

    [Header("手動入力を受け付けるかどうか。手動はtrue、機械学習はfalse。必ず変更すること")]
    [SerializeField] bool isMove = true;
    [Header("いずれかの機体が学習中かどうか(実質的にはランダム出現を有効化するフラグ)")]
    [SerializeField] bool isDefaultBehavior = false;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (isMove == true) 
        {
            // 入力取得（上下左右）
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            MovePlayer(h, v);
        }
    }

    // 移動
    public void MovePlayer(float h, float v) 
    {
        // リセット時の挙動制御
        if (playerBaseScript.IsRestart == false)
        {
            // プレイヤーのスプライトの更新
            playerSpriteControllerScript.UpdateSprite(h);

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
                transform.position += (Vector3)(inputDir * moveSpeed * Time.fixedDeltaTime);
            }
            else
            {
                transform.position += (Vector3)(inputDir * moveSpeed * Time.fixedDeltaTime * slowSpeedRate);
            }

            // 移動制限
            Vector3 pos = transform.position;
            pos.x = Mathf.Clamp(pos.x, -5.5f, 5.5f);
            pos.y = Mathf.Clamp(pos.y, -4.0f, 4.0f);
            transform.position = pos;
        }
    }

    // 復帰移動(登場含む)
    public async UniTaskVoid RestartAsync(bool isStart, CancellationToken token)
    {
        // 開始時に自分がすでにいなければ終了
        if (this == null || playerBaseScript == null) return;

        playerBaseScript.IsRestart = true;

        Vector3 startPos;
        Vector3 resetPos;

        // 最初に登場する場合
        /*
        if (isStart == true)
        {
            float posX = this.transform.position.x;
            startPos = new Vector3(posX, -3.0f);        // 開始位置
            resetPos = new Vector3(posX, -6.0f);        // 画面外位置
        }
        // リスポーン時
        else 
        {
            startPos = new Vector3(0, -3.0f);           // 開始位置
            resetPos = new Vector3(0, -6.0f);           // 画面外位置
        }*/

        // 学習中ではない(通常)
        if (IsDefaultBehavior == false) 
        {
            startPos = new Vector3(0f, -3.0f);          // 開始位置
            resetPos = new Vector3(0f, -6.0f);          // 画面外位置
        }
        // 学習中(ランダム)
        else
        {
            float noisePosX = UnityEngine.Random.Range(-4.0f, 4.0f);
            float posX = noisePosX;
            startPos = new Vector3(posX, -3.0f);        // 開始位置
            resetPos = new Vector3(posX, -6.0f);        // 画面外位置
        }

        playerSpriteControllerScript.UpdateCenterSprite();  // 中央差分に上書き

        try
        {
            // 開始時に初期位置を設定
            transform.position = resetPos;

            float duration = 1f; // 移動にかける時間(秒)
            float elapsed = 0f;

            while (elapsed < duration)
            {
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, token); // FixedUpdateタイミングで動かす
                elapsed += Time.fixedDeltaTime;

                // --- 重要：復帰直後に自分が生きているか確認 ---
                if (this == null) return;

                float t = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(resetPos, startPos, t);
            }

            // 終了時に確実にリセット位置へ
            transform.position = startPos;
        }
        catch (System.OperationCanceledException)
        {
            Debug.Log("Move canceled.");
            return;
        }

        playerBaseScript.IsRestart = false;
    }

    public bool IsDefaultBehavior
    {
        get { return isDefaultBehavior; }
        set { isDefaultBehavior = value; }
    }
}
