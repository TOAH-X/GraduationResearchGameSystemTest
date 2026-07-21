using UnityEngine;

// SafeSingletonを継承して作成
public class StageManager : SafeSingleton<StageManager>
{
    [Header("進捗ID")]        // -1：エラー、0：学習環境、1：チュートリアル、2：被支援エージェント、3：支援エージェント
    public int phaseID = 1;

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

    public int PhaseID
    {
        get { return phaseID; }
        set { phaseID = value; }
    }
}