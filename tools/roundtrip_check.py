#!/usr/bin/env python
"""Phase 5 step 4's exit criterion, as a gate.

    "a recorded game round-trips -- record in Godot, replay in Godot, replay in
     the oracle, all three agree."

    python tools/roundtrip_check.py                  # 60 cases, ~90 s
    python tools/roundtrip_check.py --runs 300
    python tools/roundtrip_check.py --collection quirks/tricks/Tricks.lvl

The recordings are made **with undo in them**, which is the whole point.  A
keystream pressed straight through was already covered by step 1's gate; what
step 4 adds is that undoing rewinds `Game.RecP`, so the keys after the undo
overwrite the ones before it and what F6 finally writes is not the sequence of
keys the player pressed.  Whether that shorter, rewritten keystream still
reproduces the board is the claim -- and it is a claim about a *file*, so it can
be handed to a 25-year-old C program that knows nothing about any of this.

Six runs per case, and each comparison earns its place:

  A  oracle  --script S            the reference: undo in the real C
  B  core    --script S            trace-diffed against A, byte for byte
  C  godot   --play --script S     the game's own command path (Session.Undo,
                                   Session.SavePos, ...) -- must agree with A on
                                   result, ticks, moves and shots, and writes
                                   the .lpb
  D  oracle  --lpb C.lpb           the recording, replayed by the C
  E  core    --lpb C.lpb           trace-diffed against D
  F  godot   --replay C.lpb        the recording, replayed through the *playback*
                                   path -- PBOpen, PBHold and Speed, which
                                   pressing the keys never exercises

Three drivers implement the script rule -- oracle/driver.c's `script_feed`,
LaserTank.Cli's `Feed`, PlayMode's `Feed` -- which is on purpose: the first two
prove the engine's UndoStep, and only the third proves that what the U key calls
does the same thing.

**What is deliberately not asserted: that the recording replays to the position
it was saved from.** It was, until the C was asked. Level 1499 of the flagship,
script `ffrlrfzcrfzzzzzzfufuZvfdddffdflzzzflllflrrrfr`, plays to 100 ticks and 2
moves in the *oracle*; the 13 keys that leaves in RecBuffer[0..RecP) replay -- in
the same oracle -- to 60 ticks and 6 moves. Nothing about the port is involved.

The reason is one line of UndoStep: `Game = UndoBuffer[UndoP]`. TGAMEREC holds
the playfields, the tank and the two scores, and that is all it holds. The laser
is a separate global, and so are `wasIce`, `WaitToTrans`, `ConvMoving`,
`BlackHole` and the contents of the slide stacks -- which undo *clears* rather
than restores. So the world after an undo is not the world a clean replay of the
rewound keystream would produce, the keys pressed after it land in a different
game, and `Game.RecP` keeps counting. A recording made with undos in it is a
valid keystream that wins or loses on its own terms; it is not a transcript of
what the player saw. LaserTank 4.1.2 has always been like this.

So the tie-in is asserted **only for undo-free scripts**, where it is step 1's
claim re-checked through a different driver, and *counted* for the rest -- a
run where the number of undo-carrying cases that still reproduce drops to zero
is as suspicious as one where it reaches 100%.

Needs both engines and Godot.  Nothing here touches build/, so it is safe beside
a live solve -- point $LT_CORE at a locally built core.

Exit: 0 clean, 1 a divergence, 2 environment.
"""
import argparse
import pathlib
import random
import re
import subprocess
import sys
import tempfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
import undo_check                                       # noqa: E402
from engines import ROOT, Case, ScriptCase              # noqa: E402
from atlas_check import find_godot                      # noqa: E402

GAME = ROOT / "src" / "LaserTank.Game"
FLAGSHIP = ROOT / "data" / "levels" / "LaserTank.lvl"

