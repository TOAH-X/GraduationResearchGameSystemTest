using UnityEngine;
using System.IO;
using System;

public class ActionLogCSV : MonoBehaviour
{
    string fileName = "ActionLog";
    private string logFilePath;


    void Awake()
    {
        logFilePath = Path.Combine(Application.dataPath, fileName + "StatsLog.csv");

        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "JST,SubjectID,StageID," +
                "EnemyCount," +
                "Total_Score," +
                "TotalAttackedCount,TotalKilledCount,TotalTaskAssigneeCount,TotalTaskAssigneeKilldedCount,TotalNonTaskAssigneeKilldedCount,TotalEscapedCount,TotalEscapedPartnerKilledSelf,TotalEscapedSelfKilledSelf," +
                "A_ID," +
                "A_BehaviorTypeName," +
                "A_CurrentHP," +
                "A_Score," +
                "AAttackedCount,AKilledCount,ATaskAssigneeCount,ATaskAssigneeKilldedCount,ANonTaskAssigneeKilldedCount,AEscapedCount,AEscapedPartnerKilledSelf,AEscapedSelfKilledSelf," +
                "B_ID," +
                "B_BehaviorTypeName," +
                "B_CurrentHP," +
                "B_Score," +
                "BttackedCount,BKilledCount,BTaskAssigneeCount,BTaskAssigneeKilldedCount,BNonTaskAssigneeKilldedCount,BEscapedCount,BEscapedPartnerKilledSelf,BEscapedSelfKilledSelf," +
                "\n");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    /*
    // 書き込み(step,time,hp,reward,state)
    public void UpdateData()
    {
        string jst = GetCurrentJST();
        //stepCounter += 1 * 1000;
        string line =
            $"{jst}," +
            $"{stepCounter}," +
            $"{time:F2}," +
            $"{hp:F1}," +
            $"{rewardManager.RewardSum:F3}," +
            $"{rewardManager.GetRewardCount(RewardType.AttackReward)}," +
            $"{rewardManager.GetRewardCount(RewardType.KillReward)}," +
            $"{rewardManager.GetRewardCount(RewardType.SurvivalReward)}," +
            $"{rewardManager.GetRewardCount(RewardType.GrazePenalty)}," +
            $"{rewardManager.GetRewardCount(RewardType.HitPenalty)}," +
            $"{rewardManager.GetRewardCount(RewardType.PunishPenalty)}," +
            $"{rewardManager.GetRewardCount(RewardType.GraceReward)}," +
            $"{state}\n";
        File.AppendAllText(logFilePath, line);
    }
    */

    // JST,被験者ID,ステージID,プレイヤーID,学習タイプ,CurrentHP,*報酬関係(左右情報込)※死亡するまでも入れると良いかも,出現した敵の総数,各スコア,ActionDataGroup(Total,A,B)
    // 書き込み(step,time,hp,reward,state)
    public void UpdateData(ActionDataGroup t, ActionDataGroup a, ActionDataGroup b)
    {
        if (string.IsNullOrEmpty(logFilePath))
        {
            logFilePath = Path.Combine(Application.persistentDataPath, fileName + "StatsLog.csv");
        }

        if (!File.Exists(logFilePath))
        {
            if (!File.Exists(logFilePath))
            {
                File.WriteAllText(logFilePath, "JST,SubjectID,StageID," +
                    "EnemyCount," +
                    "Total_Score," +
                    "TotalAttackedCount,TotalKilledCount,TotalTaskAssigneeCount,TotalTaskAssigneeKilldedCount,TotalNonTaskAssigneeKilldedCount,TotalEscapedCount,TotalEscapedPartnerKilledSelf,TotalEscapedSelfKilledSelf," +
                    "A_ID," +
                    "A_BehaviorTypeName," +
                    "A_CurrentHP," +
                    "A_Score," +
                    "AAttackedCount,AKilledCount,ATaskAssigneeCount,ATaskAssigneeKilldedCount,ANonTaskAssigneeKilldedCount,AEscapedCount,AEscapedPartnerKilledSelf,AEscapedSelfKilledSelf," +
                    "B_ID," +
                    "B_BehaviorTypeName," +
                    "B_CurrentHP," +
                    "B_Score," +
                    "BttackedCount,BKilledCount,BTaskAssigneeCount,BTaskAssigneeKilldedCount,BNonTaskAssigneeKilldedCount,BEscapedCount,BEscapedPartnerKilledSelf,BEscapedSelfKilledSelf," +
                    "\n");
            }
        }

        string jst = GetCurrentJST();
        //stepCounter += 1 * 1000;
        string line =
            $"{jst}," +
            $"{SubjectManager.Instance.SubjectID}," +
            $"{ActionLogController.Instance.ActionLog_StageID}," +
            $"{ActionLogController.Instance.EnemyCount}," +
            // Total
            $"{ActionLogController.Instance.ActionLog_Score_Total}," +
            $"{t.attackedCount},{t.killedCount},{t.taskAssigneeCount},{t.taskAssigneeKilldedCount},{t.nonTaskAssigneeKilldedCount},{t.escapedCount},{t.escapedPartnerKilledSelf},{t.escapedSelfKilledSelf}," +
            // A
            $"{ActionLogController.Instance.ActionLog_PlayerID_A}," +
            $"{ActionLogController.Instance.ActionLog_BehaviorTypeName_A}," +
            $"{ActionLogController.Instance.ActionLog_CurrentHP_A}," +
            $"{ActionLogController.Instance.ActionLog_Score_A}," +
            $"{a.attackedCount},{a.killedCount},{a.taskAssigneeCount},{a.taskAssigneeKilldedCount},{a.nonTaskAssigneeKilldedCount},{a.escapedCount},{a.escapedPartnerKilledSelf},{a.escapedSelfKilledSelf}," +
            // B
            $"{ActionLogController.Instance.ActionLog_PlayerID_B}," +
            $"{ActionLogController.Instance.ActionLog_BehaviorTypeName_B}," +
            $"{ActionLogController.Instance.ActionLog_CurrentHP_B}," +
            $"{ActionLogController.Instance.ActionLog_Score_B}," +
            $"{b.attackedCount},{b.killedCount},{b.taskAssigneeCount},{b.taskAssigneeKilldedCount},{b.nonTaskAssigneeKilldedCount},{b.escapedCount},{b.escapedPartnerKilledSelf},{b.escapedSelfKilledSelf}," +
            $"{jst}\n";
        File.AppendAllText(logFilePath, line);
    }

    // 日本の現在時間の取得
    public string GetCurrentJST()
    {
        // 協定世界時 (UTC) を取得
        DateTime utcNow = DateTime.UtcNow;

        // JSTはUTC+9時間なので、9時間を加算する
        DateTime jstTime = utcNow.AddHours(9);

        return jstTime.ToString("yyyy/MM/dd HH:mm:ss");
    }

    // 書き込みたい内容...撃破数、撃ち漏らし補助
}