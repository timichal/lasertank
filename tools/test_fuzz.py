#!/usr/bin/env python
"""Self-test for tools/fuzz.py: inject known faults into the C# core, red then green.

    python tools/test_fuzz.py            # everything, ~2 min (rebuilds the core 3x)
    python tools/test_fuzz.py --no-build # only the parts that need no engine

A fuzzer that has never gone red is untested -- it can be broken in ways that
look exactly like success, and "2,000 cases, no divergence" is then a claim
about the fuzzer rather than about the port.  So this does what
`tools/test_difftrace.py` does for the differ: it breaks the thing under test on
purpose and fails unless the breakage is caught.

Three layers, cheapest first:

  1. `engines.compare` classification, on hand-written traces.  No engine.
  2. `fuzz.reduce_keys` on synthetic predicates, where the minimal answer is
     known in advance.  No engine.
  3. **Fault injection.**  Patch `src/LaserTank.Core/Engine.cs`, rebuild, run the
     fuzzer, and require that it finds the fault, shrinks it, and that the
     shrunk keystream still reproduces it.  Then restore and require green.

The two faults are the ones a transliteration would plausibly get wrong, and
each tests a different part of the trace:

  * **antitank-scan-order** -- AntiTank's four scans reordered (vertical pair
    before horizontal).  Two effects: which of two aligned anti-tanks fires
    first, and -- because every scan is a `while (CheckLoc(...))` loop -- what
    `wasIce` is left holding when none of them match (quirk #3).  A no-op-looking
    reorder that changes state through a hidden second return value.
  * **movetank-blocked-slidet** -- MoveTank's `SlideT.dx/dy` writes moved inside
    the `if`, so a blocked move stops recording the direction it was blocked in.
    This is the transcription error, not an invented one: the write sits after
    the if/else in the original precisely so that *both* arms take it.

Engine.cs is restored in a `finally`, and a copy is saved to build/ first, so an
interrupted run is recoverable.  If this script is ever killed between the patch
and the restore, `git checkout src/LaserTank.Core/Engine.cs` puts it back.
"""
import argparse
import json
import pathlib
import shutil
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
import fuzz                                             # noqa: E402
from engines import Case, ROOT                          # noqa: E402

ENGINE_CS = ROOT / "src" / "LaserTank.Core" / "Engine.cs"
BACKUP = ROOT / "build" / "Engine.cs.test_fuzz_backup"
LEVELS = ROOT / "data" / "levels" / "LaserTank.lvl"

passed = failed = 0


def check(name, cond, detail=""):
    global passed, failed
    if cond:
        passed += 1
        print("ok    %s%s" % (name, ("  " + detail) if detail else ""))
    else:
        failed += 1
        print("FAIL  %s%s" % (name, ("  " + detail) if detail else ""))
    return bool(cond)


# --- layer 1: engines.compare ----------------------------------------------

HEAD = "# lasertank oracle trace\n# levels=x.lvl level=1 name=L author=A keys=2\n"
TICK = ("t=%d T=1,1,1,0,0 L=0,0,0,0,0 S=0,0 P=0 C=0 SlT=0,0,0,0,0 "
        "SlO=0,0,0,0,0 N=0 A=0,%d D=0 G=1 H=aaaaaaaa,bbbbbbbb")
FOOT = "# result=%s ticks=%d moves=0 shots=0 keys_used=0/0 dialogs=0\n"


def trace_text(n, foot="UNFINISHED", mutate=None):
    lines = [TICK % (i, i) for i in range(n)]
    if mutate:
        lines[mutate[0]] = mutate[1]
    return HEAD + "\n".join(lines) + "\n" + FOOT % (foot, n - 1)


def fake(tmp, name, text, rc=1):
    p = tmp / name
    if text is None:
        return engines.Run(rc, "", "boom", p)
    p.write_text(text, encoding="latin-1")
    return engines.Run(rc, "", "", p)


