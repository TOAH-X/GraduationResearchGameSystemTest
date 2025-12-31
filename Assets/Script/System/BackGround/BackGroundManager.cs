using UnityEngine;

public class BackGroundManager : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private float scrollSpeed = 2.0f; // 下に流れる速さ
    [SerializeField] private float backgroundHeight = 10.0f; // 背景1枚の高さ

    [Header("子オブジェクトの参照")]
    [SerializeField] private Transform bg1;
    [SerializeField] private Transform bg2;

    void Update()
    {
        // 1. 2つの子オブジェクトを同時に下に移動させる
        float moveAmount = scrollSpeed * Time.deltaTime;
        bg1.position += Vector3.down * moveAmount;
        bg2.position += Vector3.down * moveAmount;

        // 2. 各背景が「画面外（下）」に出たか個別にチェックしてワープさせる
        CheckAndResetPosition(bg1);
        CheckAndResetPosition(bg2);
    }

    private void CheckAndResetPosition(Transform bgTransform)
    {
        // 背景の高さ分だけ下に移動したら、一番上へ移動させる
        if (bgTransform.position.y <= -backgroundHeight)
        {
            // 現在の座標に「背景2枚分の高さ」を足して、もう一方の背景の真上へ送る
            bgTransform.position += new Vector3(0, backgroundHeight * 2, 0);
        }
    }
}