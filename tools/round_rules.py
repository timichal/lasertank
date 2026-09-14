#!/usr/bin/env python3
"""Item 4: what keeping a won round open bought, and what it cost.

    python tools/round_rules.py a build/reports/item4-a-ctrl.jsonl \
                                  build/reports/item4-a-bor.jsonl \
                                  build/reports/item4-a-shots.jsonl

The first report is the control -- the driver as it ships, where the first win
ends the round -- and every one after it is an arm that kept the round open
under some rule.  `tools/bor_campaign.sh` runs them; this reads what they wrote
and is free to re-run.

**Read the "THE MEASUREMENT" line, not the total.**  A driver round polls its
stop bit every 120 ms, so two identical runs do not bank identical routes: which
rungs have crossed the line when the round cancels is a race.  Stage A measured
that floor at 5-9 levels and ~20 keys per run.  The levels the rule actually
fired on are the only ones where the arm and the control are different
configurations; everything else is the same configuration run twice, and it is
printed as the noise floor so the headline can be judged against it.

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


def bites(rule, r, keys=None):
    """Whether `rule` would keep a round open on a win of `keys` keys.

    The two rules are Auto.KeepOpen's, and this is the only other place they
    are written down: a level with no record keeps the round open under both;
    `bor` keeps it open above 2.0x the record; `shots` keeps it open whenever
    the win spends more shots than the record, and otherwise at 3.0x.

    **It is judged on a route, not on a level, because that is what the flag
    does** -- `KeepOpen` is handed the shortest win that has landed by some
    120 ms poll, which early in a round is not the route the level ends up
    banking.  `LaserTank.lvl` 9 is the whole item in one level: the round that
    banked 115 keys / 1.9x was held open because the *beam's* 294 / 5.0x landed
    first, and 1.9x is inside the 2.0 that would have closed it.  Judge that
    level on its banked row and the rule reads as never firing; judge it on the
    294 and it reads as firing.  So the caller brackets it -- `keys` (the best
    route, the pessimistic read) against `longest` (the worst route that landed
    in the same round, the optimistic one) -- and the truth is between them.

    The shot half needs no bracket: shots are a property of the plan, and every
    rung that wins a round is compared on the same record.
    """
    if not r["solved"]:
        return False
    if r["ratio"] <= 0:
        return True
    k = r["keys"] if keys is None else keys
    target = (r.get("ghs_moves") or 0) + (r.get("ghs_shots") or 0)
    ratio = k / target if target else 0.0
    if rule == "bor":
        return ratio > 2.0
    return r["shots"] > r["ghs_shots"] or ratio > 3.0


def arm(label, rule, ctl, a):
    both = [k for k in ctl if k in a and ctl[k]["solved"] and a[k]["solved"]]
    gained = [k for k in a if a[k]["solved"] and not (k in ctl and ctl[k]["solved"])]
    lost = [k for k in ctl if ctl[k]["solved"] and k in a and not a[k]["solved"]]
    missing = [k for k in ctl if ctl[k]["solved"] and k not in a]

    # **The split that makes this table readable, and it was not obvious until
    # the first run produced it.**  Nine of stage A's levels came back LONGER
    # under `--best-of-round`, which the mechanism says is impossible: a round
    # kept open sees every route the cancelled round saw and picks the shortest.
    # Split by whether the rule could fire at all and the contradiction goes
    # away -- every regression is on a level where the flag was never consulted,
    # and on those the arm and the control are *the same configuration run
    # twice*.  The driver polls its stop bit every 120 ms, so which rungs have
    # crossed the line when a round cancels is a race with the thread scheduler,
    # and two identical runs do not bank identical routes.
    #
    # So: the levels the rule fired on are the measurement, and the rest are the
    # instrument's own noise floor, printed rather than hidden because it is the
    # number that says whether the measurement means anything.
    # Three buckets, because the bracket in `bites` is real: the flag judged
    # whatever route had landed at some poll, and the row records the best and
    # the worst of the round's winners.  A level whose *banked* route trips the
    # rule certainly held the round open; one where only the round's longest
    # route trips it may have; one where neither does cannot have.
    certain = [k for k in both if bites(rule, ctl[k])]
    maybe = [k for k in both
             if k not in set(certain)
             and bites(rule, ctl[k], ctl[k].get("longest") or ctl[k]["keys"])]
    fired = certain + maybe
    quiet = [k for k in both if k not in set(fired)]

    def moved(ks):
        sh = [k for k in ks if a[k]["keys"] < ctl[k]["keys"]]
        lo = [k for k in ks if a[k]["keys"] > ctl[k]["keys"]]
        return len(sh), len(lo), sum(ctl[k]["keys"] - a[k]["keys"] for k in ks)

    print(f"  {label}")
    print(f"    solved by both {len(both)}"
          + (f"   NOT RUN YET {len(missing)}" if missing else "")
          + (f"   arm-only {len(gained)} (INSTRUMENT WARNING)" if gained else "")
          + (f"   control-only {len(lost)} (INSTRUMENT WARNING)" if lost else ""))

    fs, fl, fk = moved(fired)
    qs, ql, qk = moved(quiet)
    ms_, ml, mk = moved(maybe)
    cs, cl, cck = moved(certain)
    ck = sum(ctl[k]["keys"] for k in both)
    print(f"    the rule fired on {len(certain)} of {len(both)}"
          f" ({100.0 * len(certain) / len(both) if both else 0:.1f}%),"
          f" and may have on {len(maybe)} more")
    print(f"      THE MEASUREMENT   {cs} shorter, {cl} longer   {cck:+d} keys"
          + ("   <- a longer route here is a real regression" if cl else ""))
    if maybe:
        print(f"      may have fired    {ms_} shorter, {ml} longer   {mk:+d} keys")
    print(f"      the noise floor   {qs} shorter, {ql} longer"
          f"   {qk:+d} keys over {len(quiet)} levels the rule never saw"
          " -- the same run twice")
    print(f"    total keys {ck} -> {ck - fk - qk}"
          f"   ({fk + qk} net, of which {cck} is certainly the rule)")

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
    fn = sum(a[k].get("total_nodes", 0) - ctl[k].get("total_nodes", 0) for k in fired)
    qn = sum(a[k].get("total_nodes", 0) - ctl[k].get("total_nodes", 0) for k in quiet)
    print(f"    cost      nodes {num(cn)} -> {num(an)}"
          f" ({an / cn if cn else 0:.2f}x)   wall {span(cm)} -> {span(am)}"
          f" ({am / cm if cm else 0:.2f}x)")
    if fk > 0:
        print(f"              {num(fn / fk)} extra nodes per key saved"
              f"   ({num(fn)} extra where the rule fired,"
              f" {'+' if qn >= 0 else '-'}{num(abs(qn))} on the rest, which is noise)")

    worst = sorted(certain or fired, key=lambda k: a[k]["keys"] - ctl[k]["keys"])[:8]
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

    # **A level a keypress gave up on is not a level the budget could not
    # solve, and the table cannot tell them apart on its own.**  Stage acc lost
    # its whole level-8 control row to one stray byte on stdin -- `skipped
    # after 0s`, 30k nodes -- and nothing in any table said so: it read as an
    # unsolved level, the arms skipped it for having no keys to compare, and
    # the arm was simply absent from the measurement.  The campaign now runs
    # the driver with stdin closed, which is the fix; this is the detector, so
    # that a report produced by an older run (or by a hand-run) still says it.
    for why, what in (("skipped", "given up on by a KEYPRESS"),
                      ("stopped", "cut short by q / Ctrl-C")):
        hit = sorted(k[1] for k, r in ctl.items() if r.get("stop") == why)
        if hit:
            print(f"  INSTRUMENT WARNING: {len(hit)} level(s) {what}, not measured:"
                  f" {', '.join(str(x) for x in hit[:12])}"
                  + (" ..." if len(hit) > 12 else ""))

    # The control's own rule, read off the row rather than assumed.  A report
    # banked before the shot rule shipped has no marker and is a control; one
    # banked after, by a command that forgot --no-best-of-round, is not -- and
    # comparing an arm against it would report a rule that saves nothing.
    rules = {r.get("config", "").partition("[round-rule ")[2].partition("]")[0]
             for r in ctl.values()}
    rules.discard("")
    if rules - {"off"}:
        print(f"  INSTRUMENT WARNING: the control was not run with the round rule"
              f" off -- its rows say [round-rule {', '.join(sorted(rules))}]."
              " Re-run it with --no-best-of-round; every delta below is understated.")
    print()
    for path in sys.argv[3:]:
        # ctrl2 is the null arm -- the control's own configuration run twice --
        # so no rule fired on any level and every row of its table is the
        # instrument's noise floor.  "bor" is an arbitrary choice of rule there;
        # what makes it read correctly is `flags`, below.
        rule = "shots" if "shots" in path else "bor"
        if "ctrl2" in path:
            print("  (the null arm: the control's configuration run a second time."
                  " Everything below is the instrument's own noise --")
            print("   a 'shorter' or 'longer' here is two identical runs disagreeing,"
                  " not a flag doing anything.)")
        arm(path, rule, ctl, load(path))
    return 0


if __name__ == "__main__":
    sys.exit(main())
