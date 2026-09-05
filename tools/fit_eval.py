#!/usr/bin/env python
"""Read layer 4's rank dump: report the distribution, then fit the evaluation.

Two commands over the same file, and the order is not negotiable -- the report
is what says whether fitting anything is the right move at all:

    python tools/fit_eval.py build/reports/rank.tsv
    python tools/fit_eval.py build/reports/rank.tsv --fit --out build/reports/w.txt

**The report.**  Each group is one subgoal expansion from a state on a winning
trajectory: every candidate the shipped expansion offered, and which of them the
winner went through.  Two numbers come out of that and they point at different
layers:

    coverage -- is a candidate the winner used in the group at all?  If not,
        no ordering can help and the constraint is the closure or the
        acceptance test, i.e. a different layer.
    rank -- where does WorkDistance put the best such candidate?  The beam
        keeps --sg-width of them (4, doubling to 64 on a restart), so the rate
        at which the winning line is inside the first 4 is the rate at which
        the shipped search could still be on it one step later.

**The fit.**  A linear evaluation over Feat, trained pairwise *within* a group:
the winner's successor should score below the siblings it was offered beside.
Positives and negatives therefore come from the same expansion, which is the
comparison the beam actually makes -- a model fit to cost-to-go over trajectory
states alone would never have seen a state the winner declined.

Two things about the sampling, both of which change the answer:

    Groups are weighted by 1 / (groups from the same recording), so every
    recording counts once.  Without it two quirk packs supply 16,599 of 20,148
    groups and the fit is a fit to Tutor-with-Playbacks.
    The split is by *recording*, never by group: groups from one recording
    share a board and most of a position, so a group-wise split would report a
    held-out score that is really a training score.
"""
import argparse
import collections
import pathlib
import random
import sys

import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent.parent

NAMES = ["bias", "work", "work_far", "flagdist", "flag_unreach", "manhattan",
         "route_obs", "component", "bricks", "blocks", "mirrors", "antitanks",
         "water", "thin_ice", "block_by_water", "threats", "far_man"]
NF = len(NAMES)
SCALE = 1024

# The seed vector, which is WorkDistance exactly -- see Weights.cs.  Kept here
# so the report can score the baseline through the same code path as a fit
# model, rather than through a second implementation of the same sum.
SEED = np.zeros(NF)
SEED[NAMES.index("work")] = 1.0
SEED[NAMES.index("work_far")] = 1000.0
SEED[NAMES.index("far_man")] = 1.0

# Quirk packs are human recordings; the 13 level collections are solver output.
HUMAN = {"Game-Objects-in-LT", "Pono's_trick", "Rotary Mirrors-Challenge",
         "Tutor-with-Playbacks", "4-Triangles", "Inchworm", "L40", "Telekinesis-1",
         "Tricks", "Tutor"}


def groups(path, keep_neg, rng):
    """Stream the dump, yielding one group at a time.

    Rows of a group are contiguous: the exe writes a whole recording's buffer
    under one lock and emits its groups in order.  Negatives are subsampled
    here rather than after loading, because the full file is 6M rows and only
    the report needs all of them.

    Yields (collection, level, group, F, onpath, is_best, tier, work) where F
    is the (n, NF) feature block and the rest are parallel arrays.
    """
    cur_key = None
    rows = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            p = line.rstrip("\n").split("\t")
            key = (p[0], p[1], p[2])
            if key != cur_key:
                if rows:
                    yield pack(cur_key, rows, keep_neg, rng)
                cur_key, rows = key, []
            rows.append(p)
    if rows:
        yield pack(cur_key, rows, keep_neg, rng)


def pack(key, rows, keep_neg, rng):
    onpath = np.fromiter((int(r[4]) for r in rows), dtype=np.int32, count=len(rows))
    best = np.fromiter((int(r[5]) for r in rows), dtype=np.int8, count=len(rows))
    tier = np.fromiter((int(r[6]) for r in rows), dtype=np.int8, count=len(rows))
    work = np.fromiter((int(r[7]) for r in rows), dtype=np.int32, count=len(rows))

    idx = np.arange(len(rows))
    if keep_neg and (onpath < 0).sum() > keep_neg:
        neg = idx[onpath < 0]
        pos = idx[onpath >= 0]
        idx = np.sort(np.concatenate([pos, rng.choice(neg, keep_neg, replace=False)]))

    F = np.empty((len(idx), NF), dtype=np.float64)
    for i, j in enumerate(idx):
        F[i] = [float(v) for v in rows[j][8:8 + NF]]
    return (key[0], int(key[1]), int(key[2]), F,
            onpath[idx], best[idx], tier[idx], work[idx])


