using UnityEngine;
using System.IO;
using System;
using Unity.MLAgents;

public class LearningLogCSV : MonoBehaviour
{
    [SerializeField] PlayerBase playerBase;
    [SerializeField] RewardManager rewardManager;
    string fileName = "";

    private string logFilePath;

    private int stepCounter = 0;

    void Awake()
    {
        // 学習関係が動いているときは止める(多重起動で故障を防ぐため)
        if (Academy.Instance.IsCommunicatorOn == true) return;

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

    // 書き込み(step,time,hp,reward,state)
    public void UpdateData(int step, float time, float hp, string state)
    {
        // 学習関係が動いているときは止める(多重起動で故障を防ぐため)
        if (Academy.Instance.IsCommunicatorOn == true) return;

        // パスが未設定ならその場で作る（絶対にNullを回避する）
        if (string.IsNullOrEmpty(logFilePath)) 
        {
            string pName = (playerBase != null) ? playerBase.PlayerName : "UnknownPlayer";
            // dataPathよりも書き込み権限が安定している persistentDataPath を推奨
            logFilePath = Path.Combine(Application.persistentDataPath, pName + "StatsLog.csv");
        }

        // ヘッダーがない（ファイルがない）場合は作成
        if (!File.Exists(logFilePath))
        {
            File.WriteAllText(logFilePath, "JST,Step,Time,HP,reward,AttackReward,KillReward,SurvivalReward,GrazePenalty,HitPenalty,PunishPenalty,GraceReward,State\n");
        }

        string jst = GetCurrentJST();
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

        try
        {
            // 書き込み
            File.AppendAllText(logFilePath, line);
        }
        catch (Exception e)
        {
            Debug.LogError($"CSV書き込み失敗: {e.Message}");
        }
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
}
