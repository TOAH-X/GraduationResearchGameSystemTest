using UnityEngine;

public class SafeSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; private set; }
    private static bool isInitialized = false;

    protected virtual void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this as T;
        isInitialized = true;
        OnSingletonAwake();
    }

    protected virtual void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            isInitialized = false;
        }
    }

    public static bool IsAvailable()
    {
        return Instance != null && isInitialized;
    }

    protected virtual void OnSingletonAwake()
    {
        // 子クラスの初期化用
    }
}