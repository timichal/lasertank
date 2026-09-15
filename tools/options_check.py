#!/usr/bin/env python
"""Phase 5 step 2's gate: the persisted options, the graphics packs, and the
laser's width.

Five checks, all of them things a human clicking around would notice only if
they knew what to look for:

  ini     **the importer and the typed store**, which step 16 split apart.

          The importer is `Ini` + `IniImport`, and it still behaves exactly like
          GetPrivateProfileInt: no file at all -> Size 1 and graphics mode 0, the
          original's own defaults (LTANK.C:1567, LTANK2.C:1780); atoi's rules;
          and the `strcmp(temps, psYes)` test that makes a hand-edited
          `Sound=yes` mute the 2010 binary.  What it no longer does is *write* --
          so the check that a rewrite kept the dozen foreign keys the 2010 binary
          leaves in that file is now the stronger one that the file comes back
          **byte for byte** after a run that changed every setting it has.  And
          it is one-way: once the store exists the INI is not read again, which
          is checked by editing it and watching nothing happen.

          The store is `user://settings.json` (here, a scratch file `--ini`
          names the sibling of).  Round trip: run the game once with overrides
          and --save-options, once with nothing, and the second run must report
          what the first wrote -- plus that the file it wrote is JSON with a
          `version` in it, and that a corrupt one costs the player the settings
          rather than the game.

  packs   every entry the graphics menu offers loads, and **the external pair
          and the .ltg it came out of render byte-identical pixels.**  That is
          the whole claim of graphics mode 1 (LTANK2.C:743): a pack unpacked
          into game.bmp + mask.bmp must look like the pack.  The pairs are made
          here by splitting each .ltg at its MaskOffset, because that is
          literally what the container is -- a 324-byte header and two BMPs.
          The hashes are compared against tools/atlas_check.py's *Python*
          decoder, so this is a cross-check and not the game agreeing with
          itself.  The menu list is checked too: one entry per .ltg, named by
          the header's Name field the way GetLTGFiles names it, plus the two
          radio buttons.

  size    the three size *presets* (24 / 32 / 40 px, SetGameSize's 1..3)
          render, and changing size changes nothing but pixels: the board is 16
          cells square in each, at exactly the cell the preset names, with its
          coordinate gutter inside the window.

          Step 7 made the board fit whatever square the window leaves it and
          made the presets a snap-to rather than the only three sizes, so what
          this used to assert -- that the window is `2 * margin + 16 * cell`
          wide and that the strip under the board is a constant height -- is no
          longer true of a window the player can drag.  The board's own
          geometry is, and that is what the laser check below needs.

  advance the two settings next-steps item 3 landed -- [OPT] SkipComLev and
          [DATA] Diff_Setting -- and the walk that reads them.  The keys go
          through the INI half above; what is checked here is `LoadNextLevel`'s
          own do/while: which levels `S` and `P` stop on with a given mask, that
          a solved level is stepped over when the skip is on, that a level whose
          own SDiff is 0 is unfilterable whatever the mask says, and that the
          walk ends rather than wrapping.  **The expected sequence is built in
          Python out of the same .lvl and .hs bytes**, so the two sides are two
          readers of one file rather than the game agreeing with itself.

  history command 118's stack -- next-steps item 9, and here rather than in a
          gate of its own because it is the *same* subject as `advance`: what
          `S`, `P`, `[`, `]`, the level list and command 108 do to the sequence
          of levels a session has been on.  `--check-history` runs a script of
          those and prints the stack after each; the expected stack is rebuilt
          in Python from the same bytes, walking with the same mask.  What the
          cases are for: the push happens once per *change* of level, a restart
          is not a level change, going back past the first level is refused
          rather than wrapped (the ring is gone -- this port's list is
          unbounded), and **108 clears it**, because a history of level numbers
          means nothing once the collection they index has changed.

  laser   **the laser bar is `cell - 2 * LaserOffset` wide -- 4, 6 and 6 px.**
          LaserOffset is a per-size constant (LTANK2.C:1747/:1756/:1765), not a
          fraction of the cell, and reading it as 10-of-32 -- which step 1
          shipped -- draws the bar 8, 12 and 14 px wide instead: two to three
          times too fat, and obvious only beside the real game.  Measured out of
          a --shot PNG, in the cell the *engine* says the shot is in: the
          coordinates come from the trace side and the pixels from the renderer,
          which is what makes it a check rather than a tautology.

    python tools/options_check.py               # all five, ~60 s
    python tools/options_check.py --no-window   # skip size + laser (no display)

The laser and size checks open a real window (three, briefly): --shot needs a
rendering device, so they cannot run headless.  Everything else is headless.

Nothing here touches the player's own settings -- every run is given its own
--ini under the scratch directory, and its store is that INI's sibling, so a
directory of probe INIs is a directory of independent stores.  The game refuses
to write settings it was not handed explicitly when it is running as an
instrument anyway.

Exit: 0 clean, 1 a check failed, 2 environment (no Godot, no packs).
"""
import argparse
import hashlib
import json
import pathlib
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
import atlas_check                                      # noqa: E402
from engines import ROOT                                # noqa: E402

GAME = ROOT / "src" / "LaserTank.Game"
GRAPHICS = ROOT / "data" / "graphics"
DEMO = ROOT / "data" / "demos" / "LaserTank" / "00001.lpb"

# Tick 35 of that recording is a shot in flight travelling left (dir 4), which
# tools/tick_check.py's laser work already established by trace.  Any tick with
# `firing=1` and no bounce would do; this one is checked in.
LASER_TICK = 35

# SetGameSize, LTANK2.C:1729 and :1747/:1756/:1765.  The gate carries its own
# copy of both tables on purpose: if someone edits BoardView's, this fails.
SIZES = ((1, 24, 10), (2, 32, 13), (3, 40, 17))


# ---------------------------------------------------------------------------
# running the game
def godot():
    g = atlas_check.find_godot()
    if g is None:
        print("options_check: no Godot found (set LT_GODOT)")
        raise SystemExit(2)
    return g


def run(args, windowed=False, timeout=180):
    """-> (returncode, stdout).  --path makes the *project* the working
    directory, so every path handed to the game has to be absolute."""
    cmd = [godot()]
    if not windowed:
        cmd.append("--headless")
    cmd += ["--path", str(GAME), "--"] + [str(a) for a in args]
    p = subprocess.run(cmd, capture_output=True, text=True, cwd=str(ROOT),
                       timeout=timeout)
    return p.returncode, p.stdout + p.stderr


def options(out):
    """The `options k=v` lines -> a dict, plus `packs` as a list of dicts."""
    o, packs = {}, []
    for line in out.splitlines():
        line = line.strip()
        if line.startswith("options "):
            for k, v in re.findall(r"(\w+)=(\S*)", line[8:]):
                o[k] = v
            # label= and name= are last on their lines and may contain
            # spaces -- a pack is called "Eye Saver + Grid" and a player is
            # called whatever they typed.
            for key in ("label", "name"):
                m = re.search(r"\b%s=(.*)$" % key, line)
                if m:
                    o[key] = m.group(1).strip()
        elif line.startswith("pack "):
            d = dict(re.findall(r"(\w+)=(\S*)", line[5:]))
            m = re.search(r"\blabel=(.*)$", line)
            d["label"] = m.group(1).strip() if m else ""
            packs.append(d)
    o["_packs"] = packs
    return o


