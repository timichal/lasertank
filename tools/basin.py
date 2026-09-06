#!/usr/bin/env python
"""How far uphill a winning line goes -- the number that predicts a beam's failure.

    build/lasertank-solve.exe --levels data/levels/X.lvl \
        --lpb-list list-of-recordings.txt --profile build/prof.tsv
    python tools/basin.py build/prof.tsv [--events] [--per-level]

`--profile` (Profile.cs) replays each winning recording through the real engine
and prints FlagDistance and WorkDistance at every keypress.  This reads that and
reports, per level, the *excursions*: the maximal stretches that stay at or above
the best heuristic value seen so far.

Why that and not the heuristic's value: a greedy beam of width W keeps the best W
successors at each depth, so it follows a descending line for free and an
ascending one only while the whole cross-section of the ascent fits in W.  The
cross-section grows with the length of the ascent, so the *longest excursion* is
the quantity that says whether any affordable width would have worked.  A level
whose longest excursion is 4 keypresses is a beam level; one whose longest is 69
is not, at any width, and no amount of budget changes that -- which is the
difference between "needs more nodes" and "needs a different move set".

`--events` measures the same excursions in board-changing keypresses instead of
all keypresses, i.e. at the granularity a push/macro-action layer would search.
The gap between the two columns is the case for such a layer.
"""
import argparse
import collections
import statistics


def excursions(values):
    """(longest run at-or-above the running minimum, deepest such rise)."""
    best = None
    start = None
    rise = 0
    longest = 0
    deepest = 0
    for i, v in enumerate(values):
        if best is None or v < best:
            best = v
            if start is not None:
                longest = max(longest, i - start)
                deepest = max(deepest, rise)
            start = None
            rise = 0
        else:
            if start is None:
                start = i
            rise = max(rise, v - best)
    if start is not None:
        longest = max(longest, len(values) - 1 - start)
        deepest = max(deepest, rise)
    return longest, deepest


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("tsv")
    ap.add_argument("--events", action="store_true",
                    help="measure in board-changing keypresses, not keypresses")
    ap.add_argument("--per-level", action="store_true")
    ap.add_argument("--ferry-weight", type=float, default=1.0,
                    help="weight on the ferry term in the third column "
                         "(= --push-ferry); sweep it here rather than by "
                         "re-running the solver")
    args = ap.parse_args()

    rows = collections.defaultdict(list)
    for line in open(args.tsv, encoding="utf-8"):
        if line.startswith("#"):
            continue
        f = line.rstrip("\n").split("\t")
        if len(f) < 8:
            continue
        ferry = max(0, int(f[7]))
        rows[(f[0], int(f[1]))].append(
            (int(f[2]), int(f[5]), int(f[6]),
             int(round(int(f[6]) + args.ferry_weight * ferry)), f[8] == "1"))

    out = []
    for (coll, level), rs in sorted(rows.items()):
        rs.sort()
        keep = [r for r in rs if r[4]] if args.events else rs
        if len(keep) < 2:
            continue
        fl, fd = excursions([r[1] for r in keep])
        wl, wd = excursions([r[2] for r in keep])
        el, ed = excursions([r[3] for r in keep])
        out.append((coll, level, len(rs), len(keep), fl, fd, wl, wd, el, ed))

    if not out:
        raise SystemExit("no winning trajectories in " + args.tsv)

    unit = "events" if args.events else "keys"
    if args.per_level:
        print(f"{'collection':14s} {'lvl':>5s} {'keys':>5s} {unit:>7s} "
              f"{'flagRun':>8s} {'flagUp':>7s} {'workRun':>8s} {'workUp':>7s} "
              f"{'+ferryRun':>10s} {'+ferryUp':>9s}")
        for r in out:
            print(f"{r[0]:14s} {r[1]:5d} {r[2]:5d} {r[3]:7d} "
                  f"{r[4]:8d} {r[5]:7d} {r[6]:8d} {r[7]:7d} {r[8]:10d} {r[9]:9d}")
        print()

    def col(i):
        return sorted(r[i] for r in out)

    def q(a, p):
        return a[min(len(a) - 1, int(len(a) * p))]

    print(f"{len(out)} winning trajectories, {sum(r[2] for r in out)} keypresses"
          + (f", {sum(r[3] for r in out)} board-changing" if args.events else ""))
    for name, i in (("FlagDistance", 4), ("WorkDistance", 6), ("Work+ferry", 8)):
        a = col(i)
        print(f"  longest excursion above best-so-far, {name:12s} ({unit}): "
              f"p50 {q(a,.5):4d}   p90 {q(a,.9):4d}   max {a[-1]:4d}")
    a = col(6)
    print(f"  trajectories whose WorkDistance excursion exceeds 8 {unit}: "
          f"{sum(1 for v in a if v > 8)} of {len(a)}")


if __name__ == "__main__":
    main()
