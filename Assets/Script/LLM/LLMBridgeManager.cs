// 【このファイルの役割】
// Unity 側の LLMBridge の窓口となるマネージャー。LLMTestScene にだけ置く。
// Python（LLMBridge）とつながっているときだけ、ゲームの情報を GameStateChannel で Python へ送る。
// Python とつながっていないとき（ふつうの再生）は何もしない。既存のシーン・スクリプトには影響しない。
//
// 送るもの（すべて JSON。1件ずつ送る）:
//   ・"scene" … シーンを読み込むたび。何回目の読み込みか、設定値（fixedDeltaTime、DecisionPeriod、MaxStep）、
//               シーン内の PlayerAgent の一覧（無効なものや DontDestroyOnLoad に残っているものも含む）
//   ・"frame" … 毎フレーム（FixedUpdate ごと）。通しのフレーム番号、Academy のステップ数、ゲーム内時刻、
//               自分（PlayerID 1）と相方の位置
//
// 既存のマネージャーと同じく SafeSingleton を使う。シーンをまたがず（DontDestroyOnLoad にしない）、
// シーンを読み込むたびに作り直される。読み込みの回数とフレーム番号は static で持ち、やり直しても続けて数える。
using System;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

public class LLMBridgeManager : SafeSingleton<LLMBridgeManager>
{
    [Header("LLMBridge 設定")]
    [SerializeField] int selfPlayerID = 1;                  // 自分（LLM が操作する被支援プレイヤー）の PlayerID
    [SerializeField] string playerParentName = "PlayerParent";  // 機体がまとめて入っている親オブジェクトの名前

    static int s_SceneLoadCount = 0;    // これまでにシーンを読み込んだ回数（やり直しても続けて数える）
    static int s_Frame = 0;             // 送ったフレームの通し番号（やり直しても続けて数える）

    bool isActive = false;              // このシーンで情報を送るかどうか（Python とつながっているときだけ true）
    PlayerBase selfPlayer;              // 自分の機体
    PlayerBase partnerPlayer;           // 相方の機体

    // ---- 送る JSON の形（JsonUtility で文字列にする） ----
    [Serializable]
    class AgentInfo
    {
        public string name;             // オブジェクト名
        public string scene;            // 置かれているシーン名（DontDestroyOnLoad に残っていればその名前）
        public bool activeInHierarchy;  // 有効か
        public bool enabled;            // コンポーネントが有効か
        public string behaviorType;     // Default / HeuristicOnly / InferenceOnly
        public string behaviorName;     // Behavior 名
    }

    [Serializable]
    class SceneMessage
    {
        public string type = "scene";
        public int loadCount;           // 何回目の読み込みか（1から）
        public string sceneName;
        public int frame;               // この時点の通しフレーム番号
        public int academyStep;         // この時点の Academy の通しステップ数
        public float fixedDeltaTime;    // 設定値：FixedUpdate 1回の秒数
        public int decisionPeriod;      // 設定値：自分の機体を操作するエージェントの判断の間隔（見つからなければ -1）
        public int maxStep;             // 設定値：同じく MaxStep（見つからなければ -1）
        public float timeScale;
        public int playerAgentCount;    // シーン内の PlayerAgent の数（無効なものも含む）
        public AgentInfo[] playerAgents;
    }

    [Serializable]
    class FrameMessage
    {
        public string type = "frame";
        public int frame;               // 通しのフレーム番号（このマネージャーが数える。0から）
        public int academyStep;         // Academy の通しステップ数（ML-Agents が数える）
        public float time;              // ゲーム内時刻（Time.fixedTime）
        public float[] self;            // 自分の位置 [x, y]。見つからないときは空
        public float[] partner;         // 相方の位置 [x, y]。見つからないときは空
    }

