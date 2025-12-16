using System;
using UnityEngine;
using System.Threading;
using Cysharp.Threading.Tasks;

public class PlayerBase : MonoBehaviour
{
    [Header("プレイヤーの名前")]
    [SerializeField] string playerName;
    [Header("プレイヤーID")]
    [SerializeField] int playerID = 1;
    [Header("PlayerType、プレイヤーIDと統合しておくこと")]
    [SerializeField] string playerType;
    [Header("現在の残機")]
    [SerializeField] int currentHp = 10;
    
    [SerializeField] PlayerMoveController playerMoveController;

    [SerializeField] InvincibilityController invincibilityControllerScript; // InvincibilityTimeのスクリプト

    [SerializeField] RewardManager rewardManager;

    private CancellationTokenSource cts;

    // Start is called before the first frame update
    void Start()
    {
        // リスタート
        cts = new CancellationTokenSource();
        playerMoveController.RestartAsync(true, cts.Token).Forget();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        
    }

    /// <summary>
    /// プレイヤーの被ダメージ
    /// </summary>
    /// <param name="damage">ダメージ量</param>
    public void Damage(int damage) 
    {
        if (invincibilityControllerScript.IsInvincibility() == false)  
        {
            currentHp -= damage;                                        // ダメージ反映
            invincibilityControllerScript.UpdateInvincibility(3.0f);    // 無敵時間更新
                                                                        // 被弾したときの報酬減少
            rewardManager.AddReward(RewardType.HitPenalty);

            // リスタート
            cts = new CancellationTokenSource();
            playerMoveController.RestartAsync(false, cts.Token).Forget();
        }
    }

    /*
    public async UniTaskVoid RestartAsync(CancellationToken token) 
    {
        IsRestart = true;

        Vector3 startPos = new Vector3(0, -3.0f);           // 開始位置
        Vector3 resetPos = new Vector3(0, -6.0f);           // 画面外位置

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

                float t = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(resetPos, startPos, t);
            }

            // 終了時に確実にリセット位置へ
            transform.position = startPos;
        }
        catch (System.OperationCanceledException)
        {
            Debug.Log("Move canceled.");
        }

        IsRestart = false;
    }
    */

    // playerID参照(getset)
    public int PlayerID
    {
        get { return playerID; }
        set { playerID = value; }
    }
    // currentHP参照(getset)
    public int CurrentHP
    {
        get { return currentHp; }
        set { currentHp= value; }
    }

    // リセット状態かどうか
    public bool IsRestart { get; set; } = false;

    // playerName参照(getset)
    public string PlayerName
    {
        get { return playerName; }
        set { playerName = value; }
    }

}
