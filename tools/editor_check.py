#!/usr/bin/env python
"""Phase 5 step 5's gate: the level editor, and the `.lvl` it writes.

    python tools/editor_check.py                    # ~60 s
    python tools/editor_check.py --runs 3000        # a campaign
    python tools/editor_check.py --board-only
    python tools/editor_check.py --replay 7 "<05l33s44RRUD"

Two halves, because the editor has two outputs and they fail differently.

**The board half** is a trace diff, and it is a real one: `ChangeGO` is
LTANK2.C:809, compiled verbatim by `oracle/build.sh`, so the C being diffed
against here was written in 2002 and this project has never read it into
anything.  `--edit` drives it with a token stream -- the same trick `--script`
was for undo -- and traces the board after every token, so a wrong rotation
table or a shift that forgets to move `BMF` shows up on the step that caused it
rather than as a wrong answer ten edits later.

The commands *around* `ChangeGO` -- Clear Field, the four Shifts, the tunnel
wait-bit strip on the way in -- are LTANK.C window-proc cases, so they are
transliterated twice and diffed against each other, exactly as step 4's `z`,
`c` and `v` were.  What the diff proves for those is agreement, not truth; what
it proves for `ChangeGO` is truth.

**The file half** is the step's exit criterion, and it is not a trace at all:

  1. *Identity.*  Load a level, open the editor, change nothing, save.  The 576
     bytes must come back **byte for byte**, which they only do because the
     writer rewrites the record it read rather than re-encoding decoded
     strings.  Most `.lvl` files in the wild have bytes after a name's
     terminator -- the tail of some earlier, longer name -- and a re-encoding
     writer silently rewrites those on every unedited level in the collection.
     Constraint 2 says these files stay *writable*; this is what writable has
     to mean.
  2. *The write widths.*  `GetWindowText(Ed1, CurRecData.LName, 30)` copies at
     most 29 characters and a terminator into a 31-byte field, so a shorter
     name does not erase the longer one behind it.  Python builds the expected
     576 bytes and compares.
  3. *The playfield tie-in.*  The saved record's first 256 bytes must equal the
     board the trace ended on, with the tank stamped back in at
     `PF[Tank.X][Tank.Y] = 1`.  This is what links the two halves: the traced
     board is already proven identical to the oracle's, so the file is checked
     against the C by transitivity rather than against a second guess at what
     the C would have done.
  4. *The gap.*  Saving level N into a shorter file writes zero-filled records
     in front of it, because seeking past the end and writing zero-fills.  The
     2010 binary does this and lists the filler levels; so does this.
  5. *The 2010 binary opens it.*  The oracle **is** the 2010 loader --
     `LoadNextLevel` out of LTANK2.C -- so the saved file is loaded by the
     oracle and by the port and the two boards are diffed.  A file the oracle
     loads is a file the original opens, and that is as close as this project
     can get to the criterion without running the 2010 binary.

**The game half** is the third implementation, and it is there for the reason
step 4 gave: two engines prove the arithmetic, a third proves that what the UI
invokes *is* that arithmetic.  `godot -- --editor --edit STR --save` drives the
palette, the mouse and command 603 through `EditMode` -- the same object the
player's clicks reach -- and the 576 bytes it writes must equal the ones
`lasertank-core --edit --save` writes.  It is skipped, loudly, when Godot is not
installed.

Exit: 0 all green, 1 a divergence or a bad file, 2 environment, 3 cosmetic-only.
"""
import argparse
import filecmp
import pathlib
import random
import shutil
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
from engines import ROOT, EditCase                      # noqa: E402

FLAGSHIP = ROOT / "data" / "levels" / "LaserTank.lvl"
HEX = "0123456789abcdef"
REC = 576

# Offsets inside a 576-byte record; PROGRESS.md, "Data formats".
NAME, NAMELEN = 256, 31
HINT, HINTLEN = 287, 256
AUTHOR, AUTHORLEN = 543, 31
DIFF = 574
# The original's GetWindowText counts, terminator included.
NAME_ENTRY, HINT_ENTRY = 30, 255


def collections():
    """Every .lvl under data/, flagship first."""
    out = [FLAGSHIP]
    for p in sorted((ROOT / "data").rglob("*")):
        if p.suffix.lower() == ".lvl" and p != FLAGSHIP:
            out.append(p)
    return out


