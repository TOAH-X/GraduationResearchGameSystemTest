/*
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EnemySpawnerController : MonoBehaviour
{
    public static EnemySpawnerController Instance { get; private set; }

    [Header("エネミープレハブ")]
    [SerializeField] GameObject enemyPrefab;
    [Header("エネミー格納先(親)")]
    [SerializeField] GameObject enemyParentObj;
    [Header("プールサイズ")]
    [SerializeField] int poolSize = 50;

    private Queue<GameObject> enemyPool = new Queue<GameObject>();
    private float timer = 0f;

    private int enemyGroupID = 0;



    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // シーンが再読み込みされた時に再初期化
        SceneManager.sceneLoaded += OnSceneLoaded;

        InitializePool();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // リセット時（シーン再読み込み時）にプールを再初期化
        ResetPool();
    }

    /// <summary>
    /// プール初期化
    /// </summary>
    private void InitializePool()
    {
        enemyPool.Clear();

        if (enemyParentObj == null)
        {
            enemyParentObj = new GameObject("EnemyParent");
        }

        for (int i = 0; i < poolSize; i++)
        {
            GameObject enemy = Instantiate(enemyPrefab);
            enemy.transform.SetParent(enemyParentObj.transform);
            enemy.SetActive(false);
            enemyPool.Enqueue(enemy);
        }
    }

    /// <summary>
    /// プールを安全にリセット
    /// </summary>
    public void ResetPool()
    {
        // 既存のプールオブジェクトを破棄
        while (enemyPool.Count > 0)
        {
            var e = enemyPool.Dequeue();
            if (e != null) Destroy(e);
        }

        // 親も再取得（シーンに再生成されている可能性に備える）
        enemyParentObj = GameObject.Find("EnemyParent");
        if (enemyParentObj == null)
        {
            enemyParentObj = new GameObject("EnemyParent");
        }

        InitializePool();
        Debug.Log("Enemy pool reset complete.");
    }

    void FixedUpdate()
    {
        timer += Time.deltaTime;

        enemyGroupID = UnityEngine.Random.Range(1, 5);

        if (timer >= 3.0f)
        {
            // パターン1
            if (enemyGroupID == 1)
            {
                EnemyGroupSpawn1(5, 0.2f);
            }
            // パターン2
            else if (enemyGroupID == 2) 
            {
                EnemyGroupSpawn2(5, 0.2f);
            }
            // パターン3
            else if (enemyGroupID == 3)
            {
                EnemyGroupSpawn3(5, 0.2f);
            }
            // パターン4
            else if (enemyGroupID == 4)
            {
                EnemyGroupSpawn4(5, 0.2f);
            }

            timer = 0;
        }
    }

    /// <summary>
    /// プールから敵を取得
    /// </summary>
    private GameObject GetEnemyFromPool()
    {
        // 1. 新しいキューを作成
        Queue<GameObject> tempPool = new Queue<GameObject>();
        while (enemyPool.Count > 0)
        {
            var e = enemyPool.Dequeue();

            // Unityの特殊なnullチェック（破棄待ちも含む）
            if (e != null)
            {
                tempPool.Enqueue(e);
            }
        }
        enemyPool = tempPool; // 安全なプールに置き換える

        // 2. プールが空の場合の処理 (前回の修正案を適用)
        if (enemyPool.Count == 0)
        {
            Debug.LogWarning("Enemy pool empty — adding new instance.");

            // 1体のみ生成して返す
            GameObject newEnemy = Instantiate(enemyPrefab);
            newEnemy.transform.SetParent(enemyParentObj.transform);
            return newEnemy;
        }

        // 3. 安全なオブジェクトを取り出す
        var enemy = enemyPool.Dequeue();
        // ここに来るオブジェクトは既に null チェック済み

        enemy.SetActive(true);
        return enemy;
    }

    /// <summary>
    /// 敵をプールに戻す
    /// </summary>
    public void ReturnEnemyToPool(GameObject enemy)
    {
        if (enemy == null) return;
        enemy.SetActive(false);
        enemyPool.Enqueue(enemy);
    }

    /// <summary>
    /// 敵の生成（再利用対応）
    /// </summary>
    public void EnemySpawn(int enemyID, Vector3 pos)
    {
        GameObject enemyObj = GetEnemyFromPool();
        if (enemyObj == null)
        {
            return;
        }

        enemyObj.transform.position = pos;
        enemyObj.transform.rotation = Quaternion.identity;
        enemyObj.transform.SetParent(enemyParentObj.transform);

        var enemyBase = enemyObj.GetComponent<EnemyBase>();
        enemyBase.EnemyID = enemyID;
        enemyBase.ResetEnemy();
    }

    public async void EnemyGroupSpawn1(int enemySpawnCount, float enemySpawnInterval)
    {
        for (int i = 0; i < enemySpawnCount; i++)
        {
            EnemySpawn(1, new Vector3(-10, 5));
            EnemySpawn(2, new Vector3(10, 5));
            await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval));
        }
    }

    public async void EnemyGroupSpawn2(int enemySpawnCount, float enemySpawnInterval)
    {
        for (int i = 0; i < enemySpawnCount; i++)
        {
            EnemySpawn(3, new Vector3(-10, -3));
            EnemySpawn(4, new Vector3(10, -3));
            await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval));
        }
    }

    public async void EnemyGroupSpawn3(int enemySpawnCount, float enemySpawnInterval)
    {
        for (int i = 0; i < enemySpawnCount; i++)
        {
            EnemySpawn(5, new Vector3(-4, 5));
            EnemySpawn(6, new Vector3(4, 5));
            await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval));
        }
    }

    public async void EnemyGroupSpawn4(int enemySpawnCount, float enemySpawnInterval)
    {
        for (int i = 0; i < enemySpawnCount; i++)
        {
            EnemySpawn(7, new Vector3(-5, 5));
            EnemySpawn(8, new Vector3(5, 5));
            await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval));
        }
    }
}
*/
/*
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading; // ★ 追加：CancellationTokenSourceのために必要

public class EnemySpawnerController : MonoBehaviour
{
    public static EnemySpawnerController Instance { get; private set; }

    [Header("エネミープレハブ")]
    [SerializeField] GameObject enemyPrefab;
    [Header("エネミー格納先(親)")]
    [SerializeField] GameObject enemyParentObj;
    [Header("プールサイズ")]
    [SerializeField] int poolSize = 50;

    private Queue<GameObject> enemyPool = new Queue<GameObject>();
    private float timer = 0f;

    private int enemyGroupID = 0;

    // ★ 追加：非同期処理を管理・キャンセルするためのトークンソース
    private CancellationTokenSource cts = null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // シーンが再読み込みされた時に再初期化
        SceneManager.sceneLoaded += OnSceneLoaded;

        InitializePool();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        // ★ 追加：オブジェクト破棄時に非同期処理を停止
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // リセット時（シーン再読み込み時）にプールを再初期化
        ResetPool();
    }

    /// <summary>
    /// プール初期化
    /// </summary>
    private void InitializePool()
    {
        enemyPool.Clear();

        if (enemyParentObj == null)
        {
            enemyParentObj = new GameObject("EnemyParent");
        }

        for (int i = 0; i < poolSize; i++)
        {
            GameObject enemy = Instantiate(enemyPrefab);
            enemy.transform.SetParent(enemyParentObj.transform);
            enemy.SetActive(false);
            enemyPool.Enqueue(enemy);
        }
    }

    /// <summary>
    /// プールを安全にリセット
    /// </summary>
    public void ResetPool()
    {
        // 既存のプールオブジェクトを破棄
        while (enemyPool.Count > 0)
        {
            var e = enemyPool.Dequeue();
            if (e != null) Destroy(e);
        }

        // 親も再取得（シーンに再生成されている可能性に備える）
        enemyParentObj = GameObject.Find("EnemyParent");
        if (enemyParentObj == null)
        {
            enemyParentObj = new GameObject("EnemyParent");
        }

        InitializePool();
        Debug.Log("Enemy pool reset complete.");
    }

    void FixedUpdate()
    {
        timer += Time.deltaTime;

        enemyGroupID = UnityEngine.Random.Range(1, 5);

        if (timer >= 3.0f)
        {
            // ★ 修正 1：既存のスポーン処理があればキャンセルし、新しいトークンを作成
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }
            cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;

            // パターン1
            if (enemyGroupID == 1)
            {
                EnemyGroupSpawn1(5, 0.2f, token).Forget(); // ★ UniTaskを呼び出し、Forget()とトークンを渡す
            }
            // パターン2
            else if (enemyGroupID == 2)
            {
                EnemyGroupSpawn2(5, 0.2f, token).Forget();
            }
            // パターン3
            else if (enemyGroupID == 3)
            {
                EnemyGroupSpawn3(5, 0.2f, token).Forget();
            }
            // パターン4
            else if (enemyGroupID == 4)
            {
                EnemyGroupSpawn4(5, 0.2f, token).Forget();
            }

            timer = 0;
        }
    }

    /// <summary>
    /// プールから敵を取得
    /// </summary>
    private GameObject GetEnemyFromPool()
    {
        // 1. null を除去
        Queue<GameObject> tempPool = new Queue<GameObject>();
        while (enemyPool.Count > 0)
        {
            var e = enemyPool.Dequeue();
            if (e != null)
                tempPool.Enqueue(e);
        }
        enemyPool = tempPool;

        // 2. プールが空の場合
        if (enemyPool.Count == 0)
        {
            var enemy = Instantiate(enemyPrefab, enemyParentObj.transform);
            enemy.SetActive(false); // 初期化直後は false
            enemyPool.Enqueue(enemy);
        }

        // 3. 取り出す（まだ非アクティブ）
        var obj = enemyPool.Dequeue();

        // ★★★ 最重要：ここでアクティブにする ★★★
        obj.SetActive(true);

        return obj;
    }

    /// <summary>
    /// 敵をプールに戻す
    /// </summary>
    public void ReturnEnemyToPool(GameObject enemy)
    {
        if (enemy == null) return;

        // ★ このログを仕込んでください。これが犯人を確実に捕まえます。★
        Debug.LogError($"[POOL RETURN TRACE] Enemy ID {enemy.GetComponent<EnemyBase>().EnemyID} returned. " +
                       $"CALL STACK TRACE: {System.Environment.StackTrace}");

        enemy.SetActive(false);
        enemyPool.Enqueue(enemy);
    }

    /// <summary>
    /// 敵の生成（再利用対応）
    /// </summary>
    public void EnemySpawn(int enemyID, Vector3 pos)
    {
        var enemyObj = GetEnemyFromPool(); // ここでSetActive(true)済み

        var enemyBase = enemyObj.GetComponent<EnemyBase>();
        enemyBase.EnemyID = enemyID;
        enemyBase.ResetEnemy();

        enemyObj.transform.position = pos;
        enemyObj.transform.rotation = Quaternion.identity;
        enemyObj.transform.SetParent(enemyParentObj.transform);
    }

    // ★ 修正 2：async UniTask に変更し、CancellationTokenを受け取る
    public async UniTask EnemyGroupSpawn1(int enemySpawnCount, float enemySpawnInterval, CancellationToken token)
    {
        try
        {
            for (int i = 0; i < enemySpawnCount; i++)
            {
                // ★ キャンセルされていないかチェック
                token.ThrowIfCancellationRequested();

                EnemySpawn(1, new Vector3(-10, 5));
                EnemySpawn(2, new Vector3(10, 5));

                // ★ Delay中にキャンセルを監視
                await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval), cancellationToken: token);
            }
        }
        catch (OperationCanceledException)
        {
            // 意図的なキャンセルの場合は何もしない
        }
        catch (Exception ex)
        {
            Debug.LogError($"GroupSpawn1 Error: {ex}");
        }
        finally
        {
            // ★ 処理完了時、またはキャンセル時にトークンソースを確実にクリーンアップ
            if (cts != null && cts.Token == token)
            {
                cts.Dispose();
                cts = null;
            }
        }
    }

    // ★ 修正 3：他のグループも同様に修正
    public async UniTask EnemyGroupSpawn2(int enemySpawnCount, float enemySpawnInterval, CancellationToken token)
    {
        try
        {
            for (int i = 0; i < enemySpawnCount; i++)
            {
                token.ThrowIfCancellationRequested();
                EnemySpawn(3, new Vector3(-10, -3));
                EnemySpawn(4, new Vector3(10, -3));
                await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval), cancellationToken: token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.LogError($"GroupSpawn2 Error: {ex}"); }
        finally
        {
            if (cts != null && cts.Token == token) { cts.Dispose(); cts = null; }
        }
    }

    public async UniTask EnemyGroupSpawn3(int enemySpawnCount, float enemySpawnInterval, CancellationToken token)
    {
        try
        {
            for (int i = 0; i < enemySpawnCount; i++)
            {
                token.ThrowIfCancellationRequested();
                EnemySpawn(5, new Vector3(-4, 5));
                EnemySpawn(6, new Vector3(4, 5));
                await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval), cancellationToken: token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.LogError($"GroupSpawn3 Error: {ex}"); }
        finally
        {
            if (cts != null && cts.Token == token) { cts.Dispose(); cts = null; }
        }
    }

    public async UniTask EnemyGroupSpawn4(int enemySpawnCount, float enemySpawnInterval, CancellationToken token)
    {
        try
        {
            for (int i = 0; i < enemySpawnCount; i++)
            {
                token.ThrowIfCancellationRequested();
                EnemySpawn(7, new Vector3(-5, 5));
                EnemySpawn(8, new Vector3(5, 5));
                await UniTask.Delay(TimeSpan.FromSeconds(enemySpawnInterval), cancellationToken: token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.LogError($"GroupSpawn4 Error: {ex}"); }
        finally
        {
            if (cts != null && cts.Token == token) { cts.Dispose(); cts = null; }
        }
    }
}
*/
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
        // 1) まず pool 内の破棄済み/null を掃除（遅延削除）
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            if (pool[i].obj == null)
            {
                pool.RemoveAt(i);
            }
        }

        // 2) 空きがあるか探す
        foreach (var item in pool)
        {
            if (!item.inUse)
            {
                item.inUse = true;

                // 内部初期化（Active前に行う）
                if (item.script != null)
                {
                    item.script.ResetEnemy_InternalOnly();
                }

                // アクティブ化はここで行う（呼び出し側が位置をセットしたいなら SetActive(true) を呼ぶ前提に変えること）
                item.obj.SetActive(true);
                return item.obj;
            }
        }

        // 3) 空きがなければ追加生成（伸張）
        PoolItem newItem = CreateNewItem();
        newItem.inUse = true;
        if (newItem.script != null) newItem.script.ResetEnemy_InternalOnly();
        newItem.obj.SetActive(true);
        return newItem.obj;
    }

    /// <summary>
    /// プールへ返却（Release）
    /// </summary>
    public void ReleaseEnemy(GameObject enemy)
    {
        if (enemy == null) return;

        // find pool item
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i].obj == enemy)
            {
                pool[i].inUse = false;

                // debug trace to find who returns (helpful when debugging "unexpected disable")
                Debug.Log($"[EnemyPoolManager] ReleaseEnemy: ID={pool[i].script?.EnemyID ?? -1} (frame {Time.frameCount})");

                pool[i].obj.SetActive(false);
                return;
            }
        }

        // もし見つからなければ（何かで生成された別物）、安全に破棄して再補充可能にする
        Debug.LogWarning("[EnemyPoolManager] ReleaseEnemy: object not found in pool, destroying it.");
        Destroy(enemy);
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

