#!/usr/bin/env python
"""Phase 5 step 3's gate: the sounds, and the ids that choose them.

Three checks.  The first is the one that matters, and it is the same kind of
check the whole project is built on -- a differential against the 25-year-old C:

  corpus  every recorded .lpb replayed through **both** engines with --sound,
          which adds SF=<ids> to each tick line: the SoundPlay calls that tick
          made, in call order.  The oracle records them in its own stub
          (oracle/driver.c), the port records them in Engine.SoundLog, and the
          traces must be byte-identical -- so "does the port play the right
          sound at the right moment" is answered by a trace diff rather than by
          listening.  This is what caught SoundPlay(S_Move) being dropped from
          UpDateTankPos when Phase 2 read it as paint.

  wavs    the sixteen .wav files decode, and decode to the **same PCM in two
          independent readers**: Python's here, and C#'s SoundFile through
          `godot --headless -- --check-sounds`, compared by sha256.  Plus the
          id -> name table, which this file carries its own copy of (lt_sfx.c's
          SoundLoad order) so that editing SoundFile.Names alone fails.

  ini     [OPT] Sound: the default, the original's case-sensitive test, the
          round trip through --save-options, and that writing it leaves the
          rest of LaserTank.ini alone.

    python tools/sound_check.py                  # all three, ~90 s
    python tools/sound_check.py --corpus-only --jobs 8
    python tools/sound_check.py --wavs-only

Needs both engines (bash oracle/build.sh, bash src/build.sh) for `corpus`, and
Godot for the cross-check half of `wavs` and for `ini`; a missing Godot makes
those SKIP loudly rather than pass quietly.  Nothing here touches
build/lasertank-solve.exe or the player's own LaserTank.ini, so it is safe to
run beside a live solve.

Exit: 0 clean, 1 a divergence or a bad file, 2 environment.
"""
import argparse
import hashlib
import pathlib
import re
import subprocess
import sys
import tempfile
import time
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
import tick_check                                       # noqa: E402
from engines import ROOT                                # noqa: E402
from atlas_check import find_godot                      # noqa: E402

SOUNDS = ROOT / "original" / "src" / "Sounds"
GAME = ROOT / "src" / "LaserTank.Game"

# lt_sfx.c:47, SoundLoad's call order -- which is the LT_Sound_Types enum
# (lt_sfx.h:13) and Ltank.rc's RCDATA list.  Index 0 is unused because
# SoundLoad pre-increments LastSFWord, so the first sound is id 1.
#
# This is a second copy on purpose: LaserTank.Core/SoundFile.cs has the other
# one, and a table that agrees with itself is not a check.
NAMES = [None,
         "BRICKS", "FIRE", "MOVE", "HEAD", "TURN", "ENDLEV", "DIE", "ANTI1",
         "ANTI2", "DEFLB", "LASER2", "PUSH2", "PUSH1", "ROTATE", "PUSH3", "SINK"]

# Where each id is called from, for the coverage line.  Not a check -- a reader
# aid, so "id 16 never fired" can be recognised as "no corpus recording pushes
# anything into water" rather than as a missing sound.
WHERE = {
    1: "laser vs brick (LTANK2.C:1490)",
    2: "the tank firing (LTANK.C:634)",
    3: "UpDateTankPos (LTANK2.C:1221)",
    4: "tank into something hard (:1254)",
    5: "tank turning (:1248)",
    6: "reaching the flag (LTANK.C:650)",
    7: "WM_Dead (LTANK.C:724)",
    8: "an anti-tank dying (:1456)",
    9: "an anti-tank firing (:1044 via FireLaser)",
    10: "laser deflected (:1618)",
    11: "laser hitting anything else (:1482)",
    12: "pushing an object (:1528 MoveObj S_Push2)",
    13: "pushing a block (:797 MoveObj S_Push1)",
    14: "a mirror rotating (:1530)",
    15: "pushing the heavy things (MoveObj S_Push3)",
    16: "something sinking (MoveObj, PF == 3)",
}


# --- check 1: the corpus, both engines, SF and all ---------------------------


class _Case:
    """engines.command's duck type: a level file plus a .lpb."""

    def __init__(self, levels, lpb):
        self.levels = levels
        self.lpb = lpb
        self.level = 0
        self.keys = ""


_local = __import__("threading").local()


def scratch():
    if not hasattr(_local, "s"):
        _local.s = engines.Scratch("lt-sound")
    return _local.s


SF_RE = re.compile(r" SF=([-0-9,+]+)")


