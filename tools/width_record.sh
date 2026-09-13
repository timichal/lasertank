#!/usr/bin/env bash
# Item 14's decision run: per-level width from the record, against the global
# width, over the 138 unsolved GAUNTLETs with a short record.
#
#   bash tools/width_record.sh              # F = 4.6 14 60, the calibration's
#                                           # own p25 / p50 / p75
#   F_LIST="14" bash tools/width_record.sh  # one arm
#
# **The control is already banked and this script does not re-run it.**
# `build/reports/gt-fire.jsonl` is `l8fire` over this exact population at 40M
# -- 85 of 138, the number in SOLVER.md's status table -- and the flag is
# raise-only and inert when it is off, so the arm here differs from that one by
# `--push-width-record F` and by nothing else.  The equivalence that lets a
# report from an older binary stand as the control was measured before the
# first arm ran: over `LaserTank.lvl` 3, 4, 7, 11 and 20 the new binary with
# the flag off is **node-identical and keystream-identical** to the one that
# wrote `gt-fire.jsonl`, and it writes no `width` field, so the two reports are
# byte-comparable.
#
# Why a sweep of F rather than one value: the estimate is
# `budget / (poses x .ghs shots x F)`, and F is what the arithmetic is wrong
# by.  Over `l8fire`'s 66 solved levels at a known width of 128 the true factor
# reads p10 1.9 / p25 4.6 / p50 14.2 / p75 59.9 / p90 446 -- two and a half
# orders of magnitude -- so no single F is *the* calibration and the honest
# question is whether any of them beats the global.  Small F is wide, large F
# is nearly off; the flag never narrows a level below --push-beam.
#
# Read it as solved count *and* exclusive levels, per this project's fourth
# rule: an arm that ties the control and holds exclusive levels is a portfolio
# member, and one that ties with none is the same searcher wearing a flag.
#
# LT_SOLVE points at a side build, because item 2's pass holds
# build/lasertank-solve.exe open:
#
#   dotnet publish src/LaserTank.Solver/LaserTank.Solver.csproj -c Release -o build/wr
#   LT_SOLVE=$PWD/build/wr/lasertank-solve.exe JOBS=4 bash tools/width_record.sh
#
# JOBS defaults to 4 rather than 16 for the same reason: the arms are
# node-governed, so sharing the machine with the pass moves wall clock and no
# measured number.  One arm is ~3.5 h of job time, ~1 h wall at four jobs.
set -u
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
exe="${LT_SOLVE:-$root/build/lasertank-solve.exe}"
first=$root/build/reports/gauntlet-tail.jsonl
control=$root/build/reports/gt-fire.jsonl

[ -f "$control" ] || { echo "no control at $control -- run tools/gauntlet_tail.sh" >&2; exit 1; }

L8="--push-reach --push-ferry-match --push-ferry-maze --push-dead 20
    --push-fire 8 --push-shot-run 16 --push-beam 128 --max-keys 5000"

args=(fire="$control")

for f in ${F_LIST:-4.6 14 60}; do
  tag=f${f/./p}
  LT_SOLVE="$exe" NODES="${NODES:-40000000}" BUDGET_MS="${BUDGET_MS:-1800000}" \
  JOBS="${JOBS:-4}" RESUME="${RESUME:-1}" bash "$root/tools/second_pass.sh" \
      "$first" "gtw/$tag" "$root/build/reports/gtw-$tag.jsonl" \
      --no-ida --no-beam --push --push-read \
      --max-keys 5000 --max-keys-record $L8 --push-eval work \
      --push-fire-tier --push-width-record "$f"
  python "$root/tools/verify_solutions.py" "$root/build/gtw/$tag"
  args+=("$tag=$root/build/reports/gtw-$tag.jsonl")
done

python "$root/tools/arms_union.py" "${args[@]}"

# What the widths actually came out as -- the run is only interpretable next to
# the distribution of what it chose, and a level whose row carries no `width`
# was left at the global 128 and is a control row inside the arm.
python - "${args[@]:1}" <<'PY'
import json, sys
for spec in sys.argv[1:]:
    tag, path = spec.split("=", 1)
    w = [json.loads(l).get("width", 0) for l in open(path, encoding="utf-8-sig") if l.strip()]
    raised = sorted(x for x in w if x)
    if not raised:
        print("%-8s no level raised above --push-beam 128" % tag)
        continue
    q = lambda p: raised[min(len(raised) - 1, int(p * len(raised)))]
    print("%-8s raised %d/%d   width p10 %d  p50 %d  p90 %d  max %d"
          % (tag, len(raised), len(w), q(.1), q(.5), q(.9), raised[-1]))
PY
