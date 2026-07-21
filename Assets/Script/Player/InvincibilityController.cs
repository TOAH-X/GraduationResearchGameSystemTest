using UnityEngine;

public class InvincibilityController : MonoBehaviour
{
    [Header("無敵時間")]
    [SerializeField] float invincibilityTime = 1.0f;
    [Header("無敵時間のタイマー")]
    [SerializeField] float invincibilityTimer;


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (invincibilityTimer > 0)
        {
            invincibilityTimer -= Time.fixedDeltaTime;
        }
    }

    // 無敵かどうか
    public bool IsInvincibility() 
    { 
        // 無敵
        if(invincibilityTimer > 0) 
        {
            return true;
        }
        // 無敵じゃない
        else 
        {
            return false;
        }
    }

    // 無敵時間更新
    public void UpdateInvincibility(float num) 
    {
        if (invincibilityTimer <= num) 
        {
            invincibilityTimer = num;
        }
    }
}
