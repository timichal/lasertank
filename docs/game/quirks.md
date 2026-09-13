# Quirk hazards — every one is load-bearing

The bugs and surprises that must survive the port, numbered, because everything else in these files
refers to them by number. Whole level packs exist *only* to exploit some of them, and upstream says
so: *"Some of the tricks are bugs that have been intentionally left in the software because they
make the game more interesting."*

**The rule these generalise to**, learned twice and worth holding while reading any of them: *in this
program a function's name tells you nothing about whether it mutates state.* `UpDateTank()` clears
`TankDirty` (`LTANK2.C:537`) and `Animate()` ends by setting it (`LTANK2.C:1161`) — both were nearly
missed because they are named like paint calls.

The spec these ride on is [`engine.md`](engine.md); the file layouts three of them are about are in
[`formats.md`](formats.md).

---

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
   changed in 4.0.6, and the ordering is observable. **It does *not* mean the tank takes an extra
   move after dying** — that read of it was wrong for two years of these files and is corrected in
   [*Finished*](history.md), the level-39 report. Both arms of the handler end with
   `RB_TOS = Game.RecP`: the VHS arm writes it out (`LTANK.C:720`), and the interactive arm reaches
   it through the modal DeadBox, whose every
   exit calls `UndoStep`. What the two arms actually differ on is **when** that happens relative to
   the key test at `LTANK.C:613` — `SendMessage` from `CheckLLoc` (`LTANK2.C:1469`) lands at tick
   step 2, *before* it, so the tick consumes nothing further and `AntiTank()` gets no turn;
   `PostMessage` from water and black holes lands after the tick, by which point that tick's key is
   already spent. `Engine.SendDead` and `oracle/driver.c`'s `LT_WndProc` carry the clear.
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

