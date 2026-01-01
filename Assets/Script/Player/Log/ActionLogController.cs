using UnityEngine;

[System.Serializable] 
public struct ActionDataGroup
{
    // プレイヤーが敵を攻撃した数
    public int attackedCount;
    // プレイヤーが敵を倒した数(倒したプレイヤー)
    public int killedCount;
    // 責任割り当て数(プレイヤーの責任の数)
    public int taskAssigneeCount;
    // 自身の責任が付いた敵を倒した数(サブ手法)
    public int taskAssigneeKilldedCount;
    // 自身の責任が付いていない敵を倒した数(サブ手法)
    public int nonTaskAssigneeKilldedCount;
    // 撃ち漏らしの数(責任の所属)
    public int escapedCount;
    // 相手が撃ち漏らして自分が敵を倒した数
    public int escapedPartnerKilledSelf;
    // 自分が撃ち漏らして自分が敵を倒した数
    public int escapedSelfKilledSelf;
}

public class ActionLogController : SafeSingleton<ActionLogController>
{
    [SerializeField] ActionLogCSV actionLogCSV;

    public ActionDataGroup statsTotal;
    public ActionDataGroup statsA;
    public ActionDataGroup statsB;

    public ActionDataGroup StatsTotal => statsTotal;
    public ActionDataGroup StatsA => statsA;
    public ActionDataGroup StatsB => statsB;

    //ステージID
    public int ActionLog_StageID { get; set; } = -1;
    // PlayerID(奇数機体)
    public int ActionLog_PlayerID_A { get; set; } = 0;
    // PlayerID(偶数機体)
    public int ActionLog_PlayerID_B { get; set; } = 0;
    // CurrentHP(奇数機体)
    public int ActionLog_CurrentHP_A { get; set; } = 0;
    // CurrentHP(偶数機体)
    public int ActionLog_CurrentHP_B { get; set; } = 0;
    // 学習タイプ(奇数機体)
    public string ActionLog_BehaviorTypeName_A { get; set; } = "";
    // 学習タイプ(偶数機体)
    public string ActionLog_BehaviorTypeName_B { get; set; } = "";
    // ゲーム内スコア(総合)
    public int ActionLog_Score_Total { get; set; } = 0;
    // ゲーム内スコア(奇数機体)
    public int ActionLog_Score_A { get; set; } = 0;
    // ゲーム内スコア(偶数機体)
    public int ActionLog_Score_B { get; set; } = 0;

    /*
    // プレイヤーが敵を倒した数(倒したプレイヤー)
    [SerializeField] private int killedCount = 0;
    [SerializeField] private int killedCountA = 0;
    [SerializeField] private int killedCountB = 0;
    // 責任割り当て数(プレイヤーの責任の数)
    [SerializeField] private int taskAssigneeCount = 0;
    [SerializeField] private int taskAssigneeCountA = 0;
    [SerializeField] private int taskAssigneeCountB = 0;
    // 自身の責任が付いた敵を倒した数(サブ手法)
    [SerializeField] private int taskAssigneeKilldedCount = 0;
    [SerializeField] private int taskAssigneeKilldedCountA = 0;
    [SerializeField] private int taskAssigneeKilldedCountB = 0;
    // 自身の責任が付いていない敵を倒した数(サブ手法)
    [SerializeField] private int nonTaskAssigneeKilldedCount = 0;
    [SerializeField] private int nonTaskAssigneeKilldedCountA = 0;
    [SerializeField] private int nonTaskAssigneeKilldedCountB = 0;
    // 撃ち漏らしの数(責任の所属)
    [SerializeField] private int escapedCount = 0;
    [SerializeField] private int escapedCountA = 0;
    [SerializeField] private int escapedCountB = 0;
    // 相手が撃ち漏らして自分が敵を倒した数
    [SerializeField] private int escapedPartnerKilledSelf = 0;
    [SerializeField] private int escapedPartnerKilledSelfA = 0;
    [SerializeField] private int escapedPartnerKilledSelfB = 0;
    // 自分が撃ち漏らして自分が敵を倒した数
    [SerializeField] private int escapedSelfKilledSelf = 0;
    [SerializeField] private int escapedSelfKilledSelfA = 0;
    [SerializeField] private int escapedSelfKilledSelfB = 0;
    */
    // 逃げ切った敵の数(完全な撃ち漏らし)

    // 出現した敵の数
    [SerializeField] private int enemyCount = 0;
    

    // ※システム上、ゲームが中断されてしまって倒されずに処理されなかった敵の扱いも考えておく

    // Start is called before the first frame update
    void Start()
    {

    }

    // 初期化したい内容
    protected override void OnSingletonAwake()
    {
        base.OnSingletonAwake();

        Debug.Log("Cooperation Manager が安全に初期化されました。");

        // ここでデータの初期化や必要なオブジェクトの参照取得を行う
    }

    void FixedUpdate()
    {

    }

