# LaserTank → Godot port: progress & handoff

**Purpose:** single source of truth for this port. Read this first after a context clear.
Update the *Status* block and *Session log* at the end of every working session.

---

## Status

**Phase:** 4 — **layer 1 built and measured; the whole corpus has been run through both
layers and every solution verified.** Phase 3's harness is green and its first campaign
found nothing. Phases 1 and 2 are complete. The C reference oracle replays the
whole recorded corpus, and the C# core now traces **byte-identically to it on all 187 recordings**
with `--field --bmf`. The step-by-step history below is kept because each step's reasoning is the
handoff; if you only need the current state, read the step 5 block and "What is still not ported".
**Result:** `python tools/replay_all.py` is green — **187 replayed, 181 win, 6 documented
non-winners, 0 unexpected**, exit 0. 112/112 `Tutor-with-Playbacks` match their bundled `.ghs` move
*and* shot targets exactly. **54,162 replayed keypresses, zero wasted.**
The 6 non-winners are recordings their own authors describe as incomplete, pinned in
`EXPECTED_NON_WIN` with the numbers they must reproduce; the suite fails if any of them changes in
either direction (fault-injection tested).
**Phase 2, step 0 — complete.** `tools/difftrace.py` exists and is proven. It reports the first
diverging tick, the first field to move (sub-field resolution: `S.moves`, `SlO.dy`, `T.dir`) and,
for `PF`/`PF2`, the individual cells decoded to object names. Given two *directories* it pairs
traces by filename, which is the Phase 2 exit criterion in one command.
`python tools/test_difftrace.py` is green — **29 cases**, and every one of 7 fault injections into
the differ was caught. Whole-corpus check: `replay_all.py --traces … --field --bmf` run twice gives
**187/187 identical** (327 MB per side, ~1 s to diff), and poking a single playfield cell in one of
the 187 is found and localised.

**Phase 2, step 1 — complete.** .NET SDK 10 and Godot 4.7.2 Mono are installed. `src/` holds
`LaserTank.Core` (the transliteration, pure C#) and `LaserTank.Cli` (the oracle's command line,
the oracle's trace format), building to `build/lasertank-core.exe` via `bash src/build.sh`.
Ported in step 1: `BuildBMField`, `PutLevel`, `GameOn`, `Animate`, the logic-carrying half of
`LoadNextLevel`, the whole `Tick()` frame and the `SendMessage`/`PostMessage` death split.
Everything else throws `NotPortedException` rather than no-opping.
**Empty-keystream equivalence holds on 2,347 levels** — all 2030 of the flagship collection and
all 317 quirk-pack levels — byte-identical with `--field` and `--bmf`.

**Phase 2, step 2 — complete.** `CheckLoc` and `MoveObj` are transliterated, and with them the
dependency closure `MoveObj` drags in: `TranslateTunnel`, `UpDateTankPos`, `UpdateUndo` and
`ResetUndoBuffer` (which `LoadLevel` was missing — `LoadNextLevel` calls it at `LTANK2.C:1033`).
The empty-keystream sweep still gives **2,347/2,347 byte-identical** with `--field --bmf`, so
nothing regressed; see below for why that is the *only* trace signal these two functions can
produce yet.

**Phase 2, step 3 — complete.** `MoveTank` *and* `AntiTank` are transliterated, and together they
turn the recorded corpus back into a gate. `MoveTank` alone could not: `Tick()` calls `AntiTank()`
after every consumed key, so a `MoveTank`-only engine still stopped at tick 1 on all 16
`game-objects` recordings — the earlier "MoveTank is what turns the corpus back into a gate" was
wrong on that point and is corrected here. `AntiTank` is portable ahead of `FireLaser` because its
four scans need only `CheckLoc` and every `FireLaser` call sits behind a match, so a recording with
no aligned anti-tank runs to whatever stops it next.

**Result: 187 compared, 1 identical, 186 `DIVERGE length mismatch`, and not one field divergence.**
Every recording is byte-identical with `--field --bmf` up to the tick where the port stops, and
`Game-Objects-in-LT_0001` (pure movement, no shot) now replays end to end and wins.
1,041 of 156,504 corpus tick-lines are reproduced. Where the 186 stop:
`FireLaser` 161, `IceMoveT` 20, `ConvMoveTank` 5 — every one a function that still throws.
`AntiTank`'s scans are genuinely exercised, not merely compiled: `_0007` ("Covering the
anti-tanks") replays 52 ticks of pure movement and then stops in `FireLaser` *called from
`AntiTank`*, having consumed no fire key at all.
Empty-keystream spot sweep (202 levels across `LaserTank.lvl`) still byte-identical — neither new
function is reachable without a consumed key, so nothing there could regress, and nothing did.

**Phase 2, step 4 — complete.** The whole laser subsystem: `FireLaser`, `MoveLaser`, `CheckLLoc`,
`KillAtank`, the logic half of `UpDateLaserBounce`, `TestIfConvCanMoveTank`, and the six
`SlideO`/`SlideMem` stack helpers `CheckLLoc` drags in (`Mem_to_SlideO`, `SlideO_to_Mem`,
`add_SlideO_to_Mem`, `sub_SlideO_from_Mem`, `del_SlideO_from_Mem`).

**Result: 187 compared, 41 identical, 146 `DIVERGE length mismatch`, still zero field
divergences.** **53.2% of the corpus is now reproduced** — 83,219 of 156,504 tick-lines, up from
1,041 — and every one of them is byte-identical with `--field --bmf`. The remaining stops are
`ConvMoveTank` 59, `IceMoveT` 55, `IceMoveO` 32; `FireLaser`, `MoveLaser` and `CheckLLoc` have
disappeared from that list entirely. 41 recordings now replay end to end and win.

**A real bug was hiding behind the exception.** The step-1 tick frame called
`FireLaser(..., 0)` where the oracle passes `S_Fire` (`driver.c:193`). That is not a sound-only
argument: `FireLaser` ends with `laser.Good = (sf == 2)`, which is how the engine tells the tank's
own shot from an anti-tank's (`S_Anti2` = 9). It was invisible for three steps because `FireLaser`
threw before reading it. Fixed with the constant, not the literal. **This is the argument for
`NotPortedException` over a no-op, stated concretely:** a stub would have run this call happily and
the divergence would have surfaced hundreds of ticks later as a wrong `laser.Good`.

**Hazard #1 is transliterated but NOT yet exercised — do not treat it as verified.**
*(Superseded by step 5 below: it is exercised and verified now. Kept because the probe technique
is reusable and because "written" vs. "reached" is a distinction worth keeping visible.)* Probed
directly: temporarily replacing `LaserBounceOnIce = true` inside `UpDateLaserBounce` with a throw
and re-running all 187 recordings fires it **zero times**. It needs a mirror that is *actively
sliding* at the moment the beam deflects off it, and the sliding is `IceMoveO`'s job, which still
throws. So the `goto LaserMoveJump` second-step path gets its first real test in step 5, not here.

**Phase 2, step 5 — complete, and with it PHASE 2 ITSELF.** `ConvMoveTank`, `IceMoveT` and
`IceMoveO` are transliterated. The exit criterion is met:

> **`python tools/difftrace.py build/t-oracle build/t-csharp` — 187 compared, 187 identical,
> 0 cosmetic-only, 0 diverged, 0 unusable. Exit 0.**

With `--field --bmf`, so that is every playfield cell, both layers, both bitmap layers, the tank
pose, the laser, both slide records, the whole slide stack and the scores, on every tick of all
187 recordings — 156,504 tick-lines, not one byte different. Even the cosmetic `BMF` tripwire holds
(0 cosmetic-only), which was allowed to differ and does not.

Driven independently, the C# core also reproduces the oracle's replay result exactly:
**187 replayed, 181 win, 6 documented non-winners, 0 unexpected, 112/112 `.ghs` move+shot targets
exact** — the same numbers Phase 1 pinned for the oracle, now produced by the port.
The empty-keystream sweep is still **2,347/2,347 byte-identical**.

**Hazard #1 is now genuinely exercised.** The step-4 probe (swap `LaserBounceOnIce = true` for a
throw, replay all 187) fired *zero* times; re-run after `IceMoveO`, it fires on
**`Tutor-with-Playbacks_0093` at tick 527 and `_0094` at tick 206**. Level 93 is "One suicidal
anti-tank", and its own hint spells the mechanism out: *"The laser is deflected by three mirrors at
K8 (**sliding mirror**), K10, N10, and N8."* A paint routine's side effect, driving a second beam
step inside one tick, on a level built to depend on it — reproduced exactly. Hazard #1 is the one
that justified compiling `LTANK2.C` verbatim in the oracle, and it is now verified in the port too.

