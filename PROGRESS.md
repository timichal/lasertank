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
divergences. Phase 4 (the solver) is in `SOLVER.md`. **Phase 5 (Godot) is complete: steps 0-6 are
all done** — `src/LaserTank.Game/` is a playable game with the game around it, an editor, and ten
languages. It draws any level from `Game.BMF` with any of the four sprite sheets, runs a fixed
20 Hz tick, takes the keyboard through the original's own `WM_KEYDOWN` filter and on the original's
own accelerator keys, plays the original's sixteen WAVs off the sound ids the tick itself computes,
undoes, saves and restores a position, picks levels and shows both high-score lists out of
`.lvl`/`.hs`/`.ghs`, writes a `.hs` the 2010 binary would recognise byte for byte, records and plays
back `.lpb` at all three of the original's speeds, takes the mouse — both as a *move order* through
the original's own `MouseOperation` pathfinder and as the editor's brush — edits and saves a `.lvl`
byte-faithfully, shows its UI in any of the original's ten translations with a picker on Ctrl+L, and
remembers its graphics set, board size, sound, animation, auto-record, player initials, language and
the level you were on in a `LaserTank.ini` with the original's own section and key names.
`atlas_check.py`, `tick_check.py`, `options_check.py`, `sound_check.py`, `undo_check.py`,
`list_check.py`, `roundtrip_check.py`, `mouse_check.py`, `editor_check.py` and `lang_check.py` gate
those seven steps. **There are no stubs left in the transliteration**, and no step of Phase 5 is
left as a plan.

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

**Since 2026-09-08 the two halves of the project run on two machines** — this repo's *game* work
(this file) on one, the *solver* (`SOLVER.md`) on the other. So the live-solve trap below is no
longer the default case here: on the game machine `build/` is usually free and the plain build block
above works. It is kept because it still applies the moment a solve is started locally, which the
interactive driver makes easy to do without thinking about it.

**If a solve is running on this machine, that block does not work as written** and the first symptom
is a build failure, not a red gate: `src/build.sh` publishes into `build/`, which a live `lasertank-solve.exe`
holds open. Build into each project's own `bin/` and point the tools at it instead — and skip
`test_fuzz.py` until the solve is done, because it rebuilds the core:

```bash
dotnet build src/LaserTank.Cli/LaserTank.Cli.csproj -c Release
export LT_CORE=$PWD/src/LaserTank.Cli/bin/Release/net8.0/lasertank-core.exe
python tools/replay_all.py --engine "$LT_CORE"   # replay_all takes --engine, not $LT_CORE
python tools/sweep.py                            # everything on engines.py reads $LT_CORE
```

Phase 5 adds ten gates of its own, all about the presentation rather than the rules, so they are
listed apart from the four and nothing in Phases 1-4 depends on them:

```bash
python tools/atlas_check.py                    # step 0: 2,347 levels + 4 sprite sheets, ~35 s
python tools/tick_check.py                     # step 1: 208/208 vs the oracle + the rate, ~20 s
python tools/options_check.py                  # step 2: the INI, the packs, the laser's width, ~50 s
python tools/sound_check.py                    # step 3: 208 SoundPlay streams + 16 WAVs, ~60 s
python tools/undo_check.py                     # step 4: undo + save/restore vs the oracle, ~60 s
python tools/list_check.py                     # step 4: list rows + .hs bytes vs Python, ~25 s
python tools/roundtrip_check.py                # step 4: record -> replay -> oracle, ~150 s
python tools/mouse_check.py                    # step 5: MouseOperation vs the oracle, ~12 s
python tools/editor_check.py                   # step 5: the editor + the .lvl it writes, ~25 s
python tools/lang_check.py                     # step 6: 10 languages back to the 2007 bytes, ~25 s
```

All ten want Godot; `atlas_check` and `sound_check`'s WAV half degrade to a loud SKIP without it,
`undo_check` and `mouse_check` need only the two engines, `editor_check`'s third half and
`lang_check`'s fourth skip loudly without Godot and their earlier halves do not need it, the rest
need Godot outright. Every one that runs the
project **rebuilds its C# first**, because `godot --path` does not and would otherwise report green
for the previous session's assembly — see *Environment notes*. None of them touches
`build/lasertank-solve.exe`, so all are safe to run beside a live solver. `options_check` opens
three brief windows for its pixel measurements (`--shot` needs a rendering device); `--no-window`
skips that half. The corpus halves of `sound_check`, `undo_check` and `roundtrip_check` drive
`build/lasertank-core.exe`, which a live solve locks against rebuilding — `$LT_CORE` points them at
a locally built one instead (*Environment notes*). `lang_check` drives it too, for `--lang-dump`.

**What is deliberately frozen.** `original/` is a read-only historical artifact.
`src/LaserTank.Core/Engine.cs` differs from a literal transliteration by the word `partial`, twice:
on the class, and on `SoundPlay`, whose body moved to `Engine.Sound.cs` in step 3 while every call
site stayed identical. Step 3 also *restored* three `SoundPlay` calls Phase 2 had read as paint —
`S_Move`, `S_EndLev`, `S_Die` — and step 4 added `UndoStep` (`LTANK2.C:455`, the one function Phase 2
left unported for lack of a caller) plus commands 111 and 112. **Step 5 closed the last hole:
`MouseOperation` and `FindTarget` (`LTANK2.C:277`, `:298`) are transliterated, and `AddKBuff`
(`LTANK2.C:256`) moved into `Engine` because they call it — the drivers' copies now delegate.**
`Editor.cs` joined Core beside `GraphicsFile.cs`, on the same test: nothing inside `Tick()` can
reach it. All of those are a transliteration getting *closer* to the C, not further from it, and the
corpus proves each: 208/208 on the sound stream, 400 scripts of undo and 5,000 of mouse diffed
against the oracle's own `UndoStep` and `MouseOperation`, and 3,000 edit scripts against its own
`ChangeGO`. **Step 6 added `Language.cs` to Core and is the one part of the port that is
deliberately not a transliteration** — `LANGUAGE.C` reads a positional file the port does not ship,
so the ten translations were converted once into keyed UTF-8 JSON and the gate ties that JSON back
to the 2007 bytes instead. The argument is in step 6 below; the short version is that the thing
being ported is a *file format for a closed population of ten files*, not a rule.
`Engine.Search.cs` has not changed since the solver's first layer. **If a solver change
seems to need an engine change, that is the signal to stop and re-read.**

**Artifacts live under `build/`, which is gitignored** — they survive a context clear but not a
`git clean`. Everything there is a measurement that can be re-run.

---

## Status

**Phases 1-3 complete. Phase 5 complete: steps 0-6 all done.**

| | state |
|---|---|
| C reference oracle | replays the whole corpus; ground truth, never refactored |
| C# core | **byte-identical to the oracle on all 187 recordings** with `--field --bmf`, on 400 undo scripts and 5,000 mouse scripts with `--script`, and on 3,000 edit scripts with `--edit`. **No stubs left** |
| Differential fuzzer | harness proven by fault injection; 20,626 cases, 0 divergences |
| Solver | four shipped layers, five more as driver rungs; 11.3% of a 4,185-level sample against a goal of all 20,914 — see `SOLVER.md`. **Since 2026-09-08 the solver runs on the other machine, so treat that number as a last-known value rather than current** |
| Presentation (Godot) | **steps 0-6 done — the phase is finished**: a playable game with the game around it, an editor, and ten languages. Board renders from `Game.BMF`, all four sheets decode, 20 Hz tick, keyboard through the original's `WM_KEYDOWN` filter and its own accelerator table, the sixteen WAVs off the engine's own `SoundPlay` ids, undo / save / restore position, the level picker and both high-score lists, a `.hs` writer faithful to the byte, record and playback at all three speeds, the mouse on both of the window proc's arms (a move order through `MouseOperation`, and the editor's brush), a `.lvl` writer that round-trips an untouched level byte for byte, the original's ten translations on a Ctrl+L picker, and a `LaserTank.ini` that remembers ten keys under the original's names. Gated by `atlas_check.py` + `tick_check.py` + `options_check.py` + `sound_check.py` + `undo_check.py` + `list_check.py` + `roundtrip_check.py` + `mouse_check.py` + `editor_check.py` + `lang_check.py` |

