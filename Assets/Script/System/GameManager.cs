using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Header("倍速")]
    [SerializeField] float simulationSpeed = 1f;
    [Header("ステージID")]      // -1：エラー、0：学習環境、1：チュートリアル、2：被支援エージェント、3：支援エージェント
    [SerializeField] int stageID = -1;
    public int StageID => stageID;

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

    [SerializeField] PlayerBase playerBase_Player;
    [SerializeField] PlayerBase playerBase_PlayerAnother;
    [SerializeField] PlayerBase playerBase_SubPlayer;
    [SerializeField] PlayerBase playerBase_SubPlayerAnother;


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
        
        Debug.Log("TTP" + playerAgent_Player.ExportPosValue()[12]);
        Debug.Log("TTS" + subPlayerAgent_SubPlayerAnother.ExportPosValue()[12]);
        // 値の取得
        if (playerObj.activeSelf == true)
        {
            ActionLogController.Instance.ActionLog_PlayerID_A = 1;
            ActionLogController.Instance.ActionLog_CurrentHP_A = playerBase_Player.CurrentHP;
            ActionLogController.Instance.ActionLog_BehaviorTypeName_A = playerAgent_Player.BehaviorTypeName;
            ActionLogController.Instance.ActionLog_PosValue_A = playerAgent_Player.ExportPosValue();
            //playerAgent_Player.EndGame();
        }
        if (playerAnotherObj.activeSelf == true)
        {
            ActionLogController.Instance.ActionLog_PlayerID_B = 2;
            ActionLogController.Instance.ActionLog_CurrentHP_B = playerBase_PlayerAnother.CurrentHP;
            ActionLogController.Instance.ActionLog_BehaviorTypeName_B = playerAgent_PlayerAnother.BehaviorTypeName;
            ActionLogController.Instance.ActionLog_PosValue_B = playerAgent_PlayerAnother.ExportPosValue();
            //playerAgent_PlayerAnother.EndGame();
        }
        if (subPlayerObj.activeSelf == true)
        {
            ActionLogController.Instance.ActionLog_PlayerID_A = 3;
            ActionLogController.Instance.ActionLog_CurrentHP_A = playerBase_SubPlayer.CurrentHP;
            ActionLogController.Instance.ActionLog_BehaviorTypeName_A = subPlayerAgent_SubPlayer.BehaviorTypeName;
            ActionLogController.Instance.ActionLog_PosValue_A = subPlayerAgent_SubPlayer.ExportPosValue();
            //subPlayerAgent_SubPlayer.EndGame();
        }
        if (subPlayerAnotherObj.activeSelf == true)
        {
            ActionLogController.Instance.ActionLog_PlayerID_B = 4;
            ActionLogController.Instance.ActionLog_CurrentHP_B = playerBase_SubPlayerAnother.CurrentHP;
            ActionLogController.Instance.ActionLog_BehaviorTypeName_B = subPlayerAgent_SubPlayerAnother.BehaviorTypeName;
            ActionLogController.Instance.ActionLog_PosValue_B = subPlayerAgent_SubPlayerAnother.ExportPosValue();
            //subPlayerAgent_SubPlayerAnother.EndGame();
        }

        // 情報の収集
        ActionLogController.Instance.ActionLog_StageID = stageID;
        ActionLogController.Instance.ActionLog_Score_Total = ScoreSystemController.Instance.TotalScore;
        ActionLogController.Instance.ActionLog_Score_A = ScoreSystemController.Instance.Player1Score;
        ActionLogController.Instance.ActionLog_Score_B = ScoreSystemController.Instance.Player2Score;

        // アクションログに出力
        ActionLogController.Instance.ExportData();

        // EndGameの通知
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

        Debug.Log("シーン遷移実行");
        
        if (StageID <= 0)
        {
            // 学習時、エラー時はリロード
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        else 
        {
            // 正規は会話画面へ
            SceneManager.LoadScene("StartScene");
        }
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