SUM_RE = re.compile(
    r"^(?:script|replay)\s+(?P<result>\w+)\s+level=(?P<level>\d+)\s+"
    r"ticks=(?P<ticks>\d+)\s+moves=(?P<moves>\d+)\s+shots=(?P<shots>\d+)\s+"
    r"keys=(?P<recp>\d+)/(?P<offered>\d+)")


def godot(exe, args, timeout=300):
    """-> (summary dict or None, combined output)."""
    p = subprocess.run([exe, "--headless", "--path", str(GAME), "--"] + args,
                       capture_output=True, timeout=timeout, cwd=str(ROOT))
    out = (p.stdout + p.stderr).decode("utf-8", "replace")
    for line in out.splitlines():
        m = SUM_RE.match(line)
        if m:
            d = m.groupdict()
            for k in ("level", "ticks", "moves", "shots", "recp", "offered"):
                d[k] = int(d[k])
            return d, out
    return None, out


def same(a, b, *fields):
    """-> the first field that differs, or None."""
    for f in fields:
        if a[f] != b[f]:
            return "%s %s vs %s" % (f, a[f], b[f])
    return None


# The commands a script can carry.  A script with none of them is a keystream
# by another name, and then the recording *must* reproduce the play.
CMDS = "zZcv"

# Both engines default to 200,000 ticks and the Godot side to 100,000; a runaway
# script -- a tank that turns in place forever -- hits whichever is smaller and
# the two then "disagree" about a case neither finished.  One number for all
# three, small enough to keep the campaign quick and far above any real play.
MAX_TICKS = 20000


