#!/usr/bin/env python
"""Differential fuzzing: random keystreams through both engines, shrunk to a repro.

    python tools/fuzz.py --runs 2000                 # flagship collection
    python tools/fuzz.py --each 1 --jobs 8           # one keystream per level
    python tools/fuzz.py --level 93 --runs 500       # hammer one level
    python tools/fuzz.py --replay 47 rrfudlf         # re-check a saved repro

The 187 recorded playbacks are 187 paths through the state space and they are
all green; `data/levels/LaserTank.lvl` alone is 2,030 levels the corpus never
touches.  This is what explores the rest: generate a keystream, run the C oracle
and the C# core on it, compare traces (tools/engines.py, which is
tools/difftrace.py's comparison), and on a divergence **shrink it**.

The shrinker is the point.  A divergence at key 300 of 400 on level 1,712 is not
a bug report; `level 1712, keys "rrfud"` is.  See `reduce_keys()` -- shortest
diverging prefix, then delta debugging, then a pass that *measures* 1-minimality
rather than claiming it, all holding the *signature* (the first field that
moved) fixed so what comes out is a reduction of the same bug and not a
different one found along the way.

A green fuzz run means nothing until the harness has gone red on purpose:
`python tools/test_fuzz.py` injects known faults into the C# core, rebuilds, and
fails unless the fuzzer finds and shrinks each one.

Keystream shape: weighted toward fire and turn.  `--p-fire` is how often a key
is a shot; `--p-repeat` is how often a direction key repeats the last direction
(which *moves* the tank) rather than picking another one (which only *turns*
it -- MoveTank returns early, spending the key without moving).  Turning and
firing are the two input classes that reach the most engine code per key:
firing runs the whole laser subsystem, and a turn leaves the tank in place to
be caught by an anti-tank scan.  Defaults 0.30 / 0.45.

Findings are deduped by signature, so a systematic bug reports once and not
9,000 times; each distinct one gets a directory under `--out` holding the
minimal keystream, both traces re-run with `--field --bmf`, the full
`difftrace.py` report, and the level as ASCII.

`MouseOperation` is still unported and still throws.  If a keystream ever
reaches it the run reports `NOTPORTED` as a finding, which is the correct
outcome: it means the mouse buffer is reachable from a keystream, and that is
news about the buffer, not an obstacle to route around.

Exit: 0 no divergence, 1 diverged, 3 cosmetic-only divergence.
"""
import argparse
import json
import pathlib
import random
import subprocess
import sys
import threading
import time
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import difftrace                                        # noqa: E402
import engines                                          # noqa: E402
from engines import Case, ROOT                          # noqa: E402

FLAGSHIP = ROOT / "data" / "levels" / "LaserTank.lvl"
DIRS = "udlr"
NO_SHRINK = {"runs": 0, "seconds": 0.0, "minimal": False}


# --- keystreams -------------------------------------------------------------


def keystream(rng, n, p_fire, p_repeat):
    """n keys, weighted toward fire and toward turning rather than moving."""
    out = []
    last = rng.choice(DIRS)
    for _ in range(n):
        if rng.random() < p_fire:
            out.append("f")
            continue
        if rng.random() >= p_repeat:
            last = rng.choice([d for d in DIRS if d != last])   # a turn
        out.append(last)
    return "".join(out)


def cases(args, rng, nlevels):
    """The (level, keystream) sequence.  Deterministic given --seed."""
    lo, hi = args.level or args.first, args.level or min(args.last or nlevels, nlevels)
    if args.each:
        for level in range(lo, hi + 1):
            for _ in range(args.each):
                yield Case(args.levels, level, keystream(rng, args.keys,
                                                         args.p_fire, args.p_repeat))
    else:
        for _ in range(args.runs):
            yield Case(args.levels, rng.randint(lo, hi),
                       keystream(rng, args.keys, args.p_fire, args.p_repeat))


# --- the shrinker -----------------------------------------------------------


class Budget(Exception):
    """The shrink budget ran out.  Whatever has been reduced so far still holds."""


