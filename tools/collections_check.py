#!/usr/bin/env python
"""Command 108's gate: the collection picker's list, and the switch it drives.

The port's answer to "Open Data File" is a list of the .lvl files under
data/levels/ and data/quirks/ rather than a native file dialog (see
src/LaserTank.Game/CollectionList.cs for why, and for what is lost).  That
makes two claims, of two different kinds, and this checks both:

  list    every row the picker offers is derived from the filesystem -- a walk
          of two directory trees, a file size, and a count of solved records in
          a .hs.  No engine is involved, so it gets the cross-implementation
          treatment the sprite sheets and the three list dialogs get: the game
          dumps every row, this rebuilds them in Python from the same
          directories, and the two must agree row by row and by sha256.  Two
          implementations agreeing is evidence; one agreeing with itself is not,
          which is why the walk, the sort, the column widths and the solved
          count are all written out again here rather than imported.

  switch  what the game does with the file that comes back.  `--check-collections`
          opens **every collection in the list, in order, through one Session**,
          and this checks the part PROGRESS.md calls load-bearing: that
          AssignHSFile ran, so the .hs, the .ghs and the default recording name
          followed the collection.  A picker that moved the level file and not
          the score files would post the new collection's scores into the old
          one's .hs -- and a .hs is positional, so it would overwrite a real
          score rather than append a wrong one, with nothing on screen to say
          so.  It also checks which level a freshly opened collection lands on
          -- the first one with no record in the player's .hs, computed here
          from the file rather than read back from the game -- and the restore
          after a file that will not open.

    python tools/collections_check.py          # ~20 s
    python tools/collections_check.py -v       # every row

It rebuilds the Godot project's C# first, because `godot --path` does not.
Needs Godot; without it both halves SKIP loudly rather than pass quietly.
Nothing here touches build/, so it is safe beside a live solve.  It writes
nothing at all: the game's Session is built without Options, so no .hs is
posted and no INI key moves.

Exit: 0 clean, 1 a mismatch, 2 environment.
"""
import argparse
import hashlib
import pathlib
import re
import subprocess
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
from engines import ROOT                                # noqa: E402
from atlas_check import find_godot                      # noqa: E402

GAME = ROOT / "src" / "LaserTank.Game"

LEVEL_REC = 576         # sizeof(TLEVEL)
HS_REC = 10             # sizeof(THSREC)
NAME_OFF = 256          # TLEVEL.LName
NAME_LEN = 31

# CollectionList.Roots, in its order -- the two corpus trees and the editor's
# output.  Written out again rather than read from the C#, for the reason in the
# docstring: a list that agreed with itself would prove nothing.
ROOTS = ["data/levels", "data/quirks", "out/levels"]

LABEL_W = 24            # CollectionList.BuildRows' first column


def cstr(b):
    """A NUL-terminated latin-1 field."""
    i = b.find(b"\0")
    return (b if i < 0 else b[:i]).decode("latin-1")


def scan(root):
    """CollectionList.Scan, again: every .lvl under each root, at any depth.

    **Matched on the suffix, never by a glob.**  The corpus mixes `.lvl` and
    `.LVL` -- four collections ship uppercase, and they are not the small ones --
    so a `*.lvl` pattern is right on Windows and silently short on Linux.  Same
    trap the graphics packs have.
    """
    out = []
    for rel in ROOTS:
        base = root / rel
        if not base.is_dir():
            continue
        hits = [p for p in base.rglob("*")
                if p.is_file() and p.suffix.lower() == ".lvl"]
        hits.sort(key=lambda p: str(p).lower())
        out += hits
    return out


def level_name(lvl, level):
    """TLEVEL.LName of one level, by seek -- the collections are up to 1 MB."""
    with lvl.open("rb") as f:
        f.seek((level - 1) * LEVEL_REC + NAME_OFF)
        return f.read(NAME_LEN)


