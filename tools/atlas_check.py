#!/usr/bin/env python
"""Phase 5 step 0's gate: the sprite atlas, and the bitmap numbers that index it.

Two checks, both cheap enough to run beside the fidelity gates:

  grid    every BMF / BMF2 value the engine produces is a bitmap number that
          lands inside the 10x6 grid of 32x32 sprites -- over all 2,347 corpus
          levels by default.  This is the silent off-by-one the renderer would
          otherwise ship: BMA[] is filled row-major *from i = 1* (GFXInit,
          LTANK2.C:782), so bitmap i is cell ((i-1) % 10, (i-1) / 10), and a
          value of 0 or 58+ would index a sprite that does not exist.

  sheets  every shipped graphics pack decodes to a 320x192 sheet -- and decodes
          to the *same pixels* in Python here and in C# (LaserTank.Core's
          SpriteSheet) via Godot's headless --check-sheets.  Two independent
          BMP readers agreeing on a sha256 is the point; one reader agreeing
          with itself is not a check.  If Godot is not installed the sheet
          decode still runs, and the cross-check reports SKIP loudly.

    python tools/atlas_check.py                 # both checks, ~2,347 levels
    python tools/atlas_check.py --stride 20     # a spot check
    python tools/atlas_check.py --sheets-only

The grid check needs build/lasertank-core.exe (bash src/build.sh); it reads the
BMF/BMF2 hex the trace already carries with --bmf, so it adds no engine code.

Exit: 0 clean, 1 a bad bitmap number or a sheet mismatch, 2 environment.
"""
import argparse
import glob
import hashlib
import os
import pathlib
import re
import struct
import subprocess
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
from engines import Case, ROOT                          # noqa: E402

LEVELS = ROOT / "data" / "levels"
QUIRKS = ROOT / "data" / "quirks"
FLAGSHIP = LEVELS / "LaserTank.lvl"
GRAPHICS = ROOT / "data" / "graphics"

# GFXInit (LTANK2.C:782) and LTANK.H:92.
SPRITE, COLS, ROWS = 32, 10, 6
SHEET_W, SHEET_H = SPRITE * COLS, SPRITE * ROWS
MAX_BITMAPS = 58
TUNNEL_BM = 55                  # painted over a solid colour, masked either way

# LTANK2.C:80, copied verbatim.  The declaration is [MaxBitMaps+1] = 59 wide but
# the initializer lists 58 values, so C zero-fills the last one: the trailing 0
# here is that, not a typo.
BMSTA = [0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 1, 1, 0,
         1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1,
         1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0,
         0]
assert len(BMSTA) == MAX_BITMAPS + 1


def cell_of(bm):
    """Bitmap number -> grid cell, or None if it is not on the sheet."""
    if not 1 <= bm <= MAX_BITMAPS:
        return None
    cx, cy = (bm - 1) % COLS, (bm - 1) // COLS
    return None if cy >= ROWS else (cx, cy)


# --- check 1: the bitmap numbers ---------------------------------------------


def lvls(d):
    """Level files in one directory, case-insensitively (four packs ship .LVL)."""
    return sorted(p for p in d.iterdir() if p.suffix.lower() == ".lvl")


def default_files(all_collections):
    """sweep.py's surface: flagship + every quirk pack = the 2,347."""
    files = lvls(LEVELS) if all_collections else [FLAGSHIP]
    return files + [p for d in sorted(QUIRKS.iterdir()) if d.is_dir()
                    for p in lvls(d)]


_local = threading.local()


def scratch():
    if not hasattr(_local, "s"):
        _local.s = engines.Scratch("lt-atlas")
    return _local.s


FIELD_RE = re.compile(r" (BMF2?)=([0-9a-f]+)")


