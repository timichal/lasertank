# LaserTank → Godot port: the game

**Purpose:** the port's entry point — what is built, what is deliberately frozen, the gates that must
be green, and what to do next. Read this first after a context clear. The detail it used to carry is
in [`docs/game/`](docs/game/), one file per subject, and the table below says which.

**The solver lives in [`SOLVER.md`](SOLVER.md)** and is a goal in its own right — solving every one
of the 20,914 known-solvable levels. It is *also* the second differential test of this port, and it
is deliberately not on the critical path for correctness: nothing in this file depends on it.
**Since 2026-09-08 the two halves run on two machines** — the game (this file) here, the solver
there.

---

## Where things are

| file | what is in it |
|---|---|
| **this file** | the status, the build and the gates, the goal, the next steps in short, the rules, and what is still an open question |
| [`docs/game/engine.md`](docs/game/engine.md) | the spec: the 20 Hz tick order, the input path and its surprises, and the three engines that have to agree |
| [`docs/game/quirks.md`](docs/game/quirks.md) | the eighteen quirk hazards, numbered — every one load-bearing, none of them to be "fixed" |
| [`docs/game/formats.md`](docs/game/formats.md) | `.lvl`, `.hs`/`.ghs`, `.lpb`, `.ltg`, the object ids and the board's own coordinates |
| [`docs/game/harnesses.md`](docs/game/harnesses.md) | the trace differ, the input languages, the fuzzer and its shrinker, and the three tiers of corpus |
| [`docs/game/rendering.md`](docs/game/rendering.md) | the atlas, the mask rule, the laser, the monophonic audio, and `LaserTank.ini` |
| [`docs/game/ui.md`](docs/game/ui.md) | the game's own UI as it stands — keys, panels, the editor, the review instruments — and i18n as built |
| [`docs/game/repo.md`](docs/game/repo.md) | the tree, what each tool proves in one line, and the environment traps |
| [`docs/game/next-steps.md`](docs/game/next-steps.md) | the open items in full |
| [`docs/game/history.md`](docs/game/history.md) | *Finished* — done, and kept because the reasoning outlived the work |

---

## Where the project is

Everything planned is built. A C reference oracle (`oracle/`), a C# transliteration that traces
byte-identically to it on the whole recorded corpus (`src/LaserTank.Core/`), a differential fuzzer,
and `src/LaserTank.Game/` — a playable Godot game with the game around it, an editor, and ten
languages.

It draws any level from `Game.BMF` with any of the four sprite sheets, runs a fixed 20 Hz tick,
takes the keyboard through the original's own `WM_KEYDOWN` filter and its own accelerator keys,
plays the original's sixteen WAVs off the sound ids the tick itself computes, undoes, saves and
restores a position, picks a collection and a level inside it and shows both high-score lists out of
`.lvl`/`.hs`/`.ghs`, writes
a `.hs` the 2010 binary would recognise byte for byte, records and plays back `.lpb` at all three
speeds, takes the mouse both as a *move order* through the original's own `MouseOperation` and as
the editor's brush, **answers that mouse in its own chrome as well — every keycap, pill, row and
card is a button, and a tap is a click**, edits and saves a `.lvl` byte-faithfully, labels the
board A1–P16 on all four sides the way `WM_PAINT` does, shows its UI in any of the original's ten
translations, remembers its settings in a `LaserTank.ini` with the original's own section and
key names, and takes the player's name **once** where the original asks for it in two dialogs.

**It now also looks like something.** Step 7 — the redesign the faithful port was the prelude to —
replaced the text strip under the board with a designed interface: a resizable, aspect-locked board
that takes whatever square the window gives it, a top bar, an info column, a status line, an `F1`
key overlay in place of the old wall of legend text, and a hint that is finally *behind* a key
instead of spoiling every level that has one. Nothing mechanical moved and every gate says so. See
[*Finished*](docs/game/history.md).

**And since step 10 it looks like something in particular.** Step 7's chrome was competent and
generic — a cool slate ground, cards at one radius with one border and one gap, a stat-tile row for
the two counters, and type asked for by the names `Segoe UI` / `Inter` / `SF Pro` — and a player
said so, in the one register no instrument in this tree can measure. It is an ember-on-warm-black
technical readout now: one dominant hue chosen by *measuring* the four packs' grounds rather than by
assuming they contest the whole wheel (they occupy a third of it), hard corners enforced at the
primitive so they cannot drift back, two OFL faces shipped rather than named — which also fixed the
fixed-pitch level list on the one target that had no fixed-pitch face — and an info column laid out
on a rail with no cards in it, read from both ends. **All nineteen gates are green, `options_check`
included**, because nothing about the board moved. The full diagnosis, and the one wrong argument in
`Ui.cs` that produced most of it, is in [*Finished*](docs/game/history.md).

