#!/usr/bin/env bash
# Item 19 -- the narrow beam.  One arm at --push-beam 32 over the same 138-level
# GAUNTLET tail item 14 swept, against the already-banked gt-fire control, then
# the five-arm union with item 14's three arms folded in for free.
# The recipe and how to read it: docs/solver/next-actions.md, item 19.
#
#   bash tools/gtw_b32.sh                          # run it
#   tail -f build/reports/gtw-b32-run.log          # from any other shell
#
# Safe beside item 2's l5_pass.sh: JOBS=4 against its 16 on 20 cores, and both
# are node-governed, so contention moves wall clock and no measured number.
# The two item-14 arms that ran beside the pass topped out at 542 s and 429 s a
# level against BUDGET_MS's 1,800 s, and no arm has ever stopped on time.
#
# Resumable: RESUME=1 drops the levels gtw-b32.jsonl has already attempted, so
# Ctrl-C or a reboot and the same command picks up where it stopped.
set -u
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"

LOG=${LOG:-build/reports/gtw-b32-run.log}
NODES=${NODES:-40000000}
BUDGET_MS=${BUDGET_MS:-1800000}
JOBS=${JOBS:-4}

mkdir -p "$(dirname "$LOG")"
exec > >(tee -a "$LOG") 2>&1

say() { echo "[$(date '+%F %T')] $*"; }

say "item 19 -- --push-beam 32  NODES=$NODES BUDGET_MS=$BUDGET_MS JOBS=$JOBS  log=$LOG"
start=$SECONDS

RESUME=1 LT_SOLVE="$root/build/lasertank-solve.exe" \
NODES=$NODES BUDGET_MS=$BUDGET_MS JOBS=$JOBS \
bash tools/second_pass.sh build/reports/gauntlet-tail.jsonl gtw/b32 \
     build/reports/gtw-b32.jsonl \
     --no-ida --no-beam --push --push-read \
     --max-keys 5000 --max-keys-record \
     --push-reach --push-ferry-match --push-ferry-maze \
     --push-dead 20 --push-fire 8 --push-shot-run 16 \
     --push-beam 32 --push-eval work --push-fire-tier
rc=$?

say "arm exited rc=$rc after $(( (SECONDS - start) / 60 )) min"
if [ $rc -ne 0 ]; then
  say "ABORTED -- rerun the same command, it resumes from its own report"
  exit $rc
fi

say "=== two-engine gate ==="
python tools/verify_solutions.py build/gtw/b32 || say "GATE FAILED -- the arm's count is not trustworthy until this passes"

say "=== five-arm union (item 14's three arms folded in) ==="
python tools/arms_union.py \
  fire=build/reports/gt-fire.jsonl \
  b32=build/reports/gtw-b32.jsonl \
  f4p6=build/reports/gtw-f4p6.jsonl \
  f14=build/reports/gtw-f14.jsonl \
  f60=build/reports/gtw-f60.jsonl

say "=== b32 against the control, and the long-record split ==="
python - <<'PY'
import json, statistics

def load(path):
    rows = {}
    for line in open(path, encoding="utf-8-sig"):
        line = line.strip()
        if line:
            r = json.loads(line)
            rows[(r["collection"], r["level"])] = r      # last line wins
    return rows

ctl = load("build/reports/gt-fire.jsonl")
arm = load("build/reports/gtw-b32.jsonl")
cs = {k for k, r in ctl.items() if r["solved"]}
bs = {k for k, r in arm.items() if r["solved"]}

print(f"control gt-fire (--push-beam 128)  {len(cs)} of {len(ctl)}")
print(f"arm     b32     (--push-beam  32)  {len(bs)} of {len(arm)}")
print(f"both {len(cs & bs)}   only b32 {len(bs - cs)}   only control {len(cs - bs)}")

shots = lambda k: (ctl.get(k) or arm[k]).get("ghs_shots") or 0
med = statistics.median([shots(k) for k in ctl])
print(f"median ghs_shots over the {len(ctl)}: {med}")

# Item 19's prediction is not the count: a narrow beam should hold the *deep*
# levels -- long record -- and drop shallow ones.  A tie at 85 with exclusives
# at the long end is a bigger result than beating it by two on no pattern.
for label, ks in (("only b32", bs - cs), ("only control", cs - bs)):
    long_end = [k for k in ks if shots(k) > med]
    print(f"{label}: {len(ks)}   long-record {len(long_end)}   short {len(ks) - len(long_end)}")
    for k in sorted(ks, key=lambda k: -shots(k)):
        r = arm.get(k) or ctl[k]
        print(f"    {k[0]:14} {k[1]:>5}  {r['name'][:26]:26}  ghs_shots {shots(k):>3}  nodes {r.get('nodes')}")
PY

say "=== ITEM 19 DONE ==="
