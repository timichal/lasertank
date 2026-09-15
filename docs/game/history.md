# Finished

Done, and kept rather than deleted because the reasoning outlived the work — half of what follows is
cited from elsewhere in these files. Newest last. What is *not* done is in
[`next-steps.md`](next-steps.md); the solver's own log is in
[`docs/solver/history.md`](../solver/history.md).

---

## ~~i18n: ISO language codes~~ — **done 2026-09-08**

**Superseded in part by step 13 below**, which threw the converted strings away and put the port's
own catalogue in `data/language/` instead. What survives unchanged is everything this entry is
actually *about*: the codes, the table under them, and the Cs/Ct finding. What no longer exists is
`lang_check.py` and the JSON shape described at the end.

`data/language/` was `en.json`, `fr.json`, `de.json`, `nl.json`, `pt.json`, `es.json`, `sv.json`,
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

`lang_check.py` was green on all four halves: 2,293 lines rebuilt byte for byte across the ten
files, ten languages identical in the CLI and in the game, 5 INI checks. Step 13 retired it with the
conversion it was checking; `tools/convert_language.py` still decodes all ten `.dat` files, and the
table above is the reason it is kept.

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

### The DeadBox's first-turn guard — **decided against, 2026-09-15**

One thing the DeadBox deliberately does not do here. Its *headline* is the port's own since step 13
(`status.dead`); this is about its buttons. The dialog proc is four lines (`LTANK_D.C:159`) and the
second one is a guard: `if (Game.RecP > 1) EndDialog(Dialog, wparam); else EndDialog(Dialog,
ID_DEADBOX_RESTART);` — the proc *lies about which button was pressed* when at most one key has
been consumed, and the caller switches on that return value (`LTANK.C:725`), so **die on the first
turn and every button is Restart**, Undo included. `RetBox` ("Return to Game") has the identical
test, and there it is dead code: command 907 discards the `DialogBox` result (`LTANK.C:1373`).

**Undo behaves the same on turn 1 as on every other turn here.** Not an oversight to come back
to — the reasons, because this is the kind of call that gets re-litigated:

  * **Nothing that measures this port can see it.** Undo is not a key, so it is in no keystream: no
    `.lpb`, no `replay_all` score, no solver result changes. And `UndoStep` rewinds `Game.RecP` with
    the rest of `Game`, so even `RecBuffer[0..RecP)` — what `WM_SaveRec` writes — lands where the
    original's restart would have left it. What is left is one interactive button on one turn.
  * **All three script drivers already agree without it**, which is why no gate went red: `Z` is
    bare `UndoStep(); GameOn(TRUE)` in `oracle/driver.c:510`, in `LaserTank.Cli`, and in
    `Session.UndoDead`. **So the deviation is the cheap state and faithfulness is the expensive
    one** — implementing the guard means implementing Restart, which is command 105 and has no
    token in any of the three, added to all three, to make a difference no diff could check.
  * **105 is not a level reload either**, so that work is not the `Session.Restart` already here:
    it restores `Game.PF` from `CurRecData.PF` and calls `BuildBMField` (which re-scans the tank
    home and zeroes moves/shots), and it deliberately does *not* call `ResetUndoBuffer`, so the
    undo buffer survives a restart in the original — hence the death path's `UndoStep()` before
    `PostMessage(105)`, *"we have to undo the error first"*. The port's `Restart()` is
    `Load(Level)`, and `LoadLevel` does reset the buffer (`Engine.cs:481`).

**If this is ever reopened, both `Z` tokens move with it**, or the drivers stop being comparable.
`Session.UndoDead`, `oracle/driver.c` and `LaserTank.Cli` each carry a comment pointing here.

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
lose" needs the game to know what a player calls a loss. It was built as the first of four modal prompts the rest of
the port was said to be blocked on; **it is now the first of two** — the other being the editor's
*save changes?*, item 8 — because the other two were answered without a dialog at all (see the last
entry in this file).

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

## ~~Step 11: the level list becomes usable at 2,030 rows~~ — **done 2026-09-13**

Step 8 built the one table and step 9 gave it a pointer. What it still was, as a *list*, was one
screenful of a two-thousand-row file: no way to narrow it, no way to see at a glance which rows were
done, no way down it except holding a key or spinning a wheel, and a habit of closing itself the
moment a finger slipped onto a letter. Five changes — and **four of them turned out to be the
original's own behaviour**, which is the finding worth keeping: the port had invented past the C in
places nobody had gone back and read.

**Only `Esc` closes it, and that is `TransListKey`.** `LTANK_D.C:87` is eleven lines and answers
Home / Up / Down / End / PgUp / PgDn and `VK_ESCAPE`, returning **-2 — *no action* — for everything
else**. The port's "any other key closes" was a step-7 convenience with no warrant here, and it is
what a text field makes impossible anyway. Space went with it: the original's Enter is `WM_COMMAND`
id 1 and space was never a second one. Every *other* panel kept "any other key" at the time, because
none of them had anywhere for a letter to go — the collection picker followed in step 12, for a
different reason.

**The filter bar is the Search sub-dialog, inlined.** `SearchBox` (`LTANK_D.C:197`) is a modal child
of the LoadBox behind a `&Filter` button: a substring field, a Title/Author radio pair, a difficulty
mask and an "only unsolved" checkbox. All four are here — in the panel rather than in a dialog over
it, for the same reason step 8 merged three dialogs into one table: **a filter you cannot see while
you read the list is a filter you forget is on.** The rules are the C's, including the one line that
matters most and would never have been guessed: `if (TempRecData.SDiff == 0) TempRecData.SDiff = 255;`
(`LTANK_D.C:414`) — **an unrated level is promoted before the mask test**, so it matches whatever is
ticked. Without that line, unticking any one rank hides every unrated level in the corpus.

Three departures, each deliberate. The field is **always focused**, so a letter filters — the
listbox's own type-ahead, widened from a prefix to a substring, and the thing "only Esc closes" frees
the keyboard for. A **digit query also matches the level number**, which is `ID_LOADLEV_02`, the "or
Direct Level Number Entry" box the original put under its list, folded into the one field. And the
**filter survives the panel closing** but not the collection changing — the original rebuilds
unfiltered in `WM_INITDIALOG` every time, which is right for a dialog you open to pick one level and
wrong for the only instrument this port has for traversing a collection.

The original's own bug here is *not* reproduced, and it is named in `LevelList`'s header so nobody
re-finds it and assumes it was missed: the search branch never resets `i` before its loop, so every
row it lists is numbered from wherever the unfiltered pass left the counter.

**The mark column replaced two markers with three ranks.** `*` solved, `**` matched the posted best,
`***` beat it — at the row's own left edge, in the chrome's amber rather than in the row's difficulty
tint, because the rank is one channel and the marks are another. What it replaces is the original's
`**` beside the number and its `>` between the score groups, which were **one bit of information
(`BHS`, `LTANK_D.C:939`) drawn twice** — and the four characters they cost are what paid for the
column gutters below.

**The predicate is deliberately narrower than `BHS`.** `LevelFile.Beats` counts an *absent* posted
best as beaten, because there is nothing there to lose to — right for the original's marker, and it
would put three stars on every solved row of any collection shipped without a `.ghs`, which is most
of them. So ranks 2 and 3 require a posted best to exist. `Beats` itself is untouched: `BuildRows` is
the transliteration and still calls it, and `list_check.py` still diffs it against Python.

**The columns have gutters, and the rules are floats.** The two group hairlines were derived as
`cell - 1` and drawn at `+ 0.5`, which put them half a glyph from the cells either side. The
commonest `who` in the corpus is four characters — `%4s` is the reachable width of a six-byte field —
and it touched the rule dividing it from your own score. The rule positions are floats in character
units now and sit a character and a half clear on both sides. Same species as everything else in this
table: **a column is a position, so its divider has one too.**

