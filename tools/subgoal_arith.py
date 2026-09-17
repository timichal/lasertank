#!/usr/bin/env python3
"""Item 22 -- the arithmetic the subgoal chain has to survive before it is built.

Item 22 proposes layer 2's decomposition one level out: an **outer** search over
the *order* of the subgoals the read names, and an **inner** sub-search for one
subgoal at a time, accepted by a board test (`Subgoal.Offer`, `Subgoal.cs:344`).
Its sizing is one line of multiplication --

    width 64  x  depth <= 6  x  ~5,100 nodes per expansion  ~=  2M nodes

-- and ten of those is 20M, a budget one round of the driver already spends.
The item wrote its own refusal against that line: *a read that names materially
more subgoals than that, or a hand line whose subgoals run deeper than ~6 board
changes each, turns 20M into a number the driver does not have.*

Both halves are free, so this runs them before anything is built.  The count
comes from `--analyze`; the depths come from replaying the hand line with
`--push-line` and cutting it by **the item's own acceptance tests**, which are
different on the two verdicts and are quoted here from the item:

  SOKOBAN   *block b stands on the next cell of its maze path*.  The carried
            block is identified backwards from the fill -- the block that goes
            into the hole, then whatever push put it where it was, and so on --
            so a `carry` is one subgoal and its `advancing` count is how much of
            it the test can actually name.
  GAUNTLET  *the safe flood gained a named route cell*.  The flood is not
            printed per depth and `--analyze` reads the authored board however
            it is seeded, so the cut used here is the coarser thing the flood is
            a proxy for: a `run` is a maximal stretch of consecutive changes
            spent on **one** anti-tank.  That is generous to the item -- it can
            only make the subgoals look shorter and more numerous than a flood
            test would -- which is the right direction for a falsifier.

    python tools/subgoal_arith.py            # the two levels, the two tables
    python tools/subgoal_arith.py --verbose  # ...plus every setup change

**What the setup/interleave columns are for.**  A count and a depth refuse the
item on cost.  The two columns beside them say whether the *shape* survives even
where the cost does: `setup` counts the changes inside a carry that advance no
block toward any hole, and the GAUNTLET table's repeated `at#` says whether the
human finishes one subgoal before starting the next.  An outer search over
orderings cannot produce an interleaved line, and a board test cannot name a
change that leaves the board where it found it.
"""

import argparse
import os
import re
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LEVELS = os.path.join(ROOT, "data", "levels", "LaserTank.lvl")
DEMO = os.path.join(ROOT, "data", "demos", "LaserTank", "%05d.lpb")

WIDTH, PER_EXPANSION, SIZED_DEPTH = 64, 5100, 6   # the item's own three numbers

HEAD = re.compile(r"level (\d+): (\d+) keypresses, (\d+) board changes")
ROW = re.compile(r"^line d=\s*(\d+)\s+h=\d+\s+\S+\s+tank\(\d+,\d+\)\s+(.*)$")
CELL = re.compile(r"\((\d+),(\d+)\) (\S+)->(\S+)")


def run(args):
    return subprocess.run(args, capture_output=True, text=True).stdout


def line_dump(exe, level):
    """`--push-line` with one node: the replay prints, the search does not run.

    The same instrument and the same budget `tools/phases.py` uses, so the
    depths here and that file's segment lengths are the same unit -- the board
    change number, which is also what `--push-seed K` counts in.
    """
    out = run([exe, "--levels", LEVELS, "--level", str(level),
               "--push-line", DEMO % level, "--nodes", "1",
               "--budget-ms", "5000", "--jobs", "1"])
    if HEAD.search(out) is None:
        raise RuntimeError("level %d: --push-line printed no line" % level)
    rows = []
    for ln in out.splitlines():
        m = ROW.match(ln.strip())
        if not m or int(m.group(1)) == 0:
            continue
        rows.append((int(m.group(1)),
                     [((int(x), int(y)), a, b)
                      for x, y, a, b in CELL.findall(m.group(2))]))
    return rows


def read_verdict(exe, level):
    """The read's own verdict line, and the subgoal count it names."""
    out = run([exe, "--levels", LEVELS, "--level", str(level), "--analyze"])
    verdict = ""
    for ln in out.splitlines():
        s = ln.strip()
        if s.startswith("verdict"):
            verdict = s.split(None, 1)[1]
            break
    # SOKOBAN xN names N; GAUNTLET names the anti-tanks that cover the route.
    m = re.search(r"SOKOBAN x(\d+)", verdict)
    if m:
        return verdict, int(m.group(1))
    m = re.search(r"(\d+) anti-tanks? cover", verdict)
    return verdict, (int(m.group(1)) if m else 0)


def carries(rows):
    """SOKOBAN: (changes, advancing, [(d, from, to)]) per carry.

    A carry ends at a fill.  The carried block is walked backwards from the cell
    that went into the hole, so `advancing` is exactly the set of changes the
    item's acceptance test can name and the returned list is the remainder.
    """
    out, cur = [], []
    for d, cells in rows:
        cur.append((d, cells))
        if any(a == "water" for _, a, _ in cells):
            src = [c for c, a, b in cur[-1][1] if a == "block"][0]
            chain, p = set(), src
            for dd, cc in reversed(cur[:-1]):
                s = [c for c, a, b in cc if a == "block"]
                t = [c for c, a, b in cc if b == "block"]
                if t and t[0] == p:
                    chain.add(dd)
                    p = s[0]
            setup = [(dd, [c for c, a, b in cc if a == "block"][0],
                      [c for c, a, b in cc if b == "block"][0])
                     for dd, cc in cur[:-1] if dd not in chain]
            out.append((len(cur), len(chain) + 1, setup))
            cur = []
    return out


