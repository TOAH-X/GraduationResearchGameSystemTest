# -*- coding: utf-8 -*-
"""
【このファイルの役割】
段階3b（状況の送信：中身）で実行するスクリプト。OpenAI の API は使わない。
流れと3a の5項目の確認は step3a_state.py をそのまま使い、3b で増えた情報の確認（下の4項目）だけをここに書く。

3b で増えた情報（Unity の LLMBridgeManager が settings.STATE_INTERVAL_FRAMES フレームに1回送る。並びだけの詰めた形）:
- self / partner : [x, y, 移動量x×係数, 移動量y×係数]（移動量が無ければ [x, y]、見つからなければ []）
- enemies / bullets : [[ID, x, y, 移動量x×係数, 移動量y×係数], ...]（現れた最初のフレームは [ID, x, y]）
  ID は文字列で、敵は "E"＋番号（E10 など）、弾は "B"＋番号（B340 など）
- events : 前の記録からこの記録までに起きた出来事。{"kind": hit/kill/escape, "who": self/partner, "enemy": 敵 ID（E10 など。分からなければ null）, "frame": 起きたフレーム}
移動量は「1フレームの移動量 × settings.MOVE_SCALE」。元に戻すには MOVE_SCALE で割る。

追加で確かめること（PLAN.md 段階3b）:
6. 弾と敵の一覧が届き、数が増えたり減ったりする
7. ID と移動量のつじつまが合う
   - 弾はまっすぐ進むので、記録と記録のあいだに進んだ距離 ≒ 1フレームの移動量 × 間隔 になるか
   - 一度消えた ID が、あとでまた出てこないか（オブジェクトプーリングの使い回しを、新しい ID として扱えているか）
   - 同じ記録の中に同じ ID が2回出ないか
   - 敵の一覧の ID が E で、弾の一覧の ID が B で始まっているか（ID だけで敵か弾かが分かるはず）
8. 被弾の出来事のあと、その機体が復活の動き（画面下の外から戻る）をする
9. 消えた敵1体ごとに、撃破か逃げられたの出来事がちょうど1つ対応する
   （ゲーム終了時の統計の撃破数は、既存コードが途中で0に戻すため照合に使えない。PLAN.md 段階3b を参照）

使い方（LLMBridge フォルダで実行し、表示が出たら Unity の再生ボタンを押す）:
  python step3b_state.py --wait-sec 300

終了コード: 0=すべて OK / 2=設定エラー / 5=Unity との接続・通信のエラー / 8=確認項目に NG がある
"""
from __future__ import print_function

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import settings  # noqa: E402
import step3a_state  # noqa: E402

# 弾の「進んだ距離 ≒ 移動量 × 間隔」の照合で許す誤差（位置・移動量の丸めと、わずかな曲がりを見込む）
BULLET_TOLERANCE = 0.05
# 被弾のあと、何フレーム以内に復活の動き（画面下の外にいる）が見えればよいか
HIT_WINDOW_FRAMES = 20
# 復活の動きとみなす高さ（復活は y=-6 の画面外から、1秒かけて y=-3 へ戻る）
RESPAWN_Y = -5.0


def body_pos(arr):
    """機体の並び [x, y, (移動量)] から位置 (x, y) を取り出す。無ければ None。"""
    return (arr[0], arr[1]) if arr and len(arr) >= 2 else None


def entity_parts(arr):
    """敵・弾の並び [ID, x, y, (移動量x, 移動量y)] を (ID, 位置, 移動量 or None) に分ける。移動量は1フレームの値に戻す。"""
    move = (arr[3] / settings.MOVE_SCALE, arr[4] / settings.MOVE_SCALE) if len(arr) >= 5 else None
    return str(arr[0]), (arr[1], arr[2]), move


