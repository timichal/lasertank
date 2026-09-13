#!/usr/bin/env python3
"""Item 18 -- the search horizon per level, in board changes.

Closed item 15 measured one level: `LaserTank.lvl` 6 finishes from **48** board
changes out and not from 51, at 40M nodes and width 512.  It could not say
whether 48 is a property of the *searcher* or of that *level*, and the two
answers send item 5 in different directions -- a constant is a hard design
bound on how long a committed phase may be, a spread of an order of magnitude
says the lever is width per level (item 14) instead.  This runs the same
measurement over all 20 hand recordings.

    python tools/horizon.py           # run the sweep, resumable
    python tools/horizon.py status    # the table, free, from the report alone

**The report is the working state.**  Every row `--push-seed` writes carries
`hint=push-seed:K`, so a probe that has already been run is a row in
`build/reports/horizon.jsonl` and is never re-run; killing this and starting it
again loses at most the probes that were in flight.  That is the whole of the
resume logic and it is why there is no separate state file to get out of step.

**Two things keep the cost down and the first is most of it.**  K = 0 is the
ordinary root, so a level the arm solves unseeded has a horizon of its whole
recording and needs no bisection at all -- which is most of `LaserTank.lvl`
1-9.  Only the levels that fail from K = 0 are bisected, ~4-6 probes each of
which about half fail, and a failing probe costs the full budget (~4 min at one
job) where a solving one costs seconds to two minutes.

**The budget is not a tuning knob here.**  40M nodes and width 512 are the
numbers level 6's 48 was measured at, and a horizon is only comparable with it
at the same budget -- a cheaper sweep measures a different quantity and answers
the question only by accident.  `PAR` runs several *levels* concurrently, one
job each, which changes wall clock and no measured number; it defaults to 4 so
that a run of `tools/l5_pass.sh` keeps its sixteen.
"""
import argparse
import concurrent.futures
import json
import os
import subprocess
import sys
import threading

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LEVELS = os.path.join(ROOT, "data", "levels", "LaserTank.lvl")
DEMOS = os.path.join(ROOT, "build", "demos.txt")
# HORIZON_DIR exists so a plumbing test cannot poison the sweep's own state: a
# probe run at a throwaway budget is indistinguishable from a real one once it
# is a row in a report, and the resume logic would believe it.
HDIR = os.environ.get("HORIZON_DIR") or os.path.join(ROOT, "build", "horizon")
# **One report file per probe, and this is not tidiness.**  `--report` opens with
# `append: true`, which is not atomic across processes on Windows: two solvers
# that open the same file both write at the offset they saw as EOF, and one row
# silently replaces the other.  The first version of this ran four levels into
# one report and lost two levels' probes that way -- and a *missing* row read as
# a failed probe, which is the worst direction for the error to point, because a
# horizon is the smallest K that solves.  So probes never share a file, a probe
# that leaves no row raises instead of counting as a failure, and the merged
# `build/reports/horizon.jsonl` the docs name is written once, single-threaded,
# at the end.
PDIR = os.path.join(HDIR, "reports")
REPORT = (os.path.join(HDIR, "horizon.jsonl") if os.environ.get("HORIZON_DIR")
          else os.path.join(ROOT, "build", "reports", "horizon.jsonl"))
PROFILE = os.path.join(HDIR, "profile.tsv")
# The board-change count per recording, cached so `status` needs no solver.  It
# is the bisection's upper bound, so `status` inferring it from "the largest K
# probed" is wrong for exactly the levels that solve from the root and were
# never probed above K = 0 -- which printed their board changes as 0.
CHANGES = os.path.join(HDIR, "changes.tsv")
COLLECTION = "LaserTank"

# The arm, and it is the one closed item 15 measured level 6's 48 with.
ARM = ["--push", "--push-read", "--push-reach", "--push-ferry-match",
       "--push-ferry-maze", "--push-dead", "20"]

_print_lock = threading.Lock()


def log(msg):
    with _print_lock:
        sys.stderr.write(msg + "\n")
        sys.stderr.flush()


def solver():
    exe = os.environ.get("LT_SOLVE") or os.path.join(ROOT, "build", "lasertank-solve.exe")
    if not os.path.exists(exe):
        sys.exit("no %s -- run bash src/build.sh, or set LT_SOLVE" % exe)
    return exe


