#!/usr/bin/env bash
# One solver campaign over all 13 collections in data/levels/, cheapest-by-.ghs
# first, appending to one report.
#
#   tools/campaign.sh <out-subdir-under-build> <report.jsonl> [solver args...]
#
#   tools/campaign.sh solutions/l0 build/reports/l0.jsonl --no-macro
#   tools/campaign.sh solutions/l1 build/reports/l1.jsonl
#
# **Governed by --nodes, not by wall clock.**  The point of a campaign is to
# compare one layer of the solver with the next, and a wall-clock budget makes
# that comparison depend on what else the machine was doing -- the first run of
# this was contended by the test gates and had to be thrown away.  A node is an
# Engine.ApplyKey call, so an equal node budget is equal work, and the answer is
# reproducible.  --budget-ms stays only as a backstop against a level whose
# ticks are pathologically slow (see the Grand Prix note in PROGRESS).
#
# Knobs, as environment variables so the pass-through args stay the solver's:
#   JOBS=14  NODES=150000  BUDGET_MS=60000  STRIDE=1
#
# STRIDE is the one that needs justifying.  The whole corpus at a real budget is
# hours -- 20,914 levels, and the hard collections spend the full budget on
# nearly every one -- so a campaign meant to *measure per-tier rates* takes
# every Nth level of every collection instead.  Level numbers are not sorted by
# difficulty, so a stride is an unbiased sample of each tier, and it is the same
# trick sweep.py already uses.
#
# Resumable: lasertank-solve skips a level whose .lpb already exists, so an
# interrupted campaign continues where it stopped -- but note that a resumed run
# does not re-emit report lines for the levels it skips, so re-run into a fresh
# out-subdir when the report is the thing you want.
set -u
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
sub=$1
report=$2
shift 2

exe="$root/build/lasertank-solve.exe"
[ -x "$exe" ] || { echo "no $exe -- run bash src/build.sh" >&2; exit 1; }
mkdir -p "$(dirname "$report")"

for lvl in "$root"/data/levels/*.lvl; do
  echo "=== $(basename "$lvl" .lvl) ==="
  "$exe" --levels "$lvl" --out "$root/build/$sub" --report "$report" \
         --jobs "${JOBS:-14}" --nodes "${NODES:-1000000}" \
         --budget-ms "${BUDGET_MS:-60000}" --stride "${STRIDE:-1}" \
         --quiet "$@"
done
echo "=== CAMPAIGN DONE ==="
