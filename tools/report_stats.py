#!/usr/bin/env python3
"""Summarise a campaign report (the .jsonl lasertank-solve --report writes).

lasertank-solve prints a per-collection summary as it goes, but a campaign is
thirteen invocations and the question the run was for is a whole-corpus one:
which tiers fell, how good the solutions are, and -- the one that decides what
to build next -- where the search stopped on the ones that did not.

    python tools/report_stats.py build/reports/layer0.jsonl
    python tools/report_stats.py a.jsonl b.jsonl --diff   # two layers, compared

Stdlib only, like everything else in tools/.  Reads utf-8-sig because a report
written by an older solver build starts with a BOM.
"""
import argparse
import json
import sys
from collections import Counter, defaultdict

TIERS = [(1, "Kids"), (2, "Easy"), (4, "Medium"), (8, "Hard"), (16, "Deadly"), (0, "unrated")]


def load(path):
    """Last line wins: the report is append-only, so a re-solved level appears
    more than once and the later attempt is the current answer."""
    rows = {}
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            r = json.loads(line)
            rows[(r["collection"], r["level"])] = r
    return rows


def pct(a, b):
    return f"{100.0 * a / b:.1f}%" if b else "-"


def median(xs):
    xs = sorted(xs)
    return xs[len(xs) // 2] if xs else 0.0


def tiers(rows):
    by = defaultdict(list)
    for r in rows.values():
        by[r["difficulty"]].append(r)
    out = []
    for tier, name in TIERS:
        v = by.get(tier)
        if not v:
            continue
        ok = [r for r in v if r["solved"]]
        ratios = [r["ratio"] for r in ok if r["ratio"] > 0]
        out.append((name, len(v), len(ok), pct(len(ok), len(v)), median(ratios),
                    sum(1 for r in ok if r["ratio"] and r["ratio"] <= 1.0)))
    return out


def report(path, rows):
    total = len(rows)
    ok = [r for r in rows.values() if r["solved"]]
    print(f"{path}  --  {total} levels attempted, {len(ok)} solved ({pct(len(ok), total)})")
    print()
    print("  tier       attempted    solved     rate    median ratio    at/under record")
    for name, n, s, rate, med, exact in tiers(rows):
        print(f"  {name:<9} {n:9} {s:9} {rate:>8} {med:14.1f}x {exact:16}")

    print()
    by_coll = defaultdict(list)
    for r in rows.values():
        by_coll[r["collection"]].append(r)
    print("  collection            attempted    solved     rate")
    for coll in sorted(by_coll):
        v = by_coll[coll]
        s = sum(1 for r in v if r["solved"])
        print(f"  {coll:<20} {len(v):9} {s:9} {pct(s, len(v)):>8}")

    print()
    stuck = [r for r in rows.values() if not r["solved"]]
    print(f"  {len(stuck)} unsolved, by where the search stopped")
    for k, n in Counter(r["stop"] for r in stuck).most_common():
        print(f"    {k:<16} {n:7}  {pct(n, len(stuck)):>7}")
    err = [r for r in rows.values() if r.get("error")]
    if err:
        print(f"  {len(err)} errored")
        for k, n in Counter(r["error"][:60] for r in err).most_common(5):
            print(f"    {k}  x{n}")

    print()
    print("  solved, by method")
    for k, n in Counter(r["method"] for r in ok).most_common():
        print(f"    {k:<16} {n:7}  {pct(n, len(ok)):>7}")
    ratios = sorted(r["ratio"] for r in ok if r["ratio"] > 0)
    if ratios:
        print()
        print(f"  keypresses / .ghs moves+shots:  p50 {median(ratios):.1f}x   "
              f"p90 {ratios[int(0.9 * len(ratios))]:.1f}x   worst {ratios[-1]:.1f}x   "
              f"over 10x: {sum(1 for v in ratios if v > 10)}   "
              f"at or under the record: {sum(1 for v in ratios if v <= 1.0)}")


def diff(a_path, a, b_path, b):
    """What the second report changed about the first, level by level."""
    keys = set(a) | set(b)
    gained = [k for k in keys if k in b and b[k]["solved"] and not (k in a and a[k]["solved"])]
    lost = [k for k in keys if k in a and a[k]["solved"] and not (k in b and b[k]["solved"])]
    both = [k for k in keys if k in a and k in b and a[k]["solved"] and b[k]["solved"]]
    print(f"  {a_path} -> {b_path}")
    print(f"    solved {sum(1 for r in a.values() if r['solved'])} -> "
          f"{sum(1 for r in b.values() if r['solved'])}"
          f"   gained {len(gained)}   lost {len(lost)}")
    if both:
        shorter = sum(1 for k in both if b[k]["keys"] < a[k]["keys"])
        longer = sum(1 for k in both if b[k]["keys"] > a[k]["keys"])
        print(f"    of {len(both)} solved by both: {shorter} shorter, {longer} longer")
    by = defaultdict(lambda: [0, 0])
    for k in keys:
        tier = (b.get(k) or a[k])["difficulty"]
        if k in a and a[k]["solved"]:
            by[tier][0] += 1
        if k in b and b[k]["solved"]:
            by[tier][1] += 1
    print("    tier        before     after")
    for tier, name in TIERS:
        if tier in by:
            print(f"    {name:<9} {by[tier][0]:9} {by[tier][1]:9}")
    if lost:
        print("    lost: " + ", ".join(f"{c}:{lv}" for c, lv in sorted(lost)[:20]))


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("reports", nargs="+")
    ap.add_argument("--diff", action="store_true",
                    help="with two reports: what the second changed about the first")
    a = ap.parse_args()

    loaded = [(p, load(p)) for p in a.reports]
    if a.diff:
        if len(loaded) != 2:
            ap.error("--diff needs exactly two reports")
        diff(loaded[0][0], loaded[0][1], loaded[1][0], loaded[1][1])
        return 0
    for i, (p, rows) in enumerate(loaded):
        if i:
            print()
        report(p, rows)
    return 0


if __name__ == "__main__":
    sys.exit(main())
