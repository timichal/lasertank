// Phase 5, steps 0 and 1: the board on screen, and the 20 Hz tick under it.
//
// This node draws and routes keys.  It reads Game.BMF / Game.BMF2 / Game.PF2,
// the tank and the laser, exactly as UpDateSprite, UpDateTank, UpDateLaser and
// UpDateLaserBounce do (LTANK2.C:490, :536, :549, :565), and it never decides
// anything -- no rule, no movement, no laser logic lives here.  The tick is
// Session's; the rules are LaserTank.Core's.
//
// Read BMF, never re-derive it from PF.  BuildBMField is not GetOBM(PF): a
// tunnel is 55, the tank's own cell is 1 and its PF is zeroed, and Animate()
// then cycles BMF for animated objects.
//
// **The tick is _PhysicsProcess, never _Process** (hazard #10).  Godot's
// physics step is a fixed rate decoupled from rendering, and project.godot sets
// it to 1000 / GameDelay = 20 Hz, which is the original's SetTimer(1, 50).
// _Process only asks for a redraw, so a 144 Hz display draws 144 interpolated
// frames a second over the same 20 ticks -- and, separately, cannot consume 144
// keys a second, because the pending-key test in Session.Key is what gates
// that and no frame rate enters into it.
using System;
using System.IO;
using Godot;
using LaserTank.Core;
// Godot has an `Engine` class too.  The alias keeps the original's name at the
// use sites rather than renaming the thing the whole project is about.
using Engine = LaserTank.Core.Engine;

namespace LaserTank.Game
{
    public partial class BoardView : Node2D
    {
        // SetGameSize (LTANK2.C:1729) offers exactly these three, as sizes 1, 2
        // and 3 -- which is what [SCREEN] Size persists.  The original resamples
        // the whole sheet at load; we scale at draw time instead, so switching
        // size here changes nothing but pixels.  (Hazard #11 lives in that
        // function -- `if (GFXOn) GFXKill;`, missing its parens.  It stays
        // missing; nothing here calls it.)
        private static readonly int[] Zooms = { 24, 32, 40 };

        /// **LaserOffset is a per-size constant, not a fraction of the cell**
        /// (LTANK2.C:1747, :1756, :1765 -- 10, 13, 17 for the three sizes).  So
        /// the bar UpDateLaser paints is `SpBm_Width - 2 * LaserOffset` wide:
        /// **4, 6 and 6 px**, a hairline down the middle of the cell.  Reading
        /// the initialiser at LTANK2.C:46 as 10-of-32 instead -- which is what
        /// this file did until the picture was compared with the 2010 binary --
        /// gives 8, 12 and 14 px and a laser two to three times too fat.  It is
        /// the same species of mistake as re-deriving BMF from PF: the value is
        /// in a table in the original, so read the table.
        private static readonly int[] LaserOffsets = { 10, 13, 17 };

        /// The original's size 1..3, which is [SCREEN] Size.
        private int _size = 1;
        private int Cell => Zooms[_size - 1];
        private int LaserOffset => LaserOffsets[_size - 1];

        internal int Size => _size;
        internal static int CellOf(int size) => Zooms[Math.Clamp(size, 1, 3) - 1];

        private const int Margin = 16;
        /// EditMode does the same window arithmetic for its palette.
        internal const int MarginPx = Margin;
        // Room for DrawHud's eight lines: the header, the scores, the state
        // line, four key legends and the hint.  Step 4 added a dozen keys, and
        // they went into *four short* legends rather than three long ones
        // because the window is only as wide as the board -- 384 px at the
        // default 24 px zoom, where DrawString clips a long line silently.
        // Keep this in step with DrawHud or the hint falls off the window;
        // options_check.py checks the strip is the same height at all three
        // sizes, not that it is any particular height.
        private const int HudH = 166;

        private Session _s;
        private Atlas _atlas;
        private Options _opt;
        private System.Collections.Generic.List<Pack> _packs;
        private Pack _pack;
        private GraphicsMenu _menu;
        private LevelList _list;
        private EditMode _edit;
        private Sfx _sfx;
        private string _error;

        /// A monospace face for the three list panels.  Their rows are the
        /// original's own `%4d %-30.30s` sprintf output, so the padding only
        /// lines up in a fixed-pitch font; ThemeDB.FallbackFont is proportional.
        /// A SystemFont falls through its name list and then to the default, so
        /// a machine with none of the three still draws readable rows.
        private Font _mono;

        /// Off in --shot mode: the shot awaits two frames, and physics would
        /// otherwise tick the game past the frame being captured.
        private bool _driving = true;

        /// Interpolating the tank between ticks is a presentation choice the
        /// original did not make -- it snapped, one cell per 50 ms.  On by
        /// default because a 60 Hz display shows the step as a stutter; `I`
        /// turns it off, which is the honest A/B against the 2010 binary.
        private bool _interpolate = true;

