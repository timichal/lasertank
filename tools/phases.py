#!/usr/bin/env python3
"""Item 5 -- where a hand line's phase boundaries are, and how long the phases are.

Item 18 measured the **horizon**: the board changes of *suffix* the push beam
can close, 2 to 50 over the 12 recordings it cannot solve unseeded.  It could
not say whether a decomposition is possible, because a horizon is a statement
about the end of a line and a decomposition is a statement about all of it.
This is the other half and it costs no search at all: cut each hand line at its
**milestones** and report the segment lengths against that level's horizon.

    python tools/phases.py            # the table
    python tools/phases.py --verbose  # ...plus every milestone, per level

**A milestone is a board change that the game cannot undo.**  Every board change
in a recording is one of two kinds and the distinction is a census, not a model:
a push *moves* an object (a block leaves one cell and arrives at another, an
anti-tank shoved back, a mirror rearranged, a rotary mirror turned) and the
count of that object on the board is unchanged; a fill, a shot brick, a
destroyed mirror, a destroyed anti-tank and a used thin ice *consume* one and
the count drops.  So the rule is: some counted object is strictly rarer after
the change than before.  No list of "important" cells, no threshold, and it is
exact rather than derived -- the same kind of test as `--push-fire-tier`'s.

Two things it deliberately does not count.  **Conveyors, ice, tunnels and dirt
are underlays** -- a block pushed onto a conveyor reads as `conveyor->block` and
the conveyor comes back when the block leaves, so a census over them reports a
milestone for an ordinary push.  And **the flag is not a milestone**: reaching
it ends the line rather than dividing it.

**What the segment lengths are for.**  Item 5 proposes to search for one phase
at a time and commit, so the question that decides whether that can work is
whether the phases are inside the reach the searcher already has.  `max seg` vs
`horizon` is that question, and the column to read is `max/hor`: below 1.0 says
every phase of the human's own line is within a reach already measured on that
level, above 1.0 says at least one phase is not and the decomposition would
need a finer cut than its milestones.

The horizon comes from item 18's own report via `tools/horizon.py`, so this
never re-runs a probe and never disagrees with that table.
"""
import argparse
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LEVELS = os.path.join(ROOT, "data", "levels", "LaserTank.lvl")
DEMOS = os.path.join(ROOT, "build", "demos.txt")

sys.path.insert(0, os.path.join(ROOT, "tools"))
import horizon  # noqa: E402  -- for probes()/cached_changes(), never for a run

# The objects whose disappearance is progress.  Anti-tanks are one entry with
# four spellings on purpose: `--push-line` names them by facing, and an
# anti-tank pushed back a cell would read as one kind lost and another gained
# if the four were counted apart.  Mirrors and rotary mirrors are the same case.
COUNTED = {
    "block": "block",
    "bricks": "bricks",
    "water": "water",
    "mirror": "mirror",
    "roto": "roto",
    "crystal": "crystal",
    "anti-tank^": "anti-tank", "anti-tank>": "anti-tank",
    "anti-tankv": "anti-tank", "anti-tank<": "anti-tank",
}

# One changed cell: `(x,y) was->now`, anchored at the start of its field.
#
# **The row's `tank(x,y)` prefix has to be cut off before this runs**, and the
# first version of this file did not do it: an unanchored search let `.+?` run
# from `tank(13,12)` across the following `(13,11) ` and read the *pose* as the
# changed cell, so the first transition on every row was lost.  Level 6 still
# read correctly by luck -- its fills are the second cell on their row -- and
# every DEMOLITION read as having no milestone at all, which is the shape of
# error that looks like a finding.  Fields are separated by two spaces.
CHANGE = re.compile(r"^\((\d+),(\d+)\) (.+?)->(.+)$")
POSE = re.compile(r"tank\(\d+,\d+\) ")
HEAD = re.compile(r"level (\d+): (\d+) keypresses, (\d+) board changes")


def line_dump(exe, level, lpb):
    """`--push-line` with no budget: the replay prints, the search does not run.

    One node rather than zero because zero is "no budget" and the run then
    reports a stopped search rather than a dumped line; the line is printed
    before the beam starts either way, so the cost is one expansion.
    """
    out = subprocess.run(
        [exe, "--levels", LEVELS, "--level", str(level), "--push-line", lpb,
         "--nodes", "1", "--budget-ms", "5000", "--jobs", "1"],
        capture_output=True, text=True)
    if HEAD.search(out.stdout) is None:
        raise RuntimeError("level %d: --push-line printed no line\n%s"
                           % (level, out.stderr.strip()[:400]))
    return out.stdout