def first_unsolved(hs, levels):
    """HighScores.FirstUnsolved, again: the level a new collection opens at.

    The first record with `moves == 0`, counting a record past the end of a
    short .hs as unbeaten, and 1 when every level is done -- the port's one
    deviation from command 108's `CurLevel = 0`, argued in HighScores.cs and
    borrowed from the original's own SkipCL.
    """
    if levels < 1:
        return 1
    data = hs.read_bytes() if hs.exists() else b""
    have = len(data) // HS_REC
    for i in range(levels):
        if i >= have:
            return i + 1
        if int.from_bytes(data[i * HS_REC:i * HS_REC + 2], "little") == 0:
            return i + 1
    return 1


def count_solved(hs):
    """Records in the player's .hs with moves > 0.

    The blanks are CheckHighScore's padding -- a .hs is dense and positional, so
    beating level 500 first writes 499 records with moves == 0 in front of it.
    """
    if not hs.exists():
        return 0
    data = hs.read_bytes()
    return sum(1 for i in range(len(data) // HS_REC)
               if int.from_bytes(data[i * HS_REC:i * HS_REC + 2], "little") > 0)


def build_row(root, lvl):
    """CollectionList.BuildRows' one format, again."""
    label = lvl.stem
    levels = lvl.stat().st_size // LEVEL_REC
    solved = count_solved(lvl.with_suffix(".hs"))
    where = lvl.parent.relative_to(root).as_posix()
    return "%s %5d/%-5d %s" % (label[:LABEL_W].ljust(LABEL_W), solved, levels, where)


# --- running the Godot side ------------------------------------------------

HEAD_RE = re.compile(r"^collections root=(?P<root>\S+) n=(?P<n>\d+)$")
COLL_RE = re.compile(r"^coll (?P<i>\d+) path=(?P<path>.*?) row=(?P<row>.*)$")
HASH_RE = re.compile(r"^hash (?P<n>\d+) (?P<sha>[0-9a-f]{64})$")
OPEN_RE = re.compile(
    r"^open path=(?P<path>.*?) ok=(?P<ok>[01]) level=(?P<level>\d+) "
    r"levels=(?P<levels>\d+) lname=(?P<lname>.*?) hs=(?P<hs>.*?) "
    # `.*` and not `\S+` on the last field: one collection in the corpus is
    # `Rotary Mirrors-Challenge.LVL`, and a space in a path is exactly the kind
    # of thing that makes a gate quietly check 22 of 23 things.
    r"ghs=(?P<ghs>.*?) pb=(?P<pb>.*)$")
PB_RE = re.compile(
    r"^playback (?:skipped=1|loaded=(?P<loaded>[01]) closed-by-108=(?P<closed>[01]))")
MISS_RE = re.compile(
    r"^missing ok=(?P<ok>[01]) restored=(?P<restored>[01]) kept=(?P<kept>.*?) "
    r"level=(?P<level>\d+) levels=(?P<levels>\d+)$")


def run_godot(godot):
    """-> stdout+stderr as text.

    latin-1 bytes reach this (one level is named `®otary mirrors 1-a`) and
    Godot's stdout re-encodes them, so decode permissively; the names are
    compared through the same decoding on both sides and the rows are compared
    by hash as well.
    """
    p = subprocess.run([godot, "--headless", "--path", str(GAME), "--",
                        "--check-collections"],
                       capture_output=True, timeout=600, cwd=str(ROOT))
    return (p.stdout + p.stderr).decode("utf-8", "replace"), p.returncode


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("-v", "--verbose", action="store_true", help="print every row")
    args = ap.parse_args()

    godot = find_godot()
    if not godot:
        print("collections SKIPPED: no Godot found (set LT_GODOT)")
        return 2

    _ok, warn = engines.build_godot_game()
    print("  build: LaserTank.Game ok%s" % ("" if not warn else "   %d warning(s)" % warn))

    out, rc = run_godot(godot)
    if rc != 0 or "collections OK" not in out:
        print("  FAIL: --check-collections rc=%d" % rc)
        print("\n".join("      " + l for l in out.splitlines()[-12:]))
        return 1

    got_rows, got_paths, got_open = [], [], []
    got_hash, head, miss, pb = None, None, None, None
    for line in out.splitlines():
        m = PB_RE.match(line)
        if m:
            pb = m
            continue
        m = COLL_RE.match(line)
        if m:
            got_paths.append(m.group("path"))
            got_rows.append(m.group("row"))
            continue
        m = HASH_RE.match(line)
        if m:
            got_hash = (int(m.group("n")), m.group("sha"))
            continue
        m = OPEN_RE.match(line)
        if m:
            got_open.append(m)
            continue
        m = MISS_RE.match(line) or HEAD_RE.match(line)
        if m:
            if m.re is MISS_RE:
                miss = m
            else:
                head = m

    fails = []

    # ---- the list ---------------------------------------------------------
    want_paths = scan(ROOT)
    want_rows = [build_row(ROOT, p) for p in want_paths]
    want_rel = [p.relative_to(ROOT).as_posix() for p in want_paths]

    if got_paths != want_rel:
        fails.append("the files offered differ\n      godot  %r\n      python %r"
                     % (got_paths, want_rel))
    for i in range(min(len(got_rows), len(want_rows))):
        if got_rows[i] != want_rows[i]:
            fails.append("row %d\n      godot  %r\n      python %r"
                         % (i, got_rows[i], want_rows[i]))
            break
    wsha = hashlib.sha256("\n".join(want_rows).encode("latin-1")).hexdigest()
    if got_hash != (len(want_rows), wsha):
        fails.append("row hash %s vs %s" % (got_hash, (len(want_rows), wsha)))
    if head and int(head.group("n")) != len(want_rows):
        fails.append("count %s vs %d" % (head.group("n"), len(want_rows)))
    print("  list:   %d collections under %s" % (len(want_rows), ", ".join(ROOTS)))
    if args.verbose:
        for r in want_rows:
            print("      " + r)

    # ---- the switch -------------------------------------------------------
    if [m.group("path") for m in got_open] != want_rel:
        fails.append("not every collection was opened: %r"
                     % [m.group("path") for m in got_open])
    for m in got_open:
        rel = m.group("path")
        lvl = ROOT / rel
        stem = rel[:rel.rfind(".")]                     # AssignHSFile: the last dot
        levels = lvl.stat().st_size // LEVEL_REC
        want_level = first_unsolved(lvl.with_suffix(".hs"), levels)
        want = {
            "ok": "1",
            "level": str(want_level),                   # the first unsolved one
            "levels": str(levels),
            "hs": stem + ".hs",
            "ghs": stem + ".ghs",
            "pb": "%s_%04d.lpb" % (stem, want_level),
            "lname": cstr(level_name(lvl, want_level)),
        }
        for key, w in want.items():
            if m.group(key) != w:
                fails.append("%s: %s is %r, want %r" % (rel, key, m.group(key), w))
    print("  switch: %d collections opened, .hs/.ghs/.lpb followed each one"
          % len(got_open))

    # A playback open when the collection changes has to be closed: `Load`
    # keeps one whose level number matches, and the number stopped identifying
    # the level the moment a Session could hold two collections.
    if pb is None:
        fails.append("no `playback` line -- the rule was not exercised")
    elif pb.group("loaded") is None:
        print("  playback: SKIPPED -- no data/demos/LaserTank/00001.lpb here")
    elif pb.group("loaded") != "1" or pb.group("closed") != "1":
        fails.append("a playback across a change of collection: loaded=%s closed=%s, "
                     "want 1 and 1" % (pb.group("loaded"), pb.group("closed")))
    else:
        print("  playback: open across command 108 -> closed, as a level load does")

    if miss is None:
        fails.append("no `missing` line -- the restore was not exercised")
    elif miss.group("ok") != "0" or miss.group("restored") != "1":
        fails.append("a level file that is not there: ok=%s restored=%s, "
                     "want 0 and 1" % (miss.group("ok"), miss.group("restored")))
    else:
        print("  missing .lvl: refused, and the collection on screen kept (%s)"
              % miss.group("kept"))

    for f in fails:
        print("    " + f)
    print("collections %s" % ("OK" if not fails else "FAILED (%d)" % len(fails)))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
