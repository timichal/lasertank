#!/usr/bin/env python3
"""The solution ledger: one line per level, what solved it and at which pass.

The .jsonl the driver appends under --report is the state -- append-only, last
row per level wins, safe to interrupt.  It is not, however, a thing anybody
reads: 20,914 rows of json with a ten-element `attempts` array on each is a
file for a program.  This renders it as the file a person opens a year later
and asks the only question that matters by then: *what is still unsolved, and
what has already been tried on it?*

    python tools/ledger.py data/reports/solutions.jsonl                  # the table
    python tools/ledger.py data/reports/solutions.jsonl --stops          # why they fail
    python tools/ledger.py data/reports/solutions.jsonl --ladder         # the settings
    python tools/ledger.py data/reports/solutions.jsonl \
        --solutions SOLUTIONS.md                        # the whole document

`--solutions` is the one tools/iteration.sh runs, after every collection so an
interrupted pass still leaves the file current.  It writes a table per level
file listing *every* level in it -- including the ones the ledger has never
seen, because "not attempted" and "attempted and failed" are different answers
and a pass that stopped early needs to say which is which.

`--stops` is the one that picks the next approach.  It counts the last round's
stop reason per rung over the unsolved levels, which is the question closed
item 20 had to re-run a whole pass to answer ("177 rows stopped on push-depth")
and which this file now answers for free.

Stdlib only, like everything else in tools/.  Reads utf-8-sig because a report
written by an older solver build starts with a BOM.
"""
import argparse
import json
import pathlib
import struct
import sys
from collections import Counter, defaultdict

TIERS = {1: "Kids", 2: "Easy", 4: "Medium", 8: "Hard", 16: "Deadly", 0: "unrated"}

# A .lvl is a flat array of 576-byte records -- see tools/dump_level.py, which
# is where these offsets are documented.  Read here so the document can list a
# level the ledger has never seen: *not attempted* and *attempted and failed*
# are different answers, and a file that cannot tell them apart cannot say how
# far an iteration actually got.
REC = 576


