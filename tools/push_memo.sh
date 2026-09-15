#!/usr/bin/env bash
# Item 10 of docs/solver/next-actions.md, the second half: --push-memo built,
# gated and priced.  One command, two answers -- that the memo changes nothing
# and that it costs less.
#
#   bash tools/push_memo.sh                # the gate and the table, all rungs
#   bash tools/push_memo.sh gate           # equality only, no seconds reported
#   bash tools/push_memo.sh bench          # seconds only, no equality checked
#   bash tools/push_memo.sh time           # the --push-time split, one level a rung
#   RUNGS=l8fire bash tools/push_memo.sh   # one rung instead of three
#
# **What it measures and why both halves are one run.**  Each rung is run twice
# over the same level list at the same node budget, once with --no-push-memo and
# once with the default (on since session 51), and the pair answers both
# questions at once: the reports have to agree field for field and the .lpb
# files byte for byte (the memo is a pure function of what PushH already
# reads, so anything else is a bug), and the job
# seconds are then the only thing that may differ.  A run that changed a number
# is not a slower or faster run, it is a wrong one, so the gate is reported
# first and the table says nothing if it fails.
#
# **Node-governed, and it has to be.**  BUDGET_MS defaults to an hour so that
# the *node* budget is what stops every level: at the 4-second default a faster
# expansion simply searches further, the two arms walk different boards and
# neither the equality gate nor the seconds mean anything.  (That is not a
# hypothetical -- at --budget-ms 4000 the memo arm makes 1.70x the ApplyKey
# calls of the control in the same wall clock, which is the speedup showing up
# as *more search* rather than as fewer seconds.)
#
# **Three rungs, because the memo's key is not the same in all three.**  `rung8`
# is the shipped push rung the item's table is measured on, `l8fire` is the arm
# the fourth pass actually runs, and `layer7` is the one arm that sets
# --push-stop: StopPrice is the only thing in the heuristic that reads Game.PF2,
# so it is the only configuration whose memo key includes it, and a gate that
# skipped it would leave that branch of the key untested.
#
# **Seconds want an idle machine.**  Job time is the sum of the per-level ms the
# report carries, so it is not wall clock and two arms are comparable under
# equal load -- but item 2's pass is not an equal load minute to minute.  Run
# this beside it for the gate, and on a quiet machine for the number that goes
# in the docs.  REPEAT=n runs each arm n times and keeps its best, which is the
# cheap way to buy back some of that.
#
# Env: LIST LEVELS NODES BUDGET_MS JOBS REPEAT RUNGS PREFIX.  LT_SOLVE points at
# a different binary, for the reason bench.sh gives -- a running pass holds
# build/lasertank-solve.exe open, so a change can only be benched from the
# project's own bin/ while one is going.
set -u

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root" || exit 1

LIST=${LIST:-bench/deep-levels.txt}
LEVELS=${LEVELS:-data/levels/Beginner-I.lvl}
NODES=${NODES:-400000}
BUDGET_MS=${BUDGET_MS:-3600000}
JOBS=${JOBS:-4}
REPEAT=${REPEAT:-1}
RUNGS=${RUNGS:-"rung8 l8fire layer7"}
PREFIX=${PREFIX:-memo}
TIME_LEVEL=${TIME_LEVEL:-10}
TIME_NODES=${TIME_NODES:-6000000}

exe="${LT_SOLVE:-$root/src/LaserTank.Solver/bin/Release/net8.0/lasertank-solve.exe}"
[ -x "$exe" ] || exe="$root/build/lasertank-solve.exe"

rung_flags () {
  case $1 in
    rung8)   echo "--no-ida --no-beam --push --push-read --push-beam 8" ;;
    l8fire)  echo "--no-ida --no-beam --push --push-read --push-reach --push-ferry-match --push-ferry-maze --push-dead 20 --push-fire 8 --push-shot-run 16 --push-beam 128 --push-eval work --push-fire-tier" ;;
    layer7)  echo "--no-ida --no-beam --push --push-read --push-stop 1 --push-shot-run 16 --push-beam 128" ;;
    *)       echo "unknown rung: $1 (have: rung8 l8fire layer7)" >&2; return 1 ;;
  esac
}

usage () { sed -n '2,/^set -u/p' "$0" | sed '$d'; }

what=${1:-all}
case $what in
  -h|--help) usage; exit 0 ;;
  all|gate|bench|time) ;;
  *) echo "unknown phase: $what (have: all gate bench time)" >&2; usage >&2; exit 2 ;;
