#!/usr/bin/env python
"""Print a .lvl level as ASCII, with its name, author, difficulty and hint.

    python tools/dump_level.py FILE.lvl N [N ...]

Playfield records are 576 bytes; PF is char[16][16] indexed PF[x][y], x = column.
"""
import pathlib
import struct
import sys

REC = 576

SYM = {
    0: ".",   # dirt
    1: "T",   # tank start
    2: "F",   # flag
    3: "~",   # water
    4: "#",   # solid block
    5: "B",   # movable block
    6: "b",   # bricks
    7: "^",   # anti-tank up
    8: ">",   # anti-tank right
    9: "v",   # anti-tank down
    10: "<",  # anti-tank left
    11: "m",  # mirror up-left
    12: "n",  # mirror up-right
    13: "o",  # mirror dn-right
    14: "p",  # mirror dn-left
    15: "U",  # conveyor up
    16: "R",  # conveyor right
    17: "D",  # conveyor down
    18: "L",  # conveyor left
    19: "C",  # crystal block
    20: "q",  # roto mirror up-left
    21: "w",  # roto mirror up-right
    22: "e",  # roto mirror dn-right
    23: "r",  # roto mirror dn-left
    24: "I",  # ice
    25: "i",  # thin ice
}

LEGEND = ("  . dirt  # solid  B block  b bricks  ~ water  F flag  T tank\n"
          "  ^>v< anti-tank   mnop mirrors   qwer roto mirrors\n"
          "  URDL conveyors   C crystal   I ice   i thin ice   0-7 tunnel")


def cell(v):
    if v >= 0x40:
        return str((v & 0x0F) >> 1)     # tunnel id 0-7
    return SYM.get(v, "?")


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    path = pathlib.Path(sys.argv[1])
    data = path.read_bytes()

    for arg in sys.argv[2:]:
        n = int(arg)
        rec = data[(n - 1) * REC:n * REC]
        if len(rec) < REC:
            print(f"level {n}: out of range ({len(data)//REC} levels)")
            continue
        pf = rec[:256]
        name = rec[256:287].split(b"\0")[0].decode("latin1")
        hint = rec[287:543].split(b"\0")[0].decode("latin1")
        auth = rec[543:574].split(b"\0")[0].decode("latin1")
        diff = struct.unpack_from("<H", rec, 574)[0]

        print(f"level {n}: {name!r} by {auth!r} difficulty={diff}")
        if hint:
            print(f"hint: {hint}")
        print("    " + "".join(f"{x%10}" for x in range(16)))
        for y in range(16):
            print(f" {y:2d} " + "".join(cell(pf[x * 16 + y]) for x in range(16)))
        print(LEGEND)
        print()


if __name__ == "__main__":
    main()
