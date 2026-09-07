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
        // SetGameSize (LTANK2.C:1729) offers exactly these three.  The original
        // resamples the whole sheet at load; we scale at draw time instead, so
        // switching zoom here changes nothing but pixels.  (Hazard #11 lives in
        // that function -- `if (GFXOn) GFXKill;`, missing its parens.  It stays
        // missing; nothing here calls it.)
        private static readonly int[] Zooms = { 24, 32, 40 };
        private int _zoom = 1;
        private int Cell => Zooms[_zoom];

        private const int Margin = 16;
        private const int HudH = 126;

        /// LaserOffset (LTANK2.C:46) is 10 of a 32 px sprite -- the laser bar is
        /// 12 px wide down the middle of its cell.  Kept as a fraction so the
        /// three zooms all get the same picture.
        private const float LaserFrac = 10f / 32f;

        private Session _s;
        private Atlas _atlas;
        private string[] _packs;
        private int _pack;
        private string _error;

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

            // The tick rate is a project setting, so a stale project.godot
            // would silently play the game at 60 Hz.  Fail loudly instead.
            int tps = Godot.Engine.PhysicsTicksPerSecond;
            if (tps != 1000 / Session.GameDelayMs)
                throw new InvalidOperationException(
                    $"physics_ticks_per_second is {tps}, must be " +
                    $"{1000 / Session.GameDelayMs} (GameDelay = {Session.GameDelayMs} ms)");

            _packs = Paths.GraphicsPacks();
            LoadPack(Arg(args, "--pack", 0));
            _zoom = Math.Clamp(Array.IndexOf(Zooms, Arg(args, "--zoom", 32)), 0, Zooms.Length - 1);

            string levels = ArgStr(args, "--levels") ?? Paths.Flagship;
            int level = Arg(args, "--level", 1);
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
                GetTree().Quit(ArgStr(args, "--lpb-list") is string list
                    ? PlayMode.RunList(list, outDir, maxTicks, pending, author)
                    : PlayMode.Run(levels, level, script, lpbName, outDir,
                                   maxTicks, pending, author));
                return;
            }

            _s = new Session(levels);
            if (!_s.Load(level)) _error = _s.Error;
            Resize();

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
            DisplayServer.WindowSetSize(
                new Vector2I(2 * Margin + 16 * Cell, 2 * Margin + 16 * Cell + HudH));
        }

        private async void Shot(string path)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Error err = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.PrintRaw($"shot {path} level {_s?.Level} tick {_s?.Ticks} " +
                        $"pack {_atlas?.Label} zoom {Cell} -> {err}\n");
            GetTree().Quit(err == Error.Ok ? 0 : 1);
        }

        private void LoadPack(int i)
        {
            _pack = ((i % _packs.Length) + _packs.Length) % _packs.Length;
            try { _atlas = Atlas.Load(_packs[_pack]); }
            catch (Exception ex) { _error = ex.Message; }
            QueueRedraw();
        }

        // ---- the tick, LTANK.C:579 -----------------------------------------
        /// WM_TIMER.  Session.Step is the whole 50 ms unit and returns false
        /// when GameOn(FALSE) has stopped the timer -- winning, dying, or no
        /// level loaded.
        public override void _PhysicsProcess(double delta)
        {
            if (_driving) _s?.Step();
        }

        /// Rendering only.  Nothing here may touch the game.
        public override void _Process(double delta) => QueueRedraw();

        public override void _UnhandledInput(InputEvent ev)
        {
            if (ev is not InputEventKey k || !k.Pressed) return;

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
            if (k.Echo) return;

            switch (k.Keycode)
            {
                case Key.Bracketright: _s?.Load(_s.Level + 1); break;
                case Key.Bracketleft: _s?.Load(_s.Level - 1); break;
                case Key.Enter:
                    // The original's flag case calls LoadNextLevel straight
                    // away (LTANK.C:655); a Godot win waits, so the recording
                    // is still there to save.
                    if (_s != null && _s.Now == Session.State.Won) _s.Load(_s.Level + 1);
                    break;
                case Key.R: _s?.Restart(); break;                    // command 105
                case Key.F6: SaveRecording(); break;                 // command 117
                case Key.G: LoadPack(_pack + 1); break;
                case Key.Z: _zoom = (_zoom + 1) % Zooms.Length; Resize(); break;
                case Key.I: _interpolate = !_interpolate; break;
                case Key.Escape: GetTree().Quit(); break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
        }

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

        private void SaveRecording()
        {
            if (_s?.E == null) return;
            try
            {
                string dir = Path.Combine(Paths.Root, "out", "recordings");
                _error = "saved " + _s.Save(dir);
            }
            catch (Exception ex) { _error = ex.Message; }
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
            DrawRect(dst, Color.Color8(r, gg, b));
            DrawRect(dst, Colors.Black, false, 1);
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
                float o = Mathf.Round(LaserFrac * Cell);
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
            float o = Mathf.Round(LaserFrac * Cell);
            float h = Mathf.Round(Cell / 2f);
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
        /// which is why the laser has an outline in the original.
        private void Fill(Rect2 rect, Color c)
        {
            DrawRect(rect, c);
            DrawRect(rect, Colors.Black, false, 1);
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
            string head = $"{_s.Level}/{_s.LevelCount}  {lv.LName}";
            if (!string.IsNullOrEmpty(lv.Author)) head += $"   by {lv.Author}";
            DrawString(font, new Vector2(Margin, y), head,
                       HorizontalAlignment.Left, w, 16, Colors.White);
            DrawString(font, new Vector2(Margin, y + 20),
                       $"moves {g.ScoreMove}   shots {g.ScoreShot}    " +
                       $"{_atlas.Label}  {Cell}px  {(_interpolate ? "smooth" : "snap")}",
                       HorizontalAlignment.Left, w, 14, Colors.White);

            (string what, Color tint) = _s.Now switch
            {
                Session.State.Won => ("SOLVED -- Enter for the next level, F6 saves it",
                                      Colors.LightGreen),
                Session.State.Dead => ("DEAD -- R restarts", Colors.OrangeRed),
                _ => (_error ?? "", Colors.Yellow),
            };
            if (what != "")
                DrawString(font, new Vector2(Margin, y + 38), what,
                           HorizontalAlignment.Left, w, 14, tint);

            DrawString(font, new Vector2(Margin, y + 56),
                       "arrows move, space fires, R restart, F6 saves the recording",
                       HorizontalAlignment.Left, w, 12, Colors.Gray);
            DrawString(font, new Vector2(Margin, y + 72),
                       "[ ] level, G graphics, Z zoom, I smooth, Esc quit",
                       HorizontalAlignment.Left, w, 12, Colors.Gray);
            if (!string.IsNullOrEmpty(lv.Hint))
                DrawString(font, new Vector2(Margin, y + 90), lv.Hint.Replace("\r\n", " "),
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
