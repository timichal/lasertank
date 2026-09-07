#!/usr/bin/env python
"""Phase 5 step 1's gate: the Godot tick loop, judged by the C oracle.

Step 1's exit criterion is a human playing a level in Godot, winning it, saving
the keystream, and that .lpb replaying byte-identically through the unmodified
25-year-old C.  A human did that (see PROGRESS.md).  This is the same proof
with the human replaced by a script, so it can be re-run after every change to
the driver instead of once.

For each recorded .lpb in the corpus, the Godot project plays it back through
its *own* input path and its *own* tick -- Session.Key is LTANK.C:570's
WM_KEYDOWN filter, Session.Step is the WM_TIMER handler, and a synthetic player
presses the next key whenever the buffer has drained -- then writes what it
recorded with the same Save() the game's F6 calls.  Four things must hold:

  1. Godot's result, tick count, moves and shots equal the C oracle's for the
     same input.  Equal tick counts are the strong claim: the driver is not
     merely reaching the same ending, it is taking the same number of 50 ms
     steps to get there.
  2. the keystream Godot recorded is the input keystream, byte for byte, up to
     the length it saved.  (WM_SaveRec saves Game.RecP -- the keys *consumed* --
     so a recording with unconsumed keys on the end is legitimately shorter.)
  3. replaying Godot's own .lpb through the oracle gives the oracle's original
     result, ticks, moves and shots.  This is the round trip that matters: the
     2010 binary must be able to play what Godot records.
  4. header sanity -- the level number and level name Godot wrote are the ones
     the level file has, since lasertank-core refuses a mismatch.

And then, separately, the rate: the game runs for a few seconds through
_PhysicsProcess against the wall clock and must have taken exactly 20 ticks a
second.  Every check above calls Step() in a loop, so they prove what a tick
does and nothing about when one happens.

The six documented non-winning recordings need no special case: the expectation
is whatever the oracle says, and DEAD is as good an agreement as WIN.

    python tools/tick_check.py                     # all 208 + the rate, ~20 s
    python tools/tick_check.py --stride 10         # a spot check
    python tools/tick_check.py --collection LaserTank
    python tools/tick_check.py --rate-seconds 0    # skip the 5 s measurement

It rebuilds the Godot project's C# first, because `godot --path` does not -- it would
otherwise happily report 208/208 for the assembly the *previous* session built.

Needs oracle/build/oracle.exe, the .NET SDK, and Godot (LT_GODOT, or the winget path).
Exit: 0 clean, 1 a divergence, 2 environment.
"""
import argparse
import pathlib
import re
import struct
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
from engines import ROOT                                # noqa: E402
from atlas_check import find_godot                      # noqa: E402
from verify_solutions import find_lvl                   # noqa: E402

QUIRKS = ROOT / "data" / "quirks"
DEMOS = ROOT / "data" / "demos"
GAME = ROOT / "src" / "LaserTank.Game"

PLAY_RE = re.compile(
    r"^play\s+(?P<result>\w+)\s+level=(?P<level>\d+)\s+ticks=(?P<ticks>\d+)\s+"
    r"moves=(?P<moves>\d+)\s+shots=(?P<shots>\d+)\s+"
    r"keys=(?P<recp>\d+)/(?P<nkeys>\d+)\s+out=(?P<out>\S+)")


RATE_RE = re.compile(r"^rate ticks=(?P<ticks>\d+) seconds=(?P<secs>[\d.]+) "
                     r"hz=(?P<hz>[\d.]+) want=(?P<want>\d+)")


def build_game():
    """engines.build_godot_game with this gate's one line of reporting.

    Here rather than left to the caller because `godot --path` does not build
    C#: without this the gate can report a green 208/208 for the assembly the
    previous session built.  See engines.build_godot_game.
    """
    _ok, warn = engines.build_godot_game()
    print("build: LaserTank.Game ok%s"
          % ("" if not warn else "   %d warning(s)" % warn))


