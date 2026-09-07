#!/usr/bin/env python3
"""Compare several arms of one pass by what they *add*, not by what they score.

    python tools/arms_union.py plain=build/reports/l5-s15-plain.jsonl \
        enables=build/reports/l5-s15-enables.jsonl ...

SOLVER.md's *Next actions* item 2 runs the fourth pass in five arms and says to
compare unions rather than solo counts, and the reason is layer 1's finding one
layer down: an arm that scores the same as another is not the same arm if it
scores it on different levels.  Two arms at 15/248 that overlap in 5 are worth
25 to a chain that can afford both passes and 15 to one that must pick.

Prints, over the levels every arm was given:
  * solo -- what each arm solved on its own
  * only -- what it solved that no other arm did, which is what it is *for*
  * the cumulative union in greedy order, i.e. the pass to actually ship
  * the pairwise overlap matrix

Stdlib only, like everything else in tools/.
"""
import itertools
import json
import sys


def load(path):
    rows = {}
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if line:
                r = json.loads(line)
                rows[(r["collection"], r["level"])] = r
    return rows


def main(argv):
    specs = argv[1:]
    if not specs or any("=" not in s for s in specs):
        sys.exit(__doc__)
    arms, seen, why = {}, {}, {}
    for spec in specs:
        name, path = spec.split("=", 1)
        rows = load(path)
        seen[name] = set(rows)
        arms[name] = {k for k, r in rows.items() if r["solved"]}
        why[name] = rows

    common = set.intersection(*seen.values())
    pop = len(common)
    ragged = [n for n in arms if len(seen[n]) != pop]
    if ragged:
        print(f"note: arms disagree on the population; comparing the "
              f"{pop} levels all {len(arms)} were given "
              f"(uneven: {', '.join(sorted(ragged))})\n")

    solved = {n: arms[n] & common for n in arms}
    print(f"population: {pop} levels\n")
    print(f"{'arm':<12} {'solo':>6} {'rate':>7} {'only':>6}")
    for n in arms:
        others = set().union(*[solved[m] for m in arms if m != n]) if len(arms) > 1 else set()
        print(f"{n:<12} {len(solved[n]):>6} {100.0*len(solved[n])/pop:>6.1f}% "
              f"{len(solved[n] - others):>6}")

    # Greedy: the arm that adds most to what is already held, which is the order
    # a chain would be built in.
    print("\ncumulative union, greedy order:")
    held, order = set(), []
    while len(order) < len(arms):
        n = max((m for m in arms if m not in order),
                key=lambda m: (len(solved[m] - held), m))
        add = len(solved[n] - held)
        held |= solved[n]
        order.append(n)
        print(f"  + {n:<12} +{add:<4} -> {len(held)} ({100.0*len(held)/pop:.1f}%)")

    if len(arms) > 1:
        print("\npairwise overlap:")
        for a, b in itertools.combinations(arms, 2):
            i = len(solved[a] & solved[b])
            u = len(solved[a] | solved[b])
            print(f"  {a:<12} {b:<12} both {i:>4}  union {u:>4}  "
                  f"only-{a} {len(solved[a]-solved[b]):>4}  only-{b} {len(solved[b]-solved[a]):>4}")

    # Where the ones nothing solved stopped: budget vs dead end decides whether
    # the answer is a bigger run or a different search.
    lost = common - held
    if lost:
        stop = {}
        for k in lost:
            r = why[order[0]][k]
            stop[r.get("stop", r.get("reason", "?"))] = stop.get(
                r.get("stop", r.get("reason", "?")), 0) + 1
        print(f"\n{len(lost)} solved by nothing; stop reason (arm {order[0]}):")
        for s, c in sorted(stop.items(), key=lambda x: -x[1]):
            print(f"  {s:<16} {c:>5} ({100.0*c/len(lost):.1f}%)")


if __name__ == "__main__":
    main(sys.argv)