        public override void _Ready()
        {
            string[] args = OS.GetCmdlineUserArgs();
            if (Array.IndexOf(args, "--check-sheets") >= 0)
            {
                GetTree().Quit(SheetCheck.Run());
                return;
            }
            if (Array.IndexOf(args, "--check-sounds") >= 0)
            {
                GetTree().Quit(Sfx.Check());
                return;
            }
            // Step 4's two, before any of the options work below: neither needs
            // an INI, a pack or a level, and both must be runnable in parallel.
            if (ArgStr(args, "--check-lists") is string cl)
            {
                GetTree().Quit(Step4Check.CheckLists(cl));
                return;
            }
            if (ArgStr(args, "--check-scores") is string cs)
            {
                GetTree().Quit(Step4Check.CheckScores(cs));
                return;
            }

            // The tick rate is a project setting, so a stale project.godot
            // would silently play the game at 60 Hz.  Fail loudly instead.
            int tps = Godot.Engine.PhysicsTicksPerSecond;
            if (tps != 1000 / Session.GameDelayMs)
                throw new InvalidOperationException(
                    $"physics_ticks_per_second is {tps}, must be " +
                    $"{1000 / Session.GameDelayMs} (GameDelay = {Session.GameDelayMs} ms)");

            // ---- the persisted options, and the overrides on top of them ----
            // An instrument run -- a screenshot, a scripted playthrough, a
            // check, a clock measurement -- reads the file and writes nothing,
            // because eight parallel gate jobs must not race over one INI and a
            // screenshot must not change what the next player sees.  An
            // explicit --ini says "this file is yours", which is how
            // tools/options_check.py drives the writing half.
            string ini = ArgStr(args, "--ini");
            bool instrument = ArgStr(args, "--shot") != null
                              || Array.IndexOf(args, "--play") >= 0
                              || ArgStr(args, "--replay") != null
                              || Array.IndexOf(args, "--check-options") >= 0
                              || Arg(args, "--tick-rate", 0) > 0;
            // One rule, used twice: **an explicit --ini makes the options
            // live** -- writable, and allowed to choose the level -- while an
            // instrument left to find the file on its own gets the settings
            // read-only and starts wherever it was told to.  That is what keeps
            // `--shot` reproducible and lets the gate exercise both halves.
            bool live = !instrument || ini != null;
            _opt = new Options(new Ini(ini ?? Paths.Ini, readOnly: !live));

            string gfxDir = ArgStr(args, "--gfx-dir");
            if (gfxDir != null && gfxDir != _opt.GraphicsDir) _opt.SetGraphicsDir(gfxDir);

            _size = _opt.Size;
            if (ArgStr(args, "--zoom") is string zs)
            {
                int i = Array.IndexOf(Zooms, Ini.Atoi(zs));
                _size = i >= 0 ? i + 1 : Math.Clamp(Ini.Atoi(zs), 1, 3);
            }

            // --sound yes|no, this run only unless --save-options is given --
            // the same arrangement --pack and --zoom have.  The player's way in
            // is the S key, which is command 102.
            bool? soundArg = ParseYesNo(ArgStr(args, "--sound"));

            _packs = Packs.Scan(_opt.GraphicsDir);
            _menu = new GraphicsMenu(this);
            _list = new LevelList(this);
            _edit = new EditMode(this);
            _mono = new SystemFont
            {
                FontNames = new[] { "Consolas", "DejaVu Sans Mono", "Courier New", "monospace" },
            };
            Pack want = Packs.FromOptions(_packs, _opt);
            if (ArgStr(args, "--pack") is string ps)
            {
                // A number is step 0's index; a word is `internal`, `external`
                // or a .ltg by name.
                want = int.TryParse(ps.Trim(), out int pi)
                    ? Packs.ByIndex(_packs, pi)
                    : Packs.ByName(_packs, ps);
                if (want == null)
                {
                    GD.PrintErr($"no graphics pack \"{ps}\" in {_opt.GraphicsDir}");
                    GetTree().Quit(2);
                    return;
                }
            }
            ApplyPack(want);

            // The menu is the way in; the command line is the other way, and
            // --save-options makes it persist what it was given, exactly as the
            // menu does.  Without it an override is for this run only.
            if (Array.IndexOf(args, "--save-options") >= 0)
            {
                _opt.Ini.ReadOnly = false;
                _opt.SetSize(_size);
                if (soundArg.HasValue) _opt.SetSound(soundArg.Value);
                PersistGraphics();
                if (gfxDir != null) _opt.SetGraphicsDir(gfxDir);
            }

            // [DATA] RLLFilename / RLLLevel: pick up where the last session left
            // off, which is what command 101 (New Game) does when RLL is on
            // (LTANK.C:866).  An explicit --levels / --level / --lpb outranks
            // it, because those name a level on purpose.
            string levels = ArgStr(args, "--levels");
            int level = Arg(args, "--level", 0);
            if (_opt.RememberLastLevel && live)
            {
                if (levels == null && _opt.LastLevelFile.Length > 0
                    && File.Exists(_opt.LastLevelFile))
                    levels = _opt.LastLevelFile;
                if (level == 0 && levels == _opt.LastLevelFile) level = _opt.LastLevel;
            }
            levels ??= Paths.Flagship;
            if (level == 0) level = 1;

            if (Array.IndexOf(args, "--check-options") >= 0)
            {
                GetTree().Quit(CheckOptions(levels, level));
                return;
            }
            byte[] script = Array.Empty<byte>();
            string lpbName = null;

            string lpb = ArgStr(args, "--lpb");
            if (lpb != null)
            {
                TRECORDREC r = LevelFile.ReadPlayback(lpb, out script);
                level = r.Level;
                lpbName = r.LName;
            }
            else if (ArgStr(args, "--keys") is string ks)
            {
                script = PlayMode.ParseKeys(ks);
            }

            // --play: the synthetic playthrough, headless and reproducible.
            // See PlayMode and tools/tick_check.py.
            if (Array.IndexOf(args, "--play") >= 0)
            {
                _driving = false;
                string outDir = ArgStr(args, "--out")
                                ?? Path.Combine(Paths.Root, "out", "recordings");
                int maxTicks = Arg(args, "--max-ticks", 100000);
                int pending = Arg(args, "--pending", 1);
                string author = ArgStr(args, "--author") ?? "LTGodot";
                GetTree().Quit(
                    ArgStr(args, "--lpb-list") is string list
                        ? PlayMode.RunList(list, outDir, maxTicks, pending, author)
                    // Step 4's script mode: the same token stream the oracle and
                    // the CLI take, run through the game's own command path.
                    : ArgStr(args, "--script") is string sc
                        // The options go in only when the run is live -- an
                        // explicit --ini.  Without them the Session posts no high
                        // score, which is what keeps roundtrip_check from
                        // writing .hs files across data/ (it did once).
                        ? PlayMode.RunScript(levels, level, sc, outDir, maxTicks,
                                             author, ArgStr(args, "--stem"),
                                             live ? _opt : null)
                    : PlayMode.Run(levels, level, script, lpbName, outDir,
                                   maxTicks, pending, author));
                return;
            }

            // --replay: watch a .lpb through the *playback* path (PBOpen and the
            // three speeds), which is not the same thing as pressing its keys.
            // Step 4's exit criterion needs both.
            if (ArgStr(args, "--replay") is string rep)
            {
                _driving = false;
                var sp = (PbSpeed)Math.Clamp(Arg(args, "--speed", 1), 1, 3);
                GetTree().Quit(PlayMode.RunReplay(
                    levels, rep, Arg(args, "--max-ticks", 100000), sp));
                return;
            }

            _s = new Session(levels, _opt);
            if (!_s.Load(level)) _error = _s.Error;
            Resize();

            // SFxInit (lt_sfx.c:47), at WM_CREATE where the original does it
            // (LTANK.C:452).  Not in a headless run: there is nobody to hear
            // it, and the gates that run headless must not depend on a wave
            // device existing.  A pack that will not load leaves Sfx.Error set
            // and the game silent, which is SFXError's own behaviour.
            if (DisplayServer.GetName() != "headless")
            {
                _sfx = new Sfx(Paths.SoundsDir) { SoundOn = soundArg ?? _opt.SoundOn };
                AddChild(_sfx);
                if (_sfx.Error != null) _error = "sound: " + _sfx.Error;
            }

            // `--tick-rate SECONDS`: let the real driver run against the clock
            // and report what it measured.  This is the one claim in step 1 the
            // rest of the checking cannot make -- tools/tick_check.py calls
            // Step() synchronously, so it proves the tick's *content*, never
            // its rate.  With no keys the level never ends, so the game ticks
            // for the whole window.
            if (Arg(args, "--tick-rate", 0) is int secs && secs > 0)
            {
                TickRate(secs);
                return;
            }

            // `-- --shot FILE`: draw one frame, write a PNG, quit.  With
            // `--keys`/`--lpb` and `--ticks N` it runs the script for N ticks
            // first, which is how a rendering change to a *moving* board -- a
            // laser in flight, a pushed block -- gets reviewed without a window.
            // `--menu` opens the graphics dialog on start, which is the only
            // way to review the panel with --shot rather than by hand.
            if (Array.IndexOf(args, "--menu") >= 0) _menu.Show(_packs, _pack);

            // `--panel levels|scores|global|playback` is the same idea for step
            // 4's overlays: with --shot it is the only way to review one without
            // a window and a hand on the keyboard.
            switch (ArgStr(args, "--panel"))
            {
                case "levels": OpenList(ListMode.Levels); break;
                case "scores": OpenList(ListMode.MyScores); break;
                case "global": OpenList(ListMode.GlobalScores); break;
                case "playback": OpenPlayback(); break;
                case null: break;
                default:
                    GD.PrintErr("--panel wants levels|scores|global|playback");
                    GetTree().Quit(2);
                    return;
            }

            // `--editor` opens the editor on start, for the same reason
            // `--menu` and `--panel` exist: with --shot it is the only way to
            // review the palette and the field strip without a window.  It also
            // takes an optional edit script -- the same token language
            // `lasertank-core --edit` takes, so a picture can be asked for a
            // *particular* board rather than the level as it loaded.
            if (Array.IndexOf(args, "--editor") >= 0 && _s?.E != null)
            {
                _edit.Enter(_s);
                if (ArgStr(args, "--edit") is string es) _edit.Script(es);
                // `--save` is what Ctrl+S is, reachable from a command line:
                // tools/editor_check.py drives the *game's* save through it and
                // compares the bytes with the headless driver's, which is the
                // same third-implementation check step 4 made of undo.  It goes
                // through EditMode.Save, so the copy-on-write rule for data/
                // applies here too -- the gate hands it a copy outside data/ so
                // it saves in place.
                if (Array.IndexOf(args, "--save") >= 0)
                {
                    _edit.Save();
                    GD.PrintRaw("editor-save " + _edit.Status + "\n");
                    // With no --shot there is nothing to look at, so this is a
                    // batch run and it ends here rather than sitting in a
                    // window a gate cannot close.
                    if (ArgStr(args, "--shot") == null)
                    {
                        GetTree().Quit(_edit.Modified ? 1 : 0);
                        return;
                    }
                }
                Resize();
            }

            string shot = ArgStr(args, "--shot");
            if (shot != null)
            {
                _driving = false;
                RunTicks(script, Arg(args, "--ticks", 0));
                Shot(shot);
            }
        }