def runs(rows):
    """GAUNTLET: [[at#, first d, last d, changes]], one entry per stretch.

    Anti-tanks have no identity in the dump, so they are tracked by position:
    the one that left a cell is the one that arrived at the next.  A shot that
    only shoves is not a milestone -- `tools/phases.py` cuts this level into
    nothing at all -- which is why the cut has to come from the item's test.
    """
    ident, seq, out, cur = {}, 0, [], None
    for d, cells in rows:
        s = [c for c, a, b in cells if a.startswith("anti-tank") and b == "dirt"]
        t = [c for c, a, b in cells if b.startswith("anti-tank")]
        if len(s) != 1 or len(t) != 1:
            continue
        if s[0] not in ident:
            seq += 1
            ident[s[0]] = seq
        i = ident.pop(s[0])
        ident[t[0]] = i
        if cur and cur[0] == i:
            cur[2], cur[3] = d, cur[3] + 1
        else:
            if cur:
                out.append(cur)
            cur = [i, d, d, 1]
    if cur:
        out.append(cur)
    return out, seq


def cost(depth):
    return WIDTH * depth * PER_EXPANSION / 1e6


def sokoban(rows, verbose):
    cs = carries(rows)
    print("  %-7s %8s %10s %7s %11s"
          % ("carry", "changes", "advancing", "setup", "sub-search"))
    for i, (n, adv, setup) in enumerate(cs, 1):
        print("  %-7d %8d %10d %7d %10.1fM" % (i, n, adv, len(setup), cost(n)))
        if verbose:
            for dd, s, t in setup:
                print("          setup d=%-4d %s -> %s" % (dd, s, t))
    su = sum(len(s) for _, _, s in cs)
    pairs = 0
    for _, _, setup in cs:
        held = []
        for _dd, s, t in setup:
            m = [j for j, (es, et) in enumerate(held) if (es, et) == (t, s)]
            if m:
                pairs += 2
                held.pop(m[0])
            else:
                held.append((s, t))
    print("  the test names %d subgoals, one a carry; %d of %d changes advance "
          "no block toward any hole" % (len(cs), su, len(rows)))
    print("  %d of those %d are exact there-and-back pairs on a block that is "
          "not the one being carried -- board-identical, so no board test names "
          "either half" % (pairs, su))
    return [n for n, _, _ in cs]


def gauntlet(rows, named):
    rs, distinct = runs(rows)
    print("  %-7s %-9s %8s %11s" % ("at#", "depths", "changes", "sub-search"))
    for i, f, l, n in rs:
        print("  %-7d %-9s %8d %10.1fM" % (i, "%d-%d" % (f, l), n, cost(n)))
    per = {}
    for i, _, _, n in rs:
        per[i] = per.get(i, 0) + n
    again = sorted(i for i in per if sum(1 for r in rs if r[0] == i) > 1)
    print("  %d of the %d named anti-tanks are touched, in %d stretches; %d are "
          "returned to after another is started (%s)"
          % (distinct, named, len(rs), len(again),
             ", ".join("#%d x%d" % (i, sum(1 for r in rs if r[0] == i))
                       for i in again)))
    print("  per anti-tank, total changes spent on it: %s"
          % ", ".join("#%d:%d" % (k, v) for k, v in sorted(per.items())))
    return sorted(per.values(), reverse=True)


def main():
    ap = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--verbose", action="store_true",
                    help="print every setup change, per carry")
    ap.add_argument("--exe", default=os.environ.get("LT_SOLVE")
                    or os.path.join(ROOT, "build", "lasertank-solve.exe"))
    a = ap.parse_args()

    print("the item's sizing: width %d x depth %d x %d nodes = %.2fM a subgoal, "
          "ten of them 20M\n"
          % (WIDTH, SIZED_DEPTH, PER_EXPANSION, cost(SIZED_DEPTH)))

    over = []
    for level in (6, 10):
        verdict, named = read_verdict(a.exe, level)
        rows = line_dump(a.exe, level)
        print("LaserTank %d -- %s" % (level, verdict))
        print("  the read names %d subgoals; the hand line is %d board changes"
              % (named, len(rows)))
        depths = (sokoban(rows, a.verbose) if verdict.startswith("SOKOBAN")
                  else gauntlet(rows, named))
        deep = sorted((d for d in depths if d > SIZED_DEPTH), reverse=True)
        print("  one ordering costs %.1fM nodes; the item budgeted 20M and the "
              "driver's cap is 40M" % cost(len(rows)))
        print("  %d of %d subgoals run deeper than %d board changes: %s\n"
              % (len(deep), len(depths), SIZED_DEPTH,
                 ", ".join(str(d) for d in deep) or "none"))
        if deep:
            over.append(level)

    print("REFUSED on %s -- the item's own second condition, a hand line whose "
          "subgoals run deeper than ~%d board changes each."
          % (" and ".join("level %d" % l for l in over), SIZED_DEPTH)
          if over else "the sizing survives both levels.")


if __name__ == "__main__":
    main()
