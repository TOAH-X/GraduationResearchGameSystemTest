// 【このファイルの役割】
// Unity 側の LLMBridge の窓口となるマネージャー。LLMTestScene にだけ置く。
// Python（LLMBridge）とつながっているときだけ、ゲームの情報を GameStateChannel で Python へ送る。
// Python とつながっていないとき（ふつうの再生）は何もしない。既存のシーン・スクリプトには影響しない（読むだけ）。
//
// 送るもの（JSON。1件ずつ送る）:
//   ・"scene" … シーンを読み込むたび。何回目の読み込みか、設定値（fixedDeltaTime、DecisionPeriod、MaxStep、
//               送る間隔、移動量の係数）、シーン内の PlayerAgent の一覧（無効なものや DontDestroyOnLoad に残っているものも含む）
//   ・"frame" … sendIntervalFrames（既定10）フレームに1回。次のものを含む（段階3b）
//       - 自分（PlayerID 1）と相方：[x, y, 移動量x×係数, 移動量y×係数]
//       - 敵（EnemyParent の有効な子）：["E10", x, y, 移動量x×係数, 移動量y×係数] の並び（ID は E＋番号）
//       - 敵の弾（BulletParent の有効な子）：["B340", x, y, …] の並び（ID は B＋番号。自機と相方の弾は送らない）
//       - 出来事：前の記録からこの記録までに起きた 被弾（hit）・撃破（kill）・画面外へ逃げられた（escape）。起きたフレーム番号つき
//     移動量が無いとき（現れた最初のフレーム）は [x, y] / [ID, x, y]、機体が見つからないときは []
//
// 追跡は毎フレーム行う（ID の振り方、使い回しの見分け、出来事の見つけ方のため）。送るのを間引くだけ。
// 数字は小数2桁で書き出す（末尾の0は省く）。データ量を抑えるため、JsonUtility ではなく自分で文字列を組み立てる。
// 移動量は「1フレームで進んだ距離（前のフレームの位置との差）」に MoveScale（既定12）を掛けたもの。
// 12 は1手（段階3d の LLM の1つの操作の長さ）のフレーム数なので、送る移動量はそのまま「1手で進む距離」になる。
//
// ID：敵と弾は使い回される（オブジェクトプーリング。非表示にして再利用される）ので、Unity の番号ではなく、
//     「新しく現れた」たびにこのマネージャーが新しい通し番号を振る。番号は敵と弾で別々に1から数え、
//     頭に敵は "E"、弾は "B" を付ける（E10、B340 など）。ID だけで敵か弾かが分かり、人や LLM が読んでも区別しやすい。
//     同じフレームのうちに消えて使い回された場合（非表示の瞬間が見えない）も、
//     「敵の HP が増えた」か「1フレームで 1.0 以上ワープした」ことで見分け、新しい番号を振る。
// 出来事の見つけ方（既存のスクリプトは変えず、値を読むだけ）:
//   - 被弾  … 機体の HP（PlayerBase.CurrentHP）が前のフレームより減った。HP の数値そのものは送らない
//   - 撃破  … ActionLogController の撃破数が増えた。奇数 ID の機体（StatsA）と偶数 ID の機体（StatsB）で別々に数えている
//   - 逃げられた … ActionLogController の「敵が消えた回数」（StatsTotal.attackedCount。倒されても画面外でも増える）
//                  の増え方から、撃破数の増え方を引いたもの（＝倒されずに消えた敵）
//   - どの敵か … そのフレームで消えた敵の ID を当てる（画面の外側に近いものを「逃げられた」に、残りを「撃破」に）
//
// 既存のマネージャーと同じく SafeSingleton を使う。シーンをまたがず（DontDestroyOnLoad にしない）、
// シーンを読み込むたびに作り直される。読み込みの回数・フレーム番号・ID の通し番号・まだ送っていない出来事は
// static で持ち、やり直しても続けて扱う。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

