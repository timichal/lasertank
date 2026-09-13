# Finished

Done, and kept rather than deleted because the reasoning outlived the work — half of what follows is
cited from elsewhere in these files. Newest last. What is *not* done is in
[`next-steps.md`](next-steps.md); the solver's own log is in
[`docs/solver/history.md`](../solver/history.md).

---

## ~~i18n: ISO language codes~~ — **done 2026-09-08**

`data/language/` is `en.json`, `fr.json`, `de.json`, `nl.json`, `pt.json`, `es.json`, `sv.json`,
`hr.json`, `zh-Hans.json`, `zh-Hant.json`; the `code` field inside each, `Language.BaseCode`, the
picker's rows and `[DATA] Language` all carry the same ISO code. What is left of the original's
**installer-directory** names is the left column of one table, `LANGUAGES` in
`tools/convert_language.py`, which is where `original/src/Setups/<dir>/` is frozen and therefore
where the pairing has to live:

| `Setups/` | ISO | display name | source codepage |
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

**No legacy alias.** An old `Language=US` in a hand-kept INI resolves to nothing and
`Language.Load` degrades it to the base language — which is `en`, so the player who had English
still gets English and the player who had Croatian re-picks it once. That was a decision, not an
oversight: the alias table would be ten dead rows kept alive for one boot.

**The display names are the port's now, and that is the other half of what changed.** They used to
be the translators' own banner lines — `"English - ( Example )"`, `"Croatian - ( 100 %)"`,
`"Español ( 85% complete !)"` — a version note, a completeness claim and a stray space, which also
sorted Spanish under E. `LANGUAGES` assigns the name; the banner survives verbatim in each file as
`sourceName`, percentage included (it is real information — four files are labelled 90% or less),
and `lang_check.check_structure` now compares it against the `.dat`'s own banner, because it is the
one string in the file the round trip cannot reach: it lives on a `#` line the original's own loader
skips. Two more fields joined it, `sourceDir` and the existing `sourceEncoding`, and all four
header fields plus `code` and `name` are now asserted against `LANGUAGES` rather than merely
written by it.

They are the **English** names rather than the endonyms because the picker draws in
`ThemeDB.FallbackFont`, which has no CJK glyphs — `简体中文` would be two boxes. The endonyms are
worth having the day this gets a font that can render them. The picker also draws the code and the
name as two columns instead of one padded string, since `en` and `zh-Hans` do not line up under a
proportional font.

`lang_check.py` is green on all four halves: 2,293 lines rebuilt byte for byte across the ten
files, ten languages identical in the CLI and in the game, 5 INI checks.

## ~~The level-39 report — and the oracle's one blind spot~~ — **done 2026-09-11**

Reported: flagship level 39, tank on C1, hold Right — the tank reaches I1 **and dies, but then
moves to J1**; Undo puts it back on I1, alive and playable.