def reduce_keys(keys, ok):
    """Shrink a keystream to a 1-minimal one that still satisfies `ok`.

    `ok(keys) -> truthy` is the whole interface, which is what makes this
    testable without an engine: tools/test_fuzz.py drives it with synthetic
    predicates before it drives it with a real injected fault.  `ok` may raise
    `Budget` to stop early; what has been reduced so far is still returned.

    Three passes, cheapest first:

    1. **Shortest passing prefix**, by binary search.  Keys after the divergence
       cannot have caused it, and this is where nearly all the length goes --
       O(log n) calls to drop a 400-key stream to the ~20 that matter.  The
       predicate is not perfectly monotone in principle (a longer stream
       consumes a key where a shorter one had run out and stopped), so nothing
       is assumed: every prefix that survives is one that was actually tested,
       and pass 2 cleans up whatever the search left behind.

    2. **Delta debugging** (ddmin): try each of n chunks alone, then each
       complement, doubling n until nothing more can be removed.

    3. **Measure 1-minimality** instead of asserting it.  ddmin's exit condition
       guarantees it, but a guarantee on paper is not what a bug report should
       claim, so this deletes each remaining key in turn and checks the property
       goes away.  Nearly free -- ddmin already tried most of those and `ok` is
       expected to cache -- and it is the only pass that notices when the budget
       ran out mid-shrink.

    Returns (keys, minimal).
    """
    minimal = False
    try:
        # --- 1. shortest passing prefix
        lo, hi = 0, len(keys)
        while lo < hi:
            mid = (lo + hi) // 2
            if ok(keys[:mid]):
                hi = mid
            else:
                lo = mid + 1
        keys = keys[:hi]

        # --- 2. ddmin
        n = 2
        while len(keys) >= 2:
            size = len(keys) / n
            chunks = [keys[int(i * size):int((i + 1) * size)] for i in range(n)]
            for c in chunks:                       # is a single chunk enough?
                if c and ok(c):
                    keys, n = c, 2
                    break
            else:
                for i in range(n):                 # or everything but one chunk?
                    comp = "".join(chunks[:i] + chunks[i + 1:])
                    if comp and ok(comp):
                        keys, n = comp, max(n - 1, 2)
                        break
                else:
                    if n >= len(keys):
                        break
                    n = min(2 * n, len(keys))

        # --- 3. measure it
        minimal = all(not ok(keys[:i] + keys[i + 1:]) for i in range(len(keys)))
    except Budget:
        pass
    return keys, minimal


class Probe:
    """`reduce_keys`'s predicate, backed by the two engines.  Counts and caches.

    `want` is the signature to hold fixed.  A candidate counts as a hit only if
    it diverges *and* the first field to move is the same one, so what comes out
    is a reduction of the bug we started with rather than some other bug found
    along the way.  `--shrink-any` drops that to "any divergence", which is the
    escape hatch for when the same root cause surfaces through a different field
    once the noise around it is gone.
    """

    def __init__(self, level, args, want, budget):
        self.level, self.args, self.want, self.budget = level, args, want, budget
        self.runs = 0
        self.seen = {}
        self.scratch = engines.Scratch("lt-shrink")

    def close(self):
        self.scratch.close()

    def __call__(self, keys):
        if keys in self.seen:
            return self.seen[keys]
        if self.runs >= self.budget:
            raise Budget()
        self.runs += 1
        case = Case(self.args.levels, self.level, keys)
        a, b = engines.run_pair(case, self.scratch.a, self.scratch.b,
                                max_ticks=self.args.max_ticks)
        div = engines.compare(a, b)
        if div is not None and not self.args.shrink_any and div.sig != self.want:
            div = None
        self.seen[keys] = div
        return div


def shrink(level, keys, div, args):
    """A diverging keystream -> the shortest one that still diverges the same way.

    The reduction is `reduce_keys`; this wires it to the engines and re-measures
    the divergence on the result, so the tick and field a report quotes belong to
    the minimal repro and not to the 48-key stream it came from.
    """
    probe = Probe(level, args, div.sig, args.shrink_budget)
    t0 = time.time()
    keys, minimal = reduce_keys(keys, probe)
    final = probe.seen.get(keys) or div
    stats = {"runs": probe.runs, "seconds": time.time() - t0, "minimal": minimal}
    probe.close()
    return keys, final, stats