# --- the board half ---------------------------------------------------------


def tokens(script):
    """The script split into tokens, so the shrinker never breaks one up."""
    out, i = [], 0
    while i < len(script):
        c = script[i]
        if c in "<>lrspqP" and i + 3 <= len(script):
            out.append(script[i:i + 3])
            i += 3
        elif c == "t" and i + 2 <= len(script):
            out.append(script[i:i + 2])
            i += 2
        else:
            out.append(c)
            i += 1
    return out


def script(rng, n):
    """One edit script of about `n` tokens.

    Weighted towards the two things that actually have arithmetic in them --
    painting (which is `ChangeGO`) and shifting (which wraps two grids and a
    tank) -- with the selector deliberately allowed to reach 26 (the tunnel,
    which opens a dialog and writes an encoded cell) and 27 (one past the last
    sprite, which the palette's own bound admits and `GetOBM` answers with 1).
    """
    def cell():
        return rng.choice(HEX) + rng.choice(HEX)

    out = []
    while len(out) < n:
        out += rng.choice((
            ["<%02x" % rng.randint(0, 27)],
            [">%02x" % rng.randint(0, 27)],
            ["<%02x" % rng.randint(0, 25)] + ["l" + cell() for _ in range(rng.randint(1, 4))],
            ["<1a", "t%x" % rng.randint(0, 7), "l" + cell()],      # a tunnel
            ["<1a", "p" + cell()],                                 # ... dragged: a no-op
            ["<1b", "l" + cell()],                                 # object 27
            ["l" + cell()],
            ["r" + cell()],
            ["s" + cell()],                                        # rotate
            ["s" + cell(), "s" + cell(), "s" + cell()],            # ... round its family
            ["p" + cell() for _ in range(rng.randint(2, 5))],      # a drag
            ["q" + cell() for _ in range(rng.randint(2, 5))],
            ["P" + cell()],                                        # Shift+drag: nothing
            ["<01", "l" + cell()],                                 # move the tank
            [rng.choice("RLUD")],
            [rng.choice("RLUD")] * rng.randint(2, 4),
            ["C"],
            ["E"],
            [rng.choice("12345")],
        ))
    return "".join(out[:n])


def check(case, scratch, field=True, bmf=True):
    a, b = engines.run_pair(case, scratch.a, scratch.b, field=field, bmf=bmf)
    return engines.compare(a, b), a, b


