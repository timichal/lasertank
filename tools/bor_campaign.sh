#!/usr/bin/env bash
# Item 4 -- the campaign that decides whether `--best-of-round` is a default,
# and whether closed item 13's shot test is the rule inside it.
# The recipe and how to read it: docs/solver/next-actions.md, item 4.
#
#   bash tools/bor_campaign.sh                     # everything, ~6-7 h
#   bash tools/bor_campaign.sh acc                 # the acceptance bars -- HOURS at the
#                                                  # default MAXACC=5; the six-minute
#                                                  # recipe falsifier runs first
#   bash tools/bor_campaign.sh floor a             # the null arm: the control twice, ~10 min
#   bash tools/bor_campaign.sh a                   # the dense stage only, ~30 min (measured)
#   bash tools/bor_campaign.sh b                   # the deep stage only, ~5 h
#   bash tools/bor_campaign.sh report              # the table again, free
#   tail -f build/reports/item4-run.log            # from any other shell
#
# Priced from stage A, which is run: 494 levels, 9m40s and 106.7M nodes for the
# control, 9m34s / 10m13s for the two arms -- every level fell in round 0.  A
# level nobody solves is the expensive case, ~27 s to round 2 and ~110 s to
# round 3 at ~1.15M nodes/s across the ten rungs, which is what stage B is made
# of and why the arms there run only where the control won.
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
# **Neither stage contains LaserTank 8 or 9**, which was assumed and is not so:
# chain.jsonl is a STRIDE=5 sample, so it holds levels 1, 6, 11, 16, and stage
# B is derived from its failures.  Level 9 -- 294 keys from the beam against 115
# from push-ferry in the same round -- is the whole argument for the flag and it
# is only visited by stage `acc`.
#
# **A driver ROUND is not node-governed, even though every rung in it is, and
# stage A is where that was measured.**  The stop bit is polled every 120 ms and
# a rung already inside Clean() never sees it, so which rungs have crossed the
# line when a round cancels is a race with the thread scheduler: two runs of the
# same configuration over the same 494 levels disagree on 5-9 levels and about
# 20 keys.  That is bigger than the ratio rule's entire headline, which is why
# round_rules.py leads with the levels the rule fired on and prints the rest as
# the instrument's noise floor.  `floor` measures it directly.
#
# It is still safe beside item 2's l5_pass.sh -- JOBS=4 against its 16 on 20
# cores -- but "contention moves wall clock and no measured number" is NOT true
# of this campaign the way it was of item 19's.  Run the arms of a stage under
# comparable load, and read the node columns, not the seconds.
#
# Resumable: every arm drops the levels its own report has already attempted, so
# Ctrl-C or a reboot and the same command picks up where it stopped.
#
# Knobs (defaults are what the numbers in next-actions.md are measured at):
#   NODES=150000   round 0's budget per rung; round r gets 4^r x this
#   MAXA=2         stage A's last round (150k / 600k / 2.4M per rung)
#   MAXB=3         stage B's last round (adds 9.6M)
#   SAMPLE=6       stage B's stride over the short-record failures
#   MAXACC=5       stage acc's last round.  5, not 3, because the recovered
#                  level-8 run spent 60M nodes in one searcher and a rung is
#                  only handed that much at round 5 (153.6M each).  MAXACC=3
#                  makes the stage a ~20-minute smoke that cannot reach it
#   W8NODES=60M    the candidate recipe's budget, off the recovered run log
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
MAXACC=${MAXACC:-5}
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
           --jobs "$JOBS" --nodes "$NODES" --max-round "$maxr" "$@" < /dev/null \
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

