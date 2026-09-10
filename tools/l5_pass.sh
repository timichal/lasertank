#!/usr/bin/env bash
# Item 2 of docs/solver/next-actions.md, run for real: the fourth pass, three
# push arms in greedy order over every level the shipped chain fails, at 40M
# nodes.  Priced from the session-33 rehearsal at ~18 h an arm, so ~54 h.
#
#   bash tools/l5_pass.sh                  # all three arms, in order, resumable
#   bash tools/l5_pass.sh l8fire layer7    # a subset, in the order given
#   bash tools/l5_pass.sh status           # the table, from any other shell
#   bash tools/l5_pass.sh status --brief   # the one-line form the ticker prints
#
# One arm at a time on purpose: each wants the whole machine, and 16 jobs is
# already past the point where more parallelism buys anything -- the search is
# bandwidth-bound and effective parallelism is only ~6.4x at 16 jobs.  Greedy
# order, so the largest single result lands first if the run is interrupted.
#
# Progress: one line every TICK seconds (default 300) to stdout *and* appended
# to build/reports/l5-run.log, plus the full table at every arm boundary.  From
# another shell, `bash tools/l5_pass.sh status` prints that table without
# touching the run.
#
# Interruptible.  Ctrl-C it, reboot, start it again with the same command:
# RESUME=1 goes to second_pass.sh, so each arm picks up at the levels its own
# report has not attempted yet rather than at the top of the corpus.  Solved
# levels were already skipped (their .lpb is on disk); what resume recovers is
# the failures, which is where all of the time went.
#
# Survive a closed terminal with nohup:
#   nohup bash tools/l5_pass.sh > /dev/null 2>&1 &
#   tail -f build/reports/l5-run.log
#
# Env, defaulting to the rehearsal's values -- the numbers in the doc are only
# comparable at these: NODES BUDGET_MS JOBS TICK CHAIN LOG.  PREFIX (default
# l5) names the reports, the output dirs and the log, so a smoke test at a toy
# budget can be told to keep out of the real run's files.
set -u

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root" || exit 1

CHAIN=${CHAIN:-build/reports/chain.jsonl}
NODES=${NODES:-40000000}
BUDGET_MS=${BUDGET_MS:-1800000}
JOBS=${JOBS:-16}
TICK=${TICK:-300}
PREFIX=${PREFIX:-l5}
LOG=${LOG:-build/reports/$PREFIX-run.log}
STAMPS=build/reports/$PREFIX-run.stamps
ALL_ARMS="l8fire layer7 enables"

# The layer-8 set, and the flags every arm carries.  --max-keys 5000
# --max-keys-record is the one difference from the arms tabled in the doc:
# layer7 and enables ran at the default 1,200 and could not cross it.  It is
# free and can only raise a cap.
L8="--push-reach --push-ferry-match --push-ferry-maze --push-dead 20 --push-fire 8 --push-shot-run 16 --push-beam 128"
BASE="--no-ida --no-beam --push --push-read --max-keys 5000 --max-keys-record"

arm_flags () {
  case $1 in
    l8fire)  echo "$L8 --push-eval work --push-fire-tier" ;;
    layer7)  echo "--push-stop 1 --push-shot-run 16 --push-beam 128" ;;
    enables) echo "--push-enables 8" ;;
    *)       echo "unknown arm: $1 (have: $ALL_ARMS)" >&2; return 1 ;;
  esac
}