def ids_in(trace):
    """-> Counter of sound ids, and the number of ticks that made a sound."""
    read = engines.read_trace(trace)
    hist, loud, seen_sf = Counter(), 0, False
    if read is None:
        return hist, loud, seen_sf
    for line in read[1]:
        m = SF_RE.search(line)
        if not m:
            continue
        seen_sf = True
        if m.group(1) == "-":
            continue
        loud += 1
        for part in m.group(1).rstrip("+").split(","):
            hist[int(part)] += 1
    return hist, loud, seen_sf


def check_corpus(args):
    engines.require_engines()
    cases = tick_check.recordings(args.collection)
    if args.stride > 1:
        cases = cases[::args.stride]
    if not cases:
        print("corpus: no recordings found")
        return False
    print("corpus: %d recordings, both engines, --sound%s"
          % (len(cases), " --field" if args.field else ""))

    hist = Counter()
    bad = []
    loud_ticks = total_ticks = 0
    no_sf = []

    def work(entry):
        lvl, lpb, _stem = entry
        s = scratch()
        case = _Case(lvl, lpb)
        a, b = engines.run_pair(case, s.a, s.b, field=args.field, sound=True)
        div = engines.compare(a, b)
        h, loud, seen = ids_in(s.a)
        ticks = len(engines.read_trace(s.a)[1]) if engines.read_trace(s.a) else 0
        return lpb, div, h, loud, ticks, seen

    t0 = time.time()
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for i, (lpb, div, h, loud, ticks, seen) in enumerate(pool.map(work, cases), 1):
            if div is not None:
                bad.append((lpb, div))
                print("  %-44s DIVERGED  %s\n    %s"
                      % (lpb.name, div.kind, div.detail.splitlines()[0]))
            if not seen:
                no_sf.append(lpb)
            hist += h
            loud_ticks += loud
            total_ticks += ticks
            if i % 25 == 0 or i == len(cases):
                print("  %-40s" % ("%d/%d" % (i, len(cases))), end="\r", flush=True)
    print(" " * 44, end="\r")

    if no_sf:
        print("  %d recording(s) carried no SF field at all -- --sound is not "
              "reaching one of the engines" % len(no_sf))
        return False

    unknown = sorted(k for k in hist if not 1 <= k <= 16)
    if unknown:
        print("  ids outside 1..16: %s" % unknown)

    print("corpus: %d/%d identical (SF included)   %d of %d ticks made a sound   (%.0fs)"
          % (len(cases) - len(bad), len(cases), loud_ticks, total_ticks,
             time.time() - t0))
    print("  %-8s %-9s %10s   %s" % ("id", "name", "calls", "from"))
    for i in range(1, 17):
        print("  %-8d %-9s %10s   %s"
              % (i, NAMES[i], "{:,}".format(hist[i]) if hist[i] else "-", WHERE[i]))
    missing = [NAMES[i] for i in range(1, 17) if not hist[i]]
    if missing:
        print("  never fired in the corpus: %s   (not a failure -- no recording "
              "reaches them)" % ", ".join(missing))
    return not bad and not unknown


# --- check 2: the sixteen files ----------------------------------------------


def wav_decode(path):
    """A RIFF/WAVE file -> (rate, channels, bits, signed 8-bit PCM bytes).

    An independent reader, deliberately: LaserTank.Core/SoundFile.cs is the
    other one, and the point of the cross-check is that two of them agree.
    8-bit WAV samples are *unsigned* and every raw-PCM consumer wants them
    signed, which is the xor below and the one conversion that silently
    produces a click instead of an error.
    """
    d = path.read_bytes()
    if d[:4] != b"RIFF" or d[8:12] != b"WAVE":
        raise ValueError("not a RIFF/WAVE file")
    fmt = ch = rate = bits = 0
    data = None
    i = 12
    while i + 8 <= len(d):
        cid = d[i:i + 4]
        size = int.from_bytes(d[i + 4:i + 8], "little")
        body = i + 8
        size = min(size, len(d) - body)
        if cid == b"fmt " and size >= 16:
            fmt = int.from_bytes(d[body:body + 2], "little")
            ch = int.from_bytes(d[body + 2:body + 4], "little")
            rate = int.from_bytes(d[body + 4:body + 8], "little")
            bits = int.from_bytes(d[body + 14:body + 16], "little")
        elif cid == b"data":
            data = d[body:body + size]
        i = body + size + (size & 1)
    if data is None or not rate:
        raise ValueError("no fmt/data chunk")
    if fmt != 1:
        raise ValueError("WAVE format %d, only PCM is read" % fmt)
    if bits == 8:
        pcm = bytes(b ^ 0x80 for b in data)
    elif bits == 16:
        pcm = bytes(data[1::2])
    else:
        raise ValueError("%d-bit PCM" % bits)
    return rate, ch, bits, pcm


