# Next steps — the open items in full

Nothing is blocked. The order and the short form are in
[`PROGRESS.md`](../../PROGRESS.md#next-steps); this file carries each item in full — what it is
waiting on and what it would cost.

**Open items only.** What is done, and what was deliberately decided against, is in
[`history.md`](history.md) and is not repeated here. **Items keep their numbers** because the other
files refer to them by number, so a number retires when its item closes rather than being reused —
which is why this file starts at 3.

---

## 3. The rest of the original that is still missing

Everything here was named as left out at the time rather than forgotten. **The editor's share of it
is item 8** — it holds the one genuine modal prompt the port still wants, plus a file dialog, and
nothing in it touches the game's own window.

**Not blocked on a modal prompt — which is what this paragraph used to say.** Four things were
listed here as waiting on a dialog. **Two of them turned out to be answered already, and two want a
settings surface rather than a prompt**, which leaves item 3 with no modal work in it at all: the
one box still worth building is the editor's, and it is item 8's.

*The two that are answered* are [*Finished*](history.md) and were being carried here as deferrals
they are not: the DeadBox, which is a status line here, and the Difficulty dialog (225), which is
the level list's rank chips.

*The two that want a field* are the `RecordBox` and `HSBox` name prompts — `[DATA] Record Author`
and `[DATA] Player`. Both keys are read and written already (`Options.SetRecordAuthor`,
`Options.SetPlayer:402`), both are legal blank, and **both are identity rather than a decision about
the level just played**. The original asks mid-action because a 1996 dialog is the only surface it
has; asking for four characters at the instant the board turns green is the DeadBox's mistake with a
text field in it, and `WinLine` already carries the whole of HSBox's content without stopping
anything. So these two want two rows in a small settings panel — the `GraphicsMenu` /
`LanguageMenu` shape — and **that inverts the dependency item 7 records**: item 7 waits on item 3
for the *keyspace*, and these two halves of item 3 wait on item 7 for somewhere to put them.

**And "there is nowhere to type" is no longer true.** Text entry exists twice, and neither instance
is modal: `LevelList.Key`'s filter field (substring, `strupr`, `Backspace`, `QueryMax`) and
`EditMode.Typing`'s three fields, which walk focus on Tab and clamp to what a `char[31]` in a file
the 2010 binary reads back can hold. What is missing is those two factored into one `Ui` field with
a caret — a refactor, not a blocker, and the thing to do before either name row is written.

**Step 7 built the shape** — `Ui.Dialog` plus a scrim, a measured panel, keycaps for the buttons —
and **step 8 built what now looks like the only modal question the game side needs**: `Esc`'s quit
answer (`DrawQuitAsk`), which settles the modality, the clock rule and the "every other key is the
safe answer".

**Additive, nothing blocking:** `Backspace[]`'s ten-level history (118); Resume Recording (125);
Print (126); "View
Opening Screen" (`ID_GRAPHBOX_08` — toggles `QHELP` and paints `Opening.bmp` over the board, its own
piece of drawing); "Change Directory" (`ID_GRAPHBOX_09`, a shell folder browser — the key is
persisted and `--gfx-dir` sets it); and `LoadImageFile`'s per-language `Control.bmp` / `Opening.bmp`
/ `LaserTank.hlp`. **`LaserTank.hlp` is the one F1 stands in for**: command 907 is WinHelp in the
original and the key list here, which is the answer the port can actually give.

**Wants a `LoadNextLevel` port rather than a menu:** `[OPT] SkipComLev` and `[DATA] Diff_Setting`.
Both are read into `Options` as comments only. **`Diff_Setting` has no UI work left in it**: the
five-bit mask the Difficulty dialog would have written is the mask the level list's rank chips
already toggle (`Ctrl+1`..`Ctrl+5`, `Ctrl+0` to clear), so what remains is persisting that mask and
having `LoadNextLevel` read it — engine and settings, no dialog.

**And the route these want, when they land, is the `F1` list — not a menu bar** (which was item 2,
and is [*Finished*](history.md), decided against). Each of these is blocked on *being written*; once
one is, it gets a row in `PlayKeys` or `EditorKeys` like every other command in this port, and that
row is both its documentation and its click target.

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
* **The graphics packs' own chrome.** `Control.bmp` and `Opening.bmp` ship per language and per pack
  and nothing reads them. The top bar's app mark is drawn from the sheet, which is as far as step 7
  took the idea.

  **This was offered as the answer to step 10 and turned down**, so the reasoning is worth having
  here rather than re-derived. `Control.bmp` is the info column's own ancestor — a tiled brick wall
  out of the game's sprite sheet, sunken Win3.1 wells for the readouts, a serif display face, hard
  corners, no shadows — and it is a complete, genuinely unmistakable design language that no
  template could accidentally produce. It would have been the *safest* possible fix for "this looks
  generated". It was declined because it makes the chrome a costume: the port's whole argument is
  that the rules are sacred and the interface is not, and a pastiche of the original's panel blurs
  exactly that line, while also fighting the resizable layout step 7 built. If this bullet is ever
  taken up, the thing to take is probably the *materials* — the sheet's own tiles as a texture — and
  not the 1996 panel's layout.
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

**Waiting on item 3** for the *store*, and **owed to it** for the *panel*. `[OPT] SkipComLev` and
`[DATA] Diff_Setting` are still to land — both are read into `Options` as comments today — so the
original's keyspace is not finished being filled in, and splitting the store before they arrive
means writing the migration mapping twice. That is the half that waits.

**The other half runs the other way, and it is new.** Item 3's two name prompts — `[DATA] Record
Author` and `[DATA] Player` — stopped being dialog work and became settings rows, so they want a
surface this item has not built either: there is no settings *panel* in the port at all today, only
the three list panels (`GraphicsMenu`, `LanguageMenu`, `CollectionList`) and the `F1` key list.
**Panel and store are separable** — the two rows could be written against `Options` as it stands and
migrate with everything else — so this is not a cycle, but whoever takes either one should know the
other is now pointing at it.

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