status () {   # status [brief]
  python - "$CHAIN" "$STAMPS" "$PREFIX" "${1:-full}" $ALL_ARMS <<'PY'
import json, os, sys, time

chain, stamps, prefix = sys.argv[1], sys.argv[2], sys.argv[3]
arms, brief = sys.argv[5:], sys.argv[4] == "brief"

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
                    rows[(r["collection"], r["level"])] = r   # last line wins
    return rows

todo = set(k for k, r in load(chain).items() if not r["solved"])
total = len(todo)

# Wall clock per arm, from the stamp file the runner appends to: one
# "<epoch> <arm> start|end" line per arm segment.  An open segment -- the run
# is live, or it died -- is closed at the report's mtime once that is a quarter
# of an hour old, so a dead run stops accumulating wall time and says so.
seg = dict((a, []) for a in arms)
if os.path.exists(stamps):
    for line in open(stamps, encoding="utf-8"):
        p = line.split()
        if len(p) == 3 and p[1] in seg:
            seg[p[1]].append((float(p[0]), p[2]))

rows_by_arm, live = {}, None
for a in arms:
    rep = "build/reports/%s-%s.jsonl" % (prefix, a)
    rows_by_arm[a] = load(rep)
    mtime = os.path.getmtime(rep) if os.path.exists(rep) else 0
    wall, start = 0.0, None
    for t, ev in seg[a]:
        if ev == "start":
            start = t
        elif start is not None:
            wall += t - start
            start = None
    if start is not None:
        now = time.time()
        stale = mtime > 0 and now - mtime > 900
        wall += (mtime if stale else now) - start
        live = (a, "stale" if stale else "live")
    seg[a] = wall

out = []
for a in arms:
    rows = rows_by_arm[a]
    done = [r for k, r in rows.items() if k in todo]
    n = len(done)
    if not n and not seg[a]:
        out.append((a, 0, 0, 0.0, 0.0, None, "not started"))
        continue
    solved = sum(1 for r in done if r["solved"])
    jobt = sum(r["ms"] for r in done) / 1000.0
    wall = seg[a]
    miss = sorted(r["ms"] for r in done if not r["solved"])
    med = miss[len(miss) // 2] / 1000.0 if miss else 0.0
    par = jobt / wall if wall > 0 else 0.0
    eta = (total - n) * med / par if par > 0 and med > 0 else None
    if n >= total:
        note, eta = "done", None
    elif live and live[0] == a:
        note = live[1]
    else:
        note = "stopped"
    out.append((a, n, solved, wall, jobt, eta, note))

ts = time.strftime("%H:%M:%S")
if brief:
    for a, n, solved, wall, jobt, eta, note in out:
        if note in ("live", "stale"):
            pct = 100.0 * n / total if total else 0.0
            warn = "  STALE -- no report line in 15 min" if note == "stale" else ""
            print("%s [%s] %d/%d (%.1f%%)  solved %d  wall %s  jobtime %s  eta %s%s"
                  % (ts, a, n, total, pct, solved, hm(wall), hm(jobt),
                     hm(eta) if eta else "?", warn))
    sys.exit(0)

print("%s  item 2, the fourth pass -- %d levels per arm (%s)" % (ts, total, chain))
print("arm       attempted      solved   wall     jobtime   par    eta      state")
for a, n, solved, wall, jobt, eta, note in out:
    print("%-9s %5d/%-5d %4d %5.1f%%  %-8s %-8s %5.1fx %-8s %s"
          % (a, n, total, solved, (100.0 * solved / n) if n else 0.0, hm(wall),
             hm(jobt), (jobt / wall) if wall > 0 else 0.0,
             hm(eta) if eta else "-", note))

union, per_arm, stops = set(), [], {}
for a in arms:
    got = set(k for k, r in rows_by_arm[a].items() if k in todo and r["solved"])
    per_arm.append("%s %d" % (a, len(got)))
    union |= got
    for k, r in rows_by_arm[a].items():
        if k in todo and not r["solved"]:
            stops[r["stop"]] = stops.get(r["stop"], 0) + 1
if union:
    print("")
    print("union so far: %d levels  (solo: %s)" % (len(union), ", ".join(per_arm)))
    print("stops on the misses: " + ", ".join("%s %d" % kv for kv in
          sorted(stops.items(), key=lambda kv: -kv[1])))
    print("(partial arms included -- the pass's number is arms_union.py at the end)")
PY
}

case ${1:-run} in
  status)    status "${2:+brief}"; exit 0 ;;
  -h|--help) sed -n '2,/^set -u/p' "$0" | sed '$d'; exit 0 ;;
esac

arms=$ALL_ARMS
if [ $# -gt 0 ]; then
  arms=""
  for a in "$@"; do
    arm_flags "$a" > /dev/null || exit 2
    arms="$arms $a"
  done
fi

[ -x build/lasertank-solve.exe ] || { echo "no build/lasertank-solve.exe -- run bash src/build.sh" >&2; exit 1; }
[ -f "$CHAIN" ] || { echo "no chain report at $CHAIN -- tools/chain_union.py rebuilds it" >&2; exit 1; }
mkdir -p build/reports
exec > >(tee -a "$LOG") 2>&1

echo "=================================================================="
echo "l5 pass starting $(date '+%F %T')   arms:$arms"
echo "NODES=$NODES BUDGET_MS=$BUDGET_MS JOBS=$JOBS  chain=$CHAIN  tick=${TICK}s"
echo "=================================================================="
status

arm=
ticker () { while true; do sleep "$TICK"; "$root/tools/l5_pass.sh" status --brief; done; }
ticker &
tick_pid=$!
trap 'kill $tick_pid 2>/dev/null; [ -n "$arm" ] && echo "$(date +%s) $arm end" >> "$STAMPS"; echo "-- interrupted $(date "+%F %T") --"; exit 130' INT TERM
trap 'kill $tick_pid 2>/dev/null' EXIT

for arm in $arms; do
  flags=$(arm_flags "$arm")
  echo ""
  echo "--- arm $arm starting $(date '+%F %T') ---"
  echo "$(date +%s) $arm start" >> "$STAMPS"
  RESUME=1 NODES=$NODES BUDGET_MS=$BUDGET_MS JOBS=$JOBS bash tools/second_pass.sh "$CHAIN" "$PREFIX/$arm" "build/reports/$PREFIX-$arm.jsonl" $BASE $flags
  echo "$(date +%s) $arm end" >> "$STAMPS"
  echo "--- arm $arm finished $(date '+%F %T'); the two-engine gate ---"
  python tools/verify_solutions.py "build/$PREFIX/$arm" || echo "!!! $arm: solutions that do not verify, see above"
  status
done
arm=

echo ""
echo "=== all arms done $(date '+%F %T') ==="
have=
for a in $ALL_ARMS; do
  [ -f "build/reports/$PREFIX-$a.jsonl" ] && have="$have $a=build/reports/$PREFIX-$a.jsonl"
done
[ -n "$have" ] && python tools/arms_union.py $have
