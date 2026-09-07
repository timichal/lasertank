#!/usr/bin/env python
"""Phase 5 step 4's gate: Undo and Save/Restore Position, judged by the C oracle.

    python tools/undo_check.py                    # 600 scripts, ~60 s
    python tools/undo_check.py --runs 5000        # a campaign
    python tools/undo_check.py --collection quirks/rotary-mirrors.LVL
    python tools/undo_check.py --replay 93 "uufz.zllZ"

Step 4 adds the three commands no keystream can reach.  `UndoStep` is a *reader*
of the undo buffer, and the buffer has been written and maintained since Phase 2
with nothing ever reading it back -- so its arithmetic (the growth, the
roll-over, quirk #7's `UndoP--` in MoveObj's tunnel path) has never been checked
against anything but itself.  Save and Restore Position are the same shape with
one slot.

The trap step 3 walked into is the reason this file exists: an exit criterion
that only says "the other gates stayed green" tests that undo *changed* nothing,
never that undo is *right*.  So the commands were made observable in the same
way sound was -- `--script`, a token stream consumed one token per tick, where a
token may be a command and not just a key -- and both engines take it:

    oracle/build/oracle.exe --levels L.lvl --level 7 --script "uufz.zc.v" --trace a --field
    build/lasertank-core.exe --levels L.lvl --level 7 --script "uufz.zc.v" --trace b --field
    python tools/difftrace.py a b

The oracle's UndoStep is the real one: `oracle/build.sh` compiles LTANK2.C
verbatim, so the C being diffed against here was written in 2002 and has never
been read by this project.  Nothing in the oracle driver had to learn what undo
means -- only *when* the player asks for it.

Script shape.  A pure-key script is a fuzz keystream by another name and Phase 3
already covers that ground, so the interesting tokens are seeded deliberately:

  * `z` after some play, which is the ordinary undo;
  * `z` **with the laser in flight** (`f` then a few `.` then `z`), which is the
    one case where the original's asymmetry shows -- UndoStep restores
    `Game.Tank.Firing` from the snapshot while the laser global keeps its
    position, and clears the sliding stacks rather than restoring them;
  * runs of `zzz`, which walk the buffer down towards its self-marking bottom
    and then keep asking, so the empty case is hit on purpose;
  * `Z`, the DeadBox's "Undo Last Move" -- the only path that resumes a dead
    game, and the only one that can produce a trace after a death;
  * `c` / `v` pairs, and `v` with no `c` before it.

Divergences are shrunk the way fuzz.py shrinks a keystream: shortest diverging
prefix, then delta debugging over tokens, holding the signature fixed.  A script
is not a keystream, though -- dropping a token can change *when* every later
command lands -- so the shrinker keeps whatever still diverges with the same
signature and makes no claim of 1-minimality.

Exit: 0 no divergence, 1 diverged, 2 environment, 3 cosmetic-only.
"""
import argparse
import pathlib
import random
import sys
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engines                                          # noqa: E402
from engines import ROOT, ScriptCase                    # noqa: E402

FLAGSHIP = ROOT / "data" / "levels" / "LaserTank.lvl"
DIRS = "udlr"


# --- scripts ----------------------------------------------------------------


def script(rng, n, p_fire=0.30, p_repeat=0.45, p_cmd=0.18):
    """One script of about `n` tokens.

    The key half is fuzz.py's shape and for its reasons -- weighted to fire and
    to turn, the two input classes that reach the most engine code per key.  The
    command half is what is new here, and it is *not* uniform: an undo is only
    interesting after something has happened, so commands are drawn only once
    the script has some play in it, and the shapes listed in this file's header
    are emitted as small phrases rather than single tokens so that "undo with
    the laser still flying" actually occurs instead of being hoped for.
    """
    out = []
    last = rng.choice(DIRS)
    while len(out) < n:
        if len(out) >= 3 and rng.random() < p_cmd:
            out += rng.choice((
                ["z"],
                ["z"],
                ["z", "z"],
                ["z", "z", "z", "z"],
                ["f"] + ["."] * rng.randint(1, 4) + ["z"],   # undo mid-flight
                ["."] * rng.randint(1, 3) + ["z"],
                ["Z"],
                ["c"],
                ["v"],
                ["c"] + [rng.choice(DIRS)] * rng.randint(1, 3) + ["v"],
            ))
            continue
        if rng.random() < p_fire:
            out.append("f")
        else:
            last = last if rng.random() < p_repeat else rng.choice(DIRS)
            out.append(last)
    return "".join(out[:n])


# --- one case ---------------------------------------------------------------


def check(case, scratch, field=False, max_ticks=20000):
    a, b = engines.run_pair(case, scratch.a, scratch.b, field=field,
                            max_ticks=max_ticks)
    return engines.compare(a, b), a, b