def demos():
    """(level, lpb path) for each recording, read from --lpb-list's own file."""
    out = []
    with open(DEMOS, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            base = os.path.basename(line)
            out.append((int(os.path.splitext(base)[0]), line))
    return out


def board_changes(exe):
    """Board changes per recording, from a profile of the 20.

    Re-derived rather than read from a banked .tsv because it costs 0.4 s and a
    stale count would put the bisection's upper bound in the wrong place.  The
    quantity is `basin.py --events`' own: a keypress that changed `Game.PF`.
    """
    os.makedirs(os.path.dirname(PROFILE), exist_ok=True)
    subprocess.run([exe, "--levels", LEVELS, "--lpb-list", DEMOS,
                    "--profile", PROFILE, "--push-ferry-match", "--quiet"],
                   check=True, capture_output=True)
    out = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "basin.py"),
                          "--per-level", "--events", PROFILE],
                         check=True, capture_output=True, text=True).stdout
    counts = {}
    for line in out.splitlines():
        parts = line.split()
        if len(parts) >= 4 and parts[0] == COLLECTION:
            try:
                counts[int(parts[1])] = int(parts[3])
            except ValueError:
                pass
    with open(CHANGES, "w", encoding="utf-8", newline="\n") as f:
        f.write("level\tchanges\n")
        for lv in sorted(counts):
            f.write("%d\t%d\n" % (lv, counts[lv]))
    return counts


def cached_changes():
    # The counts board_changes banked, so `status` needs no solver.
    out = {}
    if not os.path.exists(CHANGES):
        return out
    with open(CHANGES, encoding="utf-8") as f:
        next(f, None)
        for line in f:
            parts = line.split()
            if len(parts) == 2:
                out[int(parts[0])] = int(parts[1])
    return out


def rows_of(path):
    """The `push-seed` rows of one report, newest last."""
    out = []
    if not os.path.exists(path):
        return out
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue
            hint = r.get("hint") or ""
            if not hint.startswith("push-seed:"):
                continue
            try:
                r["_k"] = int(hint.split(":", 1)[1])
            except ValueError:
                continue
            out.append(r)
    return out


def probes():
    """Every probe already run: (level, K) -> row.

    Last row wins, the same rule `bench.sh` and `report_stats.py` read a report
    by, so a re-run of one probe supersedes it rather than appearing twice.
    """
    done = {}
    for name in sorted(os.listdir(PDIR)) if os.path.isdir(PDIR) else []:
        if not name.endswith(".jsonl"):
            continue
        for r in rows_of(os.path.join(PDIR, name)):
            done[(r["level"], r["_k"])] = r
    return done


def run_probe(exe, level, lpb, k, nodes, budget_ms, width):
    """One probe, into its own report.  Raises if it left no row at all."""
    os.makedirs(PDIR, exist_ok=True)
    rep = os.path.join(PDIR, "L%d-K%d.jsonl" % (level, k))
    out = os.path.join(HDIR, "L%d" % level, "K%d" % k)
    # A probe this script killed on a timeout can still hold its report open for
    # a moment, and deleting it then raises WinError 32.  Rather than wait on the
    # file, count the rows that are already there and require a *new* one: the
    # solver appends, so a stale row can never be mistaken for this run's.
    before = len([r for r in rows_of(rep)
                  if r["level"] == level and r["_k"] == k])
    cmd = [exe, "--levels", LEVELS, "--level", str(level),
           "--push-seed", "%s:%d" % (lpb, k)] + ARM + [
           "--push-beam", str(width), "--max-keys", "5000",
           "--nodes", str(nodes), "--budget-ms", str(budget_ms), "--jobs", "1",
           "--out", out, "--report", rep, "--quiet", "--force"]
    p = subprocess.run(cmd, capture_output=True, text=True)
    got = [r for r in rows_of(rep) if r["level"] == level and r["_k"] == k]
    if len(got) <= before:
        raise RuntimeError(
            "L%d K=%d left no report row (exit %d): %s"
            % (level, k, p.returncode, (p.stderr or p.stdout or "")[-300:]))
    return got[-1]