public class LLMBridgeManager : SafeSingleton<LLMBridgeManager>
{
    [Header("LLMBridge 設定")]
    [SerializeField] int selfPlayerID = 1;                      // 自分（LLM が操作する被支援プレイヤー）の PlayerID
    [SerializeField] string playerParentName = "PlayerParent";  // 機体がまとめて入っている親オブジェクトの名前
    [SerializeField] string enemyParentName = "EnemyParent";    // 敵がまとめて入っている親オブジェクトの名前
    [SerializeField] string bulletParentName = "BulletParent";  // 敵の弾がまとめて入っている親オブジェクトの名前
    [SerializeField] int sendIntervalFrames = 10;               // 何フレームに1回 "frame" を送るか（Python の settings.STATE_INTERVAL_FRAMES と同じ値）
    [SerializeField] float moveScale = 12f;                     // 移動量に掛ける係数（Python の settings.MOVE_SCALE と同じ値。12＝1手のフレーム数）

    // 1フレームでこれ以上動いたら「同じフレームのうちに使い回されて別の場所に出てきた」とみなす
    const float ReuseJumpDistance = 1.0f;

    static int s_SceneLoadCount = 0;    // これまでにシーンを読み込んだ回数（やり直しても続けて数える）
    static int s_Frame = 0;             // フレームの通し番号（やり直しても続けて数える）
    static int s_NextEnemyId = 1;       // 次に振る敵の番号（送るときは頭に EnemyIdPrefix を付ける）
    static int s_NextBulletId = 1;      // 次に振る弾の番号（送るときは頭に BulletIdPrefix を付ける）
    const string EnemyIdPrefix = "E";   // 敵の ID の頭の文字
    const string BulletIdPrefix = "B";  // 弾の ID の頭の文字
    static readonly List<GameEvent> s_PendingEvents = new List<GameEvent>();   // まだ送っていない出来事

    bool isActive = false;              // このシーンで情報を送るかどうか（Python とつながっているときだけ true）
    PlayerBase selfPlayer;              // 自分の機体
    PlayerBase partnerPlayer;           // 相方の機体
    Transform enemyParent;              // 敵の親
    Transform bulletParent;             // 敵の弾の親

    // 前のフレームの値（移動量と出来事を求めるため）
    readonly BodyTrack selfTrack = new BodyTrack();
    readonly BodyTrack partnerTrack = new BodyTrack();
    readonly Dictionary<Transform, Tracked> enemies = new Dictionary<Transform, Tracked>();
    readonly Dictionary<Transform, Tracked> bullets = new Dictionary<Transform, Tracked>();
    bool hasCounts = false;             // ActionLogController の数を一度でも読んだか
    int lastKillA, lastKillB, lastKillTotal, lastGoneTotal;

    // ---- 追跡用の入れ物 ----
    class BodyTrack
    {
        public PlayerBase player;       // 前のフレームで見ていた機体
        public bool hasPos;             // 前のフレームの位置があるか
        public Vector2 pos;             // 前のフレームの位置
        public int hp = int.MinValue;   // 前のフレームの HP（まだ無ければ MinValue）
    }

    class Tracked
    {
        public int id;                  // 振った通し番号
        public Vector2 pos;             // 前のフレームの位置
        public bool seen;               // このフレームで見つかったか（見つからなければ消えた）
        public EnemyBase enemy;         // 敵なら EnemyBase（使い回しを HP で見分けるため。弾なら null）
        public int hp = int.MinValue;   // 前のフレームの敵の HP（送らない。使い回しの見分けにだけ使う）
    }

    struct Gone
    {
        public int id;
        public Vector2 pos;
    }

    // 1フレームで見た機体・敵・弾の状態（送るときに文字列にする）
    struct Snapshot
    {
        public bool found;              // 見つかったか（機体のときだけ使う）
        public int id;                  // 敵・弾の ID
        public Vector2 pos;
        public bool hasMove;            // 移動量があるか（現れた最初のフレームは無い）
        public Vector2 move;            // 1フレームの移動量（係数を掛ける前）
    }