def test_compare(tmp):
    a = fake(tmp, "a", trace_text(5))

    check("compare: identical -> None",
          engines.compare(a, fake(tmp, "b", trace_text(5))) is None)

    moved = TICK % (3, 3)
    d = engines.compare(a, fake(tmp, "b", trace_text(
        5, mutate=(3, moved.replace("T=1,1,1,0,0", "T=1,1,4,0,0")))))
    check("compare: tick divergence -> field signature",
          d and d.kind == "tick" and d.sig == "T.dir" and d.tick == "3" and not d.cosmetic,
          "%s at tick %s" % (d.sig if d else "-", d.tick if d else "-"))

    d = engines.compare(a, fake(tmp, "b", trace_text(3)))
    check("compare: length mismatch -> 'length'",
          d and d.kind == "length" and d.sig == "length")

    d = engines.compare(a, fake(tmp, "b", trace_text(3, foot="NOTPORTED")))
    check("compare: short + NOTPORTED footer -> 'NOTPORTED'",
          d and d.sig == "NOTPORTED",
          "MouseOperation would land here")

    d = engines.compare(a, fake(tmp, "b", trace_text(5, foot="WIN")))
    check("compare: same ticks, different footer -> 'result'",
          d and d.kind == "result" and d.sig == "result")

    d = engines.compare(a, fake(tmp, "never-written", None))
    check("compare: no trace at all -> 'no-trace'", d and d.sig == "no-trace")

    d = engines.compare(a, fake(tmp, "b", trace_text(5), rc=0))
    check("compare: identical traces, different exit code -> 'exit-code'",
          d and d.sig == "exit-code")

    # A cosmetic-only divergence is still reported -- it is just labelled, the
    # way difftrace.py exits 3 rather than 1 for one.
    d = engines.compare(a, fake(tmp, "b", trace_text(
        5, mutate=(3, (TICK % (3, 3)).replace("A=0,3", "A=0,9")))))
    check("compare: A/BMF-only divergence is flagged cosmetic",
          d and d.cosmetic and d.sig == "A.AniCount")


# --- layer 2: reduce_keys ---------------------------------------------------


def counting(pred):
    calls = [0]
    cache = {}

    def ok(keys):
        if keys not in cache:
            calls[0] += 1
            cache[keys] = pred(keys)
        return cache[keys]
    return ok, calls


def test_reduce():
    # A predicate whose minimal witness is known: needs an 'f' and a later 'u'.
    def needs_fu(k):
        i = k.find("f")
        return i >= 0 and "u" in k[i + 1:]

    ok, calls = counting(needs_fu)
    keys, minimal = fuzz.reduce_keys("dlrfdlrdlruddlr" * 8, ok)
    check("reduce: 120 keys -> the 2 that matter",
          keys == "fu" and minimal, "%r in %d calls" % (keys, calls[0]))

    # Two required keys at opposite ends: the prefix pass cannot do it alone,
    # ddmin has to remove from the middle.
    ok, calls = counting(lambda k: k.startswith("r") and k.endswith("l")
                         and needs_fu(k))
    keys, minimal = fuzz.reduce_keys("r" + "d" * 40 + "f" + "d" * 40 + "u"
                                     + "d" * 40 + "l", ok)
    check("reduce: ddmin removes from the middle, not just the tail",
          keys == "rful" and minimal, "%r in %d calls" % (keys, calls[0]))

    # Nothing is removable: the answer must be the input, and still minimal.
    ok, _ = counting(lambda k: k == "rrfud")
    keys, minimal = fuzz.reduce_keys("rrfud", ok)
    check("reduce: irreducible input comes back unchanged and 1-minimal",
          keys == "rrfud" and minimal)

    # Budget exhaustion must not lie about minimality.
    def broke(k):
        raise fuzz.Budget()
    keys, minimal = fuzz.reduce_keys("rrfud", broke)
    check("reduce: exhausted budget reports minimal=False",
          keys == "rrfud" and not minimal)

    # A predicate satisfied by the empty keystream reduces all the way -- that
    # is a load-time divergence, and the shrinker must not hide it behind a key.
    ok, _ = counting(lambda k: True)
    keys, minimal = fuzz.reduce_keys("rrfud", ok)
    check("reduce: a divergence needing no keys shrinks to none",
          keys == "" and minimal, "%r" % keys)


# --- layer 3: fault injection ----------------------------------------------


