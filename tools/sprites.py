#!/usr/bin/env python
"""The game's own sprite sheet, and every board cell it can draw.

This exists so the harvester's goal-tile table is *derived* rather than
labelled by eye.  A start screenshot labels itself, because the corpus already
knows that board; nothing labels the states only play produces -- an anti-tank
pushed onto ice, a block sunk in water, the tank facing anywhere but up -- and
session 34 costed that residue as half a session of hand input.  It is not hand
input: the 2010 binary's own graphics are committed under `original/src/`, the
tables that pick a sprite are in `LTANK2.C`, and putting the two together
reproduces the blog's pixels exactly.

Three things have to be right, and all three are in the original source:

  * **Size.**  LTANK2.C:1742 sets `SpBm_Width = SpBm_Height = 24`, and GFXInit
    (:766) StretchBlt's the whole 320x192 sheet down to `24*10 x 24*6`, so
    every 32x32 sprite is drawn at 24x24.  `BMA[i]` is filled row-major *from
    i = 1* (:784), so bitmap `i` sits at cell `((i-1) % 10, (i-1) / 10)`.

  * **The shrink.**  GFXInit never calls SetStretchBltMode, so the mode is
    GDI's default BLACKONWHITE = STRETCH_ANDSCANS: the rows and columns the
    shrink eliminates are *ANDed* into the ones that survive, per RGB channel.
    The grouping is `dst = (src * 24 + 12) // 32`.  Both were solved from a
    real dirt tile rather than guessed, and reproduce it pixel-exactly -- a
    plain nearest-neighbour shrink gets the palette right and the pixels wrong,
    which is the whole reason this looked like a labelling job.

  * **The composite.**  UpDateSprite (:487) draws a cell as the `BMF2`
    background -- an opaque sprite, or a `ColorList` rectangle for a tunnel --
    and then, if the foreground `BMF` is transparent (`BMSTA[bmn] == 1`), the
    mask ANDed in and the sprite ORed on top.  UpDateTank (:537) is a further
    mask+OR of the tank sprite over whatever the cell already shows, and
    UpDateLaser (:549) paints a plain `Rectangle` inset by `LaserOffset = 10`.

`PF` then follows from the bitmap, and three rules are not just GetOBMArray:

  * a shot anti-tank is **`PF = 4`**, not dirt, with junk bitmap 54/52/12/53 for
    the way it was facing -- KillAtank, `Engine.cs:868`; the wreck keeps
    blocking the square;
  * a block pushed into water is **`PF = 0`** with `BMF = BMF2 = 19`,
    `Engine.cs:731`;
  * the tank is **not in `PF` at all** -- BuildBMField clears `PF` at its cell
    on load (`Engine.cs:348`), so a cell with the tank on it carries the
    terrain's `PF` and the tank is separate output.  This is the one place the
    blog's pixels are genuinely ambiguous: `T` as a foreground bitmap and the
    tank overlay facing up are the same pixels, and the rule above is what
    decides them.

    python tools/sprites.py                     # the tables, and what they cover
    python tools/sprites.py --sheet build/sheet24.png

Used by `tools/harvest.py tiles`.  No third-party dependency, same reason
`png.py` and `atlas_check.py` hand-roll their own readers.
"""
import argparse
import hashlib
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME_BMP = os.path.join(ROOT, "original", "src", "Game.BMP")
MASK_BMP = os.path.join(ROOT, "original", "src", "Mask.BMP")

PITCH = 24                      # LTANK2.C:1742, SpBm_Width
SHEET_W, SHEET_H = PITCH * 10, PITCH * 6
NBYTES = PITCH * PITCH * 3
MAXBITMAPS = 57

# GetOBMArray (LTANK2.C:77): PF value -> the bitmap that draws it.
OBM = [1, 2, 6, 9, 13, 14, 15, 16, 36, 39, 42, 20, 21, 22, 23, 24, 27, 30, 33,
       45, 47, 48, 49, 50, 56, 57, 55]
# BMSTA (LTANK2.C:80): 1 = transparent, so the cell needs a background under it.
BMSTA = [0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 1, 1, 0, 1, 1, 1,
         1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0,
         0, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0]
