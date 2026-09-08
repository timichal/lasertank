# LaserTank → Godot port: the game

**Purpose:** single source of truth for the *port* — what is built, what is deliberately frozen, the
fidelity gates, the file formats, the quirks, and what to do next. Read this first after a context
clear.

**The solver lives in [`SOLVER.md`](SOLVER.md)** and is a goal in its own right — solving every one
of the 20,914 known-solvable levels. It is *also* the second differential test of this port, and it
is deliberately not on the critical path for correctness: nothing in this file depends on it.
**Since 2026-09-08 the two halves run on two machines** — the game (this file) here, the solver
there.

---

## Where the project is

Everything planned is built. A C reference oracle (`oracle/`), a C# transliteration that traces
byte-identically to it on the whole recorded corpus (`src/LaserTank.Core/`), a differential fuzzer,
and `src/LaserTank.Game/` — a playable Godot game with the game around it, an editor, and ten
languages.

It draws any level from `Game.BMF` with any of the four sprite sheets, runs a fixed 20 Hz tick,
takes the keyboard through the original's own `WM_KEYDOWN` filter and its own accelerator keys,
plays the original's sixteen WAVs off the sound ids the tick itself computes, undoes, saves and
restores a position, picks levels and shows both high-score lists out of `.lvl`/`.hs`/`.ghs`, writes
a `.hs` the 2010 binary would recognise byte for byte, records and plays back `.lpb` at all three
speeds, takes the mouse both as a *move order* through the original's own `MouseOperation` and as
the editor's brush, edits and saves a `.lvl` byte-faithfully, shows its UI in any of the original's
ten translations, and remembers its settings in a `LaserTank.ini` with the original's own section
and key names.

**There are no stubs left in the transliteration.** `MouseOperation` was the last one.

**What is deliberately frozen.** `original/` is a read-only historical artifact.
`src/LaserTank.Core/Engine.cs` differs from a literal transliteration by the word `partial`, twice
(on the class, and on `SoundPlay`, whose body is in `Engine.Sound.cs`). `Engine.Search.cs` has not
changed since the solver's first layer — **if a solver change seems to need an engine change, that
is the signal to stop and re-read.** Core also carries four files that are *not* reachable from
`Tick()` and so cannot move a rule: `GraphicsFile.cs`, `SoundFile.cs`, `Editor.cs`, `Language.cs`.
`NotPortedException` stays in the tree, wired into `fuzz.py`'s signatures, because the argument
behind it has not changed: **an unported function must throw rather than no-op**, or it produces a
plausible wrong trace, which is the one failure mode this whole approach exists to prevent.

**Artifacts live under `build/`, which is gitignored** — they survive a context clear but not a
`git clean`. Everything there is a measurement that can be re-run.

---

## Build, then check nothing rotted

```bash
bash oracle/build.sh && bash src/build.sh      # -> build/lasertank-{core,solve}.exe
python tools/replay_all.py                     # 187 replayed, 181 win, 6 documented non-win
python tools/test_difftrace.py                 # 29 passed
python tools/sweep.py                          # 2,347/2,347 identical
python tools/test_fuzz.py                      # 25 passed  (slow: injects faults and rebuilds)
```

**Those four are the fidelity gates and must be green before anything else is believed.** They are
about the *rules*, and they are the ones a UI change must not move. `test_fuzz.py` patches
`Engine.cs` and restores it in bytes — if it is ever killed between the two,
`git checkout src/LaserTank.Core/Engine.cs`; and never run it while a solver process is alive (see
*Environment notes*).

Ten more gates cover the presentation. They are listed apart because nothing about the rules depends
on them, and `options_check.py`'s pixel arithmetic is the one gate *expected* to be edited when the
look changes on purpose:

```bash
python tools/atlas_check.py      # 2,347 levels' BMF inside the grid + 4 sheets, ~35 s
python tools/tick_check.py       # 208/208 recordings vs the oracle + the 20 Hz rate, ~20 s
python tools/options_check.py    # the INI, the packs, the laser's width, ~50 s
python tools/sound_check.py      # 208 SoundPlay streams + 16 WAVs, ~60 s
python tools/undo_check.py       # undo + save/restore vs the oracle, 400 scripts, ~60 s
python tools/list_check.py       # list rows + .hs bytes vs Python, ~25 s
python tools/roundtrip_check.py  # record -> replay -> oracle, 60 cases x 6 runs, ~150 s
python tools/mouse_check.py      # MouseOperation vs the oracle, 5,000 scripts, ~12 s
python tools/editor_check.py     # the editor + the .lvl it writes, ~25 s
python tools/lang_check.py       # 10 languages back to the 2007 bytes, ~25 s
```

All ten want Godot; `atlas_check`, `sound_check`'s WAV half, `editor_check`'s third half and
`lang_check`'s fourth degrade to a loud SKIP without it, `undo_check` and `mouse_check` need only
the two engines, the rest need Godot outright. Every one that runs the project **rebuilds its C#
first**, because `godot --path` does not — see *Environment notes*. None of them touches
`build/lasertank-solve.exe`, so all are safe beside a live solver. `options_check` opens three brief
windows for its pixel measurements; `--no-window` skips that half.

**If a solve is running on this machine the plain build block does not work**, and the first symptom
is a build failure, not a red gate: `src/build.sh` publishes into `build/`, which a live
`lasertank-solve.exe` holds open. Build into each project's own `bin/` and point the tools there:

```bash
dotnet build src/LaserTank.Cli/LaserTank.Cli.csproj -c Release
export LT_CORE=$PWD/src/LaserTank.Cli/bin/Release/net8.0/lasertank-core.exe
python tools/replay_all.py --engine "$LT_CORE"   # replay_all takes --engine, not $LT_CORE
python tools/sweep.py                            # everything on engines.py reads $LT_CORE
```

---

## Next steps

Nothing is blocked. Roughly in the order they are worth doing.

### 1. i18n: ISO language codes  *(do this first — it is a rename, and it gets worse to do later)*

`data/language/*.json`, the `code` field inside each, and `[DATA] Language` all use the original's
**installer-directory** names, which are not language codes and in two cases are actively
misleading. The mapping, with the codepages that were *measured* rather than guessed:

| now | ISO | language | source codepage |
|---|---|---|---|
| `US` | `en` | English (the base language) | cp1252 |
| `Fr` | `fr` | French | cp1252 |
| `De` | `de` | German | cp1252 |
| `Du` | `nl` | Dutch | cp1252 |
| `Sp` | `es` | Spanish | cp1252 |
| `Pt` | `pt` | Portuguese | cp1252 |
| `Sw` | `sv` | Swedish | cp1252 |
| `Hr` | `hr` | Croatian | cp1250 |
| `Cs` | `zh-Hans` | **Simplified Chinese** — not Czech | gbk |
| `Ct` | `zh-Hant` | **Traditional Chinese** — not Czech | big5 |

`Cs`/`Ct` are Roy Chen's two files: 2,348 and 1,833 high bytes, identical in size and line count,
which is exactly what made them look like a duplicated pair of Czech files until they were decoded.

Touches: the ten file names; the `"code"` field in each; `Language.Base` in
`src/LaserTank.Core/Language.cs`; `LanguageMenu.cs`; `Options.PsLang`'s default;
`tools/convert_language.py` (which maps `original/src/Setups/<dir>/Language/Language.dat` to an
output name — the `Setups/` names stay as they are, `original/` is frozen, so the mapping table
lives here); and `tools/lang_check.py`, which walks the same pairing.

Two decisions to make while doing it: whether `[DATA] Language=US` from an existing INI should still
resolve (a one-line legacy alias table is cheap and the key already round-trips), and that the
picker's display names are the *translators' own* strings — `"English - ( Example )"`,
`"Croatian - ( 100 %)"`, `"Español ( 85% complete !)"`. Cleaning those up is part of item 2, not
part of the rename.

### 2. i18n: actually use the translations  *(after the UI is polished — not before)*

The strings **are** wired, but only sixteen of them: `ID_DEADBOX_DEAD`, `ID_GHIGHLIST_00`,
`ID_GRAPHBOX_00`–`_05`, `ID_HIGHLIST_00`, `ID_LOADLEV_00`, `REC_Title` and `txt009`–`txt014`. That
is 16 of the **155** keys in each file. The rest describe dialogs and a nine-button control panel
this port does not have, which is why they read as unused.

So the job is an audit, and it has to wait for the UI to settle: for each key, either a widget reads
it or it goes. `ButText1`–`ButText9` are the original's button strip; the 96 `ID_*` slots are its
dialogs; `txt001`–`txt045` are its status and message lines. Deleting a key means editing all ten
JSON files **and** `lang_check.py`'s expectation, because the gate ties the JSON back to the 2007
bytes line by line — a dropped key currently fails it, which is the gate working. Whatever the
audit removes, the *converter* should keep reading, so the mapping from the frozen artifact stays
complete and re-runnable.

The port's own legend strip has no key in the original (the original has no such strip) and falls
back to English by having only an English form. That is the model for any new UI string.

### 3. The coordinate grid (A1–P16)

So a position can be *said*: "the tank is on A9". **The original never drew one** — the only
`TextOut` calls in `LTANK2.C` are the score readout (`:1227`, `:1648`) and `ShowTunnelID`'s
`(%1d)` overlay (`:1725`) — so this is an addition, not a port item, and there is nothing to
transliterate. It is worth having anyway because **the level hints already use the notation** and
are unreadable without it.

The convention is not a choice; it is fixed by those hints and was measured against them:

- **columns `A`–`P` = `x` 0–15, left to right.**
- **rows `1`–`16` = `y` 0–15, top to bottom.**

Evidence: `data/quirks/tutor/Tutor.LVL` level 80's hint names "tunnel L7", and `PF[11][6]` is
tunnel id 0; `tutor-with-playbacks` level 93's hint names static mirrors at "K10" and "N10", and
`PF[10][9]`/`PF[13][9]` are the two mirrors. Both readings only work with A→x=0 and row 1→y=0.

Purely a `BoardView` draw pass — no engine involvement, no gate to move.

### 4. A collection picker — command 108, "Open Data File"

The port can only be pointed at a `.lvl` collection from the command line (`--levels`) or by what
`[DATA] RLLFilename` remembered. In-game, `L` picks a *level* inside the current collection and
there is no way to change collections at all, so 22 of the 23 shipped ones are unreachable without a
restart.

The original's is command **108**, accelerator plain `O` (`lt32l_us.inc:125`, menu label
`"&Open Data File...\tO"`). Its body (`LTANK.C:924`) is the whole specification, and three of its
five lines are the parts that are easy to forget:

```c
OFN.Flags = OFN_HIDEREADONLY | OFN_FILEMUSTEXIST;
if (GetOpenFileName(&OFN)) {
    AssignHSFile();                 // the .hs/.ghs must follow the collection
    CurLevel = 0;
    Backspace[BS_SP] = 0;           // clear the ten-level history
    EnableMenuItem(MMenu, 118, MF_GRAYED);
    LoadNextLevel(TRUE, FALSE);
}
else { GameOn(x); strcpy(FileName, temps); }   // restore the old name on cancel
```

