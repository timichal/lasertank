# LaserTank → Godot port: the game

**Purpose:** single source of truth for the *port* — the oracle, the C# engine, the fidelity gates,
the file formats, the quirks, and Phase 5 (Godot). Read this first after a context clear.

**The solver lives in [`SOLVER.md`](SOLVER.md)** and is a goal in its own right — solving every one of
the 20,914 known-solvable levels, mostly to prove it can be done. It is *also* the second differential
test of this port, and it is deliberately not on the critical path for correctness: nothing in this
file depends on it, and the port could ship without it. That independence is a property of the
validation plan, not a ranking of the two halves.

---

## Start here after a context clear

**Where the project is.** Phases 1-3 complete: a C reference oracle (`oracle/`), a C#
transliteration that traces byte-identically to it on the whole recorded corpus
(`src/LaserTank.Core/`), and a differential fuzzer (`tools/fuzz.py`) with 20,626 cases and no
divergences. Phase 4 (the solver) is in `SOLVER.md`. **Phase 5 (Godot) is not started** and its
section below is a plan with exit criteria, not a wish list.

**Build, then check nothing rotted** (about three minutes all in):

```bash
bash oracle/build.sh && bash src/build.sh      # -> build/lasertank-{core,solve}.exe
python tools/replay_all.py                     # 187 replayed, 181 win, 6 documented non-win
python tools/test_difftrace.py                 # 29 passed
python tools/sweep.py                          # 2,347/2,347 identical
python tools/test_fuzz.py                      # 25 passed  (slow: injects faults and rebuilds)
```

Those four are the fidelity gates and must be green before anything else is believed.
`test_fuzz.py` patches `Engine.cs` and restores it — a green run leaves the tree byte-clean, and if
it ever does not, read the line-ending trap in *Environment notes* before anything else. Never run
it while a solver process is alive (see the same section).

**What is deliberately frozen.** `original/` is a read-only historical artifact.
`src/LaserTank.Core/Engine.cs` differs from a literal transliteration by the single word `partial`,
and `Engine.Search.cs` has not changed since the solver's first layer. **If a solver change seems to
need an engine change, that is the signal to stop and re-read.**

**Artifacts live under `build/`, which is gitignored** — they survive a context clear but not a
`git clean`. Everything there is a measurement that can be re-run.

---

## Status

**Phases 1-3 complete. Phase 5 not started.**

| | state |
|---|---|
| C reference oracle | replays the whole corpus; ground truth, never refactored |
| C# core | **byte-identical to the oracle on all 187 recordings** with `--field --bmf` |
| Differential fuzzer | harness proven by fault injection; 20,626 cases, 0 divergences |
| Solver | four shipped layers, five more as driver rungs; 11.3% of a 4,185-level sample against a goal of all 20,914 — see `SOLVER.md` |
| Presentation (Godot) | not started — see Phase 5 |

**The gates, and what green looks like.** `replay_all.py` 187 replayed / 181 win / 6 documented
non-winners / 0 unexpected, and 112/112 `Tutor-with-Playbacks` matching their bundled `.ghs` on
moves *and* shots. `test_difftrace.py` 29 passed. `test_fuzz.py` 25 passed. `sweep.py` 2,347/2,347
identical. `tools/verify_solutions.py` over any solver output — every `.lpb` wins on both engines
with byte-identical traces.

**What is still not ported: `MouseOperation`, and only that.** The mouse buffer is empty headless
(`MB_TOS == MB_SP` always), so the tick's mouse block never fires and no keystream can reach it —
measured, not assumed: the fuzz campaign reached it zero times. It **throws** rather than no-ops, so
if that premise ever breaks the run stops loudly. It is Phase 5 work: a UI entry point, not game
logic.

**Next action for this half of the project: Phase 5, step 0.** Phase 3's fuzzer can keep running in
parallel on new seeds and the 12 collections its first campaign never touched.

**Blocked on:** nothing.

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
- So the solver is never a *prerequisite* for the port — do not let it become the critical path for
  correctness. It pursues its own goal (`SOLVER.md`) and hands this half a stream of long legal
  winning keystreams for free.

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