def check_rate(godot, secs):
    """Does the driver really tick at 20 Hz?

    Everything else here calls Session.Step() in a loop, which proves what a
    tick *does* and says nothing about when one happens.  This runs the game
    the way a player does -- _PhysicsProcess against the wall clock -- and
    counts.  The count is the assertion, not the reported Hz: CreateTimer fires
    on a process frame, so the elapsed seconds carry a few ms of skew either
    way while the tick count is exact.
    """
    p = subprocess.run([godot, "--headless", "--path", str(GAME), "--",
                        "--tick-rate", str(secs)],
                       capture_output=True, text=True, cwd=str(ROOT),
                       timeout=60 + secs)
    m = next((RATE_RE.match(l.strip()) for l in p.stdout.splitlines()
              if RATE_RE.match(l.strip())), None)
    if m is None:
        print(p.stdout[-2000:] or p.stderr[-2000:])
        print("rate: godot reported nothing (exit %d)" % p.returncode)
        return False
    ticks, want = int(m.group("ticks")), int(m.group("want"))
    ok = abs(ticks - want * secs) <= 1
    print("rate: %d ticks in %ss, %s Hz measured -- want %d*%d=%d   %s"
          % (ticks, m.group("secs"), m.group("hz"), want, secs, want * secs,
             "ok" if ok else "WRONG RATE"))
    return ok


def recordings(collection=None):
    """Every recorded .lpb in the corpus -> [(lvl, lpb, stem)].

    replay_all.py's surface plus data/demos: each data/quirks/ pack holds
    exactly one .lvl and its .lpb files, so pairing there is by directory --
    that is the 187 the fidelity gates use.  data/demos/<collection>/ pairs by
    name against data/levels/, the way verify_solutions.py does.  Stems carry
    the pack name because two collections both have a 00001.lpb.
    """
    out = []
    dirs = [p for p in QUIRKS.iterdir() if p.is_dir()]
    dirs += [p for p in DEMOS.iterdir() if p.is_dir()] if DEMOS.exists() else []
    for d in sorted(dirs, key=lambda p: p.name.lower()):
        if collection and d.name.lower() != collection.lower():
            continue
        lpbs = sorted(p for p in d.iterdir() if p.suffix.lower() == ".lpb")
        if not lpbs:
            continue
        own = [p for p in d.iterdir() if p.suffix.lower() == ".lvl"]
        lvl = own[0] if len(own) == 1 else find_lvl(d.name)
        if lvl is None:
            print("  %-22s no .lvl found for this collection" % d.name)
            continue
        out += [(lvl, lpb, "%s__%s" % (d.name, lpb.stem)) for lpb in lpbs]
    return out


def keystream(path):
    b = path.read_bytes()
    return b[66:]


def header(path):
    """A .lpb's 66-byte header -> (name bytes, level, keystream size)."""
    b = path.read_bytes()[:66]
    level, size = struct.unpack_from("<HH", b, 62)
    return b[0:31].split(b"\0")[0], level, size


def level_name(lvl, number):
    """The level's own name, as bytes.

    Bytes, not text: these are latin-1 fields with the odd 0xA0 and 0xAE in
    them, and this comparison is about what lasertank-core checks -- the .lpb
    header naming the same level the .lvl does -- not about text.  Trimmed to
    30 because WritePlayback's fixed-width field reserves one byte of the 31
    for the terminator, exactly as the original's char[31] fields are used.
    """
    with open(lvl, "rb") as f:
        f.seek((number - 1) * 576 + 256)
        return f.read(31).split(b"\0")[0][:30]


def run_godot(godot, rows, out_dir, scratch):
    """Play every row in one Godot process -> [parsed result line]."""
    listing = scratch / "tick_check.lst"
    listing.write_text("".join("%s\t%s\t%s\n" % (lvl, lpb, stem)
                               for lvl, lpb, stem in rows), encoding="utf-8")
    p = subprocess.run([godot, "--headless", "--path", str(GAME), "--",
                        "--play", "--lpb-list", str(listing),
                        "--out", str(out_dir)],
                       capture_output=True, text=True, cwd=str(ROOT), timeout=3600)
    got = []
    for line in p.stdout.splitlines():
        m = PLAY_RE.match(line.strip())
        if m:
            got.append(m.groupdict())
    if len(got) != len(rows):
        print(p.stdout[-3000:] or "(no stdout)")
        print(p.stderr[-3000:], file=sys.stderr)
        raise SystemExit("godot played %d of %d recordings (exit %d)"
                         % (len(got), len(rows), p.returncode))
    return got