`AssignHSFile` is the load-bearing one: the high-score files are named after the collection, and a
picker that skipped it would post scores into the previous collection's `.hs`.

`GetOpenFileName` is also what blocks the editor's Load Level (602) and Save As (606) — this port
has no file-dialog equivalent yet, and that one gap accounts for three commands. A **list of the
collections under `data/levels/` and `data/quirks/`** is probably the better answer here than a
native file dialog: it is the same shape as `LevelList` (which is already one class in three
modes), it is reviewable with `--panel`, and the content is in the repo. Note the case trap:
`.lvl` and `.LVL` both occur, and the four uppercase packs are the four biggest — match on
`suffix.lower()`.

### 5. Drop the Animation option

`[OPT] Animation` / command 104 / the `A` key. It gates one line — `if (Ani_On) AniCount++`
(`LTANK.C:589`) — which stops `Animate()` cycling `BMF`, so water and conveyors stop shimmering and
nothing else changes. It is not worth a key, and there is no reason to ever turn it off.

**Remove the UI, not the field.** `Engine.Ani_On` is transliterated (`LTANK2.C:28`, default `TRUE`)
and so is the line that reads it; deleting either would be de-transliterating for a cosmetic
setting. Drop `Options.AnimationOn`, the `A` binding and the HUD readout, and leave `Ani_On = true`
where it is — which is what every headless gate already runs with (`PlayMode` builds its Sessions
with no `Options` at all).

Once nothing reads the key, `Ini`'s write path preserves it as a foreign key, which is exactly
right: the 2010 binary still keeps it in the same file.

### 6. The one real bug behind the level-39 report

Reported: flagship level 39, tank on C1, hold Right — the tank reaches I1 **and dies, but then
moves to J1**; Undo puts it back on I1, alive and playable.

**Measured: the visible sequence is the original's, not ours.** Route `llllllluurrrrrrrrrr` (left to
C16, up onto the C conveyor, which carries the tank to C1, then right) traces **identically** in the
oracle and the core — 38 ticks, `DEAD`, 14 moves, `difftrace.py` exit 0. At t=32 an anti-tank fires
up column I; at t=36 the shot is at `(8,1)`; at t=37 `MoveLaser` (tick step 2) kills the tank at
`(8,0)` and *posts* `WM_Dead`, then step 4 consumes the pending key and moves the tank to `(9,0)`,
and the death is pumped after the tick. That is **quirk #8** — `PostMessage(WM_Dead)`, deliberately
changed in 4.0.6 — and the extra move is what it means. Undoing back onto I1 alive is the DeadBox's
"Undo Last Move" (command 110 plus `GameOn(TRUE)`, `LTANK.C:727`), the only path in the game that
resumes a death; being able to *continue* is **hazard #13**, because `UndoStep` restores `Game` and
the laser is not in `Game`, so the shot that killed you is simply gone.

**The genuine deviation is smaller and is next to it.** `Session.Key` filters exactly as
`WM_KEYDOWN` does (VK 32..40, auto-repeat dropped while a key is pending) but **does not check
whether the game is running**, so a keypress after death still lands in `RecBuffer`. In the
original it cannot: dying opens the DeadBox, a modal `DialogBox`, and while it is up every keystroke
belongs to the dialog and never reaches `AddKBuff`. So in the port `UndoDead`'s `GameOn(TRUE)`
resumes into a buffer with a key already in it and the tank takes a move the player never aimed —
and because `WM_SaveRec` writes `RecBuffer[0..RecP)`, the recording keeps it.

The fix is one condition, and it belongs where the dialog's exclusivity belongs: gate `Session.Key`
on the game being on, the same way the driver already carries `Engine.CanRestore` as "the menu's own
guard" (see *Rules learned the hard way*). The bound is one key, because echoes are dropped while
`RB_TOS > RecP` and `RecP` stops advancing once the timer is off.

**No gate can currently express this**, which is why it took a human: `--script`'s tokens press a
key only when the buffer has drained, on purpose. Checking it needs either a token that presses
regardless of pending, or a `PlayMode` case that presses while `Now == Dead`.

### 7. Hint on demand — command 301

The hint is currently drawn under the board **always**, which spoils every level that has one. The
original has a Hint dialog (`ButText7`, command 301) behind a button. Cheap, and it is the one
piece of current UI that is actively wrong rather than merely plain.

### 8. The rest of the original that is still missing

Everything here was named as left out at the time rather than forgotten.

**Blocked on a file dialog:** Load Level in the editor (602), Save As (606) — plus 108 above. See
item 4.

**Blocked on a modal prompt:** the "save changes?" prompt on leaving the editor (`Modified` is
tracked and shown, there is just no message box), the `RecordBox`/`HSBox` name prompts (both INI
keys are read and written; there is nowhere to type), the Difficulty dialog (225), the DeadBox
itself (a HUD line here — see item 6), and the `LoadTID` tunnel dialog *as* a dialog (the id is a
mode here, cycled with `T`, because a modal prompt per painted cell is worse than a mode).

**Additive, nothing blocking:** the Search sub-dialog (`SearchBox`, `LTANK_D.C:394` — name or
author substring, difficulty mask, skip-completed) and `TransListKey`'s type-ahead, neither of which
changes a row; `Backspace[]`'s ten-level history (118); Resume Recording (125); Print (126); "View
Opening Screen" (`ID_GRAPHBOX_08` — toggles `QHELP` and paints `Opening.bmp` over the board, its own
piece of drawing); "Change Directory" (`ID_GRAPHBOX_09`, a shell folder browser — the key is
persisted and `--gfx-dir` sets it); and `LoadImageFile`'s per-language `Control.bmp` / `Opening.bmp`
/ `LaserTank.hlp`.

**Wants a `LoadNextLevel` port rather than a menu:** `[OPT] SkipComLev` and `[DATA] Diff_Setting`.
Both are read into `Options` as comments only.

**Not coming:** the `.ln` files under `Setups/Language/` (a 4.0-era format superseded by the
`.dat`s and not read by the 2007 build).

### 9. A menu bar

Step 6 converted all 73 menu items of both trees with their command ids and accelerator labels, so
`Language.MainMenu` / `Language.EditorMenu` is a ready-made model. The port is still key-driven and
has no menu widget. This is the cheapest remaining piece of the original that is *fully specified
data* rather than design work.

### 10. The UI redesign this whole approach was a prelude to

The line was drawn out loud and holds: **the mechanics of the puzzles must be exactly the same —
every level solvable in exactly the way it was — and the UI need not be.** The port was finished
faithfully first because that is the cheap way to be sure nothing mechanical moved while the game
was built around it. Every UI detail in this file is therefore **written down rather than locked
down**: when one is deliberately changed, the note explaining what the original did stays (it is why
the change is a choice rather than a regression). What must not move: `replay_all.py`, `sweep.py`,
`test_difftrace.py`, `tick_check.py`'s 208/208. What is expected to be edited on purpose:
`options_check.py`'s pixel arithmetic.

### 11. More fuzzing, indefinitely

`fuzz.py` can keep running on new seeds and on the **12 collections its first campaign never
touched**. `undo_check.py`, `mouse_check.py` and `editor_check.py` are three more campaigns of the
same kind against the same oracle. All four are worth leaving running.

### 12. The solver

The larger unfinished half of the project and a goal in its own right: 11.3% of a 4,185-level sample
against a goal of all 20,914. It runs on the other machine now, so treat that number as a
last-known value. See `SOLVER.md`.

---

## Goal & hard constraints

Modernize LaserTank 4.1.2 (public domain, Jim Kindley / Yves Maingoy) into Godot.

1. **Game logic must be preserved exactly, quirks included.** Many "bugs" are load-bearing — whole
   level packs exist *only* to exploit them. Upstream says so explicitly: *"Some of the tricks are
   bugs that have been intentionally left in the software because they make the game more
   interesting."* (`data/quirks/tutor/Tutor-ReadMe.txt`)
2. **Community file formats stay readable and writable**: `.lvl`, `.lpb`, `.hs`, `.ghs`, `.ltg`.
   25 years of community content depends on them.
3. Equivalence with the original must be *demonstrated*, not asserted.

---

## The key insight

**The game is a deterministic 20 Hz tick machine driven by a keystroke stream.**

Live keypresses do not act directly. `WM_KEYDOWN` (`LTANK.C:570`) appends the raw VK code to
`RecBuffer` via `AddKBuff`; the timer tick consumes one key per tick when the world is quiescent
(`LTANK.C:613`). Playback and live play run *the same code path*. No RNG, no floats, no frame-rate
dependence.

So **validation does not require solutions**: feed both engines the same keystream, dump a per-tick
state trace, diff. That is the primary correctness strategy, and it is why the solver is never a
prerequisite for the port.

### Tick order (`LTANK.C:579`) — this *is* the spec

1. `Animate()` every `ani_delay`(=4) ticks
2. `MoveLaser()` if `Game.Tank.Firing`
3. Playback pacing / `PBHold`
4. Consume **one** key from `RecBuffer` if
   `!(Firing || ConvMoving || SlideO.s || SlideT.s || PBHold)` — **and, inside that same `if`,
   `AntiTank()`.** Anti-tanks act only on ticks where a key was consumed; on a tick with no key
   they do not play. (This matters for the solver: it is why a "wait" is not free.)
5. `IceMoveO()` then `IceMoveT()`
6. `ConvMoving = FALSE`, then conveyor / flag / water check on the tank's cell
7. Mouse buffer
8. Repaint tank

### The input path, which is where the surprises are (`LTANK.C:570`)

- **The filter is `wparam >= 32 && wparam <= 40`, not the five game keys.** Space is 32 and the
  arrows are 37-40, but **33-36 — PageUp, PageDown, End, Home — are inside the range too**, and
  `AddKBuff` filters nothing. The tick's switch (`:616`) has no `default` and `Game.RecP++` runs
  regardless, so those four record a legal one-tick **wait** that still gives the anti-tanks their
  turn. They are reachable from a real keyboard and always were, but **no human ever used one**: all
  54,162 bytes of all 187 `.lpb` are the five game keys, zero exceptions. The port keeps them
  reachable, which is why every dev binding in the game is outside 32..40.
- **`if ((RB_TOS > Game.RecP) && (lparam & 0x40000000)) return(0);`** — auto-repeat is dropped *only
  while a key is still pending*. That one line stops a held arrow flooding the buffer while the tank
  is busy and lets it keep the tank moving once the buffer drains. It is also the real answer to "a
  144 Hz display must not consume 144 keys a second": the frame rate never enters into it.
  Godot's `InputEventKey.Echo` is the same bit.
- **`WM_SaveRec` writes `Game.RecP`, not `RB_TOS`** (`LTANK.C:709`) — the keys *consumed*, not the
  keys pressed. That is what makes a recording saved the instant a level is won end exactly at the
  winning move.