def rank_of(score, mask):
    """Position of the best masked candidate in an ascending sort of `score`.

    Ties count as half, the same convention throughout: two candidates the
    evaluation cannot separate are, for a beam that sorts and cuts, a coin
    flip.  Returns None when nothing is masked.
    """
    if not mask.any():
        return None
    s = score[mask].min()
    better = int((score < s).sum())
    ties = int((score == s).sum())
    return better + (ties + 1) / 2.0


def pct(n, d):
    return f"{n}/{d} = {100.0 * n / d:.1f}%" if d else "-"


def report(path, keep_neg, seed):
    rng = np.random.default_rng(seed)
    per_rec = collections.Counter()
    stats = []
    for coll, level, g, F, onpath, best, tier, work in groups(path, keep_neg, rng):
        per_rec[(coll, level)] += 1
        pos = onpath >= 0
        stats.append(dict(
            coll=coll, level=level, n=len(onpath),
            has_pos=bool(pos.any()),
            r_any=rank_of(work, pos),
            r_best=rank_of(work, best.astype(bool)),
            pos_tier=int(tier[pos].min()) if pos.any() else -1,
            adv=int((tier == 0).sum()), slack=int((tier == 1).sum()),
        ))

    n = len(stats)
    print(f"{n} groups from {len(per_rec)} recordings, "
          f"{sum(s['n'] for s in stats)} candidates")

    for label, keep in (("all", lambda s: True),
                        ("human recordings", lambda s: s["coll"] in HUMAN),
                        ("solver solutions", lambda s: s["coll"] not in HUMAN)):
        sel = [s for s in stats if keep(s)]
        if not sel:
            continue
        sizes = sorted(s["n"] for s in sel)
        cov = sum(1 for s in sel if s["has_pos"])
        print(f"\n-- {label}: {len(sel)} groups from "
              f"{len({(s['coll'], s['level']) for s in sel})} recordings")
        print(f"   candidates per group   p10 {sizes[len(sizes)//10]}  "
              f"p50 {sizes[len(sizes)//2]}  p90 {sizes[9*len(sizes)//10]}  "
              f"max {sizes[-1]}")
        print(f"   the winner's successor is in the group   {pct(cov, len(sel))}")

        withpos = [s for s in sel if s["has_pos"]]
        tiers = collections.Counter(s["pos_tier"] for s in withpos)
        print("   and the search calls it   "
              + "  ".join(f"{('advanced','slack','fallback')[t]} "
                          f"{pct(tiers[t], len(withpos))}" for t in sorted(tiers)))

        for what in ("r_any", "r_best"):
            r = sorted(s[what] for s in withpos if s[what] is not None)
            if not r:
                continue
            which = ("any state the winner stood on" if what == "r_any"
                     else "the furthest one it stood on")
            print(f"   WorkDistance rank of {which}:")
            print(f"      p50 {r[len(r)//2]:.0f}   p90 {r[9*len(r)//10]:.0f}   "
                  f"mean {sum(r)/len(r):.0f}")
            print("      " + "  ".join(
                f"top-{k} {100.0 * sum(1 for x in r if x <= k + 0.5) / len(r):4.1f}%"
                for k in (1, 4, 8, 16, 64)))
    print("\n(--sg-width is 4 and doubles to 64 over layer 3's restarts, so "
          "top-4 is the\n rate at which the shipped search can still be on the "
          "winner's line one step later.)")
    return stats


# ---- the fit -----------------------------------------------------------------

def load(path, keep_neg, seed):
    """Everything the fit needs, as flat arrays plus group boundaries."""
    rng = np.random.default_rng(seed)
    F, gid, pos, bestm, wt, key = [], [], [], [], [], []
    per_rec = collections.Counter()
    for coll, level, g, f, onpath, best, tier, work in groups(path, keep_neg, rng):
        if not (best > 0).any():
            continue                        # no label to learn from
        per_rec[(coll, level)] += 1
        gid.append(np.full(len(f), len(key), dtype=np.int32))
        F.append(f)
        pos.append(onpath >= 0)
        bestm.append(best.astype(bool))
        key.append((coll, level))
    if not F:
        sys.exit("no labelled groups")
    w = np.array([1.0 / per_rec[k] for k in key])
    return (np.concatenate(F), np.concatenate(gid), np.concatenate(pos),
            np.concatenate(bestm), w, key)


