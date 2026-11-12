# -*- coding: utf-8 -*-
"""
【このファイルの役割】
段階2b で使う「テスト用の間接操作」の一覧と、それぞれで Player に送る移動の値をまとめた対応表。
状況（敵の位置など）は見ない。「Python からの指示で動きが変わった」ことを画面で見分けるための目印。

値の意味:
- 横（horizontal）: -1 で左、+1 で右
- 縦（vertical）  : -1 で下、+1 で上
（Player の操作は「横・縦・ショット」の3つ。ショットは既存コードで常に出る設定なので、ここでは扱わない）

段階4では、この対応表のところが「状況を見ながら、意図に沿って細かく操作を決める仕組み」に置き換わる。

【どこから使われるか】
step2b_unity_loop.py と command/selector.py。
"""
from typing import List, Tuple

# （名前, 横, 縦）の並び。名前は画面表示とログにそのまま使う
TEST_COMMANDS = [
    ("止まる", 0.0, 0.0),
    ("左", -1.0, 0.0),
    ("右", 1.0, 0.0),
    ("上", 0.0, 1.0),
    ("下", 0.0, -1.0),
]  # type: List[Tuple[str, float, float]]


def command_names():
    # type: () -> List[str]
    """間接操作の名前の一覧を返す（選ぶ係に渡すため）。"""
    return [name for name, _, _ in TEST_COMMANDS]


def movement_of(name):
    # type: (str) -> Tuple[float, float]
    """間接操作の名前から、Player に送る（横, 縦）の値を引く。知らない名前ならエラーにする。"""
    for n, h, v in TEST_COMMANDS:
        if n == name:
            return h, v
    raise KeyError("知らない間接操作です: {}".format(name))
