#!/usr/bin/env bash
# Item 7 of docs/solver/next-actions.md, run for real: the solved-vs-budget
# curve for the shipped chain's four searchers, over every level the chain
# fails, at 1M / 10M / 50M nodes.
#
#   bash tools/curve_pass.sh                    # the whole curve, in order, resumable
#   bash tools/curve_pass.sh 1000000 10000000   # a subset of the rungs, in the order given
#   bash tools/curve_pass.sh status             # the table, from any other shell
#   bash tools/curve_pass.sh status --brief     # the one-line form the ticker prints
#   bash tools/curve_pass.sh price              # what the full run costs, measured not guessed
#
#   nohup bash tools/curve_pass.sh > /dev/null 2>&1 &   # to survive a closed terminal
#   tail -f build/reports/curve-run.log
#
# **The question.**  Every number in these files is quoted at 150k nodes so that
# layers can be *attributed*; that is the right measurement budget and the wrong
# production budget, and the two have been conflated.  The chain stops on
# `budget` on 3,540 of its 3,691 failures (95.9%), and the one accidental 1M run
# came back 3.3x the levels.  This is the production number the project is
# measured by and it has never been run above 150k except by accident.
#
# **Four searchers, in the chain's own order, each pass over what the previous
# one failed.**  `--sg-eval coarse` on the second pass is not optional: a bare
# --subgoal means --sg-eval work, which is the ranking that pass is *retired*
# for, and appended after coarse -> learned it adds 0.  The three searchers the
# shipped 494 is made of are --no-ida, coarse and learned, in that order; the
# macro beam is the fourth and adds 3 at 150k.
#
# **SAMPLE=15 by default, and the default is the point.**  The full population
# is 3,691 levels and the curve asks a *rate* question, so a 1-in-15 stride over
# each collection's failures answers it unbiased -- the same argument the fourth
# pass's own stride rests on, and the same stride, so the two are read against
# each other.  SAMPLE=1 runs the whole population; `price` below says what that
# costs before you commit to it.  The stride is deterministic, so every rung of
# the curve attacks the *same* levels and the rungs are comparable.
#
# **BUDGET_MS is deliberately enormous (30 min) and that is not a wall-clock
# budget, it is a guard.**  This measures nodes; a level that stops on the clock
# instead of on the node cap is a level measured at some other budget than the
# one the column says.  The table reports, per stage, how many levels reached
# the node cap -- read that as the column's honesty and not as trivia.
#
# **--max-keys 5000 --max-keys-record on every rung**, where the item's recipe
# carries it from 10M up.  Deliberate: it can only *raise* a cap, never lower
# one, and carrying it everywhere makes the three rungs the same searcher, so a
# difference between them is the budget rather than the key cap.  1,367 of the
# chain's 3,691 failures have a record long enough to lift them past the default
# 1,200 and 302 past 5,000, and `Challenge-IV` 641 needed 1,876 keys against a
# record of 143.  MAXKEYS= (empty) drops it.
#
# **Interaction with item 2.**  That item's arms are the push rungs as a *fifth*
# pass, already measured at 40M on this same stride; this is the production
# curve for the four the chain actually ships.  Run this first if the question
# is what to run production at, that one if the question is what layer 5 adds.
#
# Resumable.  Ctrl-C it, reboot, start it again: every finished stage leaves a
# stamp and is skipped, and the stage that was interrupted picks up at the
# levels its own report has not attempted yet (RESUME=1 to second_pass.sh).
#
# Env: CHAIN BUDGETS SAMPLE JOBS BUDGET_MS TICK PREFIX MAXKEYS LOG, and
# PRICE_NODES / PRICE_SAMPLE for `price`.
set -u

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root" || exit 1

CHAIN=${CHAIN:-build/reports/chain.jsonl}
BUDGETS=${BUDGETS:-"1000000 10000000 50000000"}
SAMPLE=${SAMPLE:-15}
JOBS=${JOBS:-16}
BUDGET_MS=${BUDGET_MS:-1800000}
TICK=${TICK:-300}
PREFIX=${PREFIX:-curve}
MAXKEYS=${MAXKEYS---max-keys 5000 --max-keys-record}
LOG=${LOG:-build/reports/$PREFIX-run.log}
STAMPS=build/reports/$PREFIX-run.stamps
PRICE_NODES=${PRICE_NODES:-1000000}
PRICE_SAMPLE=${PRICE_SAMPLE:-150}
ALL_STAGES="l0 l3 l4 l1"

