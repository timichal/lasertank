#!/usr/bin/env python
"""Union the chain's per-pass reports into one, so `chain.jsonl` is derivable.

    python tools/chain_union.py build/reports/chain.jsonl \
        build/reports/l0.jsonl build/reports/l3n.jsonl \
        build/reports/l34.jsonl build/reports/l34pass4.jsonl

A pass only ever reports on levels the previous pass failed, so the shipped
chain's per-level state is the *union* of its four reports on
(collection, level) -> solved: the last report that solved a level wins, and a
level nothing solved keeps its earliest row (the deepest budget it was given).
That union is what `second_pass.sh` has to be pointed at to attack "everything
the chain still fails", which is the fourth pass in SOLVER.md's Next actions.

It existed only as a sentence in SOLVER.md until session 25, and the file it
describes lived only in gitignored `build/` -- the same way the three bench
level lists did, and they did not survive a machine move.  A recipe that is
prose is a recipe that gets retyped differently every time; this is the recipe.
"""
import collections
import json
import sys


def main(argv):
    if len(argv) < 3:
        sys.exit(__doc__)
    out, ins = argv[1], argv[2:]
    rows = {}
    solved_by = collections.Counter()
    for path in ins:
        for line in open(path, encoding="utf-8-sig"):
            line = line.strip()
            if not line:
                continue
            r = json.loads(line)
            key = (r["collection"], r["level"])
            was = rows.get(key)
            # A later pass wins only by solving; an unsolved re-attempt must not
            # overwrite an earlier pass's win, and must not replace the row that
            # recorded where the level actually stopped.
            if was is None or (r["solved"] and not was["solved"]):
                rows[key] = r
                if r["solved"]:
                    solved_by[path] += 1
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        for key in sorted(rows):
            f.write(json.dumps(rows[key]) + "\n")
    solved = sum(1 for r in rows.values() if r["solved"])
    print(f"{out}: {len(rows)} levels, {solved} solved ({solved / len(rows):.1%})")
    for path in ins:
        print(f"  {solved_by[path]:>4} from {path}")


if __name__ == "__main__":
    main(sys.argv)