esac

[ -x "$exe" ] || { echo "no solver binary -- build with" \
  "dotnet build src/LaserTank.Solver/LaserTank.Solver.csproj -c Release" >&2; exit 1; }
[ -f "$LIST" ] || { echo "no level list at $LIST" >&2; exit 1; }
[ -f "$LEVELS" ] || { echo "no collection at $LEVELS" >&2; exit 1; }
for r in $RUNGS; do rung_flags "$r" > /dev/null || exit 2; done
mkdir -p build/reports "build/$PREFIX"

levels=$(grep -c '^[0-9]' "$LIST")
echo "=================================================================="
echo "item 10, --push-memo   $(date '+%F %T')"
echo "  $exe"
echo "  $levels levels from $LIST over $(basename "$LEVELS" .lvl)"
echo "  NODES=$NODES BUDGET_MS=$BUDGET_MS JOBS=$JOBS REPEAT=$REPEAT  rungs:$RUNGS"
echo "=================================================================="

# ---- one arm -------------------------------------------------------------
# $1 rung, $2 off|on, $3 the repeat index.  Each (rung, arm) keeps its own
# report and its own solutions directory, so the comparison below has two whole
# runs to diff rather than two halves of one.
run_arm () {
  rung=$1; arm=$2; rep=$3
  out="build/$PREFIX/$rung-$arm"
  report="build/reports/$PREFIX-$rung-$arm.jsonl"
  extra="--no-push-memo"; [ "$arm" = on ] && extra=""
  rm -rf "$out"; rm -f "$report"
  mkdir -p "$out"
  "$exe" --levels "$LEVELS" --levels-list "$LIST" --out "$out" --report "$report" \
         --jobs "$JOBS" --nodes "$NODES" --budget-ms "$BUDGET_MS" --quiet --force \
         $(rung_flags "$rung") $extra > "build/$PREFIX/$rung-$arm.log" 2>&1
}

if [ "$what" = all ] || [ "$what" = gate ] || [ "$what" = bench ]; then
  for rung in $RUNGS; do
    for r in $(seq 1 "$REPEAT"); do
      for arm in off on; do
        printf '  %-8s %-3s run %d/%d ... ' "$rung" "$arm" "$r" "$REPEAT"
        t0=$(date +%s)
        run_arm "$rung" "$arm" "$r"
        # Keep the best of REPEAT by job time: a slower repeat is the machine,
        # not the code.  The report of the best one is what is left on disk.
        best="build/reports/$PREFIX-$rung-$arm-best.jsonl"
        python - "build/reports/$PREFIX-$rung-$arm.jsonl" "$best" "build/$PREFIX/$rung-$arm" <<'PY'
import json, os, shutil, sys
cur, best, out = sys.argv[1], sys.argv[2], sys.argv[3]
def jobms(p):
    rows = {}
    for line in open(p, encoding="utf-8-sig"):
        line = line.strip()
        if line:
            r = json.loads(line)
            rows[(r["collection"], r["level"])] = r
    return sum(r["ms"] for r in rows.values())
ms = jobms(cur)
if not os.path.exists(best) or ms < jobms(best):
    shutil.copyfile(cur, best)
    if os.path.isdir(out + "-best"):
        shutil.rmtree(out + "-best")
    shutil.copytree(out, out + "-best")
print("%8.1f s job time" % (ms / 1000.0))
PY
        echo "                      $(($(date +%s) - t0)) s wall"
      done
    done
  done
fi

# ---- the gate and the table ----------------------------------------------
if [ "$what" = all ] || [ "$what" = gate ] || [ "$what" = bench ]; then
  python - "$what" "$PREFIX" $RUNGS <<'PY'
import hashlib, json, os, sys

what, prefix, rungs = sys.argv[1], sys.argv[2], sys.argv[3:]

def load(path):
    rows = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8-sig") as f:
            for line in f:
                line = line.strip()
                if line:
                    r = json.loads(line)
                    rows[(r["collection"], r["level"])] = r      # last line wins
    return rows

def sigs(d):
    """Every .lpb under `d`, by relative path, as an md5 of its bytes."""
    out = {}
    for base, _, files in os.walk(d):
        for fn in files:
            if not fn.endswith(".lpb"):
                continue
            p = os.path.join(base, fn)
            out[os.path.relpath(p, d).replace("\\", "/")] = \
                hashlib.md5(open(p, "rb").read()).hexdigest()
    return out

