#!/usr/bin/env python3
"""Item 23 -- the FESS feature space, projected before any search is written.

Item 23 proposes a FESS-shaped rung for the FERRY + SOKOBAN half of the corpus
(Shoham & Schaeffer, IEEE CoG 2020): project states into a small **feature
space** and advance by *cycling through the occupied feature cells*, expanding
the best state in each.  The item wrote its own refusal against that shape:

  *FESS is cycling through occupied feature cells, so it buys nothing if the
  levels the chain fails all land in one cell, or if solved and unsolved land
  in the same cells at the same rate.  A feature space that does not separate
  the population it is aimed at is not a feature space, and this is the one
  thing that can be known before the build.*

Both halves are free, because the columns already exist.  `--analyze-tsv`
carries `blocks` and `region` -- the movable blocks and the cells the tank can
currently stand in -- which are the two shipped columns closest to FESS's first
two features (boxes packed, and the connectivity of the player's region).  This
joins them against the fourth pass's report on `(collection, level)` and runs
the two tests.

    python tools/fess_project.py             # the tests, the control and the table
    python tools/fess_project.py --pairs     # ...plus every other near-FESS pair
    python tools/fess_project.py --x water --y region    # another feature space
    python tools/fess_project.py --reps 0    # skip the permutation p-values

Tests 1 and 2 are the item's own two refusals.  Test 3 asks whether each column
separates at a fixed value of the other, because a *pair* that is one column
twice is one feature.  Test 4 is the confound these files keep catching: both
columns are counts that grow with the level, so the cell must still separate
inside strata of `--control` (`poses`, the reachable tank poses) or all it has
measured is that bigger levels are harder.

Regenerate the projection first if `build/reports/analyze-corpus.tsv` predates
the binary -- the loop is in docs/solver/instruments.md and costs ~3 minutes.

**What this can and cannot say.**  `--analyze-tsv` reads the *authored* board,
so every number here is a root-state, per-level projection: it can show that the
space is not degenerate over the population, which is what the item asked, and
it cannot show that a state *moves* between cells as a search pushes a block.
That second question needs the features computed per node, which is the build.

Bins are log2 (`0`, `1`, `2-3`, `4-7`, ...) because both columns are counts with
a long tail; the tests below are run on the binned cells, so a coarser or finer
binning is a different test and `--bin` says which one ran.  Stdlib only.
"""

import argparse
import collections
import json
import math
import os
import random
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ANALYZE = os.path.join(ROOT, "build", "reports", "analyze-corpus.tsv")
CHAIN = os.path.join(ROOT, "build", "reports", "chain5.jsonl")
VERDICTS = ("FERRY", "SOKOBAN")

# FESS's four features and the shipped column closest to each, for --pairs.
NEAR_FESS = [
    ("blocks", "the movable blocks -- what is there to pack"),
    ("region", "cells the tank can stand in -- the player's connectivity"),
    ("water", "water on the route -- the holes still to fill"),
    ("alive", "blocks that can be moved at all -- blocks not yet stuck"),
    ("mob_max", "the largest area one block can be moved in"),
    ("mob_sum", "the total block mobility"),
]


def log2bin(n):
    return 0 if n <= 0 else int(math.log2(n)) + 1


def binlabel(b):
    if b == 0:
        return "0"
    lo, hi = 1 << (b - 1), (1 << b) - 1
    return str(lo) if lo == hi else "%d-%d" % (lo, hi)


def load_analyze(path):
    rows, hdr = {}, None
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            if line.startswith("#"):
                hdr = line[2:].rstrip("\n").split("\t")
                continue
            if not line.strip():
                continue
            d = dict(zip(hdr, line.rstrip("\n").split("\t")))
            rows[(d["collection"], int(d["level"]))] = d
    return rows