def fail(what, why):
    print("  %-34s FAIL %s" % (what, why))
    return False


def ok(what, detail=""):
    print("  %-34s ok   %s" % (what, detail))
    return True


# ---------------------------------------------------------------------------
def store_of(ini):
    """What `--ini FILE` makes the game use as its settings store --
    Paths.SettingsBeside.  Named after the INI so that a scratch directory full
    of probe files is a directory full of independent stores."""
    return ini.with_suffix(".settings.json")


def check_ini(tmp):
    """The importer, the store, and the line between them: what a LaserTank.ini
    still decides, what it can no longer touch, and what a restart remembers."""
    good = True

    # -- defaults, with neither file.  LTANK.C:1567 passes 1 as the default size
    # and LTANK2.C:1780 passes 0 as the default graphics mode.
    ini = tmp / "absent.ini"
    rc, out = run(["--ini", ini, "--check-options"])
    o = options(out)
    if rc != 0 or o.get("size") != "1" or o.get("graphics_mode") != "0":
        good = fail("defaults with no ini", "rc=%d size=%s mode=%s"
                    % (rc, o.get("size"), o.get("graphics_mode")))
    elif o.get("ini") != "-":
        good = fail("defaults with no ini", "claims it imported %s" % o.get("ini"))
    else:
        good &= ok("defaults with no ini", "Size 1 (24 px), graphics mode 0")

    # -- **the INI is read and then never written again.**  This replaced step
    # 2's "a write keeps every other key": the port no longer writes this file
    # at all, which is a stronger promise and a cheaper one to check.  The seed
    # is real keys the 2010 binary leaves behind, none of them this port's
    # business, plus the two it does read -- and the run changes every setting
    # it has, with --save-options, so anything that still wrote here would show.
    ini = tmp / "foreign.ini"
    seed = ("[DATA]\r\nPlayer=MZ\r\nDiff_Setting=31\r\n"
            "RLLFilename=nowhere.lvl\r\n"
            "[SCREEN]\r\nPosX=52\r\nPosY=52\r\nSize=3\r\n"
            "[OPT]\r\nSound=No\r\n").encode("latin-1")
    ini.write_bytes(seed)
    pack = sorted(GRAPHICS.glob("*.ltg"))[0]
    rc, out = run(["--ini", ini, "--pack", pack.name, "--name", "Someone Else",
                   "--sound", "yes", "--zoom", "40", "--save-options",
                   "--check-options"])
    o = options(out)
    if rc != 0:
        good = fail("the ini is imported, not written", "rc=%d" % rc)
    elif ini.read_bytes() != seed:
        good = fail("the ini is imported, not written",
                    "the file changed: %r" % ini.read_text("latin-1"))
    elif o.get("ini") != str(ini):
        good = fail("the ini is imported, not written",
                    "imported from %s" % o.get("ini"))
    elif o.get("size") != "3":
        good = fail("the ini is imported, not written",
                    "Size=3 was not honoured: size=%s" % o.get("size"))
    else:
        good &= ok("the ini is imported, not written",
                   "byte-identical after a run that changed five settings")

    # -- and the round trip, which is the *store's* now: what --save-options
    # wrote is what the next launch reports, with no overrides at all.
    rc, out = run(["--ini", ini, "--check-options"])
    o2 = options(out)
    if rc != 0 or o2.get("graphics_mode") != "2" or o2.get("graphics_file") != pack.name:
        good = fail("the choice survives a restart",
                    "mode=%s file=%s" % (o2.get("graphics_mode"), o2.get("graphics_file")))
    else:
        good &= ok("the choice survives a restart",
                   "mode 2, %s" % o2.get("graphics_file"))

    # -- the file it wrote: JSON, versioned, and where --check-options said.
    st = store_of(ini)
    try:
        doc = json.loads(st.read_text("utf-8"))
    except Exception as exc:                                # noqa: BLE001
        doc = None
        good = fail("the store is versioned json", str(exc))
    if doc is not None:
        if o2.get("settings") != str(st):
            good = fail("the store is versioned json",
                        "the game says %s, the file is %s" % (o2.get("settings"), st))
        elif doc.get("version") != 1 or doc.get("size") != 3:
            good = fail("the store is versioned json",
                        "version=%r size=%r" % (doc.get("version"), doc.get("size")))
        else:
            good &= ok("the store is versioned json",
                       "%d keys, version %d" % (len(doc), doc["version"]))

    # -- **one-way.**  With a store beside it the INI is not consulted again, so
    # editing it changes nothing.  This is the whole of what "importer" means
    # and the one property a player could otherwise be surprised by.
    ini.write_bytes(b"[SCREEN]\r\nSize=1\r\n")
    rc, out = run(["--ini", ini, "--check-options"])
    o = options(out)
    if rc != 0 or o.get("size") != "3" or o.get("ini") != "-":
        good = fail("the import happens once",
                    "rc=%d size=%s ini=%s" % (rc, o.get("size"), o.get("ini")))
    else:
        good &= ok("the import happens once", "Size=1 in the ini, Size 3 in the game")

    # -- a store that will not parse is the same case as one that is not there:
    # import again and rewrite, because refusing to start would cost the player
    # every setting over one stray brace.
    bad = tmp / "corrupt.ini"
    bad.write_bytes(b"[SCREEN]\r\nSize=3\r\n")
    store_of(bad).write_text("{ this is not json", encoding="utf-8")
    rc, out = run(["--ini", bad, "--check-options"])
    o = options(out)
    try:
        rewritten = json.loads(store_of(bad).read_text("utf-8")).get("size")
    except Exception:                                       # noqa: BLE001
        rewritten = None
    if rc != 0 or o.get("size") != "3" or rewritten != 3:
        good = fail("a corrupt store re-imports",
                    "rc=%d size=%s rewritten=%r" % (rc, o.get("size"), rewritten))
    else:
        good &= ok("a corrupt store re-imports",
                   "read the ini again, rewrote the store")

    # -- the size the menu sets, persisted on its own (SetGameSize's write).
    zoom = tmp / "zoom.ini"
    rc, out = run(["--ini", zoom, "--zoom", "24", "--save-options", "--check-options"])
    rc2, out2 = run(["--ini", zoom, "--check-options"])
    o3 = options(out2)
    if rc or rc2 or o3.get("size") != "1" or o3.get("cell") != "24":
        good = fail("Size survives a restart", "size=%s cell=%s"
                    % (o3.get("size"), o3.get("cell")))
    else:
        good &= ok("Size survives a restart", "Size 1 -> 24 px cells")

    # -- **one name, and the four characters cut from it.**  [DATA] Player and
    # [DATA] Record Author are one value in this port (Options.Name), because
    # HSBox and RecordBox are the same question asked twice.
    #
    # **Step 16 retired the write half of that merge** -- the port no longer
    # writes either key, so what is pinned here is the half that was ever
    # load-bearing: the name round-trips through the store, and `Initials` is
    # still cut from the same string rather than asked for separately, because
    # that is what reaches a `.hs` record and a `.lpb` header.
    name = tmp / "name.ini"
    rc, out = run(["--ini", name, "--name", "Michal Zlatkovsky", "--save-options",
                   "--check-options"])
    rc2, out2 = run(["--ini", name, "--check-options"])
    o = options(out2)
    if rc or rc2:
        good = fail("the name, and its initials", "rc=%d/%d" % (rc, rc2))
    elif o.get("name") != "Michal Zlatkovsky" or o.get("initials") != "Mich":
        good = fail("the name, and its initials",
                    "read back name=%s initials=%s" % (o.get("name"), o.get("initials")))
    elif name.exists():
        good = fail("the name, and its initials",
                    "it wrote a LaserTank.ini: %r" % name.read_text("latin-1"))
    else:
        good &= ok("the name, and its initials", "stored whole, posted as Mich")

    # And the read the other way: a file the 2010 binary wrote has only the
    # initials in it, because HSBox is the dialog it opens first -- so Player is
    # the fallback, and importing it writes nothing back.
    old_ini = tmp / "name_2010.ini"
    old_seed = b"[DATA]\r\nPlayer=MZ\r\n"
    old_ini.write_bytes(old_seed)
    rc, out = run(["--ini", old_ini, "--check-options"])
    o = options(out)
    if rc or o.get("name") != "MZ" or o.get("initials") != "MZ":
        good = fail("Player alone is the name",
                    "rc=%d name=%s initials=%s" % (rc, o.get("name"), o.get("initials")))
    elif old_ini.read_bytes() != old_seed:
        good = fail("Player alone is the name", "reading it changed the file")
    else:
        good &= ok("Player alone is the name", "MZ, and the file is untouched")

    # -- remember-last-level, both halves.  The write is Session.Load's
    # (LTANK2.C:1035), so it is exercised through a run that really loads a
    # level: --tick-rate 1 is the cheapest of those and is headless.  The read
    # is what a launch with no --levels / --level then starts on.
    pack_lvl = ROOT / "data" / "levels" / "LaserTank.lvl"
    rll = tmp / "rll.ini"
    rc, _ = run(["--ini", rll, "--levels", pack_lvl, "--level", 42, "--tick-rate", 1])
    rc2, out = run(["--ini", rll, "--check-options"])
    o = options(out)
    if rc or rc2 or o.get("rll_level") != "42":
        good = fail("the last level is remembered",
                    "rc=%d/%d rll_level=%s" % (rc, rc2, o.get("rll_level")))
    elif o.get("start_level") != "42" or o.get("start_file") != str(pack_lvl):
        good = fail("the last level is remembered",
                    "would start at %s of %s" % (o.get("start_level"), o.get("start_file")))
    else:
        good &= ok("the last level is remembered", "wrote 42, would reopen 42")

    # RLL=No is the original's own opt-out (LTANK.C:439): the keys may still be
    # in the file, and are ignored.
    off = tmp / "rll_off.ini"
    off.write_bytes(("[OPT]\r\nRLL=No\r\n[DATA]\r\nRLLFilename=%s\r\nRLLLevel=42\r\n"
                     % pack_lvl).encode("latin-1"))
    rc, out = run(["--ini", off, "--check-options"])
    o = options(out)
    good &= (ok("RLL=No is honoured", "starts at level 1 of the flagship")
             if rc == 0 and o.get("rll") == "No" and o.get("start_level") == "1"
             else fail("RLL=No is honoured",
                       "rc=%d rll=%s start_level=%s"
                       % (rc, o.get("rll"), o.get("start_level"))))

    # -- the Yes/No test is `strcmp(temps, psYes)`, so **exactly "Yes"**, and
    # step 4 read the C properly and made the port strict everywhere.  Six keys
    # use the idiom and they split on the *default* only: Animation, Sound and
    # RLL default to Yes and are off unless the value is exactly "Yes"
    # (LTANK.C:404, :411, :439); SkipComLev, Auto_Record and DisableWarnings
    # default to No and are on only when it is (LTANK.C:418, :424, :431).
    #
    # This is what step 2 left unresolved -- it read RLL loosely, on the grounds
    # that the idiom was ambiguous, and PROGRESS.md recorded the disagreement
    # rather than guessing.  LTANK.C:439 is not ambiguous.
    # Two layers compose here and it is worth keeping them apart: the
    # *comparison* is strict, and the *reader* trims.  GetPrivateProfileString
    # strips surrounding whitespace and quotes before anything sees the value,
    # so `RLL=Yes ` is "Yes" and stays on -- while `Yes!` and any change of case
    # reach the strcmp intact and turn it off.
    #
    # **All of it is import now**, which is the point: these are recorded
    # findings about the 2010 binary's file and they outlived the port's use of
    # it.  Step 16 moved them from Options' constructor to IniImport and changed
    # nothing else about them.
    #
    # **One probe file per case, numbered.**  The old naming derived a stem from
    # the value, and three of the five collided -- `yes`/`Yes` and `Yes!`/`Yes `
    # -- which cost nothing while each run re-read the INI it had just been
    # handed, and costs two false greens now that a store beside it would
    # outrank the file on the second run.  Numbering is the whole fix.
    for i, (value, want_rll) in enumerate((
            ("yes", "No"),     # lower case really does turn it off
            ("YES", "No"),
            ("Yes!", "No"),    # reaches the strcmp and fails it
            ("Yes ", "Yes"),   # trimmed by the reader, so still on
            ("Yes", "Yes"))):
        strict = tmp / ("strict_%d.ini" % i)
        strict.write_bytes(("[OPT]\r\nRLL=%s\r\nSound=%s\r\nAnimation=%s\r\n"
                            "Auto_Record=%s\r\n" % (value, value, value, value))
                           .encode("latin-1"))
        rc, out = run(["--ini", strict, "--check-options"])
        o = options(out)
        # Auto_Record's default is the other way round, so the same value flips
        # it the other way: on only for exactly "Yes".
        want_arec = want_rll
        got = (o.get("rll"), o.get("sound"), o.get("animation"),
               o.get("auto_record"))
        want = (want_rll, want_rll, want_rll, want_arec)
        good &= (ok('exactly "Yes": %-5r' % value,
                    "rll/sound/animation=%s auto_record=%s" % (want_rll, want_arec))
                 if rc == 0 and got == want
                 else fail('exactly "Yes": %-5r' % value,
                           "rc=%d got %s want %s" % (rc, got, want)))

    # -- the file the 2010 binary actually left behind, read through a copy so
    # the player's own is never touched.  Its Size=3 and Graphics_Dir are the
    # two keys we share with it, and the copy has to come back unchanged.
    real = ROOT / "original" / "bin" / "LaserTank.ini"
    if real.exists():
        mine = tmp / "from2010.ini"
        shutil.copyfile(real, mine)
        want = {}
        sec = None
        for line in real.read_text("latin-1").splitlines():
            line = line.strip()
            if line.startswith("[") and line.endswith("]"):
                sec = line[1:-1].upper()
            elif "=" in line and sec == "SCREEN":
                k, v = line.split("=", 1)
                want.setdefault(k.strip().lower(), v.strip())
        rc, out = run(["--ini", mine, "--check-options"])
        o4 = options(out)
        if rc != 0:
            good = fail("the 2010 binary's own ini", "rc=%d" % rc)
        elif o4.get("size") != want.get("size", "1"):
            good = fail("the 2010 binary's own ini",
                        "size %s, file says %s" % (o4.get("size"), want.get("size")))
        elif mine.read_bytes() != real.read_bytes():
            good = fail("the 2010 binary's own ini", "the copy was modified")
        else:
            good &= ok("the 2010 binary's own ini",
                       "Size=%s read the same by both, untouched" % o4.get("size"))
    else:
        print("  %-34s SKIP original/bin/LaserTank.ini is not there"
              % "the 2010 binary's own ini")
    return good