def collection_levels(lvl_path):
    """[(level, name, difficulty)] for every level in a .lvl, or [] if absent."""
    try:
        data = pathlib.Path(lvl_path).read_bytes()
    except OSError:
        return []
    out = []
    for i in range(len(data) // REC):
        rec = data[i * REC:(i + 1) * REC]
        out.append((i + 1,
                    rec[256:287].split(b"\0")[0].decode("latin1").strip(),
                    struct.unpack_from("<H", rec, 574)[0]))
    return out


def load(path):
    """Last line wins -- the same rule report_stats.load reads by, and the same
    rule the solver's own ledger skip applies.  A line that does not parse is
    skipped rather than raised on: a truncated last line is what an interrupted
    append looks like, and it must not stop the file being readable."""
    rows, runs, bad = {}, [], 0
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                bad += 1
                continue
            # The driver's header row: one per run under --iteration, carrying
            # the ladder's tuning at every round (Auto.Header).  It has no
            # level, which is how every reader of a report skips it.
            if "level" not in r or "collection" not in r:
                runs.append(r)
                continue
            rows[(r["collection"], r["level"])] = r
    return rows, runs, bad


def ordinal(n):
    if n <= 0:
        return "an unnumbered run"
    if 10 <= n % 100 <= 20:
        return f"the {n}th iteration"
    return f"the {n}{ {1: 'st', 2: 'nd', 3: 'rd'}.get(n % 10, 'th') } iteration"


def settings(r):
    """What the row says the search was.  `config` is the run's flags and
    `rung` is what Ladder set on top of them for the searcher that won --
    both, because neither alone reproduces the command (Program.ConfigString).
    """
    parts = [p for p in (r.get("config"), r.get("rung")) if p]
    return "  ".join(parts) if parts else "default settings"


def why(r):
    """The unsolved level's own words: the rungs of its last round, worst news
    first.  Without this a ledger of 13,000 failures says `budget` 13,000 times
    and names no lever."""
    ats = r.get("attempts") or []
    if not ats:
        return r.get("stop", "-")
    stops = Counter(a.get("stop", "-") for a in ats)
    return ", ".join(f"{n}x {s}" for s, n in stops.most_common())


def lines(rows):
    for (coll, lv), r in sorted(rows.items()):
        name = (r.get("name") or "").strip()
        head = f"{coll} {lv:>5}  {name[:28]:<28}"
        it = ordinal(r.get("iteration", 0))
        if r.get("solved"):
            yield (f"{head} solved in {it} at {r.get('keys', 0)} keys "
                   f"by {r.get('method', '-')}  --  {settings(r)}")
        else:
            yield (f"{head} UNSOLVED after {it}  --  {why(r)}  --  {settings(r)}")


def table(rows, runs, bad):
    done = [r for r in rows.values() if r.get("solved")]
    out = [f"# Solution ledger -- {len(done)} of {len(rows)} levels solved", ""]
    if bad:
        out.append(f"*{bad} unreadable rows skipped.*\n")

    if runs:
        out += ["| run | iteration | max-round | flags |", "|---|---:|---:|---|"]
        for r in runs:
            out.append(f"| {(r.get('run') or '')[:19]} | {r.get('iteration', '-')} "
                       f"| {r.get('max_round', '-')} | {r.get('config') or '-'} |")
        out += ["", "*`--ladder` prints what each rung was configured as, "
                "per round, for any of these runs.*", ""]

    by_it = defaultdict(lambda: [0, 0])
    for r in rows.values():
        slot = by_it[r.get("iteration", 0)]
        slot[0 if r.get("solved") else 1] += 1
    out += ["| iteration | solved | still unsolved |", "|---|---:|---:|"]
    for it in sorted(by_it):
        s, u = by_it[it]
        out.append(f"| {it or '-'} | {s} | {u} |")
    out.append("")

    tier = defaultdict(lambda: [0, 0])
    for r in rows.values():
        slot = tier[TIERS.get(r.get("difficulty", 0), "unrated")]
        slot[0 if r.get("solved") else 1] += 1
    out += ["| tier | solved | unsolved |", "|---|---:|---:|"]
    for name in ("Kids", "Easy", "Medium", "Hard", "Deadly", "unrated"):
        if name in tier:
            s, u = tier[name]
            out.append(f"| {name} | {s} | {u} |")
    out += ["", "```"] + list(lines(rows)) + ["```"]
    return "\n".join(out)


def stops(rows):
    """Per rung, over the unsolved levels only: how its last round ended.  The
    column that matters is the one a rung is *always* in -- a rung that ends on
    push-dead-end everywhere is asking for width, one that ends on budget
    everywhere is asking for a better ranking, and item 7 has already priced
    the third answer."""
    per = defaultdict(Counter)
    for r in rows.values():
        if r.get("solved"):
            continue
        for a in r.get("attempts") or []:
            per[a.get("rung", "-")][a.get("stop", "-")] += 1
    if not per:
        unsolved = sum(1 for r in rows.values() if not r.get("solved"))
        return ("every level in this ledger is solved" if not unsolved else
                f"{unsolved} unsolved levels, none carrying per-rung detail "
                "-- their rows were written by a build before `attempts` existed")
    kinds = sorted({s for c in per.values() for s in c})
    out = ["| rung | " + " | ".join(kinds) + " |",
           "|---" * (len(kinds) + 1) + "|"]
    for rung in sorted(per):
        out.append(f"| {rung} | "
                   + " | ".join(str(per[rung].get(k, 0)) for k in kinds) + " |")
    return "\n".join(out)


def ladder(runs, want):
    """What every rung was configured as, per round -- the header rows, which
    are the only place the driver's own tuning is written down (Auto.Ladder is
    not expressible as a command line)."""
    if not runs:
        return ("no header rows -- they are written under --iteration, so this "
                "ledger predates it or was written without one")
    picked = [r for r in runs if want in (None, r.get("iteration"))] or runs[-1:]
    out = []
    for r in picked:
        out.append(f"## iteration {r.get('iteration', '-')}  "
                   f"({(r.get('run') or '')[:19]})  {r.get('config') or 'no flags'}")
        out += ["", "| round | rung | nodes | ms | tuned |", "|---:|---|---:|---:|---|"]
        for e in r.get("ladder") or []:
            out.append(f"| {e['round']} | {e['rung']} | {e['nodes']:,} "
                       f"| {e['ms']:,} | `{e['tuned']}` |")
        out.append("")
    return "\n".join(out)


def how(r):
    """The `how` column: what solved it, or what the last round died of."""
    if r.get("solved"):
        return r.get("method", "-")
    ats = r.get("attempts") or []
    if not ats:
        return r.get("stop", "-")
    stops = Counter(a.get("stop", "-") for a in ats)
    return ", ".join(f"{n}x {s}" for s, n in stops.most_common(3))


def document(rows, runs, levels_dir):
    """SOLUTIONS.md: one table per level file, every level in it, whether or not
    the ledger has ever seen it.

    **Derived, and overwritten every time.**  The ledger is the state and this
    is a view of it, so it is never merged into and never guarded -- a run that
    comes back worse rewrites the file and the git diff is the alarm.  That is
    the same rule the .lpb writer follows for the same reason.
    """
    total = [r for r in rows.values() if r.get("solved")]
    out = [f"# Solutions -- {len(total)} solved", "",
           "*Generated by `tools/ledger.py` from `data/reports/solutions.jsonl`, "
           "which is the state; this file is a view of it and is overwritten in "
           "full on every run. Do not edit it.*", ""]

    if runs:
        last = runs[-1]
        out += [f"Latest iteration **{last.get('iteration', '-')}**, "
                f"`--max-round {last.get('max_round', '-')}`. "
                "`python tools/ledger.py <ledger> --ladder` prints what every "
                "rung was configured as, per round.", ""]

    by_coll = defaultdict(dict)
    for (coll, lv), r in rows.items():
        by_coll[coll][lv] = r

    # Every .lvl on disk, plus any collection the ledger knows and the
    # directory does not -- a renamed file must not silently drop its rows.
    names = sorted({p.stem for p in pathlib.Path(levels_dir).glob("*.lvl")}
                   | set(by_coll))

    out += ["| collection | levels | solved | attempted | rate |",
            "|---|---:|---:|---:|---:|"]
    counted = {}
    for coll in names:
        all_lv = collection_levels(pathlib.Path(levels_dir) / f"{coll}.lvl")
        seen = by_coll.get(coll, {})
        n = len(all_lv) or len(seen)
        s = sum(1 for r in seen.values() if r.get("solved"))
        counted[coll] = (all_lv, seen, n, s)
        rate = f"{100.0 * s / n:.1f}%" if n else "-"
        out.append(f"| [{coll}](#{coll.lower()}) | {n} | {s} | {len(seen)} | {rate} |")
    out.append("")

    for coll in names:
        all_lv, seen, n, s = counted[coll]
        out += [f"## {coll}", "",
                f"**{s} of {n} solved**, {len(seen)} attempted.", "",
                "| level | name | tier | status | keys | vs record | how |",
                "|---:|---|---|---|---:|---:|---|"]
        # The .lvl is the spine when it is there, so a level nobody has run
        # still gets a row; the ledger's own keys are the fallback for a
        # collection whose file has gone missing.
        spine = all_lv or [(lv, (r.get("name") or "").strip(),
                            r.get("difficulty", 0))
                           for lv, r in sorted(seen.items())]
        for lv, name, diff in spine:
            r = seen.get(lv)
            tier = TIERS.get(diff, "unrated")
            if r is None:
                out.append(f"| {lv} | {name} | {tier} | not attempted | | | |")
                continue
            # A row with no iteration predates them -- say nothing rather than
            # print a dash where a number belongs.
            it = r.get("iteration", 0)
            it = f" (it {it})" if it else ""
            if r.get("solved"):
                ratio = r.get("ratio") or 0
                out.append(f"| {lv} | {name} | {tier} | solved{it} "
                           f"| {r.get('keys', 0)} "
                           f"| {f'{ratio:.1f}x' if ratio else '-'} | {how(r)} |")
            else:
                out.append(f"| {lv} | {name} | {tier} | **unsolved**{it} "
                           f"| | | {how(r)} |")
        out.append("")
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("report", help="the .jsonl the driver appends under --report")
    ap.add_argument("-o", "--out", help="write the table here instead of stdout")
    ap.add_argument("--stops", action="store_true",
                    help="per-rung stop reasons over the unsolved levels")
    ap.add_argument("--unsolved", action="store_true",
                    help="list only the levels still unsolved")
    ap.add_argument("--ladder", nargs="?", type=int, const=-1, default=None,
                    metavar="N",
                    help="what each rung was configured as, per round; "
                         "optionally for iteration N only")
    ap.add_argument("--solutions", metavar="PATH",
                    help="write the whole document -- a table per level file, "
                         "every level in it -- to PATH, overwriting it")
    ap.add_argument("--levels", metavar="DIR",
                    default=str(pathlib.Path(__file__).resolve().parent.parent
                                / "data" / "levels"),
                    help="where the .lvl files are, for --solutions")
    a = ap.parse_args()

    try:
        rows, runs, bad = load(a.report)
    except FileNotFoundError:
        print(f"no ledger at {a.report}", file=sys.stderr)
        return 2
    if not rows and not runs:
        print(f"{a.report} has no readable rows", file=sys.stderr)
        return 2

    if a.solutions:
        text = document(rows, runs, a.levels)
        with open(a.solutions, "w", encoding="utf-8", newline="\n") as f:
            f.write(text + "\n")
        done = sum(1 for r in rows.values() if r.get("solved"))
        print(f"wrote {a.solutions} -- {done} solved of {len(rows)} attempted")
        return 0

    if a.ladder is not None:
        text = ladder(runs, None if a.ladder < 0 else a.ladder)
    elif a.stops:
        text = stops(rows)
    elif a.unsolved:
        text = "\n".join(l for l in lines(rows) if "UNSOLVED" in l)
    else:
        text = table(rows, runs, bad)

    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(text + "\n")
        print(f"wrote {a.out}")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
