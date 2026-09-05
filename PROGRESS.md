# LaserTank → Godot port: progress & handoff

**Purpose:** single source of truth for this port. Read this first after a context clear.
Update the *Status* block and *Session log* at the end of every working session.

---

## Start here after a context clear

**Where the project is:** Phases 1-3 complete, Phase 4 (the solver) complete through layer 4,
Phase 5 (presentation) not started. The rest of this file is the reasoning behind each of those,
kept because the reasoning *is* the handoff. This block is the ninety-second version.

**What is built.** A C reference oracle (`oracle/`), a C# transliteration of the engine that traces
byte-identically to it (`src/LaserTank.Core/`), a differential fuzzer (`tools/fuzz.py`), and a
five-layer solver (`src/LaserTank.Solver/`). No Godot yet — that is Phase 5.

**Build, then check nothing rotted** (about three minutes all in):

```bash
bash oracle/build.sh && bash src/build.sh      # -> build/lasertank-{core,solve}.exe
python tools/replay_all.py                     # 187 replayed, 181 win, 6 documented non-win
python tools/test_difftrace.py                 # 29 passed
python tools/sweep.py                          # 2,347/2,347 identical
python tools/test_fuzz.py                      # 25 passed  (slow: injects faults and rebuilds)
```

Those four are the fidelity gates and must be green before anything else is believed. The fifth,
`tools/verify_solutions.py`, needs solver output to check and so runs after a campaign.
`test_fuzz.py` patches `Engine.cs` and restores it — a green run leaves the tree byte-clean, and if
it ever does not, read the line-ending trap in *Environment notes* before anything else.

**Solve one level and watch it:**

```bash
build/lasertank-solve.exe --levels data/levels/Beginner-I.lvl --level 44 --verbose \
  --out build/try --nodes 400000
python tools/verify_solutions.py build/try      # both engines, byte-identical, or it did not happen
```

**The whole shipped chain** — a layer-0 campaign, then three passes that each attack only what the
previous one failed. `STRIDE=5` gives the 4,185-level sample every number in Phase 4 is quoted
against; each of the three passes over ~3,700 failures measured 6-8 minutes at 14 jobs, and the
campaign itself is the longer half. `STRIDE=1` is the whole 20,914-level corpus and is hours —
budget it as such rather than trusting an extrapolation:

```bash
STRIDE=5 NODES=150000 tools/campaign.sh solutions/l0 build/reports/l0.jsonl --no-macro
tools/second_pass.sh build/reports/l0.jsonl  solutions/l34 build/reports/l3n.jsonl \
                     --no-ida --no-beam --subgoal
tools/second_pass.sh build/reports/l3n.jsonl solutions/l34 build/reports/l34.jsonl \
                     --no-ida --no-beam --subgoal --sg-eval learned
tools/second_pass.sh build/reports/l34.jsonl solutions/l34 build/reports/l34pass4.jsonl \
                     --no-ida --no-beam --macro --macro-first
python tools/verify_solutions.py build/solutions/l0     # layer 0's own solutions
python tools/verify_solutions.py build/solutions/l34    # everything the three passes added
```

`report_stats.py` reads any of those reports (`--diff` compares two layers); the composite is the
*union* of the four, which is why the two verify runs are separate. A level solved by an earlier
pass is skipped by a later one, so no `.lpb` is ever written twice.

**Three rules the last four sessions each paid to learn**, and they are the reason the numbers in
this file can be trusted:

- **Bench on the corpus, not on a filtered population.** `build/reports/bench-levels.txt` is levels
  layer 0 failed. It flattered layer 1, flattered layer 2, overstated layer 3's budget scaling and
  *understated* layer 4. A bench picks parameters; only a campaign decides what ships.
- **Govern by `--nodes`, never wall clock.** A node is one `Engine.ApplyKey`. Seconds are not
  reproducible on a machine that is also running the gates.
- **Instrument before theorising.** `--sg-trace` killed two of layer 2's designs and one of layer
  3's; layer 4's `tools/rankdump.py` decided whether that layer was worth building at all. A new
  layer should report a distribution before it reports a solved count.

**What is deliberately frozen.** `original/` is a read-only historical artifact.
`src/LaserTank.Core/Engine.cs` is the transliteration and differs from a literal one by the single
word `partial`; `Engine.Search.cs` has not changed since layer 0, and layers 1-4 all kept it that
way. If a solver change seems to need an engine change, that is the signal to stop and re-read.

**Artifacts live under `build/`, which is gitignored** — they survive a context clear but not a
`git clean`. The one thing that does not need regenerating is layer 4's weight vector: it is
compiled into `src/LaserTank.Solver/Weights.cs`, so the solver is fully functional from a fresh
clone. Everything else in `build/reports/` is a measurement that can be re-run.

---

## Status

**Phases 1-3 complete. Phase 4 complete through layer 4. Phase 5 not started.**

| | state |
|---|---|
| C reference oracle | replays the whole corpus; ground truth, never refactored |
| C# core | **byte-identical to the oracle on all 187 recordings** with `--field --bmf` |
| Differential fuzzer | harness proven by fault injection; 20,626 cases, 0 divergences |
| Solver | five layers; **472 of the 4,185-level stride sample (11.3%)**, all verified |
| Presentation (Godot) | not started — see Phase 5 |

**The gates, and what green looks like.** `replay_all.py` 187 replayed / 181 win / 6 documented
non-winners / 0 unexpected, and 112/112 `Tutor-with-Playbacks` matching their bundled `.ghs` on
moves *and* shots. `test_difftrace.py` 29 passed. `test_fuzz.py` 25 passed. `sweep.py` 2,347/2,347
identical. `verify_solutions.py` over `build/solutions/` — every solver `.lpb` wins on both engines
with byte-identical traces.

**What is still not ported: `MouseOperation`, and only that.** The mouse buffer is empty headless
(`MB_TOS == MB_SP` always), so the tick's mouse block never fires and no keystream can reach it —
measured, not assumed: the fuzz campaign reached it zero times. It **throws** rather than no-ops, so
if that premise ever breaks the run stops loudly. It is Phase 5 work: a UI entry point, not game
logic.

**Next action:** Phase 5, and its section below is a plan rather than a wish list. Phase 3's
fuzzer can also keep running in parallel on new seeds and the 12 collections its first campaign
never touched. **Blocked on:** nothing.

---

## Goal & hard constraints

Modernize LaserTank 4.1.2 (public domain, Jim Kindley / Yves Maingoy) into Godot.

1. **Game logic must be preserved exactly, quirks included.** Many "bugs" are load-bearing —
   whole level packs exist *only* to exploit them. Upstream says so explicitly:
   *"Some of the tricks are bugs that have been intentionally left in the software because they
   make the game more interesting."* (`data/quirks/tutor/Tutor-ReadMe.txt`)
2. **Community file formats stay readable and writable**: `.lvl`, `.lpb`, `.hs`, `.ghs`, `.ltg`.
   25 years of community content depends on them.
3. Equivalence with the original must be *demonstrated*, not asserted.

---

## The key insight

**The game is a deterministic 20 Hz tick machine driven by a keystroke stream.**

Live keypresses do not act directly. `WM_KEYDOWN` (`LTANK.C:570`) appends the raw VK code to
`RecBuffer` via `AddKBuff`; the timer tick consumes one key per tick when the world is quiescent
(`LTANK.C:613`). Playback and live play run *the same code path*. No RNG, no floats, no
frame-rate dependence.

Consequences:

- **Validation does not require solutions.** Feed both engines the same keystream, dump a per-tick
  state trace, diff. This is the primary correctness strategy.
- A solver is *corpus extension*, not the validation plan. Do not let it become the critical path.

---

## Architecture decision

Three engines, one truth.

| # | Engine | Role |
|---|---|---|
| 1 | **C reference oracle** | Original `LTANK2.C`, unmodified logic + stub Win32 layer, headless, emits traces. **Ground truth forever. Never refactor.** |
| 2 | **Godot core** | Literal transliteration. Headless, pure, no `Node`/rendering/signals. Steps one tick. |
| 3 | **Presentation** | Godot nodes reading core state, interpolating between ticks. |

**Core language: C#** (Godot .NET). Fast enough for a solver doing millions of state expansions,
readable, one build across desktop targets.
Rejected: GDScript (10–50× too slow for search); C++ GDExtension (max fidelity but keeps us
shipping 25-year-old code and complicates web export).

**Transliterate literally.** Including the ugly parts. Idiomatic rewriting is how quirks die.

---

## Phases

### Phase 1 — Oracle & tooling  ☑
- ☑ Build `LTANK2.C` headless, **preserving logic-carrying side effects**. Key move: stub only the
  Win32 *API*, never LaserTank's own code — see `oracle/README.md`. `UpDateLaserBounce` compiles
  verbatim, so hazard #1 survives for free.
- ☑ Drive from a keystream file; emit per-tick trace.
- ☑ Parsers for `.lvl` / `.ghs` / `.lpb`.
- ☑ Trace format — see `oracle/README.md`.
- ☑ Replay all 187 corpus `.lpb` (186 shipped + level 21 decoded from its packed `.txt`).
- ☑ For `Tutor-with-Playbacks`, assert move/shot counts match its bundled `.ghs` — **112/112 exact**.

**Exit criterion — met, restated.** "186/186 reach the flag" was the wrong bar: the corpus does not
contain 186 solutions. 181 of 187 reach the flag; the other 6 are documented incomplete recordings.
The suite encodes that distinction, so it is a real regression gate rather than a permanently red
run. See "The six non-winning recordings" below.

Toolchain: MinGW-w64 (WinLibs, gcc 16.1, UCRT) installed via
`winget install BrechtSanders.WinLibs.POSIX.UCRT`. Nothing else is needed to build the oracle.

### Phase 2 — Transliterate the core  ☑
- ☑ **step 0:** `tools/difftrace.py` — compare two traces (or two directories of them), report the
  first diverging tick and the first field that moved. Nothing downstream is checkable without it.
  Self-tested by `tools/test_difftrace.py`; see "The Phase 2 harness".
- ☑ **step 1:** the C# projects exist and build; the load path and the tick frame are
  transliterated, and an empty-keystream run traces identically to the oracle. See below.
- ☑ **step 2:** `CheckLoc` and `MoveObj`, plus `MoveObj`'s closure — `TranslateTunnel`,
  `UpDateTankPos`, `UpdateUndo`, `ResetUndoBuffer`.
- ☑ **step 3:** `MoveTank` and `AntiTank`. They go together: `Tick()` runs `AntiTank()` after
  every consumed key, so `MoveTank` on its own advances the trace by nothing.
- ☑ **step 4:** the laser subsystem — `FireLaser`, `MoveLaser`, `CheckLLoc`, plus `KillAtank`,
  `UpDateLaserBounce`, `TestIfConvCanMoveTank` and the `SlideO`/`SlideMem` stack helpers.
- ☑ **step 5:** `ConvMoveTank`, `IceMoveT`, `IceMoveO` — the ice and conveyor code, and with it
  quirk #6 and the first real exercise of hazard #1.

**Exit criterion — MET.** `difftrace.py build/t-oracle build/t-csharp` reports **187/187 identical**
with `--field --bmf`, exit 0. `MouseOperation` is the only unported function and is unreachable
from a keystream.

Run both engines with `--field` (and `--bmf` while porting `Animate`) so the diff has the whole
playfield to bite on, not just the hashes.