    // 出力。ActionLogCSVに渡す。
    public void ExportData() 
    {
        actionLogCSV.UpdateData(statsTotal, statsA, statsB);

        // 初期化
        ResetAllStats();
    }

    // 撃ち漏らし補助関係
    // isKilled(倒されたのか),isEscape(撃ち漏らしが発生したか),taskAssigneeID_Player(責任者),attackID(攻撃者),true/false(協調成立か)
    public void AddIsEscapeData(bool isKilled, bool isEscape, int taskAssigneeID_Player, int attackID)
    {
        
        if (isKilled == true) 
        {
            // プレイヤーが敵を倒した数
            statsTotal.killedCount++;
            // 奇数ID機体
            if (attackID == 1 || attackID == 3)
            {
                statsA.killedCount++;
            }
            // 偶数ID機体
            else if (attackID == 2 || attackID == 4)
            {
                statsB.killedCount++;
            }

            // 自身の責任が付いた敵を倒した数(サブ手法)
            if (taskAssigneeID_Player == attackID)
            {
                statsTotal.taskAssigneeKilldedCount++;
                // 奇数ID機体
                if (attackID == 1 || attackID == 3)
                {
                    statsA.taskAssigneeKilldedCount++;
                }
                // 偶数ID機体
                else if (attackID == 2 || attackID == 4)
                {
                    statsB.taskAssigneeKilldedCount++;
                }
            }
            // 自身の責任が付いていない敵を倒した数(サブ手法)
            else if (taskAssigneeID_Player != attackID) 
            {
                statsTotal.nonTaskAssigneeKilldedCount++;
                // 奇数ID機体
                if (attackID == 1 || attackID == 3)
                {
                    statsA.nonTaskAssigneeKilldedCount++;
                }
                // 偶数ID機体
                else if (attackID == 2 || attackID == 4)
                {
                    statsB.nonTaskAssigneeKilldedCount++;
                }
            }

        }

        // 協調成立かはtaskAssigneeID_PlayerとattackIDを見る
        if (isEscape == true)  
        {
            // 撃ち漏らしの数(責任の所属)
            statsTotal.escapedCount++;
            // 奇数ID機体
            if (taskAssigneeID_Player == 1 || taskAssigneeID_Player == 3)
            {
                statsA.escapedCount++;
            }
            // 偶数ID機体
            else if (taskAssigneeID_Player == 2 || taskAssigneeID_Player == 4)
            {
                statsB.escapedCount++;
            }

            if (isKilled == true) 
            {
                // 協調行動成立
                if (taskAssigneeID_Player != attackID)
                {
                    // 相手が撃ち漏らして自分が敵を倒した数
                    statsTotal.escapedPartnerKilledSelf++;
                    // 奇数ID機体
                    if (attackID == 1 || attackID == 3)
                    {
                        statsA.escapedPartnerKilledSelf++;
                    }
                    // 偶数ID機体
                    else if (attackID == 2 || attackID == 4)
                    {
                        statsB.escapedPartnerKilledSelf++;
                    }
                }
                // 協調行動不成立
                else
                {
                    // 自分が撃ち漏らして自分が敵を倒した数
                    statsTotal.escapedSelfKilledSelf++;
                    // 奇数ID機体
                    if (attackID == 1 || attackID == 3)
                    {
                        statsA.escapedSelfKilledSelf++;
                    }
                    // 偶数ID機体
                    else if (attackID == 2 || attackID == 4)
                    {
                        statsB.escapedSelfKilledSelf++;
                    }
                }
            }
            
        }
        // 不成立
        else 
        { 
        }
    }

    // 責任の割り当て
    // taskAssigneeID_Player(責任者)
    public void AddTaskAssignee(int taskAssigneeID_Player) 
    {
        // 責任割り当て数(プレイヤーの責任の数)
        statsTotal. taskAssigneeCount++;
        // 奇数ID機体
        if (taskAssigneeID_Player == 1 || taskAssigneeID_Player == 3)
        {
            statsA.taskAssigneeCount++;
        }
        // 偶数ID機体
        else if (taskAssigneeID_Player == 2 || taskAssigneeID_Player == 4) 
        {
            statsB.taskAssigneeCount++;
        }
    }

    // 敵の生成数カウント
    public void CountEnemy() 
    { 
        enemyCount++;
    }

    public int EnemyCount
    {
        get { return enemyCount; }
        set { enemyCount = value; }
    }

    public void ResetAllStats()
    {
        // 構造体（struct）を new することで、中身の int は全て 0 に戻ります
        statsTotal = new ActionDataGroup();
        statsA = new ActionDataGroup();
        statsB = new ActionDataGroup();

        // システム系変数のリセット
        enemyCount = 0;

        // スコア・HP情報の初期化（必要に応じて）
        ActionLog_Score_Total = 0;
        ActionLog_Score_A = 0;
        ActionLog_Score_B = 0;
        ActionLog_CurrentHP_A = 0;
        ActionLog_CurrentHP_B = 0;

        Debug.Log("ActionLog: エピソード統計をリセットしました。");
    }
}