def sweep_level(exe, level, lpb, events, nodes, budget_ms, width, k0_only=False):
    """K = 0, then bisect only if it failed.

    The invariant the bisection keeps is *fails at `lo`, solves at `hi`*, so it
    needs both ends established before it narrows: `lo` is the failed K = 0 and
    `hi` is the whole recording, which wins by replay in a few keypresses and
    is asserted rather than assumed -- a level that cannot solve from its own
    last board change means the arm cannot finish the recording at all, which
    is a finding about the arm and not a horizon, and it says so and stops.

    Monotonicity is what makes a bisection legal here and item 15 measured it
    on level 6 (every K below 120 fails, every K above solves).  The beam is
    deterministic at a fixed width, so a probe is a function of (level, K).
    """
    done = probes()

    def ask(k):
        if (level, k) in done:
            return bool(done[(level, k)].get("solved"))
        r = run_probe(exe, level, lpb, k, nodes, budget_ms, width)
        done[(level, k)] = r
        ok = bool(r.get("solved"))
        log("  L%-3d K=%-4d %-6s %s %10d nodes" %
            (level, k, "solved" if ok else "failed", r.get("stop"), r.get("nodes", 0)))
        return ok

    if ask(0):
        return ("root", events, 0)
    if k0_only:
        return ("needs-bisect", None, None)

    if not ask(events):
        return ("no-win", None, None)

    lo, hi = 0, events           # fails at lo, solves at hi
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if ask(mid):
            hi = mid
        else:
            lo = mid
    return ("bisected", events - hi, hi)


def merge():
    """Every probe in one file, for `report_stats.py` and for the record.

    Written here rather than by the solvers for the reason `PDIR` exists: this
    is the one place that is single-threaded.
    """
    done = probes()
    os.makedirs(os.path.dirname(REPORT), exist_ok=True)
    with open(REPORT, "w", encoding="utf-8", newline="\n") as f:
        for key in sorted(done):
            r = dict(done[key])
            r.pop("_k", None)
            f.write(json.dumps(r) + "\n")
    return len(done)


