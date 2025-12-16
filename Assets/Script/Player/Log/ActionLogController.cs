using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ActionLogController : SafeSingleton<ActionLogController>
{
    [SerializeField] ActionLogCSV actionLogCSV;

    private int killedCountA = 0;
    private int killedCountB = 0;
    private int escapedCountA = 0;
    private int escapedCountB = 0;

    // Start is called before the first frame update
    void Start()
    {

    }

    // 初期化したい内容
    protected override void OnSingletonAwake()
    {
        base.OnSingletonAwake();

        Debug.Log("Cooperation Manager が安全に初期化されました。");

        // ここでデータの初期化や必要なオブジェクトの参照取得を行う
    }

    void FixedUpdate()
    {

    }

    // 撃ち漏らし補助
    // isKilled(倒されたのか),isEscape(撃ち漏らしが発生したか),taskAssigneeID_Player(責任者),attackID(攻撃者),true/false(協調成立か)
    public void AddIsEscapeData(bool isKilled, bool isEscape, int taskAssigneeID_Player, int attackID)
    {
        actionLogCSV.UpdateData(isKilled, isEscape, taskAssigneeID_Player, attackID);
        // 協調成立かはtaskAssigneeID_PlayerとattackIDを見る
        if (isEscape == true)
        {
            // 協調行動成立
            if (taskAssigneeID_Player == attackID)
            {
                
            }
            // 協調行動不成立
            else
            {
                //
            }
        }
        // 不成立
        else 
        { 
        }
    }
}
