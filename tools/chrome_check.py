#!/usr/bin/env python
"""Step 9's gate: the chrome answers the mouse, and answers it the way the keys do.

Everything the redesign drew was a *label for a key* until step 9 -- keycaps,
pills, rows, the `H`/`F1` affordances in the column -- and none of it was
clickable.  Making it clickable put a third arm in front of the window proc's
two (`MouseOperation` and the editor brush, LTANK.C:785), and that arm has three
ways to be wrong that a screenshot cannot show:

  aim        a target whose rectangle is not where its label is drawn, or is
             under something drawn after it.  Immediate mode makes the first
             unlikely -- the rectangle is handed to the draw call and to
             Hits.Add in the same breath -- and does nothing at all about the
             second, which is what the overlap check below is for.

  reach      a target too small for a finger.  The chrome scales with the window
             (Ui.Px), so the keycap that is comfortable at 1100 px is 17 px
             across at 520 -- which is the width a phone gets, and the one place
             this has to work.

  meaning    a target that hits, is big enough, and runs the wrong command.
             That is the one worth a differential, and the hit list is named so
             that it can have one: a target called `key:U` claims that clicking
             it is pressing U, so this presses U in one process, clicks the
             centre of that rectangle in another, and requires the two runs to
             end in the same state.  Two paths agreeing is evidence; one path
             agreeing with itself is not -- the rule the sprite sheets and the
             three list dialogs are held to.

  typing    step 11 put a *text field* in the level list -- the Search sub-dialog's, inlined --
             and that is the one arm of the chrome neither of the two above can
             drive: `--press` goes through the accelerator table and a field is
             not an accelerator, `--click` reaches only the hit list and the
             field is a swallow because it is always focused.  `--type` is its
             instrument and `filter_check` below is what drives it.

**It needs a window**, and there is no way around that: the hit list is built by
`_Draw` and by nothing else, so a headless run has an empty one -- which is
exactly why tools/mouse_check.py and tools/editor_check.py never had to learn
that the chrome exists.  So this opens a short-lived window per case, the way
options_check does for its pixel measurements.

    python tools/chrome_check.py            # ~110 s
    python tools/chrome_check.py -v         # every target
    python tools/chrome_check.py --no-diff  # aim and reach only, ~15 s

It rebuilds the Godot project's C# first, because `godot --path` does not.  It
neither writes nor reads the player's settings: the baseline is an INI this gate
*writes* (see write_baseline), and every case starts from a fresh copy of it
with no settings store beside it -- so each run *imports* the baseline and a
`key:N` that toggles the sound persists into the throwaway store and not into
anyone's settings.  Because the baseline cannot move under a pair of runs, a
second gate, a game left open elsewhere, or a session that happened to end on a
different collection cannot turn this red.

Exit: 0 clean, 1 a mismatch, 2 environment.
"""
import argparse
import pathlib
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
from engines import ROOT                                # noqa: E402
from atlas_check import find_godot                      # noqa: E402

GAME = ROOT / "src" / "LaserTank.Game"

# The screens worth dumping, and the flags that put the UI on each.  `--level 1`
# everywhere so the playback panel finds data/demos/LaserTank/00001.lpb; the
# level is otherwise whatever the INI last remembered, which is not something a
# gate may depend on.
SCREENS = [
    ("play",        ["--level", "1"]),
    ("stacked",     ["--level", "1", "--window", "520x760"]),
    ("help",        ["--level", "1", "--panel", "help"]),
    ("levels",      ["--level", "1", "--panel", "levels"]),
    ("collections", ["--level", "1", "--panel", "collections"]),
    ("graphics",    ["--level", "1", "--menu"]),
    ("language",    ["--level", "1", "--open-lang"]),
    ("name",        ["--level", "1", "--panel", "name"]),
    ("options",     ["--level", "1", "--panel", "options"]),
    ("playback",    ["--level", "1", "--panel", "playback"]),
    ("quit",        ["--level", "1", "--panel", "quit"]),
    ("editor",      ["--level", "1", "--editor"]),
    # The hint card is only in the column once 301 has asked for it, and it is
    # its own target (clicking it hides it again), so the screen has to be here
    # or that one rectangle is never tested.
    ("hint",        ["--level", "1", "--panel", "hint"]),
    # A panel at phone width, because that is where the floor bites: everything
    # in the chrome is scaled off the window, so a close button that is a
    # comfortable 28 px at 1100 is 19 at 520 unless Ui.Touch is on it.
    ("levels-narrow", ["--level", "1", "--panel", "levels", "--window", "520x760"]),
]