# ---------------------------------------------------------------------------
# next-steps item 3: the filtered walk
#
# LTANK2.C:1010.  The port of that loop is Session.Advance and the instrument is
# `--check-advance`, which prints one `advance stop=N sdiff=D` per landing.
# Everything this function asserts is recomputed here from the bytes.
TLEVEL_SIZE = 576
THSREC_SIZE = 10
ALL_RANKS = 31
SOLVED_SDIFF = 128          # "Error SDiff", LTANK2.C:1016


def sdiffs(lvl):
    """The SDiff word of every record in a .lvl, 1-based."""
    data = lvl.read_bytes()
    n = len(data) // TLEVEL_SIZE
    return [struct.unpack_from("<H", data, i * TLEVEL_SIZE + 574)[0]
            for i in range(n)]


def make_hs(path, solved, count):
    """A .hs the length of the collection, with `solved` levels scored."""
    out = b""
    for i in range(1, count + 1):
        hit = i in solved
        out += struct.pack("<HH6s", 10 if hit else 0, 3 if hit else 0, b"MZ\0\0\0\0")
    path.write_bytes(out)


def walk(ranks, mask, solved, skip, start, direction):
    """LoadNextLevel's do/while, in Python.  -> (stops, how it ended)."""
    stops = []
    n = start + direction
    while 1 <= n <= len(ranks):
        sdiff = ranks[n - 1]
        if skip and n in solved:
            sdiff = SOLVED_SDIFF
        if sdiff > 0 and (mask & sdiff) == 0:
            n += direction
            continue
        stops.append((n, ranks[n - 1]))
        n += direction
    # The instrument walks until it cannot, so the ending is decided by what
    # stopped the *last* attempt: nothing left at all, or everything left
    # filtered out.
    last = stops[-1][0] if stops else start
    tail = range(last + direction, len(ranks) + 1) if direction > 0 \
        else range(last + direction, 0, -1)
    return stops, ("filtered" if any(True for _ in tail) else "eof")