**Phase 3, first campaign — complete, and the harness is proven.** `tools/fuzz.py` exists:
random keystreams, both engines, diff, and — the part that matters — **shrink any divergence to a
minimal repro**. `tools/sweep.py` lifts the empty-keystream sweep out of scratch, and
`tools/engines.py` is the two-engine plumbing both share.

**The fuzzer has gone red on purpose before being trusted green.** `python tools/test_fuzz.py`
injects two known faults into `Engine.cs`, rebuilds, and fails unless the fuzzer finds *and*
shrinks each one. **25 passed, 0 failed**, with a green control run before and after:

| injected fault | found in | shrunk to |
|---|---|---|
| `AntiTank`'s four scans reordered | 93 cases, 4 s | **2 keys** — level 765, `dd` → `L.x` at tick 8 |
| `MoveTank`'s `SlideT` write moved inside the `if` | 2 cases, 2 s | **2 keys** — level 1314, `rr` → `SlT.dx` at tick 2 |

Both minimal repros are re-verified independently of the shrinker: they still diverge, and
deleting any single remaining key stops the divergence.

**First campaign: 20,626 cases, 0 divergences.** Six keystream shapes over all 2,030 flagship
levels plus every quirk pack — **3,751,638 tick-lines compared, 24× the whole recorded corpus**
(156,504). 890,690 of 1,630,624 generated keys were actually consumed (55%); 141 runs won their
level by accident, 10,758 killed the tank, 9,727 ran out of keys. `MouseOperation` was never
reached (`NOTPORTED` 0), which is the expected answer and now a measured one.

**Phase 4, layer 0 — complete.** The solver's foundation, not the solver. `Engine.cs` gained one
word (`partial`); `Engine.Search.cs` adds snapshot/restore, `ApplyKey` (one keypress, then tick to
quiescence) and `StateHash`. `build/lasertank-solve.exe` batch-solves levels with a beam + IDA*
portfolio and writes each solution as a **`.lpb`** — a real recording, playable in the 2010 binary.
`tools/verify_solutions.py` replays every one through the *unmodified* oracle and the core.

**First numbers.** 150 cheapest-by-`.ghs` flagship levels: 110 solved in 20 s (Kids 81.5%, Easy
60.7%). `Beginner-I`'s 400 cheapest: **384 solved in 12 s**. Every solution verified —
**494/494 byte-identical on both engines, 405 matching the `.ghs` record exactly**, median 1.6× the
record's keypresses. See the Phase 4 section for the measured bar, the layer plan above this one,
and the two bugs the self-check caught.

**Phase 4, layer 1 — built, measured, and the measurement is the deliverable.** Macro-actions:
`Goto` + `Shoot` instead of raw keys, with `Goto` a breadth-first closure *over `Engine.ApplyKey`*
rather than a grid A*, so ice, conveyors, tunnels, pushes and anti-tank turns are resolved by being
executed and the repo still holds exactly one implementation of the game.

**It wins where the raw beam fails and loses over the corpus, and no ordering or share fixes
that.** On 60 levels layer 0 could not solve it goes 18→28, 23→31, 33→38 (at 150k / 400k / 1M
nodes). Over 4,185 levels of the real corpus, as a portfolio member it *loses*: 395 → 381 run
first, 395 → 354 run last. Most solvable levels are ones the raw beam gets easily, and every node
the macro beam spends is one taken from it. **So it ships as a second pass, not a portfolio
member** (`tools/second_pass.sh`, `RunMacro` off by default): layer 0 runs, then the macro beam
re-attacks only its failures. Composite **395 → 416 of 4,185 (9.4% → 9.9%)**, nothing lost.

**The whole corpus has been run through both layers, and every solution verified.** 1-in-5 stride,
all 13 collections, 150,000 `ApplyKey` calls per level. **416/416 composite solutions and 381/381
layer-1 solutions replay byte-identically through the unmodified C oracle and the C# core with
`--field --bmf`** — 797 solver-produced winning recordings, zero divergences, 280 of the composite
matching the `.ghs` record exactly, median 1.6×. 95.9% of the unsolved stopped on **budget**, not
on a dead end: the binding constraint is depth, not correctness.

**Campaigns are governed by nodes, not by wall clock.** The session's first campaign was
wall-clock-budgeted and was thrown away — the gates and benches were running beside it, so its
budget bought a varying amount of work. `tools/campaign.sh` takes `--nodes` (equal work,
load-independent, comparable between layers) and demotes `--budget-ms` to a backstop;
`tools/report_stats.py` reads a report and `--diff`s two.

**Next action:** layer 2 — subgoal decomposition, which is what layer 1's measurement argues for
(the reason to fire has to be derived, not scored). `data/quirks/tutor` is the operator library,
92 levels with one named technique each. Keep fuzzing in parallel — new seeds, the other 12
collections, longer keystreams. **Blocked on:** nothing.

**What is still not ported:** `MouseOperation` only. The mouse buffer is empty headless
(`MB_TOS == MB_SP` always), so the tick's mouse block never fires and no keystream can reach it.
It still throws rather than no-opping, so if that premise ever breaks the run stops loudly. It is
Phase 5 work — it is a UI entry point, not game logic.

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

### Phase 3 — Differential fuzzing  ◐ (harness done and proven; campaign ongoing)
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

### Phase 4 — Solver  ◐ (layers 0 and 1 done; whole corpus run and verified)

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
- **Layer 2 — subgoal decomposition.** Relax the world, find what blocks the flag, ask per obstacle
  how it is removed (shoot the brick, push the block into water, redirect via mirror). Each subgoal
  is a *shallow* search, which is how a 400-key solution becomes reachable. The operator library is
  already curated and does not have to be mined: **`data/quirks/tutor` is 92 levels, one named
  technique each** — "Climbing on things", "Moving a dead anti-tank", "Tank-Mover Entry" (the first
  square is skipped), "Collisions - Tunnel Exit", "Pass the anti-tanks". The pack this port already
  uses as its quirk spec is also its technique spec.