## Phase 1 — Oracle & tooling  ☑

- ☑ Build `LTANK2.C` headless, **preserving logic-carrying side effects**. Key move: stub only the
  Win32 *API*, never LaserTank's own code — see `oracle/README.md`. `UpDateLaserBounce` compiles
  verbatim, so hazard #1 survives for free.
- ☑ Drive from a keystream file; emit per-tick trace (format in `oracle/README.md`).
- ☑ Parsers for `.lvl` / `.ghs` / `.lpb`.
- ☑ Replay all 187 corpus `.lpb` (186 shipped + level 21 decoded from its packed `.txt`).
- ☑ For `Tutor-with-Playbacks`, assert move/shot counts match its bundled `.ghs` — **112/112 exact**.

**Exit criterion — met, restated.** "186/186 reach the flag" was the wrong bar: the corpus does not
contain 186 solutions. 181 of 187 reach the flag; the other 6 are documented incomplete recordings.
The suite encodes that distinction, so it is a real regression gate rather than a permanently red
run. See *The six non-winning recordings*.

The load-bearing decision that made everything after it possible was measuring the external surface
first (`nm -u`: 49 calls, ~12 globals), which turned "port a Win32 game" into a bounded job.

---

## Phase 2 — Transliterate the core  ☑

Six steps, each gated on the whole corpus:

- ☑ **step 0:** `tools/difftrace.py` — compare two traces (or two directories of them), report the
  first diverging tick and the first field that moved. Nothing downstream is checkable without it.
  Self-tested by `tools/test_difftrace.py`, and fault-injected seven ways.
- ☑ **step 1:** the C# projects build; the load path and the tick frame are transliterated, and an
  empty-keystream run traces identically to the oracle.
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
from a keystream. `BMF`/`AniLevel` differences would be cosmetic (hazard #2) — `difftrace.py`
encodes that distinction: exit 1 for a logic divergence, exit **3** for a cosmetic-only one, and
`--strict` to hold the cosmetic line too.

### The Phase 2 harness

```bash
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
divergence rather than as a parser bug. Trace line 1 differs by design (`# lasertank core trace` vs
`# lasertank oracle trace`); line 2 and every tick line are byte-identical, and `difftrace.py`
reads level/name/author/keys off line 2 to check both sides ran the same input.

**Run both engines with `--field`** (and `--bmf` while touching `Animate`) so the diff has the whole
playfield to bite on, not just the hashes.

**When a trace stops early.** `NotPortedException` used to take the process down before the
buffered trace was flushed, so a partly-ported engine produced *no* trace and `difftrace.py`
reported `UNUSABLE` — no signal at all. The CLI now catches it, closes the trace after the last
*complete* tick, writes `# result=NOTPORTED`, prints the function and the tick to stderr and exits
**4**. `difftrace.py` then says `DIVERGE length mismatch after N ticks`, which is the useful
reading: the ported half matched for N ticks and the port continues at N+1. `NOTPORTED` is not a
result.

**The first milestone, before any game logic — worth re-using for any future port.** Run with an
empty keystream (`--keys ""`). Both engines trace exactly two lines, `t=0` and `t=1`: level load,
then one idle tick. Matching those two means the `.lvl` parser, the `TGAMEREC` layout,
`BuildBMField`, `PutLevel`, `Animate`, the fnv1a hashes and the trace formatting are already right.

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

### What the C# side looks like

```
src/LaserTank.Core/    Objects.cs  GameState.cs  LevelFile.cs  Engine.cs
                       Engine.Search.cs — snapshot/restore, ApplyKey, StateHash (for the solver)
src/LaserTank.Cli/     Program.cs  TraceWriter.cs      -> build/lasertank-core.exe
```

