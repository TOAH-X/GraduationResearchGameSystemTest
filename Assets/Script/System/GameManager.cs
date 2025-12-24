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

    /*
    // ログを出して、通信を待ってからリロードするコルーチン
    IEnumerator SafeResetCoroutine()
    {
        isResetting = true;

        // 1. ログ出力（各AgentのEndGameを呼ぶ）
        EndGame();

        // 2. ML-Agentsにエピソード終了を通知（全員分呼ぶのが安全）
        if (playerAgent_Player != null) playerAgent_Player.EndEpisode();
        if (playerAgent_PlayerAnother != null) playerAgent_PlayerAnother.EndEpisode();
        if (subPlayerAgent_SubPlayer != null) subPlayerAgent_SubPlayer.EndEpisode();
        if (subPlayerAgent_SubPlayerAnother != null) subPlayerAgent_SubPlayerAnother.EndEpisode();

        // 3. データの送信完了まで1フレーム待つ（ここが最重要！）
        yield return new WaitForFixedUpdate();

        // 4. シーンをリロード
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
    */

    /*
    IEnumerator SafeResetCoroutine()
    {
        isResetting = true;
        Debug.Log("【GameManager】リセットシーケンスを開始します...");

        // 1. ログ出力（ここが一番エラーが出やすい箇所）
        // try-catchで囲むことで、ログ出力に失敗してもリロードだけは必ず行わせる
        try
        {
            EndGame();
            Debug.Log("【GameManager】EndGame（ログ出力）完了");
        }
        catch (System.Exception e)
        {
            Debug.LogError("【エラー】ログ出力中に例外が発生しましたが、リセットを続行します。\n詳細: " + e.Message + "\n" + e.StackTrace);
        }

        // 2. ML-Agentsにエピソード終了を通知
        // ここも念のため個別チェックを入れる
        if (playerAgent_Player != null) playerAgent_Player.EndEpisode();
        if (playerAgent_PlayerAnother != null) playerAgent_PlayerAnother.EndEpisode();
        if (subPlayerAgent_SubPlayer != null) subPlayerAgent_SubPlayer.EndEpisode();
        if (subPlayerAgent_SubPlayerAnother != null) subPlayerAgent_SubPlayerAnother.EndEpisode();

        Debug.Log("【GameManager】EndEpisode通知完了。通信待機(WaitForFixedUpdate)に入ります。");

        // 3. データの送信完了まで1フレーム待つ
        yield return new WaitForFixedUpdate();

        // 4. シーンをリロード
        Debug.Log("【GameManager】シーンをリロードします。");
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
    */

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