/// <summary>
/// スポーンのルール管理（EnemyPoolManager に実際の生成を委譲）
/// - FixedUpdate で timer を見て、3秒ごとにランダムなパターンを開始する
/// - 複数パターンが重ならないように EnemyPoolManager.StartGroupSpawn を使う
/// </summary>
public class EnemySpawnerController : MonoBehaviour
{
    private float timer = 0f;

    void FixedUpdate()
    {
        timer += Time.fixedDeltaTime;

        if (timer >= 3.0f)
        {
            // 遅延時間
            float initialDelay_PatternLeft = 0f;
            float initialDelay_PatternRight = 0f;
            int delayPattern = Random.Range(1, 3);
            if (delayPattern == 1) 
            {
                initialDelay_PatternLeft += 0.5f;
            }
            else if (delayPattern == 2) 
            {
                initialDelay_PatternRight += 0.5f;
            }
            
            // 奇数が左、偶数が右
            int patternLeft = Random.Range(1, 6); // 1..5
            EnemyPoolManager.Instance.StartGroupSpawn(patternLeft * 2 - 1, 5, 0.2f, initialDelay_PatternLeft);
            int patternRight = Random.Range(1, 6); // 1..5
            EnemyPoolManager.Instance.StartGroupSpawn(patternRight * 2, 5, 0.2f, initialDelay_PatternLeft);
            timer = 0f;
        }
    }
}
