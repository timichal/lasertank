#!/usr/bin/env python
"""Diff two LaserTank traces: the first diverging tick, and the field that moved first.

    python tools/difftrace.py A.trace B.trace [-c N] [-q] [--strict]
    python tools/difftrace.py traces-oracle/ traces-csharp/     # whole corpus

Given two directories it pairs traces by filename and prints one verdict line
each -- which is the Phase 2 exit criterion, since `replay_all.py --traces DIR`
writes one trace per recording.  With -q, only the failures are listed.

Traces come from `oracle.exe --trace` (format in `oracle/README.md`) and, from
Phase 2 on, from the C# core.  One line per tick of whitespace-separated
`key=value` tokens; `#` lines are the header and the result footer.  Ticks are
aligned by position, and `t=` is compared like any other field, so a numbering
desync is reported rather than silently absorbed.

What it reports:

  * the first tick whose lines differ,
  * within that tick, every differing field -- ordered so the most primary one
    is named first, because *that name is the localisation*: `S.moves` says look
    at ScoreMove, `SlO.dy` says look at IceMoveO,
  * for PF / PF2 / BMF / BMF2, the individual 16x16 cells that changed, decoded
    to object names,
  * a whole-file summary: how many ticks diverge, and which fields ever move.

Field order (see ORDER) is a reporting heuristic, not a claim about causality:
state the engines compute directly -- tank, laser, score, key pointer, slide
stack, playfield -- is named ahead of what is derived from it (the H hashes, the
death and Game_On flags).

`A` (AniLevel/AniCount), `BMF` and `BMF2` are cosmetic: no bitmap feeds a
decision anywhere in the program (hazard #2, corrected in Phase 1).  A run where
only those differ exits 3, not 1, so a gate can treat it as the tripwire it is
rather than as a correctness failure.  --strict makes it a failure.

Exit: 0 identical, 1 diverged, 3 cosmetic-only divergence, 2 unusable input.
"""
import argparse
import pathlib
import re
import sys
from collections import Counter, deque

# --- field vocabulary -------------------------------------------------------

# Reporting order, most primary first.  M1..M15 (the slide stack) sort between
# N and PF; anything unrecognised sorts last, so a new field is never hidden.
PRIMARY = ["t", "T", "L", "S", "P", "C", "SlT", "SlO", "N"]
DERIVED = ["PF", "PF2", "H", "D", "G"]
COSMETIC = ["A", "BMF", "BMF2"]
ORDER = PRIMARY + ["M*"] + DERIVED + COSMETIC

# Sub-field names, so a diff reads "T.dir 1 -> 3", not "T 5,0,1,0,0 -> 5,0,3,0,0".
POSE = ("x", "y", "dir", "firing", "good")
SLIDE = ("x", "y", "dx", "dy", "s")
SUBFIELD = {
    "T": POSE, "L": POSE, "S": ("moves", "shots"),
    "SlT": SLIDE, "SlO": SLIDE, "A": ("AniLevel", "AniCount"), "H": ("PF", "PF2"),
}
WHAT = {
    "t": "tick number", "T": "tank", "L": "laser", "S": "score",
    "P": "RecP (key index)", "C": "ConvMoving", "SlT": "SlideT", "SlO": "SlideO",
    "N": "SlideMem.count", "PF": "playfield", "PF2": "under-playfield",
    "H": "fnv1a(PF),fnv1a(PF2)", "D": "deaths", "G": "Game_On",
    "A": "animation", "BMF": "bitmaps", "BMF2": "under-bitmaps",
}
GRIDS = ("PF", "PF2", "BMF", "BMF2")     # 256 hex bytes; PF[x][y] lives at x*16+y

# Object IDs, table at the top of LTANK.H.  Tunnels are encoded out of band as
# 0x40 | (id << 1) | waitbit -- see the GetTunnelID / ISTunnel macros.
OBJ = {
    0: "dirt", 1: "tank", 2: "flag", 3: "water", 4: "solid", 5: "block",
    6: "bricks", 7: "a-t up", 8: "a-t right", 9: "a-t down", 10: "a-t left",
    11: "mirror ul", 12: "mirror ur", 13: "mirror dr", 14: "mirror dl",
    15: "conveyor up", 16: "conveyor right", 17: "conveyor down",
    18: "conveyor left", 19: "crystal", 20: "roto ul", 21: "roto ur",
    22: "roto dr", 23: "roto dl", 24: "ice", 25: "thin ice",
}

