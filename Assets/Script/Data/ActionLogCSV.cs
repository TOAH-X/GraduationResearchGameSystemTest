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
            File.WriteAllText(logFilePath, "JST,isKilled,isEscape,taskAssigneeID_Player,attackID\n");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    /*
    // ‘‚«‚İ(step,time,hp,reward,state)
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

    // JST,”íŒ±ÒID,ActionDataGroup(Total,A,B)
    // ‘‚«‚İ(step,time,hp,reward,state)
    public void UpdateData(ActionDataGroup t, ActionDataGroup a, ActionDataGroup b)
    {
        if (string.IsNullOrEmpty(logFilePath))
        {
            logFilePath = Path.Combine(Application.persistentDataPath, fileName + "StatsLog.csv");
        }

        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "JST," +
                "TotalKilledCount,TotalTaskAssigneeCount,TotalTaskAssigneeKilldedCount,TotalNonTaskAssigneeKilldedCount,TotalEscapedCount,TotalEscapedPartnerKilledSelf,TotalEscapedSelfKilledSelf," +
                "AKilledCount,ATaskAssigneeCount,ATaskAssigneeKilldedCount,ANonTaskAssigneeKilldedCount,AEscapedCount,AEscapedPartnerKilledSelf,AEscapedSelfKilledSelf," +
                "BKilledCount,BTaskAssigneeCount,BTaskAssigneeKilldedCount,BNonTaskAssigneeKilldedCount,BEscapedCount,BEscapedPartnerKilledSelf,BEscapedSelfKilledSelf," +
                "attackID\n") ;
        }

        string jst = GetCurrentJST();
        //stepCounter += 1 * 1000;
        string line =
            $"{jst}," +
            // Total
            $"{t.killedCount},{t.taskAssigneeCount},{t.taskAssigneeKilldedCount},{t.nonTaskAssigneeKilldedCount},{t.escapedCount},{t.escapedPartnerKilledSelf},{t.escapedSelfKilledSelf}," +
            // Group A
            $"{a.killedCount},{a.taskAssigneeCount},{a.taskAssigneeKilldedCount},{a.nonTaskAssigneeKilldedCount},{a.escapedCount},{a.escapedPartnerKilledSelf},{a.escapedSelfKilledSelf}," +
            // Group B
            $"{b.killedCount},{b.taskAssigneeCount},{b.taskAssigneeKilldedCount},{b.nonTaskAssigneeKilldedCount},{b.escapedCount},{b.escapedPartnerKilledSelf},{b.escapedSelfKilledSelf}," +
            $"{jst}\n";
        File.AppendAllText(logFilePath, line);
    }

    // “ú–{‚ÌŒ»İŠÔ‚Ìæ“¾
    public string GetCurrentJST()
    {
        // ‹¦’è¢ŠE (UTC) ‚ğæ“¾
        DateTime utcNow = DateTime.UtcNow;

        // JST‚ÍUTC+9ŠÔ‚È‚Ì‚ÅA9ŠÔ‚ğ‰ÁZ‚·‚é
        DateTime jstTime = utcNow.AddHours(9);

        return jstTime.ToString("yyyy/MM/dd HH:mm:ss");
    }

    // ‘‚«‚İ‚½‚¢“à—e...Œ‚”j”AŒ‚‚¿˜R‚ç‚µ•â•
}