# ---- stage ACC: the two recovered routes as acceptance bars ----------------
#
# **`bench/recovered/` turned item 4's "lost result" into two verified files,
# and reading the records off them changes what they can test.**  Both level-8
# routes -- the recovered 308 and the banked 335 -- spend exactly the record's
# 46 shots, and sit at 1.39x and 1.51x.  So *neither round rule fires on level
# 8*: the ratio rule closes under 2.0, and the shot rule closes too, because a
# route matching the record's shot count is by its own definition the right
# plan.  Level 8's 27 keys are execution slack, not a different strategy, which
# is closed item 13's distinction doing exactly what it was built for.
#
# What does test something is the other flag.  `--beat-banked` refuses a round
# that comes back longer than the .lpb on disk and holds every later round open
# chasing it -- and driver.md says that refusal path "is unit tests and nothing
# more", because in the level-9 acceptance run it never had to fire.  Seed a
# scratch output directory with the recovered 308 and the driver must beat it or
# report `unsolved in N rounds`.  That is the first real exercise of the default
# and the only way the 308 is an acceptance bar at all.
#
# Level 9 is the opposite case and is the one the ROUND rules split on: both its
# routes spend 33 shots against the record's 22, at 1.93x and 1.95x -- inside
# the ratio rule's 2.0, outside the shot rule's test.  It is the disagreement in
# one level, which is why it is here rather than only in the stride.
#
# data/solutions/ is never written: --out points at build/item4/acc-*, so the
# question of re-banking the 308 stays Michal's, as bench/recovered/README.md
# asks.
acc() {
  local lvl=data/levels/LaserTank.lvl
  local rep=build/reports/item4-acc.jsonl
  local seeded=build/item4/acc-beat/LaserTank

  # **The acceptance reports start empty, unlike every other stage's.**  A and B
  # append because they resume -- an interrupted arm must not lose its rows.
  # This stage has no resume: it is eight runs over two levels, and a row left
  # from an earlier run at a different MAXACC would be read as this run's answer
  # by the table below, which takes the last row per level.  Cheaper to be sure.
  rm -f "$rep" build/reports/item4-acc-beat.jsonl build/reports/item4-acc-w2048.jsonl

  # **Cheapest and most decisive first**, the ordering l5_pass.sh uses for the
  # same reason: if the stage is interrupted, the part that settles a question
  # has already landed.  The recipe falsifier is six minutes and answers "is the
  # 308 reproducible"; the round arms are minutes and answer "do the rules fire
  # on these two levels"; the --beat-banked arm needs round 5 and is hours.
  say "=== stage acc: the candidate recipe for level 8's 308 ==="
  # bench/recovered/README.md read the budget straight off the surviving run
  # log -- 1 worker, 60M nodes, beam 600 -- and the directory name w8-2048 is
  # the width.  A batch run, not the driver: the log's banner is the batch one,
  # and a rung is not what is being tested here.
  rm -rf build/item4/acc-w2048
  "$exe" --levels "$lvl" --level 8 --out build/item4/acc-w2048          --report build/reports/item4-acc-w2048.jsonl          --jobs 1 --nodes "${W8NODES:-60000000}" --budget-ms 5400000          --no-ida --push --push-read --push-beam 2048 --push-eval work          --max-keys 5000 --max-keys-record --verbose < /dev/null     || say "acc/w2048 exited rc=$?"
  python tools/verify_solutions.py build/item4/acc-w2048     || say "GATE FAILED on acc-w2048"

  say "=== stage acc: the round rules on levels 8 and 9, from an empty directory ==="
  # Levels 8 and 9 are in neither stage A's population nor stage B's --
  # chain.jsonl is a STRIDE=5 sample, so it holds 1, 6, 11, 16, and stage B is
  # derived from its failures.  This is the stage that actually visits them.
  #
  # What this can show is level 9, where the two rules disagree: both its routes
  # spend 33 shots against the record's 22 at 1.93x and 1.95x, so the ratio rule
  # closes the round and the shot rule holds it open.  It shows nothing on level
  # 8 -- both level-8 routes match the record's 46 shots at 1.39x and 1.51x, so
  # every rule here closes.  Level 8 is the --beat-banked arm's business.
  for name in ctrl bor shots; do
    local flags=--no-best-of-round
    [ "$name" = bor ] && flags=--best-of-round
    [ "$name" = shots ] && flags=--best-of-shots
    rm -rf "build/item4/acc-$name"
    say "acc/$name"
    "$exe" "$lvl" --from 8 --to 9 --max-round "$MAXACC" --nodes "$NODES"            --jobs "$JOBS" --out "build/item4/acc-$name" --report "$rep" $flags < /dev/null       || say "acc/$name exited rc=$? -- carrying on, the rows it wrote still count"
  done

  say "=== stage acc: --beat-banked against the recovered 308, which has never been tested in anger ==="
  # **The first real exercise of a shipped default.**  driver.md: the refusal
  # path "is unit tests and nothing more", because in the level-9 acceptance run
  # --beat-banked never had to fire.  Seed the output directory with the
  # recovered routes and it must fire on every round that comes back longer.
  #
  # This is the expensive arm and MAXACC is why: the recovered level-8 run spent
  # 60M nodes in ONE searcher, and the driver only hands a rung that much at
  # round 5 (153.6M each).  At MAXACC=3 a rung gets 9.6M and the 308 is out of
  # reach, so the arm reports "refused every round" and has measured nothing.
  rm -rf build/item4/acc-beat
  mkdir -p "$seeded"
  cp bench/recovered/LaserTank/00008.lpb "$seeded/00008.lpb"
  cp bench/recovered/LaserTank/00009.lpb "$seeded/00009.lpb"
  # --force, because --beat-banked can only bite on a level that is re-solved:
  # without it the seeded .lpb makes the driver skip the level entirely.
  "$exe" "$lvl" --from 8 --to 9 --max-round "$MAXACC" --nodes "$NODES"          --jobs "$JOBS" --out build/item4/acc-beat --report build/reports/item4-acc-beat.jsonl          --force --best-of-round < /dev/null     || say "acc/beat exited rc=$?"
  # The seeded files are the *target*, not a result: if the run could not beat
  # them they are still sitting there, and gating them would report a pass that
  # measured nothing.  Compare instead.
  python - <<'PY2'
import collections, pathlib
for lv in (8, 9):
    rec = pathlib.Path(f"bench/recovered/LaserTank/{lv:05d}.lpb")
    got = pathlib.Path(f"build/item4/acc-beat/LaserTank/{lv:05d}.lpb")
    if not got.exists():
        print(f"  level {lv}: nothing banked -- the ladder did not beat the target")
        continue
    a, b = rec.read_bytes()[66:], got.read_bytes()[66:]
    if a == b:
        print(f"  level {lv}: unchanged at {len(a)} keys -- refused every round, target not beaten")
    else:
        # Shots only.  Counting "moves" as the four arrow bytes disagreed with
        # the driver's own row -- it printed 257 where the driver said 178 --
        # because a keystream carries more than arrows and shots, and a gloss
        # that contradicts the report row beside it is worse than no gloss.
        print(f"  level {lv}: BEAT the target -- {len(a)} -> {len(b)} keys"
              f" ({collections.Counter(b)[32]} shots)")
PY2
}