- **Layer 3 — portfolio and restarts.** Beam and IDA* already; NRPA / nested Monte-Carlo for the
  levels beam gets stuck on.
- **Layer 4 — learning.** The 187 recordings are labelled winning trajectories: fit a small
  evaluation function over board features and use it to order the beam, then feed every newly
  solved level back in. Hints as landmarks belongs here too, but it is a *tail* tool and the size
  is measured: only **175 of 20,914** hints are recipe-grade (≥2 grid references or numbered
  steps), and they concentrate where search fails — 0.4% of Kids, 3.5% of Hard, 7.3% of Deadly.

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

**150,000 `ApplyKey` calls per level, every 5th level of all 13 collections — 4,185 levels — 7
workers, ordered cheapest-by-`.ghs` first.** Layer 0 is the raw-keypress portfolio (IDA* + beam);
"+ pass 2" is that campaign followed by `tools/second_pass.sh` re-attacking its 3,790 failures
with the macro beam at the same budget.

| tier | levels | layer 0 | + pass 2 | median ratio |
|---|---:|---:|---:|---:|
| Kids | 960 | 303 (31.6%) | **319 (33.2%)** | 1.6× |
| Easy | 2,118 | 84 (4.0%) | **89 (4.2%)** | 1.6× |
| Medium | 784 | 7 (0.9%) | 7 (0.9%) | 1.6× |
| Hard | 257 | 0 | 0 | — |
| Deadly | 56 | 1 (1.8%) | 1 (1.8%) | — |
| unrated | 10 | 0 | 0 | — |
| **all** | **4,185** | 395 (9.4%) | **416 (9.9%)** | **1.6×** |

| collection | levels | layer 0 | + pass 2 |
|---|---:|---:|---:|
| `Beginner-I` | 400 | 143 (35.8%) | **150 (37.5%)** |
| `Beginner-II` | 276 | 82 (29.7%) | **86 (31.2%)** |
| `Special-I` | 105 | 19 (18.1%) | **20 (19.0%)** |
| `Challenge-I` | 400 | 39 (9.8%) | **42 (10.5%)** |
| `LaserTank` | 406 | 34 (8.4%) | **35 (8.6%)** |
| `Challenge-III` | 400 | 31 (7.8%) | **34 (8.5%)** |
| `Challenge-II` | 400 | 17 (4.2%) | **18 (4.5%)** |
| `Challenge-IV` | 400 | 14 (3.5%) | **15 (3.8%)** |
| `Gary-I` | 400 | 7 (1.8%) | 7 (1.8%) |
| `Sokoban-II` | 348 | 4 (1.1%) | 4 (1.1%) |
| `Sokoban-I` | 400 | 3 (0.8%) | 3 (0.8%) |
| `Gary-II` | 65 | 1 (1.5%) | 1 (1.5%) |
| `Challenge-V` | 185 | 1 (0.5%) | 1 (0.5%) |

**The unsolved are unsolved on budget, not on structure: 3,636 of layer 0's 3,790 stopped at
`budget` (95.9%) and only 154 at a beam dead end.** No errors, no `NOTPORTED`, no crashes in
8,370 level-solves. That matters for reading the low tiers: `Hard` at 0/257 is not the search
failing to find a route, it is the search never getting near the end of one.

**Every solution verified through both engines** — `tools/verify_solutions.py` replays each `.lpb`
through the *unmodified* C oracle and the C# core with `--field --bmf` and requires WIN on both
plus byte-identical traces. **416/416 verified, 280 matching the `.ghs` record exactly**, median
1.6× and worst 5.0×. The separate all-layer-1 run verified 381/381 the same way. That is 797
solver-produced recordings replayed tick-for-tick through the 25-year-old C, with zero
divergences — and none of it random: these are long, legal, *winning* paths, which is the coverage
a fuzzer cannot reach.

**Where this leaves the deliverable.** The Phase 4 promise was "a solved-count-vs-budget curve,
Kids-first ordered by `.ghs` cost, not a promise of 20,914". At 150k nodes that curve reads:
**Kids 33%, Easy 4%, everything else ≈ 0**, and the binding constraint is depth, not correctness.

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

### Phase 5 — Presentation & features  ☐
Rendering (`Game.BMP` sprite sheet + `Mask.BMP`; `.ltg` packs in `data/graphics/` — format at
`LTANK2.C:688`), the 16 WAVs in `original/src/Sounds/`, three zoom levels (`SetGameSize`), level editor, undo,
record/playback UI, high scores, `language.dat` i18n, and read/write compat for community files.

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
                   Macro.cs — layer 1: Goto (a movement closure over ApplyKey)
                   + Shoot, so search depth is shots rather than keypresses
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
                      byte-identical traces, and the ratio to the .ghs record
  campaign.sh       one solver campaign over all 13 collections into one report.
                      Node-governed, not wall-clock: two layers have to be
                      comparable and a contended machine makes seconds lie.
                      STRIDE=N samples every Nth level of every collection
  second_pass.sh    re-attack a campaign's unsolved levels with a different
                      searcher, into the same solutions dir  <- where layer 1 ships
  report_stats.py   read a campaign .jsonl: per-tier solved rates, per-collection
                      rates, stop-reason breakdown.  --diff compares two layers
  bump_rate.py      classify consumed keys; bumps = desync signature
  dump_level.py     print a .lvl level as ASCII with its hint
  unpack_lpb_txt.py decode a Text-Converter .txt wrapper back to .lpb
