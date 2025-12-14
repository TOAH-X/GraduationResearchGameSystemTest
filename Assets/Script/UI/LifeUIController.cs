using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LifeUIController : MonoBehaviour
{
    [SerializeField] PlayerBase playerBaseScript;       // playerBaseのスクリプト
    [SerializeField] GameObject lifeObj;                   // 残機のImage

    [SerializeField] Transform lifeContainer;           // ハートを並べる親

    private List<GameObject> heartList = new List<GameObject>();

    // Start is called before the first frame update
    void Start()
    {
        CreateHearts();
    }

    // Update is called once per frame
    void Update()
    {
        UpdateHearts();
    }

    void CreateHearts()
    {
        int maxHp = playerBaseScript.CurrentHP;

        for (int i = 0; i < maxHp; i++)
        {
            GameObject heart = Instantiate(lifeObj, lifeContainer);
            heartList.Add(heart);
        }
    }

    void UpdateHearts()
    {
        int currentHp = playerBaseScript.CurrentHP;

        for (int i = 0; i < heartList.Count; i++)
        {
            Image heartImage = heartList[i].GetComponent<Image>();
            if (heartImage != null)
            {
                heartImage.color = i < currentHp ? Color.red : Color.gray;
            }
        }
    }
}