# The chain, as four searchers.  These are names for what SOLVER.md calls
# layers 0, 3, 4 and 1, in the order the shipped composite runs them.
stage_flags () {
  case $1 in
    l0) echo "--no-ida" ;;
    l3) echo "--no-ida --no-beam --subgoal --sg-eval coarse" ;;
    l4) echo "--no-ida --no-beam --subgoal --sg-eval learned" ;;
    l1) echo "--no-ida --no-beam --macro --macro-first" ;;
    *)  echo "unknown stage: $1 (have: $ALL_STAGES)" >&2; return 1 ;;
  esac
}

# The report a stage reads and the one it writes.  Stage l0 reads the chain
# itself; every later stage reads the one before it, which is what makes this a
# chain rather than four independent passes.
stage_in ()  { [ "$2" = l0 ] && echo "$CHAIN" || echo "build/reports/$PREFIX-$1-$3.jsonl"; }
stage_rep () { echo "build/reports/$PREFIX-$1-$2.jsonl"; }
prev_of ()   { case $1 in l3) echo l0 ;; l4) echo l3 ;; l1) echo l4 ;; *) echo "" ;; esac; }

status () {   # status [brief]
  python - "$CHAIN" "$PREFIX" "$SAMPLE" "$STAMPS" "${1:-full}" "$BUDGETS" $ALL_STAGES <<'PY'
import json, os, sys, time

chain, prefix, sample, stamps = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
brief = sys.argv[5] == "brief"
budgets = sys.argv[6].split()
stages = sys.argv[7:]

def hm(s):
    s = int(max(0, s))
    return "%dh%02dm" % (s // 3600, s % 3600 // 60)

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

# The population: the chain's failures, strided exactly the way second_pass.sh
# strides them, so "attempted n of N" below counts against what this run was
# ever going to attack rather than against the whole corpus.
fails = {}
for k, r in load(chain).items():
    if not r["solved"]:
        fails.setdefault(k[0], []).append(k[1])
todo = set()
for coll, lv in fails.items():
    for x in sorted(lv)[::max(1, sample)]:
        todo.add((coll, x))
total = len(todo)

wall = {}
if os.path.exists(stamps):
    open_at = {}
    for line in open(stamps, encoding="utf-8"):
        p = line.split()
        if len(p) != 4:
            continue
        t, b, st, ev = float(p[0]), p[1], p[2], p[3]
        if ev == "start":
            open_at[(b, st)] = t
        elif (b, st) in open_at:
            wall[(b, st)] = wall.get((b, st), 0.0) + t - open_at.pop((b, st))
    now = time.time()
    for (b, st), t in open_at.items():
        rep = "build/reports/%s-%s-%s.jsonl" % (prefix, b, st)
        m = os.path.getmtime(rep) if os.path.exists(rep) else 0
        stale = m > 0 and now - m > 900
        wall[(b, st)] = wall.get((b, st), 0.0) + ((m if stale else now) - t)
        wall[("live", "")] = (b, st, "stale" if stale else "live")

live = wall.get(("live", ""))
ts = time.strftime("%H:%M:%S")

if brief:
    if live:
        b, st, how = live
        rows = load("build/reports/%s-%s-%s.jsonl" % (prefix, b, st))
        done = [r for k, r in rows.items() if k in todo]
        n, solved = len(done), sum(1 for r in done if r["solved"])
        miss = sorted(r["ms"] for r in done if not r["solved"])
        med = miss[len(miss) // 2] / 1000.0 if miss else 0.0
        w = wall.get((b, st), 0.0)
        jobt = sum(r["ms"] for r in done) / 1000.0
        par = jobt / w if w > 0 else 0.0
        eta = (total - n) * med / par if par > 0 and med > 0 else None
        print("%s [%s %s] %d/%d  solved %d  wall %s  jobtime %s  eta %s%s"
              % (ts, b, st, n, total, solved, hm(w), hm(jobt),
                 hm(eta) if eta else "?",
                 "  STALE -- no report line in 15 min" if how == "stale" else ""))
    sys.exit(0)

print("%s  item 7, the solved-vs-budget curve -- %d levels a rung "
      "(stride 1-in-%d over %s's failures)" % (ts, total, sample, chain))
print("")
print("budget      stage  attempted    solved  cumulative   at the node cap"
      "    jobtime   wall")
curve = []
for b in budgets:
    got = set()
    any_rows = False
    for st in stages:
        rows = load("build/reports/%s-%s-%s.jsonl" % (prefix, b, st))
        done = [r for k, r in rows.items() if k in todo]
        if not done and (b, st) not in wall:
            continue
        any_rows = True
        solved = [r for r in done if r["solved"]]
        got |= set((r["collection"], r["level"]) for r in solved)
        # A level that stopped on `budget` without reaching the node cap
        # stopped on the *clock*, and is measured at some other budget than
        # this column says.  Counted rather than assumed.
        miss = [r for r in done if not r["solved"]]
        capped = sum(1 for r in miss if r["nodes"] >= int(b) * 0.99)
        jobt = sum(r["ms"] for r in done) / 1000.0
        print("%-11s %-5s  %5d      %5d       %5d   %5d of %-5d      %-8s %s"
              % (b, st, len(done), len(solved), len(got), capped, len(miss),
                 hm(jobt), hm(wall.get((b, st), 0.0))))
    if any_rows:
        curve.append((b, len(got)))
if curve:
    print("")
    print("the curve -- levels the chain fails that a bigger budget solves")
    print("budget        added   of %d    rate" % total)
    for b, n in curve:
        print("%-11s   %5d   %5d   %5.1f%%" % (b, n, total, 100.0 * n / total if total else 0))
    print("")
    print("(150k, the budget every other number in these files is quoted at,")
    print(" is this population's 0 by construction: it is what failed there.)")
PY
}

price () {
  echo "pricing the run: stage $ALL_STAGES at $PRICE_NODES nodes over a"
  echo "1-in-$PRICE_SAMPLE stride, then scaled to the rungs by node count."
  echo ""
  # A price run is a measurement and not a resumption: clear its own stamps and
  # reports so it re-measures rather than reporting the last one back.
  rm -f build/reports/$PREFIX-price-$PRICE_NODES-*.done         build/reports/$PREFIX-price-$PRICE_NODES-*.jsonl
  rm -rf build/$PREFIX-price-$PRICE_NODES
  SAMPLE=$PRICE_SAMPLE NODES=$PRICE_NODES BUDGET_MS=$BUDGET_MS JOBS=$JOBS \
    PREFIX=$PREFIX-price BUDGETS=$PRICE_NODES bash "$0" "$PRICE_NODES" --price-run \
    > build/reports/$PREFIX-price.log 2>&1
  echo "(the run itself is in build/reports/$PREFIX-price.log)"
  python - "$CHAIN" "$PREFIX-price" "$PRICE_SAMPLE" "$PRICE_NODES" "$SAMPLE" \
           "$JOBS" "$BUDGETS" $ALL_STAGES <<'PY'
import json, os, sys

chain, prefix, psample = sys.argv[1], sys.argv[2], int(sys.argv[3])
pnodes, sample, jobs = int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6])
budgets = [int(x) for x in sys.argv[7].split()]
stages = sys.argv[8:]

def load(path):
    rows = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8-sig") as f:
            for line in f:
                line = line.strip()
                if line:
                    r = json.loads(line)
                    rows[(r["collection"], r["level"])] = r
    return rows

fails = {}
for k, r in load(chain).items():
    if not r["solved"]:
        fails.setdefault(k[0], []).append(k[1])
pop = sum(len(sorted(v)[::max(1, sample)]) for v in fails.values())
whole = sum(len(v) for v in fails.values())

# Effective parallelism, measured on the price run itself rather than assumed:
# the search is bandwidth-bound and 16 jobs buys about 6.4x, which is the single
# biggest error a naive estimate makes here.
# The projection, per level rather than off a median, because the stages do not
# all scale with the node budget.  A level that reached the cap burns the whole
# of a bigger one too, so its cost scales; a level that stopped on a dead end
# costs what it cost at any budget, and the macro beam is *entirely* that -- 0
# of 30 at the cap in the first price run, so multiplying its median by 50 would
# have invented hours that do not exist.
print("")
print("stage  priced on   median s   at the node cap   -> s a level at")
print("                    a level                     %s"
      % "  ".join("%11d" % b for b in budgets))
rows = []
for st in stages:
    r = load("build/reports/%s-%d-%s.jsonl" % (prefix, pnodes, st))
    if not r:
        continue
    vals = list(r.values())
    v = sorted(x["ms"] for x in vals) or [0]
    med = v[len(v) // 2] / 1000.0
    ncap = sum(1 for x in vals if x["nodes"] >= pnodes * 0.99)
    at = {}
    for b in budgets:
        # What this level would cost at budget b: scaled if the node cap is
        # what stopped it, unchanged if something else was.
        at[b] = sum((x["ms"] * b / float(pnodes))
                    if x["nodes"] >= pnodes * 0.99 else x["ms"]
                    for x in vals) / 1000.0 / len(vals)
    rows.append((st, len(vals), at))
    print("%-6s %9d %10.1f %8d of %-8d %s"
          % (st, len(vals), med, ncap, len(vals),
             "  ".join("%11.1f" % at[b] for b in budgets)))

print("")
print("the whole run, %d levels a stage -- job time, the sum of the per-level ms"
      % pop)
print("budget        l0        l3        l4        l1       rung")
grand = 0.0
for b in budgets:
    cells = [pop * at[b] / 3600.0 for _, _, at in rows]
    tot = sum(cells)
    grand += tot
    print("%-11s %s %9s"
          % (b, " ".join("%9.1f" % c for c in cells), "%.1f h" % tot))
print("")
print("%.1f h of job time.  At the ~6.4x effective parallelism 16 jobs buys on"
      % grand)
print("this machine -- the search is bandwidth-bound, which is why 16 jobs is")
print("not 16x -- that is about %.0f h of wall clock." % (grand / 6.4))
print("SAMPLE=1 is the whole %d-level population rather than this %d-level"
      % (whole, pop))
print("stride: about %.0fx of the above." % (whole / float(pop) if pop else 0))
print("")
print("Priced on whatever load the machine was under, because job time is wall")
print("time per level: run beside the fourth pass it comes out pessimistic, and")
print("that is the direction to be wrong in.")
PY
}

case ${1:-run} in
  status)    status "${2:+brief}"; exit 0 ;;
  price)     price; exit 0 ;;
  -h|--help) sed -n '2,/^set -u/p' "$0" | sed '$d'; exit 0 ;;
