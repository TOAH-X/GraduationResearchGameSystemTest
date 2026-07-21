using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MLAgents;
using UnityEngine;

public class RewardManager : MonoBehaviour
{
    [SerializeField] Agent agent;

    //[SerializeField] string playerType;

    [Header("報酬：敵攻撃")]      //0.5
    [SerializeField] float attackReward = 0.5f;
    [Header("報酬：敵撃破")]      //5
    [SerializeField] float killReward = 5f;
    [Header("報酬：耐久")]        //0.01
    [SerializeField] float survivalReward = 0.01f;
    [Header("報酬(負)：掠る")]    //-0.1
    [SerializeField] float grazePenalty = -0.1f;
    [Header("報酬(負)：被弾")]    //-10
    [SerializeField] float hitPenalty = -10f;
    [Header("報酬(負)：天罰")]    //-50
    [SerializeField] float punishPenalty = -50f;
    [Header("報酬：天祐")]        //50
    [SerializeField] float graceReward = 50f;

    List<RewardType> rewardList = new List<RewardType>();
    // 報酬合計
    public float RewardSum { get; set; } = 0;

    // 報酬
    public void AddReward(RewardType rewardType)
    {
        float reward = rewardType switch
        {
            RewardType.AttackReward => attackReward,
            RewardType.KillReward => killReward,
            RewardType.SurvivalReward => survivalReward,
            RewardType.GrazePenalty => grazePenalty,
            RewardType.HitPenalty => hitPenalty,
            RewardType.PunishPenalty => punishPenalty,
            RewardType.GraceReward => graceReward,
            _ => throw new InvalidOperationException()
        };


        rewardList.Add(rewardType);

        agent.AddReward(reward);
        RewardSum += reward;
    }

    // 報酬の取得
    public int GetRewardCount(RewardType rewardType)
    {
        return rewardList.Count(r => r == rewardType);
    }
}

// 報酬名定義
public enum RewardType
{
    AttackReward,
    KillReward,
    SurvivalReward,
    GrazePenalty,
    HitPenalty,
    PunishPenalty,
    GraceReward
}