**The report was right and the first round of analysis was wrong, because it asked the oracle a
question the oracle cannot answer.** Route `llllllluurrrrrrrrrr` traced identically in the oracle and
the core — `DEAD`, 37 ticks, 14 moves — and that agreement was taken for fidelity and written up as
quirk #8 working as designed. It was not. Running the **actual 2010 binary** (`original/bin/
lasertank.exe`, which is in the tree) settles it in one try: hold Right, the tank freezes on I1 while
the shot travels, and it dies **on I1**. It never reaches J1. Two engines agreeing is not evidence
when both inherit the same missing line.

**The mechanism, and why it is invisible headless.** Being shot is
`SendMessage(MainH, WM_Dead, 0, 0)` — `CheckLLoc`, `LTANK2.C:1469`, *synchronous*. So `WM_Dead` runs
inside `MoveLaser`, at **tick step 2**, and the handler (`LTANK.C:717`) does `GameOn(FALSE)` and then
opens `DialogBox(hInst, "DeadBox", ...)`, which is **modal and blocks right there, mid-tick**. The
player sees the tank where it was last painted — I1 — with the dialog over it. Execution does not
reach step 4's key test at `LTANK.C:613` until a button has been pressed, and **every** way out of
that dialog calls `UndoStep`: `ID_DEADBOX_UNDO` through command 110, `ID_DEADBOX_RESTART` and Cancel
directly (*"We have to undo the error first"*). `UndoStep`'s third line is `RB_TOS = Game.RecP`. So
by the time `:613` is evaluated the buffer is empty, its test is false, and no key is consumed —
**and `AntiTank()`, which lives inside that same block, does not get a turn either.**

The other arm says the same thing in one line and without a dialog: `if (VHSOn) { RB_TOS =
Game.RecP; return(0); }` (`LTANK.C:720`). That is the original's *own* non-interactive death path,
and it clears the buffer explicitly. Both arms end with the pending keys gone.

`Engine.SendDead` modelled neither. It did `GameOn(false)`, the sound, `Deaths++`, and returned — so
`Tick()` walked straight into its transliteration of `:613` with a key still pending, consumed it,
moved the tank to J1 and gave the anti-tanks an extra turn. `oracle/driver.c`'s `LT_WndProc` had the
identical hole, which is why the two agreed. **The fix is `RB_TOS = Game.RecP` in both**, with the
reasoning written out at each site.

```
t=36 T=8,0,2,1,0  S=13,0 P=16 D=0 G=1      before:  t=37 T=9,0 S=14,0 D=1   (J1, 14 moves)
t=37 T=8,0,2,0,0  S=13,0 P=16 D=1 G=0      after:   t=37 T=8,0 S=13,0 D=1   (I1, 13 moves)
```

**`:613` has no `Game_On` in it**, and that is the load-bearing detail — the test is
`(Game.RecP < RB_TOS) && !(Firing || ConvMoving || SlideO.s || SlideT.s || PBHold)`. The original is
not protected by a flag; it is protected by the fact that a modal dialog is already on the screen.
That is the same species as `Engine.CanRestore` and `Session.AcceptsInput` below — **a guard the
original gets from Windows still has to be written down somewhere** — and it is the third instance
of that rule in this project.

**The lesson, and it is the expensive one.** The oracle is the arbiter for the *rules*, and it earns
that on 2,347 levels and 187 recordings. It is **not** the arbiter for anything that depends on a
modal dialog, because `oracle/driver.c` says so in its own comment: *"Headless there is nobody to
answer it."* Quirk #8's write-up and this item's own first analysis were both derived from the
oracle alone and both inherited the error. `original/bin/lasertank.exe` is in the tree and takes thirty
seconds to check. **When the question is what the player sees, run the 2010 binary.**

**The second deviation, found on the way, and separately real.** `Session.Key` filtered exactly as
`WM_KEYDOWN` does but never asked whether the game was running, and `BoardView`'s mouse arm was the
same — worse, because `MouseOperation` writes *arrow keys* into `RecBuffer`
([hazard #15](quirks.md)). The
original is protected by the same modality: while the DeadBox is up, keystrokes and clicks belong to
the dialog. `Session.AcceptsInput` (`E.Game_On && E.Deaths == 0`) is that written down; `Session.Key`
and the new `Session.Click` are both behind it. It is invisible on the `UndoDead` path, because
`UndoStep` clears both queues anyway — the two paths that keep a phantom key are `EditorResume`
(command 604, no `UndoStep` on it) and `Replay` (command 124, which keeps `RB_TOS` on purpose).

All three script drivers apply the same rule — `oracle/driver.c`'s `script_box_up`, `LaserTank.Cli`'s
`BoxUp`, `PlayMode.Feed`'s `!s.AcceptsInput` — or `roundtrip_check` diverges the first time a random
script presses after a death. Aligning them made a `Z` reachable that late for the first time and
exposed a third thing: `Session.UndoDead` was `if (!Undo()) return false;`, but `LTANK.C:727` is two
statements and `GameOn(TRUE)` is **not** conditional on the undo, so Undo with an empty buffer
resurrects the tank where it died. Level 1719, script
`lllldllruzzzzuZZuduuff...zrdfu..zufurzzffflfc` — oracle and CLI 27 ticks, Godot 3.

**The criterion is a differential inside the game**, `--check-deadbox`, because a trace diff between
the three drivers cannot see the `AcceptsInput` half at all — they agree just as well with the rule
left out of all three. It plays a route until the box is up, knocks (five *distinct* keys, which is
not auto-repeat, plus two clicks), resumes through each of `UndoDead` / `EditorResume` / `Replay`,
and requires the transcript to match the un-knocked run. Guard reverted, it fails 3/3.

```bash
"$GODOT" --headless --path src/LaserTank.Game -- --check-deadbox \
         --levels D:/abs/data/levels/LaserTank.lvl --level 39
```

**The status line was sticky, too, and that was the third report.** `_error` is drawn every frame by
the status line's `_ =>` arm (`DrawHud` then, `DrawStatusBar` since step 7) and nothing cleared it, so the first *"nothing to undo"* — which every player
gets, by pressing U on turn one or by holding it one repeat past the bottom of the buffer — stayed on
screen for the rest of the level, contradicting every undo that worked afterwards. It is cleared at
the top of key handling now: a message survives until the next key. The board has no status line in
the original, so this is the port's own UI and a decision rather than a transliteration.

Green after: `replay_all` 187 (**112/112 move/shot counts still exact against the bundled `.ghs`** —
the strongest single check that the death change is right, since those are the 2010 game's own
recorded scores), `test_difftrace` 29, `sweep` 2,347/2,347, `undo_check` 600, `mouse_check` 500,
`roundtrip_check` 60×6, `tick_check` 208, `sound_check`, `editor_check`, `list_check`, `test_fuzz` 25.
**One thing the DeadBox still does not do here — see [*Next steps*](next-steps.md) item 4.**

## ~~The coordinate grid (A1–P16)~~ — **done 2026-09-13**

So a position can be *said*: "the tank is on A9". The level hints already use the notation and are
unreadable without it — and, it turns out, so did the original.

**This entry was written on a false premise and the premise is the more useful half.** It said *"the
original never drew one — the only `TextOut` calls in `LTANK2.C` are the score readout (`:1227`,
`:1648`) and `ShowTunnelID`'s `(%1d)` overlay (`:1725`)"*, and concluded there was nothing to
transliterate. Both halves of that sentence are true and the conclusion is wrong: **the grid is
drawn in `LTANK.C:502`**, in the `WM_PAINT` arm, under the comment `// Lable Game Grid`. The
window's paint code lives in `LTANK.C`; `LTANK2.C` is the board. A grep scoped to one file of a
five-file program is not a survey of the program. It was caught by a player looking at the 2010
binary and saying *"it is not true that the original never drew one, I see the numbers and letters
there"* — the same way the level-39 report was caught, and the second time these files have
recorded that lesson.