        /// Measure the tick rate the same way a player experiences it: through
        /// _PhysicsProcess, against the wall clock.
        private async void TickRate(int secs)
        {
            ulong t0 = Time.GetTicksMsec();
            long before = _s.Ticks;
            await ToSignal(GetTree().CreateTimer(secs), SceneTreeTimer.SignalName.Timeout);
            double elapsed = (Time.GetTicksMsec() - t0) / 1000.0;
            long ticks = _s.Ticks - before;
            // Invariant culture: this line is parsed by tools/tick_check.py, and
            // a Czech locale would print "20,57".
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            GD.PrintRaw(string.Format(inv, "rate ticks={0} seconds={1:F3} hz={2:F2} want={3}\n",
                                      ticks, elapsed, ticks / elapsed,
                                      1000 / Session.GameDelayMs));
            GetTree().Quit(0);
        }

        /// Drive the script forward n ticks synchronously, so a screenshot names
        /// a tick rather than a moment.  Same press-on-drain player as PlayMode.
        private void RunTicks(byte[] script, int n)
        {
            int at = 0;
            for (int i = 0; i < n; i++)
            {
                while (at < script.Length && _s.Pending < 1) _s.Key(script[at++], false);
                if (!_s.Step()) break;
            }
        }

        private static string ArgStr(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// yes/no as the INI spells it, plus the shapes a shell user will
        /// type.  -> null when the flag was not given at all, which is what
        /// keeps "not mentioned" different from "no".
        private static bool? ParseYesNo(string s)
        {
            if (s == null) return null;
            switch (s.Trim().ToLowerInvariant())
            {
                case "yes": case "on": case "true": case "1": return true;
                case "no": case "off": case "false": case "0": return false;
                default: return null;
            }
        }

        private static int Arg(string[] args, string name, int dflt)
        {
            string s = ArgStr(args, name);
            return s != null && int.TryParse(s, out int v) ? v : dflt;
        }

        /// The original resizes its window per zoom too (SetGameSize); the
        /// numbers there are its own layout's and mean nothing here.
        private void Resize()
        {
            if (DisplayServer.GetName() == "headless") return;
            // The editor widens the window rather than covering the board: the
            // original turns the same window into an editor and hangs the
            // palette in its 180-pixel control panel (ContXPos, LTANK2.C:47).
            // There is no such panel here, so the window grows one instead.
            int extra = _edit != null && _edit.Open ? _edit.PanelWidth : 0;
            DisplayServer.WindowSetSize(
                new Vector2I(2 * Margin + 16 * Cell + extra,
                             2 * Margin + 16 * Cell + HudH));
        }

        private async void Shot(string path)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Error err = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.PrintRaw($"shot {path} level {_s?.Level} tick {_s?.Ticks} " +
                        $"pack {_atlas?.Label} zoom {Cell} -> {err}\n");
            // Where the board is, and what the engine says is on it: a tool
            // reading the PNG back can then look in the right place without
            // hardcoding this file's layout.  The laser fields are the trace's
            // own L=x,y,dir,firing,good -- read out of the engine, while the
            // pixels come from the renderer, which is what makes comparing the
            // two a check rather than a tautology.
            TTANKREC l = _s.E.laser;
            GD.PrintRaw($"shot-geometry margin={Margin} cell={Cell} " +
                        $"laser_offset={LaserOffset}\n" +
                        $"shot-laser x={l.X} y={l.Y} dir={l.Dir} " +
                        $"firing={_s.E.Game.Tank.Firing} good={l.Good}\n");
            GetTree().Quit(err == Error.Ok ? 0 : 1);
        }

        // ---- the graphics set and the size, which are the two persisted -----
        /// SetUpGraphicsBox (LTANK_D.C:1153): kill the sheet, load the new one,
        /// repaint.  Loading is Packs.Load, which is GFXInit's three branches
        /// including its fall back to the internal sheet.
        internal void ApplyPack(Pack p)
        {
            _pack = p;
            _atlas = Packs.Load(p, _opt.GraphicsDir, out string fallback);
            _error = fallback;
            QueueRedraw();
        }

        /// GraphBox's Close and Cancel, which both write the mode and (in mode
        /// 2) the file name (LTANK_D.C:1247).
        internal void PersistGraphics()
        {
            if (_pack != null) _opt.SetGraphics(_pack.Mode, _pack.File);
        }

        /// SetGameSize (LTANK2.C:1729), less the window furniture: sizes 1..3,
        /// persisted to [SCREEN] Size on the spot as it does.
        internal void SetSize(int size)
        {
            _size = Math.Clamp(size, 1, 3);
            _opt.SetSize(_size);
            Resize();
            QueueRedraw();
        }

