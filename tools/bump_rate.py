#!/usr/bin/env python
"""Classify every consumed keypress in an oracle trace.

A key that produces neither a move, a turn, nor a shot is a "bump" -- the tank
walked into something solid.  Human recordings bump rarely, so a high bump rate
is the signature of a replay that has desynced from the original engine, while
a low rate says the replay is faithful and the recording simply does not finish.

    python tools/bump_rate.py TRACE [TRACE ...]
"""
import pathlib
import re
import sys

FIELD = re.compile(r"(\w+)=([-\d,]+)")


def parse(line):
    return {k: v for k, v in FIELD.findall(line)}


def classify(path):
    prev = None
    moves = turns = shots = bumps = 0
    for line in pathlib.Path(path).read_text(errors="replace").splitlines():
        if line.startswith("#"):
            continue
        f = parse(line)
        if "t" not in f:
            continue
        cur = {
            "P": int(f["P"]),
            "dir": int(f["T"].split(",")[2]),
            "m": int(f["S"].split(",")[0]),
            "s": int(f["S"].split(",")[1]),
        }
        if prev is not None and cur["P"] > prev["P"]:
            if cur["s"] > prev["s"]:
                shots += 1
            elif cur["m"] > prev["m"]:
                moves += 1
            elif cur["dir"] != prev["dir"]:
                turns += 1
            else:
                bumps += 1
        prev = cur
    used = moves + turns + shots + bumps
    return moves, turns, shots, bumps, used


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    print(f"{'trace':<46} {'move':>5} {'turn':>5} {'shot':>5} {'bump':>5} {'keys':>5} {'bump%':>6}")
    for p in sys.argv[1:]:
        m, t, s, b, used = classify(p)
        pct = (100.0 * b / used) if used else 0.0
        print(f"{pathlib.Path(p).name:<46} {m:>5} {t:>5} {s:>5} {b:>5} {used:>5} {pct:>5.1f}%")


if __name__ == "__main__":
    main()