def shrink(case, scratch, sig, budget=400):
    """A shorter script with the same signature.  Best effort, by design.

    Dropping a token from a script does not merely remove input: every command
    after it lands on a different tick, so the reduction that survives is not a
    subsequence of the original run in the way a shrunk keystream is.  What is
    held fixed is the *signature* -- the first trace field that moved -- which is
    what makes the result a smaller repro of the same bug rather than a different
    bug found on the way down.
    """
    best, runs = case.script, 0

    def diverges(s):
        nonlocal runs
        if not s or runs >= budget:
            return False
        runs += 1
        div, _, _ = check(case._replace(script=s), scratch)
        return div is not None and div.sig == sig

    # 1. shortest diverging prefix
    lo, hi = 1, len(best)
    while lo < hi:
        mid = (lo + hi) // 2
        if diverges(best[:mid]):
            hi = mid
        else:
            lo = mid + 1
    if lo <= len(best) and diverges(best[:lo]):
        best = best[:lo]

    # 2. delta debugging over tokens
    step = max(1, len(best) // 2)
    while step >= 1 and runs < budget:
        i, moved = 0, False
        while i < len(best):
            cand = best[:i] + best[i + step:]
            if diverges(cand):
                best, moved = cand, True
            else:
                i += step
        if not moved:
            step //= 2
    return best, runs


# --- the campaign -----------------------------------------------------------


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--runs", type=int, default=600,
                    help="scripts to try (default 600)")
    ap.add_argument("--tokens", type=int, default=40,
                    help="tokens per script (default 40)")
    ap.add_argument("--collection", default=None,
                    help="a .lvl under data/, or an absolute path "
                         "(default data/levels/LaserTank.lvl)")
    ap.add_argument("--level", type=int, default=0,
                    help="hammer one level instead of drawing at random")
    ap.add_argument("--levels-max", type=int, default=0,
                    help="only draw from the first N levels (default: all)")
    ap.add_argument("--seed", type=int, default=4)
    ap.add_argument("--jobs", type=int, default=8)
    ap.add_argument("--max-ticks", type=int, default=20000)
    ap.add_argument("--no-shrink", action="store_true")
    ap.add_argument("--replay", nargs=2, metavar=("LEVEL", "SCRIPT"),
                    help="re-check one script, with --field, and report it")
    args = ap.parse_args(argv)

    lvl = FLAGSHIP
    if args.collection:
        p = pathlib.Path(args.collection)
        lvl = p if p.is_absolute() else ROOT / "data" / args.collection
        if not lvl.exists():
            lvl = ROOT / "data" / "levels" / args.collection
    if not lvl.exists():
        print("undo_check: no such collection: %s" % lvl)
        return 2
    try:
        engines.require_engines()
    except SystemExit as ex:
        print(ex)
        return 2

    if args.replay:
        case = ScriptCase(lvl, int(args.replay[0]), args.replay[1])
        with engines.Scratch("undo") as sc:
            div, a, b = check(case, sc, field=True, max_ticks=args.max_ticks)
            cmd, report = engines.difftrace_report(sc.a, sc.b)
            print(" ".join(engines.command(engines.ORACLE, case, sc.a, field=True)))
            print(" ".join(engines.command(engines.CORE, case, sc.b, field=True)))
            print(report)
        return 0 if div is None else (3 if div.cosmetic else 1)

    n = engines.count_levels(lvl)
    top = min(n, args.levels_max) if args.levels_max else n
    rng = random.Random(args.seed)
    cases = []
    for i in range(args.runs):
        level = args.level or rng.randint(1, top)
        cases.append(ScriptCase(lvl, level,
                                script(random.Random(rng.randrange(1 << 30)),
                                       args.tokens)))

    print("undo_check: %d scripts of %d tokens over %s (%d levels)"
          % (len(cases), args.tokens, lvl.name, top))

    found, tokens_seen = [], Counter()
    for c in cases:
        tokens_seen.update(t for t in c.script if t in "zZcv")

    def work(case):
        with engines.Scratch("undo") as sc:
            div, _, _ = check(case, sc, max_ticks=args.max_ticks)
            return case, div

    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for i, (case, div) in enumerate(pool.map(work, cases), 1):
            if div is not None:
                found.append((case, div))
            if i % 100 == 0 or i == len(cases):
                print("  %4d/%d  %d divergence(s)" % (i, len(cases), len(found)))

    print("  commands exercised: " +
          ", ".join("%s=%d" % (k, tokens_seen[k]) for k in "zZcv"))

    if not found:
        print("undo_check OK -- %d scripts, 0 divergences" % len(cases))
        return 0

    # Dedupe by signature: a systematic bug reports once, not 600 times.
    by_sig = {}
    for case, div in found:
        by_sig.setdefault(div.sig, []).append((case, div))
    print("\nundo_check FAILED -- %d divergences, %d distinct signature(s)"
          % (len(found), len(by_sig)))
    cosmetic_only = True
    for sig, hits in by_sig.items():
        case, div = hits[0]
        cosmetic_only = cosmetic_only and div.cosmetic
        s = case.script
        if not args.no_shrink:
            with engines.Scratch("undo") as sc:
                s, runs = shrink(case, sc, sig)
            print("\n  [%s] %d hit(s); shrunk %d -> %d tokens in %d runs"
                  % (sig, len(hits), len(case.script), len(s), runs))
        else:
            print("\n  [%s] %d hit(s)" % (sig, len(hits)))
        print("    python tools/undo_check.py --replay %d %s" % (case.level, s))
        print("    " + "\n    ".join(div.detail.splitlines()[:8]))
    return 3 if cosmetic_only else 1


if __name__ == "__main__":
    sys.exit(main())