**So the convention is not inferred from hints at all; it is in the source.**
`strcpy(temps,"@"); temps[0] = temps[0] + i` for `i` = 1..16 is A..P across the top, and `itoa(i)`
is 1..16 down the side. Which is to say:

- **columns `A`–`P` = `x` 0–15, left to right.**
- **rows `1`–`16` = `y` 0–15, top to bottom.**

The hints agree, which is what made the earlier reading right by luck: `data/quirks/tutor/Tutor.LVL`
level 80's hint names "tunnel L7" and `PF[11][6]` is tunnel id 0; it also names five doors at
"O7, L7, I7, F7, C7" and the labelled board puts one in each; `tutor-with-playbacks` level 93 names
static mirrors at "K10" and "N10", and `PF[10][9]`/`PF[13][9]` are the two.

**What `LTANK.C:502` actually does, and what `BoardView.DrawGrid` does with it:**

- **All four sides.** Numbers down the left *and* the right, letters along the top *and* the bottom
  — so a cell in the middle of the board is two short looks from a label rather than one long one.
  Ported as it stands.
- **The type size is read, not chosen.** The row labels sit at `y = (SpBm_Height - 15) / 2` into the
  cell, and that `15` is the line height being centred — MS Sans Serif 8 pt. So 15 px here.
- **The gutter is `XOffset`/`YOffset` = 17** (`LTANK.H:93`), and it exists *for these labels*: at
  size 1 the board's right edge is 17 + 384 = 401 and `ContXPos` is 419, so the right gutter is 18 px
  and its labels start 8 px in. **The port's `Margin` went 16 → 24**, which is the one deliberate
  deviation: the original hand-kerns its two-digit row numbers into that 10 px — `strcpy(temps,"1 ")`
  at `x-1`, then `itoa(i-10)` at `x+3`, two `TextOut` calls to fake "16" (`LTANK.C:514`) — and a
  port that reproduced the hack instead of the intent would be transliterating a workaround for a
  window it no longer has. Ours is one string in a gutter wide enough for it.
- **Its column letters are drawn with `x = SpBm_Width / 2` as the left edge** under `TA_LEFT`, so
  they sit half a glyph right of centre. Ours are centred. Written down rather than locked down.
- **The tank's own column and row are lit.** The original does not do this; it is the one thing in
  the pass that is purely the port's, and it is what makes a position readable without counting
  across to K.
- Nothing is drawn over the board, which also keeps the pass clear of `options_check.py` — that gate
  measures the laser bar inside a cell to the pixel.

**`C` toggles it**, on by default and not persisted — the same terms as `I`, and again the port's
own: the original has no key, no menu item and no INI key for the grid, it is simply always on.
Plain `C` is free in *both* accelerator tables (ACC1 binds `VK_C` only with Control, 111 Save
Position; ACC2 the same, 601 Clear Field), and the editor keeps the key the way it keeps `Z`,
because a level's hint is written in this notation and the editor is where one gets written.

The margin change moved the window and the HUD strip (`HudH` 166 → 190, and the HUD now starts below
the bottom row of labels). Green after: `options_check` — the three window sizes and the laser bar,
which is the gate this could have moved — plus `editor_check`, `mouse_check` and `list_check`.

*Step 7 dissolved both constants.* The 24 px margin became `GutterFor(cell)` and `HudH` became a top
bar and a status strip that are laid out rather than reserved; the grid itself, and the reasoning
above for drawing it on all four sides, is unchanged.

## ~~Drop the Animation option~~ — **decided against, 2026-09-13**

`[OPT] Animation` / command 104 / the `A` key was listed here for removal on the grounds that
nothing would ever want `Animate()` switched off. That was wrong: a still board is easier to read
than a shimmering one, which is a reason to keep the key rather than a reason to drop it. It stays,
UI and all, and `Engine.Ani_On` stays the transliterated field it always was.

*Step 7 finished the job*: the toggle now takes effect on the tick after the keypress rather than on
the next level load, which is what the original does and what makes "a still board is easier to
read" an argument for the key at all — deferred to the next level, the key does not answer the
question the player asked it. See *step 7* below.

## ~~A collection picker — command 108, "Open Data File"~~ — **done 2026-09-13**

Before this the port could only be pointed at a `.lvl` from the command line (`--levels`) or by
what `[DATA] RLLFilename` remembered: `L` picked a *level* inside the current collection and there
was no way to change collections at all, so **22 of the 23 shipped ones were unreachable without a
restart**. `O` now opens a list of them.

**The original's is a native file dialog and this is not**, which is the one deliberate deviation
here: `OFN.Flags = OFN_HIDEREADONLY | OFN_FILEMUSTEXIST` over `*.LVL`, filter label `txt002`. What
is lost is opening a `.lvl` from anywhere on the disk, which `--levels` still does; what is gained
is a list that needs no file dialog, is reviewable with `--shot`, and can say something a file
dialog cannot — **how many of each collection's levels this player has solved**, counted out of the
`.hs` beside it.