        /// `--check-options`: what the options layer resolved to, for
        /// tools/options_check.py to compare against the INI it wrote.  The
        /// sheet hash is the part that proves the *pixels* followed the option
        /// and not just the label -- it is the same sha256 --check-sheets
        /// prints, so an external pack unpacked out of a .ltg must match that
        /// .ltg exactly.
        private int CheckOptions(string levels, int level)
        {
            byte[] h = System.Security.Cryptography.SHA256.HashData(_atlas.Sheet.Rgba);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            GD.PrintRaw(string.Format(inv,
                "options ini={0}\n" +
                "options size={1} cell={2} laser_offset={3}\n" +
                "options graphics_mode={4} graphics_file={5} graphics_dir={6}\n" +
                "options pack={7} label={8} sha256={9}\n" +
                "options rll={10} rll_file={11} rll_level={12}\n" +
                "options sound={13} animation={14} auto_record={15}\n" +
                "options player={16} record_author={17}\n",
                _opt.Ini.Path, _size, Cell, LaserOffset,
                _pack.Mode, _pack.File.Length > 0 ? _pack.File : "-", _opt.GraphicsDir,
                _pack.Mode == 1 ? "external" : _pack.Mode == 0 ? "internal" : _pack.File,
                _atlas.Label, Convert.ToHexString(h).ToLowerInvariant(),
                _opt.RememberLastLevel ? "Yes" : "No",
                _opt.LastLevelFile.Length > 0 ? _opt.LastLevelFile : "-", _opt.LastLevel,
                _opt.SoundOn ? "Yes" : "No",
                // Step 4's three: the option that moves a trace, the one that
                // starts the recorder, and the two names.
                _opt.AnimationOn ? "Yes" : "No", _opt.AutoRecord ? "Yes" : "No",
                _opt.Player.Length > 0 ? _opt.Player : "-",
                _opt.RecordAuthor.Length > 0 ? _opt.RecordAuthor : "-"));
            // What the level resolution above settled on -- the collection and
            // the number this run would have opened.
            GD.PrintRaw(string.Format(inv, "options start_file={0} start_level={1}\n",
                                      levels, level));

            // The menu's own list, which is GetLTGFiles (LTANK_D.C:1170) plus
            // the two radio buttons.  Printed so the gate can check the list
            // itself -- that the .ltg files are found, that they are named by
            // their header and not their file name, and that "User Graphics"
            // knows whether the pair is actually there.
            GD.PrintRaw(string.Format(inv, "options packs={0}\n", _packs.Count));
            for (int i = 0; i < _packs.Count; i++)
            {
                Pack p = _packs[i];
                GD.PrintRaw(string.Format(inv, "pack {0} mode={1} available={2} file={3} " +
                                               "label={4}\n",
                                          i, p.Mode, p.Available ? 1 : 0,
                                          p.File.Length > 0 ? p.File : "-", p.Label));
            }
            return _error == null ? 0 : 1;
        }

        // ---- the tick, LTANK.C:579 -----------------------------------------
        /// WM_TIMER.  Session.Step is the whole 50 ms unit and returns false
        /// when GameOn(FALSE) has stopped the timer -- winning, dying, or no
        /// level loaded.
        public override void _PhysicsProcess(double delta)
        {
            if (!_driving) return;
            // Command 106 is the one dialog in this port that stops the clock,
            // because it is the one the original stops it for: `x = Game_On;
            // GameOn(FALSE); DialogBox(...)` (LTANK.C:906).  The graphics dialog
            // (226) and the two score lists (113, 906) do not, so the tank can
            // die while they are up -- see GraphicsMenu and LevelList.StopsClock.
            if (_list != null && _list.Open && _list.StopsClock) return;
            if (_s == null || !_s.Step()) return;
            // The tick's sounds, after Tick() *and* Pump(): a drowning death
            // posts WM_Dead, so S_Die belongs to the tick that caused it
            // (quirk #8).  Only the last is audible -- PlaySound is
            // monophonic; see Sfx.
            _sfx?.PlayTick(_s.Sounds);
        }

        /// Rendering only.  Nothing here may touch the game.
        public override void _Process(double delta) => QueueRedraw();

        public override void _UnhandledInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton mb) { MouseButton(mb); return; }
            if (ev is InputEventMouseMotion mm) { MouseMotion(mm); return; }
            if (ev is not InputEventKey k || !k.Pressed) return;

            // The graphics menu is a modal dialog: while it is up the main
            // window has no focus, so no WM_KEYDOWN fires and nothing reaches
            // AddKBuff -- not even the arrows and space, which is what makes it
            // safe for the menu to navigate with them.  The *timer* is not
            // modal, though: command 226 never calls GameOn(FALSE) and
            // DialogBox's loop still dispatches WM_TIMER, so the game below
            // keeps ticking and an exposed tank can die while you pick a pack.
            if (_menu != null && _menu.Open)
            {
                _menu.Key(k.Keycode);
                GetViewport().SetInputAsHandled();
                return;
            }

            // Same for the level picker and the two score lists.  On Enter they
            // hand back a level number, which is `EndDialog(Dialog, i + 100)`
            // and the `if (i > 100)` that meets it (LTANK.C:910).
            if (_list != null && _list.Open)
            {
                _list.Key(k.Keycode);
                if (_list.Chosen > 0) _s?.Load(_list.Chosen);
                GetViewport().SetInputAsHandled();
                return;
            }

            // PBWindow is a DialogBox too (LTANK_D.C:1003), so while it is up
            // the keys are its four buttons and none reach AddKBuff -- which is
            // the whole point: a playback is watched, not played.  The clock
            // keeps running, as it does behind the graphics dialog.
            if (_s != null && _s.Pb.PanelUp)
            {
                PlaybackKey(k.Keycode);
                GetViewport().SetInputAsHandled();
                return;
            }

            // The editor is a *mode of this window*, not a dialog over it:
            // command 201 swaps the menu bar and the accelerator table
            // (LTANK.C:1446 picks hAccelTable2 when EditorOn), so while it is on
            // the play keys are gone -- there is nothing to drive.  F9 is in
            // both tables, which is why one key toggles.
            if (_edit != null && _edit.Open)
            {
                if (!_edit.Key(k))
                {
                    // The one accelerator ACC2 shares with ACC1: Ctrl+G, the
                    // graphics dialog.  It falls through to the block below.
                    if (k.Keycode == Key.G && k.CtrlPressed) _menu.Show(_packs, _pack);
                }
                if (!_edit.Open) Resize();       // it left
                GetViewport().SetInputAsHandled();
                return;
            }

            // The game keys first, and untouched: Session.Key is the original's
            // WM_KEYDOWN filter (VK 32..40, auto-repeat dropped only while a
            // key is still pending) feeding AddKBuff.  Everything below it is
            // ours, and deliberately outside 32..40 so no binding of ours can
            // ever eat a byte the recording needed.
            int vk = ToVk(k.Keycode);
            if (vk != 0)
            {
                _s?.Key(vk, k.Echo);
                GetViewport().SetInputAsHandled();
                return;
            }
            // **Auto-repeat, and why one key keeps it.**  A Win32 accelerator
            // repeats: auto-repeat WM_KEYDOWNs are ordinary WM_KEYDOWNs and
            // TranslateAccelerator translates every one of them, so holding U
            // in the 2010 binary walks the undo buffer back a step per repeat.
            // Dropping every echo here made U a one-shot, which is a port
            // regression rather than a UI choice -- so the keys where repeating
            // *is* what the key means are let through.  The rest stay one-shot
            // on purpose: the original repeats those too, but a held S that
            // skips forty levels or a held Ctrl+V that restores the same
            // position forty times is a misfire, and this is the UI half of the
            // port, which is written down rather than locked down.
            if (k.Echo && !RepeatsOnHold(k.Keycode)) return;