**And since step 11 the one table is a list you can use at the size the corpus actually is.**
Steps 8 and 9 built it and gave it a pointer; what it still was, at 2,030 rows, was one screenful of
a file with no way to narrow it, no way to see which rows were done, no way down it but the wheel,
and a habit of closing itself whenever a finger slipped onto a letter. It has a filter bar — the
original's Search sub-dialog inlined, substring, title-or-author, difficulty mask and only-unsolved
— a three-rank mark column, gutters between its column rules, and a scrollbar that is the one thing
in this interface that is *dragged*. **Four of the five changes turned out to be the original's own
behaviour**, read out of `LTANK_D.C` rather than invented, which is the finding worth keeping: the
port had gone past the C in places nobody had gone back and re-read. All nineteen gates are green
and `chrome_check` has two more things to check. See [*Finished*](docs/game/history.md).

**And since step 12 the collection picker is a catalogue rather than a directory listing.** It
had 23 rows in one alphabetical run, with `4triang` between `Special-I` and `Game-Objects-in-LT`
and nothing to say that one of those is 2,030 levels of the actual game and another is five
positions of a walkthrough for level 149. It has shelves now — **Collections**, **Tutorials**,
**Walkthroughs**, **Your levels**, this port's naming rather than the website's — a line per
collection on hover, `LaserTank` first, the tutorials in teaching order and the hint files in level
order, the ten of them named (`Level 149: The 4 Triangles`, not `4triang`) rather than left as
whatever the zip called the file, **only `Esc` to close it**
(which is what the level list has done since step 11 — the two pickers are one panel to use), and
the column heads `LevelList` has had since step 8 (the rows' own size,
faint, with a rule under them, which is also what stopped the first row's hover band lighting
them). **The grouping is a display layer downstream of `Scan` and `BuildRows`**, so
`collections_check.py` still diffs the same rows against Python; the ten names are the one thing
that reaches a row, and they are written out again in the gate. The copy
is laser-tank.com's facts in this port's sentences, with the upstream wording quoted beside each
group in `CollectionNotes.cs`.

**And since step 14 the player has a name, once.** The original asks twice — `HSBox` wants
initials the instant a level is beaten, `RecordBox` wants an author the first time a recording is
saved — and they are the same question about the same person, asked at two moments because a 1996
dialog was the only surface the program had. `Ctrl+N` is one field on one panel, and `Options.Name`
**writes both of the original's keys** from it, so a `LaserTank.ini` this port wrote is one the 2010
binary opens with both of its dialogs already answered. The panel shows the four characters a `.hs`
record can hold as they are typed, because that is the one thing the merge can surprise anyone with.
On the way it took the refactor next-steps item 3 had named as the thing to do first — the level
list's filter, the editor's three fields and this one are `TextField` now — and **that second copy
made two bugs visible**: the editor's fields had never had the length cap their own comment claimed,
and the filter accepted characters no `.lvl` can contain. See [*Finished*](docs/game/history.md).

It also turned up **a gate that had been red for a reason nobody could reproduce**, on a screen
nothing had touched: `chrome_check` took its baseline INI by *copying the player's*, and three
fields in that file decide what the gate is looking at — the collection (`--panel playback` needs
one of the two with `data/demos/`), the sound (the `MUTED` pill is a clickable target), and the size
preset (every target scales off the window). The baseline is a file the gate writes now. Both green.
See [*Finished*](docs/game/history.md).

**There are no stubs left in the transliteration.** `MouseOperation` was the last one.

