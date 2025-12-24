using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SocialPlatforms;

public class GameManager : MonoBehaviour
{
    [Header("倍速")]
    [SerializeField] float simulationSpeed = 1f;

    // ゲーム終了用
    [SerializeField] GameObject playerObj;
    [SerializeField] GameObject playerAnotherObj;
    [SerializeField] GameObject subPlayerObj;
    [SerializeField] GameObject subPlayerAnotherObj;

    [SerializeField] PlayerAgent playerAgent_Player;
    [SerializeField] PlayerAgent playerAgent_PlayerAnother;
    [SerializeField] SubPlayerAgent subPlayerAgent_SubPlayer;
    [SerializeField] SubPlayerAgent subPlayerAgent_SubPlayerAnother;

    void Awake()
    {
        // 学習開始時に時間スケールを設定する
        if (simulationSpeed != 1) 
        {
            Time.timeScale = simulationSpeed;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        SoundManager.PlayBGM(BGMType.GameMain1);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // ゲーム終了時処理、可能ならデリゲートに書き換えること
    public void EndGame() 
    {
        if (playerObj != null)
        {
            playerAgent_Player.EndGame();
        }
        if (playerAnotherObj != null) 
        { 
            playerAgent_PlayerAnother.EndGame();
        }
        if (subPlayerObj != null)
        {
            subPlayerAgent_SubPlayer.EndGame();
        }
        if (subPlayerAnotherObj != null) 
        {
            subPlayerAgent_SubPlayerAnother.EndGame();
        }
    }
}
