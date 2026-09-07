// Phase 5, step 0: the board on screen.
//
// This node draws and nothing else.  It reads Game.BMF / Game.BMF2 / Game.PF2
// and the tank, exactly as UpDateSprite and UpDateTank do (LTANK2.C:490, :536),
// and it never decides anything -- no rule, no movement, no laser logic lives
// here.  There is no tick yet either: that is step 1, where a fixed 20 Hz timer
// drives Engine.Tick() and the gate is a Godot playthrough replaying in the C
// oracle.
//
// Read BMF, never re-derive it from PF.  BuildBMField is not GetOBM(PF): a
// tunnel is 55, the tank's own cell is 1 and its PF is zeroed, and Animate()
// then cycles BMF for animated objects.
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

        private Engine _e;
        private Atlas _atlas;
        private string[] _packs;
        private int _pack;
        private string _levels;
        private int _level = 1, _levelCount;
        private string _error;

        public override void _Ready()
        {
            string[] args = OS.GetCmdlineUserArgs();
            if (Array.IndexOf(args, "--check-sheets") >= 0)
            {
                GetTree().Quit(SheetCheck.Run());
                return;
            }

            _packs = Paths.GraphicsPacks();
            _levels = Paths.Flagship;
            _levelCount = LevelFile.CountLevels(_levels);
            LoadPack(Arg(args, "--pack", 0));
            _zoom = Math.Clamp(Array.IndexOf(Zooms, Arg(args, "--zoom", 32)), 0, Zooms.Length - 1);
            LoadLevel(Arg(args, "--level", 1));
            Resize();

            // `-- --shot FILE`: draw one frame, write a PNG, quit.  A rendered
            // board is the only honest evidence for step 0, and a file is
            // reviewable where a window is not.
            string shot = ArgStr(args, "--shot");
            if (shot != null) Shot(shot);
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
                new Vector2I(2 * Margin + 16 * Cell, 2 * Margin + 16 * Cell + 70));
        }

        private async void Shot(string path)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Error err = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.PrintRaw($"shot {path} level {_level} pack {_atlas?.Label} zoom {Cell} -> {err}\n");
            GetTree().Quit(err == Error.Ok ? 0 : 1);
        }

        private void LoadPack(int i)
        {
            _pack = ((i % _packs.Length) + _packs.Length) % _packs.Length;
            try { _atlas = Atlas.Load(_packs[_pack]); }
            catch (Exception ex) { _error = ex.Message; }
            QueueRedraw();
        }

        private void LoadLevel(int n)
        {
            if (n < 1) n = _levelCount;
            if (n > _levelCount) n = 1;
            // Quirk #12: LoadLevel leaves wasIce / WaitToTrans / ConvMoving /
            // BlackHole where the previous game left them, faithfully.  A fresh
            // Engine per level is the rule that follows from it.
            _e = new Engine();
            if (!_e.LoadLevel(_levels, n)) { _error = $"cannot load level {n}"; return; }
            _level = n;
            _error = null;
            QueueRedraw();
        }

        public override void _UnhandledInput(InputEvent ev)
        {
            if (ev is not InputEventKey k || !k.Pressed || k.Echo) return;
            switch (k.Keycode)
            {
                case Key.Pagedown: LoadLevel(_level + 1); break;
                case Key.Pageup: LoadLevel(_level - 1); break;
                case Key.Home: LoadLevel(1); break;
                case Key.G: LoadPack(_pack + 1); break;
                case Key.Z: _zoom = (_zoom + 1) % Zooms.Length; Resize(); QueueRedraw(); break;
                case Key.Escape: GetTree().Quit(); break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
        }

        public override void _Draw()
        {
            Font font = ThemeDB.FallbackFont;
            if (_error != null)
            {
                DrawString(font, new Vector2(Margin, Margin + 16), _error,
                           HorizontalAlignment.Left, -1, 16, Colors.OrangeRed);
                return;
            }
            if (_e == null || _atlas == null) return;

            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    DrawCell(x, y);
            DrawTank();
            DrawHud(font);
        }

        private Rect2 CellRect(int x, int y) =>
            new Rect2(Margin + x * Cell, Margin + y * Cell, Cell, Cell);

        /// UpDateSprite (LTANK2.C:490), transliterated: a tunnel is a solid
        /// colour with sprite 55 masked on top; a transparent sprite gets the
        /// under-bitmap painted first; anything else is a plain copy.
        private void DrawCell(int x, int y)
        {
            TGAMEREC g = _e.Game;
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
        private void DrawTank()
        {
            TTANKREC t = _e.Game.Tank;
            Blit(1 + t.Dir, CellRect(t.X, t.Y));
        }

        private void DrawHud(Font font)
        {
            float y = Margin + 16 * Cell + 22;
            TLEVEL lv = _e.CurRecData;
            string head = $"{_level}/{_levelCount}  {lv.LName}";
            if (!string.IsNullOrEmpty(lv.Author)) head += $"   by {lv.Author}";
            DrawString(font, new Vector2(Margin, y), head,
                       HorizontalAlignment.Left, -1, 16, Colors.White);
            DrawString(font, new Vector2(Margin, y + 20),
                       $"graphics: {_atlas.Label}    zoom {Cell}px" +
                       "     PgUp/PgDn level, G graphics, Z zoom, Esc quit",
                       HorizontalAlignment.Left, -1, 12, Colors.Gray);
            if (!string.IsNullOrEmpty(lv.Hint))
                DrawString(font, new Vector2(Margin, y + 40), lv.Hint.Replace("\r\n", " "),
                           HorizontalAlignment.Left, 16 * Cell, 12, Colors.DarkGray);
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
