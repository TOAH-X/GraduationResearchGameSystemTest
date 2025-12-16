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

    // 書き込み(step,time,hp,reward,state)
    public void UpdateData(bool isKilled, bool isEscape, int taskAssigneeID_Player, int attackID)
    {
        string jst = GetCurrentJST();
        //stepCounter += 1 * 1000;
        string line =
            $"{jst}," +
            $"{isKilled}," +
            $"{isEscape}," +
            $"{taskAssigneeID_Player}," +
            $"{attackID}\n";
        File.AppendAllText(logFilePath, line);
    }

    // 日本の現在時間の取得
    public string GetCurrentJST()
    {
        // 協定世界時 (UTC) を取得
        DateTime utcNow = DateTime.UtcNow;

        // JSTはUTC+9時間なので、9時間を加算する
        DateTime jstTime = utcNow.AddHours(9);

        Debug.Log("現在の日本時間 (簡易版): " + jstTime.ToString("yyyy/MM/dd HH:mm:ss"));

        return jstTime.ToString("yyyy/MM/dd HH:mm:ss");
    }
}

// 書き込みたい内容...撃破数、撃ち漏らし補助、、