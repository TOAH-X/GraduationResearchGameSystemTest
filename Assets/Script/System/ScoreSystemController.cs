using System.Collections;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScoreSystemController : MonoBehaviour
{
    [Header("総合スコア")]
    [SerializeField] int totalScore = 0;
    [Header("プレイヤー1のスコア")]
    [SerializeField] int player1Score = 0;
    [Header("プレイヤー2のスコア")]
    [SerializeField] int player2Score = 0;

    //[SerializeField] RewardManager rewardManagerScript;

    public static ScoreSystemController Instance { get; private set; }


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    /// <summary>
    /// スコア更新(加算)
    /// </summary>
    /// <param name="case">ケース、敵の被弾…1,敵の死亡…0</param>
    /// <param name="id">加算対象(プレイヤー1かプレイヤー2か)</param>
    public void UpdateScore(int caseType, int id) 
    {
        int score = 0;
        // 敵の被弾
        if (caseType == 1) 
        {
            score = 1;
        }
        // 敵の死亡
        if (caseType == 2) 
        {
            score = 100;
        }
        totalScore += score;

        // 奇数プレイヤー(A)
        if (id == 1 || id == 3)
        {
            player1Score += score;

            // 試験的な報酬(後で書き換えること)※敵を倒したとき
            // 敵の被弾
            /*if (caseType == 1)
            {
                rewardManagerScript.AddReward(RewardType.AttackReward);
            }
            // 敵の死亡
            if (caseType == 2)
            {
                rewardManagerScript.AddReward(RewardType.KillReward);
            }*/
        }
        // 偶数プレイヤー(B)
        else if (id == 2 || id == 4)  
        {
            player2Score += score;

            // 試験的な報酬(後で書き換えること)※敵を倒したとき
            // 敵の被弾
            /*if (caseType == 1)
            {
                rewardManagerScript.AddReward(RewardType.AttackReward);
            }
            // 敵の死亡
            if (caseType == 2)
            {
                rewardManagerScript.AddReward(RewardType.KillReward);
            }*/
        }
    }

    // totalScore参照(getset)
    public int TotalScore
    {
        get { return totalScore; }
        set { totalScore = value; }
    }

    // player1Score参照(getset)
    public int Player1Score
    {
        get { return player1Score; }
        set { player1Score = value; }
    }

    // player2Score参照(getset)
    public int Player2Score
    {
        get { return player2Score; }
        set { player2Score = value; }
    }

    //シングルトン
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); // シーン切り替え時に消えない
    }
}