ANTITANK_HORIZ = """            x = Game.Tank.X;    // Look to the right
            while (CheckLoc(x, Game.Tank.Y)) x++;
            if ((x < 16) && (Game.PF[x, Game.Tank.Y] == 10) && (Game.Tank.X != x))
            {
                FireLaser(x, Game.Tank.Y, 4, S_Anti2);
                return;
            }
            x = Game.Tank.X;    // Look to the left
            while (CheckLoc(x, Game.Tank.Y)) x--;
            if ((x >= 0) && (Game.PF[x, Game.Tank.Y] == 8) && (Game.Tank.X != x))
            {
                FireLaser(x, Game.Tank.Y, 2, S_Anti2);
                return;
            }
"""
ANTITANK_VERT = """            y = Game.Tank.Y;    // Look Down
            while (CheckLoc(Game.Tank.X, y)) y++;
            if ((y < 16) && (Game.PF[Game.Tank.X, y] == 7) && (Game.Tank.Y != y))
            {
                FireLaser(Game.Tank.X, y, 1, S_Anti2);
                return;
            }
            y = Game.Tank.Y;    // Look Up
            while (CheckLoc(Game.Tank.X, y)) y--;
            if ((y >= 0) && (Game.PF[Game.Tank.X, y] == 9) && (Game.Tank.Y != y))
            {
                FireLaser(Game.Tank.X, y, 3, S_Anti2);
                return;
            }
"""

# All four arms, written out rather than templated: these have to match
# Engine.cs byte for byte, and a template that silently stops matching is worse
# than one that is tedious to read.
MOVETANK_PATCHES = [(
    """                case 1:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y - 1)) UpDateTankPos(0, -1);
                    else SoundPlay(S_Head);      // Ouch we are hitting something hard
                    SlideT.dy = -1; SlideT.dx = 0;
                    break;""",
    """                case 1:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y - 1)) { UpDateTankPos(0, -1); SlideT.dy = -1; SlideT.dx = 0; }
                    else SoundPlay(S_Head);      // Ouch we are hitting something hard
                    break;"""), (
    """                case 2:
                    if (CheckLoc(Game.Tank.X + 1, Game.Tank.Y)) UpDateTankPos(1, 0);
                    else SoundPlay(S_Head);
                    SlideT.dy = 0; SlideT.dx = 1;
                    break;""",
    """                case 2:
                    if (CheckLoc(Game.Tank.X + 1, Game.Tank.Y)) { UpDateTankPos(1, 0); SlideT.dy = 0; SlideT.dx = 1; }
                    else SoundPlay(S_Head);
                    break;"""), (
    """                case 3:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y + 1)) UpDateTankPos(0, 1);
                    else SoundPlay(S_Head);
                    SlideT.dy = 1; SlideT.dx = 0;
                    break;""",
    """                case 3:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y + 1)) { UpDateTankPos(0, 1); SlideT.dy = 1; SlideT.dx = 0; }
                    else SoundPlay(S_Head);
                    break;"""), (
    """                case 4:
                    if (CheckLoc(Game.Tank.X - 1, Game.Tank.Y)) UpDateTankPos(-1, 0);
                    else SoundPlay(S_Head);
                    SlideT.dy = 0; SlideT.dx = -1;
                    break;""",
    """                case 4:
                    if (CheckLoc(Game.Tank.X - 1, Game.Tank.Y)) { UpDateTankPos(-1, 0); SlideT.dy = 0; SlideT.dx = -1; }
                    else SoundPlay(S_Head);
                    break;""")]


FAULTS = [
    {
        "name": "antitank-scan-order",
        "why": "AntiTank's four scans reordered: changes which aligned anti-tank "
               "fires first, and what wasIce holds when none match (quirk #3)",
        "patches": [(ANTITANK_HORIZ + ANTITANK_VERT, ANTITANK_VERT + ANTITANK_HORIZ)],
        "runs": 600,
    },
    {
        "name": "movetank-blocked-slidet",
        "why": "MoveTank's SlideT.dx/dy write moved inside the if, so a blocked "
               "move no longer records the direction it was blocked in",
        "patches": MOVETANK_PATCHES,
        "runs": 600,
    },
]


def build():
    ok, out = engines.build("src/build.sh")
    if not ok:
        print(out[-2000:])
    return ok


def apply_patches(patches):
    """Patch Engine.cs in place, in bytes.

    Python's text mode would rewrite every line ending to the platform's on the
    way out (Engine.cs is LF, this is Windows), which git -- with
    core.autocrlf -- then shows as a clean diff over a changed file: a
    self-test that silently rewrites 1,362 lines of the thing it is testing.
    So: decode, patch with the file's own newline, encode back.
    """
    raw = ENGINE_CS.read_bytes().decode("utf-8")
    nl = "\r\n" if "\r\n" in raw else "\n"
    for old, new in patches:
        old, new = old.replace("\n", nl), new.replace("\n", nl)
        if raw.count(old) != 1:
            raise SystemExit(
                "test_fuzz: injection site not found (%d matches).  Engine.cs has "
                "moved on; update the patch in tools/test_fuzz.py:\n%s"
                % (raw.count(old), old[:200]))
        raw = raw.replace(old, new)
    ENGINE_CS.write_bytes(raw.encode("utf-8"))


