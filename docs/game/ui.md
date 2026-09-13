# The game's own UI, and i18n as built

The port's interface: the original's accelerator tables read rather than invented, the panels that
stand in for its dialogs, the editor as a mode of the window, the instruments that review any of it
without a human at the screen, and the ten translations. The facts underneath the drawing are in
[`rendering.md`](rendering.md); what the redesign did and in what order is in
[`history.md`](history.md); what is still missing is in [`next-steps.md`](next-steps.md).

---

## The visual language

One file, [`Ui.cs`](../../src/LaserTank.Game/Ui.cs), and **nothing in it is a transliteration** — the
board, the laser, the sprites and the grid are pixel-faithful and everything around them is designed.
It is one file because the port draws its whole interface in immediate mode, which has no theme, no
stylesheet and no cascade: the only way a panel here can look like a panel there is for both to ask
the same file. Six files draw this interface and not one of them names a colour, a radius or a face.

Step 10 re-decided all three. What it replaced and why is in [*Finished*](history.md); what it *is*:

* **Ember on warm black, one dominant hue.** Amber carries the wordmark, the rail, the section
  labels, the keycap glyphs, the live numerals and every modal's top edge — it is the dominant
  colour, not an accent, and a frame of this interface with no amber in it is a bug. The hot
  vermilion is the sharp accent and is spent on recording and death only. Every neutral is warm.
  **The hue was chosen by measurement**: the four packs' grounds are `#949410`, `#109494`, `#60C000`
  and `#187B00`, all in the yellow-green-cyan arc, so amber is the complement of every one of them
  and appears in no sheet as a field colour.
* **One radius, 2 px, enforced at the primitive.** `Ui.Rad` compresses whatever a caller asks for,
  so the radius cannot drift back one call site at a time. Drop shadows are for true modals and
  nothing else.
* **Two faces, shipped rather than named** — Archivo for the wordmark and the level name, IBM Plex
  Mono for everything else, both OFL with their licences in the tree. See
  [`fonts/README.md`](../../src/LaserTank.Game/fonts/README.md) for why the body of this interface
  is a monospace: the content is fixed-pitch, and so is the one gate that reads it.
* **Hierarchy by size and space, not by boxes.** The info column is laid out on a **rail** — one
  vertical hairline down its left that every group hangs off, with the level's own span lit amber —
  and is read from both ends: what the level *is* flows down from the top, what a player can *do* is
  anchored to the bottom. The counters are the largest thing in it and have nothing drawn around
  them.

**Two layout rules this pass leaves behind.** A string gets **measured** width, never reserved width
— two captions in `LevelList` had been given a flat `Ui.Px(280)` and a panel's inner width, which
was enough for a proportional sans and not for a monospace, and both clipped mid-word. And when the
measurement says no, the right answer is to **shed a whole clause or a whole column**, ranked in
advance, rather than to let `DrawString` cut where it happens to run out: `LevelList.Fit` already
dropped the author column before shrinking the type, and the header and footer legends now do the
same thing with their clauses.

---

## The game's own UI, as it stands

