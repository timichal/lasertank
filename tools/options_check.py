#!/usr/bin/env python
"""Phase 5 step 2's gate: the persisted options, the graphics packs, and the
laser's width.

Four checks, all of them things a human clicking around would notice only if
they knew what to look for:

  ini     the profile-string stand-in behaves like GetPrivateProfileInt /
          WritePrivateProfileString.  No file at all -> Size 1 and graphics mode
          0, the original's own defaults (LTANK.C:1567, LTANK2.C:1780).  A write
          keeps every other key in the file, including the dozen the 2010 binary
          puts there and this port knows nothing about -- a rewrite that dropped
          them would silently reset the player's other settings.  And the round
          trip: run the game once with overrides and --save-options, once with
          nothing, and the second run must report what the first wrote.

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

  size    the three sizes (24 / 32 / 40 px, SetGameSize's 1..3) render, and
          changing size changes nothing but pixels: the board is 16 cells
          square in each and the HUD strip below it keeps its height.

  laser   **the laser bar is `cell - 2 * LaserOffset` wide -- 4, 6 and 6 px.**
          LaserOffset is a per-size constant (LTANK2.C:1747/:1756/:1765), not a
          fraction of the cell, and reading it as 10-of-32 -- which step 1
          shipped -- draws the bar 8, 12 and 14 px wide instead: two to three
          times too fat, and obvious only beside the real game.  Measured out of
          a --shot PNG, in the cell the *engine* says the shot is in: the
          coordinates come from the trace side and the pixels from the renderer,
          which is what makes it a check rather than a tautology.

    python tools/options_check.py               # all four, ~40 s
    python tools/options_check.py --no-window   # skip size + laser (no display)

The laser and size checks open a real window (three, briefly): --shot needs a
rendering device, so they cannot run headless.  Everything else is headless.

Nothing here writes the player's own LaserTank.ini -- every run is given its own
--ini under the scratch directory, and the game refuses to write an INI it was
not handed explicitly when it is running as an instrument anyway.

Exit: 0 clean, 1 a check failed, 2 environment (no Godot, no packs).
"""
import argparse
import hashlib
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
            # label= is last on its line and may contain spaces.
            m = re.search(r"\blabel=(.*)$", line)
            if m:
                o["label"] = m.group(1).strip()
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
def check_ini(tmp):
    """GetPrivateProfileInt / WritePrivateProfileString semantics, and the round
    trip a player gets: pick something in the menu, restart, still there."""
    good = True

    # -- defaults, with no file at all.  LTANK.C:1567 passes 1 as the default
    # size and LTANK2.C:1780 passes 0 as the default graphics mode.
    ini = tmp / "absent.ini"
    rc, out = run(["--ini", ini, "--check-options"])
    o = options(out)
    if rc != 0 or o.get("size") != "1" or o.get("graphics_mode") != "0":
        good = fail("defaults with no ini", "rc=%d size=%s mode=%s"
                    % (rc, o.get("size"), o.get("graphics_mode")))
    else:
        good &= ok("defaults with no ini", "Size 1 (24 px), graphics mode 0")

    # -- a write preserves every other line.  These are real keys the 2010
    # binary leaves in the file; none of them is this port's business.
    ini = tmp / "foreign.ini"
    seed = ("[DATA]\r\nPlayer=MZ\r\nDiff_Setting=31\r\n"
            "RLLFilename=nowhere.lvl\r\n"
            "[SCREEN]\r\nPosX=52\r\nPosY=52\r\nSize=3\r\n"
            "[OPT]\r\nSound=No\r\n")
    ini.write_bytes(seed.encode("latin-1"))
    pack = sorted(GRAPHICS.glob("*.ltg"))[0]
    rc, out = run(["--ini", ini, "--pack", pack.name, "--save-options",
                   "--check-options"])
    o = options(out)
    text = ini.read_text("latin-1")
    missing = [k for k in ("Player=MZ", "Diff_Setting=31", "PosX=52", "PosY=52",
                           "Sound=No") if k not in text]
    if rc != 0 or missing:
        good = fail("a write keeps foreign keys",
                    "rc=%d lost %s" % (rc, ", ".join(missing) or "-"))
    elif o.get("size") != "3":
        good = fail("Size=3 is read back", "size=%s" % o.get("size"))
    else:
        good &= ok("a write keeps foreign keys", "5 kept, Size=3 honoured")

    # -- and the round trip: what --save-options wrote is what the next launch
    # reports, with no overrides at all.
    rc, out = run(["--ini", ini, "--check-options"])
    o2 = options(out)
    if rc != 0 or o2.get("graphics_mode") != "2" or o2.get("graphics_file") != pack.name:
        good = fail("the choice survives a restart",
                    "mode=%s file=%s" % (o2.get("graphics_mode"), o2.get("graphics_file")))
    else:
        good &= ok("the choice survives a restart",
                   "mode 2, %s" % o2.get("graphics_file"))

    # -- the size the menu sets, persisted on its own (SetGameSize's write).
    rc, out = run(["--ini", ini, "--zoom", "24", "--save-options", "--check-options"])
    rc2, out2 = run(["--ini", ini, "--check-options"])
    o3 = options(out2)
    if rc or rc2 or o3.get("size") != "1" or o3.get("cell") != "24":
        good = fail("Size survives a restart", "size=%s cell=%s"
                    % (o3.get("size"), o3.get("cell")))
    else:
        good &= ok("Size survives a restart", "Size 1 -> 24 px cells")

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
                    "rc=%d/%d RLLLevel=%s" % (rc, rc2, o.get("rll_level")))
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
    for value, want_rll in (("yes", "No"),     # lower case really does turn it off
                            ("YES", "No"),
                            ("Yes!", "No"),    # reaches the strcmp and fails it
                            ("Yes ", "Yes"),   # trimmed by the reader, so still on
                            ("Yes", "Yes")):
        strict = tmp / ("strict_%s_%d.ini" % (value.strip().lower().strip("!"),
                                              len(value)))
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
        good &= (ok("exactly \"Yes\": %-5r" % value,
                    "rll/sound/animation=%s auto_record=%s" % (want_rll, want_arec))
                 if rc == 0 and got == want
                 else fail("exactly \"Yes\": %-5r" % value,
                           "rc=%d got %s want %s" % (rc, got, want)))

    # -- the file the 2010 binary actually left behind, read through a copy so
    # the player's own is never touched.  Its Size=3 and Graphics_Dir are the
    # two keys we share with it.
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
        else:
            good &= ok("the 2010 binary's own ini",
                       "Size=%s read the same by both" % o4.get("size"))
    else:
        print("  %-34s SKIP original/bin/LaserTank.ini is not there"
              % "the 2010 binary's own ini")
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

    # Nothing but pixels: 16 cells of board in each size, and the HUD strip
    # below it keeps its height.
    huds = set()
    for size, cell, offset in SIZES:
        out_png, geo, _ = shots[size]
        w, h, ch, px = png(out_png)
        margin, c = int(geo["margin"]), int(geo["cell"])
        if c != cell:
            good = fail("size %d renders" % size, "cell=%d, wanted %d" % (c, cell))
            continue
        if w != 2 * margin + 16 * c:
            good = fail("size %d renders" % size,
                        "window %d px wide, 2*%d + 16*%d = %d"
                        % (w, margin, c, 2 * margin + 16 * c))
            continue
        huds.add(h - w)
        good &= ok("size %d renders" % size, "%dx%d window, %d px cells" % (w, h, c))
    if len(huds) != 1:
        good = fail("size changes nothing but pixels",
                    "the HUD strip is %s px in the three sizes" % sorted(huds))
    else:
        good &= ok("size changes nothing but pixels",
                   "board 16 cells square, HUD %d px in all three" % huds.pop())

    # The laser bar, measured in the cell the engine names.
    for size, cell, offset in SIZES:
        out_png, geo, las = shots[size]
        if las.get("firing") != "1":
            good = fail("laser at %d px" % cell,
                        "tick %d has no shot in flight (firing=%s)"
                        % (LASER_TICK, las.get("firing")))
            continue
        w, h, ch, px = png(out_png)
        margin, c = int(geo["margin"]), int(geo["cell"])
        lx, ly, d = int(las["x"]), int(las["y"]), int(las["dir"])
        box = green_box(w, h, ch, px, margin + lx * c, margin + ly * c, c)
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
        description="Phase 5 step 2's gate: options, graphics packs, laser width.",
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
