using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrazeController : MonoBehaviour
{
    [SerializeField] RewardManager rewardManager;

    [Header("PlayerType、PlayerかSubPlayerか。取得できるように変更しておくこと")]
    [SerializeField] string playerType;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // 必ずリファクタリングを行うこと
    public void AddReward() 
    {
        // グレイズすると負の報酬
        rewardManager.AddReward(RewardType.GrazePenalty);
    }
}