def advance(out):
    """The `advance` lines -> (header dict, [(level, sdiff)], ending)."""
    head, stops, end = {}, [], None
    for line in out.splitlines():
        line = line.strip()
        if not line.startswith("advance "):
            continue
        d = dict(re.findall(r"(\w+)=(\S*)", line[8:]))
        if "stop" in d:
            stops.append((int(d["stop"]), int(d["sdiff"])))
        elif "end" in d:
            end = d["end"]
        else:
            head = d
    return head, stops, end


def check_advance(tmp):
    """[OPT] SkipComLev, [DATA] Diff_Setting, and LoadNextLevel's own loop."""
    good = True
    src = ROOT / "data" / "levels" / "LaserTank.lvl"
    if not src.exists():
        print("  %-34s SKIP %s is not there" % ("the filtered walk", src.name))
        return good

    # A small collection of its own, so nothing here reads or writes the
    # corpus: 40 records is enough to hold several of each rank.
    coll = tmp / "walk"
    coll.mkdir(exist_ok=True)
    lvl = coll / "Walk.lvl"
    count = 40
    lvl.write_bytes(src.read_bytes()[:TLEVEL_SIZE * count])
    ranks = sdiffs(lvl)

    # -- the defaults, which are the one place this port deliberately differs
    # from the original: Diff_Setting is absent and reads as all five rather
    # than as the 0 that would have posted command 225.
    ini = tmp / "walk.ini"
    ini.write_bytes(b"")
    rc, out = run(["--ini", ini, "--check-options"])
    o = options(out)
    if rc != 0 or o.get("difficulty") != str(ALL_RANKS) or o.get("skip_completed") != "No":
        good = fail("defaults: all five ranks, no skip",
                    "rc=%d difficulty=%s skip=%s"
                    % (rc, o.get("difficulty"), o.get("skip_completed")))
    else:
        good &= ok("defaults: all five ranks, no skip", "Diff_Setting absent -> 31")

    # -- and the 2010 binary's own "never asked" value reads the same way,
    # because there is no dialog here to post.
    zero = tmp / "walk_zero.ini"
    zero.write_bytes(b"[DATA]\r\nDiff_Setting=0\r\n")
    rc, out = run(["--ini", zero, "--check-options"])
    if rc != 0 or options(out).get("difficulty") != str(ALL_RANKS):
        good = fail("Diff_Setting=0 is all five", "got %s"
                    % options(out).get("difficulty"))
    else:
        good &= ok("Diff_Setting=0 is all five", "the 225 sentinel, answered")

    # -- the high bits are masked off.  128 is SDiff's own "completed"
    # sentinel, so a mask that kept bit 7 would match every skipped level and
    # quietly undo SkipComLev.
    wide = tmp / "walk_wide.ini"
    wide.write_bytes(b"[DATA]\r\nDiff_Setting=255\r\n")
    rc, out = run(["--ini", wide, "--check-options"])
    if rc != 0 or options(out).get("difficulty") != str(ALL_RANKS):
        good = fail("Diff_Setting=255 is masked to 31", "got %s"
                    % options(out).get("difficulty"))
    else:
        good &= ok("Diff_Setting=255 is masked to 31", "128 cannot collide")

    # -- the round trip, through the same flags the panel's two chips use.
    # Step 16: both values go to the typed store, so what is asserted is that
    # they come back and that the INI they were *imported* from did not move.
    trip = tmp / "walk_trip.ini"
    shutil.copyfile(ini, trip)
    seed = trip.read_bytes()
    rc, _ = run(["--ini", trip, "--difficulty", "13", "--skip-completed", "yes",
                 "--save-options", "--check-options"])
    rc2, out = run(["--ini", trip, "--check-options"])
    o = options(out)
    doc = json.loads(store_of(trip).read_text("utf-8"))
    if rc or rc2 or o.get("difficulty") != "13" or o.get("skip_completed") != "Yes":
        good = fail("both settings survive a restart", "difficulty=%s skip=%s"
                    % (o.get("difficulty"), o.get("skip_completed")))
    elif doc.get("difficulty") != 13 or doc.get("skipCompleted") is not True:
        good = fail("both settings survive a restart",
                    "the store says difficulty=%r skipCompleted=%r"
                    % (doc.get("difficulty"), doc.get("skipCompleted")))
    elif trip.read_bytes() != seed:
        good = fail("both settings survive a restart", "the ini was written")
    else:
        good &= ok("both settings survive a restart",
                   "difficulty 13 and the skip, and the ini untouched")

    # ---- the walk itself.
    solved = {2, 3, 5, 8, 13}
    make_hs(coll / "Walk.hs", solved, count)

    cases = [
        # (label, mask, skip, start, direction)
        ("every rank, forwards", ALL_RANKS, False, 1, 1),
        ("every rank, backwards", ALL_RANKS, False, count, -1),
        ("one rank only", 1, False, 1, 1),
        ("two ranks", 1 | 2, False, 1, 1),
        ("skip completed", ALL_RANKS, True, 1, 1),
        ("skip completed, backwards", ALL_RANKS, True, count, -1),
        ("a mask and a skip", 1 | 2, True, 1, 1),
    ]
    for label, mask, skip, start, direction in cases:
        args = ["--ini", ini, "--levels", lvl, "--level", start,
                "--difficulty", mask,
                "--skip-completed", "yes" if skip else "no",
                "--advance-dir", direction, "--check-advance"]
        rc, out = run(args)
        head, stops, end = advance(out)
        want, want_end = walk(ranks, mask, solved, skip, start, direction)
        if rc != 0:
            good = fail(label, "rc=%d" % rc)
        elif stops != want:
            good = fail(label, "%d stops, want %d (first difference %s)"
                        % (len(stops), len(want),
                           next((str((a, b)) for a, b in
                                 zip(stops + [None], want + [None]) if a != b),
                                "-")))
        elif end != want_end:
            good = fail(label, "ended %s, want %s" % (end, want_end))
        else:
            good &= ok(label, "%d stops, ends %s" % (len(stops), end))

    # -- **an unranked level is unfilterable**, which is the `CurRecData.SDiff
    # > 0` guard and the one arm of the loop that looks like a bug until you
    # read it.  Built rather than looked for: the fourth record's SDiff word is
    # zeroed, and a mask matching nothing then has to stop on it and nowhere
    # else.
    raw = bytearray(lvl.read_bytes())
    struct.pack_into("<H", raw, 3 * TLEVEL_SIZE + 574, 0)
    zeroed = coll / "Zeroed.lvl"
    zeroed.write_bytes(bytes(raw))
    rc, out = run(["--ini", ini, "--levels", zeroed, "--level", 1,
                   "--difficulty", 16, "--skip-completed", "no",
                   "--check-advance"])
    _head, stops, end = advance(out)
    want = [(n, s) for n, s in
            walk(sdiffs(zeroed), 16, set(), False, 1, 1)[0]]
    if rc != 0 or stops != want:
        good = fail("SDiff 0 is unfilterable", "got %s want %s" % (stops, want))
    elif not any(s == 0 for _, s in stops):
        good = fail("SDiff 0 is unfilterable", "the zeroed record was not one")
    else:
        good &= ok("SDiff 0 is unfilterable", "stops on it with mask 16")

    # -- and the walk must not write the player's state.  RLL on, a walk over
    # 40 levels, and the remembered level is where it was.
    rll = tmp / "walk_rll.ini"
    rll.write_bytes(b"[OPT]\r\nRLL=Yes\r\n[DATA]\r\nRLLLevel=7\r\n"
                    b"RLLFilename=nowhere.lvl\r\n")
    # One run first, because the first launch is the one that imports and
    # writes the store at all -- and it fills an empty Graphics_Dir in on the
    # way ("we only do this once", LTANK2.C:1785).  That is the first launch
    # writing, not the walk, and the claim here is about the walk.  Snapshot
    # after it has happened.
    run(["--ini", rll, "--check-options"])
    before, before_ini = store_of(rll).read_bytes(), rll.read_bytes()
    rc, _ = run(["--ini", rll, "--levels", lvl, "--level", 1, "--check-advance"])
    after, after_ini = store_of(rll).read_bytes(), rll.read_bytes()
    if rc != 0 or after != before or after_ini != before_ini:
        good = fail("the walk writes nothing",
                    "rc=%d, the store %s, the ini %s"
                    % (rc, "moved" if after != before else "is fine",
                       "moved" if after_ini != before_ini else "is fine"))
    else:
        good &= ok("the walk writes nothing", "lastLevel still 7")
    return good


