using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Header("倍速")]
    [SerializeField] float simulationSpeed = 1f;

    // ゲーム終了用
    [SerializeField] GameObject playerObj;
    [SerializeField] GameObject playerAnotherObj;
    [SerializeField] GameObject subPlayerObj;
    [SerializeField] GameObject subPlayerAnotherObj;

    [SerializeField] PlayerAgent playerAgent_Player;
    [SerializeField] PlayerAgent playerAgent_PlayerAnother;
    [SerializeField] SubPlayerAgent subPlayerAgent_SubPlayer;
    [SerializeField] SubPlayerAgent subPlayerAgent_SubPlayerAnother;

    private bool isResetting = false;       // 二重リセット防止用

    

    void Awake()
    {
        // 学習開始時に時間スケールを設定する
        if (simulationSpeed != 1) 
        {
            Time.timeScale = simulationSpeed;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        SoundManager.PlayBGM(BGMType.GameMain1);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // ゲーム終了時処理、可能ならデリゲートに書き換えること
    public void EndGame() 
    {
        // アクションログに出力
        ActionLogController.Instance.ExportData();

        if (playerObj != null)
        {
            playerAgent_Player.EndGame();
        }
        if (playerAnotherObj != null) 
        { 
            playerAgent_PlayerAnother.EndGame();
        }
        if (subPlayerObj != null)
        {
            subPlayerAgent_SubPlayer.EndGame();
        }
        if (subPlayerAnotherObj != null) 
        {
            subPlayerAgent_SubPlayerAnother.EndGame();
        }
    }

    // Agentから呼ばれる「安全なリセット要求」メソッド
    public void RequestSafeDelayedReset()
    {
        if (isResetting) return; // 既にリセット処理中なら無視
        StartCoroutine(SafeResetCoroutine());
    }

    IEnumerator SafeResetCoroutine()
    {
        isResetting = true;
        Debug.Log("リセットシーケンス開始");

        // 1. ログ出力（個別でTry-Catchして、1つのログ失敗でリセットが止まらないようにする）
        try { EndGame(); } catch (System.Exception e) { Debug.LogError("EndGameエラー: " + e.Message); }

        // 2. ML-Agents通知（安全な呼び出し）
        // エージェントが有効で、かつ初期化が完了している場合のみ呼ぶ
        SafeEndEpisode(playerAgent_Player);
        SafeEndEpisode(playerAgent_PlayerAnother);
        SafeEndEpisode(subPlayerAgent_SubPlayer);
        SafeEndEpisode(subPlayerAgent_SubPlayerAnother);

        // 3. 確実に1フレーム待つ
        yield return new WaitForFixedUpdate();

        Debug.Log("リロード実行");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void SafeEndEpisode(Unity.MLAgents.Agent agent)
    {
        // agent自体がNullでなく、かつGameObjectがActive（シーンに存在）しているか
        if (agent != null && agent.gameObject.activeInHierarchy)
        {
            try
            {
                agent.EndEpisode();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{agent.name} の終了処理に失敗: {e.Message}");
            }
        }
    }
}