            // **The bindings are the original's accelerator table**
            // (`ACC1 ACCELERATORS`, lt32l_us.inc:120), which is a table, and the
            // rule in this project when the original has a table is to read it.
            // Step 4 is where that became affordable -- there are enough
            // commands now for an invented set to be worse than the real one --
            // and it moved two of step 2's keys: the sound is N, not S (S is
            // Skip Level), and the graphics dialog is Ctrl+G, not G (G is the
            // global high-score list).  `[`/`]` and I are this port's own and
            // have no accelerator; every one of them is outside VK 32..40, so
            // none can eat a byte a recording needed.
            bool ctrl = k.CtrlPressed;
            switch (k.Keycode)
            {
                // ---- levels -------------------------------------------------
                case Key.L: OpenList(ListMode.Levels); break;         // 106
                case Key.S: _s?.Load(_s.Level + 1); break;            // 107
                case Key.P: _s?.Load(_s.Level - 1); break;            // 119
                case Key.Bracketright: _s?.Load(_s.Level + 1); break; // ours
                case Key.Bracketleft: _s?.Load(_s.Level - 1); break;  // ours
                case Key.Enter:
                    // The original's flag case calls LoadNextLevel straight
                    // away (LTANK.C:655); a Godot win waits, so the recording
                    // is still there to save.
                    if (_s != null && _s.Now == Session.State.Won) _s.Load(_s.Level + 1);
                    break;
                case Key.R: _s?.Restart(); break;                     // 105
                case Key.F2: NewGame(); break;                        // 101

                // ---- undo and the saved position ----------------------------
                case Key.U:                                           // 110
                    // A dead game takes the DeadBox's Undo, which is 110 plus
                    // GameOn(TRUE) -- the only way back from a death, and the
                    // only choice the original's dialog offers besides Restart.
                    bool undone = _s != null && (_s.Now == Session.State.Dead
                                                 ? _s.UndoDead() : _s.Undo());
                    if (!undone) _error = "nothing to undo";
                    break;
                case Key.C when ctrl: _s?.SavePos(); _error = "position saved"; break;
                case Key.V when ctrl:                                 // 112
                    if (_s != null && !_s.RestorePos()) _error = "no saved position";
                    break;

                // ---- scores -------------------------------------------------
                case Key.V: OpenList(ListMode.MyScores); break;       // 113
                case Key.G when !ctrl: OpenList(ListMode.GlobalScores); break;   // 906

                // ---- recording and playback ---------------------------------
                case Key.F5: ToggleRecording(); break;                // 123
                case Key.F6: SaveRecording(); break;                  // 117
                case Key.F7: OpenPlayback(); break;                   // 114
                case Key.F4: _s?.Replay(); break;                     // 124

                // ---- options ------------------------------------------------
                // Command 226, the Options menu's "Graphics" (LTANK.C:1122).
                case Key.G when ctrl: _menu.Show(_packs, _pack); break;
                // Commands 120/121/122, the Options menu's three sizes.
                case Key.Z: SetSize(_size % 3 + 1); break;
                case Key.I: _interpolate = !_interpolate; break;
                // Command 102, "Sound" (LTANK.C:875).  The checkmark is the INI
                // here; ToggleOpt writes it immediately.
                case Key.N:
                    bool on = _opt.ToggleSound();
                    if (_sfx != null) _sfx.SoundOn = on;
                    break;
                // Command 104, "Animation" (LTANK.C:886).  The only option that
                // changes what a tick does, so it takes effect on the next level
                // load rather than mid-level: Ani_On is read by Session.Load.
                case Key.A:
                    _error = "animation " + (_opt.ToggleAnimation() ? "on" : "off")
                             + " -- next level";
                    break;
                // Command 115, "Auto Record" (LTANK.C:978), which also turns the
                // recorder itself on or off.
                case Key.F8:
                    _error = "auto-record "
                             + (_s != null && _s.Rec2.ToggleAutoRecord() ? "on" : "off");
                    break;

                // Command 201, "Editor" -- VK_F9 in ACC1 (lt32l_us.inc:132).
                case Key.F9:
                    if (_s?.E != null) { _edit.Enter(_s); Resize(); }
                    break;

                case Key.Escape: GetTree().Quit(); break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
        }

        /// The accelerators that repeat while the key is held.  Undo is the
        /// one so far: walking a mistake back is a *rate*, not an event, and
        /// the original's accelerator repeated it -- see the echo test above.
        /// The repeat rate is the OS's, not the game's 20 Hz tick, which is
        /// right: UndoStep is not a tick, it is a WM_COMMAND, and the original
        /// took it as fast as Windows sent it.
        private static bool RepeatsOnHold(Key k) => k == Key.U;

        // ---- the mouse, WM_?BUTTONDOWN and WM_MOUSEMOVE ---------------------
        //
        // **The window proc has two arms and so does this** (LTANK.C:785).  In
        // the editor a click paints; out of it, a click is a *move order* --
        // pushed into `MBuffer` and turned into arrow keys by `MouseOperation`
        // on a later tick, which is the function Phase 2 left unported because
        // nothing could reach it.  Both arms are live here for the first time.
        //
        // The dialogs come first for the same reason they do for keys: a modal
        // window over the board takes the mouse with it, and clicking through
        // one would drive a tank the player cannot see.
        private void MouseButton(InputEventMouseButton mb)
        {
            if (!mb.Pressed) { _held = 0; return; }
            if (_menu != null && _menu.Open) return;
            if (_list != null && _list.Open) return;
            if (_s != null && _s.Pb.PanelUp) return;

            int button = mb.ButtonIndex switch
            {
                Godot.MouseButton.Left => 1,
                Godot.MouseButton.Right => 2,
                _ => 0,
            };
            if (button == 0) return;
            _held = button;

            if (_edit != null && _edit.Open)
            {
                _edit.Click(mb.Position, button, mb.ShiftPressed);
                GetViewport().SetInputAsHandled();
                return;
            }

            // The play arm.  `Engine.MouseClick` is the ring-buffer push out of
            // the window proc; nothing decides anything here, and the click is
            // queued even while the tank is mid-slide, exactly as a window
            // message is.
            if (_s?.E == null) return;
            int cell = Cell;
            int x = (int)Math.Floor((mb.Position.X - Margin) / (float)cell);
            int y = (int)Math.Floor((mb.Position.Y - Margin) / (float)cell);
            if (x < 0 || x > 15 || y < 0 || y > 15) return;
            _s.E.MouseClick(x, y, button);
            GetViewport().SetInputAsHandled();
        }

        /// WM_MOUSEMOVE (LTANK.C:764).  **Only the editor has one**: out of the
        /// editor the original's WM_MOUSEMOVE case does nothing at all, so
        /// dragging across the board while playing queues nothing.
        private void MouseMotion(InputEventMouseMotion mm)
        {
            if (_held == 0 || _edit == null || !_edit.Open) return;
            _edit.Drag(mm.Position, _held, mm.ShiftPressed);
        }

