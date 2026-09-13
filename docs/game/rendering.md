# Rendering, audio, and the options file

Everything decoded out of the original's presentation layer: the atlas geometry, the mask rule, the
laser bar, the monophonic sound, and the `LaserTank.ini` the player's choices live in. **None of it
is a rule** — the line is drawn in [`engine.md`](engine.md): a Godot node reads `Game.BMF` and draws
it, and never decides anything. What the UI built on top of this looks like is
[`ui.md`](ui.md).

---

## Rendering and audio: the decoded facts

**Read `Game.BMF`, never re-derive it from `PF`.** `BuildBMField` maintains the *bitmap number* per
cell, `Animate()` cycles it, and `Obj.GetOBM()` is the object→bitmap table (`LTANK2.C:77`). It is
not simply `GetOBM(PF)`: a tunnel is 55, the tank's own cell is 1 *and its `PF` is zeroed*, and
`Animate` then cycles animated objects. Every one of those is a place a re-derivation drifts.

**The atlas geometry.** The sheet is **always 320×192 — a 10×6 grid of 32×32 sprites** — and `BMA[]`
is filled row-major from **i = 1**, ten per row (`GFXInit`, `LTANK2.C:782`). So sprite index `i` is
atlas cell `((i-1) % 10, (i-1) / 10)`. `MaxBitMaps` is 58 (`LTANK.H:92`) and the highest index the
object table yields is 57, so the last row is partly unused.

**The mask is not simply an alpha channel.** The original blits mask-`SRCAND` then bitmap-`SRCPAINT`
only for the sprites `BMSTA[]` (`LTANK2.C:80`) marks transparent; everything else is a plain
`SRCCOPY` that never looks at the mask. In the internal sheet the mask cell for an opaque sprite is
*solid white*, so applying it to everything erases the board. **Except the tunnel, which is masked
anyway:** `UpDateSprite`'s tunnel branch (`LTANK2.C:498`) paints `ColorList[id]` and then masks
sprite 55 over it **regardless of `BMSTA[55]` being 0** — that is the only reason a tunnel's colour
is visible. `Gfx.Masked()` is the one place that rule lives. And **`BMSTA` is declared
`[MaxBitMaps+1]` = 59 wide with 58 initialisers**, so C zero-fills the last entry; both ports carry
the trailing 0, because bitmap 58 is a legal index.

**Pixel formats.** The `.ltg` sheets are `BI_RGB` 24 bpp (8 bpp for `Warcraft_II`) with a 1 bpp
mask; the *internal* pair — `original/src/Game.BMP` and `Mask.BMP`, the resources the 2007 build
carries — are **RLE8 and RLE4**. Both readers handle 1/4/8/24 bpp and both RLE modes.

**Zoom is 24/32/40 px** (`SetGameSize`, `LTANK2.C:1729`), and the original implements it by
`StretchBlt`-ing the whole sheet at load. **Do not copy that** — keep the atlas at native 32×32 and
scale at draw time: same picture without the resample, and no logic reads the sprite size. Hazard
#11 lives in `SetGameSize` and must stay broken; `SetUpGraphicsBox`'s copy of that line *does* have
its parens, which is why picking a pack in the menu really does reload the sheet.