# ColorList (LTANK2.C:81), COLORREF 0x00BBGGRR: the tunnel rectangles.
COLORREF = [0x000000FF, 0x0000FF00, 0x00FF0000, 0x00FFFF00, 0x0000FFFF,
            0x00FF00FF, 0x00FFFFFF, 0x00808080]
# AnimateSprites (LTANK2.C:1105): these PF values cycle base + AniLevel, 0..2.
ANIM = frozenset((2, 3, 7, 8, 9, 10, 15, 16, 17, 18))
# KillAtank (LTANK2.C:1449 / Engine.cs:868): junk bitmap -> the facing it had.
JUNK = {54: "^", 52: ">", 12: "v", 53: "<"}
SUNK = 19                       # a block pushed into water, Engine.cs:731
TANKBM = {2: "up", 3: "right", 4: "down", 5: "left"}    # Tank.Dir 1..4
LASER_OFFSET = 10                                       # LTANK2.C:46
LASER_RGB = {"red": (0xFF, 0, 0), "green": (0, 0xFF, 0)}    # LTANK.C:449

SYM = {0: ".", 1: "T", 2: "F", 3: "~", 4: "#", 5: "B", 6: "b", 7: "^", 8: ">",
       9: "v", 10: "<", 11: "m", 12: "n", 13: "o", 14: "p", 15: "U", 16: "R",
       17: "D", 18: "L", 19: "C", 20: "q", 21: "w", 22: "e", 23: "r", 24: "I",
       25: "i"}


# ------------------------------------------------------------------ the sheet