# What every screen must offer, so that a panel losing its way out is a red gate
# rather than something a player discovers on a phone.  A modal panel with no
# close button and no click-off is a trap for anyone without a keyboard.
MUST = {
    "play":        ["key:U", "key:R", "key:H", "key:L", "key:O", "key:F1"],
    "stacked":     ["key:U", "key:R", "key:H", "key:L", "key:O", "key:F1"],
    "help":        ["scrim", "close"],
    # Step 11's filter bar and scrollbar are on this list for the same reason
    # `close` is: they are the only way a player without a keyboard narrows a
    # 2,030-row table or gets to the far end of it.  Both widths, because the
    # chips shed to initials when the panel is narrow and a shed label that
    # stopped being a button would be invisible in a screenshot.
    "levels":      ["scrim", "close", "row:0", "scroll",
                    "by:title", "by:author", "unsolved",
                    "diff:1", "diff:2", "diff:3", "diff:4", "diff:5"],
    "levels-narrow": ["scrim", "close", "row:0", "scroll",
                      "by:title", "by:author", "unsolved",
                      "diff:1", "diff:2", "diff:3", "diff:4", "diff:5"],
    "collections": ["scrim", "close", "row:0"],
    "graphics":    ["scrim", "close", "row:0", "snap:1"],
    "language":    ["scrim", "close", "row:0"],
    # The name panel's two answers are on this list for the reason `close` is:
    # they are the only way a player without a keyboard commits or abandons a
    # name.  The field itself is a swallow -- it always has the caret -- so it
    # is not here and cannot be: there is nothing for a click on it to do.
    "name":        ["scrim", "close", "name:save", "name:cancel"],
    # Item 3's panel is all targets and no text: six chips, a close and the
    # scrim.  All six chips are named because each one is a *different* bit of
    # one mask plus the skip, and a row of tags that answer to one rectangle is
    # exactly the failure Ui.Touch and this list exist to catch.
    "options":     ["scrim", "close", "opt:skip",
                    "opt:rank:1", "opt:rank:2", "opt:rank:3",
                    "opt:rank:4", "opt:rank:5"],
    "playback":    ["pb:space", "pb:R", "pb:Esc"],
    "quit":        ["scrim", "quit:yes", "quit:no"],
    "editor":      ["field:1", "field:2", "field:3", "diff", "key:F1"],
}

# Targets left out of the click-vs-press differential, each for a reason:
#
#   key:F6      saves a recording -- the one binding that writes a file outside
#               the throwaway settings, and a gate that writes into out/ is a
#               gate that passes differently the second time.
#   key:F1      on the help overlay a row closes the panel and does *not* press
#               its key, which is deliberate (see DrawHelp): pressing F1 there
#               would shut the overlay and open it again.
#   key:Escape  the same row's other special case.  From the overlay it closes
#               and raises the quit prompt; from a plain screen it only raises
#               it, so the two end states differ by `help=` by design.
NO_DIFF = {"key:F6", "key:F1", "key:Escape"}

# A finger's floor in real pixels: Ui.Touch grows a hit box to 32 design px, and
# the smallest UI scale is 0.85, so 26 is what survives at the narrow end.  This
# is the assertion that Ui.Touch was actually applied where it is needed.
MIN_PX = 26


def run(godot, args, ini):
    """One short-lived window.  -> the lines it printed."""
    p = subprocess.run([godot, "--path", str(GAME), "--", "--ini", str(ini)] + args,
                       capture_output=True, timeout=300, cwd=str(ROOT))
    out = (p.stdout + p.stderr).decode("utf-8", "replace")
    return out.splitlines()


def dump(godot, flags, ini):
    """-> ([(name, (x, y, w, h), live)] in draw order, the window rect)."""
    hits, window = [], None
    for line in run(godot, flags + ["--dump-hits"], ini):
        if line.startswith("chrome "):
            for field in line.split():
                if field.startswith("window="):
                    window = tuple(int(v) for v in field[7:].split(","))
        elif line.startswith("hit "):
            _, name, rect, kind = line.split()
            hits.append((name, tuple(int(v) for v in rect.split(",")), kind == "live"))
    return hits, window


def topmost(hits, at):
    """Hits.Click, in Python.  The last rectangle drawn that contains the point
    takes it, which is the whole of that function -- written out again here
    rather than imported, for the reason every gate in this directory writes its
    half out again."""
    x, y = at
    for name, (rx, ry, rw, rh), _live in reversed(hits):
        if rx <= x < rx + rw and ry <= y < ry + rh:
            return name
    return None


