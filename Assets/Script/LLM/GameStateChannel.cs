// 【このファイルの役割】
// LLMBridge（Python）へゲームの情報を送るための、自作のサイドチャンネル（送る側）。
// 文字列（JSON）を1件ずつ Python に送る。Python 側の受け口は LLMBridge/unity/state_channel.py。
//
// ・チャンネルの ID（下の GUID）は Python 側と同じ値にしてある。変えるときは両方を変える
// ・ML-Agents の決まりで、同じ ID のチャンネルは1つしか登録できない。そのため、このクラスは
//   static で1つだけ持ち、LLMBridgeManager が「登録を外してから登録し直す」形で使う
// ・送った文字列は、次に Unity と Python がやり取りするとき（Player の判断のとき）にまとめて届く
using System;
using Unity.MLAgents.SideChannels;

public class GameStateChannel : SideChannel
{
    // Python 側（state_channel.py）と同じ ID
    public const string ChannelGuid = "7a3c1e52-4b8d-4f2a-9c61-2d5e8b0f4a17";

    // 唯一のチャンネル（シーンをやり直しても同じものを使い続ける）
    static GameStateChannel s_Instance;

    // 唯一のチャンネルを返す（無ければ作る）
    public static GameStateChannel Instance
    {
        get
        {
            if (s_Instance == null)
            {
                s_Instance = new GameStateChannel();
            }
            return s_Instance;
        }
    }

    // チャンネルの ID を設定する
    GameStateChannel()
    {
        ChannelId = new Guid(ChannelGuid);
    }

    // ML-Agents への登録を確実にする。外してから登録し直すので、何回呼んでも二重登録にならない
    // （Python との接続が切れると ML-Agents が登録を全部消すため、シーンの読み込みごとに呼ぶ）
    public static void EnsureRegistered()
    {
        SideChannelManager.UnregisterSideChannel(Instance);
        SideChannelManager.RegisterSideChannel(Instance);
    }

    // 文字列（JSON）を1件、Python へ送る列に積む
    public void SendText(string text)
    {
        using (var msg = new OutgoingMessage())
        {
            msg.WriteString(text);
            QueueMessageToSend(msg);
        }
    }

    // Python から届いたメッセージを受け取る（今は Python から送らないので何もしない）
    protected override void OnMessageReceived(IncomingMessage msg)
    {
    }
}
