#!/usr/bin/env python3
"""Item 5's falsifier: can the search walk *one* phase of a line it is given?

`tools/phases.py` cuts a hand line at its milestones and shows the phases are
short -- `LaserTank.lvl` 6 is six phases of 18 to 34 board changes against a
horizon of 50.  That is a statement about the *line*.  This is the statement
about the *search*, and it is the one that decides whether `--push-phases` can
work: seed at each milestone and ask whether the beam reaches the next one.

    python tools/phase_reach.py 6              # the six phases of level 6
    python tools/phase_reach.py 6 --width 128  # ...at another width
    python tools/phase_reach.py 6 status       # the table, free, from the report

**Why this is not item 18 again.**  `tools/horizon.py` measures how many board
changes of *suffix* the search can close: it seeds at K and asks for a **win**.
Every probe it runs therefore ends at the flag, and the boards it measures are
the ones near the end of the line -- where a Sokoban has the fewest blocks and
the fewest holes left.  This seeds at K and asks only for the **next
milestone**, so it measures the middle of the line as well, and the middle is
where a decomposition has to live.  Where the two overlap they must agree: the
last phase of a line *is* a suffix.

**A probe is a real run of the real chain**, `--push-phases --push-trace` with
the seed, and the answer is read off the trace's own commit line rather than
off a win.  So a YES here is the searcher actually doing the thing the flag
does, not a proxy for it.

Every row is `hint=push-seed:K` like item 18's, so none of it touches the
solver's rate.
"""
import argparse
import io
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LEVELS = os.path.join(ROOT, "data", "levels", "LaserTank.lvl")
DEMOS = os.path.join(ROOT, "data", "demos", "LaserTank")
COLLECTION = "LaserTank"

sys.path.insert(0, os.path.join(ROOT, "tools"))
import phases as ph  # noqa: E402  -- the milestone rule, defined once

# The same arm tools/horizon.py probes with, so a phase-reach number and a
# horizon number are the same searcher.  --push-phases is what turns it into
# a chain; --push-trace is how the commit is read.
ARM = ["--push", "--push-read", "--push-reach", "--push-ferry-match",
       "--push-ferry-maze", "--push-dead", "20"]

PDIR_NAME = "phase-reach"
COMMIT = re.compile(r"phase (\d+) committed: census (\d+) -> (\d+), "
                    r"keys (\d+), nodes (\d+)")


def outdir():
    d = os.environ.get("PHASE_REACH_DIR") or os.path.join(ROOT, "build", PDIR_NAME)
    os.makedirs(d, exist_ok=True)
    return d


def report_path():
    return (os.path.join(outdir(), "phase-reach.jsonl")
            if os.environ.get("PHASE_REACH_DIR")
            else os.path.join(ROOT, "build", "reports", "phase-reach.jsonl"))


def load(path):
    rows = []
    if not os.path.exists(path):
        return rows
    with io.open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if line:
                try:
                    rows.append(json.loads(line))
                except json.JSONDecodeError:
                    pass
    return rows


def banked(level, width, nodes):
    """Probes already run at this (level, width, nodes) -- K -> row.

    Keyed on the budget as well as on K for horizon.py's reason: a probe run at
    a throwaway budget is indistinguishable from a real one once it is a row,
    and a resume that believed it would report a reach the search does not have.
    """
    out = {}
    for r in load(report_path()):
        if (r.get("level") == level and r.get("width") == width
                and r.get("nodes_budget") == nodes):
            out[r["k"]] = r
    return out


def append(row):
    p = report_path()
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with io.open(p, "a", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(row) + "\n")