def one_case(exe, lvl, level, script, tmp, i):
    """-> (failures, reproduced) where `reproduced` is None for an undo-free
    script (the claim is then asserted, not counted)."""
    bad = []
    sc = ScriptCase(lvl, level, script)
    ta, tb = tmp / ("a%d.tr" % i), tmp / ("b%d.tr" % i)

    # A and B: the script through both headless engines, trace-diffed.
    a, b = engines.run_pair(sc, ta, tb, field=True, max_ticks=MAX_TICKS)
    div = engines.compare(a, b)
    if div is not None:
        return ["script trace: " + div.detail.splitlines()[0]], None
    ref = engines.outcome(ta)
    if ref is None:
        return ["oracle wrote no usable trace for the script"], None

    # C: the script through the game.
    stem = "rt%05d" % i
    got, out = godot(exe, ["--play", "--script", script, "--level", str(level),
                           "--levels", str(lvl), "--out", str(tmp),
                           "--stem", stem, "--max-ticks", str(MAX_TICKS)])
    if got is None:
        return ["godot --play --script printed no summary\n      "
                + "\n      ".join(out.splitlines()[-4:])], None
    for f, want in (("result", ref["result"]), ("ticks", ref["ticks"]),
                    ("moves", ref["moves"]), ("shots", ref["shots"])):
        if got[f] != want:
            bad.append("godot script %s %s, oracle %s" % (f, got[f], want))
    lpb = tmp / (stem + ".lpb")
    if not lpb.exists():
        return bad + ["godot saved no .lpb"], None

    # D and E: the recording through both headless engines, trace-diffed.
    lc = engines.LpbCase(lvl, lpb)
    td, te = tmp / ("d%d.tr" % i), tmp / ("e%d.tr" % i)
    d, e = engines.run_pair(lc, td, te, field=True, max_ticks=MAX_TICKS)
    div = engines.compare(d, e)
    if div is not None:
        bad.append("replay trace: " + div.detail.splitlines()[0])
    rep = engines.outcome(td)
    if rep is None:
        return bad + ["oracle wrote no usable trace for the recording"], None

    # F: the recording through the game's playback path.
    got2, out2 = godot(exe, ["--replay", str(lpb), "--levels", str(lvl),
                             "--max-ticks", str(MAX_TICKS)])
    if got2 is None:
        return bad + ["godot --replay printed no summary\n      "
                      + "\n      ".join(out2.splitlines()[-4:])], None
    for f, want in (("result", rep["result"]), ("ticks", rep["ticks"]),
                    ("moves", rep["moves"]), ("shots", rep["shots"])):
        if got2[f] != want:
            bad.append("godot replay %s %s, oracle %s" % (f, got2[f], want))

    # Does the recording reproduce the play?  Asserted without commands in the
    # script, counted with them -- see this file's header.
    same_play = all(rep[f] == ref[f] for f in ("result", "moves", "shots"))
    if not any(c in script for c in CMDS):
        if not same_play:
            bad.append("undo-free script, yet recording %s/%s/%s != play %s/%s/%s"
                       % (rep["result"], rep["moves"], rep["shots"],
                          ref["result"], ref["moves"], ref["shots"]))
        return bad, None
    return bad, same_play


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--runs", type=int, default=60)
    ap.add_argument("--tokens", type=int, default=45)
    ap.add_argument("--collection", default=None)
    ap.add_argument("--level", type=int, default=0)
    ap.add_argument("--seed", type=int, default=17)
    ap.add_argument("--script", default=None,
                    help="one script instead of a campaign (needs --level)")
    args = ap.parse_args(argv)

    lvl = FLAGSHIP
    if args.collection:
        p = pathlib.Path(args.collection)
        lvl = p if p.is_absolute() else ROOT / "data" / args.collection
    if not lvl.exists():
        print("roundtrip_check: no such collection: %s" % lvl)
        return 2
    try:
        engines.require_engines()
    except SystemExit as ex:
        print(ex)
        return 2
    exe = find_godot()
    if exe is None:
        print("roundtrip_check: SKIP -- Godot not found (set LT_GODOT)")
        return 2
    # `godot --path` does not build C#.
    _ok, warn = engines.build_godot_game()
    print("roundtrip_check: build ok%s" % (" (%d warnings)" % warn if warn else ""))

    rng = random.Random(args.seed)
    n = engines.count_levels(lvl)
    if args.script:
        cases = [(args.level or 1, args.script)]
    else:
        # One case in four is command-free, so the tie-in claim -- the recording
        # reproduces the play -- is asserted on a real share of the campaign and
        # not merely counted.  undo_check.script's `p_cmd = 0` is a keystream.
        cases = []
        for k in range(args.runs):
            r = random.Random(rng.randrange(1 << 30))
            cases.append((args.level or rng.randint(1, n),
                          undo_check.script(r, args.tokens,
                                            p_cmd=0.0 if k % 4 == 0 else 0.18)))
    print("roundtrip_check: %d case(s) over %s, six runs each"
          % (len(cases), lvl.name))

    failed, plain, withcmd, reproduced = 0, 0, 0, 0
    with tempfile.TemporaryDirectory(prefix="lt-rt-") as d:
        tmp = pathlib.Path(d)
        for i, (level, script) in enumerate(cases, 1):
            bad, same = one_case(exe, lvl, level, script, tmp, i)
            if same is None:
                plain += 1
            else:
                withcmd += 1
                reproduced += int(same)
            if bad:
                failed += 1
                print("  FAIL level=%d script=%s" % (level, script))
                for line in bad:
                    print("    " + line)
            if i % 10 == 0 or i == len(cases):
                print("  %3d/%d  %d failed" % (i, len(cases), failed))

    print("  %d command-free script(s): the recording reproduced the play, as"
          " step 1 requires" % plain)
    if withcmd:
        print("  %d with undo/position commands: %d of them reproduced the play"
              " -- see this tool's header on why that is a measurement and not"
              " a requirement" % (withcmd, reproduced))
    if failed:
        print("roundtrip_check FAILED -- %d of %d cases" % (failed, len(cases)))
        return 1
    print("roundtrip_check OK -- %d cases round-tripped" % len(cases))
    return 0


if __name__ == "__main__":
    sys.exit(main())
