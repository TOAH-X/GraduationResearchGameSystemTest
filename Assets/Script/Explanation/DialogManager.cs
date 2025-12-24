using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using static UnityEngine.Mesh;

public class DialogManager : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI messageText;
    public Image characterIcon;

    private DialogData currentData;
    private int lineIndex = 0;
    private bool isTyping = false;
    private bool cancelTyping = false;

    [SerializeField] private DialogData startDialogData;        // 最初のセリフ群

    private void Start()
    {
        // 再生
        if (startDialogData != null)
        {
            StartDialog(startDialogData);
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

    void EndDialog()
    {
        // 会話終了時の処理（UIを消すなど）
        gameObject.SetActive(false);
    }
}