    class GameEvent
    {
        public string kind;             // hit（被弾）/ kill（撃破）/ escape（画面外へ逃げられた）
        public string who = "";         // hit・kill のとき：self（自分）/ partner（相方）
        public int enemy = -1;          // kill・escape のとき：どの敵か（番号。送るときは "E10" の形。分からなければ -1 で、null として送る）
        public int frame;               // 起きたフレーム番号
    }

    // ---- "scene" の形（1ゲームに1回なので JsonUtility を使う） ----
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
        public int sendInterval;        // 設定値："frame" を送る間隔（フレーム）
        public float moveScale;         // 設定値：移動量に掛ける係数
        public int playerAgentCount;    // シーン内の PlayerAgent の数（無効なものも含む）
        public AgentInfo[] playerAgents;
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

    // 毎フレーム呼ばれる。追跡と出来事の検出は毎フレーム行い、"frame" は sendIntervalFrames に1回だけ送る
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
        Snapshot self = TrackBody(selfPlayer, selfTrack, "self");
        Snapshot partner = TrackBody(partnerPlayer, partnerTrack, "partner");

        var goneEnemies = new List<Gone>();
        List<Snapshot> enemyList = TrackChildren(FindParent(ref enemyParent, enemyParentName), enemies, ref s_NextEnemyId, goneEnemies, true);
        List<Snapshot> bulletList = TrackChildren(FindParent(ref bulletParent, bulletParentName), bullets, ref s_NextBulletId, null, false);
        AddKillAndEscapeEvents(goneEnemies);

