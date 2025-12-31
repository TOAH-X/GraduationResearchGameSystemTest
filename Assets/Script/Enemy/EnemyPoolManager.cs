/*
using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;

/// <summary>
/// ★ 新規／差し替え用の安定した敵プールマネージャ
/// - PoolItem（obj, script, inUse）で管理
/// - GetEnemy() は内部初期化を行い、最後に SetActive(true) で返す（呼び出し側は位置設定後に SetActive(true) する方式も可能）。
/// - ReleaseEnemy() で inUse を切り、SetActive(false) で戻す
/// - GroupSpawn は UniTask + CancellationToken 対応（呼び出し時に token を渡す）
/// </summary>
public class EnemyPoolManager : MonoBehaviour
{
    public static EnemyPoolManager Instance { get; private set; }

    [Header("エネミープレハブ")]
    [SerializeField] GameObject enemyPrefab;
    [Header("エネミー格納先(親)")]
    [SerializeField] GameObject enemyParentObj;
    [Header("初期プール数")]
    [SerializeField] int initialPoolSize = 50;

    // internal pool item
    private class PoolItem
    {
        public GameObject obj;
        public EnemyBase script;
        public bool inUse;
    }

    private readonly List<PoolItem> pool = new List<PoolItem>();

    // --- 非同期制御用（スポーンのキャンセル管理） ---
    private CancellationTokenSource cts = null;

    private void Awake()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("[EnemyPoolManager] enemyPrefab is NULL or Missing!");
        }
        if (enemyParentObj == null) 
        {
            enemyParentObj = GameObject.Find("EnemyParent");
        }

        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this.gameObject);

        if (enemyParentObj == null)
        {
            // シーン内に parent が無ければ作る（Inspectorで割り当てているなら不要）
            var go = GameObject.Find("EnemyParent");
            if (go == null)
            {
                enemyParentObj = new GameObject("EnemyParent");
            }
            else enemyParentObj = go;
        }

        InitializePool();
    }

    private void OnDestroy()
    {
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
            cts = null;
        }
    }

    // プール初期化
    private void InitializePool()
    {
        pool.Clear();
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewItem();
        }
    }

    // 新しいプールアイテムを作る（内部用）
    private PoolItem CreateNewItem()
    {
        GameObject obj = Instantiate(enemyPrefab);
        obj.transform.SetParent(enemyParentObj.transform);
        obj.SetActive(false);

        PoolItem item = new PoolItem
        {
            obj = obj,
            script = obj.GetComponent<EnemyBase>(),
            inUse = false
        };

        if (item.script == null)
        {
            Debug.LogWarning("[EnemyPoolManager] Enemy prefab does not have EnemyBase component.");
        }

        pool.Add(item);
        return item;
    }

    /// <summary>
    /// 使用可能な敵を取得する（Get -> 内部リセット -> SetActive(true)）
    /// 呼び出し側は取得後に transform.position 等を設定しても良い（ただし SetActive(true) のタイミングを気にするなら
    /// ResetEnemy_InternalOnly を用意しているので、GetEnemy はアクティブ化せず返す設計にも変更できます）。
    /// ここでは「内部で最低限の初期化を済ませてからアクティブ化して返す」方式にしています。
    /// </summary>
    public GameObject GetEnemy()
    {
        // 1. 未使用オブジェクトを探す
        foreach (var item in pool)
        {
            if (item.obj != null && !item.inUse)
            {
                item.inUse = true;
                item.script.ResetEnemy_Internal();
                return item.obj;
            }
        }

        // 2. なければ増やす
        var newItem = CreateNewItem();
        newItem.inUse = true;
        newItem.script.ResetEnemy_Internal();
        return newItem.obj;
    }

    /// <summary>
    /// プールへ返却（Release）
    /// </summary>
    public void ReleaseEnemy(GameObject obj)
    {
        foreach (var item in pool)
        {
            if (item.obj == obj)
            {
                item.inUse = false;
                item.obj.SetActive(false);
                return;
            }
        }
    }

    // --- 以下：使いやすいスポーン/グループ関数（呼び出し側は CancellationToken を渡す） ---
    // 単体スポーン（位置はここで設定して最終的にアクティブ化）
    public GameObject SpawnEnemyAt(int enemyID, Vector3 pos)
    {
        var obj = GetEnemy();
        if (obj == null) return null;

        // set transform BEFORE any heavy script logic (we already called ResetEnemy_InternalOnly inside GetEnemy)
        obj.transform.position = pos;
        obj.transform.rotation = Quaternion.identity;

        var eb = obj.GetComponent<EnemyBase>();
        if (eb != null) eb.EnemyID = enemyID;

        return obj;
    }

    // Group spawn functions with cancellation token (UniTask)
    public async UniTask GroupSpawnPattern(int patternId, int enemySpawnCount, float enemySpawnInterval, CancellationToken token)
    {
        try
        {
            for (int i = 0; i < enemySpawnCount; i++)
            {
                token.ThrowIfCancellationRequested();

                switch (patternId)
                {
                    case 1:
                        SpawnEnemyAt(1, new Vector3(-10, 5));
                        SpawnEnemyAt(2, new Vector3(10, 5));
                        break;
                    case 2:
                        SpawnEnemyAt(3, new Vector3(-10, -3));
                        SpawnEnemyAt(4, new Vector3(10, -3));
                        break;
                    case 3:
                        SpawnEnemyAt(5, new Vector3(-4, 5));
                        SpawnEnemyAt(6, new Vector3(4, 5));
                        break;
                    case 4:
                        SpawnEnemyAt(7, new Vector3(-5, 5));
                        SpawnEnemyAt(8, new Vector3(5, 5));
                        break;
                }

                await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval), cancellationToken: token);
            }
        }
        catch (OperationCanceledException)
        {
            // 意図的キャンセルは無視
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EnemyPoolManager] GroupSpawnPattern error: {ex}");
        }
        finally
        {
            // cleanup token if matches
            if (cts != null && cts.Token == token)
            {
                cts.Dispose();
                cts = null;
            }
        }
    }

    // helper to start a pattern and cancel previous
    /// <summary>
    /// ★ 便利メソッド：前の群生成が走っていればキャンセルしてから開始する
    /// </summary>
    public void StartGroupSpawn(int patternId, int enemySpawnCount = 5, float interval = 0.2f)
    {
        // cancel previous
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
            cts = null;
        }

        cts = new CancellationTokenSource();
        var token = cts.Token;
        GroupSpawnPattern(patternId, enemySpawnCount, interval, token).Forget();
    }

    /// <summary>
    /// シーン再初期化が必要なとき（外部から呼ぶ）
    /// </summary>
    public void ResetPool()
    {
        // destroy all current pool objects
        foreach (var p in pool)
        {
            if (p.obj != null) Destroy(p.obj);
        }
        pool.Clear();
        InitializePool();
    }
}
*/
using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;
using UnityEngine.SceneManagement;

