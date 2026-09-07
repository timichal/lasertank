#!/usr/bin/env python
"""Phase 5 step 4's second gate: the list rows and the .hs file, cross-checked.

Two checks, both of the shape the sprite sheets and the WAVs use -- decode the
same bytes twice, in two languages, and compare -- because neither of these two
outputs is engine behaviour and so neither is something the C oracle can be made
to emit.  (What *is* engine behaviour in step 4 -- Undo, Save/Restore Position,
the playback speeds -- goes through the oracle instead: tools/undo_check.py.)

  lists   the three list dialogs' rows.  LoadBox, HSList and GHSList each build
          one string per level with a sprintf whose padding and truncation are
          observable (`%4d   %-30.30s %s`, `%4d %-30.30s`, `%4d** %-28.28s %5d
          %5d  %4s>`), prefix it with a difficulty digit that DrawLevels then
          colours by, and hand it to an owner-drawn listbox.  This rebuilds
          every row in Python straight out of the .lvl / .hs / .ghs bytes and
          compares against `--check-lists`, row by row and by sha256.

          Two implementations of the original's format strings agreeing is
          evidence.  One agreeing with itself is not -- which is why the digit
          table, the field offsets and the widths are all written out again
          here rather than imported from anywhere.

  scores  the .hs writer.  A .hs is dense and positional, so beating level 8 of
          a collection whose file only reaches level 3 writes records 4..7 in
          front of it -- and the original pads with its `HS` global *before*
          refreshing that global from the file, so those records carry the
          previous level's shots and initials with `moves` forced to 0.
          Nothing reads them.  Every read-back test passes with them zeroed.
          So the check is on the bytes: the same scripted sequence of scores is
          performed by `--check-scores` and by this file, and the whole file is
          compared as hex after every step.

    python tools/list_check.py                       # both, ~20 s
    python tools/list_check.py --lists-only
    python tools/list_check.py --collection quirks/tutor/Tutor.LVL

It rebuilds the Godot project's C# first, because `godot --path` does not.
Needs Godot; without it both halves SKIP loudly rather than pass quietly.
Nothing here touches build/, so it is safe beside a live solve.

Exit: 0 clean, 1 a mismatch, 2 environment.
"""
import argparse
import hashlib
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
from engines import ROOT                                # noqa: E402
from atlas_check import find_godot                      # noqa: E402

GAME = ROOT / "src" / "LaserTank.Game"
LEVELS = ROOT / "data" / "levels"

LEVEL_REC = 576         # sizeof(TLEVEL)
HS_REC = 10             # sizeof(THSREC)
NAME_W = 6              # THSREC.name is char[6]

# The collections the `lists` half runs over by default: the flagship, one that
# ships uppercase (the .LVL/.lvl split cost a campaign four packs once), and one
# with no .ghs at all so the empty-list path is exercised.
DEFAULT_COLLECTIONS = [
    "levels/LaserTank.lvl",
    "quirks/tutor-with-playbacks/Tutor-with-Playbacks.LVL",
    "quirks/inchworm/inchworm.lvl",
]


# --- the file formats, read again ------------------------------------------


def cstr(b):
    """A NUL-terminated latin-1 field."""
    i = b.find(b"\0")
    return (b if i < 0 else b[:i]).decode("latin-1")