_M_RE = re.compile(r"M(\d+)\Z")
_META_RE = re.compile(r"\b(level|name|author|keys)=(.*?)(?=\s+\w+=|$)")


def rank(key):
    """Sort position of a field name, following ORDER."""
    if key in PRIMARY:
        return (0, PRIMARY.index(key), key)
    m = _M_RE.match(key)
    if m:
        return (1, int(m.group(1)), key)
    if key in DERIVED:
        return (2, DERIVED.index(key), key)
    if key in COSMETIC:
        return (3, COSMETIC.index(key), key)
    return (4, 0, key)


def objname(byte):
    """Decode one PF / PF2 cell byte to an object name."""
    if byte & 0x40:
        return "tunnel %d%s" % ((byte & 0x0F) >> 1, "+wait" if byte & 1 else "")
    return OBJ.get(byte, "?")


# --- parsing ----------------------------------------------------------------


class Trace:
    """Streams one trace file, keeping the leading and trailing # lines aside."""

    def __init__(self, path):
        self.path = pathlib.Path(path)
        self.head, self.foot, self.count = [], [], 0
        try:
            self._fh = self.path.open("r", encoding="latin-1")
        except OSError as e:
            print("difftrace: %s" % e, file=sys.stderr)
            raise SystemExit(2)

    def ticks(self):
        for raw in self._fh:
            raw = raw.strip()
            if not raw:
                continue
            if raw.startswith("#"):
                (self.foot if self.count else self.head).append(raw)
                continue
            self.count += 1
            yield raw
        self._fh.close()

    def meta(self):
        """level / name / keys from the header, for the same-input sanity check."""
        out = {}
        for line in self.head:
            for k, v in _META_RE.findall(line):
                out[k] = v.strip()
        return out

    def result(self):
        for line in self.foot:
            if "result=" in line:
                return line.lstrip("# ").strip()
        return None


def split(raw):
    """A tick line -> {key: value}."""
    out = {}
    for tok in raw.split():
        k, _, v = tok.partition("=")
        out[k] = v
    return out


