#!/usr/bin/env python
"""Phase 5 step 5's first gate: the mouse player, judged by the C oracle.

    python tools/mouse_check.py                    # 500 scripts, ~60 s
    python tools/mouse_check.py --runs 5000        # a campaign
    python tools/mouse_check.py --collection quirks/rotary-mirrors.LVL
    python tools/mouse_check.py --replay 7 "mefn0f..m0a"

`MouseOperation` was the one function Phase 2 left unported, and the reason was
not that it is hard: it is that **nothing could reach it.**  It is driven by
`MBuffer`, which only `WM_LBUTTONDOWN` / `WM_RBUTTONDOWN` write, so no
keystream and no recording could make the tick call it, so no trace could say
whether a port of it was right.  Two phases of differential testing walked past
it without touching it.

Step 4's lesson is what closes it: **when a feature seems untestable because the
oracle has no way to be asked, the question is what input language is missing.**
`--script` grew two tokens -- `mXY` and `nXY`, a left and a right click on cell
XY written as two hex digits -- and both drivers push them into `MBuffer`
exactly as the window proc's non-editor arm does (LTANK.C:785, :825).  That
turns the last stub into an ordinary trace diff:

    oracle/build.exe        --levels L.lvl --level 7 --script "mefn0f" --trace a --field
    build/lasertank-core.exe --levels L.lvl --level 7 --script "mefn0f" --trace b --field
    python tools/difftrace.py a b

**What is actually being checked, and why it is worth a gate of its own.**  The
click is not a move.  `MouseOperation` runs a recursive flood fill
(`FindTarget`, LTANK2.C:277) from the clicked cell back to the tank and then
walks the path *backwards*, writing arrow keys into `RecBuffer` -- two per step
when the tank has to turn first, one when it does not.  So a click produces a
burst of keys and the tick consumes them one at a time, which means a single
diverging character anywhere in the fill, the tie-breaking order of the four
`else if`s, or the turn accounting shows up as a different keystream and then as
a different board.  It also means a `.lpb` recorded with the mouse is
indistinguishable from one played on the keyboard, which is the property that
makes porting this function change no rule at all.

**The three behaviours the scripts are shaped to hit.**

  * The destination filter, which is a hand-written range test on the object id
    (`dx < 3 || (dx > 14 && dx < 19) || dx > 23 || tunnel`).  It admits
    **water**, so a left-click on water is a legal instruction to drown; it
    admits a one-way from the wrong side; and it rejects everything else by
    returning FALSE, which the tick reads as "throw the rest of the buffer
    away" (`else MB_SP = MB_TOS;`).  Clicks are therefore drawn over the *whole*
    board rather than over reachable dirt -- the rejections are half the
    function.
  * The ring buffer.  `MB_TOS` wraps at 20 with no overrun check, and the tick
    drains at most one entry per tick and only when the key buffer is empty and
    the world is quiescent.  Bursts of clicks with no idle tokens between them
    are emitted on purpose so that the queue actually backs up and wraps.
  * Restore Position clears it (`MB_TOS = MB_SP = 0`, LTANK.C:963), and undo
    does not.  So `c` / `v` / `z` are mixed in with the clicks rather than being
    left to undo_check.py, because the interesting case is a *pending* click
    surviving, or not surviving, a rewind.

Divergences are shrunk the way undo_check.py shrinks a script, with the same
caveat and one more: a click token is three characters, and dropping one of them
changes the coordinate rather than removing the click, so the shrinker moves
whole tokens.

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
HEX = "0123456789abcdef"


# --- scripts ----------------------------------------------------------------


def tokens(script):
    """The script split into tokens, so the shrinker moves clicks whole.

    A click is three characters and its two hex digits are not tokens of their
    own; every other token is one character.  A truncated click at the end is
    returned as-is, because both drivers agree on what that means (consume the
    rest, post nothing) and the shrinker is allowed to produce one.
    """
    out, i = [], 0
    while i < len(script):
        if script[i] in "mn" and i + 3 <= len(script):
            out.append(script[i:i + 3])
            i += 3
        else:
            out.append(script[i])
            i += 1
    return out


def script(rng, n, p_click=0.34, p_fire=0.16, p_repeat=0.40, p_cmd=0.08):
    """One script of about `n` tokens, weighted towards the mouse.

    Clicks are drawn uniformly over the 16x16 board rather than over cells the
    filter accepts -- see this file's header: a rejected click is a code path,
    not a wasted token, and it is the one that empties the queue.  Bursts are
    emitted as phrases so `MB_TOS` gets ahead of `MB_SP` instead of the buffer
    holding at most one entry for the whole run.
    """
    out = []
    last = rng.choice(DIRS)

    def click(z):
        return ("m" if z == 1 else "n") + rng.choice(HEX) + rng.choice(HEX)

    while len(out) < n:
        r = rng.random()
        if r < p_click:
            out += rng.choice((
                [click(1)],
                [click(1)],
                [click(2)],
                [click(1), click(1)],                       # queue two
                [click(1)] * rng.randint(3, 6),             # let the ring back up
                [click(1)] + ["."] * rng.randint(1, 4),     # drain in the open
                [click(1), click(2)],
                [click(2), click(2)],
            ))
        elif len(out) >= 3 and r < p_click + p_cmd:
            out += rng.choice((
                ["z"],                                      # undo keeps the queue
                [click(1), "z"],                            # ... rewind mid-burst
                [click(1), "v"],                            # 112 clears it
                ["c", click(1), "v"],
                [click(1), "Z"],
            ))
        elif r < p_click + p_cmd + p_fire:
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

    Same shape and same caveat as undo_check.py's -- dropping a token moves
    every later one onto a different tick, so what is held fixed is the
    signature rather than the run -- with the one difference that the unit is a
    *token* from `tokens()`, never a character, so a click never loses a digit
    and turns into a different click.
    """
    best, runs = tokens(case.script), 0

    def diverges(ts):
        nonlocal runs
        if not ts or runs >= budget:
            return False
        runs += 1
        div, _, _ = check(case._replace(script="".join(ts)), scratch)
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
    return "".join(best), runs


