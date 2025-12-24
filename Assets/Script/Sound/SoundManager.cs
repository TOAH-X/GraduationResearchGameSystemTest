using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    // BGM設定
    [Header("BGM Settings")]
    [SerializeField] private List<BGMData> bgmList = new List<BGMData>();
    private AudioSource bgmSource;
    private Dictionary<BGMType, AudioClip> bgmClips = new Dictionary<BGMType, AudioClip>();

    // SE設定
    [Header("SE Settings")]
    [SerializeField] private List<SEData> seList = new List<SEData>();
    [SerializeField] private int audioSourcePoolSize = 5;
    private Dictionary<SEType, AudioClip> seClips = new Dictionary<SEType, AudioClip>();
    private Queue<AudioSource> audioSourcePool = new Queue<AudioSource>();

    private CancellationToken destroyToken;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            // 親オブジェクトがいる場合、切り離してルート階層に移動させる(DontDestroyOnLoadの警告対策)
            if (transform.parent != null)
            {
                transform.SetParent(null);
            }

            DontDestroyOnLoad(gameObject);
            destroyToken = this.GetCancellationTokenOnDestroy();
            Initialize();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Initialize()
    {
        // BGM初期化
        GameObject bgmObject = new GameObject("BGM_Source");
        bgmObject.transform.SetParent(this.transform);
        bgmSource = bgmObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        foreach (var data in bgmList)
        {
            if (data.type != BGMType.None && !bgmClips.ContainsKey(data.type))
            {
                bgmClips.Add(data.type, data.clip);
            }
        }

        // SE初期化
        foreach (var data in seList)
        {
            if (data.type != SEType.None && !seClips.ContainsKey(data.type))
            {
                seClips.Add(data.type, data.clip);
            }
        }
        for (int i = 0; i < audioSourcePoolSize; i++)
        {
            GameObject child = new GameObject($"SE_Source_{i}");
            child.transform.SetParent(this.transform);
            AudioSource source = child.AddComponent<AudioSource>();
            audioSourcePool.Enqueue(source);
        }
    }

    // =======================================================
    // BGM 再生/停止 ロジック
    // =======================================================

    public static void PlayBGM(BGMType type)
    {
        if (Instance == null)
        {
            Debug.LogError("SoundManagerが初期化されていません。");
            return;
        }

        if (!Instance.bgmClips.ContainsKey(type))
        {
            Debug.LogWarning($"BGM Type '{type}' は登録されていません。");
            return;
        }

        // 既に同じBGMが再生中の場合は何もしない
        if (Instance.bgmSource.clip == Instance.bgmClips[type] && Instance.bgmSource.isPlaying)
        {
            return;
        }

        AudioClip clip = Instance.bgmClips[type];

        // 再生ロジック
        Instance.bgmSource.clip = clip;
        Instance.bgmSource.volume = 1.0f; // BGMDataから音量を設定することも可能
        Instance.bgmSource.Play();
    }

    public static void StopBGM()
    {
        if (Instance == null) return;
        Instance.bgmSource.Stop();
    }

    // =======================================================
    // SE 再生ロジック (UniTask利用)
    // =======================================================

    public static void PlaySE(SEType type)
    {
        if (Instance == null || !Instance.seClips.ContainsKey(type) || Instance.audioSourcePool.Count == 0) return;

        AudioSource source = Instance.audioSourcePool.Dequeue();
        source.clip = Instance.seClips[type];
        source.volume = 1.0f;

        source.Play();

        // UniTaskで非同期的にプール戻し処理を開始
        Instance.ReturnToPoolAsync(source, source.clip.length).Forget();
    }

    // UniTaskを使ったAudioSourceのプール戻し処理
    private async UniTaskVoid ReturnToPoolAsync(AudioSource source, float delay)
    {
        try
        {
            await UniTask.Delay(System.TimeSpan.FromSeconds(delay), cancellationToken: destroyToken);

            if (source != null && Instance != null)
            {
                source.Stop();
                source.clip = null;
                Instance.audioSourcePool.Enqueue(source);
            }
        }
        catch (System.OperationCanceledException)
        {
            // キャンセルされた場合は、安全に処理を終了
        }
    }
}