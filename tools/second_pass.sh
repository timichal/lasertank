#!/usr/bin/env bash
# The second pass: re-attack the levels a previous campaign did not solve, with
# a different searcher, at a fresh budget.
#
#   tools/second_pass.sh <first-report.jsonl> <out-subdir> <report.jsonl> [args...]
#
#   tools/campaign.sh     solutions/l0 build/reports/l0.jsonl --no-macro
#   tools/second_pass.sh  build/reports/l0.jsonl solutions/l0 \
#                         build/reports/pass2.jsonl --no-ida --no-beam
#
# **Why this exists rather than a bigger portfolio.**  Layer 1's macro beam wins
# decisively on levels the raw beam cannot solve (18 -> 28, 23 -> 31, 33 -> 38
# of 60, at three budgets) and loses over the corpus as a whole (395 -> 381 run
# first, 354 run last), because most solvable levels are ones the raw beam gets
# easily and every node the macro beam spends there is a node taken from it.
# A portfolio has to make that trade on every level in advance.  A second pass
# does not: the first pass identifies the population, the second attacks only
# it, and neither one pays for the other.  See PROGRESS.md, Phase 4 layer 1.
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
for coll, lv in todo.items():
    with open(os.path.join(out, coll + ".txt"), "w") as f:
        f.write("\n".join(str(x) for x in sorted(lv)))
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
