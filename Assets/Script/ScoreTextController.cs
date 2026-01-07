using UnityEngine;
using TMPro;

public class ScoreTextController : MonoBehaviour
{
    [Header("ScoreTextA")]
    [SerializeField] TextMeshProUGUI scoreTextA;
    [Header("ScoreTextB")]
    [SerializeField] TextMeshProUGUI scoreTextB;

    float timer = 60.0f;

    // Start is called before the first frame update
    void Start()
    {
        UpdateText();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        UpdateText();
        timer -= Time.fixedDeltaTime;
    }

    // テキストのアップデート
    public void UpdateText() 
    {
        scoreTextA.text = "総合スコア：" + ScoreSystemController.Instance.TotalScore
            + "\nあなたのスコア：" + ScoreSystemController.Instance.Player1Score
            + "\n支援機のスコア：" + ScoreSystemController.Instance.Player2Score
            + "\n\n\n\n\n"
            + "\n残り時間：" + timer.ToString("F1");
        scoreTextB.text = "TotalScore:" + ScoreSystemController.Instance.TotalScore
            + "\nあなたのスコア：" + ScoreSystemController.Instance.Player1Score
            + "\n支援機のスコア：" + ScoreSystemController.Instance.Player2Score
            + "\n\n\n\n\n"
            + "\n残り時間：" + timer.ToString("F1");
    }
}