        /// Which button is down, for the drag.  The original reads it out of
        /// `wparam`'s MK_LBUTTON / MK_RBUTTON on every WM_MOUSEMOVE; Godot
        /// delivers press and release, so it is kept here instead.
        private int _held;

        /// Godot keycodes -> Win32 virtual-key codes, for the nine keys
        /// LTANK.C:572's `(wparam < 32) || (wparam > 40)` admits.  33..36 are
        /// the recordable one-tick wait; see Session.Key.
        private static int ToVk(Key k) => k switch
        {
            Key.Space => 32,
            Key.Pageup => 33,
            Key.Pagedown => 34,
            Key.End => 35,
            Key.Home => 36,
            Key.Left => 37,
            Key.Up => 38,
            Key.Right => 39,
            Key.Down => 40,
            _ => 0,
        };

        /// Command 117 (F6) reaching WM_SaveRec.  The original opens a Save
        /// dialog; this writes to out/recordings/ under the name BuildPB_Name
        /// would have offered, which is where step 1's F6 has always written and
        /// what tools/tick_check.py reads.
        private void SaveRecording()
        {
            if (_s?.E == null) return;
            try
            {
                string dir = Path.Combine(Paths.Root, "out", "recordings");
                string p = _s.SaveRecording(dir);
                // `if (Recording)` is command 117's own guard -- F6 does nothing
                // at all when the recorder is off, which is worth saying out
                // loud rather than looking broken.
                _error = p == null ? "not recording -- F5 starts it"
                                   : "saved " + Path.GetFileName(p);
            }
            catch (Exception ex) { _error = ex.Message; }
        }

        /// Command 123 (F5).  The original's checkmark is the window title
        /// ("LaserTank *** RECORDING ***", txt046); here it is the HUD.
        private void ToggleRecording()
        {
            if (_s == null) return;
            _error = _s.Rec2.Toggle() ? "recording" : "recording off";
        }

        /// Command 101, New Game (F2): back to the remembered level, or level 1.
        /// `LastLevel = CurLevel; CurLevel = 0; if (RLL) CurLevel = [DATA]
        /// RLLLevel - 1; LoadNextLevel` (LTANK.C:864) -- so with Remember Last
        /// Level off it really does start over at 1.
        private void NewGame()
        {
            if (_s == null) return;
            int want = _opt.RememberLastLevel ? _opt.LastLevel : 1;
            _s.Load(want < 1 ? 1 : want);
        }

        /// PBWindow's WM_COMMAND (LTANK_D.C:1054), which is four buttons and a
        /// radio group of three.
        private void PlaybackKey(Key k)
        {
            Playback pb = _s.Pb;
            switch (k)
            {
                case Key.Space:
                case Key.Enter:
                case Key.KpEnter:
                    // ID_PLAYBOX_02, whose label is txt017/txt018 -- "&Play" and
                    // "&Pause", one control that reads as whichever it will do
                    // next.
                    pb.TogglePlay(_s.E);
                    break;
                case Key.R:                                  // ID_PLAYBOX_03, Reset
                    _s.Replay();
                    break;
                case Key.Key1: pb.SetSpeed(_s.E, PbSpeed.Fast); break;   // _04
                case Key.Key2: pb.SetSpeed(_s.E, PbSpeed.Slow); break;   // _05
                case Key.Key3: pb.SetSpeed(_s.E, PbSpeed.Step); break;   // _06
                default:
                    // ID_PLAYBOX_01 / Cancel: the recording becomes what was
                    // actually watched.  See Playback.Close.
                    pb.Close(_s.E);
                    break;
            }
        }

        private void OpenList(ListMode mode)
        {
            if (_s == null) return;
            _list.Show(mode, _s.LevelPath, _s.Level);
        }

        /// Command 114 (F7), PlayBack Recording.  The original opens a file
        /// dialog; there is no file dialog here, so the two names BuildPB_Name
        /// would have offered are tried in order -- out/recordings/ first,
        /// because that is where this port's own F6 writes, then beside the
        /// .lvl, which is where the corpus in data/demos/ lives.
        private void OpenPlayback()
        {
            if (_s == null) return;
            foreach (string cand in Playback.Candidates(Paths.Root, _s.LevelPath, _s.Level))
            {
                if (!File.Exists(cand)) continue;
                string bad = _s.LoadPlayback(cand);
                _error = bad ?? ("playing " + Path.GetFileName(cand));
                return;
            }
            _error = "no recording for this level in out/recordings/ or beside the .lvl";
        }

        public override void _Draw()
        {
            Font font = ThemeDB.FallbackFont;
            if (_s?.E == null || _atlas == null)
            {
                DrawString(font, new Vector2(Margin, Margin + 16), _error ?? "no level",
                           HorizontalAlignment.Left, -1, 16, Colors.OrangeRed);
                return;
            }

            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    DrawCell(x, y);
            DrawTank();
            DrawLaser();
            DrawHud(font);
            // The dialogs, over the board and under nothing: the original's are
            // modal windows on top of the game, which keeps playing behind them.
            var board = new Rect2(Margin, Margin, 16 * Cell, 16 * Cell);
            _edit?.Draw(this, font, _atlas);
            if (_menu.Open) _menu.Draw(this, font, board);
            if (_list.Open) _list.Draw(this, font, _mono, board);
            if (_s.Pb.PanelUp) DrawPlaybackPanel(font, board);
        }

        private Rect2 CellRect(int x, int y) =>
            new Rect2(Margin + x * Cell, Margin + y * Cell, Cell, Cell);

        /// UpDateSprite (LTANK2.C:490), transliterated: a tunnel is a solid
        /// colour with sprite 55 masked on top; a transparent sprite gets the
        /// under-bitmap painted first; anything else is a plain copy.
        private void DrawCell(int x, int y)
        {
            TGAMEREC g = _s.E.Game;
            int bmn = g.BMF[x, y];
            Rect2 dst = CellRect(x, y);

            if (bmn == Gfx.TunnelBM)
            {
                DrawTunnel(dst, Obj.GetTunnelID(g.PF[x, y]));
                return;
            }
            if (Gfx.BMSTA[bmn] == 1)
            {
                int bmn2 = g.BMF2[x, y];
                if (bmn2 == Gfx.TunnelBM) DrawTunnel(dst, (g.PF2[x, y] & 0x0F) >> 1);
                else Blit(bmn2, dst);
            }
            Blit(bmn, dst);
        }

        /// The original fills the cell with a GDI Rectangle() in ColorList[id]
        /// -- brush plus the default one-pixel black pen -- and then blits the
        /// tunnel sprite masked over it.
        private void DrawTunnel(Rect2 dst, int id)
        {
            Gfx.Rgb(Gfx.ColorList[id & 7], out byte r, out byte gg, out byte b);
            Fill(dst, Color.Color8(r, gg, b));
            Blit(Gfx.TunnelBM, dst);
        }

        private void Blit(int bm, Rect2 dst)
        {
            if (_atlas.Region(bm, out Rect2 src))
                DrawTextureRectRegion(_atlas.Texture, dst, src);
        }