def probe(exe, level, lpb, k, width, nodes, budget_ms):
    """One phase: seed at K, run the chain, read the first commit off the trace.

    `--push-phases` ends a phase at the depth that produces a milestone, so the
    *first* commit line is the answer and anything after it is the next phase's
    business.  A run that commits nothing spent its whole budget looking.

    **Two ways this lied before it raised, and they are item 18's harness bugs
    arriving again in a new file.**  A solver built before `--push-phases`
    existed rejects the flag, and the run then leaves no commit line -- which
    read as *"phase not reached"*, the worst direction for the error to point,
    because a miss is this table's finding.  And the per-probe report is named
    for (level, width, K) but not for the *budget*, so a cheap re-probe read the
    previous run's row and reported its `solved`.  So: the row count is taken
    before the run and a new row is required after it, only that new row is
    read, and a non-zero exit raises with the solver's own stderr.
    """
    d = outdir()
    out = os.path.join(d, "L%d-W%d-K%d" % (level, width, k))
    rep = os.path.join(d, "L%d-W%d-K%d.jsonl" % (level, width, k))
    cmd = [exe, "--levels", LEVELS, "--level", str(level)]
    if k > 0:
        cmd += ["--push-seed", "%s:%d" % (lpb, k)]
    cmd += ARM + ["--push-phases", "--push-trace",
                  "--push-beam", str(width), "--max-keys", "5000",
                  "--nodes", str(nodes), "--budget-ms", str(budget_ms),
                  "--jobs", "1", "--out", out, "--report", rep,
                  "--quiet", "--force"]
    before = len([r for r in load(rep) if r.get("level") == level])
    p = subprocess.run(cmd, capture_output=True, text=True)
    if p.returncode != 0:
        raise RuntimeError(
            "L%d W%d K=%d: solver exited %d.  If this solver predates "
            "--push-phases, rebuild: bash src/build.sh.  It said: %s"
            % (level, width, k, p.returncode,
               # The *head* of stderr, not the tail: a run that fails mid-search
               # still exits 0 and leaves a row (the next check catches that),
               # so a non-zero exit here is an argument or setup error, and the
               # solver prints that first and its whole help after it.
               (p.stderr or p.stdout or "").strip()[:300]))
    rows = [r for r in load(rep) if r.get("level") == level]
    if len(rows) <= before:
        raise RuntimeError(
            "L%d W%d K=%d left no report row -- the run did nothing.  If this "
            "solver predates --push-phases, rebuild: bash src/build.sh"
            % (level, width, k))
    m = COMMIT.search(p.stderr or "")
    solved = bool(rows[-1].get("solved"))
    return {
        "collection": COLLECTION, "level": level, "k": k, "width": width,
        "nodes_budget": nodes,
        "reached": m is not None,
        "cost": int(m.group(5)) if m else None,
        "census_from": int(m.group(2)) if m else None,
        "census_to": int(m.group(3)) if m else None,
        "solved": solved,
        "hint": "push-seed:%d" % k,
    }


def milestones_of(exe, level):
    lpb = os.path.join(DEMOS, "%05d.lpb" % level)
    if not os.path.exists(lpb):
        raise SystemExit("no hand recording for level %d (%s)" % (level, lpb))
    total, marks = ph.milestones(ph.line_dump(exe, level, lpb))
    return lpb, total, marks


def table(level, width, nodes, total, marks):
    done = banked(level, width, nodes)
    ks = [0] + [d for d, _w in marks]
    print("%-6s %-6s %-9s %-9s %-11s %s"
          % ("phase", "seed K", "target", "length", "reached", "cost (nodes)"))
    for i, k in enumerate(ks):
        if i >= len(marks):
            break
        tgt, what = marks[i]
        r = done.get(k)
        state = "-" if r is None else ("YES" if r["reached"] else "no")
        cost = "" if not r or not r["reached"] else "{:,}".format(r["cost"])
        print("%-6d %-6d %-9d %-9d %-11s %s"
              % (i + 1, k, tgt, tgt - k, state, cost))
    have = [done[k] for k in ks[:len(marks)] if k in done]
    if have:
        got = [r for r in have if r["reached"]]
        print("\n%d of %d phases probed, **%d reached** at width %d on %s nodes"
              % (len(have), len(marks), len(got), width, "{:,}".format(nodes)))
        if got:
            print("reached phases cost %s to %s nodes"
                  % ("{:,}".format(min(r["cost"] for r in got)),
                     "{:,}".format(max(r["cost"] for r in got))))
        miss = [r["k"] for r in have if not r["reached"]]
        if miss:
            print("not reached from K = %s -- and a phase the search cannot "
                  "walk is one --push-phases cannot chain"
                  % ", ".join(str(k) for k in miss))


def main():
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("level", type=int)
    ap.add_argument("cmd", nargs="?", default="run", choices=["run", "status"])
    ap.add_argument("--width", type=int, default=32)
    ap.add_argument("--nodes", type=int, default=8000000)
    ap.add_argument("--budget-ms", type=int, default=600000)
    ap.add_argument("--exe", default=os.environ.get("LT_SOLVE")
                    or os.path.join(ROOT, "build", "lasertank-solve.exe"))
    a = ap.parse_args()

    lpb, total, marks = milestones_of(a.exe, a.level)
    if not marks:
        raise SystemExit(
            "level %d's recording has no milestone -- nothing to decompose, and "
            "--push-phases runs it as one phase (tools/phases.py)" % a.level)
    print("level %d: %d board changes, %d milestones at %s"
          % (a.level, total, len(marks), ", ".join(str(d) for d, _ in marks)))

    if a.cmd == "run":
        done = banked(a.level, a.width, a.nodes)
        ks = [0] + [d for d, _w in marks][:-1]
        for i, k in enumerate(ks):
            if k in done:
                continue
            tgt = marks[i][0]
            print("  phase %d: seed K=%d, target %d (%d changes) ..."
                  % (i + 1, k, tgt, tgt - k), flush=True)
            row = probe(a.exe, a.level, lpb, k, a.width, a.nodes, a.budget_ms)
            append(row)
            print("    %s%s" % ("reached at %s nodes" % "{:,}".format(row["cost"])
                                if row["reached"] else "NOT reached",
                                " (and solved the level)" if row["solved"] else ""),
                  flush=True)
        print()
    table(a.level, a.width, a.nodes, total, marks)


if __name__ == "__main__":
    main()