```

See `README.md` and `data/SOURCES.md` for provenance.

## Test corpus

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

### 2026-09-05
- Read the full source; mapped logic to `LTANK2.C` + the `WM_TIMER` loop in `LTANK.C`.
- Decoded `.lvl`, `.ghs`, `.lpb` formats against the real files; confirmed 2030 levels and
  2030 non-zero global high scores.
- Established the determinism insight (keystream-driven 20 Hz tick) and its consequence for
  validation strategy.
- Catalogued the 10 quirk hazards above with file:line references.
- Downloaded and inventoried the 10-pack quirk corpus (now `data/quirks/`).
- Settled architecture: C oracle + C# Godot core + presentation layer.
- Reorganized the repo (`original/` frozen, `data/` corpus, `oracle/`+`tools/` for the build);
  wrote `.gitignore`, `README.md`, `data/SOURCES.md`.
- Corpus is much larger than first thought: **20,914 levels across 13 collections**, all with
  `.ghs` targets, plus 317 quirk levels and **186** `.lpb` solutions (was 128).
- Verified and removed duplicates: `Updates.zip` was byte-identical to the extracted level files;
  root `Tutor.*` and `.ltg` copies duplicated the packs and `original/src/Setups/Files/`.
- Logged an open question: which of the two `lasertank.exe` builds is the behavioural reference.
- Dropped the redundant archive: all 202 quirk-pack zip entries verified byte-identical to
  `data/quirks/` before removal. Kept the one genuinely unique file as
  `data/levels/LaserTank-2016-snapshot.zip` — a third vintage of the flagship collection,
  59 of 2030 levels differing from the current one. Relevant if a `.lpb` ever fails to replay.
- Committed as `3ce1efa init` and pushed. **No code written yet — Phase 1 starts clean.**

### 2026-09-05 (session 2) — Phase 1 done
- Installed MinGW-w64 (WinLibs gcc 16.1 UCRT) via winget; the box had no C compiler at all.
- Built the oracle. The load-bearing decision: **stub only the Win32 API, compile `LTANK2.C`
  verbatim.** Hazard #1 (`UpDateLaserBounce` mutating `LaserBounceOnIce`) then needs no special
  handling — it survives because only `Rectangle`/`SelectObject` are stubbed out from under it.
  Hazard #8 needed a real message pump: `SendMessage` re-enters the window proc, `PostMessage`
  queues for after the tick.
- Measured the external surface first (`nm -u` on `LTANK2.C` built against real headers): 49 Win32
  calls and ~12 globals. That made the stub a bounded job rather than an open-ended one.
- `LT_Tick()` in `oracle/driver.c` is a line-by-line transliteration of `WM_TIMER` (`LTANK.C:579`).
  `LTANK.C`/`LTANK_D.C` are not compiled — they are window and dialog code.
- **Replayed all recordings: 181 reach the flag, 54,162 keypresses, 0 bumps.** All 112
  `Tutor-with-Playbacks` match their bundled `.ghs` on both moves and shots.
- Decoded level 21's playback out of its Text-Converter `.txt` wrapper and added it to the corpus
  (187 now). Taught `replay_all.py` which recordings are documented non-winners and what numbers
  they must reproduce, so the suite exits 0 when correct and 1 on any change — verified by fault
  injection in both directions. Before that it was permanently red and so useless as a gate.
- Established that the 5 non-winners are incomplete recordings, corroborated by two level hints
  quoting the exact stopping points we reproduce, and by the level-21 `148/257` confirmation.
- **Corrected hazard #2**: `Game.BMF` is cosmetic — no bitmap feeds a decision anywhere in the
  program. This removes an animation-phase dependency that would have made replays non-deterministic
  across level loads.
- Also noticed but left alone: `LTANK2.C:1738` reads `if (GFXOn) GFXKill;` — a missing `()`, so the
  call never happens. Real bug in the original, in `SetGameSize`, cosmetic. Do not "fix" it.
- Open: which `lasertank.exe` is the behavioural reference is still unresolved, but the oracle now
  agrees with 132/132 quirk-pack recordings that carry independent score targets, so the question is
  much less urgent than it looked.

### 2026-09-05 (session 3) — Phase 2 steps 0 and 1

**Step 0 — the differ.**
- Wrote `tools/difftrace.py`: first diverging tick, first field, sub-field resolution
  (`T.dir`, `S.moves`, `SlO.dy`, `M1.dy`), per-cell `PF`/`PF2` diffs decoded to object names
  including the tunnel encoding, a context window, and a whole-file summary counting how many
  ticks each field moves on. Directory mode pairs two trace trees by filename.
- Cosmetic fields (`A`, `BMF`, `BMF2`) get their own exit code, **3**, so the Phase 2 exit
  criterion's "cosmetic is a tripwire, not a failure" is expressed in the tool rather than in a
  convention someone has to remember. `--strict` collapses it back to a failure.
- Proved it before trusting it, which was the point of doing this first:
  `tools/test_difftrace.py`, **29 cases**. Two are real oracle runs of the same level differing by
  one keypress (index 60, a shot turned into a left turn), which must be reported at tick 90 as
  `T.dir 1 -> 4`; the rest are synthetic mutations of a real trace, which is the only way to pin
  the exact wording — a changed keypress moves half the fields at once.
- Fault-injected the differ 7 ways (emptied the cosmetic set, swapped the `S`/`T` ranking, disabled
  grid decoding, disabled first-divergence capture, disabled cell decoding, dropped absent-field
  handling, dropped length-mismatch detection). **All 7 caught.** Green again after restoring.
- Corpus scale: `replay_all.py --traces … --field --bmf` twice → **187/187 identical**, 327 MB a
  side, ~1 s to diff. Poking one playfield cell in one of the 187 is found and localised.
- Taught `replay_all.py` `--field`/`--bmf` (it wrote hash-only traces before, which Phase 2 cannot
  bite on) and `--engine EXE`.

**Step 1 — the C# core stood up.**
- Installed .NET SDK 10.0.400 and Godot 4.7.2 Mono. Godot is *not* needed for Phase 2: the core is
  a plain class library and the driver is a console app, so the whole trace-diff loop is
  `dotnet` only.
- `src/LaserTank.Core` + `src/LaserTank.Cli` → `build/lasertank-core.exe`, taking the oracle's
  command line and writing the oracle's trace format byte for byte.
- Ported: `BuildBMField`, `PutLevel`, `GameOn`, `Animate`, the logic-carrying half of
  `LoadNextLevel`, the `Tick()` frame, and the `SendMessage`/`PostMessage` death split.
- Two state writes hiding in "paint" functions, both nearly missed and both now carried:
  `UpDateTank()` clears `TankDirty` (`LTANK2.C:537`) and **`Animate()` ends with
  `TankDirty = TRUE`** (`LTANK2.C:1161`). Hazard #1's lesson generalises: in this program a
  function's name tells you nothing about whether it mutates state.
- Unported functions **throw `NotPortedException`** instead of no-opping. Verified:
  `replay_all.py --engine build/lasertank-core.exe` stops at the first `MoveTank` with a stack
  trace rather than emitting a plausible wrong trace.
- Empty-keystream milestone reached, and then widened: `--keys ""` on **2,347 levels** — all 2030
  of `data/levels/LaserTank.lvl` plus all 317 quirk-pack levels — is **byte-identical on every
  one**, with `--field` and `--bmf`, and no `NotPortedException` anywhere (no level in the corpus
  leaves the tank on water or a conveyor at load). Level 1 was also checked independently of
  `difftrace.py` with a raw `diff` of everything after line 1: LF-only on both sides, and the only
  size difference is line 1 itself.

  The sweep is a shell loop, not a tool — Phase 3's fuzzer will generalise it:

  ```bash
  for i in $(seq 1 2030); do
    ./oracle/build/oracle.exe  --levels "$L" --level $i --keys "" --trace i0/$i.trace --field --bmf -q
    ./build/lasertank-core.exe --levels "$L" --level $i --keys "" --trace i1/$i.trace --field --bmf -q
  done
  python tools/difftrace.py i0 i1 -q
  ```

### 2026-09-05 (session 4) — Phase 2 step 2: `CheckLoc` and `MoveObj`

- **`CheckLoc`** (`LTANK2.C:1278`) with `CheckArray` (`:78`), and `wasIce` as a field rather than an
  `out` parameter. The reason is behavioural, not stylistic: `CheckLoc` returns early on an
  off-board coordinate **without writing `wasIce`**, so a move blocked at the edge of the board
  leaves the previous call's value for `MoveTank`'s `if (wasIce)` to read. An `out` parameter
  cannot express "sometimes I don't write this".
- **`MoveObj`** (`:1287`) and the closure it drags in — `TranslateTunnel` (`:1164`),
  `UpDateTankPos` (`:1216`), `UpdateUndo` (`:423`), `ResetUndoBuffer` (`:402`). Quirk #7 is intact:
  `Game.ScoreMove--` / `UpDateTankPos(0,0)` / `UndoP--` bracket to a net zero, and only because all
  three are present.
- **Found a gap in the load path:** `LoadLevel` never called `ResetUndoBuffer`, which
  `LoadNextLevel` does at `LTANK2.C:1033`. Invisible until now because nothing touched `UndoP`;
  `MoveObj` decrements it, so it would have started drifting from the second level onward.
- `WaitToTrans` and `BlackHole` stay globals because the *staleness* is load-bearing:
  `UpDateTankPos` and `ConvMoveTank` read `WaitToTrans` after a move that was not into a tunnel,
  where nobody assigned it this tick. `MoveObj` is the only caller that clears it (`else
  WaitToTrans = FALSE`).

**The honest state of the verification.** The recorded corpus cannot reach either function yet.
`CheckLoc` is called from `MoveTank`, `IceMoveT`, `IceMoveO`, `CheckLLoc` and the conveyor arm of
the tick; `MoveObj` only from `CheckLLoc` and `IceMoveO`. All of those are still unported, and no
corpus level starts the tank on a conveyor, so a `.lpb` replay still stops on tick 1 at the first
`MoveTank` and `difftrace` reports a 1-tick prefix on all 16 of `game-objects`. **Step 2 is
verified as "nothing regressed", not as "the new code is right"** — that arrives with `MoveTank`
in step 3, which is the first thing that makes 54,162 recorded keypresses run through `CheckLoc`.
What was actually checked:

- Empty-keystream sweep, **2,347/2,347 byte-identical** with `--field --bmf` (2030 flagship + 317
  quirk levels), so the changed load path and the new code cost nothing on the idle tick.
- `tools/test_difftrace.py` 29/29, and the oracle corpus still 187 replayed / 181 win / 0
  unexpected / 112 `.ghs` exact.
- A mechanical transliteration check over all six functions: strip comments, normalise the
  substitutions the port is allowed to make, and diff the token streams against `LTANK2.C`. Every
  remaining difference is accounted for — `private`, return types, `TRUE`/`FALSE`, the `byte` casts
  C# needs, `ref` unpacking for `TranslateTunnel`'s `int *`, `PF[x][y]` → `PF[x,y]`, two unused C
  locals (`i` in `TranslateTunnel` and `MoveObj`), and the two unreachable `GlobalReAlloc == NULL`
  branches. Nothing unexplained. It is a reading aid, not a proof; the trace is the proof.
- Harness: the CLI now flushes the trace before dying on `NotPortedException` (`# result=NOTPORTED`,
  exit 4). Before this, a partly-ported engine wrote *nothing* and `difftrace` said `UNUSABLE` —
  the prefix, which is the whole point of porting one function at a time, was being thrown away.

