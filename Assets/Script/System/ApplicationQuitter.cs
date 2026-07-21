using UnityEngine;

// 指定のキーを入力するとゲームを終了します
// Unityのプロジェクトフォルダ内に入れるだけで機能します
public class ApplicationQuitter : MonoBehaviour
{
    readonly KeyCode quitKey = KeyCode.Escape;

    void Update()
    {
        if (Input.GetKeyDown(quitKey))
        {
            QuitGame();
        }
    }

    public static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_STANDALONE
        Application.Quit();
#endif
    }

    // ゲームの開始時にインスタンスを作成します
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateInstanceOnLoad()
    {
        var instance = new GameObject(nameof(ApplicationQuitter), typeof(ApplicationQuitter));
        DontDestroyOnLoad(instance);
    }
}