def bmp(path):
    """A 4/8 bpp BMP -> (w, h, top-down RGB).  BI_RGB, BI_RLE8 and BI_RLE4.

    The default pack is RLE-compressed where the shipped `.ltg` packs are not,
    which is why this cannot reuse atlas_check.py's BI_RGB-only reader.
    """
    with open(path, "rb") as f:
        d = f.read()
    off = struct.unpack_from("<I", d, 10)[0]
    w, h = struct.unpack_from("<ii", d, 18)
    bpp, = struct.unpack_from("<H", d, 28)
    comp, = struct.unpack_from("<I", d, 30)
    if bpp not in (4, 8) or comp not in (0, 1, 2):
        raise ValueError("%s: bpp=%d compression=%d not supported"
                         % (path, bpp, comp))
    pb = 14 + struct.unpack_from("<I", d, 14)[0]
    pal = [bytes(d[pb + i * 4:pb + i * 4 + 3][::-1]) for i in range(1 << bpp)]
    idx = bytearray(w * h)

    def put(x, y, v):
        if 0 <= x < w and 0 <= y < h:
            idx[y * w + x] = v

    if comp == 0:
        stride = ((w * bpp + 31) // 32) * 4
        for y in range(h):
            for x in range(w):
                b = d[off + y * stride + (x if bpp == 8 else x >> 1)]
                put(x, y, b if bpp == 8 else
                    ((b >> 4) if x % 2 == 0 else (b & 0xF)))
    else:
        p, x, y = off, 0, 0
        while p < len(d) - 1:
            n, c = d[p], d[p + 1]
            p += 2
            if n:                                       # encoded run
                for k in range(n):
                    put(x, y, c if bpp == 8 else
                        ((c >> 4) if k % 2 == 0 else (c & 0xF)))
                    x += 1
            elif c == 0:                                # end of line
                x, y = 0, y + 1
            elif c == 1:                                # end of bitmap
                break
            elif c == 2:                                # delta
                x, y, p = x + d[p], y + d[p + 1], p + 2
            elif bpp == 8:                              # absolute run
                for k in range(c):
                    put(x, y, d[p + k])
                    x += 1
                p += c + (c & 1)
            else:
                nb = (c + 1) // 2
                for k in range(c):
                    b = d[p + (k >> 1)]
                    put(x, y, (b >> 4) if k % 2 == 0 else (b & 0xF))
                    x += 1
                p += nb + (nb & 1)

    px = bytearray(w * h * 3)
    for y in range(h):
        row = h - 1 - y                                 # BMPs are bottom-up
        for x in range(w):
            px[(y * w + x) * 3:(y * w + x) * 3 + 3] = pal[idx[row * w + x]]
    return w, h, bytes(px)


GROUP = [(i * PITCH + PITCH // 2) // 32 for i in range(32)]


def _shrink_tile(px, w, bmn):
    """Bitmap `bmn` at 24x24, the way GFXInit's StretchBlt produces it."""
    cx, cy = (bmn - 1) % 10, (bmn - 1) // 10
    acc = [bytearray(b"\xff\xff\xff") for _ in range(PITCH * PITCH)]
    for sy in range(32):
        dy = GROUP[sy]
        for sx in range(32):
            i = ((cy * 32 + sy) * w + cx * 32 + sx) * 3
            a = acc[dy * PITCH + GROUP[sx]]
            a[0] &= px[i]
            a[1] &= px[i + 1]
            a[2] &= px[i + 2]
    return b"".join(bytes(a) for a in acc)


def sheets(game=GAME_BMP, mask=MASK_BMP):
    """{bitmap number: 24x24 RGB} for the sprites and for their masks."""
    gw, _, g = bmp(game)
    mw, _, m = bmp(mask)
    return ({i: _shrink_tile(g, gw, i) for i in range(1, MAXBITMAPS + 1)},
            {i: _shrink_tile(m, mw, i) for i in range(1, MAXBITMAPS + 1)})


# --------------------------------------------------------------- the composite
# Bitwise work on whole tiles as big integers: SRCAND and SRCPAINT are per-byte
# and a 1,728-byte Python loop per composite would dominate the run.

def _i(b):
    return int.from_bytes(b, "big")


def _b(v):
    return v.to_bytes(NBYTES, "big")


class Cells(object):
    """Every cell the engine can draw, and the `PF` the engine holds for it."""

    def __init__(self, game=GAME_BMP, mask=MASK_BMP):
        self.spr, self.msk = sheets(game, mask)
        self._spr = {k: _i(v) for k, v in self.spr.items()}
        self._msk = {k: _i(v) for k, v in self.msk.items()}
        self._seen = {}
        # bitmap -> PF symbol, and a human name, for every bitmap play can show
        self.pf, self.name = {}, {}
        for v in range(26):
            base = OBM[v]
            for a in ((0, 1, 2) if v in ANIM else (0,)):
                self.pf[base + a] = SYM[v]
                self.name[base + a] = SYM[v] + ("/%d" % a if v in ANIM else "")
        for b, facing in JUNK.items():
            self.pf[b] = SYM[4]                  # KillAtank: a solid wreck
            self.name[b] = "wreck(%s)" % facing
        self.pf[SUNK] = SYM[0]                   # a block sunk in water
        self.name[SUNK] = "sunk"

    def over(self, bg, bmn):
        """PutSprite's transparency: background, mask SRCAND, sprite SRCPAINT."""
        return _b((_i(bg) & self._msk[bmn]) | self._spr[bmn])

    def tunnel(self, tid):
        """A tunnel cell: the ColorList rectangle, then bitmap 55 over it."""
        c = COLORREF[tid]
        flat = bytes((c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF))
        return self.over(flat * (PITCH * PITCH), 55)

    def laser(self, cell, vertical, colour="red"):
        """UpDateLaser: a Rectangle inset by LaserOffset, over a drawn cell.

        GDI outlines with the current (black) pen and fills with the brush, and
        the right and bottom edges are excluded -- so a beam across the cell is
        two black rows enclosing two coloured ones, the full width of the cell.
        """
        out = bytearray(cell)
        lo, hi = LASER_OFFSET, PITCH - LASER_OFFSET - 1
        rgb = bytes(LASER_RGB[colour])
        for along in range(PITCH):
            for across in range(lo, hi + 1):
                x, y = (across, along) if vertical else (along, across)
                edge = across in (lo, hi) or along in (0, PITCH - 1)
                i = (y * PITCH + x) * 3
                out[i:i + 3] = b"\x00\x00\x00" if edge else rgb
        return bytes(out)

    def backgrounds(self):
        """Every opaque cell a transparent sprite can be drawn on top of."""
        out = {}
        for bmn, sym in self.pf.items():
            if BMSTA[bmn] == 0:
                out[self.name[bmn]] = (self.spr[bmn], sym)
        for t in range(8):
            out["tun%d" % t] = (self.tunnel(t), str(t))
        return out

    def table(self, laser=True):
        """{tile hash: (PF symbol, tank facing or None, description)}.

        A hash two different `PF` values can both produce is dropped and
        reported by `conflicts()` rather than resolved silently -- so an
        ambiguous tile decodes as *unknown*, loudly, instead of as a guess.

        Two notes on what that leaves, because both look like gaps and are not.
        `T` is excluded from the foregrounds: `PF` is never 1 at runtime
        (`Engine.cs:348`), so the tank is only ever the overlay, and that alone
        removes the only ambiguity the goal boards actually contain.  What is
        left over -- 289 hashes -- is entirely **the tank drawn on top of an
        anti-tank or its wreck**, which the tank occludes completely; every one
        of those states is unreachable, since `PF` 7-10 and `PF` 4 are both
        impassable, so there is nothing there to resolve.
        """
        seen = {}

        def add(px, sym, tank, desc):
            h = hashlib.md5(px).hexdigest()[:16]
            seen.setdefault(h, set()).add((sym, tank, desc))

        fg = [b for b in range(1, MAXBITMAPS + 1)
              if BMSTA[b] == 1 and b in self.pf and b not in TANKBM]
        for bname, (bpx, bsym) in self.backgrounds().items():
            layers = [(bpx, bsym, bname)]
            for b in fg:
                layers.append((self.over(bpx, b), self.pf[b],
                               "%s on %s" % (self.name[b], bname)))
            for px, sym, desc in layers:
                add(px, sym, None, desc)
                # The tank is not in PF, so it changes the pixels and not the
                # symbol -- and it occludes whatever it is standing on.
                for tb, facing in TANKBM.items():
                    add(self.over(px, tb), sym, facing,
                        "%s + tank %s" % (desc, facing))
                if laser:
                    for vert in (False, True):
                        for col in LASER_RGB:
                            add(self.laser(px, vert, col), sym, None,
                                "%s + %s laser %s"
                                % (desc, col, "up" if vert else "across"))
        self._seen = seen
        out = {}
        for h, v in seen.items():
            syms = {s for s, _, _ in v}
            if len(syms) == 1:
                tanks = {t for _, t, _ in v}
                out[h] = (syms.pop(),
                          tanks.pop() if len(tanks) == 1 else None,
                          sorted(d for _, _, d in v)[0])
        return out

    def conflicts(self):
        """Hashes two different PF values both produce.  Call after table()."""
        return {h: sorted(v) for h, v in self._seen.items()
                if len({s for s, _, _ in v}) > 1}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sheet", help="write the shrunk 240x144 sheet here")
    ap.add_argument("--no-laser", action="store_true")
    a = ap.parse_args()

    c = Cells()
    t = c.table(laser=not a.no_laser)
    print("sprite sheet: %s + %s"
          % (os.path.relpath(GAME_BMP, ROOT), os.path.relpath(MASK_BMP, ROOT)))
    print("bitmaps: %d, of which %d transparent; PF-bearing: %d"
          % (MAXBITMAPS, sum(BMSTA[1:MAXBITMAPS + 1]), len(c.pf)))
    print("backgrounds: %d terrains + 8 tunnels"
          % (len(c.backgrounds()) - 8))
    print("cells derived: %d distinct tiles with an unambiguous PF" % len(t))
    print("dropped as PF-ambiguous: %d" % len(c.conflicts()))

    if a.sheet:
        sheet = bytearray(SHEET_W * SHEET_H * 3)
        for i in range(1, MAXBITMAPS + 1):
            cx, cy = (i - 1) % 10, (i - 1) // 10
            for yy in range(PITCH):
                d = ((cy * PITCH + yy) * SHEET_W + cx * PITCH) * 3
                s = yy * PITCH * 3
                sheet[d:d + PITCH * 3] = c.spr[i][s:s + PITCH * 3]
        with open(a.sheet, "wb") as f:
            f.write(png.encode(SHEET_W, SHEET_H, sheet))
        print("sheet -> %s" % a.sheet)
    return 0


if __name__ == "__main__":
    sys.exit(main())
