#!/usr/bin/env bash
# Item 4 -- the campaign that decides whether `--best-of-round` is a default,
# and whether closed item 13's shot test is the rule inside it.
# The recipe and how to read it: docs/solver/next-actions.md, item 4.
#
#   bash tools/bor_campaign.sh                     # both stages, ~6-7 h
#   bash tools/bor_campaign.sh a                   # the dense stage only, ~1.5 h
#   bash tools/bor_campaign.sh b                   # the deep stage only, ~5 h
#   bash tools/bor_campaign.sh report              # the table again, free
#   tail -f build/reports/item4-run.log            # from any other shell
#
# Priced rather than guessed, from a 13-level rehearsal at JOBS=4 beside item
# 2's pass: the ten rungs get through about 1.15M nodes a second between them,
# so a level nobody solves costs 31M nodes / ~27 s to round 2 and 127M / ~110 s
# to round 3, and a round held open costs the whole of its own budget -- 96M at
# round 3.  Stage A is nearly all solved levels and cheap; stage B is the other
# way round, which is why the arms there run only where the control won.
#
# **Three arms, and the third one is new code.**  The control is the driver as
# it ships (the first win ends the round); `bor` is `--best-of-round`, whose
# rule is `keys / record <= 2.0`; `shots` is `--best-of-shots`, which keeps the
# round open whenever the win spends more shots than the record whatever its
# ratio, and closes it on a looser 3.0 otherwise.  Closed item 13 measured that
# those two tests disagree on 58 of 452 solved rows; what nobody has measured is
# whether the rounds the disagreement keeps open pay for themselves.
#
# **Why the arms are run over the levels the control solved rather than over the
# whole population.**  Neither flag can change *whether* a level falls -- both
# are consulted only once a rung has already won, and neither adds budget -- so
# a level the control could not solve costs all three arms exactly the same and
# contributes no keys to compare.  Running the arms over the control's solved
# set is therefore the same measurement for a fraction of the machine time, and
# `round_rules.py` checks the assumption rather than trusting it: a level an arm
# solves that the control did not is reported as an instrument warning.
#
# **Two stages, because the flag's cost and its benefit live in different
# populations.**  Stage A is the 494 levels the shipped chain solves at 150k --
# dense, cheap, and the population a default would apply to on nearly every run.
# Stage B is a stride over `bench/short-record-failures.txt`, the levels with a
# record of <= 60 that the chain fails: those are what the driver takes several
# rounds over, and a late round is where the ladder's rungs disagree most.
# Level 9 -- 294 keys from the beam against 115 from push-ferry in the same
# round -- is a stage-B level, and it is the whole argument for the flag.
#
# Safe beside item 2's l5_pass.sh: JOBS=4 against its 16 on 20 cores, and the
# driver is node-governed round by round, so contention moves wall clock and no
# measured number.  Wall clock IS one of the numbers this item wants, so the
# report prints node counts beside every second it quotes.
#
# Resumable: every arm drops the levels its own report has already attempted, so
# Ctrl-C or a reboot and the same command picks up where it stopped.
#
# Knobs (defaults are what the numbers in next-actions.md are measured at):
#   NODES=150000   round 0's budget per rung; round r gets 4^r x this
#   MAXA=2         stage A's last round (150k / 600k / 2.4M per rung)
#   MAXB=3         stage B's last round (adds 9.6M)
#   SAMPLE=6       stage B's stride over the short-record failures
#   JOBS=4         search slots, shared by the ten rungs of a round
set -u
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"

stage=${1:-all}
LOG=${LOG:-build/reports/item4-run.log}
NODES=${NODES:-150000}
MAXA=${MAXA:-2}
MAXB=${MAXB:-3}
SAMPLE=${SAMPLE:-6}
JOBS=${JOBS:-4}
CHAIN=${CHAIN:-build/reports/chain.jsonl}
SHORT=${SHORT:-bench/short-record-failures.txt}

# LT_SOLVE overrides the binary, for the same reason $LT_CORE exists (PROGRESS):
# a running solve holds build/lasertank-solve.exe open, so `dotnet publish -o
# build` cannot replace it and the only way to run a changed driver during a
# long pass is to point at the project's own bin/.
exe="${LT_SOLVE:-$root/build/lasertank-solve.exe}"
[ -x "$exe" ] || { echo "no $exe -- run bash src/build.sh" >&2; exit 1; }

