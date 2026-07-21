using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class DialogManager : MonoBehaviour
{
    [SerializeField] SceneController sceneController;

    public TextMeshProUGUI nameText;
    public TextMeshProUGUI messageText;
    public Image characterIcon;

    private DialogData currentData;
    private int lineIndex = 0;
    private bool isTyping = false;
    private bool cancelTyping = false;

    [SerializeField] private DialogData startDialogData;        // 説明+チュートリアル会話
    [SerializeField] private DialogData play1DialogData;        // 協力プレイ1の会話
    [SerializeField] private DialogData play2DialogData;        // 協力プレイ2の会話
    [SerializeField] private DialogData finishDialogData;       // 終わりの会話

    private void Start()
    {
        // チュートリアル前の会話
        if (StageManager.Instance.PhaseID == 1)
        {
            // 再生
            if (startDialogData != null)
            {
                StartDialog(startDialogData);
            }
        }
        // 1回目協力プレイ前の会話
        else if (StageManager.Instance.PhaseID == 2)
        {
            // 再生
            if (play1DialogData != null)
            {
                StartDialog(play1DialogData);
            }
        }
        // 2回目協力プレイ前の会話
        else if (StageManager.Instance.PhaseID == 3)
        {
            // 再生
            if (play2DialogData != null)
            {
                StartDialog(play2DialogData);
            }
        }
        // 終了後の会話
        else if (StageManager.Instance.PhaseID == 4)
        {
            // 再生
            if (finishDialogData != null)
            {
                StartDialog(finishDialogData);
            }
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
        // クリック、Z、Enterのいずれかを離した瞬間
        if (Input.GetKeyUp(KeyCode.Z) || Input.GetKeyUp(KeyCode.Return) || Input.GetMouseButtonUp(0))
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

        messageText.text = "";
        foreach (char c in line.message.ToCharArray())
        {
            messageText.text += c;

            // スキップが押されたらループを抜けて全文表示
            if (cancelTyping)
            {
                messageText.text = line.message;
                break;
            }
            yield return new WaitForSeconds(0.02f);
        }

        isTyping = false;
    }

    // 会話終了時
    void EndDialog()
    {
        // 会話終了時の処理（UIを消すなど）
        gameObject.SetActive(false);

        // 進捗レベルを進める
        string targetScene = "GameScene";
        if (StageManager.Instance.PhaseID == 1)
        {
            // チュートリアル
            StageManager.Instance.PhaseID = 2;
            targetScene = "TutorialScene";
        }
        else if (StageManager.Instance.PhaseID == 2)
        {
            // 協力プレイ1回目
            StageManager.Instance.PhaseID = 3;
            if (SubjectManager.Instance.SubjectID % 2 == 1)  
            {
                targetScene = "A_Scene";
            }
            else 
            {
                targetScene = "B_Scene";
            }
        }
        else if (StageManager.Instance.PhaseID == 3)
        {
            // 協力プレイ2回目
            StageManager.Instance.PhaseID = 4;
            if (SubjectManager.Instance.SubjectID % 2 == 0)
            {
                targetScene = "A_Scene";
            }
            else
            {
                targetScene = "B_Scene";
            }
        }
        else if (StageManager.Instance.PhaseID == 4)
        {
            // 終了
            StageManager.Instance.PhaseID = 5;
            targetScene = "TitleScene";
        }
        else 
        {
            targetScene = "GameScene";
            StageManager.Instance.PhaseID = 0;
        }

        // 遷移
        sceneController.ChangeScene(targetScene);
    }
}