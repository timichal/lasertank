# LaserTank → Godot port: progress & handoff

**Purpose:** single source of truth for this port. Read this first after a context clear.
Update the *Status* block and *Session log* at the end of every working session.

---

## Status

**Phase:** 0 — planning complete, repo reorganized, nothing built yet.
**Next action:** Phase 1, step 1 — get `LTANK2.C` compiling headless under MinGW/clang with a stub
Win32 layer, driven by a keystream. See "Phase 1" below.
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

### Phase 1 — Oracle & tooling  ☐
This is the real gate. If the C reference will not build headless, everything downstream stalls.
Timebox it.

- ☐ Build `LTANK2.C` headless: stub the Win32/GDI layer, **preserving logic-carrying side effects**
  (see hazard #1 — `UpDateLaserBounce` is a *paint* function that mutates control flow).
- ☐ Drive from a keystream file; emit per-tick trace.
- ☐ Parsers for `.lvl` / `.ghs` / `.lpb`.
- ☐ Trace format: per tick — `PF`, `PF2`, `BMF`, `BMF2`, tank x/y/dir/firing, laser x/y/dir,
  `ScoreMove`, `ScoreShot`, slide stack, `AniLevel`/`AniCount`.
- ☐ Replay all 186 corpus `.lpb` files; assert each reaches the flag.
- ☐ For `Tutor-with-Playbacks`, also assert move/shot counts match its bundled `.ghs`.

**Exit criterion:** 186/186 green.

### Phase 2 — Transliterate the core  ☐
Port in dependency order, re-running the trace diff after *each* function:

`CheckLoc` → `MoveObj` → `CheckLLoc` → `MoveLaser` → `AntiTank` → `IceMoveT`/`IceMoveO` →
conveyor → `Animate` → tick loop.

**Exit criterion:** byte-identical traces on all 186.

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
2. **Animation frame is game state.** `Animate()` writes `Game.BMF[][]`; `MoveObj` reads
   `bm = Game.BMF[x][y]` to carry the sprite along (the "Tere6 Bug" fix, `original/src/Bugs.txt` 02-25-02).
   Tutor readme *requires* Animation ON or "the tank will temporarily disappear."
   `AniLevel`/`AniCount` must be simulated.
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
  quirks/     10 tutorial/trick packs, 317 levels, 186 .lpb solutions
  graphics/   .ltg packs      meta/  changelogs & name indexes
oracle/     Phase 1 goes here      tools/  parsers, differ, fuzzer, solver
```

See `README.md` and `data/SOURCES.md` for provenance.

## Test corpus

**Tier 1 — `data/quirks/`, 317 quirk-focused levels, 186 with recorded solutions.**
Upstream deliberately withholds `.lpb` for the main collections; these help-section packs are the
only recorded human solutions in existence.

| Directory | Levels | LPB | Note |
|---|---:|---:|---|
| `tutor-with-playbacks` | 112 | 112 | + bundled `.ghs` — the only pack where recorded counts can be checked against a target |
| `tutor` | 92 | 0 | **the quirk specification** — each hint documents its trick |
| `rotary-mirrors` | 39 | 38 | |
| `tricks` | 26 | 0 | |
| `pono-trick` | 18 | 20 | more LPBs than levels (alternate solutions) |
| `game-objects` | 16 | 16 | one level per object — **best first target for the oracle** |
| `4triang`, `telek-1`, `l40`, `inchworm` | 14 | 0 | |

**Tier 2 — `data/levels/`, 20,914 levels across 13 collections**, every one with a non-zero `.ghs`
entry. No keystreams, but a solvability guarantee and a (moves, shots) target for each. This is the
fuzzing surface for Phase 3.

## Environment notes

- Python is `python` on this box, **not** `python3` (that alias hits the Microsoft Store shim).
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
- Downloaded and inventoried the 10-pack test corpus into `_port/corpus/`.
- Settled architecture: C oracle + C# Godot core + presentation layer.
- Nothing built yet.
- Reorganized the repo (`original/` frozen, `data/` corpus, `oracle/`+`tools/` for the build);
  wrote `.gitignore`, `README.md`, `data/SOURCES.md`.
- Corpus is much larger than first thought: **20,914 levels across 13 collections**, all with
  `.ghs` targets, plus 317 quirk levels and **186** `.lpb` solutions (was 128).
- Verified and removed duplicates: `Updates.zip` was byte-identical to the extracted level files;
  root `Tutor.*` and `.ltg` copies duplicated the packs and `original/src/Setups/Files/`.
- Logged an open question: which of the two `lasertank.exe` builds is the behavioural reference.
- Nothing committed yet — repo is git-inited with no commits.
- Dropped the redundant archive: all 202 quirk-pack zip entries verified byte-identical to
  `data/quirks/` before removal. Kept the one genuinely unique file as
  `data/levels/LaserTank-2016-snapshot.zip` — a third vintage of the flagship collection,
  59 of 2030 levels differing from the current one. Relevant if a `.lpb` ever fails to replay.