### 2026-09-05 (session 5) — Phase 2 step 3: `MoveTank`, `AntiTank`

**The corpus is a gate again.** Step 2 closed saying step 3 would be the moment 54,162 recorded
keypresses start running through `CheckLoc`. That is what happened, but it took two functions, not
one, and the handoff note was wrong about which:

- `MoveTank` alone moved nothing. `Tick()` runs `AntiTank()` immediately after every consumed key
  (`LTANK.C:617`), so the very first keypress still ended in `NotPortedException` and all 16
  `game-objects` recordings stayed at a 1-tick prefix — identical to the step-2 baseline, with only
  the `keys=` counter moving from 0 to 1. Ported in isolation it is unverifiable.
- `AntiTank` is portable *before* `FireLaser` because its four scans need nothing but `CheckLoc`,
  and every `FireLaser` call in it sits behind a match. A recording with no anti-tank lined up on
  the tank therefore runs straight through it. That is what makes the pair, rather than the whole
  laser subsystem, the smallest thing that unlocks the corpus.

**Result — 187 recordings, `--field --bmf`: 1 identical, 186 `DIVERGE length mismatch`, zero field
divergences.** Not one playfield cell, score, tank pose or `BMF` byte differs anywhere in the
1,041 tick-lines the port now reproduces (of 156,504). Every divergence is the port stopping, and
every stop is a function that still throws: `FireLaser` 161, `IceMoveT` 20, `ConvMoveTank` 5.
`Game-Objects-in-LT_0001` — pure movement, no shot — replays end to end and wins.

**`AntiTank` is exercised, not just compiled.** `game-objects/_0007` ("Covering the anti-tanks")
replays 52 ticks of pure movement and then stops inside `FireLaser` *reached from `AntiTank`*,
having consumed no fire key at all: the scans found a live anti-tank and tried to shoot. Levels
13/14 (Ice, Thin ice) stop in `IceMoveT`, which means `MoveTank`'s `if (wasIce)` arm ran and set
`SlideT` — the two halves of the function are both on the tested path.

Kept verbatim, and worth not "cleaning up" later:

- **`MoveTank` turns without moving.** A key whose direction differs from the tank's sets
  `Tank.Dir` and returns, spending the keypress and leaving `SlideT` untouched. Only a repeat of
  the facing direction attempts a move.
- **`SlideT.dx`/`dy` are written on both arms of every `if`.** A move blocked by a wall still
  records the direction it was blocked in, and `IceMoveT` reads it later, so a bump is not a no-op.
- **Quirk #5 is `AntiTank`'s shape**, not a comment on it: right → left → down → up, first match
  returns. Two anti-tanks on the same row/column and only one fires, chosen by scan order rather
  than by distance. Tutor level 42 is a level built to test exactly this. The four scans are not a
  loop over four directions and must not become one.
- **`AntiTank` writes `wasIce` without ever naming it.** Its scans are `while (CheckLoc(...))`
  loops, so whichever ran last leaves its final probe in the flag, and `MoveTank`, `IceMoveT`,
  `IceMoveO` and `ConvMoveTank` all read `wasIce` after their own `CheckLoc`. Quirk #3 reaches
  further than the three callers hazard #3 lists — noted in "What the C# side looks like now"
  because it is a trap for step 4 and beyond.
- The bound checks in `AntiTank` are `x < 16` / `x >= 0`, not a `CheckLoc` result, so a scan that
  walked off the board is rejected before the `Game.PF` read. `Game.Tank.X != x` rejects the tank
  standing on the anti-tank's own cell.

Also checked: empty-keystream spot sweep over 202 levels of `LaserTank.lvl` still byte-identical
with `--field --bmf`. Neither new function is reachable without a consumed key, so this could not
have regressed — it is a guard against a stray edit, not evidence about the new code.

**Next:** `FireLaser` (161 of the 186 stops), then its closure `CheckLLoc` → `MoveLaser`. Hazard #1
lands there: `UpDateLaserBounce` is a *paint* routine that sets `LaserBounceOnIce` and makes
`MoveLaser` `goto LaserMoveJump` for a second step in the same tick (`LTANK2.C:1631`). The oracle
gets that for free by compiling the original; the C# port will not.

### 2026-09-05 (session 6) — Phase 2 step 4: the laser subsystem

