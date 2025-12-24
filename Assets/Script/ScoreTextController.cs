using UnityEngine;
using TMPro;

public class ScoreTextController : MonoBehaviour
{
    [Header("ScoreTextA")]
    [SerializeField] TextMeshProUGUI scoreTextA;
    [Header("ScoreTextB")]
    [SerializeField] TextMeshProUGUI scoreTextB;

    // Start is called before the first frame update
    void Start()
    {
        UpdateText();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        UpdateText();
    }

    // テキストのアップデート
    public void UpdateText() 
    {
        scoreTextA.text = "TotalScore:" + ScoreSystemController.Instance.TotalScore
            + "\nPlayer1Score:" + ScoreSystemController.Instance.Player1Score
            + "\nPlayer2Score:" + ScoreSystemController.Instance.Player2Score;
        scoreTextB.text = "TotalScore:" + ScoreSystemController.Instance.TotalScore
            + "\nPlayer1Score:" + ScoreSystemController.Instance.Player1Score
            + "\nPlayer2Score:" + ScoreSystemController.Instance.Player2Score;
    }
}