Everything *after* the file comes back is `LTANK.C:924` transliterated, and the parts worth writing
down are the ones that are easy to forget:

- **`AssignHSFile()` is the load-bearing line.** The `.hs`, the `.ghs` and the default recording
  name are all derived from the level file's name (`LTANK2.C:1055`). A picker that changed the
  collection and not those three would post the new collection's scores into the old one's `.hs` —
  and a `.hs` is *positional*, so it would overwrite a real score rather than append a wrong one,
  with nothing on screen to say so. `Session.Files` is one object and reassigning it is the whole
  of `AssignHSFile`; `collections_check.py` checks all three names followed, for all 23.
- **`CurLevel = 0; LoadNextLevel(TRUE, FALSE)` is level 1**, not the level the last collection
  happened to be on: `LoadNextLevel` reads at `CurLevel` and increments after.
- **The two lines about command 118 have nothing to do yet and must not be forgotten.**
  `Backspace[BS_SP] = 0; EnableMenuItem(MMenu, 118, MF_GRAYED);` clear the ten-level history,
  which this port does not have. When 118 arrives, its stack has to be cleared here — a history of
  level *numbers* means nothing once the collection they index has changed. The note is in
  `Session.OpenDataFile` where it will be read.
- **108 stops the clock** (`x = Game_On; GameOn(FALSE); ... else GameOn(x);`), like 106 and unlike
  113/906/226.

Two things the port adds because it has no dialog loop to fall back into. The original's
`LoadNextLevel` answers an unreadable file with a message box and **re-posts command 108** to ask
for another one; here a failed open restores the previous collection, its two score files and the
level on screen, and says so on the status line. And `LoadNextLevel`'s opening `if (GameInProg)`
prompt — *"you will lose game data, do you want to save the game?"* (`txt039`) — is not here, for
the same reason the editor's "save changes?" is not: there is nowhere to answer it. A recording in
progress is dropped, exactly as pressing `S` already drops one.

**A second identity stopped being sufficient the moment a Session could hold two collections.**
`Session.Load` keeps an open playback when the level number matches and closes it otherwise — the
number *was* the level while a Session could only ever hold one collection. It is not any more:
F7 then `O` would have left a recording of level 1 playing over a level 1 it has nothing to do
with. A change of data file now ends a playback outright, which is the reason `Load` already gives
for closing one ("the keystream in `RecBuffer` belongs to a level that is no longer on screen"),
applied twice over. Same species as the `.hs` trap above: **a name that identified something only
because nothing could change underneath it.**

**One bug fell out of writing the gate, and it is the reason the gate exists.**
`Session.Load` took `Engine.LoadLevel`'s `false` as "no such level" — which is right for a file
that is *short* (`LevelFile.ReadLevel` returns null past the end, which is `LoadNextLevel`'s own eof
test) and wrong for a file that is *absent*: `File.OpenRead` throws. Until 108 nothing could hand a
Session an arbitrary name — the collection came from the command line or the INI and was checked
before the Session was built — but a picker can offer a file that is deleted before Enter is
pressed, and an exception out of a key handler takes the window down. The check found it by asking
for a `.lvl` that is not there, and the symptom was not a red line: Godot never reached `Quit()`
and the run hung. Both answers are one now, in the driver: `Error`, which the status line already shows.

`tools/collections_check.py` is the gate, and it has the two halves the shape of the change asks
for. The **list** is derived from the filesystem and nothing else, so it gets the
cross-implementation treatment the sprite sheets and the three list dialogs get — the game dumps
every row, Python rebuilds them from the same directories, and the two must agree row by row and by
`sha256`. The **switch** is behaviour, so it is driven: `--check-collections` opens all 23
collections in order *through one Session*, which is both the AssignHSFile check and the strongest
cheap claim available here — every shipped collection loads. Two more cases ride on that Session
because nothing else in the tree reaches them: a playback open across a change of collection, and a
`.lvl` that is not there.

The case trap the earlier note warned about is real and is why nothing in `CollectionList` uses a
`*.lvl` pattern: four collections ship uppercase (`Tutor.LVL`, `Game-Objects-in-LT.LVL`,
`Rotary Mirrors-Challenge.LVL`, `Tutor-with-Playbacks.LVL`), and `Directory.GetFiles`' pattern is
only case-insensitive on Windows. A second one showed up in the gate itself: `Rotary
Mirrors-Challenge.LVL` has a **space** in it, and a `\S+` in the tool's regex quietly checked 22 of
23 things and reported green on the other 22.

`out/levels/` is in the scan alongside the two corpus trees. It is where `EditMode.Save` puts a
level that came out of `data/`, and listing it is what closes that loop: edit, save, open, play.

## ~~Step 7: the UI redesign, first pass~~ — **done 2026-09-13**

The thing the whole approach was a prelude to. The line was drawn out loud at the start and it
held: **the mechanics of the puzzles must be exactly the same — every level solvable in exactly the
way it was — and the UI need not be.** The port was finished faithfully first because that is the
cheap way to be sure nothing mechanical moved while the game was built around it, and this is the
cashing-in. All four fidelity gates and all eleven presentation gates are green on the far side.

