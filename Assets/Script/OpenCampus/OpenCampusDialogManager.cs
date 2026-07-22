using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

// オープンキャンパス用の会話管理
// タイトル → 会話(ゲーム前) → ゲーム → 会話(ゲーム後) → タイトル の流れのみを扱う
// 実験用のPhaseID・SubjectIDには依存しない
public class OpenCampusDialogManager : MonoBehaviour
{
    [SerializeField] SceneController sceneController;

    public TextMeshProUGUI nameText;
    public TextMeshProUGUI messageText;
    public Image characterIcon;

    private DialogData currentData;
    private int lineIndex = 0;
    private bool isTyping = false;
    private bool cancelTyping = false;

    [SerializeField] private DialogData startDialogData;        // ゲーム前の会話
    [SerializeField] private DialogData finishDialogData;       // ゲーム後の会話

    [Header("遷移先シーン名")]
    [SerializeField] private string gameSceneName = "OpenCampusGameScene";
    [SerializeField] private string titleSceneName = "OpenCampusTitleScene";

    private void Start()
    {
        // ゲーム前とゲーム後で再生する会話を切り替える
        // （OpenCampusManager不在時＝シーン直接再生時はゲーム前扱い）
        bool played = OpenCampusManager.IsAvailable() && OpenCampusManager.Instance.GamePlayed;
        DialogData data = played ? finishDialogData : startDialogData;
        if (data != null)
        {
            StartDialog(data);
        }
    }

    public void StartDialog(DialogData data)
    {
        currentData = data;
        lineIndex = 0;
        StartCoroutine(PlayDialog());
    }

    void Update()
    {
        // 会話データ未設定なら何もしない
        if (currentData == null) return;

        // クリック、Z、Enter、パッドのボタン(0〜3)のいずれかを離した瞬間
        bool padButtonUp = Input.GetKeyUp(KeyCode.JoystickButton0) || Input.GetKeyUp(KeyCode.JoystickButton1)
                        || Input.GetKeyUp(KeyCode.JoystickButton2) || Input.GetKeyUp(KeyCode.JoystickButton3);

        if (Input.GetKeyUp(KeyCode.Z) || Input.GetKeyUp(KeyCode.Return) || Input.GetMouseButtonUp(0) || padButtonUp)
        {
            if (isTyping)
            {
                // 文字送り中に押されたら「全文表示」フラグを立てる
                cancelTyping = true;
            }
            else
            {
                // 全文表示済みなら「次のセリフ」へ
                lineIndex++;
                if (lineIndex < currentData.lines.Length)
                {
                    StartCoroutine(PlayDialog());
                }
                else
                {
                    EndDialog();
                }
            }
        }
    }

    IEnumerator PlayDialog()
    {
        isTyping = true;
        cancelTyping = false;

        var line = currentData.lines[lineIndex];
        nameText.text = line.name;
        characterIcon.sprite = line.characterImage;

        // セリフ中のスコア用の目印を直近ゲームのスコアに置換
        string message = line.message;
        if (OpenCampusManager.IsAvailable())
        {
            message = message.Replace("{TotalScore}", OpenCampusManager.Instance.LastTotalScore.ToString())
                             .Replace("{Player1Score}", OpenCampusManager.Instance.LastPlayer1Score.ToString())
                             .Replace("{Player2Score}", OpenCampusManager.Instance.LastPlayer2Score.ToString());
        }

        messageText.text = "";
        foreach (char c in message.ToCharArray())
        {
            messageText.text += c;

            // スキップが押されたらループを抜けて全文表示
            if (cancelTyping)
            {
                messageText.text = message;
                break;
            }
            yield return new WaitForSeconds(0.02f);
        }

        isTyping = false;
    }

    // 会話終了時
    void EndDialog()
    {
        // 会話終了時の処理（UI消したりなど）
        gameObject.SetActive(false);

        bool played = OpenCampusManager.IsAvailable() && OpenCampusManager.Instance.GamePlayed;
        if (played)
        {
            // ゲーム後の会話が終わったのでタイトルへ戻る（次のプレイに備えて状態をリセット）
            OpenCampusManager.Instance.GamePlayed = false;
            sceneController.ChangeScene(titleSceneName);
        }
        else
        {
            // ゲーム前の会話が終わったのでゲームへ
            if (OpenCampusManager.IsAvailable())
            {
                OpenCampusManager.Instance.GamePlayed = true;
            }
            sceneController.ChangeScene(gameSceneName);
        }
    }
}
