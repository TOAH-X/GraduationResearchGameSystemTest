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
                    "A_Left_Bottom,A_Left_Middle,A_Left_Top,A_Center_Bottom,A_Center_Middle,A_Center_Top,A_Right_Bottom,A_Right_Middle,A_Right_Top," +
                    "A_Pos_Bottom,A_Pos_Middle_Y,A_Pos_Top,A_Pos_Left,A_Pos_Center_X,A_Pos_Right," +
                    "A_Relationship_Avg_Distance,A_Relationship_Spread_Rate," +
                    "A_BehaviorTypeName," +
                    "A_CurrentHP," +
                    "A_Score," +
                    "AAttackedCount,AKilledCount,ATaskAssigneeCount,ATaskAssigneeKilldedCount,ANonTaskAssigneeKilldedCount,AEscapedCount,AEscapedPartnerKilledSelf,AEscapedSelfKilledSelf," +
                    "B_ID," +
                    "B_Left_Bottom,B_Left_Middle,B_Left_Top,B_Center_Bottom,B_Center_Middle,B_Center_Top,B_Right_Bottom,B_Right_Middle,B_Right_Top," +
                    "B_Pos_Bottom,B_Pos_Middle_Y,B_Pos_Top,B_Pos_Left,B_Pos_Center_X,B_Pos_Right, " +
                    "B_Relationship_Avg_Distance,B_Relationship_Spread_Rate," +
                    "B_BehaviorTypeName," +
                    "B_CurrentHP," +
                    "B_Score," +
                    "BAttackedCount,BKilledCount,BTaskAssigneeCount,BTaskAssigneeKilldedCount,BNonTaskAssigneeKilldedCount,BEscapedCount,BEscapedPartnerKilledSelf,BEscapedSelfKilledSelf," +
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
                    "A_Left_Bottom,A_Left_Middle,A_Left_Top,A_Center_Bottom,A_Center_Middle,A_Center_Top,A_Right_Bottom,A_Right_Middle,A_Right_Top," +
                    "A_Pos_Bottom,A_Pos_Middle_Y,A_Pos_Top,A_Pos_Left,A_Pos_Center_X,A_Pos_Right," +
                    "A_Relationship_Avg_Distance,A_Relationship_Spread_Rate," +
                    "A_BehaviorTypeName," +
                    "A_CurrentHP," +
                    "A_Score," +
                    "AAttackedCount,AKilledCount,ATaskAssigneeCount,ATaskAssigneeKilldedCount,ANonTaskAssigneeKilldedCount,AEscapedCount,AEscapedPartnerKilledSelf,AEscapedSelfKilledSelf," +
                    "B_ID," +
                    "B_Left_Bottom,B_Left_Middle,B_Left_Top,B_Center_Bottom,B_Center_Middle,B_Center_Top,B_Right_Bottom,B_Right_Middle,B_Right_Top," +
                    "B_Pos_Bottom,B_Pos_Middle_Y,B_Pos_Top,B_Pos_Left,B_Pos_Center_X,B_Pos_Right, " +
                    "B_Relationship_Avg_Distance,B_Relationship_Spread_Rate," +
                    "B_BehaviorTypeName," +
                    "B_CurrentHP," +
                    "B_Score," +
                    "BAttackedCount,BKilledCount,BTaskAssigneeCount,BTaskAssigneeKilldedCount,BNonTaskAssigneeKilldedCount,BEscapedCount,BEscapedPartnerKilledSelf,BEscapedSelfKilledSelf," +
                    "\n");
            }
        }

        string jst = GetCurrentJST();
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
            $"{ActionLogController.Instance.ActionLog_PosValue_A[0]},{ActionLogController.Instance.ActionLog_PosValue_A[1]},{ActionLogController.Instance.ActionLog_PosValue_A[2]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_A[3]},{ActionLogController.Instance.ActionLog_PosValue_A[4]},{ActionLogController.Instance.ActionLog_PosValue_A[5]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_A[6]},{ActionLogController.Instance.ActionLog_PosValue_A[7]},{ActionLogController.Instance.ActionLog_PosValue_A[8]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_A[9]},{ActionLogController.Instance.ActionLog_PosValue_A[10]},{ActionLogController.Instance.ActionLog_PosValue_A[11]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_A[12]},{ActionLogController.Instance.ActionLog_PosValue_A[13]},{ActionLogController.Instance.ActionLog_PosValue_A[14]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_A[15]},{ActionLogController.Instance.ActionLog_PosValue_A[16]}," +
            $"{ActionLogController.Instance.ActionLog_BehaviorTypeName_A}," +
            $"{ActionLogController.Instance.ActionLog_CurrentHP_A}," +
            $"{ActionLogController.Instance.ActionLog_Score_A}," +
            $"{a.attackedCount},{a.killedCount},{a.taskAssigneeCount},{a.taskAssigneeKilldedCount},{a.nonTaskAssigneeKilldedCount},{a.escapedCount},{a.escapedPartnerKilledSelf},{a.escapedSelfKilledSelf}," +
            // B
            $"{ActionLogController.Instance.ActionLog_PlayerID_B}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_B[0]},{ActionLogController.Instance.ActionLog_PosValue_B[1]},{ActionLogController.Instance.ActionLog_PosValue_B[2]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_B[3]},{ActionLogController.Instance.ActionLog_PosValue_B[4]},{ActionLogController.Instance.ActionLog_PosValue_B[5]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_B[6]},{ActionLogController.Instance.ActionLog_PosValue_B[7]},{ActionLogController.Instance.ActionLog_PosValue_B[8]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_B[9]},{ActionLogController.Instance.ActionLog_PosValue_B[10]},{ActionLogController.Instance.ActionLog_PosValue_B[11]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_B[12]},{ActionLogController.Instance.ActionLog_PosValue_B[13]},{ActionLogController.Instance.ActionLog_PosValue_B[14]}," +
            $"{ActionLogController.Instance.ActionLog_PosValue_B[15]},{ActionLogController.Instance.ActionLog_PosValue_B[16]}," +
            $"{ActionLogController.Instance.ActionLog_BehaviorTypeName_B}," +
            $"{ActionLogController.Instance.ActionLog_CurrentHP_B}," +
            $"{ActionLogController.Instance.ActionLog_Score_B}," +
            $"{b.attackedCount},{b.killedCount},{b.taskAssigneeCount},{b.taskAssigneeKilldedCount},{b.nonTaskAssigneeKilldedCount},{b.escapedCount},{b.escapedPartnerKilledSelf},{b.escapedSelfKilledSelf}," +
            $"\n";
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