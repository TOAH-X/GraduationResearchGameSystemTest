using System;
using UnityEngine;

public class PlayerBase : MonoBehaviour
{
    [Header("プレイヤーID")]
    [SerializeField] int playerID = 1;
    [Header("現在の残機")]
    [SerializeField] int currentHp = 3;

    [SerializeField] PlayerAgent playerAgentScript;     // PlayerAgentのスクリプト

    [SerializeField] InvincibilityController invincibilityControllerScript; // InvincibilityTimeのスクリプト

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        
    }

    /// <summary>
    /// プレイヤーの被ダメージ
    /// </summary>
    /// <param name="damage">ダメージ量</param>
    public void Damage(int damage) 
    {
        if (invincibilityControllerScript.IsInvincibility() == false)  
        {
            currentHp -= damage;                                        // ダメージ反映
            invincibilityControllerScript.UpdateInvincibility(3.0f);    // 無敵時間更新
                                                                        // 被弾したときの報酬減少
            playerAgentScript.AddReward(-300f);
        }
    }

    // playerID参照(getset)
    public int PlayerID
    {
        get { return playerID; }
        set { playerID = value; }
    }
    // currentHP参照(getset)
    public int CurrentHP
    {
        get { return currentHp; }
        set { currentHp= value; }
    }

}