**The laser is the one genuinely new piece of drawing, and it is *paint only*.** `UpDateLaser`
(`LTANK2.C:549`) is a bar down the middle of a cell, `SpBm_Width - 2 * LaserOffset` wide — so
**4, 6 and 6 px** for the three sizes — green when `laser.Good` and red otherwise (which is
`FireLaser`'s `laser.Good = (sf == 2)`, already load-bearing for logic). `UpDateLaserBounce`
(`:565`) paints *two half-bars*, the half the shot came in through and the half it leaves by — but
that function is **[hazard #1](quirks.md)**, and **the core already calls it inside the tick**, so the renderer
must not call, skip or reimplement it. The one fact the paint call has and the state does not is the
laser's incoming direction, so `Session` recovers it by watching `laser.Dir` across a tick.

**The bounce glyph needs a distance test.** When the laser bounces off a mirror that is itself
sliding on ice, `UpDateLaserBounce` sets `LaserBounceOnIce` and `MoveLaser` `goto`s back for a
*second* step in the same tick — so the bend happened one cell back and the laser now sits in a cell
it went straight through. Comparing `laser.Dir` across the tick says "bounced", and the half-bars
would be painted **in the wrong cell**. Two cells of travel in one tick is exactly that case, so the
bounce glyph is suppressed and the ordinary straight bar drawn. `Tutor-with-Playbacks` 93 (tick 527)
and 94 (tick 206) are the only recordings in the corpus that reach it.

**Interpolation is ours; the original had none** — it snapped, one cell per 50 ms. The tank is lerped
between the previous cell and the current one, guarded three ways: only while the timer is running,
only between *adjacent* cells so a tunnel does not slide the tank across the board, and rounded to
whole pixels because the sheet is nearest-filtered. `I` turns it off, which is the honest A/B against
the 2010 binary, and `--shot` forces it off so a screenshot names a tick rather than a moment
between two.

**Audio is monophonic, and that is a fact about the original worth keeping.** `PlaySound(p, 0, 5)`
is `SND_MEMORY | SND_ASYNC` with no `SND_NOSTOP`, so a second call *stops the first*: the tank
moving cuts off the laser bounce. One `AudioStreamPlayer`, `Play()` every time, reproduces that;
sixteen players would be a nicer game that does not sound like this one. A tick that asks for
several sounds therefore only ever plays the last — `Session` hands the renderer the whole per-tick
list in call order and `Sfx` plays the last of it, so the audible-behaviour choice and the fidelity
check stay separate things: the **list** is what the gate diffs.

**Muting lives in the presentation, not the engine.** `lt_sfx.c:29` returns early on `!Sound_On`, so
a muted original makes no `PlaySound` call at all — but it makes the same decisions, and decisions
are what the port records. Putting `Sound_On` in the engine would make a muted game trace
differently from a loud one.

**The WAVs.** All sixteen are PCM mono 8-bit, 11025 Hz except `MOVE` and `PUSH3` at 8000.
`SoundFile.cs` walks every RIFF chunk (these files carry `fact`, `LIST` and `DISP`, and several put
`LIST` *after* `data`) and converts to signed 8-bit, because **8-bit WAV samples are unsigned and
every raw-PCM consumer wants them signed**. That xor with `0x80` silently produces a DC-offset click
rather than an error, so it is cross-checked. Nothing was imported into `res://`:
`original/src/Sounds/` is frozen and read from where it lies, exactly as the internal sheet is.

**`SoundPlay`'s cost to the core is one word.** `partial void SoundPlay(int sn);` with the body
`SoundLog?.Add(sn)` in `Engine.Sound.cs`. `SoundLog` is null unless a driver opts in, so the solver
pays one null test per call and allocates nothing.

**The one thing no gate covers** is that `AudioStreamPlayer` actually makes a noise: the ids, the
decode and the option are all checked headless, and the last hop needs a human with speakers. It was
walked once by hand. If the audio path is ever refactored, that hop has to be walked again.

---

## Options: `LaserTank.ini`

`Options.cs` is the file, in the file and under the key names the original persists them under
(`LTANK.H:113-128`). `Ini` is a stand-in for the three profile-string calls rather than an INI
library:

- **first match wins**; sections and keys match case-insensitively;
- integers follow **`atoi`** — a present-but-junk value reads as 0, and only a *missing* key gives
  the default;
- **a write preserves every other line in the file.** Load-bearing, not politeness: the 2010 binary
  keeps a dozen keys in this same file, and a rewrite that dropped them would silently reset the
  player's other settings.

The defaults are the original's, and one of them is easy to get wrong: `Size` defaults to **1**, the
24 px board (`LTANK.C:1567`). `Graphics_Mode` defaults to 0.

**The Yes/No test is the original's, case and all.** `if (strcmp(temps, psYes)) Sound_On = FALSE;`
(`LTANK.C:411`) means **exactly `Yes` or the sound is off** — a hand-edited `Sound=yes` really does
mute the 2010 binary. Kept for `Sound`, `Animation` and `RLL`. `Auto_Record` is the mirror image
(`strcmp(...) == 0`, default **No**), so a missing key means off rather than on. Step 2 read `RLL`
with the looser sense and its gate now pins that; the two idioms disagreeing is recorded here rather
than harmonised by guess.

Keys read: `[SCREEN] Size`, `Graphics_Mode`, `Graphics_File`, `Graphics_Dir`; `[OPT] Sound`,
`Animation`, `Auto_Record`, `RLL`; `[DATA] RLLFilename`, `RLLLevel`, `Player`, `Record Author`, and
the invented `Language`.

**`--ini` is what makes the options live.** A run left to find `LaserTank.ini` on its own gets it
read-only and starts on whatever level it was told to: eight parallel gate jobs must not race over
one file, and a screenshot must not change what the next player sees. Passing `--ini` says "this file
is yours" and turns both halves back on.