        if (s_Frame % Mathf.Max(1, sendIntervalFrames) == 0)
        {
            GameStateChannel.Instance.SendText(BuildFrameJson(self, partner, enemyList, bulletList));
            s_PendingEvents.Clear();
        }
        s_Frame++;
    }

    // 機体1つの位置と移動量を求め、HP が減っていれば被弾の出来事をためる
    Snapshot TrackBody(PlayerBase p, BodyTrack track, string who)
    {
        // 機体が入れ替わったら、前のフレームの値は使わない
        if (track.player != p)
        {
            track.player = p;
            track.hasPos = false;
            track.hp = int.MinValue;
        }
        if (p == null || !p.gameObject.activeInHierarchy)
        {
            track.hasPos = false;
            return new Snapshot { found = false };
        }

        Vector2 pos = p.transform.position;
        var snap = new Snapshot { found = true, pos = pos, hasMove = track.hasPos, move = pos - track.pos };
        track.pos = pos;
        track.hasPos = true;

        int hp = p.CurrentHP;
        if (track.hp != int.MinValue && hp < track.hp)
        {
            s_PendingEvents.Add(new GameEvent { kind = "hit", who = who, frame = s_Frame });
        }
        track.hp = hp;
        return snap;
    }

    // 親の下の有効な子（敵や弾）に ID を振り、位置と移動量を返す。消えたものは gone に入れる（null なら入れない）
    // isEnemy が true なら、HP が増えたことでも使い回しを見分ける
    static List<Snapshot> TrackChildren(Transform parent, Dictionary<Transform, Tracked> table, ref int nextId, List<Gone> gone, bool isEnemy)
    {
        foreach (var t in table.Values) t.seen = false;

        var list = new List<Snapshot>();
        if (parent != null)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == null || !child.gameObject.activeSelf) continue;

                Vector2 pos = child.position;
                Tracked tr;
                bool hasMove;
                Vector2 move = Vector2.zero;
                if (table.TryGetValue(child, out tr))
                {
                    // 同じフレームのうちに「消えて、使い回されて、別の場所に出てきた」かを見分ける
                    //   ・敵の HP が前のフレームより増えた（使い回すときに HP が最大に戻される）
                    //   ・1フレームで ReuseJumpDistance 以上ワープした
                    Vector2 delta = pos - tr.pos;
                    int hp = HpOf(tr);
                    bool reused = delta.magnitude >= ReuseJumpDistance
                                  || (tr.hp != int.MinValue && hp > tr.hp);
                    if (reused)
                    {
                        // 古い番号は「消えた」扱いにして、新しい番号を振る
                        if (gone != null) gone.Add(new Gone { id = tr.id, pos = tr.pos });
                        tr.id = nextId++;
                        hasMove = false;
                    }
                    else
                    {
                        hasMove = true;
                        move = delta;
                    }
                    tr.hp = hp;
                }
                else
                {
                    // 新しく現れた（または非表示から再利用された）ので、新しい番号を振る
                    tr = new Tracked { id = nextId++ };
                    if (isEnemy) tr.enemy = child.GetComponent<EnemyBase>();
                    tr.hp = HpOf(tr);
                    table[child] = tr;
                    hasMove = false;
                }
                tr.pos = pos;
                tr.seen = true;
                list.Add(new Snapshot { found = true, id = tr.id, pos = pos, hasMove = hasMove, move = move });
            }
        }

        // 見つからなかったもの（非表示になった・消えた）を表から外す
        var removed = new List<Transform>();
        foreach (var kv in table)
        {
            if (!kv.Value.seen) removed.Add(kv.Key);
        }
        foreach (var key in removed)
        {
            if (gone != null) gone.Add(new Gone { id = table[key].id, pos = table[key].pos });
            table.Remove(key);
        }
        return list;
    }

    // ActionLogController の数の増え方から、撃破と「画面外へ逃げられた」の出来事をためる
    void AddKillAndEscapeEvents(List<Gone> goneEnemies)
    {
        if (!ActionLogController.IsAvailable()) return;
        var log = ActionLogController.Instance;
        int killA = log.StatsA.killedCount;
        int killB = log.StatsB.killedCount;
        int killTotal = log.StatsTotal.killedCount;
        int goneTotal = log.StatsTotal.attackedCount;   // 敵が消えた回数（倒されても画面外でも増える）

        // 初めて読んだとき、または数が0に戻されたとき（ゲーム終了時など）は、基準を取り直すだけにする
        if (!hasCounts || killA < lastKillA || killB < lastKillB || killTotal < lastKillTotal || goneTotal < lastGoneTotal)
        {
            hasCounts = true;
            lastKillA = killA; lastKillB = killB; lastKillTotal = killTotal; lastGoneTotal = goneTotal;
            return;
        }

        int dKillA = killA - lastKillA;
        int dKillB = killB - lastKillB;
        int dEscape = (goneTotal - lastGoneTotal) - (killTotal - lastKillTotal);
        lastKillA = killA; lastKillB = killB; lastKillTotal = killTotal; lastGoneTotal = goneTotal;
        if (dKillA <= 0 && dKillB <= 0 && dEscape <= 0) return;

        // 消えた敵を「画面の外側に近い順」に並べ、逃げられた分を先に当て、残りを撃破に当てる
        goneEnemies.Sort((a, b) => Outside(b.pos).CompareTo(Outside(a.pos)));
        int next = 0;
        bool selfIsOdd = selfPlayerID % 2 == 1;   // StatsA は奇数 ID の機体、StatsB は偶数 ID の機体
        for (int i = 0; i < dEscape; i++)
        {
            s_PendingEvents.Add(new GameEvent { kind = "escape", enemy = next < goneEnemies.Count ? goneEnemies[next++].id : -1, frame = s_Frame });
        }
        for (int i = 0; i < dKillA; i++)
        {
            s_PendingEvents.Add(new GameEvent { kind = "kill", who = selfIsOdd ? "self" : "partner", enemy = next < goneEnemies.Count ? goneEnemies[next++].id : -1, frame = s_Frame });
        }
        for (int i = 0; i < dKillB; i++)
        {
            s_PendingEvents.Add(new GameEvent { kind = "kill", who = selfIsOdd ? "partner" : "self", enemy = next < goneEnemies.Count ? goneEnemies[next++].id : -1, frame = s_Frame });
        }
    }

    // ---- "frame" の文字列を組み立てる（並びだけの詰めた形。数字は小数2桁） ----
    string BuildFrameJson(Snapshot self, Snapshot partner, List<Snapshot> enemyList, List<Snapshot> bulletList)
    {
        var sb = new StringBuilder(256 + 24 * (enemyList.Count + bulletList.Count));
        sb.Append("{\"type\":\"frame\",\"frame\":").Append(s_Frame);
        sb.Append(",\"academyStep\":").Append(Academy.Instance.TotalStepCount);
        sb.Append(",\"time\":").Append(Num(Time.fixedTime));
        sb.Append(",\"self\":"); AppendBody(sb, self);
        sb.Append(",\"partner\":"); AppendBody(sb, partner);
        sb.Append(",\"enemies\":"); AppendEntities(sb, enemyList, EnemyIdPrefix);
        sb.Append(",\"bullets\":"); AppendEntities(sb, bulletList, BulletIdPrefix);
        sb.Append(",\"events\":[");
        for (int i = 0; i < s_PendingEvents.Count; i++)
        {
            GameEvent e = s_PendingEvents[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"kind\":\"").Append(e.kind).Append('"');
            if (e.who.Length > 0) sb.Append(",\"who\":\"").Append(e.who).Append('"');
            if (e.kind != "hit")
            {
                sb.Append(",\"enemy\":");
                if (e.enemy >= 0) sb.Append('"').Append(EnemyIdPrefix).Append(e.enemy).Append('"');
                else sb.Append("null");
            }
            sb.Append(",\"frame\":").Append(e.frame).Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // 機体を [x, y, 移動量x×係数, 移動量y×係数] で書く（移動量が無ければ [x, y]、見つからなければ []）
    void AppendBody(StringBuilder sb, Snapshot s)
    {
        sb.Append('[');
        if (s.found)
        {
            sb.Append(Num(s.pos.x)).Append(',').Append(Num(s.pos.y));
            if (s.hasMove) sb.Append(',').Append(Num(s.move.x * moveScale)).Append(',').Append(Num(s.move.y * moveScale));
        }
        sb.Append(']');
    }

    // 敵や弾を [[ID, x, y, 移動量x×係数, 移動量y×係数], ...] で書く（移動量が無ければ [ID, x, y]）
    // ID は頭に prefix（敵は "E"、弾は "B"）を付けた文字列にする
    void AppendEntities(StringBuilder sb, List<Snapshot> list, string prefix)
    {
        sb.Append('[');
        for (int i = 0; i < list.Count; i++)
        {
            Snapshot s = list[i];
            if (i > 0) sb.Append(',');
            sb.Append("[\"").Append(prefix).Append(s.id).Append("\",").Append(Num(s.pos.x)).Append(',').Append(Num(s.pos.y));
            if (s.hasMove) sb.Append(',').Append(Num(s.move.x * moveScale)).Append(',').Append(Num(s.move.y * moveScale));
            sb.Append(']');
        }
        sb.Append(']');
    }

    // 数字を小数2桁の文字列にする（末尾の0は省く。パソコンの地域設定に関係なく小数点は "."）
    static string Num(float v)
    {
        string s = Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    // 敵の今の HP を返す（敵でない、または EnemyBase が無ければ MinValue）
    static int HpOf(Tracked tr)
    {
        return tr.enemy != null ? tr.enemy.CurrentHP : int.MinValue;
    }

    // 画面の外側への近さ（PlayerAgent の観察と同じく、x は 6、y は 5 で割った値の大きいほう）
    static float Outside(Vector2 p)
    {
        return Mathf.Max(Mathf.Abs(p.x) / 6f, Mathf.Abs(p.y) / 5f);
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
            sendInterval = sendIntervalFrames,
            moveScale = moveScale,
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

    // 親オブジェクトを名前で探す（見つかったら覚えておき、消えたら探し直す）
    static Transform FindParent(ref Transform cache, string name)
    {
        if (cache == null)
        {
            GameObject go = GameObject.Find(name);
            cache = go != null ? go.transform : null;
        }
        return cache;
    }
}