Ported: `BuildBMField`, `PutLevel`, `UpDateTank`'s `TankDirty` write, `GameOn`, `Animate`, the
logic-carrying half of `LoadNextLevel`, the whole `Tick()` frame, the `SendMessage`/`PostMessage`
death split (quirk #8: `SendDead` runs inline, `PostDead` queues for `Pump()` after the tick,
exactly as the oracle's stub message pump does), and then every function a keystream can reach:
`CheckLoc`, `TranslateTunnel`, `UpDateTankPos`, `UpdateUndo`, `ResetUndoBuffer`, `MoveObj`,
`MoveTank`, `AntiTank`, `FireLaser`, `MoveLaser`, `CheckLLoc`, `KillAtank`, `UpDateLaserBounce`,
`TestIfConvCanMoveTank`, the `SlideO`/`SlideMem` helpers, `ConvMoveTank`, `IceMoveT`, `IceMoveO`.

**`MouseOperation` is the only remaining stub, and it throws rather than no-ops.** A silent stub
would produce a *plausible* wrong trace, which is the one failure mode this whole approach exists to
prevent — and step 4 turned that from an argument into an incident report: the tick frame had been
passing `0` instead of `S_Fire` to `FireLaser` since step 1, and only the exception kept it from
silently corrupting `laser.Good`.

**Worth knowing: `AntiTank` is a `wasIce` writer** even though it never names the flag. Its four
scans are `while (CheckLoc(...))` loops, so whichever scan ran last leaves `wasIce` holding its
final probe — and `MoveTank`, `IceMoveT`, `IceMoveO` and `ConvMoveTank` all read it after their own
`CheckLoc`. That is quirk #3 with a longer reach than the three callers the hazard list names.

Decisions worth not relitigating:

- **`byte[16,16]`, not `sbyte`.** `PF`/`PF2`/`BMF`/`BMF2` are `char[16][16]` in C, and gcc's `char`
  is signed. It cannot matter: `BuildBMField`'s 2003 sanitisation forces every cell to `<= 0x19`
  or to a tunnel (`0x40 | id<<1 | wait`), so nothing above `0x7F` survives a load. The single place
  the original's signedness is visible is `GetOBM(char)`'s `ob > -1` guard, which `Obj.GetOBM`
  keeps verbatim.
- **Original names, not C# conventions.** `Game`, `ScoreMove`, `SlideO`, `wasIce`, `IceMoveO`.
  Renaming is how a quirk stops looking like a quirk.
- **`net8.0` for both projects.** Lowest TFM Godot 4.x accepts, so Phase 5 can reference
  `LaserTank.Core` unchanged; the CLI sets `RollForward=LatestMajor` because only the .NET 10
  runtime is installed.
- **The undo buffer is carried even though nothing headless reads it.** `UndoStep` is unreachable
  from a keystream, so the `TGAMEREC` snapshots `UpdateUndo` stores are write-only. `UndoP` is not:
  `MoveObj`'s tunnel path decrements it (quirk #7), so its growth (`UndoBufSize` in steps of 200)
  and its roll-over at `UndoMax` have to be exact, and the cheapest way to be sure is to keep the
  buffer they index into. The two `GlobalReAlloc == NULL` branches in
  `UpdateUndo`/`ResetUndoBuffer` are *not* carried: the oracle's stub is plain `realloc`
  (`oracle/win32_stub.c:74`), so they are unreachable on both sides. They are the only pieces of
  either function left out.

---

## Phase 3 — Differential fuzzing  ◐ (harness done and proven; more campaigns welcome)

Random keystreams (weighted toward fire/turn), both engines, diff traces, shrink any divergence to a
minimal repro. Runs across all 20,914 levels. Cheap, unlimited, needs no solutions.

```bash
bash oracle/build.sh && bash src/build.sh
python tools/test_difftrace.py                       # trust the differ      29 cases
python tools/test_fuzz.py                            # trust the fuzzer      25 cases, ~90 s
python tools/sweep.py                                # 2,347 levels, empty keystream, ~50 s
python tools/fuzz.py --each 3 --seed 1               # 6,090 cases over the flagship, ~140 s
```

**Run `test_fuzz.py` before believing any green fuzz run.** It is the one step that is easy to skip
and the one the whole phase rests on: a fuzzer that has never gone red is untested, and "20,626
cases, no divergence" is then a claim about the fuzzer rather than about the port. It patches
`Engine.cs` with two faults a transliteration would plausibly make, rebuilds, and requires that each
is found, shrunk and independently reproducible — then restores `Engine.cs` **in bytes** and requires
green again. If it is ever killed between the patch and the restore:
`git checkout src/LaserTank.Core/Engine.cs`.

**The shrinker is the deliverable, not an extra.** A divergence at key 300 of 400 on level 1,712 is
not a bug report; `level 1712, keys "rrfud"` is. `fuzz.reduce_keys` is three passes: shortest
diverging prefix by binary search (where nearly all the length goes), delta debugging (ddmin) over
what is left, and then **measuring** 1-minimality rather than claiming it — delete each remaining
key and check the divergence goes away. All three hold the divergence **signature** fixed (the name
of the first field that moved, which is `difftrace.py`'s localisation), so what comes out is a
reduction of the bug you started with and not a different one found along the way. `--shrink-any`
relaxes that.

Each finding gets a directory under `--out`: the minimal keystream, both traces re-run with
`--field --bmf`, the full `difftrace.py` report, the level as ASCII, and the exact commands, plus
`findings.json` for scripts. Findings are deduped by signature, so a systematic bug reports once
rather than 9,000 times.

**Fuzzing runs without `--field`/`--bmf`** — those add 512 hex bytes per tick each, and the default
trace already carries `H=fnv1a(PF),fnv1a(PF2)`, so a playfield divergence still shows up as a hash
rather than a cell. The minimal repro is then re-run *with* both, and the tool warns if the wider
trace does not reproduce the same signature.

### What the first campaign covered

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

**Random keystreams are shallow.** Over half of all runs end `DEAD`, and the flagship's own level 1
needs 149 keypresses to win. Random play is wide but not deep, which is the argument for the solver
being the *other* kind of coverage rather than a nice-to-have: a solved level is a long, legal,
non-random path through the engine. (Nine hundred-odd verified solver recordings have now been
replayed tick-for-tick through the 25-year-old C with zero divergences.)

---

## Phase 5 — Presentation & features  ☐

**This is the first phase where the deliverable is the game rather than a measurement, and the
discipline that got the project here still applies: the presentation layer must not become a second
implementation of the rules.** Everything in `LaserTank.Core` stays untouched — a Godot node reads
`Game.PF` and `Game.BMF` and draws them; it never decides anything. That is the same contract the
solver kept for nine layers (`Engine.Search.cs` unchanged throughout), and it is why the fidelity
gates keep working while this phase is built.

**The head start, which is larger than it looks.** The renderer's input is already computed and
already ported. `BuildBMField` maintains `Game.BMF[x][y]` — the *bitmap number* per cell, not the
object id — `Animate()` cycles it, and `Obj.GetOBM()` is the object→bitmap table (`LTANK2.C:77`).
So drawing a frame is: for each cell, look up `BMF`, index the sprite atlas, blit. Hazard #2 is what
makes this safe (no bitmap ever feeds a decision) and hazard #1 is what makes it dangerous
(`UpDateLaserBounce` is a *paint* function that mutates game state): **the core already calls it
inside the tick — the renderer must not call it, skip it, or reimplement it.**

### The steps, each with an exit criterion

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
*Exit — and this is Phase 5's real gate:* play a level in Godot, win it, save the keystream with the
existing `LevelFile.WritePlayback`, and **that `.lpb` must replay byte-identically through the
unmodified C oracle** (`tools/verify_solutions.py` already does exactly this, unchanged). A human
playthrough that survives the oracle is the same proof every solver solution gives.

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

### What to be careful about

- **Do not "fix" anything on the way past.** Hazards #9 and #11 are both real bugs in the original
  that must survive, and #11 is inside `SetGameSize` — a function this phase has to touch.
- **The 20 Hz tick is not a rendering rate.** Interpolate sprites between ticks; a 144 Hz display
  must not consume 144 keys a second.
- **`.lpb` compatibility is bidirectional.** The 2010 binary must be able to play what Godot
  records. `LevelFile.WritePlayback` already writes the real 66-byte header format, and the
  solver's recordings are the existing evidence that it round-trips.
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
   consumed; on a tick with no key they do not play. (An earlier revision of this list showed
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
   Still worth tracing: a BMF divergence is a cheap tripwire for a transliteration slip.
3. **`wasIce` is a hidden return channel** from `CheckLoc()` (`LTANK2.C:1278`), read by three callers
   — and written by `AntiTank`'s four scans as well, so its reach is longer than those three.
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
12. **`LoadLevel` does not reset the stale flags, so one `Engine` cannot replay two keystreams.**
    It resets `PF`, the tank, `RecP` and the slide records; it leaves `wasIce`, `WaitToTrans`,
    `ConvMoving` and `BlackHole` exactly where the previous game left them — faithfully, because
    the original never reloaded a level into a fresh process either. The consequence is a rule for
    *our* code rather than a quirk in the original: **anything that replays candidate keystreams
    must build a fresh `Engine` for each one.** The solver's trimmer did not, and silently reported
    winning keystreams as losing (see `SOLVER.md`, the polishing addendum). The search itself is
    unaffected: `Restore()` puts all four flags back, which is exactly why `EngineSnapshot`
    carries them.

---

## Repo layout

```
original/   the frozen 25-year-old artifact — read-only
  src/        2007 source distribution, verbatim
  bin/        shipped 2010 lasertank.exe + LTUDU data updater
data/       game content = the regression corpus
  levels/     13 collections, 20,914 levels, all with .ghs targets
  quirks/     10 tutorial/trick packs, 317 levels, 187 .lpb recordings
  demos/      human playthroughs — recorded by hand, cannot be regenerated, and
              deliberately NOT under build/solutions/ so a hand solution is never
              mistaken for a solver one
  solutions/  what the interactive driver banks — one hand-supervised level at a
              time, each already through the two-engine gate
  graphics/   .ltg packs      meta/  changelogs & name indexes
oracle/     the C reference oracle — see oracle/README.md
  stub/       minimal <windows.h> that shadows the real one
  win32_stub.c  real memory/files/messages, no-op GDI
  driver.c    LTANK.C globals + window proc + the WM_TIMER tick loop + tracing
  build.sh    gcc -x c -I stub -I original/src
src/        the C# port         build.sh -> build/lasertank-core.exe + lasertank-solve.exe
  LaserTank.Core/  Objects.cs GameState.cs LevelFile.cs Engine.cs  (no Godot here)
                   Engine.Search.cs — snapshot/restore, ApplyKey, StateHash
  LaserTank.Cli/   Program.cs TraceWriter.cs — the oracle's CLI, the oracle's trace
  LaserTank.Solver/ the batch solver and the interactive driver — see SOLVER.md
build/      C# output (gitignored)      LaserTank.slnx  the solution
tools/      see below; the solver-only tools are listed in SOLVER.md
```

The fidelity tooling:

```
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
verify_solutions.py replay every .lpb through BOTH engines: WIN on each,
                    byte-identical traces, and the ratio to the .ghs record.
                    --levels names the .lvl instead of finding it by directory name
bump_rate.py      classify consumed keys; bumps = desync signature
dump_level.py     print a .lvl level as ASCII with its hint
unpack_lpb_txt.py decode a Text-Converter .txt wrapper back to .lpb
```

See `README.md` and `data/SOURCES.md` for provenance.

---

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
| `rotary-mirrors` | 39 | 39 | 6 of these do not reach the flag — see below |
| `tricks` | 26 | 0 | |
| `pono-trick` | 18 | 20 | more LPBs than levels (alternate solutions) |
| `game-objects` | 16 | 16 | one level per object — **best first target for the oracle** |
| `4triang`, `telek-1`, `l40`, `inchworm` | 14 | 0 | |

**Tier 2 — `data/levels/`, 20,914 levels across 13 collections**, every one with a non-zero `.ghs`
entry. No keystreams, but a solvability guarantee and a (moves, shots) target for each. This is the
fuzzing surface for Phase 3 and the population `SOLVER.md` measures against.

**Tier 3 — `data/demos/`, 20 hand playthroughs of `LaserTank.lvl` 1-19**, recorded by Michal and
verified through both engines. They cannot be regenerated and they are the only long-level winning
lines that are not solver output — which is what makes them the solver's instrument population.

---

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

---

## Environment notes

- Two Pythons, both fine: `python` = 3.12.7 (miniforge), `python3` = 3.14.7. Everything in `tools/`
  is stdlib-only and verified on both — keep it that way so either alias works. **One exception:
  `tools/fit_eval.py` imports `numpy`** (least-squares on the rank dump). It is a layer-4
  instrument, not a gate, so no gate and no build depends on it — but the alias rule really does
  break there: `numpy` 2.3.2 is installed for `python` (3.12 miniforge) and **not** for `python3`
  (3.14), so `fit_eval.py` runs under `python` only. A second carve-out should be argued for rather
  than assumed.
- C toolchain: MinGW-w64 (WinLibs gcc 16.1, UCRT), `winget install BrechtSanders.WinLibs.POSIX.UCRT`.
  Not on `PATH` globally; `oracle/build.sh` finds it under `~/AppData/Local/Microsoft/Winget/Packages/`.
- **.NET SDK 10.0.400**, `winget install Microsoft.DotNet.SDK.10`, at `C:\Program Files\dotnet`.
  Same story: not on the shell's `PATH` until it restarts, so `src/build.sh` finds it. Only the
  .NET 10 runtime is present, hence `RollForward=LatestMajor` on the `net8.0` CLI.
- **Godot 4.7.2 (.NET/Mono build)**, `winget install GodotEngine.GodotEngine.Mono`, unpacked under
  `~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_*/`. The `godot` alias
  needs admin to be created, so call the `.exe` by path. Nothing before Phase 5 needs it.
- **Trap in the oracle's own usage text:** it advertises `--keys` as accepting "raw decimal VK
  codes separated by commas", but `driver.c` only parses the characters `u d l r f` and silently
  skips everything else. `--keys 38,38,32` therefore yields an *empty* keystream and an idle run
  that looks like it worked. The C# CLI matches this behaviour deliberately (same parser, same
  skipping) — if you fix one, fix both and re-run the corpus.
- **`bash` invoked from Python is WSL's `System32\bash.exe`**, not Git Bash — different filesystem,
  no gcc, no dotnet, and it fails with an unreadable `execvpe` error. `tools/engines.py`'s
  `find_bash()` skips System32 and falls back to the Git for Windows paths; `$LT_BASH` overrides.
  Any new tool that shells out should use it rather than bare `bash`.
- **Rewriting a source file from Python in text mode rewrites every line ending.** `src/` is LF,
  Python's text mode makes it CRLF, and `core.autocrlf=true` then makes `git diff` show *nothing*
  while every line on disk has changed. Patch and restore in **bytes**, or pass `newline=''`.
  `tools/test_fuzz.py` does, and a green self-test leaves the tree byte-clean.
- **Never run `test_fuzz.py` while a solver process is alive.** It rebuilds the core, and Windows
  keeps `build/LaserTank.Core.dll` locked open by every running `lasertank-solve.exe`, so the
  rebuild loses its retry ladder and *both* the "clean core builds" control and the restore check
  report `FAIL` — a red gate that is entirely the machine. The tell is `MSB3027 ... The file is
  locked by: "lasertank-solve (NNNNN)"`. The tree is still left byte-clean, so the fix is to wait
  and re-run. Same trap in the other direction: `src/build.sh` cannot replace the binary while a
  search is running.
- **A backslash does not survive `python - <<'EOF'` in this harness.** Rewriting a Markdown file
  through a Python heredoc silently turns `\n` into a real newline and eats `\` line continuations.
  Use the editing tools for anything containing a backslash, or write the replacement text to a
  file first and read it in.
- **The quirk packs mix `.lvl` and `.LVL`**, and the four that ship uppercase are the four biggest
  (`tutor`, `tutor-with-playbacks`, `rotary-mirrors`, `game-objects`). A `glob("*.lvl")` is
  case-insensitive on Windows and silently drops them on Linux — it already cost one campaign four
  packs with no warning. Match on `suffix.lower()`, as `replay_all.py`, `sweep.py` and
  `verify_solutions.py` all now do.
- laser-tank.com is behind Cloudflare: `WebFetch` returns 403. Use `curl` with a browser
  User-Agent. The site is a frameset — real content is in `menu.html`, `help.html`, `levels.html`.
- Original build was lcc-win32 (`original/src/_How to compile LTank.txt`, `LTank.prj`). Its
  dependencies are shallow; MinGW/clang work.

---

## Open questions

- **Which binary is the behavioural reference?** `original/bin/lasertank.exe` is dated 2010;
  `original/src/Setups/Files/lasertank.exe` is the 2007 build matching this source. Both are
  UPX-packed and 148,512 bytes but differ across ~95% of their bytes, so the version can't be read
  off without unpacking. `Bugs.txt` stops at 4.1.2 (2005), so the 2010 build may contain changes
  we have no source for. Resolvable by trace-diffing the oracle against both.
  The Tutor readme warns: *"made/verified using LaserTank.exe Ver 4.1. The use of earlier versions
  may cause different results."*

---

## Cross-references — do not trust for quirk fidelity

- `github.com/tobiasvl/lasertank` — mirror of this same source.
- `github.com/h4tr3d/laser-tank` — SDL2/C++ port, but descends from a KolibriOS *reimplementation*.
- `lasertankpedia.zdobywca.com`, `lasertanksolutions.blogspot.com` — community game-data references.

The oracle is the only authority.

---

## Session log

The reasoning behind each phase lives in that phase's section above; this is the changelog, kept
short on purpose. Where a finding is still load-bearing it has been moved to where it belongs —
traps to *Environment notes*, engine hazards to *Quirk hazards* — so nothing here needs reading to
work on the project. **The solver's own log is in `SOLVER.md`.**

**2026-09-05, sessions 1-2 — Phase 1.** Read the source, decoded `.lvl`/`.ghs`/`.lpb` against the
real files, catalogued the quirk hazards, settled the three-engine architecture, reorganised the
repo. Then built the oracle on the decision that made everything after it possible: **stub only the
Win32 API and compile `LTANK2.C` verbatim.** Result: all 187 recordings replay, 181 win, 54,162
keypresses, 0 bumps, 112/112 `Tutor-with-Playbacks` matching their bundled `.ghs`. Hazard #2 was
*corrected* here rather than confirmed — animation is cosmetic — which is what makes replays
independent of the animation phase at load.

**2026-09-05, sessions 3-7 — Phase 2, the transliteration.** Six steps, each gated on the corpus:
the differ, then the core stood up, then `CheckLoc`/`MoveObj`, then `MoveTank`/`AntiTank`, then the
whole laser subsystem, then the ice and conveyor movers. Ended **byte-identical to the oracle on all
187 recordings** with `--field --bmf`. Two decisions earned their keep repeatedly: unported
functions **throw** rather than no-op (a stub would have hidden the `FireLaser(..., S_Fire)` bug for
three more steps), and the exit criterion was always the corpus rather than a spot check.

**2026-09-05, session 8 — Phase 3, the fuzzer.** `engines.py`, `sweep.py`, `fuzz.py` and
`test_fuzz.py`, with both planned faults injected, caught and shrunk to two keys each. Campaign:
**20,626 cases, 3,751,638 tick-lines, 0 divergences** — 24x the recorded corpus. The honest limit is
recorded with it: only 55% of generated keys are ever *consumed*, because random play is wide and
shallow, which is the argument for the solver as complementary coverage.

**2026-09-06, session 14 — the handoff itself.** No code. Turned Phase 5 from a seven-line wish list
into a plan with exit criteria, decoded the sprite atlas properly (320×192, a 10×6 grid of 32×32,
`BMA` row-major from i=1) and verified the `.ltg` header against all three packs — which corrected
two claims the plan would otherwise have shipped wrong.

**2026-09-07, session 23 — this file split in two.** `PROGRESS.md` is the port; `SOLVER.md` is
Phase 4. Nothing was measured or changed; both files are the same facts with the narrative of
superseded reasoning removed.

*Sessions 9-22 were all solver work and are logged in `SOLVER.md`. Their standing engine claim,
re-checked at the end of each: `Engine.cs` differs from a literal transliteration by the single word
`partial`, and `Engine.Search.cs` has not changed since the solver's layer 0.*
