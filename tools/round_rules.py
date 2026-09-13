#!/usr/bin/env python3
"""Item 4: what keeping a won round open bought, and what it cost.

    python tools/round_rules.py a build/reports/item4-a-ctrl.jsonl \
                                  build/reports/item4-a-bor.jsonl \
                                  build/reports/item4-a-shots.jsonl

The first report is the control -- the driver as it ships, where the first win
ends the round -- and every one after it is an arm that kept the round open
under some rule.  `tools/bor_campaign.sh` runs them; this reads what they wrote
and is free to re-run.

**Keys, not solved count.**  Neither flag can change whether a level falls:
both are consulted only once a rung has already won and neither adds budget.
So the solved sets should be identical and the routes shorter, and the question
the campaign asks is *how much shorter, and at what price*.  A level an arm
solves that the control did not is therefore not a win -- it is the instrument
disagreeing with itself -- and it is reported as a warning rather than counted.

**What the price is measured in.**  Nodes, because the campaign runs beside
other work and a second is not reproducible; wall clock is printed beside it
because it is what a person waits through, and the two together say whether a
default is affordable.

Stdlib only, like everything else in tools/.
"""
import json
import sys
from collections import Counter


def load(path):
    """Last line wins: the report is append-only, so a resumed run appends and
    the later attempt is the current answer."""
    rows = {}
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if line:
                r = json.loads(line)
                rows[(r["collection"], r["level"])] = r
    return rows


