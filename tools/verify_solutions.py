#!/usr/bin/env python
"""Verify solver output: replay every produced .lpb through *both* engines.

This is the gate that makes the solver's claims mean something.  The solver
already replays each solution through its own engine before writing the file --
but that is the same code that produced it, so it proves only self-consistency.
Here each .lpb is handed to the frozen C oracle and to the C# core as an
ordinary recording, with --field --bmf, and three things must hold:

  1. both engines report WIN,
  2. their traces are byte-identical (this is the Phase 2/3 gate, reused: a
     solved level is a long, legal, non-random path through the engine, which
     is exactly the coverage random keystreams cannot reach),
  3. the reported moves/shots match between the two.

Then, informationally, each solution is compared with the level's .ghs record.
Falling short of the record is not a failure -- "any valid solution" is the
contract -- but the ratio is what says whether the solver is embarrassing.

Layout: solver output lives in <out>/<collection>/NNNNN.lpb, and the level file
is data/levels/<collection>.lvl.  Point this at either the <out> root or a
single collection directory.

    python tools/verify_solutions.py solutions
    python tools/verify_solutions.py solutions/LaserTank --jobs 8

Exit 0 only if every solution verified.
"""
import argparse
import concurrent.futures
import pathlib
import statistics
import struct
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402

ROOT = engines.ROOT
LEVELS = ROOT / "data" / "levels"
QUIRKS = ROOT / "data" / "quirks"


def find_lvl(collection):
    """<collection> -> its .lvl, in data/levels/ or in a data/quirks/ pack.

    Both halves of the name are matched case-insensitively.  The quirk packs
    mix .lvl and .LVL -- four of the ten ship uppercase, and exactly those four
    are the biggest packs -- and the stem has its own casing trap: the file is
    `Tutor-with-Playbacks.LVL`, so a Windows shell happily opens it as
    `Tutor-With-Playbacks.LVL` and the output directory is then named with the
    capital W the file does not have.
    """
    want = collection.lower()
    for cand in LEVELS.iterdir():
        if cand.suffix.lower() == ".lvl" and cand.stem.lower() == want:
            return cand
    for pack in sorted(q for q in QUIRKS.iterdir() if q.is_dir()):
        for cand in pack.iterdir():
            if cand.suffix.lower() == ".lvl" and cand.stem.lower() == want:
                return cand
    return None


def ghs_target(lvl_path, level):
    """(moves, shots) from the sibling .ghs, or None."""
    ghs = lvl_path.with_suffix(".ghs")
    if not ghs.exists():
        return None
    data = ghs.read_bytes()
    at = (level - 1) * 10
    if at + 10 > len(data):
        return None
    moves, shots = struct.unpack_from("<HH", data, at)
    if moves == 0 or moves >= 65500:
        return None
    return moves, shots


def lpb_level(path):
    """The level number out of the 66-byte header."""
    head = path.read_bytes()[:66]
    return struct.unpack_from("<H", head, 62)[0]


def verify_one(lvl_path, lpb, scratch):
    """-> (ok, level, keys, moves, shots, note)."""
    level = lpb_level(lpb)
    nkeys = len(lpb.read_bytes()) - 66
    case = engines.LpbCase(str(lvl_path), str(lpb))
    a, b = engines.run_pair(case, scratch.a, scratch.b, field=True, bmf=True)

    div = engines.compare(a, b)
    if div is not None:
        return False, level, nkeys, 0, 0, "engines diverge (%s): %s" % (div.sig, div.kind)

    out = engines.outcome(scratch.a)
    if out is None:
        return False, level, nkeys, 0, 0, "no trace footer"
    if out["result"] != "WIN":
        return False, level, nkeys, out["moves"], out["shots"], "oracle says " + out["result"]
    if a.rc != engines.RC_WIN:
        return False, level, nkeys, out["moves"], out["shots"], "oracle exit %d" % a.rc
    return True, level, nkeys, out["moves"], out["shots"], ""


def verify_collection(directory, jobs, lvl=None):
    collection = directory.name
    lvl = lvl or find_lvl(collection)
    lpbs = sorted(p for p in directory.iterdir() if p.suffix.lower() == ".lpb")
    if not lpbs:
        return None
    if lvl is None:
        print("  %-22s no .lvl found for this collection" % collection)
        return (collection, 0, len(lpbs), [])

    rows, bad = [], []

    def work(lpb):
        with engines.Scratch("verify") as sc:
            return lpb, verify_one(lvl, lpb, sc)

    with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
        for lpb, res in pool.map(work, lpbs):
            ok, level, nkeys, moves, shots, note = res
            target = ghs_target(lvl, level)
            ratio = (nkeys / sum(target)) if target else None
            rows.append((level, ok, nkeys, moves, shots, target, ratio, note))
            if not ok:
                bad.append((lpb.name, note))

    good = [r for r in rows if r[1]]
    ratios = sorted(r[6] for r in good if r[6] is not None)
    exact = sum(1 for r in good
                if r[5] and (r[3], r[4]) == r[5])
    line = "  %-22s %4d/%-4d verified" % (collection, len(good), len(rows))
    if ratios:
        line += "   ratio p50 %.1fx  p90 %.1fx  worst %.1fx" % (
            statistics.median(ratios), ratios[min(len(ratios) - 1, int(.9 * len(ratios)))],
            ratios[-1])
    if exact:
        line += "   %d match the record exactly" % exact
    print(line)
    for name, note in bad:
        print("      FAIL %-28s %s" % (name, note))
    return (collection, len(good), len(rows), bad)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("path", nargs="?", default="solutions",
                    help="solver output root, or one collection directory")
    ap.add_argument("--jobs", type=int, default=8)
    # Normally the .lvl is found from the directory name, which is how a
    # campaign's output is laid out.  A caller that already knows the path --
    # the interactive driver hands one candidate at a time to this gate -- says
    # so instead, and is then not required to keep its levels in data/.
    ap.add_argument("--levels", help="the .lvl these solutions play, "
                                     "instead of looking it up by directory name")
    args = ap.parse_args()

    engines.require_engines()
    lvl = pathlib.Path(args.levels) if args.levels else None
    root = pathlib.Path(args.path)
    if not root.exists():
        raise SystemExit("no such directory: %s" % root)

    dirs = [root] if any(p.suffix.lower() == ".lpb" for p in root.iterdir()) \
        else sorted(p for p in root.iterdir() if p.is_dir())
    if not dirs:
        raise SystemExit("no .lpb files under %s" % root)

    print("verifying solver output through both engines (--field --bmf)\n")
    total_ok = total = 0
    failures = []
    for d in dirs:
        res = verify_collection(d, args.jobs, lvl)
        if res is None:
            continue
        _, ok, n, bad = res
        total_ok += ok
        total += n
        failures += bad

    print("\n%d/%d solutions verified" % (total_ok, total))
    if failures:
        print("%d FAILED -- these .lpb do not win, or the two engines disagree on them"
              % len(failures))
        return 1
    if total_ok != total:
        # A collection whose .lvl could not be found checks nothing, and
        # "nothing checked" must never read as a pass.
        print("%d solutions were NOT checked -- see above" % (total - total_ok))
        return 1
    print("every solution wins, and both engines agree on every tick of every one")
    return 0


if __name__ == "__main__":
    sys.exit(main())