GODOT_RE = re.compile(
    r"sound (\d+) (\S+) file=(\S+) rate=(\d+) channels=(\d+) bits=(\d+) "
    r"frames=(\d+) sha256=([0-9a-f]{64})")


def godot_sounds(godot):
    """The project's own --check-sounds -> {id: (name, rate, frames, sha256)}.

    Builds first: `godot --path` does not compile C#, so without this the
    cross-check could compare Python against the previous session's assembly
    and call it agreement (engines.build_godot_game).
    """
    _ok, warn = engines.build_godot_game()
    print("  build: LaserTank.Game ok%s"
          % ("" if not warn else "   %d warning(s)" % warn))
    p = subprocess.run([godot, "--headless", "--path", str(GAME), "--",
                        "--check-sounds"],
                       capture_output=True, text=True, cwd=str(ROOT), timeout=600)
    out = {}
    for line in p.stdout.splitlines():
        m = GODOT_RE.match(line.strip())
        if m:
            out[int(m.group(1))] = (m.group(2), int(m.group(4)), int(m.group(7)),
                                    m.group(8))
    if not out:
        print(p.stdout[-2000:] or p.stderr[-2000:])
    return out


def check_wavs(args):
    ok = True
    mine = {}
    for i in range(1, 17):
        name = NAMES[i]
        hits = [p for p in SOUNDS.iterdir()
                if p.name.lower() == (name + ".wav").lower()]
        if not hits:
            ok = False
            print("  %-2d %-8s FAIL no %s.wav in %s" % (i, name, name, SOUNDS))
            continue
        try:
            rate, ch, bits, pcm = wav_decode(hits[0])
        except Exception as e:                              # noqa: BLE001
            ok = False
            print("  %-2d %-8s FAIL %s" % (i, name, e))
            continue
        mine[i] = (name, rate, len(pcm) // ch, hashlib.sha256(pcm).hexdigest())
        print("  %-2d %-8s %-14s %5d Hz  %2d bit  %6d frames  %s"
              % (i, name, hits[0].name, rate, bits, len(pcm) // ch,
                 mine[i][3][:16]))
        if ch != 1 or bits != 8:
            print("     note: %d channel(s), %d-bit -- the shipped set is mono 8-bit"
                  % (ch, bits))

    godot = None if args.no_godot else find_godot()
    if godot is None:
        print("  cross-check SKIPPED: no Godot found (set LT_GODOT) -- the C# "
              "decoder was NOT compared")
        return ok and not args.require_godot

    theirs = godot_sounds(godot)
    for i in range(1, 17):
        a, b = mine.get(i), theirs.get(i)
        if b is None:
            ok = False
            print("  %-2d %-8s FAIL C# decoded nothing" % (i, NAMES[i]))
        elif a is None:
            continue
        elif b[0] != a[0]:
            ok = False
            print("  %-2d FAIL id names disagree: python %s, c# %s" % (i, a[0], b[0]))
        elif b[3] != a[3] or b[1] != a[1] or b[2] != a[2]:
            ok = False
            print("  %-2d %-8s FAIL python %d Hz %d frames %s != c# %d Hz %d frames %s"
                  % (i, a[0], a[1], a[2], a[3][:16], b[1], b[2], b[3][:16]))
    if ok:
        print("  cross-check: 16 sounds, python and C# decode to the same PCM, "
              "and to the same ids")
    return ok


# --- check 3: [OPT] Sound -----------------------------------------------------


# `options size=1 cell=24 laser_offset=10` -- several pairs per line, and a
# value may contain spaces (a path, a pack label), so a pair ends where the
# next `word=` begins.
OPT_RE = re.compile(r"(\w+)=(.*?)(?=\s+\w+=|$)")


def run_options(godot, ini, extra=()):
    """--check-options against `ini` -> {key: value}."""
    cmd = [godot, "--headless", "--path", str(GAME), "--",
           "--ini", str(ini), "--check-options"] + list(extra)
    p = subprocess.run(cmd, capture_output=True, text=True, cwd=str(ROOT),
                       timeout=300)
    out = {}
    for line in p.stdout.splitlines():
        line = line.strip()
        if not line.startswith("options "):
            continue
        for k, v in OPT_RE.findall(line[len("options "):]):
            out[k] = v.strip()
    if not out:
        print("    " + (p.stdout[-800:] or p.stderr[-800:]).replace("\n", "\n    "))
    return out


def check_ini(args):
    godot = None if args.no_godot else find_godot()
    if godot is None:
        print("  SKIPPED: no Godot found (set LT_GODOT)")
        return not args.require_godot
    engines.build_godot_game()

    ok = True
    tmp = pathlib.Path(tempfile.mkdtemp(prefix="lt-sound-ini-"))

    def want(label, got, expect):
        nonlocal ok
        good = got == expect
        ok = ok and good
        print("  %-46s %-4s %s" % (label, got, "" if good else "FAIL want " + expect))

    # 1. no file at all: LTANK.C:411 defaults Sound to Yes.
    want("no ini at all -> the default", run_options(godot, tmp / "none.ini")
         .get("sound"), "Yes")

    # 2. the original's test is `strcmp(temps, "Yes")`, so anything else is off
    #    -- including a lower-case "yes", which really does mute the 2010
    #    binary.  Kept deliberately; see Options.cs.
    for value, expect in (("Yes", "Yes"), ("No", "No"), ("yes", "No"),
                          ("", "No"), ("Maybe", "No")):
        f = tmp / ("v-%s.ini" % (value or "empty"))
        f.write_bytes(("[OPT]\r\nSound=%s\r\n" % value).encode("latin-1"))
        want("[OPT] Sound=%-7r" % value, run_options(godot, f).get("sound"), expect)

    # 3. the round trip: --sound no --save-options writes it, and a plain
    #    launch afterwards reports what was written.
    f = tmp / "trip.ini"
    f.write_bytes(b"[SCREEN]\r\nSize=3\r\n[OPT]\r\nPlayer=Michal\r\n")
    run_options(godot, f, ["--sound", "no", "--save-options"])
    got = run_options(godot, f)
    want("round trip: --sound no, then a plain launch", got.get("sound"), "No")
    want("  ...and Size survived it", got.get("size"), "3")
    text = f.read_bytes().decode("latin-1")
    keep = "Player=Michal" in text
    ok = ok and keep
    print("  %-46s %-4s %s" % ("  ...and the foreign key survived it",
                               "Yes" if keep else "no",
                               "" if keep else "FAIL [OPT] Player was dropped"))
    written = [l for l in text.splitlines() if l.lower().startswith("sound=")]
    good = written == ["Sound=No"]
    ok = ok and good
    print("  %-46s %-4s %s" % ("  ...written as the original spells it",
                               written[0] if written else "-",
                               "" if good else "FAIL want exactly Sound=No"))

    # 4. and back on again.
    run_options(godot, f, ["--sound", "yes", "--save-options"])
    want("round trip: --sound yes", run_options(godot, f).get("sound"), "Yes")
    return ok


# --- main ---------------------------------------------------------------------


def main():
    ap = argparse.ArgumentParser(
        description="Phase 5 step 3's gate: sound ids, the WAVs, and [OPT] Sound.",
        epilog="exit 0 clean, 1 a divergence or a bad file, 2 environment")
    ap.add_argument("--corpus-only", action="store_true")
    ap.add_argument("--wavs-only", action="store_true")
    ap.add_argument("--ini-only", action="store_true")
    ap.add_argument("--collection", help="only this pack / demo directory")
    ap.add_argument("--stride", type=int, default=1, metavar="N",
                    help="only every Nth recording -- a spot check")
    ap.add_argument("--field", action="store_true",
                    help="also diff the full PF/PF2 (slower; the other gates "
                         "already do)")
    ap.add_argument("--jobs", type=int, default=8)
    ap.add_argument("--no-godot", action="store_true")
    ap.add_argument("--require-godot", action="store_true",
                    help="fail rather than SKIP when Godot is missing")
    args = ap.parse_args()

    only = args.corpus_only or args.wavs_only or args.ini_only
    results = []
    if args.corpus_only or not only:
        results.append(("corpus", check_corpus(args)))
        print()
    if args.wavs_only or not only:
        print("wavs: original/src/Sounds, python and C#")
        results.append(("wavs", check_wavs(args)))
        print()
    if args.ini_only or not only:
        print("ini: [OPT] Sound")
        results.append(("ini", check_ini(args)))
        print()

    bad = [n for n, good in results if not good]
    print("sound_check: " + ("OK  (%s)" % ", ".join(n for n, _ in results)
                             if not bad else "FAILED  (%s)" % ", ".join(bad)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