**A scrollbar, and it is the one target in this interface that is dragged.** Everything step 9
registered is a click, and `Hits` carries the fact of a click and not the pointer's position — which
is most of why it can be forty lines. So the thumb gets a latch instead: the press on `scroll` sets a
flag in `BoardView` and `MouseMotion` feeds `LevelList.DragTo` until the button comes up. It moves
`_top` and pulls the cursor into the viewport after it, which is the mirror image of what the wheel
does: **the panel has one position and not two**, for the reason in `Scroll`'s comment.

### The instrument, and the rule that asked for it

The filter field is the one arm of the chrome that **neither existing instrument can reach**:
`--press` goes through `BoardView.Press`, which is the accelerator table, and a text field is not an
accelerator; `--click` reaches only what the hit list holds, and the field is a swallow because it is
always focused. So the one part of this step that it was mostly *for* would have shipped with no way
to review it — which is exactly *"an exit criterion that only says nothing changed is not one"*, the
rule the silent tank taught. `--type STRING` is that instrument: characters through the same
`Key(InputEventKey)` the router calls, and a `type <s> list=True rows=N of=M filtering=B q=Q` line
for a gate to read. `chrome_check.py` drives five queries through it and asserts the row *count*,
which is the one number all four filter fields land in — as bounds, not exact counts, because
`LaserTank.lvl` is corpus data and a gate that pinned 7 would go red the day someone adds a level
called Sokoban.

**`\b` and `\t` are two characters on that command line, not control codes.** A real backspace does
not survive the shell, Python's argument quoting and Godot's own command-line split — measured, not
assumed, and the symptom was a backspace that silently did nothing while the digits beside it went
green. `BoardView.Unescape` decodes them.

### Two things found on the way, and both were older than this step

**`--click`, `--press` and `--dump-hits` were not on the instrument list.** `Ui`'s own rule is that
an instrument left to find `LaserTank.ini` on its own gets it **read-only** — *"the test is whether a
gate could be run eight times in parallel and leave the tree as it found it"* — and step 9's three
flags were never added to it. The gap never showed because `chrome_check.py` always passes `--ini`,
and an explicit `--ini` makes the options live anyway. What it cost was an afternoon of driving the
chrome by hand: each run rewrote the player's INI, and the session ended with `[DATA] RLLFilename`
pointing at a different collection than the one that had been open. All four flags are on the list
now.

**And that was why `chrome_check`'s playback screen had been red.** `--panel playback` wants
`data/demos/<collection>/00001.lpb`; with the INI pointing at a collection that ships no demos, the
panel never opened and the gate saw the play screen's targets instead. It is green on all twelve
screens again, which makes the fix above load-bearing rather than tidy: **a gate that reads the
player's mutable state has a second failure mode nobody can reproduce.**

## ~~Step 12: the collection picker becomes a catalogue~~ — **done 2026-09-15**

The picker built in step 8 answered *which file*, and nothing else. Twenty-three rows in one
alphabetical run, `4triang` between `Special-I` and `Game-Objects-in-LT`, and no way to tell from
the list that one of those is 2,030 levels of the actual game, one is five positions of a
walkthrough for level 149, and one is the file whose readme calls its own contents bugs. The
information was all on laser-tank.com and none of it was in the game. Four changes.

**The column heads were drawn wrong and the first row's hover band lit them.** They were set a
point smaller than the rows — in a table whose cells are placed by *character column*, which is
only a position if every line advances by the same glyph — and separated from the rows by air
rather than by a rule, so the first row's band (`y - Line + 4`, the same rectangle that is its hit
box) reached up into the head's baseline and highlighted it on hover. Both are `LevelList`'s rules,
which that panel has had since step 8: **the rows' own size, faint rather than small, and a rule
under them.** The head string is spaced to `BuildRows`' own fields — `solved/total` set flush from
column 29 puts its slash on the row's slash and `where` on the row's path.

**The list has shelves, and they are this port's naming rather than upstream's.** The site has
*More Levels* — which means the ones with no high-score page, none of which are in this repo — and
*Trainings, Tutorials & Tricks* and *Hint files*, which are a heading and a filename. What actually
separates the three groups here is what you do with them, so: **Collections** (the thirteen with a
`.ghs`, the game proper), **Tutorials** (the six teaching packs), **Walkthroughs** (the four hint
files, one hard level shown part-solved), and **Your levels** for `out/levels/`. A shelf with
nothing on it is not drawn, which is `out/levels/`'s normal state.

**Only Escape closes it, which is what the level list has done since step 11.** This panel kept
"any other key closes" for one step longer, on the argument that it has no filter field for a letter
to fall into — true, and beside the point. `TransListKey` (`LTANK_D.C:87`) answers Home / Up / Down
/ End / PgUp / PgDn and `VK_ESCAPE` and returns **-2, *no action*, for everything else**, and the
two pickers are one panel to look at, so they have to be one panel to use: a hand that has learned
`O`, a glance, `Esc` should not discover that *here* the glance's stray keystroke took the panel
away. Everything else is swallowed and does nothing — swallowed rather than passed on, because a
letter reaching the board would move a tank nobody can see.

**`LaserTank` sorts first in Collections.** It is the original file and the one the other twelve are
measured against — upstream's own description of the Challenge files is *"You're done with the
LaserTank.lvl file? You can now continue with these"* — and alphabetical order buried it between
Gary-II and Sokoban-I, reading as the tenth of thirteen peers.

**Walkthroughs sort by the level they open out, not by their filename.** 40, 149, 173, 179 —
`l40`, `4triang`, `telek-1`, `inchworm` — rather than the alphabetical run the directory walk
produces, which interleaves them meaninglessly. A hint file is *about* a level of the original, so
the level is what orders it.

**The tutorials run in teaching order**, 1 to 6: what the objects do, then the beginner's course
with its solutions recorded, then the same course to fight, then rotating mirrors — the one object
big enough for a file of its own — then the two packs built out of what the Tutor files taught.
Alphabetically that is `Game-Objects, Pono's_trick, Rotary Mirrors, Tricks, Tutor,
Tutor-with-Playbacks`, which puts the two tutors last and the trick pack second: the exact reverse
of the order upstream tells people to work through them in.

Those ten and `LaserTank`'s **-1** are the whole of `CollectionNotes.Order` — a sparse side table
rather than a field on every row, because the twelve Challenge / Beginner / Sokoban / Gary / Special
files need nothing: they return 0 and keep the scan order, which their own names already sort into
series. The scan index is the tie-break, so the result is the same on every filesystem whether or
not `List.Sort` is stable. A stem nobody has placed sorts to the *top* of its shelf, 0 being below
both 1 and 40, which is the useful direction — the only unplaced files are ones somebody has just
added. All three kinds share one map because they are one thing: a position in a list.

**Ten of the twenty-three rows are named rather than stemmed.** `Game Objects in LaserTank`, `Tutor
with Playbacks`, `Tutor`, `Rotary Mirrors`, `Tricks`, `Pono's Trick`; and the four walkthroughs as
`Level 40: Down the Drain`, `Level 149: The 4 Triangles`, `Level 173: Telekinesis`, `Level 179:
Being an Inchworm`. The thirteen collections keep their stems, because `LaserTank`, `Challenge-IV`
and `Sokoban-I` are what they are called on the website, on their high-score pages and by anyone
asking for help. The other ten are not like that: their stems are whatever the zip happened to hold
— `Game-Objects-in-LT` abbreviated to fit, `4triang` and `telek-1` abbreviated past legibility,
`Pono's_trick` with the underscore a 1996 filesystem wanted — and none is a name a player would say
out loud. A walkthrough takes its number first because that is the whole of what it is, it is how
the shelf is already sorted, and `Level 179` is how a player who wants it arrived at wanting it; the
titles after the colon are the levels' own `TLEVEL.LName`s, spelled as the game spells them.