# ---- the null arm ----------------------------------------------------------
#
# **The header has advertised this since session 47 and the function was never
# written** -- `bash tools/bor_campaign.sh floor a` reached the dispatch, hit
# `floor: command not found`, and then ran the gate and printed the table, so it
# looked like it had worked.  What it measures: the control a second time, same
# flags, same population, so every difference in the table is the 120 ms stop-bit
# race and nothing else.  round_rules.py already reads `ctrl2` as an arm.
floor() {
  local st=${1:-a}
  say "=== stage $st: the null arm -- the control, run a second time ==="
  [ "$st" = a ] && lists_a || lists_b
  arm ctrl2 "$st" "build/item4/lists-$st" --no-best-of-round
}

run_stage() {
  local st=$1
  # **The control has to ask for the old behaviour now, and that is the whole
  # cost of shipping the rule.**  The campaign's own answer made the shot test
  # the default, so a `ctrl` arm with no flags stopped being a control the
  # moment the driver was rebuilt -- it would quietly be a second `shots` arm
  # and the table would report a rule that saves nothing.  The reports say
  # which rule ran either way (`config` carries [round-rule ...] whether it
  # came from a flag or from the default), so a stale row is detectable; this
  # is what stops one being written.
  say "=== stage $st: the control (the first win ends the round) ==="
  [ "$st" = a ] && lists_a || lists_b
  arm ctrl "$st" "build/item4/lists-$st" --no-best-of-round
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
    acc|ACC) acc ;;
    floor) floor "${2:-a}" ;;
    all) acc; run_stage a; run_stage b ;;
    *) echo "usage: bash tools/bor_campaign.sh [acc|a|b|all|floor [a|b]|report]" >&2; exit 2 ;;
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
  for name in ctrl2 bor shots; do
    [ -f "build/reports/item4-$st-$name.jsonl" ] && arms+=("build/reports/item4-$st-$name.jsonl")
  done
  echo
  python tools/round_rules.py "$st" "build/reports/item4-$st-ctrl.jsonl" ${arms[@]+"${arms[@]}"}