# --- the campaign -----------------------------------------------------------


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--runs", type=int, default=500,
                    help="scripts to try (default 500)")
    ap.add_argument("--tokens", type=int, default=30,
                    help="tokens per script (default 30)")
    ap.add_argument("--collection", default=None,
                    help="a .lvl under data/, or an absolute path "
                         "(default data/levels/LaserTank.lvl)")
    ap.add_argument("--level", type=int, default=0,
                    help="hammer one level instead of drawing at random")
    ap.add_argument("--levels-max", type=int, default=0,
                    help="only draw from the first N levels (default: all)")
    ap.add_argument("--seed", type=int, default=5)
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
        print("mouse_check: no such collection: %s" % lvl)
        return 2
    try:
        engines.require_engines()
    except SystemExit as ex:
        print(ex)
        return 2

    if args.replay:
        case = ScriptCase(lvl, int(args.replay[0]), args.replay[1])
        with engines.Scratch("mouse") as sc:
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

    print("mouse_check: %d scripts of %d tokens over %s (%d levels)"
          % (len(cases), args.tokens, lvl.name, top))

    found = []
    seen = Counter()
    for c in cases:
        for t in tokens(c.script):
            seen[t[0] if t[0] in "mn" else t] += 1

    def work(case):
        with engines.Scratch("mouse") as sc:
            div, _, _ = check(case, sc, max_ticks=args.max_ticks)
            return case, div

    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for i, (case, div) in enumerate(pool.map(work, cases), 1):
            if div is not None:
                found.append((case, div))
            if i % 100 == 0 or i == len(cases):
                print("  %4d/%d  %d divergence(s)" % (i, len(cases), len(found)))

    print("  tokens exercised: " +
          ", ".join("%s=%d" % (k, seen[k]) for k in "mnzZcv" if seen[k]))

    if not found:
        print("mouse_check OK -- %d scripts, 0 divergences" % len(cases))
        return 0

    by_sig = {}
    for case, div in found:
        by_sig.setdefault(div.sig, []).append((case, div))
    print("\nmouse_check FAILED -- %d divergences, %d distinct signature(s)"
          % (len(found), len(by_sig)))
    cosmetic_only = True
    for sig, hits in by_sig.items():
        case, div = hits[0]
        cosmetic_only = cosmetic_only and div.cosmetic
        s = case.script
        if not args.no_shrink:
            with engines.Scratch("mouse") as sc:
                s, runs = shrink(case, sc, sig)
            print("\n  [%s] %d hit(s); shrunk %d -> %d tokens in %d runs"
                  % (sig, len(hits), len(tokens(case.script)), len(tokens(s)), runs))
        else:
            print("\n  [%s] %d hit(s)" % (sig, len(hits)))
        print("    python tools/mouse_check.py --replay %d %s" % (case.level, s))
        print("    " + "\n    ".join(div.detail.splitlines()[:8]))
    return 3 if cosmetic_only else 1


if __name__ == "__main__":
    sys.exit(main())