**What is deliberately frozen.** `original/` is a read-only historical artifact.
`src/LaserTank.Core/Engine.cs` differs from a literal transliteration by the word `partial`, twice
(on the class, and on `SoundPlay`, whose body is in `Engine.Sound.cs`). `Engine.Search.cs` has not
changed since the solver's first layer — **if a solver change seems to need an engine change, that
is the signal to stop and re-read.** Core also carries four files that are *not* reachable from
`Tick()` and so cannot move a rule: `GraphicsFile.cs`, `SoundFile.cs`, `Editor.cs`, `Strings.cs`.
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
[*Environment notes*](docs/game/repo.md#environment-notes)).

**`test_fuzz.py` must not run beside *any* other gate, not just beside a solver.** It injects a
fault, **rebuilds `build/lasertank-core.exe`**, checks that the gate catches it, and restores — so
for most of its runtime the shared core binary is deliberately wrong. Anything else reading that
binary meanwhile gets a spurious red. Observed exactly once and it cost a diagnosis:
`roundtrip_check` reported `FAILED -- 1 of 60` while `test_fuzz` was running alongside it, and the
same seed re-run cleanly on its own. The other gates parallelise fine with each other; this one is
exclusive. A red gate that will not reproduce serially was probably racing this.

Twelve more gates cover the presentation. They are listed apart because nothing about the rules
depends on them, and `options_check.py`'s pixel arithmetic is the one gate *expected* to be edited
when the look changes on purpose:

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
python tools/strings_check.py    # 11 UI catalogues, both ways against the source, ~20 s
python tools/collections_check.py  # the 23 collections + command 108's switch, ~20 s
python tools/chrome_check.py     # the chrome under the mouse + the filter field, ~110 s
```

All twelve want Godot; `atlas_check`, `sound_check`'s WAV half, `editor_check`'s third half and
`strings_check`'s last two degrade to a loud SKIP without it, `undo_check` and `mouse_check` need only
the two engines, the rest need Godot outright. Every one that runs the project **rebuilds its C#
first**, because `godot --path` does not — see
[*Environment notes*](docs/game/repo.md#environment-notes). None of them touches
`build/lasertank-solve.exe`, so all are safe beside a live solver. `options_check` opens three brief
windows for its pixel measurements; `--no-window` skips that half.
**`chrome_check` is windows all the way down and cannot be otherwise** — the hit list is built by
`_Draw` and by nothing else, so a headless run has an empty one; `--no-diff` cuts it to the twelve
dumps, ~15 s. Since step 11 it also drives `--type` into the level list's filter field and asserts
the row count, because that field is the one arm of the chrome neither `--press` nor `--click` can
reach.

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

Nothing is blocked. Roughly in the order they are worth doing; each one in full, with what it is
waiting on and what it would cost, is in
[`docs/game/next-steps.md`](docs/game/next-steps.md) — which carries the **open** items only, and
sends what closes to [*Finished*](docs/game/history.md).

**Items keep their numbers**, so a number retires with its item rather than being reused and the
gaps where **1** and **2** were are deliberate. **1** was i18n and it is
[*Finished*](docs/game/history.md), step 13: the audit it asked for was run, the rule it was to be
run on — *a key is read by a widget or it goes* — answered *go* for the 2007 strings as a whole, and
`data/language/` is eleven catalogues of the port's own text with `tools/strings_check.py` failing
both ways over them. **2** was a menu bar and it is [*Finished*](docs/game/history.md), decided
against — `F1` and step 9 had already taken both halves of what it was for, and the route it still
claimed to add is a route to commands items 3 and 8 have not written yet.

| # | what it is | the short of it |
|---|---|---|
| **3** | **`SkipComLev` and `Diff_Setting`** | what is left of *the rest of the original that is still missing*, which was read through on 2026-09-15 and emptied: three entries became items **9**–**12**, three were declined ([*Finished*](docs/game/history.md)), and **the modal work is gone entirely** — the DeadBox is the status line, the Difficulty dialog (225) is the rank chips, and the two name prompts were one settings row, which step 14 built. What remains is two INI keys read into `Options` as comments, a `LoadNextLevel` port to read them, and **the dependency item 7 is waiting on** |
| **4** | **The UI redesign, third pass** | steps 9 and 10 closed the pointing half and the *looks generated* half, and step 11 the *unusable at 2,030 rows* half — none of which was on this list until someone played it. Open: web export (closer — the faces are shipped now rather than named), motion, and a drag or two-finger gesture on the *board*, which would be this port's own rather than the original's. **The packs' own `Control.bmp`/`Opening.bmp` is closed** — declined a second time and for good on 2026-09-15, with item **11** taking its place |
| **5** | **More fuzzing, indefinitely** | `fuzz.py` on new seeds and on the 12 collections its first campaign never touched, plus `undo_check` / `mouse_check` / `editor_check` as three more campaigns of the same kind |
| **6** | **The solver** | the larger unfinished half and a goal in its own right. It runs on the other machine now, so any number here is a last-known value. See [`SOLVER.md`](SOLVER.md) |
| **7** | **Settings: `user://` and a typed store** | three jobs are tangled in `Ini` — a fidelity artifact worth keeping, interop with the 2010 binary's own file, and the port's own settings, which are already drifting (`[DATA] Language` is invented). The plan is the language files' plan: demote `Ini` to a one-way importer, move the port's settings to a typed `user://settings.json`. **Waiting on item 3** for the store — `SkipComLev` and `Diff_Setting` are still to land, and doing it first means writing the migration twice. **The panel half is no longer waiting**: step 14's name row is the first settings surface and it will migrate with everything else. Carries a real bug either way: `Paths.Ini` writes to the repo root and nothing in the tree uses `user://` |
| **8** | **The editor** | the commands are ported and gated (`--edit`, `editor_check.py`); what is missing is the chrome. Two blocked on a file dialog — Load Level (602) and Save As (606), both of which the collection picker is the model for — and one modal prompt, which is now the port's only one rather than a dependency shared with item 3: the *save changes?* question on leaving, `DrawQuitAsk` plus a third button. `LoadTID` *as* a dialog stays argued against — a prompt per painted cell is worse than the `T` mode it is here |
| **9** | **The level history, unbounded** | `Backspace[]` (118) — **not undo**, which is 110 and ported: a stack of level *numbers*, "back to the level I was just on". Ten slots because 1996 fixed arrays, so unbounded here. Free of every gate (letters never reach `AddKBuff`). `Session.cs:261` already holds the one line that must survive: command 108 clears it |
| **10** | **Recording: 125 and two file dialogs** | Resume Recording (125) replays a `.lpb` with no panel and records on from its end — and `Recorder.cs:127` split `PanelUp` from `Open` *for this*. The missing piece is a picker, shared with 114 (`F7`, which guesses three paths) and 117 (`F6`, which writes where step 1 happened to write). Model is the collection picker, same as item 8's two. **`F8` is a conflict**: the original's 125 key, spent here on 115 |
| **11** | **The opening screen** | `ID_GRAPHBOX_08` / `QHELP`, which is also what command 907 and `CurLevel == 0` paint. **A new screen, not `Opening.bmp`** — the per-language bitmaps are declined and that closes item 4's bullet. **Route undecided and the obvious one is taken**: `F1` went to the key list, so the current thinking is `Esc` growing from one modal into a screen |
| **12** | **The help dialog** | **WinHelp is 902–905, not 907** — this file had it the other way round until 2026-09-15. Base it on the old `.hlp` and rewrite to a mature style. **It must not displace the key list**, which is the right answer to `F1` and stays; the help is a surface that links into it. Inherits one open question from step 13: eleven catalogues, and help *text* is a different order of volume from help *labels* |
| **13** | **Hotkeys a player would expect** | asked for: next level on `N`, mute on `M`. Feasible — `M` is free everywhere, `N` is Sound today, so it chains `M`←102, `N`←107, `S` kept as a silent alias, `P` unchanged. **No gate can see it**: `LTANK.C:573` drops every VK outside 32–40 before `AddKBuff`, so letters are accelerators only. Four more on the list, worst first: `Ctrl+C`/`Ctrl+V` for save/restore position, no `Ctrl+Z` for undo, `F8`, and `Z` for zoom |
| **14** | **One options dialog** | `Ctrl+G` (graphics, 226), `Ctrl+L` (language) and `Ctrl+N` (name) are the same panel three times — step 6 copied 226's shape on purpose. A merge, not a rewrite, and 226's three properties are the design constraint: **applies immediately, no Cancel, the game keeps ticking**, so it gets no OK button. **This is the surface items 3 and 7 are waiting for** — order is 3 → 14 → 7, or 3 and 14 together |

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

## Rules learned the hard way

These are the ones that cost something. Each is a rule, not a story.

- **Do not "fix" anything on the way past.** If a bug has a *defined* effect, keep it —
  [hazards #9, #11, #14, #16–#18](docs/game/quirks.md) are all real bugs in the original that must
  survive. The exception argued for so far is a bug whose only effect is **undefined**: `GFXInit`'s
  `if (!(Mh || Gh)) GraphM = 0;` (`||` where it means `&&`) hands a NULL bitmap to `SelectObject`,
  and a decoder that throws cannot produce half a sheet. Same for rotating a tunnel in the editor:
  the read has no defined value, so both engines skip it.
- **When the original has a table, read the table.** `LaserOffset` is initialised to 10 at
  `LTANK2.C:46` and then *reassigned per board size* by `SetGameSize` — 10/13/17. Taking the
  initialiser for the rule and scaling it drew the laser three times too wide, and nothing but a
  pixel measurement said so. Same species as re-deriving `BMF` from `PF` (hazard #2), and as
  inventing key bindings instead of reading `ACC1`/`ACC2` out of `lt32l_us.inc`.
- **An exit criterion that only says "nothing changed" is not one.** Sound was planned as "adding
  audio must change no trace, so the gates staying green *is* the proof" — which would have shipped
  a game whose tank drove around in silence, because `SoundPlay(S_Move)` had been dropped and no
  trace carried sound. Make the thing being added *observable*, then diff it against the C.
- **The oracle is the arbiter for the rules, and not for what the player sees.** It earns the first
  on 2,347 levels and 187 recordings. But `oracle/driver.c`'s own `WM_Dead` comment says *"Headless
  there is nobody to answer it"* — so anything whose behaviour comes from a **modal dialog** is
  outside its competence, and two engines agreeing proves nothing there, because the second was
  written from the first. That is exactly how the level-39 extra move survived a full write-up as
  "quirk #8 working as designed": `CheckLLoc`'s death is `SendMessage`, the DeadBox blocks *inside*
  the tick before the key test at `LTANK.C:613`, and every exit from it calls `UndoStep` — none of
  which a headless stub reproduces. **`original/bin/lasertank.exe` is in the tree.** When the
  question is what the player sees, run it; it took one try and thirty seconds to overturn the
  analysis (the level-39 report, [*Finished*](docs/game/history.md)).
- **Grep the whole source, and scope a negative claim to what was actually searched.** *"The
  original never drew a coordinate grid — the only `TextOut` calls in `LTANK2.C` are the score
  readout and `ShowTunnelID`"* was a true observation with a false conclusion stapled to it: the
  grid is drawn in `LTANK.C:502`, because `LTANK.C` owns the *window* and `LTANK2.C` owns the
  board. It stood in this file for a day and turned a port item into an "addition". The cheap
  guard is the same one as for the 2010 binary: **before writing "the original does not", run it or
  grep all five files.** A player spotted this one by looking at the screen.
- **When the oracle can answer, ask it before writing down what "correct" means.** "A recorded game
  round-trips, all three agree" silently assumed a recording replays to the position it was saved
  from, and the oracle disproved that in one command ([hazard #13](docs/game/quirks.md)).
  A criterion written before the
  measurement is a hypothesis.
- **A guard the original gets from Windows still has to be written down somewhere.** A grayed menu
  item is the only reason `RestorePosition`'s three guardless lines are safe (hazard #14). The rule:
  transliterate the function literally, and put the menu's own condition in the **driver**, named
  after the `EnableMenuItem` call it stands for — then every driver can apply it and be diffed
  against the others. `Engine.CanRestore` is that, and `Session.AcceptsInput` is the second one: the
  DeadBox's *modality*, which — with `LoadNextLevel` on the winning side — is the only reason a
  keypress after death or a win is impossible in the 2010 binary (the level-39 report,
  [*Finished*](docs/game/history.md)). The sting is that once every driver applies such a guard, no
  differential between them can check it any more, which
  is what `--check-deadbox` is for: the same argument as *"an exit criterion that only says nothing
  changed is not one"* three bullets up.
- **An instrument must not write the player's state**, and that covers three files: the INI, the
  `.hs`, and anything under `out/recordings/`. `Session` writes a `.hs` only when it was given
  `Options` whose INI is writable; a `--shot`/`--play`/`--check-*`/`--tick-rate` run left to find
  `LaserTank.ini` on its own gets it **read-only**. The test is whether a gate could be run eight
  times in parallel and leave the tree as it found it. `tick_check.py` replaying 208 winning
  recordings wrote `.hs` files into six collections of `data/` before this was noticed, and
  `.gitignore` is why nothing said so.
  **And the list of what counts as an instrument is part of the rule.** Step 9's `--click`,
  `--press` and `--dump-hits` were never added to it, so a run of any of them left to find
  `LaserTank.ini` on its own got it *writable*. The gap hid for two steps because
  `chrome_check.py` always passes `--ini` and an explicit `--ini` makes the options live anyway —
  so the only way to meet it was to drive the chrome by hand, which is exactly what reviewing a
  panel means. It cost a session's worth of `[DATA] RLLFilename` walking off onto another
  collection, **and then a red gate that would not reproduce**: with the INI pointing at a
  collection that ships no demos, `--panel playback` opened nothing and `chrome_check` reported
  three missing targets that had nothing to do with any code. A gate that reads the player's
  mutable state has a second failure mode nobody can reproduce.
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
