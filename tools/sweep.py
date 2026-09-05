#!/usr/bin/env python
"""Run one fixed keystream through every level of a .lvl on both engines and diff.

With no arguments this is **the empty-keystream sweep** -- the check that has
been rewritten as an ad-hoc shell loop once a session since Phase 2 step 1:

    python tools/sweep.py                 # 2,347 levels, --keys "", --field --bmf

Default surface is `data/levels/LaserTank.lvl` (2,030) plus every `data/quirks/`
pack (317), which is the 2,347 the Status block pins.  `--all` widens it to all
13 collections; naming files explicitly overrides both.

What an empty keystream proves is narrow and worth restating, because it is
cheap enough to run after any change: both engines trace exactly two lines,
`t=0` and `t=1` -- level load, then one idle tick.  Matching those means the
`.lvl` parser, the `TGAMEREC` layout, `BuildBMField`, `PutLevel`, `Animate`, the
fnv1a hashes and the trace formatting agree, on every level in the game.  It
reaches no game logic at all: nothing but a consumed key can call `MoveTank`.
For the logic, use `tools/fuzz.py`.

`--keys` generalises it -- the same keystream on 2,030 levels is a fine smoke
test after touching a specific rule -- but note that a fixed keystream is a
*fixed* path; random ones are `tools/fuzz.py`'s job.

Exit: 0 identical, 1 diverged, 3 cosmetic-only divergence.
"""
import argparse
import pathlib
import sys
import threading
import time
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
from engines import Case, ROOT                          # noqa: E402

LEVELS = ROOT / "data" / "levels"
QUIRKS = ROOT / "data" / "quirks"
FLAGSHIP = LEVELS / "LaserTank.lvl"


def lvls(d):
    """Level files in one directory.

    Case-insensitively: four of the ten quirk packs ship `.LVL` and the other
    six ship `.lvl`, so a `glob("*.lvl")` finds all ten on Windows and six on
    Linux.  replay_all.py already does it this way.
    """
    return sorted(p for p in d.iterdir() if p.suffix.lower() == ".lvl")


def default_files(all_collections):
    """The documented sweep surface: flagship + every quirk pack."""
    files = lvls(LEVELS) if all_collections else [FLAGSHIP]
    return files + [p for d in sorted(QUIRKS.iterdir()) if d.is_dir()
                    for p in lvls(d)]


_local = threading.local()


def scratch():
    if not hasattr(_local, "s"):
        _local.s = engines.Scratch("lt-sweep")
    return _local.s


def main():
    ap = argparse.ArgumentParser(
        description="Sweep every level of a .lvl through both engines with one keystream.",
        epilog="exit 0 identical, 1 diverged, 3 cosmetic-only")
    ap.add_argument("files", nargs="*", type=pathlib.Path,
                    help="level files (default: LaserTank.lvl + all quirk packs)")
    ap.add_argument("--all", action="store_true",
                    help="all 13 collections in data/levels/, not just the flagship")
    ap.add_argument("--keys", default="",
                    help='keystream for every level (default: "" -- load and idle)')
    ap.add_argument("--no-field", dest="field", action="store_false",
                    help="drop PF/PF2 from the trace (H hashes still cover them)")
    ap.add_argument("--no-bmf", dest="bmf", action="store_false",
                    help="drop the cosmetic BMF/BMF2 tripwire")
    ap.add_argument("--max-ticks", type=int, default=None)
    ap.add_argument("--stride", type=int, default=1, metavar="N",
                    help="only every Nth level -- a spot sweep")
    ap.add_argument("--limit", type=int, default=None, metavar="N",
                    help="at most N levels per file")
    ap.add_argument("--jobs", type=int, default=8)
    ap.add_argument("-v", "--verbose", action="store_true",
                    help="print a line per level, not just the failures")
    args = ap.parse_args()

    engines.require_engines()
    files = args.files or default_files(args.all)
    missing = [p for p in files if not p.exists()]
    if missing:
        raise SystemExit("no such level file: %s" % ", ".join(str(p) for p in missing))

    cases = []
    for path in files:
        n = engines.count_levels(path)
        nums = list(range(1, n + 1))[::args.stride]
        if args.limit:
            nums = nums[:args.limit]
        cases += [Case(path, i, args.keys) for i in nums]

    print("sweep: %d level%s from %d file%s, keys=%r%s"
          % (len(cases), "" if len(cases) == 1 else "s",
             len(files), "" if len(files) == 1 else "s", args.keys,
             "".join(f for f, on in ((" --field", args.field), (" --bmf", args.bmf)) if on)))

    def work(case):
        s = scratch()
        a, b = engines.run_pair(case, s.a, s.b, args.field, args.bmf, args.max_ticks)
        return case, engines.compare(a, b)

    tally = Counter()
    bad = []
    t0 = time.time()
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for i, (case, div) in enumerate(pool.map(work, cases), 1):
            if div is None:
                tally["identical"] += 1
                if args.verbose:
                    print("  %s %d  identical" % (case.levels.name, case.level))
            else:
                tally["cosmetic" if div.cosmetic else "diverged"] += 1
                bad.append((case, div))
                print("  %s %d  DIVERGE %s%s  (%s)"
                      % (case.levels.name, case.level, div.sig,
                         " [cosmetic]" if div.cosmetic else "",
                         "tick %s" % div.tick if div.tick else div.kind))
            if not args.verbose and (i % 200 == 0 or i == len(cases)):
                print("  %-44s" % ("%d/%d  %.0f levels/s"
                                   % (i, len(cases), i / max(time.time() - t0, 1e-9))),
                      end="\r", flush=True)
    print(" " * 48, end="\r")

    print("\n%d/%d identical   %d diverged   %d cosmetic-only   (%.0fs)"
          % (tally["identical"], len(cases), tally["diverged"], tally["cosmetic"],
             time.time() - t0))
    for case, div in bad[:20]:
        print("\n--- %s level %d ---" % (case.levels, case.level))
        print(div.detail)
    if len(bad) > 20:
        print("\n... and %d more" % (len(bad) - 20))
    if bad:
        c = bad[0][0]
        print("\nreproduce the first one:")
        for exe, tr in ((engines.ORACLE, "a.trace"), (engines.CORE, "b.trace")):
            print("  %s" % " ".join(engines.command(exe, c, tr, args.field,
                                                    args.bmf, args.max_ticks)))
        print("  python tools/difftrace.py a.trace b.trace")

    if tally["diverged"]:
        return 1
    return 3 if tally["cosmetic"] else 0


if __name__ == "__main__":
    sys.exit(main())