Ported `FireLaser`, `MoveLaser`, `CheckLLoc` and everything they drag in: `KillAtank`, the logic
half of `UpDateLaserBounce`, `TestIfConvCanMoveTank`, and the six `SlideO`/`SlideMem` stack
helpers (`Mem_to_SlideO`, `SlideO_to_Mem`, `add_SlideO_to_Mem`, `sub_SlideO_from_Mem`,
`del_SlideO_from_Mem`). Trace diff after each of the three stages, as the protocol requires:

| after | game-objects (16) | stops in |
|---|---|---|
| step 3 | 1 identical, 15 diverge | `FireLaser` |
| `FireLaser` | 1 identical, 15 diverge — *no tick gain* | `MoveLaser` |
| `+ CheckLLoc` + helpers | 1 identical, 15 diverge — *no tick gain* | `MoveLaser` |
| `+ MoveLaser` | **12 identical**, 4 diverge | ice / conveyor |

The first two stages moving nothing is the same shape as step 3's `MoveTank`: `FireLaser` ends by
calling `MoveLaser`, and `CheckLLoc` is only reachable *from* `MoveLaser`, so neither is
independently observable. Port them anyway, in that order, and check — a stage that fails to move
the number is information, and a stage that moves it *unexpectedly* would be a bug.

**Whole corpus: 41 identical, 146 `DIVERGE length mismatch`, zero field divergences.**
**53.2% of the corpus now replays byte-identically** — 83,219 of 156,504 tick-lines, up from 1,041
after step 3. Remaining stops: `ConvMoveTank` 59, `IceMoveT` 55, `IceMoveO` 32.

**The `S_Fire` bug — the case for `NotPortedException`, made concrete.** Reading `FireLaser`
turned up that the step-1 tick frame passes `0` where the oracle passes `S_Fire` (`driver.c:193`,
`LTANK.C:631`). That argument is not sound-only: `FireLaser` ends with `laser.Good = (sf == 2)`,
which is how the engine distinguishes the tank's own shot from an anti-tank's (`S_Anti2` = 9).
The literal `0` had been sitting in the port for three steps and could not be caught, because
`FireLaser` threw before reading it. A no-op stub would have accepted the call, and the divergence
would have surfaced much later and much further from its cause. Fixed to the named constant.

**Hazard #1 is written but NOT verified. Do not tick it off.** `UpDateLaserBounce`'s slide scan is
transliterated and `MoveLaser` keeps the `goto LaserMoveJump`, but the path is provably not
exercised yet: temporarily swapping `LaserBounceOnIce = true` for a throw and re-running all 187
recordings fires it **zero times**. It needs a mirror *actively sliding* when the beam deflects off
it, and the sliding is `IceMoveO`'s, which still throws. Step 5 is where the second-step-in-one-tick
behaviour gets its first real test — expect it to be the interesting failure, and re-run this probe
afterwards to confirm the path is genuinely hit rather than merely compiled.

Shapes kept verbatim, worth not tidying later:

- **`CheckLLoc`'s `wasIce` is not about the cell that was hit.** It is set FALSE on entry, then the
  push arms call `CheckLoc(x+dx, y+dy)` which sets it from the cell the object is being pushed
  *into*. That is the entire mechanism by which a laser starts an object sliding, and it only reads
  that way if the `wasIce = FALSE` and the tail `if (wasIce)` stay where they are.
- **An anti-tank dies only when shot in the face** (`dy == 1` for a down-travelling beam vs. an
  up-facing A-T, and so on). Shot in the side or the back it is *pushed*, like a block. And it dies
  into `Obj_Solid`, not dirt — `KillAtank` sets `PF = 4`, so the wreck still blocks the square.
