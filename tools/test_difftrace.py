#!/usr/bin/env python
"""Self-test for tools/difftrace.py.  Run it before trusting a difftrace verdict.

    python tools/test_difftrace.py [-v]

Two kinds of case:

  * against the oracle -- two real runs of the same level, one of them with a
    single keypress changed, so the divergence has a known cause at a known
    tick.  This is the check that matters: it proves the differ localises a
    real behavioural difference, not just a textual one.
  * against synthetic mutations of a real trace -- one playfield cell, one
    animation counter, one slide-stack entry, a dropped field, a truncation.
    These pin the exact reported wording, which the oracle cases cannot
    (a changed keypress moves half the fields at once).

Everything is built in a temp directory; nothing under data/ or oracle/ is
touched.  Exit 0 if every case passes.
"""
import argparse
import pathlib
import shutil
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
DIFF = ROOT / "tools" / "difftrace.py"
ORACLE = ROOT / "oracle" / "build" / "oracle.exe"
PACK = ROOT / "data" / "quirks" / "game-objects"
LVL = PACK / "Game-Objects-in-LT.LVL"

# Level 3 of game-objects, and its recorded solution as characters.  The .lpb
# and this string are asserted to produce identical traces (case "keys==lpb"),
# so the fixture cannot drift away from the corpus unnoticed.
LEVEL = 3
LPB = PACK / "Game-Objects-in-LT_0003.lpb"
KEYS = ("dfffddddddlfffllllldfffffffdddddddddrfffrrrruffuuurfffrrrruffuuurfff"
        "rrrruuurrrruuulfuuuuulfdddddddllllllluuuuurfdddddrrrruuuuuuuu")
# Flipping key 60 (a shot) to a left turn first shows up at tick 90.
FLIP_AT, FLIP_TO, FLIP_TICK = 60, "l", 90

# Level 14 slides a block on thin ice, so its trace carries a slide stack.
ICE_LEVEL, ICE_LPB = 14, PACK / "Game-Objects-in-LT_0014.lpb"


class Fail(Exception):
    pass


def run_oracle(out, *args):
    cmd = [str(ORACLE), "--levels", str(LVL), "--trace", str(out), "--quiet"] + list(args)
    p = subprocess.run(cmd, capture_output=True, text=True)
    if not out.exists():
        raise Fail("oracle produced no trace: %s\n%s" % (" ".join(cmd), p.stderr))
    return out


def run_diff(a, b, *flags):
    p = subprocess.run([sys.executable, str(DIFF), str(a), str(b)] + list(flags),
                       capture_output=True, text=True)
    return p.returncode, p.stdout, p.stderr


# --- trace surgery ----------------------------------------------------------


def load(path):
    return path.read_text(encoding="latin-1").splitlines()


def save(lines, path):
    path.write_text("\n".join(lines) + "\n", encoding="latin-1")
    return path


def tick_index(lines, tick):
    """Index of the line for t=<tick>."""
    for i, ln in enumerate(lines):
        if ln.startswith("t=%d " % tick):
            return i
    raise Fail("no tick %d in trace" % tick)


def edit_token(lines, tick, key, newval):
    """Replace one key=value token on one tick line."""
    i = tick_index(lines, tick)
    toks = lines[i].split()
    for j, tok in enumerate(toks):
        if tok.startswith(key + "="):
            toks[j] = "%s=%s" % (key, newval)
            lines[i] = " ".join(toks)
            return lines
    raise Fail("tick %d has no field %s" % (tick, key))


def get_token(lines, tick, key):
    for tok in lines[tick_index(lines, tick)].split():
        k, _, v = tok.partition("=")
        if k == key:
            return v
    raise Fail("tick %d has no field %s" % (tick, key))


def drop_token(lines, tick, key):
    i = tick_index(lines, tick)
    lines[i] = " ".join(t for t in lines[i].split() if not t.startswith(key + "="))
    return lines


def poke_cell(lines, tick, grid, x, y, value):
    """Set one 16x16 cell of a hex grid.  Returns the old byte."""
    hexs = get_token(lines, tick, grid)
    i = (x * 16 + y) * 2
    old = int(hexs[i:i + 2], 16)
    edit_token(lines, tick, grid, hexs[:i] + "%02x" % value + hexs[i + 2:])
    return old