def fit(F, gid, posm, gw, iters, lr, l2, seed):
    """Rank the winner's successor first, within its own group.  Adam, full batch.

    The loss is a softmax over the group -- the probability the evaluation puts
    *some* state the winner stood on at the front of the sort:

        L_g = -log( sum_{j in winner} e^-s_j  /  sum_j e^-s_j )

    which is the metric the report prints, not a proxy for it.  A pairwise or a
    cost-to-go loss would both spend capacity on the ordering *among* the 390
    candidates the beam is going to discard anyway; the beam keeps four, so what
    is worth fitting is which four.

    Scores are `w . f` and lower is better, so the exponent is negated.
    Standardised inside, un-standardised on the way out: a per-feature offset
    cancels in every within-group comparison, so the constant that standardising
    introduces is not a term the search can see.
    """
    mu, sd = F.mean(0), F.std(0)
    sd[sd < 1e-9] = 1.0
    Z = (F - mu) / sd

    order = np.argsort(gid, kind="stable")
    Z, gid, posm = Z[order], gid[order], posm[order].astype(float)
    ng = int(gid.max()) + 1
    starts = np.searchsorted(gid, np.arange(ng))
    keep = np.bincount(gid, weights=posm, minlength=ng) > 0
    gwk = np.where(keep, gw, 0.0)
    total = gwk.sum()
    rw = gwk[gid]

    w = np.zeros(Z.shape[1])
    m = np.zeros_like(w)
    v = np.zeros_like(w)

    for it in range(1, iters + 1):
        s = Z @ w
        a = -s
        mx = np.maximum.reduceat(a, starts)
        e = np.exp(np.clip(a - mx[gid], -60, 0))
        zs = np.bincount(gid, weights=e, minlength=ng)
        ps = np.bincount(gid, weights=e * posm, minlength=ng)
        p = np.where(keep, ps / np.maximum(zs, 1e-300), 1.0)
        loss = float((gwk * -np.log(np.maximum(p, 1e-300))).sum() / total)

        q = e / np.maximum(zs, 1e-300)[gid]
        c = rw * (q * posm / np.maximum(p, 1e-300)[gid] - q)
        grad = (Z.T @ c) / total + l2 * w

        m = 0.9 * m + 0.1 * grad
        v = 0.999 * v + 0.001 * grad * grad
        w -= lr * (m / (1 - 0.9 ** it)) / (np.sqrt(v / (1 - 0.999 ** it)) + 1e-8)
        if it == 1 or it % max(1, iters // 8) == 0:
            print(f"   iter {it:5d}  loss {loss:.4f}")

    return w / sd, mu


def evaluate(path, wvec, keep_neg, seed, only=None):
    """top-k rates for a weight vector, on the same groups the report used."""
    rng = np.random.default_rng(seed)
    r_any, r_best = [], []
    for coll, level, g, F, onpath, best, tier, work in groups(path, keep_neg, rng):
        if only is not None and (coll, level) not in only:
            continue
        pos = onpath >= 0
        if not pos.any():
            continue
        s = F @ wvec
        r_any.append(rank_of(s, pos))
        rb = rank_of(s, best.astype(bool))
        if rb is not None:
            r_best.append(rb)
    return r_any, r_best


def topk(r, k):
    return 100.0 * sum(1 for x in r if x <= k + 0.5) / len(r) if r else 0.0


def show(label, r_any, r_best):
    for what, r in (("any", r_any), ("best", r_best)):
        if not r:
            continue
        print(f"   {label:<12} {what:<5} "
              + "  ".join(f"top-{k} {topk(r, k):5.1f}%" for k in (1, 4, 8, 16, 64))
              + f"   p50 {sorted(r)[len(r)//2]:.0f}")


def write_weights(path, w, source):
    ints = [int(round(x * SCALE)) for x in w]
    lines = ["# layer 4 evaluation weights, Eval.Scale = %d" % SCALE,
             "# " + source]
    lines += [f"{n} {v}" for n, v in zip(NAMES, ints)]
    pathlib.Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")
    return ints


def write_cs(path, ints, source):
    rows = []
    for i in range(0, NF, 6):
        rows.append("            " + ", ".join(f"{v}" for v in ints[i:i + 6]) + ",")
        rows.insert(len(rows) - 1,
                    "            // " + "  ".join(NAMES[i:i + 6]))
    body = "\n".join(rows)
    pathlib.Path(path).write_text(f"""// GENERATED by tools/fit_eval.py -- do not edit by hand.
//
// The shipped weights for layer 4's learned evaluation, in Eval.Scale fixed
// point.  Checked in rather than loaded from a file so that a campaign worker
// has no runtime dependency and two runs of the same binary rank identically;
// --eval-weights overrides them for a bench.
//
// {source}
namespace LaserTank.Solver
{{
    public static class Weights
    {{
        public const string Source = "{source}";

        public static readonly int[] Default =
        {{
{body}
        }};
    }}
}}
""", encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dump")
    ap.add_argument("--fit", action="store_true")
    ap.add_argument("--out", default="build/reports/eval-weights.txt")
    ap.add_argument("--cs", default=None, help="also write a Weights.cs")
    ap.add_argument("--keep-neg", type=int, default=64,
                    help="negatives sampled per group for the fit (0 = all)")
    ap.add_argument("--iters", type=int, default=4000)
    ap.add_argument("--lr", type=float, default=0.02)
    ap.add_argument("--l2", type=float, default=1e-4)
    ap.add_argument("--seed", type=int, default=12345)
    ap.add_argument("--holdout", type=float, default=0.2)
    args = ap.parse_args()

    path = pathlib.Path(args.dump)
    if not path.is_absolute():
        path = ROOT / path
    if not path.exists():
        sys.exit(f"no dump at {path} -- run tools/rankdump.py")

    if not args.fit:
        report(path, 0, args.seed)
        return

    print("loading...")
    F, gid, pos, bestm, gw, key = load(path, args.keep_neg, args.seed)
    print(f"{len(key)} labelled groups, {len(F)} rows, "
          f"{len(set(key))} recordings")

    rnd = random.Random(args.seed)
    recs = sorted(set(key))
    rnd.shuffle(recs)
    cut = int(len(recs) * (1 - args.holdout))
    train_recs, test_recs = set(recs[:cut]), set(recs[cut:])
    tr = np.array([k in train_recs for k in key])
    print(f"train {tr.sum()} groups / {len(train_recs)} recordings, "
          f"test {(~tr).sum()} / {len(test_recs)}")

    keep = tr[gid]
    remap = np.cumsum(tr) - 1
    w, _ = fit(F[keep], remap[gid[keep]], pos[keep], gw[tr],
               args.iters, args.lr, args.l2, args.seed)

    print("\nranking, WorkDistance against the fit:")
    # Split every report by source, because the two populations are not
    # comparable and averaging them hides the one that matters.  A solver
    # solution is a solution *this ranking already found*, so the baseline is
    # flattered on it by survivorship; the human recordings are levels the
    # solver cannot do, which is the population layer 4 exists for.
    for label, only in (("train", train_recs), ("HELD OUT", test_recs)):
        for src_label, sel in (("all", only),
                               ("human", {k for k in only if k[0] in HUMAN}),
                               ("solver", {k for k in only if k[0] not in HUMAN})):
            if not sel:
                continue
            print(f"  -- {label} / {src_label}  ({len(sel)} recordings)")
            show("WorkDistance", *evaluate(path, SEED, 0, args.seed, sel))
            show("learned", *evaluate(path, w, 0, args.seed, sel))

    # Refit on everything for the vector that ships.  The held-out numbers
    # above are the claim; this is the model, fit the same way on more of the
    # same data rather than tuned against the test set.
    print("")
    print("refit on all %d recordings for the shipped vector:" % len(recs))
    w, _ = fit(F, gid, pos, gw, args.iters, args.lr, args.l2, args.seed)

    src = (f"fit on {len(key)} groups / {len(recs)} recordings, "
           f"softmax-in-group, seed {args.seed}")
    ints = write_weights(ROOT / args.out if not pathlib.Path(args.out).is_absolute()
                         else args.out, w, src)
    print("\nweights:")
    for n, v in zip(NAMES, ints):
        print(f"   {n:<16} {v:>10}")
    if args.cs:
        write_cs(ROOT / args.cs if not pathlib.Path(args.cs).is_absolute() else args.cs,
                 ints, src)
        print(f"wrote {args.cs}")


if __name__ == "__main__":
    main()