def centre(rect):
    x, y, w, h = rect
    return (x + w // 2, y + h // 2)


def clicked_state(lines):
    """The state a `--click` run ended in.  The line is
    `click X,Y Button took=.. on=NAME <state>`, and what is compared is the
    state, so the `on=` field is dropped with the fields before it."""
    for line in lines:
        if line.startswith("click "):
            rest = line.split("on=", 1)
            if len(rest) == 2:
                return rest[1].split(" ", 1)[1]
    return None


def pressed_state(lines):
    for line in lines:
        if line.startswith("press "):
            return line.split(" ", 2)[2]
    return None


BASELINE = """[SCREEN]
Graphics_Dir=%s
Size=3
Graphics_Mode=0
[DATA]
Language=en
[OPT]
Animation=Yes
Sound=No
"""


# **The baseline is a file this gate writes, not a copy of the player's.**
#
# It used to be a copy, and the copy carried whatever the last session left in
# LaserTank.ini -- which is not a detail, because three of those fields decide
# what this gate is even looking at:
#
#   RLLFilename  the collection.  `--panel playback` wants
#                `data/demos/<collection>/00001.lpb`, which exists for two
#                collections out of twenty-three, so a tree left on Challenge-IV
#                failed three playback targets.
#   Sound        the `MUTED` pill is drawn only when sound is off, and a pill is
#                a clickable target -- so turning sound on in the game silently
#                removed one target from `play`, `stacked`, `editor` and `hint`
#                and four pairs from the differential.
#   Size         the window preset, and every target's size scales off the
#                window -- a small enough one puts them under the MIN_PX floor
#                this gate exists to enforce.
#
# Each of those is a red screen nobody had touched, which reads as someone
# else's regression rather than as the environment, and none of them reproduces
# on another machine.  So the values are written here instead of inherited.
#
# **Step 16 left this an INI on purpose.**  The port's settings live in a typed
# `settings.json` now and the INI is a one-way importer, read only when there is
# no store beside it -- so `fresh()` deletes the store as well as replacing the
# INI, and every one of the forty-odd runs below re-imports this baseline from
# scratch.  Writing the baseline as JSON instead would have been a second copy
# of the record's field names living in a gate; this way the gate states the
# starting position in the 2010 binary's own vocabulary, which is what it was
# already doing, and exercises the importer on the way past.
# `Graphics_Dir` is the one field taken from the tree rather than invented,
# because it is a path and this is the only place that knows it; `Graphics_Mode`
# is 0, the internal sheet, so the gate does not depend on a .ltg being present.
#
# The rule, which step 11 got half of: it is not enough that a gate writes
# nothing of the player's -- **it must not read what the player can change
# either.**
def write_baseline(path):
    path.write_text(BASELINE % (ROOT / "data" / "graphics"),
                    encoding="latin-1")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("-v", "--verbose", action="store_true", help="print every target")
    ap.add_argument("--no-diff", action="store_true",
                    help="aim and reach only -- no per-target windows")
    args = ap.parse_args()

    godot = find_godot()
    if not godot:
        print("chrome SKIPPED: no Godot found (set LT_GODOT)")
        return 2
    engines.build_godot_game()
    print("build: LaserTank.Game ok")

    tmp = pathlib.Path(tempfile.mkdtemp(prefix="chrome_check_"))
    ini = tmp / "LaserTank.ini"

    # **The baseline is a snapshot, not the player's live file.**  `--ini` makes
    # the options writable (BoardView: an explicit --ini means live), which is
    # what lets a `key:N` pair be compared at all -- but it also means the
    # baseline has to be a file nothing else can move.  Copying LaserTank.ini per
    # case instead cost a flaky red: a click run and its press run are two
    # processes a second apart, and anything that rewrote that file in between
    # -- another gate, a game left open -- gave them different starting sound
    # and animation and the pair "disagreed" about a key that does neither.
    snapshot = tmp / "base.ini"
    write_baseline(snapshot)
    # What `--ini <path>` makes the game use as its store: Paths.SettingsBeside.
    store = ini.with_suffix(".settings.json")

    def fresh():
        """Every case starts from the same INI and no store, so every case
        imports the same baseline.  None of them touches the player's settings
        -- nor reads them.  See write_baseline."""
        shutil.copy(snapshot, ini)
        store.unlink(missing_ok=True)

    fails = []
    screens = 0
    print("targets:")
    for name, flags in SCREENS:
        fresh()
        hits, window = dump(godot, flags, ini)
        if window is None:
            fails.append("%s: the game printed no chrome line" % name)
            continue
        screens += 1
        live = [(n, r) for n, r, is_live in hits if is_live]
        names = [n for n, _ in live]

        for want in MUST.get(name, []):
            if want not in names:
                fails.append("%s: no %s target -- has %s"
                             % (name, want, sorted(set(names))))

        wx, wy, ww, wh = window
        for n, rect in live:
            x, y, w, h = rect
            if x < wx or y < wy or x + w > wx + ww or y + h > wy + wh:
                fails.append("%s: %s at %s is outside the window %s"
                             % (name, n, rect, window))
            # A scrim is the window and a row is as wide as its panel; what has
            # to clear the floor is a target a finger has to aim at.
            if n != "scrim" and not n.startswith("row:") \
                    and (w < MIN_PX or h < MIN_PX):
                fails.append("%s: %s is %dx%d, under the %d px floor"
                             % (name, n, w, h, MIN_PX))
            # The scrim is the exception and is meant to be: it is the whole
            # window, registered first so that everything else is drawn on top
            # of it, and its centre is under the panel by construction.  What is
            # checked of it is that it is *there* (MUST) and that clicking the
            # corner of the window reaches it, below.
            got = topmost(hits, centre(rect))
            if n != "scrim" and got != n:
                fails.append("%s: the centre of %s lands on %s -- they overlap"
                             % (name, n, got))
        if "scrim" in names:
            corner = topmost(hits, (wx + 2, wy + wh - 2))
            if corner != "scrim":
                fails.append("%s: the window's corner lands on %s, not the scrim "
                             "-- there is no click-off" % (name, corner))
        if args.verbose:
            print("  %-12s %d live, %d swallowed"
                  % (name, len(live), len(hits) - len(live)))
            for n, rect in live:
                print("      %-14s %s" % (n, rect))
        else:
            print("  %-12s %2d live   ok" % (name, len(live)))

    if args.no_diff:
        shutil.rmtree(tmp, ignore_errors=True)
        return report(fails, screens, 0)

    # ---- the differential: clicking `key:X` is pressing X.
    print("click vs press:")
    pairs = 0
    for name, flags in SCREENS:
        fresh()
        hits, _ = dump(godot, flags, ini)
        want, done = [], set()
        for n, rect, is_live in hits:
            if not is_live or not n.startswith("key:") or n in NO_DIFF or n in done:
                continue
            done.add(n)
            want.append((n, rect))
        for key, rect in want:
            cx, cy = centre(rect)
            fresh()
            click = clicked_state(run(godot, flags + ["--click", "%d,%d" % (cx, cy)],
                                      ini))
            # **The baseline is the same screen, with one exception.**  A chrome
            # click goes to whichever accelerator table is live (BoardView.Press:
            # ACC1 while playing, ACC2 in the editor), so the key it has to equal
            # is the key pressed *where the player is standing* -- pressing O on
            # the play screen opens 108 and pressing it in the editor is
            # swallowed, and both of those are right.
            #
            # The help overlay is the exception because clicking one of its rows
            # means "close the overlay and then press the key", so what it has to
            # equal is that key pressed with no overlay up.
            fresh()
            base = ["--level", "1"] if name == "help" else flags
            press = pressed_state(run(godot, base + ["--press", key], ini))
            pairs += 1
            if click is None or press is None:
                fails.append("%s/%s: no state line (click=%r press=%r)"
                             % (name, key, click, press))
            elif click != press:
                fails.append("%s/%s: clicking it is not pressing it\n"
                             "      click %s\n      press %s" % (name, key, click, press))
            elif args.verbose:
                print("      %-12s %-12s ok" % (name, key))
        if want:
            print("  %-12s %2d binding(s) clicked and pressed" % (name, len(want)))

    fails += filter_check(godot, ini, fresh)
    fails += name_check(godot, ini, fresh)

    shutil.rmtree(tmp, ignore_errors=True)
    return report(fails, screens, pairs)


# The filter field is the one arm of the chrome neither of the two above can
# reach: `--press` goes through the accelerator table and `--click` reaches only
# what the hit list holds, and the field is a swallow because it is always
# focused.  `--type` is its instrument and this is what drives it.
#
# **What is asserted is the row count**, which is the one number all four filter
# fields land in -- an empty query, a substring that matches some, one that
# matches none, the digits that are the original's own direct level-number entry
# (ID_LOADLEV_02, folded into the field), and a query backspaced away again.
#
# **`\b` is two characters on this command line, not a control code.**  `--type`
# decodes it (BoardView.Unescape), because a real backspace does not survive the
# shell, this file's own argument quoting and Godot's command-line split.  Found
# the ugly way: a backspace that silently did nothing while the digits beside it
# went green.
FILTERS = [
    (r"\b",               lambda n, a: n == a,      "an empty field lists everything"),
    ("sokoban",           lambda n, a: 0 < n < 50,  "a substring narrows it"),
    ("zzzznotalevelname", lambda n, a: n == 0,      "a miss lists nothing"),
    ("1760",              lambda n, a: n >= 1,      "digits reach a level number"),
    ("sokoban" + r"\b" * 7, lambda n, a: n == a,
     "backspacing the query away restores every row"),
]


def filter_check(godot, ini, fresh):
    """`--type` into the level list, and the row count it answers with.

    The expectations are bounds rather than exact counts on purpose: they are
    about LaserTank.lvl, which is corpus data, and a gate that pinned 7 would go
    red the day someone adds a level called Sokoban.  The total is read off the
    same line rather than hard-coded, for the same reason.
    """
    print("the filter field:")
    out = []
    flags = ["--level", "1", "--panel", "levels", "--window", "1180x820"]
    for query, ok, what in FILTERS:
        fresh()
        n, total = typed_rows(run(godot, flags + ["--type", query], ini))
        if n is None:
            out.append("filter %r: no type line" % query)
        elif not ok(n, total):
            out.append("filter %r: %d rows of %d -- %s" % (query, n, total, what))
        else:
            print("  %-22s %5d of %d rows   ok" % (repr(query), n, total))
    return out


# The other text field, and the one assertion the merge is actually about.
#
# `[DATA] Player` and `[DATA] Record Author` are one value in this port (see
# Options.Name), which means a long name and the four characters a `.hs` record
# can hold are now derived from each other rather than typed separately -- so
# the cut is the thing to pin.  `--type` reaches the field the same way it
# reaches the filter, and the panel logs both halves.
NAMES = [
    ("MZ",                   "MZ",         "MZ"),
    ("Michal Z",             "Michal Z",   "Mich"),
    ("Michal Z" + r"\b" * 2, "Michal",     "Mich"),
    # Thirty is RecordBox's own width (Options.NameMax) and the field stops
    # there rather than letting the surplus reach a file that would drop it.
    ("x" * 40,               "x" * 30,     "xxxx"),
]


def name_check(godot, ini, fresh):
    """`--type` into the name panel: what it holds, and what a .hs would get."""
    print("the name field:")
    out = []
    flags = ["--level", "1", "--panel", "name"]
    for typed, want_text, want_initials in NAMES:
        fresh()
        text, initials = typed_name(run(godot, flags + ["--type", typed], ini))
        if text is None:
            out.append("name %r: no type line" % typed)
        elif text != want_text or initials != want_initials:
            out.append("name %r: text=%r initials=%r -- wanted %r / %r"
                       % (typed, text, initials, want_text, want_initials))
        else:
            print("  %-22s %-12s scores as %s   ok"
                  % (repr(typed), repr(text), initials))
    return out


def typed_name(lines):
    """`type <s> name=True initials=I text=T` -> (T, I).

    **`text=` is last and is taken as the whole rest of the line**, because a
    name has spaces in it -- which is the one thing this field can hold that
    none of the others can, and the reason the game prints it there."""
    for line in lines:
        if line.startswith("type ") and " name=True" in line:
            initials = ""
            for field in line.split():
                if field.startswith("initials="):
                    initials = field[len("initials="):]
            return line.split(" text=", 1)[1], initials
    return None, None


def typed_rows(lines):
    """`type <s> list=True rows=N of=M filtering=B q=Q` -> (N, M)."""
    for line in lines:
        if line.startswith("type "):
            got = {}
            for field in line.split():
                for key in ("rows=", "of="):
                    if field.startswith(key):
                        got[key[:-1]] = int(field[len(key):])
            if "rows" in got and "of" in got:
                return got["rows"], got["of"]
    return None, None


def report(fails, screens, pairs):
    if fails:
        print()
        for f in fails:
            print("  FAIL  " + f)
        print("chrome_check FAILED -- %d problem(s)" % len(fails))
        return 1
    print("chrome_check OK -- %d screens, %d click/press pairs" % (screens, pairs))
    return 0


if __name__ == "__main__":
    sys.exit(main())