- **`GameOn()` is `SetTimer`/`KillTimer` (`LTANK2.C:881`), so a finished game receives no ticks at
  all.** `Session.Step()` returns false rather than ticking when `Game_On` is clear. Calling
  `Tick()` on a won game would not crash — it would quietly keep playing.

The "wait" the tutor hints describe (level 4 *"Move up, wait"*, level 14 *"Wait 11 seconds"*) is a
different thing: it is *free* time while the world is non-quiescent (riding a conveyor, sliding on
ice), during which no key is consumed and no byte is needed. Neither engine's `--keys` parser can
express a recorded wait anyway — both accept only `u d l r f`. If that changes, both change together
and the corpus gets re-run.

---

## Architecture: three engines, one truth

| # | Engine | Role |
|---|---|---|
| 1 | **C reference oracle** | Original `LTANK2.C`, unmodified logic + stub Win32 layer, headless, emits traces. **Ground truth forever. Never refactor.** |
| 2 | **C# core** | Literal transliteration. Headless, pure, no `Node`/rendering/signals. Steps one tick. |
| 3 | **Presentation** | Godot nodes reading core state, interpolating between ticks. |

**Core language: C#** (Godot .NET). Fast enough for a solver doing millions of state expansions,
readable, one build across desktop targets. Rejected: GDScript (10–50× too slow for search); C++
GDExtension (max fidelity but keeps us shipping 25-year-old code and complicates web export).

**Transliterate literally, including the ugly parts.** Idiomatic rewriting is how quirks die.

**The presentation layer must not become a second implementation of the rules.** A Godot node reads
`Game.PF` and `Game.BMF` and draws them; it never decides anything. Core may still gain
*presentation data* — the line is whether a rule could move, not which directory a file sits in.

Decisions worth not relitigating:

- **`byte[16,16]`, not `sbyte`.** `PF`/`PF2`/`BMF`/`BMF2` are `char[16][16]` in C and gcc's `char`
  is signed, but `BuildBMField`'s 2003 sanitisation forces every cell to `<= 0x19` or to a tunnel,
  so nothing above `0x7F` survives a load. The one place the signedness is visible is
  `GetOBM(char)`'s `ob > -1` guard, which `Obj.GetOBM` keeps verbatim.
- **Original names, not C# conventions.** `Game`, `ScoreMove`, `SlideO`, `wasIce`, `IceMoveO`.
  Renaming is how a quirk stops looking like a quirk.
- **`net8.0` for both projects.** Lowest TFM Godot 4.x accepts; the CLI sets
  `RollForward=LatestMajor` because only the .NET 10 runtime is installed.
- **The undo buffer is carried even where nothing reads it back.** `UndoP` is load-bearing on its
  own — `MoveObj`'s tunnel path decrements it (quirk #7) — so its growth (`UndoBufSize` in steps of
  200) and roll-over at `UndoMax` have to be exact, and the cheapest way to be sure was to keep the
  `TGAMEREC` snapshots they index into. The two `GlobalReAlloc == NULL` branches in
  `UpdateUndo`/`ResetUndoBuffer` are *not* carried: the oracle's stub is plain `realloc`
  (`oracle/win32_stub.c:74`), so they are unreachable on both sides. They are the only pieces of
  either function left out.
