# -*- coding: utf-8 -*-
"""
【このファイルの役割】
Unity から届いた状況の記録（段階3b の形。10フレームに1回）を、LLM に渡す短い日本語の文章にする。

記録の形（LLMBridgeManager が送るもの）:
- self / partner : [x, y, 移動量x×係数, 移動量y×係数]（移動量が無ければ [x, y]、見つからなければ []）
- enemies / bullets : [[ID, x, y, 移動量x×係数, 移動量y×係数], ...]（ID は E10・B340 など）
- events : 出来事（hit / kill / escape）
移動量は「1フレームの移動量 × settings.MOVE_SCALE」なので、ここで割り戻して使う。

文章に入れるもの:
- 自分の位置（と、動ける範囲）、相方の位置（位置だけ。LLM は人間のプレイヤー役なので、協力の指示はしない）
- 敵の弾：全部の数、自分の近く（半径 NEAR_RADIUS 以内）の数、近い順に数発（向き・距離・近づいているか）
- 敵：全部の数、近い順に数体（向き・距離）
- 前回の選択からの出来事（自分の被弾・撃破）

【どこから使われるか】
command/llm_selector.py（段階3c）。段階4・5 でも使う予定。
"""
import math
from typing import Any, Dict, List, Optional, Tuple

import settings

# 自機が動ける範囲（PlayerMoveController.cs の Mathf.Clamp と同じ値）
PLAYER_X_RANGE = (-5.5, 5.5)
PLAYER_Y_RANGE = (-4.0, 4.0)
# 「自分の近く」とみなす距離
NEAR_RADIUS = 2.0
# 近い順に何個まで書くか
TOP_BULLETS = 5
TOP_ENEMIES = 3
# 「近づいている弾」の判定で見る先の長さ（フレーム）。この間にどこまで近づくかを計算する
LOOKAHEAD_FRAMES = 21


def _pos(arr):
    # type: (List[Any]) -> Optional[Tuple[float, float]]
    """機体の並び [x, y, ...] から位置を取り出す。無ければ None。"""
    return (float(arr[0]), float(arr[1])) if arr and len(arr) >= 2 else None


def _entity(arr):
    # type: (List[Any]) -> Tuple[str, Tuple[float, float], Optional[Tuple[float, float]]]
    """敵・弾の並び [ID, x, y, (移動量)] を (ID, 位置, 1フレームの移動量 or None) にする。"""
    move = None
    if len(arr) >= 5:
        move = (float(arr[3]) / settings.MOVE_SCALE, float(arr[4]) / settings.MOVE_SCALE)
    return str(arr[0]), (float(arr[1]), float(arr[2])), move


def direction_name(dx, dy):
    # type: (float, float) -> str
    """自分から見た向き（dx, dy）を「上・右上・右・…」の8方向の言葉にする。"""
    if abs(dx) < 1e-9 and abs(dy) < 1e-9:
        return "同じ位置"
    angle = math.degrees(math.atan2(dy, dx))  # 右が0度、上が90度
    names = ["右", "右上", "上", "左上", "左", "左下", "下", "右下"]
    return names[int(((angle + 22.5) % 360) // 45)]


def closest_approach(rel, move, frames):
    # type: (Tuple[float, float], Optional[Tuple[float, float]], int) -> Tuple[float, int]
    """弾が今の向きと速さのまま進んだとき、frames フレーム以内に自分へ一番近づく距離と、そのフレーム数を返す。

    rel は「自分から見た弾の位置」。自分は動かないものとして計算する（簡単な見積もり）。
    """
    if move is None:
        return math.hypot(rel[0], rel[1]), 0
    vx, vy = move
    v2 = vx * vx + vy * vy
    if v2 < 1e-12:
        return math.hypot(rel[0], rel[1]), 0
    t = -(rel[0] * vx + rel[1] * vy) / v2  # 一番近づくまでのフレーム数
    t = max(0.0, min(float(frames), t))
    cx, cy = rel[0] + vx * t, rel[1] + vy * t
    return math.hypot(cx, cy), int(round(t))


def summarize(record, events=None):
    # type: (Optional[Dict[str, Any]], Optional[List[Dict[str, Any]]]) -> str
    """状況の記録1つ（と、前回の選択からの出来事）を、LLM に渡す日本語の文章にする。"""
    if not record:
        return "まだ状況の情報が届いていません。"
    lines = []
    me = _pos(record.get("self"))
    if me is None:
        lines.append("自分の機体：見つからない（復活中など）")
        me = (0.0, PLAYER_Y_RANGE[0])
    else:
        lines.append("自分の位置：({:.2f}, {:.2f})（動ける範囲は 横 {}〜{}、縦 {}〜{}）".format(
            me[0], me[1], PLAYER_X_RANGE[0], PLAYER_X_RANGE[1], PLAYER_Y_RANGE[0], PLAYER_Y_RANGE[1]))
    partner = _pos(record.get("partner"))
    if partner is not None:
        lines.append("相方の位置：({:.2f}, {:.2f})".format(partner[0], partner[1]))

    # 敵の弾
    bullets = [_entity(b) for b in record.get("bullets") or []]
    rows = []
    for bid, pos, move in bullets:
        rel = (pos[0] - me[0], pos[1] - me[1])
        dist = math.hypot(rel[0], rel[1])
        closest, when = closest_approach(rel, move, LOOKAHEAD_FRAMES)
        rows.append((dist, bid, rel, closest, when))
    rows.sort(key=lambda r: r[0])
    near = sum(1 for r in rows if r[0] <= NEAR_RADIUS)
    lines.append("敵の弾：全部で{}発、自分から半径{}以内に{}発".format(len(rows), NEAR_RADIUS, near))
    for dist, bid, rel, closest, when in rows[:TOP_BULLETS]:
        note = ""
        if closest < dist - 0.05:
            note = "、近づいている（約{:.1f}秒後に距離{:.2f}まで）".format(when * settings.FIXED_DT, closest)
        else:
            note = "、離れている"
        lines.append("  - {}：{} 距離{:.2f}{}".format(bid, direction_name(rel[0], rel[1]), dist, note))

    # 敵
    enemies = [_entity(e) for e in record.get("enemies") or []]
    erows = sorted(((math.hypot(p[0] - me[0], p[1] - me[1]), eid, (p[0] - me[0], p[1] - me[1]))
                    for eid, p, _ in enemies), key=lambda r: r[0])
    lines.append("敵：全部で{}体".format(len(erows)))
    for dist, eid, rel in erows[:TOP_ENEMIES]:
        lines.append("  - {}：{} 距離{:.2f}（横のずれ {:+.2f}）".format(eid, direction_name(rel[0], rel[1]), dist, rel[0]))

    # 前回の選択からの出来事（自分のことだけ）
    hits = sum(1 for e in events or [] if e.get("kind") == "hit" and e.get("who") == "self")
    kills = sum(1 for e in events or [] if e.get("kind") == "kill" and e.get("who") == "self")
    lines.append("前回の選択からの出来事：被弾{}回、撃破{}体".format(hits, kills))
    return "\n".join(lines)