**What it replaced.** One function, `DrawHud`, drew eight lines of grey text in a fixed 190 px strip
under a board that was one of exactly three sizes. Four of those eight lines were a key legend —
twenty-eight bindings set as running text, a paragraph to be read rather than a list to be scanned,
costing a permanent quarter of the window to something a player needs twice. The ninth thing it drew
was the level's hint, **always**, which spoils every level that has one.

**The four changes that matter:**

1. **The board resizes with the window.** The layout is measured from the window every frame
   (`BoardView.Measure`): the board takes the largest *whole-pixel* cell that fits the square it is
   left, aspect locked, and the coordinate gutter and its type are derived from that cell rather
   than being the constant 24 and 15 they were. Whole pixels because a fractional cell puts the
   sprite grid off the pixel grid, and with nearest-neighbour filtering — which is what keeps this
   looking like the original rather than like a photo of it — that shows as rows of sprites one
   pixel taller than their neighbours.

2. **The three sizes became presets rather than the whole story.** `Z` and `[SCREEN] Size` are still
   `SetGameSize`'s 24 / 32 / 40 and still persist, but what they do now is *snap the window* so the
   board lands on that cell exactly. `_pinCell` holds it there; the player's next drag of the window
   frame frees it (`_selfResizes` tells our own resize from theirs). So the presets are a snap-to
   and not a mode, and `[SCREEN] Size` is never silently redefined by a drag.

3. **Animation applies on the spot.** `A` (command 104) used to say "next level" and mean it:
   `Engine.Ani_On` is the one persisted option that changes what a tick does, and that was read as
   a reason to defer it to the next `Session.Load`. The original does no such thing — `Ani_On` is a
   global, `ToggleOpt` flips it (`LTANK.C:887`) and the very next `WM_TIMER` reads it (`if (Ani_On)
   AniCount++;`, `LTANK.C:589`), so the board stops shimmering between one 50 ms tick and the next.
   It was a port artifact, not a fidelity constraint: **every gate runs headless with `Engine`'s own
   default of `true` and none of them presses `A`**, so nothing was protecting the old behaviour.

4. **The hint is command 301 again.** `H` — the original's own accelerator for it (`lt32l_us.inc:140`)
   — and off by default, as a card in the info column or, on a narrow window, a panel over the
   board. This was the one piece of the old UI that was actively *wrong* rather than merely plain.

**And the legend wall became `F1`**, which is also not an invented binding: `VK_F1` is command 907
in `ACC1` and 903 in `ACC2`, so the overlay took the key the original already had for the question.
It draws every binding as keycaps, grouped, in one or two columns depending on the window, and it
swaps its list for the editor's when the editor is open. The bindings live in two tables
(`PlayKeys`, `EditorKeys`) so the overlay and the router cannot drift.

**`Ui.cs` is new and answers to nothing in the original**, on purpose — palette, three faces, a
scale, and `StyleBoxFlat` factories for cards, dialogs, pills and keycaps. The port draws its whole
interface in immediate mode, which has no theme and no cascade, so the only way a panel here can
look like a panel there is for both to ask one file; nine dialogs, the editor palette, the HUD and
the overlay went from eleven private palettes to one. Immediate mode was **kept** rather than
rebuilt on Control nodes because the dialogs are the part of this port with observable *rules* —
modality, what stops the clock, what reaches `AddKBuff` — and those live in the key router, not in a
node tree. A Control-node rewrite would have bought layout containers at the price of re-deriving
all of that. `DrawStyleBox` is what makes immediate mode enough: corner radii, borders and drop
shadows are every modern-chrome primitive this interface needs. Web and native are the same code
path — `SystemFont` falls through its name list to Godot's own face, which is what a browser export
does — so there is no per-platform branch and no font file to ship.

**The one interpolation, and why it is one.** `LaserOffset` was a three-entry table read out of the
original (`LTANK2.C:1747/:1756/:1765` — 10, 13, 17), and a continuously-sized board needs it at cell
sizes the table does not have. The rule those three are samples of turns out to be
`round(cell * 10 / 24)`: it reproduces all three **exactly** — 24 → 10, 32 → 13.33 → 13, 40 → 16.67
→ 17 — so it is an interpolation *through* the original's own points rather than a guess beside
them. `_Ready` asserts that against the table at start-up, beside the tick-rate guard and for the
same reason. `options_check.py` still measures the bar in the PNG and still gets 4, 6 and 6 px.

**Two bugs the gates caught, both worth keeping written down** — they are the same bug in two
places, and it is the bug a *derived* layout has that a constant one does not:

- **A headless run has no window, and its viewport is not one.** Godot gives a headless root
  viewport a size of its own that has nothing to do with `project.godot`'s, and `Measure` believed
  it: a 92×160 "window", a board origin at (−274, −228), and a palette whose five columns landed on
  top of board cells — so `EditMode.Script`, driving the mouse headless, clicked cell (7,10) and
  selected palette slot 25 instead of painting. `editor_check.py` went red on one edit in eight.
  Headless now lays out the preset's window instead, so the geometry a gate exercises is the
  geometry a player at that preset gets, which is what makes driving the mouse headless mean
  anything.