def oracle(lvl, lpb, scratch):
    """The oracle's own verdict on a .lpb -> dict, or None if it would not run."""
    run = engines.run_one(engines.ORACLE, engines.LpbCase(str(lvl), str(lpb)),
                          scratch / "oracle.trace")
    if run.rc not in (engines.RC_WIN, engines.RC_NOTWIN):
        return None
    out = engines.outcome(run.trace)
    if out is None:
        # No --trace footer means the run produced nothing to compare; fall
        # back to the stdout summary, which has the same fields.
        m = re.match(r"^(\w+)\s+level=(\d+)\s+ticks=(\d+)\s+moves=(\d+)\s+shots=(\d+)",
                     run.stdout.strip())
        if not m:
            return None
        return dict(result=m.group(1), ticks=int(m.group(3)),
                    moves=int(m.group(4)), shots=int(m.group(5)))
    return dict(result=out["result"], ticks=int(out["ticks"]),
                moves=int(out["moves"]), shots=int(out["shots"]))


def check(rows, godot, out_dir, scratch):
    got = run_godot(godot, rows, out_dir, scratch)
    bad, ok = [], 0

    for (lvl, lpb, stem), g in zip(rows, got):
        saved = out_dir / (stem + ".lpb")
        why = None

        ref = oracle(lvl, lpb, scratch)
        if ref is None:
            why = "the oracle would not replay the input"
        elif (g["result"], int(g["ticks"]), int(g["moves"]), int(g["shots"])) != \
             (ref["result"], ref["ticks"], ref["moves"], ref["shots"]):
            why = ("godot %s ticks=%s moves=%s shots=%s vs oracle %s ticks=%d "
                   "moves=%d shots=%d" % (g["result"], g["ticks"], g["moves"],
                                          g["shots"], ref["result"], ref["ticks"],
                                          ref["moves"], ref["shots"]))
        elif not saved.exists():
            why = "no recording written"
        else:
            want, mine = keystream(lpb), keystream(saved)
            name, level, size = header(saved)
            if mine != want[:len(mine)]:
                at = next(i for i in range(len(mine)) if i >= len(want) or mine[i] != want[i])
                why = "recorded keystream differs at byte %d" % at
            elif len(mine) != int(g["recp"]):
                why = ("saved %d keys, consumed %s -- WM_SaveRec writes Game.RecP"
                       % (len(mine), g["recp"]))
            elif (size, level, name) != (len(mine), int(g["level"]),
                                         level_name(lvl, int(g["level"]))):
                why = ("header says level %d %r size %d, played level %s with %d "
                       "keys of %r" % (level, name, size, g["level"], len(mine),
                                       level_name(lvl, int(g["level"]))))
            else:
                back = oracle(lvl, saved, scratch)
                if back is None:
                    why = "the oracle would not replay what godot recorded"
                elif back != ref:
                    why = ("round trip: oracle replays godot's recording as %s "
                           "ticks=%d moves=%d shots=%d" %
                           (back["result"], back["ticks"], back["moves"], back["shots"]))

        if why:
            bad.append((stem, why))
            print("      FAIL %-34s %s" % (stem, why))
        else:
            ok += 1
    return ok, bad


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--collection", help="just this one, e.g. LaserTank")
    ap.add_argument("--stride", type=int, default=1, help="every Nth recording")
    ap.add_argument("--godot", help="the Godot binary (else LT_GODOT / winget)")
    ap.add_argument("--rate-seconds", type=int, default=5,
                    help="how long the tick-rate measurement runs (0 skips it)")
    ap.add_argument("--no-build", action="store_true",
                    help="trust the assembly already in .godot/ (see build_game)")
    args = ap.parse_args()

    if not engines.ORACLE.exists():
        raise SystemExit("oracle not built: %s\nrun: bash oracle/build.sh" % engines.ORACLE)
    godot = args.godot or find_godot()
    if godot is None:
        raise SystemExit("no Godot found -- set LT_GODOT to the binary")

    if not args.no_build:
        build_game()

    rows = recordings(args.collection)[::args.stride]
    if not rows:
        raise SystemExit("no recordings under %s" % DEMOS)

    print("tick: %d recordings, godot 20 Hz driver vs the C oracle" % len(rows))
    t0 = time.time()
    with tempfile.TemporaryDirectory(prefix="lt-tick-") as tmp:
        scratch = pathlib.Path(tmp)
        out_dir = scratch / "recordings"
        out_dir.mkdir()
        ok, bad = check(rows, godot, out_dir, scratch)

    print("tick: %d/%d agree with the oracle   %d bad   (%.0fs)"
          % (ok, len(rows), len(bad), time.time() - t0))

    rate_ok = args.rate_seconds <= 0 or check_rate(godot, args.rate_seconds)
    return 0 if not bad and rate_ok else 1


if __name__ == "__main__":
    sys.exit(main())
