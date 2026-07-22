using UnityEngine;
using UnityEngine.UI;

// オープンキャンパス用タイトル画面のパッド/キーボード対応
// パッドのボタン(0〜3)またはZ/Enterで、スタートボタンのクリックと同じ動作をする
public class OpenCampusTitleInput : MonoBehaviour
{
    [SerializeField] private Button startButton;    // 既存のスタートボタンを割り当てる

    void Update()
    {
        if (startButton == null) return;

        bool padButtonDown = Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.JoystickButton1)
                          || Input.GetKeyDown(KeyCode.JoystickButton2) || Input.GetKeyDown(KeyCode.JoystickButton3);

        if (padButtonDown || Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Return))
        {
            // 既存ボタンのOnClickをそのまま実行（遷移先の設定を二重管理しない）
            startButton.onClick.Invoke();
        }
    }
}