Keys are **the original's accelerator tables**, read rather than invented — `ACC1` at
`lt32l_us.inc:120` and `ACC2` (the editor's) at `:150`. That table carries a comment which is really
a design rule — *"DONT use Keys that can be entered in the Author & Level Name field"* — and it is
about focus: while an edit control has the caret the accelerators must not fire. In the editor, Tab
moves in and out of the three text fields, and while one has focus every letter goes into it.

Arrows move, space fires. Everything else: `R` restart (105), `F2` new game (101), `U` undo (110),
`Ctrl+C`/`Ctrl+V` save/restore position (111/112), `L` levels **and both high-score lists**
(106 + 113 + 906 — see the one table below), `O` collections (108),
`S`/`P` next/previous level (107/119), `N` sound (102), `A` animation (104),
`H` hint (301), `F1` the key list (907, and 903 in the editor),
`Ctrl+G` graphics dialog (226), `F5`/`F6`/`F7`/`F4` record / save recording / playback / replay
(123/117/114/124), `F8` auto-record, `F9` editor (201), `Ctrl+L` language picker (invented), `Z`
board-size preset, `I` interpolation, `C` the A1–P16 grid, `Esc` quit — **which asks first**.
`[` and `]` are ours.

**`V` and `G` are unbound and free**, which is the one thing the merge was *for*: ACC1 has no spare
letters, every future command needs one, and 113 and 906 were two keys spent on two renderings of
the list `L` already opens. `Ctrl+V` (112) and `Ctrl+G` (226) are untouched — different
accelerators, still bound, still in the table.

**The list is also `PlayKeys` and `EditorKeys` in `BoardView.cs`**, which is what `F1` draws, so
the overlay and this paragraph are two renderings of one table rather than two lists to keep in
step. A binding added to the router and not to the table is a binding no player will find.

**Every label in that list is also a button** (step 9 — see [*Finished*](history.md)). The pills in the top bar
turn off the state they name, the collection beside them opens 108, the blocks in the column open the
panels they summarise, the five key rows at the foot of it do what they say, the rows of all four
list panels select and then commit on a second click, the F1 overlay's thirty-odd rows *press the
key they draw*, and every panel has a close button and closes on a click outside. A click is routed
through `BoardView.Press`, which is the accelerator table as a function — so a chrome click is the
key, going to whichever table is live (ACC1 while playing, ACC2 in the editor), and a binding added
to the router is reachable from the pointer or from neither.

**The window is resizable and the board is the thing that resizes** (step 7 — see
[*Finished*](history.md)). The
layout is measured from the window every frame: the board takes the largest whole-pixel cell that
fits the square it is left, aspect locked, with the coordinate gutter and the label type scaled off
that cell. `Z` and `[SCREEN] Size` are still the original's three sizes and still persist, but what
they now do is **snap the window** so the board lands on exactly 24, 32 or 40 px cells; the next
drag of the window frame frees it again. Below ~660 px of width the info column moves under the
board and becomes a strip. `--window WxH` is the instrument for reviewing any of that.

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

**The three list dialogs are one dialog three times — and one table since step 8.** `LoadBox`
(106), `HSList` (113) and `GHSList` (906) each build one string per level with a `sprintf` whose
padding and truncation are observable, prefix it with a difficulty digit that `DrawLevels` colours
by, and hand it to an owner-drawn listbox; all three seek to `CurLevel - 1` and load on Enter. They
differ in one thing only: **which columns of the same row they print**. LoadBox prints the name and
the author, HSList the name and your score, GHSList the name, the posted best and your score again.

So step 8 prints all of them at once, in one table — not three keys, and **not three tabs either**,
which was the first attempt and was the same three lists with two of them hidden. One row per level:
number, name, author, the posted best's moves / shots / initials, then yours, with a hairline
between the groups and the original's own `**` and `>` marking the rows where you beat the posted
best. `L` opens it and that is the whole of it. (**Step 11 replaced those two markers** with a
three-rank mark column at the row's left edge — see below.)

**`V` and `G` are unbound and free**, which is what the merge was *for*: ACC1 has no spare letters,
every future command needs one, and 113 and 906 were two of them spent on two renderings of the
list `L` already opened.

Four things follow, and all four are written down in `LevelList`'s header beside the C they depart
from:

* **The table is the level file's, so it has a row per level**, blank score cells and all. The two
  score dialogs stop where their *score* file stops (`while (BytesMoved == sizeof(THSREC))` reads a
  score record and then a level record), so a 2,030-level collection with a 79-record `.hs` gave a
  79-row list. A table that did that would hide level 80 onwards from the only list there is.
* **`BuildRows` is now only the record.** What is drawn is `BuildTable`; what `list_check.py` diffs
  against Python byte for byte is still `BuildRows`, the original's three `sprintf` formats. **The
  field widths are shared** — the table's name cell is `%-30.30s`, its score cells `%5d` and `%4s`
  — so the merge changed the arrangement of the columns and not one of them. The author is the one
  cell with a width of the port's own choosing (20): the original prints it `%s` at the end of a row
  with nothing after it, and here it has two score groups after it.
* **The clock stops for the panel, and this is the first observable behaviour the port drops on
  purpose.** 106 stops it (`x = Game_On; GameOn(FALSE); DialogBox(...)`, `LTANK.C:906`) and 113/906
  do not, so a tank in the open could die while you read your scores and could not while you picked
  a level. One panel cannot be both, and it is `L` — 106 — that opens this one. What is lost is
  being killed by a laser you left in flight while reading a score list.
* **The table has headers**, which the original's listboxes never had, placed at *the character
  columns `BuildTable` lands its cells at*. Two lines: the column names, and above them `posted
  best` and `yours` over the two groups of three. Six columns of bare numbers is what the global
  list was, and nothing on screen said which three were whose.

**Two layout facts that cost a debugging session each, in a table drawn by character column.** The
column rules are placed in glyph units, so both of these showed up as a rule drifting through the
text it divides. First, **the header lines must be set at the rows' own size** — one point smaller
and `moves` sits a finger to the left of the moves it names; faint, not small, is what makes a
header. Second, **`Font.GetStringSize("0")` is that glyph's width, not the advance the next glyph is
placed at**: 0.15 px apart here, which over the 77 columns to the right-hand rule is a character and
a half. `Advance` measures a 32-character run and divides.

**The panel sizes itself to the table and drops a column before it shrinks past legibility.** A
table's columns are only worth having if they are all on screen, so the fit is searched: the full
table at 12 px, then down to 10.5, then the table *without the author* — flavour rather than score
— and only then is anything allowed to clip. At an 860 px window the whole table fits; at 560 the
author goes.

**And since step 11 it is a list you can use at the size the corpus actually is.** The table was
built for `LaserTank.lvl`'s 2,030 rows and could only show one screenful of them. Five changes, and
the reason they are worth reading as a group is that **four turned out to be the original's own
behaviour** — the port had invented past the C in places nobody had gone back and read. The full
account is in [*Finished*](history.md), step 11; the short form:

* **Only `Esc` closes it.** `TransListKey` (`LTANK_D.C:87`) answers Home / Up / Down / End / PgUp /
  PgDn and `VK_ESCAPE` and returns **-2 — no action — for everything else**. The port's "any other
  key closes" was a step-7 convenience with no warrant here, and a text field makes it impossible
  anyway. Space went with it; the original's Enter is `WM_COMMAND` id 1. **Every other panel keeps
  "any other key"**, because none of them has anywhere for a letter to go.
* **A filter bar: the Search sub-dialog (`SearchBox`, `LTANK_D.C:197`) inlined.** A substring field,
  a Title/Author pair, the five-bit difficulty mask and "only unsolved" — in the panel rather than in
  a dialog over it, because a filter you cannot see while you read the list is a filter you forget is
  on. The C's rules, including the one that would never have been guessed: **an unrated level is
  promoted to 255 before the mask test** (`LTANK_D.C:414`), so it matches whatever is ticked.
  Type to filter; `Tab` swaps title/author; `Ctrl+1`–`5` toggle the ranks, `Ctrl+0` restores them
  all, `Ctrl+U` is unsolved-only — and every one of those is a chip you can click. A **digit query
  also matches the level number**, which is `ID_LOADLEV_02`, the original's direct-entry box folded
  into the one field. The filter survives the panel closing and is cleared by a change of collection.
* **A mark column**: `*` solved, `**` matched the posted best, `***` beat it, at the row's left edge
  in amber rather than in the row's difficulty tint. It replaces the original's `**` and `>`, which
  were one bit (`BHS`) drawn twice — and the four characters they cost paid for the gutters below.
  **The predicate is narrower than `BHS` on purpose**: `LevelFile.Beats` counts an absent posted best
  as beaten, which would three-star every solved row of any collection with no `.ghs`.
* **Gutters, and the two group rules are floats.** They were `cell - 1` drawn at `+ 0.5`, half a
  glyph from the cells either side, and the commonest `who` in the corpus is four characters wide and
  touched one. A column is a position, so its divider has one too.
* **A scrollbar — the one target in this interface that is dragged.** `Hits` carries the fact of a
  click and not the pointer's position, which is most of why it can be forty lines, so the thumb gets
  a latch in `BoardView` and `MouseMotion` feeds `LevelList.DragTo`. It moves the viewport and pulls
  the cursor after it: **one position, not two**, which is the same rule the wheel follows from the
  other end.

**`--type STRING` is the filter field's instrument**, and it exists because neither of the other two
could reach it: `--press` goes through the accelerator table and a text field is not an accelerator,
`--click` reaches only the hit list and the field is a swallow because it is always focused. It types
through the same `Key(InputEventKey)` the router calls and prints
`type <s> list=True rows=N of=M filtering=B q=Q`; `chrome_check.py` drives five queries through it.
**`\b` and `\t` are two characters on that command line, not control codes** — a real backspace does
not survive the shell, the gate's argument quoting and Godot's command-line split.


**`Esc` asks before it quits, and that prompt is the port's first modal question.** The original
has no quit accelerator at all — its ways out are the window's close box and the File menu's Exit
(103), both two deliberate acts with a title bar or a menu in between. `Esc` is this port's, added
because a keyboard-driven game wants a keyboard way out, and it is also the key that closes every
panel here: one press too many after closing a list and the session was gone, mid-level. So the key
raises a prompt and a *different* key confirms — `Enter` or `Y` quits, every other key including
`Esc` keeps playing, so a double-tap of one key cannot quit and a mistaken press always lands on
the safe answer. It asks unconditionally, on a won board and an untouched one too: "only when there
is something to lose" needs the game to know what a player would call a loss, and being wrong about
that costs the session against one keystroke. The prompt freezes the board while it is up, for a
plainer reason than any dialog above — a tank that dies while the player decides whether to leave
was killed by the interface. `--panel quit` is how it is reviewed.

**It is also the shape the port's remaining modal prompts want** — the editor's "save changes?",
the `RecordBox`/`HSBox` name fields, the Difficulty dialog (225), the DeadBox as a dialog. Step 7
built the look; this is the first one built, and what the rest need past it is a text field and a
third button.

**Playback needed no new rules** — `PBOpen`, `PlayBack`, `PBHold`, `Speed` and `SlowPB` have been
read by `Engine.Tick` since Phase 2, so the panel is four buttons wired to five fields. One line of
`PBWindow` has to live outside the engine: in Single Step the original's tick posts `ID_PLAYBOX_02`
back at the dialog from *inside* the tick, and an engine with no dialog cannot, so
`Playback.AfterTick` pauses instead. Measured on the corpus's first recording: Fast 372 ticks
(identical to the oracle), Slow 1,136, Step 372.

**The editor is a mode of the window, not a dialog over it**, because that is what the original is:
command 201 swaps the menu bar and the accelerator table (`LTANK.C:1446` picks `hAccelTable2` when
`EditorOn`), hides the nine buttons and repaints the control panel as a palette. **Step 7 put the
palette where the original's is**: there was no control panel here to repaint, so until then the
window *widened* by a palette's width on `F9` and narrowed again on the way out — the one thing in
this UI that moved the window out from under the player. The redesign gave the window a column of
its own, so the editor now takes that column over, the window is left alone, and the palette's slot
size is the column's rather than the board's. Three things the C does that a reasonable reading gets
wrong:

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
exits, and `--menu` / `--panel levels|scores|global|collections|playback|help|hint|quit` / `--editor`
open the thing first. **Step 9 added four more, and they need a window rather than avoiding one**:
`--dump-hits` prints every clickable rectangle this frame registered, with its name; `--click X,Y`
pushes a synthetic press and release through `MouseButton` — the whole arm, guards included — and
prints what it landed on and what changed; `--press key:U` does the same through the keyboard's
route, which is what makes the two comparable; and `--hover X,Y` parks the pointer so a screenshot
can show a hot target. `X,Y,r` is the right button and `X,Y,u` / `X,Y,d` the wheel. `--window WxH` sets the window to an arbitrary size and frees the preset,
which is how the responsive layout is reviewed: there is no fixed board size to screenshot any
more, so "what does it look like at that size" needed an instrument like every other panel.
Every path after `--` must be **absolute** — see [*Environment notes*](repo.md#environment-notes).

```bash
GODOT=$(echo ~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_*/*/Godot_v4.7.2-stable_mono_win64_console.exe)
"$GODOT" --path src/LaserTank.Game                                     # play it
"$GODOT" --path src/LaserTank.Game -- --shot D:/abs/out.png --menu --pack 3 --zoom 40 --level 7
"$GODOT" --path src/LaserTank.Game -- --panel levels --level 900 --shot D:/abs/out.png
"$GODOT" --path src/LaserTank.Game -- --panel help --window 520x560 --shot D:/abs/out.png
"$GODOT" --headless --path src/LaserTank.Game -- --play --lpb D:/abs/x.lpb
"$GODOT" --headless --path src/LaserTank.Game -- --play --script "uufz.zllZ" --level 7 --out DIR
"$GODOT" --headless --path src/LaserTank.Game -- --editor --edit '<06l22' --save \
         --levels D:/abs/COPY.lvl --level 7
"$GODOT" --headless --path src/LaserTank.Game -- --ini D:/tmp/x.ini --check-options
"$GODOT" --headless --path src/LaserTank.Game -- --check-deadbox --level 39 \
         --levels D:/abs/data/levels/LaserTank.lvl        # the DeadBox's modality
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

**The codes are ISO, not the installer's directory names.** `en.json`, `fr.json`, `de.json`,
`nl.json`, `pt.json`, `es.json`, `sv.json`, `hr.json`, `zh-Hans.json`, `zh-Hant.json`, and the same
string in each file's `code`, in `Language.BaseCode` (`en`) and in `[DATA] Language`. The 2007
directory names — `US`, `Du`, `Sp`, `Sw`, and the `Cs`/`Ct` that turned out to be Chinese rather
than Czech — survive in exactly one place, the left column of `LANGUAGES` in
`tools/convert_language.py`, because `original/` is frozen and that is where the two naming systems have
to meet. The table is reproduced in [*Finished*](history.md), under the ISO language codes, with the
codepages.

**The display name is the port's, the banner is kept as data.** `LANGUAGES` assigns `name` (the
English name of the language, because `ThemeDB.FallbackFont` has no CJK glyphs); each file also
carries `sourceDir`, `sourceEncoding` and `sourceName` — the translator's own banner line verbatim,
completeness claim and all. `lang_check.check_structure` asserts all six header fields, and
`sourceName` against the `.dat`'s banner, which is the one string in the file the round trip below
cannot reach: it lives on a `#` line the original's own loader skips.

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
is in [*Finished*](history.md), under the ISO language codes, along with the surprise:
`zh-Hans`/`zh-Hant` (`Setups/Cs`, `Setups/Ct`) are Chinese, not Czech.

**Two findings worth keeping in view.**

*The `while(!feof(fd))` bug is real and the port is free of it by construction.* `InitLanguage`'s
loop reads with `fgets`, then unconditionally chops the last character with
`szTmp[strlen(szTmp)-1] = 0`. On the iteration after the final line `fgets` returns NULL and — per
C99 7.19.7.2, so this is *defined*, not luck — leaves the buffer unchanged, so **the last
non-comment line is applied twice, the second time one character shorter, into the next slot.** In
the English file that lands the yahoo-group line into `about[12]` with its trailing newline eaten,
which is why the About box shows it twice. It is a quirk of a *loader the port does not have*, so
there is nothing to transliterate it into — which is why it is here and not in
[*Quirk hazards*](quirks.md).

*Four of the ten files are labelled 90% or less, and the fallback still never fires.* A translator
who skipped a line copied the English one rather than leaving it blank, and the only section any file
actually stops short in is `about` — which does not fall back, because borrowing the English tail
would put two languages in one paragraph. So the shipped corpus resolves **zero** strings through
`Language.Load`'s fill-from-base branch. The fallback is kept anyway (a future or hand-edited file
needs it, and the alternative is `[ID_WINBOX_03]` on a label) and `lang_check.check_fallback` builds
the partial file the corpus does not contain: one key blanked, one removed outright, one section
truncated, and a translated key that must *not* be overwritten. Both ways of being absent are
separate lines of code and both are tested.