- **`Measure` only ran in `_Draw`, which a headless run never calls.** Same gate, same symptom,
  found first. `Resize` measures unconditionally now.

`EditMode.Palette` also got the bound it should always have had: the *panel rect*, not just the
grid inside it. The original's test is `LOWORD(lparam) > ContXPos`, a half-plane, because its
palette and its board were either side of a fixed divider and could not overlap. Here both are laid
out from the window, so "cannot overlap" has to be asserted rather than assumed — otherwise a bad
layout silently turns board clicks into palette selections instead of missing loudly.

**`options_check.py` was edited on purpose, as [`PROGRESS.md`](../../PROGRESS.md) said it would
be.** It used to assert that
the window is `2 * margin + 16 * cell` wide and that the strip under the board is a constant height
at all three sizes. Both were true while the window *was* the board plus a fixed strip and neither
survives a window the player can drag. What it asserts now is what the presets actually promise: the
board is 16 cells of exactly the requested size, square, with its coordinate gutter inside the
window. `--shot` grew `board_x` / `board_y` for it — `margin` kept its old meaning, the gutter, and
is no longer the board's origin, so a tool wanting cell (x,y) wants `board_x + x * cell`.

**What was deliberately left for a second pass:** everything drawn is still keyboard-driven and
none of the new chrome is clickable; the HTML5 export has not been tried; there is no motion
anywhere. See [*Next steps*](next-steps.md), "The UI redesign, second pass".

## ~~Step 8: one table, and a quit that asks~~ — **done 2026-09-13**

Two changes out of playing the thing step 7 built, both in the same spirit: the UI is ours to
decide, and every decision is written down beside the C it departs from.

**The three lists became one table.** The original's three dialogs are three *column sets* of one
row — LoadBox prints the name and author, HSList your score, GHSList the posted best and your score
— so the panel prints all of them at once, one row per level. `L` opens it; there is nothing else
to press. It was built as three tabs first and that was wrong for the reason the merge was worth
doing at all: tabs are the same three lists with two of them hidden.

**`V` and `G` are free**, which is the point. ACC1 has no spare letters and 113 and 906 were two of
them spent on two renderings of the list `L` already opened.

**What the table is made of.** The rows are `BuildTable`, the port's own; `BuildRows` stays as the
original's three `sprintf` formats and is still what `list_check.py` diffs against Python byte for
byte. The *widths* are shared, so what the merge changed is the arrangement of the columns and not
one of them. Headers on two lines at the table's own character columns, a hairline between the two
score groups, and the original's own `**` and `>` on the rows where you beat the posted best.

**The panel stops the clock on every row, and that is the first observable behaviour this port drops
on purpose.** 106 stops it and 113/906 do not; one panel cannot be both. The C is quoted beside the
decision in `LevelList`'s header and in *The game's own UI* above — written down, not silently gone.

**`Esc` asks before it quits.** `Enter`/`Y` quits, every other key including `Esc` keeps playing, and
the board freezes while the question is up. Unconditional, because "only when there is something to
lose" needs the game to know what a player calls a loss. It is also the first of the modal prompts
the rest of the port is blocked on — see [*Next steps*](next-steps.md), item 3.

`--panel quit` reviews the prompt; `--panel levels|scores|global` are three names for the one table
now, kept so that no instrument named in these files started printing usage.

## ~~Step 9: the chrome answers the mouse~~ — **done 2026-09-13**

Step 7 drew an interface and every piece of it was a *label for a key*. The keycaps were pictures of
keys, the pills were read-outs, the rows were things the arrows moved a cursor through. This is the
half that was missing, and it is [*Next steps*](next-steps.md) item 4's first bullet, closed.

**The click is the key.** The accelerator switch was the tail of `_UnhandledInput` and nothing else
could reach it, so every clickable thing would have needed its own copy of what the key does. It is
`BoardView.Press(code, ctrl)` now, and a chrome click presses the key — going to whichever table is
live, ACC1 while playing and ACC2 in the editor, by walking the same two branches the key router
walks. That is the one rule this arm has: **the chrome may not reach a command the keyboard could
not have reached from where the player is standing.** Clicking `muted` in the top bar while the
editor is open is swallowed exactly as pressing `N` there is.

**A hit list, not a node tree.** `Ui.cs`'s header says why this UI is immediate mode — the dialogs'
observable rules (modality, what stops the clock, what reaches `AddKBuff`) live in the key router,
and a Control-node rewrite would have to re-derive all of them. That argument did not change when
the mouse arrived, so the mouse was fitted to immediate mode instead: **everything clickable
registers the rectangle it just drew** (`Hits.cs`), and the click is tested against what the last
frame put on screen. That inverts the usual bug — there is one rectangle, handed to the draw call
and to `Hits.Add` in the same breath, so a button that moves takes its hit box with it and a button
that is not drawn is not clickable. The cost is one frame of staleness, which `_Process`'s redraw
every frame makes unobservable.

