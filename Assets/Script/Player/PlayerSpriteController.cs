using UnityEngine;

public class PlayerSpriteController : MonoBehaviour
{
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] Sprite playerCenterSprite;         // 中央画像
    [SerializeField] Sprite playerLeftSprite;           // 左画像
    [SerializeField] Sprite playerRIghtSprite;          // 右画像

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void UpdateSprite(float horizontalInput)
    {
        if (horizontalInput > 0)
        {
            if (spriteRenderer.sprite != playerRIghtSprite)
                spriteRenderer.sprite = playerRIghtSprite;
        }
        else if (horizontalInput < 0)
        {
            if (spriteRenderer.sprite != playerLeftSprite)
                spriteRenderer.sprite = playerLeftSprite;
        }
        else
        {
            if (spriteRenderer.sprite != playerCenterSprite)
                spriteRenderer.sprite = playerCenterSprite;
        }
    }

    public void UpdateCenterSprite() 
    {
        spriteRenderer.sprite = playerCenterSprite;
    }
}
