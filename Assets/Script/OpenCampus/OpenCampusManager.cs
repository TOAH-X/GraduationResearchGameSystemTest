using UnityEngine;
using UnityEngine.SceneManagement;

// オープンキャンパス版の全体管理（タイトルシーンに1つ配置）
// - 進行状態（ゲーム前/ゲーム後）の保持
// - Shift+R による強制ホーム帰還
public class OpenCampusManager : SafeSingleton<OpenCampusManager>
{
    [Header("戻り先シーン名")]
    [SerializeField] private string titleSceneName = "OpenCampusTitleScene";
    [Header("ゲームシーン名")]
    [SerializeField] private string gameSceneName = "OpenCampusGameScene";

    // ゲームを終えて戻ってきたか
    public bool GamePlayed { get; set; } = false;

    // 直近のゲームのスコア（会話のセリフ埋め込み用）
    public int LastTotalScore { get; private set; } = 0;
    public int LastPlayer1Score { get; private set; } = 0;
    public int LastPlayer2Score { get; private set; } = 0;

    protected override void OnSingletonAwake()
    {
        // シーンをまたいでも消えないように設定
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // シーンが読み込まれた際に呼ばれる
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // ゲームシーン以外に移ったらBGMを停止する（ゲーム終了・Shift+R帰還の両方をカバー）
        if (scene.name != gameSceneName)
        {
            SoundManager.StopBGM();
        }
    }

    void Update()
    {
        // Shift（左右どちらでも）+ R で強制ホーム帰還
        bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (isShiftPressed && Input.GetKeyDown(KeyCode.R))
        {
            ReturnHome();
        }

        // ゲームシーン中だけスコアを控え続ける
        // （ScoreSystemControllerはシーン読み込みごとに0リセットされるため、会話シーンでは記録しない）
        if (SceneManager.GetActiveScene().name == gameSceneName && ScoreSystemController.Instance != null)
        {
            LastTotalScore = ScoreSystemController.Instance.TotalScore;
            LastPlayer1Score = ScoreSystemController.Instance.Player1Score;
            LastPlayer2Score = ScoreSystemController.Instance.Player2Score;
        }
    }

    // 進行状態をリセットしてタイトルへ戻る
    public void ReturnHome()
    {
        GamePlayed = false;
        SceneManager.LoadScene(titleSceneName);
    }
}
