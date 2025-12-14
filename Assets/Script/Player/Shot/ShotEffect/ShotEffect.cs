using DG.Tweening;
using UnityEngine;

public class ShotEffect : MonoBehaviour
{
    private float destroyDelay = 0f;

    // アニメーションのパラメータ
    private Vector2 targetScale = new Vector3(1.0f, 1.0f);

    private float scaleDuration = 0.2f;

    // Start is called before the first frame update
    void Start()
    {
        transform.localScale = new Vector2(0f, 0f);

        // 役割ごとに分けた関数をシーケンスで連結します
        PlayCombinedEffect();
    }

    /// <summary>
    /// サイズ変更アニメーションと破棄処理の流れを定義し実行します。
    /// </summary>
    private void PlayCombinedEffect()
    {
        // 既存のTweenが残っている場合は念のためKillします
        transform.DOKill();

        // 1. シーケンスを作成
        Sequence sequence = DOTween.Sequence();

        // 2. サイズ変更アニメーションをシーケンスに追加
        sequence.Append(AnimateScale());

        // 3. サイズ変更完了後に待機時間を追加 (破棄の準備)
        sequence.AppendInterval(destroyDelay);

        // 4. 全ての処理が完了した後、オブジェクトを破棄する関数を呼び出す
        sequence.OnComplete(() => {
            // ここで破棄関数を呼び出します
            DestroyObject();
        });

        // 安全性のためSetTargetを設定
        sequence.SetTarget(gameObject);
    }

    /// <summary>
    /// 自身のサイズを targetScale に 0.5秒かけて変更するTweenを返します。
    /// </summary>
    private Tween AnimateScale()
    {
        // Tweenオブジェクトを生成し、シーケンスに渡せるように返します
        return transform.DOScale(targetScale, scaleDuration)
            .SetEase(Ease.OutQuad);
    }

    /// <summary>
    /// オブジェクトの破棄を実行します。
    /// </summary>
    private void DestroyObject()
    {
        if (gameObject != null)
        {
            Destroy(gameObject);
        }
    }

    // ★補足：DOTweenシーケンスを使わない場合は、以下のように直接呼び出すことも可能です
    /*
    private void DestroyAfterDelay()
    {
        // 遅延処理としてDOVirtual.DelayedCallを使い、破棄関数を呼び出す
        DOVirtual.DelayedCall(destroyDelay, DestroyObject);
    }
    */

    /*
    // Update is called once per frame
    void FixedUpdate()
    {
        destroyTimer += Time.deltaTime;

        if (destroyTimer >= destroyTime) 
        {
            Destroy(gameObject);
            destroyTimer = 0;
        }
    }
    */
}