/// <summary>
/// ★ 新規／差し替え用の安定した敵プールマネージャ
/// （※あなたの構造そのまま維持）
/// </summary>
public class EnemyPoolManager : MonoBehaviour
{
    public static EnemyPoolManager Instance { get; private set; }

    [Header("エネミープレハブ")]
    [SerializeField] GameObject enemyPrefab;
    [Header("エネミー格納先(親)")]
    [SerializeField] GameObject enemyParentObj;
    [Header("初期プール数")]
    [SerializeField] int initialPoolSize = 50;

    // internal pool item
    private class PoolItem
    {
        public GameObject obj;
        public EnemyBase script;
        public bool inUse;
    }

    private readonly List<PoolItem> pool = new List<PoolItem>();

    private CancellationTokenSource cts = null;

    private void Awake()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("[EnemyPoolManager] enemyPrefab is NULL or Missing!");
        }
        if (enemyParentObj == null)
        {
            enemyParentObj = GameObject.Find("EnemyParent");
            //enemyParentObj = this.gameObject;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(this.gameObject);

        if (enemyParentObj == null)
        {
            var go = GameObject.Find("EnemyParent");
            // var go = this.gameObject;
            if (go == null)
            {
                enemyParentObj = new GameObject("EnemyParent");
            }
            else enemyParentObj = go;
        }

        InitializePool();
    }

    private void OnDestroy()
    {
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
            cts = null;
        }
    }

    private void InitializePool()
    {
        pool.Clear();
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewItem();
        }
    }

    private PoolItem CreateNewItem()
    {
        GameObject obj = Instantiate(enemyPrefab);
        obj.transform.SetParent(enemyParentObj.transform);
        obj.SetActive(false);

        PoolItem item = new PoolItem
        {
            obj = obj,
            script = obj.GetComponent<EnemyBase>(),
            inUse = false
        };

        pool.Add(item);
        return item;
    }

    /// ★★★ 修正はここだけ ★★★
    public GameObject GetEnemy()
    {
        foreach (var item in pool)
        {
            if (item.obj != null && !item.inUse)
            {
                item.inUse = true;
                item.script.ResetEnemy_Internal();

                item.obj.SetActive(true);  // ★ 追加：確実に活性化

                return item.obj;
            }
        }

        var newItem = CreateNewItem();
        newItem.inUse = true;
        newItem.script.ResetEnemy_Internal();

        newItem.obj.SetActive(true);  // ★ 追加：生成時も活性化

        return newItem.obj;
    }
    /// ★★★ 修正はここだけ ★★★

    public void ReleaseEnemy(GameObject obj)
    {
        foreach (var item in pool)
        {
            if (item.obj == obj)
            {
                item.inUse = false;
                item.obj.SetActive(false);
                return;
            }
        }
    }

    public GameObject SpawnEnemyAt(int enemyID, Vector3 pos)
    {
        var obj = GetEnemy();
        if (obj == null) return null;

        obj.transform.position = pos;
        obj.transform.rotation = Quaternion.identity;

        var eb = obj.GetComponent<EnemyBase>();
        if (eb != null) eb.EnemyID = enemyID;

        return obj;
    }


    public async UniTask GroupSpawnPattern(int patternId, int enemySpawnCount, float enemySpawnInterval, float initialDelay, CancellationToken token)
    {
        try
        {
            // ★追加：生成を開始する前に、指定された秒数だけ待つ（左右のズレを作る）
            if (initialDelay > 0)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(initialDelay), cancellationToken: token);
            }

            for (int i = 0; i < enemySpawnCount; i++)
            {
                token.ThrowIfCancellationRequested();

                switch (patternId)
                {
                    case 1:
                        SpawnEnemyAt(1, new Vector3(-8, 5));
                        break;
                    case 2:
                        SpawnEnemyAt(2, new Vector3(8, 5));
                        break;
                    case 3:
                        SpawnEnemyAt(3, new Vector3(-8, -1));
                        break;
                    case 4:
                        SpawnEnemyAt(4, new Vector3(8, -1));
                        break;
                    case 5:
                        SpawnEnemyAt(5, new Vector3(-4, 5));
                        break;
                    case 6:
                        SpawnEnemyAt(6, new Vector3(4, 5));
                        break;
                    case 7:
                        SpawnEnemyAt(7, new Vector3(-5, 5));
                        break;
                    case 8:
                        SpawnEnemyAt(8, new Vector3(5, 5));
                        break;
                    case 9:
                        SpawnEnemyAt(9, new Vector3(-2, 5));
                        break;
                    case 10:
                        SpawnEnemyAt(10, new Vector3(2, 5));
                        break;
                    case 11:
                        SpawnEnemyAt(11, new Vector3(-((float)i * (5 / (float)enemySpawnCount) + 1.0f) * 1.0f, 5));
                        break;
                    case 12:
                        SpawnEnemyAt(12, new Vector3(((float)i * (5 / (float)enemySpawnCount) + 1.0f) * 1.0f, 5));
                        break;
                }

                await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval), cancellationToken: token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EnemyPoolManager] GroupSpawnPattern error: {ex}");
        }
    }

    public void StartGroupSpawn(int patternId, int enemySpawnCount, float interval, float initialDelay)
    {
        // マネージャーが破棄された時に止まる共通のトークンを取得
        var token = this.GetCancellationTokenOnDestroy();

        // 前の処理をキャンセルせず、新しいタスクを並列で開始する
        GroupSpawnPattern(patternId, enemySpawnCount, interval, initialDelay, token).Forget();
    }

    public void ResetPool()
    {
        foreach (var p in pool)
        {
            if (p.obj != null) Destroy(p.obj);
        }
        pool.Clear();
        InitializePool();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ★ シーン読み込みごとに EnemyParent を作りなおし、
    // ★ 全ての敵をその子として再アタッチする
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // ★追加：会話シーン（名前は適宜合わせてください）なら自分を消して終了
        if (scene.name == "StartScene" || scene.name == "TitleScene")
        {
            // プール内の敵を物理的に削除
            foreach (var item in pool)
            {
                if (item.obj != null) Destroy(item.obj);
            }
            pool.Clear();

            // 自分自身（Manager）を削除
            Destroy(this.gameObject);
            return;
        }

        // --- EnemyParent の再取得または作成 ---
        GameObject parent = GameObject.Find("EnemyParent");

        if (parent == null)
        {
            parent = new GameObject("EnemyParent");
        }

        enemyParentObj = parent;

        // --- プール内の全敵を強制的にこの EnemyParent の子にする ---
        foreach (var item in pool)
        {
            if (item.obj != null)
            {
                item.obj.transform.SetParent(enemyParentObj.transform);
            }
        }
    }
}