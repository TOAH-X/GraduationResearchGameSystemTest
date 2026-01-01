using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MLAgents;
using UnityEngine;

public class RewardManager : MonoBehaviour
{
    [SerializeField] Agent agent;

    //[SerializeField] string playerType;

    [Header("•ñVF“GUŒ‚")]      //0.5
    [SerializeField] float attackReward = 0.5f;
    [Header("•ñVF“GŒ‚”j")]      //5
    [SerializeField] float killReward = 5f;
    [Header("•ñVF‘Ï‹v")]        //0.01
    [SerializeField] float survivalReward = 0.01f;
    [Header("•ñV(•‰)F—©‚é")]    //-0.1
    [SerializeField] float grazePenalty = -0.1f;
    [Header("•ñV(•‰)F”í’e")]    //-10
    [SerializeField] float hitPenalty = -10f;
    [Header("•ñV(•‰)F“V”±")]    //-50
    [SerializeField] float punishPenalty = -50f;
    [Header("•ñVF“V—S")]        //50
    [SerializeField] float graceReward = 50f;

    List<RewardType> rewardList = new List<RewardType>();
    // •ñV‡Œv
    public float RewardSum { get; set; } = 0;

    // •ñV
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

    // •ñV‚Ìæ“¾
    public int GetRewardCount(RewardType rewardType)
    {
        return rewardList.Count(r => r == rewardType);
    }
}

// •ñV–¼’è‹`
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