**The third arm comes before the other two**, and the six modality guards come *behind* it — because
every panel registers its own scrim, so while one is up the chrome has already answered. Those
guards are what answer on the frame a panel was opened and not yet drawn, and headless, where
nothing draws and the list is always empty. That last part is why `mouse_check.py` and
`editor_check.py` never had to learn that any of this exists.

**What became a button.** The pills turn off the state they name; the collection beside them opens
108; the level card and the info strip open the table; the `Keys` card's five rows do what they say;
all four list panels select on the first click and commit on the second (a double-click that does
not have to be fast — touch has no hover, so the selection *is* the preview); every panel grew a
close button and closes on a click outside; the playback panel's transport is three keycaps; the
quit prompt is two buttons far enough apart that no repeated gesture reaches both; the editor's
three fields take the caret and its difficulty chip cycles 701..705. **And the F1 overlay became a
command list** — thirty-odd rows that press the key they draw.

**`Ui.Touch` is the concession to a finger.** The chrome scales with the window, so a keycap that is
comfortable at 1100 px is 17 px across at 520 — which is the width a phone gets. The hit rectangle
is grown to a 32-design-pixel floor and the drawing is left alone. 32 and not the 44 the guidelines
ask for: past the row gap neighbours overlap, and since draw order is z order the right-hand one
would quietly steal the left one's edge.

**Touch is the emulated mouse**, declared in `project.godot` rather than assumed — a tap is a press
and a release at the same position, so the hit list, the editor's brush and `MouseOperation` all
work on a phone with no code of their own. `emulate_touch_from_mouse` stays off: it would cost a
desktop player the hover the chrome uses to say what is clickable.

**A bug fell out of it.** `EditMode.Key`'s switch swallows everything it does not decline, and `F1`
fell through — so command 903 did nothing at all in the editor while the panel's own footer
advertised it. Found by wiring that footer to the pointer: the click worked and the key did not.

**And it has a gate**, which is the point of naming every target after the key it presses:
`chrome_check.py` dumps twelve screens, re-derives `Hits.Click` in Python to catch overlaps,
asserts the touch floor, and then runs 41 pairs of `--click centre` against `--press key:X` and
requires the two to end in the same state. Two paths agreeing is evidence; one path agreeing with
itself is not.

---

## ~~The file split~~ — **done 2026-09-13**

`PROGRESS.md` had grown to 1,924 lines and was doing two jobs badly: the entry point a context clear
needs, and the reference nine different questions land in. Split the way `SOLVER.md` was split in the
solver's session 32 — a hub plus one file per subject, **by what a reader wants rather than by
heading order**: the spec, the quirks, the formats, the harnesses, the rendering facts, the UI, the
tree and the machine, what is next, and this archive.

**No prose was rewritten and nothing was summarised away.** Every line of the old file is either in
the hub or in one of the nine files under `docs/game/`, checked line by line rather than assumed; the
only content that did not survive is one paragraph of the old purpose, rewritten to name the new
layout, and a line that had been duplicated by a bad paste (*"last-known value. See `SOLVER.md`."*,
twice). Section names the file referred to in italics — *Finished*, *Environment notes*, *Quirk
hazards* — are links now, because a reader who cannot resolve one is the whole cost of a split.

**What stayed in the hub is the part that is read every session:** where the project is, the build
and the twelve-plus-four gates, the goal and its three constraints, the next steps in one table, the
rules learned the hard way, and the one open question. The rules stayed because they are the reason
the rest can be trusted, which is the same argument that keeps `SOLVER.md`'s eight rules in its own
hub.

**Nothing mechanical moved**, so no gate could have noticed this and none was re-run. The check that
applies to a documentation change is the one that was run: every relative link and every anchor in
all 21 markdown files resolves.

## ~~Step 10: the chrome stops looking generated~~ — **done 2026-09-13**

Step 7 built an interface that worked, that every gate agreed with, and that a player looked at and
said *this looks AI-made*. That is a report about the **product**, not about the code, and it is the
first one in this project that no instrument could have raised: `chrome_check` was green, the twelve
presentation gates were green, and none of them has an opinion about whether a window looks like
every other window shipped in 2025.

It does not, any more. The board, the laser, the sprites and the grid are untouched — the
pixel-faithful half is not what was wrong — and **all nineteen gates are green**, including the four
fidelity gates and `options_check`, whose board arithmetic did not move because none of this
changed the board.

### The diagnosis, and why it was one decision rather than a taste

