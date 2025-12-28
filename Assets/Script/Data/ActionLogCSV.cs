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
            //File.WriteAllText(logFilePath, "JST,\n");
            File.WriteAllText(logFilePath, "JST,SubjectID," +
                "TotalKilledCount,TotalTaskAssigneeCount,TotalTaskAssigneeKilldedCount,TotalNonTaskAssigneeKilldedCount,TotalEscapedCount,TotalEscapedPartnerKilledSelf,TotalEscapedSelfKilledSelf," +
                "A_ID," +
                "A_BehaviorTypeName," +
                "A_CurrentHP," +
                "AKilledCount,ATaskAssigneeCount,ATaskAssigneeKilldedCount,ANonTaskAssigneeKilldedCount,AEscapedCount,AEscapedPartnerKilledSelf,AEscapedSelfKilledSelf," +
                "B_ID," +
                "B_BehaviorTypeName," +
                "B_CurrentHP," +
                "BKilledCount,BTaskAssigneeCount,BTaskAssigneeKilldedCount,BNonTaskAssigneeKilldedCount,BEscapedCount,BEscapedPartnerKilledSelf,BEscapedSelfKilledSelf," +
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

    // JST,被験者ID,ステージID,プレイヤーID,学習タイプ,CurrentHP,*報酬関係,*各スコア,ActionDataGroup(Total,A,B)※より充実させること,
    // 書き込み(step,time,hp,reward,state)
    public void UpdateData(ActionDataGroup t, ActionDataGroup a, ActionDataGroup b)
    {
        if (string.IsNullOrEmpty(logFilePath))
        {
            logFilePath = Path.Combine(Application.persistentDataPath, fileName + "StatsLog.csv");
        }

        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "JST,SubjectID,StageID," +
                "TotalKilledCount,TotalTaskAssigneeCount,TotalTaskAssigneeKilldedCount,TotalNonTaskAssigneeKilldedCount,TotalEscapedCount,TotalEscapedPartnerKilledSelf,TotalEscapedSelfKilledSelf," +
                "A_ID," +
                "A_BehaviorTypeName," +
                "A_CurrentHP," +
                "AKilledCount,ATaskAssigneeCount,ATaskAssigneeKilldedCount,ANonTaskAssigneeKilldedCount,AEscapedCount,AEscapedPartnerKilledSelf,AEscapedSelfKilledSelf," +
                "B_ID," +
                "B_BehaviorTypeName," +
                "B_CurrentHP," +
                "BKilledCount,BTaskAssigneeCount,BTaskAssigneeKilldedCount,BNonTaskAssigneeKilldedCount,BEscapedCount,BEscapedPartnerKilledSelf,BEscapedSelfKilledSelf," +
                "\n");
        }

        string jst = GetCurrentJST();
        //stepCounter += 1 * 1000;
        string line =
            $"{jst}," +
            $"{SubjectManager.Instance.SubjectID}," +
            $"{ActionLogController.Instance.ActionLog_StageID}," +
            // Total
            $"{t.killedCount},{t.taskAssigneeCount},{t.taskAssigneeKilldedCount},{t.nonTaskAssigneeKilldedCount},{t.escapedCount},{t.escapedPartnerKilledSelf},{t.escapedSelfKilledSelf}," +
            // A
            $"{ActionLogController.Instance.ActionLog_PlayerID_A}," +
            $"{ActionLogController.Instance.ActionLog_BehaviorTypeName_A}," +
            $"{ActionLogController.Instance.ActionLog_CurrentHP_A}," +
            $"{a.killedCount},{a.taskAssigneeCount},{a.taskAssigneeKilldedCount},{a.nonTaskAssigneeKilldedCount},{a.escapedCount},{a.escapedPartnerKilledSelf},{a.escapedSelfKilledSelf}," +
            // B
            $"{ActionLogController.Instance.ActionLog_PlayerID_B}," +
            $"{ActionLogController.Instance.ActionLog_BehaviorTypeName_B}," +
            $"{ActionLogController.Instance.ActionLog_CurrentHP_B}," +
            $"{b.killedCount},{b.taskAssigneeCount},{b.taskAssigneeKilldedCount},{b.nonTaskAssigneeKilldedCount},{b.escapedCount},{b.escapedPartnerKilledSelf},{b.escapedSelfKilledSelf}," +
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