        /// UpDateTank (LTANK2.C:536): sprite 1 + Tank.Dir, masked, over whatever
        /// the cell already holds.  The tank's own PF cell is zeroed by
        /// BuildBMField, so the cell under it is drawn as dirt.
        ///
        /// The interpolation is ours and is guarded twice: only while the timer
        /// is running (a finished game has no tick in flight to be part-way
        /// through) and only between adjacent cells, so a tunnel does not slide
        /// the tank across the board.  Rounded to whole pixels because the
        /// sheet is nearest-filtered pixel art.
        private void DrawTank()
        {
            TTANKREC t = _s.E.Game.Tank;
            Rect2 dst = CellRect(t.X, t.Y);

            int dx = t.X - _s.PrevTankX, dy = t.Y - _s.PrevTankY;
            if (_interpolate && _driving && _s.E.Game_On
                && Math.Abs(dx) + Math.Abs(dy) == 1)
            {
                float back = 1f - (float)Godot.Engine.GetPhysicsInterpolationFraction();
                dst.Position -= new Vector2(Mathf.Round(dx * back * Cell),
                                            Mathf.Round(dy * back * Cell));
            }
            Blit(1 + t.Dir, dst);
        }

        /// UpDateLaser (LTANK2.C:549) and UpDateLaserBounce (:565), as paint
        /// only.  **UpDateLaserBounce also sets LaserBounceOnIce** (hazard #1),
        /// which makes MoveLaser take a second step in the same tick -- the
        /// core already calls it inside the tick, so this must not call, skip or
        /// reimplement it.  Session recovers the one thing the paint call knows
        /// and the state does not, the laser's incoming direction, by watching
        /// laser.Dir across the tick.
        ///
        /// Game.Tank.Firing is the "a laser exists" flag: MoveLaser clears it
        /// and erases the cell in the same breath when the shot stops.  Colour
        /// is laser.Good -- FireLaser's `laser.Good = (sf == 2)` -- green for
        /// the tank's own shot, red for an anti-tank's (LTANK2.C:1649).
        ///
        /// One thing retained-mode drawing cannot reproduce: when a laser
        /// bounces off a mirror that is itself sliding on ice, MoveLaser takes
        /// two steps in one tick and the original painted both cells.  Only the
        /// second is visible here.  Two recordings in the whole corpus reach it.
        private void DrawLaser()
        {
            if (_s.E.Game.Tank.Firing == 0) return;
            TTANKREC l = _s.E.laser;
            Gfx.Rgb(l.Good != 0 ? 0x0000FF00u : 0x000000FFu,
                    out byte r, out byte g, out byte b);
            Color c = Color.Color8(r, g, b);

            if (_s.LaserBounced)
            {
                Bar(l.X, l.Y, _s.LaserFromDir, true, c);
                Bar(l.X, l.Y, l.Dir, false, c);
            }
            else
            {
                Rect2 cell = CellRect(l.X, l.Y);
                float o = LaserOffset;
                Fill((l.Dir & 1) == 1
                        ? new Rect2(cell.Position.X + o, cell.Position.Y, Cell - 2 * o, Cell)
                        : new Rect2(cell.Position.X, cell.Position.Y + o, Cell, Cell - 2 * o),
                     c);
            }
        }

        /// One half-bar of UpDateLaserBounce: the half of the cell on the side
        /// the laser came in from (`incoming`) or left by.  Direction is the
        /// original's 1 = up, 2 = right, 3 = down, 4 = left, and an incoming
        /// direction paints the *opposite* half -- a shot travelling up entered
        /// through the bottom.
        private void Bar(int cx, int cy, int dir, bool incoming, Color c)
        {
            Rect2 cell = CellRect(cx, cy);
            float o = LaserOffset;
            float h = Cell / 2;      // h = SpBm_Width / 2, integer division
            float x = cell.Position.X, y = cell.Position.Y;
            bool far = incoming;                       // the half behind the laser
            Fill(dir switch
            {
                1 => new Rect2(x + o, far ? y + h : y, Cell - 2 * o, far ? Cell - h : h),
                2 => new Rect2(far ? x : x + h, y + o, far ? h : Cell - h, Cell - 2 * o),
                3 => new Rect2(x + o, far ? y : y + h, Cell - 2 * o, far ? h : Cell - h),
                _ => new Rect2(far ? x + h : x, y + o, far ? Cell - h : h, Cell - 2 * o),
            }, c);
        }

        /// GDI Rectangle() is a brush fill plus the default one-pixel black pen,
        /// which is why the laser and the tunnels have an outline in the
        /// original -- and the pen goes **inside** the rectangle: it spans
        /// left..right-1, so the border eats a pixel of the fill on all four
        /// sides and never touches the neighbouring cell.
        ///
        /// Godot's `DrawRect(filled: false, width: 1)` strokes *centred* on the
        /// edge, and at width 1 that rounds the outline outside the rect on the
        /// top and left and inside it on the bottom and right -- an asymmetric
        /// border that bled a pixel into the cell above and to the left.  Two
        /// fills instead: the border, then the interior.  Measured rather than
        /// reasoned about; tools/options_check.py reads the bar back out of a
        /// PNG and would fail again if this drifted.
        private void Fill(Rect2 rect, Color c)
        {
            DrawRect(rect, Colors.Black);
            DrawRect(new Rect2(rect.Position + Vector2.One, rect.Size - 2 * Vector2.One), c);
        }

        /// HSBox (LTANK_D.C:598) as one line.  That dialog's whole content is
        /// this: the score just made, the previous personal best if there was
        /// one (txt008), and either the posted best (txt009) or
        /// "Congratulation's You beat it !!" (txt012) when the posted best has
        /// been beaten.  What the dialog also does -- ask for the initials --
        /// is `[DATA] Player` here, read before the write rather than after.
        private string WinLine()
        {
            ScoreResult r = _s.Score;
            if (r == null)
                return _s.Pb.Open ? "playback reached the flag"
                                  : "SOLVED -- Enter for the next level, F6 saves it";
            string s = "SOLVED";
            if (r.Global) s += " -- Congratulation's You beat it !!";
            else if (r.Personal) s += " -- your best yet";
            else s += " -- your best stands at " + HighScores.Describe(r.Old);
            if (r.Target != null && !r.Global)
                s += "   (par " + r.Target.Moves + "/" + r.Target.Shots + ")";
            if (r.Error != null) s += "   [.hs not written: " + r.Error + "]";
            return s + "   Enter next, F6 saves";
        }