done

# The acceptance rows are three runs over the same two levels, so the table
# above is the wrong shape for them -- what is wanted is the two levels side by
# side against the recovered files.
if [ -f build/reports/item4-acc.jsonl ]; then
  echo
  python - <<'PY3'
import collections, json, pathlib, struct

ghs = pathlib.Path("data/levels/LaserTank.ghs").read_bytes()
rows = {}
for path, arm in (("build/reports/item4-acc.jsonl", None),
                  ("build/reports/item4-acc-beat.jsonl", "beat")):
    if not pathlib.Path(path).exists():
        continue
    for line in open(path, encoding="utf-8-sig"):
        line = line.strip()
        if not line:
            continue
        r = json.loads(line)
        # The three round arms share one report and are told apart by their own
        # `config` -- the session-48 column earning its keep on the first table
        # that needed it.  Read the EFFECTIVE rule, not the flags: the driver
        # appends [round-rule shots R|ratio R|off] whether it came from a flag
        # or from the default, and matching on flag text now gets it backwards
        # twice over -- `--no-best-of-round` contains `--best-of-round`, and an
        # arm that passes nothing at all is the shot rule rather than a control.
        cfg = r.get("config", "")
        rule = cfg.partition("[round-rule ")[2].partition("]")[0]
        if rule:
            which = ("shots" if rule.startswith("shots")
                     else "bor" if rule.startswith("ratio") else "ctrl")
        else:
            # A report banked before the marker existed, which every row from
            # the 2026-09-14 run is.  Order matters: --no-best-of-round has
            # --best-of-round inside it, so the negative has to be tested
            # first or the control reads as the ratio arm.
            which = ("shots" if "--best-of-shots" in cfg
                     else "ctrl" if "--no-best-of-round" in cfg
                     else "bor" if "--best-of-round" in cfg else "ctrl")
        name = arm or which
        rows[(name, r["level"])] = r

print("item 4, stage acc  --  LaserTank 8 and 9 against bench/recovered/")
for lv in (8, 9):
    m, sh = struct.unpack_from("<HH", ghs, (lv - 1) * 10)
    rec = pathlib.Path(f"bench/recovered/LaserTank/{lv:05d}.lpb").read_bytes()[66:]
    c = collections.Counter(rec)
    print(f"  level {lv}   record {m}+{sh}={m + sh}"
          f"   recovered {len(rec)} keys, {c[32]} shots, {len(rec) / (m + sh):.2f}x")
    for name in ("ctrl", "bor", "shots", "beat"):
        r = rows.get((name, lv))
        if r is None:
            continue
        if not r["solved"]:
            # `stop` is the driver's own enum and "rounds" is the value for
            # "--max-round ran out", so printing it raw reads as `rounds after
            # 6 rounds`.  Say it the way the driver's own line says it, and
            # keep the other two values distinguishable: a level a key gave up
            # on and a level the budget ran out on are not the same result.
            how = {"rounds": "unsolved", "skipped": "SKIPPED BY A KEYPRESS",
                   "stopped": "STOPPED (q)"}.get(r["stop"], r["stop"])
            print(f"    {name:<6} {how} in {r.get('rounds', 0)} rounds"
                  f"   {r.get('total_nodes', 0) / 1e6:.0f}M nodes")
            continue
        d = r["keys"] - len(rec)
        print(f"    {name:<6} {r['keys']:>5} keys ({r['moves']}m/{r['shots']}s)"
              f" {r['ratio']:.2f}x   {d:+d} against the recovered"
              f"   {r['method']}, round {r.get('rounds', 1) - 1},"
              f" {r.get('total_nodes', 0) / 1e6:.0f}M nodes")
        if r.get("rung"):
            print(f"           rung: {r['rung']}")
PY3
fi

[ "$stage" = report ] || say "=== ITEM 4 DONE ==="