def status(counts=None):
    """The table, and the fourth column is the one to read second."""
    done = probes()
    if not done:
        print("no probes yet -- run `python tools/horizon.py`")
        return
    events = dict(counts or cached_changes())
    for (lv, k) in done:                 # a floor, never a correction
        events[lv] = max(events.get(lv, 0), k)
    ok = {k: bool(v.get("solved")) for k, v in done.items()}
    rows = []
    for lv in sorted({lv for lv, _ in done}):
        ks = sorted(k for l, k in done if l == lv)
        solved = sorted(k for k in ks if ok[(lv, k)])
        failed = sorted(k for k in ks if not ok[(lv, k)])
        ev = events.get(lv, max(ks) if ks else 0)
        if ok.get((lv, 0)):
            rows.append((lv, ev, ev, 0, "root", len(ks)))
            continue
        kmin = min(solved) if solved else None
        kmax_fail = max(failed) if failed else None
        settled = (kmin is not None and kmax_fail is not None
                   and kmin - kmax_fail == 1)
        rows.append((lv, ev, (ev - kmin) if kmin is not None else None, kmin,
                     "settled" if settled else "open", len(ks)))
    print("%-5s %8s %9s %6s %8s %8s %7s"
          % ("level", "changes", "horizon", "Kmin", "h/chg", "state", "probes"))
    hs = []
    for lv, ev, h, kmin, state, n in rows:
        frac = "%.2f" % (h / ev) if h is not None and ev else "-"
        print("%-5d %8d %9s %6s %8s %8s %7d"
              % (lv, ev, "-" if h is None else h,
                 "-" if kmin is None else kmin, frac, state, n))
        if h is not None and state in ("root", "settled"):
            hs.append((lv, ev, h))
    if hs:
        vals = sorted(h for _, _, h in hs)
        capped = [(lv, ev, h) for lv, ev, h in hs if h == ev]
        print("\n%d levels settled: horizon min %d, p50 %d, max %d -- spread %.1fx"
              % (len(hs), vals[0], vals[len(vals) // 2], vals[-1],
                 vals[-1] / max(vals[0], 1)))
        print("%d of them solve from the root, so the horizon there is only a "
              "*lower* bound (the whole recording)" % len(capped))
        free = [h for lv, ev, h in hs if h < ev]
        if free:
            fs = sorted(free)
            print("%d are bounded above by the search rather than by the "
                  "recording: min %d, p50 %d, max %d -- spread %.1fx"
                  % (len(fs), fs[0], fs[len(fs) // 2], fs[-1],
                     fs[-1] / max(fs[0], 1)))
    left = [r for r in rows if r[4] == "open"]
    if left:
        print("%d levels still open: %s" % (len(left), [r[0] for r in left]))


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", nargs="?", default="run", choices=["run", "status"])
    ap.add_argument("--nodes", type=int, default=40000000)
    ap.add_argument("--budget-ms", type=int, default=1800000)
    ap.add_argument("--width", type=int, default=512)
    ap.add_argument("--par", type=int, default=int(os.environ.get("PAR", 4)),
                    help="levels swept concurrently, one job each")
    ap.add_argument("--stop-after-k0", action="store_true",
                    help="run phase 1 only; the rest resumes from the reports")
    ap.add_argument("--only", type=str, default="",
                    help="comma-separated levels, for finishing a partial sweep")
    a = ap.parse_args()

    if a.cmd == "status":
        status()
        return

    exe = solver()
    counts = board_changes(exe)
    todo = [(lv, lpb) for lv, lpb in demos() if lv in counts]
    if a.only:
        want = {int(x) for x in a.only.split(",") if x.strip()}
        todo = [(lv, lpb) for lv, lpb in todo if lv in want]
    os.makedirs(os.path.dirname(REPORT), exist_ok=True)

    log("item 18 -- the horizon per level, %d recordings, %d board changes total"
        % (len(todo), sum(counts[lv] for lv, _ in todo)))
    log("%d nodes, width %d, %d levels at a time, one job each"
        % (a.nodes, a.width, a.par))

    # **Phase 1 is every level's K = 0, and it runs before any bisection.**  Not
    # an optimisation: a level the arm solves unseeded has a horizon of its whole
    # recording and needs no bisection, so this is the half of the sweep that
    # prices the other half -- and it is the half that answers the question if
    # the horizons turn out to be capped by the recordings rather than by the
    # search.  Running it first means an interrupted sweep is interrupted with
    # the cheap information in hand rather than half a bisection of level 6.
    def k0(lv, lpb):
        return lv, sweep_level(exe, lv, lpb, counts[lv], a.nodes,
                               a.budget_ms, a.width, k0_only=True)

    log("phase 1 -- K=0 on all %d, the control the sweep is read against" % len(todo))
    errs = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=a.par) as ex:
        for fut in concurrent.futures.as_completed(
                [ex.submit(k0, lv, lpb) for lv, lpb in todo]):
            try:
                fut.result()
            except Exception as e:
                log("  ERROR %s" % e)
                errs += 1

    # **The work list comes from the banked probes, never from this run's own
    # bookkeeping.**  An interrupted invocation leaves rows that a later one
    # reads from cache, and a probe that raised here still banked its row in the
    # run before -- the first version tallied what *this* process classified and
    # silently dropped three levels that were already measured and failing.
    # Same defect as the shared report file, one layer up: the report is the
    # state, so ask the report.
    done = probes()
    roots = [lv for lv, _ in todo if done.get((lv, 0), {}).get("solved")]
    bisect = [lv for lv, _ in todo
              if (lv, 0) in done and not done[(lv, 0)].get("solved")]
    missing = [lv for lv, _ in todo if (lv, 0) not in done]
    log("phase 1: %d solve from the root, %d need bisecting (%s)%s"
        % (len(roots), len(bisect), sorted(bisect),
           ", %d K=0 probes still missing %s -- re-run to finish phase 1"
           % (len(missing), sorted(missing)) if missing else ""))
    if errs:
        log("  (%d probes were interrupted or failed to run; they are not "
            "counted as failures)" % errs)
    merge()

    if a.stop_after_k0:
        log("--stop-after-k0: stopping with phase 1 banked and resumable")
        print()
        status(counts)
        return

    lpbs = dict(todo)
    log("phase 2 -- bisect the %d that failed, ~4-6 probes each" % len(bisect))
    with concurrent.futures.ThreadPoolExecutor(max_workers=a.par) as ex:
        futs = {ex.submit(sweep_level, exe, lv, lpbs[lv], counts[lv],
                          a.nodes, a.budget_ms, a.width): lv
                for lv in sorted(bisect, key=lambda l: -counts[l])}
        for fut in concurrent.futures.as_completed(futs):
            lv = futs[fut]
            try:
                kind, h, kmin = fut.result()
            except Exception as e:              # a probe that could not run
                log("  L%-3d ERROR %s" % (lv, e))
                continue
            log("L%-3d %s horizon=%s" % (lv, kind, h))

    n = merge()
    log("%d probes merged into %s" % (n, os.path.relpath(REPORT, ROOT)))
    print()
    status(counts)


if __name__ == "__main__":
    main()
