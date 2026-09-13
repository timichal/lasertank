# The tick, the input path, and the three engines

What the game *is*, where the surprises in its input handling are, and the three implementations
that have to agree about it. This is the spec everything else in these files is measured against.
The quirks riding on top of it are in [`quirks.md`](quirks.md), the harnesses that prove the
agreement are in [`harnesses.md`](harnesses.md), and the status, the build and the rules are in
[`PROGRESS.md`](../../PROGRESS.md).

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