def median(xs):
    xs = sorted(xs)
    return xs[len(xs) // 2] if xs else 0.0


def span(seconds):
    m, s = divmod(int(seconds), 60)
    h, m = divmod(m, 60)
    return f"{h}h{m:02d}m" if h else f"{m}m{s:02d}s"


def num(n):
    for cut, suffix in ((1e9, "G"), (1e6, "M"), (1e3, "k")):
        if n >= cut:
            return f"{n / cut:.1f}{suffix}"
    return str(int(n))


def bites(rule, r):
    """Whether `rule` would have kept this round open, judged on the control's
    banked row.

    The two rules are Auto.KeepOpen's, and this is the only other place they
    are written down: a level with no record keeps the round open under both;
    `bor` keeps it open above 2.0x the record; `shots` keeps it open whenever
    the win spends more shots than the record, and otherwise at 3.0x.

    It is an *approximation of* what the flag saw, not a replay of it: the flag
    judges the shortest win that had landed by some 120 ms poll, and the control
    banks the shortest that had landed by the time its searchers actually
    stopped, which can be a rung or two later.  The two agree on every level
    where one rung wins alone -- which is most of them -- and where they do not,
    the control's row is the more pessimistic of the pair.
    """
    if not r["solved"]:
        return False
    if r["ratio"] <= 0:
        return True
    if rule == "bor":
        return r["ratio"] > 2.0
    return r["shots"] > r["ghs_shots"] or r["ratio"] > 3.0


def arm(label, rule, ctl, a):
    both = [k for k in ctl if k in a and ctl[k]["solved"] and a[k]["solved"]]
    gained = [k for k in a if a[k]["solved"] and not (k in ctl and ctl[k]["solved"])]
    lost = [k for k in ctl if ctl[k]["solved"] and k in a and not a[k]["solved"]]
    missing = [k for k in ctl if ctl[k]["solved"] and k not in a]

    shorter = [k for k in both if a[k]["keys"] < ctl[k]["keys"]]
    longer = [k for k in both if a[k]["keys"] > ctl[k]["keys"]]
    saved = sum(ctl[k]["keys"] - a[k]["keys"] for k in both)
    ck = sum(ctl[k]["keys"] for k in both)

    print(f"  {label}")
    print(f"    solved by both {len(both)}"
          + (f"   NOT RUN YET {len(missing)}" if missing else "")
          + (f"   arm-only {len(gained)} (INSTRUMENT WARNING)" if gained else "")
          + (f"   control-only {len(lost)} (INSTRUMENT WARNING)" if lost else ""))
    print(f"    keys      {len(shorter)} shorter, {len(longer)} longer,"
          f" {len(both) - len(shorter) - len(longer)} unchanged"
          f"   total {ck} -> {ck - saved}  ({saved} keys saved,"
          f" {100.0 * saved / ck if ck else 0:.1f}%)")
    print(f"    ratio     p50 {median([ctl[k]['ratio'] for k in both if ctl[k]['ratio'] > 0]):.2f}x"
          f" -> {median([a[k]['ratio'] for k in both if a[k]['ratio'] > 0]):.2f}x"
          f"   over 2.0x {sum(1 for k in both if ctl[k]['ratio'] > 2.0)}"
          f" -> {sum(1 for k in both if a[k]['ratio'] > 2.0)}")
    # The strategy half, and the one the ratio cannot see: a round kept open
    # that comes back with fewer shots found a different plan, not a tidier
    # keystream.  Closed item 13 is the reason this column is here at all.
    over = lambda rows, k: rows[k]["shots"] > rows[k]["ghs_shots"] and rows[k]["ghs_shots"] > 0
    print(f"    shots     above the record {sum(1 for k in both if over(ctl, k))}"
          f" -> {sum(1 for k in both if over(a, k))}"
          f"   fewer shots than the control on {sum(1 for k in both if a[k]['shots'] < ctl[k]['shots'])}")

    cn = sum(ctl[k].get("total_nodes", 0) for k in both)
    an = sum(a[k].get("total_nodes", 0) for k in both)
    cm = sum(ctl[k].get("total_ms", 0) for k in both) / 1000.0
    am = sum(a[k].get("total_ms", 0) for k in both) / 1000.0
    print(f"    cost      nodes {num(cn)} -> {num(an)}"
          f" ({an / cn if cn else 0:.2f}x)   wall {span(cm)} -> {span(am)}"
          f" ({am / cm if cm else 0:.2f}x)")
    if saved > 0 and an > cn:
        print(f"              {num((an - cn) / saved)} extra nodes and"
              f" {(am - cm) / saved:.1f} s per key saved")

    # Where the rule fired, read off the control's row.  A rule that keeps a
    # round open on a level and buys nothing there is the case the campaign is
    # for: the cost is real and the benefit is a maybe.
    bit = [k for k in both if bites(rule, ctl[k])]
    bsaved = sum(ctl[k]["keys"] - a[k]["keys"] for k in bit)
    if bit:
        bn = sum(a[k].get("total_nodes", 0) - ctl[k].get("total_nodes", 0) for k in bit)
        print(f"    the rule fired on {len(bit)} of {len(both)}"
              f" ({100.0 * len(bit) / len(both):.1f}%):"
              f" {sum(1 for k in bit if a[k]['keys'] < ctl[k]['keys'])} came back shorter,"
              f" {bsaved} keys saved, {num(bn)} extra nodes")
    # Keys that moved on a level the rule did not fire on are not the flag: two
    # rungs that land inside one 120 ms poll are compared whatever the flags
    # say, and which of them lands first is the machine's business.  Naming the
    # difference is what keeps the arm's headline honest.
    if saved != bsaved:
        print(f"    {saved - bsaved} of the keys came from levels the rule did not"
              " fire on -- two rungs landing inside one poll, not the flag")
    worst = sorted(bit, key=lambda k: a[k]["keys"] - ctl[k]["keys"])[:8]
    for k in worst:
        d = a[k]["keys"] - ctl[k]["keys"]
        if d >= 0:
            break
        print(f"      {k[0]:14} {k[1]:>5}  {ctl[k]['name'][:24]:24}"
              f" {ctl[k]['keys']:>5} -> {a[k]['keys']:<5} ({d:+d})"
              f"  {ctl[k]['method']} -> {a[k]['method']}"
              f"   {ctl[k]['ratio']:.1f}x -> {a[k]['ratio']:.1f}x")
    print()


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    stage, ctl_path = sys.argv[1], sys.argv[2]
    ctl = load(ctl_path)
    solved = [r for r in ctl.values() if r["solved"]]
    rounds = Counter(r.get("rounds", 0) for r in solved)
    print(f"item 4, stage {stage}  --  control {ctl_path}")
    print(f"  {len(solved)} of {len(ctl)} solved"
          f"   rounds spent by a win: "
          + ", ".join(f"{k}:{v}" for k, v in sorted(rounds.items()))
          + f"   whole population {num(sum(r.get('total_nodes', 0) for r in ctl.values()))} nodes,"
          f" {span(sum(r.get('total_ms', 0) for r in ctl.values()) / 1000.0)}")
    unsolved_nodes = sum(r.get("total_nodes", 0) for r in ctl.values() if not r["solved"])
    if unsolved_nodes:
        print(f"  of which {num(unsolved_nodes)} went on the levels nobody solved --"
              " the same bill in every arm, which is why the arms skip them")
    print()
    for path in sys.argv[3:]:
        rule = "shots" if "shots" in path else "bor"
        arm(path, rule, ctl, load(path))
    return 0


if __name__ == "__main__":
    sys.exit(main())