# **Preflight the third arm rather than discovering it two hours in.**  The
# `shots` arm is a flag that did not exist before session 48, and while item 2's
# pass is running `build/lasertank-solve.exe` cannot be replaced -- the running
# solve holds it open -- so the only build of the new driver is the project's
# own bin/.  Find it and say so, rather than running three arms of which one
# silently repeats the second.
if [ "$stage" != report ] && ! "$exe" 2>&1 | grep -q -- "--best-of-shots"; then
  bin=$root/src/LaserTank.Solver/bin/Release/net8.0/lasertank-solve.exe
  if [ -x "$bin" ] && "$bin" 2>&1 | grep -q -- "--best-of-shots"; then
    echo "$exe predates --best-of-shots; using $bin instead"
    exe=$bin
  else
    echo "no build of the driver knows --best-of-shots -- run bash src/build.sh," >&2
    echo "or, while a pass holds build/ open:" >&2
    echo "  dotnet build src/LaserTank.Solver/LaserTank.Solver.csproj -c Release" >&2
    exit 1
  fi
fi

mkdir -p build/reports build/item4
[ "$stage" = report ] || exec > >(tee -a "$LOG") 2>&1
say() { echo "[$(date '+%F %T')] $*"; }

# ---- one arm over one per-collection list directory ------------------------
#
# $1 arm name (ctrl|bor|shots)  $2 stage (a|b)  $3 list dir  $4+ solver flags
arm() {
  local name=$1 st=$2 lists=$3; shift 3
  local report=build/reports/item4-$st-$name.jsonl
  local out=build/item4/$st-$name
  local maxr; [ "$st" = a ] && maxr=$MAXA || maxr=$MAXB
  local start=$SECONDS n=0

  for lvl in data/levels/*.lvl; do
    local coll; coll=$(basename "$lvl" .lvl)
    local list=$lists/$coll.txt
    [ -s "$list" ] || continue
    # Resume: drop what this arm's own report already has a row for.  Unlike a
    # campaign, an unsolved level leaves no .lpb behind, so the .lpb skip inside
    # the driver recovers the solved levels only -- and the failures are where
    # the hours went.
    local todo=$lists/$coll.$name.txt
    python - "$list" "$report" "$coll" "$todo" <<'PY'
import json, os, sys
list_path, report, coll, todo = sys.argv[1:5]
want = [int(x) for x in open(list_path) if x.strip()]
done = set()
if os.path.exists(report):
    for line in open(report, encoding="utf-8-sig"):
        line = line.strip()
        if line:
            r = json.loads(line)
            if r["collection"] == coll:
                done.add(r["level"])
left = [x for x in want if x not in done]
open(todo, "w").write("".join(str(x) + "\n" for x in left))
print(f"{coll}: {len(left)} of {len(want)} left")
PY
    [ -s "$todo" ] || continue
    n=$((n + $(wc -l < "$todo")))
    say "arm $name stage $st: $coll"
    "$exe" "$lvl" --levels-list "$todo" --out "$out" --report "$report" \
           --jobs "$JOBS" --nodes "$NODES" --max-round "$maxr" "$@" \
      || { say "ARM $name ABORTED rc=$? -- rerun the same command, it resumes"; exit 1; }
  done
  say "arm $name stage $st: $n levels in $(( (SECONDS - start) / 60 )) min"
}

# ---- the two populations ---------------------------------------------------

# Stage A: what the shipped chain solves.  One list per collection.
lists_a() {
  local out=build/item4/lists-a
  mkdir -p "$out"; rm -f "$out"/*.txt
  python - "$CHAIN" "$out" <<'PY'
import json, os, sys
chain, out = sys.argv[1], sys.argv[2]
rows = {}
for line in open(chain, encoding="utf-8-sig"):
    line = line.strip()
    if line:
        r = json.loads(line)
        rows[(r["collection"], r["level"])] = r          # last line wins
by = {}
for (coll, lv), r in rows.items():
    if r["solved"]:
        by.setdefault(coll, []).append(lv)
n = 0
for coll, lv in by.items():
    lv.sort()
    n += len(lv)
    open(os.path.join(out, coll + ".txt"), "w").write("\n".join(str(x) for x in lv))
print(f"stage A: {n} levels the chain solves, over {len(by)} collections")
PY
}

# Stage B: a stride over the short-record failures, which is the population
# whose header rule is in the file itself -- a .ghs record of <= 60 and the
# chain fails it.  SAMPLE=1 is all 687 of them and about a day.
lists_b() {
  local out=build/item4/lists-b
  mkdir -p "$out"; rm -f "$out"/*.txt
  python - "$SHORT" "$out" "$SAMPLE" <<'PY'
import os, sys
short, out, step = sys.argv[1], sys.argv[2], max(1, int(sys.argv[3]))
by = {}
for line in open(short, encoding="utf-8-sig"):
    line = line.strip()
    if not line or line.startswith("#"):
        continue
    coll, lv = line.split()[0], int(line.split()[1])
    by.setdefault(coll, []).append(lv)
n = 0
for coll, lv in by.items():
    lv = sorted(lv)[::step]
    n += len(lv)
    open(os.path.join(out, coll + ".txt"), "w").write("\n".join(str(x) for x in lv))
print(f"stage B: {n} short-record failures at stride {step}, over {len(by)} collections")
PY
}

# The arms after the control run only where the control won: neither flag can
# change whether a level falls, so a level the control failed is a level with
# nothing to compare and the same bill in all three arms.
lists_solved() {
  local st=$1 out=build/item4/lists-$st-solved
  mkdir -p "$out"; rm -f "$out"/*.txt
  python - "build/reports/item4-$st-ctrl.jsonl" "$out" <<'PY'
import json, os, sys
report, out = sys.argv[1], sys.argv[2]
rows = {}
for line in open(report, encoding="utf-8-sig"):
    line = line.strip()
    if line:
        r = json.loads(line)
        rows[(r["collection"], r["level"])] = r
by = {}
for (coll, lv), r in rows.items():
    if r["solved"]:
        by.setdefault(coll, []).append(lv)
n = 0
for coll, lv in by.items():
    lv.sort()
    n += len(lv)
    open(os.path.join(out, coll + ".txt"), "w").write("\n".join(str(x) for x in lv))
print(f"the control solved {n} of them")
PY
}

run_stage() {
  local st=$1
  say "=== stage $st: the control (the driver as it ships) ==="
  [ "$st" = a ] && lists_a || lists_b
  arm ctrl "$st" "build/item4/lists-$st"
  lists_solved "$st"
  say "=== stage $st: --best-of-round (ratio 2.0) ==="
  arm bor "$st" "build/item4/lists-$st-solved" --best-of-round
  say "=== stage $st: --best-of-shots (the shot test, looser ratio 3.0) ==="
  arm shots "$st" "build/item4/lists-$st-solved" --best-of-shots
}

if [ "$stage" != report ]; then
  say "item 4 -- stage=$stage NODES=$NODES MAXA=$MAXA MAXB=$MAXB SAMPLE=$SAMPLE JOBS=$JOBS"
  t0=$SECONDS
  case "$stage" in
    a|A) run_stage a ;;
    b|B) run_stage b ;;
    all) run_stage a; run_stage b ;;
    *) echo "usage: bash tools/bor_campaign.sh [a|b|all|report]" >&2; exit 2 ;;
  esac
  say "=== two-engine gate over everything the three arms banked ==="
  for d in build/item4/?-ctrl build/item4/?-bor build/item4/?-shots; do
    [ -d "$d" ] || continue
    python tools/verify_solutions.py "$d" \
      || say "GATE FAILED on $d -- no count here is trustworthy until this passes"
  done
  say "campaign done in $(( (SECONDS - t0) / 60 )) min"
fi

# Free, and it reads what is on disk now -- so `report` in another shell is also
# the way to see how far a running campaign has got, the way l5_pass.sh's
# `status` is.  An arm that has not started yet is simply not in the table.
for st in a b; do
  [ -f "build/reports/item4-$st-ctrl.jsonl" ] || continue
  arms=()
  for name in bor shots; do
    [ -f "build/reports/item4-$st-$name.jsonl" ] && arms+=("build/reports/item4-$st-$name.jsonl")
  done
  echo
  python tools/round_rules.py "$st" "build/reports/item4-$st-ctrl.jsonl" ${arms[@]+"${arms[@]}"}
done

[ "$stage" = report ] || say "=== ITEM 4 DONE ==="