def run_fuzz(out, runs, extra=()):
    out = pathlib.Path(out)
    if out.exists():
        shutil.rmtree(out)
    cmd = [sys.executable, str(ROOT / "tools" / "fuzz.py"), "--runs", str(runs),
           "--jobs", "8", "--stop-after", "1", "--out", str(out), "-q"] + list(extra)
    p = subprocess.run(cmd, capture_output=True, text=True, cwd=str(ROOT))
    findings = out / "findings.json"
    got = json.loads(findings.read_text()) if findings.exists() else []
    return p, got


def independently_reproduces(f):
    """Re-run the shrunk repro without going through fuzz.py, and re-check
    1-minimality here rather than trusting the flag the shrinker wrote."""
    case = Case(pathlib.Path(f["levels"]), f["level"], f["keys"])
    with engines.Scratch("lt-verify") as s:
        def diverges(keys):
            a, b = engines.run_pair(Case(case.levels, case.level, keys), s.a, s.b)
            d = engines.compare(a, b)
            return d is not None and d.sig == f["signature"]
        if not diverges(case.keys):
            return False, "does not reproduce"
        for i in range(len(case.keys)):
            if diverges(case.keys[:i] + case.keys[i + 1:]):
                return False, "not minimal: dropping key %d still diverges" % i
    return True, "%d key%s, 1-minimal" % (len(case.keys),
                                          "" if len(case.keys) == 1 else "s")


def test_faults(control_runs):
    # Bytes for the backup and the restore, as in apply_patches: putting the
    # file back has to be exact, or a green self-test leaves the tree dirty.
    original = ENGINE_CS.read_bytes()
    BACKUP.parent.mkdir(parents=True, exist_ok=True)
    BACKUP.write_bytes(original)
    try:
        if not check("control: clean core builds", build()):
            return
        p, got = run_fuzz(ROOT / "build" / "fuzz-selftest-control", control_runs)
        check("control: clean core is green over %d cases" % control_runs,
              p.returncode == 0 and not got,
              "exit %d, %d finding(s)" % (p.returncode, len(got)))

        for fault in FAULTS:
            print("\n--- injecting %s ---\n      %s" % (fault["name"], fault["why"]))
            ENGINE_CS.write_bytes(original)
            apply_patches(fault["patches"])
            if not check("%s: patched core builds" % fault["name"], build()):
                continue
            t0 = time.time()
            p, got = run_fuzz(ROOT / "build" / ("fuzz-selftest-" + fault["name"]),
                              fault["runs"])
            if not check("%s: fuzzer goes red" % fault["name"],
                         p.returncode == 1 and got,
                         "exit %d, %d finding(s) in %.0fs"
                         % (p.returncode, len(got), time.time() - t0)):
                print(p.stdout[-1500:])
                continue
            f = got[0]
            check("%s: shrunk" % fault["name"],
                  len(f["keys"]) < len(f["original_keys"]),
                  "%d -> %d keys in %d runs: level %d, %r  [%s]"
                  % (len(f["original_keys"]), len(f["keys"]), f["shrink_runs"],
                     f["level"], f["keys"], f["signature"]))
            ok, why = independently_reproduces(f)
            check("%s: minimal repro verified independently" % fault["name"], ok, why)
    finally:
        ENGINE_CS.write_bytes(original)
        BACKUP.unlink(missing_ok=True)
        print("\n--- Engine.cs restored ---")
        check("restore: clean core builds", build())
        p, got = run_fuzz(ROOT / "build" / "fuzz-selftest-restored", control_runs)
        check("restore: clean core is green again over %d cases" % control_runs,
              p.returncode == 0 and not got,
              "exit %d, %d finding(s)" % (p.returncode, len(got)))


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--no-build", action="store_true",
                    help="skip fault injection; only the parts needing no engine")
    ap.add_argument("--control-runs", type=int, default=200,
                    help="cases in each green control run (default 200)")
    args = ap.parse_args()

    with tempfile.TemporaryDirectory(prefix="lt-testfuzz-") as td:
        test_compare(pathlib.Path(td))
    print()
    test_reduce()

    if not args.no_build:
        engines.require_engines()
        print()
        test_faults(args.control_runs)

    print("\n%d passed, %d failed" % (passed, failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