def shrink(case, scratch, sig, budget=300):
    """A shorter script with the same signature; token-wise, best effort."""
    best, runs = tokens(case.edit), 0

    def diverges(ts):
        nonlocal runs
        if not ts or runs >= budget:
            return False
        runs += 1
        div, _, _ = check(case._replace(edit="".join(ts)), scratch)
        return div is not None and div.sig == sig

    lo, hi = 1, len(best)
    while lo < hi:
        mid = (lo + hi) // 2
        if diverges(best[:mid]):
            hi = mid
        else:
            lo = mid + 1
    if lo <= len(best) and diverges(best[:lo]):
        best = best[:lo]

    step = max(1, len(best) // 2)
    while step >= 1 and runs < budget:
        i, moved = 0, False
        while i < len(best):
            cand = best[:i] + best[i + step:]
            if diverges(cand):
                best, moved = cand, True
            else:
                i += step
        if not moved:
            step //= 2
    return "".join(best), runs


def board_half(args):
    lvl = args.lvl
    n = engines.count_levels(lvl)
    top = min(n, args.levels_max) if args.levels_max else n
    rng = random.Random(args.seed)
    cases = [EditCase(lvl, args.level or rng.randint(1, top),
                      script(random.Random(rng.randrange(1 << 30)), args.tokens))
             for _ in range(args.runs)]

    print("editor_check: board -- %d scripts of %d tokens over %s (%d levels)"
          % (len(cases), args.tokens, lvl.name, top))

    found = []

    def work(case):
        with engines.Scratch("edit") as sc:
            div, _, _ = check(case, sc)
            return case, div

    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for i, (case, div) in enumerate(pool.map(work, cases), 1):
            if div is not None:
                found.append((case, div))
            if i % 100 == 0 or i == len(cases):
                print("  %4d/%d  %d divergence(s)" % (i, len(cases), len(found)))

    if not found:
        print("  board OK -- %d scripts, 0 divergences" % len(cases))
        return 0

    by_sig = {}
    for case, div in found:
        by_sig.setdefault(div.sig, []).append((case, div))
    print("\n  board FAILED -- %d divergences, %d distinct signature(s)"
          % (len(found), len(by_sig)))
    cosmetic_only = True
    for sig, hits in by_sig.items():
        case, div = hits[0]
        cosmetic_only = cosmetic_only and div.cosmetic
        s = case.edit
        if not args.no_shrink:
            with engines.Scratch("edit") as sc:
                s, runs = shrink(case, sc, sig)
            print("\n    [%s] %d hit(s); shrunk %d -> %d tokens in %d runs"
                  % (sig, len(hits), len(tokens(case.edit)), len(tokens(s)), runs))
        else:
            print("\n    [%s] %d hit(s)" % (sig, len(hits)))
        print("      python tools/editor_check.py --replay %d %s" % (case.level, s))
        print("      " + "\n      ".join(div.detail.splitlines()[:8]))
    return 3 if cosmetic_only else 1


# --- the file half ----------------------------------------------------------


def core(*argv):
    """The port, with whatever argv this check needs; -> CompletedProcess."""
    return subprocess.run([str(engines.CORE)] + [str(a) for a in argv],
                          capture_output=True, text=True, cwd=str(ROOT))


def record(path, level):
    with open(path, "rb") as f:
        f.seek((level - 1) * REC)
        return f.read(REC)


def field_bytes(rec, off, entry, text):
    """What `SetWindowText` + `GetWindowText(h, field, entry)` leaves behind:
    at most entry-1 bytes and one NUL, and **nothing after that** -- the rest of
    the field keeps whatever the record already held."""
    b = bytearray(rec)
    enc = text.encode("latin-1")[:entry - 1]
    b[off:off + len(enc)] = enc
    b[off + len(enc)] = 0
    return bytes(b)


def fail(msgs, what):
    msgs.append(what)
    print("  FAIL  " + what)


def file_half(args):
    rng = random.Random(args.seed + 1)
    msgs = []
    cols = collections()
    tmp = pathlib.Path(tempfile.mkdtemp(prefix="editfile-"))
    print("editor_check: files -- %d collections" % len(cols))
    try:
        # 1. identity: re-save an unedited level, byte for byte.
        n_id = 0
        for src in cols:
            total = src.stat().st_size // REC
            if total == 0:
                continue
            copy = tmp / ("id-" + src.name)
            shutil.copyfile(src, copy)
            picks = sorted({1, total} | {rng.randint(1, total)
                                         for _ in range(min(6, total))})
            for lv in picks:
                p = core("--levels", src, "--level", lv, "--edit", "",
                         "--save", copy, "--save-level", lv, "--quiet")
                if p.returncode != 0:
                    fail(msgs, "identity: --edit failed on %s level %d: %s"
                               % (src.name, lv, p.stderr.strip()))
                n_id += 1
            if not filecmp.cmp(src, copy, shallow=False):
                bad = [lv for lv in range(1, total + 1)
                       if record(src, lv) != record(copy, lv)]
                fail(msgs, "identity: %s changed at level(s) %s"
                           % (src.name, bad[:8]))
        print("  identity  %d re-saves over %d collections, all byte-identical"
              % (n_id, len(cols)))

        # 2. the write widths.
        src, lv = FLAGSHIP, 7
        base = record(src, lv)
        out = tmp / "widths.lvl"
        shutil.copyfile(src, out)
        long_name = "X" * 40                      # longer than the field
        short = "ab"
        p = core("--levels", src, "--level", lv, "--edit", "",
                 "--save", out, "--save-level", lv,
                 "--set-name", long_name, "--set-author", "AUTHOR-LONG-NAME-HERE",
                 "--set-hint", "H" * 300, "--quiet")
        want = field_bytes(base, NAME, NAME_ENTRY, long_name)
        want = field_bytes(want, AUTHOR, NAME_ENTRY, "AUTHOR-LONG-NAME-HERE")
        want = field_bytes(want, HINT, HINT_ENTRY, "H" * 300)
        got = record(out, lv)
        if got[NAME:DIFF] != want[NAME:DIFF]:
            fail(msgs, "widths: a truncated write does not match "
                       "GetWindowText's own limits")
        # ... and now a *shorter* name over that longer one, which must leave
        # the tail of the longer one in the file.
        p = core("--levels", out, "--level", lv, "--edit", "",
                 "--save", out, "--save-level", lv, "--set-name", short, "--quiet")
        want2 = field_bytes(got, NAME, NAME_ENTRY, short)
        got2 = record(out, lv)
        if got2[NAME:NAME + NAMELEN] != want2[NAME:NAME + NAMELEN]:
            fail(msgs, "widths: a shorter name did not leave the longer one's tail")
        if got2[NAME + len(short) + 1] != ord("X"):
            fail(msgs, "widths: the byte after the new terminator was erased")
        print("  widths    name/author/hint truncate and overwrite as GetWindowText does")

        # 3 + 4 + 5. an edited level: the playfield tie-in, the gap, the reload.
        n_edit = 0
        with engines.Scratch("editfile") as sc:
            for i in range(args.file_runs):
                lv = rng.randint(1, engines.count_levels(FLAGSHIP))
                sc_edit = script(random.Random(rng.randrange(1 << 30)), 24)
                target = rng.randint(1, 6)
                out = tmp / ("save-%d.lvl" % i)
                p = core("--levels", FLAGSHIP, "--level", lv, "--edit", sc_edit,
                         "--save", out, "--save-level", target,
                         "--trace", sc.b, "--field", "--quiet")
                if p.returncode != 0:
                    fail(msgs, "save: --edit failed: %s" % p.stderr.strip())
                    continue

                # 4. the gap: the file is exactly `target` records, and every
                # record in front of the one written is all zeros.
                if out.stat().st_size != target * REC:
                    fail(msgs, "gap: %s is %d bytes, expected %d"
                               % (out.name, out.stat().st_size, target * REC))
                for g in range(1, target):
                    if record(out, g) != b"\0" * REC:
                        fail(msgs, "gap: filler level %d is not zero-filled" % g)

                # 3. the playfield tie-in: the saved PF is the traced board with
                # the tank stamped back in.
                _, ticks, _ = engines.read_trace(sc.b)
                last = dict(tok.split("=", 1)
                            for tok in ticks[-1].split() if "=" in tok)
                pf = bytearray.fromhex(last["PF"])
                tx, ty = (int(v) for v in last["T"].split(",")[:2])
                pf[tx * 16 + ty] = 1
                if record(out, target)[:256] != bytes(pf):
                    fail(msgs, "playfield: the saved board is not the traced one "
                               "(level %d, edit %s)" % (lv, sc_edit))

                # 5. the oracle -- the 2010 loader -- opens what was written,
                # and sees the same board the port sees.
                case = EditCase(out.resolve(), target, "")
                div, a, b = check(case, sc)
                if a.rc != 0:
                    fail(msgs, "reload: the oracle rejected %s level %d: %s"
                               % (out.name, target, a.stderr.strip()))
                elif div is not None:
                    fail(msgs, "reload: engines disagree on the saved %s level %d [%s]"
                               % (out.name, target, div.sig))
                n_edit += 1
        print("  saves     %d edited levels: gap filled, board tied to the trace, "
              "oracle loads them" % n_edit)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    return 1 if msgs else 0


# --- the game half ----------------------------------------------------------


def game_half(args):
    """The same edit, saved by the game and by the headless driver.

    Both write into their own copy of the collection, made outside `data/` on
    purpose: `EditMode.Save` copies a level that came out of the corpus into
    `out/levels/` rather than writing over it (see EditMode's header), and a
    gate must exercise the ordinary in-place path rather than that guard.
    """
    from atlas_check import find_godot                    # noqa: E402

    exe = find_godot()
    if not exe:
        print("editor_check: game -- SKIP, Godot not found (set LT_GODOT)")
        return 0
    # `godot --path` does not build C#: without this the run tests whatever
    # assembly the previous session left in .godot/mono/temp/bin.
    _ok, warn = engines.build_godot_game()
    print("editor_check: game -- build ok%s"
          % ("" if not warn else "   %d warning(s)" % warn))

    rng = random.Random(args.seed + 2)
    tmp = pathlib.Path(tempfile.mkdtemp(prefix="editgame-"))
    msgs = []
    try:
        for i in range(args.game_runs):
            lv = rng.randint(1, engines.count_levels(FLAGSHIP))
            sc_edit = script(random.Random(rng.randrange(1 << 30)), 18)
            a = tmp / ("game-%d.lvl" % i)
            b = tmp / ("cli-%d.lvl" % i)
            shutil.copyfile(FLAGSHIP, a)
            shutil.copyfile(FLAGSHIP, b)

            g = subprocess.run(
                [exe, "--headless", "--path",
                 str(ROOT / "src" / "LaserTank.Game"), "--",
                 "--editor", "--edit", sc_edit, "--save",
                 "--levels", str(a), "--level", str(lv)],
                capture_output=True, text=True, cwd=str(ROOT), timeout=300)
            if "editor-save saved" not in (g.stdout + g.stderr):
                fail(msgs, "game: no save on level %d (%s)"
                           % (lv, (g.stdout + g.stderr).strip()[-200:]))
                continue
            c = core("--levels", b, "--level", lv, "--edit", sc_edit,
                     "--save", b, "--save-level", lv, "--quiet")
            if c.returncode != 0:
                fail(msgs, "game: the headless driver failed: %s" % c.stderr.strip())
                continue
            if record(a, lv) != record(b, lv):
                fail(msgs, "game: the game's save differs from the driver's "
                           "(level %d, edit %s)" % (lv, sc_edit))
            elif not filecmp.cmp(a, b, shallow=False):
                fail(msgs, "game: the two saves differ outside level %d" % lv)
        print("  game      %d edits saved through EditMode match the driver, "
              "byte for byte" % args.game_runs)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return 1 if msgs else 0


# --- the campaign -----------------------------------------------------------


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--runs", type=int, default=400,
                    help="edit scripts to try (default 400)")
    ap.add_argument("--file-runs", type=int, default=25,
                    help="saved levels to check (default 25)")
    ap.add_argument("--tokens", type=int, default=24,
                    help="tokens per script (default 24)")
    ap.add_argument("--collection", default=None,
                    help="a .lvl under data/, or an absolute path")
    ap.add_argument("--level", type=int, default=0)
    ap.add_argument("--levels-max", type=int, default=0)
    ap.add_argument("--seed", type=int, default=6)
    ap.add_argument("--jobs", type=int, default=8)
    ap.add_argument("--no-shrink", action="store_true")
    ap.add_argument("--game-runs", type=int, default=6,
                    help="edits saved through the game (default 6; Godot is slow "
                         "to start, so this half is small on purpose)")
    ap.add_argument("--board-only", action="store_true")
    ap.add_argument("--files-only", action="store_true")
    ap.add_argument("--no-game", action="store_true",
                    help="skip the Godot half")
    ap.add_argument("--replay", nargs=2, metavar=("LEVEL", "EDIT"),
                    help="re-check one edit script and report it")
    args = ap.parse_args(argv)

    args.lvl = FLAGSHIP
    if args.collection:
        p = pathlib.Path(args.collection)
        args.lvl = p if p.is_absolute() else ROOT / "data" / args.collection
        if not args.lvl.exists():
            args.lvl = ROOT / "data" / "levels" / args.collection
    if not args.lvl.exists():
        print("editor_check: no such collection: %s" % args.lvl)
        return 2
    try:
        engines.require_engines()
    except SystemExit as ex:
        print(ex)
        return 2

    if args.replay:
        case = EditCase(args.lvl, int(args.replay[0]), args.replay[1])
        with engines.Scratch("edit") as sc:
            div, a, b = check(case, sc)
            print(" ".join(engines.command(engines.ORACLE, case, sc.a,
                                           field=True, bmf=True)))
            print(" ".join(engines.command(engines.CORE, case, sc.b,
                                           field=True, bmf=True)))
            print(engines.difftrace_report(sc.a, sc.b)[1])
        return 0 if div is None else (3 if div.cosmetic else 1)

    rc = 0
    if not args.files_only:
        rc = max(rc, board_half(args))
    if not args.board_only:
        rc = max(rc, file_half(args))
        if not args.no_game:
            rc = max(rc, game_half(args))
    print("editor_check " + ("OK" if rc == 0 else
                             "COSMETIC" if rc == 3 else "FAILED"))
    return rc


if __name__ == "__main__":
    sys.exit(main())
