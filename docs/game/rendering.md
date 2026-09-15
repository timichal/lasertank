# Rendering, audio, and the settings

Everything decoded out of the original's presentation layer: the atlas geometry, the mask rule, the
laser bar, the monophonic sound, and where the player's choices live. **None of it
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

## Settings: `user://settings.json`, and the INI as an importer

**Two files, and only one of them is ever written.** Step 2 put the options where the original puts
them; step 16 split that into the job it was doing well and the job it was doing by accident.

### The store — `Settings.cs`

The port's settings are one typed record, serialised through `System.Text.Json` to
`user://settings.json` (`%APPDATA%\Godot\app_userdata\LaserTank\` on Windows, IndexedDB in a web
export). `$LT_SETTINGS` and `--settings FILE` override the path; `--ini FILE` moves it too, to
`FILE`'s sibling `<name>.settings.json`, so a scratch directory full of probe INIs is a directory
full of independent stores.

- **`user://` rather than beside the exe.** The INI was written to the repo root, which is the
  install directory in an exported build — unwritable in every `Program Files` install, and not a
  filesystem at all in a browser. This was a real bug independent of the format.
- **A `version` field**, and a `Migrate()` hook that has nothing to do yet. Adding the field later
  would have meant guessing what a file without one was. Growing the record needs no version bump:
  an unknown field is ignored on read and an absent one keeps its default.
- **Every default is the original's**, in one place — `Settings`' field initialisers. `Size` is
  **1**, the 24 px board (`LTANK.C:1567`); `Graphics_Mode` is 0; `Sound`, `Animation` and `RLL` are
  on; `Auto_Record` and `SkipComLev` are off.
- **Written through a temporary and moved into place.** The INI did not bother; JSON has to, because
  a half-written object does not parse at all. A store that will not parse is treated as one that is
  not there: the port says so on stderr, imports again, and rewrites.
- **Not Godot's `ConfigFile`** — it is INI-shaped anyway, so the trade would have been gate-pinned
  lines we own for an engine class. **Not a `Resource`/`.tres`** — it binds the save format to
  engine classes and is miserable to diff.

### The importer — `Ini` and `IniImport` in `Options.cs`

On the **first run only** — when there is no usable store — a `LaserTank.ini` is read and folded
into the record. After that it is never opened again: editing it changes nothing, and **nothing in
the port writes it, ever**. It is found at `$LT_INI`, at `--ini`, or beside the repo root.

`Ini` is still a stand-in for the Win32 profile calls rather than an INI library, and everything
that made it a *finding* about the 2010 binary survives, pinned by `options_check.py`:

- **first match wins**; sections and keys match case-insensitively;
- integers follow **`atoi`** — a present-but-junk value reads as 0, and only a *missing* key gives
  the default;
- **the Yes/No test is a `strcmp`, case and all.** `if (strcmp(temps, psYes)) Sound_On = FALSE;`
  (`LTANK.C:411`) means **exactly `Yes` or the sound is off** — a hand-edited `Sound=yes` really
  does mute the 2010 binary. Same for `Animation` and `RLL`. `Auto_Record` and `SkipComLev` are the
  mirror image (`strcmp(...) == 0`, default **No**), so a missing key means off rather than on. The
  two layers compose and are worth keeping apart: the *comparison* is strict, and the *reader*
  trims, so `RLL=Yes ` is still on while `Yes!` is not.

Keys read: `[SCREEN] Size`, `Graphics_Mode`, `Graphics_File`, `Graphics_Dir`; `[OPT] Sound`,
`Animation`, `Auto_Record`, `RLL`, `SkipComLev`; `[DATA] RLLFilename`, `RLLLevel`, `Player`,
`Record Author`, `Diff_Setting`, and the invented `Language`.

**What retired with the write half** is interop with a live 2010 install: the preserve-every-other-
line rule, and step 14's write of the one name into both `[DATA] Record Author` and `[DATA] Player`.
Nothing in this tree has ever been pointed at a real install, and that possibility was the
justification for the most awkward code in the class. The *reads* stay — either key still names the
player, long one first — and so does `Initials`, which is the half that was ever load-bearing
because it is what reaches a `.hs` and a `.lpb` header.

**`--ini` or `--settings` is what makes the settings live.** A run left to find them on its own gets
them read-only and starts on whatever level it was told to: eight parallel gate jobs must not race
over one file, and a screenshot must not change what the next player sees. Passing either says "this
state is yours" and turns both halves back on. The list of runs that count as instruments is
`BoardView.Instrument`, and it has been found short twice — step 9's four pointer flags, and step
16's `--edit` / `--save`.
