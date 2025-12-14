using UnityEngine;
using System.IO;

public class DataOutput : MonoBehaviour
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
            File.WriteAllText(logFilePath, "Step,Time,HP,reward,AttackReward,KillReward,SurvivalReward,GrazePenalty,HitPenalty,PunishPenalty,GraceReward,State\n");
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

    // èëÇ´çûÇ›(step,time,hp,reward,state)
    public void UpdateData(int step, float time, float hp, string state)
    {
        stepCounter += 1 * 1000;
        string line =
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
}