- **One deliberate deviation in the driver: `Session.Load` builds a fresh `Engine` per level**, and
  `Restart` reloads rather than restoring `CurRecData.PF` in place the way command 105 does.
  `LoadLevel` leaves `wasIce`, `WaitToTrans`, `ConvMoving` and `BlackHole` standing (quirk #12),
  faithfully, because the original never reloaded a level into a fresh process either — but every
  keystream this game records is going to be replayed somewhere that *did* start clean (the oracle,
  the 2010 binary, `verify_solutions.py`), and a clean start is the only configuration the whole
  equivalence was ever established for. The cost is documented: command 105 never calls
  `ResetUndoBuffer` — it restores the playfield in place and pushes one more snapshot first
  (*"Without this we loose the last move"*) — so in the 2010 binary `R` then `U` walks back into the
  attempt you just abandoned. Here the restart is clean and the undo history goes with it.

---

## The differential harnesses, and how to debug with them

### Traces

`replay_all.py --engine` is what makes both sides one script, and it works because **the C# CLI
takes the oracle's arguments and emits byte-identical trace lines** — same field order, same
spacing, same `%08lx` hashes, same `#` header and result footer (`oracle/driver.c`, `trace_tick`).
Do not invent a nicer format: the differ is textual on purpose, so any drift shows up as a
divergence rather than as a parser bug. Trace line 1 differs by design (`# lasertank core trace` vs
`# lasertank oracle trace`); line 2 and every tick line are byte-identical, and `difftrace.py` reads
level/name/author/keys off line 2 to check both sides ran the same input.

```bash
python tools/test_difftrace.py                                  # trust the differ first
python tools/replay_all.py --traces build/t-oracle --field --bmf
python tools/replay_all.py --traces build/t-csharp --field --bmf \
       --engine build/lasertank-core.exe
python tools/difftrace.py build/t-oracle build/t-csharp -q      # -q: failures only
```

**Run both engines with `--field`** (and `--bmf` while touching `Animate`) so the diff has the whole
playfield to bite on, not just the hashes. Add `--pack game-objects` for a 16-recording fast loop
(one level per object); run the whole 187 before believing anything. The full pair takes a few
minutes and about 327 MB per side.

`difftrace.py` exits **1** for a logic divergence, **3** for a cosmetic-only one (`BMF`/`AniLevel`
— hazard #2), and `--strict` holds the cosmetic line too. What it gives you:

```
=== first divergence: tick 90 (line 91) ===
first field:  T.dir 1 -> 4                 [tank]
also:         T.firing 1 -> 0, L.y 11 -> 12, S.shots 23 -> 22
    PF[x=4,y=9]       0d mirror dr      -> 19 thin ice
```

**The field name *is* the localisation** — `S.moves` sends you to `ScoreMove`, `SlO.dy` to
`IceMoveO`, `P` to the key-consume test at `LTANK.C:613`. The summary counts how many ticks each
field diverges on, which separates "one wrong cell" from "everything after tick 90".

**The cheapest first milestone for any future port:** run with an empty keystream (`--keys ""`).
Both engines trace exactly two lines, `t=0` and `t=1`. Matching those two means the `.lvl` parser,
the `TGAMEREC` layout, `BuildBMField`, `PutLevel`, `Animate`, the fnv1a hashes and the trace
formatting are already right.

**When a trace stops early:** the CLI catches `NotPortedException`, closes the trace after the last
*complete* tick, writes `# result=NOTPORTED`, names the function and tick on stderr and exits **4**.
`difftrace.py` then says `DIVERGE length mismatch after N ticks`, which is the useful reading.
`NOTPORTED` is not a result.

### The input languages, and why they exist

Every one of these was added because a feature looked untestable — and the lesson generalised:
**when the oracle has no way to be asked, the question is what input language is missing, not
whether the C can answer.**

| flag | tokens | reaches |
|---|---|---|
| `--keys` | `u d l r f` | the tick, from a keystream |
| `--lpb FILE` | — | a recording |
| `--sound` | *(trace field `SF=`)* | `SoundPlay`'s per-tick id stream, in call order, `-` for silence |
| `--script` | `u d l r f` (press, only when the buffer has drained — the original's own pending rule), `.` (idle one tick), `z` (110), `Z` (the DeadBox's Undo Last Move: 110 + `GameOn(TRUE)`), `c` (111), `v` (112), `mXY`/`nXY` (a left/right click on cell XY, two hex digits) | `UndoStep`, Save/Restore Position, `MouseOperation` — all `WM_COMMAND` or `MBuffer` cases no keystream can reach |
| `--edit` | one token per editor step (`<NN` select, `lXY`/`rXY` paint, shifts, clear…) | `ChangeGO` and the editor's `LTANK.C` cases |

```bash
oracle/build/oracle.exe  --levels data/levels/LaserTank.lvl --level 7 \
    --script "uufz.zc.v" --trace a.tr --field
build/lasertank-core.exe --levels data/levels/LaserTank.lvl --level 7 \
    --script "uufz.zc.v" --trace b.tr --field
python tools/difftrace.py a.tr b.tr
```

`oracle/driver.c` implements each of these against the **real** 2002 C: `oracle/build.sh` compiles
`LTANK2.C` verbatim, so `UndoStep`, `MouseOperation`, `FindTarget` and `ChangeGO` on that side were
written in 2002 and this project has never read them into anything.

**Where a rule is implemented three times, that is on purpose.** For `LTANK.C` window-proc cases
(the undo commands, the editor's Clear/Shift, the mouse's `MBuffer` push) there is `oracle/driver.c`,
`LaserTank.Cli`'s own driver, and the *game's* path — `PlayMode` / `EditMode`. The first two prove
the arithmetic; the third proves that what the UI invokes **is** that arithmetic.

### Fuzzing

```bash
python tools/test_fuzz.py                            # trust the fuzzer      25 cases, ~90 s
python tools/sweep.py                                # 2,347 levels, empty keystream, ~50 s
python tools/fuzz.py --each 3 --seed 1               # 6,090 cases over the flagship, ~140 s
```

**Run `test_fuzz.py` before believing any green fuzz run.** A fuzzer that has never gone red is
untested, and "20,626 cases, no divergence" is then a claim about the fuzzer rather than about the
port. It patches `Engine.cs` with two faults a transliteration would plausibly make, rebuilds, and
requires that each is found, shrunk and independently reproducible — then restores `Engine.cs` in
bytes and requires green again.

**The shrinker is the deliverable, not an extra.** A divergence at key 300 of 400 on level 1,712 is
not a bug report; `level 1712, keys "rrfud"` is. `fuzz.reduce_keys` is three passes: shortest
diverging prefix by binary search, delta debugging (ddmin) over what is left, and then *measuring*
1-minimality rather than claiming it — delete each remaining key and check the divergence goes away.
All three hold the divergence **signature** fixed (the name of the first field that moved), so what
comes out is a reduction of the bug you started with. `--shrink-any` relaxes that. Findings are
deduped by signature, so a systematic bug reports once rather than 9,000 times; each gets a
directory with the minimal keystream, both traces re-run with `--field --bmf`, the full report, the
level as ASCII, the exact commands, and `findings.json`.

**Fuzz without `--field`/`--bmf`** — they add 512 hex bytes per tick each, and the default trace
already carries `H=fnv1a(PF),fnv1a(PF2)`, so a playfield divergence still shows up as a hash. The
minimal repro is then re-run *with* both, and the tool warns if the wider trace does not reproduce
the same signature.

**Consumed keys, not generated keys, is the honest coverage number**: random play drowns or shoots
the tank early, so the two differ by nearly half. The first campaign was six keystream shapes
(varying length and the fire/turn weights, because one shape is one bias) over the flagship and all
ten quirk packs: **20,626 cases, 3,751,638 tick-lines, 0 divergences**, 20–100% of keys consumed
depending on shape.

**Random keystreams are shallow.** Over half of all runs end `DEAD`, and the flagship's own level 1
needs 149 keypresses to win. That is the argument for the solver being the *other* kind of coverage:
a solved level is a long, legal, non-random path through the engine.

---

## Rules learned the hard way

These are the ones that cost something. Each is a rule, not a story.

- **Do not "fix" anything on the way past.** If a bug has a *defined* effect, keep it — hazards #9,
  #11, #14, #16–#18 are all real bugs in the original that must survive. The exception argued for so
  far is a bug whose only effect is **undefined**: `GFXInit`'s `if (!(Mh || Gh)) GraphM = 0;` (`||`
  where it means `&&`) hands a NULL bitmap to `SelectObject`, and a decoder that throws cannot
  produce half a sheet. Same for rotating a tunnel in the editor: the read has no defined value, so
  both engines skip it.
- **When the original has a table, read the table.** `LaserOffset` is initialised to 10 at
  `LTANK2.C:46` and then *reassigned per board size* by `SetGameSize` — 10/13/17. Taking the
  initialiser for the rule and scaling it drew the laser three times too wide, and nothing but a
  pixel measurement said so. Same species as re-deriving `BMF` from `PF` (hazard #2), and as
  inventing key bindings instead of reading `ACC1`/`ACC2` out of `lt32l_us.inc`.
- **An exit criterion that only says "nothing changed" is not one.** Sound was planned as "adding
  audio must change no trace, so the gates staying green *is* the proof" — which would have shipped
  a game whose tank drove around in silence, because `SoundPlay(S_Move)` had been dropped and no
  trace carried sound. Make the thing being added *observable*, then diff it against the C.
- **When the oracle can answer, ask it before writing down what "correct" means.** "A recorded game
  round-trips, all three agree" silently assumed a recording replays to the position it was saved
  from, and the oracle disproved that in one command (hazard #13). A criterion written before the
  measurement is a hypothesis.
- **A guard the original gets from Windows still has to be written down somewhere.** A grayed menu
  item is the only reason `RestorePosition`'s three guardless lines are safe (hazard #14). The rule:
  transliterate the function literally, and put the menu's own condition in the **driver**, named
  after the `EnableMenuItem` call it stands for — then every driver can apply it and be diffed
  against the others. `Engine.CanRestore` is that. **Item 6 of *Next steps* is the same shape**: the
  DeadBox's modality is a guard the port has not written down yet.
- **An instrument must not write the player's state**, and that covers three files: the INI, the
  `.hs`, and anything under `out/recordings/`. `Session` writes a `.hs` only when it was given
  `Options` whose INI is writable; a `--shot`/`--play`/`--check-*`/`--tick-rate` run left to find
  `LaserTank.ini` on its own gets it **read-only**. The test is whether a gate could be run eight
  times in parallel and leave the tree as it found it. `tick_check.py` replaying 208 winning
  recordings wrote `.hs` files into six collections of `data/` before this was noticed, and
  `.gitignore` is why nothing said so.
- **A gate that can write into `data/` is a bug in the gate, and so is a *feature* that can.** The
  editor is the first thing a *player* drives that writes a file the corpus is made of. Command 603
  saves in place; here, saving a level that came out of `data/` writes a working copy under
  `out/levels/` (seeded from the collection, so the file stays a whole collection) and says so on
  screen. A collection opened from anywhere else saves in place.
- **A gate's own output must not move with a player's setting.** The `highscore` line `--play`
  prints is parsed by `list_check.py`, so localising its labels would make a fidelity tool's output
  depend on `[DATA] Language`. `HighScores.Describe` has two forms for that reason: frozen English
  for the instrument, the loaded language for the screen.
- **Reading a format is not the same as being able to write it.** A `.lvl` writer built on decoded
  strings would have passed every read-back test in the project and still corrupted 20,914 levels'
  worth of trailing bytes. When constraint 2 says *writable*, the check is a byte-for-byte identity
  round trip on files nobody edited.
- **Measure pixels, do not look at them.** Both rendering bugs found so far were invisible in a
  screenshot until someone knew the number to expect. Anything geometric — bar widths, outlines,
  cell rects — gets a reader-and-compare, and `options_check.py`'s laser check takes the coordinates
  from the *engine* and the pixels from the *renderer* so the two cannot agree by construction.
  (The second bug: **Godot's `DrawRect(filled: false, width: 1)` strokes *centred* on the edge**, so
  a 1 px outline rounds outside on the top and left. GDI's `Rectangle()` puts its pen strictly
  inside. `Gfx.Fill()` now paints a border as the difference of two fills.)
- **A table that agrees with itself is not a check.** The sprite-sheet decoder, the sixteen WAVs,
  the sound-id names, the size and `LaserOffset` tables and the `.hs` writer are each implemented
  twice — once in C# and once in the Python gate — precisely so the comparison means something.
- **The 20 Hz tick is not a rendering rate.** The tick is `_PhysicsProcess` with
  `physics_ticks_per_second = 20` in `project.godot`, `_Process` only calls `QueueRedraw`, and
  `BoardView` refuses to start if that setting and `GameDelay` disagree — the logic rate is the one
  number in this project that must not drift.
- **Not everything the original does is a rule worth porting, and the test is whether the input
  population is open.** `LANGUAGE.C` was not transliterated because the files it reads are a closed
  set of ten, frozen in 2007, and nothing in the rules reads a UI string — so the format is a
  one-time import rather than a behaviour. That does **not** generalise to `.lvl`, `.lpb`, `.hs` or
  the graphics packs: those are all still written, by this port among others, which is why every one
  of them is read and written by a transliteration. When the temptation to convert rather than
  transliterate comes up again, the question is who else will ever write the file.

---

## Rendering and audio: the decoded facts

**Read `Game.BMF`, never re-derive it from `PF`.** `BuildBMField` maintains the *bitmap number* per
cell, `Animate()` cycles it, and `Obj.GetOBM()` is the object→bitmap table (`LTANK2.C:77`). It is
not simply `GetOBM(PF)`: a tunnel is 55, the tank's own cell is 1 *and its `PF` is zeroed*, and
`Animate` then cycles animated objects. Every one of those is a place a re-derivation drifts.

**The atlas geometry.** The sheet is **always 320×192 — a 10×6 grid of 32×32 sprites** — and `BMA[]`
is filled row-major from **i = 1**, ten per row (`GFXInit`, `LTANK2.C:782`). So sprite index `i` is
atlas cell `((i-1) % 10, (i-1) / 10)`. `MaxBitMaps` is 58 (`LTANK.H:92`) and the highest index the
object table yields is 57, so the last row is partly unused.

**The mask is not simply an alpha channel.** The original blits mask-`SRCAND` then bitmap-`SRCPAINT`
only for the sprites `BMSTA[]` (`LTANK2.C:80`) marks transparent; everything else is a plain
`SRCCOPY` that never looks at the mask. In the internal sheet the mask cell for an opaque sprite is
*solid white*, so applying it to everything erases the board. **Except the tunnel, which is masked
anyway:** `UpDateSprite`'s tunnel branch (`LTANK2.C:498`) paints `ColorList[id]` and then masks
sprite 55 over it **regardless of `BMSTA[55]` being 0** — that is the only reason a tunnel's colour
is visible. `Gfx.Masked()` is the one place that rule lives. And **`BMSTA` is declared
`[MaxBitMaps+1]` = 59 wide with 58 initialisers**, so C zero-fills the last entry; both ports carry
the trailing 0, because bitmap 58 is a legal index.

**Pixel formats.** The `.ltg` sheets are `BI_RGB` 24 bpp (8 bpp for `Warcraft_II`) with a 1 bpp
mask; the *internal* pair — `original/src/Game.BMP` and `Mask.BMP`, the resources the 2007 build
carries — are **RLE8 and RLE4**. Both readers handle 1/4/8/24 bpp and both RLE modes.

**Zoom is 24/32/40 px** (`SetGameSize`, `LTANK2.C:1729`), and the original implements it by
`StretchBlt`-ing the whole sheet at load. **Do not copy that** — keep the atlas at native 32×32 and
scale at draw time: same picture without the resample, and no logic reads the sprite size. Hazard
#11 lives in `SetGameSize` and must stay broken; `SetUpGraphicsBox`'s copy of that line *does* have
its parens, which is why picking a pack in the menu really does reload the sheet.

**The laser is the one genuinely new piece of drawing, and it is *paint only*.** `UpDateLaser`
(`LTANK2.C:549`) is a bar down the middle of a cell, `SpBm_Width - 2 * LaserOffset` wide — so
**4, 6 and 6 px** for the three sizes — green when `laser.Good` and red otherwise (which is
`FireLaser`'s `laser.Good = (sf == 2)`, already load-bearing for logic). `UpDateLaserBounce`
(`:565`) paints *two half-bars*, the half the shot came in through and the half it leaves by — but
that function is **hazard #1**, and **the core already calls it inside the tick**, so the renderer
must not call, skip or reimplement it. The one fact the paint call has and the state does not is the
laser's incoming direction, so `Session` recovers it by watching `laser.Dir` across a tick.

**The bounce glyph needs a distance test.** When the laser bounces off a mirror that is itself
sliding on ice, `UpDateLaserBounce` sets `LaserBounceOnIce` and `MoveLaser` `goto`s back for a
*second* step in the same tick — so the bend happened one cell back and the laser now sits in a cell
it went straight through. Comparing `laser.Dir` across the tick says "bounced", and the half-bars
would be painted **in the wrong cell**. Two cells of travel in one tick is exactly that case, so the
bounce glyph is suppressed and the ordinary straight bar drawn. `Tutor-with-Playbacks` 93 (tick 527)
and 94 (tick 206) are the only recordings in the corpus that reach it.

**Interpolation is ours; the original had none** — it snapped, one cell per 50 ms. The tank is lerped
between the previous cell and the current one, guarded three ways: only while the timer is running,
only between *adjacent* cells so a tunnel does not slide the tank across the board, and rounded to
whole pixels because the sheet is nearest-filtered. `I` turns it off, which is the honest A/B against
the 2010 binary, and `--shot` forces it off so a screenshot names a tick rather than a moment
between two.

**Audio is monophonic, and that is a fact about the original worth keeping.** `PlaySound(p, 0, 5)`
is `SND_MEMORY | SND_ASYNC` with no `SND_NOSTOP`, so a second call *stops the first*: the tank
moving cuts off the laser bounce. One `AudioStreamPlayer`, `Play()` every time, reproduces that;
sixteen players would be a nicer game that does not sound like this one. A tick that asks for
several sounds therefore only ever plays the last — `Session` hands the renderer the whole per-tick
list in call order and `Sfx` plays the last of it, so the audible-behaviour choice and the fidelity
check stay separate things: the **list** is what the gate diffs.

**Muting lives in the presentation, not the engine.** `lt_sfx.c:29` returns early on `!Sound_On`, so
a muted original makes no `PlaySound` call at all — but it makes the same decisions, and decisions
are what the port records. Putting `Sound_On` in the engine would make a muted game trace
differently from a loud one.

**The WAVs.** All sixteen are PCM mono 8-bit, 11025 Hz except `MOVE` and `PUSH3` at 8000.
`SoundFile.cs` walks every RIFF chunk (these files carry `fact`, `LIST` and `DISP`, and several put
`LIST` *after* `data`) and converts to signed 8-bit, because **8-bit WAV samples are unsigned and
every raw-PCM consumer wants them signed**. That xor with `0x80` silently produces a DC-offset click
rather than an error, so it is cross-checked. Nothing was imported into `res://`:
`original/src/Sounds/` is frozen and read from where it lies, exactly as the internal sheet is.

**`SoundPlay`'s cost to the core is one word.** `partial void SoundPlay(int sn);` with the body
`SoundLog?.Add(sn)` in `Engine.Sound.cs`. `SoundLog` is null unless a driver opts in, so the solver
pays one null test per call and allocates nothing.

**The one thing no gate covers** is that `AudioStreamPlayer` actually makes a noise: the ids, the
decode and the option are all checked headless, and the last hop needs a human with speakers. It was
walked once by hand. If the audio path is ever refactored, that hop has to be walked again.

---

## Options: `LaserTank.ini`

`Options.cs` is the file, in the file and under the key names the original persists them under
(`LTANK.H:113-128`). `Ini` is a stand-in for the three profile-string calls rather than an INI
library:

- **first match wins**; sections and keys match case-insensitively;
- integers follow **`atoi`** — a present-but-junk value reads as 0, and only a *missing* key gives
  the default;
- **a write preserves every other line in the file.** Load-bearing, not politeness: the 2010 binary
  keeps a dozen keys in this same file, and a rewrite that dropped them would silently reset the
  player's other settings.

The defaults are the original's, and one of them is easy to get wrong: `Size` defaults to **1**, the
24 px board (`LTANK.C:1567`). `Graphics_Mode` defaults to 0.

**The Yes/No test is the original's, case and all.** `if (strcmp(temps, psYes)) Sound_On = FALSE;`
(`LTANK.C:411`) means **exactly `Yes` or the sound is off** — a hand-edited `Sound=yes` really does
mute the 2010 binary. Kept for `Sound`, `Animation` and `RLL`. `Auto_Record` is the mirror image
(`strcmp(...) == 0`, default **No**), so a missing key means off rather than on. Step 2 read `RLL`
with the looser sense and its gate now pins that; the two idioms disagreeing is recorded here rather
than harmonised by guess.

Keys read: `[SCREEN] Size`, `Graphics_Mode`, `Graphics_File`, `Graphics_Dir`; `[OPT] Sound`,
`Animation`, `Auto_Record`, `RLL`; `[DATA] RLLFilename`, `RLLLevel`, `Player`, `Record Author`, and
the invented `Language`.

**`--ini` is what makes the options live.** A run left to find `LaserTank.ini` on its own gets it
read-only and starts on whatever level it was told to: eight parallel gate jobs must not race over
one file, and a screenshot must not change what the next player sees. Passing `--ini` says "this file
is yours" and turns both halves back on.

---

## The game's own UI, as it stands

Keys are **the original's accelerator tables**, read rather than invented — `ACC1` at
`lt32l_us.inc:120` and `ACC2` (the editor's) at `:150`. That table carries a comment which is really
a design rule — *"DONT use Keys that can be entered in the Author & Level Name field"* — and it is
about focus: while an edit control has the caret the accelerators must not fire. In the editor, Tab
moves in and out of the three text fields, and while one has focus every letter goes into it.

Arrows move, space fires. Everything else: `R` restart (105), `F2` new game (101), `U` undo (110),
`Ctrl+C`/`Ctrl+V` save/restore position (111/112), `L` levels (106), `V` own scores (113), `G`
global scores (906), `S`/`P` next/previous level (107/119), `N` sound (102), `A` animation (104 —
see *Next steps* item 5), `Ctrl+G` graphics dialog (226), `F5`/`F6`/`F7`/`F4` record / save
recording / playback / replay (123/117/114/124), `F8` auto-record, `F9` editor (201), `Ctrl+L`
language picker (invented), `Z` board size, `I` interpolation, `Esc` quit. `[` and `]` are ours.

**Three dialog properties are reproduced because they are observable.** The graphics dialog (226)
**applies immediately** (every `WM_COMMAND` branch ends in `SetUpGraphicsBox`, which is
`GFXKill(); GFXInit();`), has **no Cancel** (Close and Cancel run the same code and both write the
INI, `LTANK_D.C:1247`), and **the game keeps ticking underneath** — 226 never calls `GameOn(FALSE)`
the way the Difficulty dialog (225) does, and `DialogBox`'s modal loop still dispatches the
`WM_TIMER` posted to the main window, so an exposed tank can die while you pick a pack. Keys, though,
go to the dialog and never reach `AddKBuff`, which is the only reason the menu may navigate with the
arrows at all. The language picker is modelled on it deliberately: 226 *is* the original's own
Options-menu entry, so copying its properties puts the new dialog where a player already expects
this kind of choice.

**The three list dialogs are one dialog three times.** `LoadBox` (106), `HSList` (113) and `GHSList`
(906) each build one string per level with a `sprintf` whose padding and truncation are observable,
prefix it with a difficulty digit that `DrawLevels` colours by, and hand it to an owner-drawn
listbox; all three seek to `CurLevel - 1` and load on Enter. So `LevelList` is one class with three
modes carrying the original's own format strings. One difference is kept because it is observable:
**command 106 stops the clock and 113/906 do not** (`x = Game_On; GameOn(FALSE); DialogBox(...)`,
`LTANK.C:906`), so a tank in the open can die while you read your scores and cannot while you pick a
level.

**Playback needed no new rules** — `PBOpen`, `PlayBack`, `PBHold`, `Speed` and `SlowPB` have been
read by `Engine.Tick` since Phase 2, so the panel is four buttons wired to five fields. One line of
`PBWindow` has to live outside the engine: in Single Step the original's tick posts `ID_PLAYBOX_02`
back at the dialog from *inside* the tick, and an engine with no dialog cannot, so
`Playback.AfterTick` pauses instead. Measured on the corpus's first recording: Fast 372 ticks
(identical to the oracle), Slow 1,136, Step 372.

**The editor is a mode of the window, not a dialog over it**, because that is what the original is:
command 201 swaps the menu bar and the accelerator table (`LTANK.C:1446` picks `hAccelTable2` when
`EditorOn`), hides the nine buttons and repaints the control panel as a palette. There is no
180-pixel control panel here, so the window *widens* and the palette goes beside the board. Three
things the C does that a reasonable reading gets wrong:

- **`GetNextBMArray` is declared `[MaxObjects+1]` and initialised with 25 entries** (`LTANK.C:18`),
  so C zero-fills the last two. Rotating thin ice (25) or the tunnel selector (26) therefore turns
  the cell into **dirt** — defined behaviour of the 2010 binary, and nothing like "rotates to
  itself".
- **Rotating a *tunnel* really is out of range** (a tunnel cell is `0x40 | id << 1 | wait` = 64..79)
  and that read has no defined value, so both engines skip it. Returning the cell's own value and
  calling `ChangeGO` with it is *not* a no-op, because `ChangeGO` rewrites `BMF` through `GetOBM`,
  which answers 1 for a tunnel and erases the sprite. Repro: `<1alcasca`.
- **The palette's click bound is `i > MaxObjects+1`, so 27 is selectable** — one slot past the last
  drawn sprite. `GetOBM(27)` falls through its range test to bitmap 1, the cell holds an object id
  no table knows, and the *next* level load sanitises it into tunnel 5 (`BuildBMField`'s `pt > 0x19`
  arm). Defined, so it stays; the panel draws the slot as an empty frame so it is at least visible.

**Reviewing UI without a window** is the same trick everywhere: `--shot FILE` draws one frame and
exits, and `--menu` / `--panel levels|scores|global|playback` / `--editor` open the thing first.
Every path after `--` must be **absolute** — see *Environment notes*.

```bash
GODOT=$(echo ~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_*/*/Godot_v4.7.2-stable_mono_win64_console.exe)
"$GODOT" --path src/LaserTank.Game                                     # play it
"$GODOT" --path src/LaserTank.Game -- --shot D:/abs/out.png --menu --pack 3 --zoom 40 --level 7
"$GODOT" --path src/LaserTank.Game -- --panel levels --level 900 --shot D:/abs/out.png
"$GODOT" --headless --path src/LaserTank.Game -- --play --lpb D:/abs/x.lpb
"$GODOT" --headless --path src/LaserTank.Game -- --play --script "uufz.zllZ" --level 7 --out DIR
"$GODOT" --headless --path src/LaserTank.Game -- --editor --edit '<06l22' --save \
         --levels D:/abs/COPY.lvl --level 7
"$GODOT" --headless --path src/LaserTank.Game -- --ini D:/tmp/x.ini --check-options
"$GODOT" --headless --path src/LaserTank.Game -- --tick-rate 5         # the clock, timed
```

`--pack` takes a number (0 the internal sheet, 1..n the `.ltg` files sorted by name) or a word:
`internal`, `external`, or a `.ltg` by file name or header name.

---

## i18n as built

The original's ten translations, converted **once** into keyed UTF-8 JSON under `data/language/`,
plus a picker on `Ctrl+L` and a `[DATA] Language` key. The picker and the key are **invented** — the
original has neither: the language is chosen by *which of the ten `Setups/` trees you installed*
(`LANGFile` is built at `LTANK.C:1421` and never varies).

**Why this one file is not a transliteration.** `LANGUAGE.C` exists to read a *positional* file:
`Language\Language.dat` is 240 lines in six fixed-size sections (`SIZE_MMENU` 49, `SIZE_EMENU` 24,
`SIZE_BUTTON` 9, `SIZE_TEXT` 48, `SIZE_DIALOGS` 96, `SIZE_ABOUTMSG` 14, `LT32L_US.H:42`), comments
and blanks skipped, each translation in whichever 8-bit codepage its author's Windows happened to
use. **The population of such files is closed** — the ten that shipped in 2007 are all there will
ever be, and no rule reads a UI string. So the conversion happens once, in
`tools/convert_language.py`, and the game reads JSON.

That is a deviation from this project's usual answer, so it carries the usual price: the conversion
is checkable **against the artifact**.

- **Nothing is hand-typed.** The 153 string keys are parsed out of the frozen `LT32L_US.H` —
  `ButText1..9`, `txt001..txt045`, `REC_Title`, `help01..03`, `HelpFileName`, and the 96 `ID_*`
  dialog slots — and the menu trees' shape, command ids and separator positions out of the frozen
  `lt32l_us.inc`. The text section's numbering has two gaps (no `txt003`, no `txt030`), which is
  exactly what a hand-written table gets wrong.
- **The gate runs the conversion backwards.** `lang_check.py` rebuilds every one of the 240 source
  lines out of the JSON — undoes the escape conversion, re-attaches the accelerator hint after a
  tab, re-encodes to that file's own codepage — and compares **bytes** with the original line: 2,293
  lines across the ten files. That is what catches a mangled accent, a shifted section, a dropped
  string or a mis-keyed slot.
- **The 90 lines it cannot rebuild are asserted, not excused.** Nine lines per file address a menu
  *separator*, and the JSON keeps no text for one because the original never applies one either —
  `ChangeMenuText` checks `ItemInfo.fType == MFT_STRING` and a separator's is not. That is why all
  ten files carry the untranslated word `SEPARATOR` in those slots, and the gate asserts they do.

**The codepages were measured, not guessed** — nothing in the distribution records them. The table
is in *Next steps* item 1, along with the surprise: `Cs`/`Ct` are Chinese, not Czech.

**Two findings worth keeping in view.**

*The `while(!feof(fd))` bug is real and the port is free of it by construction.* `InitLanguage`'s
loop reads with `fgets`, then unconditionally chops the last character with
`szTmp[strlen(szTmp)-1] = 0`. On the iteration after the final line `fgets` returns NULL and — per
C99 7.19.7.2, so this is *defined*, not luck — leaves the buffer unchanged, so **the last
non-comment line is applied twice, the second time one character shorter, into the next slot.** In
the English file that lands the yahoo-group line into `about[12]` with its trailing newline eaten,
which is why the About box shows it twice. It is a quirk of a *loader the port does not have*, so
there is nothing to transliterate it into — which is why it is here and not in *Quirk hazards*.

*Four of the ten files are labelled 90% or less, and the fallback still never fires.* A translator
who skipped a line copied the English one rather than leaving it blank, and the only section any file
actually stops short in is `about` — which does not fall back, because borrowing the English tail
would put two languages in one paragraph. So the shipped corpus resolves **zero** strings through
`Language.Load`'s fill-from-base branch. The fallback is kept anyway (a future or hand-edited file
needs it, and the alternative is `[ID_WINBOX_03]` on a label) and `lang_check.check_fallback` builds
the partial file the corpus does not contain: one key blanked, one removed outright, one section
truncated, and a translated key that must *not* be overwritten. Both ways of being absent are
separate lines of code and both are tested.

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
rewrites the record it read rather than re-encoding it, its string writes stop one byte short of each
field, saving past the end of a file zero-fills the gap into playable levels, and Clear Field NULs
only the first byte of the hint. `LaserTank.Core.LevelRecord` is where all four live.

**`.ghs` / `.hs`** — flat array of 10-byte records, indexed by `level - 1`:

```
0  2  moves  u16
2  2  shots  u16
4  6  initials
```

**Every entry in all 13 `.ghs` files is non-zero** → every level is known-solvable, with best-known
move/shot targets. Ranking is lexicographic: moves first, then shots.

**A `.hs` is dense and positional, and that gives it a quirk.** Beating level 8 of a collection whose
file reaches level 3 writes records 4..7 in front of it — and `CheckHighScore` pads with its `HS`
global **before** refreshing that global from the file, with only `moves` forced to 0. So the padding
carries the *previous* level's shots and initials. Nothing reads them; every read-back test passes
with them zeroed; they are in every `.hs` the 2010 binary has ever written, and constraint 2 says
these files stay writable and not merely readable. So `HS` is a `ScoreState` the Session owns for its
whole life.

**`.lpb`** — 66-byte header then raw VK bytes:

```
0   31  level name
31  31  author
62   2  level number  u16
64   2  data size     u16
66   ..  keystream
```

Key codes: `37`=Left `38`=Up `39`=Right `40`=Down `32`=Fire. **`.lpb` compatibility is
bidirectional** — the 2010 binary must be able to play what Godot records.

**`.ltg`** — a 324-byte `TLTGREC` header (`Name[40]`, `Author[30]`, `Info[245]`, `ID[5]` = `"LTG1"`,
`MaskOffset` DWORD) followed by two ordinary Windows BMPs: the game bitmap from the end of the header
to `MaskOffset`, the mask from there to EOF (`LoadLTG`, `LTANK2.C:688`). Splitting a `.ltg` at its
`MaskOffset` into `game.bmp` + `mask.bmp` is a **byte copy**, because that is literally what the
container is — which is why external mode (`GraphM == 1`) and `.ltg` mode render identical pixels.
`GetLTGFiles` names a pack by the `Name` field in its header, so `Lasertank_Comix.ltg` lists as
*Lasertank Comix*.

**Objects** — IDs 0–25, table at top of `LTANK.H`. Tunnels are encoded out-of-band as
`0x40 | (id << 1) | waitbit`; see the `GetTunnelID` / `ISTunnel` macros.

**Board coordinates in level hints** — columns `A`–`P` = x 0–15 left to right, rows `1`–`16` = y 0–15
top to bottom. See *Next steps* item 3 for the evidence; the original never drew the grid.

---

## Quirk hazards — every one is load-bearing

**The rule these generalise to**, learned twice and worth holding while reading any of them: *in this
program a function's name tells you nothing about whether it mutates state.* `UpDateTank()` clears
`TankDirty` (`LTANK2.C:537`) and `Animate()` ends by setting it (`LTANK2.C:1161`) — both were nearly
missed because they are named like paint calls.

1. **Rendering mutates game state.** `UpDateLaserBounce()` (`LTANK2.C:565`) is a *paint* function
   that sets `LaserBounceOnIce`, making `MoveLaser` `goto LaserMoveJump` and take a second step in
   the same tick (`LTANK2.C:1631`). Stub drawing naively → laser-on-sliding-mirror behaviour changes.
   **Confirmed live:** `Tutor-with-Playbacks` levels 93 and 94 are the only two recordings in the
   corpus that reach it (tick 527 and tick 206). Level 93's own hint names the mechanism —
   *"deflected by three mirrors at K8 (**sliding mirror**), K10, N10, and N8"*. To re-check after any
   change to the laser or ice code, swap `LaserBounceOnIce = true` for a throw and replay the corpus:
   it must fire exactly twice.
2. ~~**Animation frame is game state.**~~ **Corrected — animation is cosmetic.** `Animate()` writes
   `Game.BMF[][]` and `MoveObj:1293` reads `bm = Game.BMF[x][y]` to carry the sprite along (the
   "Tere6 Bug" fix, `original/src/Bugs.txt` 02-25-02), but *every* read of `BMF` in the whole program
   is either a paint call or that sprite carry — **no bitmap ever feeds a decision.** Verified by
   exhaustive grep; see `oracle/README.md`. So `AniLevel`/`AniCount` need not be simulated for logic
   equivalence, and — more importantly — a `.lpb` replays identically regardless of which animation
   phase the game was in when the level loaded. Still worth tracing: a `BMF` divergence is a cheap
   tripwire for a transliteration slip.
3. **`wasIce` is a hidden return channel** from `CheckLoc()` (`LTANK2.C:1278`), read by `MoveTank`,
   `IceMoveT`, `IceMoveO` and `ConvMoveTank` — and **written by `AntiTank` as well**, even though it
   never names the flag: its four scans are `while (CheckLoc(...))` loops, so whichever ran last
   leaves `wasIce` holding its final probe. Its reach is longer than the three callers it looks like.
4. **Tunnel low bit is a flag.** `Game.PF2[x][y] |= 1` marks "waiting to transport";
   `Game.PF[x][y] & 0xFE` strips it.
5. **Anti-tank fire order is right → left → down → up**, and only the *first* match fires per call
   (`LTANK2.C:1655`). Tutor level 42 is literally "Inverse A-T's shooting order."
6. **Slide stack caps at 15**, silently (`if (SlideMem.count < MAX_TICEMEM-1)`), and `IceMoveO`
   mutates the stack while iterating it top-down (`LTANK2.C:1390`).
7. **`MoveObj` decrements `ScoreMove` and `UndoP`** in the tunnel path — the "Bartok Bug" workaround
   (`LTANK2.C:1310`).
8. **`SendMessage(WM_Dead)` vs `PostMessage(WM_Dead)`** — immediate vs deferred death, deliberately
   changed in 4.0.6. Ordering is observable, and the deferred arm is why **the tank can take one more
   move after it has already died**: `MoveLaser` kills at tick step 2, the key is consumed at step 4,
   and `WM_Dead` is pumped after the tick. See *Next steps* item 6 for a worked example.
9. `BuildBMField()` (`LTANK2.C:843`) leaves `i` uninitialized on one branch; currently unreachable
   because of the 2003 sanitization above it, but do not "fix" it silently.
10. Logic runs on a **fixed 20 Hz tick decoupled from rendering**, interpolating visuals. Never drive
    logic from `_process`. The "must not consume 144 keys a second" half turned out not to be about
    the frame rate at all — `WM_KEYDOWN` drops auto-repeat while a key is still pending, and that is
    the whole mechanism.
11. **`LTANK2.C:1738` reads `if (GFXOn) GFXKill;`** — a missing `()`, so the call never happens. A
    real bug in the original, in `SetGameSize`, and cosmetic. Same species as #9: **do not "fix"
    it.**
12. **`LoadLevel` does not reset the stale flags, so one `Engine` cannot replay two keystreams.** It
    resets `PF`, the tank, `RecP` and the slide records; it leaves `wasIce`, `WaitToTrans`,
    `ConvMoving` and `BlackHole` exactly where the previous game left them — faithfully, because the
    original never reloaded a level into a fresh process either. The consequence is a rule for *our*
    code: **anything that replays candidate keystreams must build a fresh `Engine` for each one.**
    The solver's trimmer did not, and silently reported winning keystreams as losing. The search
    itself is unaffected: `Restore()` puts all four flags back, which is exactly why
    `EngineSnapshot` carries them.
13. **Undo restores `Game` and nothing else, so a recording made with undos in it is not a transcript
    of what the player saw.** `UndoStep` is `Game = UndoBuffer[UndoP]` (`LTANK2.C:459`) and `TGAMEREC`
    holds the four playfields, the tank and the two scores. The laser is a *separate* global; so are
    `wasIce`, `WaitToTrans`, `ConvMoving`, `BlackHole` and `LaserBounceOnIce`; and the slide stacks
    are **cleared** by the next three lines rather than restored. So the world after an undo is not
    the world a clean replay of the rewound keystream would produce — while `Game.RecP` is rewound as
    if it were. Keys pressed after the undo then land in a different game and `RecP` keeps counting,
    so `RecBuffer[0..RecP)` — exactly what `WM_SaveRec` writes — can replay somewhere else entirely.
    Measured in the **oracle alone**, no port involved: flagship level 1,499, script
    `ffrlrfzcrfzzzzzzfufuZvfdddffdflzzzflllflrrrfr` plays to 100 ticks and 2 moves, and the 13 keys
    it leaves behind replay to 60 ticks and 6 moves. Not a bug to fix; it is why
    `roundtrip_check.py` asserts "the recording reproduces the play" **only for command-free
    scripts** and counts it for the rest. It also means an undo-heavy `.lpb` is still a perfectly
    valid recording — it wins or loses on its own terms, and all three engines agree on which. It is
    also why undoing a death lets you keep playing: the shot that killed you is not in `Game`.
14. **`SaveGame` starts blank, and Restore Position is only unreachable because of a menu.**
    `SaveGame` is a file-scope `TGAMEREC` (`LTANK2.C:59`), so command 112 before command 111 copies a
    *zeroed* record over the live game: tank at 0,0 facing 0, every playfield cell 0. The copy itself
    is defined, but command 112 does **not** stop sliding (unlike `UndoStep` three lines away), so
    the tank keeps whatever ice slide was running and `ConvMoveTank` walks off the end of `Game.PF` —
    out of bounds in C, a thrown `IndexOutOfRangeException` here. The original cannot get there:
    `LoadLevel` grays command 112 and only 111 enables it (`LTANK2.C:1028`, `LTANK.C:957`). So the
    engine reproduces the blank record faithfully and `Engine.CanRestore` carries the *menu's* guard,
    which every script driver applies before issuing the command. Found by `undo_check.py` on its
    first run: `Tutor.LVL` level 85, three-token script `llv`.
15. **A left-click is allowed to drown you.** `MouseOperation`'s destination filter (`LTANK2.C:314`)
    is a hand-written range test on the object id — `(dx < 3) || (dx > 14 && dx < 19) || (dx > 23) ||
    tunnel` — and `dx < 3` covers **water**, so clicking on water is a legal move order and the
    pathfinder will happily walk the tank in. It also admits a one-way from the wrong side, where the
    key walk simply stalls. Neither is guarded there and neither is guarded here. The same function's
    right-button arm has *no* filter at all: it turns along the larger axis and fires, without a path
    or a reachability test. And a click is never a move: `FindTarget` (`LTANK2.C:277`, recursive,
    four-way, over `PF == 0` only) floods from the clicked cell back to the tank, and
    `MouseOperation` walks the path backwards writing **arrow keys into `RecBuffer`** — two per step
    where the tank must turn first, one where it need not. So a recording made with the mouse is
    indistinguishable from one played on the keyboard.
16. **A level record is written back, not re-encoded, and the editor's write widths are one short of
    the field.** Command 603 writes the `TLEVEL` struct it read, and
    `GetWindowText(Ed1, CurRecData.LName, 30)` puts at most 29 characters and a terminator into a
    31-byte field — so byte 30 is *never* written by the editor, and the bytes between a short name's
    terminator and offset 29 keep whatever the record already held. Most `.lvl` files in the wild have
    the tail of an earlier, longer name sitting there. A writer that re-encodes from decoded strings
    passes every read-back test and still rewrites every unedited level in a collection.
17. **Saving level N into a shorter `.lvl` invents levels.** Command 603 seeks to
    `(CurLevel-1) * 576` in a file opened `OPEN_ALWAYS` and writes; the gap is zero-filled, and a
    zero-filled record is a legal level — all dirt, no name, no author, difficulty 0 — which the 2010
    binary lists and loads. Same species as the `.hs` padding quirk, and kept for the same reason.
18. **Clear Field truncates the hint, it does not erase it.** Command 601's line is
    `CurRecData.Hint[0] = 0` — one NUL into a 256-byte field — so the rest of the old hint stays in
    the record and is written to disk behind the empty one. It is in every `.lvl` the 2010 editor has
    ever produced from a cleared field.

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
              deliberately NOT under solutions/ so a hand solution is never
              mistaken for a solver one
  solutions/  what the interactive driver banks — one hand-supervised level at a
              time, each already through the two-engine gate.  A missing .lpb
              means a level has not been re-run, not that it is unsolved
  graphics/   .ltg packs      meta/  changelogs & name indexes
  language/   the ten translations as keyed UTF-8 JSON, converted once from
              original/src/Setups/*/Language/Language.dat
oracle/     the C reference oracle — see oracle/README.md
  stub/       minimal <windows.h> that shadows the real one
  win32_stub.c  real memory/files/messages, no-op GDI
  driver.c    LTANK.C globals + window proc + the WM_TIMER tick loop + tracing,
              plus the keystream / script / edit-token feeds
  build.sh    gcc -x c -I stub -I original/src
src/        the C# port         build.sh -> build/lasertank-{core,solve}.exe
  LaserTank.Core/   Objects.cs GameState.cs LevelFile.cs Engine.cs  (no Godot here)
                    Engine.Search.cs — snapshot/restore, ApplyKey, StateHash
                    Engine.Sound.cs  — SoundPlay's body: SoundLog?.Add, nothing else
                    LevelFile.cs also carries LevelRecord — the raw 576 bytes and
                      the editor's own write widths (hazards #16-#18)
                    not reachable from Tick(), and that is the test:
                      GraphicsFile.cs  .ltg + BMP readers, BMSTA/ColorList
                      SoundFile.cs     the .wav reader and lt_sfx.c's id->name table
                      Editor.cs        ChangeGO, the Shifts, Clear Field
                      Language.cs      the UI strings + both menu trees.  The one
                                       file here that is NOT a transliteration
  LaserTank.Cli/    Program.cs TraceWriter.cs — the oracle's CLI, the oracle's trace
                    EditDriver.cs — `--edit`;  LangDump.cs — `--lang-dump`/`--lang-list`
  LaserTank.Solver/ the batch solver and the interactive driver — see SOLVER.md
  LaserTank.Game/   the Godot 4.7 project
                    BoardView.cs   draws Game.BMF, routes keys, owns the HUD
                    Session.cs     LTANK.C's driver half — WM_TIMER, WM_KEYDOWN,
                                   WM_Dead, ReStart, WM_SaveRec, commands 110/111/
                                   112/114/124
                    PlayMode.cs    that driver with a scripted player
                    Atlas.cs       hands the sheet to the renderer
                    Options.cs     LaserTank.ini;  Packs.cs  GFXInit's three modes
                    GraphicsMenu.cs  GraphBox (226);  LanguageMenu.cs  Ctrl+L (ours)
                    LevelList.cs   LoadBox/HSList/GHSList — one class, three modes
                    HighScores.cs  AssignHSFile + CheckHighScore + the HS global
                    Recorder.cs    command 123 and PBWindow
                    Sfx.cs         lt_sfx.c — one player, monophonic
                    EditMode.cs    commands 201/601/603/604/605/701-705/710-713
                                   and the palette
                    Step4Check.cs, Step6Check.cs  the headless dumps list_check.py
                                   and lang_check.py compare against
                    Paths.cs       finds data/
                    Built by Godot or `dotnet build`, never published into build/
build/      C# output (gitignored)      LaserTank.slnx  the solution
tools/      the fidelity and presentation gates; solver-only tools are in SOLVER.md
```

| tool | what it proves |
|---|---|
| `difftrace.py` | two traces, or two directories of them: first diverging tick, first field, per-cell playfield diff. **The gate everything else is built on** |
| `test_difftrace.py` | self-test for the differ — run it before trusting a verdict |
| `replay_all.py` | replay every `.lpb`; expected outcomes + `.ghs` targets. `--traces DIR [--field] [--bmf]` writes one trace per recording |
| `engines.py` | both engines on one input + compare → a `Div` or `None`. Shared plumbing; also finds `bash`, Godot and `$LT_CORE`, and **builds the Godot C# first** |
| `sweep.py` | one fixed keystream over every level of a `.lvl`, both engines. Bare, the empty-keystream sweep: 2,347 levels |
| `fuzz.py` / `test_fuzz.py` | random keystreams, both engines, and **shrink** a divergence to level + shortest keystream / the self-test that injects known faults |
| `verify_solutions.py` | replay every `.lpb` through both engines: WIN on each, byte-identical traces, ratio to the `.ghs` record. `--levels` names the `.lvl` instead of finding it by directory name |
| `atlas_check.py` | every `BMF`/`BMF2` value over the corpus lands inside the 10×6 grid; every pack decodes to the same 320×192 pixels in Python and in C# |
| `tick_check.py` | every recorded `.lpb` through Godot's own input path and tick agrees with the oracle on result/ticks/moves/shots, re-records the same keystream byte for byte, and its own `.lpb` replays in the oracle. Plus `--tick-rate`: the real driver, timed, must be 20 ticks/second |
| `options_check.py` | the INI's semantics and round trip; every pack loading with mode 1 == mode 2 pixel for pixel; the three board sizes; and the laser bar measured out of a `--shot` PNG **in the cell the engine names** |
| `sound_check.py` | every recorded `.lpb` through both engines with `--sound`, so the per-tick `SoundPlay` id stream must be identical too; the sixteen WAVs decoding to the same PCM in Python and C#; `[OPT] Sound`'s semantics |
| `undo_check.py` | Undo and Save/Restore Position, which no keystream can reach, through `--script` and trace-diffed against the oracle's own `UndoStep`. Shrinks token-wise. `--replay LEVEL SCRIPT` re-checks one with `--field` |
| `list_check.py` | the three list dialogs' rows and the `.hs` writer's bytes, rebuilt in Python against `--check-lists` / `--check-scores`. Neither output is engine behaviour, so neither can go through the oracle — this is the sprite-sheet pattern instead. Plus `win`: level 1's own recorded solution replayed with a live `--ini`, to check the flag case posts a score |
| `roundtrip_check.py` | per undo-carrying script, six runs — the script through both engines and through Godot's command path, then the `.lpb` it records through both engines and through Godot's *playback* path |
| `mouse_check.py` | `MouseOperation` through `--script`'s click tokens, trace-diffed against the oracle's own copy |
| `editor_check.py` | 3,000 edit scripts against the oracle's own `ChangeGO`; the `.lvl` writer (an untouched level re-saves byte for byte across all 23 collections, the `GetWindowText` widths rebuilt in Python, the gap zero-filled, the saved board tied to the trace, and **the oracle — which *is* the 2010 loader — opening what was written**); and the game's own editor saving the same bytes as the driver |
| `lang_check.py` | the tab policy; 2,293 source lines rebuilt out of the JSON and compared **as bytes in each file's own codepage**; the key set and both menu trees against the frozen header and `.inc`; `--lang-dump` and the game's `--check-lang` against a Python rebuild; a synthetic partial language for the fallback; 5 INI checks |
| `convert_language.py` | the one-time import behind that. `--check` reports staleness without writing |
| `bump_rate.py` | classify consumed keys; bumps = desync signature |
| `dump_level.py` | print a `.lvl` level as ASCII with its hint |
| `unpack_lpb_txt.py` | decode a Text-Converter `.txt` wrapper back to `.lpb` |

Everything in `tools/` is stdlib-only. See `README.md` and `data/SOURCES.md` for provenance.

---

## Test corpus

**A trap for the day a `.lpb` will not replay:** `data/levels/LaserTank-2016-snapshot.zip` is a third
vintage of the flagship collection, **59 of its 2,030 levels differing** from the current one. A
recording made against one vintage cannot replay against another, so before debugging the engine,
check the level bytes.

**Tier 1 — `data/quirks/`, 317 quirk-focused levels, 187 recorded playbacks.** Upstream deliberately
withholds `.lpb` for the main collections; these help-section packs are the only recorded human
solutions in existence.

| Directory | Levels | LPB | Note |
|---|---:|---:|---|
| `tutor-with-playbacks` | 112 | 112 | + bundled `.ghs` — the only pack where recorded counts can be checked against a target |
| `tutor` | 92 | 0 | **the quirk specification** — each hint documents its trick |
| `rotary-mirrors` | 39 | 39 | 6 of these do not reach the flag — see below |
| `tricks` | 26 | 0 | |
| `pono-trick` | 18 | 20 | more LPBs than levels (alternate solutions) |
| `game-objects` | 16 | 16 | one level per object — **best first target for any new check** |
| `4triang`, `telek-1`, `l40`, `inchworm` | 14 | 0 | |

**Tier 2 — `data/levels/`, 20,914 levels across 13 collections**, every one with a non-zero `.ghs`
entry. No keystreams, but a solvability guarantee and a (moves, shots) target for each. The fuzzing
surface, and the population `SOLVER.md` measures against.

**Tier 3 — `data/demos/`, 20 hand playthroughs of `LaserTank.lvl` 1-19**, recorded by Michal and
verified through both engines. They cannot be regenerated and they are the only long winning lines
that are not solver output.

### The six non-winning recordings, and why they are the recordings

All six are in `rotary-mirrors`, and five of the six are the only files in that pack whose `.lpb`
author field reads `Ihab` rather than `Ihab-Ihab`. They consume their entire keystream and stop short
of the flag. `replay_all.py` pins each one's expected outcome, asserting the exact numbers wherever a
level hint documents them — so the suite is a real regression gate rather than a permanently red run.
`_0036` replays 621 keys / 419 shots against a hint that says *"stopped at step 621 (after 419
shots)"*; `_0009` gives 39 moves against *"blocked at 39 steps"*; `_0021` gives 148 moves / 257 shots
against *"it has a solution : 148/257 or better"*. `_0011`, `_0013` and `_0017` have no hint text.

**The zero-bump result.** A key that produces neither a move, a turn, nor a shot means the tank
walked into something solid. Across all 187 replays — **54,162 keypresses — not one bump.** A
desynced engine puts the tank in the wrong place and blocked moves pile up immediately.
`tools/bump_rate.py` computes this.

**The level-21 confirmation.** Its hint says *"it has a solution : 148/257 or better."* Replaying
gives exactly 148/257 and leaves the tank one cell above the flag, facing it — then its final two
keys turn the tank around and drive it into water. Replace that trailing `uu` with `dd` and the
oracle **wins at exactly 148/257**, the documented optimum. So the engine reproduces a known-good
solution precisely and the distributed recording's tail is simply wrong. (Level 21's playback ships
as `_0021.txt`, a Text-Converter base64 wrapper; decoded with `unpack_lpb_txt.py` and the only
derived file under `data/`, documented in `data/SOURCES.md` and regenerable from the `.txt` beside
it.)

---

## Environment notes

- **Two Pythons, both fine:** `python` = 3.12.7 (miniforge), `python3` = 3.14.7. Everything in
  `tools/` is stdlib-only and verified on both — keep it that way. **One exception:
  `tools/fit_eval.py` imports `numpy`**, which is installed for `python` only; it is a solver
  instrument, not a gate. A second carve-out should be argued for rather than assumed.
- **C toolchain:** MinGW-w64 (WinLibs gcc 16.1, UCRT),
  `winget install BrechtSanders.WinLibs.POSIX.UCRT`. Not on `PATH` globally; `oracle/build.sh` finds
  it under `~/AppData/Local/Microsoft/Winget/Packages/`.
- **.NET SDK 10.0.400**, `winget install Microsoft.DotNet.SDK.10`, at `C:\Program Files\dotnet`. Not
  on the shell's `PATH` until it restarts, so `src/build.sh` finds it. Only the .NET 10 runtime is
  present, hence `RollForward=LatestMajor` on the `net8.0` CLI.
- **Godot 4.7.2 (.NET/Mono build)**, `winget install GodotEngine.GodotEngine.Mono`, under
  `~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_*/`. The `godot` alias
  needs admin, so call the `.exe` by path — `..._console.exe` if you want stdout. `$LT_GODOT`
  overrides. A fresh checkout needs one
  `"$GODOT" --headless --path src/LaserTank.Game --import` before `--path` will run the project, and
  `Godot.NET.Sdk` restores from nuget.org on the first build (the install also ships it under
  `GodotSharp/Tools/nupkgs/` if the machine is offline).
- **`godot --path` makes the *project* the working directory**, so every relative path handed to the
  game resolves against `src/LaserTank.Game/`, not the shell's cwd. Worse than a wrong answer:
  `_Ready` throws, Godot logs the exception and **keeps the window open**, so the run hangs instead
  of failing. Pass absolute paths, and read a hung `--shot` as a path error until proven otherwise.
- **`godot --path` does not build C#, and says nothing about it.** It loads whatever assembly is
  already in `src/LaserTank.Game/.godot/mono/temp/bin/`, so editing `Session.cs` and running the
  project — or a gate — silently exercises the *previous* build. Only the editor builds on run.
  Verified the ugly way: a changed string on disk did not appear in the output while the gate still
  reported a green 208/208. Every Godot-backed gate calls `engines.build_godot_game()` first, and
  anything new that runs the project must do the same. By hand:
  `dotnet build src/LaserTank.Game/LaserTank.Game.csproj`. It builds into `.godot/` and
  `src/LaserTank.Core/bin/`, never `build/`, so it is safe beside a live solve.
- **A running solver blocks `src/build.sh`, but not the compilers.** `dotnet publish -o build` cannot
  replace `build/LaserTank.Core.dll` while a `lasertank-solve.exe` holds it open. Build each project
  into its own `bin/` instead and use **`$LT_CORE`** (overrides `engines.CORE`) or
  `replay_all.py --engine`. `LT_SOLVE=<exe>` is the solver's half, for `tools/bench.sh`,
  `campaign.sh` and `second_pass.sh`. The Godot project never publishes into `build/` at all.
- **Never run `test_fuzz.py` while a solver process is alive.** It rebuilds the core, Windows keeps
  `build/LaserTank.Core.dll` locked open by every running `lasertank-solve.exe`, and the rebuild
  loses its retry ladder — so *both* the "clean core builds" control and the restore check report
  `FAIL`, a red gate that is entirely the machine. The tell is
  `MSB3027 ... The file is locked by: "lasertank-solve (NNNNN)"`. The tree is still left byte-clean,
  so the fix is to wait and re-run.
- **Trap in the oracle's own usage text:** it advertises `--keys` as accepting "raw decimal VK codes
  separated by commas", but `driver.c` only parses the characters `u d l r f` and silently skips
  everything else. `--keys 38,38,32` therefore yields an *empty* keystream and an idle run that looks
  like it worked. The C# CLI matches this deliberately — if you fix one, fix both and re-run the
  corpus.
- **`bash` invoked from Python is WSL's `System32\bash.exe`**, not Git Bash — different filesystem,
  no gcc, no dotnet, and it fails with an unreadable `execvpe` error. `engines.find_bash()` skips
  System32 and falls back to the Git for Windows paths; `$LT_BASH` overrides. Any new tool that
  shells out should use it rather than bare `bash`.
- **Rewriting a source file from Python in text mode rewrites every line ending.** `src/` is LF,
  Python's text mode makes it CRLF, and `core.autocrlf=true` then makes `git diff` show *nothing*
  while every line on disk has changed. Patch and restore in **bytes**, or pass `newline=''`.
- **`text=True` on a subprocess decodes with the *locale* codec, and fails silently.** That was fine
  while every output was ASCII; the language dumps are not. On the first byte cp1252 leaves undefined
  the reader thread raises `UnicodeDecodeError`, `subprocess` swallows it in the thread, and
  `p.stdout` comes back **empty** — so four of the ten languages looked like a crashed game rather
  than a decoding bug. `lang_check.run_godot` captures bytes and decodes UTF-8 explicitly. Any new
  gate whose output can carry non-ASCII must do the same.
- **A backslash does not survive `python - <<'EOF'` in this harness**, and a *doubled* backslash
  arrives as a single one even in `cat > f <<'EOF'` — so escaping for the inner language is exactly
  backwards here. Use the editing tools for anything containing a backslash, or write the
  replacement text to a file first and read it in. What does work for a Python patch script that has
  to match C or C# source containing a `\n` escape: `cat > file <<'EOF'`, single backslashes
  throughout, and **raw** string literals — the triple-quoted form, switching to the single-quoted
  one where the text itself contains a double quote. A patch that fails to match a
  plausible-looking string is usually this.
- **The quirk packs mix `.lvl` and `.LVL`**, and the four that ship uppercase are the four biggest
  (`tutor`, `tutor-with-playbacks`, `rotary-mirrors`, `game-objects`). A `glob("*.lvl")` is
  case-insensitive on Windows and silently drops them on Linux — it already cost one campaign four
  packs with no warning. Match on `suffix.lower()`. Same for `.ltg`.
- **laser-tank.com is behind Cloudflare:** `WebFetch` returns 403. Use `curl` with a browser
  User-Agent. The site is a frameset — real content is in `menu.html`, `help.html`, `levels.html`.
- The original build was lcc-win32 (`original/src/_How to compile LTank.txt`, `LTank.prj`). Its
  dependencies are shallow; MinGW/clang work.

---

## Open questions

- **Which binary is the behavioural reference?** `original/bin/lasertank.exe` is dated 2010;
  `original/src/Setups/Files/lasertank.exe` is the 2007 build matching this source. Both are
  UPX-packed and 148,512 bytes but differ across ~95% of their bytes, so the version cannot be read
  off without unpacking. `Bugs.txt` stops at 4.1.2 (2005), so the 2010 build may contain changes we
  have no source for. Resolvable by trace-diffing the oracle against both. The Tutor readme warns:
  *"made/verified using LaserTank.exe Ver 4.1. The use of earlier versions may cause different
  results."* This is also the only way to settle a report of the form "the original didn't do that" —
  the oracle answers what the *2007 source* does.

---

## Cross-references — do not trust for quirk fidelity

- `github.com/tobiasvl/lasertank` — mirror of this same source.
- `github.com/h4tr3d/laser-tank` — SDL2/C++ port, but descends from a KolibriOS *reimplementation*.
- `lasertankpedia.zdobywca.com`, `lasertanksolutions.blogspot.com` — community game-data references.

**The oracle is the only authority.**