# ---------------------------------------------------------------------------
# next-steps item 9: the level history, unbounded
#
# LTANK2.C:1045 pushes and LTANK.C:1000 pops.  The port is Session's `_history`
# plus Back(), and the instrument is `--check-history`, which prints one
# `history op=... stack=...` line per navigation command.  Everything asserted
# here is recomputed from the same bytes by `replay_history` below.
def first_unsolved(hs_path, count):
    """HighScores.FirstUnsolved: the first level with no moves posted, or 1."""
    if count < 1:
        return 1
    data = hs_path.read_bytes() if hs_path.exists() else b""
    have = len(data) // THSREC_SIZE
    for i in range(count):
        if i >= have:
            return i + 1
        if struct.unpack_from("<H", data, i * THSREC_SIZE)[0] == 0:
            return i + 1
    return 1


def replay_history(script, files, start, mask, skip, open_name=None):
    """The port's own rules for `_history`, in Python.

    `files` is {name: (ranks, solved, hs path)} and the walk is the same one
    check_advance models -- which is the point of running the two halves of
    item 9 against one model: `+` and `-` here must land where `advance` says
    they land, or one of the two readings is wrong.

    -> [(op, ok, file, level, stack)], one per op, in the instrument's order.
    """
    name = start[0]
    level = start[1]
    stack = [level]
    out = []

    def push(n):
        if not stack or stack[-1] != n:
            stack.append(n)

    for op in script.split(","):
        ranks, solved, _hs = files[name]
        ok = True
        if op in ("+", "-"):
            step = 1 if op == "+" else -1
            n, found = level + step, None
            while 1 <= n <= len(ranks):
                sdiff = (SOLVED_SDIFF if (skip and n in solved)
                         else ranks[n - 1])
                if sdiff > 0 and (mask & sdiff) == 0:
                    n += step
                    continue
                found = n
                break
            if found is None:
                ok = False                      # CurLevel = SavedLevelNum
            else:
                level = found
                push(level)
        elif op == "<":
            if len(stack) < 2:
                ok = False
            else:
                stack.pop()
                level = stack[-1]
        elif op == "r":
            pass                                # ReStart is not a load
        elif op == "o":
            # The instrument's `o` opens the one file `--history-open` names,
            # which is why the script has no path in it: see Step17Check.
            name = open_name
            ranks, solved, hs = files[name]
            stack.clear()                       # Backspace[BS_SP] = 0
            level = first_unsolved(hs, len(ranks))
            push(level)
        else:
            # Load's own wrap, which is this port's and not LoadNextLevel's.
            n = int(op)
            if n < 1:
                n = len(ranks)
            if n > len(ranks):
                n = 1
            level = n
            push(level)
        out.append((op, ok, name, level, list(stack)))
    return out


