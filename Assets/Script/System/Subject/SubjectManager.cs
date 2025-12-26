using UnityEngine;
using UnityEngine.SceneManagement;

// SafeSingletonを継承して作成
public class SubjectManager : SafeSingleton<SubjectManager>
{
    [Header("被験者情報")]
    public int subjectID = -1;
    public int trialCount = 1;

    private string inputBuffer;



    [Header("永続化設定")]
    public bool shouldPersist = true;

    protected override void OnSingletonAwake()
    {
        // シーンをまたいでも消えないように設定
        if (shouldPersist)
        {
            DontDestroyOnLoad(gameObject);
        }

        // Debug.Log($"SubjectManagerが起動しました。現在のID: {subjectID}");
    }

    // IDを更新するメソッド（UIなどから呼ぶ用）
    public void SetSubjectID(int newID)
    {
        subjectID = newID;
        // Debug.Log($"被験者IDが更新されました: {subjectID}");
    }

    void FixedUpdate()
    {
        // 1. Shiftキーが押されているかチェック (左右どちらのShiftでも可)
        bool isShiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (isShiftPressed)
        {
            // 2. 数字キーの入力を受け付ける
            for (int i = 0; i <= 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                {
                    inputBuffer += i.ToString();
                    // Debug.Log($"入力中の数値: {inputBuffer}");
                }
            }

            // 3. Backspace で一文字消去
            if (Input.GetKeyDown(KeyCode.Backspace) && inputBuffer.Length > 0)
            {
                inputBuffer = inputBuffer.Substring(0, inputBuffer.Length - 1);
                // Debug.Log($"消去後のバッファ: {inputBuffer}");
            }

            // 4. Enterキーで確定
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (inputBuffer.Length > 0)
                {
                    ConfirmID();
                }
            }
        }
    }

    void ConfirmID()
    {
        // 文字列を int に変換して保存
        if (int.TryParse(inputBuffer, out int result))
        {
            subjectID = result;
            // Debug.Log($"★ID確定: {subjectID} (int型)");

            inputBuffer = ""; // バッファクリア

            // 必要であればここでシーン遷移
            // SceneManager.LoadScene(experimentSceneName);
        }
    }

    // デバッグ表示用（画面左上）
    void OnGUI()
    {
        string status = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        ? "<color=red>入力有効</color>" : "Shiftで入力開始";

        GUI.Box(new Rect(10, 10, 200, 60), "Subject ID Manager");
        GUI.Label(new Rect(20, 30, 180, 20), $"状態: {status}");
        GUI.Label(new Rect(20, 50, 180, 20), $"現在のバッファ: {inputBuffer}");
    }

    public int SubjectID
    {
        get { return subjectID; }
        set { subjectID = value; }
    }
}