Read against [the catalogue of AI-design tells](https://github.com/funboy322/avoid-ai-design) the
old chrome scored almost a full house, and the hits were not independent:

| the tell | where it was |
|---|---|
| system fonts only, no display pairing | `Ui.Sans`/`Bold` asked for `Segoe UI` → `Inter` → `SF Pro` by name |
| a uniform generous radius on everything | `Card` 10 px, `Tile` 8 px, `Dialog` 14 px, pills at `h/2`, all with a drop shadow |
| uniform padding without hierarchy | three cards down the info column, one border, one gap, one caps label each |
| a timid evenly-spread palette | eight cool greys, one amber spent on about 2% of the pixels |
| the stat-tile row | `MOVES 0` / `SHOTS 0` as two bordered tiles — the KPI row of every analytics dashboard |
| no asymmetry | a centred card stack, symmetric about its own axis |

**One argument in `Ui.cs` produced most of that list, and it was wrong.** It ran: the four sprite
sheets are saturated primaries and they disagree about their ground, so a chrome in any hue would
fight whichever pack is loaded, so the chrome must be desaturated. The premise is true. The
conclusion does not follow, and measuring said so in one command — the four grounds are `#949410`
(internal), `#109494` (Eye Saver), `#60C000` (Comix) and `#187B00` (Warcraft II). **All four sit in
the yellow-green-cyan arc.** Half the wheel, red through violet, was never contested by anything. So
"desaturate" was never the only way to avoid the fight; it was the way that also removed every trace
of a point of view, and a chrome with no point of view is what a template is.

This is the same shape as [*"when the original has a table, read the table"*](../../PROGRESS.md) one
level up: a defensible-sounding derivation, never checked against the artifact, standing in for a
measurement that took thirty seconds.

### What it is now

**A technical readout, in ember on warm black.** The point of view is not decoration — it is what
the content already is. The board is a 16×16 grid labelled A1–P16, the score is two counters against
a posted par, and `LevelList` draws the original's own `%4d %-30.30s %5d %4s` and places its column
rules in glyph units. Every number on this screen is a measurement, so the interface is set like an
instrument.

* **One dominant hue, used lavishly.** Amber carries the wordmark, the rail, the section labels, the
  keycap glyphs, the live numerals and every modal's top edge. The hot vermilion is the *sharp*
  accent and is spent on two states only, recording and death, so it means something when it turns
  up. Every neutral is warm; there is no blue in the greys at all.
* **Corners are hard.** One radius, 2 px, and it is enforced at the primitive: `Ui.Rad` compresses
  whatever a caller asks for, so the six files that draw this interface changed with it and cannot
  drift back. 2 rather than 0 because a square corner on a 1 px border at fractional scale aliases
  into a visible nick.
* **Two real faces, shipped rather than named.** Archivo (variable, `wght` + `wdth` on one file) for
  exactly two strings — the wordmark and the level name — and IBM Plex Mono for everything else. See
  [`fonts/README.md`](../../src/LaserTank.Game/fonts/README.md). Both OFL, both with their licence in
  the tree. **This also fixed something the redesign's second pass wanted anyway**: `SystemFont`
  name lists drew differently on Windows, on macOS and in a browser export, where they fall through
  to Godot's own proportional face — so the one thing in this interface that *must* be fixed-pitch
  was not, on the one target [*Next steps*](next-steps.md) item 4 is aiming at.
* **The info column has no cards in it.** What separates its groups is a **rail** — one vertical
  hairline down the left that every group hangs off, with the level's own span of it lit amber — plus
  a rule and an amount of air. What ranks them is type size. A rail has a side, so the column has a
  spine and a reading edge; three centred cards are symmetric about their own axis and rank nothing.
* **The column is read from both ends.** What the level *is* flows down from the top and grows with
  its content; what a player can *do* is anchored to the bottom and never moves. Step 7 flowed all
  four cards from the top and left a third of the column blank underneath, which is not composition,
  it is where the stack ran out. The keys still yield to a long name and an open hint, and the rest
  of them are on `F1`, which stays.
* **The counters kept their size and lost their tiles.** That the two numbers a player watches are
  the largest thing in the column was step 7's one genuinely good decision about this panel, and it
  survives the boxes it arrived in — label hard left, number hard right, nothing drawn around either.
* **The top bar is not a filled bar** and the difficulty rank is not a filled badge. A slab with a
  hairline under it is the header component every framework ships; a pill beside a name is the same
  reflex one size down. Both are now a word and a rule.

### What was declined, and why it is written down

The obvious answer to *this looks generated* was sitting unread in the tree. **`original/src/Control.bmp`**
is the panel this info column replaced: a tiled brick wall out of the game's own sprite sheet, sunken
Win3.1 wells for the readouts, a serif display face, hard corners, zero shadows — a complete design
language, per language and per pack, and one that no template could ever accidentally produce. It is
still [*Next steps*](next-steps.md) item 4's third bullet and it is still unread.

It was offered and turned down deliberately: **the chrome is of today and has an opinion, rather than
wearing 1996 as a costume.** The port's whole argument is that the *rules* are sacred and the
interface is not, and a pastiche of the original's chrome would have blurred exactly that line while
also fighting the resizable layout step 7 built. Recorded here because the reasoning is the kind that
gets re-litigated, and because the bitmaps are still there for anyone who wants the other answer.

### Two clips the new face caused, and the rule they leave behind

IBM Plex Mono advances about 20% wider than the proportional sans it replaced, and two strings in
`LevelList` had been given **reserved** width rather than measured width. Both clipped, and both
clipped in the way that reads as a bug rather than as a truncation: the header came out
`… 21 solve`, and the footer lost `> beat the posted best` — the only thing on screen that explains
the two markers in the rows.

Neither was a font bug. **A layout that reserves a width has guessed, and a guess that survives is
only one that was never tested against a different face.** Both now measure what they need and shed a
*clause* when the answer is too wide — a ranked list of shorter forms, with the marker legend last to
go because nothing else documents it. That is the same shape as `LevelList.Fit` dropping the author
column before it shrinks the type, which step 8 already got right one level up.