def bad_bitmaps(trace):
    """-> [(tick, field, index, value)] for every off-sheet bitmap number."""
    read = engines.read_trace(trace)
    if read is None:
        return [(None, "trace", 0, 0)]
    _head, ticks, _foot = read
    bad = []
    for line in ticks:
        tick = line.split(" ", 1)[0]
        for field, hexs in FIELD_RE.findall(line):
            raw = bytes.fromhex(hexs)
            for i, v in enumerate(raw):
                if cell_of(v) is None:
                    bad.append((tick, field, i, v))
    return bad


def check_grid(args):
    if not engines.CORE.exists():
        raise SystemExit("engine not built: %s\nrun: bash src/build.sh" % engines.CORE)
    files = args.files or default_files(args.all)
    missing = [p for p in files if not p.exists()]
    if missing:
        raise SystemExit("no such level file: %s" % ", ".join(str(p) for p in missing))

    cases = []
    for path in files:
        nums = list(range(1, engines.count_levels(path) + 1))[::args.stride]
        cases += [Case(path, i, args.keys) for i in nums]

    print("grid: %d levels from %d file%s, keys=%r"
          % (len(cases), len(files), "" if len(files) == 1 else "s", args.keys))

    def work(case):
        s = scratch()
        run = engines.run_one(engines.CORE, case, s.b, field=False, bmf=True)
        if run.rc not in (engines.RC_WIN, engines.RC_NOTWIN):
            return case, [(None, "engine exit %d" % run.rc, 0, 0)]
        return case, bad_bitmaps(s.b)

    ok, bad = 0, []
    t0 = time.time()
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for i, (case, problems) in enumerate(pool.map(work, cases), 1):
            if problems:
                bad.append((case, problems))
                for tick, field, at, v in problems[:4]:
                    print("  %s %d  tick %s  %s[%d,%d] = %d  not on the sheet"
                          % (case.levels.name, case.level, tick, field,
                             at // 16, at % 16, v))
            else:
                ok += 1
            if i % 200 == 0 or i == len(cases):
                print("  %-40s" % ("%d/%d  %.0f levels/s"
                                   % (i, len(cases), i / max(time.time() - t0, 1e-9))),
                      end="\r", flush=True)
    print(" " * 44, end="\r")
    print("grid: %d/%d levels clean   %d bad   (%.0fs)"
          % (ok, len(cases), len(bad), time.time() - t0))
    return not bad


# --- check 2: the sheets ------------------------------------------------------


def _u32(b, o):
    return struct.unpack_from("<I", b, o)[0]


def _i32(b, o):
    return struct.unpack_from("<i", b, o)[0]


def bmp_decode(d, off, end, what):
    """A Windows BMP -> (w, h, bytearray of RGB triples, top-down).

    Independent of LaserTank.Core's reader on purpose: 1/4/8/24 bpp, BI_RGB,
    BI_RLE8 and BI_RLE4, which is exactly what this game ships.
    """
    if d[off:off + 2] != b"BM":
        raise ValueError("%s: not a BMP" % what)
    data_off = _u32(d, off + 10)
    hdr = _u32(d, off + 14)
    w, h = _i32(d, off + 18), _i32(d, off + 22)
    bpp = struct.unpack_from("<H", d, off + 28)[0]
    comp = _u32(d, off + 30)
    clr = _u32(d, off + 46)
    top_down = h < 0
    h = abs(h)

    pal_off = off + 14 + hdr
    n_pal = clr if clr else (1 << bpp if bpp <= 8 else 0)
    pal = [(d[pal_off + i * 4 + 2], d[pal_off + i * 4 + 1], d[pal_off + i * 4])
           for i in range(n_pal)]

    px = bytearray(w * h * 3)

    def put(x, y, rgb):
        if 0 <= x < w and 0 <= y < h:
            i = (y * w + x) * 3
            px[i:i + 3] = bytes(rgb)

    def row_y(row):
        return row if top_down else h - 1 - row

    p = off + data_off
    if comp == 0:
        stride = ((w * bpp + 31) // 32) * 4
        for row in range(h):
            src = p + row * stride
            y = row_y(row)
            for x in range(w):
                if bpp == 24:
                    put(x, y, (d[src + x * 3 + 2], d[src + x * 3 + 1], d[src + x * 3]))
                    continue
                if bpp == 1:
                    idx = (d[src + (x >> 3)] >> (7 - (x & 7))) & 1
                elif bpp == 4:
                    idx = d[src + (x >> 1)] >> 4 if x % 2 == 0 else d[src + (x >> 1)] & 0x0F
                elif bpp == 8:
                    idx = d[src + x]
                else:
                    raise ValueError("%s: unsupported %d bpp" % (what, bpp))
                put(x, y, pal[idx])
    elif (comp, bpp) in ((1, 8), (2, 4)):
        four = bpp == 4
        x = row = 0
        while p + 1 < end:
            count, val = d[p], d[p + 1]
            p += 2
            if count:
                for i in range(count):
                    idx = val if not four else (val >> 4 if i % 2 == 0 else val & 0x0F)
                    put(x, row_y(row), pal[idx])
                    x += 1
            elif val == 0:
                x, row = 0, row + 1
            elif val == 1:
                break
            elif val == 2:
                x += d[p]
                row += d[p + 1]
                p += 2
            else:
                for i in range(val):
                    at = p + (i >> 1) if four else p + i
                    idx = (d[at] >> 4 if i % 2 == 0 else d[at] & 0x0F) if four else d[at]
                    put(x, row_y(row), pal[idx])
                    x += 1
                n = (val + 1) // 2 if four else val
                p += n + (n & 1)
    else:
        raise ValueError("%s: unsupported %d bpp / compression %d" % (what, bpp, comp))
    return w, h, px


def fold(game, mask, what):
    """Game bitmap + 1-bit mask -> RGBA, the mask in the alpha channel.

    The original blits mask-SRCAND then bitmap-SRCPAINT, so a white mask pixel
    leaves the background: alpha 0 there.  Only the sprites that are ever
    blitted that way get it -- BMSTA's, *plus the tunnel*, whose branch in
    UpDateSprite (LTANK2.C:498) masks over a coloured rectangle regardless of
    BMSTA[55] being 0.  For the rest the original does a plain SRCCOPY and
    never looks at the mask, which in the internal pair is solid white for
    exactly those sprites.
    """
    gw, gh, gpx = game
    mw, mh, mpx = mask
    for w, h, side in ((gw, gh, "game"), (mw, mh, "mask")):
        if (w, h) != (SHEET_W, SHEET_H):
            raise ValueError("%s: %s bitmap is %dx%d, expected %dx%d"
                             % (what, side, w, h, SHEET_W, SHEET_H))

    cut_cell = set()
    for bm in range(1, MAX_BITMAPS + 1):
        c = cell_of(bm)
        if c and (BMSTA[bm] == 1 or bm == TUNNEL_BM):
            cut_cell.add(c)

    out = bytearray(SHEET_W * SHEET_H * 4)
    for y in range(SHEET_H):
        for x in range(SHEET_W):
            i3, i4 = (y * SHEET_W + x) * 3, (y * SHEET_W + x) * 4
            out[i4:i4 + 3] = gpx[i3:i3 + 3]
            white = all(v > 127 for v in mpx[i3:i3 + 3])
            cut = white and (x // SPRITE, y // SPRITE) in cut_cell
            out[i4 + 3] = 0 if cut else 255
    return bytes(out)


def ltg_sheet(path):
    """LoadLTG (LTANK2.C:688): 324-byte header, game bitmap, then the mask."""
    d = path.read_bytes()
    if d[315:319] != b"LTG1":
        raise ValueError("%s: not an LTG file" % path.name)
    mask_off = _u32(d, 320)
    return fold(bmp_decode(d, 324, mask_off, path.name + " (game)"),
                bmp_decode(d, mask_off, len(d), path.name + " (mask)"),
                path.name)


def internal_sheet():
    """The pair the 2007 build carries as resources, GFXInit's mode 0."""
    src = ROOT / "original" / "src"
    g, m = (src / "Game.BMP").read_bytes(), (src / "Mask.BMP").read_bytes()
    return fold(bmp_decode(g, 0, len(g), "Game.BMP"),
                bmp_decode(m, 0, len(m), "Mask.BMP"), "internal")


def find_godot():
    env = os.environ.get("LT_GODOT")
    if env and pathlib.Path(env).exists():
        return env
    pat = os.path.expanduser(
        "~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono*"
        "/*/Godot_*_console.exe")
    hits = sorted(glob.glob(pat))
    return hits[-1] if hits else None


def godot_hashes(godot):
    """Run the Godot project's own --check-sheets -> {label: sha256}."""
    p = subprocess.run([godot, "--headless", "--path",
                        str(ROOT / "src" / "LaserTank.Game"), "--", "--check-sheets"],
                       capture_output=True, text=True, cwd=str(ROOT), timeout=600)
    out = {}
    for line in p.stdout.splitlines():
        m = re.match(r"sheet (\S+) \d+x\d+ sha256=([0-9a-f]{64})", line.strip())
        if m:
            out[m.group(1)] = m.group(2)
    if not out:
        print(p.stdout[-2000:] or p.stderr[-2000:])
    return out


def check_sheets(args):
    sheets = {}
    ok = True
    try:
        sheets["internal"] = internal_sheet()
    except Exception as e:                                  # noqa: BLE001
        ok = False
        print("  internal  FAIL %s" % e)
    for p in sorted(GRAPHICS.glob("*.ltg")):
        try:
            sheets[p.name] = ltg_sheet(p)
        except Exception as e:                              # noqa: BLE001
            ok = False
            print("  %-22s FAIL %s" % (p.name, e))
    for label, rgba in sheets.items():
        print("  %-22s %dx%d  sha256=%s"
              % (label, SHEET_W, SHEET_H, hashlib.sha256(rgba).hexdigest()))

    godot = None if args.no_godot else find_godot()
    if godot is None:
        print("  cross-check SKIPPED: no Godot found (set LT_GODOT) -- the C# "
              "decoder was NOT compared")
        return ok and not args.require_godot
    cs = godot_hashes(godot)
    for label, rgba in sheets.items():
        mine = hashlib.sha256(rgba).hexdigest()
        theirs = cs.get(label)
        if theirs is None:
            ok = False
            print("  %-22s FAIL C# produced no sheet" % label)
        elif theirs != mine:
            ok = False
            print("  %-22s FAIL python %s != c# %s" % (label, mine[:16], theirs[:16]))
    if ok:
        print("  cross-check: %d sheets, python and C# agree byte for byte" % len(sheets))
    return ok


def main():
    ap = argparse.ArgumentParser(
        description="Phase 5 step 0's gate: atlas geometry and bitmap numbers.",
        epilog="exit 0 clean, 1 bad bitmap number or sheet mismatch")
    ap.add_argument("files", nargs="*", type=pathlib.Path,
                    help="level files (default: LaserTank.lvl + all quirk packs)")
    ap.add_argument("--all", action="store_true",
                    help="all 13 collections, not just the flagship")
    ap.add_argument("--keys", default="",
                    help='keystream per level (default: "" -- load and idle)')
    ap.add_argument("--stride", type=int, default=1, metavar="N",
                    help="only every Nth level -- a spot check")
    ap.add_argument("--jobs", type=int, default=8)
    ap.add_argument("--sheets-only", action="store_true")
    ap.add_argument("--grid-only", action="store_true")
    ap.add_argument("--no-godot", action="store_true",
                    help="skip the C# cross-check even if Godot is installed")
    ap.add_argument("--require-godot", action="store_true",
                    help="fail rather than skip when Godot is missing")
    args = ap.parse_args()

    good = True
    if not args.sheets_only:
        good &= check_grid(args)
    if not args.grid_only:
        print("sheets:")
        good &= check_sheets(args)
    print("atlas_check: %s" % ("OK" if good else "FAILED"))
    return 0 if good else 1


if __name__ == "__main__":
    sys.exit(main())