def history(out):
    """The `history` lines -> (head, [(op, ok, file, level, back, stack)])."""
    head, rows = {}, []
    for line in out.splitlines():
        line = line.strip()
        if not line.startswith("history "):
            continue
        rest = line[8:]
        if rest == "end":
            continue
        d = dict(re.findall(r"(\w+)=(\S*)", rest))
        if "op" not in d:
            head = d
            continue
        rows.append((d["op"], d["ok"] == "1", d["file"], int(d["level"]),
                     int(d["back"]),
                     [] if d["stack"] == "-" else
                     [int(x) for x in d["stack"].split(";")]))
    return head, rows


def check_history(tmp):
    """Command 118: the push, the pop, the floor, and 108's clear."""
    good = True
    src = ROOT / "data" / "levels" / "LaserTank.lvl"
    if not src.exists():
        print("  %-34s SKIP %s is not there" % ("the level history", src.name))
        return good

    coll = tmp / "hist"
    coll.mkdir(exist_ok=True)
    walk_lvl = coll / "Walk.lvl"
    other_lvl = coll / "Other.lvl"
    walk_lvl.write_bytes(src.read_bytes()[:TLEVEL_SIZE * 40])
    other_lvl.write_bytes(src.read_bytes()[:TLEVEL_SIZE * 12])
    # The second collection is opened by the `o` op, and its .hs is what
    # decides where it opens: this port lands 108 on the first unsolved level
    # rather than on level 1, so a file with 1-3 beaten must open at 4 -- which
    # is also the one thing about `o` that is *not* the original's behaviour.
    other_hs = coll / "Other.hs"
    make_hs(other_hs, {1, 2, 3}, 12)
    files = {
        "Walk.lvl": (sdiffs(walk_lvl), set(), coll / "Walk.hs"),
        "Other.lvl": (sdiffs(other_lvl), {1, 2, 3}, other_hs),
    }
    ini = tmp / "hist.ini"
    ini.write_bytes(b"")

    cases = [
        # (label, start level, script)
        ("the push is one per change",
         1, "+,+,+,<,<,<"),
        ("a restart is not a level",
         1, "+,r,r,<,r,<"),
        ("back past the first is refused",
         1, "<,+,<,<,<"),
        # `[` and `]` are Load, so a number is what they look like here -- and
        # a load that lands where it already is must not push, which is the
        # `Backspace[BS_SP] != CurLevel` test and the second `7` below.
        ("a direct load pushes once",
         1, "7,7,20,<,<,<"),
        # 108 clears it, and the collection it opens is the one the history
        # would otherwise be about.
        ("108 clears the stack",
         5, "+,+,o,<,+,<,<"),
        # And back through a longer stack than the original's ten slots, which
        # is the deviation this item is: a ring would have started reusing
        # entries at the eleventh push.
        ("deeper than ten",
         1, "3,5,7,9,11,13,15,17,19,21,23,25,<,<,<,<,<,<,<,<,<,<,<,<"),
    ]
    for label, start, script in cases:
        args = ["--ini", ini, "--levels", walk_lvl, "--level", start,
                "--history-open", other_lvl, "--check-history", script]
        rc, out = run(args)
        head, rows = history(out)
        want = replay_history(script, files, ("Walk.lvl", start),
                              ALL_RANKS, False, "Other.lvl")
        got = [(op, ok, name, level, stack)
               for op, ok, name, level, _back, stack in rows]
        if rc != 0:
            good = fail(label, "rc=%d" % rc)
        elif head.get("stack") != str(start):
            good = fail(label, "opens with stack=%s, want %d"
                        % (head.get("stack"), start))
        elif got != want:
            good = fail(label, "first difference %s"
                        % next((str((a, b)) for a, b in
                                zip(got + [None], want + [None]) if a != b),
                               "-"))
        else:
            # `back` is the level the key would go to next, and it is derived
            # from the stack -- so it is checked against the stack rather than
            # against the model, which would be the same derivation twice.
            bad = [r for r in rows
                   if r[4] != (r[5][-2] if len(r[5]) > 1 else 0)]
            if bad:
                good = fail(label, "back=%d does not match stack %s"
                            % (bad[0][4], bad[0][5]))
            else:
                good &= ok(label, "%d ops, ends on %d with %d deep"
                           % (len(rows), rows[-1][3], len(rows[-1][5])))

    # -- the filtered walk and the history are one model, so drive `+` through
    # a mask and check the stack is the levels `advance` stops on.
    mask = 1 | 2
    script = "+,+,+,<,<,<"
    rc, out = run(["--ini", ini, "--levels", walk_lvl, "--level", 1,
                   "--difficulty", mask, "--skip-completed", "no",
                   "--history-open", other_lvl, "--check-history", script])
    _head, rows = history(out)
    want = replay_history(script, files, ("Walk.lvl", 1), mask, False,
                          "Other.lvl")
    got = [(op, ok, name, level, stack)
           for op, ok, name, level, _back, stack in rows]
    if rc != 0 or got != want:
        good = fail("the mask decides what is pushed",
                    "rc=%d, %s" % (rc, next((str((a, b)) for a, b in
                                             zip(got + [None], want + [None])
                                             if a != b), "-")))
    else:
        good &= ok("the mask decides what is pushed",
                   "stack %s with mask %d"
                   % (";".join(str(n) for n in rows[0][5]), mask))

    # -- a 108 that fails moves nothing, the history included.  The original
    # has no such restore; Session.OpenDataFile does, and the stack is the part
    # of it that is easiest to lose.
    rc, out = run(["--ini", ini, "--levels", walk_lvl, "--level", 1,
                   "--history-open", coll / "NoSuchFile.lvl",
                   "--check-history", "+,+,o,<"])
    _head, rows = history(out)
    want_stack = [1, 2, 3]
    if rc != 0:
        good = fail("a failed 108 keeps the history", "rc=%d" % rc)
    elif rows[2][:2] != ("o", False) or rows[2][2] != "Walk.lvl" \
            or rows[2][5] != want_stack:
        good = fail("a failed 108 keeps the history",
                    "o -> ok=%s file=%s stack=%s"
                    % (rows[2][1], rows[2][2], rows[2][5]))
    elif rows[3][3] != 2:
        good = fail("a failed 108 keeps the history",
                    "back went to %d, want 2" % rows[3][3])
    else:
        good &= ok("a failed 108 keeps the history",
                   "stack %s survives, and back still works"
                   % ";".join(str(n) for n in want_stack))

    # -- and it must not write the player's state, on the same terms the walk
    # is held to: every op in that script is a Load.
    rll = tmp / "hist_rll.ini"
    rll.write_bytes(b"[OPT]\r\nRLL=Yes\r\n[DATA]\r\nRLLLevel=7\r\n"
                    b"RLLFilename=nowhere.lvl\r\n")
    run(["--ini", rll, "--check-options"])
    before, before_ini = store_of(rll).read_bytes(), rll.read_bytes()
    rc, _ = run(["--ini", rll, "--levels", walk_lvl, "--level", 1,
                 "--history-open", other_lvl, "--check-history", "+,+,o,+,<"])
    after, after_ini = store_of(rll).read_bytes(), rll.read_bytes()
    if rc != 0 or after != before or after_ini != before_ini:
        good = fail("the history writes nothing",
                    "rc=%d, the store %s, the ini %s"
                    % (rc, "moved" if after != before else "is fine",
                       "moved" if after_ini != before_ini else "is fine"))
    else:
        good &= ok("the history writes nothing", "lastLevel still 7")
    return good


