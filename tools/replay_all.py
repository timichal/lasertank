#!/usr/bin/env python
"""Replay every recorded .lpb in data/quirks/ through the oracle.

Each quirk pack holds exactly one .lvl and its .lpb files, so pairing is by
directory.  Where a pack ships a .ghs (only Tutor-with-Playbacks does), the
recorded move/shot counts are checked against those targets too.

Not every .lpb is a solution.  Six are recordings their own authors describe as
incomplete; they are listed in EXPECTED_NON_WIN below with what they should do,
so this script is a green/red gate: it fails only on a real change in
behaviour, in either direction.

    python tools/replay_all.py [-v] [--traces DIR] [--pack NAME]
"""
import argparse
import pathlib
import re
import struct
import subprocess
import sys
from collections import Counter

ROOT = pathlib.Path(__file__).resolve().parent.parent
ORACLE = ROOT / "oracle" / "build" / "oracle.exe"
QUIRKS = ROOT / "data" / "quirks"

RESULT_RE = re.compile(
    r"^(?P<res>\w+)\s+level=(?P<level>\d+)\s+ticks=(?P<ticks>\d+)\s+"
    r"moves=(?P<moves>\d+)\s+shots=(?P<shots>\d+)\s+keys=(?P<used>\d+)/(?P<total>\d+)\s+"
    r"(?P<name>.*)$"
)

# Recordings that do not reach the flag, and what the oracle must do with them.
# Where a level hint documents the stopping point, the numbers are asserted --
# that turns the corroboration into an actual regression test.
_RM = "rotary-mirrors"
EXPECTED_NON_WIN = {
    (_RM, "Rotary Mirrors-Challenge_0009.lpb"): {
        "result": "UNFINISHED", "moves": 39,
        "why": 'level 9 hint: "blocked at 39 steps"; level 10 is the same puzzle and wins',
    },
    (_RM, "Rotary Mirrors-Challenge_0011.lpb"): {
        "result": "UNFINISHED",
        "why": "level 11, author 'Ihab': no hint text; incomplete (0 bumps in replay)",
    },
    (_RM, "Rotary Mirrors-Challenge_0013.lpb"): {
        "result": "UNFINISHED",
        "why": "level 13, author 'Ihab': no hint text; incomplete (0 bumps in replay)",
    },
    (_RM, "Rotary Mirrors-Challenge_0017.lpb"): {
        "result": "UNFINISHED",
        "why": "level 17, author 'Ihab': no hint text; incomplete (0 bumps in replay)",
    },
    (_RM, "Rotary Mirrors-Challenge_0021.lpb"): {
        "result": "DEAD", "moves": 148, "shots": 257,
        "why": 'level 21 hint documents a solution at "148/257 or better"; the replay hits exactly '
               "148/257 one cell above the flag, then the recording's last two keys turn the tank "
               "around into water (swap the trailing uu for dd and it wins)",
    },
    (_RM, "Rotary Mirrors-Challenge_0036.lpb"): {
        "result": "UNFINISHED", "shots": 419, "keys": 621,
        "why": 'level 36 hint: "it is stopped at step 621 (after 419 shots)"',
    },
}


