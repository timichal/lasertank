# The C reference oracle

Headless build of the original LaserTank 4.1.2 game logic. **Ground truth forever.**
`original/src/LTANK2.C` is compiled *verbatim* — it is never edited, patched or
refactored, and nothing in this directory may change what it computes.

```
bash oracle/build.sh
./oracle/build/oracle.exe --levels data/quirks/game-objects/Game-Objects-in-LT.LVL \
                          --lpb    data/quirks/game-objects/Game-Objects-in-LT_0001.lpb
python tools/replay_all.py            # replay the whole recorded corpus
```

## How it works

| file | role |
|---|---|
| `stub/windows.h` | a minimal `<windows.h>` that shadows the real one via `-I` |
| `win32_stub.c` | implementations: real memory/files/messages, no-op GDI |
| `driver.c` | globals from `LTANK.C`/`LTANK_D.C`, a window proc, the tick loop, tracing |

The important design choice is that **only the Win32 *API* is stubbed, never
LaserTank's own code**. That matters because several of this game's
logic-carrying side effects live inside functions that look purely cosmetic:

- `UpDateLaserBounce()` (`LTANK2.C:565`) is a *paint* routine that sets
  `LaserBounceOnIce`, which makes `MoveLaser()` `goto LaserMoveJump` and take a
  second step in the same tick (`LTANK2.C:1631`). Stub the drawing by deleting
  calls and laser-on-sliding-mirror behaviour silently changes. Stub
  `Rectangle()` and `SelectObject()` instead and the quirk survives untouched.
- `SendMessage(WM_Dead)` from `CheckLLoc()` is synchronous; drowning and black
  holes use `PostMessage(WM_Dead)`. The ordering is observable, and was
  deliberately changed in 4.0.6. So `SendMessageA` re-enters `LT_WndProc`
  immediately while `PostMessageA` queues into a FIFO the driver drains after
  each tick — what a real Windows message pump does.

`LTANK.C` and `LTANK_D.C` are *not* compiled: they are window and dialog code.
The only part of `LTANK.C` that matters is the `WM_TIMER` handler at `:579`,
which is the game loop; `LT_Tick()` in `driver.c` is a line-by-line
transliteration of it.

The external surface turned out to be small: 49 Win32 calls plus a dozen
globals (`MainH`, `RB_TOS`, `VHSOn`, `LANGText`, `SoundPlay`, the button
handles). Everything else LTANK2.C needs, it defines itself.

## Replay configuration

`oracle_init()` sets `PBOpen = PlayBack = TRUE, Speed = 1`. This is exactly the
state the real program is in while playing back a `.lpb`, and it is behaviourally
identical to live play: the pacing block reduces to `PBHold = (ConvMoving ||
SlideO.s || SlideT.s)`, all of which the key-consume test at `LTANK.C:613`
already covers. It also means the original skips `CheckHighScore()` and
`LoadNextLevel()` on reaching the flag, so **the oracle never writes to the
corpus**. `Ani_On` is TRUE, as the tutor pack requires.

Death has no headless equivalent — the real handler runs `GameOn(FALSE)` and
then blocks on a modal dialog. The oracle keeps the `GameOn(FALSE)` (which
`MoveLaser()` observes via `Game_On || VHSOn`), records the death and halts,
which is the state the real game sits in while that dialog is up.

## Trace format

One line per tick. `--field` adds full `PF`/`PF2` hex, `--bmf` adds `BMF`/`BMF2`.

```
t=<tick> T=<x,y,dir,firing,good> L=<laser x,y,dir,firing,good> S=<moves,shots>
P=<RecP> C=<ConvMoving> SlT=<x,y,dx,dy,s> SlO=<x,y,dx,dy,s> N=<slide count>
M<i>=<x,y,dx,dy,s> ...  A=<AniLevel,AniCount> D=<deaths> G=<Game_On>
H=<fnv1a(PF),fnv1a(PF2)>
```

`BMF`/`BMF2`/`AniLevel`/`AniCount` are **cosmetic**. Every read of `Game.BMF` in
the whole program is either a paint call (`UpDateSprite`) or `MoveObj:1293`
carrying a sprite along with a pushed object; no bitmap ever feeds a decision.
They are traced anyway because a divergence there still catches a
transliteration slip in Phase 2 — but it is not a correctness failure, and the
animation phase at level start does not affect replay.

## Build notes

- `gcc` treats an uppercase `.C` as C++; `-x c` is required.
- `-fpermissive`: `LTANK2.C:677` assigns `char*` to `LPBITMAPINFO` inside a
  bitmap loader that is never called. lcc-win32 allowed it, gcc 14+ does not.
- `GetLastError` is `#define`d to `lt_GetLastError` because mingw links
  `libkernel32` by default and would otherwise collide.
- 64-bit is fine: `TGAMEREC`, `TLEVEL` (576 B), `THSREC` (10 B) and
  `TRECORDREC` (66 B) all have identical layout under 32- and 64-bit, since none
  contains a pointer.