# --- the cases --------------------------------------------------------------


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    if not ORACLE.exists():
        raise SystemExit("oracle not built: %s\nrun: bash oracle/build.sh" % ORACLE)

    tmp = pathlib.Path(tempfile.mkdtemp(prefix="difftrace-test-"))
    passed, failed = 0, []

    def check(name, rc, out, err, want_rc, must=(), must_not=(), must_err=()):
        """Assert one difftrace invocation's exit code and report text."""
        nonlocal passed
        bad = []
        if rc != want_rc:
            bad.append("exit %d, expected %d" % (rc, want_rc))
        for s in must:
            if s not in out:
                bad.append("missing from stdout: %r" % s)
        for s in must_not:
            if s in out:
                bad.append("unexpected in stdout: %r" % s)
        for s in must_err:
            if s not in err:
                bad.append("missing from stderr: %r" % s)
        if bad:
            failed.append((name, bad, out, err))
            print("FAIL  %s" % name)
            for b in bad:
                print("        %s" % b)
        else:
            passed += 1
            print("ok    %s" % name)
            if args.verbose:
                print("".join("        %s\n" % l for l in out.splitlines()))

    try:
        # ---- fixtures: real oracle runs -----------------------------------
        ref = run_oracle(tmp / "ref.trace", "--level", str(LEVEL), "--keys", KEYS,
                         "--field", "--bmf")
        ref2 = run_oracle(tmp / "ref2.trace", "--level", str(LEVEL), "--keys", KEYS,
                          "--field", "--bmf")
        from_lpb = run_oracle(tmp / "lpb.trace", "--lpb", str(LPB), "--field", "--bmf")
        flipped = KEYS[:FLIP_AT] + FLIP_TO + KEYS[FLIP_AT + 1:]
        pert = run_oracle(tmp / "pert.trace", "--level", str(LEVEL), "--keys", flipped,
                          "--field", "--bmf")
        ice = run_oracle(tmp / "ice.trace", "--lpb", str(ICE_LPB))

        # ---- oracle cases --------------------------------------------------
        rc, out, err = run_diff(ref, ref2)
        check("identical: two runs of the same input", rc, out, err, 0,
              must=["IDENTICAL"])

        # Guards the fixture: the character keystream above must be the .lpb.
        # Only the header differs (--keys has no recording), so compare ticks.
        a, b = load(ref)[2:], load(from_lpb)[2:]
        if a != b:
            failed.append(("keys==lpb", ["--keys trace differs from the .lpb trace"], "", ""))
            print("FAIL  keys==lpb: the KEYS fixture no longer matches %s" % LPB.name)
        else:
            passed += 1
            print("ok    keys==lpb: fixture still matches the recorded solution")

        rc, out, err = run_diff(ref, pert)
        check("oracle: one keypress changed", rc, out, err, 1,
              must=["DIVERGE", "first at tick %d" % FLIP_TICK,
                    "=== first divergence: tick %d" % FLIP_TICK,
                    "T.dir 1 -> 4",            # left turn instead of a shot
                    "S.shots 23 -> 22",
                    "matching tick", "=== summary ==="])

        rc, out, err = run_diff(pert, ref)
        check("oracle: divergence is symmetric", rc, out, err, 1,
              must=["first at tick %d" % FLIP_TICK, "T.dir 4 -> 1"])

        # ---- synthetic cases -----------------------------------------------
        # Tick 40 of this replay is mid-flight: the tank is firing and the
        # playfield is quiet, so a poked cell cannot be confused with a move.
        TICK = 40

        lines = load(ref)
        old = poke_cell(lines, TICK, "PF", 1, 1, 0x18)   # 0x18 = 24 = ice
        one_cell = save(lines, tmp / "cell.trace")
        rc, out, err = run_diff(ref, one_cell)
        check("one playfield cell", rc, out, err, 1,
              must=["first at tick %d" % TICK, "PF (1 cell)",
                    "PF[x=1,y=1]", "%02x" % old, "-> 18 ice"],
              must_not=["T.x", "S.moves"])

        lines = load(ref)
        poke_cell(lines, TICK, "PF", 1, 1, 0x18)
        poke_cell(lines, TICK, "PF", 2, 5, 0x05)         # 5 = movable block
        two = save(lines, tmp / "cells.trace")
        rc, out, err = run_diff(ref, two)
        check("two playfield cells, both listed", rc, out, err, 1,
              must=["PF (2 cells)", "PF[x=1,y=1]", "PF[x=2,y=5]",
                    "-> 18 ice", "-> 05 block"])

        # Tunnels are out of band: 0x40 | (id << 1) | waitbit.
        lines = load(ref)
        poke_cell(lines, TICK, "PF", 1, 1, 0x44)
        poke_cell(lines, TICK, "PF", 2, 5, 0x41)
        tun = save(lines, tmp / "tunnel.trace")
        rc, out, err = run_diff(ref, tun)
        check("tunnel cells decode id and wait bit", rc, out, err, 1,
              must=["-> 44 tunnel 2", "-> 41 tunnel 0+wait"])

        lines = edit_token(load(ref), TICK, "A", "9,9")
        cosmetic = save(lines, tmp / "ani.trace")
        rc, out, err = run_diff(ref, cosmetic)
        check("animation only -> cosmetic", rc, out, err, 3,
              must=["COSMETIC", "A.AniLevel", "(cosmetic)"])
        rc, out, err = run_diff(ref, cosmetic, "--strict")
        check("animation only, --strict -> failure", rc, out, err, 1,
              must=["DIVERGE", "A.AniLevel"])

        lines = load(ref)
        poke_cell(lines, TICK, "BMF", 0, 0, 0x2a)
        bmf = save(lines, tmp / "bmf.trace")
        rc, out, err = run_diff(ref, bmf)
        check("one bitmap cell -> cosmetic", rc, out, err, 3,
              must=["COSMETIC", "BMF", "BMF[x=0,y=0]"])

        # A cosmetic field must never mask a logic field on the same tick.
        lines = load(ref)
        poke_cell(lines, TICK, "PF", 1, 1, 0x18)
        edit_token(lines, TICK, "A", "9,9")
        mixed = save(lines, tmp / "mixed.trace")
        rc, out, err = run_diff(ref, mixed)
        check("cosmetic never masks a logic field", rc, out, err, 1,
              must=["DIVERGE", "field PF (1 cell)"])

        # Reporting order: the tank is named before the score it caused.
        lines = edit_token(edit_token(load(ref), TICK, "S", "99,99"),
                           TICK, "T", "9,9,9,9,9")
        order = save(lines, tmp / "order.trace")
        rc, out, err = run_diff(ref, order)
        check("most primary field is named first", rc, out, err, 1,
              must=["first field:  T.x"])
        also = ([l for l in out.splitlines() if l.startswith("also:")] or [""])[0]
        if "S.moves" not in also:
            failed.append(("order/also", ["S.moves not on the 'also:' line"], out, err))
            print("FAIL  order: S.moves missing from the 'also:' line")
        else:
            passed += 1
            print("ok    order: the other differing fields are still listed")

        lines = edit_token(load(ref), TICK, "H", "deadbeef,e6a1d1c5")
        hashonly = save(lines, tmp / "hash.trace")
        rc, out, err = run_diff(ref, hashonly)
        check("hash-only divergence (what a run without --field sees)",
              rc, out, err, 1, must=["H.PF ", "deadbeef"], must_not=["H.PF2"])

        # Slide stack: M1 is 1-based and only present while something slides.
        lines = edit_token(load(ice), 22, "M1", "5,9,0,1,1")
        slide = save(lines, tmp / "slide.trace")
        rc, out, err = run_diff(ice, slide)
        check("slide stack entry", rc, out, err, 1, must=["M1.dy -1 -> 1"],
              must_not=["M1.x", "M1.s"])

        lines = drop_token(load(ice), 22, "M1")
        gone = save(lines, tmp / "nom1.trace")
        rc, out, err = run_diff(ice, gone)
        check("field present in one trace only", rc, out, err, 1,
              must=["M1", "<absent>"])

        head = [l for l in load(ref) if l.startswith("#")][:1]
        body = [l for l in load(ref) if not l.startswith("#")][:-5]
        short = save(head + body, tmp / "short.trace")
        rc, out, err = run_diff(ref, short)
        check("truncated trace", rc, out, err, 1,
              must=["length mismatch", "A has 5 more"])
        rc, out, err = run_diff(short, ref)
        check("truncated trace, other way round", rc, out, err, 1,
              must=["length mismatch", "B has 5 more"])

        # Diffing two different levels is a footgun, not a result.
        other = run_oracle(tmp / "other.trace", "--lpb", str(ICE_LPB))
        rc, out, err = run_diff(ref, other)
        check("different level -> loud warning", rc, out, err, 1,
              must_err=["not of the same input", "level"])

        empty = save(["# lasertank oracle trace"], tmp / "empty.trace")
        rc, out, err = run_diff(ref, empty)
        check("trace with no ticks -> input error", rc, out, err, 2,
              must_err=["no tick lines"])

        rc, out, err = run_diff(ref, tmp / "does-not-exist.trace")
        check("missing file -> input error", rc, out, err, 2,
              must_err=["difftrace:"])

        rc, out, err = run_diff(ref, pert, "-q")
        check("--quiet prints the verdict only", rc, out, err, 1,
              must=["DIVERGE"], must_not=["=== summary ===", "first field:"])

        rc, out, err = run_diff(ref, pert, "--first-only")
        check("--first-only skips the summary", rc, out, err, 1,
              must=["first divergence"], must_not=["=== summary ==="])

        # ---- directory mode ------------------------------------------------
        # Three pairs: one identical, one cosmetic-only, one diverged, plus a
        # file that exists on one side only.
        da, db = tmp / "dirA", tmp / "dirB"
        da.mkdir()
        db.mkdir()
        for d in (da, db):
            shutil.copyfile(ref, d / "same.trace")
        shutil.copyfile(ref, da / "ani.trace")
        shutil.copyfile(cosmetic, db / "ani.trace")
        shutil.copyfile(ref, da / "moved.trace")
        shutil.copyfile(one_cell, db / "moved.trace")
        shutil.copyfile(ref, da / "orphan.trace")

        rc, out, err = run_diff(da, db)
        check("directory mode", rc, out, err, 1,
              must=["same.trace    IDENTICAL", "ani.trace     COSMETIC",
                    "moved.trace   DIVERGE", "orphan.trace  MISSING     only in A",
                    "3 compared: 1 identical, 1 cosmetic-only, 1 diverged, 0 unusable",
                    "1 file in only one directory"])

        rc, out, err = run_diff(da, db, "-q")
        check("directory mode, -q lists failures only", rc, out, err, 1,
              must=["moved.trace", "orphan.trace"], must_not=["same.trace"])

        for d in (da, db):
            (d / "orphan.trace").unlink(missing_ok=True)
            (d / "moved.trace").unlink(missing_ok=True)
        rc, out, err = run_diff(da, db)
        check("directory mode: cosmetic-only corpus exits 3", rc, out, err, 3,
              must=["1 identical, 1 cosmetic-only"])
        rc, out, err = run_diff(da, db, "--strict")
        check("directory mode: --strict fails on cosmetic", rc, out, err, 1)

        (db / "ani.trace").unlink()
        shutil.copyfile(ref, db / "ani.trace")
        rc, out, err = run_diff(da, db)
        check("directory mode: all identical exits 0", rc, out, err, 0,
              must=["2 compared: 2 identical"])

        rc, out, err = run_diff(da, ref)
        check("directory vs file -> input error", rc, out, err, 2,
              must_err=["not one of each"])

    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    print()
    print("%d passed, %d failed" % (passed, len(failed)))
    if failed:
        print()
        for name, bad, out, err in failed:
            print("--- %s ---" % name)
            for b in bad:
                print("  %s" % b)
            if out:
                print("  stdout:\n" + "".join("    %s\n" % l for l in out.splitlines()))
            if err:
                print("  stderr:\n" + "".join("    %s\n" % l for l in err.splitlines()))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