def elide(raw, limit=32):
    """A tick line with the 256-byte hex grids cut out, for context printing."""
    keep = []
    for tok in raw.split():
        k, _, v = tok.partition("=")
        keep.append("%s=<%dB>" % (k, len(v) // 2)
                    if k in GRIDS and len(v) > limit else tok)
    return " ".join(keep)


# --- comparison -------------------------------------------------------------


def cells(key, av, bv, limit):
    """Per-cell diff of a hex grid -> (number of differing cells, detail lines)."""
    n = min(len(av), len(bv)) // 2
    hits = [i for i in range(n) if av[2 * i:2 * i + 2] != bv[2 * i:2 * i + 2]]
    lines = []
    for i in hits[:limit]:
        a = int(av[2 * i:2 * i + 2], 16)
        b = int(bv[2 * i:2 * i + 2], 16)
        loc = "%s[x=%d,y=%d]" % (key, i // 16, i % 16)
        if key in ("PF", "PF2"):
            lines.append("%-17s %02x %-14s -> %02x %s"
                         % (loc, a, objname(a), b, objname(b)))
        else:
            lines.append("%-17s %3d -> %3d" % (loc, a, b))
    if len(hits) > limit:
        lines.append("... and %d more cell%s" % (len(hits) - limit,
                                                 "" if len(hits) - limit == 1 else "s"))
    return len(hits), lines


def diff_field(key, av, bv, limit):
    """One differing field -> (names to report, detail lines).

    Named sub-fields are reported individually, so only the component that
    actually moved gets blamed.
    """
    if key in GRIDS and len(av) > 32 and len(bv) > 32:
        n, lines = cells(key, av, bv, limit)
        return ["%s (%d cell%s)" % (key, n, "" if n == 1 else "s")], lines
    subs = SUBFIELD.get(key) or (SLIDE if _M_RE.match(key) else None)
    if subs:
        a, b = av.split(","), bv.split(",")
        if len(a) == len(b) == len(subs):
            return (["%s.%s %s -> %s" % (key, subs[i], a[i], b[i])
                     for i in range(len(subs)) if a[i] != b[i]], [])
    return ["%s %s -> %s" % (key, av, bv)], []


def compare_tick(ra, rb, limit):
    """Two differing tick lines -> [(key, names, details)], in reporting order."""
    fa, fb = split(ra), split(rb)
    out = []
    for k in sorted(set(fa) | set(fb), key=rank):
        av, bv = fa.get(k, "<absent>"), fb.get(k, "<absent>")
        if av == bv:
            continue
        if "<absent>" in (av, bv):
            out.append((k, ["%s %s -> %s" % (k, av, bv)], []))
            continue
        names, details = diff_field(k, av, bv, limit)
        out.append((k, names or ["%s %s -> %s" % (k, av, bv)], details))
    return out


# --- reporting --------------------------------------------------------------


def compare_files(pa, pb, args, report=True):
    """Compare two trace files.  Returns (exit code, one-line verdict)."""
    A, B = Trace(pa), Trace(pb)
    ia, ib = A.ticks(), B.ticks()

    first = None          # (line index, raw A, raw B, context)
    tail = None           # (which trace runs on, how many extra ticks)
    hist = deque(maxlen=max(args.context, 0) + 1)
    seen = Counter()      # field -> number of ticks it differs on
    n = 0

    while True:
        ra, rb = next(ia, None), next(ib, None)
        if ra is None and rb is None:
            break
        if ra is None or rb is None:
            rest = ib if ra is None else ia
            tail = ("B" if ra is None else "A", 1 + sum(1 for _ in rest))
            break
        n += 1
        hist.append(ra if ra == rb else None)
        if ra == rb:
            continue
        if first is None:
            first = (n, ra, rb, [h for h in hist if h is not None])
        for k, _, _ in compare_tick(ra, rb, args.max_cells):
            seen[k] += 1
        if args.first_only:
            break

    empty = [str(t.path) for t in (A, B) if not t.count]
    if empty:
        print("difftrace: no tick lines in %s" % ", ".join(empty), file=sys.stderr)
        return 2, "UNUSABLE    no tick lines"

    ma, mb = A.meta(), B.meta()
    mismatch = {k: (ma[k], mb[k]) for k in set(ma) & set(mb) if ma[k] != mb[k]}
    if mismatch:
        print("difftrace: WARNING -- these traces are not of the same input:",
              file=sys.stderr)
        for k, (va, vb) in sorted(mismatch.items()):
            print("  %s: %r vs %r" % (k, va, vb), file=sys.stderr)

    cosmetic_only = bool(seen) and set(seen) <= set(COSMETIC) and tail is None
    diverged = bool(seen) or tail is not None

    if report and not args.quiet:
        # --first-only stops mid-stream, so the counts are lines read, not totals.
        how = "ticks read" if args.first_only else "ticks"
        print("A  %s   %d %s" % (A.path, A.count, how))
        print("B  %s   %d %s" % (B.path, B.count, how))

        if first:
            idx, ra, rb, ctx = first
            fields = compare_tick(ra, rb, args.max_cells)
            names = [nm for _, nms, _ in fields for nm in nms]
            print("\n=== first divergence: tick %s (line %d) ==="
                  % (split(ra).get("t", "?"), idx))
            print("first field:  %-28s [%s]"
                  % (names[0], WHAT.get(fields[0][0], "?")))
            if len(names) > 1:
                print("also:         " + ", ".join(names[1:]))

            if ctx:
                print("\n--- %d matching tick%s before ---"
                      % (len(ctx), "" if len(ctx) == 1 else "s"))
                for line in ctx:
                    print("    " + elide(line))
            print("\n--- at the divergence ---")
            print("  A " + elide(ra))
            print("  B " + elide(rb))
            details = [ln for _, _, ds in fields for ln in ds]
            if details:
                print()
                for ln in details:
                    print("    " + ln)

        if tail:
            longer, extra = tail
            print("\n=== length mismatch ===")
            print("%s stops after %d tick%s; %s has %d more"
                  % ("A" if longer == "B" else "B", n, "" if n == 1 else "s",
                     longer, extra))

        if seen and not args.first_only:
            print("\n=== summary ===")
            print("ticks compared:  %d" % n)
            print("diverging ticks: %d" % max(seen.values()))
            for k in sorted(seen, key=rank):
                print("  %-5s %-22s %4d tick%s%s"
                      % (k, WHAT.get(k, ""), seen[k], "" if seen[k] == 1 else "s",
                         "   (cosmetic)" if k in COSMETIC else ""))

        fa, fb = A.result(), B.result()
        if fa != fb:
            print("\n=== result footer ===")
            print("  A %s" % (fa or "<none>"))
            print("  B %s" % (fb or "<none>"))
        print()

    if not diverged:
        return 0, "IDENTICAL   %d ticks" % n
    if cosmetic_only and not args.strict:
        return 3, ("COSMETIC    logic identical over %d ticks; %s differ"
                   % (n, "/".join(sorted(seen, key=rank))))
    if first:
        field = compare_tick(first[1], first[2], 1)[0][1][0]
        return 1, ("DIVERGE     first at tick %s, field %s"
                   % (split(first[1]).get("t", "?"), field))
    return 1, "DIVERGE     length mismatch after %d ticks" % n


def compare_dirs(da, db, args):
    """Pair two directories of traces by filename.  This is the Phase 2 gate:
    replay_all.py --traces writes one trace per recording, so a whole-corpus
    check is `difftrace.py traces-oracle/ traces-csharp/`."""
    fa = {p.name: p for p in sorted(da.iterdir()) if p.is_file()}
    fb = {p.name: p for p in sorted(db.iterdir()) if p.is_file()}
    both = sorted(set(fa) & set(fb))
    only = sorted(set(fa) ^ set(fb))
    if not both:
        print("difftrace: no filenames in common between %s and %s" % (da, db),
              file=sys.stderr)
        return 2

    severity = {0: 0, 3: 1, 1: 2, 2: 2}      # identical < cosmetic < broken
    width = max(len(n) for n in both + only)
    tally = Counter()
    worst = 0
    for name in both:
        rc, verdict = compare_files(fa[name], fb[name], args, report=False)
        tally[rc] += 1
        if severity[rc] > severity[worst]:
            worst = rc
        if rc or not args.quiet:
            print("%-*s  %s" % (width, name, verdict))

    for name in only:
        print("%-*s  MISSING     only in %s" % (width, name, "A" if name in fa else "B"))
    if only:
        worst = 1

    print("\n%d compared: %d identical, %d cosmetic-only, %d diverged, %d unusable"
          % (len(both), tally[0], tally[3], tally[1], tally[2]))
    if only:
        print("%d file%s in only one directory" % (len(only), "" if len(only) == 1 else "s"))
    return worst


def main():
    ap = argparse.ArgumentParser(
        description="Diff two LaserTank oracle traces, or two directories of them.",
        epilog="exit 0 identical, 1 diverged, 3 cosmetic-only, 2 unusable input")
    ap.add_argument("a", metavar="A")
    ap.add_argument("b", metavar="B")
    ap.add_argument("-c", "--context", type=int, default=2, metavar="N",
                    help="ticks of context before the divergence (default 2)")
    ap.add_argument("--max-cells", type=int, default=12, metavar="N",
                    help="playfield cells to list per grid (default 12)")
    ap.add_argument("--first-only", action="store_true",
                    help="stop at the first diverging tick; skips the summary")
    ap.add_argument("--strict", action="store_true",
                    help="treat a cosmetic-only divergence as a failure")
    ap.add_argument("-q", "--quiet", action="store_true",
                    help="verdict lines only; in directory mode, failures only")
    args = ap.parse_args()

    pa, pb = pathlib.Path(args.a), pathlib.Path(args.b)
    if pa.is_dir() and pb.is_dir():
        return compare_dirs(pa, pb, args)
    if pa.is_dir() or pb.is_dir():
        print("difftrace: compare two files or two directories, not one of each",
              file=sys.stderr)
        return 2

    rc, verdict = compare_files(pa, pb, args)
    print(verdict)
    return rc


if __name__ == "__main__":
    sys.exit(main())
