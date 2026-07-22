using UnityEngine;
using UnityEngine.SceneManagement;

// オープンキャンパス版の全体管理（タイトルシーンに1つ配置）
// - 進行状態（ゲーム前/ゲーム後）の保持
// - Shift+R による強制ホーム帰還
public class OpenCampusManager : SafeSingleton<OpenCampusManager>
{
    [Header("戻り先シーン名")]
    [SerializeField] private string titleSceneName = "OpenCampusTitleScene";

    // ゲームを終えて戻ってきたか
    public bool GamePlayed { get; set; } = false;

    protected override void OnSingletonAwake()
    {
        // シーンをまたいでも消えないように設定
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        // Shift（左右どちらでも）+ R で強制ホーム帰還
        bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (isShiftPressed && Input.GetKeyDown(KeyCode.R))
        {
            ReturnHome();
        }
    }

    // 進行状態をリセットしてタイトルへ戻る
    public void ReturnHome()
    {
        GamePlayed = false;
        SceneManager.LoadScene(titleSceneName);
    }
}