# The fields a memo may not touch.  `ms` is the one it is allowed to change and
# the only one; `config` differs by the flag itself.
FIELDS = ["solved", "keys", "raw_keys", "moves", "shots", "ratio", "trimmed",
          "polished", "replanned", "method", "stop", "depth", "restarts", "nodes"]

rows, ok = [], True
for rung in rungs:
    a = load("build/reports/%s-%s-off-best.jsonl" % (prefix, rung))
    b = load("build/reports/%s-%s-on-best.jsonl" % (prefix, rung))
    if not a or not b:
        print("  %-8s no reports -- the arm did not run" % rung)
        ok = False
        continue
    bad = []
    for k in sorted(set(a) | set(b)):
        x, y = a.get(k), b.get(k)
        if x is None or y is None:
            bad.append("%s %s: only %s has it" % (k[0], k[1], "off" if y is None else "on"))
            continue
        for f in FIELDS:
            if x.get(f) != y.get(f):
                bad.append("%s %s: %s %r vs %r" % (k[0], k[1], f, x.get(f), y.get(f)))
    sa = sigs("build/%s/%s-off-best" % (prefix, rung))
    sb = sigs("build/%s/%s-on-best" % (prefix, rung))
    for p in sorted(set(sa) | set(sb)):
        if sa.get(p) != sb.get(p):
            bad.append("%s: solution bytes differ" % p)
    solved = sum(1 for r in a.values() if r["solved"])
    ja = sum(r["ms"] for r in a.values()) / 1000.0
    jb = sum(r["ms"] for r in b.values()) / 1000.0
    nodes = sum(r["nodes"] for r in a.values())
    rows.append((rung, len(a), solved, len(sa), nodes, ja, jb, bad))
    if bad:
        ok = False

print("")
print("the gate -- same nodes, same keys, same stops, same solution bytes")
print("rung      levels  solved  .lpb   nodes        verdict")
for rung, n, solved, lpb, nodes, ja, jb, bad in rows:
    print("%-9s %5d  %5d  %5d  %11d  %s"
          % (rung, n, solved, lpb, nodes,
             "IDENTICAL" if not bad else "%d DIFFERENCES" % len(bad)))
    for line in bad[:10]:
        print("    !!! " + line)
    if len(bad) > 10:
        print("    !!! ... and %d more" % (len(bad) - 10))

if what != "gate":
    print("")
    if not ok:
        print("the table is not printed: an arm changed a number, so its seconds")
        print("are not the same search and comparing them would be meaningless.")
    else:
        print("the seconds -- job time, the sum of the per-level ms, best of REPEAT")
        print("rung         off        on      saved    speedup   nodes/s off -> on")
        for rung, n, solved, lpb, nodes, ja, jb, bad in rows:
            print("%-9s %8.1fs %8.1fs %8.1fs %8.2fx   %9.0f -> %.0f"
                  % (rung, ja, jb, ja - jb, (ja / jb) if jb > 0 else 0,
                     nodes / ja if ja > 0 else 0, nodes / jb if jb > 0 else 0))
        print("")
        print("(job time, not wall clock: comparable between arms only under equal")
        print(" load.  The number that goes in the docs wants an idle machine.)")

sys.exit(0 if ok else 1)
PY
  gate=$?
else
  gate=0
fi

# ---- the --push-time split ------------------------------------------------
# The item's own instrument, on the level its table is measured on, so the
# buckets before and after are directly comparable with the ones in the doc --
# and so the memo's hit rate is read rather than assumed.
if [ "$what" = all ] || [ "$what" = time ]; then
  echo ""
  echo "the --push-time split, LaserTank.lvl $TIME_LEVEL at $TIME_NODES nodes, one thread"
  for rung in $RUNGS; do
    for arm in off on; do
      extra="--no-push-memo"; [ "$arm" = on ] && extra=""
      echo "--- $rung, memo $arm ---"
      "$exe" --levels data/levels/LaserTank.lvl --level "$TIME_LEVEL" \
             --jobs 1 --nodes "$TIME_NODES" --budget-ms "$BUDGET_MS" \
             --out "build/$PREFIX/pt" --report "build/reports/$PREFIX-pt.jsonl" \
             --force --quiet --push-time $(rung_flags "$rung") $extra \
             2>&1 1>/dev/null | grep push-time
    done
  done
fi

echo ""
[ "$gate" -eq 0 ] && echo "=== done $(date '+%F %T') ===" \
                  || echo "=== done $(date '+%F %T') -- THE GATE FAILED, see above ==="
exit $gate
