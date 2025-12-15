using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SocialPlatforms;

public class GameManager : MonoBehaviour
{
    [Header("倍速")]
    [SerializeField] float simulationSpeed = 1f;

    void Awake()
    {
        // 学習開始時に時間スケールを設定する
        //Time.timeScale = simulationSpeed;
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
}
