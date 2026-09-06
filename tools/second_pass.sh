#!/usr/bin/env bash
# The second pass: re-attack the levels a previous campaign did not solve, with
# a different searcher, at a fresh budget.
#
#   tools/second_pass.sh <first-report.jsonl> <out-subdir> <report.jsonl> [args...]
#
#   tools/campaign.sh     solutions/l0 build/reports/l0.jsonl --no-macro
#   tools/second_pass.sh  build/reports/l0.jsonl solutions/l34 \
#                         build/reports/l3n.jsonl --no-ida --no-beam --subgoal
#   tools/second_pass.sh  build/reports/l3n.jsonl solutions/l34 \
#                         build/reports/l34.jsonl --no-ida --no-beam --subgoal --sg-eval learned
#   tools/second_pass.sh  build/reports/l34.jsonl solutions/l34 \
#                         build/reports/l34pass4.jsonl --no-ida --no-beam --macro --macro-first
#
# Layer 5 (--push) is *not* in that chain, and the reason is a number rather
# than an omission: one push expansion is a whole PF-preserving closure, some
# 4,500 ApplyKey calls against layer 0's five per keypress, so at the budgets
# these passes run at it is behind both -- 3/50 at 400k and 7/50 at 4M on the
# 50 deep bench levels, where layer 0 is 13/50 at both.  Run it as a pass of
# its own, at a budget in the tens of millions, to see what it does:
#
#   NODES=40000000 tools/second_pass.sh  build/reports/l34.jsonl solutions/l5 \
#                         build/reports/l5.jsonl --no-ida --no-beam --push
#
# **Three passes now, and the middle one is why the first is still there.**
# Layer 4 is layer 3 with a learned ranking key, so replacing layer 3's pass
# with it scores the same 469 -- but it *loses* three levels layer 3 found,
# because a different ranking is not a strictly better one.  Appending instead
# costs one more pass over what is left and loses nothing: 444 -> 472.  A
# restart was additive by construction (Restart.cs); a re-ranking is only
# additive if you keep the run it re-ranks.
#
# --subgoal now carries layer 3 with it: the subgoal beam restarts when it dies
# of an empty frontier with budget still in hand (--sg-restarts 0 turns that off
# and gives layer 2 exactly).  Layer 3 *is* layer 2 plus restarts, so it replaced
# that pass rather than following it.  Layer 4 is a different case -- see above.
#
# **Why this exists rather than a bigger portfolio.**  Both specialists win
# decisively on levels the raw beam cannot solve and lose over the corpus as a
# whole -- layer 1's macro beam 395 -> 381 run first and 354 run last, layer 2's
# subgoal beam 387 and 365 -- because most solvable levels are ones the raw beam
# gets easily and every node a specialist spends there is a node taken from it.
# A portfolio has to make that trade on every level in advance.  A pass does
# not: the previous pass identifies the population, this one attacks only it,
# and neither pays for the other.
#
# Chain them in that order.  Over layer 0's 3,790 failures the subgoal beam with
# restarts adds 44, the same beam ranked by the learned evaluation adds 30 more,
# and the macro beam then adds 3, so the composite is 472 of 4,185.  Without
# layer 4 those are 44 and 5, composite 444; without restarts, 40 and 6, 441.
# See PROGRESS.md, Phase 4 layers 3 and 4 -- including why the four levels
# restarts buy are a smaller result than the 717 dead-ends they eliminate would
# suggest, and why 41 of layer 4's 69 are levels its own training set had.
#
# Writes into the *same* solutions directory by design -- a level solved by
# either pass is one solution -- so verify_solutions.py sees the union and the
# second report only ever contains levels the first one failed.
set -u
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
first=$1
sub=$2
report=$3
shift 3

exe="$root/build/lasertank-solve.exe"
[ -x "$exe" ] || { echo "no $exe -- run bash src/build.sh" >&2; exit 1; }
[ -f "$first" ] || { echo "no first-pass report at $first" >&2; exit 1; }
mkdir -p "$(dirname "$report")"

lists=$(mktemp -d)
trap 'rm -rf "$lists"' EXIT
python - "$first" "$lists" <<'PY'
import json, os, sys
first, out = sys.argv[1], sys.argv[2]
rows = {}
for line in open(first, encoding="utf-8-sig"):
    line = line.strip()
    if line:
        r = json.loads(line)
        rows[(r["collection"], r["level"])] = r          # last line wins
todo = {}
for (coll, lv), r in rows.items():
    if not r["solved"]:
        todo.setdefault(coll, []).append(lv)
# SAMPLE=N takes every Nth unsolved level instead of all of them.  A pass at a
# budget bigger than the campaign's is hours, and the question such a run asks is
# a *rate* question, so a stride over each collection's failures answers it
# unbiased -- the same argument as the campaign's STRIDE.  It is how the 400k
# control in PROGRESS (Phase 4 layer 3) was measured.
step = max(1, int(os.environ.get("SAMPLE", "1")))
for coll, lv in todo.items():
    lv = sorted(lv)[::step]
    with open(os.path.join(out, coll + ".txt"), "w") as f:
        f.write("\n".join(str(x) for x in lv))
    print(f"{coll}: {len(lv)} unsolved")
PY

for lvl in "$root"/data/levels/*.lvl; do
  name=$(basename "$lvl" .lvl)
  [ -f "$lists/$name.txt" ] || continue
  echo "=== $name ==="
  "$exe" --levels "$lvl" --out "$root/build/$sub" --report "$report" \
         --levels-list "$lists/$name.txt" \
         --jobs "${JOBS:-14}" --nodes "${NODES:-1000000}" \
         --budget-ms "${BUDGET_MS:-60000}" --quiet "$@"
done
echo "=== SECOND PASS DONE ==="
