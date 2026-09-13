#!/usr/bin/env python
"""A PNG reader and writer with no third-party dependency.

Independent of anything installed, on purpose: this machine has neither PIL
nor numpy, and tools/atlas_check.py already hand-rolls a BMP reader for the
same reason -- a gate that needs `pip install` is a gate that stops running.

    decode(bytes) -> (w, h, bytearray of RGB triples, row-major)
    load(path)    -> the same
    encode(w, h, rgb) -> an 8-bit truecolour PNG, filter-0 rows

Reads the eight-bit non-interlaced colour types Blogger serves for the
harvester's screenshots -- 0 grey, 2 RGB, 3 palette, 6 RGBA -- and raises on
anything else rather than guessing.  Sixteen-bit and interlaced PNGs have
never appeared in 6,218 posts; if one does, this is the line that will say so.
"""
import struct
import zlib

MAGIC = b"\x89PNG\r\n\x1a\n"


def decode(d):
    if d[:8] != MAGIC:
        raise ValueError("not a PNG")
    w = h = ctype = None
    pal = None
    idat = bytearray()
    o = 8
    while o + 8 <= len(d):
        ln = struct.unpack_from(">I", d, o)[0]
        typ = d[o + 4:o + 8]
        body = d[o + 8:o + 8 + ln]
        if typ == b"IHDR":
            w, h, depth, ctype, comp, filt, inter = struct.unpack(">IIBBBBB", body)
            if depth != 8 or inter != 0 or ctype not in (0, 2, 3, 6):
                raise ValueError("unsupported PNG: depth=%d ctype=%d interlace=%d"
                                 % (depth, ctype, inter))
        elif typ == b"PLTE":
            pal = [(body[i], body[i + 1], body[i + 2]) for i in range(0, ln, 3)]
        elif typ == b"IDAT":
            idat += body
        elif typ == b"IEND":
            break
        o += 12 + ln
    if w is None:
        raise ValueError("no IHDR")

    raw = zlib.decompress(bytes(idat))
    nch = {0: 1, 2: 3, 3: 1, 6: 4}[ctype]
    stride = w * nch
    prev = bytearray(stride)
    out = bytearray(w * h * 3)
    p = 0
    for y in range(h):
        ft = raw[p]; p += 1
        line = bytearray(raw[p:p + stride]); p += stride
        if ft == 1:
            for i in range(nch, stride):
                line[i] = (line[i] + line[i - nch]) & 0xFF
        elif ft == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ft == 3:
            for i in range(stride):
                a = line[i - nch] if i >= nch else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif ft == 4:
            for i in range(stride):
                a = line[i - nch] if i >= nch else 0
                b = prev[i]
                c = prev[i - nch] if i >= nch else 0
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        elif ft != 0:
            raise ValueError("bad PNG filter %d on row %d" % (ft, y))
        prev = line

        q = y * w * 3
        if ctype == 2:
            out[q:q + w * 3] = line
        elif ctype == 6:
            for x in range(w):
                out[q + x * 3:q + x * 3 + 3] = line[x * 4:x * 4 + 3]
        elif ctype == 3:
            for x in range(w):
                out[q + x * 3:q + x * 3 + 3] = bytes(pal[line[x]])
        else:
            for x in range(w):
                v = line[x]
                out[q + x * 3:q + x * 3 + 3] = bytes((v, v, v))
    return w, h, out


def load(path):
    with open(path, "rb") as f:
        return decode(f.read())


def encode(w, h, rgb):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgb[y * w * 3:(y + 1) * w * 3]

    def chunk(t, b):
        return (struct.pack(">I", len(b)) + t + b
                + struct.pack(">I", zlib.crc32(t + b) & 0xFFFFFFFF))
    return (MAGIC
            + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))