def read_ghs(path):
    """.ghs / .hs: flat array of 10-byte records indexed by level-1."""
    if not path.exists():
        return {}
    data = path.read_bytes()
    out = {}
    for i in range(len(data) // 10):
        moves, shots = struct.unpack_from("<HH", data, i * 10)
        if moves:
            out[i + 1] = (moves, shots)
    return out


def find_lvl(pack):
    hits = [p for p in pack.iterdir() if p.suffix.lower() == ".lvl"]
    if len(hits) != 1:
        raise SystemExit(f"{pack.name}: expected 1 .lvl, found {len(hits)}")
    return hits[0]


def check_expected(exp, m):
    """Return a list of ways the run departs from its documented outcome."""
    bad = []
    if m["res"] != exp["result"]:
        bad.append(f"result {m['res']}, expected {exp['result']}")
    for key, got in (("moves", "moves"), ("shots", "shots"), ("keys", "used")):
        if key in exp and int(m[got]) != exp[key]:
            bad.append(f"{key} {m[got]}, expected {exp[key]}")
    return bad


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-v", "--verbose", action="store_true")
    ap.add_argument("--traces", metavar="DIR", help="write a per-replay trace here")
    ap.add_argument("--pack", help="only this pack directory")
    args = ap.parse_args()

    if not ORACLE.exists():
        raise SystemExit(f"oracle not built: {ORACLE}\nrun: bash oracle/build.sh")

    traces = pathlib.Path(args.traces) if args.traces else None
    if traces:
        traces.mkdir(parents=True, exist_ok=True)

    packs = sorted(p for p in QUIRKS.iterdir() if p.is_dir())
    if args.pack:
        packs = [p for p in packs if p.name == args.pack]

    totals = Counter()
    problems = []          # anything that should fail the run
    score_mismatch = []
    score_checked = 0

    for pack in packs:
        lpbs = sorted(p for p in pack.iterdir() if p.suffix.lower() == ".lpb")
        if not lpbs:
            continue
        lvl = find_lvl(pack)
        ghs = read_ghs(lvl.with_suffix(".ghs"))
        won = expected = 0

        for lpb in lpbs:
            cmd = [str(ORACLE), "--levels", str(lvl), "--lpb", str(lpb)]
            if traces:
                cmd += ["--trace", str(traces / f"{pack.name}.{lpb.stem}.trace")]
            proc = subprocess.run(cmd, capture_output=True, text=True)
            out = proc.stdout.strip()
            line = out.splitlines()[-1] if out else ""
            m = RESULT_RE.match(line)
            exp = EXPECTED_NON_WIN.get((pack.name, lpb.name))

            totals["total"] += 1
            if not m:
                totals["broken"] += 1
                problems.append((pack.name, lpb.name, line or proc.stderr.strip()))
            elif exp:
                bad = check_expected(exp, m)
                if bad:
                    totals["broken"] += 1
                    problems.append((pack.name, lpb.name,
                                     "documented non-winner changed: " + "; ".join(bad)))
                else:
                    expected += 1
                    totals["expected"] += 1
            elif m["res"] == "WIN":
                won += 1
                totals["win"] += 1
                level = int(m["level"])
                if level in ghs:
                    score_checked += 1
                    got = (int(m["moves"]), int(m["shots"]))
                    if got != ghs[level]:
                        score_mismatch.append((pack.name, lpb.name, got, ghs[level]))
            else:
                totals["broken"] += 1
                problems.append((pack.name, lpb.name, line))

            if args.verbose:
                print(f"  {lpb.name:<40} {line}")

        tail = f"  (+{expected} documented non-winners)" if expected else ""
        print(f"{pack.name:<24} {won}/{len(lpbs) - expected}{tail}")

    print()
    print(f"replayed {totals['total']}   win {totals['win']}   "
          f"documented non-win {totals['expected']}   unexpected {totals['broken']}")
    if score_checked:
        print(f"move/shot counts vs bundled .ghs: "
              f"{score_checked - len(score_mismatch)}/{score_checked} exact")

    if problems:
        print(f"\n--- {len(problems)} unexpected ---")
        for pack, lpb, info in problems:
            print(f"  {pack}/{lpb}: {info}")

    if score_mismatch:
        print(f"\n--- {len(score_mismatch)} score mismatches vs bundled .ghs ---")
        for pack, lpb, got, want in score_mismatch:
            print(f"  {pack}/{lpb}: replay {got[0]}m/{got[1]}s, ghs target {want[0]}m/{want[1]}s")

    return 1 if (problems or score_mismatch) else 0


if __name__ == "__main__":
    sys.exit(main())