class StateChecker(object):
    """3b で増えた情報を記録ごとに受け取り、最後に4項目の確認結果を返す係（step3a_state.py の extra）。"""

    def __init__(self):
        """数え上げの入れ物を用意する。"""
        self.interval = settings.STATE_INTERVAL_FRAMES
        self.prev = None            # 前の記録
        self.reload_frames = []     # シーンを読み込み直したフレーム番号（この前後は照合しない）
        self.n_enemies = []         # 記録ごとの敵の数
        self.n_bullets = []         # 記録ごとの弾の数
        self.dup_ids = 0            # 同じ記録の中に同じ ID が2回出た件数（敵と弾をまとめて数える）
        self.gone_ids = set()       # これまでに消えた ID（E10・B340 のように頭の文字で敵と弾が分かれる）
        self.bad_prefix = 0         # 敵の一覧なのに E で始まらない・弾の一覧なのに B で始まらない ID の件数
        self.reappeared = 0         # 一度消えた ID がまた出てきた件数
        self.bullet_checked = 0     # 弾の距離を照合した件数
        self.bullet_bad = []        # 合わなかったもの（最初の5件だけ例として残す）
        self.bullet_bad_count = 0
        self.body_y = {}            # 記録のフレーム番号 → {"self": y, "partner": y}（被弾の確認用）
        self.hits = []              # （起きたフレーム, who）
        self.kills = []             # （起きたフレーム, who, 敵 ID）
        self.escapes = []           # （起きたフレーム, 敵 ID）
        self.pairs_checked = 0      # 消えた敵の照合をした記録の組の数
        self.gone_total = 0         # 記録と記録のあいだに消えた敵の数（照合した組のぶん）
        self.unexplained = 0        # 出来事の付かない消えた敵
        self.no_id = 0              # 敵 ID の無い撃破・逃げられた
        self.wrong = 0              # まだ残っている敵を指した撃破・逃げられた
        self.short_lived = 0        # 記録のあいだに現れて消えた敵を指したもの（記録には一度も出ていない）
        self.seen_enemy_ids = set()

    def on_scene(self, ev):
        """シーンの読み込みを受け取る。読み込み直したフレームの前後は、記録どうしの照合から外す。"""
        if ev.get("loadCount", 1) > 1:
            self.reload_frames.append(int(ev.get("frame", -1)))

    def on_frame(self, fr):
        """1回分の記録を受け取り、数え上げと照合をする。"""
        f = int(fr["frame"])
        enemies = [entity_parts(e) for e in fr.get("enemies") or []]
        bullets = [entity_parts(b) for b in fr.get("bullets") or []]
        self.n_enemies.append(len(enemies))
        self.n_bullets.append(len(bullets))
        self.body_y[f] = {}
        for who in ("self", "partner"):
            pos = body_pos(fr.get(who))
            self.body_y[f][who] = pos[1] if pos else None

        # ID の重複と、一度消えた ID の再登場
        # （ID は頭の文字で敵と弾が分かれるので、まとめて調べる。頭の文字が一覧の種類と合っているかも見る）
        ids = [e[0] for e in enemies] + [b[0] for b in bullets]
        self.dup_ids += len(ids) - len(set(ids))
        self.reappeared += sum(1 for i in ids if i in self.gone_ids)
        self.bad_prefix += sum(1 for e in enemies if not e[0].startswith("E"))
        self.bad_prefix += sum(1 for b in bullets if not b[0].startswith("B"))
        self.seen_enemy_ids.update(e[0] for e in enemies)

        events = fr.get("events") or []
        for ev in events:
            kind = ev.get("kind")
            at = int(ev.get("frame", f))
            if kind == "hit":
                self.hits.append((at, ev.get("who")))
            elif kind == "kill":
                self.kills.append((at, ev.get("who"), ev.get("enemy")))
            elif kind == "escape":
                self.escapes.append((at, ev.get("enemy")))

        prev = self.prev
        if prev is not None:
            pf = int(prev["frame"])
            prev_enemies = {e[0]: e for e in (entity_parts(x) for x in prev.get("enemies") or [])}
            prev_bullets = {b[0]: b for b in (entity_parts(x) for x in prev.get("bullets") or [])}
            cur_enemy_ids = set(e[0] for e in enemies)
            cur_bullet_ids = set(b[0] for b in bullets)
            # 消えた ID を覚える（再登場の確認のため。やり直しをまたいでも ID は続けて振られる）
            self.gone_ids.update(set(prev_enemies) - cur_enemy_ids)
            self.gone_ids.update(set(prev_bullets) - cur_bullet_ids)

            crosses_reload = any(pf < r <= f for r in self.reload_frames)
            if f - pf == self.interval and not crosses_reload:
                self._check_bullets(prev_bullets, bullets, f - pf)
                self._check_gone_enemies(prev_enemies, cur_enemy_ids, events)
        self.prev = fr

    def _check_bullets(self, prev_bullets, bullets, gap):
        """弾ごとに、記録のあいだに進んだ距離が「1フレームの移動量 × 間隔」と合うかを照合する。"""
        for bid, pos, move in bullets:
            if bid not in prev_bullets:
                continue
            _, ppos, pmove = prev_bullets[bid]
            moves = [m for m in (pmove, move) if m is not None]
            if not moves:
                continue
            vx = sum(m[0] for m in moves) / len(moves)
            vy = sum(m[1] for m in moves) / len(moves)
            dx, dy = pos[0] - ppos[0], pos[1] - ppos[1]
            self.bullet_checked += 1
            if abs(dx - vx * gap) > BULLET_TOLERANCE or abs(dy - vy * gap) > BULLET_TOLERANCE:
                self.bullet_bad_count += 1
                if len(self.bullet_bad) < 5:
                    self.bullet_bad.append("id={}: 進んだ距離 ({:.2f}, {:.2f}) / 移動量×{} ({:.2f}, {:.2f})".format(
                        bid, dx, dy, gap, vx * gap, vy * gap))

    def _check_gone_enemies(self, prev_enemies, cur_ids, events):
        """前の記録にいて今の記録にいない敵それぞれに、撃破か逃げられたの出来事がちょうど1つ付いているかを照合する。"""
        self.pairs_checked += 1
        gone = set(prev_enemies) - cur_ids
        self.gone_total += len(gone)
        explained = set()
        for ev in events:
            if ev.get("kind") not in ("kill", "escape"):
                continue
            eid = ev.get("enemy")   # E10 など。分からなければ None
            if eid is None:
                self.no_id += 1
            elif eid in gone:
                explained.add(eid)
            elif eid in cur_ids:
                self.wrong += 1
            else:
                self.short_lived += 1   # 記録と記録のあいだに現れて消えた敵（正常）
        self.unexplained += len(gone - explained)

    def results(self):
        """4項目の確認結果を（名前, OK か, 詳しい内容）の並びで返す。"""
        out = []

        def avg(xs):
            return float(sum(xs)) / len(xs) if xs else 0.0

        # 6. 弾と敵の一覧
        ok6 = bool(self.n_enemies) and max(self.n_enemies) > 0 and max(self.n_bullets) > 0
        out.append(("弾と敵の一覧が届く", ok6, "弾 平均{:.1f}発（最大{}発）、敵 平均{:.1f}体（最大{}体）".format(
            avg(self.n_bullets), max(self.n_bullets or [0]), avg(self.n_enemies), max(self.n_enemies or [0]))))

        # 7. ID と移動量
        ok7 = (self.bullet_checked > 0 and self.bullet_bad_count == 0 and self.dup_ids == 0
               and self.reappeared == 0 and self.bad_prefix == 0)
        detail7 = "弾の距離の照合 {}件・不一致 {}件、同じ記録内の ID の重複 {}件、消えた ID の再登場 {}件、頭の文字が合わない ID {}件".format(
            self.bullet_checked, self.bullet_bad_count, self.dup_ids, self.reappeared, self.bad_prefix)
        if self.bullet_bad:
            detail7 += "（例: " + " / ".join(self.bullet_bad) + "）"
        out.append(("ID と移動量のつじつま", ok7, detail7))

        # 8. 被弾と復活
        confirmed = 0
        for at, who in self.hits:
            for g in sorted(self.body_y):
                if at <= g <= at + HIT_WINDOW_FRAMES:
                    y = self.body_y[g].get(who)
                    if y is not None and y < RESPAWN_Y:
                        confirmed += 1
                        break
        by_who = {w: sum(1 for _, x in self.hits if x == w) for w in ("self", "partner")}
        detail8 = "被弾 {}回（自分{}・相方{}）、うち{}フレーム以内に復活の動きあり {}回".format(
            len(self.hits), by_who["self"], by_who["partner"], HIT_WINDOW_FRAMES, confirmed)
        if not self.hits:
            detail8 += "（被弾が無かったため確かめられていない）"
        out.append(("被弾のあとに復活の動き", confirmed == len(self.hits), detail8))

        # 9. 消えた敵と、撃破・逃げられたの対応
        kills_by = {w: sum(1 for _, x, _ in self.kills if x == w) for w in ("self", "partner")}
        ok9 = self.no_id == 0 and self.wrong == 0 and self.unexplained == 0
        out.append(("消えた敵と撃破・逃げられたの対応", ok9,
                    "撃破 {}回（自分{}・相方{}）、逃げられた {}回。照合した記録の組 {}、そのあいだに消えた敵 {}体、"
                    "出来事の付かない消えた敵 {}体、敵 ID なし {}件、残っている敵を指した {}件、記録のあいだに現れて消えた敵 {}件".format(
                        len(self.kills), kills_by["self"], kills_by["partner"], len(self.escapes), self.pairs_checked,
                        self.gone_total, self.unexplained, self.no_id, self.wrong, self.short_lived)))
        return out


def main(argv=None):
    """step3a_state.py の流れを、3b 用の追加の確認係（StateChecker）つきで動かす。"""
    return step3a_state.main(argv, tag="[step3b]", stage="step3b", extra=StateChecker())


if __name__ == "__main__":
    sys.exit(main())