**The gates, and what green looks like.** `replay_all.py` 187 replayed / 181 win / 6 documented
non-winners / 0 unexpected, and 112/112 `Tutor-with-Playbacks` matching their bundled `.ghs` on
moves *and* shots. `test_difftrace.py` 29 passed. `test_fuzz.py` 25 passed. `sweep.py` 2,347/2,347
identical. `tools/verify_solutions.py` over any solver output — every `.lpb` wins on both engines
with byte-identical traces. Phase 5's ten: `atlas_check.py` OK (2,347 levels clean, 4 sheets
cross-checked), `tick_check.py` 208/208 agreeing with the oracle plus 100 ticks in 5 s,
`options_check.py` OK (25 checks: the INI's semantics and round trip, the strict `Yes` test on all
four Yes/No keys, mode 1 == mode 2 pixels for every pack, the three board sizes, the laser bar
4/6/6 px wide), `sound_check.py` OK (208/208 recordings identical *including* the per-tick
`SoundPlay` stream, 16/16 WAVs decoding to the same PCM in Python and C#, 12 checks on
`[OPT] Sound`), `undo_check.py` OK (400 scripts, 0 divergences, all four commands exercised),
`list_check.py` OK (12 list dumps and 8 `.hs` writes rebuilt in Python, plus 6 checks that reaching
the flag posts a score), `roundtrip_check.py` OK (60 cases, six runs each), `mouse_check.py` OK
(5,000 click scripts, 0 divergences, and 120 more on each of the ten quirk packs),
`editor_check.py` OK (3,000 edit scripts against the oracle's own `ChangeGO`; some 160 unedited
levels drawn from all 23 collections re-saved byte-identically; the `GetWindowText` write widths rebuilt in
Python; 60 edited levels whose saved board matches the trace, whose gap is zero-filled and which
the oracle loads; 6 more saved through the *game's* editor and matching the driver byte for byte),
`lang_check.py` OK (2,293 source lines rebuilt out of the JSON and compared **as bytes in their own
codepage** across all ten files, 90 ignored exactly where the original ignores them; the tab policy
that licenses the one asymmetry, measured; the key set and both menu trees against the frozen
`LT32L_US.H` and `lt32l_us.inc`; ten `--lang-dump`s identical to a Python rebuild; three fallback
cases on a synthetic partial language; ten more resolved identically inside the game, plus 5 INI
checks).

**Everything is ported.** `MouseOperation` was the last stub and step 5 wrote it. The reason it
survived two phases is worth keeping in view: it is driven by `MBuffer`, which only
`WM_LBUTTONDOWN` / `WM_RBUTTONDOWN` write, so no keystream and no recording could make the tick
call it — measured, not assumed, since the fuzz campaign reached it zero times and so did step 4's.
What reached it in the end was the same move that reached `UndoStep`: **a new input language.**
`--script` grew `mXY` / `nXY`, a left and a right click, and the last stub became an ordinary trace
diff. `NotPortedException` stays in the tree, wired up in `fuzz.py`'s signatures, because the
argument behind it has not changed.

**Next action for this half of the project: pick one — Phase 5 has no step 7.** The phase set out to
finish the port faithfully so that nothing mechanical could move while the game was built around it,
and it did that: steps 0-6 are done and all ten Phase 5 gates are green beside the four fidelity
ones. What is *deliberately* left, and was named as left at the time rather than forgotten:

- **The UI redesign the phase was always a prelude to.** Step 2 wrote down the line — *the mechanics
  must be identical, the UI need not be* — and every step since has recorded its UI details as
  "written down rather than locked down" for exactly this. The rules gates (`replay_all.py`,
  `sweep.py`, `test_difftrace.py`, `tick_check.py`'s 208/208) are what must not move;
  `options_check.py`'s pixel arithmetic is the one gate *expected* to be edited when the look changes
  on purpose.
- **The six dialogs step 5 left out**, all of them file-picker or modal-prompt shaped: Load Level
  inside the editor (602), Save As (606), the `LoadTID` tunnel dialog *as a dialog*, the "save
  changes?" prompt, the Difficulty dialog (225) and Print (126).
- **A menu bar.** Step 6 converted all 73 menu items of both trees, with their command ids and
  accelerator labels, so `Language.MainMenu` / `EditorMenu` is a ready-made model for one — but the
  port is still key-driven and has no menu widget. This is the cheapest remaining piece of the
  original that is *fully specified data* rather than design work.
- **More fuzzing, indefinitely.** Phase 3's fuzzer can keep running on new seeds and the 12
  collections its first campaign never touched; `undo_check.py`, `mouse_check.py` and
  `editor_check.py` are three more campaigns of the same kind on the same engine, and all four are
  worth leaving running.
- **The solver**, which is a goal in its own right and is the larger unfinished half of the project:
  11.3% of a 4,185-level sample against a goal of all 20,914. See `SOLVER.md`.

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
with `--field --bmf`, exit 0. `MouseOperation` was the only unported function and was unreachable
from a keystream; Phase 5 step 5 ported it, so the tick has no stubs left at all. `BMF`/`AniLevel` differences would be cosmetic (hazard #2) — `difftrace.py`
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

**An unported function throws rather than no-ops.** A silent stub would produce a *plausible* wrong
trace, which is the one failure mode this whole approach exists to prevent — and step 4 turned that
from an argument into an incident report: the tick frame had been passing `0` instead of `S_Fire` to
`FireLaser` since step 1, and only the exception kept it from silently corrupting `laser.Good`.
`MouseOperation` was the last function this applied to, and it stayed a stub for two phases *because
nothing could reach it*; Phase 5 step 5 built the input language that could (`--script`'s click
tokens) and ported it. `NotPortedException` stays in the tree, wired into `fuzz.py`'s signatures,
because the argument has not changed.

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
- **The undo buffer was carried for two phases even though nothing read it back**, and step 4 is
  what vindicated the decision. `UndoStep` is unreachable from a keystream, so the `TGAMEREC`
  snapshots `UpdateUndo` stores were write-only. `UndoP` was not: `MoveObj`'s tunnel path
  decrements it (quirk #7), so its growth (`UndoBufSize` in steps of 200) and its roll-over at
  `UndoMax` have to be exact, and the cheapest way to be sure was to keep the buffer they index
  into. Phase 5 step 4 then wrote the reader and diffed 400 undo scripts against the oracle's own
  `UndoStep` with no divergence — so the arithmetic really was right, and it was right because the
  snapshots were kept rather than optimised away. The two `GlobalReAlloc == NULL` branches in
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

## Phase 5 — Presentation & features  ☑  (steps 0-6 all done)

**Where the fidelity line actually runs, decided out loud in the step 2 session and worth reading
before the next UI change: the mechanics of the puzzles must be exactly the same — every level has
to be solvable in exactly the way it was in the old game — and the UI does not.** A redesign of the
25-year-old interface is expected *later*, on purpose; this phase finishes the port the way it
started, faithfully, because a faithful port is the cheap way to be sure nothing mechanical moved
while it is being built. The consequence for everything below: the UI details recorded in these
steps are **written down rather than locked down.** When one of them is deliberately changed, the
note explaining what the original did stays (it is why the change is a choice rather than a
regression), and the gates that must not move are the ones about the rules — `replay_all.py`,
`sweep.py`, `test_difftrace.py`, `tick_check.py`'s 208/208. `options_check.py`'s pixel arithmetic is
the one gate that is *expected* to be edited when the look changes on purpose.

**This is the first phase where the deliverable is the game rather than a measurement, and the
discipline that got the project here still applies: the presentation layer must not become a second
implementation of the rules.** The transliteration in `LaserTank.Core` stays untouched — a Godot
node reads `Game.PF` and `Game.BMF` and draws them; it never decides anything. (Core may still gain
*presentation data*: step 0 added `GraphicsFile.cs`, which no code path inside `Tick()` can reach.
The line is whether a rule could move, not which directory a file sits in.) That is the same contract the
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

**Step 0 — the Godot project, and the board on screen. ☑ DONE.** `src/LaserTank.Game/` is a Godot
4.7 project referencing `LaserTank.Core` as a plain library (it has no Godot dependency, which is
what makes this a reference rather than a rewrite). `BoardView.cs` loads a level and draws the 16×16
board from `Game.BMF`; `PgUp`/`PgDn` walk the 2,030 flagship levels, `G` cycles the four graphics
packs, `Z` cycles the three zooms.

```bash
GODOT=$(echo ~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_*/*/Godot_v4.7.2-stable_mono_win64_console.exe)
"$GODOT" --headless --path src/LaserTank.Game --import         # once per checkout
"$GODOT" --path src/LaserTank.Game                             # play with it
"$GODOT" --path src/LaserTank.Game -- --shot out.png --level 7 --pack 3 --zoom 40
python tools/atlas_check.py                                    # the gate, ~35 s
```

`--shot` draws one frame to a PNG and exits, which is how a rendering change gets reviewed without a
window. `--pack 0` is the internal sheet, 1-3 the `.ltg` files in `data/graphics/` sorted by name.

**Nothing in `LaserTank.Core`'s transliteration moved.** Core gained one new file, `GraphicsFile.cs`
— the `.ltg`/BMP readers and the two rendering tables from the top of `LTANK2.C` — which nothing in
`Tick()` can reach. `Engine.cs` still differs from a literal transliteration by the single word
`partial`, `Engine.Search.cs` is still untouched, and the corpus was re-replayed against a core
built *with* the new file: 187 replayed / 181 win / 112/112 `.ghs`, and `game-objects` traces
16/16 identical to the oracle with `--field --bmf`.

**The atlas geometry, decoded and verified against all three packs**, because getting it wrong is a
silent off-by-one: the sheet is **always 320×192 — a 10×6 grid of 32×32 sprites** — and `BMA[]` is
filled row-major from **i = 1**, ten per row (`GFXInit`, `LTANK2.C:782`). So sprite index `i` is
atlas cell `((i-1) % 10, (i-1) / 10)`. `MaxBitMaps` is 58 (`LTANK.H:92`) and the highest index the
object table yields is 57, so the last row is partly unused.

**Read `Game.BMF`, never re-derive it from `PF`.** `BuildBMField` is not simply `GetOBM(PF)`: a
tunnel is 55, the tank's own cell is 1 *and its `PF` is zeroed*, and `Animate()` then cycles `BMF`
for animated objects. Every one of those is a place a re-derivation drifts.

*Exit — MET.* `tools/atlas_check.py` is two checks, both green:

- **grid**, 2,347/2,347 corpus levels: every `BMF`/`BMF2` byte the engine produces is a bitmap
  number that lands inside the 10×6 grid. It reads the `--bmf` hex the trace already carries, so it
  needs no engine code and no rendering — 30 s at `--jobs 8`.
- **sheets**, 4/4: every shipped pack decodes to 320×192, and **decodes to the same pixels in two
  independent implementations** — Python's reader in the tool, and C#'s `SpriteSheet` reached
  through `godot --headless -- --check-sheets` — compared by sha256. One decoder agreeing with
  itself would not be a check. Godot missing downgrades that half to a loud SKIP.

**Three things the sheet decode had to get right**, each of which silently produces a
plausible-but-wrong picture:

- **The mask is not simply an alpha channel.** The original blits mask-`SRCAND` then
  bitmap-`SRCPAINT` only for the sprites `BMSTA[]` (`LTANK2.C:80`) marks transparent; everything
  else is a plain `SRCCOPY` that never looks at the mask. In the internal sheet the mask cell for
  an opaque sprite is *solid white*, so applying it to everything erases the board.
- **…except the tunnel, which is masked anyway.** `UpDateSprite`'s tunnel branch (`LTANK2.C:498`)
  paints `ColorList[id]` and then masks sprite 55 over it **regardless of `BMSTA[55]` being 0** —
  that is the only reason a tunnel's colour is visible. Caught here by eye after the first render
  showed eight identical black discs; `Gfx.Masked()` is now the one place that rule lives.
- **`BMSTA` is declared `[MaxBitMaps+1]` = 59 wide with 58 initialisers**, so C zero-fills the last
  entry. Both ports carry the trailing 0 rather than the shorter array, because bitmap 58 is a
  legal index.

Also decoded on the way: the sheets are `BI_RGB` 24 bpp (8 bpp for `Warcraft_II`) with a 1 bpp
mask, but the *internal* pair — `original/src/Game.BMP` and `Mask.BMP`, the ones the 2007 build
carries as resources — are **RLE8 and RLE4**. Both readers handle 1/4/8/24 bpp and both RLE modes.

**Step 1 — the tick loop, and the gate that matters. ☑ DONE.** `src/LaserTank.Game/` is now a
game you can play: `Session.cs` is the Godot replacement for the *driver* half of `LTANK.C`'s window
proc — `WM_TIMER`, `WM_KEYDOWN`, `WM_Dead`, ReStart (command 105), `WM_SaveRec` — and `BoardView`
draws it and routes keys. `PlayMode.cs` is the same driver with the human replaced by a script,
which is what makes the exit criterion re-runnable.

```bash
"$GODOT" --path src/LaserTank.Game                             # play it
python tools/tick_check.py                                     # the gate, ~20 s
"$GODOT" --headless --path src/LaserTank.Game -- --play --lpb data/demos/LaserTank/00001.lpb
"$GODOT" --path src/LaserTank.Game -- --shot out.png --lpb <f>.lpb --ticks 40
"$GODOT" --headless --path src/LaserTank.Game -- --tick-rate 5
```

Keys: arrows move, space fires, `R` restarts, `F6` saves the recording to `out/recordings/`,
`Enter` takes the next level once one is solved, `[` `]` walk levels, `G` opens the graphics menu
(step 2; it used to cycle packs), `Z` cycles the board size, `I` toggles interpolation, `Esc` quits.
Inside the menu the arrows pick a pack, `1`/`2`/`3` or `Z` set the size, and `Enter` or `Esc`
closes. **The dev keys are all deliberately outside VK 32..40** —
see below for why that range and not the five game keys.

*Exit — MET, and by more than was asked.* The criterion was a human playthrough surviving the C
oracle; that happened, and then it was mechanised so it survives the *next* change too.
`tools/tick_check.py` plays all 208 recorded `.lpb` in the corpus through Godot's own input path
and its own tick, and checks four things per recording plus one about the clock:

- **208/208 agree with the oracle** on result *and tick count* and moves and shots. Equal tick
  counts are the strong claim: the driver is not merely reaching the same ending, it takes the same
  number of 50 ms steps to get there.
- the keystream Godot recorded is the input keystream **byte for byte**.
- **the round trip**: replaying Godot's own `.lpb` through the oracle reproduces the oracle's
  original verdict. Confirmed by hand first — `data/demos/LaserTank/00001.lpb` re-recorded through
  Godot differs from the original in bytes 32-38 only, which is the `Author` field, and nowhere
  else in 330 bytes.
- header sanity: the level number and name Godot wrote are the ones the `.lvl` has, because
  `lasertank-core` refuses a mismatch.
- **the rate**, measured separately: `--tick-rate 5` runs the real `_PhysicsProcess` driver against
  the wall clock and must have taken exactly 20 ticks a second (100 in 5 s, headless and windowed
  alike). Every other check calls `Step()` in a loop, so they prove what a tick *does* and nothing
  about when one happens — worth having as its own assertion.

**The input path is the part with the surprises in it**, and all three are in `LTANK.C:570`:

- **The filter is `wparam >= 32 && wparam <= 40`, not the five game keys.** Space is 32 and the
  arrows are 37-40, but **33-36 — PageUp, PageDown, End, Home — are inside the range too**, and
  `AddKBuff` filters nothing. The tick's switch has no `default` and `RecP++` runs regardless, so
  those four record a legal one-tick **wait** that still gives the anti-tanks their turn. They are
  reachable from a real keyboard and always were; no human ever used them (all 54,162 bytes of all
  187 `.lpb` are the five keys). The port keeps them reachable, which is also why every dev binding
  in the game had to move off `PgUp`/`PgDn`/`Home` — step 0 had used exactly those three.
- **`if ((RB_TOS > Game.RecP) && (lparam & 0x40000000)) return(0);`** — auto-repeat is dropped
  *only while a key is still pending*. That one line is what stops a held-down arrow flooding the
  buffer while the tank is busy and what lets it keep the tank moving once the buffer drains. It is
  also the real answer to hazard #10's "a 144 Hz display must not consume 144 keys a second": the
  frame rate never enters into it, the pending-key test does. Godot's `InputEventKey.Echo` is the
  same bit.
- **`WM_SaveRec` writes `Game.RecP`, not `RB_TOS`** (`LTANK.C:709`) — the keys *consumed*, not the
  keys pressed. That is what makes a recording saved the instant a level is won end exactly at the
  winning move, and it is why `tick_check` compares against a prefix of the input keystream rather
  than the whole of it.

**`GameOn()` is `SetTimer` / `KillTimer` (`LTANK2.C:881`), so a finished game receives no ticks at
all.** `Session.Step()` returns false rather than ticking when `Game_On` is clear, which is the
faithful shape and not a guard bolted on: calling `Tick()` on a won game would not crash, it would
quietly keep playing. The cheap evidence is that `--ticks 20` on a keystream that drowns the tank
stops at tick 3, matching the oracle's `DEAD … ticks=3`.

**The laser is the one genuinely new piece of drawing**, and it had to be reconstructed as *paint
only*. `UpDateLaser` (`LTANK2.C:549`) is a bar down the middle of a cell — `LaserOffset = 10` of 32
— green when `laser.Good` and red otherwise, which is `FireLaser`'s `laser.Good = (sf == 2)` and so
already load-bearing for logic. `UpDateLaserBounce` (`:565`) paints *two half-bars*, the half the
shot came in through and the half it leaves by. But that function is hazard #1: it also sets
`LaserBounceOnIce`, and **the core already calls it inside the tick**, so the renderer must not
call, skip or reimplement it. The one fact the paint call has and the state does not is the laser's
incoming direction, so `Session` recovers it by watching `laser.Dir` across a tick — reading the
same fact without touching anything.

**Verify the laser by pixels, not by eye** — this is the recipe, and it caught a real bug on the
first try. Dump the trace's `L=x,y,dir,firing,good` for a tick, render that exact tick with
`--shot … --ticks N`, read the PNG back and compare. Tick 40 of `00001.lpb` is a bounce
(dir 1 → dir 4 at cell 5,10) and the frame carries exactly two half-bars, lower and left, matching
`UpDateLaserBounce(1,4)`'s `Rectangle` calls; tick 35 is straight and the bar is 300/300 pure green.

The bug it caught is hazard #1's cost to a retained-mode renderer, and it is subtler than "one
frame is missing". When the laser bounces off a mirror that is itself **sliding on ice**,
`UpDateLaserBounce` sets `LaserBounceOnIce` and `MoveLaser` `goto`s back for a *second* step in the
same tick — so the bend happened one cell back and the laser now sits in a cell it went straight
through. Comparing `laser.Dir` across the tick says "bounced", and the two half-bars would be
painted **in the wrong cell**, drawing a bend that is not there. The fix is a distance test: two
cells of travel in one tick is exactly that case, so the bounce glyph is suppressed and the
ordinary straight bar drawn. What is lost is the bend itself for one 50 ms frame; what is avoided
is drawing it somewhere it never happened.

`Tutor-with-Playbacks` 93 (tick 527) and 94 (tick 206) are the only recordings in the corpus that
reach this, and 93 is now checked end to end: ticks 526/527/528 render a horizontal bar, a
*straight* vertical bar in the double-step cell, and `UpDateLaserBounce(3,2)`'s upper-plus-right
halves — read out of the PNGs, against the trace. That recording also plays identically through
Godot's driver at buffer depths 1, 2, 5 and 50 pending keys, which is the demonstration that a
player who presses ahead gets the same game: key consumption is gated on quiescence, so the depth
of the queue moves no tick.

**One deliberate deviation, and it is not a rule.** `Session.Load` builds a **fresh `Engine` per
level**, and `Restart` reloads rather than restoring `CurRecData.PF` in place the way command 105
does. `LoadLevel` leaves `wasIce`, `WaitToTrans`, `ConvMoving` and `BlackHole` standing (quirk
#12), faithfully, because the original never reloaded a level into a fresh process either — but
every keystream this game records is going to be replayed somewhere that *did* start clean: the C
oracle, the 2010 binary, `verify_solutions.py`. A clean start is also the only configuration the
whole oracle equivalence was ever established for, since every fidelity gate builds a fresh engine
per case. So the port starts every level clean and a recording it writes always replays. The four
flags are uninitialised state, not a rule; nothing about hazards #9 or #11 is touched by this.

**Interpolation is ours, and the original had none** — it snapped, one cell per 50 ms. The tank is
lerped between the previous cell and the current one, guarded three ways: only while the timer is
running, only between *adjacent* cells so a tunnel does not slide the tank across the board, and
rounded to whole pixels because the sheet is nearest-filtered. `I` turns it off, which is the
honest A/B against the 2010 binary, and `--shot` forces it off so a screenshot names a tick rather
than a moment between two.

The tick itself is `_PhysicsProcess` with `physics_ticks_per_second = 20` in `project.godot`, and
`BoardView` refuses to start if that setting and `GameDelay` disagree — the logic rate is the one
number in this project that must not drift. `_Process` only calls `QueueRedraw`.

**Not in step 1, on purpose:** sound (step 3), undo (step 4 — `UndoStep` was still the one missing
reader), the level picker and the recording UI (step 4). `F6` writing to `out/recordings/` was the
placeholder for the last of those; step 4 kept the directory and gave it the recorder, the author
name and BuildPB_Name's file name.

**Step 2 — graphics packs and zoom. ☑ DONE.** `.ltg` is a 324-byte `TLTGREC` header (`Name[40]`,
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
*Exit — MET, and the gate found two rendering bugs on the way.* All three packs load and render, so
does the internal pair, so does the external mode; switching size changes nothing but pixels. Note
hazard #11 lives in `SetGameSize` — `if (GFXOn) GFXKill;` is missing its parens and must stay
missing; nothing in the port calls it, and `SetUpGraphicsBox`'s copy of that line *does* have its
parens, which is why picking a pack in the menu really does reload the sheet.

```bash
# from the repo root.  Every path after `--` must be ABSOLUTE: godot --path makes
# src/LaserTank.Game/ the working directory, and a relative one silently resolves
# there and hangs the run -- see Environment notes.
GODOT=$(echo ~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_*/*/Godot_v4.7.2-stable_mono_win64_console.exe)

"$GODOT" --path src/LaserTank.Game                             # play; G opens the menu
python tools/options_check.py                                  # the gate, ~40 s

# the graphics menu as a picture, which is the only way to review it without a
# window session: --menu opens the dialog on start, --shot draws one frame and quits
"$GODOT" --path src/LaserTank.Game -- --shot D:/code/lasertank/out/menu.png \
         --menu --pack 3 --zoom 40 --level 7

# the options themselves, headless.  --ini is what makes them live (writable, and
# allowed to pick the level); without it an instrument run gets them read-only
"$GODOT" --headless --path src/LaserTank.Game -- --ini D:/tmp/x.ini --check-options
"$GODOT" --headless --path src/LaserTank.Game -- --ini D:/tmp/x.ini --pack external \
         --gfx-dir D:/some/pack/dir --save-options --check-options
```

`--pack` takes step 0's number (0 the internal sheet, 1..n the `.ltg` files sorted by name), or a
word: `internal`, `external`, or a `.ltg` by file name or header name.

**Most of the machinery landed with step 0** — the loading, the mask fold, all four sheets and the
draw-time scaling at 24/32/40. What step 2 added is the game's own way in and the memory of it:

- **`Options.cs` is `LaserTank.ini`**, in the file and under the key names the original persists
  them under (`LTANK.H:113-128`): `[SCREEN] Size`, `Graphics_Mode`, `Graphics_File`,
  `Graphics_Dir`, and `[DATA] RLLFilename` / `RLLLevel` with `[OPT] RLL`. `Ini` is a stand-in for
  the three profile-string calls rather than an INI library: first match wins, sections and keys
  match case-insensitively, integers follow **atoi** (a present-but-junk value reads as 0, and only
  a *missing* key gives the default), and **a write preserves every other line in the file.** That
  last one is load-bearing, not politeness: the 2010 binary keeps a dozen keys in this same file —
  `PosX`, `Player`, `Diff_Setting`, `Animation`, `Sound` — and a rewrite that dropped them would
  silently reset the player's other settings. The gate checks exactly that with a seeded file.
- **The defaults are the original's**, which changed one thing: `Size` defaults to **1**, the 24 px
  board (`LTANK.C:1567`), where this port had been starting at 32. `Graphics_Mode` defaults to 0.
- **`GraphicsMenu.cs` is `GraphBox` (`LTANK_D.C:1202`)**, command 226 on the Options menu, as an
  overlay: the two radio buttons (internal, user graphics), then the `.ltg` packs **named by the
  `Name` field in their header** the way `GetLTGFiles` names them — so `Lasertank_Comix.ltg` lists
  as *Lasertank Comix* — plus the selected pack's `Author` and `Info`. Three of the dialog's
  properties are kept because they are observable: the choice **applies immediately** (every
  `WM_COMMAND` branch ends in `SetUpGraphicsBox`, which is `GFXKill(); GFXInit();`), there is **no
  Cancel** (Close and Cancel run the same code and both write the INI, `LTANK_D.C:1247`), and **the
  game keeps ticking underneath** — command 226 never calls `GameOn(FALSE)` the way the Difficulty
  dialog (225) does, and `DialogBox`'s modal loop still dispatches the `WM_TIMER` posted to the main
  window, so an exposed tank can die while you pick a pack. Keys, though, go to the dialog and never
  reach `AddKBuff`, which is the only reason the menu may navigate with the arrows and space at all.
- **`Packs.cs` is `GFXInit`'s three branches**, including the external mode (`GraphM == 1`):
  `game.bmp` + `mask.bmp` in `Graphics_Dir`, matched case-insensitively because `LT32L_US.H:18`
  spells them lower case and the packs people ship are not that careful. A pack that will not load
  falls back to the internal sheet and says so in the HUD, which is `if (!LoadLTG(...)) GraphM = 0;`.
- **Not ported, on purpose:** "Change Directory" (`ID_GRAPHBOX_09`, a shell folder browser — the key
  is persisted and `--gfx-dir` sets it) and "View Opening Screen" (`ID_GRAPHBOX_08`, which toggles
  `QHELP` and paints `Opening.bmp` over the board; that is its own piece of drawing).

**One deliberate deviation, and it is a bug we are not reproducing.** `GFXInit`'s external branch
reads `if (!(Mh || Gh)) GraphM = 0;` — **`||` where it means `&&`** — so with exactly one of
`game.bmp` and `mask.bmp` present it stays in mode 1 and hands a NULL bitmap to `SelectObject`, and
what follows is undefined GDI rather than a picture. There is nothing to transliterate: a decoder
that throws cannot produce half a sheet. Either file missing means the internal sheet here, which
is what the working half of that line intends. (Contrast hazard #11, which *is* kept: that one has
a defined and observable effect — nothing happens.)

**The two bugs the gate found, both of them ours and both invisible without measuring pixels:**

- **`LaserOffset` is a per-size constant, not a fraction of the cell.** `SetGameSize` sets it to
  **10, 13, 17** for the three sizes (`LTANK2.C:1747`, `:1756`, `:1765`), and `UpDateLaser` paints a
  bar `SpBm_Width - 2 * LaserOffset` wide — so **4, 6 and 6 px**, a hairline. Step 1 read the
  initialiser at `LTANK2.C:46` as "10 of a 32 px sprite" and scaled it, which draws 8, 12 and 14 px
  and a laser two to three times too fat. Reported by eye against the 2010 binary, then pinned by
  the gate. The lesson is the one hazard #2 teaches about `BMF`: **the value is in a table in the
  original, so read the table** — an initialiser that happens to equal the first table entry is not
  the rule.
- **Godot's `DrawRect(filled: false, width: 1)` strokes *centred* on the edge**, so a 1 px outline
  rounds *outside* the rect on the top and left and inside on the bottom and right. GDI's
  `Rectangle()` puts its pen strictly inside (`left..right-1`). The laser's and the tunnels'
  outlines were therefore bleeding a pixel into the cell above and to the left. `Fill()` now paints
  the border as the difference of two fills, which is exactly GDI's semantics.

*The gate, `tools/options_check.py`* — 20 checks in four groups, and the pixel half is the part that
would have caught the laser:

- **ini**: the defaults with no file at all; a write that keeps five foreign keys; the round trip
  (`--save-options` then a launch with no overrides at all must report what was written); Size
  likewise; remember-last-level in both directions (`--tick-rate 1` really loads a level, so the
  write goes through `Session.Load` where the original does it, and a later launch starts there);
  `RLL=No` honoured; and the `LaserTank.ini` the 2010 binary actually left in `original/bin/` parsed
  through a copy, agreeing with a plain Python read of the same file.
- **packs**: the menu lists one entry per `.ltg` under its header name plus the two radios; the
  internal sheet's sha256 matches `atlas_check`'s independent Python decoder; and **mode 1 and
  mode 2 of the same pack are byte-identical pixels** for all three, checked by splitting each
  `.ltg` at its `MaskOffset` into `game.bmp` + `mask.bmp` — which is a byte copy, because that is
  literally what the container is. A mode 2 naming a deleted file falls back to mode 0.
- **size**: all three render, the board is 16 cells square in each, and the HUD strip below it keeps
  its height — "nothing but pixels", asserted rather than assumed.
- **laser**: the bar is measured **out of the PNG, in the cell the engine says the shot is in.**
  `--shot` now also prints `shot-geometry` and `shot-laser` lines, so the coordinates come from the
  trace side and the pixels from the renderer; comparing the two is a check rather than a tautology.
  The tool carries its own copy of both tables (`{24,32,40}` and `{10,13,17}`), so editing
  `BoardView`'s copy fails it.

The gate reads PNGs with a ~40-line stdlib decoder (8-bit RGB/RGBA, no interlace — what Godot's
`SavePng` writes). Nothing in it writes the player's own `LaserTank.ini`: every run is handed its
own `--ini` under a scratch directory, and the game refuses to write an INI it was not given
explicitly whenever it is running as an instrument.

**`--ini` is what makes the options live.** A `--shot`, `--play`, `--check-options` or `--tick-rate`
run left to find `LaserTank.ini` on its own gets it **read-only** and starts on whatever level it
was told to: eight parallel `atlas_check` jobs must not race over one file, and a screenshot must
not change what the next player sees or depend on what the last one did. Passing `--ini` says "this
file is yours" and turns both halves back on, which is how the gate exercises the writing side.

**Step 3 — sound. ☑ DONE.** The 16 WAVs in `original/src/Sounds/`, played off the ids the tick
already computes. The plan called this the cheapest exit criterion of any step — "the gates stay
green" — and that turned out to be the wrong bar for the same reason "186/186 reach the flag" was in
Phase 1: it tests that sound changed nothing, never that the sound is *right*. So the ids got a
differential of their own, and it found a real bug in the first run.

```bash
"$GODOT" --path src/LaserTank.Game                             # play it; S mutes
python tools/sound_check.py                                    # the gate, ~60 s

# the ids, from either engine, as a trace field
build/lasertank-core.exe --levels data/levels/LaserTank.lvl \
    --lpb data/demos/LaserTank/00001.lpb --trace b.tr --sound
oracle/build/oracle.exe  --levels data/levels/LaserTank.lvl \
    --lpb data/demos/LaserTank/00001.lpb --trace a.tr --sound
python tools/difftrace.py a.tr b.tr

"$GODOT" --headless --path src/LaserTank.Game -- --check-sounds
"$GODOT" --headless --path src/LaserTank.Game -- --ini D:/tmp/x.ini --sound no --save-options
```

**`SoundPlay` became a trace field, which is what makes this checkable at all.** `--sound` adds
`SF=<ids>` to every tick line on *both* engines: the ids that tick asked for, in call order, `-` for
a silent tick. The oracle records them in its own stub (`oracle/driver.c` — `lt_sfx.c` is not
compiled there and never was), the port records them in `Engine.SoundLog`, and the two must be
byte-identical. `tools/sound_check.py` replays all 208 recorded `.lpb` that way: **208/208
identical, 103,682 of 163,791 ticks make a sound, and all sixteen ids fire at least once** (id 7,
`DIE`, exactly once — the corpus is nearly all winning play).

**The bug it caught immediately: `SoundPlay(S_Move)` was missing.** Phase 2 read `UpDateTankPos`'s
opening line as paint, alongside the `SetTextAlign`/`TextOut` score readout it sits next to
(`LTANK2.C:1216`), and dropped it. Nothing could have noticed before this session — no trace carried
sound — and nothing about the rules moved, but the tank would have driven around in silence, which
is 23,808 of the corpus's calls. Two more were restored with it, and both are in `LTANK.C` rather
than `LTANK2.C`, which is why they were never in the core to begin with: `S_EndLev` in the flag case
(`:650`, and the oracle's `LT_Tick` had it all along) and `S_Die` in `WM_Dead` (`:718`, now in
`SendDead` where quirk #8's ordering already lives). Restoring three calls is a transliteration
getting *closer* to the C, and the corpus is the proof that the set is now complete: a fourth
missing call would show up as an `SF` divergence on some tick of some recording.

**What it cost the core: one word.** `private static void SoundPlay(int sn) { }` became
`partial void SoundPlay(int sn);`, with the body in `Engine.Sound.cs`:
`partial void SoundPlay(int sn) => SoundLog?.Add(sn);`. `SoundLog` is null unless a driver opts in,
so the solver — millions of `MoveTank`/`FireLaser` calls — pays one null test per `SoundPlay` and
allocates nothing, and every headless trace without `--sound` behaves exactly as before. An event
would have cost a delegate invocation; a per-tick array would have cost an allocation per `Engine`.

**Two facts about the original's audio that a nicer implementation would lose**, both hiding in one
argument — `PlaySound(p, 0, 5)`, i.e. `SND_MEMORY | SND_ASYNC`:

- **It is monophonic.** `PlaySound` owns the process's waveform device and a second call *stops the
  first* unless `SND_NOSTOP` is passed, which it is not. So the original never mixes two effects:
  the tank moving cuts off the laser bounce. One `AudioStreamPlayer`, `Play()` every time,
  reproduces that; sixteen players would be a *nicer* game that does not sound like this one.
- **A tick that asks for several sounds therefore only ever plays the last one** — and `MoveObj`,
  `AntiTank` and the laser routinely ask for several. `Session` hands the renderer the whole
  per-tick list in call order and `Sfx` plays the last of it, so the audible-behaviour choice and
  the fidelity check stay separate things: the *list* is what the gate diffs.

**Muting lives in the presentation, not the engine.** `lt_sfx.c:29` returns early on `!Sound_On`, so
a muted original makes no `PlaySound` call at all — but it makes the same decisions, and decisions
are what the port records. Putting `Sound_On` in the engine would have made a muted game trace
differently from a loud one. `[OPT] Sound` is therefore read by `Options` and applied by `Sfx`; the
`S` key is command 102 (`ToggleOpt`, `LTANK.C:875`) and writes `Yes`/`No` on the spot.

**The INI test is the original's, case and all.** `if (strcmp(temps, psYes)) Sound_On = FALSE;`
(`LTANK.C:411`) means **exactly `Yes` or the sound is off** — a hand-edited `Sound=yes` really does
mute the 2010 binary. Kept, because it is one line, it is observable, and there is no argument for
leniency beyond taste. Worth knowing that step 2's `RLL` reader took the looser reading of the same
idiom and its gate now pins that; the two disagreeing is recorded here rather than harmonised by
guess.

**The WAVs are read at run time and cross-checked, like the sprite sheets.** All sixteen are PCM
mono 8-bit, 11025 Hz except `MOVE` and `PUSH3` at 8000; `LaserTank.Core/SoundFile.cs` reads RIFF
(walking every chunk — these files carry `fact`, `LIST` and `DISP`, and several put `LIST` *after*
`data`) and converts to signed 8-bit, because **8-bit WAV samples are unsigned and every raw-PCM
consumer wants them signed**. That xor with `0x80` is the conversion that silently produces a
DC-offset click rather than an error, so it is checked: `sound_check.py` decodes the same files in
Python and compares sha256 against C#'s through `--check-sounds`. The id -> name table is carried
twice for the same reason — `SoundFile.Names` and the tool's own copy — because a table agreeing
with itself is not a check. Nothing was imported into `res://`: `original/src/Sounds/` is frozen and
read from where it lies, exactly as the internal sprite sheet is.

*Exit — MET, and by more than was asked.* The four Phase 5 gates and `replay_all.py`, `sweep.py`,
`test_difftrace.py` are green with the sound in (187/181/112, 2,347/2,347, 208/208, 29 passed), so
adding audio changed no trace — and `sound_check.py` adds the claim the original criterion could not
make: the port asks for the same sound, in the same order, on the same tick, as the C, on every
recording in the corpus. **The one thing no gate covers** is that `AudioStreamPlayer` actually
makes a noise: the ids, the decode and the option are all checked headless, and the last hop needs a
human with speakers. That hop was walked at the end of this session -- the game was played and the
sounds were heard -- so step 3 is closed. It stays the part a future regression would have to be
noticed rather than measured, which is worth remembering if the audio path is ever refactored.

**Not in step 3, on purpose:** an Options menu (the `S` key is the whole UI, as `G` and `Z` are),
`Ani_On`'s `[OPT] Animation` twin, and the volume/mixer settings the original never had.

**Step 4 — the game around the game. ☑ DONE.** Level picker, the two high-score lists, undo,
Save/Restore Position, and the record/playback UI.

```bash
"$GODOT" --path src/LaserTank.Game        # U undo, L levels, V/G scores, F5/F6/F7 record

python tools/undo_check.py                # the commands, vs the oracle, ~60 s
python tools/list_check.py                # the list rows and the .hs bytes, vs Python, ~25 s
python tools/roundtrip_check.py           # step 4's exit criterion, six runs a case, ~150 s

# the commands, from either engine, as a token stream
oracle/build/oracle.exe  --levels data/levels/LaserTank.lvl --level 7 \
    --script "uufz.zc.v" --trace a.tr --field
build/lasertank-core.exe --levels data/levels/LaserTank.lvl --level 7 \
    --script "uufz.zc.v" --trace b.tr --field
python tools/difftrace.py a.tr b.tr

"$GODOT" --headless --path src/LaserTank.Game -- --play --script "uufz.zllZ" --level 7 --out DIR
"$GODOT" --headless --path src/LaserTank.Game -- --replay DIR/x.lpb --speed 2
"$GODOT" --headless --path src/LaserTank.Game -- --check-lists ABS/PATH/TO.lvl
"$GODOT" --headless --path src/LaserTank.Game -- --check-scores ABS/DIR

# the panels as pictures, which is the only way to review one without a window
# -- the same trick --menu is for.  levels | scores | global | playback
"$GODOT" --path src/LaserTank.Game -- --panel levels --zoom 2 --level 900 \
    --levels ABS/PATH/TO.lvl --shot ABS/OUT.png
```

**A rule that step 4 had to learn the hard way: an instrument must not post a high score.**
`Session` writes a `.hs` only when it was given `Options` whose INI is writable — the same
live-versus-instrument rule `Ini.ReadOnly` is, and for the same two reasons (parallel gate jobs
racing over one file, and a measurement changing what the next player sees). `PlayMode` builds its
Sessions without `Options`, so `tick_check.py` can replay 208 *winning* recordings and leave the
corpus alone. It did not, at first: one run left `.hs` files in six collections under `data/`
(gitignored, so the tree stayed clean and nothing said so). `--play --script --ini FILE` is the one
configuration that does post, which is how `list_check.py`'s `win` half checks that reaching the
flag writes anything at all — the `--check-scores` half drives `HighScores.Check` directly and so
can only prove the writer, never its caller.

**`--script` is what made this checkable, and it is the same move `--sound` was.** Undo, Save
Position and Restore Position are `WM_COMMAND` cases, not bytes in `RecBuffer` — which is precisely
why Phase 2 left `UndoStep` unported: no keystream can reach it, so nothing headless ever called it,
so the undo buffer had been written and maintained for two phases *with nothing reading it back*.
Its arithmetic — the growth, the roll-over, quirk #7's `UndoP--` in `MoveObj`'s tunnel path — had
never been checked against anything but itself. A **script** reaches it: a token stream consumed at
most one token per tick, where `u d l r f` press a key (only when the buffer has drained, which is
the original's own pending-key rule), `.` idles one tick, `z` is command 110, `Z` is the DeadBox's
"Undo Last Move" — command 110 plus `GameOn(TRUE)`, the only path in the game that resumes a death —
and `c`/`v` are commands 111 and 112. `oracle/driver.c` grew a `script_feed` for it, and **the
oracle's `UndoStep` is the real one**: `oracle/build.sh` compiles `LTANK2.C` verbatim, so the C being
diffed against was written in 2002 and this project has never read it into anything.

**Three drivers implement that fifteen-line rule on purpose** — `oracle/driver.c`'s `script_feed`,
`LaserTank.Cli`'s `Feed`, `PlayMode`'s `Feed`. The first two prove the *engine* undoes correctly;
only the third proves that what the `U` key calls does. Same argument as carrying the sound-id table
twice.

**It found two things on its first runs, and both are now hazards #13 and #14.** `undo_check.py`'s
very first campaign flagged `v` before `c` — a blank `SaveGame` copied over the live game, which
walks `ConvMoveTank` off the end of `Game.PF` and is unreachable in the original only because the
menu item is grayed. And `roundtrip_check.py` disproved the exit criterion's most natural reading:
*a recording made with undos in it is not a transcript of what the player saw*, because `UndoStep`
restores `Game` and `Game` does not contain the laser. That is the C's behaviour, measured in the
oracle with no port involved, so the gate asserts the tie-in only for command-free scripts and
counts it for the rest (44 of 45 in the default campaign).

**The keys are the original's accelerator table** (`lt32l_us.inc:120`), read rather than invented,
which moved two of step 2's: the sound is `N` (`S` is Skip Level) and the graphics dialog is
`Ctrl+G` (`G` is the global high-score list). Step 4 is where that became affordable — with a dozen
commands, an invented set is worse than the real one — and it is the same rule as reading
`LaserOffset` out of its table.

**The three list dialogs are one dialog three times.** `LoadBox` (106), `HSList` (113) and `GHSList`
(906) each build one string per level with a `sprintf` whose padding and truncation are observable,
prefix it with a difficulty digit that `DrawLevels` then colours by, and hand it to an owner-drawn
listbox; all three seek to `CurLevel - 1` and load on Enter. So `LevelList` is one class with three
modes carrying the original's own format strings, and `--check-lists` dumps every row for
`tools/list_check.py` to rebuild in Python from the `.lvl` / `.hs` / `.ghs` bytes. One difference
between them is kept because it is observable: **command 106 stops the clock and 113/906 do not**
(`x = Game_On; GameOn(FALSE); DialogBox(...)`, `LTANK.C:906`), so a tank in the open can die while
you read your scores and cannot while you pick a level.

**Playback needed no new rules at all** — `PBOpen`, `PlayBack`, `PBHold`, `Speed` and `SlowPB` have
been read by `Engine.Tick` since Phase 2, so the panel is four buttons wired to five fields. One
line of `PBWindow` does have to live outside the engine: in Single Step the original's tick posts
`ID_PLAYBOX_02` back at the dialog from *inside* the tick, and an engine with no dialog cannot, so
`Playback.AfterTick` pauses instead. Measured at all three speeds on the corpus's first recording:
Fast 372 ticks (identical to the oracle), Slow 1,136, Step 372.

**The `.hs` writer is where the file format has a quirk in it.** A `.hs` is dense and positional, so
beating level 8 of a collection whose file reaches level 3 writes records 4..7 in front of it — and
`CheckHighScore` pads with its `HS` global **before** refreshing that global from the file, with only
`moves` forced to 0. So the padding carries the *previous* level's shots and initials. Nothing reads
them; every read-back test passes with them zeroed; they are in every `.hs` the 2010 binary has ever
written, and constraint 2 says these files stay writable and not merely readable. So `HS` is a
`ScoreState` the Session owns for its whole life, and `--check-scores` replays a scripted sequence of
writes that `tools/list_check.py` performs again in Python and compares as hex, step by step.

**One documented loss.** Command 105 never calls `ResetUndoBuffer` — it restores the playfield in
place and pushes one more snapshot first (*"Without this we loose the last move"*), so in the 2010
binary `R` then `U` walks back into the attempt you just abandoned, resuming its recording from the
middle of a `RecBuffer` that 105 rewound but did not clear. Reproducing that means keeping the
`Engine` across a restart, and keeping it is what `Session.Load` argues against (quirk #12): a
recording saved after a restart would then not replay in a process that started clean, which every
replay of it does. So the restart is clean and the undo history goes with it.

*Exit — MET.* `roundtrip_check.py` 60/60 over six runs each: the script traces byte-identical
between the oracle and the port, Godot's own command path agreeing with the oracle on result, ticks,
moves and shots, the recording it writes replaying byte-identically in both engines, and Godot's
*playback* path — `PBOpen`, `PBHold`, `Speed`, none of which pressing keys exercises — agreeing with
the oracle on that replay. `undo_check.py` 400 scripts, 0 divergences, over the flagship and all ten
quirk packs. `list_check.py` 12 lists and 8 writes. And the four fidelity gates and the three
Phase 5 gates before them are green with all of it in.

**Not in step 4, on purpose:** the Search sub-dialog (`SearchBox` — name/author substring,
difficulty mask, skip-completed), `TransListKey`'s type-ahead, `[OPT] SkipComLev` and
`[DATA] Diff_Setting` (both belong with a `LoadNextLevel` port rather than with a menu),
`Backspace[]`'s ten-level history (command 118), Resume Recording (command 125), Print (126), the
`RecordBox`/`HSBox` name prompts (the two INI keys are read and written; there is no dialog to type
into yet), and the hint box (301).

**Step 5 — the mouse and the level editor. ☑ DONE.** Two halves that share one cause: both are
driven by the mouse, and the mouse is the one input this project had never had.

```bash
"$GODOT" --path src/LaserTank.Game       # F9 opens the editor; click to paint

python tools/mouse_check.py              # MouseOperation, vs the oracle, ~12 s
python tools/editor_check.py             # the editor and the .lvl it writes, ~25 s

# the two new input languages, from either engine
oracle/build/oracle.exe  --levels data/levels/LaserTank.lvl --level 1 \
    --script "mefn0f..m0a" --trace a.tr --field       # mXY / nXY = a click
build/lasertank-core.exe --levels data/levels/LaserTank.lvl --level 1 \
    --edit "<05l33s44RRUD" --trace b.tr --field --bmf # an edit, one token per step

# the editor as a picture, and as a batch save
"$GODOT" --path src/LaserTank.Game -- --editor --edit '<06l22RD' --level 7 --zoom 2 --shot OUT.png
"$GODOT" --headless --path src/LaserTank.Game -- --editor --edit '<06l22' --save \
    --levels ABS/COPY.lvl --level 7
```

**`MouseOperation` is why this step existed at all, and finding the way in was the whole job.**
Phase 2 left it as the one stub not because it is hard — it is sixty lines — but because *nothing
could reach it*: it is driven by `MBuffer`, which only `WM_LBUTTONDOWN` and `WM_RBUTTONDOWN` write,
so no keystream and no recording could make the tick call it, so no trace could say whether a port
of it was right. Two phases of differential testing walked past it. What closed it is step 4's own
lesson, applied a second time: **when a feature seems untestable because the oracle has no way to be
asked, the question is what input language is missing.** `--script` grew two tokens — `mXY` and
`nXY`, a left and a right click on cell XY as two hex digits — and both drivers push them into
`MBuffer` exactly as the window proc's non-editor arm does. The last stub became an ordinary trace
diff: **5,000 scripts, 0 divergences**, plus 120 on each of the ten quirk packs.

**What the function actually is, and why porting it moved no rule.** A click is not a move. The
tick's mouse block hands the entry to `MouseOperation`, which floods `findmap` from the *clicked*
cell back to the tank (`FindTarget`, `LTANK2.C:277` — recursive, four-way, over `PF == 0` only),
walks the path backwards, and writes **arrow keys into `RecBuffer`** — two per step where the tank
has to turn first, one where it does not. So the tick still consumes one key at a time from the same
buffer, a recording made with the mouse is indistinguishable from one played on the keyboard, and a
`.lpb` from either replays on the other. Two behaviours in it are worth naming because they look
like bugs and are not: the destination filter (`dx < 3 || (dx > 14 && dx < 19) || dx > 23 ||
tunnel`) **admits water**, so a left-click on water is a legal instruction to drown, and the right
button is a *shot* — turn along the larger axis and fire, with no path and no reachability test at
all.

**The editor's rule about where its arithmetic lives.** `ChangeGO` is `LTANK2.C:809` and writes
`Game.PF` and `Game.BMF` directly, so it is in Core — `src/LaserTank.Core/Editor.cs`, beside
`GraphicsFile.cs` and on the same test: nothing inside `Tick()` can reach it. That placement is what
lets `lasertank-core.exe --edit` run the editor headless and diff it against **the oracle's own
`ChangeGO`**, compiled verbatim from the 2002 C. The commands *around* it — Clear Field, the four
Shifts, the tunnel wait-bit strip on the way in — are `LTANK.C` window-proc cases, so they are
transliterated twice and diffed against each other, exactly as step 4's `z`/`c`/`v` were: for those
the diff proves agreement, for `ChangeGO` it proves truth.

**Three things the C does that a reasonable reading would have got wrong**, all found by writing the
gate rather than by reading:

* **`GetNextBMArray` is declared `[MaxObjects+1]` and initialised with 25 entries**
  (`LTANK.C:18`), so C zero-fills the last two. Rotating thin ice (25) or the tunnel selector (26)
  therefore turns the cell into **dirt**, which is defined behaviour of the 2010 binary and is
  nothing like "rotates to itself". Counting the initialiser and sizing the array at 25 would have
  turned two defined cases into an out-of-range one.
* **Rotating a *tunnel* really is out of range** — a tunnel cell is `0x40 | id << 1 | wait` = 64..79
  — and that read has no defined value, so both engines skip it. `editor_check.py` found the port
  getting this wrong on its first run: returning the cell's own value and calling `ChangeGO` with it
  is *not* a no-op, because `ChangeGO` rewrites `BMF` through `GetOBM`, which answers 1 for a
  tunnel and erased the sprite. Repro `<1alcasca`. This is `GFXInit`'s `!(Mh || Gh)` again: a bug
  with an undefined effect is the one class this port does not keep.
* **The palette's click bound is `i > MaxObjects+1`, so 27 is selectable** — one slot past the last
  drawn sprite. `GetOBM(27)` falls through its range test to bitmap 1, the cell holds an object id
  no table knows, and the *next* level load sanitises it into tunnel 5 (BuildBMField's `pt > 0x19`
  arm). Defined, so it stays; the panel draws the slot as an empty frame so it is at least visible.

**The `.lvl` writer is where constraint 2 stops being an assertion, and it has a trap in it.**
Command 603 writes back **the 576-byte struct it read** — `GetWindowText(Ed1, CurRecData.LName, 30)`
copies at most 29 characters and a terminator into a 31-byte field and touches nothing after that.
So a writer that re-encodes from decoded strings would silently rewrite the bytes *behind* every
name's terminator, and most `.lvl` files in the wild have some there (the tail of an earlier, longer
name). `LevelRecord` therefore holds the raw bytes and edits them in place, and the gate's first
check is the one that matters: **load a level, open the editor, change nothing, save — some 160 levels drawn
from all 23 collections come back byte for byte.** Two more file quirks are kept rather than
smoothed: saving level N into a shorter file **zero-fills the records in front of it** (seeking past
the end and writing does that on Win32 and here), and command 601 clears the hint with
`CurRecData.Hint[0] = 0` — **one NUL**, so the rest of the old hint stays in the record and goes to
disk behind the empty one.

**"A `.lvl` the 2010 binary opens" is measured, not claimed.** The oracle *is* the 2010 loader —
`LoadNextLevel` out of `LTANK2.C`, compiled verbatim — so the gate saves an edited level, loads the
result in the oracle *and* in the port, and diffs the two boards. That is as close to the criterion
as this project can get without running the 2010 binary, and it is the same trick the `.hs` half of
step 4 used.

**The editor is a mode of the window, not a dialog over it**, because that is what the original is:
command 201 swaps the menu bar and the accelerator table (`LTANK.C:1446` picks `hAccelTable2` when
`EditorOn`), hides the nine buttons and repaints the control panel as a palette. There is no
180-pixel control panel here, so the window *widens* and the palette goes beside the board. **The
keys are ACC2** (`lt32l_us.inc:150`), read rather than invented: F9 toggles (it is in both tables),
Ctrl+C clears the field, Ctrl+S saves, Ctrl+H is the hint, Ctrl+arrows shift the board. That table
carries a comment which is really a design rule — *"DONT use Keys that can be entered in the Author
& Level Name field"* — and it is about focus: while an edit control has the caret the accelerators
must not fire. Tab is what moves in and out of the three text fields here, and while one has focus
every letter goes into it.

**One deliberate deviation, and it is a safety one.** Command 603 saves in place, over the `.lvl`
the level came from. In this repo `data/` is the regression corpus — 20,914 levels that every
fidelity gate replays — so an editor that can silently rewrite it is a hazard rather than a feature.
Saving a level that came out of `data/` writes a working copy under `out/levels/` (seeded from the
collection, so the file stays a whole collection) and says so on screen; a collection opened from
anywhere else saves in place. Same rule as *an instrument must not write the player's state*,
pointed at the corpus instead of at the INI.

**A third implementation again, and for step 4's reason.** `Core.Editor` is the port,
`oracle/driver.c`'s `edit_token` is the driver-side transliteration of the `LTANK.C` cases, and
`EditMode` is what the game's mouse actually calls. Two engines prove the arithmetic; the third
proves that what the UI invokes *is* that arithmetic. `--editor --edit STR --save` drives the game's
own palette-and-mouse path from a command line, and `editor_check.py`'s last half compares the 576
bytes it writes with the driver's.

*Exit — MET.* `editor_check.py`: 3,000 edit scripts trace byte-identically between the oracle and
the port with `--field --bmf`; some 160 unedited levels drawn from all 23 collections re-save byte
for byte;
the `GetWindowText` write widths rebuilt in Python and matched; 60 edited levels whose saved
playfield equals the traced board with the tank stamped back in, whose gap records are zero-filled,
and which **the oracle loads and agrees with the port about**; 6 more saved through the game's own
editor and matching the driver byte for byte. `mouse_check.py`: 5,000 scripts, 0 divergences, plus
the ten quirk packs. And the four fidelity gates and the seven earlier Phase 5 gates are green with
all of it in.

**Not in step 5, on purpose:** Load Level inside the editor (command 602 — it is `LoadBox` plus a
`GetOpenFileName`, and the file dialog is the part this port has no equivalent for yet), Save As
(606, the same reason plus `PickBox`), the `LoadTID` tunnel dialog as a *dialog* (the id is a mode
here, cycled with `T`, because a modal prompt per painted cell is worse than a mode), the "save
changes?" prompt on the way out (`Modified` is tracked and shown, but there is no message box), the
Difficulty dialog (225) and Print (126).

**Step 6 — i18n. ☑ DONE.** The original's ten translations, on a picker, in the game.

**This is the one step of the port that deliberately does not transliterate its C, and the decision
was the whole of the step's design work.** `LANGUAGE.C` exists to read a *positional* file:
`Language\Language.dat` is 240 lines in six fixed-size sections (`SIZE_MMENU` 49, `SIZE_EMENU` 24,
`SIZE_BUTTON` 9, `SIZE_TEXT` 48, `SIZE_DIALOGS` 96, `SIZE_ABOUTMSG` 14, `LT32L_US.H:42`), comments
and blanks skipped, each translation in whichever 8-bit codepage its author's Windows happened to
use, with the language chosen by *which of the ten `Setups/` trees you installed* — there is no INI
key and no in-game switch (`LANGFile` is built at `LTANK.C:1421` and never varies). Transliterating
that reader would have bought nothing: **the population of such files is closed.** The ten that
shipped in 2007 are all there will ever be, no rule reads a UI string, and a port that kept the
format would be maintaining a line-counting parser forever to serve files nobody will write again.
So the conversion happens **once**, in `tools/convert_language.py`, and the game reads keyed UTF-8
JSON out of `data/language/`.

That is a deviation from the project's usual answer, so it comes with the usual price: the
conversion has to be *checkable against the artifact*, not merely plausible. Three things make it
so, and none of them is "it loads and looks right".

- **Nothing is hand-typed.** The 153 string keys are parsed out of the frozen `LT32L_US.H` —
  `ButText1..9`, `txt001..txt045`, `REC_Title`, `help01..03`, `HelpFileName`, and the 96 `ID_*`
  dialog slots — and the menu trees' shape, command ids and separator positions out of the frozen
  `lt32l_us.inc`. The text section's numbering has two gaps (there is no `txt003` and no `txt030`),
  which is exactly what a hand-written table gets wrong.
- **The gate runs the conversion backwards.** `lang_check.py` rebuilds every one of the 240 source
  lines out of the JSON — undoes the escape conversion, re-attaches the accelerator hint after a
  tab, re-encodes to that file's own codepage — and compares **bytes** with the original line.
  2,293 lines across the ten files, byte for byte. That is what would catch a mangled accent, a
  shifted section, a dropped string or a mis-keyed slot.
- **The 90 lines it cannot rebuild are asserted, not excused.** Nine lines per file address a menu
  *separator*, and the JSON keeps no text for one because the original never applies one either —
  `ChangeMenuText` checks `ItemInfo.fType == MFT_STRING` and a separator's is not. That is why all
  ten files carry the untranslated word `SEPARATOR` in those slots, and the gate asserts they do.

**The codepages are a measurement, not a guess, and one directory name lies.** Nothing in the
distribution records them. They were established by decoding every file under every candidate and
reading the result: `cp1252` for US/Fr/De/Du/Pt/Sp/Sw, `cp1250` for Hr (Croatian needs č ć ž š đ,
and cp1252 renders them as è ‘ ž š ð), and — the surprise — **`Cs` and `Ct` are Simplified and
Traditional Chinese, not Czech**, in `gbk` and `big5`. Roy Chen's two files, 2,348 and 1,833 high
bytes, identical in size and line count, which is what made them look like a duplicated pair of
Czech files until they were decoded.

**Two findings from the corpus that are worth keeping in view.**

*The `while(!feof(fd))` bug is real and the port is free of it by construction.* `InitLanguage`'s
loop reads with `fgets`, then unconditionally chops the last character with
`szTmp[strlen(szTmp)-1] = '\0'`. On the iteration after the final line, `fgets` returns NULL and —
per C99 7.19.7.2, so this is *defined*, not luck — leaves the buffer unchanged, so **the last
non-comment line is applied twice, the second time one character shorter, into the next slot.** In
the English file that lands the yahoo-group line into `about[12]` with its trailing newline eaten,
which is why the About box shows it twice. It is a genuine quirk of a *loader the port does not
have*, so there is nothing to transliterate it into; it is recorded here rather than in *Quirk
hazards* because no port code can exhibit it.

*Four of the ten files are labelled 90% or less, and the fallback still never fires.* A translator
who skipped a line copied the English one rather than leaving it blank, and the only section any
file actually stops short in is `about` — which does not fall back, because borrowing the English
tail would put two languages in one paragraph. So the shipped corpus resolves **zero** strings
through `Language.Load`'s fill-from-base branch. The first run of the gate asserted that six
languages would exercise it and failed, which was the gate being right: the honest fix was to keep
the fallback (a future file, or a hand-edited one, needs it, and the alternative is
`[ID_WINBOX_03]` on a label) and have `check_fallback` build the partial file the corpus does not
contain — one key blanked, one removed outright, one section truncated, and a translated key that
must *not* be overwritten. Both ways of being absent are separate lines of code and both are tested.

**The one invented behaviour, named as one:** `[DATA] Language`, plus the picker on **Ctrl+L** that
writes it. The original has neither. Ctrl+L is free in `ACC1` — plain `L` is Load Level, and the
editor's own Ctrl+L (602) is on `ACC2`, a different table that only applies while `EditorOn` — and
the picker is modelled on `GraphBox` deliberately rather than for symmetry: the graphics dialog *is*
the original's own Options-menu entry (226), so copying its three load-bearing properties puts the
new dialog where a player already expects this kind of choice. The choice applies immediately (so
the panel redraws in the language being previewed — the file names are `Fr` and `Hr`, and being able
to *read* the answer is the point), there is no Cancel, and the game keeps ticking underneath while
keys go to the dialog.

**Where the strings actually landed.** `GraphicsMenu`'s two English constants became
`ID_GRAPHBOX_00`/`_05`, its pack list `ID_GRAPHBOX_02`/`_03`; `LevelList`'s three captions became
`ID_LOADLEV_00`/`ID_HIGHLIST_00`/`ID_GHIGHLIST_00` (and the level picker stopped saying "Select a
Level", which was ours, and started saying "Pick Level to Load", which is the original's); the win
line takes `txt012`, the playback panel `txt013`/`txt014`, the death line `ID_DEADBOX_DEAD`, the old
score `txt009`/`txt010`/`txt011`, and F5 now retitles the window with `REC_Title` the way
`SetWindowText(MainH, REC_Title)` does. **`HighScores.Describe` had to split in two**, and that is
the rule worth carrying forward: `--play`'s `highscore` line is an *instrument* output that
`list_check.py` parses, so it keeps frozen English labels while the screen gets the localised ones.
A gate's output must not move when the player picks a language — the same rule as *an instrument
must not write the player's state*, pointed at stdout.

*Exit — MET.* `lang_check.py`, four halves: the tab policy measured over all ten files; **2,293
source lines rebuilt out of the JSON and compared as bytes in their own codepage**, 90 ignored
exactly where the original ignores them; the key set and both menu trees checked against the frozen
header and `.inc`; ten `--lang-dump`s identical to a Python rebuild of the same dump, plus three
fallback cases on a synthetic partial language; and ten more resolved identically *inside the game*
through the same `BoardView.Strings` property every label reads, plus 5 INI checks (an absent key
defaults to the base, the picker's choice survives a restart, it really loads, an unknown code
degrades to the base rather than to `[key]` labels, and a foreign key in the file survives). And the
four fidelity gates and the nine earlier Phase 5 gates are green with all of it in.

**Not in step 6, on purpose:** a menu *bar* (the model is there — all 73 items with command ids and
accelerator labels — but the port is still key-driven), the `.ln` files under `Setups/Language/`
(a different and older 4.0-era format, superseded by the `.dat`s and not read by the 2007 build),
`LoadImageFile`'s per-language `Control.bmp` / `Opening.bmp` / `LaserTank.hlp`, and translating the
port's *own* legend lines — those have no key in the original because the original has no such
strip, and they fall back to English by having only an English form.

### What to be careful about

- **Do not "fix" anything on the way past.** Hazards #9 and #11 are both real bugs in the original
  that must survive, and #11 is inside `SetGameSize` — a function this phase has to touch. The one
  exception argued for so far is `GFXInit`'s `!(Mh || Gh)` (step 2): a bug whose only effect is
  undefined GDI has nothing to transliterate into. If a bug has a *defined* effect, keep it.
- **When the original has a table, read the table.** Step 2's laser bug is the pattern: `LaserOffset`
  is initialised to 10 at `LTANK2.C:46` and then *reassigned per board size* by `SetGameSize`
  (10/13/17). Taking the initialiser for the rule and scaling it drew the laser three times too
  wide, and nothing but a pixel measurement would have said so. Same species as re-deriving `BMF`
  from `PF` (hazard #2).
- **An exit criterion that only says "nothing changed" is not one.** Step 3 was planned as "adding
  audio must change no trace, so the gates staying green *is* the proof" — which would have shipped
  a game whose tank drove around in silence, because `SoundPlay(S_Move)` had been dropped in Phase 2
  and no trace carried sound. The fix is the same one Phase 1 found when "186/186 reach the flag"
  turned out to be the wrong bar: make the thing being added *observable*, then diff it against the
  C. Every step after this one should ask what its own new output is and how the oracle can be made
  to emit it too. **Step 4 is what generalised the trick:** `--sound` made the tick's decisions
  visible, and `--script` made the *player's commands* expressible — undo, Save and Restore Position
  are `WM_COMMAND` cases that no keystream can reach, and inventing a token stream for them turned
  three unreachable functions into an ordinary trace diff. When the next step's feature seems
  untestable because the oracle has no way to be asked, the question is what input language is
  missing rather than whether the C can answer.
- **When the oracle can answer, ask it before writing down what "correct" means.** Step 4's exit
  criterion — "a recorded game round-trips, all three agree" — silently assumed that a recording
  replays to the position it was saved from, and the oracle disproved that on its own in one command
  (hazard #13). The gate now asserts it only where it holds. A criterion written before the
  measurement is a hypothesis; three of step 4's four surprises were the gate contradicting one.
- **A guard the original gets from Windows still has to be written down somewhere.** Two of step 4's
  three invented behaviours were guards: a null check in `RestorePosition` and a resurrect-on-restore
  that command 112 does not do. Both looked like the obviously-sane reading of a function whose C is
  three lines with no guard at all, because the guard is a grayed menu item. The rule that came out
  of it: transliterate the function literally, and put the menu's own condition in the *driver*,
  named after the `EnableMenuItem` call it stands for — then both script drivers can apply it and be
  diffed against each other.
- **An instrument must not write the player's state**, and that now covers three files rather than
  one: the INI (step 2), and — since step 4 — the `.hs` and anything under `out/recordings/`. The
  test is whether a gate could be run eight times in parallel and leave the tree as it found it.
  `tick_check.py` replaying 208 winning recordings wrote `.hs` files into six collections of `data/`
  before this was noticed, and `.gitignore` is why nothing said so.
- **Measure pixels, do not look at them.** Both step 2 bugs were invisible in a screenshot until
  someone knew the number to expect. Anything geometric — bar widths, outlines, cell rects — gets a
  reader-and-compare in `tools/options_check.py`, whose laser check takes the coordinates from the
  engine and the pixels from the renderer so the two cannot agree by construction.
- **The 20 Hz tick is not a rendering rate.** Interpolate sprites between ticks; a 144 Hz display
  must not consume 144 keys a second. Both halves are done and measured — see step 1, and note that
  the key-rate half is the original's own pending-key test rather than anything about frames.
- **`.lpb` compatibility is bidirectional.** The 2010 binary must be able to play what Godot
  records. `LevelFile.WritePlayback` already writes the real 66-byte header format, and the
  solver's recordings are the existing evidence that it round-trips.
- **A gate that can write into `data/` is a bug in the gate, and now so is a *feature* that can.**
  Step 4's rule was about instruments; step 5's editor is the first thing in the project a *player*
  drives that writes a file the corpus is made of. It copies into `out/levels/` instead. The test is
  unchanged — could this be run eight times in parallel and leave the tree as it found it — but the
  thing being tested is no longer only a tool.
- **Reading a format is not the same as being able to write it.** `LevelFile.ReadLevel` decodes
  strings and has been right since Phase 1; a writer built on those decoded strings would have
  passed every read-back test in the project and still corrupted 20,914 levels' worth of trailing
  bytes. When constraint 2 says *writable*, the check is a byte-for-byte identity round trip on
  files nobody edited, not a load-and-compare.
- **A gate's own output must not move with a player's setting.** Step 6 is where this bit: the
  `highscore` line `--play` prints is parsed by `list_check.py`, and localising the labels in it
  would have made a fidelity tool's output depend on `[DATA] Language`. `HighScores.Describe` has
  two forms for that reason — frozen English for the instrument, the loaded language for the screen.
  Same rule as *an instrument must not write the player's state*, pointed at stdout instead of at a
  file.
- **`text=True` on a subprocess decodes with the *locale* codec, and fails silently.** Every gate in
  `tools/` used it, which was fine while every output was ASCII. Step 6's is not: on the first byte
  cp1252 leaves undefined (0x8d, inside `ã`) the reader thread raises `UnicodeDecodeError`,
  `subprocess` swallows it in the thread, and `p.stdout` comes back **empty** — so four of the ten
  languages looked like a crashed game rather than a decoding bug. `lang_check.run_godot` captures
  bytes and decodes UTF-8 explicitly. Any new gate whose output can carry non-ASCII must do the
  same.
- **Not everything the original does is a rule worth porting, and the test is whether the input
  population is open.** Step 6 declined to transliterate `LANGUAGE.C` because the files it reads are
  a closed set of ten, frozen in 2007, and nothing in the rules reads a UI string — so the format is
  a one-time import rather than a behaviour. That reasoning does **not** generalise to `.lvl`,
  `.lpb`, `.hs` or the graphics packs: those are all still written, by this port among others, which
  is why every one of them is read and written by a transliteration. When the temptation to convert
  rather than transliterate comes up again, the question is who else will ever write the file.
- **Godot 4.7.2 Mono** is installed but has no `godot` alias (needs admin) — call the `.exe` by
  path; see *Environment notes*.

---

## Source map

| File | Lines | Role |
|---|---|---|
| `original/src/LTANK.C` | 1572 | Win32 window proc. **`WM_TIMER` at `:579` is the real game loop.** |
| `original/src/LTANK2.C` | 1834 | Game logic *and* GDI rendering, interleaved |
| `original/src/LTANK_D.C` | 1319 | Dialogs (level picker, high scores, playback, graphics) |
| `original/src/LANGUAGE.C` | 279 | i18n from `Language.dat`. **Read, not transliterated** — step 6 says why; `LT32L_US.H` and `lt32l_us.inc` are the tables it needs |
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
anti-tanks their turn, and `AddKBuff` (`LTANK2.C:256`) filters nothing. **Which keys can a human
actually record one with?** `WM_KEYDOWN` admits `wparam >= 32 && wparam <= 40` (`LTANK.C:572`), so
exactly four: **33-36 = PageUp, PageDown, End, Home.** (Phase 5 step 1 pinned this down; an earlier
revision of this note said "any other key", which over-states it.) **No human ever did:** all
54,162 bytes of all 187 `.lpb` are those five keys, zero exceptions. The "wait" the tutor hints describe (level 4 *"Move up, wait"*, level 14
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

**Writing one back is not symmetric with reading it** — see hazards #16, #17 and #18. The editor
rewrites the record it read rather than re-encoding it, its string writes stop one byte short of
each field, saving past the end of a file zero-fills the gap into playable levels, and Clear Field
NULs only the first byte of the hint. `LaserTank.Core.LevelRecord` is where all four live.

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
    Never drive logic from `_process`. **Settled in step 1:** the tick is `_PhysicsProcess` with
    `physics_ticks_per_second = 20`, `_Process` only redraws, and `BoardView` refuses to start if
    that setting and `GameDelay` disagree. The "must not consume 144 keys a second" half turned out
    not to be about the frame rate at all — `WM_KEYDOWN` drops auto-repeat while a key is still
    pending (`RB_TOS > Game.RecP`), and that is the whole mechanism.
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
13. **Undo restores `Game` and nothing else, so a recording made with undos in it is not a
    transcript of what the player saw.** `UndoStep` is `Game = UndoBuffer[UndoP]`
    (`LTANK2.C:459`) and `TGAMEREC` holds the four playfields, the tank and the two scores.
    The laser is a *separate* global; so are `wasIce`, `WaitToTrans`, `ConvMoving`,
    `BlackHole` and `LaserBounceOnIce`; and the slide stacks are **cleared** by the next three
    lines rather than restored. So the world after an undo is not the world a clean replay of
    the rewound keystream would produce — while `Game.RecP` is rewound as if it were. Keys
    pressed after the undo then land in a different game and `RecP` keeps counting, so
    `RecBuffer[0..RecP)` — which is exactly what `WM_SaveRec` writes — can replay somewhere
    else entirely. Measured in the **oracle alone**, no port involved: flagship level 1,499,
    script `ffrlrfzcrfzzzzzzfufuZvfdddffdflzzzflllflrrrfr` plays to 100 ticks and 2 moves, and
    the 13 keys it leaves behind replay to 60 ticks and 6 moves. This is not a port bug and not
    a bug to fix; it is why `tools/roundtrip_check.py` asserts "the recording reproduces the
    play" **only for command-free scripts** and counts it for the rest. It also means an
    undo-heavy `.lpb` is still a perfectly valid recording — it wins or loses on its own terms,
    and all three engines agree on which.
14. **`SaveGame` starts blank, and Restore Position is only unreachable because of a menu.**
    `SaveGame` is a file-scope `TGAMEREC` (`LTANK2.C:59`), so command 112 before command 111
    copies a *zeroed* record over the live game: tank at 0,0 facing 0, every playfield cell 0.
    The copy itself is defined, but command 112 does **not** stop sliding (unlike `UndoStep`
    three lines away), so the tank then keeps whatever ice slide was running and `ConvMoveTank`
    walks off the end of `Game.PF` — out of bounds in C, a thrown
    `IndexOutOfRangeException` in the port. The original cannot get there: `LoadLevel` grays
    command 112 and only 111 enables it (`LTANK2.C:1028`, `LTANK.C:957`). So the engine
    reproduces the blank record faithfully and `Engine.CanRestore` carries the *menu's* guard,
    which both script drivers apply before issuing the command. `tools/undo_check.py` found
    this on its first run against `Tutor.LVL` level 85 with the three-token script `llv`.
15. **A left-click is allowed to drown you.** `MouseOperation`'s destination filter
    (`LTANK2.C:314`) is a hand-written range test on the object id — `(dx < 3) || (dx > 14 && dx <
    19) || (dx > 23) || tunnel` — and `dx < 3` covers **water**, so clicking on water is a legal
    move order and the pathfinder will happily walk the tank in. It also admits a one-way from the
    wrong side, where the key walk simply stalls against it. Neither is guarded there and neither
    is guarded here. The same function's right-button arm has *no* filter at all: it turns along
    the larger axis and fires, without a path or a reachability test.
16. **A level record is written back, not re-encoded, and the editor's write widths are one short
    of the field.** Command 603 writes the `TLEVEL` struct it read, and
    `GetWindowText(Ed1, CurRecData.LName, 30)` puts at most 29 characters and a terminator into a
    31-byte field — so byte 30 is *never* written by the editor, and the bytes between a short
    name's terminator and offset 29 keep whatever the record already held. Most `.lvl` files in
    the wild have the tail of an earlier, longer name sitting there. A writer that re-encodes from
    decoded strings passes every read-back test and still rewrites every unedited level in a
    collection, which is exactly the failure constraint 2 exists to prevent. `LevelRecord` holds
    the raw 576 bytes; `editor_check.py`'s first check is that some 160 untouched levels drawn from all 23
    collections re-save byte for byte.
17. **Saving level N into a shorter `.lvl` invents levels.** Command 603 seeks to
    `(CurLevel-1) * 576` in a file opened `OPEN_ALWAYS` and writes; the gap is zero-filled, and a
    zero-filled record is a legal level — all dirt, no name, no author, difficulty 0 — which the
    2010 binary lists and loads. Same species as the `.hs` padding quirk in step 4, and kept for
    the same reason: the file the original writes is the file this has to write.
18. **Clear Field truncates the hint, it does not erase it.** Command 601's line is
    `CurRecData.Hint[0] = 0` — one NUL into a 256-byte field — so the rest of the old hint stays
    in the record and is written to disk behind the empty one. Nothing reads it, and it is in
    every `.lvl` the 2010 editor has ever produced from a cleared field.

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
  language/   the ten translations as keyed UTF-8 JSON, converted once from
              original/src/Setups/*/Language/Language.dat -- see Phase 5 step 6
oracle/     the C reference oracle — see oracle/README.md
  stub/       minimal <windows.h> that shadows the real one
  win32_stub.c  real memory/files/messages, no-op GDI
  driver.c    LTANK.C globals + window proc + the WM_TIMER tick loop + tracing
  build.sh    gcc -x c -I stub -I original/src
src/        the C# port         build.sh -> build/lasertank-core.exe + lasertank-solve.exe
  LaserTank.Core/  Objects.cs GameState.cs LevelFile.cs Engine.cs  (no Godot here)
                   Engine.Search.cs — snapshot/restore, ApplyKey, StateHash
                   GraphicsFile.cs — .ltg + BMP readers, BMSTA/ColorList (Phase 5)
                   SoundFile.cs — the .wav reader and lt_sfx.c's id->name table
                   Engine.Sound.cs — SoundPlay's body: SoundLog?.Add, nothing else
                   Editor.cs — ChangeGO, the Shifts, Clear Field (Phase 5 step 5);
                   in Core on GraphicsFile.cs's test, not reachable from Tick()
                   Language.cs — the UI strings + both menu trees (step 6).  The
                   one file here that is NOT a transliteration; step 6 says why
                   LevelFile.cs also carries LevelRecord — the raw 576 bytes and
                   the editor's own write widths (hazards #16-#18)
  LaserTank.Cli/   Program.cs TraceWriter.cs — the oracle's CLI, the oracle's trace
                   EditDriver.cs — `--edit`, the editor as a token stream
                   LangDump.cs — `--lang-dump` / `--lang-list`, step 6's own output
  LaserTank.Solver/ the batch solver and the interactive driver — see SOLVER.md
  LaserTank.Game/  the Godot 4.7 project.  BoardView.cs draws Game.BMF and routes
                   keys, Session.cs is LTANK.C's driver half (WM_TIMER, WM_KEYDOWN,
                   WM_Dead, ReStart, WM_SaveRec, commands 110/111/112/114/124),
                   PlayMode.cs is that driver with a scripted player, Atlas.cs
                   hands the sheet to the renderer, Options.cs is LaserTank.ini,
                   Packs.cs is GFXInit's three modes, GraphicsMenu.cs is GraphBox,
                   LevelList.cs is LoadBox/HSList/GHSList (one class, three modes),
                   HighScores.cs is AssignHSFile + CheckHighScore + the HS global,
                   Recorder.cs is command 123 and PBWindow, Sfx.cs is lt_sfx.c (one
                   player, monophonic), EditMode.cs is commands 201/601/603/604/
                   605/701-705/710-713 and the palette (step 5), LanguageMenu.cs
                   is step 6's picker (Ctrl+L -- a dialog the original has no
                   equivalent for), Step4Check.cs and Step6Check.cs are the
                   headless dumps list_check.py and lang_check.py compare
                   against, Paths.cs finds data/.  Built by Godot or `dotnet build`, never published
                   into build/
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
atlas_check.py    Phase 5 step 0's gate: every BMF/BMF2 value over the corpus
                    lands inside the 10x6 sprite grid, and every graphics pack
                    decodes to the same 320x192 pixels in Python and in C#
tick_check.py     Phase 5 step 1's gate: every recorded .lpb replayed through
                    Godot's own input path and 20 Hz tick must agree with the C
                    oracle on result/ticks/moves/shots, must re-record the same
                    keystream, and its own .lpb must replay in the oracle.  Plus
                    --tick-rate: the real driver, timed, must be 20 ticks/second
options_check.py  Phase 5 step 2's gate: LaserTank.ini's semantics and round
                    trip (defaults, foreign keys kept, the choice surviving a
                    restart, remember-last-level), every graphics pack loading
                    with mode 1 == mode 2 pixel for pixel, the three board
                    sizes, and the laser bar measured out of a --shot PNG in
                    the cell the engine names.  Opens three brief windows;
                    --no-window skips that half
sound_check.py    Phase 5 step 3's gate: every recorded .lpb replayed through
                    both engines with --sound, so the per-tick SoundPlay id
                    stream must be identical too; the sixteen .wav files
                    decoding to the same PCM in Python and in C#; and
                    [OPT] Sound's semantics, including the original's
                    case-sensitive test for "Yes"
undo_check.py     Phase 5 step 4's gate: Undo and Save/Restore Position, which
                    no keystream can reach, driven through --script -- a token
                    stream both engines take -- and trace-diffed against the
                    oracle's own UndoStep.  Shrinks a divergence like fuzz.py.
                    --replay LEVEL SCRIPT re-checks one with --field
list_check.py     Phase 5 step 4's second gate: the three list dialogs' rows and
                    the .hs writer's bytes, rebuilt in Python and compared
                    against --check-lists / --check-scores.  Neither output is
                    engine behaviour, so neither can go through the oracle;
                    this is the sprite-sheet pattern instead.  Plus `win`:
                    level 1's own recorded solution replayed as a script with a
                    live --ini, to check the flag case actually posts a score
roundtrip_check.py Phase 5 step 4's exit criterion: for each of N undo-carrying
                    scripts, six runs -- the script through both engines
                    (trace-diffed) and through Godot's own command path, then
                    the .lpb it records through both engines (trace-diffed) and
                    through Godot's playback path
mouse_check.py    Phase 5 step 5's first gate: MouseOperation -- the last stub,
                    unreachable from any keystream -- driven through --script's
                    new click tokens (mXY / nXY) and trace-diffed against the
                    oracle's own copy.  Shrinks token-wise, like undo_check
editor_check.py   Phase 5 step 5's exit criterion, in three halves: 3,000 edit
                    scripts through --edit, trace-diffed against the oracle's own
                    ChangeGO; the .lvl writer (an untouched level re-saves byte
                    for byte across all 23 collections, the GetWindowText write
                    widths rebuilt in Python, the gap zero-filled, the saved
                    board tied to the trace, and the oracle -- which *is* the
                    2010 loader -- opening what was written); and the game's own
                    editor saving the same bytes as the driver
lang_check.py     Phase 5 step 6's gate, in four halves: the tab policy that
                    licenses its one asymmetry; 2,293 source lines rebuilt out of
                    data/language/*.json and compared as BYTES in each file's own
                    codepage against the frozen Setups/*/Language.dat; the key set
                    and both menu trees against LT32L_US.H and lt32l_us.inc; the
                    C# --lang-dump and the game's own --check-lang against a
                    Python rebuild, plus a synthetic partial language for the
                    fallback and 5 INI checks.  No oracle: see step 6 on why
convert_language.py the one-time import behind that -- reads the ten Language.dat
                    files with their measured codepages and the two frozen
                    headers, writes data/language/*.json.  `--check` reports
                    staleness without writing
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
  needs admin to be created, so call the `.exe` by path — `..._console.exe` if you want stdout.
  `tools/atlas_check.py` finds it by that glob; `$LT_GODOT` overrides. A fresh checkout needs one
  `"$GODOT" --headless --path src/LaserTank.Game --import` before `--path` will run the project,
  and `Godot.NET.Sdk` restores from nuget.org on the first build (the install also ships it under
  `GodotSharp/Tools/nupkgs/` if that machine is offline).
- **`godot --path` makes the *project* the working directory**, so every relative path handed to the
  game resolves against `src/LaserTank.Game/` and not the shell's cwd — `--lpb
  data/demos/LaserTank/00001.lpb` looks for it under `src/LaserTank.Game/data/`. Worse than a wrong
  answer: `_Ready` throws, Godot logs the exception and **keeps the window open**, so the run hangs
  instead of failing. Pass absolute paths (`tools/options_check.py` does), and read a hung `--shot`
  as a path error until proven otherwise.
- **`godot --path` does not build C#, and says nothing about it.** It loads whatever assembly is
  already in `src/LaserTank.Game/.godot/mono/temp/bin/`, so editing `Session.cs` and running the
  project — or a gate — silently exercises the *previous* build. Only the editor builds on run.
  Verified the ugly way: a changed string on disk did not appear in the output, while the gate
  still reported a green 208/208. All four Godot-backed gates now call
  `engines.build_godot_game()` first (`tick_check.py` and `options_check.py` always,
  `atlas_check.py` and `sound_check.py` in their cross-check halves), and anything new that runs the
  project must do the same. By hand:
  `dotnet build src/LaserTank.Game/LaserTank.Game.csproj`. It builds into `.godot/` and
  `src/LaserTank.Core/bin/`, never `build/`, so it is safe beside a live solve.
- **A running solver blocks `src/build.sh`, but not the compilers.** `dotnet publish -o build`
  cannot replace `build/LaserTank.Core.dll` while a `lasertank-solve.exe` holds it open, so during
  a long solve build each project into its own `bin/` instead — `dotnet build
  src/LaserTank.Cli/LaserTank.Cli.csproj -c Release`, and then either `replay_all.py --engine
  src/LaserTank.Cli/bin/Release/net8.0/lasertank-core.exe` or, for everything built on
  `tools/engines.py` (`sweep.py`, `atlas_check.py`, `sound_check.py`, `fuzz.py`), **`$LT_CORE`**,
  which overrides `engines.CORE` for exactly this reason. **Session 27 added the solver's half of
  that:** `LT_SOLVE=<exe>` overrides `build/lasertank-solve.exe` in `tools/bench.sh`,
  `campaign.sh` and `second_pass.sh`, so a solver change can be benched from
  `src/LaserTank.Solver/bin/Release/net8.0/` while a long solve still holds the published binary. That is how Phase 5 steps 0 and 3 were
  checked without touching a live solve. The Godot project never publishes into `build/` at all.
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
- **And a *doubled* backslash arrives as a single one even in `cat > f <<'EOF'`** -- measured
  this session: `a\\nb` in the heredoc lands on disk as `a\nb`, while a lone `a\nb` lands
  unchanged, so escaping for the inner language is exactly backwards here.  What does work for a
  Python patch script that has to match C or C# source containing a `\n` escape: write the script
  with `cat > file <<'EOF'`, single backslashes throughout, and **raw** string literals -- the
  triple-quoted raw form, switching to the single-quoted one where the text itself contains a
  double quote.  A patch that fails to match a plausible-looking string is usually this.
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

**2026-09-07, session 24 — Phase 5 step 0: the board on screen.** The Godot project, the sprite
sheet readers, and a gate for both. `src/LaserTank.Game/` renders any of the 2,030 flagship levels
from `Game.BMF` with any of the four sheets at any of the three zooms; `SpriteSheet` in Core reads
`.ltg` and the internal RLE `.bmp` pair; `tools/atlas_check.py` is green on 2,347/2,347 levels and
4/4 sheets. Two findings paid for themselves immediately: the mask must be applied per-sprite by
`BMSTA[]` (applying it to everything erases the board, because an opaque sprite's mask cell is
solid white) **and to the tunnel anyway**, which is what makes the eight tunnel colours visible —
the first render showed eight identical black discs. The sheet cross-check exists for exactly that
class of bug: one decoder agreeing with itself proves nothing, so Python's reader and C#'s must
produce the same sha256. Run under a live solve, so `build/` was never republished; the corpus was
re-replayed against a locally built core instead (187/181/112, and `game-objects` 16/16 identical
to the oracle).

**2026-09-07, session 25 — Phase 5 step 1: the game is playable.** A fixed 20 Hz
`_PhysicsProcess` tick driving `Engine.Tick()` + `Pump()`, the keyboard through the original's own
`WM_KEYDOWN` filter into `AddKBuff`, restart, death and win handling, the laser drawn, and `F6`
writing a `.lpb`. Step 1's exit criterion was met and then mechanised: `tools/tick_check.py` replays
all 208 corpus recordings through Godot's own input path and tick and gets **208/208 agreement with
the C oracle on result, tick count, moves and shots**, with the re-recorded keystreams byte-identical
and each Godot-written `.lpb` replaying in the oracle to the same verdict — and, separately, 100
ticks in 5 seconds from the real driver. Three things the input path taught: the `WM_KEYDOWN` filter
is **VK 32..40**, so PageUp/PageDown/End/Home are recordable one-tick waits (which forced every dev
binding off those keys — step 0 had used exactly them); auto-repeat is dropped **only while a key is
still pending**, which is the whole of hazard #10's "not 144 keys a second" and has nothing to do
with frames; and `WM_SaveRec` saves `Game.RecP`, the keys consumed, not `RB_TOS`. The laser was
verified by reading pixels out of a `--shot` PNG against the trace's own `L=` state rather than by
eye — tick 40 of `00001.lpb` is a mirror bounce and carries exactly `UpDateLaserBounce(1,4)`'s two
half-bars. That is how the one real rendering bug of the session was found: on the ice double-step
(hazard #1's `goto LaserMoveJump`) the bend happens a cell back, so comparing `laser.Dir` across
the tick paints the two half-bars in a cell the laser went *straight through* — suppressed now by a
distance test, and checked against the PNGs at ticks 526/527/528 of `Tutor-with-Playbacks` 93. One deliberate deviation, documented in the step: a fresh `Engine` per level and per
restart, because a recording made on a dirty engine may not replay on a clean one and clean is the
only configuration the oracle equivalence was ever established for. `LaserTank.Core` was not
touched. Run under a live solve again, so `build/` was never republished; `replay_all` (187/181/112),
`test_difftrace` (29) and `atlas_check --sheets-only` (4/4) were re-run to confirm nothing rotted,
and `sweep`/`test_fuzz` were skipped as they could not have — Core is byte-identical. One trap
found while checking the handoff: **`godot --path` does not compile C#**, so both Godot-backed
gates now build before they measure.

**2026-09-07, session 26 — Phase 5 step 2: the options, the menu, and two pixel bugs.**
`LaserTank.ini` under the original's own section and key names, a `GraphBox`-shaped graphics menu on
`G`, `GFXInit`'s external `game.bmp`/`mask.bmp` mode, remember-last-level, and
`tools/options_check.py` — 20 checks, green. The session's real find was reported by eye and then
pinned by the gate: **the laser was two to three times too wide**, because `LaserOffset` was read as
the initialiser at `LTANK2.C:46` scaled to the cell (10-of-32) when it is actually reassigned per
board size by `SetGameSize` — 10, 13, 17, giving a bar of 4, 6 and 6 px. Fixing it exposed a second
one underneath: Godot's `DrawRect(filled: false, width: 1)` strokes *centred* on the edge, so the
laser's and the tunnels' 1 px outlines were rounding outside the rect on two sides and bleeding into
the neighbouring cell, where GDI's `Rectangle()` keeps its pen strictly inside. Both are ours, not
the original's, and neither was visible without a number to expect — hence the gate's laser check,
which takes the coordinates from the engine and the pixels from the renderer so they cannot agree by
construction. One bug in the original is deliberately *not* reproduced (`GFXInit`'s
`!(Mh || Gh)`, a `||` for an `&&`, whose only effect is undefined GDI); hazard #11 next door still
is. Core gained one method — `SpriteSheet.LtgHeader`, the header without the bitmaps, which is what
the dialog's listbox reads — and was re-verified against the corpus (187/181/112) built into its own
`bin/` beside a live solve; `tick_check` 208/208 and `atlas_check` both stayed green. **The scope
was also clarified this session and it changes how the rest of the phase should be read: only the
puzzle mechanics have to match the original exactly — the UI is expected to be redesigned later.**
See the note at the top of Phase 5.

**2026-09-07, session 27 — Phase 5 step 3: sound, and the differential that made it a real step.**
The sixteen WAVs play, off the ids `Tick()` already computes; `S` mutes and `[OPT] Sound` remembers
it. The step's planned exit criterion — "the gates stay green, i.e. audio changed no trace" — was
replaced on the way in, because it can only ever prove that sound changed nothing: `--sound` now
adds `SF=<ids>` to a trace line on **both** engines (the oracle records them in its own `SoundPlay`
stub, the port in `Engine.SoundLog`), and `tools/sound_check.py` replays all 208 recordings through
both: **208/208 identical including the whole per-tick sound stream**, all sixteen ids exercised.
That found the bug on the first run — `SoundPlay(S_Move)`, dropped from `UpDateTankPos` in Phase 2
as paint, 23,808 calls in the corpus — plus two more calls that live in `LTANK.C` and so had never
been in the core: `S_EndLev` at the flag and `S_Die` in `WM_Dead`. Three restored calls, and the
corpus is now what says the set is complete. The core's cost is one word (`SoundPlay` is a partial
method; `SoundLog?.Add` is the body, null for the solver and every headless trace). Two properties
of the original's audio were kept that a nicer implementation drops: `PlaySound` with
`SND_ASYNC`/no `SND_NOSTOP` is **monophonic**, so one player and last-call-wins — and therefore only
the last sound of a tick is ever heard, while the *list* is what the gate diffs. Muting stayed out
of the engine on purpose: `lt_sfx.c` returns before `PlaySound` when `!Sound_On`, but the decisions
are identical, and a muted game must not trace differently. The WAVs are read from frozen
`original/src/Sounds/` at run time and cross-checked Python-vs-C# by sha256, like the sprite sheets;
the id→name table is carried twice for the same reason. One thing no gate covers, and the only
claim in the step that was checked by ear rather than measured: that `AudioStreamPlayer` actually
makes a noise -- confirmed by hand at the end of the session.

**2026-09-07, session 28 — Phase 5 step 4: the game around the game, and `--script`.** Undo, Save
and Restore Position, the level picker, both high-score lists, the `.hs` writer, and record/playback
at all three speeds. The step's problem was not the features but the *check*: Undo, 111 and 112 are
`WM_COMMAND` cases, so **no keystream can reach them** — which is exactly why Phase 2 left `UndoStep`
unported and why the undo buffer had been maintained for two phases with nothing reading it back.
So the commands were made expressible the way sound was made observable: `--script`, a token stream
consumed one token per tick (`u d l r f` press on drain, `.` idles, `z` = 110, `Z` = the DeadBox's
undo, `c`/`v` = 111/112), implemented three times on purpose — `oracle/driver.c`, `LaserTank.Cli`,
and `PlayMode` so that what the `U` key calls is checked and not just what the engine does. The
oracle's `UndoStep` is the 2002 C, compiled verbatim, and had never been read by this project.

It paid for itself immediately. `undo_check.py`'s first campaign found a null guard in
`RestorePosition` that had invented behaviour the C does not have (`SaveGame` is a *zeroed* global,
so restoring before saving blanks the board — hazard #14), then found that the resulting state walks
`ConvMoveTank` off the end of `Game.PF`, so the menu's own gray-out is now carried by both drivers.
`roundtrip_check.py` then disproved the step's own exit criterion in its most natural reading:
**a recording made with undos in it is not a transcript of what the player saw**, because `UndoStep`
is `Game = UndoBuffer[UndoP]` and `Game` does not contain the laser (hazard #13) — measured in the
oracle alone, flagship level 1,499, 100 ticks and 2 moves for the play against 60 and 6 for the 13
keys it leaves behind. And a third invention was caught the same way: `RestorePos` resuming a dead
game, which command 112 does not do. Undo is the way back from a death; that is what the buffer is
for. Two outputs that no oracle can emit — the three list dialogs' `sprintf` rows and the `.hs`
file's bytes — went through the sprite-sheet pattern instead, rebuilt in Python and compared
(`list_check.py`), which is where the `HS` global turned out to be load-bearing: `CheckHighScore`
pads a sparse `.hs` with it *before* refreshing it, so the padding carries the previous level's
initials. The keys were moved onto the original's own accelerator table (`ACC1`), which cost step 2's
`S` and `G` their old meanings. One deviation is recorded rather than fixed: a restart loses the undo
history, because keeping it means keeping the `Engine` across the restart and quirk #12 says a
recording must replay from clean. **`test_fuzz.py` was not re-run this session** — a solve was live
throughout and it rebuilds the core, which Windows will not allow while `lasertank-solve.exe` holds
`build/LaserTank.Core.dll` open (see *Environment notes*). Everything it gates is unchanged:
`Engine.cs` is 89 added lines and none removed. Run it once the solve is done.

**2026-09-08, session 29 — Phase 5 step 5: the mouse, and the level editor.** The step had two
halves and one cause: both are driven by the mouse, which is the one input this project had never
had. The first half closed the last stub. `MouseOperation` survived two phases of differential
testing not because it is hard but because **nothing could reach it** — it is driven by `MBuffer`,
which only the two mouse-button messages write — so step 4's lesson was applied a second time and
`--script` grew `mXY` / `nXY`, a left and a right click. The last stub became an ordinary trace
diff: 5,000 scripts, 0 divergences, plus 120 on each of the ten quirk packs. `AddKBuff` moved into
`Engine` on the way (it is `LTANK2.C:256` and `MouseOperation` calls it); the drivers' copies
delegate. Two behaviours in the function are hazard #15 now: the destination filter admits **water**,
so a left-click can be a legal instruction to drown, and the right button is a shot with no path
and no reachability test at all.

The second half is the editor, and its exit criterion is the one that turns constraint 2 from an
assertion into a demonstration. `ChangeGO` is `LTANK2.C:809`, so it went to Core (`Editor.cs`,
beside `GraphicsFile.cs` and on the same test), which is what let `--edit` run the editor headless
and diff it against the oracle's own copy: 3,000 scripts, 0 divergences. The gate found the port's
one real bug on its first run — Shift+click on a **tunnel** indexes past `GetNextBMArray`, and
"rotate to itself" is not the same as "skip", because `ChangeGO` rewrites `BMF` through `GetOBM`
and erased the sprite (repro `<1alcasca`). It also settled the array's real size: the declaration is
`[MaxObjects+1]` and the initialiser has 25 entries, so C zero-fills two and rotating thin ice or
the tunnel selector really does turn the cell into dirt.

**The `.lvl` writer is where reading and writing turned out not to be symmetric.** Command 603
writes back the struct it read and `GetWindowText` stops one byte short of each field, so a writer
that re-encodes from decoded strings passes every read-back test and still rewrites every unedited
level in a collection — hazard #16, and the reason `LevelRecord` holds raw bytes. Two more file
quirks are kept rather than smoothed (#17, the zero-filled gap that invents playable levels; #18,
Clear Field NULing only the hint's first byte). *"A `.lvl` the 2010 binary opens"* is measured
rather than claimed: the oracle **is** the 2010 loader, so the gate saves an edited level and makes
the oracle open it. One deliberate deviation, and it is a safety one: a level that came out of
`data/` saves to a working copy under `out/levels/`, because `data/` is the regression corpus and an
editor that can silently rewrite it is a hazard rather than a feature — the same rule as *an
instrument must not write the player's state*, pointed somewhere new.

Also this session: **holding U now keeps undoing.** A Win32 accelerator repeats — auto-repeat
`WM_KEYDOWN`s are ordinary `WM_KEYDOWN`s and `TranslateAccelerator` translates every one — so
dropping every echo in `_UnhandledInput` had quietly made undo a one-shot. That is a port
regression, not a UI choice, so it is fixed rather than argued about; the other accelerators stay
one-shot on purpose, which *is* a UI choice and is written down as one.

**2026-09-08, session 30 — Phase 5, step 6: i18n, and the phase is finished.** The ten
translations, converted once out of `original/src/Setups/*/Language/Language.dat` into keyed UTF-8
JSON, on a Ctrl+L picker, wired into every label the game already drew. **The step's real work was
the decision not to transliterate `LANGUAGE.C`** — the file it reads is a positional 240-line format
in ten different 8-bit codepages, chosen by which installer you ran, and the population of such
files closed in 2007; the reasoning, and the test it generalises to ("who else will ever write this
file"), are in the step and in *What to be careful about*. The price of that deviation is a gate that
runs the conversion **backwards**: `lang_check.py` rebuilds 2,293 source lines out of the JSON and
compares them as bytes in each file's own codepage, and asserts that the 90 it cannot rebuild are
exactly the menu-separator lines the original does not apply either.

Four things the measurements said and guessing would not have. The codepages are nowhere in the
distribution and had to be established by decoding — and **`Cs`/`Ct` turned out to be Simplified and
Traditional Chinese, not Czech**, which the identical file sizes had disguised. `Du`'s about-message
line 233 begins with a **real 0x09 byte**, so the round trip's escape inversion is deliberately
asymmetric between the menu sections and the rest, with a `check_tab_policy` half that measures both
halves of that claim over all ten files rather than assuming it. **The fallback never fires on the
shipped corpus** — the partial translations copied English lines instead of leaving them blank, so
the gate's first run correctly failed an assertion that six languages would exercise it, and the fix
was a synthetic partial language rather than a weakened check or a deleted branch. And `text=True`
on a subprocess decodes with the *locale* codec: on `ã`'s 0x8d the reader thread raises inside
`subprocess` and `p.stdout` comes back empty, so four languages looked like a crashed game.

Two smaller rules came out of it. `HighScores.Describe` **split in two** — a gate parses `--play`'s
`highscore` line, so an instrument's output must not move when the player picks a language, which is
*an instrument must not write the player's state* pointed at stdout. And `[DATA] Language` plus the
picker are the step's **only** invented behaviour, named as such in `Options.PsLang` and in
`LanguageMenu`'s header, because the original genuinely has no such setting.

All fourteen gates green with it in — the four fidelity ones, and Phase 5's ten. `test_fuzz.py`
left the tree byte-clean.

*Sessions 9-22 were all solver work and are logged in `SOLVER.md`. Their standing engine claim,
re-checked at the end of each: `Engine.cs` differs from a literal transliteration by the word
`partial` — on the class, and (since step 3) on `SoundPlay`, whose body is in `Engine.Sound.cs` —
plus (since step 4) `UndoStep`, `SavePosition` and `RestorePosition`, and (since step 5)
`MouseOperation`, `FindTarget`, `AddKBuff` and `MouseClick`, all of which are transliterations of
`LTANK2.C` and `LTANK.C` that Phase 2 had no caller for. Step 6 added nothing to it: `Language.cs`
is presentation data on `GraphicsFile.cs`'s test and no path inside `Tick()` can reach it.
`Engine.Search.cs` has not changed since the solver's layer 0.*