# --- reporting --------------------------------------------------------------


def level_ascii(levels, level):
    p = subprocess.run([sys.executable, str(ROOT / "tools" / "dump_level.py"),
                        str(levels), str(level)],
                       capture_output=True, text=True, cwd=str(ROOT))
    return (p.stdout or p.stderr).rstrip()


def write_repro(outdir, n, case, div, stats, original, args):
    """Everything needed to act on one divergence, in one directory."""
    safe = "".join(c if c.isalnum() or c in "._-" else "_" for c in div.sig)
    d = outdir / ("%03d-%s" % (n, safe))
    d.mkdir(parents=True, exist_ok=True)

    a, b = engines.run_pair(case, d / "a-oracle.trace", d / "b-core.trace",
                            field=True, bmf=True, max_ticks=args.max_ticks)
    cmd, report = engines.difftrace_report(a.trace, b.trace)
    recheck = engines.compare(a, b)
    head = engines.read_trace(a.trace)[0]
    meta = difftrace.parse_meta(head)

    cmds = [" ".join(engines.command(exe, case, tr, True, True, args.max_ticks))
            for exe, tr in ((engines.ORACLE, "a.trace"), (engines.CORE, "b.trace"))]
    if not stats["runs"]:
        shrunk = "Not shrunk (--no-shrink): this is the keystream as generated."
    elif stats["minimal"]:
        shrunk = ("Shrunk from %d keys to %d in %d runs (%.1fs), and **verified 1-minimal**:\n"
                  "deleting any single remaining key stops the divergence."
                  % (len(original), len(case.keys), stats["runs"], stats["seconds"]))
    else:
        shrunk = ("Shrunk from %d keys to %d in %d runs (%.1fs), but the shrink budget ran\n"
                  "out first -- this is **not** minimal.  Reduce it further with:\n\n"
                  "    python tools/fuzz.py --replay %d %s --shrink-budget 5000"
                  % (len(original), len(case.keys), stats["runs"], stats["seconds"],
                     case.level, case.keys))
    text = f"""# Divergence {n:03d}: `{div.sig}`

    level file  {case.levels}
    level       {case.level}  "{meta.get('name', '?')}"  by {meta.get('author', '?')}
    keystream   "{case.keys}"   ({len(case.keys)} keys)
    kind        {div.kind}{'  [cosmetic fields only]' if div.cosmetic else ''}
    first tick  {div.tick if div.tick is not None else '-'}

{shrunk}
Original keystream: `{original}`

## Reproduce

```
{cmds[0]}
{cmds[1]}
python tools/difftrace.py a.trace b.trace
```

Traces from that run are `a-oracle.trace` and `b-core.trace` in this directory
(`--field --bmf`, so every playfield cell on both layers).

## What moved

```
{div.detail}
```

## difftrace.py

`{cmd}`

```
{report}
```

## Level {case.level}

```
{level_ascii(case.levels, case.level)}
```
"""
    (d / "README.md").write_text(text, encoding="utf-8")
    # The repro is re-run here with --field --bmf, which is a wider trace than
    # the one that found it.  It must still diverge, and in the same place.
    if recheck is None or recheck.sig != div.sig:
        print("  !! WARNING: re-run with --field --bmf gives %s, not %s; see %s"
              % (recheck.sig if recheck else "no divergence", div.sig, d))
    return d


def write_findings(outdir, found):
    """findings.json -- the same repros, for a script rather than a reader.

    tools/test_fuzz.py reads this to check that an injected fault was not just
    found but shrunk to something 1-minimal.
    """
    out = []
    for sig, (case, div, stats, original) in found.items():
        out.append({
            "signature": sig, "kind": div.kind, "cosmetic": div.cosmetic,
            "levels": str(case.levels), "level": case.level, "keys": case.keys,
            "tick": div.tick, "original_keys": original,
            "shrink_runs": stats["runs"], "minimal": stats["minimal"],
        })
    (outdir / "findings.json").write_text(json.dumps(out, indent=2), encoding="utf-8")
    return out


# --- main -------------------------------------------------------------------


_local = threading.local()


def scratch():
    if not hasattr(_local, "s"):
        _local.s = engines.Scratch("lt-fuzz")
    return _local.s


