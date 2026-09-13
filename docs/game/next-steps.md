# Next steps — the open items in full

Nothing is blocked. The order and the short form are in
[`PROGRESS.md`](../../PROGRESS.md#next-steps); this file carries each item in full — what it is
waiting on, what it would cost, and what the finished work beside it already answered. Items keep
their numbers because the other files refer to them by number; what is done is in
[`history.md`](history.md).

---

## 1. i18n: actually use the translations  *(the UI has settled — this is now unblocked)*

The strings **are** wired, but only fourteen of them: `ID_DEADBOX_DEAD`, `ID_GRAPHBOX_00`–`_05`,
`ID_LOADLEV_00`, `REC_Title` and `txt009`–`txt014`. That is 14 of the **155** keys in each file. The
rest describe dialogs and a nine-button control panel this port does not have, which is why they
read as unused.

**It was sixteen until step 8, and the two it lost are the audit working.** `ID_HIGHLIST_00` and
`ID_GHIGHLIST_00` are the captions of the two score dialogs, and merging the three lists into one
table left the port with no widget that is either of them — their columns are in `L`'s table under
headers of the port's own. That is exactly the finding this item is for: a key is read by a widget
or it goes. They are not deleted yet, because deleting one means editing all ten JSON files *and*
`lang_check.py`'s expectation, which is this item's job and not step 8's.

So the job is an audit, and step 7 is what it was waiting for: for each key, either a widget reads
it or it goes. `ButText1`–`ButText9` are the original's button strip; the 96 `ID_*` slots are its
dialogs; `txt001`–`txt045` are its status and message lines. Deleting a key means editing all ten
JSON files **and** `lang_check.py`'s expectation, because the gate ties the JSON back to the 2007
bytes line by line — a dropped key currently fails it, which is the gate working. Whatever the
audit removes, the *converter* should keep reading, so the mapping from the frozen artifact stays
complete and re-runnable.

**Step 7 made the audit bigger and easier at the same time.** Bigger, because the redesign wrote a
lot of new English: the F1 overlay's six group headings and thirty-odd key labels, the info
column's `Level n of m` / `Score` / `moves` / `shots` / `par`, the status bar's default line, and
the editor panel's `Palette` / `left` / `right`. Easier, because they are all in **two tables and
two draw functions** (`PlayKeys`, `EditorKeys`, `DrawInfoColumn`, `EditMode.Draw`) rather than
scattered through a legend. The port's own strings still have no key in the original and still fall
back to English by having only an English form — the model set by the legend strip, which step 7
deleted.

## 2. A menu bar — *or whatever step 7 makes of it*

Step 6 converted all 73 menu items of both trees with their command ids and accelerator labels, so
`Language.MainMenu` / `Language.EditorMenu` is a ready-made model, and this is still the cheapest
remaining piece of the original that is *fully specified data* rather than design work.

**But step 7 answered half of what it was for.** The reason a menu bar was worth building was
discoverability: the port was key-driven and the only way to learn a key was a wall of grey legend
text under the board. F1 is that now — the original's own help accelerator (907 / 903), showing
every binding as keycaps, grouped, including the editor's. What a menu bar would still add is a
*pointing* route to the commands, which a touch or web build would want and which F1 does not give.
So this is no longer "the cheapest thing left" so much as "the thing to do if the port is going
somewhere without a keyboard". If it is built, the top bar is where it goes.

**Step 9 answered the other half, and answered it without a menu bar.** Every keycap, pill, row and
card the redesign drew is now a button, and the F1 overlay is a *command list* rather than a legend:
the pointing route to thirty-odd commands is the same list that documents them. What a menu bar
would still add over that is a route to the handful of commands nothing on screen names — the ones
in *item 3* that are not built yet. So it is worth less than it was, not more.

## 3. The rest of the original that is still missing

Everything here was named as left out at the time rather than forgotten.

**Blocked on a file dialog:** Load Level in the editor (602) and Save As (606). 108 was the third
and is done — and the way it was done is the model for these two: a list of what is *in the repo*
rather than a native file dialog. See [*Finished*](history.md), the collection picker. 602 wants exactly the same
list plus a level inside the chosen collection (which is `LevelList`, already built); 606 wants
somewhere to type a name, so it is really blocked on the modal prompt below rather than on a file
dialog.

**Blocked on a modal prompt:** the "save changes?" prompt on leaving the editor (`Modified` is
tracked and shown, there is just no message box), the `RecordBox`/`HSBox` name prompts (both INI
keys are read and written; there is nowhere to type), the Difficulty dialog (225), the DeadBox
itself (a status line here — see [*Finished*](history.md), the level-39 report), and the `LoadTID`
tunnel dialog
*as* a dialog (the id is a mode here, cycled with `T`, because a modal prompt per painted cell is
worse than a mode). **Step 7 built the shape all of these want** — `Ui.Dialog` plus a scrim, a
measured panel, keycaps for the buttons — and **step 8 built the first one**: `Esc`'s quit prompt
(`DrawQuitAsk`), which is the yes/no with its modality, its clock rule and its "every other key is
the safe answer" already settled. So what is left for the rest is a text field, not a look.

**And the DeadBox has a rule of its own that nothing here implements yet.** Its dialog proc is four
lines (`LTANK_D.C:159`) and the second one is a guard: `if (Game.RecP > 1) EndDialog(Dialog, wparam);
else EndDialog(Dialog, ID_DEADBOX_RESTART);` — **die on the first turn and every button is Restart**,
Undo included. `RetBox` ("Return to Game") has the identical test. It is the same species as the
level-39 report in [*Finished*](history.md): logic that lives in a dialog proc rather than in the
game, and
therefore a guard the port has to write down or lose. It is a *separate* change because Restart is
command 105, which none of the three script drivers has a token for — implementing it faithfully
means adding one to all three, so it does not ride along with `Session.AcceptsInput`.

**Additive, nothing blocking:** the Search sub-dialog (`SearchBox`, `LTANK_D.C:394` — name or
author substring, difficulty mask, skip-completed) and `TransListKey`'s type-ahead, neither of which
changes a row; `Backspace[]`'s ten-level history (118); Resume Recording (125); Print (126); "View
Opening Screen" (`ID_GRAPHBOX_08` — toggles `QHELP` and paints `Opening.bmp` over the board, its own
piece of drawing); "Change Directory" (`ID_GRAPHBOX_09`, a shell folder browser — the key is
persisted and `--gfx-dir` sets it); and `LoadImageFile`'s per-language `Control.bmp` / `Opening.bmp`
/ `LaserTank.hlp`. **`LaserTank.hlp` is the one F1 stands in for**: command 907 is WinHelp in the
original and the key list here, which is the answer the port can actually give.

**Wants a `LoadNextLevel` port rather than a menu:** `[OPT] SkipComLev` and `[DATA] Diff_Setting`.
Both are read into `Options` as comments only.

**Not coming:** the `.ln` files under `Setups/Language/` (a 4.0-era format superseded by the
`.dat`s and not read by the 2007 build).

## 4. The UI redesign, second pass

The first pass is built — see [*Finished*](history.md), step 7 — and it deliberately stopped at the
chrome. The biggest thing it left was **mouse and touch**, and that is [*Finished*](history.md),
step 9. The second-biggest was that the chrome looked generated, and that is step 10. What is still
open:

* **Web.** Nothing here needs a platform branch and there is no stretch mode to fight, so an HTML5
  export should draw correctly today. It has not been tried. **Step 10 removed the one thing that
  would have drawn wrong**: the chrome asked for `Segoe UI` / `Inter` / `SF Pro` by name and
  `SystemFont` fell through to Godot's own *proportional* face in a browser, so the level list —
  whose column rules are placed in glyph units off the original's `sprintf` padding — was
  fixed-pitch everywhere except the one target this bullet is about. Both faces are shipped now.
  `Paths` and the `.hs`/`.ini` writes are what will need work, not the drawing.
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