    // シーンを読み込むたびに呼ばれる。Python とつながっていれば、チャンネルを登録して "scene" を送る
    protected override void OnSingletonAwake()
    {
        base.OnSingletonAwake();
        isActive = Academy.Instance.IsCommunicatorOn;
        if (!isActive) return;

        GameStateChannel.EnsureRegistered();
        s_SceneLoadCount++;
        SendSceneMessage();
        Debug.Log($"[LLMBridgeManager] シーン読み込み {s_SceneLoadCount}回目。Python へ情報を送ります");
    }

    // 毎フレーム呼ばれる。自分と相方の位置などを "frame" として送る
    void FixedUpdate()
    {
        if (!isActive) return;
        if (!Academy.Instance.IsCommunicatorOn)
        {
            // Python との接続が切れたら、このシーンではもう送らない
            isActive = false;
            return;
        }

        FindPlayersIfNeeded();
        var msg = new FrameMessage
        {
            frame = s_Frame,
            academyStep = Academy.Instance.TotalStepCount,
            time = Time.fixedTime,
            self = PositionOf(selfPlayer),
            partner = PositionOf(partnerPlayer),
        };
        GameStateChannel.Instance.SendText(JsonUtility.ToJson(msg));
        s_Frame++;
    }

    // "scene" を作って送る（読み込み回数、設定値、PlayerAgent の一覧）
    void SendSceneMessage()
    {
        PlayerAgent[] agents = FindObjectsOfType<PlayerAgent>(true);
        var infos = new List<AgentInfo>();
        int decisionPeriod = -1;
        int maxStep = -1;
        foreach (var a in agents)
        {
            var bp = a.GetComponent<BehaviorParameters>();
            infos.Add(new AgentInfo
            {
                name = a.gameObject.name,
                scene = a.gameObject.scene.name,
                activeInHierarchy = a.gameObject.activeInHierarchy,
                enabled = a.enabled,
                behaviorType = bp != null ? bp.BehaviorType.ToString() : "",
                behaviorName = bp != null ? bp.BehaviorName : "",
            });
            // 設定値は、Python が操作する（Default の）有効なエージェントから取る
            if (decisionPeriod < 0 && bp != null && bp.BehaviorType == BehaviorType.Default && a.gameObject.activeInHierarchy)
            {
                var dr = a.GetComponent<DecisionRequester>();
                decisionPeriod = dr != null ? dr.DecisionPeriod : -1;
                maxStep = a.MaxStep;
            }
        }

        var msg = new SceneMessage
        {
            loadCount = s_SceneLoadCount,
            sceneName = gameObject.scene.name,
            frame = s_Frame,
            academyStep = Academy.Instance.TotalStepCount,
            fixedDeltaTime = Time.fixedDeltaTime,
            decisionPeriod = decisionPeriod,
            maxStep = maxStep,
            timeScale = Time.timeScale,
            playerAgentCount = agents.Length,
            playerAgents = infos.ToArray(),
        };
        GameStateChannel.Instance.SendText(JsonUtility.ToJson(msg));
    }

    // 自分と相方の機体を探す（まだ見つかっていないとき、または消えたときだけ探し直す）
    void FindPlayersIfNeeded()
    {
        bool selfOk = selfPlayer != null && selfPlayer.gameObject.activeInHierarchy;
        bool partnerOk = partnerPlayer != null && partnerPlayer.gameObject.activeInHierarchy;
        if (selfOk && partnerOk) return;

        GameObject parent = GameObject.Find(playerParentName);
        if (parent == null) return;
        foreach (var p in parent.GetComponentsInChildren<PlayerBase>(false))
        {
            if (p.PlayerID == selfPlayerID)
            {
                selfPlayer = p;
            }
            else if (!partnerOk)
            {
                // 自分以外の有効な機体を相方とみなす
                partnerPlayer = p;
                partnerOk = true;
            }
        }
    }

    // 機体の位置を [x, y] で返す。見つからない・無効なときは空にする
    static float[] PositionOf(PlayerBase p)
    {
        if (p == null || !p.gameObject.activeInHierarchy) return new float[0];
        Vector3 pos = p.transform.position;
        return new float[] { (float)Math.Round(pos.x, 4), (float)Math.Round(pos.y, 4) };
    }
}
