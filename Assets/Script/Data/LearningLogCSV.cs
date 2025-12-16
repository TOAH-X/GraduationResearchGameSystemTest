using UnityEngine;
using System.IO;
using System;

public class LearningLogCSV : MonoBehaviour
{
    [SerializeField] PlayerBase playerBase;
    [SerializeField] RewardManager rewardManager;
    string fileName = "";

    private string logFilePath;

    private int stepCounter = 0;

    void Awake()
    {
        fileName = playerBase.PlayerName;
        logFilePath = Path.Combine(Application.dataPath, fileName + "StatsLog.csv");

        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "JST,Step,Time,HP,reward,AttackReward,KillReward,SurvivalReward,GrazePenalty,HitPenalty,PunishPenalty,GraceReward,State\n");
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // ‘‚«‚İ(step,time,hp,reward,state)
    public void UpdateData(int step, float time, float hp, string state)
    {
        string jst = GetCurrentJST();
        stepCounter += 1 * 1000;
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
}