def read_levels(path):
    """-> [(number, name, author, sdiff)] for every 576-byte record."""
    data = path.read_bytes()
    out = []
    for i in range(len(data) // LEVEL_REC):
        at = i * LEVEL_REC
        out.append((i + 1,
                    cstr(data[at + 256:at + 287]),
                    cstr(data[at + 543:at + 574]),
                    int.from_bytes(data[at + 574:at + 576], "little")))
    return out


def read_hs(path):
    """-> [(moves, shots, name)] for every 10-byte record, or []."""
    if not path.exists():
        return []
    data = path.read_bytes()
    out = []
    for i in range(len(data) // HS_REC):
        at = i * HS_REC
        out.append((int.from_bytes(data[at:at + 2], "little"),
                    int.from_bytes(data[at + 2:at + 4], "little"),
                    cstr(data[at + 4:at + 4 + NAME_W])))
    return out


def diff_digit(sdiff):
    """LoadBox's switch (LTANK_D.C:343): 1/2/4/8/16 -> '1'..'5', else '0'.

    A bitmask, so a level rated for two difficulty sets, and an unrated one,
    both fall through to '0' -- the switch has no case for either.
    """
    return {1: "1", 2: "2", 4: "3", 8: "4", 16: "5"}.get(sdiff, "0")


def pad(s, w):
    """`%-30.30s`: pad to w, truncate at w."""
    return s[:w].ljust(w)


def beats(moves, shots, best):
    """CheckHighScore's test (LTANK2.C:1088).  `best` is a tuple or None."""
    if best is None or best[0] == 0:
        return True
    return moves < best[0] or (moves == best[0] and shots < best[1])


def build_rows(mode, levels, mine, posted):
    """The rows, from the original's own sprintf strings.

    Note the list *length*: HSList and GHSList are driven by the score file --
    `while (BytesMoved == sizeof(THSREC))` reads a score record and then a level
    record -- so they stop where that file stops.  LoadBox is driven by the level
    file.  A 2,030-level collection with a 12-record .hs has a 12-row HSList.
    """
    if mode == "MyScores":
        n = min(len(levels), len(mine))
    elif mode == "GlobalScores":
        n = min(len(levels), len(posted))
    else:
        n = len(levels)

    rows = []
    for i in range(n):
        num, name, author, sdiff = levels[i]
        my = mine[i] if i < len(mine) else None
        best = posted[i] if i < len(posted) else None
        if mode == "MyScores":
            s = "%4d %s" % (num, pad(name, 30))
            if my and my[0] > 0:
                s += "%5d  %5d  %s" % (my[0], my[1], my[2])
        elif mode == "GlobalScores":
            bhs = bool(my and my[0] > 0 and beats(my[0], my[1], best))
            bm = best[0] if best else 0
            bs = best[1] if best else 0
            bn = best[2] if best else ""
            if bhs:
                s = "%4d** %s %5d %5d  %4s>" % (num, pad(name, 28), bm, bs, bn)
            else:
                s = "%4d %s %5d %5d  %4s " % (num, pad(name, 30), bm, bs, bn)
            if my and my[0] > 0:
                s += "%5d  %5d  %s" % (my[0], my[1], my[2])
        else:
            s = "%4d   %s %s" % (num, pad(name, 30), author)
        rows.append(diff_digit(sdiff) + s)
    return rows


# --- the .hs writer, written again ----------------------------------------


def hs_bytes(moves, shots, name):
    b = bytearray(HS_REC)
    b[0:2] = moves.to_bytes(2, "little")
    b[2:4] = shots.to_bytes(2, "little")
    enc = name.encode("latin-1")[:NAME_W - 1]      # one byte kept for the NUL
    b[4:4 + len(enc)] = enc
    return bytes(b)


def check_high_score(path, level, moves, shots, player, hs):
    """CheckHighScore + HSBox's OK, in the original's order.

    `hs` is the `HS` global as a mutable [moves, shots, name] -- and it is a
    parameter rather than a local for the one reason this function exists: the
    padding loop writes it to disk *before* the read refreshes it.

    -> (personal_best_fell, error_or_None)
    """
    data = bytearray(path.read_bytes() if path.exists() else b"")

    # `HS.moves = 0`, then pad, then read.
    hs[0] = 0
    if level * HS_REC > len(data):
        blank = hs_bytes(0, hs[1], hs[2])
        first = len(data) // HS_REC
        del data[first * HS_REC:]                  # a partial tail is overwritten
        for _ in range(first, level - 1):
            data += blank

    at = (level - 1) * HS_REC
    if at + HS_REC <= len(data):                   # ReadFile, BytesMoved unchecked
        rec = data[at:at + HS_REC]
        hs[0] = int.from_bytes(rec[0:2], "little")
        hs[1] = int.from_bytes(rec[2:4], "little")
        hs[2] = cstr(rec[4:4 + NAME_W])

    won = beats(moves, shots, (hs[0], hs[1], hs[2]))
    if won:
        hs[0], hs[1] = moves, shots
        if player.lower() != hs[2].lower():        # stricmp
            hs[2] = player
        if at + HS_REC > len(data):
            data += b"\0" * (at + HS_REC - len(data))
        data[at:at + HS_REC] = hs_bytes(hs[0], hs[1], hs[2])
    path.write_bytes(bytes(data))
    return won


# `Step(...)` in Step4Check.CheckScores, in order.  Carried here rather than
# read out of the C# so that changing one side alone fails.
SCORE_SCRIPT = [
    ("first-at-3", 3, 12, 4, "MZ"),
    ("in-front", 1, 7, 2, "MZ"),
    ("better", 3, 11, 4, "MZ"),
    ("worse", 3, 99, 1, "MZ"),
    ("equal-fewer-shots", 3, 11, 3, "MZ"),
    ("far-past-end", 8, 30, 9, "ABCD"),
    ("long-name", 2, 5, 1, "TOOLONG"),
    ("pad-after-abcd", 12, 6, 2, "ZZ"),
]


# --- running the Godot side ------------------------------------------------


ROW_RE = re.compile(r"^row (?P<mode>\w+) (?P<i>\d+) (?P<text>.*)$")
HASH_RE = re.compile(r"^hash (?P<mode>\w+) (?P<n>\d+) (?P<sha>[0-9a-f]{64})$")
SCORE_RE = re.compile(
    r"^score (?P<label>\S+) level=(?P<level>\d+) m=(?P<m>\d+) s=(?P<s>\d+) "
    r"name=(?P<name>\S*) beats=(?P<beats>[01]) len=(?P<len>\d+) (?P<hex>[0-9a-f]*)"
    r"(?P<err> ERROR=.*)?$")


def run_godot(godot, args, timeout=300):
    """-> the run's stdout as latin-1 text.

    latin-1, not UTF-8: level names carry bytes like 0xE9, Godot's stdout
    re-encodes them as UTF-8, and a name is compared by hash anyway -- so
    decoding permissively and trusting the hash beats guessing.
    """
    p = subprocess.run([godot, "--headless", "--path", str(GAME), "--"] + args,
                       capture_output=True, timeout=timeout, cwd=str(ROOT))
    return (p.stdout + p.stderr).decode("utf-8", "replace"), p.returncode


# --- the two checks --------------------------------------------------------


def synth(tmp, src, n=40):
    """A copy of `src` with a synthetic .hs beside it, for the modes the corpus
    cannot exercise.

    `/data/**/*.hs` is gitignored and no .hs is committed -- correctly, since it
    is the player's own state -- so on a fresh checkout both score lists are
    empty and `MyScores` is checked at length zero.  This makes one: `n` records
    whose scores are deliberately mixed against the .ghs so that GHSList's `**`
    / `>` marking, its unmarked branch, and the blank-record case are all in the
    list at once.  The .ghs is copied unchanged; nothing here writes to data/.
    """
    dst = tmp / src.name
    shutil.copyfile(src, dst)
    ghs_src = src.with_suffix(".ghs")
    posted = read_hs(ghs_src)
    if ghs_src.exists():
        shutil.copyfile(ghs_src, dst.with_suffix(".ghs"))

    out = bytearray()
    for i in range(min(n, len(read_levels(src)))):
        best = posted[i] if i < len(posted) else None
        if i % 5 == 0:
            out += hs_bytes(0, 0, "")                      # never solved: blank
        elif i % 5 == 1 and best and best[0] > 1:
            out += hs_bytes(best[0] - 1, best[1], "AB")    # better -> ** and >
        elif i % 5 == 2 and best and best[0] > 0:
            out += hs_bytes(best[0], best[1], "CD")        # equal: not better
        elif i % 5 == 3 and best and best[0] > 0:
            out += hs_bytes(best[0], max(best[1] - 1, 0), "EF")  # same moves, fewer shots
        else:
            out += hs_bytes(999, 42, "GHIJ")               # far worse
    dst.with_suffix(".hs").write_bytes(bytes(out))
    return dst


def check_lists(godot, collections, tmp=None):
    """-> (ok, checked, failures)."""
    ok, n, fails = True, 0, []
    for rel in collections:
        lvl = rel if isinstance(rel, pathlib.Path) else ROOT / "data" / rel
        rel = str(rel) if isinstance(rel, pathlib.Path) else rel
        if not lvl.exists():
            print("  %-44s SKIP (not in this checkout)" % rel)
            continue
        out, rc = run_godot(godot, ["--check-lists", str(lvl)])
        if rc != 0 or "lists OK" not in out:
            print("  %-44s FAIL (rc=%d)" % (rel, rc))
            print("\n".join("      " + l for l in out.splitlines()[-8:]))
            ok = False
            continue

        got_rows, got_hash = {}, {}
        for line in out.splitlines():
            m = ROW_RE.match(line)
            if m:
                got_rows.setdefault(m.group("mode"), []).append(m.group("text"))
                continue
            m = HASH_RE.match(line)
            if m:
                got_hash[m.group("mode")] = (int(m.group("n")), m.group("sha"))

        files = ScoreNames(lvl)
        levels = read_levels(lvl)
        mine = read_hs(files.hs)
        posted = read_hs(files.ghs)
        line = ["  %-44s" % rel]
        for mode in ("Levels", "MyScores", "GlobalScores"):
            want = build_rows(mode, levels, mine, posted)
            wsha = hashlib.sha256("\n".join(want).encode("latin-1")).hexdigest()
            gn, gsha = got_hash.get(mode, (-1, ""))
            n += 1
            if gn == len(want) and gsha == wsha:
                line.append("%s %d ok" % (mode, len(want)))
                continue
            ok = False
            line.append("%s FAIL" % mode)
            # Localise: the first row that differs, which is what a diff of two
            # 2,030-row lists has to say to be readable at all.
            gr = got_rows.get(mode, [])
            where = "count %d vs %d" % (gn, len(want))
            for i in range(min(len(gr), len(want))):
                if gr[i] != want[i]:
                    where = ("row %d\n      godot  %r\n      python %r"
                             % (i + 1, gr[i], want[i]))
                    break
            fails.append("%s / %s: %s" % (rel, mode, where))
        print("   ".join(line))
    for f in fails:
        print("    " + f)
    return ok, n, fails


class ScoreNames:
    """AssignHSFile (LTANK2.C:1055), again."""

    def __init__(self, lvl):
        self.hs = lvl.with_suffix(".hs")
        self.ghs = lvl.with_suffix(".ghs")


def check_scores(godot):
    """-> (ok, steps)."""
    with tempfile.TemporaryDirectory(prefix="lt-hs-") as d:
        out, rc = run_godot(godot, ["--check-scores", d])
        if rc != 0 or "scores OK" not in out:
            print("  scores FAIL (rc=%d)" % rc)
            print("\n".join("      " + l for l in out.splitlines()[-10:]))
            return False, 0
        got = []
        for line in out.splitlines():
            m = SCORE_RE.match(line)
            if m:
                got.append(m)

    ok = True
    with tempfile.TemporaryDirectory(prefix="lt-hs-py-") as d:
        path = pathlib.Path(d) / "check.hs"
        hs = [0, 0, ""]                            # the `HS` global, zeroed
        for i, (label, level, moves, shots, name) in enumerate(SCORE_SCRIPT):
            won = check_high_score(path, level, moves, shots, name, hs)
            want = path.read_bytes().hex()
            if i >= len(got):
                print("  %-18s FAIL: godot printed only %d steps"
                      % (label, len(got)))
                return False, i
            g = got[i]
            if (g.group("label") != label or int(g.group("beats")) != int(won)
                    or g.group("hex") != want):
                ok = False
                print("  %-18s FAIL" % label)
                print("      godot  beats=%s %s" % (g.group("beats"), g.group("hex")))
                print("      python beats=%d %s" % (int(won), want))
            else:
                print("  %-18s ok  len=%-4d beats=%d"
                      % (label, len(want) // 2, int(won)))
    return ok, len(SCORE_SCRIPT)


WIN_RE = re.compile(
    r"^highscore level=(?P<level>\d+) personal=(?P<personal>[01]) "
    r"global=(?P<global>[01]) old=(?P<old>.*?) target=(?P<target>.*?)"
    r"(?: error=(?P<error>.*))?$")


def check_win(godot):
    """The whole win path, end to end: reaching the flag writes a .hs.

    `--check-scores` drives `HighScores.Check` directly, so it proves the
    *writer*.  What it cannot prove is that the flag case calls it -- and that
    is a live wire, because a Session built without Options deliberately posts
    nothing (`tick_check.py` replays 208 winning recordings and must leave the
    corpus alone; it wrote .hs files across six collections of data/ once).
    So: copy the flagship and its .ghs somewhere scratch, replay level 1's own
    recorded solution as a script with a live `--ini`, and check both the
    decision the game reports and the ten bytes it leaves behind.

    The solution comes from the corpus rather than being invented, so the
    expected score is a fact about data/demos/LaserTank/00001.lpb and not about
    this file.
    """
    lpb = ROOT / "data" / "demos" / "LaserTank" / "00001.lpb"
    lvl = LEVELS / "LaserTank.lvl"
    if not lpb.exists() or not lvl.exists():
        print("  win SKIP -- the flagship or its first recording is not here")
        return True, 0

    raw = lpb.read_bytes()
    n = int.from_bytes(raw[64:66], "little")
    tok = {38: "u", 40: "d", 37: "l", 39: "r", 32: "f"}
    script = "".join(tok[k] for k in raw[66:66 + n])

    with tempfile.TemporaryDirectory(prefix="lt-win-") as d:
        tmp = pathlib.Path(d)
        shutil.copyfile(lvl, tmp / lvl.name)
        ghs = lvl.with_suffix(".ghs")
        if ghs.exists():
            shutil.copyfile(ghs, tmp / ghs.name)
        ini = tmp / "lt.ini"
        ini.write_bytes(b"[DATA]\r\nPlayer=MZ\r\n")
        out, rc = run_godot(godot, ["--play", "--script", script, "--level", "1",
                                    "--levels", str(tmp / lvl.name),
                                    "--ini", str(ini), "--out", str(tmp)])
        m, moves, shots, won = None, None, None, False
        for line in out.splitlines():
            m = WIN_RE.match(line) or m
            if line.startswith("script "):
                f = dict(p.split("=", 1) for p in line.split() if "=" in p)
                moves, shots = int(f.get("moves", -1)), int(f.get("shots", -1))
                won = " WIN " in line
        hs = (tmp / "LaserTank.hs")
        got = read_hs(hs)

    if rc != 0 or m is None or moves is None:
        print("  win FAIL (rc=%d) -- no highscore line" % rc)
        print("\n".join("      " + l for l in out.splitlines()[-6:]))
        return False, 0

    ok = True
    # The score is whatever the recording scores; replay_all.py already pins
    # that against the bundled .ghs, so here it only has to be self-consistent.
    want = [(moves, shots, "MZ")]
    checks = [
        ("reached the flag", won),
        ("a personal best was posted", m.group("personal") == "1"),
        ("no write error", m.group("error") is None),
        ("the .hs holds exactly one record", len(got) == 1),
        ("...with the play's own score and initials", got[:1] == want),
        # The corpus's 00001.lpb is a human's 125/49 and the .ghs target is
        # Duck's 103/46, so the global best must *not* have fallen.  If this
        # ever flips, either the recording or the .ghs changed.
        ("the posted best survived", m.group("global") == "0"),
    ]
    for label, good in checks:
        ok &= bool(good)
        print("  %-42s %s" % (label, "ok" if good else "FAIL"))
    if not ok:
        print("      reported: %s" % m.group(0))
        print("      .hs:      %r  want %r" % (got, want))
    else:
        print("  win: %d moves / %d shots as MZ, target %s"
              % (moves, shots, m.group("target")))
    return ok, len(checks)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--lists-only", action="store_true")
    ap.add_argument("--scores-only", action="store_true")
    ap.add_argument("--win-only", action="store_true")
    ap.add_argument("--collection", action="append", default=None,
                    help="a path under data/; repeatable "
                         "(default: three, including one .LVL and one with no .ghs)")
    args = ap.parse_args(argv)

    godot = find_godot()
    if godot is None:
        print("list_check: SKIP -- Godot not found (set LT_GODOT)")
        return 2
    # `godot --path` does not build C#: without this the checks would compare
    # Python against the assembly the *previous* session built.
    _ok, warn = engines.build_godot_game()
    print("list_check: build ok%s" % (" (%d warnings)" % warn if warn else ""))

    only = args.lists_only or args.scores_only or args.win_only
    ok = True
    if not only or args.lists_only:
        print("lists -- the three dialogs' rows, rebuilt in Python:")
        cols = list(args.collection or DEFAULT_COLLECTIONS)
        with tempfile.TemporaryDirectory(prefix="lt-lists-") as d:
            tmp = pathlib.Path(d)
            # A synthetic .hs, so MyScores and GHSList's marked/unmarked
            # branches are checked at a length other than zero.
            if not args.collection:
                src = LEVELS / "LaserTank.lvl"
                if src.exists():
                    cols.append(synth(tmp, src))
            good, n, _fails = check_lists(godot, cols, tmp)
        ok = ok and good
        print("  %d list(s) compared" % n)
    if not only or args.scores_only:
        print("scores -- the .hs writer, byte for byte:")
        good, n = check_scores(godot)
        ok = ok and good
        print("  %d write(s) compared" % n)
    if not only or args.win_only:
        print("win -- reaching the flag really does post a score:")
        good, n = check_win(godot)
        ok = ok and good

    print("list_check " + ("OK" if ok else "FAILED"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
