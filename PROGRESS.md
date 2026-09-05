# LaserTank → Godot port: progress & handoff

**Purpose:** single source of truth for this port. Read this first after a context clear.
Update the *Status* block and *Session log* at the end of every working session.

---

## Status

**Phase:** 2 — in progress. Phase 1 is **complete**: the oracle builds, runs headless, and replays
the whole recorded corpus.
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

**Next action:** Phase 2, step 3 — `MoveTank`, which is what turns the recorded corpus back into a
gate. Its dependencies are now all present (`CheckLoc`, `UpDateTankPos`, `UpdateUndo`), so it is a
small function on top of finished parts.
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

### Phase 2 — Transliterate the core  ☐
- ☑ **step 0:** `tools/difftrace.py` — compare two traces (or two directories of them), report the
  first diverging tick and the first field that moved. Nothing downstream is checkable without it.
  Self-tested by `tools/test_difftrace.py`; see "The Phase 2 harness".
- ☑ **step 1:** the C# projects exist and build; the load path and the tick frame are
  transliterated, and an empty-keystream run traces identically to the oracle. See below.
- ☑ **step 2:** `CheckLoc` and `MoveObj`, plus `MoveObj`'s closure — `TranslateTunnel`,
  `UpDateTankPos`, `UpdateUndo`, `ResetUndoBuffer`.
- ☐ **step 3..n:** port in dependency order, re-running the trace diff after *each* function:
  `MoveTank` → `CheckLLoc` → `MoveLaser` → `AntiTank` → `IceMoveT`/`IceMoveO` →
  conveyor → tick loop. (`Animate` is already done — it fell out of the idle tick.)

Run both engines with `--field` (and `--bmf` while porting `Animate`) so the diff has the whole
playfield to bite on, not just the hashes.

**Exit criterion:** identical traces on all 187, `--field` included. `BMF`/`AniLevel` differences
are cosmetic (hazard #2) — worth investigating as a tripwire, but not a correctness failure.
`difftrace.py` encodes exactly that distinction: exit 1 for a logic divergence, exit **3** for a
cosmetic-only one, and `--strict` if you want to hold the cosmetic line too.

#### The Phase 2 harness

```
bash oracle/build.sh
python tools/test_difftrace.py                                  # trust the differ first
python tools/replay_all.py --traces build/t-oracle --field --bmf
<csharp> ... --trace build/t-csharp/<same name> --field --bmf
python tools/difftrace.py build/t-oracle build/t-csharp -q      # -q: failures only
```

For that last line to be one command, **the C# CLI must take the oracle's arguments and emit
byte-identical trace lines** — same field order, same spacing, same `%08lx` hashes,
same `#` header and result footer (`oracle/driver.c`, `trace_tick`). Do not invent a nicer
format: the differ is textual on purpose, so any drift shows up as a divergence rather than as a
parser bug. Once the C# CLI answers to the same flags, teach `replay_all.py` an `--engine` option
and both sides come from one script.

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
`Pump()` after the tick, exactly as the oracle's stub message pump does), and — step 2 —
`CheckLoc`, `TranslateTunnel`, `UpDateTankPos`, `UpdateUndo`, `ResetUndoBuffer`, `MoveObj`.

**Everything not yet ported throws `NotPortedException`, not a no-op.** `MoveTank`, `FireLaser`,
`MoveLaser`, `AntiTank`, `IceMoveO`, `IceMoveT`, `ConvMoveTank`, `MouseOperation`. A silent stub
would produce a *plausible* wrong trace, which is the one failure mode this whole approach exists
to prevent; today `replay_all.py --engine build/lasertank-core.exe` stops at the first `MoveTank`,
which is the correct answer.

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

### Phase 3 — Differential fuzzing  ☐
This is what finds the remaining divergences. Random keystreams (weight toward fire/turn), both
engines, diff traces, shrink any divergence to a minimal repro. Run across all 20,914 levels.
Cheap, unlimited, needs no solutions.

### Phase 4 — Solver  ☐
Search over **macro-steps**: one keypress, then tick until quiescent (condition at `LTANK.C:613`).
Deterministic, branching factor 5.
Canonical state = `PF` + `PF2` + tank pose + slide stack. Ice melt and brick destruction are
irreversible → strong pruning. IDA* with distance-to-flag heuristic.

**Be realistic:** level 1's global best is 103 moves + 46 shots = 149 keypresses. Exhaustive search
will not reach that. Expect the short levels to fall and the long ones to time out — likely a few
hundred of 20,914, not all. Every solved level gets cross-run through the oracle *and* compared to
its `.ghs` target.

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
5. `AntiTank()`
6. `IceMoveO()` then `IceMoveT()`
7. `ConvMoving = FALSE`, then conveyor / flag / water check on tank's cell
8. Mouse buffer
9. Repaint tank

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
src/        the C# port                       build.sh -> build/lasertank-core.exe
  LaserTank.Core/  Objects.cs GameState.cs LevelFile.cs Engine.cs  (no Godot here)
  LaserTank.Cli/   Program.cs TraceWriter.cs — the oracle's CLI, the oracle's trace
build/      C# output (gitignored)      LaserTank.slnx  the solution
tools/
  replay_all.py     replay every .lpb; green/red gate (expected outcomes + .ghs targets)
                      --traces DIR [--field] [--bmf] writes one trace per recording
  difftrace.py      compare two traces, or two directories of them: first diverging
                      tick, first field, per-cell playfield diff   <- the Phase 2 gate
  test_difftrace.py self-test for difftrace.py; run it before trusting a verdict
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