**The names are deliberately untranslated, unlike the blurbs.** A blurb *describes* a collection, so
it is copy and lives in `data/language/*.json` in eleven languages. A name is the identity of one
file, sitting next to thirteen rows that are file names; and the four walkthrough titles are level
names the game already draws in English out of the `.lvl`, because that is the only place they
exist. Translating the row and not the level it points at would read as two different levels. The
name column went 24 → 28 to hold `Level 179: Being an Inchworm`, the longest of the ten, and the
head positions and `strings_check`'s width cap moved with it.

**The grouping is a display layer and deliberately nothing more; the names are the one exception.**
`Scan` and `BuildRows` are what `tools/collections_check.py` rebuilds in Python and diffs row by row
and by `sha256`, and `--check-collections` dumps them in scan order. The shelves and the blurbs are
applied downstream of both, so the gate still compares the same two things it always did and went
green unchanged. `Names` is not downstream — it is *in* a row — so the ten are written out again in
the gate, beside the walk, the sort and the column widths that are duplicated there for the same
reason: a name column read from the implementation it is meant to check would be the one column in
the row that checks nothing. Same reasoning as
step 8's for keeping these rows out of `ListMode`: **a gate means what it says only if what it
checks did not move under it.** The hit names stay the collection's index in `_all` rather than the
drawn position, so `chrome_check`'s `row:0` is still the first collection however the shelves are
ordered.

**And every collection has a line saying what it is, on hover.** A `.lvl` names its levels and
cannot name itself — `TLEVEL` is 576 bytes of playfield, hint, author and rank, repeated, with no
header — so the original never needed the copy and this port has nowhere else to put it: its Open
Data File was comdlg32 listing a directory, and what a player knew about a collection they read on
the website before downloading it. `CollectionNotes` is that catalogue. **The facts are upstream's
and the sentences are not** — the download pages are a decade of accreted HTML written by several
hands in a second language — and the upstream wording is quoted above each group in the file so the
rewrite can be checked against what it is a rewrite *of*. A stem nobody wrote a line for still
lists: it lands on the shelf its directory implies and shows its path instead.

**The counts came out of the copy, because the row above it already has them.** Two of the
walkthrough lines quoted the site's own stage count and two of those contradicted the file: upstream
calls `telek-1` a *"1 level hint file"* and it ships five, and `l40` a *"2 level hint file"* and it
ships three. A description that disagrees with the `0/5` a centimetre above it is worse than one
that says nothing, and the row is the thing with a number in it.

**The strip answers the pointer and falls back to the selection**, because a player on the keyboard
never moves a pointer and a strip that was blank for them would be a dead third of the panel. It is
a fixed two lines whether or not the copy fills them — a strip that grew with the sentence would
move the list under the pointer that is asking about it.

### Three things the shelves broke, and what each one cost

**The panel had to stop being a fixed square.** Step 8's flat 560 was right for 23 rows of one
kind; with headings between them and prose at the foot it is either too small to show the list or,
on a big window, a third of a panel of empty floor with the note stranded at the bottom. It is sized
to its contents now — three fixed bands measured once and used twice, to size the panel and then to
place what is in it — clamped by the window, with the list scrolling when the clamp bites.

**The footer clipped, which is the step-10 lesson arriving one panel late.** The line as it stood — `↑↓ or wheel picks ·
Enter or a second click opens · any other key or a click outside closes`, before Escape-only
shortened its last clause — is wider than the panel's inside in the monospace, and Godot answers an overrun by cutting mid-word: `...or a click o`. It
sheds a clause at a time now, which is what `LevelList`'s caption and footer have done since step
10. The shelf blurbs take the same treatment by a different route — **dropped rather than shed**,
because the blurb is the one thing on its line that is optional and half of one reads as a broken
string.

**A wheel that moved the viewport was undone by the next frame.** The first attempt had `Scroll`
move `_top` and leave the selection where it was, on the argument that `Move` skips headings; but
the draw pulls the selection back into view every frame, so wheeling past it snapped straight back.
The wheel moves the *cursor*, as it does in `LevelList`, and the viewport follows — **one position,
not two**, which is the rule step 11 wrote down for the scrollbar from the other end. There is no
scrollbar here: `LevelList`'s needs a drag latch in `BoardView`, and what the overflow is worth at
the smallest window is one or two rows. The head line carries a count instead, because **a list that
is a row short with nothing saying so is a list a player believes.**

### And the gate that had been red for a reason nobody could reproduce

`chrome_check` was failing three `playback` targets before this step started, and the stash test said
so: the failure was on the untouched tree. The cause is the one its own step-11 entry named and did
not finish fixing. **The snapshot the gate takes of `LaserTank.ini` carries `[DATA] RLLFilename`**,
which is the player's "pick up where the last session left off" — and `--ini` makes the options live,
so it applies. `--panel playback` wants `data/demos/<collection>/00001.lpb`, which exists for two
collections out of twenty-three. This tree's INI pointed at Challenge-IV, so the panel never opened
and the gate saw the play screen's targets instead.

Confirmed rather than assumed: pointing the INI at `LaserTank.lvl` turned all twelve screens green,
and putting it back turned three of them red again.

**The first fix was too small, and the thing that caught it was the player using the game.** It
stripped `RLLFilename` and `RLLLevel` from the snapshot and left the rest of the copy alone; then the
next run came back green but with *nine* live targets on `play` instead of ten and 43 pairs instead
of 47, because someone had turned the sound on in between — and the `MUTED` pill is drawn only while
sound is off, and a pill is a clickable target. `Size` is a third one: every target scales off the
window, so a small enough preset puts them under the `MIN_PX` floor this gate exists to enforce.

So the baseline is a file the gate **writes** now — `write_baseline`, seven lines of INI, with
`Graphics_Dir` the one field taken from the tree because it is a path and nothing else knows it, and
`Graphics_Mode=0` so the gate does not depend on a `.ltg` being present. Green on all twelve screens
and back to a stable 10 live / 47 pairs with this tree's INI left exactly as the player left it —
`LaserTank.lvl`, sound on — which is the check that the inheritance is actually gone rather than
pointed somewhere else.

**Step 11 made the instruments read-only and stopped one line short, and so did the first attempt
at this.** *"A gate that reads the
player's mutable state has a second failure mode nobody can reproduce"* is the sentence that entry
ends on, and it is exactly right; what it fixed was the gate **writing** the player's file. Reading
it is the same bug from the other side, and it survived because the symptom was a red screen nobody
had touched — which reads as someone else's regression rather than as the environment. The rule this
leaves — written down once and then not implemented, which is why it took two passes: **a gate's
baseline is a file it wrote, not a file it copied.** A copy with the dangerous fields removed is
still a copy, and the next dangerous field is the one nobody has thought of yet.

## ~~A menu bar~~ — **decided against, 2026-09-15**

Item 2 of [*Next steps*](next-steps.md), and for a long time the cheapest thing on that list: step 6
converted all 73 menu items of both trees with their command ids and accelerator labels, so
`Language.MainMenu` / `Language.EditorMenu` is a ready-made model and building the bar would have
been data entry rather than design. It is not being built. What ended it is that **both halves of
what it was for were answered by something else**, and the doc had already recorded both without
drawing the conclusion.

**Discoverability was the first half, and `F1` answered it in step 7.** The port was key-driven and
the only way to learn a key was a wall of grey legend text under the board. The overlay is the
original's own help accelerator (907 / 903) and it shows every binding as keycaps, grouped, the
editor's included.

