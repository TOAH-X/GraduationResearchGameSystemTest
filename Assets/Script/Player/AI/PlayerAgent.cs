using UnityEngine;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine.SceneManagement;

public class PlayerAgent : Agent
{
    public Transform taraget;

    Rigidbody2D rb2D;

    [SerializeField] PlayerBase playerBaseScript;   // PlayerBaseスクリプト
    [SerializeField] PlayerMoveController playerMoveControllerScript;   // PlayerMoveControllerスクリプト
    [SerializeField] PlayerShotController playerShotControllerScript;   // PlayerShotControllerスクリプト

    [SerializeField] Transform bulletParent;        // BulletParentのTransform
    [SerializeField] Transform enemyParent;         // EnemyParentのTransform



    // Start is called before the first frame update
    void Start()
    {
        
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    
    // ゲームオブジェクト生成時に呼ばれる
    public override void Initialize() 
    {
        this.rb2D = GetComponent<Rigidbody2D>();
    }

    // エピソード開始時に呼ばれる
    public override void OnEpisodeBegin() 
    {
        // リロード
        //SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
    
    // 観察取得時に呼ばれる
    public override void CollectObservations(VectorSensor vectorSensor)//posかも？
    {
        // 自身の位置を取得
        vectorSensor.AddObservation(transform.position.x);
        vectorSensor.AddObservation(transform.position.y);

        // 弾幕の座標の取得(のちのち1フレーム前の座標も取得しておくと精度が上がるかもしれない)
        int maxBulletsToObserve = 1000;     // バレットプーラから取得すること
        int count = 0;

        foreach (Transform bullet in bulletParent)
        {
            if (bullet.gameObject.activeSelf)
            {
                Vector2 relativePos = bullet.position - transform.position; // 相対位置でもOK
                vectorSensor.AddObservation(relativePos.x);
                vectorSensor.AddObservation(relativePos.y);
                count++;

                if (count >= maxBulletsToObserve)
                    break;
            }
        }

        // 観測数が max に満たない場合、ゼロ埋め
        for (int i = count; i < maxBulletsToObserve; i++)
        {
            vectorSensor.AddObservation(0f); // x
            vectorSensor.AddObservation(0f); // y
        }

        // 弾幕の座標の取得(のちのち1フレーム前の座標も取得しておくと精度が上がるかもしれない)
        int maxEnemysToObserve = 100;     // エネミープーラから取得すること
        int count2 = 0;

        foreach (Transform bullet in enemyParent)
        {
            if (bullet.gameObject.activeSelf)
            {
                Vector2 relativePos = bullet.position - transform.position; // 相対位置でもOK
                vectorSensor.AddObservation(relativePos.x);
                vectorSensor.AddObservation(relativePos.y);
                count2++;

                if (count2 >= maxEnemysToObserve)
                    break;
            }
        }

        // 観測数が max に満たない場合、ゼロ埋め
        for (int i = count2; i < maxEnemysToObserve; i++)
        {
            vectorSensor.AddObservation(0f); // x
            vectorSensor.AddObservation(0f); // y
        }
    }
    
    // 行動決定時に呼ばれる
    public override void OnActionReceived(ActionBuffers actionBuffers) 
    {
        float moveSpeed = 5.0f;
        //float slowSpeedRate = 0.5f;

        // 行動：連続値2つ（横・縦）を取得
        float h = Mathf.Clamp(actionBuffers.ContinuousActions[0], -1f, 1f);
        float v = Mathf.Clamp(actionBuffers.ContinuousActions[1], -1f, 1f);

        // 移動
        playerMoveControllerScript.MovePlayer(h, v);

        // ショットするかどうかの値を取得(正の値をショット可とする)
        float rawValue = Mathf.Clamp(actionBuffers.ContinuousActions[2], -1f, 1f);
        bool isShot = false;
        if (rawValue > 0)
        {
            isShot = true;
        }
        else
        { 
            isShot = false;
        }

        // ショット
        playerShotControllerScript.ShotTrigger(isShot);

        // 報酬設計：生きてるほど報酬 +0.5
        AddReward(0.5f);
        // 報酬…被弾(負)、敵に弾を当てる
        

        // エピソード終了(HPが0のとき)
        if (playerBaseScript.CurrentHP <= 0)  
        {
            EndEpisode();

            playerBaseScript.CurrentHP = 3;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        
    }

    // ヒューリスティックモード
    public override void Heuristic(in ActionBuffers actionBuffers) 
    {
        var actionsOut = actionBuffers.ContinuousActions;
        // 移動
        actionsOut[0] = Input.GetAxis("Horizontal");
        actionsOut[1] = Input.GetAxis("Vertical");
        // ショット
        actionsOut[2] = Input.GetKey(KeyCode.Z) ? 1f : -1f; // trueは1、falseは-1を出力する。
        // 低速モード
        //actionsOut[3] = Input.GetKey(KeyCode.LeftShift) ? 1f : -1f; // trueは1、falseは-1を出力する。
    }
}
