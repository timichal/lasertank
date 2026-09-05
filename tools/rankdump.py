#!/usr/bin/env python
"""Layer 4's instrument: dump the ranking groups of every winning trajectory.

What this produces is not training data first and a measurement second -- it is
a measurement first.  Each row is one candidate the *shipped* subgoal expansion
offered at a shot boundary of a winning recording, together with the board
features a learned evaluation would see and whether the winner in fact went
through that candidate.  Reading the file two ways then answers two different
questions with the same bytes:

    Is the successor the winner used even in the group?   -- coverage
    Where does WorkDistance rank it when it is?           -- headroom

Only the second one is a ranking problem, and only a ranking problem is what
layer 4 is allowed to fix (Learn.cs: acceptance stays layer 2's board test).
If coverage is the constraint the answer is a different layer, and this tool is
how that gets found out before a model is fit rather than after.

The trajectories are every winning .lpb the project has:

    data/quirks/<pack>/*.lpb        187 human recordings (6 do not win; skipped)
    build/solutions/<dir>/<coll>/*  the verified solver output

Pairing is by directory for the solver output and by pack for the recordings,
but the *level number* always comes from the .lpb header, never from the file
name -- a recording's own claim about what it plays is the only one that has
been checked against both engines.

    python tools/rankdump.py --out build/reports/rank.tsv
    python tools/rankdump.py --out /tmp/smoke.tsv --limit 20

Columns (TSV, no header -- tools/fit_eval.py names them):

    collection  level  group  at_key  onpath_idx  is_best  tier  work  f0..f16

`onpath_idx` is -1 for a candidate the winner never stood on, otherwise the
keystream index at which it did; `is_best` marks the furthest-along one in the
group, which is the single positive a ranker is asked to put first.  `tier` is
the search's own: 0 advanced, 1 slack, 2 the fallback Goto.
"""
import argparse
import pathlib
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
EXE = ROOT / "build" / "lasertank-solve.exe"
LEVELS = ROOT / "data" / "levels"
QUIRKS = ROOT / "data" / "quirks"

# The recordings are shorter than the solver's cap in almost every case, but
# "almost" is not a thing to build a corpus on: BeginSearch sizes the keystream
# buffer from this, and a recording longer than it could not be replayed at all.
# RecMax is 65500, so this is the whole range the format can express.
MAX_KEYS = 65500


def pairs(solution_dirs, want_quirks):
    """[(lvl path, [lpb path, ...])], one entry per collection."""
    out = []
    if want_quirks:
        for pack in sorted(p for p in QUIRKS.iterdir() if p.is_dir()):
            lvls = [p for p in pack.iterdir() if p.suffix.lower() == ".lvl"]
            lpbs = sorted(p for p in pack.iterdir() if p.suffix.lower() == ".lpb")
            if len(lvls) == 1 and lpbs:
                out.append((lvls[0], lpbs))

    by_collection = {}
    for d in solution_dirs:
        d = pathlib.Path(d)
        if not d.is_absolute():
            d = ROOT / d
        if not d.is_dir():
            print(f"no solutions at {d}", file=sys.stderr)
            continue
        for sub in sorted(p for p in d.iterdir() if p.is_dir()):
            lvl = LEVELS / (sub.name + ".lvl")
            if not lvl.exists():
                print(f"no level file for {sub}", file=sys.stderr)
                continue
            by_collection.setdefault(lvl, []).extend(
                sorted(p for p in sub.iterdir() if p.suffix.lower() == ".lpb"))
    out.extend(sorted(by_collection.items()))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--solutions", nargs="*",
                    default=["build/solutions/l0", "build/solutions/l3n"])
    ap.add_argument("--no-quirks", action="store_true")
    ap.add_argument("--jobs", type=int, default=14)
    ap.add_argument("--limit", type=int, default=0,
                    help="at most N recordings per collection -- for a smoke run")
    ap.add_argument("--sg-closure", type=int, default=0,
                    help="override the closure cap the expansion runs at")
    args = ap.parse_args()

    if not EXE.exists():
        sys.exit(f"no {EXE} -- run bash src/build.sh")

    out = pathlib.Path(args.out)
    if not out.is_absolute():
        out = ROOT / out
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()                       # the exe appends; a stale file would mix runs

    total = 0
    with tempfile.TemporaryDirectory() as tmp:
        for lvl, lpbs in pairs(args.solutions, not args.no_quirks):
            if args.limit:
                lpbs = lpbs[:args.limit]
            if not lpbs:
                continue
            listing = pathlib.Path(tmp) / (lvl.stem + ".txt")
            listing.write_text("\n".join(str(p) for p in lpbs), encoding="utf-8")
            cmd = [str(EXE), "--levels", str(lvl), "--lpb-list", str(listing),
                   "--rank-dump", str(out), "--jobs", str(args.jobs),
                   "--max-keys", str(MAX_KEYS)]
            if args.sg_closure:
                cmd += ["--sg-closure", str(args.sg_closure)]
            r = subprocess.run(cmd, capture_output=True, text=True)
            sys.stdout.write(r.stdout)
            if r.stderr.strip():
                sys.stderr.write(r.stderr)
            if r.returncode != 0:
                sys.exit(f"{lvl.stem}: exit {r.returncode}")
            total += len(lpbs)

    size = out.stat().st_size if out.exists() else 0
    rows = sum(1 for _ in out.open(encoding="utf-8")) if size else 0
    print(f"{total} recordings -> {rows} rows, {size / 1e6:.1f} MB in {out}")


if __name__ == "__main__":
    main()
