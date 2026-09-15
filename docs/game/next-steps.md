# Next steps — the open items in full

Nothing is blocked. The order and the short form are in
[`PROGRESS.md`](../../PROGRESS.md#next-steps); this file carries each item in full — what it is
waiting on and what it would cost.

**Open items only.** What is done, and what was deliberately decided against, is in
[`history.md`](history.md) and is not repeated here. **Items keep their numbers** because the other
files refer to them by number, so a number retires when its item closes rather than being reused —
which is why this file starts at 3.

---

## 3. `SkipComLev` and `Diff_Setting`, and the `LoadNextLevel` port they want

**What this item used to be** was *the rest of the original that is still missing* — a list of
everything named as left out at the time rather than forgotten. That list is gone: it was read
through on 2026-09-15 and every entry on it either closed, was declined, or became an item of its
own, and what is left is two INI keys and one function. **The editor's share was always item 8**,
and still is.

Where the list went, so that nothing has to be re-derived from the fact that it is no longer here:

| what it was | where it is now |
|---|---|
| `Backspace[]`'s ten-level history (118) | **item 9**, and unbounded rather than ten |
| Resume Recording (125) | **item 10**, with the two file dialogs it shares a problem with |
| "View Opening Screen" (`ID_GRAPHBOX_08`) | **item 11** — a new screen, not `Opening.bmp` |
| `LaserTank.hlp` / WinHelp (902–905) | **item 12** — the old help's content, rewritten |
| Print (126) | [*Finished*](history.md), decided against |
| "Change Directory" (`ID_GRAPHBOX_09`) | [*Finished*](history.md), decided against — `--gfx-dir` |
| per-language `Control.bmp` / `Opening.bmp` | [*Finished*](history.md), decided against — and this closes item 4's bullet too |

**And there is no modal work left in it**, which is what four separate entries used to claim.
The DeadBox is the status line and the Difficulty dialog (225) is the rank chips, both
[*Finished*](history.md); the `RecordBox` and `HSBox` name prompts turned out to be *one* settings
row about one person, which step 14 built on `Ctrl+N`. The one box still worth building is the
editor's, and it is item 8's.

**Both of these want a `LoadNextLevel` port rather than a menu.** `[OPT] SkipComLev` and
`[DATA] Diff_Setting` are read into `Options` as comments only. **`Diff_Setting` has no UI work
left in it at all**: the five-bit mask the Difficulty dialog would have written is the mask the
level list's rank chips already toggle (`Ctrl+1`..`Ctrl+5`, `Ctrl+0` to clear), so what remains is
persisting that mask and having `LoadNextLevel` read it — engine and settings, no dialog.

**Neither wants a key, which is what is unusual about them.** Everything else in this port that
was a menu item became a row in `PlayKeys` or `EditorKeys`, where the row is both the documentation
and the click target — that is the route item 2 was declined in favour of. These two are settings
that `LoadNextLevel` *reads*, and the surface they want is item 14's options dialog, not a keycap.

**And this is the item that blocks item 7.** The typed settings store cannot be written until these
two land, because doing it first means writing the migration twice.

## 4. The UI redesign, third pass

The first pass stopped at the chrome and steps 9, 10 and 11 closed the three biggest things it
left ([*Finished*](history.md)). **The third of those was never on this list**: the one table step 8
built was unusable at the size the corpus actually is, and it took a filter bar, a mark column, a
scrollbar and column gutters to fix — none of which was visible until someone played it. Worth
remembering before the bullets below are treated as the whole of what is left.

* **Web.** Nothing here needs a platform branch and there is no stretch mode to fight, so an HTML5
  export should draw correctly today. It has not been tried. **Step 10 removed the one thing that
  would have drawn wrong**: the chrome asked for `Segoe UI` / `Inter` / `SF Pro` by name and
  `SystemFont` fell through to Godot's own *proportional* face in a browser, so the level list —
  whose column rules are placed in glyph units off the original's `sprintf` padding — was
  fixed-pitch everywhere except the one target this bullet is about. Both faces are shipped now.
  `Paths` and the `.hs`/`.ini` writes are what will need work, not the drawing. **Step 13 left
  one thing to watch here**: the eleven column heads `strings_check.WIDTHS` caps are capped in
  *characters*, which is only a width at all while the face is fixed-pitch.
* **Motion.** There is none, and three places want it now: the status line, which replaces its
  content with no transition; the win state, which is a colour change on a line of text; and, since
  step 9, the press itself — a target that highlights on hover but does not move under a click says
  nothing to a finger, which has no hover. The tick is 20 Hz and `_Process` already redraws every
  frame, so a tween has somewhere to live.
* **The graphics packs' own chrome — closed.** `Control.bmp` and `Opening.bmp` were this
  bullet and they are [*Finished*](history.md), decided against a second time and for good on
  2026-09-15. The reasoning is kept there rather than here because it is the kind that gets
  re-derived: the panel is a genuinely unmistakable design language and would have been the
  *safest* fix for "this looks generated", and it was turned down because it makes the chrome a
  costume. **What replaces it is item 11**, an opening screen that is this port's own.
* **A drag on the board, and a two-finger gesture.** Step 9 gave the chrome the pointer and left the
  *board* exactly as the original has it: a click is a move order and `WM_MOUSEMOVE` outside the
  editor does nothing at all, so there is no swipe-to-move and no pinch-to-zoom. Both would be this
  port's own rather than the original's, and both are the kind of thing that has to be decided
  rather than added — a swipe over a board whose clicks are already a move order is two gestures
  competing for one surface.

## 5. More fuzzing, indefinitely

`fuzz.py` can keep running on new seeds and on the **12 collections its first campaign never
touched**. `undo_check.py`, `mouse_check.py` and `editor_check.py` are three more campaigns of the
same kind against the same oracle. All four are worth leaving running.

## 6. The solver

The larger unfinished half of the project and a goal in its own right: 11.3% of a 4,185-level sample
against a goal of all 20,914. It runs on the other machine now, so treat that number as a
last-known value. See `SOLVER.md`.

## 7. Settings: `user://`, a typed store, and the INI demoted to an importer

**Waiting on item 3** for the *store*. `[OPT] SkipComLev` and
`[DATA] Diff_Setting` are still to land — both are read into `Options` as comments today — so the
original's keyspace is not finished being filled in, and splitting the store before they arrive
means writing the migration mapping twice.

**The panel half is no longer waiting on anything, because step 14 built it.** Item 3's two name
prompts became one settings row (`NameMenu`, `Ctrl+N`), written against `Options` as it stands, and
it will migrate with everything else. What that settles for this item is the *shape* a second
setting takes — a panel over the board, a `TextField` for anything typed, a Save and a Cancel — and
the one property worth keeping when the store lands: **the panel writes on Save and not before**, so
opening it changes no file. It is one row rather than a settings screen; the day there are three,
whether they stay one panel per setting or become a list is a question this item can answer with
something already on screen.

Three separate jobs are tangled in `Ini` (`src/LaserTank.Game/Options.cs`), and only one of them is
a settings mechanism:

1. **A fidelity artifact.** `Atoi`, the case-sensitive `strcmp(temps, psYes)` test, *only a missing
   key gives the default* — these are recorded findings about the 2010 binary, pinned by
   `options_check.py`'s `ini` arm and six checks in `strings_check.py` — five of them the INI
   checks `lang_check.py` used to carry, which step 13 moved across intact before deleting it.
   Research output, not plumbing, and worth keeping whatever happens to the rest.
2. **Interop with the 2010 binary.** The preserve-every-other-line rule is load-bearing only
   because the port can share one file with the original and must not reset the dozen keys it knows
   nothing about (`PosX`, `Diff_Setting`, `Player`). This is the one job that *requires* the
   on-disk format to be INI, and it is worth asking out loud whether anyone will ever point this at
   a real 2010 install — it is the justification for the most awkward code in the class.
3. **The port's own settings**, which are already drifting away from the other two. `[DATA]
   Language` is invented and says so at `PsLang`; steps 7, 10 and 11 added window geometry, a
   theme and a filter state that will never have an original key name.

**There is also a real bug here, and it is independent of the format.** `Paths.Ini` writes to the
repo root, and there is no `user://` anywhere in the tree. That is fine for a dev checkout and
breaks the moment an exported build lands somewhere unwritable — which is every `Program Files`
install and the web export item 4 is circling. `user://` is where this belongs regardless of what
is decided below.

**The shape, which is the language files' shape** — and step 13 has now built it, so this is a
precedent rather than an analogy. `data/language/` is the port's own catalogue and
`tools/convert_language.py` is a decoder that writes nothing and refuses to write there: the
artifact's reader stays complete and re-runnable while the runtime moves on. Same split here:

- `Ini` stays, demoted to a **one-way importer**: on first run, read a `LaserTank.ini` if one is
  beside the repo or named by `$LT_INI`, fold it into the settings object, and never write it
  again. Every atoi/strcmp semantic and the gate that pins it survive untouched.
- The port's own settings become a typed record at `user://settings.json` through
  `System.Text.Json`, with a `version` field. One class, no parser, and room for the chrome
  settings the original never had.
- **The read-only-instrument rule survives unchanged**, because it was never about INI: an
  instrument must not write the player's state (`PROGRESS.md`, *Rules learned the hard way*), and
  that is as true of a JSON file. `--ini` either points at the new file or grows a sibling.

**Not Godot's `ConfigFile`**: it is INI-shaped anyway, so the trade is 150 gate-pinned lines we own
for an engine class, and it does not preserve foreign lines. **Not a `Resource`/`.tres`**: it binds
the save format to engine classes and is miserable to diff.

**What it costs:** `options_check.py`'s `ini` arm and `strings_check.py`'s INI arm both need a
second half for the new store (the existing halves stay, pointed at the importer),
`chrome_check.py`'s self-written baseline moves with it, and `Step6Check`'s three-`Options`
round trip is rewritten against the typed record.

## 8. The editor

The editor's commands are ported and gated — `--edit` scripts against the oracle's own `ChangeGO`,
and `editor_check.py`'s 3,000 of them plus the `.lvl` writer's byte-for-byte round trip. **What is
missing is the chrome around them** — four commands, none of which touches the game's own window,
which is why they are an item rather than four more bullets in item 3. **One of the four is the
port's last modal prompt** — item 3 used to share that dependency and no longer does, so it lands
here or not at all.

**Blocked on a file dialog:** Load Level (602) and Save As (606). 108 was the third of these and is
done — and the way it was done is the model for both: a list of what is *in the repo* rather than a
native file dialog. See [*Finished*](history.md), the collection picker. 602 wants exactly that
list plus a level inside the chosen collection, which is `LevelList` and already built; 606 wants
somewhere to type a name, and **that name is the one item 3's two could not be**: a filename is
chosen at the moment of the act, so it cannot move to a settings row. It is still not a bare box —
per the 108 model it is a name row on the same picker 602 builds.

**The one modal prompt left in the port:** the "save changes?" question on leaving the editor —
`Modified` is tracked and shown, there is just no box. It is `DrawQuitAsk`'s case exactly, an
unanswerable-later question in front of an irreversible act, and what it needs past that box is a
third button, because *Cancel* is a third answer and quitting has only two. **Item 3 was carrying
this as a shared dependency and no longer is**: nothing else in the port is waiting on it, so it is
small, local, and the whole of the modal work that is left.

**And `LoadTID` *as* a dialog is the one to keep arguing against**: the tunnel id is a mode here,
cycled with `T`, because a modal prompt per painted cell is worse than a mode, and that reasoning
does not weaken when the panel exists.

## 9. The level history, unbounded

`Backspace[]` (command 118, `VK_BACK` in ACC1) — and the first thing to know about it is what it is
not. **It is not undo.** Undo is command 110, it is bound to `U`, it repeats while the key is held
and it has been ported since Phase 2. 118 is a *navigation* history: a stack of level **numbers**,
pushed by `LoadNextLevel` whenever the level actually changes (`LTANK2.C:1045`) and popped by
`Backspace` — "take me back to the level I was just on".

**Ten, and why it is ten.** `int Backspace[10]` with `BS_SP` walking it as a ring
(`LTANK2.C:94`). The bound is not a design decision about how far back a player should get to go;
it is a 1996 fixed array, and the ring is why the pop has to leave a zero behind it
(`Backspace[BS_SP] = 0; // this is so we dont loop around`, `LTANK.C:1003`) and why the menu item
greys itself out by peeking one slot further down. **So: unbounded here**, a plain list, no
sentinels and no wrap — the behaviour the ten slots were approximating.

**That is a deviation and it is a safe one.** Letters and `VK_BACK` never enter a keystream:
`WM_KEYDOWN` drops everything outside VK 32–40 before `AddKBuff` ever sees it (`LTANK.C:573`), so
the history cannot reach a `.lpb`, an `.hs`, a solver result or any fidelity gate. It is interface,
and the interface is the half this port is allowed to change.

**The one thing that must not be forgotten** is already written down where it will be read:
`Session.OpenDataFile` (`Session.cs:261`) carries a note that command 108 clears this stack
(`Backspace[BS_SP] = 0; EnableMenuItem(MMenu, 118, MF_GRAYED)`), because a history of level
*numbers* means nothing once the collection they index has changed. Unbounded or not, that line is
the one that has to survive the port.

**Worth deciding first: whether it still earns a key.** Step 11 gave the level list a filter bar and
direct level-number entry, and `[`/`]` already walk the collection — so the question this item
should answer before it is built is whether a back stack is a thing a player reaches for when the
list is one keystroke away. It is cheap either way; that is not the same as being wanted.

## 10. Recording: Resume Recording, and the two file dialogs

Split out of item 3 because it was never one command. **Command 125, Resume Recording**
(`LTANK.C:1058`) opens a `.lpb`, replays the whole keystream with no panel on screen, and then
starts recording from the end of it — `VHSPlayback()` followed by a posted 123. It is how you
recover a run you stopped halfway through.

**Most of the machinery is already here, and it was built for this.** `Recorder.cs:127` splits
`PanelUp` from `Open` for exactly this case — *"a playback can be open with the panel hidden
(command 125, Resume Recording, runs the whole keystream with no dialog at all), so these are two
flags"* — and `Close` already rewinds `RB_TOS` to `Game.RecP` *in case we stop short*, which is the
state 125 resumes from. What is missing is the file picker.

**Which is the real content of this item**, and it is shared with two commands that are already
bound:

* **114 (`F7`), Open Playback.** There is no dialog, so `Recorder.Candidates` guesses three paths in
  order — `out/recordings/`, beside the `.lvl`, and `data/demos/<collection>/00001.lpb` — and says
  so in its own comment: *a real file dialog makes all three moot; until there is one, a list beats
  a single guess*.
* **117 (`F6`), Save Recording.** Writes to `out/recordings/` because that is where it has written
  since step 1, not because anyone chose it.

**The model is the collection picker**, the same as item 8's two: a list of what is in the repo
rather than a native dialog. All three commands then want one list, and the one that needs a name
typed into it (117, and 606 in the editor) wants the name row item 8 describes.

**One conflict to settle before this lands: `F8`.** The original gives `F8` to 125
(`lt32l_us.inc:131`) and this port has already spent it on 115, Auto Record. So Resume Recording
arrives to find its own accelerator taken by a command the original put on a menu. It is item 13's
problem as much as this one's — and note that `PlayKeys` labelled the `F8` row `// 125` while the
handler is 115, which was the comment being right about the original and wrong about the port.

## 11. The opening screen

`ID_GRAPHBOX_08`, "View Opening Screen" — and what it actually is, is worth having straight,
because it is two surfaces and one of them is a key this port has already spent.

`QHELP` is the flag. It is set from the Graphics dialog's checkbox as a persistent *show this
instead of the board*, it is set by **command 907** for as long as a dialog is up, and the board's
paint path also draws the screen whenever `CurLevel == 0` (`LTANK.C:478`) — before any level has
been loaded. The drawing itself is `Opening.bmp`, stretched over the board.

**The bitmap is not what is wanted.** Per-language `Opening.bmp` / `Control.bmp` are declined
([*Finished*](history.md), and see item 4). This item is a new screen, on the port's own terms.

**The route is undecided, and the obvious one is taken.** `VK_F1` is command 907 in ACC1
(`lt32l_us.inc:145`) and this port gave `F1` to the key list instead — the answer it could actually
give. So the opening screen cannot simply reclaim 907's key. **The current thinking is `Esc`**: the
quit question grows into a screen that the opening screen is part of, which also gives `Esc` a job
better than one modal. Not decided.

**And `CurLevel == 0` is the other half**, and the easier one: this port always has a level loaded,
so the state the original drew the screen in does not occur here. Whether a fresh launch should
land on the opening screen rather than on a board is a genuine question about what the port opens
into, and item 12's help and item 14's options are both things such a screen would route to.

## 12. The help dialog

**Which is commands 902–905, not 907** — a correction this file was carrying the other way round
until 2026-09-15, and it matters because the two are different features. WinHelp is 902
(`HELP_INDEX`, the contents) and 903/904/905 (`HELP_KEY` on `help01`/`02`/`03`, three keyword
topics), all four on `LaserTank.hlp` (`LTANK.C:1356`). 907 is the Quick About Box and belongs to
item 11.

**`F1` is both, which is why the port could answer with one panel.** ACC1 binds `VK_F1` to 907
(play) and ACC2 binds it to 903 (editor) — two help ids for one key, as `BoardView.cs:1610` already
notes. The port answers both with `DrawHelp` and the `PlayKeys` / `EditorKeys` tables, which was the
honest answer while there was no help text at all.

**This item is the help text.** Base it on the old `.hlp` — it is the original's own documentation
of its own rules, and a port whose whole argument is that the rules are preserved should not write
new ones — but rewrite it to a mature style rather than transliterate 1996 WinHelp prose.

**What it must not do is displace the key list.** `F1` answering *the keys* is the right answer to
`F1`, and the reason the panel measures and reflows (`DrawHelp`, and the German clipping step 13
found). So the help is a second surface that the key list links into, or a section of it — not a
replacement for it. It is also the natural body for item 11's screen to route to.

**One thing it inherits from step 13:** every label in these tables is a catalogue key across eleven
languages and `tools/strings_check.py` fails both ways over them. Help *text* is a different order
of translation volume from help *labels*, and that is a decision this item has to make openly:
an English-only body with translated keycaps is a defensible answer, but it should be a chosen one.

## 13. Hotkeys a player would expect

Asked for directly: **next level on `N`, mute on `M`**. Both are feasible, and the cost is one
displacement rather than a conflict.

**`M` is free.** It appears in neither accelerator table, neither mode's router, nor any panel's key
handling. **`N` is not** — it is command 102, Sound, the mute toggle itself (`lt32l_us.inc:141`),
which is the letter the request wants for next level. So the change is a chain and not a swap:

| key | now | proposed |
|---|---|---|
| `M` | unbound | Sound / mute (102) |
| `N` | Sound (102) | next level (107) |
| `S` | next level (107) | free — **keep as a silent alias** |
| `P` | previous level (119) | unchanged, and now the pair `N` wants |

`[` and `]` already walk the collection as the port's own pair and stay. Keeping `S` as an unlisted
alias costs nothing and old fingers know it.

**None of this can break a gate, and the reason is worth stating once.** `WM_KEYDOWN` drops every
virtual-key code outside 32–40 before `AddKBuff` (`LTANK.C:573`), so letters are accelerators and
*only* accelerators: they are not in a `.lpb`, not in an `.hs`, not in a solver result, and no
fidelity harness presses one. Remapping letters is free in the only sense this project cares about.
The cost is elsewhere and it is small: the keycap strings in `PlayKeys` / `EditorKeys` are literals,
while the labels beside them are catalogue keys and do not move.

**Four more worth taking while the table is open**, in the order they are worth doing:

1. **`Ctrl+C` and `Ctrl+V` for save and restore position** (111/112) are the worst of what is here.
   They are copy and paste on every platform the port runs on, and a player who presses `Ctrl+C`
   over a board is as likely to mean *copy* as anything. Give the pair its own keys and keep the
   originals as aliases.
2. **Undo has no `Ctrl+Z`.** `U` is right and must stay — it repeats while held, which is the whole
   point of undo as a *rate* — but `Ctrl+Z` costs nothing and is the first thing a hand reaches for.
3. **`F8`, before item 10 needs it.** The port spent it on 115 (Auto Record); the original gives it
   to 125 (Resume Recording), which is item 10. Decide which one keeps it here rather than there.
4. **`Z` cycles the three board sizes** (120–122), which is an arbitrary letter for a zoom. `+`/`-`
   is the modern shape if a key can be spared.

**What this item may not spend:** `Esc`, which item 11 is likely to want, and `F1`, which is the key
list and stays (item 12).

**And three letters are already free** from step 8's merge — `D`, `G` and `V` bare, released when
the Difficulty dialog became rank chips and the two high-score lists became one panel. ACC1 had no
spare letters in 1996; this port has several.

## 14. One options dialog

Three panels sit behind three `Ctrl` keys today and they are the same panel three times:

| key | what | id |
|---|---|---|
| `Ctrl+G` | graphics packs | 226, `GraphBox` (`LTANK_D.C:1202`) |
| `Ctrl+L` | language | the port's own (step 6) |
| `Ctrl+N` | name | the port's own (step 14) |

**They are already one shape**, which is what makes this a merge rather than a rewrite. Step 6 built
the language picker *modelled on* the graphics dialog deliberately — "so copying its shape puts the
new dialog where a player already expects to find this kind of choice, on the same modifier" — and
step 14's name row was built as the first non-modal panel with a field in it. All three keep 226's
three load-bearing properties: **the choice applies immediately** on cursor move, **there is no
Cancel** because the thing being cancelled is already on screen, and **the game keeps ticking
underneath** (226 does not call `GameOn(FALSE)`, unlike 225).

**Those three properties are the whole of the design constraint.** A merged dialog with an OK button
would break all three at once, so it does not get one: sections rather than tabs-with-a-commit, and
every control still applying as it is touched.

**What differs between the three is only the control** — a list plus author and description for
graphics, a list with a live preview for language (the labels behind the panel change as the cursor
moves, which is the only honest way to pick one), and a text field for the name. Three shapes in one
frame, which is what a settings panel is.

**This is the surface item 3 and item 7 have been waiting for.** `SkipComLev` and `Diff_Setting` are
settings with no key and nowhere to live (item 3); item 7's typed `user://settings.json` is the
store with no panel. This item is the panel. The three want doing in the order 3 → 14 → 7, or 3 and
14 together, so that the migration is written once.

**One key survives and it should be `Ctrl+G`** — the only one of the three with an original command
id behind it — unless item 13 finds something better. `Ctrl+O` is free (`O` bare is Open
Collections) and reads as *options* to a modern hand; that is item 13's call, not this one's. Two
`Ctrl` keys come back either way.