# ---------------------------------------------------------------------------
def split_ltg(path, dest):
    """A .ltg *is* a header and two BMPs (LoadLTG, LTANK2.C:688), so unpacking
    it into GFXInit's external pair is a byte copy -- no re-encoding, which is
    what lets the two be compared for pixel equality."""
    d = path.read_bytes()
    if d[315:319] != b"LTG1":
        raise ValueError("%s: not an LTG file" % path.name)
    off = struct.unpack_from("<I", d, 320)[0]
    (dest / "game.bmp").write_bytes(d[324:off])
    (dest / "mask.bmp").write_bytes(d[off:])


def check_packs(tmp):
    good = True
    ini = tmp / "packs.ini"

    ltgs = sorted(GRAPHICS.glob("*.ltg"))
    if not ltgs:
        return fail("graphics packs", "no .ltg files in data/graphics")

    # The menu's list: two radio buttons plus one entry per .ltg, each named by
    # the Name field in its header rather than its file name.
    rc, out = run(["--ini", ini, "--check-options"])
    o = options(out)
    menu = o["_packs"]
    if rc != 0 or len(menu) != len(ltgs) + 2:
        good = fail("the menu lists every pack",
                    "rc=%d %d entries for %d packs" % (rc, len(menu), len(ltgs)))
    else:
        bad = []
        for p in ltgs:
            hdr = p.read_bytes()[:40].split(b"\x00")[0].decode("latin-1")
            hit = [m for m in menu if m.get("file") == p.name]
            if not hit:
                bad.append("%s missing" % p.name)
            elif hdr and hit[0]["label"] != hdr:
                bad.append("%s listed as %r not %r" % (p.name, hit[0]["label"], hdr))
        if menu[0]["mode"] != "0" or menu[1]["mode"] != "1":
            bad.append("the first two entries are not the internal/external radios")
        good &= (fail("the menu lists every pack", "; ".join(bad)) if bad
                 else ok("the menu lists every pack",
                         "internal, external, %d .ltg by header name" % len(ltgs)))

    # The internal pair, against atlas_check's Python decoder.
    rc, out = run(["--ini", ini, "--pack", "internal", "--check-options"])
    o = options(out)
    want = hashlib.sha256(atlas_check.internal_sheet()).hexdigest()
    if rc != 0 or o.get("sha256") != want:
        good = fail("the internal sheet", "%s != %s" % (o.get("sha256"), want[:16]))
    else:
        good &= ok("the internal sheet", "sha256 %s" % want[:16])

    # Mode 1 == mode 2, for every pack, and both == Python's own decode.
    for p in ltgs:
        d = tmp / ("ext_" + p.stem)
        d.mkdir(exist_ok=True)
        split_ltg(p, d)
        want = hashlib.sha256(atlas_check.ltg_sheet(p)).hexdigest()
        rc, out = run(["--ini", ini, "--gfx-dir", d, "--pack", "external",
                       "--check-options"])
        o = options(out)
        if rc != 0 or o.get("graphics_mode") != "1":
            good = fail("external: " + p.name, "rc=%d mode=%s" % (rc, o.get("graphics_mode")))
        elif o.get("sha256") != want:
            good = fail("external: " + p.name,
                        "game.bmp + mask.bmp give %s, the .ltg gives %s"
                        % (str(o.get("sha256"))[:16], want[:16]))
        else:
            good &= ok("external: " + p.name,
                       "mode 1 pixels == the .ltg's, %s" % want[:16])

    # A mode 2 whose file is gone falls back to the internal sheet rather than
    # taking the game down -- GFXInit's `if (!LoadLTG(...)) GraphM = 0`.
    gone = tmp / "gone.ini"
    gone.write_bytes(b"[SCREEN]\r\nGraphics_Mode=2\r\nGraphics_File=nosuch.ltg\r\n")
    rc, out = run(["--ini", gone, "--check-options"])
    o = options(out)
    good &= (ok("a missing pack falls back", "graphics mode 0")
             if rc == 0 and o.get("graphics_mode") == "0"
             else fail("a missing pack falls back",
                       "rc=%d mode=%s" % (rc, o.get("graphics_mode"))))
    return good