- **The laser death is `SendMessage`** (quirk #8) — immediate, mid-tick — so a shot can kill the
  tank in the same tick it was fired, unlike drowning, which is posted.
- **`del_SlideO_from_Mem`'s trailing `SlideO.s = (count > 0)` runs only when nothing matched.**
  The `return` inside the loop skips it, leaving `SlideO` holding whatever `sub_SlideO_from_Mem`'s
  shuffle left there. `sub_SlideO_from_Mem` shuffles *through* `SlideO`, so it clobbers it too —
  which is why `IceMoveO` reloads from the stack every iteration.
- **`MoveLaser`'s dead-shot arm carries three separate pieces of tick behaviour**: clearing
  `Game.Tank.Firing` (what lets the next tick consume a key), `AntiTank()` (an A-T can answer a
  shot the instant it expires), and `TestIfConvCanMoveTank() -> ConvMoving = TRUE`, MGY's 2002
  speed-bug handling, which blocks the key consume for one more tick.
- `MoveLaser`'s `goto` is kept as a `goto`. The label is also where `LaserBounceOnIce` is cleared;
  a `while` would have to reproduce that ordering anyway.
- `TestIfConvCanMoveTank` is another `wasIce` writer — four `CheckLoc` calls on the conveyor cases,
  and none on the default. Same trap as `AntiTank`, noted in step 3.

**Next:** `IceMoveO` / `IceMoveT` (87 of the 146 stops), then `ConvMoveTank` (59). `IceMoveO` is
where quirk #6 lands — the stack is walked top-down *while being mutated*, and the 16-entry cap is
silent — and where hazard #1 finally becomes testable.

### 2026-09-05 (session 7) — Phase 2 step 5, and Phase 2 complete

Ported `ConvMoveTank`, then `IceMoveT`, then `IceMoveO`, diffing after each:

| after | game-objects (16) | whole corpus |
|---|---|---|
| step 4 | 12 identical | 41 identical, 146 stops |
| `ConvMoveTank` | 13 identical | — |
| `+ IceMoveT` | 14 identical | — |
| `+ IceMoveO` | **16 identical** | **187 identical, 0 diverged** |

**`difftrace.py build/t-oracle build/t-csharp` → 187 compared, 187 identical, 0 cosmetic-only,
0 diverged, 0 unusable. Exit 0.** With `--field --bmf`: every playfield cell on both layers, both
bitmap layers, tank pose, laser, both slide records, the whole slide stack and the scores, on all
156,504 tick-lines. The cosmetic `BMF` tripwire — which the exit criterion *permits* to differ —
does not differ either.

Independently, driving the C# core through `replay_all.py` reproduces Phase 1's pinned numbers:
**187 replayed, 181 win, 6 documented non-winners, 0 unexpected, 112/112 `.ghs` exact.** Both the
6 non-winners and the `.ghs` targets are asserted, so this is a real gate, not a tautology of the
trace comparison. `test_difftrace.py` 29/29 first, as the harness section requires. Empty-keystream
sweep still 2,347/2,347.

**Hazard #1 is verified, not just written.** The step-4 probe fired zero times; re-run now it fires
exactly twice — `Tutor-with-Playbacks_0093` tick 527 and `_0094` tick 206. Level 93 ("One suicidal
anti-tank") documents the mechanism in its own hint: *"The laser is deflected by three mirrors at
K8 (**sliding mirror**), K10, N10, and N8."* A paint routine's side effect driving a second beam
step inside a single tick, on levels built to depend on it. The probe is now written into hazard #1
as the way to re-check it after any change to the laser or ice code: **it must fire exactly twice.**

Shapes kept verbatim in this step:

- **`ConvMoveTank` is `UpDateTankPos` minus the accounting.** Same move, same tunnel translation,
  but no `UpdateUndo`, no `ScoreMove++`, no `S_Move` — a tank *carried* by ice or a conveyor does
  not spend a move. It also never clears `Tank.Good` first (`UpDateTankPos` does), so a tunnel wait
  survives a ride, and it sets `ConvMoving = TRUE`, which costs the player a tick at the key
  consume. Two functions that look mergeable and are not.
- **`savei` in both `IceMoveT` and `IceMoveO` captures `wasIce` before the call that clobbers it.**
  `ConvMoveTank` ends in `AntiTank()`, and `MoveObj`+`AntiTank` do the same in `IceMoveO`; both
  overwrite `wasIce` via their `CheckLoc` scans. Read the flag after the call instead of saving it
  and the slide ends on whatever the anti-tank scan last probed. `savei` is what makes a slide
  terminate correctly — quirk #3 biting exactly where step 3 predicted it would.
- **The melt writes different layers in the two functions.** `IceMoveT` turns `PF` to water (the
  tank has nothing under it); `IceMoveO` turns `PF2` to water (the ice is *under* the sliding
  object). Irreversible either way — Phase 4's pruning depends on it.
- **Quirk #6, `IceMoveO`'s loop.** Top-down from `SlideMem.count`, and both arms can call
  `sub_SlideO_from_Mem`, which decrements `count` and shuffles entries down: the collection is
  mutated while iterated. Walking top-down is what makes that survive, and MGY's
  `if (iSlideObj <= SlideMem.count)` guard — comment: *"just in case ..."* — is the seatbelt. Do
  not rewrite as a filtered list; which object moves next is observable.
- **`SlideO_to_Mem(i)` immediately followed by `sub_SlideO_from_Mem(i)`** writes a slot the shuffle
  then overwrites — dead for every `i < count`, live only at `i == count`. Kept.
- **`IceMoveO`'s tail clears `SlideO` when the stack empties.** `Mem_to_SlideO(0)` passes its own
  `0 <= count` guard and copies slot 0, which nothing ever writes, so it is still zeroed. Depends
  on `TICEMEM.Objects` starting zeroed and `LoadLevel` resetting only `count` — which is exactly
  what the C global does.
- **`IceMoveO` tests the tank's square separately from `CheckLoc`**, because `CheckLoc` does not
  know where the tank is; without that test an object slides into the tank.

**Phase 2 is done. `MouseOperation` is the only unported function** — the mouse buffer is empty
headless (`MB_TOS == MB_SP`), so no keystream reaches it. It still throws. It is a UI entry point,
Phase 5 work, not game logic.

**Next: Phase 3, differential fuzzing.** 187 recordings are 187 paths; the flagship collection
alone is 2,030 levels the corpus never touches, and 20,914 across all 13. Random keystreams
weighted toward fire/turn, both engines, diff, shrink any divergence to a minimal repro. Two things
worth building into it from the start: the trace comparison is already the oracle, and
`NotPortedException` proved twice this phase that a loud stop beats a plausible answer — keep
`MouseOperation` throwing rather than teaching the fuzzer to avoid it.

### 2026-09-05 (session 8) — Phase 3: the fuzzer, and proving it works

Three new tools plus a self-test; nothing in `src/` or `oracle/` changed, and `Engine.cs` is
byte-identical to where it started.

- **`tools/engines.py`** — run both engines on one input, compare, classify. It *imports*
  `difftrace.py` rather than reimplementing the comparison, so a sweep verdict and a
  `difftrace.py` verdict cannot drift apart. Returns a `Div` whose `sig` is the first field that
  moved, values stripped: `T.dir`, `PF`, `S.moves`, `length`, `NOTPORTED`. That signature is the
  dedup key *and* what the shrinker holds fixed.
- **`tools/sweep.py`** — the empty-keystream sweep, out of scratch and into a tool. Bare, it is
  the documented check: **2,347/2,347 identical** with `--field --bmf`, 49 s.
- **`tools/fuzz.py`** — the phase. Weighted random keystreams (`--p-fire`, `--p-repeat`), both
  engines, diff, shrink, one directory per finding.
- **`tools/test_fuzz.py`** — the reason to believe any of it. **25 passed, 0 failed.**

**Fault injection, and it works.** The two faults the plan named, both caught:

| fault | found | shrunk |
|---|---|---|
| `AntiTank` scans reordered | 93 cases, 4 s | 48 → **2** keys: level 765 `dd`, `L.x` at tick 8 |
| `MoveTank` `SlideT` write moved into the `if` | 2 cases, 2 s | 48 → **2** keys: level 1314 `rr`, `SlT.dx` at tick 2 |

Level 765 is worth looking at, because it is the shape of report the shrinker is for: the tank at
(4,9) has an anti-tank aligned to its right *and* one aligned above it, so the reorder changes
which one fires — oracle laser at `5,9 dir 4`, injected core at `4,1 dir 3`. The tank dies either
way; only the trace tells them apart. Two keys, and the level's ASCII is in the report.
Level 1314 catches the other fault at the *board edge*: `rr` turns then bumps into the wall at
x=15, and `CheckLoc` returns off-board without writing `wasIce`, which is quirk #3's exact shape.

**Three things this session got wrong first and fixed, all worth not repeating:**

- **`bash` from Python is WSL's `System32\bash.exe`**, not Git Bash — different filesystem, no
  gcc, no dotnet, and the failure is an `execvpe` error rather than anything readable.
  `engines.find_bash()` skips System32 and falls back to the Git for Windows paths; `$LT_BASH`
  overrides.
- **Restoring a patched source file with `write_text` rewrites every line ending.** Engine.cs is
  LF; Python's text mode made it CRLF; `core.autocrlf` made `git diff` show nothing while the
  file on disk had all 1,362 lines changed. `test_fuzz.py` now patches and restores in bytes, and
  a green self-test leaves the tree byte-clean.
- **The quirk packs mix `.lvl` and `.LVL`.** The first campaign's shell glob silently skipped
  `tutor`, `tutor-with-playbacks`, `rotary-mirrors` and `game-objects` — the four biggest packs,
  no warning. `sweep.py` had the same latent bug (`glob("*.lvl")` is case-insensitive on Windows
  and would have dropped four packs on Linux). Both fixed to match `replay_all.py`'s
  `suffix.lower()`.

**Campaign: 20,626 cases, 3,751,638 tick-lines, 0 divergences** — 24× the recorded corpus's
156,504 tick-lines. Six keystream shapes over all 2,030 flagship levels and all ten quirk packs.
141 random keystreams won their level; 10,758 killed the tank.

`fuzz.py` reports **keys consumed, not keys generated** (55%: 890,690 of 1,630,624). The gap is
the honest limit of this technique: random play is wide but shallow, over half the runs end
`DEAD`, and level 1 alone needs 149 keypresses to win. That is the case for Phase 4's solver as
the *complementary* kind of coverage — a solved level is a long, legal, non-random path.

`MouseOperation` still throws and was never reached (`NOTPORTED` 0). Reaching it stays a finding,
not an obstacle: `engines.compare` classifies a `NOTPORTED` footer as its own signature, and
`test_fuzz.py` asserts that classification.

Gates at the end of the session, all green: `test_difftrace.py` 29/29, `test_fuzz.py` 25/25,
`sweep.py` 2,347/2,347, `replay_all.py` on *both* engines 187 replayed / 181 win / 6 documented
non-winners / 0 unexpected / 112/112 `.ghs` exact.

**Next: more fuzzing (new seeds, the other 12 collections — 18,884 levels the campaign has not
touched, longer keystreams), and Phase 4.**

### 2026-09-05 (session 9) — Phase 4, layer 0: the search API and the harness

Not the solver — the thing the solver stands on, plus the gate that makes its claims mean
something. `Engine.cs` changed by exactly one word (`partial`); everything else is new files.

**Measured the bar before building.** The `.ghs` targets say the median level needs 126
moves+shots and even the median *Kids* level needs 46; only 1,771 of 20,914 are ≤20 total. And
`ScoreMove` counts moves, not keypresses — `MoveTank` spends a key on a turn and scores nothing
(`Engine.cs:491`) — so the real search depth is worse than `.ghs` suggests. That killed the
original "IDA* over keypresses" plan as a whole-corpus strategy on the spot, and is why the Phase 4
section is now a layer plan.

**Two findings that changed the design, both from reading rather than guessing:**

- **The wait exists, and no human ever used it.** `LTANK.C:616`'s switch has no `default` and
  `RecP++` runs regardless, so any byte outside the five keys is a legal one-tick wait that still
  gives the anti-tanks their turn. Histogrammed all 187 `.lpb` — 54,162 bytes, every one of the
  five keys, zero waits. The "wait" in the tutor hints is free non-quiescent time (conveyor, ice),
  which needs no byte. Action set is therefore the five keys. While checking this, found that
  PROGRESS's own "Tick order" section listed `AntiTank()` as an unconditional step 5; it is inside
  the key-consume `if`. Fixed — that section is labelled "this *is* the spec".
- **The technique library already exists, curated.** `data/quirks/tutor` is 92 levels with one
  named trick each ("Moving a dead anti-tank", "Tank-Mover Entry", "Collisions - Tunnel Exit").
  That is layer 2's operator list, pre-isolated by humans. Conversely, hint-mining is *small*:
  only 175 of 20,914 hints are recipe-grade, though they concentrate on the hard tiers.

**Two bugs the harness found on itself.** Both are in the Phase 4 section in full; the short form:
a macro-step is not bounded by anything cheap (level 1491 takes **3,652 ticks for one keypress**,
riding a conveyor circuit — so `ApplyKey` detects cycles rather than capping ticks), and `Restore`
rewinding `RecP` while `RecBuffer` stays shared silently corrupted every breadth-first answer
(IDA* was green the whole time; only the beam's solutions failed to replay). The second was caught
solely because the harness replays each solution before writing the `.lpb` — a self-check that
looked redundant when it was written.

**Result: 150 cheapest-by-`.ghs` flagship levels, 110 solved in 20 s wall, 110/110 verified
through both engines with `--field --bmf`, 73 matching the `.ghs` record exactly.** Kids 81.5%,
Easy 60.7%, median 1.6× the record. The 40 unsolved: 30 on budget, 5 beam dead-end, 5 IDA* depth —
which argues for layer 1 (macro-actions), not for a bigger budget.

Gates re-run and green after the change: `replay_all.py` on **both** engines 187/181/6/0 with
112/112 `.ghs` exact, `test_difftrace.py` 29/29, `sweep.py` 2,347/2,347 identical.

**Next: run the campaign over the real collections, then layer 1.**

### 2026-09-05 (session 10) — the campaign, and Phase 4 layer 1

Layer 1 is built, the whole corpus has been through both layers, and the useful output of the
session is a negative result with a fix attached.

**The campaign methodology changed first, and that is its own finding.** The first run was
wall-clock-budgeted (4 s a level) and was thrown away: the gates and the tuning benches were
running beside it on the same 16 cores, so its budget bought a varying amount of work and its
per-tier rates were not reproducible. Campaigns are now governed by **`--nodes`, an `ApplyKey`
count** — equal work, load-independent, comparable between layers — with `--budget-ms` demoted to
a backstop, and they take a **`--stride`** sample (the corpus at a real budget is ~6 h per layer,
and the question was per-tier rates, not banked solutions). New: `tools/campaign.sh`,
`tools/second_pass.sh`, `tools/report_stats.py`.

**Layer 1: `Goto` + `Shoot`, with `Goto` a closure over the engine rather than a grid A*.** That
choice is the one to keep: a hand-written A* would have to re-derive `MoveTank`'s turn-costs-a-key
rule, `IceMoveT`, `ConvMoveTank` and `TranslateTunnel`, i.e. become a second implementation of the
game after four phases spent ensuring there is one. Ice, conveyors, tunnels, pushes and anti-tank
turns are resolved by being *executed*.

**Three measurements, and the first two were misleading — this is the session's real content.**

- On 60 levels layer 0 failed, layer 1 wins big and stably: 18→28, 23→31, 33→38 at 150k/400k/1M.
- On 50 deep levels it does not win at all: 12 vs 13, 13 vs 13, and 8–9 for the macro beam alone
  at *every* parameter setting tried. Parameters are not the lever.
- On 4,185 real corpus levels it **loses**: 395 → 381 as a first probe, 395 → 354 run last.

The first bench measured a population where the raw beam is 0% by construction, so anything the
macro beam added was free. The corpus is not that population. **Both portfolio orderings fail for
the same reason: every node the macro beam spends is a node taken from the beam, and on most
solvable levels the beam wanted it.** The fix is not a share or an ordering — it is to stop making
the bet in advance. Layer 1 now ships as a **second pass** over the first campaign's failures
(`RunMacro` off by default, `--macro` to enable). Composite: **395 → 416, nothing lost.**

**Two things that look like bugs and are not, both measured before being kept.** The beams close a
state at *generation*, so the width trim is permanent — "fixing" that costs 33→27 on the raw beam
and 36→30 on the portfolio, because the budget is nodes and over-pruning buys depth. And layer 1's
large `macro-dead-end` count is a symptom of that policy, not a leak. Both are now `--closed
generate|expand` with the measured default and the reasoning in `Search.cs`.

**Verification is the strongest artefact here.** 416/416 composite and 381/381 layer-1 solutions
replay byte-identically through the *unmodified* C oracle and the C# core with `--field --bmf` —
**797 solver-produced winning recordings, zero divergences**, 280 matching the `.ghs` record
exactly. Long legal winning paths are coverage random keystreams cannot reach.

Gates green after every change: `replay_all.py` on both engines 187/181/6/0 with 112/112 `.ghs`
exact, `test_difftrace.py` 29/29, `sweep.py` 2,347/2,347 identical.

`Engine.cs` and `Engine.Search.cs` are **untouched** — `git diff src/LaserTank.Core/` is empty.

**Next: layer 2.** The shape of layer 1's failure is the argument for it. Inside a `Goto` movement
is exhausted rather than searched, so the beam ranks *board changes* and `WorkDistance` is thin at
those — a shot beam has to guess which of two hundred available shots matters. The reason to fire
has to be **derived** (this brick is on the only path), not scored.