        /// PBWindow (LTANK_D.C:1003) as a strip at the bottom of the board.  The
        /// original positions its dialog beside the game window
        /// (`SetWindowPos(..., Box.left + ContXPos + 2, Box.top + 280, ...)`);
        /// there is no second column here, so it goes over the board's foot,
        /// where it hides two rows rather than the whole thing.
        private void DrawPlaybackPanel(Font font, Rect2 board)
        {
            Playback pb = _s.Pb;
            var panel = new Rect2(board.Position.X + 6, board.End.Y - 74,
                                  board.Size.X - 12, 68);
            DrawRect(panel, new Color(0.05f, 0.06f, 0.08f, 0.96f));
            DrawRect(panel, new Color(0.55f, 0.60f, 0.70f), false, 1);
            float x = panel.Position.X + 9, w = panel.Size.X - 18;
            float y = panel.Position.Y + 17;

            // txt013 + LName + txt014 + Author: "Playback Level : " and
            // "\nRecorded by " (LANGUAGE.C:54).
            DrawString(font, new Vector2(x, y),
                       $"Playback Level : {pb.Rec.LName}   Recorded by {pb.Rec.Author}",
                       HorizontalAlignment.Left, w, 13, Colors.White);
            // ID_PLAYBOX_09 / _10: the count of keys played, over the total.
            DrawString(font, new Vector2(x, y + 18),
                       $"{_s.E.Game.RecP} / {pb.Rec.DataSize}    "
                       + (_s.E.PlayBack ? "playing" : "paused") + "    "
                       + pb.Speed.ToString().ToLowerInvariant(),
                       HorizontalAlignment.Left, w, 12, Colors.LightGreen);
            DrawString(font, new Vector2(x, y + 36),
                       "space play/pause   1 fast  2 slow  3 step   R reset   "
                       + "any other key closes",
                       HorizontalAlignment.Left, w, 11, Colors.Gray);
        }

        /// The original's own status strip is a bitmap panel beside the board
        /// (ContXPos, LTANK.C:556) showing the level name, the author and the
        /// two counters.  Same information, laid out for this window.  Every
        /// line is width-clipped, so the 24 px zoom truncates rather than
        /// spilling past the board.
        private void DrawHud(Font font)
        {
            float y = Margin + 16 * Cell + 20;
            float w = 16 * Cell;
            TLEVEL lv = _s.Rec;
            TGAMEREC g = _s.E.Game;

            // One column, never right-aligned: the window is only as wide as
            // the board, and at the 24 px zoom that is 384 px -- two columns
            // collide there.
            // The level number carries its difficulty name and takes its colour
            // from it, which is what the original's panel does -- `itoa(CurLevel)`
            // then `strcat(txt023..027)` and `SetTextColor(DifCList[1..5])`
            // (LTANK.C:532).
            var info = new TLEVELINFO { SDiff = lv.SDiff };
            string head = $"{_s.Level}{info.DiffName}/{_s.LevelCount}  {lv.LName}";
            if (!string.IsNullOrEmpty(lv.Author)) head += $"   by {lv.Author}";
            DrawString(font, new Vector2(Margin, y), head,
                       HorizontalAlignment.Left, w, 16, Colors.White);

            // The .ghs target beside the live score, because that is the number
            // a player is actually chasing.  The .hs half is only interesting
            // once it exists.
            string par = "";
            if (LevelFile.ReadHighScore(_s.Files.Ghs, _s.Level, out ushort tm, out ushort ts))
                par = $"   par {tm}/{ts}";
            // Single-spaced, and every word short: the window is only as wide as
            // the board, and at the default 24 px zoom that is 384 px.  Both
            // this line and the three legends below are written to fit *there*
            // rather than at the size they were composed at -- step 4 added a
            // dozen keys and clipped the legend mid-word before this was
            // measured.  DrawString's `w` clips rather than spilling, so a line
            // that is too long loses its tail silently.
            DrawString(font, new Vector2(Margin, y + 20),
                       $"moves {g.ScoreMove}  shots {g.ScoreShot}{par}  " +
                       $"{_atlas.Label}  {(_interpolate ? "smooth" : "snap")}  " +
                       $"{(_opt.SoundOn ? "sound" : "muted")}" +
                       (_s.Rec2.Recording ? "  *** REC ***" : ""),
                       HorizontalAlignment.Left, w, 14,
                       _s.Rec2.Recording ? Colors.Khaki : Colors.White);

            // In the editor the win/dead line has nothing to say -- the clock
            // is stopped and the board on screen is not the one anything
            // happened on -- so the strip carries the editor's own status
            // instead, which is where a save says where it went.
            bool editing = _edit != null && _edit.Open;
            (string what, Color tint) = editing
                ? (_edit.Status ?? "", Colors.Yellow)
                : _s.Now switch
                {
                    Session.State.Won => (WinLine(), Colors.LightGreen),
                    Session.State.Dead => ("DEAD -- U undoes the last move, R restarts",
                                           Colors.OrangeRed),
                    _ => (_error ?? "", Colors.Yellow),
                };
            if (what != "")
                DrawString(font, new Vector2(Margin, y + 38), what,
                           HorizontalAlignment.Left, w, 14, tint);

            // Four short lines rather than three long ones, for the same reason.
            // The editor's are the palette's, so the strip only names the two
            // keys that are *not* on the panel beside it.
            string[] legend = editing
                ? new[]
                {
                    "EDITOR -- the palette beside the board has the rest",
                    "F9 leaves and resumes play on the board you drew",
                    "ctrl+S saves; a level out of data/ goes to out/levels/",
                    "ctrl+G gfx  Z size  Esc leaves",
                }
                : new[]
            {
                "arrows move  space fires  U undo  R restart  ctrl+C/V pos",
                "L levels  V scores  G global  S/P next/prev  ctrl+G gfx",
                "F5 rec  F6 save  F7 play  F4 replay  F8 auto-rec",
                "Z size  I smooth  N sound  A anim  F9 editor  Esc quit",
            };
            for (int i = 0; i < legend.Length; i++)
                DrawString(font, new Vector2(Margin, y + 56 + 16 * i), legend[i],
                           HorizontalAlignment.Left, w, 12, Colors.Gray);
            if (!string.IsNullOrEmpty(lv.Hint))
                DrawString(font, new Vector2(Margin, y + 56 + 16 * legend.Length + 4),
                           lv.Hint.Replace("\r\n", " "),
                           HorizontalAlignment.Left, w, 12, Colors.DarkGray);
        }
    }

    /// Headless self-check for the atlas half of Phase 5 step 0's exit
    /// criterion: every shipped pack decodes to the 320x192 sheet.  The hash is
    /// what makes it a cross-check rather than a smoke test -- tools/
    /// atlas_check.py decodes the same files in Python and must agree.
    ///
    ///   godot --headless --path src/LaserTank.Game -- --check-sheets
    public static class SheetCheck
    {
        public static int Run()
        {
            int bad = 0;
            foreach (string p in Paths.GraphicsPacks())
            {
                string label = string.IsNullOrEmpty(p) ? "internal" : Path.GetFileName(p);
                try
                {
                    SpriteSheet s = string.IsNullOrEmpty(p)
                        ? SpriteSheet.FromBmpPair(Paths.InternalGameBmp, Paths.InternalMaskBmp)
                        : SpriteSheet.FromLtg(p);
                    byte[] h = System.Security.Cryptography.SHA256.HashData(s.Rgba);
                    GD.PrintRaw($"sheet {label} {Gfx.SheetW}x{Gfx.SheetH} " +
                                $"sha256={Convert.ToHexString(h).ToLowerInvariant()}\n");
                }
                catch (Exception ex) { bad++; GD.PrintRaw($"sheet {label} FAIL {ex.Message}\n"); }
            }
            GD.PrintRaw(bad == 0 ? "sheets OK\n" : $"sheets FAILED ({bad})\n");
            return bad == 0 ? 0 : 1;
        }
    }
}
