using Cysharp.Threading.Tasks;
using System;
using UnityEngine;

public class EnemySpawnerController : MonoBehaviour
{
    [Header("エネミープレハブ")]
    [SerializeField] GameObject enemyPrefab;    //enemyオブジェクトプレハブ
    [Header("エネミー格納先(親)")]
    [SerializeField] GameObject enemyParentObj;

    float timer = 0f;

    int testflag = 0;

    // Start is called before the first frame update
    void Start()
    {
        
    }
    
    // Update is called once per frame
    void FixedUpdate()
    {
        
        timer += Time.deltaTime;
        if (timer >= 3.0f)
        {
            if (testflag % 2 == 0)
            {
                EnemyGroupSpawn1(5, 0.2f);
            }
            else 
            {
                EnemyGroupSpawn2(5, 0.2f);
            }
            testflag++;
            timer = 0;
        }
    }

    /// <summary>
    /// 敵の生成
    /// </summary>
    /// <param name="enemyID">敵のID</param>
    /// <param name="pos">敵の生成位置</param>
    public void EnemySpawn(int enemyID, Vector3 pos)
    {
        if (enemyParentObj == null)
        {
            enemyParentObj = GameObject.Find("EnemyParent");      //重いので余裕があればインスタンス化
        }
        var enemyPrefabs = Instantiate(enemyPrefab, pos, Quaternion.identity, enemyParentObj.transform);
        //enemyPrefabs.transform.localEulerAngles = new Vector3(0, 0, angleDegree);
        EnemyBase enemyBaseScript = enemyPrefabs.GetComponent<EnemyBase>();
        enemyBaseScript.EnemyID = enemyID;
    }

    /// <summary>
    /// 敵をグループとして生成
    /// </summary>
    /// <param name="enemySpawnCount">敵の生成数</param>
    /// <param name="enemySpawnInterval">敵の生成間隔(秒)</param>
    public async void EnemyGroupSpawn1(int enemySpawnCount, float enemySpawnInterval)
    {
        for (int i = 0; i < enemySpawnCount; i++) 
        {
            // 敵の生成
            EnemySpawn(1, new Vector3(-10, 5));
            EnemySpawn(2, new Vector3(10, 5));

            await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval));
        }
    }

    /// <summary>
    /// 敵をグループとして生成
    /// </summary>
    /// <param name="enemySpawnCount">敵の生成数</param>
    /// <param name="enemySpawnInterval">敵の生成間隔(秒)</param>
    public async void EnemyGroupSpawn2(int enemySpawnCount, float enemySpawnInterval)
    {
        for (int i = 0; i < enemySpawnCount; i++)
        {
            // 敵の生成
            EnemySpawn(3, new Vector3(-10, -3));
            EnemySpawn(4, new Vector3(10, -3));

            await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval));
        }
    }
}