**Exit criterion:** identical traces on all 187, `--field` included. `BMF`/`AniLevel` differences
are cosmetic (hazard #2) — worth investigating as a tripwire, but not a correctness failure.
`difftrace.py` encodes exactly that distinction: exit 1 for a logic divergence, exit **3** for a
cosmetic-only one, and `--strict` if you want to hold the cosmetic line too.

#### The Phase 2 harness

```
bash oracle/build.sh && bash src/build.sh
python tools/test_difftrace.py                                  # trust the differ first
python tools/replay_all.py --traces build/t-oracle --field --bmf
python tools/replay_all.py --traces build/t-csharp --field --bmf \
       --engine build/lasertank-core.exe
python tools/difftrace.py build/t-oracle build/t-csharp -q      # -q: failures only
```

Add `--pack game-objects` to both replay lines for the 16-recording fast loop (one level per
object); run the whole 187 before believing anything. The full pair takes a few minutes and about
327 MB per side.

`replay_all.py --engine` is what makes both sides one script, and it works because **the C# CLI
takes the oracle's arguments and emits byte-identical trace lines** — same field order, same
spacing, same `%08lx` hashes, same `#` header and result footer (`oracle/driver.c`, `trace_tick`).
Do not invent a nicer format: the differ is textual on purpose, so any drift shows up as a
divergence rather than as a parser bug.

**When a trace stops early.** `NotPortedException` used to take the process down before the
buffered trace was flushed, so a partly-ported engine produced *no* trace and `difftrace.py`
reported `UNUSABLE` — no signal at all. The CLI now catches it, closes the trace after the last
*complete* tick, writes `# result=NOTPORTED`, prints the function and the tick to stderr and exits
**4**. `difftrace.py` then says `DIVERGE length mismatch after N ticks`, which is the useful
reading: the ported half matched for N ticks and the port has to continue at N+1. Nothing about
this softens the failure — the point of the exception is that it is never mistaken for a result,
and `NOTPORTED` is not a result.

**First milestone, before `CheckLoc` — done.** Run with an empty keystream (`--keys ""`). Both
engines trace exactly two lines, `t=0` and `t=1`: level load, then one idle tick. Matching those
two lines means the `.lvl` parser, the `TGAMEREC` layout, `BuildBMField`, `PutLevel`, `Animate`,
the fnv1a hashes and the trace formatting are already right — a large fraction of the surface
area, verified before a single rule of game logic is written.

#### What the C# side looks like now

```
src/LaserTank.Core/    Objects.cs  GameState.cs  LevelFile.cs  Engine.cs
src/LaserTank.Cli/     Program.cs  TraceWriter.cs      -> build/lasertank-core.exe
```

Ported: `BuildBMField`, `PutLevel`, `UpDateTank`'s `TankDirty` write, `GameOn`, `Animate`,
the logic-carrying half of `LoadNextLevel`, the whole `Tick()` frame, the
`SendMessage`/`PostMessage` death split (quirk #8: `SendDead` runs inline, `PostDead` queues for
`Pump()` after the tick, exactly as the oracle's stub message pump does), — step 2 —
`CheckLoc`, `TranslateTunnel`, `UpDateTankPos`, `UpdateUndo`, `ResetUndoBuffer`, `MoveObj`, —
step 3 — `MoveTank`, `AntiTank`, and — step 4 — `FireLaser`, `MoveLaser`, `CheckLLoc`,
`KillAtank`, `UpDateLaserBounce`, `TestIfConvCanMoveTank` and the `SlideO`/`SlideMem` helpers.

and — step 5 — `ConvMoveTank`, `IceMoveT`, `IceMoveO`. That is every function a keystream can
reach.

**`MouseOperation` is the only remaining stub, and it still throws rather than no-ops.** A silent
stub would produce a *plausible* wrong trace, which is the one failure mode this whole approach
exists to prevent — and step 4 turned that from an argument into an incident report: the tick frame
had been passing `0` instead of `S_Fire` to `FireLaser` since step 1, and only the exception kept
it from silently corrupting `laser.Good`. Today `replay_all.py --engine build/lasertank-core.exe`
reports 187 replayed / 181 win / 0 unexpected / 112 `.ghs` exact — the oracle's own numbers.

Worth knowing before step 4: `AntiTank` is a `wasIce` writer even though it never names the flag.
Its four scans are `while (CheckLoc(...))` loops, so whichever scan ran last leaves `wasIce`
holding its final probe — and `MoveTank`, `IceMoveT`, `IceMoveO` and `ConvMoveTank` all read it
after their own `CheckLoc`. That is quirk #3 with a longer reach than the three callers the
hazard list names.

Decisions worth not relitigating:

- **`byte[16,16]`, not `sbyte`.** `PF`/`PF2`/`BMF`/`BMF2` are `char[16][16]` in C, and gcc's `char`
  is signed. It cannot matter: `BuildBMField`'s 2003 sanitisation forces every cell to `<= 0x19`
  or to a tunnel (`0x40 | id<<1 | wait`), so nothing above `0x7F` survives a load. The single place
  the original's signedness is visible is `GetOBM(char)`'s `ob > -1` guard, which `Obj.GetOBM`
  keeps verbatim.
- **Original names, not C# conventions.** `Game`, `ScoreMove`, `SlideO`, `wasIce`, `IceMoveO`.
  Renaming is how a quirk stops looking like a quirk.
- **`net8.0` for both projects.** It is the lowest TFM Godot 4.x accepts, so Phase 5 can reference
  `LaserTank.Core` unchanged; the CLI sets `RollForward=LatestMajor` because only the .NET 10
  runtime is installed.
- **The undo buffer is carried even though nothing headless reads it.** `UndoStep` is unreachable
  from a keystream, so the `TGAMEREC` snapshots `UpdateUndo` stores are write-only. `UndoP` is not:
  `MoveObj`'s tunnel path decrements it (quirk #7), so its growth (`UndoBufSize` in steps of 200)
  and its roll-over at `UndoMax` have to be exact, and the cheapest way to be sure of that is to
  keep the buffer that they are the indices into. The two `GlobalReAlloc == NULL` branches in
  `UpdateUndo`/`ResetUndoBuffer` are *not* carried: the oracle's stub is plain `realloc`
  (`oracle/win32_stub.c:74`), so they are unreachable on both sides. They are the only pieces of
  either function left out.
- **Trace line 1 differs by design** (`# lasertank core trace` vs `# lasertank oracle trace`).
  Line 2 and every tick line are byte-identical; `difftrace.py` reads level/name/author/keys off
  line 2 to check both sides ran the same input.

What `difftrace.py` gives you when it does go wrong:

```
=== first divergence: tick 90 (line 91) ===
first field:  T.dir 1 -> 4                 [tank]
also:         T.firing 1 -> 0, L.y 11 -> 12, S.shots 23 -> 22
    PF[x=4,y=9]       0d mirror dr      -> 19 thin ice
```

The field name *is* the localisation — `S.moves` sends you to `ScoreMove`, `SlO.dy` to `IceMoveO`,
`P` to the key-consume test at `LTANK.C:613`. The summary at the end counts how many ticks each
field diverges on, which separates "one wrong cell" from "everything after tick 90".

### Phase 3 — Differential fuzzing  ◐ (harness done and proven; more campaigns welcome)
This is what finds the remaining divergences. Random keystreams (weight toward fire/turn), both
engines, diff traces, shrink any divergence to a minimal repro. Run across all 20,914 levels.
Cheap, unlimited, needs no solutions.

`tools/fuzz.py` does all of that; `tools/test_fuzz.py` is what makes a green run mean something.

#### The Phase 3 harness

```
bash oracle/build.sh && bash src/build.sh
python tools/test_difftrace.py                       # trust the differ      29 cases
python tools/test_fuzz.py                            # trust the fuzzer      25 cases, ~90 s
python tools/sweep.py                                # 2,347 levels, empty keystream, ~50 s
python tools/fuzz.py --each 3 --seed 1               # 6,090 cases over the flagship, ~140 s
```

**Run `test_fuzz.py` before believing any green fuzz run.** It is the one step that is easy to
skip and the one the whole phase rests on: a fuzzer that has never gone red is untested, and
"20,626 cases, no divergence" is then a claim about the fuzzer rather than about the port. It
patches `Engine.cs` with two faults a transliteration would plausibly make, rebuilds, and requires
that each is found, shrunk, and independently reproducible — then restores `Engine.cs` **in
bytes** (an earlier version used `write_text` and silently rewrote all 1,362 lines LF→CRLF, which
`git diff` hides under `core.autocrlf`) and requires green again. If it is ever killed between the
patch and the restore: `git checkout src/LaserTank.Core/Engine.cs`.

**The shrinker is the deliverable, not an extra.** A divergence at key 300 of 400 on level 1,712
is not a bug report; `level 1712, keys "rrfud"` is. `fuzz.reduce_keys` is three passes:

1. **Shortest diverging prefix**, by binary search — O(log n) runs, and where nearly all the length
   goes. Not assumed to be sound: every prefix that survives is one that actually ran and actually
   diverged, and pass 2 cleans up after it.
2. **Delta debugging** (ddmin) over what is left.
3. **Measure 1-minimality rather than claim it** — delete each remaining key and check the
   divergence goes away. ddmin's exit condition guarantees this on paper; a bug report should not
   quote a guarantee it did not check. Nearly free, because the candidates are already cached.

All three hold the divergence **signature** fixed — the name of the first field that moved, which
is `difftrace.py`'s localisation — so what comes out is a reduction of the bug you started with and
not a different one found along the way. `--shrink-any` relaxes that when the same root cause
surfaces through a different field once the noise is gone.

Each finding gets a directory under `--out`: the minimal keystream, both traces re-run with
`--field --bmf`, the full `difftrace.py` report, the level as ASCII, and the exact commands. Plus
`findings.json` for scripts. Findings are deduped by signature, so a systematic bug reports once
rather than 9,000 times.

**Fuzzing runs without `--field`/`--bmf`** — those add 512 hex bytes per tick each, and the default
trace already carries `H=fnv1a(PF),fnv1a(PF2)`, so a playfield divergence still shows up, as a hash
rather than a cell. The minimal repro is then re-run *with* both, and the tool warns if the wider
trace does not reproduce the same signature.

#### What the first campaign covered

Six keystream shapes, because one shape is one bias — varying length and the fire/turn weights is
how you reach code the default under-samples:

| run | cases | shape | keys consumed |
|---|---:|---|---:|
| `--each 3` | 6,090 | every flagship level ×3, 48 keys | 65% |
| `--keys 200` | 3,000 | long streams | 42% |
| `--p-fire 0.60` | 3,000 | shot-heavy | 71% |
| `--p-fire 0.10 --p-repeat 0.80` | 3,000 | movement-heavy, 96 keys | 51% |
| `--p-fire 0.15 --p-repeat 0.05` | 3,000 | almost pure turning | 84% |
| every quirk pack, `--each 8` | 2,536 | 64 keys | 20–100% |

**20,626 cases, 3,751,638 tick-lines, 0 divergences.** Consumed keys, not generated keys, is the
honest coverage number: random play drowns or shoots the tank early, so the two differ by nearly
half, and `fuzz.py` reports both.

**Two things the campaign taught about running it, both worth not rediscovering:**

- **Four of the ten quirk packs ship `.LVL`, six ship `.lvl`** — `tutor`, `tutor-with-playbacks`,
  `rotary-mirrors` and `game-objects` are the uppercase ones, i.e. exactly the four biggest. A
  `*.lvl` shell glob skips them in silence. `replay_all.py` already used `suffix.lower()`;
  `sweep.py` now does too. Anything new that walks `data/quirks/` must.
- **Random keystreams are shallow.** Over half of all runs end `DEAD`, and the flagship's own
  level 1 needs 149 keypresses to win. Random play is wide but not deep, which is the argument for
  Phase 4's solver being the *other* kind of coverage rather than a nice-to-have: a solved level is
  a long, legal, non-random path through the engine.

### Phase 4 — Solver  ☑ (layers 0-4; whole corpus run and every solution verified)

**The bar, measured before building anything.** Best-known solution cost from the 13 `.ghs` files,
by the difficulty rating in each level record:

| tier | levels | `.ghs` moves+shots p10 / p50 / p90 | median pushables |
|---|---:|---|---:|
| Kids | 4,720 | 7 / **46** / 215 | 12 |
| Easy | 10,572 | 34 / **122** / 607 | 19 |
| Medium | 4,058 | 68 / 265 / 1204 | 22 |
| Hard | 1,233 | 124 / 403 / 1353 | 20 |
| Deadly | 289 | 225 / 610 / 2169 | 21 |

Only 1,771 of 20,914 levels have a best-known total ≤20, and 4,750 ≤50. Eleven levels in the whole
corpus contain no block, ice, mirror, conveyor, anti-tank or tunnel. **Kids is not a shallow tier,
it is a small-branching one.** And keypresses are *worse* than these numbers: `ScoreMove` only
increments in `UpDateTankPos` (`Engine.cs:495`) while `MoveTank` spends a whole keypress on a turn
without scoring (`Engine.cs:491`), so "103 moves + 46 shots = 149 keypresses" is a lower bound —
add one key per direction change. A keypress-level exhaustive search reaches the ≤20 bucket and
nothing else, which is why the plan is layered.

**The plan.** Layer 0 is built (below). Above it:

- **Layer 1 — macro-actions.** ☑ Built and measured. `Goto(x,y,dir)` — a movement closure *over
  the engine*, not a grid A* — plus `Shoot`, so depth becomes the shot count. It wins on shallow
  branchy levels and loses on deep ones; the numbers and the reason are below, and the reason is
  the argument for layer 2.
- **Layer 2 — subgoal decomposition.** ☑ Built and measured. Find what blocks the flag, and accept
  a move because it removed one of those things rather than because a heuristic went down. The
  obstacles are derived from the *executed* movement closure, not from a model — the section below
  has the version that used a model instead, and the measurement that killed it. **As a second pass
  it is worth roughly twice layer 1: +40 against +21**, and the two are complementary (441 together).
  As a portfolio member it loses, for the same structural reason layer 1 does.
- **Layer 3 — restarts.** ☑ Built and measured. The subgoal beam is re-run when it dies of an
  *empty frontier* with budget still in hand, each restart doubling its width and slack. It is the
  first layer that is **strictly additive** — attempt 0 is layer 2 exactly, and a restart only ever
  spends budget that was already forfeit — so it needs no portfolio bet. It is also the smallest
  win: **+4 on the pass, 441 -> 444 composite**, and the section below has the negative result that
  is worth more than the number.
- **Layer 4 — learning.** ☑ Built and measured. A learned evaluation over board features,
  fit to 20,148 ranking groups taken from 652 winning trajectories, replacing `WorkDistance` as
  the subgoal beam's *ranking key only*. It is the largest win since layer 2: **+30 on the pass,
  444 -> 472 composite, none lost**. The measurement that authorised it is worth more than the
  number — the successor the winner used is in the expansion's output **97.6%** of the time and
  `WorkDistance` ranks it **100th of 395** — and so is the one that killed the second half of the
  plan: feeding the newly solved levels back in and refitting *halves* what the model discovers.
  Hints as landmarks was not built; the size was already measured and says not to — only
  **175 of 20,914** hints are recipe-grade (>=2 grid references or numbered steps), concentrated
  where search fails (0.4% of Kids, 3.5% of Hard, 7.3% of Deadly), which is a tail tool against a
  3,700-level tail.

**Be realistic.** No public automated LaserTank solver solves the full set. The deliverable is a
solved-count-vs-budget curve, Kids-first ordered by `.ghs` cost, not a promise of 20,914.

#### Layer 0 — the search API and the harness  ☑

`Engine.cs` gained exactly one word (`partial`). Everything else is additive:

- **`src/LaserTank.Core/Engine.Search.cs`** — `Snapshot`/`Restore` of the whole mutable engine,
  `ApplyKey` (one keypress, then tick to quiescence), `StateHash` for a transposition table, and
  `ActionKeys`. The rule is **restore everything, hash a subset**: staleness is load-bearing here
  (`wasIce`, quirk #3; `WaitToTrans`), so the hash keeps them while dropping `BMF`, the counters
  and the path.
- **`src/LaserTank.Solver/`** → `build/lasertank-solve.exe`. Weighted beam + IDA* over macro-steps,
  a flag-distance heuristic, the trimmer, and a parallel batch harness that writes each solution as
  a **`.lpb`** — a real recording, playable in the 2010 binary, not a private format.
- **`tools/verify_solutions.py`** — replays every produced `.lpb` through the *unmodified* oracle
  and the core with `--field --bmf` and requires WIN on both plus byte-identical traces.

Two bugs the harness found on itself, both worth not re-discovering:

- **A macro-step is not bounded by anything cheap.** Level 1491 ("Grand Prix 2", hint: *"get on the
  conveyor and watch"*) takes **3,652 ticks for one keypress** — the tank rides a closed conveyor
  circuit around the whole board. A 512-tick cap called that a hang and threw the level away.
  `ApplyKey` now detects *cycles* (sampled state hashes, started only after 256 ticks so the common
  case pays nothing) and keeps the tick cap only as a backstop. Genuine eternal cycles exist and
  must stay reportable: Tutor 43 is literally "Smallest eternal cycle".
- **`Restore` rewinds `RecP` but the keystream is one shared array.** A breadth-first search then
  silently corrupts its own answers: siblings overwrite each other's keys and the winning node
  reports whichever prefix was written last. A depth-first search never notices — IDA* was green
  while every beam solution failed to replay. The snapshot now carries its key prefix. Caught only
  because the harness replays each solution before writing it; that self-check earns its place.

**First numbers** (the 150 cheapest-by-`.ghs` levels of `LaserTank.lvl`, 3 s and 400k macro-steps
per level, 8 workers, 20 s wall):

| tier | attempted | solved | median keypresses / record |
|---|---:|---:|---:|
| Kids | 81 | 66 (81.5%) | 1.6× |
| Easy | 61 | 37 (60.7%) | 1.6× |
| Medium | 8 | 7 | 1.6× |

**110/110 verified through both engines**, byte-identical traces, and **73 of them match the
`.ghs` record exactly**. The 40 unsolved stopped on budget (30), a beam dead end (5) or the IDA*
depth cap (5) — that breakdown is the argument for layer 1 rather than for a bigger budget.

A second, larger sample on the easiest collection: **`Beginner-I.lvl`, 400 cheapest levels, 2.5 s
each, 12 workers — 384 solved in 12 s wall, 384/384 verified, 332 matching the record exactly**,
median 1.6×, exactly one solution over 10× (10.4×, and the trimmer could not shorten it). Verifying
all 384 through both engines takes 11 s, so the gate is cheap enough to run on every campaign.

#### The campaign — the whole corpus, both layers  ☑

**How it is budgeted, and why that changed.** The first run of this was wall-clock-budgeted, 4 s a
level, and it was thrown away: the test gates and the layer-1 tuning benches were running beside
it on the same 16 cores, so its budget bought a varying amount of work and its per-tier rates were
not reproducible. A campaign is now governed by **`--nodes`, a count of `Engine.ApplyKey` calls** —
equal work, load-independent, and the only way two layers can be compared honestly. `--budget-ms`
survives as a backstop against a level whose ticks are pathologically slow (the Grand Prix case
above). `tools/campaign.sh` runs one campaign over all 13 collections into one report;
`tools/report_stats.py` reads it, and `--diff` compares two.

**And it is a 1-in-5 stride sample, not the whole corpus, which is a scoping decision worth
stating.** All 20,914 levels at a budget worth having is about six hours per layer — the hard
collections spend the whole budget on nearly every level — and this campaign exists to *measure
per-tier rates*, not to bank solutions. Level numbers are not sorted by difficulty, so every fifth
level of every collection is an unbiased sample of each tier; `sweep.py` already used the same
trick. `STRIDE=1` runs the lot when banking solutions is the point.

**The stride matters for more than runtime, and getting it wrong once proved it.** An earlier,
aborted campaign was ordered cheapest-by-`.ghs` and stopped part-way, so its first 1,451
`Beginner-I` levels were *the cheapest 1,451* — a sample that reported a median of 10 shots per
level where the unbiased stride sample reports **16 shots against 27 moves, with 15% needing no
shot at all**. A truncated cheapest-first run is not a sample of the collection, and any number
taken from one is wrong in a direction that flatters the solver.

**150,000 `ApplyKey` calls per level, every 5th level of all 13 collections — 4,185 levels —
ordered cheapest-by-`.ghs` first.** Layer 0 is the raw-keypress portfolio (IDA* + beam); each
"+ pass" is `tools/second_pass.sh` re-attacking the levels the previous column failed, at the same
budget. The final column is **layer 2's subgoal beam followed by layer 1's macro beam**, which is
the composite that ships.

| tier | levels | layer 0 | + layer 1 pass | + layer 2 passes | + layer 3 passes | + layer 4 passes | median ratio |
|---|---:|---:|---:|---:|---:|---:|---:|
| Kids | 960 | 303 (31.6%) | 319 (33.2%) | 339 (35.3%) | 341 (35.5%) | **359 (37.4%)** | 1.6× |
| Easy | 2,118 | 84 (4.0%) | 89 (4.2%) | 94 (4.4%) | 95 (4.5%) | **104 (4.9%)** | 1.7× |
| Medium | 784 | 7 (0.9%) | 7 (0.9%) | 7 (0.9%) | 7 (0.9%) | **8 (1.0%)** | 1.6× |
| Hard | 257 | 0 | 0 | 0 | 0 | **0** | — |
| Deadly | 56 | 1 (1.8%) | 1 (1.8%) | 1 (1.8%) | 1 (1.8%) | **1 (1.8%)** | — |
| unrated | 10 | 0 | 0 | 0 | 0 | **0** | — |
| **all** | **4,185** | 395 (9.4%) | 416 (9.9%) | 441 (10.5%) | 444 (10.6%) | **472 (11.3%)** | **1.6×** |

| collection | levels | layer 0 | + layer 1 pass | + layer 2 passes | + layer 3 passes | + layer 4 passes |
|---|---:|---:|---:|---:|---:|---:|
| `Beginner-I` | 400 | 143 | 150 | 162 | 164 | **172** |
| `Beginner-II` | 276 | 82 | 86 | 90 | 90 | **95** |
| `Challenge-I` | 400 | 39 | 42 | 44 | 44 | **48** |
| `LaserTank` | 406 | 34 | 35 | 38 | 38 | **41** |
| `Challenge-III` | 400 | 31 | 34 | 36 | 36 | **38** |
| `Special-I` | 105 | 19 | 20 | 20 | 20 | **20** |
| `Challenge-II` | 400 | 17 | 18 | 18 | 19 | **22** |
| `Challenge-IV` | 400 | 14 | 15 | 15 | 15 | **16** |
| `Gary-I` | 400 | 7 | 7 | 8 | 8 | **10** |
| `Sokoban-II` | 348 | 4 | 4 | 5 | 5 | **5** |
| `Sokoban-I` | 400 | 3 | 3 | 3 | 3 | **3** |
| `Challenge-V` | 185 | 1 | 1 | 1 | 1 | **1** |
| `Gary-II` | 65 | 1 | 1 | 1 | 1 | **1** |

**The unsolved are unsolved on budget, not on structure: 3,636 of layer 0's 3,790 stopped at
`budget` (95.9%) and only 154 at a beam dead end.** No errors, no `NOTPORTED`, no crashes in
8,370 level-solves. That matters for reading the low tiers: `Hard` at 0/257 is not the search
failing to find a route, it is the search never getting near the end of one.

**Every solution verified through both engines** — `tools/verify_solutions.py` replays each `.lpb`
through the *unmodified* C oracle and the C# core with `--field --bmf` and requires WIN on both
plus byte-identical traces. **416/416 verified** for the layer-1 composite, 280 matching the `.ghs`
record exactly, median 1.6× and worst 5.0×; the separate all-layer-1 run verified 381/381 the same
way; layer 2's two passes verified **46/46**. That is 843 solver-produced recordings replayed
tick-for-tick through the 25-year-old C, with zero divergences — and none of it random: these are
long, legal, *winning* paths, which is the coverage a fuzzer cannot reach.

**Where the artifacts are** (under `build/`, gitignored, so they survive a context clear but not
a `git clean`):

| path | what |
|---|---|
| `build/reports/l0.jsonl` | the layer-0 campaign, 4,185 rows |
| `build/reports/pass2.jsonl` | the second pass over its 3,790 failures |
| `build/reports/l1.jsonl`, `l1b.jsonl` | layer 1 as a portfolio member, macro first / macro last |
| `build/reports/bench-budget.txt` | bench 1 and 2, the three-budget sweep |
| `build/reports/bench-portfolio.txt` | the share sweep both level sets |
| `build/reports/verify-composite.txt` | 416/416 through both engines |
| `build/solutions/l0/<collection>/NNNNN.lpb` | the 416 composite solutions |
| `build/reports/{deep,bench}-levels.txt` | the two bench level lists, so the benches are repeatable |
| `build/reports/l2pass.jsonl` | layer 2's pass over layer 0's 3,790 failures (+40) |
| `build/reports/pass3.jsonl` | layer 1's macro beam over what that left (+6) |
| `build/reports/l2first.jsonl`, `l2last.jsonl` | layer 2 as a portfolio member, both orderings |
| `build/solutions/l2pass/<collection>/NNNNN.lpb` | the 46 those two passes found |
| `build/reports/l3n.jsonl` | layer 3's pass over layer 0's 3,790 failures (+44) — **the shipped one** |
| `build/reports/l3r.jsonl` | the same pass restarting from the reserve instead (+43) |
| `build/reports/l3pass3.jsonl` | layer 1's macro beam over what layer 3 left (+5) |
| `build/reports/b3-{off,on}.jsonl` | the 400k budget control, 1-in-3 of the pass population |
| `build/solutions/l3n/<collection>/NNNNN.lpb` | the 49 layer 3's chain found |
| `build/reports/rank.tsv` | layer 4's instrument, 20,148 groups / 5.9M candidates (484 MB) |
| `build/reports/rank2.tsv` | the same with the 74 fed back in — the round that lost |
| `build/reports/l4-fit.txt`, `l4-fit2.txt` | the two fits, held-out top-k for each |
| `build/reports/eval-weights.txt` | **the shipped vector** (also baked into `Weights.cs`) |
| `build/reports/eval-weights2.txt` | the refit; measured worse at discovering, kept with its numbers |
| `build/reports/w-seed.txt` | the WorkDistance-equivalent control vector |
| `build/reports/l4.jsonl` | layer 4 *replacing* layer 3's pass over the 3,790 (+69, −3) |
| `build/reports/l4b.jsonl` | the same after the refit (+72, and half the discovery) |
| `build/reports/l34.jsonl` | **the shipped one** — layer 4 *after* layer 3, over its 3,746 (+30) |
| `build/reports/l34pass4.jsonl` | layer 1's macro beam over what that left (+3) |
| `build/solutions/l34/<collection>/NNNNN.lpb` | the 33 layer 4's chain adds |

Regenerate any of it with `tools/campaign.sh` / `tools/second_pass.sh`; read it with
`tools/report_stats.py` (`--diff` for two layers). The tuning benches are `tools/bench.sh`, one
labelled configuration over one of the two banked level lists — and its header repeats the warning
those lists have earned four times over: a bench picks parameters, a campaign decides what ships.
`SAMPLE=N` on `second_pass.sh` takes every Nth failure instead of all of them, which is how the
400k control (`b3-{off,on}.jsonl`, `SAMPLE=3` → 1,268 levels) was measured. Layer 4's dataset is
`tools/rankdump.py` (instrument) and `tools/fit_eval.py` (bare: the distribution; `--fit`: the
weights and a regenerated `Weights.cs`) — rebuild after a fit, the vector is compiled in.

**Where this leaves the deliverable.** The Phase 4 promise was "a solved-count-vs-budget curve,
Kids-first ordered by `.ghs` cost, not a promise of 20,914". At 150k nodes that curve reads:
**Kids 35%, Easy 4%, everything else ≈ 0**, and the binding constraint is depth, not correctness.
Layer 3 moves that to Kids 35.5% / Easy 4.5% and does not change the shape, which is itself the
result: see below. Layer 4 moves it to **Kids 37.4% / Easy 4.9%** and does not change the shape
either — but for the first time the reason it does not is *measured* rather than inferred: the
winning line is inside the beam's width 10% of the time, and the other 90% is where the curve is.

#### Layer 1 — macro-actions  ☑

**The action set.** `Goto(x, y, dir)` — drive the tank somewhere, spending as many keys as
that takes — plus `Shoot`, one space bar. A solution is an alternation of the two, and that is
*complete rather than restrictive*: any keystream is a run of direction keys, a space, a run of
direction keys, a space, …, so searching (Goto, Shoot) pairs expresses everything layer 0's raw
five-key search could. What changes is the depth. **Search depth is now the number of shots.**
In the unbiased 1-in-5 sample of `Beginner-I` the median level needs **16 shots against 27
moves**, and **15% need no shot at all** — those are solved by one Goto with nothing after it.

**The Goto is a sub-search *in* the engine, not a model of it.** The plan said "A* over tank
movement (ice slides, conveyors and tunnels resolved by a deterministic sub-search)". A grid A*
would have to re-derive `MoveTank`'s turn-costs-a-key rule, `IceMoveT`'s slide, `ConvMoveTank`,
`TranslateTunnel`'s pairing, and the fact that `AntiTank()` runs on every key-consuming tick —
i.e. it would be a second implementation of the game, free to drift from the one being ported.
Four phases have gone into there being exactly one. So `Goto` is a **breadth-first closure over
`Engine.ApplyKey` with the four direction keys, deduplicated by `StateHash`**: ice, conveyors,
tunnels, pushed blocks and anti-tank turns are "resolved" by being *executed*. It costs more per
node than a grid A* would and it cannot be wrong. The closure is the expensive part and the cap
on it (`--closure-nodes`, default 1500; `--closure-depth`, default 40 keys) is the one place
layer 1 gives up completeness — a knob with a number on it rather than a hidden constant.

**Two prunes, and the difference matters.**

- *A shot that changes nothing is dropped, and that is lossless.* If the state hash is identical
  after the space bar then nothing happened at all — not even an anti-tank turn, because
  `AntiTank()` runs inside the same key-consuming tick and any move it made would show. So the
  successor **is** the state it was fired from, which is already in this expansion's closure, and
  every continuation through it is some other (Goto, Shoot) pair of the same parent.
- *The closure cap is not lossless.* See above.

**The escape hatch.** Alongside the shot successors, the `--move-only` (default 6) closure states
that end nearest the flag are kept as pure-`Goto` successors. Without them a level needing no
shot at all and a solution longer than one closure has no successors to expand and the beam dies
at depth 1 — and 15% of `Beginner-I`'s sampled levels have `.ghs` shots = 0.

**A new heuristic, because the old one goes flat exactly here.** `FlagDistance` is a BFS over
cells the tank may *currently* enter. After a Goto closure that is guaranteed useless: the closure
only ends on states whose flag is not movement-reachable — if it were, the closure would already
have won there — so every macro successor scores `Unreachable + manhattan` and the beam is left
ranking by tank position. `Heuristic.WorkDistance` keeps a gradient by *charging* for obstacles
instead of refusing to cross them: a Dijkstra from the flag where an empty step costs 1, a brick 4
(one shot and walk through), a block 6, an anti-tank 6, a mirror 7, water 9 (a block has to go in
first), a rotary mirror 12, and only `Solid` and `Crystal` are impassable — crystal because
`CheckLLoc` case 19 returns `true` without touching the cell, so a laser goes straight through one
and never clears it. Tunnel mouths sharing an id are joined by zero-cost edges, which is the other
half of "tunnels resolved": a route in one and out another is a real route rather than the dead
end `FlagDistance` sees. Deliberately not admissible — a beam needs a gradient, not a lower bound,
and the admissible version of this (every price 1) *is* `FlagDistance`'s flat spot. Pushing a
block into water turns the cell to `Dirt` (`Engine.cs`, `MoveObj`'s `obt == 5` arm), so the number
really does drop by 8 when the level's central puzzle is solved.

**Measured three ways, and the first two measurements were misleading.** This is the part of
layer 1 worth reading, because the code was right and the *experiment* was wrong twice.

**Bench 1 — levels layer 0 failed.** 60 `Beginner-I` levels, node-governed, three budgets. Layer 1
wins at every one, and by a lot:

| nodes per level | layer 0 | layer 1 |
|---|---:|---:|
| 150,000 | 18/60 | **28/60** |
| 400,000 | 23/60 | **31/60** |
| 1,000,000 | 33/60 | **38/60** |

**Bench 2 — deep levels.** 50 `Beginner-I` levels with a `.ghs` total of 40–150 — the ones layer 1
was built for. Layer 1 does *not* win: 12 against 13 at 400k, 13 against 13 at 1M. The macro beam
alone scores 8–9 at every setting tried (macro beam 4/8/24/32/48/64, closure cap 150/400/1500/3000,
move-only 0/6/16, both closed-set policies). **Parameters are not the lever.**

**Bench 3 — the campaign, which is the one that counts.** Every 5th level of all 13 collections,
4,185 levels, 150,000 nodes each:

| portfolio | solved | vs layer 0 |
|---|---:|---|
| layer 0 alone (`--no-macro`) | **395** | — |
| macro beam first, a tenth of the budget | 381 | +21, **−35** |
| macro beam last, raw beam capped at 0.6 | 354 | +23, **−64** |

**As a portfolio member layer 1 is a net loss, either way round, and no share or ordering fixes
it.** Running it first taxes every level to help a few; running it last starves the beam of the
budget it was going to win with. Both directions have the same cause: **most solvable levels are
ones the raw beam gets easily, and every node the macro beam spends is a node taken from it.** A
portfolio has to make that bet on every level *in advance*.

**Why bench 1 lied.** It was run on levels layer 0 had already failed — a population where the raw
beam is 0% by construction, so anything the macro beam adds is free. That is a real population,
but it is not the corpus. Holding the level set fixed and sweeping the budget (bench 1's table)
shows the win is stable in budget, so it was never about how much the probe got.

**So layer 1 ships as a second pass, not as a portfolio member** — `tools/second_pass.sh`, which
reads a campaign report, takes the levels it did not solve, and re-attacks only those with
`--macro-first --no-ida --no-beam` into the same solutions directory. The first pass identifies
the population; the second attacks it; neither pays for the other. `RunMacro` is therefore **off
by default** and `--macro` turns it on.

**The composite, measured.** Layer 0's campaign solved 395 of 4,185. The second pass over its
3,790 failures — same 150,000-node budget, macro beam only — solved **21 more**, none of them at
layer 0's expense: **416 of 4,185, 9.4% → 9.9%**, 16 Kids and 5 Easy, median 1.6× the record.
Every one of the 416 is verified through both engines.

Layer 1's honest contribution is therefore *those 21 levels and the method that finds them*, not
a better portfolio. Small — and it is the shape of the result rather than its size that points at
layer 2.

**And the deep-level result is real regardless of ordering, which is the finding that matters
most.** A Goto closure costs `5 × |closure|` `ApplyKey` calls — the four direction keys from every
state it reaches, plus a shot from each — 1,500 to 7,500 where a raw-beam successor costs exactly
1. At an equal node budget the raw beam reaches roughly 300 keypresses of depth; the macro beam
reaches six to twenty shots. Macro-actions cut the *number* of decisions by an order of magnitude
and multiply the *price* of each by two or three.

**The ranking signal is weaker in macro space too, and that is the more interesting half.** Inside
a Goto, movement is *exhausted* rather than searched, so the beam never ranks a movement — it
ranks *board changes*, and `WorkDistance` is a thin signal for those. A keypress beam has a
gradient to walk down (get nearer the flag); a shot beam has to guess which of two hundred
available shots is the useful one. **That is the argument for layer 2 rather than for more tuning
here:** the reason to fire has to be *derived* — this brick is on the only path — not scored.

**A closed-set policy that looks like a bug and is not.** Both beams mark a successor visited the
moment it is *generated*, so a state the width trim discards is closed forever and no later depth
can regenerate it — the search prunes far more than its width suggests. That reads as a defect,
and layer 1 shows the symptom loudly: 33 of 60 bench-1 levels end at `macro-dead-end`, the
frontier having emptied because everything reachable was marked and then binned. The fix was
written and measured, and **it is a regression**: closing only on expansion takes the raw beam
from 33 to 27 and the macro portfolio from 36 to 30. Over-pruning wins, because the budget is
nodes and the greedy policy spends them on depth instead of on re-deriving positions it has
already rejected. Kept as `--closed generate|expand` with the measured default, and the reasoning
sits in `Search.cs` so it does not get "fixed" again.

#### Layer 2 — subgoal decomposition  ☑

**The brief was layer 1's result.** The macro beam made depth the shot count and still lost on deep
levels at every width, closure cap and share tried, because inside a Goto movement is *exhausted*
rather than searched: the beam never ranks a movement, it ranks board changes, and it ranks them
with `WorkDistance` — a number that also moves when the tank merely walks. A 1,500-state closure
offers ~1,500 shots, almost all of which change *something* and almost none of which change the
route, so they score alike and the beam keeps whichever twenty-four sorted first. The conclusion
recorded there was that the reason to fire has to be **derived**, not scored. This layer derives it.

**The derivation — and the first version of it was wrong, which is the part worth reading.**

*Version 1, from a model.* Run the priced Dijkstra `WorkDistance` already runs, keep its
predecessor chain, walk the route from the tank to the flag, and call every cell on it that costs
more than an empty step an obstacle. `--sg-trace` over 384 expansions on the 60 bench-1 levels:
**240 of them — 62% — derived no obstacle at all.** The price list said the flag was five cheap
steps away while the tank plainly could not get there. `Beginner-I` 101 ("BE the RABBIT") is the
clean case: the flag is walled in by bricks and reached through a tunnel, so a model that joins
tunnel mouths at zero cost reports a clear five-step run. A price list knows what a cell costs to
*enter*. It does not know the cell is covered by an anti-tank, that the thin ice on the way has
already been used, or which mouth a tunnel actually pairs with — and those are precisely what stops
a tank on the levels a solver fails.

*Version 2, from the engine.* The movement closure runs **first**, and the cells it stood on are
recorded. That set is an executed answer to "where can the tank get to", with death, spent thin
ice, conveyors and tunnel pairing all resolved by having happened. The Dijkstra then runs from the
flag and stops at the first of those cells it settles; what lies between the two is what is in the
way. Same idiom as layer 1's Goto: **the model proposes the ordering, the engine supplies every
claim about what the tank can do.** Expansions that derive no obstacle at all fell from
62% to 23%.

Two kinds of cell come back and the difference is the interesting half. A cell that costs something
to enter — brick, block, mirror, water — is its own subgoal: make it cheaper. A cell that costs
*nothing* to enter and is still not reached is one the tank died in, so there is nothing at the cell
to shoot; the anti-tanks aligned with it become the targets instead. That is Tutor 75, "Pass the
anti-tanks", derived rather than recognised.

**Acceptance is a board test; ranking is a position test.** A successor survives because a derived
obstacle got cheaper — not because a number went down. `WorkDistance` is still used, but only to
*order* what already survived. That separation is the whole point: clearing a brick usually leaves
the tank somewhere awkward, so the two tests disagree constantly, and layer 1 could not see past
their sum.

**Slack, and why a search that only accepts progress gets stuck.** A derived subgoal is often two
moves away, not one — rotate the mirror so the laser turns, *then* shoot the brick — and the first
of those clears nothing and shortens no route. Accepting only progress therefore dies: measured, 44
of 50 deep levels ended at `subgoal-dead-end` having spent **20,810 of 400,000 nodes**. So each
expansion keeps its best `--sg-slack` (default 4) board-changing successors as **Tier 1** nodes,
which `Cut()` takes only after every successor that actually advanced: slack fills the width that
progress left empty and never displaces it. That one change takes the deep bench from 6 to 9, puts
the whole budget to work (median 400,000 nodes), and drops the dead-ends from 44 to 4.

**Four things measured that did *not* work**, kept as flags with their numbers so they are not
re-invented, all on the 50 deep levels at 400k nodes, subgoal beam alone:

| | solved |
|---|---:|
| shipped defaults | **10** |
| `--sg-aim` (fire only from poses whose ray meets a target, mirror or anti-tank) | 2 |
| `--sg-strict` (accept only on a cleared obstacle, never on a shorter route) | 3 |
| `--sg-closed generate` (layer 0's measured default) | 6 |
| `--sg-width 12` instead of 4 | 8 |

`--sg-aim` is the instructive one: the ray *is* a superset of the shots that hit a target, and it is
not a superset of the shots worth firing. Rearranging a brick or block that is not itself a target
is how the next step becomes possible, and a shot whose only effect is to make an anti-tank turn is
sometimes the whole trick. The closed-set result is the other one: **over-pruning is not a universal
truth about this game, it is a property of the search.** Layer 0's 600-wide beam wants it (33
against 27, recorded above); layer 2's 4-wide beam is killed by it (10 against 6). Hence
`--sg-closed`, defaulting to `expand`, the opposite of `--closed`.

**Benched two ways before the campaign, and neither bench decides anything** — bench 1 is levels
layer 0 already failed, which is the population that flattered layer 1:

| | bench 1 — 60 levels, 150k | deep — 50 levels, 400k |
|---|---:|---:|
| layer 0 portfolio | 18 | 13 |
| layer 1's macro beam alone | 20 | 3 |
| layer 2's subgoal beam alone | **24** | **10** |
| portfolio with layer 1 | 28 | 12 |
| portfolio with layer 2 | **29** | 12 |

**The campaign, which is the one that counts.** Every 5th level of all 13 collections, 4,185
levels, 150,000 nodes each. As a portfolio member layer 2 is a **smaller loss than layer 1 and
still a loss**, in both orderings:

| portfolio | solved | vs layer 0 |
|---|---:|---|
| layer 0 alone (`--no-macro`) | **395** | — |
| layer 1 first, a tenth of the budget | 381 | +21, −35 |
| layer 1 last, raw beam capped at 0.6 | 354 | +23, −64 |
| layer 2 first, a tenth of the budget | 387 | +23, −31 |
| layer 2 last, raw beam capped at 0.6 | 365 | +30, −60 |

That is the same finding as layer 1's, confirmed on a second specialist: **a portfolio has to make
its bet on every level in advance, and most solvable levels are ones the raw beam gets easily.** No
share or ordering wins it, and the reason is arithmetic rather than tuning.

**As a second pass it is worth about twice layer 1.** Same 3,790 failures, same 150,000-node budget:

| pass over layer 0's 3,790 failures | adds | composite |
|---|---:|---|
| layer 1's macro beam (`--macro-first --no-ida --no-beam`) | 21 | 416 (9.9%) |
| layer 2's subgoal beam (`--subgoal --no-ida --no-beam`) | **40** | **435 (10.4%)** |
| layer 2, then layer 1 over what it left | **46** | **441 (10.5%)** |

The two overlap on 15 levels, so they are complementary rather than one superseding the other: the
6 levels layer 1's pass finds that layer 2's does not are *exactly* the 6 a third pass recovers.
**46/46 verified through both engines**, byte-identical traces, 11 matching the `.ghs` record
exactly. Two solutions are over 10× the record and the trimmer could not shorten them.

**Where the budget goes now, which is the brief for layer 3.** Layer 0's unsolved levels stop on
budget 95.9% of the time — the search never gets near the end of a route. Layer 2's stop on budget
80.8% of the time and at **`subgoal-dead-end` 19.1%**: a frontier that emptied, not a clock that ran
out. Those are two different failures and they want two different fixes. The dead-ends are what
restarts are for.

**Layer 2 does not touch the engine.** `Engine.cs` still differs from layer 0 by the single word
`partial`, and `Engine.Search.cs` is unchanged since layer 0. Everything above is in
`src/LaserTank.Solver/`: `Subgoal.cs` (new), `Heuristic.FrontierObstacles` (new), plus `Node.Tier`
and a width argument on `Cut()`.

#### Layer 3 — restarts  ☑

**The brief was layer 2's failure mode, and the first job was to price it.** Layer 0's unsolved
levels stop on budget 95.9% of the time; layer 2's stop on budget 80.8% and at `subgoal-dead-end`
19.1%. Instrumenting that second number before designing anything for it: over layer 2's pass on
layer 0's 3,790 failures, **717 levels dead-ended and they did it with a median of 84% of their node
budget unspent** — 507 of them below a third of it. That is about **90 million `ApplyKey` calls the
pass paid for and threw away**, and it is the resource this layer exists to spend.

**What a dead-end actually looks like, which killed the design that was going to be built.**
`--sg-trace` over six of them (`Beginner-I` 101, 126, 611, 1131, 1206, 1226, 1856):

| | |
|---|---|
| expansions before the frontier emptied | 53–357 |
| closure size, p50 | **8–18 states** (the cap is 400) |
| expansions deriving `added = 0` | **81–99%** |
| expansions offering no slack either | 33–64% |

Three readings, and each one cost a plausible idea:

- **The closure cap is not the problem.** A tank that is boxed in reaches eight states, not the 400
  it is allowed. So randomising *which* states a truncated closure keeps — the obvious diversifier,
  and the one that would matter in layer 1 — buys almost nothing here. It is still done, because it
  is three lines and levels like 101 do truncate at 403, but it is not the lever.
- **The search is running on slack.** With `added = 0` on nine expansions in ten, essentially every
  frontier node is a Tier 1 slack node, picked as the best `--sg-slack` by `WorkDistance`. *That*
  choice is the arbitrary one, so that is where the noise goes: `--sg-noise` jitters the ranking key
  **after `Offer()` has decided the successor advanced**, so a restart keeps a different handful and
  never an inadmissible one. Acceptance stays a board test; only the ordering is randomised.
- **The frontier emptied because the run had closed everything it saw**, which is what suggested
  re-seeding a restart from the nodes the width trim discarded rather than from the root. That is
  the idea the corpus refuted — below.

**Restarts are strictly additive, and that is the structural difference from layers 1 and 2.**
`SubgoalSearch` re-runs the beam only when it stopped at `subgoal-dead-end` *and* budget remains —
never on a `budget` stop, where there is nothing left to spend. Attempt 0 has no jitter, no
re-seed and the canonical key order, so **it is layer 2 exactly**: verified, `--sg-restarts 0`
reproduces layer 2's benches to the level (10/50 deep, 24/60 bench-1, identical stop breakdowns).
Layers 1 and 2 each had to be measured as a portfolio member and each lost, because a portfolio bets
on every level in advance. This one cannot tax the pass it runs inside, so the only question it has
to answer is what the recovered budget buys.

**What recovers a dead-end is width, not randomness — and the two directions of that are the
measurement worth keeping.** Buying width *up front* is a loss and buying the same width *after
narrow has provably failed* is a win:

| subgoal beam | deep (50 @ 400k) | bench 1 (60 @ 150k) |
|---|---:|---:|
| width 4, no restarts (layer 2) | **10** | 24 |
| width 8 from the start | 8 | 25 |
| width 16 from the start | 6 | 25 |
| restarts, no growth | 10 | 25 |
| restarts + `--sg-grow` (4→8→16…) | **11** | **28** |

That is the whole layer in one table. Narrow-and-deep is what buys layer 2 the depth it exists for,
so widening it costs; a restart is the only way to have both, because by then the narrow search has
*already reported* that it failed. `--sg-grow` doubles width and slack per restart, capped at 64/32,
and is on by default.

**The design prior that was wrong: restarting from the reserve.** Re-seeding a restart from the
nodes the width trim discarded is strictly cheaper — it skips re-deriving the shallow part — and it
loses, because it also inherits every commitment the beam had already made, and a *grown* beam wants
to re-take those decisions wider. Kept as `--sg-reuse reserve` with its numbers: **corpus 43 against
root's 44**, bench-1 27 against 28. The default is `root`.

**The campaign, which is the one that counts.** Same 3,790 failures, same 150,000-node budget, so it
is directly comparable with layer 2's pass:

| pass over layer 0's 3,790 failures | solved | `subgoal-dead-end` | lost |
|---|---:|---:|---:|
| layer 2 (`--sg-restarts 0`) | 40 | 717 | — |
| layer 3, restarting from the reserve | 43 | 37 | 0 |
| **layer 3, restarting from the root** | **44** | **9** | **0** |

**717 levels spent at least one restart, the mechanism did what it was built to do — dead-ends fell
from 717 to 9 — and it bought four levels.** That is the result, and the negative half of it is
worth more than the positive half: **converting an emptied frontier into a spent budget mostly does
not convert it into a solution.** The 19.1% dead-end figure marked where budget was being *wasted*,
not where solutions were being *missed*; the levels that dead-end are, with four exceptions, the
same levels that fail on budget. Depth remains the binding constraint, exactly as layer 0's 95.9%
said, and re-running a search that is out of its depth does not change that.

The four include `Beginner-I` 101 "BE the RABBIT" — the level whose walled-in flag and tunnel route
is the worked example that killed layer 2's modelled derivation. It now falls on the first restart.

**The chain that ships, and the composite.** Layer 3's pass replaces layer 2's in the chain, since
it *is* layer 2 plus restarts; layer 1's macro beam still runs third and still finds levels neither
subgoal pass does:

| pass | adds | composite |
|---|---:|---|
| layer 0's campaign | — | 395 (9.4%) |
| layer 3's subgoal beam with restarts | **44** | 439 (10.5%) |
| layer 1's macro beam over what that left | 5 | **444 (10.6%)** |

Against the layer-2 chain's 441 that is **+3 levels and none lost** — `Beginner-I` 101 and 1001,
`Challenge-II` 576. **49/49 verified through both engines**, byte-identical traces, 11 matching the
`.ghs` record exactly.

**Does the win grow with the budget?  Bench 1 says yes, the corpus says no, and the corpus wins.**
This is the third time the project has been caught benching on levels layer 0 already failed, so it
is recorded with both numbers. On the 60 bench-1 levels the gap widens with budget; on an unbiased
1-in-3 sample of the *pass population* (1,268 of the 3,790) at 400k it holds at roughly the relative
size it had at 150k:

| | 150k | 400k | 1M |
|---|---:|---:|---:|
| bench 1, layer 2 | 24 | 25 | 28 |
| bench 1, layer 3 | **28** | **29** | **34** |
| corpus sample, layer 2 | — | 26 | — |
| corpus sample, layer 3 | — | **28** | — |

What *does* grow with the budget, on both, is the waste layer 3 removes: layer 2's dead-ends are
19.1% of its failures at 150k and **26.7% at 400k** (332 of 1,242), because a bigger budget is
mostly a bigger amount left unspent when the frontier empties. The justification for the mechanism
strengthens with budget even where the solved count does not.

**Measured settings that did not win**, kept with their numbers so they are not re-invented:

| | deep | bench 1 |
|---|---:|---:|
| shipped defaults | 10 | **28** |
| `--sg-reuse reserve` | 11 | 27 |
| `--sg-noise 0` (growth alone, no randomisation) | 10 | 26 |
| `--sg-restarts 3` instead of 6 | — | 25 |
| `--sg-width 8` / `16` from the start, no restarts | 8 / 6 | 25 / 25 |

`--sg-noise` is the one to read carefully: worth +2 on bench 1, nothing on the deep bench, and never
separated on the corpus. It ships at 3 because it is free and the bench evidence points one way,
**not** because it is measured at corpus scale — unlike `--sg-grow` and `--sg-reuse`, which are.

**Why not NRPA or nested Monte-Carlo, which is what the plan said.** Those adapt *which action* a
playout picks. The measurement above says the subgoal search does not fail by picking the wrong
successor among many — it fails with `added = 0` on nine expansions in ten, i.e. with almost nothing
to pick from, and giving it strictly more (16× the width, six restarts, 84% more budget actually
spent) moved 4 levels of 3,790. A policy that learns to order an empty list has nothing to learn.
That is an argument from this game's measured shape rather than against the technique, and it is the
argument for layer 4 being about **depth** — a learned evaluation that makes the beam's long lines
pay off — rather than about restart policy.

**Layer 3 does not touch the engine either.** `Engine.cs` still differs from layer 0 by the single
word `partial`, and `Engine.Search.cs` is unchanged since layer 0. Everything above is
`src/LaserTank.Solver/Restart.cs` (new) plus a `seed` argument on `SubgoalBeam`, `_sgWidth` /
`_sgSlack` in place of the two options a restart grows, and a `restarts` field in the report.

#### Layer 4 — a learned evaluation  ☑

**The brief was layer 3's result, and the first job was to find out whether a
better ranking could even matter.** After restarts the subgoal pass fails on
budget 99.7% of the time, so there is one failure mode left and it is depth.
Layer 3 also measured *why* more search does not fix it: `added = 0` on nine
expansions in ten, so almost every frontier node is a Tier 1 slack node, picked
as the best `--sg-slack` by `WorkDistance`. Layer 3 randomised that pick and
bought four levels. This layer asks whether the pick is *wrong*.

**The instrument, and it runs inside the shipped expansion.** A winning `.lpb`
is a keystream, so replaying it one key at a time through `Engine.ApplyKey`
gives the exact sequence of states a perfect search would have visited. From
each of its shot boundaries — the shape of state the subgoal beam actually holds
in its frontier — the *real* `ExpandSubgoal` is run, and every candidate it
offers is recorded with its board features and with whether the winner in fact
went through it. `_collect` is a hook in `Offer()`, not a second copy of the
expansion: the same argument as `--sg-trace`, because a look-alike written to
observe the search would be free to drift from it and the distribution would
then be a fact about the look-alike. Off, it is one null check.

**652 recordings, 20,148 groups, 5.9 million candidates** — 187 human `.lpb`
plus 465 verified solver ones. The instrument found the six documented
non-winners on its own (`RankDump` returns 0 groups when the trajectory does not
win), which is the cheapest possible check that it is replaying what it thinks
it is.

**The distribution, which decided the layer:**

| | all | human recordings | solver solutions |
|---|---:|---:|---:|
| groups | 20,148 | 17,055 | 3,093 |
| candidates per group, p50 | 395 | 399 | 66 |
| **the winner's successor is in the group** | **97.6%** | 97.4% | 98.4% |
| ...and the board test calls it *slack*, not progress | **62.4%** | 68.2% | 30.6% |
| `WorkDistance` rank of it, p50 | **100** | 128 | 6 |
| ...inside the beam's width of 4 | **10.0%** | **4.1%** | 41.8% |

Three readings, and the first is the one that authorised the layer:

- **Coverage is not the constraint.** The state the winner went through is in
  the expansion's output 97.6% of the time. The closure reaches it and the
  acceptance test admits it. Everything that is lost is lost in the sort.
- **The sort loses it.** `WorkDistance` ranks it 100th of 395; the beam keeps
  four. So the shipped search is still on the winner's line one step later 10%
  of the time overall and **4.1% on the human recordings** — which are the hard
  population, and the one a solver that fails 99.7% on budget is up against.
- **The right move is usually one the board test does not call progress.**
  62.4% of the winners' successors are Tier 1 slack nodes. Layer 3 found that
  the search *runs* on slack; this says the slack pick is usually wrong. That is
  what makes it worth learning rather than jittering, and it is why the target is
  the ranking key and nothing else.

**What is fit, and what is deliberately not.** The datum is a *group*, not a
state: one expansion, every successor it offered, and which of them was right.
A cost-to-go regression over trajectory states was the obvious alternative and
is the wrong shape — every state on a winning trajectory is a good state, so a
model fit to those alone has never seen a bad one and cannot be asked to tell
them apart. Here the positives and the negatives come out of the same
expansion, which is exactly the comparison the beam makes. The loss is a
softmax within the group — the probability the evaluation puts *some* state the
winner stood on at the front of the sort — because that is the metric, not a
proxy for it: the beam keeps four of 395, so what is worth fitting is which
four, not the order of the 391 it will throw away.

Seventeen features (`Feat`), all functions of the *board* and never of the path
to it — two routes to the same state must score the same or the closed set and
the ranking disagree about what a state is, which rules out the obvious-looking
keys-spent and shots-fired. They cost one BFS and one 256-cell scan on top of
the Dijkstra the search already ran; measured, the learned pass is *cheaper* per
node than layer 3's (5.8 vs 8.0 µs on bench 1), so the `--budget-ms` backstop is
not at risk and the node budget is untouched by construction.

**Layer 4 is a ranking change and nothing else, and that is enforced rather than
intended.** `Rank()` is consulted only after `Offer()` has settled whether a
successor advanced — the same contract layer 3's jitter has — so acceptance
stays layer 2's board test and a model can reorder the frontier but can never
admit a state the shipped search refused. The check that this is true: the seed
weight vector `{work: 1, work_far: 1000, far_man: 1}` **is** `WorkDistance`
written in these features, and `--sg-eval learned` with it reproduces layer 3
exactly — identical keystreams, node counts and stop reasons on all 50
deep-bench levels. `far_man` exists only so that equivalence can be exact.

**Held out by recording, never by group** — groups from one recording share a
board and most of a position, so a group-wise split reports a training score and
calls it held out. Groups are also weighted by 1/(groups from the same
recording), because two quirk packs supply 16,599 of the 20,148 and without the
weight the fit is a fit to `Tutor-with-Playbacks`.

| held out, top-4 | `WorkDistance` | learned |
|---|---:|---:|
| all (121 recordings) | 13.6% | **18.2%** |
| human (33) | 5.7% | **10.4%** |
| solver (88) | 45.6% | **49.5%** |

**The benches barely move, and this time they are the ones that are wrong.**
Deep 10 → 11 at 400k, bench 1 28 → 29 at 150k. Both banked lists are levels
layer 0 failed — the population that flattered layers 1 and 2 — and here the
same bias runs the other way, because a re-ranking pays off over depth and these
lists are scored one level at a time.

**The campaign, which is the one that counts.** Same 3,790 layer-0 failures,
same 150,000-node budget, so it is directly comparable with layer 3's pass:

| pass over layer 0's 3,790 failures | solved | `subgoal-dead-end` |
|---|---:|---:|
| layer 3 (`--subgoal`) | 44 | 9 |
| **layer 4 (`--subgoal --sg-eval learned`)** | **69** | 6 |

**69 against 44 — and it is not a superset.** Three of layer 3's are lost, which
is the structural difference from layer 3 and worth stating plainly: a *restart*
is additive by construction because it only spends budget already forfeit, and a
*re-ranking* is not additive at all — it is a different search from the first
expansion. So layer 4 does not replace layer 3's pass, it follows it:

| pass | adds | composite |
|---|---:|---|
| layer 0's campaign | — | 395 (9.4%) |
| layer 3's subgoal beam with restarts | 44 | 439 (10.5%) |
| **layer 4's learned ranking over what that left** | **30** | **469 (11.2%)** |
| layer 1's macro beam over what that left | 3 | **472 (11.3%)** |

Against layer 3's chain that is **+28 and none lost**, at the cost of one extra
pass over the failures. Replacing layer 3 instead also reaches 469 in two
passes, so the extra pass buys exactly the three levels a different ranking
gives up.

| tier | levels | layer 3 chain | layer 4 chain |
|---|---:|---:|---:|
| Kids | 960 | 341 (35.5%) | **359 (37.4%)** |
| Easy | 2,118 | 95 (4.5%) | **104 (4.9%)** |
| Medium | 784 | 7 (0.9%) | **8 (1.0%)** |
| Hard | 257 | 0 | **0** |
| Deadly | 56 | 1 | **1** |
| **all** | **4,185** | 444 (10.6%) | **472 (11.3%)** |

**107/107 verified through both engines** — the 74 of the replacement chain and
the 33 of the shipped one — byte-identical traces, WIN on both, median 2.1× the
`.ghs` record. One `Special-I` solution is 0.2× the record, i.e. five times
*shorter* than the recorded best.

**Feeding the newly solved levels back in makes it worse, and the way it is
worse is the interesting part.** That was the other half of the plan, so it was
run: re-dump with the 74 new solutions included (726 trajectories), refit, and
re-measure the pass over the same 3,790.

| | pass | levels whose own solution was in the training set | levels it had never seen |
|---|---:|---:|---:|
| round 1 — fit on 187 human + 465 solver | 69 | 41 of 49 | **28** |
| round 2 — fit on 187 human + 539 solver | 72 | 58 of 77 | **14** |

The pass goes up by 3 and the *discovery* halves. Round 2 recalls more of what
it was shown and generalises less, and both benches move the other way (11 → 10,
29 → 28). **A self-trained ranker learns the levels it was fed rather than the
game**, and the total is a worse summary of it than the split is. Round 1 ships;
the refit is kept as `build/reports/eval-weights2.txt` with its numbers.

Note what that table also says about the leakage in the headline: 41 of layer
4's 69 are levels whose winning keystream was in the training set — they are
mostly the ones layer 3 and layer 1's passes had already banked — so **28 is the
size of the generalisation**, and the composite gains 28 exactly.

**Why linear, and where the ceiling is.** A 17-feature linear model over a
softmax-in-group loss converges in seconds and moves held-out top-4 from 13.6%
to 18.2%. It is still wrong four times in five: even after fitting, the winner's
successor is inside the width of 4 only 10.4% of the time on held-out human
recordings. The instrument says the headroom is not coverage (97.6%) and not the
acceptance test — it is entirely in the sort, and 80% of it is still on the
table. That is the brief for anything above this layer, and it is a measurement
rather than a hope.

**Hints as landmarks was not built**, and the arithmetic that says not to is in
the layer-4 plan above: 175 of 20,914 hints are recipe-grade. That is a tail
tool, and this layer's tail is 3,700 levels wide.

**Layer 4 does not touch the engine either.** `Engine.cs` still differs from
layer 0 by the single word `partial`, and `Engine.Search.cs` is unchanged since
layer 0 — four layers now. Everything above is
`src/LaserTank.Solver/Learn.cs` (new: `Feat`, `Eval`, `RankRow`, `Rank()`,
`RankDump`), `Weights.cs` (generated), three published fields on `Heuristic`,
and a `Rank()` call in place of two `WorkDistance` calls in `Subgoal.cs`.

### Phase 4 addendum — the interactive driver  ☑

Everything above measures the solver; this is how you *use* it on a level you
care about. `build/lasertank-solve.exe FILE.lvl [--from N] [--to N]` — a bare
`.lvl` and nothing else required — walks the collection in level-number order
and stays on each level until it falls or you press a key.

    build/lasertank-solve.exe data/levels/Beginner-I.lvl --from 12 --to 40

**It is the portfolio the campaign could not afford.** A round runs all four
searchers *at once, one per thread* — layer 0's beam (with IDA* on round 0,
where a probe is cheap), layer 3's subgoal beam, layer 4's learned ranking of
it, layer 1's macro beam — and the first win cancels the rest. In a campaign
that trade is a loss, because every node a specialist spends is a node taken
from the raw beam (hence `second_pass.sh`); here a specialist spends a *core*,
and one level at a time means the cores are there. If nobody wins, the node
budget quadruples and the round repeats — 400k on round 0, about a second;
400M on round 5, about an hour — and rounds also widen what only widening
helps: the raw beam doubles its width, since a `beam-dead-end` stop has nothing
to do with a bigger budget, and the subgoal beam gets six more restarts.

**The two-engine gate is not optional here, it is the write path.** A win goes
to a scratch `.lpb`, then to `tools/verify_solutions.py` (which grew a
`--levels` argument so one candidate can be checked against a named `.lvl`
rather than by directory name), and is moved into the output directory only if
the frozen C oracle and the C# core both report WIN with byte-identical traces.
A solution that fails is deleted and the search carries on — loudly, because
after Phase 3 that can only mean an engine divergence. Missing engines or no
python is a startup error, not a discovery made six levels in.

`Auto.cs` is the whole of it, plus a `CancelFlag` on `SolveOptions` that
`OutOfBudget` reads — the keypress ends a search at the next node rather than
at the end of a stage, and the searchers publish their node counts back through
it for the live line. `Engine.Search.cs` is still unchanged since layer 0.

### Phase 5 — Presentation & features  ☐

**This is the first phase where the deliverable is the game rather than a measurement, and the
discipline that got the project here still applies: the presentation layer must not become a second
implementation of the rules.** Everything in `LaserTank.Core` stays untouched — a Godot node reads
`Game.PF` and `Game.BMF` and draws them; it never decides anything. That is the same contract the
solver kept for four layers (`Engine.Search.cs` unchanged since layer 0), and it is why the fidelity
gates keep working while this phase is built.

**The head start, which is larger than it looks.** The renderer's input is already computed and
already ported. `BuildBMField` maintains `Game.BMF[x][y]` — the *bitmap number* per cell, not the
object id — `Animate()` cycles it, and `Obj.GetOBM()` is the object→bitmap table
(`LTANK2.C:77`). So drawing a frame is: for each cell, look up `BMF`, index the sprite atlas, blit.
Hazard #2 is what makes this safe (no bitmap ever feeds a decision) and hazard #1 is what makes it
dangerous (`UpDateLaserBounce` is a *paint* function that mutates game state): **the core already
calls it inside the tick — the renderer must not call it, skip it, or reimplement it.**

#### The steps, each with an exit criterion

**Step 0 — the Godot project, and the board on screen.** A `LaserTank.Game` Godot project
referencing `LaserTank.Core` as a plain library (it has no Godot dependency, which is what makes
this a reference rather than a rewrite). Load a level and draw the 16×16 board.

**The atlas geometry, decoded and verified against all three packs**, because getting it wrong is a
silent off-by-one: the sheet is **always 320×192 — a 10×6 grid of 32×32 sprites** — and `BMA[]` is
filled row-major from **i = 1**, ten per row (`GFXInit`, `LTANK2.C:782`). So sprite index `i` is
atlas cell `((i-1) % 10, (i-1) / 10)`. `MaxBitMaps` is 58 (`LTANK.H:92`) and the highest index the
object table yields is 57, so the last row is partly unused.

**Read `Game.BMF`, never re-derive it from `PF`.** `BuildBMField` is not simply `GetOBM(PF)`: a
tunnel is 55, the tank's own cell is 1 *and its `PF` is zeroed*, and `Animate()` then cycles `BMF`
for animated objects. Every one of those is a place a re-derivation drifts.
*Exit:* a headless test over all 2,347 corpus levels — every `BMF` value at load is in 1..57 and
maps to a cell inside the 10×6 grid. Cheap, no rendering, runs beside the other gates.

**Step 1 — the tick loop, and the gate that matters.** A fixed 20 Hz tick (`GameDelay = 50` ms,
`LTANK.H:96`) decoupled from rendering, driving `Engine.Tick()`; keyboard input appended to
`RecBuffer` exactly as `AddKBuff` does; visuals interpolated between ticks. **Never drive logic
from `_process`** (hazard #10).
*Exit — and this is Phase 5's real gate:* play a level in Godot, win it, save the keystream with
the existing `LevelFile.WritePlayback`, and **that `.lpb` must replay byte-identically through the
unmodified C oracle** (`tools/verify_solutions.py` already does exactly this, unchanged). A human
playthrough that survives the oracle is the same proof the solver's 472 solutions gave.

**Step 2 — graphics packs and zoom.** `.ltg` is a 324-byte `TLTGREC` header (`Name[40]`,
`Author[30]`, `Info[245]`, `ID[5]` = `"LTG1"`, `MaskOffset` DWORD) followed by two ordinary Windows
BMPs: the game bitmap from the end of the header to `MaskOffset`, the mask from there to EOF
(`LoadLTG`, `LTANK2.C:688`). Verified against the three shipped packs: the game bitmap is 320×192
at 24bpp (`Warcraft_II` is 8bpp) and the mask is 320×192 at **1bpp**. The original blits
mask-`SRCAND` then bitmap-`SRCPAINT` — 1-bit transparency — so the load step is "fold the mask into
an alpha channel" and the draw step is then an ordinary textured quad.

Zoom is 24/32/40 px (`SetGameSize`, `LTANK2.C:1729`), and the original implements it by
`StretchBlt`-ing the whole 320×192 sheet up or down at load. **Do not copy that.** Godot should
keep the atlas at native 32×32 and scale at draw time, which is the same picture without the
resample — the sprite size is a presentation choice and no logic reads it.
*Exit:* all three packs in `data/graphics/` load and render; switching zoom changes nothing but
pixels. Note hazard #11 lives in this function — `if (GFXOn) GFXKill;` is missing its parens and
must stay missing.

**Step 3 — sound.** The 16 WAVs in `original/src/Sounds/`. The tick already computes *which* sound
fires: `FireLaser`'s `sf` argument is the sound id and is load-bearing for logic
(`laser.Good = (sf == 2)`), so sound ids are read from the engine, never re-derived.
*Exit:* the gates stay green — i.e. adding audio changed no trace.

**Step 4 — the game around the game.** Level picker, high scores (`.hs`/`.ghs`, already read by
`LevelFile`), undo, and record/playback UI. Undo needs less than it looks: `UpdateUndo` /
`ResetUndoBuffer` and the whole `UndoBuffer` are already ported and maintained — only `UndoStep`,
the reader, is missing, because nothing headless ever called it.
*Exit:* a recorded game round-trips — record in Godot, replay in Godot, replay in the oracle, all
three agree.

**Step 5 — the level editor.** This is where `MouseOperation`, the one unported function, finally
gets written; it is a UI entry point rather than game logic, which is why it was left throwing.
*Exit:* an edited level saves as a `.lvl` the 2010 binary opens, and constraint 2 (community
formats stay readable *and* writable) is demonstrated rather than asserted.

**Step 6 — i18n.** `language.dat` via `LANGUAGE.C`.

#### What to be careful about

- **Do not "fix" anything on the way past.** Hazards #9 and #11 are both real bugs in the original
  that must survive, and #11 is inside `SetGameSize` — a function this phase has to touch.
- **The 20 Hz tick is not a rendering rate.** Interpolate sprites between ticks; a 144 Hz display
  must not consume 144 keys a second.
- **`.lpb` compatibility is bidirectional.** The 2010 binary must be able to play what Godot
  records. `LevelFile.WritePlayback` already writes the real 66-byte header format, and the
  solver's 472 recordings are the existing evidence that it round-trips.
- **Godot 4.7.2 Mono** is installed but has no `godot` alias (needs admin) — call the `.exe` by
  path; see *Environment notes*.

---

## Source map

| File | Lines | Role |
|---|---|---|
| `original/src/LTANK.C` | 1572 | Win32 window proc. **`WM_TIMER` at `:579` is the real game loop.** |
| `original/src/LTANK2.C` | 1834 | Game logic *and* GDI rendering, interleaved |
| `original/src/LTANK_D.C` | 1319 | Dialogs (level picker, high scores, playback, graphics) |
| `original/src/LANGUAGE.C` | 279 | i18n from `language.dat` |
| `original/src/lt_sfx.c` | 62 | WAV playback |
| `original/src/LTANK.H` | — | Structs, object-ID table, tunnel macros |

### Tick order (`LTANK.C:579`) — this *is* the spec
1. `Animate()` every `ani_delay`(=4) ticks
2. `MoveLaser()` if `Game.Tank.Firing`
3. Playback pacing / `PBHold`
4. Consume **one** key from `RecBuffer` if `!(Firing || ConvMoving || SlideO.s || SlideT.s || PBHold)`
   — **and, inside that same `if`, `AntiTank()`.** Anti-tanks act only on ticks where a key was
   consumed; on a tick with no key they do not play. (Earlier revisions of this list showed
   `AntiTank()` as an unconditional step 5, which is wrong and matters for the solver: it is why a
   "wait" is not free. `Engine.cs:1309` and `oracle/driver.c` both have it right.)
5. `IceMoveO()` then `IceMoveT()`
6. `ConvMoving = FALSE`, then conveyor / flag / water check on tank's cell
7. Mouse buffer
8. Repaint tank

**The wait.** The switch at `LTANK.C:616` has no `default`, and `Game.RecP++` runs regardless — so
any recorded byte outside {32, 37, 38, 39, 40} is a legal one-tick **wait** that still gives the
anti-tanks their turn, and `AddKBuff` (`LTANK2.C:256`) filters nothing, so a human pressing any
other key records one. **No human ever did:** all 54,162 bytes of all 187 `.lpb` are those five
keys, zero exceptions. The "wait" the tutor hints describe (level 4 *"Move up, wait"*, level 14
*"Wait 11 seconds"*) is a different thing — it is *free* time while the world is non-quiescent
(riding a conveyor, sliding on ice), during which no key is consumed and no byte is needed. So the
solver's action set is the five keys, matching the recordings. Note that neither engine's `--keys`
parser can express a wait anyway (both accept only `u d l r f`); if that ever changes, both change
together and the corpus gets re-run.

---

## Data formats (decoded and verified)

**`.lvl`** — flat array of 576-byte records, no header:
```
offset  size  field
0       256   playfield  char[16][16]   (PF[x][y], x = column)
256      31   name
287     256   hint
543      31   author
574       2   difficulty  u16  (1,2,4,8,16 = rated 1-5; 0 = unrated)
```
`data/levels/LaserTank.lvl` = **exactly 2030 levels**; 13 collections total, **20,914 levels**.

**`.ghs` / `.hs`** — flat array of 10-byte records, indexed by `level - 1`:
```
0  2  moves  u16
2  2  shots  u16
4  6  initials
```
**Every entry in all 13 `.ghs` files is non-zero** → every level is known-solvable, with
best-known move/shot targets. Ranking is lexicographic: moves first, then shots.

**`.lpb`** — 66-byte header then raw VK bytes:
```
0   31  level name
31  31  author
62   2  level number  u16
64   2  data size     u16
66   ..  keystream
```
Key codes: `37`=Left `38`=Up `39`=Right `40`=Down `32`=Fire.

**Objects** — IDs 0–25, table at top of `LTANK.H`. Tunnels are encoded out-of-band as
`0x40 | (id << 1) | waitbit`; see the `GetTunnelID` / `ISTunnel` macros.

---

## Quirk hazards — every one is load-bearing

**The rule these generalise to**, learned twice in Phase 2 and worth holding while reading any of
them: *in this program a function's name tells you nothing about whether it mutates state.*
`UpDateTank()` clears `TankDirty` (`LTANK2.C:537`) and `Animate()` ends by setting it
(`LTANK2.C:1161`) — both were nearly missed because they are named like paint calls.

1. **Rendering mutates game state.** `UpDateLaserBounce()` (`LTANK2.C:565`) is a *paint* function
   that sets `LaserBounceOnIce`, making `MoveLaser` `goto LaserMoveJump` and take a second step in
   the same tick (`LTANK2.C:1631`). Stub drawing naively → laser-on-sliding-mirror behaviour changes.
   **Confirmed live in Phase 2:** `Tutor-with-Playbacks` levels 93 and 94 are the only two
   recordings in the corpus that reach it (tick 527 and tick 206). Level 93's own hint names the
   mechanism — *"deflected by three mirrors at K8 (**sliding mirror**), K10, N10, and N8"*. To
   re-check it after any change to the laser or ice code, swap `LaserBounceOnIce = true` in
   `UpDateLaserBounce` for a throw and replay the corpus: it must fire exactly twice.
2. ~~**Animation frame is game state.**~~ **Corrected in Phase 1 — animation is cosmetic.**
   `Animate()` writes `Game.BMF[][]` and `MoveObj:1293` reads `bm = Game.BMF[x][y]` to carry the
   sprite along (the "Tere6 Bug" fix, `original/src/Bugs.txt` 02-25-02), but *every* read of `BMF`
   in the whole program is either a paint call or that sprite carry — **no bitmap ever feeds a
   decision.** Verified by exhaustive grep; see `oracle/README.md`.
   Consequences: `AniLevel`/`AniCount` need not be simulated for logic equivalence, and — more
   importantly — a `.lpb` replays identically regardless of which animation phase the game happened
   to be in when the level loaded, which would otherwise make every replay phase-dependent.
   The tutor readme's "the tank will temporarily disappear" is a rendering artifact.
   Still worth tracing in Phase 2: a BMF divergence is a cheap tripwire for a transliteration slip.
3. **`wasIce` is a hidden return channel** from `CheckLoc()` (`LTANK2.C:1278`), read by three callers.
4. **Tunnel low bit is a flag.** `Game.PF2[x][y] |= 1` marks "waiting to transport";
   `Game.PF[x][y] & 0xFE` strips it.
5. **Anti-tank fire order is right → left → down → up**, and only the *first* match fires per call
   (`LTANK2.C:1655`). Tutor level 42 is literally "Inverse A-T's shooting order."
6. **Slide stack caps at 15**, silently (`if (SlideMem.count < MAX_TICEMEM-1)`), and `IceMoveO`
   mutates the stack while iterating it top-down (`LTANK2.C:1390`).
7. **`MoveObj` decrements `ScoreMove` and `UndoP`** in the tunnel path — the "Bartok Bug"
   workaround (`LTANK2.C:1310`).
8. `SendMessage(WM_Dead)` vs `PostMessage(WM_Dead)` — immediate vs deferred death, deliberately
   changed in 4.0.6. Ordering is observable.
9. `BuildBMField()` (`LTANK2.C:843`) leaves `i` uninitialized on one branch; currently unreachable
   because of the 2003 sanitization above it, but do not "fix" it silently.
10. Godot must run logic on a **fixed 20 Hz tick decoupled from rendering**, interpolating visuals.
    Never drive logic from `_process`.
11. **`LTANK2.C:1738` reads `if (GFXOn) GFXKill;`** — a missing `()`, so the call never happens.
    A real bug in the original, in `SetGameSize`, and cosmetic. Same species as #9: **do not
    "fix" it.** It is Phase 5 territory, which is the phase most likely to want to.

---

## Repo layout

```
original/   the frozen 25-year-old artifact — read-only
  src/        2007 source distribution, verbatim
  bin/        shipped 2010 lasertank.exe + LTUDU data updater
data/       game content = the regression corpus
  levels/     13 collections, 20,914 levels, all with .ghs targets
  quirks/     10 tutorial/trick packs, 317 levels, 187 .lpb recordings
  graphics/   .ltg packs      meta/  changelogs & name indexes
oracle/     the C reference oracle — see oracle/README.md
  stub/       minimal <windows.h> that shadows the real one
  win32_stub.c  real memory/files/messages, no-op GDI
  driver.c    LTANK.C globals + window proc + the WM_TIMER tick loop + tracing
  build.sh    gcc -x c -I stub -I original/src
src/        the C# port         build.sh -> build/lasertank-core.exe + lasertank-solve.exe
  LaserTank.Core/  Objects.cs GameState.cs LevelFile.cs Engine.cs  (no Godot here)
                   Engine.Search.cs — snapshot/restore, ApplyKey, StateHash (Phase 4)
  LaserTank.Cli/   Program.cs TraceWriter.cs — the oracle's CLI, the oracle's trace
  LaserTank.Solver/ Search.cs Heuristic.cs Trim.cs Report.cs Program.cs — the batch
                   solver; writes .lpb, never trusted without verify_solutions.py
                   Auto.cs — the interactive driver: `lasertank-solve FILE.lvl`,
                   every searcher at once, budget x4 per round, until you press
                   a key; keeps only what verify_solutions.py has passed
                   Macro.cs — layer 1: Goto (a movement closure over ApplyKey)
                   + Shoot, so search depth is shots rather than keypresses
                   Subgoal.cs — layer 2: the obstacles between the closure and
                   the flag, derived; a successor is kept because it cleared one
                   Restart.cs — layer 3: re-run the subgoal beam when it dies of
                   an empty frontier with budget in hand, growing width each time
                   Learn.cs, Weights.cs — layer 4: board features, the learned
                   evaluation that orders the beam, and the instrument that
                   dumps one ranking group per shot of a winning recording
build/      C# output (gitignored)      LaserTank.slnx  the solution
tools/
  replay_all.py     replay every .lpb; green/red gate (expected outcomes + .ghs targets)
                      --traces DIR [--field] [--bmf] writes one trace per recording
  difftrace.py      compare two traces, or two directories of them: first diverging
                      tick, first field, per-cell playfield diff   <- the Phase 2 gate
  test_difftrace.py self-test for difftrace.py; run it before trusting a verdict
  engines.py        both engines on one input + compare -> a Div or None.  Shared
                      plumbing for the two below; the comparison is difftrace's
  sweep.py          one fixed keystream over every level of a .lvl, both engines.
                      Bare, it is the empty-keystream sweep: 2,347 levels
  fuzz.py           random keystreams, both engines, and **shrink** a divergence
                      to level + shortest keystream   <- the Phase 3 gate
  test_fuzz.py      self-test for fuzz.py: injects known faults into the C# core,
                      rebuilds, and fails unless the fuzzer finds and shrinks them
  verify_solutions.py replay every solver .lpb through BOTH engines: WIN on each,
                      byte-identical traces, and the ratio to the .ghs record.
                      --levels names the .lvl instead of finding it by directory
                      name, which is how Auto.cs gates one candidate at a time
  campaign.sh       one solver campaign over all 13 collections into one report.
                      Node-governed, not wall-clock: two layers have to be
                      comparable and a contended machine makes seconds lie.
                      STRIDE=N samples every Nth level of every collection
  second_pass.sh    re-attack a campaign's unsolved levels with a different
                      searcher, into the same solutions dir  <- where layer 1 ships
  report_stats.py   read a campaign .jsonl: per-tier solved rates, per-collection
                      rates, stop-reason breakdown.  --diff compares two layers
  rankdump.py       layer 4's instrument: replay every winning .lpb and dump the
                      group of successors the shipped subgoal expansion offered
                      at each shot boundary, labelled with what the winner did
  fit_eval.py       read that dump.  Bare, it reports the distribution (is the
                      right successor even in the group, and where does
                      WorkDistance rank it); --fit fits the evaluation and
                      regenerates Weights.cs
  bump_rate.py      classify consumed keys; bumps = desync signature
  dump_level.py     print a .lvl level as ASCII with its hint
  unpack_lpb_txt.py decode a Text-Converter .txt wrapper back to .lpb
```

See `README.md` and `data/SOURCES.md` for provenance.

## Test corpus

**A trap for the day a `.lpb` will not replay:** `data/levels/LaserTank-2016-snapshot.zip` is a
third vintage of the flagship collection, **59 of its 2,030 levels differing** from the current
one. A recording made against one vintage cannot replay against another, so before debugging the
engine, check the level bytes.

**Tier 1 — `data/quirks/`, 317 quirk-focused levels, 187 recorded playbacks.**
Upstream deliberately withholds `.lpb` for the main collections; these help-section packs are the
only recorded human solutions in existence.

| Directory | Levels | LPB | Note |
|---|---:|---:|---|
| `tutor-with-playbacks` | 112 | 112 | + bundled `.ghs` — the only pack where recorded counts can be checked against a target |
| `tutor` | 92 | 0 | **the quirk specification** — each hint documents its trick |
| `rotary-mirrors` | 39 | 39 | 6 of these do not reach the flag — see above |
| `tricks` | 26 | 0 | |
| `pono-trick` | 18 | 20 | more LPBs than levels (alternate solutions) |
| `game-objects` | 16 | 16 | one level per object — **best first target for the oracle** |
| `4triang`, `telek-1`, `l40`, `inchworm` | 14 | 0 | |

**Tier 2 — `data/levels/`, 20,914 levels across 13 collections**, every one with a non-zero `.ghs`
entry. No keystreams, but a solvability guarantee and a (moves, shots) target for each. This is the
fuzzing surface for Phase 3.

## The six non-winning recordings

All six are in `rotary-mirrors`, and five of the six are the only files in that pack whose `.lpb`
author field reads `Ihab` rather than `Ihab-Ihab`. They consume their entire keystream and stop
short of the flag. `tools/replay_all.py` pins each one's expected outcome, asserting the exact
numbers wherever a level hint documents them. Evidence that this is the recordings, not the engine:

| file | level | replay | corroboration |
|---|---|---|---|
| `_0036` | 36 `noor II` | 621 keys, 419 shots | hint: *"I invite you to complete the solution … it is stopped at step 621 (after 419 shots)"* — **exact match** |
| `_0009` | 9 `rotary mirrors 4-c1` | 39 moves | hint: *"blocked at 39 steps"* — **exact match**; level 10 is the same puzzle (`4-c2`) and its recording wins |
| `_0021` | 21 `rotary mirrors 5-g` | 148 moves, 257 shots, then dies | hint: *"it has a solution : 148/257 or better"* — **exact match**; see below |
| `_0011`, `_0013`, `_0017` | 11, 13, 17 | — | no hint text; inferred from the zero-bump result below |

**The zero-bump result.** A key that produces neither a move, a turn, nor a shot means the tank
walked into something solid. Across all 187 replays — **54,162 keypresses — not one bump.** A
desynced engine puts the tank in the wrong place and blocked moves pile up immediately; instead
every single recorded keystroke in the corpus does exactly what a keystroke should do, including
throughout all five non-winning recordings. `tools/bump_rate.py` computes this.

**The level-21 confirmation.** `rotary-mirrors` ships level 21's playback as `_0021.txt`, a
Text-Converter base64 wrapper rather than a `.lpb`. Decoded with `tools/unpack_lpb_txt.py` (594 B,
528 keys, author `Ihab`) and added to the corpus — the only derived file under `data/`, documented
in `data/SOURCES.md` and regenerable from the `.txt` beside it.

Level 21's hint says *"it has a solution : 148/257 or better."* Replaying it gives **148 moves /
257 shots** and leaves the tank one cell above the flag, facing it — then its final two keys turn
the tank around and drive it into water. Replace that trailing `uu` with `dd` and the oracle **wins
at exactly 148/257**, the documented optimum. So the engine reproduces a known-good solution
precisely, and the distributed recording's tail is simply wrong. This is one of the few independent
checks on absolute scoring outside the `Tutor-with-Playbacks` `.ghs`, which is why it earns its
place despite not winning.

## Environment notes

- Two Pythons, both fine: `python` = 3.12.7 (miniforge), `python3` = 3.14.7 (installed 2026-09-05,
  which retired the old Microsoft Store shim). Everything in `tools/` is stdlib-only and verified on
  both — keep it that way so either alias works.
- C toolchain: MinGW-w64 (WinLibs gcc 16.1, UCRT), `winget install BrechtSanders.WinLibs.POSIX.UCRT`.
  Not on `PATH` globally; `oracle/build.sh` finds it under `~/AppData/Local/Microsoft/Winget/Packages/`.
- **.NET SDK 10.0.400**, `winget install Microsoft.DotNet.SDK.10`, at
  `C:\Program Files\dotnet`. Same story: not on the shell's `PATH` until it restarts, so
  `src/build.sh` finds it. Only the .NET 10 runtime is present, hence `RollForward=LatestMajor`
  on the `net8.0` CLI.
- **Godot 4.7.2 (.NET/Mono build)**, `winget install GodotEngine.GodotEngine.Mono`, unpacked to
  `~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_*/Godot_v4.7.2-stable_mono_win64/`.
  The `godot` alias needs admin to be created, so call the `.exe` by path. Nothing in Phase 2
  needs it — the core and its CLI are plain .NET — it is here for Phase 5.
- **Trap in the oracle's own usage text:** it advertises `--keys` as accepting "raw decimal VK
  codes separated by commas", but `driver.c` only parses the characters `u d l r f` and silently
  skips everything else. `--keys 38,38,32` therefore yields an *empty* keystream and an idle run
  that looks like it worked. The C# CLI matches this behaviour deliberately (same parser, same
  skipping) — if you fix one, fix both and re-run the corpus.
- **`bash` invoked from Python is WSL's `System32\bash.exe`**, not Git Bash — different
  filesystem, no gcc, no dotnet, and it fails with an unreadable `execvpe` error.
  `tools/engines.py`'s `find_bash()` skips System32 and falls back to the Git for Windows paths;
  `$LT_BASH` overrides. Any new tool that shells out should use it rather than bare `bash`.
- **Rewriting a source file from Python in text mode rewrites every line ending.** `src/` is LF,
  Python's text mode makes it CRLF, and `core.autocrlf=true` then makes `git diff` show *nothing*
  while every line on disk has changed. Patch and restore in **bytes**, or pass `newline=''`.
  `tools/test_fuzz.py` does, and a green self-test leaves the tree byte-clean.
- **The quirk packs mix `.lvl` and `.LVL`**, and the four that ship uppercase are the four biggest.
  A `glob("*.lvl")` is case-insensitive on Windows and silently drops them on Linux — it already
  cost one campaign four packs with no warning. Match on `suffix.lower()`, as `replay_all.py`,
  `sweep.py` and `verify_solutions.py` all now do.
- laser-tank.com is behind Cloudflare: `WebFetch` returns 403. Use `curl` with a browser
  User-Agent. The site is a frameset — real content is in `menu.html`, `help.html`, `levels.html`.
- Original build was lcc-win32 (`original/src/_How to compile LTank.txt`, `LTank.prj`). Its dependencies are
  shallow; MinGW/clang should work.

## Open questions

- **Which binary is the behavioural reference?** `original/bin/lasertank.exe` is dated 2010;
  `original/src/Setups/Files/lasertank.exe` is the 2007 build matching this source. Both are
  UPX-packed and 148,512 bytes but differ across ~95% of their bytes, so the version can't be read
  off without unpacking. `Bugs.txt` stops at 4.1.2 (2005), so the 2010 build may contain changes
  we have no source for. Resolve in Phase 1 by trace-diffing the oracle against both.
  The Tutor readme warns: *"made/verified using LaserTank.exe Ver 4.1. The use of earlier versions
  may cause different results."*

## Cross-references — do not trust for quirk fidelity

- `github.com/tobiasvl/lasertank` — mirror of this same source.
- `github.com/h4tr3d/laser-tank` — SDL2/C++ port, but descends from a KolibriOS *reimplementation*.
- `lasertankpedia.zdobywca.com`, `lasertanksolutions.blogspot.com` — community game-data references.

The oracle is the only authority.

---

## Session log

The reasoning behind each phase lives in that phase's section above; this is the changelog, kept
short on purpose. Where a session's finding is still load-bearing it has been moved to where it
belongs — traps to *Environment notes*, engine hazards to *Quirk hazards*, measured negatives to
the layer that measured them — so that nothing here needs reading to work on the project.

**2026-09-05, sessions 1-2 — Phase 1.** Read the source, decoded `.lvl`/`.ghs`/`.lpb` against the
real files, catalogued the quirk hazards, settled the three-engine architecture, reorganised the
repo. Then built the oracle on the load-bearing decision that made everything after it possible:
**stub only the Win32 API and compile `LTANK2.C` verbatim.** Measuring the external surface first
(`nm -u`, 49 calls and ~12 globals) is what turned it into a bounded job. Result: all 187
recordings replay, 181 win, 54,162 keypresses, 0 bumps, and 112/112 `Tutor-with-Playbacks` match
their bundled `.ghs`. Hazard #2 was *corrected* here rather than confirmed — animation is cosmetic
— which is what makes replays independent of the animation phase at load.

**2026-09-05, sessions 3-7 — Phase 2, the transliteration.** Six steps, each gated on the corpus:
the differ (`difftrace.py`, self-tested 29 cases, fault-injected 7 ways), then the core stood up,
then `CheckLoc`/`MoveObj`, then `MoveTank`/`AntiTank`, then the whole laser subsystem, then the ice
and conveyor movers. Ended **byte-identical to the oracle on all 187 recordings** with
`--field --bmf`. Two decisions earned their keep repeatedly: unported functions **throw** rather
than no-op (a stub would have hidden the `FireLaser(..., S_Fire)` bug for three more steps), and
the exit criterion was always the corpus rather than a spot check.

**2026-09-05, session 8 — Phase 3, the fuzzer.** `engines.py`, `sweep.py`, `fuzz.py` and
`test_fuzz.py`, with both planned faults injected and caught and shrunk to two keys each. Campaign:
**20,626 cases, 3,751,638 tick-lines, 0 divergences** — 24x the recorded corpus. The honest limit
is recorded with it: only 55% of generated keys are ever *consumed*, because random play is wide
and shallow, which is the argument for Phase 4's solver as the complementary coverage.

**2026-09-05, session 9 — Phase 4, layer 0.** The search API (`Engine.Search.cs`: snapshot/restore,
`ApplyKey`, `StateHash`), the batch harness, and `verify_solutions.py`. The harness caught two bugs
in itself, both worth not re-discovering, and both are written up in the layer 0 section: a
macro-step is not bounded by anything cheap (3,652 ticks for one keypress on a conveyor circuit),
and `Restore` rewinds `RecP` while `RecBuffer` is one shared array.

**2026-09-05, session 10 — the campaign, and layer 1.** Threw away a wall-clock-budgeted campaign
and re-ran it node-governed, which is where that rule comes from. Layer 1's macro-actions win on
levels layer 0 fails and *lose* over the corpus in both orderings, so they ship as a second pass
rather than a portfolio member — the finding that shaped every layer after it. 395 -> 416.

**2026-09-05, session 11 — layer 2, subgoal decomposition.** Derive what is in the way from the
*executed* movement closure rather than from the price list; accept on a board test, rank on a
position test. The modelled first version found no obstacle on 62% of expansions and is kept in the
write-up as the thing that `--sg-trace` killed. 416 -> 441.

**2026-09-05, session 12 — layer 3, restarts.** Priced the dead-end failure mode before designing
for it (717 levels, 84% of budget unspent), then found that what recovers a dead-end is *width
bought after narrow has failed*, not randomness. Strictly additive by construction and checked to
be. The negative half is the larger half: dead-ends 717 -> 9 bought four levels. 441 -> 444.

**2026-09-06, session 13 — layer 4, a learned evaluation.** Built the instrument first
(`rankdump.py`), and it authorised the layer rather than redesigning it: the winner's successor is
in the expansion's output **97.6%** of the time and `WorkDistance` ranks it **100th of 395**, so
the loss was entirely in the sort. Fit as a ranking problem within a group, not a cost-to-go
regression. **444 -> 472, none lost.** Two results kept because they are the useful kind: a
re-ranking is *not* additive the way a restart is (so the chain grew a pass instead of swapping
one), and feeding the newly solved levels back in **halves what the model discovers** while
raising its score.

**2026-09-06, session 14 — the handoff itself.** No code. Added the *Start here* block (build,
gates, one level, the whole chain — every command in it run and verified), turned Phase 5 from a
seven-line wish list into a plan with exit criteria, and **pruned the file from 2,342 lines to
~1,650**. What was cut was narrative of completed, verified work whose outcome is stated in its
phase section — the old Status block was 187 lines duplicating Phases 2-4, and the session log was
680. What was *kept* is anything a future session could waste time re-discovering, and four such
facts that lived only in the session log were rehomed first: the WSL-`bash` trap, the
text-mode line-ending trap and the `.lvl`/`.LVL` case trap to *Environment notes*, the
`GFXKill` missing parens to *Quirk hazards* as #11, and the 2016-snapshot warning to *Test corpus*.
Decoded the sprite atlas properly while writing Phase 5 (320×192, a 10×6 grid of 32×32, `BMA`
row-major from i=1) and verified the `.ltg` header against all three packs, which corrected two
claims the plan would otherwise have shipped wrong.