def milestones(dump):
    """(changes, [(index, what-was-lost)]) for one line dump.

    `index` is the board-change number the `--push-line` row carries, i.e. the
    same unit `--push-seed K` counts in, so a milestone at 18 and a horizon of
    50 are directly comparable.
    """
    total = int(HEAD.search(dump).group(3))
    marks = []
    for row in dump.splitlines():
        row = row.strip()
        if not row.startswith("line d="):
            continue
        # `line d=  1 h=83968  fire  tank(10,2) (11,2) block->dirt ...`
        d = int(row.split("d=", 1)[1].split(None, 1)[0])
        if d == 0:
            continue                      # the root row, and the CUT report's
        cut = POSE.split(row, 1)
        if len(cut) != 2:
            raise RuntimeError("no tank pose in --push-line row: " + row)
        cells = []
        for field in re.split(r"\s{2,}", cut[1].strip()):
            m = CHANGE.match(field.strip())
            if m is None:
                raise RuntimeError("unparsed --push-line field %r in: %s"
                                   % (field, row))
            cells.append(m.groups())
        net = {}
        for _x, _y, was, now in cells:
            if was in COUNTED:
                net[COUNTED[was]] = net.get(COUNTED[was], 0) - 1
            if now in COUNTED:
                net[COUNTED[now]] = net.get(COUNTED[now], 0) + 1
            # Thin ice is consumed into water rather than removed, so the
            # census cannot see it: one cell of water more where ice was.
            if now == "water" and was in ("ice", "thin ice"):
                net.setdefault("thin ice", 0)
                net["thin ice"] -= 1
        lost = sorted(k for k, v in net.items() if v < 0)
        if lost:
            marks.append((d, "+".join(lost)))
    return total, marks


def segments(total, marks):
    """Phase lengths: the gaps between milestones, and the tail after the last.

    **The tail is a phase too, and the first version of this counted it apart.**
    A line that ends *on* a milestone has none -- `LaserTank.lvl` 6's last board
    change is its sixth fill -- but where there is one it is the run from the
    last thing consumed to the win, and a decomposition has to search it like
    any other segment.  Leaving it out of `max` said level 9 decomposes into a
    7-change phase when what it really needs is a 20-change one it has a horizon
    of 2 for.  So it is returned separately, shown in its own column, and
    counted in the maximum.
    """
    segs, prev = [], 0
    for d, _what in marks:
        segs.append(d - prev)
        prev = d
    tail = total - prev
    return segs, tail


def demos():
    with open(DEMOS, encoding="utf-8") as f:
        return [l.strip() for l in f if l.strip() and not l.startswith("#")]


def horizons():
    """Item 18's table, read from its report -- level -> (changes, horizon)."""
    done = horizon.probes()
    events = dict(horizon.cached_changes())
    for (lv, k) in done:
        events[lv] = max(events.get(lv, 0), k)
    ok = {k: bool(v.get("solved")) for k, v in done.items()}
    out = {}
    for lv in sorted({lv for lv, _ in done}):
        ev = events.get(lv, 0)
        if ok.get((lv, 0)):
            out[lv] = (ev, ev, True)      # solves unseeded: horizon >= the line
            continue
        solved = [k for (l, k) in done if l == lv and ok[(l, k)]]
        out[lv] = ((ev, ev - min(solved), False) if solved else (ev, None, False))
    return out


def main():
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--verbose", action="store_true",
                    help="print every milestone of every level")
    ap.add_argument("--exe", default=os.environ.get("LT_SOLVE")
                    or os.path.join(ROOT, "build", "lasertank-solve.exe"))
    a = ap.parse_args()

    hor = horizons()
    print("%-5s %8s %6s %-22s %5s %5s %8s %7s"
          % ("level", "changes", "phases", "segments", "max", "hor", "max/hor", "tail"))
    rows = []
    for lpb in demos():
        level = int(os.path.splitext(os.path.basename(lpb))[0])
        total, marks = milestones(line_dump(a.exe, level, lpb))
        segs, tail = segments(total, marks)
        _ev, h, rooted = hor.get(level, (total, None, False))
        allsegs = segs + ([tail] if tail else [])
        mx = max(allsegs) if allsegs else total
        ratio = "-" if not h else "%.2f" % (mx / h)
        shown = ",".join(str(s) for s in segs)
        if len(shown) > 22:
            shown = shown[:19] + "..."
        print("%-5d %8d %6d %-22s %5d %5s %8s %7d"
              % (level, total, len(segs), shown, mx,
                 ("%d%s" % (h, "+" if rooted else "")) if h else "-", ratio, tail))
        rows.append((level, total, segs, tail, h, rooted, marks))
        if a.verbose:
            for d, what in marks:
                print("        d=%-4d %s" % (d, what))

    # The two populations are different findings and averaging them hides both:
    # a level the rule cuts is one the decomposition has something to say about,
    # and a level it does not cut is one it is *empty* on however good the
    # numbers look elsewhere.
    cut = [r for r in rows if r[2]]
    def worst(r):                      # every phase, the tail among them
        return max(r[2] + ([r[3]] if r[3] else []) or [r[1]])
    uncut = [r for r in rows if not r[2]]
    sized = [r for r in cut if r[4]]
    fit = [r for r in sized if worst(r) <= r[4]]
    print("\ncut: %d of %d recordings have at least one milestone; the rule is "
          "empty on %d (%s)"
          % (len(cut), len(rows), len(uncut), ",".join(str(r[0]) for r in uncut)))
    print("fit: %d of the %d it cuts have every phase inside their own measured "
          "horizon; over: %s"
          % (len(fit), len(sized),
             ", ".join("%d (%d vs %d)" % (r[0], worst(r), r[4])
                       for r in sized if worst(r) > r[4]) or "none"))
    print("a `+` on the horizon means the level solves unseeded, so the number "
          "is a lower bound (item 18)")
    tails = [r for r in cut if r[4] and r[3] > r[4]]
    if tails:
        print("of those, the phase that is over is the *tail* -- the drive to "
              "the flag after the last milestone: %s"
              % ", ".join("%d (%d vs %d)" % (r[0], r[3], r[4]) for r in tails))


if __name__ == "__main__":
    main()