esac

# `price` re-enters here with --price-run: one budget, a coarse stride, no
# stamps, no log, no gate -- just the four stage reports it then reads.
priced=
if [ $# -gt 1 ] && [ "${2:-}" = "--price-run" ]; then priced=1; set -- "$1"; fi

budgets=$BUDGETS
if [ $# -gt 0 ] && [ "${1:-run}" != run ]; then budgets="$*"; fi

[ -x build/lasertank-solve.exe ] || { echo "no build/lasertank-solve.exe -- run bash src/build.sh" >&2; exit 1; }
[ -f "$CHAIN" ] || { echo "no chain report at $CHAIN -- tools/chain_union.py rebuilds it" >&2; exit 1; }
mkdir -p build/reports
[ -n "$priced" ] || exec > >(tee -a "$LOG") 2>&1

if [ -z "$priced" ]; then
  echo "=================================================================="
  echo "curve pass starting $(date '+%F %T')   budgets:$budgets"
  echo "SAMPLE=$SAMPLE BUDGET_MS=$BUDGET_MS JOBS=$JOBS  chain=$CHAIN  tick=${TICK}s"
  echo "stages: $ALL_STAGES   extra flags: ${MAXKEYS:-none}"
  echo "=================================================================="
  status

  # The wait is five seconds at a time rather than one sleep of $TICK, so
  # that killing the ticker at the end kills it now: `kill` reaches the
  # loop and not the `sleep` under it, and a sleeping child holds the log
  # pipe open, which is a whole TICK of the run looking hung after it has
  # finished.
  ticker () {
    while true; do
      i=0; while [ $i -lt "$TICK" ]; do sleep 5; i=$((i + 5)); done
      bash "$root/tools/curve_pass.sh" status --brief
    done
  }
  ticker &
  tick_pid=$!
  trap 'kill $tick_pid 2>/dev/null; echo "-- interrupted $(date "+%F %T") --"; exit 130' INT TERM
  trap 'kill $tick_pid 2>/dev/null' EXIT
fi

for b in $budgets; do
  sols=$PREFIX-$b
  for st in $ALL_STAGES; do
    rep=$(stage_rep "$b" "$st")
    prev=$(prev_of "$st")
    in=$(stage_in "$b" "$st" "$prev")
    done_stamp=build/reports/$PREFIX-$b-$st.done
    if [ -f "$done_stamp" ]; then
      echo "--- $b $st already done, skipping ---"
      continue
    fi
    [ -f "$in" ] || { echo "!!! $b $st: no input report at $in -- the stage before it did not finish" >&2; exit 1; }
    echo ""
    echo "--- $b $st starting $(date '+%F %T') ---"
    [ -n "$priced" ] || echo "$(date +%s) $b $st start" >> "$STAMPS"
    # SAMPLE strides the *first* stage only: every later stage reads a report
    # that is already the stride, and striding it again would take a fifteenth
    # of a fifteenth.
    RESUME=1 SAMPLE=$([ "$st" = l0 ] && echo "$SAMPLE" || echo 1) \
      NODES=$b BUDGET_MS=$BUDGET_MS JOBS=$JOBS \
      bash tools/second_pass.sh "$in" "$sols" "$rep" $(stage_flags "$st") $MAXKEYS
    [ -n "$priced" ] || echo "$(date +%s) $b $st end" >> "$STAMPS"
    touch "$done_stamp"
  done
  [ -n "$priced" ] && continue
  echo "--- $b done $(date '+%F %T'); the two-engine gate ---"
  python tools/verify_solutions.py "build/$sols" || echo "!!! $b: solutions that do not verify, see above"
  status
done

[ -n "$priced" ] && exit 0
echo ""
echo "=== the curve is done $(date '+%F %T') ==="
status