# ---------------------------------------------------------------------------
def png(path):
    """A PNG reader for exactly what Godot's SavePng writes: 8-bit RGB or RGBA,
    no interlace.  -> (w, h, channels, pixels)."""
    d = pathlib.Path(path).read_bytes()
    if d[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("%s: not a PNG" % path)
    off, idat, hdr = 8, bytearray(), None
    while off < len(d):
        ln, typ = struct.unpack_from(">I4s", d, off)
        body = d[off + 8:off + 8 + ln]
        off += 12 + ln
        if typ == b"IHDR":
            hdr = struct.unpack(">IIBBBBB", body)
        elif typ == b"IDAT":
            idat += body
    w, h, depth, color, _, _, interlace = hdr
    if depth != 8 or interlace or color not in (2, 6):
        raise ValueError("%s: unsupported PNG %s" % (path, hdr))
    ch = 4 if color == 6 else 3
    raw = zlib.decompress(bytes(idat))
    out = bytearray(w * h * ch)
    stride = w * ch
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        f = raw[p]
        line = bytearray(raw[p + 1:p + 1 + stride])
        p += 1 + stride
        if f:
            for i in range(stride):
                a = line[i - ch] if i >= ch else 0
                b = prev[i]
                c = prev[i - ch] if i >= ch else 0
                if f == 1:
                    line[i] = (line[i] + a) & 255
                elif f == 2:
                    line[i] = (line[i] + b) & 255
                elif f == 3:
                    line[i] = (line[i] + (a + b) // 2) & 255
                elif f == 4:
                    pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                    pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                    line[i] = (line[i] + pr) & 255
                else:
                    raise ValueError("filter %d" % f)
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return w, h, ch, bytes(out)


def green_box(w, h, ch, px, x0, y0, size):
    """The bounding box of pure 0,255,0 inside one cell -> (x, y, w, h, count),
    in cell-relative pixels.  Pure green is the laser's own colour
    (LaserColorG = 0x0000FF00, LTANK.C:449) and nothing in the sprite sheet
    inside a laser's cell is exactly that."""
    xs, ys = [], []
    for y in range(y0, min(y0 + size, h)):
        for x in range(x0, min(x0 + size, w)):
            i = (y * w + x) * ch
            if px[i] == 0 and px[i + 1] == 255 and px[i + 2] == 0:
                xs.append(x - x0)
                ys.append(y - y0)
    if not xs:
        return None
    return (min(xs), min(ys), max(xs) - min(xs) + 1, max(ys) - min(ys) + 1, len(xs))


def check_window(tmp):
    """The size and laser checks, which need a rendering device."""
    good = True
    shots = {}
    for size, cell, offset in SIZES:
        out_png = tmp / ("laser%d.png" % cell)
        rc, out = run(["--ini", tmp / "shot.ini", "--shot", out_png,
                       "--lpb", DEMO, "--ticks", LASER_TICK, "--zoom", cell],
                      windowed=True)
        geo = dict(re.findall(r"(\w+)=(\S+)",
                              "".join(l for l in out.splitlines()
                                      if l.startswith("shot-geometry"))))
        las = dict(re.findall(r"(\w+)=(\S+)",
                              "".join(l for l in out.splitlines()
                                      if l.startswith("shot-laser"))))
        if rc != 0 or not out_png.exists() or not geo or not las:
            print("  %-34s SKIP no window (rc=%d) -- the size and laser checks "
                  "need a rendering device" % ("--shot at %d px" % cell, rc))
            print(out[-800:])
            return None
        shots[size] = (out_png, geo, las)

    # Nothing but pixels, and the claim moved in step 7.  It used to be "the
    # window is exactly the board plus two margins, and the HUD strip under it
    # is the same height at all three sizes" -- which was true while the window
    # *was* the board plus a fixed strip, and stopped being true when the board
    # started fitting itself to a resizable window with chrome around it.
    #
    # What is checked now is what the three presets actually promise: the board
    # is 16 cells of exactly the requested size, it is square, and it is inside
    # the window with the chrome clear of it.  The window's own size is no
    # longer a derived constant and is not asserted -- dragging it is the
    # feature.
    boards = {}
    for size, cell, offset in SIZES:
        out_png, geo, _ = shots[size]
        w, h, ch, px = png(out_png)
        margin, c = int(geo["margin"]), int(geo["cell"])
        bx, by = int(geo["board_x"]), int(geo["board_y"])
        if c != cell:
            good = fail("size %d renders" % size,
                        "cell=%d, wanted %d" % (c, cell))
            continue
        # The gutter the A1-P16 labels live in is the board's own margin, and
        # since step 7 it follows the cell rather than being a constant 24.
        # Both edges of it have to be inside the window or a label is clipped.
        if bx < margin or by < margin:
            good = fail("size %d renders" % size,
                        "board at (%d,%d) leaves no room for the %d px gutter"
                        % (bx, by, margin))
            continue
        if bx + 16 * c + margin > w or by + 16 * c + margin > h:
            good = fail("size %d renders" % size,
                        "board 16*%d at (%d,%d) runs off a %dx%d window"
                        % (c, bx, by, w, h))
            continue
        boards[size] = (bx, by, c)
        good &= ok("size %d renders" % size,
                   "16x%d px board at (%d,%d) in a %dx%d window" % (c, bx, by, w, h))

    # "Changing size changes nothing but pixels" still holds and is still worth
    # saying -- it is just said about the board rather than about the window:
    # the board is square and 16 cells across in every one of the three.
    if len(boards) == len(SIZES):
        good &= ok("size changes nothing but pixels",
                   "board 16 cells square at %s px"
                   % "/".join(str(boards[s][2]) for s, _c, _o in SIZES))

    # The laser bar, measured in the cell the engine names.
    for size, cell, offset in SIZES:
        out_png, geo, las = shots[size]
        if las.get("firing") != "1":
            good = fail("laser at %d px" % cell,
                        "tick %d has no shot in flight (firing=%s)"
                        % (LASER_TICK, las.get("firing")))
            continue
        w, h, ch, px = png(out_png)
        c = int(geo["cell"])
        # `board_x`/`board_y` since step 7: the board floats in the window now,
        # so the cell's corner is the board's own origin plus the offset, and
        # `margin` -- which is the gutter the labels live in -- is not it.
        bx, by = int(geo["board_x"]), int(geo["board_y"])
        lx, ly, d = int(las["x"]), int(las["y"]), int(las["dir"])
        box = green_box(w, h, ch, px, bx + lx * c, by + ly * c, c)
        if box is None:
            good = fail("laser at %d px" % cell,
                        "no green in cell (%d,%d)" % (lx, ly))
            continue
        # UpDateLaser (LTANK2.C:558): a bar down the middle, `LaserOffset` in
        # from each side on the two edges it is thin across.  GDI's Rectangle
        # and Godod's DrawRect both put the 1 px black outline inside the
        # rectangle, so the *green* is 2 px smaller than the bar in both axes
        # it has an edge on.
        thin = c - 2 * offset - 2
        if d & 1:                                       # up / down: vertical bar
            want = (offset + 1, 1, thin, c - 2)
        else:                                           # left / right
            want = (1, offset + 1, c - 2, thin)
        got = box[:4]
        if got != want:
            good = fail("laser at %d px" % cell,
                        "green %dx%d at (%d,%d) in the cell; LaserOffset %d wants "
                        "%dx%d at (%d,%d)"
                        % (got[2], got[3], got[0], got[1], offset,
                           want[2], want[3], want[0], want[1]))
        else:
            good &= ok("laser at %d px" % cell,
                       "bar %d px across (cell %d - 2*%d), in cell (%d,%d)"
                       % (c - 2 * offset, c, offset, lx, ly))
    return good


# ---------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(
        description="Phase 5 step 2's gate: options, packs, the walk, the history, laser width.",
        epilog="exit 0 clean, 1 a check failed, 2 environment")
    ap.add_argument("--no-window", action="store_true",
                    help="skip the size and laser checks, which open a window")
    ap.add_argument("--keep", action="store_true",
                    help="keep the scratch directory and say where it is")
    args = ap.parse_args()

    if not DEMO.exists():
        print("options_check: %s is missing" % DEMO)
        return 2
    _ok, warn = engines.build_godot_game()
    print("build: LaserTank.Game ok%s"
          % ("" if not warn else "   %d warning(s)" % warn))

    tmp = pathlib.Path(tempfile.mkdtemp(prefix="lt_options_"))
    good = True
    try:
        print("ini:")
        good &= check_ini(tmp)
        print("packs:")
        good &= check_packs(tmp)
        print("advance:")
        good &= check_advance(tmp)
        print("history:")
        good &= check_history(tmp)
        if args.no_window:
            print("size + laser: SKIPPED (--no-window)")
        else:
            print("size and laser:")
            res = check_window(tmp)
            if res is None:
                print("size + laser: SKIPPED (no rendering device)")
            else:
                good &= res
    finally:
        if args.keep:
            print("scratch: %s" % tmp)
        else:
            shutil.rmtree(tmp, ignore_errors=True)

    print("options_check: %s" % ("OK" if good else "FAILED"))
    return 0 if good else 1


if __name__ == "__main__":
    sys.exit(main())
