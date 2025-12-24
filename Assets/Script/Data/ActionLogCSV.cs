using System.Collections;
using System.Collections.Generic;
using System.IO.Abstractions;
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

    // ‘‚«‚İ(step,time,hp,reward,state)
    public void UpdateData(ActionDataGroup statsTotal, ActionDataGroup statsA, ActionDataGroup statsB)
    {
        string jst = GetCurrentJST();
        //stepCounter += 1 * 1000;
        string line =
            $"{jst}," +
            //$"{isKilled}," +
            //$"{isEscape}," +
            //$"{taskAssigneeID_Player}," +
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

        Debug.Log("Œ»İ‚Ì“ú–{ŠÔ (ŠÈˆÕ”Å): " + jstTime.ToString("yyyy/MM/dd HH:mm:ss"));

        return jstTime.ToString("yyyy/MM/dd HH:mm:ss");
    }

    // ‘‚«‚İ‚½‚¢“à—e...Œ‚”j”AŒ‚‚¿˜R‚ç‚µ•â•
}