def load_chain(path):
    rows = []
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def chi2(cells):
    """Pearson chi-square of cell x solved, over {cell: (n, solved)}."""
    n = sum(v[0] for v in cells.values())
    k = sum(v[1] for v in cells.values())
    if n == 0 or k == 0 or k == n:
        return 0.0, 0
    p = k / n
    x, df = 0.0, 0
    for cn, ck in cells.values():
        if cn == 0:
            continue
        df += 1
        for obs, exp in ((ck, cn * p), (cn - ck, cn * (1 - p))):
            if exp > 0:
                x += (obs - exp) ** 2 / exp
    return x, max(df - 1, 0)


def cramers_v(x, n, df):
    return math.sqrt(x / n) if df and n else 0.0   # 2 columns -> min(r-1,c-1)=1


def chi2_within(strata, keys, solved):
    """Chi-square of cell x solved computed *inside* each stratum and summed.

    Each stratum contributes its own base rate, so this is the statistic for
    'does `keys` separate at a fixed value of `strata`' rather than for the
    joint table, which cannot tell the two columns apart.
    """
    part = collections.defaultdict(lambda: ([], []))
    for s, k, v in zip(strata, keys, solved):
        part[s][0].append(k)
        part[s][1].append(v)
    x, df = 0.0, 0
    for ks, vs in part.values():
        sx, sdf = chi2(tally(ks, vs))
        x += sx
        df += sdf
    return x, df


def permute_p(stat, solved, reps, strata=None, seed=20260917):
    """Empirical p for 'the cells all solve at the same rate'.

    Shuffles the solved labels -- within `strata` when given, which is how the
    test asks whether one feature separates *beyond* the other -- and counts how
    often a shuffle reaches the observed statistic.
    """
    if reps <= 0:
        return None
    obs = stat(solved)
    rng = random.Random(seed)
    groups = collections.defaultdict(list)
    for i in range(len(solved)):
        groups[strata[i] if strata else 0].append(i)
    labels = list(solved)
    hits = 0
    for _ in range(reps):
        for idx in groups.values():
            pool = [labels[i] for i in idx]
            rng.shuffle(pool)
            for i, v in zip(idx, pool):
                labels[i] = v
        if stat(labels) >= obs - 1e-9:
            hits += 1
    return (hits + 1) / (reps + 1)


def fmt_p(p, reps):
    return "< %.4f" % p if p <= 2.0 / (reps + 1) else "%.4f" % p


def tally(keys, solved):
    cells = collections.defaultdict(lambda: [0, 0])
    for k, s in zip(keys, solved):
        cells[k][0] += 1
        cells[k][1] += 1 if s else 0
    return {k: tuple(v) for k, v in cells.items()}