def main():
    ap = argparse.ArgumentParser(
        description="Random-keystream differential fuzzing of the C oracle against the C# core.",
        epilog="exit 0 clean, 1 diverged, 3 cosmetic-only")
    ap.add_argument("--levels", type=pathlib.Path, default=FLAGSHIP,
                    help="level file to fuzz (default: the 2,030-level flagship)")
    ap.add_argument("--level", type=int, help="pin one level")
    ap.add_argument("--first", type=int, default=1, help="lowest level number")
    ap.add_argument("--last", type=int, help="highest level number")
    ap.add_argument("--runs", type=int, default=1000, help="random cases (default 1000)")
    ap.add_argument("--each", type=int, metavar="K",
                    help="instead of --runs: K keystreams on every level, in order")
    ap.add_argument("--keys", type=int, default=48, metavar="N",
                    help="keys per stream (default 48)")
    ap.add_argument("--p-fire", type=float, default=0.30,
                    help="fraction of keys that are a shot (default 0.30)")
    ap.add_argument("--p-repeat", type=float, default=0.45,
                    help="chance a direction key repeats the last one, i.e. moves "
                         "rather than turns (default 0.45)")
    ap.add_argument("--seed", type=int, default=0, help="RNG seed (default 0)")
    ap.add_argument("--jobs", type=int, default=8)
    ap.add_argument("--max-ticks", type=int, default=10000,
                    help="tick cap; a conveyor loop never goes quiescent (default 10000)")
    ap.add_argument("--minutes", type=float, help="stop after this long")
    ap.add_argument("--stop-after", type=int, default=0, metavar="N",
                    help="stop after N distinct divergences (0: never)")
    ap.add_argument("--out", type=pathlib.Path, default=ROOT / "build" / "fuzz",
                    help="where repro directories go (default build/fuzz)")
    ap.add_argument("--no-shrink", dest="shrink", action="store_false",
                    help="report raw keystreams; for timing the loop itself")
    ap.add_argument("--shrink-any", action="store_true",
                    help="let the shrinker keep any divergence, not only this signature")
    ap.add_argument("--shrink-budget", type=int, default=600, metavar="N",
                    help="engine-pair runs one shrink may spend (default 600)")
    ap.add_argument("--replay", nargs=2, metavar=("LEVEL", "KEYS"),
                    help="skip fuzzing: check one level+keystream and report it")
    ap.add_argument("-q", "--quiet", action="store_true")
    args = ap.parse_args()

    engines.require_engines()
    if not args.levels.exists():
        raise SystemExit("no such level file: %s" % args.levels)
    nlevels = engines.count_levels(args.levels)
    args.out.mkdir(parents=True, exist_ok=True)

    if args.replay:
        case = Case(args.levels, int(args.replay[0]), args.replay[1])
        with engines.Scratch("lt-replay") as s:
            a, b = engines.run_pair(case, s.a, s.b, max_ticks=args.max_ticks)
            div = engines.compare(a, b)
        if div is None:
            print("IDENTICAL   level %d, keys %r" % (case.level, case.keys))
            return 0
        print("DIVERGE     %s at tick %s (%s)\n%s"
              % (div.sig, div.tick, div.kind, div.detail))
        original = case.keys
        stats = NO_SHRINK
        if args.shrink:
            keys, div, stats = shrink(case.level, case.keys, div, args)
            case = Case(case.levels, case.level, keys)
            print("  shrunk %d -> %d keys in %d runs (%.1fs)%s"
                  % (len(original), len(keys), stats["runs"], stats["seconds"],
                     "" if stats["minimal"] else "  [budget exhausted]"))
        d = write_repro(args.out, 0, case, div, stats, original, args)
        print("\nwrote %s" % d)
        return 3 if div.cosmetic else 1

    rng = random.Random(args.seed)
    plan = list(cases(args, rng, nlevels))
    print("fuzz: %d case%s, %s, %d keys each, p_fire=%.2f p_repeat=%.2f, seed=%d"
          % (len(plan), "" if len(plan) == 1 else "s",
             "level %d" % args.level if args.level else
             "levels %d-%d of %s" % (args.first, min(args.last or nlevels, nlevels),
                                     args.levels.name),
             args.keys, args.p_fire, args.p_repeat, args.seed))

    def work(case):
        s = scratch()
        a, b = engines.run_pair(case, s.a, s.b, max_ticks=args.max_ticks)
        return case, engines.compare(a, b), engines.outcome(a.trace)

    found = {}                 # signature -> (case, div, stats, original)
    tally = Counter()
    cover = Counter()          # how far the runs actually got, per the footers
    deadline = time.time() + args.minutes * 60 if args.minutes else None
    t0 = time.time()
    done = 0
    pool = ThreadPoolExecutor(max_workers=args.jobs)
    try:
        for case, div, got in pool.map(work, plan):
            done += 1
            if got:
                cover["ticks"] += got["ticks"]
                cover["keys"] += got["used"]
                cover["offered"] += got["offered"]
                cover[got["result"]] += 1
            if div is not None:
                tally["divergences"] += 1
                tally["cosmetic" if div.cosmetic else "logic"] += 1
                if div.sig in found:
                    tally["duplicate"] += 1
                else:
                    print("\n%s divergence: %s   level %d, tick %s, %d keys"
                          % ("cosmetic" if div.cosmetic else "LOGIC",
                             div.sig, case.level, div.tick, len(case.keys)))
                    original = case.keys
                    if args.shrink:
                        keys, div, stats = shrink(case.level, case.keys, div, args)
                        case = Case(case.levels, case.level, keys)
                        print("  shrunk %d -> %d keys in %d runs (%.1fs)%s"
                              % (len(original), len(keys), stats["runs"],
                                 stats["seconds"],
                                 "" if stats["minimal"] else "  [budget exhausted]"))
                    else:
                        stats = NO_SHRINK
                    print("  level %d, keys %r  ->  %s at tick %s"
                          % (case.level, case.keys, div.sig, div.tick))
                    d = write_repro(args.out, len(found) + 1, case, div, stats,
                                    original, args)
                    print("  wrote %s" % d)
                    found[div.sig] = (case, div, stats, original)
                    if args.stop_after and len(found) >= args.stop_after:
                        print("\nstopping: %d distinct divergence%s"
                              % (len(found), "" if len(found) == 1 else "s"))
                        break
            if not args.quiet and (done % 50 == 0 or done == len(plan)):
                print("  %-52s" % ("%d/%d  %.1f cases/s  %d divergence%s, %d distinct"
                                   % (done, len(plan), done / max(time.time() - t0, 1e-9),
                                      tally["divergences"],
                                      "" if tally["divergences"] == 1 else "s",
                                      len(found))),
                      end="\r", flush=True)
            if deadline and time.time() > deadline:
                print("\nstopping: --minutes reached after %d cases" % done)
                break
    finally:
        pool.shutdown(wait=False, cancel_futures=True)
    print(" " * 60, end="\r")

    write_findings(args.out, found)
    print("\n%d cases in %.0fs   %d divergence%s (%d distinct signature%s)"
          % (done, time.time() - t0, tally["divergences"],
             "" if tally["divergences"] == 1 else "s",
             len(found), "" if len(found) == 1 else "s"))
    # Keys *consumed*, not keys generated: random play drowns or shoots the tank
    # early, so the two numbers are far apart and only the first is coverage.
    if cover["offered"]:
        print("%d tick-lines compared, %d of %d keys consumed (%.0f%%);  %s"
              % (cover["ticks"] + done, cover["keys"], cover["offered"],
                 100.0 * cover["keys"] / cover["offered"],
                 "  ".join("%s %d" % (r, cover[r]) for r in
                           ("WIN", "DEAD", "UNFINISHED", "NOTPORTED") if cover[r])))
    if not found:
        print("no divergence.  A green fuzz run is only worth what the "
              "fault-injection test says it is: python tools/test_fuzz.py")
        return 0
    print("\n--- %d distinct ---" % len(found))
    for sig, (case, div, stats, original) in found.items():
        print("  %-12s level %-5d %-30r %s"
              % (sig, case.level, case.keys,
                 "tick %s" % div.tick if div.tick is not None else div.kind))
    print("\nrepros in %s" % args.out)
    return 3 if tally["logic"] == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