**Pointing was the second half, and step 9 answered it without a bar.** Every keycap, pill, row and
card the redesign drew is a button, which turns that overlay into a *command list* rather than a
legend: the pointing route to thirty-odd commands is the same list that documents them, and the
overlay itself opens by pointing — the info column's `F1` / *all keys* footer is a click target
(`BoardView.cs`, `DrawInfoColumn`'s footer). So a keyboardless build can already reach every command
without one, which was the last argument left for a bar and the one the two steps above were not
obviously going to cover.

**The residue is not a menu-bar job.** What the item still claimed to add was "a route to the
handful of commands nothing on screen names" — and those are the *unbuilt* ones in
[*Next steps*](next-steps.md): Load Level (602) and Save As (606), which are item 8's, and the
name prompts, Print (126) and the opening screen, which are item 3's. **A menu bar cannot route to a command that does not exist**, so every
one of them is blocked on being written, not on a bar; and the moment one is written, the route it
wants is a row in `PlayKeys` or `EditorKeys`, where it is documented and clickable in the same
stroke. The residue therefore moves to those two items, which is where the work actually is.

**What is lost, stated so it is not rediscovered as a surprise.** The original's menus are the only
converted artifact in the tree that nothing reads — `Language.MainMenu` / `Language.EditorMenu`
stay converted, gate-pinned and unused, the same standing as the `ID_*` slots item 1 is auditing.
If the port ever grows a surface where a list of *all* commands grouped by verb beats a list of
*bound* commands grouped by task, the model is still sitting there and the top bar is where it
goes.

**Step 13 took that sentence at its word and deleted them**, along with the rest of the converted
artifact: `Language.cs` is gone. The menu trees are still *derivable* — `tools/convert_language.py`
reads their shape and command ids out of the frozen `lt32l_us.inc` and prints them under `--out` —
so the model is still sitting there, one command away rather than in the tree.

## ~~Step 13: i18n, the other way round~~ — **done 2026-09-15**

Next-steps item 1 asked for an audit of the 155 keys per language file on one rule — *a key is read
by a widget or it goes*. Run against the file as a whole, the rule answered **go**. `data/language/`
is now eleven catalogues of this port's own text, keys named after the widget that draws them, and
nothing at runtime reads a 2007 string. The section that describes what is there is
[`ui.md`, *i18n as built*](ui.md#i18n-as-built); what follows is why it went this way and what it
cost.

**The audit's own arithmetic decided it.** Twenty-three of 155 were wired, nine of them arrived in
step 11 and six *findings* arrived with them — `ID_SEARCH_00` is a caption and there is no dialog,
`_02` is a group box, `_05`/`_06` are Cancel and Ok and the bar commits as you type, `_09` is a
master checkbox that greys five buttons a row of chips does not need, `ID_LOADLEV_03` is the button
that opened the dialog. Nine of fifteen in the one dialog anybody looked at hard is roughly the
ratio the whole file ran at, and the ratio is the argument: the remaining slots describe a Windows
menu bar, a nine-button control panel and sixteen dialogs, and steps 7 through 11 had replaced every
one of them. An audit that keeps 23 keys and deletes 132 is not an audit, it is a rewrite with extra
bookkeeping.

**And the strings were not worth keeping on their own account.** `ID_DEADBOX_DEAD` is *"YOU ARE
DEAD ! ! !"*; txt012 is *"Congratulation's You beat it !!"*, apostrophe and all. More to the point,
several of them are *layouts* rather than strings: txt014 is `"\nRecorded by "`, a newline smuggled
inside a translation because the original concatenates four pieces into one `MessageBox` body, and
txt009/010/011 are `"M: "`, `" S: "`, `" I: "`, three labels each carrying its own spacing so the
concatenation lines up. Neither is something a translator can work on. Both are one string with
placeholders now (`pb.recordedBy`, `score.record`), which is the form that lets a clause be put in
another language's word order.

**Eleven languages, and one of them is new.** English, Czech, German, Spanish, French, Croatian,
Dutch, Portuguese, Swedish, Simplified and Traditional Chinese — 240 keys each, 2,640 strings,
written rather than derived from the 2007 text. Czech is the addition and it is a small joke at the
distribution's expense: `Setups/Cs` and `Setups/Ct` are Chinese, not Czech, which is what the
codepage measurement found in step 6, so the port is the first LaserTank with actual Czech in it.

**The rule is a gate now, and that is the part that will still be true in a year.** An audit is a
thing somebody has to remember to run again. `tools/strings_check.py` fails if a key in `en.json`
appears in no source file, and fails if a lookup in the source names a key `en.json` does not have —
a dead key and a label with no string are both build failures. The scan is asymmetric on purpose: a
*lookup* is the indexer or `.F(`, and every one of those must resolve; a *mention* is any key-shaped
string literal anywhere, which is what keeps alive the several tables that carry their keys as plain
data (the F1 overlay's bindings, the editor's object names, `CollectionNotes`' stems). Read the
other way round it would be wrong — a mention is not evidence a key resolves — so it only ever keeps
a key and never demands one. Comments are stripped first, by a character walk rather than a regex,
because the two cases that matter are exactly the ones a regex gets wrong: `"out/recordings/"` is
not a comment and `/// `quit.title`` is.

**The eleven width-capped labels are the one place a translation has a hard limit**, and they are
capped because they are placed by character column rather than measured — over cells whose positions
are the original's own `%4d` / `%-30.30s` / `%5d` / `%4s`. A long word there does not reflow the
table, it lands on the next column's numbers. So German's shots column is `Sch.` and Dutch's is the
singular `schot`, and `strings_check.WIDTHS` is why. Everything else in the interface is measured and
the layout moves around it, which is step 7's rule and is why only eleven of 240 keys need this.

**What went.** `data/language/*.json` in its old shape; `tools/lang_check.py`, all 683 lines;
`LangDump.cs` with `--lang-dump` and `--lang-list`; `Core/Language.cs`, including the two menu trees
the entry above this one said were converted, gate-pinned and unused — they were, and this is where
they stop being converted too. `Step6Check`'s dump half is now `--check-strings`, and
`TLEVELINFO.DiffNames` — the five rank words, in English, in `Core` — is `RankKeys`, because Core
has no business holding one language's spelling of a rank while the mapping from bit to rank is a
fact about the file format and stays.

**What did not go, and why.** `original/src/Setups/*/Language/Language.dat` are frozen in the tree
and untouched; they were always the ground truth and the JSON was always derived.
`tools/convert_language.py` stays and still decodes all ten — it is the executable form of two
findings the JSON was not carrying: the codepages, which are measured and recorded nowhere in the
distribution, and `LANGUAGES`, the one table where the installer's directory names meet ISO codes.
It is a decoder now rather than a producer: it writes nothing unless given `--out DIR`, and it
refuses `data/language/`, which would otherwise let a stale 2007 conversion land on top of what
replaced it. **The `.ln` files under `Setups/Language/` are the one language artifact that will
never be read**: a 4.0-era format the `.dat`s superseded and the 2007 build does not open, so there
is nothing in them to decode against.

**What the round trip proved, and why losing it is not a loss.** `lang_check.py` rebuilt all 2,293
source lines out of the JSON and compared **bytes** in each file's own codepage. That was a real
proof and it is what made step 6's conversion trustworthy without a transliteration standing behind
it. It proved a property of a *derived artifact*, though, and once nothing consumes the artifact the
gate defends a file nobody opens — 683 lines and 25 s a run, for a claim whose producer and input
are both still in the repo and whose findings are written down. The five `[DATA] Language` INI
checks were never about translations and moved across intact, plus a sixth: the per-key fallback,
which the shipped corpus cannot exercise now that the gate holds all eleven files to one key set, so
the check builds the partial file the corpus does not contain.

**One thing the pass found that was not i18n.** `Packs.Load`'s fallback message and `Session`'s
three load errors are drawn on the status bar, which makes them interface; `--play`'s `highscore`
line and every `--check-*` dump are parsed by `tools/`, which makes them measurements. The two had
been one kind of string. They are two now, and the rule is the one `HighScores.Describe` has carried
since step 6: **a measurement that moves when the player picks a language is not one.**

## ~~The DeadBox and the Difficulty dialog, as dialogs~~ — **decided against, 2026-09-15**

Two of the four things item 3 of [*Next steps*](next-steps.md) listed as *blocked on a modal
prompt*. Neither is blocked and neither is unbuilt: **both were answered by something that is not a
dialog — the rank chips in step 11, the status line over the redesign — and the list went on
calling them deferrals** because nobody re-read the paragraph after the thing that answered them
landed. They are closed here so that item counts two rather than four.

**The DeadBox is a status line.** `DialogBox(hInst, "DeadBox", ...)` puts "YOU ARE DEAD ! ! !" over
the board with two buttons under it, Restart and Undo Last Move; here the headline is `status.dead`
on the line the board already has (`BoardView.cs:1571`) and the two buttons are two rows of legend
(`:3027`). The argument for the line over the box is the one the whole status line is: **a modal
over a dead board asks the player to dismiss a fact they can already see**, and it costs a keystroke
to say so. The player knows the tank is gone — the tank is gone on screen.

**What did not follow it out is the modality**, and that distinction is the reason this entry is
worth keeping. The DeadBox blocks *inside* `CheckLLoc`'s tick and that is load-bearing: it is why a
death mid-tick stops the rest of the tick, which is quirk #8 and `--check-deadbox`'s whole subject
(`PlayMode.CheckDeadBox:202`, and `Session.cs:556`, *The DeadBox's modality, written down*). So the
box's *look* was dropped and its *timing* was ported exactly, which is the port's usual split and
not a compromise between the two. **The first-turn guard is a third thing again** and was decided
against separately — see above, under the level-39 report.

**The Difficulty dialog (225) is the level list's rank chips.** 225 is a five-checkbox box over a
five-bit mask; `LevelList.Key` toggles the same five bits on `Ctrl+1`..`Ctrl+5`, clears to *all* on
`Ctrl+0`, and reads an empty mask as a full one because a mask with nothing in it is never what the
keystroke meant (`Rank`). The master switch is `_09` in the filter bar. **Building 225 now would be
a second control surface for one mask**, and the worse of the two by the argument step 11 inlined
`SearchBox` on — which is step 8's: a filter you cannot see while you read the list is a filter you
forget is on, and a filter behind a modal is that with one more step in front of it.

**What is genuinely left of `Diff_Setting` is not UI**: persisting the mask and having a ported
`LoadNextLevel` read it, which is where item 3 now files it.

**The rule this leaves, because it is the third time it has been applied and the first time it is
written down.** The original reaches for a modal in three situations and only one of them survives
translation: *asking a question whose answer cannot be deferred* (`Esc`'s quit, the editor's *save
changes?* — these stay modal), *stating a fact the screen already carries* (the DeadBox, and HSBox's
score summary, which is `WinLine` — these become the status line), and *collecting a setting*
(225, and the two name prompts — these become a control that is visible while the thing it affects
is visible). A 1996 dialog is not evidence that a question was being asked; it is evidence that a
dialog was the only surface there was.

---

## ~~Step 14: one name, where the original asks twice~~ — **done 2026-09-15**

Next-steps item 3 carried two of the original's dialogs as deferred work: `RecordBox`
(`LTANK_D.C:983`), which wants an author for a `.lpb` header, and `HSBox` (`:634`), which wants
initials for a score. Both keys were already read and written; what was missing was anywhere to type
them. They are now **one field on one panel** — `Ctrl+N` — and that merge is the whole of the step's
argument. What is on screen is in [`ui.md`](ui.md); this is why.

### The two dialogs are one question

`[DATA] Player` and `[DATA] Record Author` are the same person's name, kept twice. Nothing in either
box is about the level just played: neither asks for an opinion, neither offers a choice, and
neither can be answered differently without the `.hs` and the `.lpb` disagreeing about who was
playing. They are two because **a 1996 dialog was the only surface the program had** — there was no
settings screen to put a name on, so each feature asked at the moment it needed one, which is the
same finding the DeadBox and the Difficulty dialog produced two steps earlier and is worth stating
once more as a rule: *a dialog in the original is evidence of what the toolkit could do, not of what
was being asked.*

So `Options.Name` is one value and `SetName` writes both keys — the whole name into `Record Author`,
its first four characters into `Player`, which is all `THSREC`'s `char[6]` holds with `GetWindowText
(..., 5)` reading it. **Constraint 2 is what decides that it writes both rather than picking one**:
a `LaserTank.ini` this port wrote is one the 2010 binary opens with both of its dialogs already
answered, and the reverse read has a fallback for the same reason — a file that binary wrote has
only `Player` in it, because `HSBox` is the dialog it opens first.

**Two of the original's rules are recorded rather than kept**, and this is the kind of deviation the
UI half is allowed: `HSBox` writes its key only when the initials changed under a *case-insensitive*
compare, so re-typing `MZ` as `mz` does not rewrite it there, and `RecordBox` writes
unconditionally. Both are properties of a box with one field that closes on OK. Here there is one
field and one write, so the test is whether the text changed at all — typing `mz` over `MZ` is an
edit, and a settings row that quietly declined it would be the odd one out. The `stricmp` survives
where it is still load-bearing: `HighScores.Check` applies it to the *record*, which is what the
file actually carries.

### What the panel had to show, and the shape it took

The one thing a merge like this can cost is a surprise, and there is exactly one available: a long
name becomes four letters on a score line. So the panel draws those four characters in the accent as
they are typed — `SCORES AS  Mich` — rather than explaining them in a sentence nobody reads.

It is **`DrawQuitAsk`'s shape, not a list panel's**: a title, a field, a line of copy, and two
answers drawn as the keys that give them. That prompt's own header had called itself the first of
four and said what the rest would need — *"what they need past this is a text field and a third
button"*. This is the text field. It needed no third button, because the merge took one of the two
questions away.

**It is also the one panel here with a Cancel, and that is a departure worth writing down.**
`GraphicsMenu` and `LanguageMenu` deliberately have none — their choice is applied live, so an `Esc`
would be undoing what is already on screen, and the original's 226 has no Cancel either
(`LTANK_D.C:1247`: Close and Cancel run the same code). Nothing here is applied live: a name is a
string and the board does not change as it is typed, so a field with no way out that does not commit
is a field you cannot open to look at. `Enter` saves; `Esc`, the close button and a click outside
leave it. And **nothing is written when the text did not change**, which matters more than it looks
in a file shared with the 2010 binary: pressing `Ctrl+N` to see what the name is must not add a
`Record Author` line to it.

### The refactor it was waiting on, and the two bugs that were hiding in it

Item 3 named the prerequisite: *"what is missing is those two factored into one `Ui` field with a
caret — a refactor, not a blocker, and the thing to do before either name row is written."* Written
first, as it said. `Ui.Field` is the drawing and `TextField` is the state and the keys; the level
list's filter, the editor's three level fields and the name panel are its four callers.

**Two of the four things that were duplicated were wrong rather than merely repeated**, and both are
the shape of bug that only a second copy makes visible:

* **The editor's fields had no length cap at all**, while their own comment claimed they *"clamp to
  what a `char[31]` in a file the 2010 binary reads back can hold"*. They did not: `LevelRecord.Set`
  truncates on the way to disk, so a level name typed past thirty characters was accepted, drawn,
  and silently shortened at save time. `Max` is the record's own number now, so what the field shows
  is what the file will hold.
* **The filter took characters the corpus cannot contain.** Its test was `u >= 32 && u != 127` with
  no upper bound, so a Czech player typing `č` got a query that could never match anything: every
  level name in every `.lvl` is latin-1, and so is everything else these fields are compared against
  or written into. The bound is the editor's own rule, applied to the field that had missed it.

**And one thing the merge fixed on its way past**: `Recorder.Author` was a field copied out of the
INI in the constructor, so a name changed mid-session reached the *next* recording and not the one
being played. It reads `Options.Name` live now, which is what a settings row means.

### The instruments, and the one line format that had to change

`--type` was step 11's, for the one arm of the chrome neither `--press` nor `--click` can reach, and
it now types into whichever panel with a field is open — **in the same order the router tests them**,
so it cannot reach a field the keyboard could not. `--panel name` opens the panel for `--shot`;
`--name STRING` is the command line's way in, run-only unless `--save-options` is given, which is
`--sound`'s arrangement exactly.

**A name has spaces in it, and two log lines are read by splitting on whitespace.** `--check-options`
prints `name=` last on its line and `--type` prints `text=` last on its, with the gates taking the
whole rest of the line — the same fix `pack ... label=` needed first, found here by a gate reporting
`name=Michal` for a player called Michal Zlatkovsky.

Gates: `options_check` gained two arms (one name into both keys, and `Player` alone as the fallback
with the file left untouched), `chrome_check` a thirteenth screen and a `name_check` that types four
names through the panel and asserts the four-character cut, and `strings_check` answers for seven
more keys across eleven languages. The four fidelity gates cannot have moved and were run anyway —
nothing in `src/LaserTank.Core/` was touched — and every presentation gate is green, `editor_check`
included, which is the one that would have noticed if the fields' new length cap had reached a file.

---

## ~~Item 3's additive list~~ — **read through and emptied, 2026-09-15**

Item 3 of [*Next steps*](next-steps.md) carried a paragraph headed *Additive, nothing blocking*:
seven things named as left out of the original at the time rather than forgotten. It was never
worked through, only carried, and reading it out loud on 2026-09-15 turned out to be most of the
work — **three of the seven were items in their own right, three were declined, and one was not
what the list said it was.** What was left of item 3 was two INI keys, and step 15 below is those
two keys, so the item is closed entirely.

| what it was | outcome |
|---|---|
| `Backspace[]`'s ten-level history (118) | item **9**, and unbounded |
| Resume Recording (125) | item **10** |
| "View Opening Screen" (`ID_GRAPHBOX_08`) | item **11**, as a new screen |
| `LaserTank.hlp` / WinHelp | item **12** |
| Print (126) | declined, below |
| "Change Directory" (`ID_GRAPHBOX_09`) | declined, below |
| per-language `Control.bmp` / `Opening.bmp` | declined, below |

**The one that was not what the list said it was is 118.** *Ten-level history* reads as *ten levels
of undo*, and it is nothing of the kind: undo is command 110, bound to `U`, repeating while held,
ported since Phase 2. 118 is a stack of level **numbers** — `LoadNextLevel` pushes `CurLevel`
whenever the level changes (`LTANK2.C:1045`), `Backspace` pops one (`LTANK.C:1000`) — so it is
*navigation*, not history of play. Ten is `int Backspace[10]` (`LTANK2.C:94`) and nothing more: a
fixed array walked as a ring, which is why the pop leaves a zero behind it so the wrap cannot loop.
The bound was never a decision, so item 9 does not inherit it.

**And one line of item 3 was simply wrong**, which is the reason to write this entry rather than
just delete the paragraph. It read *command 907 is WinHelp in the original*. 907 is the Quick About
Box (`LTANK.C:1368`), which sets `QHELP` and paints `Opening.bmp` over the board while a `RETBOX`
dialog is up. WinHelp is 902 (`HELP_INDEX`) and 903/904/905 (`HELP_KEY` on `help01`/`02`/`03`).
Conflating them merged two features into one entry: the opening screen (item 11) and the help
(item 12). `BoardView.cs` carried the same error in its `F1` comment and it is corrected there too.
What *is* true, and is why one panel could answer both, is that `F1` is 907 in ACC1 and 903 in ACC2
— two help ids for one key.

### ~~Print (126)~~ — **decided against**

`x = Game_On; GameOn(FALSE); Print(); GameOn(x);` — a 1996 program printing the board to paper.
There is no argument to have here and the entry exists only so the command is not looked up twice.
Nothing in the port is a document, the level list and the collection catalogue are both searchable
on screen, and a screenshot is the modern form of the request. **The clock-stopping is the only
interesting line in it** and it is already ported six other places (106, 108, 225, 901, 907 and the
DeadBox all do `GameOn(FALSE)` around a surface), so nothing is lost by dropping the case.

### ~~"Change Directory" (`ID_GRAPHBOX_09`)~~ — **decided against**

A shell folder browser in the Graphics dialog: `Browse(Dialog, GraphDN, txt037)`, then re-scan for
`.ltg` files and write `[SCREEN]` `Graphics_Dir` to the INI (`LTANK_D.C:1308`). **It is the
*graphics* directory, not a data directory** — worth saying because the name does not.

It is declined because the port already answers it twice over and better: the packs live in a known
place in the repo, `Packs` enumerates them, `--gfx-dir` moves the root for anyone who needs it moved,
and the key is persisted regardless. What is genuinely lost is an in-app way to point at a pack
downloaded to some other folder. **If that ever comes back it comes back as a file dialog**, which is
item 10's and item 8's problem already — a second, older, modal folder browser is not the way to get
there.

### ~~The packs' own `Control.bmp` and `Opening.bmp`~~ — **decided against, a second time**

`LoadImageFile` ships these per language *and* per pack and nothing in the port reads them. They
were declined once as the answer to step 10 and the reasoning was kept in item 4 rather than here;
this closes them for good and moves the reasoning where declined things live, because it is exactly
the kind that gets re-derived by whoever next opens a pack and sees them.

**`Control.bmp` is the info column's own ancestor** — a tiled brick wall out of the sprite sheet,
sunken Win3.1 wells for the readouts, a serif display face, hard corners, no shadows. It is a
complete and genuinely unmistakable design language that no template could accidentally produce, and
it would have been the **safest possible fix for "this looks generated"**, which was step 10's whole
problem. That is why it keeps being offered.

**It was turned down because it makes the chrome a costume.** The port's argument is that the rules
are sacred and the interface is not; a pastiche of the original's panel blurs exactly that line, and
it fights the resizable layout step 7 built besides. **If this is ever taken up, the thing to take is
the *materials*** — the sheet's own tiles as a texture — **and not the 1996 panel's layout.**

`Opening.bmp` goes the same way and for the same reason, and item 11 is what replaces it: an opening
screen this port designs rather than one it reproduces.


---

## ~~Step 15: `SkipComLev`, `Diff_Setting`, and the other half of `LoadNextLevel`~~ — **done 2026-09-15**

The last of next-steps item 3, and with it the item: two INI keys that were read into `Options` as
comments, the loop in the original that reads them, and one panel over both. `Ctrl+O`.

**What the two keys are.** `[OPT] SkipComLev` is `SkipCL`, command 116, a checkmark on the Options
menu (`ToggleOpt`, `LTANK.C:996`) — *walk past levels I have already beaten*. `[DATA] Diff_Setting`
is the `Difficulty` global, five bits (1 Kids, 2 Easy, 4 Medium, 8 Hard, 16 Deadly), written by
`DiffBox` (command 225, `LTANK_D.C:258`) — *only take me to these ranks*. Both are consumed in one
place, the do/while in `LoadNextLevel` (`LTANK2.C:1010`).

**`Engine.LoadLevel` was already half of that function and said so**, which is what made this small:
its header has read *"the logic-carrying part of LoadNextLevel with DirectLoad = TRUE"* since Phase
2. The filter is the other half, and it went in the **driver** (`Session.Advance`) rather than in
Core — the same rule `Engine.CanRestore` and `Session.AcceptsInput` are: transliterate the function
literally, put the menu's own condition in the driver named after the thing it stands for. It reads
two settings and two files Core has no business knowing about, and keeping it out of `Engine` keeps
`LoadLevel` the literal transliteration every differential is established against.

**Three things in that loop are load-bearing and all three are kept.**

* **`CurRecData.SDiff > 0` makes an unranked level unfilterable.** Plenty of community collections
  are entirely zero-SDiff, and in the original those are unaffected by the mask rather than hidden
  by it. It reads like a missing case until you notice it is the only thing standing between a
  five-bit mask and a `.lvl` it knows nothing about.
* **128 is a sentinel, not a rank.** Skip-completed is implemented *as a difficulty mismatch*: a
  solved level has its `SDiff` replaced with `128`, which no five-bit mask can hold, so one
  condition walks past it. Kept as the C spells it — and it is the reason `Diff_Setting` is masked
  to `0x1F` on the way in, because a hand-edited `255` would match the sentinel and silently undo
  the skip. `options_check.py` pins that.
* **The walk stops at the end rather than wrapping.** `Session.Load` wraps at both ends; this does
  not. A wrap through a filter that matches nothing is an infinite loop, and *"you have finished what
  you asked for"* is a different answer from *"here is level 1 again"* — so `Advance` reports
  `filtered` separately from `eof` and the status line says which. The original says neither, and
  can afford to: it cannot reach the second case without having answered the Difficulty dialog on
  the way in.

**Which calls are filtered is not a choice — it is in the C.** `DirectLoad = FALSE` appears exactly
three times: the flag case (`LTANK.C:655`), command 107 (`:923`) and the `.lvl` on the command line
(`:1439`). Everything else — command 108, the level picker, the RLL start — is a direct load.
`LoadLastLevel` (119) is the same walk backwards, so one `dir` covers both. `S`, `P` and Enter-on-win
go through it here; **`[` and `]` deliberately do not**, and that is what they are now for: a mask
that has hidden the level you actually wanted is otherwise a setting you must go and change before
you can look at it. Both got a row in the `F1` list, because an escape hatch nobody can find is not
one.

### Two masks, not one — the decision worth re-reading

The [*DeadBox and Difficulty dialog*](#the-deadbox-and-the-difficulty-dialog-as-dialogs) entry above
filed 225 as *already built*: the level list's rank chips toggle the same five bits, so what was
left was "persisting that mask and having `LoadNextLevel` read it". **That was one step too far, and
step 15 did not do it.**

`LevelList._diff` is `SearchRec.Diff` — which rows the table shows — and it resets when you open a
different collection, because a filter is about the rows in front of you. `Options.Difficulty` is the
`Difficulty` global — where `S`, `P` and a win take you — and it persists, because it is how someone
wants to play. Same five bits, two jobs, and the original keeps them apart too (the search box's mask
and `[DATA] Diff_Setting` are different values in different structs). Unifying them would mean that
filtering a list to look something up silently changes where the next level comes from — a
consequence nobody would connect to the chip they clicked a minute earlier. The chips look alike
because they *are* alike; the labels are what say which is which, and `OptionsMenu`'s header carries
the argument so the merge is not made later by tidiness.

The related decision: **`LevelList.Chip` is copied into `OptionsMenu` rather than shared.** Fifteen
lines, duplicated on purpose — a shared helper is exactly where somebody would later unify the two
masks by accident. If a third panel wants chips, that is when it moves to `Ui`.

### The 225 popup, dropped

`if (Difficulty == 0) SendMessage(MainH, WM_COMMAND, 225, 0)` is the first line of
`LoadNextLevel`'s body: a fresh install answers a modal before it sees a level. **Zero is not a mask
there either** — it is a "never asked" sentinel and the dialog is the asking — so this port answers
it where it would have been asked, with all five ranks, which is what the shipped `LaserTank.ini`
(`Diff_Setting=31`) does anyway. A `0` left by the 2010 binary reads the same way. The panel is then
somewhere you go rather than somewhere you are sent, which is the rule the DeadBox entry above wrote
down: *a 1996 dialog is not evidence that a question was being asked.*

### The panel

`Ctrl+O` — free in both accelerator tables, `O` bare being 108 — and it takes **226's three
properties**: applies immediately, no OK, the game keeps ticking underneath. That is the opposite of
step 14's name row, which has a Save and a Cancel, and the difference is real rather than an
inconsistency: a name is typed and a toggle is flipped, and the thing you cannot undo by flipping it
back is the one that needs a Cancel. `S` toggles the skip, `1`–`5` toggle ranks, `0` restores all
five — the level list's own `Ctrl+1`..`Ctrl+5` / `Ctrl+0` over its own mask, and the editor's `1 - 5`
over the level's.

**It also settles item 14's key, in the direction that item did not expect.** `Ctrl+O` was listed
there as free and as reading like *options*; it is both, and it is now spent on the one of the four
panels with original command ids behind it. So the merge is `Ctrl+G`, `Ctrl+L` and `Ctrl+N` folding
into `Ctrl+O`, and three `Ctrl` keys come back rather than two.

### The instrument, and why there is one

**What the chips change is a sequence of level numbers**, which no frame contains — and `--press`
needs a window, so the keyboard could not reach it from a gate either. `--check-advance` prints one
`advance stop=N sdiff=D` per landing and ends `eof` or `filtered`; `--advance-dir -1` is 119's walk;
`--skip-completed` and `--difficulty` steer it and persist under `--save-options`. It forces the INI
read-only for the duration, because **an instrument must not write the player's state** and every
stop is a `Load`, which remembers the level when RLL is on — `options_check.py` asserts that too,
after one warm-up run, because `Options`' constructor fills an empty `Graphics_Dir` in on first
launch and that write is not the walk's.

`options_check.py` grew a fifth section: the defaults, `Diff_Setting=0` and `=255`, the round trip
through both keys, seven walks compared against **sequences recomputed in Python from the same
`.lvl` and `.hs` bytes**, and a record whose `SDiff` is zeroed on purpose so the unfilterable case is
built rather than looked for. `chrome_check.py` gained the panel's eight targets — all six chips
named separately, because a row of tags that answers to one rectangle is exactly what that gate is
for. Eleven catalogue keys, eleven languages.

---

## ~~Step 16: `user://`, a typed store, and the INI demoted to an importer~~ — **done 2026-09-15**

Next-steps item 7, closed. Three jobs had collected in `Options.cs` and only one of them was a
settings mechanism:

1. **a fidelity artifact** — `atoi`, the case-sensitive `strcmp(temps, psYes)`, *only a missing key
   gives the default*. Recorded findings about the 2010 binary, and research output rather than
   plumbing;
2. **interop** with a live 2010 install, which is what the preserve-every-other-line write rule
   existed for;
3. **the port's own settings**, already drifting — `[DATA] Language` is invented and says so.

The split is the one `data/language/` made in step 13, and it is a precedent rather than an analogy:
the artifact's reader stays complete and re-runnable while the runtime moves on.

**What is where now.** `Settings.cs` holds a typed record and a store that writes it to
`user://settings.json` through `System.Text.Json` — one class, no parser, a `version` field and a
`Migrate()` hook with nothing to do yet. `Ini` in `Options.cs` keeps `Get` / `GetInt` / `Atoi` and
`IniImport` carries the key names and every semantic step 2 learned; `Options` kept its whole public
surface and changed what is under it. `Options.Open(store, ini, readOnly)` is the first-run rule in
one place: load the store, and only when there is nothing usable there start from the defaults, fold
in a `LaserTank.ini` if one is beside the repo or named by `$LT_INI` / `--ini`, and write the store
out so the next run reads that instead.

**Job 2 retired, deliberately and on the record.** The port no longer writes a `LaserTank.ini` at
all — not the preserve-every-other-line rule, and not step 14's write of the one name into both
`[DATA] Record Author` and `[DATA] Player`. Nothing in this tree has ever been pointed at a real
2010 install, and that possibility was the justification for the most awkward code in the class. The
*reads* survive intact, which is the half that was ever load-bearing: either key still names the
player, long one first, and `Initials` is still cut from the same string, because that is what
reaches a `.hs` record and a `.lpb` header.

**The bug the item carried independently of the format.** `Paths.Ini` wrote to the repo root, which
is the install directory in an exported build — unwritable in every `Program Files` install, and not
a filesystem at all in a browser. `user://` is the one place Godot guarantees is writable on every
target it exports to, and it is per-user rather than per-install. `$LT_SETTINGS` and `--settings`
override it.

**`--ini` grows a sibling rather than changing meaning.** It names the file to *import*; the store
is `<name>.settings.json` beside it. Named after the INI rather than fixed per directory because
`options_check.py` keeps a dozen probe INIs in one scratch directory, and a shared `settings.json`
would have let one case's write decide the next case's defaults.

**Two things fell out of it that were not on the list, and both were latent before the step.**

- **Two gates were passing on a name collision.** `options_check`'s strict-`Yes` loop derived a
  probe filename from the value and three of its five cases collided (`yes`/`Yes`, `Yes!`/`Yes `);
  `sound_check`'s derived one too, and `Yes` and `yes` are one file on a case-insensitive
  filesystem. That cost nothing while every run re-read the INI it had just been handed. With a
  store beside it, the second run of a collided pair reads the *first* run's store and reports its
  answer — two false greens, which is how this was found. Both loops are numbered now.
- **`--edit` and `--save` were missing from `BoardView.Instrument`**, which is the same gap step 9's
  four pointer flags were in and it was found the same way: something started writing where it could
  be seen. `editor_check.py`'s game arm drives the editor with `--editor --edit SCRIPT --save
  --levels <a copy in /tmp>`, none of which counted as an instrument — so six runs of it left the
  player's remembered level pointing at a temp file that no longer exists. The game degrades
  gracefully from that (`Start` checks `File.Exists` before it reopens), which is exactly why nobody
  noticed. Bare `--editor` is deliberately *not* on the list: opening the editor is something a
  player might reasonably ask for on a command line, and driving it is not.

**The gates.** `options_check`'s `ini` arm is two halves now. The importer's half keeps every check
it had, and *"a write keeps every other key"* became the stronger *"the file comes back byte for
byte after a run that changed five settings"*. The store's half is new: the round trip, that the
file is JSON with a `version` in it, that a corrupt store re-imports and rewrites rather than taking
the game down, and — the one a player could otherwise be surprised by — that **the import happens
once**, checked by editing the INI afterwards and watching nothing happen. `strings_check`'s INI arm
went the same way, `sound_check`'s `[OPT] Sound` round trip now asserts the INI was never written,
`chrome_check`'s `fresh()` deletes the store as well as replacing the baseline so every one of its
forty-odd runs re-imports, and `Step6Check`'s three-`Options` round trip is rewritten against the
record. All of it green, plus `atlas`, `tick`, `collections`, `editor`, `roundtrip` and `list`.

**What did not change.** Every atoi/strcmp semantic, every default, `Difficulty`'s deliberate
departure from the original's zero sentinel, and the read-only-instrument rule — which was never
about INI, and is as true of a JSON file.

---

## ~~Step 17: the level history, unbounded~~ — **done 2026-09-15**

Next-steps item 9, closed. Command 118, `VK_BACK` in ACC1 — and the first thing the item had to say
about it is what it is not. **It is not undo.** Undo is 110, it is `U`, it repeats while the key is
held and it has been ported since Phase 2. 118 is a *navigation* history: a stack of level
**numbers**, pushed by `LoadNextLevel` whenever the level actually changes (`LTANK2.C:1045`) and
popped by the key — "take me back to the level I was just on".

**It earns the key, which the item asked to decide before building.** The argument against was step
11: the level list has a filter bar and direct level-number entry, so a back stack is a thing you
reach for only when the list is not one keystroke away. The answer is that they are two different
questions — the list answers *which level* and this answers *the one I was just on*, which is a
level whose number you looked away from. The case it is for is the one the brackets and the filtered
walk create: step off to look at something, come back. One row, one key nothing else wants.

**Unbounded, and the ten it replaces is the whole of the deviation.** `int Backspace[10]` with
`BS_SP` walking it as a ring (`LTANK2.C:94`), and every awkward line of the original's case body is
that ring showing through: the pop zeroes the slot it leaves (`Backspace[BS_SP] = 0; // this is so
we dont loop around`, `LTANK.C:1003`) or a full ring walks itself forever, and the menu item greys
itself by peeking one slot further down to see whether that zero is next. A list has neither
problem. So: a plain `List<int>`, no sentinel, no wrap, and `Count` is the grey.

**The invariant is the original's and it is what makes the code short.** The level on screen is the
top of the stack — `if (Backspace[BS_SP] != CurLevel)` is the push, so a load that lands where it
already is pushes nothing, and the pop's own reload then adds nothing back. One consequence worth
naming: `ReStart` (105) is not a load, so restarting a level forty times leaves the stack alone.

**The push is in `Session.Load` and nowhere else**, which is the original's shape rather than a
choice: the C has one loader and every command that changes the level goes through it, so "the level
actually changed" is a fact one function knows and the six callers do not have to remember. `S`,
`P`, `[`, `]`, the level list, `F2` and a playback all push by going through it.

**And command 108 clears it** — `Backspace[BS_SP] = 0` plus the grey, two lines in the middle of
another case body and the easiest thing in it to leave out. A history of level numbers means nothing
once the collection they index has changed: going "back" from level 3 of one file to level 7 of
another is not a place anyone has been. `Session.OpenDataFile` had carried a note about it since
step 4, which is the reason it was not forgotten. A *failed* 108 restores it with everything else,
because "nothing moved" has to be true of the history too.

**The deviation cannot reach a gate, and that is why it is allowed.** `WM_KEYDOWN` drops every
virtual-key code outside VK 32–40 before `AddKBuff` ever sees it (`LTANK.C:573`) and `VK_BACK` is 8
— so the history cannot enter a keystream, a `.lpb`, an `.hs`, a solver result or any fidelity
harness. It is interface, and the interface is the half this port is allowed to change.

**Gated anyway, because the stack is state no frame draws.** `--check-history` is step 15's
instrument in the same shape: a script of navigation ops (`+` `-` `<` `r` `o` and a bare level
number) printed as one line each — the level, the level `Backspace` would go to next, and the whole
stack. `options_check.py` gained a fifth arm that rebuilds the expected stack in Python from the
same `.lvl` and `.hs` bytes, walking with the same mask `advance` walks, so the two halves of the
item are held to one model. Nine cases: the push is one per change, a restart is not a level, back
past the first is refused rather than wrapped, a direct load pushes once, 108 clears it, a stack
deeper than the original's ten, the mask decides what is pushed, a failed 108 keeps the history, and
the instrument writes nothing.

**The label is the original's own.** `Last Level Playe&d` (`lt32l_us.inc:25`), which nine of the ten
2007 catalogues already translate, so `keys.lastPlayed` agrees with them rather than inventing a
sentence in eleven languages. The keycap is spelled `backspace` and not the menu's `BkSp`: this port
writes `space` and `tab` in full and there is no menu column to fit.