def table(bs, rs, cells, out):
    """The cross-tab: unsolved/total and the solve rate in each cell."""
    out("        " + "".join("%14s" % ("r " + binlabel(r)) for r in rs) + "%14s" % "tot")
    for b in bs:
        line = "%7s |" % ("b " + binlabel(b))
        for r in rs:
            n, k = cells.get((b, r), (0, 0))
            line += "%9s%5.0f" % ("%d/%d" % (n - k, n), 100 * k / n) if n else "%14s" % "-"
        n = sum(cells.get((b, r), (0, 0))[0] for r in rs)
        k = sum(cells.get((b, r), (0, 0))[1] for r in rs)
        out(line + "%9s%5.0f" % ("%d/%d" % (n - k, n), 100 * k / n))
    line = "%7s |" % "tot"
    for r in rs:
        n = sum(cells.get((b, r), (0, 0))[0] for b in bs)
        k = sum(cells.get((b, r), (0, 0))[1] for b in bs)
        line += "%9s%5.0f" % ("%d/%d" % (n - k, n), 100 * k / n)
    out(line)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--analyze", default=ANALYZE)
    ap.add_argument("--chain", default=CHAIN)
    ap.add_argument("--x", default="blocks", help="first feature column")
    ap.add_argument("--y", default="region", help="second feature column")
    ap.add_argument("--reps", type=int, default=2000, help="permutation reps; 0 to skip")
    ap.add_argument("--min-cell", type=int, default=20,
                    help="cells at least this big get a rate quoted (default 20)")
    ap.add_argument("--control", default="poses",
                    help="column the separation must survive as a size proxy "
                         "(default poses; '' to skip)")
    ap.add_argument("--pairs", action="store_true",
                    help="also score every other pair of near-FESS columns")
    args = ap.parse_args(argv)

    for p in (args.analyze, args.chain):
        if not os.path.exists(p):
            sys.exit("missing %s -- see docs/solver/instruments.md" % p)

    an = load_analyze(args.analyze)
    chain = load_chain(args.chain)
    missing = [r for r in chain if (r["collection"], r["level"]) not in an]
    if missing:
        sys.exit("%d of %d report rows have no --analyze-tsv row; regenerate the "
                 "projection (docs/solver/instruments.md)" % (len(missing), len(chain)))

    pop = [(r, an[(r["collection"], r["level"])]) for r in chain]
    sel = [(r, a) for r, a in pop if a["verdict"] in VERDICTS]
    solved = [bool(r["solved"]) for r, a in sel]
    nsol = sum(solved)
    n = len(sel)
    if not n:
        sys.exit("no %s rows in %s" % ("/".join(VERDICTS), args.chain))

    print("FESS feature projection -- item 23's free falsifier")
    print("  read      %s" % os.path.relpath(args.analyze, ROOT).replace("\\", "/"))
    print("  against   %s" % os.path.relpath(args.chain, ROOT).replace("\\", "/"))
    print("  bins      log2")
    print("  population  %s rows: %d of %d (%.1f%%), %d solved (%.1f%%), %d failed"
          % ("+".join(VERDICTS), n, len(pop), 100 * n / len(pop),
             nsol, 100 * nsol / n, n - nsol))
    print()

    X = [log2bin(int(a[args.x])) for r, a in sel]
    Y = [log2bin(int(a[args.y])) for r, a in sel]
    keys = list(zip(X, Y))
    cells = tally(keys, solved)
    fails = collections.Counter(k for k, s in zip(keys, solved) if not s)

    # --- test 1: do the failures all land in one cell? --------------------
    nf = n - nsol
    ordered = fails.most_common()
    half = next(i + 1 for i in range(len(ordered))
                if sum(c for _, c in ordered[:i + 1]) >= nf / 2)
    ninety = next(i + 1 for i in range(len(ordered))
                  if sum(c for _, c in ordered[:i + 1]) >= 0.9 * nf)
    print("1. do the levels the chain fails all land in one cell?")
    print("   occupied cells              %d" % len(cells))
    print("   cells holding a failure     %d" % len(ordered))
    print("   largest                     %d of %d failures (%.1f%%) at (%s, %s)"
          % (ordered[0][1], nf, 100 * ordered[0][1] / nf,
             binlabel(ordered[0][0][0]), binlabel(ordered[0][0][1])))
    print("   cells holding half of them  %d" % half)
    print("   cells holding 90%% of them   %d" % ninety)
    print("   -> NO" if len(ordered) > 1 and ordered[0][1] < nf / 2
          else "   -> YES, refused")
    print()

    # --- test 2: same cells at the same rate? -----------------------------
    x2, df = chi2(cells)
    v = cramers_v(x2, n, df)
    big = {k: c for k, c in cells.items() if c[0] >= args.min_cell}
    rates = sorted((100 * c[1] / c[0], k) for k, c in big.items())
    p = permute_p(lambda lab: chi2(tally(keys, lab))[0], solved, args.reps)
    print("2. do solved and unsolved land in the same cells at the same rate?")
    print("   chi-square                  %.1f on %d df, Cramer's V %.3f" % (x2, df, v))
    if p is not None:
        print("   permutation p               %s (%d reps, labels shuffled over the "
              "whole population)" % (fmt_p(p, args.reps), args.reps))
    print("   rate over the %d cells with n >= %d:  %.0f%% at (%s, %s)  to  %.0f%% at (%s, %s)"
          % (len(big), args.min_cell, rates[0][0], binlabel(rates[0][1][0]),
             binlabel(rates[0][1][1]), rates[-1][0], binlabel(rates[-1][1][0]),
             binlabel(rates[-1][1][1])))
    print("   population rate             %.0f%%" % (100 * nsol / n))
    print("   -> NO" if p is not None and p < 0.05
          else "   -> YES, refused" if p is not None
          else "   -> no p asked for (--reps 0)")
    print()

    # --- does each feature carry what the other does not? -----------------
    print("3. does each column separate at a fixed value of the other?")
    for name, own, stratum, other in ((args.y, Y, args.x, X), (args.x, X, args.y, Y)):
        sx, sdf = chi2_within(other, own, solved)
        sp = permute_p(lambda lab: chi2_within(other, own, lab)[0],
                       solved, args.reps, strata=other)
        bx, bdf = chi2(tally(other, solved))
        print("   %-8s within %-8s strata: chi-square %.1f on %d df%s"
              % (name, stratum, sx, sdf,
                 "" if sp is None else ", permutation p %s" % fmt_p(sp, args.reps)))
        print("            (%s alone: chi-square %.1f on %d df)" % (stratum, bx, bdf))
    print()

    # --- is any of it more than 'bigger levels are harder'? ---------------
    if args.control:
        C = [log2bin(int(a[args.control])) for r, a in sel]
        cx, cdf = chi2(tally(C, solved))
        wx, wdf = chi2_within(C, keys, solved)
        wp = permute_p(lambda lab: chi2_within(C, keys, lab)[0],
                       solved, args.reps, strata=C)
        rx, rdf = chi2_within(keys, C, solved)
        rp = permute_p(lambda lab: chi2_within(keys, C, lab)[0],
                       solved, args.reps, strata=keys)
        print("4. does the cell separate beyond %s, which is the size proxy?" % args.control)
        print("   %-8s alone                chi-square %.1f on %d df, V %.3f"
              % (args.control, cx, cdf, cramers_v(cx, n, cdf)))
        print("   cell within %-8s strata   chi-square %.1f on %d df%s"
              % (args.control, wx, wdf,
                 "" if wp is None else ", permutation p %s" % fmt_p(wp, args.reps)))
        print("   %-8s within cell strata     chi-square %.1f on %d df%s"
              % (args.control, rx, rdf,
                 "" if rp is None else ", permutation p %s" % fmt_p(rp, args.reps)))
        print()

    bs, rs = sorted(set(X)), sorted(set(Y))
    print("cross-tab: rows = %s, cols = %s, cell = unsolved/total and the solve rate"
          % (args.x, args.y))
    table(bs, rs, cells, print)
    print()

    if args.pairs:
        print("supplementary -- every pair of the near-FESS columns, by Cramer's V")
        print("  %-20s %8s %6s %6s" % ("pair", "chi2", "df", "V"))
        for i, (ca, _) in enumerate(NEAR_FESS):
            for cb, _ in NEAR_FESS[i + 1:]:
                ks = [(log2bin(int(a[ca])), log2bin(int(a[cb]))) for r, a in sel]
                cx, cdf = chi2(tally(ks, solved))
                print("  %-20s %8.1f %6d %6.3f"
                      % ("%s x %s" % (ca, cb), cx, cdf, cramers_v(cx, n, cdf)))
        print()
        print("  %-20s %8s %6s %6s" % ("single column", "chi2", "df", "V"))
        for ca, what in NEAR_FESS:
            ks = [log2bin(int(a[ca])) for r, a in sel]
            cx, cdf = chi2(tally(ks, solved))
            print("  %-20s %8.1f %6d %6.3f   %s" % (ca, cx, cdf,
                                                    cramers_v(cx, n, cdf), what))
    return 0


if __name__ == "__main__":
    sys.exit(main())
