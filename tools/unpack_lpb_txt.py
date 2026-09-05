#!/usr/bin/env python
"""Unpack a Text-Converter .txt wrapper back into the .lpb it contains.

Some community playbacks were distributed as text (for mail/forums) using the
pack format LaserTank's Text-Converter.exe produces: a header block, then
base64 of a small preamble followed by the raw .lpb.  data/quirks/rotary-mirrors
ships exactly one of these, for level 21.

    python tools/unpack_lpb_txt.py FILE.txt [-o OUT.lpb]
"""
import argparse
import base64
import pathlib
import struct


def unpack(text):
    body = text.split("-----BEGIN PACKED DATA-----")[1]
    body = body.split("-----END FILE-----")[0]
    raw = base64.b64decode("".join(body.split()))

    # The .lpb starts after a variable-length preamble.  Locate it by the
    # 66-byte TRECORDREC header: name[31], author[31], u16 level, u16 size,
    # where size must run exactly to end of data.
    for off in range(len(raw) - 66):
        level, size = struct.unpack_from("<HH", raw, off + 62)
        if 0 < level < 65535 and off + 66 + size == len(raw):
            return raw[off:], level, size
    raise SystemExit("no .lpb framing found in packed data")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("txt")
    ap.add_argument("-o", "--out")
    args = ap.parse_args()

    src = pathlib.Path(args.txt)
    lpb, level, size = unpack(src.read_text(errors="replace"))
    out = pathlib.Path(args.out) if args.out else src.with_suffix(".lpb")
    out.write_bytes(lpb)

    name = lpb[0:31].split(b"\0")[0].decode("latin1")
    auth = lpb[31:62].split(b"\0")[0].decode("latin1")
    print(f"{out}  level={level} keys={size} name={name!r} author={auth!r}")


if __name__ == "__main__":
    main()
