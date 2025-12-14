using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class PlayerSpriteController : MonoBehaviour
{
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] Sprite playerCenterSprite;         // ’†‰›‰æ‘œ
    [SerializeField] Sprite playerLeftSprite;           // ¶‰æ‘œ
    [SerializeField] Sprite playerRIghtSprite;          // ‰E‰æ‘œ

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
