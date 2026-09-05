#!/usr/bin/env bash
# One labelled solver configuration over one level list, summarised in a line.
#
#   tools/bench.sh <label> <levels-list.txt> <nodes> [solver args...]
#
# The two lists that matter are already banked, which is what makes the tuning
# tables in PROGRESS reproducible rather than anecdotal:
#
#   build/reports/bench-levels.txt   60 Beginner-I levels layer 0 failed
#   build/reports/deep-levels.txt    50 Beginner-I levels with a .ghs total 40-150
#
#   tools/bench.sh l2  build/reports/deep-levels.txt 400000 \
#                      --no-ida --no-beam --subgoal --sg-restarts 0
#   tools/bench.sh l3  build/reports/deep-levels.txt 400000 \
#                      --no-ida --no-beam --subgoal
#   tools/bench.sh l4  build/reports/deep-levels.txt 400000 \
#                      --no-ida --no-beam --subgoal --sg-eval learned
#
# **Read the result knowing what the population is.**  Both lists are levels
# layer 0 could not solve, which is the population that flattered layer 1 (28
# against 24 on bench 1, and a corpus loss), flattered layer 2, overstated layer
# 3's budget scaling, and *understated* layer 4 -- the learned evaluation is
# worth +1 here and +30 on the corpus, and the refit that lost a level on both
# benches gained three on the corpus.  A bench here picks *parameters*; only a
# campaign over the corpus decides whether a layer ships.  See PROGRESS.md,
# Phase 4.
#
# Node-governed like everything else, so two labels are comparable even if the
# machine was busy for one of them.  LEVELS overrides the collection.
set -u
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
label=$1; list=$2; nodes=$3; shift 3

exe="$root/build/lasertank-solve.exe"
[ -x "$exe" ] || { echo "no $exe -- run bash src/build.sh" >&2; exit 1; }
[ -f "$list" ] || { echo "no level list at $list" >&2; exit 1; }

out=$root/build/bench/$label
rep=$root/build/bench/$label.jsonl
rm -rf "$out"; rm -f "$rep"
mkdir -p "$out"

"$exe" --levels "${LEVELS:-$root/data/levels/Beginner-I.lvl}" --levels-list "$list" \
       --out "$out" --report "$rep" --jobs "${JOBS:-14}" --nodes "$nodes" \
       --budget-ms "${BUDGET_MS:-600000}" --quiet --force "$@" >/dev/null 2>&1

python - "$label" "$rep" <<'PY'
import collections, json, sys
label, path = sys.argv[1], sys.argv[2]
rows = {}
for line in open(path, encoding="utf-8-sig"):
    line = line.strip()
    if line:
        r = json.loads(line)
        rows[(r["collection"], r["level"])] = r        # last line wins
rs = list(rows.values())
ok = [r for r in rs if r["solved"]]
ratios = sorted(r["ratio"] for r in ok if r["ratio"] > 0)
p50 = ratios[len(ratios) // 2] if ratios else 0
stops = collections.Counter(r["stop"] for r in rs if not r["solved"])
# Nodes spent on the levels that failed: a searcher that gives up with the
# budget unspent is the signal layer 3 was built for, and it is invisible in
# the solved count alone.
spent = sorted(r["nodes"] for r in rs if not r["solved"])
med = spent[len(spent) // 2] if spent else 0
print(f"{label:<26} {len(ok):>3}/{len(rs):<3} p50 {p50:5.1f}x  "
      f"unsolved-nodes-p50 {med:>8}  {dict(stops)}")
PY
