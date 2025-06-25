using UnityEngine;

public class InvincibilityController : MonoBehaviour
{
    [Header("–³“GŠÔ")]
    [SerializeField] float invincibilityTime = 1.0f;
    [Header("–³“GŠÔ‚Ìƒ^ƒCƒ}[")]
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
            invincibilityTimer -= Time.deltaTime;
        }
    }

    // –³“G‚©‚Ç‚¤‚©
    public bool IsInvincibility() 
    { 
        // –³“G
        if(invincibilityTimer > 0) 
        {
            return true;
        }
        // –³“G‚¶‚á‚È‚¢
        else 
        {
            return false;
        }
    }

    // –³“GŠÔXV
    public void UpdateInvincibility(float num) 
    {
        if (invincibilityTimer <= num) 
        {
            invincibilityTimer = num;
        }
    }
}
