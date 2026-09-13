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
        //
        // **Step 7 made these three presets rather than the whole story.**  The
        // board now takes whatever square the window leaves it (Measure), at any
        // integer cell size; `Z` still cycles 24 / 32 / 40 and still writes
        // [SCREEN] Size, but what it does is *resize the window* so the board
        // lands on that cell exactly -- a snap to a crisp size, not a mode.  See
        // `_pinCell`.
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

        /// A continuously-sized board needs the table at cell sizes the table
        /// does not have, and the rule the three entries are three samples of
        /// turns out to be **`round(cell * 10 / 24)`**: it reproduces all three
        /// exactly -- 24 -> 10, 32 -> 13.33 -> 13, 40 -> 16.67 -> 17 -- so this
        /// is an interpolation *through* the original's own points rather than
        /// a guess beside them.  **_Ready asserts exactly that**, beside the
        /// tick-rate guard and for the same reason, so the claim cannot quietly
        /// stop being true if anyone edits either.
        ///
        /// AwayFromZero rather than C#'s banker's default: the three points do
        /// not land on a half, but cells 6, 18, 30, 42... do, and a rule that
        /// rounds 17.5 down and 18.5 up puts a one-pixel wobble in the laser as
        /// the window is dragged.
        internal static int LaserOffsetFor(int cell)
            => (int)Math.Round(cell * 10.0 / 24.0, MidpointRounding.AwayFromZero);

        /// The original's size 1..3, which is [SCREEN] Size.  Now a *preset*:
        /// the size `Z` snaps the window to, and the size the window opens at.
        private int _size = 1;

        internal int Size => _size;
        internal static int CellOf(int size) => Zooms[Math.Clamp(size, 1, 3) - 1];

        // ---- the layout, measured from the window every frame ---------------
        //
        // The original's XOffset / YOffset (LTANK.H:93) are **17**, and that
        // gutter is not decoration: it is what the coordinate labels are drawn
        // in (LTANK.C:502).  It was a constant 24 here while the cell was one of
        // three; with the cell continuous it has to follow the type it holds, so
        // both are derived from the cell -- see GridPtFor / GutterFor.

        /// Where everything is this frame.  Recomputed at the top of _Draw and
        /// read by the mouse hit test, the dialogs and the editor: one function
        /// decides the geometry and nothing else is allowed an opinion, which is
        /// what stopped `2 * Margin + 16 * Cell` from being spelled out in six
        /// files the way it was before step 7.
        private struct Layout
        {
            /// The whole window.
            public Rect2 Window;
            /// The top bar and the status strip, full width, top and bottom.
            public Rect2 Top, Status;
            /// The square the board sits in, gutter included -- the "well".
            public Rect2 Well;
            /// The 16x16 cells inside it.  `Board.Position` is cell (0,0).
            public Rect2 Board;
            /// The info column (or, in the stacked layout, the strip under the
            /// board), and in the editor the palette.
            public Rect2 Side;
            public int Cell, Gutter, GridPt;
            /// True when the window is too narrow for a column beside the board
            /// and the panel has gone under it instead.
            public bool Stacked;
        }

        private Layout _l;

        /// The coordinate labels' type, and the gutter that holds it.  The
        /// original's own 15 px at its own 24..40 px cells is the anchor the
        /// ratio is taken from (LTANK.C:504 centres its row labels against a
        /// literal 15, which is the line height of the face it was drawing
        /// with); the clamps keep the labels legible on a tiny board and stop
        /// them growing into decoration on a huge one.
        private static int GridPtFor(int cell)
            => Math.Clamp((int)MathF.Round(cell * 0.40f), 9, 20);

        private static int GutterFor(int cell) => GridPtFor(cell) + 9;

        /// The largest cell whose board -- gutter and all -- still fits a square
        /// `square` px on a side.  Integer cells only: a fractional one puts the
        /// sprite grid off the pixel grid, and with nearest-neighbour filtering
        /// (project.godot sets it, and it is what keeps this looking like the
        /// original rather than like a photo of it) that shows up as rows of
        /// sprites one pixel taller than their neighbours.
        private static int CellFor(float square)
        {
            // 16 cells plus two gutters of ~0.4 cell + 9 is ~16.8 cell + 18.
            int c = Math.Clamp((int)((square - 18f) / 16.8f), MinCell, MaxCell);
            while (c > MinCell && 16 * c + 2 * GutterFor(c) > square) c--;
            while (c < MaxCell && 16 * (c + 1) + 2 * GutterFor(c + 1) <= square) c++;
            return c;
        }

        private const int MinCell = 8, MaxCell = 120;

        // The chrome's own metrics, in design pixels -- Ui.Px turns them into
        // real ones.  See Ui.Scale for why the type does not simply track the
        // board.
        private const float PadD = 16, TopD = 54, StatusD = 36, SideD = 276, GapD = 16;
        /// The stacked layout's strip.  Two lines of numbers and a name, which
        /// is what survives of the column when there is no room for a column.
        private const float StripD = 86;
        /// Below this window width the info column goes under the board.  A raw
        /// pixel count rather than a scaled one: it is a question about the
        /// *device*, and scaling it by a factor derived from the same number is
        /// circular.
        ///
        /// The number is what a column beside a board actually costs, not a
        /// round one: two pads and a gap (~44), the column (~235 at the small
        /// end of the UI scale), and a board wide enough to still be a board --
        /// the 24 px preset's 422.  That is 700, and 660 leaves the preset a
        /// little room to be dragged narrower before the layout gives up on the
        /// column.  **It has to stay under the size-1 preset's own window** or
        /// the smallest preset opens stacked, which is what it did on the first
        /// pass here: the board was pinned to 24 px, the strip took the space
        /// the column would have had, and the board overflowed upward into the
        /// top bar.  WindowFor's clamp is the belt to this brace.
        private const int StackBelow = 660;

        /// Non-zero while the board is held at an exact cell size rather than
        /// fitted to the window -- what `Z`, `--zoom` and the startup [SCREEN]
        /// Size do.  Dragging the window clears it (see `_selfResizes`), which
        /// is the whole interaction: the presets are a snap-to, and the moment
        /// the player disagrees the board goes back to filling what it is given.
        private int _pinCell;

        /// Resize() sets the window itself, and that fires the same
        /// SizeChanged the player's own drag does.  One counter tells them
        /// apart: anything not accounted for here was the player, and frees the
        /// pin.
        private int _selfResizes;

        // The three the rest of the file reads.  They were a constant and two
        // table lookups before step 7; keeping the names means the drawing code
        // below -- DrawCell, DrawTank, DrawLaser, DrawGrid -- did not have to
        // learn that the geometry moved.
        internal int Cell => _l.Cell;
        private int LaserOffset => LaserOffsetFor(_l.Cell);
        private int GridPt => _l.GridPt;
        /// The top-left of cell (0,0), in window pixels.  EditMode and the
        /// mouse hit test read it; nothing may re-derive it.
        internal Vector2 Origin => _l.Board.Position;
        /// The square the board and its labels occupy.
        internal Rect2 Well => _l.Well;
        /// The column beside the board -- the editor's palette lives here.
        internal Rect2 SidePanel => _l.Side;

        /// Everything the frame's geometry is.  Called at the top of _Draw and
        /// by anything that needs the layout before the first draw.
        private void Measure()
        {
            Vector2 win = GetViewportRect().Size;
            // **A headless run has no window, and its viewport is not one.**
            // Godot gives a headless root viewport a size of its own that has
            // nothing to do with project.godot's, and the layout believed it: it
            // produced a 92x160 "window", a board whose origin was off at
            // (-274, -228) and a palette whose five columns landed *on top of*
            // board cells -- so a headless mouse driver clicking cell (7,10)
            // selected palette slot 25 instead of painting.  That is how
            // tools/editor_check.py found this, one edit in eight.
            //
            // So headless lays out the preset's window instead.  The geometry a
            // gate exercises is then exactly the geometry a player at that
            // preset gets, which is what makes driving the mouse headless mean
            // anything at all.
            if (win.X < 320 || win.Y < 320)
                win = WindowFor(_pinCell > 0 ? _pinCell : CellOf(_size));
            Ui.SetScale(win);
            var l = new Layout { Window = new Rect2(Vector2.Zero, win) };
            int pad = Ui.Px(PadD), top = Ui.Px(TopD), st = Ui.Px(StatusD),
                gap = Ui.Px(GapD), sideW = Ui.Px(SideD);

            l.Top = new Rect2(0, 0, win.X, top);
            l.Status = new Rect2(0, win.Y - st, win.X, st);

            var inner = new Rect2(pad, top + pad,
                                  Mathf.Max(64f, win.X - 2 * pad),
                                  Mathf.Max(64f, win.Y - top - st - 2 * pad));

            // The editor's palette is 28 sprites in a grid; there is no stacked
            // form of that which is not a scroll bar, so the editor keeps its
            // column and lets the board shrink instead.
            bool editing = _edit != null && _edit.Open;
            l.Stacked = win.X < StackBelow && !editing;

            Rect2 area;
            if (l.Stacked)
            {
                int strip = Ui.Px(StripD);
                area = new Rect2(inner.Position,
                                 new Vector2(inner.Size.X,
                                             Mathf.Max(64f, inner.Size.Y - strip - gap)));
                l.Side = new Rect2(inner.Position.X, inner.End.Y - strip,
                                   inner.Size.X, strip);
            }
            else
            {
                // Never more than two fifths of the window: on a wide-and-short
                // window the board is height-bound and a fixed column would take
                // space the board could not have used anyway -- but on a *narrow*
                // one just above the stacking threshold it would squeeze the
                // board to nothing.
                sideW = (int)Mathf.Min(sideW, inner.Size.X * 0.42f);
                area = new Rect2(inner.Position,
                                 new Vector2(Mathf.Max(64f, inner.Size.X - sideW - gap),
                                             inner.Size.Y));
                l.Side = new Rect2(inner.End.X - sideW, inner.Position.Y,
                                   sideW, inner.Size.Y);
            }

            float square = Mathf.Min(area.Size.X, area.Size.Y);
            l.Cell = _pinCell > 0 ? _pinCell : CellFor(square);
            l.Gutter = GutterFor(l.Cell);
            l.GridPt = GridPtFor(l.Cell);

            // Rounded, not floored: the board's origin has to be a whole pixel
            // or every sprite in it lands on a half one, and nearest-neighbour
            // then drops a column here and doubles one there.
            float side = 16 * l.Cell + 2 * l.Gutter;
            var at = new Vector2(
                Mathf.Round(area.Position.X + (area.Size.X - side) / 2f),
                Mathf.Round(area.Position.Y + (area.Size.Y - side) / 2f));
            l.Well = new Rect2(at, new Vector2(side, side));
            l.Board = new Rect2(at + new Vector2(l.Gutter, l.Gutter),
                                new Vector2(16 * l.Cell, 16 * l.Cell));
            _l = l;
        }

        /// The window size that puts the board at exactly `cell`.  A fixed
        /// point rather than a formula because the chrome's own metrics are
        /// scaled by a factor derived from the window (Ui.SetScale), so the
        /// answer depends on itself; four or five rounds settle it, and exactness
        /// does not actually rest on the loop converging -- `_pinCell` holds the
        /// cell whatever window comes back.
        private static Vector2I WindowFor(int cell)
        {
            float sq = 16 * cell + 2 * GutterFor(cell);
            var w = new Vector2(sq * 1.7f, sq * 1.3f);
            for (int i = 0; i < 8; i++)
            {
                Ui.SetScale(w);
                var next = new Vector2(
                    2 * Ui.Px(PadD) + sq + Ui.Px(GapD) + Ui.Px(SideD),
                    Ui.Px(TopD) + 2 * Ui.Px(PadD) + sq + Ui.Px(StatusD));
                bool settled = (next - w).LengthSquared() < 1f;
                w = next;
                if (settled) break;
            }
            // A preset must never open stacked: the arithmetic above lays out a
            // column, and a window that then falls under the threshold gets a
            // strip instead, which is a different and smaller space for the same
            // pinned board to fit in.
            w.X = Mathf.Max(w.X, StackBelow);
            return new Vector2I(Mathf.RoundToInt(w.X), Mathf.RoundToInt(w.Y));
        }

        /// The player dragged the window: the preset stops applying and the
        /// board goes back to filling whatever it is given.  `[SCREEN] Size` is
        /// *not* rewritten -- it is still the size `Z` and the next start-up
        /// mean, which is the one thing a dragged window should not silently
        /// redefine.
        private void OnWindowResized()
        {
            if (_selfResizes > 0) { _selfResizes--; return; }
            _pinCell = 0;
            QueueRedraw();
        }

        private Session _s;
        private Atlas _atlas;
        private Options _opt;

        /// The UI strings, converted from the original's ten Language.dat files
        /// (Core/Language.cs).  Read through `Strings`, never directly.
        private Language _lang;
        private LanguageMenu _langMenu;
        private System.Collections.Generic.List<Pack> _packs;
        private Pack _pack;
        private GraphicsMenu _menu;
        private LevelList _list;
        private CollectionList _collections;
        private EditMode _edit;
        private Sfx _sfx;
        private string _error;

        /// A monospace face for the list panels.  Their rows are the original's
        /// own `%4d %-30.30s` sprintf output, so the padding only lines up in a
        /// fixed pitch.  It is `Ui.Mono` since step 7 -- there is one type
        /// system now, and a second name list here would be a second answer to
        /// the same question.
        private Font _mono => Ui.Mono;

        /// Off in --shot mode: the shot awaits two frames, and physics would
        /// otherwise tick the game past the frame being captured.
        private bool _driving = true;

        /// Interpolating the tank between ticks is a presentation choice the
        /// original did not make -- it snapped, one cell per 50 ms.  On by
        /// default because a 60 Hz display shows the step as a stutter; `I`
        /// turns it off, which is the honest A/B against the 2010 binary.
        private bool _interpolate = true;

        /// The coordinate grid, A1-P16.  **The original draws it**, in
        /// WM_PAINT and on all four sides (LTANK.C:502, "Lable Game Grid"), so
        /// this is a port item and DrawGrid is a transliteration of that loop.
        ///
        /// It is worth saying how that was nearly missed, because the mistake
        /// is reusable: a grep for TextOut over **LTANK2.C** finds only the
        /// score readout (:1227, :1648) and ShowTunnelID's `(%1d)` overlay
        /// (:1725), and the conclusion "the original never drew one" was
        /// written down on the strength of it.  The paint code for the *window*
        /// lives in LTANK.C.  Grep the whole source.
        ///
        /// The convention is the original's own, and it is the one the level
        /// hints use: `temps[0] = '@' + i` for i = 1..16 is A..P across,
        /// `itoa(i)` is 1..16 down.  So columns `A`-`P` = x 0-15 left to right
        /// and rows `1`-`16` = y 0-15 top to bottom -- which is also what
        /// Tutor.LVL level 80's "tunnel L7" (`PF[11][6]`, tunnel id 0) and
        /// level 93's mirrors at "K10"/"N10" (`PF[10][9]`, `PF[13][9]`) read as.
        ///
        /// **The original has no key for this** -- it is always on, there is no
        /// menu item and no INI key.  `C` is this port's, on the same terms as
        /// `I`: on by default, not persisted.
        private bool _grid = true;

        /// Command 907 (VK_F1, lt32l_us.inc:141) -- the help, which the original
        /// answers with WinHelp and `LaserTank.hlp`.  There is no .hlp here, so
        /// F1 is the key list instead: the same question, the answer this port
        /// can actually give.  See DrawHelp.
        private bool _help;

        /// Command 301 (VK_H, :140) -- the Hint dialog.  **Off by default, and
        /// that is the point of it**: before step 7 the hint was drawn under the
        /// board on every frame, which spoils every level that has one.  Not
        /// persisted and reset by nothing: asking for a hint on one level is not
        /// a standing request for them on all of them, but it is also not worth
        /// making the player ask twice on the level they asked about.
        private bool _hint;

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
            // Step 6's two, on the same terms: no INI, no pack, no level, and
            // safe to run eight at a time.  --check-lang goes through _lang and
            // the Strings property, so it reads what a label would read.
            if (ArgStr(args, "--check-lang") is string clg)
            {
                _lang = LoadLanguage(clg);
                GetTree().Quit(Step6Check.CheckLang(clg, _lang));
                return;
            }
            if (ArgStr(args, "--check-lang-ini") is string cli)
            {
                GetTree().Quit(Step6Check.CheckLangIni(cli));
                return;
            }
            // Command 108's, on the same terms as the four above: no INI, no
            // pack, no live options, and it builds its own Session so nothing
            // it opens can write a .hs.
            if (Array.IndexOf(args, "--check-collections") >= 0)
            {
                GetTree().Quit(CollectionCheck.Run());
                return;
            }

            // The tick rate is a project setting, so a stale project.godot
            // would silently play the game at 60 Hz.  Fail loudly instead.
            int tps = Godot.Engine.PhysicsTicksPerSecond;
            if (tps != 1000 / Session.GameDelayMs)
                throw new InvalidOperationException(
                    $"physics_ticks_per_second is {tps}, must be " +
                    $"{1000 / Session.GameDelayMs} (GameDelay = {Session.GameDelayMs} ms)");

            // The same species of guard for step 7's one interpolation.  The
            // board takes any cell size now, so LaserOffset had to become a
            // rule rather than a three-entry table -- and the rule is only
            // defensible while it still answers the table's own three points.
            // Fail loudly rather than drawing a laser one pixel off the width
            // tools/options_check.py measures to.
            for (int i = 0; i < Zooms.Length; i++)
                if (LaserOffsetFor(Zooms[i]) != LaserOffsets[i])
                    throw new InvalidOperationException(
                        $"LaserOffsetFor({Zooms[i]}) is {LaserOffsetFor(Zooms[i])}, " +
                        $"and LTANK2.C's table says {LaserOffsets[i]}");

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
                              || Array.IndexOf(args, "--check-deadbox") >= 0
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

            // The language before anything that could want a label.  --lang
            // overrides the INI for this run only, the way --pack and --zoom do;
            // the player's way in is Ctrl+L, which has no command id in the
            // original because the original has no such dialog (Options.PsLang).
            _lang = LoadLanguage(ArgStr(args, "--lang") ?? _opt.LanguageCode);
            _langMenu = new LanguageMenu(this);

            _packs = Packs.Scan(_opt.GraphicsDir);
            _menu = new GraphicsMenu(this);
            _list = new LevelList(this);
            _collections = new CollectionList(this);
            _edit = new EditMode(this);
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

            // --check-deadbox: the DeadBox's modality as a criterion.  It needs
            // a collection and a level, which is why it is here rather than up
            // with the checks that need nothing; it writes no file and posts no
            // score (PlayMode builds its own Session with no Options).
            if (Array.IndexOf(args, "--check-deadbox") >= 0)
            {
                _driving = false;
                GetTree().Quit(PlayMode.CheckDeadBox(
                    levels, level, ArgStr(args, "--route") ?? "llllllluurrrrrrr"));
                return;
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

            // Step 7: the window is resizable and the board follows it.  The
            // minimum is the one the stacked layout still reads at -- below it
            // the info strip and the board start eating each other -- and the
            // start-up size is the [SCREEN] Size preset, which is what Resize
            // snaps to.  Both are no-ops headless.
            if (DisplayServer.GetName() != "headless")
            {
                DisplayServer.WindowSetMinSize(new Vector2I(460, 420));
                GetTree().Root.SizeChanged += OnWindowResized;
            }
            Resize();

            // `--window WxH` is the responsive layout's own instrument, on the
            // same terms as --panel and --editor: the board fits whatever the
            // window gives it now, so "what does it look like at that size" is a
            // question with an answer, and a screenshot is the only way to
            // review it without a hand on the window frame.  It clears the
            // preset pin deliberately -- an arbitrary size is exactly the case
            // the pin is not for.
            if (ArgStr(args, "--window") is string ws
                && DisplayServer.GetName() != "headless")
            {
                string[] wh = ws.ToLowerInvariant().Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out int ww)
                                   && int.TryParse(wh[1], out int whh))
                {
                    _selfResizes++;
                    DisplayServer.WindowSetSize(new Vector2I(ww, whh));
                    _pinCell = 0;
                    Measure();
                }
                else
                {
                    GD.PrintErr("--window wants WxH, e.g. 900x700");
                    GetTree().Quit(2);
                    return;
                }
            }

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
                case "collections": OpenCollections(); break;
                case "playback": OpenPlayback(); break;
                // Step 7's two.  `help` is command 907's overlay and `hint` is
                // 301's card, and both are here for the reason the rest are: a
                // panel nothing can screenshot is a panel nothing reviews.
                case "help": _help = true; break;
                case "hint": _hint = true; break;
                case null: break;
                default:
                    GD.PrintErr("--panel wants "
                                + "levels|scores|global|collections|playback|help|hint");
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

            // `--open-lang` puts the picker up before the frame is captured,
            // so `--shot` can show the panel.  A dialog that only a keystroke
            // can open is otherwise unphotographable, and step 2's lesson --
            // measure pixels, do not look at them -- needs a pixel to measure.
            if (Array.IndexOf(args, "--open-lang") >= 0)
                _langMenu.Show(Language.Available(Paths.Data(Language.DirName)),
                               Strings.Code);

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
        ///
        /// **What this does changed in step 7.**  It used to be the only way the
        /// board's size was ever set, and the editor called it a second time to
        /// widen the window for the palette (the original hangs that palette in
        /// its 180-pixel control panel, ContXPos LTANK2.C:47, and this port had
        /// no such panel to hang it in).  The board now fits itself to whatever
        /// the window gives it and the palette has a column of its own, so this
        /// is left doing one job: snapping the window to a preset so the board
        /// lands on exactly 24, 32 or 40 px cells.  `_pinCell` is what makes it
        /// exact rather than approximate, and the player's next drag frees it.
        private void Resize()
        {
            _pinCell = CellOf(_size);
            // **Headless still lays out.**  The layout used to be two static
            // fields, so a run with no window had the same geometry as one with
            // a window and nothing had to say so.  It is now measured in _Draw
            // -- which a headless run never calls -- and the headless drivers
            // that press the *mouse* need it: EditMode.Script turns `<05` and
            // `l3c` into clicks at board and palette coordinates, and with an
            // unmeasured layout those land on a zero-sized board.  That is
            // exactly how this was found: tools/editor_check.py went red on the
            // two of its eight edits that click the palette.
            Measure();
            if (DisplayServer.GetName() == "headless") return;
            _selfResizes++;
            DisplayServer.WindowSetSize(WindowFor(_pinCell));
            Measure();
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
            // `margin` was the board's origin *and* the gutter while those were
            // the same number; step 7 made the board a box that floats in the
            // window, so the origin is its own pair of fields and `margin` keeps
            // its old meaning -- the gutter the coordinate labels live in.  A
            // tool wanting cell (x,y) wants board_x + x * cell, never margin.
            GD.PrintRaw($"shot-geometry margin={_l.Gutter} cell={Cell} " +
                        $"board_x={(int)Origin.X} board_y={(int)Origin.Y} " +
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

        /// `data/language/<code>.json`, with the base language behind it.
        ///
        /// Complains once and carries on when the directory is missing: a port
        /// with no strings should say so rather than crash on the first label,
        /// and `[ID_WINBOX_03]` on screen is a clearer bug report than a stack
        /// trace out of _Draw.
        private static Language LoadLanguage(string code)
        {
            string dir = Paths.Data(Language.DirName);
            Language got = Language.Load(dir, code);
            if (got == null)
                GD.PrintErr("no languages under " + dir
                            + " -- run: python tools/convert_language.py");
            return got;
        }

        /// Every label on screen goes through here.
        ///
        /// Never null once _Ready has run: Language.Load falls back to the base
        /// language for an unknown code and Strings falls back again to an empty
        /// one if data/language/ is missing outright, so a label is at worst
        /// `[ID_WINBOX_03]` and never a NullReferenceException in the middle of
        /// a draw.  `--check-lang` reads this same property, which is the point
        /// of it being a property.
        internal Language Strings => _lang ?? Language.Empty;

        /// The language picker's live preview, and step 6's whole apply path.
        /// Nothing is reloaded but the strings -- no sheet, no level, no tick --
        /// because nothing else depends on them.
        internal void ApplyLanguage(string code)
        {
            Language got = Language.Load(Paths.Data(Language.DirName), code);
            if (got != null) _lang = got;
            QueueRedraw();
        }

        /// The picker's Close, on GraphBox's terms: there is no Cancel, so
        /// leaving persists the language that is already on screen.
        internal void PersistLanguage()
        {
            if (_lang != null) _opt.SetLanguage(_lang.Code);
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
                // **The preset, not the live layout.**  `--check-options` runs
                // before there is a window or a Session, so `Cell` is whatever
                // the unmeasured layout holds -- zero.  What this line is about
                // is the *option*: [SCREEN] Size, and the cell it means.  Since
                // step 7 that is a preset the window snaps to rather than the
                // only size the board can be, so it is read straight off the
                // size the same way the INI wrote it.
                _opt.Ini.Path, _size, CellOf(_size), LaserOffsetFor(CellOf(_size)),
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
            // Command 108 stops it for the same reason and in the same words --
            // see CollectionList.StopsClock.
            if (_collections != null && _collections.Open && _collections.StopsClock) return;
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

            // The help overlay is modal for keys and nothing else -- the same
            // shape as the graphics dialog (226), which never calls
            // GameOn(FALSE), so the clock runs under it and an exposed tank can
            // die while the list is up.  That is deliberate rather than
            // convenient: every other panel in this port behaves that way, and a
            // help screen that silently paused the game would be the one place
            // the rule did not hold.
            if (_help)
            {
                if (k.Keycode is Key.F1 or Key.Escape or Key.Enter or Key.KpEnter
                    or Key.Space)
                    _help = false;
                GetViewport().SetInputAsHandled();
                return;
            }

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

            // The language picker, on exactly the same terms as the graphics
            // one -- modal for keys, not for the clock.  See LanguageMenu.
            if (_langMenu != null && _langMenu.Open)
            {
                _langMenu.Key(k.Keycode);
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

            // The collection picker, which is GetOpenFileName: modal for keys
            // like every dialog here, and the one thing on the far side of it
            // is command 108's own body -- see Session.OpenDataFile.
            if (_collections != null && _collections.Open)
            {
                _collections.Key(k.Keycode);
                if (_collections.Chosen != null) OpenDataFile(_collections.Chosen);
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
                    // The two ACC2 shares with ACC1: Ctrl+G, the graphics
                    // dialog, and F1 -- which is 903 in the editor's table and
                    // 907 in the game's, two help ids for the same key.  The
                    // overlay answers both and swaps its list for the editor's.
                    if (k.Keycode == Key.G && k.CtrlPressed) _menu.Show(_packs, _pack);
                    else if (k.Keycode == Key.F1) _help = true;
                }
                if (!_edit.Open) Resize();       // it left
                GetViewport().SetInputAsHandled();
                return;
            }

            // **The status line is the last thing the player did, not a log.**
            // `_error` is drawn every frame by the `_ =>` arm of the HUD's
            // switch, and nothing used to clear it, so the first "nothing to
            // undo" -- which every player gets, either by pressing U on the
            // first turn or by *holding* it one repeat past the bottom of the
            // buffer (RepeatsOnHold(U) is true, deliberately) -- stayed on
            // screen for the rest of the level, contradicting every undo that
            // worked afterwards.  Clearing it here means a message survives
            // exactly until the next key, which is what a status line is for.
            // The board has no such line in the original -- the DeadBox, the
            // dialogs and the menu bar carried all of this -- so this is the
            // port's own UI and a decision rather than a transliteration.
            _error = null;

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
                case Key.L when !ctrl: OpenList(ListMode.Levels); break;   // 106
                case Key.O: OpenCollections(); break;                 // 108
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
                    // Its GameOn(TRUE) is unconditional, so on a dead board the
                    // message below reports the *undo* and not the resume: the
                    // tank comes back either way, standing where it died.  See
                    // Session.UndoDead.
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
                // Step 6's picker.  No command id: the original has no such
                // dialog, and Ctrl+L is free in ACC1 -- L alone is Load Level,
                // and the editor's own Ctrl+L (602) is on ACC2, a different
                // table that only applies while EditorOn.
                case Key.L when ctrl:
                    _langMenu.Show(Language.Available(Paths.Data(Language.DirName)),
                                   Strings.Code);
                    break;
                // Commands 120/121/122, the Options menu's three sizes.
                case Key.Z: SetSize(_size % 3 + 1); break;
                case Key.I: _interpolate = !_interpolate; break;
                // Ours, and no command id: the original has no grid to toggle.
                // Plain `C` is free in both accelerator tables -- ACC1 binds
                // VK_C only with CONTROL (111, Save Position) and so does ACC2
                // (601, Clear Field) -- so this takes no key the original used.
                case Key.C: _error = "grid " + (ToggleGrid() ? "on" : "off"); break;
                // Command 102, "Sound" (LTANK.C:875).  The checkmark is the INI
                // here; ToggleOpt writes it immediately.
                case Key.N:
                    bool on = _opt.ToggleSound();
                    if (_sfx != null) _sfx.SoundOn = on;
                    break;
                // Command 104, "Animation" (LTANK.C:886).
                //
                // **This takes effect on the spot, and it did not used to.**
                // Ani_On is the one persisted option that changes what a tick
                // does -- AniCount and AniLevel are both trace fields -- and
                // that was read here as a reason to defer it to the next level
                // load, so the key said "next level" and the board went on
                // shimmering.  The original does no such thing: `Ani_On` is a
                // global, ToggleOpt flips it (LTANK.C:887) and the very next
                // WM_TIMER reads it (`if (Ani_On) AniCount++;`, LTANK.C:589),
                // so animation stops between one 50 ms tick and the next.  So
                // the option is set on the live engine here *and* persisted, and
                // Session.Load keeps reading it for a new engine.
                //
                // What made the deferral look safe was the fidelity gates, and
                // they are unaffected either way: every one of them runs
                // headless with Engine's own default of true and none of them
                // presses A.  Nothing was protecting the behaviour -- it was
                // simply a port artifact, and a visible one.
                case Key.A:
                    bool ani = _opt.ToggleAnimation();
                    if (_s?.E != null) _s.E.Ani_On = ani;
                    _error = "animation " + (ani ? "on" : "off");
                    break;

                // Command 301, "Hint" (LTANK.C's ButText7 button, VK_H in ACC1).
                // The original raises a dialog; this is a card in the column, or
                // a panel over the board when the window is too narrow for one.
                case Key.H:
                    if (string.IsNullOrEmpty(_s?.Rec.Hint))
                        _error = "this level has no hint";
                    else
                        _hint = !_hint;
                    break;

                // Command 907, Help (VK_F1).  The original's is WinHelp on
                // LaserTank.hlp, which this port does not ship; the keys are
                // what it can answer with.
                case Key.F1: _help = true; break;
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
            if (_langMenu != null && _langMenu.Open) return;
            if (_list != null && _list.Open) return;
            if (_collections != null && _collections.Open) return;
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

            // The play arm.  `Session.Click` is the window proc's non-editor
            // arm: the ring-buffer push, behind the one guard the keyboard is
            // behind too.  **A dead or finished board belongs on the list
            // above** -- the DeadBox is as modal as any of those four -- and it
            // is the half of that list the port was missing.  The test cannot go
            // *with* the others, though, because the editor is a mode rather
            // than a dialog and command 201 calls GameOn(FALSE): a guard that
            // stands for a modal box has to be applied only after the editor arm
            // has had the click.  See Session.AcceptsInput.
            if (_s?.E == null) return;
            if (!CellAt(mb.Position, out int x, out int y)) return;
            _s.Click(x, y, button);
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

        /// Window pixels -> board cell.  **The one place the inverse of the
        /// layout is written down**: it used to be spelled out here and again in
        /// EditMode, in terms of a constant margin and a table lookup, and with
        /// the board now floating in the window that arithmetic can no longer be
        /// guessed from two static fields.  False when the point is off the
        /// board, which includes the gutter the labels are in.
        internal bool CellAt(Vector2 pos, out int x, out int y)
        {
            Vector2 o = Origin;
            x = (int)Math.Floor((pos.X - o.X) / Cell);
            y = (int)Math.Floor((pos.Y - o.Y) / Cell);
            return x >= 0 && x < 16 && y >= 0 && y < 16;
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

        /// Command 123 (F5).  The original's checkmark is the window title --
        /// `SetWindowText(MainH, REC_Title)` -- and step 6 made that reachable,
        /// so it is the window title here too, and the HUD as well because a
        /// title bar is easy to miss.  REC_Title leads with a space in all ten
        /// files; that is the translators' own byte and it stays.
        private void ToggleRecording()
        {
            if (_s == null) return;
            bool on = _s.Rec2.Toggle();
            _error = on ? "recording" : "recording off";
            DisplayServer.WindowSetTitle(on ? Strings["REC_Title"].TrimStart()
                                            : AppTitle);
        }

        /// LT32L_US.H:10.  Not a language string: `App_Title` is a compile-time
        /// constant in the original, outside the 240 lines, and none of the ten
        /// files translates it.
        private const string AppTitle = "LaserTank";

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

        /// Command 108's `GetOpenFileName` half -- the picker.  The other half,
        /// what happens once a file comes back, is Session.OpenDataFile.
        private void OpenCollections()
        {
            if (_s == null) return;
            _collections.Show(Paths.Root, _s.LevelPath);
        }

        /// Command 108's `if (GetOpenFileName(&OFN))` branch.  The status line
        /// says which collection is up and how big it is, because the board
        /// alone does not: every collection opens at level 1 and level 1 of
        /// three of them is the same tutorial screen.
        private void OpenDataFile(string lvlPath)
        {
            if (_s == null) return;
            if (_s.OpenDataFile(lvlPath))
                _error = $"{Path.GetFileName(lvlPath)} -- {_s.LevelCount} levels";
            else
                _error = _s.Error ?? ("cannot open " + Path.GetFileName(lvlPath));
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
            Measure();
            Font font = Ui.Sans;

            // The ground, always -- the window is resizable now, so there is
            // usually more of it than there is board.
            DrawRect(_l.Window, Ui.Bg);

            if (_s?.E == null || _atlas == null)
            {
                DrawTopBar();
                Ui.Card(this, _l.Well);
                Ui.Write(this, _l.Well.Position + new Vector2(Ui.Px(20), Ui.Px(36)),
                         _error ?? "no level", 15, Ui.Bad, _l.Well.Size.X - Ui.Px(40));
                return;
            }

            DrawTopBar();
            DrawBoardWell();

            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    DrawCell(x, y);
            DrawTank();
            DrawLaser();
            if (_grid) DrawGrid(font);

            // The editor takes the column over -- its palette *is* the panel
            // while it is open, which is the original's own arrangement (command
            // 201 repaints the control panel as a palette, LTANK2.C:1692) and
            // not, as this port had it until step 7, a second window's worth of
            // width bolted onto the right-hand edge.
            if (_edit != null && _edit.Open) _edit.Draw(this, font, _atlas, _l.Side);
            else if (_l.Stacked) DrawInfoStrip();
            else DrawInfoColumn();
            DrawStatusBar();

            // The dialogs, over the board and under nothing: the original's are
            // modal windows on top of the game, which keeps playing behind them.
            // They are centred on the *window* now rather than on the board,
            // because on a wide window the board is no longer in the middle of
            // it and a dialog that ignored that sat visibly off to one side.
            Rect2 host = _l.Window;
            if (_menu.Open) _menu.Draw(this, font, host);
            if (_langMenu.Open) _langMenu.Draw(this, font, host, Strings);
            if (_list.Open) _list.Draw(this, font, _mono, host);
            if (_collections.Open) _collections.Draw(this, font, _mono, host);
            // The stacked layout has no column to hold the hint card, so command
            // 301 becomes a panel there instead -- under the dialogs, because it
            // is content rather than a prompt.
            if (_hint && _l.Stacked && !string.IsNullOrEmpty(_s.Rec.Hint))
                DrawHintOverlay(host);
            if (_s.Pb.PanelUp) DrawPlaybackPanel(host);
            if (_help) DrawHelp(host);
        }

        private Rect2 CellRect(int x, int y) =>
            new Rect2(Origin.X + x * Cell, Origin.Y + y * Cell, Cell, Cell);

        /// The board's own frame: a well sunk into the ground, with the gutter
        /// the coordinate labels live in inside it.  Purely this port's -- the
        /// original's board is flush against its window -- and it is what stops
        /// a small board on a big window from floating unanchored.
        private void DrawBoardWell()
        {
            DrawStyleBox(Ui.Box(Ui.BoardWell, Ui.Border, 12f, 1f), _l.Well);
            // A hairline immediately around the cells, so the sprite grid has an
            // edge of its own and does not bleed into the gutter.
            DrawRect(_l.Board.Grow(1), Ui.Border, false, Mathf.Max(1, Ui.Px(1)));
        }

        /// "Lable Game Grid" (LTANK.C:502), transliterated: sixteen letters
        /// along the top edge **and the bottom**, sixteen numbers down the left
        /// **and the right**.  All four sides is the original's own layout and
        /// is the point of it -- a cell in the middle of the board is two short
        /// looks from a label instead of one long one.
        ///
        /// **The type size was read from the original rather than chosen.**  Its
        /// row labels sit at `y = (SpBm_Height - 15) / 2` into the cell, and
        /// that 15 is the line height it is centring: MS Sans Serif 8 pt at its
        /// own 24..40 px cells.  Step 7 made the cell continuous, so the literal
        /// 15 became the *ratio* it is a sample of -- GridPtFor, 0.40 of the
        /// cell, which is 16 px at the 40 px cell the original drew 15 at.  The
        /// gutter follows the type rather than the other way round.
        ///
        /// Two things the original does that this does not, both consequences
        /// of the 18 px gutter it had and this does not:
        ///
        /// * its two-digit row numbers are **hand-kerned** -- `strcpy(temps,
        ///   "1 ")` at x-1, then `itoa(i - 10)` at x+3, two TextOut calls to
        ///   squeeze "16" into 10 px (LTANK.C:514).  Ours is one string.
        /// * its column letters take `x = SpBm_Width / 2` as the *left edge*
        ///   under TA_LEFT, so they sit half a glyph right of the column's
        ///   centre.  Ours are centred.
        ///
        /// Nothing is drawn over the board, which also keeps this pass clear of
        /// tools/options_check.py: that gate measures the laser bar inside a
        /// cell to the pixel.  The tank's own column and row are lit -- the
        /// original does not do that either, and it is the one thing here that
        /// is purely this port's.
        private void DrawGrid(Font font)
        {
            TTANKREC t = _s.E.Game.Tank;
            Vector2 o = Origin;
            int gut = _l.Gutter;
            float top = o.Y - 5;                          // baseline, above the board
            float bottom = o.Y + 16 * Cell + GridPt;      // baseline, below it
            float right = o.X + 16 * Cell + 5;
            for (int i = 0; i < 16; i++)
            {
                // `temps[0] = '@' + i` for i = 1..16 -- A..P (LTANK.C:521).
                string col = ((char)('A' + i)).ToString();
                Color cc = i == t.X ? GridLit : GridDim;
                DrawString(font, new Vector2(o.X + i * Cell, top), col,
                           HorizontalAlignment.Center, Cell, GridPt, cc);
                DrawString(font, new Vector2(o.X + i * Cell, bottom), col,
                           HorizontalAlignment.Center, Cell, GridPt, cc);

                // `itoa(i)` for i = 1..16, centred on the row.
                string row = (i + 1).ToString();
                Color rc = i == t.Y ? GridLit : GridDim;
                float y = o.Y + i * Cell + (Cell + GridPt) / 2f - 2;
                DrawString(font, new Vector2(o.X - gut, y), row,
                           HorizontalAlignment.Right, gut - 5, GridPt, rc);
                DrawString(font, new Vector2(right, y), row,
                           HorizontalAlignment.Left, gut - 5, GridPt, rc);
            }
        }

        private static readonly Color GridDim = Ui.Faint;
        private static readonly Color GridLit = Ui.Accent;

        /// The editor keeps this key too, the way it keeps `Z`.
        internal bool ToggleGrid() => _grid = !_grid;

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
            if (r.Global) s += " -- " + Strings["txt012"];
            else if (r.Personal) s += " -- your best yet";
            else s += " -- your best stands at " + HighScores.Describe(r.Old, Strings);
            if (r.Target != null && !r.Global)
                s += "   (par " + r.Target.Moves + "/" + r.Target.Shots + ")";
            if (r.Error != null) s += "   [.hs not written: " + r.Error + "]";
            return s + "   Enter next, F6 saves";
        }

        // =====================================================================
        //  The chrome -- step 7's redesign.
        //
        //  What was here before was one function, DrawHud, drawing eight lines
        //  of grey text in a 190 px strip under the board: a header, the
        //  scores, a status line, *four lines of key legend* and the hint.  It
        //  worked and it was honest, and two things were wrong with it.
        //
        //  The legend was the first.  Twenty-eight keys set as running text is
        //  a paragraph to be read, not a list to be scanned, and it cost a
        //  quarter of the window permanently to something a player needs twice.
        //  It is now the F1 overlay -- which is not an invented binding: VK_F1
        //  is command 907 in the original's own accelerator table
        //  (lt32l_us.inc:141), so the redesign took the key the original
        //  already had for exactly this.
        //
        //  The hint was the second, and it was a real bug rather than plain
        //  drawing: it was on screen *always*, which spoils every level that
        //  has one.  The original puts it behind a dialog on VK_H, command 301
        //  (lt32l_us.inc:140).  So does this now.
        // =====================================================================

        /// The board's own title bar: what is loaded, and the handful of states
        /// that are true of the whole session rather than of this level.
        private void DrawTopBar()
        {
            Rect2 r = _l.Top;
            DrawRect(r, Ui.Surface);
            DrawRect(new Rect2(r.Position.X, r.End.Y - 1, r.Size.X, Mathf.Max(1, Ui.Px(1))),
                     Ui.Border);

            float pad = Ui.Px(PadD);
            float mid = r.Position.Y + r.Size.Y / 2f;

            // The app mark is the tank itself, out of whichever sheet is
            // loaded.  It costs one DrawTextureRectRegion and it means the
            // chrome changes with the graphics pack, which is a nice way of
            // showing which pack is on without a word of text.
            float x = pad;
            if (_atlas != null && _atlas.Region(Obj.GetOBM(Obj.Tank), out Rect2 mark))
            {
                float m = Ui.Px(26);
                DrawTextureRectRegion(_atlas.Texture,
                                      new Rect2(x, mid - m / 2f, m, m), mark);
                x += m + Ui.Px(10);
            }
            Ui.Caps(this, new Vector2(x, mid + Ui.Px(4)), "LaserTank", Ui.Text, 13);
            x += Ui.CapsWidth("LaserTank", 13) + Ui.Px(14);

            // The collection, which the level number alone does not say: every
            // one of the 23 opens at level 1 and three of those level 1s are the
            // same tutorial screen (see OpenDataFile).
            if (_s != null)
            {
                DrawRect(new Rect2(x, mid - Ui.Px(9), Mathf.Max(1, Ui.Px(1)), Ui.Px(18)),
                         Ui.Border);
                x += Ui.Px(14);
                Ui.Write(this, new Vector2(x, mid + Ui.Px(4)),
                         Path.GetFileNameWithoutExtension(_s.LevelPath), 12.5f, Ui.Dim,
                         r.Size.X * 0.4f);
            }

            // The pills, right to left: the exceptional states first, so the
            // one that matters is always in the same place -- hard against the
            // edge -- rather than wherever the ones before it happened to end.
            DrawTopPills(r, pad, mid);
        }

        private void DrawTopPills(Rect2 r, float pad, float mid)
        {
            var pills = new System.Collections.Generic.List<(string, Color, Color)>();
            if (_s != null && _s.Rec2.Recording)
                pills.Add(("rec", Ui.Bad, new Color(0.24f, 0.09f, 0.09f)));
            if (_s != null && _s.Pb.Open)
                pills.Add(("playback", Ui.Cyan, new Color(0.07f, 0.16f, 0.18f)));
            if (_edit != null && _edit.Open)
                pills.Add(("editor", Ui.Accent, new Color(0.20f, 0.14f, 0.05f)));
            if (_opt != null && !_opt.SoundOn)
                pills.Add(("muted", Ui.Faint, Ui.Raised));
            if (_opt != null && !_opt.AnimationOn)
                pills.Add(("still", Ui.Faint, Ui.Raised));
            // The pinned cell size, because it is the one piece of state the
            // window itself does not show: a board that exactly fills its well
            // and a board snapped to 32 px look the same until you drag.
            pills.Add((_pinCell > 0 ? _pinCell + " px" : Cell + " px fit",
                       Ui.Faint, Ui.Raised));

            float h = Ui.Px(10) + Ui.Px(8);
            float x = r.End.X - pad;
            for (int i = pills.Count - 1; i >= 0; i--)
            {
                (string text, Color fg, Color bg) = pills[i];
                float w = Ui.CapsWidth(text, 10) + 2 * Ui.Px(7);
                x -= w;
                Ui.Pill(this, x, mid - h / 2f, text, fg, bg);
                x -= Ui.Px(6);
            }
        }

        // ---- the info column ------------------------------------------------

        /// The wide layout's right-hand column: three cards down the side of the
        /// board.  This is what replaced the header line and the score line of
        /// the old strip, and it is the reason the redesign was worth doing at
        /// all -- the two numbers a player is actually watching (moves, shots)
        /// were the smallest thing on screen and are now the largest.
        private void DrawInfoColumn()
        {
            Rect2 s = _l.Side;
            float gap = Ui.Px(12);
            float y = s.Position.Y;

            y += DrawLevelCard(new Rect2(s.Position.X, y, s.Size.X, 0)) + gap;
            y += DrawScoreCard(new Rect2(s.Position.X, y, s.Size.X, 0)) + gap;

            // The hint is the only card whose height is an author's to decide,
            // so it is measured rather than reserved -- and it is only here at
            // all once `H` has asked for it (command 301).
            if (_hint && !string.IsNullOrEmpty(_s.Rec.Hint))
                y += DrawHintCard(new Rect2(s.Position.X, y, s.Size.X, 0)) + gap;

            // The five keys a player uses on every level, spelled out -- but
            // only while the cards above have left room for them.  This is the
            // part of the old legend wall that earns permanent space: the rest
            // is F1's.  It goes last so a long level name or an open hint push
            // it out rather than pushing the hint out.
            float foot0 = s.End.Y - Ui.Px(34);
            if (foot0 - y > Ui.Px(180))
                DrawActionsCard(new Rect2(s.Position.X, y, s.Size.X, 0));

            // The keys footer, pinned to the bottom of the column rather than
            // flowing after the cards: it is a permanent affordance, and a
            // permanent thing that moves is worse than one that is out of the
            // way.
            float fh = Ui.Px(34);
            var foot = new Rect2(s.Position.X, s.End.Y - fh, s.Size.X, fh);
            if (foot.Position.Y > y)
            {
                float fx = foot.Position.X + Ui.Px(10);
                float fy = foot.Position.Y + Ui.Px(6);
                fx = Ui.Keycap(this, fx, fy, "F1") + Ui.Px(9);
                Ui.Write(this, new Vector2(fx, fy + Ui.Px(15)), "all keys", 11.5f,
                         Ui.Faint, foot.End.X - fx);
            }
        }

        /// Which level, out of how many, by whom, at what difficulty.  Returns
        /// its own height so the column can stack.
        private float DrawLevelCard(Rect2 at)
        {
            TLEVEL lv = _s.Rec;
            float pad = Ui.Px(14);
            float w = at.Size.X - 2 * pad;
            var info = new TLEVELINFO { SDiff = lv.SDiff };

            // Measure first: the name is the author's and wraps to two lines
            // often enough that a fixed card clips real level names.
            string name = string.IsNullOrEmpty(lv.LName) ? "(untitled)" : lv.LName;
            float nameH = Ui.WrappedHeight(name, 17, w, 2);
            float h = pad + Ui.Px(13) + Ui.Px(8) + nameH + Ui.Px(6)
                      + Ui.Px(15) + Ui.Px(10) + Ui.Px(20) + pad;

            var r = new Rect2(at.Position, new Vector2(at.Size.X, h));
            Ui.Card(this, r);

            float x = r.Position.X + pad, y = r.Position.Y + pad + Ui.Px(9);
            Ui.Caps(this, new Vector2(x, y), $"Level {_s.Level} of {_s.LevelCount}",
                    Ui.Faint);
            y += Ui.Px(8) + Ui.Px(13);

            Ui.Wrapped(this, new Vector2(x, y + Ui.Px(13)), name, 17, Ui.Text, w, 2);
            y += nameH + Ui.Px(6);

            if (!string.IsNullOrEmpty(lv.Author))
                Ui.Write(this, new Vector2(x, y + Ui.Px(11)), "by " + lv.Author, 12,
                         Ui.Dim, w);
            y += Ui.Px(15) + Ui.Px(10);

            // The difficulty, as a chip in the original's own five ranks -- it
            // colours its level number by them (`SetTextColor(DifCList[...])`,
            // LTANK.C:532) and this is the same information given a shape.
            // DiffName is " - Kids" and the like, hence the trim.
            string rank = info.DiffName.TrimStart(' ', '-').Trim();
            Color dc = Ui.Diff[Math.Clamp((int)lv.SDiff, 0, 5)];
            Ui.Pill(this, x, y, rank == "" ? "unrated" : rank, dc,
                    dc * new Color(1, 1, 1, 0.16f));
            return h;
        }

        /// Moves, shots, and the .ghs par beside them.  The par is the number a
        /// player is chasing, so it sits with the counters rather than in a line
        /// of its own the way it did in the strip.
        private float DrawScoreCard(Rect2 at)
        {
            TGAMEREC g = _s.E.Game;
            float pad = Ui.Px(14);
            bool hasPar = LevelFile.ReadHighScore(_s.Files.Ghs, _s.Level,
                                                  out ushort tm, out ushort ts);
            float h = pad + Ui.Px(13) + Ui.Px(10) + Ui.Px(44) + pad;
            var r = new Rect2(at.Position, new Vector2(at.Size.X, h));
            Ui.Card(this, r);

            float x = r.Position.X + pad;
            float y = r.Position.Y + pad + Ui.Px(9);
            Ui.Caps(this, new Vector2(x, y), hasPar ? "Score  ·  par " + tm + "/" + ts
                                                    : "Score", Ui.Faint);
            y += Ui.Px(10) + Ui.Px(4);

            float tileW = (r.Size.X - 2 * pad - Ui.Px(10)) / 2f;
            Tile(new Rect2(x, y, tileW, Ui.Px(44)), "moves", g.ScoreMove,
                 hasPar ? tm : (ushort)0, hasPar);
            Tile(new Rect2(x + tileW + Ui.Px(10), y, tileW, Ui.Px(44)), "shots",
                 g.ScoreShot, hasPar ? ts : (ushort)0, hasPar);
            return h;

            void Tile(Rect2 t, string label, int value, ushort par, bool compare)
            {
                Ui.Tile(this, t);
                Ui.Caps(this, new Vector2(t.Position.X + Ui.Px(9),
                                          t.Position.Y + Ui.Px(14)), label, Ui.Faint, 9);
                // Amber once the count is past the posted par: the player has
                // spent the budget, which is the one thing these numbers are
                // ever compared against.  Not red -- being over par is not a
                // failure, it is just no longer a record.
                Color c = compare && par > 0 && value > par ? Ui.Accent : Ui.Text;
                DrawString(Ui.Bold, new Vector2(t.Position.X + Ui.Px(9),
                                                t.End.Y - Ui.Px(10)),
                           value.ToString(), HorizontalAlignment.Left,
                           t.Size.X - Ui.Px(18), Ui.Px(22), c);
            }
        }

        /// The handful of keys that are pressed on every level, as keycaps.
        ///
        /// Which five is a judgement and worth writing down: undo and restart
        /// are the two a player reaches for without looking (and are the
        /// DeadBox's own two buttons, LTANK_D.C:159); the hint is the one this
        /// redesign *hid*, so it has to be visible as an affordance or it is
        /// simply gone; and the level pair is how you leave a level you have
        /// given up on.  Everything else is F1's.
        private float DrawActionsCard(Rect2 at)
        {
            (string, string)[] rows =
            {
                ("U", "undo"),
                ("R", "restart"),
                ("H", "hint"),
                ("L", "levels"),
                ("O", "collections"),
            };
            float pad = Ui.Px(14), rowH = Ui.Px(24);
            float h = pad + Ui.Px(13) + Ui.Px(10) + rows.Length * rowH + pad - Ui.Px(6);
            var r = new Rect2(at.Position, new Vector2(at.Size.X, h));
            Ui.Card(this, r);

            float x = r.Position.X + pad, y = r.Position.Y + pad + Ui.Px(9);
            Ui.Caps(this, new Vector2(x, y), "Keys", Ui.Faint);
            y += Ui.Px(10) + Ui.Px(4);
            foreach ((string key, string label) in rows)
            {
                Ui.Keycap(this, x, y, key, 10.5f);
                Ui.Write(this, new Vector2(x + Ui.Px(40), y + Ui.Px(14)), label, 11.5f,
                         Ui.Dim, r.End.X - x - Ui.Px(40) - pad);
                y += rowH;
            }
            return h;
        }

        /// Command 301's content, on demand.  The frame is the original's
        /// reason for existing: a hint is a spoiler, and a spoiler on screen by
        /// default is not a hint.
        private float DrawHintCard(Rect2 at)
        {
            string hint = _s.Rec.Hint.Replace("\r\n", " ").Replace("\n", " ");
            float pad = Ui.Px(14);
            float w = at.Size.X - 2 * pad;
            float th = Ui.WrappedHeight(hint, 12.5f, w, 8);
            float h = pad + Ui.Px(13) + Ui.Px(8) + th + pad;
            var r = new Rect2(at.Position, new Vector2(at.Size.X, h));
            DrawStyleBox(Ui.Box(new Color(0.13f, 0.11f, 0.06f), Ui.AccentDim, 10f), r);
            Ui.Caps(this, new Vector2(r.Position.X + pad, r.Position.Y + pad + Ui.Px(9)),
                    "Hint  ·  H hides", Ui.Accent);
            Ui.Wrapped(this, new Vector2(r.Position.X + pad,
                                         r.Position.Y + pad + Ui.Px(13) + Ui.Px(8)
                                         + Ui.Px(11)),
                       hint, 12.5f, new Color(0.87f, 0.82f, 0.70f), w, 8);
            return h;
        }

        /// The narrow layout's replacement for the column: one strip under the
        /// board with the name and the two counters.  What is dropped is what a
        /// small window cannot afford and can be asked for -- the author, the
        /// difficulty chip and the hint, which is still on `H` and appears as an
        /// overlay instead (see DrawHintOverlay).
        private void DrawInfoStrip()
        {
            Rect2 r = _l.Side;
            Ui.Card(this, r);
            TLEVEL lv = _s.Rec;
            TGAMEREC g = _s.E.Game;
            float pad = Ui.Px(14);
            float x = r.Position.X + pad, y = r.Position.Y + pad;

            Ui.Caps(this, new Vector2(x, y + Ui.Px(9)),
                    $"Level {_s.Level} of {_s.LevelCount}", Ui.Faint);
            Ui.Write(this, new Vector2(x, y + Ui.Px(34)),
                     string.IsNullOrEmpty(lv.LName) ? "(untitled)" : lv.LName,
                     15, Ui.Text, r.Size.X * 0.55f);

            float tw = Ui.Px(74);
            float tx = r.End.X - pad - 2 * tw - Ui.Px(8);
            Tile(new Rect2(tx, y, tw, r.Size.Y - 2 * pad), "moves", g.ScoreMove);
            Tile(new Rect2(tx + tw + Ui.Px(8), y, tw, r.Size.Y - 2 * pad), "shots",
                 g.ScoreShot);

            void Tile(Rect2 t, string label, int value)
            {
                Ui.Tile(this, t);
                Ui.Caps(this, new Vector2(t.Position.X + Ui.Px(9),
                                          t.Position.Y + Ui.Px(14)), label, Ui.Faint, 9);
                DrawString(Ui.Bold, new Vector2(t.Position.X + Ui.Px(9),
                                                t.End.Y - Ui.Px(9)),
                           value.ToString(), HorizontalAlignment.Left,
                           t.Size.X - Ui.Px(18), Ui.Px(20), Ui.Text);
            }
        }

        // ---- the status bar --------------------------------------------------

        /// One line, across the foot of the window: what just happened.
        ///
        /// **The status line is the last thing the player did, not a log** --
        /// see the input router, which clears `_error` on every key.  The win
        /// and death lines override it because they are states rather than
        /// events, and in the editor the strip carries the editor's own status,
        /// which is where a save says where it went.
        private void DrawStatusBar()
        {
            Rect2 r = _l.Status;
            DrawRect(r, Ui.Surface);
            DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, Mathf.Max(1, Ui.Px(1))),
                     Ui.Border);

            bool editing = _edit != null && _edit.Open;
            (string what, Color tint) = editing
                ? (_edit.Status ?? "", Ui.Accent)
                : _s.Now switch
                {
                    Session.State.Won => (WinLine(), Ui.Good),
                    // ID_DEADBOX_DEAD is the dialog's own headline ("YOU ARE
                    // DEAD ! ! !"); the two keys after it are this port's
                    // legend, because DeadBox offers them as buttons and there
                    // are no buttons here.
                    Session.State.Dead =>
                        (Strings["ID_DEADBOX_DEAD"] + "  —  U undoes the last move, R restarts",
                         Ui.Bad),
                    _ => (_error ?? "", Ui.Accent),
                };

            float pad = Ui.Px(PadD);
            float base_ = r.Position.Y + r.Size.Y / 2f + Ui.Px(4);
            if (what != "")
            {
                // A dot in the line's own colour: at 11 px a tint alone is not
                // a strong enough signal that the line changed.
                float d = Ui.Px(6);
                DrawCircle(new Vector2(pad + d / 2f, r.Position.Y + r.Size.Y / 2f),
                           d / 2f, tint);
                Ui.Write(this, new Vector2(pad + d + Ui.Px(9), base_), what, 12, tint,
                         r.Size.X - 2 * pad - d - Ui.Px(9));
            }
            else
            {
                Ui.Write(this, new Vector2(pad, base_),
                         "arrows move · space fires · U undo · R restart · F1 keys",
                         12, Ui.Faint, r.Size.X - 2 * pad);
            }
        }

        // ---- the playback panel ----------------------------------------------

        /// PBWindow (LTANK_D.C:1003).  The original positions its dialog beside
        /// the game window (`SetWindowPos(..., Box.left + ContXPos + 2, Box.top
        /// + 280, ...)`); step 7 gave this port a column of its own, so it goes
        /// at the foot of the window as a transport bar -- over nothing, where
        /// it used to cover the board's bottom two rows.
        private void DrawPlaybackPanel(Rect2 host)
        {
            Playback pb = _s.Pb;
            float w = Mathf.Min(Ui.Px(560), host.Size.X - 2 * Ui.Px(PadD));
            // Tall enough for four rows: the level line, the author line, the
            // track, and the transport legend under it.  Sized from the flow
            // below rather than guessed -- the first pass guessed 76 and put the
            // legend a few pixels under the panel's own bottom edge.
            float h = Ui.Px(16) * 2 + Ui.Px(74);
            var panel = new Rect2(Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                                  Mathf.Round(_l.Status.Position.Y - h - Ui.Px(14)), w, h);
            Ui.Dialog(this, panel, 12f);

            float pad = Ui.Px(16);
            float x = panel.Position.X + pad;
            float y = panel.Position.Y + pad;

            // txt013 + LName + txt014 + Author: "Playback Level : " and
            // "\nRecorded by " (LANGUAGE.C:54), out of the loaded language.
            // txt014 leads with a newline because the original builds a
            // MessageBox body out of these four pieces; this is one line on a
            // panel, so the newline is turned into the spacing it stands for.
            Ui.Write(this, new Vector2(x, y + Ui.Px(11)),
                     Strings["txt013"] + pb.Rec.LName, 12.5f, Ui.Text,
                     panel.Size.X - 2 * pad);
            Ui.Write(this, new Vector2(x, y + Ui.Px(28)),
                     Strings["txt014"].Replace("\n", "") + pb.Rec.Author, 11.5f,
                     Ui.Dim, panel.Size.X - 2 * pad);

            // ID_PLAYBOX_09 / _10: the count of keys played, over the total,
            // drawn as a track as well as a number -- a recording is a
            // *duration*, and a bar says how much is left where "372 / 1136"
            // has to be read twice.
            float bx = x, by = y + Ui.Px(40), bw = panel.Size.X - 2 * pad - Ui.Px(150);
            float bh = Ui.Px(5);
            DrawStyleBox(Ui.Box(Ui.Raised, Ui.Border, bh / 2f, 0f),
                         new Rect2(bx, by, bw, bh));
            float frac = pb.Rec.DataSize > 0
                ? Mathf.Clamp(_s.E.Game.RecP / (float)pb.Rec.DataSize, 0f, 1f) : 0f;
            if (frac > 0)
                DrawStyleBox(Ui.Box(Ui.Cyan, Ui.Cyan, bh / 2f, 0f),
                             new Rect2(bx, by, Mathf.Max(bh, bw * frac), bh));

            float px = bx + bw + Ui.Px(14);
            Ui.Write(this, new Vector2(px, by + Ui.Px(5)),
                     $"{_s.E.Game.RecP}/{pb.Rec.DataSize}", 11, Ui.Dim,
                     panel.End.X - px - pad, HorizontalAlignment.Left, Ui.Mono);

            // The four buttons and the radio group of three, as keycaps: the
            // dialog has buttons and this has none, so the keys are drawn as the
            // things they stand in for.
            float kx = panel.End.X - pad;
            foreach ((string key, string label, bool on) in new[]
            {
                ("3", "step", pb.Speed == PbSpeed.Step),
                ("2", "slow", pb.Speed == PbSpeed.Slow),
                ("1", "fast", pb.Speed == PbSpeed.Fast),
            })
            {
                float lw = Ui.Width(label, 10.5f);
                kx -= lw;
                Ui.Write(this, new Vector2(kx, panel.Position.Y + Ui.Px(26)), label,
                         10.5f, on ? Ui.Cyan : Ui.Faint);
                kx -= Ui.Px(24) + Ui.Px(5);
                Ui.Keycap(this, kx, panel.Position.Y + Ui.Px(12), key, 10.5f);
                kx -= Ui.Px(12);
            }
            // The legend goes under the track, across the panel's own width --
            // not beside the counter, where there is a keycap row above it and
            // no room.
            Ui.Write(this, new Vector2(x, panel.End.Y - pad),
                     (_s.E.PlayBack ? "space pauses" : "space plays")
                     + " · R resets · any other key closes",
                     10.5f, Ui.Faint, panel.Size.X - 2 * pad);
        }

        // ---- the hint and the help overlay -----------------------------------

        /// Command 301 in the narrow layout, where there is no column to put a
        /// card in: the same content as a panel over the board.
        private void DrawHintOverlay(Rect2 host)
        {
            string hint = _s.Rec.Hint.Replace("\r\n", " ").Replace("\n", " ");
            float w = Mathf.Min(Ui.Px(420), host.Size.X - 2 * Ui.Px(PadD));
            float pad = Ui.Px(16);
            float th = Ui.WrappedHeight(hint, 13, w - 2 * pad, 10);
            float h = pad + Ui.Px(13) + Ui.Px(10) + th + pad;
            var r = new Rect2(host.Position.X + (host.Size.X - w) / 2f,
                              _l.Well.End.Y - h - Ui.Px(16), w, h);
            DrawStyleBox(Ui.Box(new Color(0.13f, 0.11f, 0.06f), Ui.AccentDim, 12f, 1f, 16f),
                         r);
            Ui.Caps(this, new Vector2(r.Position.X + pad, r.Position.Y + pad + Ui.Px(9)),
                    "Hint  ·  H hides", Ui.Accent);
            Ui.Wrapped(this, new Vector2(r.Position.X + pad,
                                         r.Position.Y + pad + Ui.Px(23) + Ui.Px(11)),
                       hint, 13, new Color(0.87f, 0.82f, 0.70f), w - 2 * pad, 10);
        }

        /// Command 907 (F1).  Every binding this port has, grouped, as keycaps
        /// and labels -- which is the whole of what the four grey legend lines
        /// under the old board were trying to be.
        ///
        /// The bindings themselves have not moved: they are still the
        /// original's own accelerator tables (see the input router).  What
        /// changed is that they are no longer *always* on screen.
        ///
        /// **Measured, then drawn.**  The first pass here guessed the height
        /// from a row count and was wrong by about a third of the panel, which
        /// on a dialog with a border and a shadow is not a rounding error but a
        /// visibly empty box.  The column assignment is the thing that has to be
        /// decided before the height can be known -- a group never splits across
        /// columns -- so it is decided once, in Plan, and both passes read it.
        private void DrawHelp(Rect2 host)
        {
            Ui.Scrim(this, host);

            bool editing = _edit != null && _edit.Open;
            (string, (string, string)[])[] groups = editing ? EditorKeys : PlayKeys;

            float pad = Ui.Px(24), colGap = Ui.Px(28);
            float rowH = Ui.Px(24), headH = Ui.Px(26), groupGap = Ui.Px(10);
            float head = pad + Ui.Px(12) + Ui.Px(14) + Ui.Px(14);   // title + rule
            float availH = host.Size.Y - Ui.Px(32) - head - pad;

            // **Two columns is about height as much as width.**  The first pass
            // here picked the column count off the window's width alone, the way
            // the board's own layout does -- and on a narrow-but-tall window
            // (560x760, a phone shape) thirty-one rows in one column ran off the
            // bottom of the panel and took the last group with them.  So: two
            // columns when the window is wide enough to prefer them, *and* two
            // when one column would not fit and the window can hold a pair of
            // narrow ones at all.
            float total = 0;
            foreach ((string _, (string, string)[] items) in groups)
                total += headH + items.Length * rowH + groupGap;

            int cols = 1;
            if (host.Size.X >= Ui.Px(700)) cols = 2;
            else if (total > availH && host.Size.X >= Ui.Px(500)) cols = 2;

            float w = Mathf.Min(cols * Ui.Px(300) + (cols - 1) * colGap + 2 * pad,
                                host.Size.X - Ui.Px(32));
            float colW = (w - 2 * pad - (cols - 1) * colGap) / cols;

            // Still too tall -- a short window, or a small one where even two
            // columns do not fit -- so squeeze the pitch rather than clip the
            // list.  A key list with a group missing off the bottom is worse
            // than a tight one, and there is nothing here to scroll with.
            float squeeze = Mathf.Clamp(availH / (total / cols), 0.66f, 1f);
            if (squeeze < 1f)
            {
                rowH *= squeeze;
                headH *= squeeze;
                groupGap *= squeeze;
                total *= squeeze;
            }

            // ---- pass one: which column each group goes in, and how tall the
            // tallest column ends up.
            float[] colH = new float[cols];
            int[] colOf = new int[groups.Length];
            float target = total / cols;

            int c = 0;
            for (int i = 0; i < groups.Length; i++)
            {
                float need = headH + groups[i].Item2.Length * rowH + groupGap;
                // Move on once this column has had its share -- but never leave
                // a column empty, and never spill past the last one.
                if (c < cols - 1 && colH[c] > 0 && colH[c] + need / 2f > target) c++;
                colOf[i] = c;
                colH[c] += need;
            }
            float body = 0;
            foreach (float ch in colH) body = Mathf.Max(body, ch);

            float h = Mathf.Min(head + body - groupGap + pad, host.Size.Y - Ui.Px(32));

            // ---- pass two: draw it.
            var r = new Rect2(Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                              Mathf.Round(host.Position.Y + (host.Size.Y - h) / 2f), w, h);
            Ui.Dialog(this, r, 16f);

            float x = r.Position.X + pad, y = r.Position.Y + pad + Ui.Px(12);
            Ui.Caps(this, new Vector2(x, y), editing ? "Editor keys" : "Keys", Ui.Text, 13);
            // Right-aligned *inside* the panel: DrawString lays a right-aligned
            // string out in the box [at.X, at.X + w], so the box has to start a
            // width back from the edge rather than at it.
            float cw = Ui.Px(150);
            Ui.Write(this, new Vector2(r.End.X - pad - cw, y), "F1 or Esc closes", 11,
                     Ui.Faint, cw, HorizontalAlignment.Right);
            y += Ui.Px(14);
            Ui.Rule(this, x, y, r.Size.X - 2 * pad);
            y += Ui.Px(14);

            float[] colY = new float[cols];
            for (int i = 0; i < cols; i++) colY[i] = y;
            for (int i = 0; i < groups.Length; i++)
            {
                (string title, (string, string)[] items) = groups[i];
                int ci = colOf[i];
                float cx = x + ci * (colW + colGap);
                Ui.Caps(this, new Vector2(cx, colY[ci] + Ui.Px(9)), title, Ui.Accent, 9.5f);
                colY[ci] += headH;
                foreach ((string key, string label) in items)
                {
                    float kx = cx;
                    foreach (string k in key.Split(' '))
                        kx = Ui.Keycap(this, kx, colY[ci], k, 10.5f) + Ui.Px(4);
                    // The labels line up at a fixed indent, except where the
                    // caps are wider than it -- four arrows are, and ran into
                    // "move the tank" on the first pass.
                    float lx = Mathf.Max(cx + Ui.Px(96), kx + Ui.Px(10));
                    Ui.Write(this, new Vector2(lx, colY[ci] + Ui.Px(14)),
                             label, 11.5f, Ui.Dim, cx + colW - lx);
                    colY[ci] += rowH;
                }
                colY[ci] += groupGap;
            }
        }

        /// The bindings, as data -- so the overlay and the router cannot drift.
        /// The command ids in the comments are the original's; every key here
        /// except the four marked "ours" is out of ACC1 (lt32l_us.inc:120).
        private static readonly (string, (string, string)[])[] PlayKeys =
        {
            ("Play", new[]
            {
                ("← ↑ → ↓", "move the tank"),
                ("space", "fire"),
                ("U", "undo the last move"),          // 110
                ("R", "restart the level"),           // 105
                ("H", "show or hide the hint"),       // 301
            }),
            ("Levels", new[]
            {
                ("L", "pick a level"),                // 106
                ("O", "pick a collection"),           // 108
                ("S", "next level"),                  // 107
                ("P", "previous level"),              // 119
                ("F2", "new game"),                   // 101
            }),
            ("Scores", new[]
            {
                ("V", "your own best times"),         // 113
                ("G", "the posted best times"),       // 906
                ("ctrl C", "save this position"),     // 111
                ("ctrl V", "restore it"),             // 112
            }),
            ("Recording", new[]
            {
                ("F5", "start or stop recording"),    // 123
                ("F6", "save the recording"),         // 117
                ("F7", "play one back"),              // 114
                ("F4", "replay this level"),          // 124
                ("F8", "record every level"),         // 125
            }),
            ("View", new[]
            {
                ("Z", "snap to 24 / 32 / 40 px"),     // 120-122
                ("C", "the A1-P16 grid"),             // ours
                ("I", "smooth or snap the tank"),     // ours
                ("N", "sound"),                       // 102
                ("A", "animation"),                   // 104
                ("ctrl G", "graphics pack"),          // 226
                ("ctrl L", "language"),               // ours
            }),
            ("Session", new[]
            {
                ("F9", "the level editor"),           // 201
                ("F1", "this list"),                  // 907
                ("Esc", "quit"),                      // ours
            }),
        };

        /// ACC2 (lt32l_us.inc:150) plus the palette's own, which the editor
        /// panel used to have to list itself in eight grey lines.
        private static readonly (string, (string, string)[])[] EditorKeys =
        {
            ("Paint", new[]
            {
                ("click", "paint with the left brush"),
                ("right", "paint with the right brush"),
                ("shift", "shift-click rotates in place"),
                ("X", "swap the two brushes"),
                ("T", "the tunnel id"),
            }),
            ("Board", new[]
            {
                ("ctrl ←→", "shift the board"),       // 710/711
                ("ctrl ↑↓", "shift the board"),       // 712/713
                ("ctrl C", "clear the field"),        // 601
                ("1 - 5", "the difficulty"),
            }),
            ("File", new[]
            {
                ("ctrl S", "save the level"),         // 603
                ("tab", "name / author / hint"),
                ("F9", "leave the editor"),           // 604
            }),
            ("View", new[]
            {
                ("Z", "snap to 24 / 32 / 40 px"),
                ("C", "the A1-P16 grid"),
                ("ctrl G", "graphics pack"),          // 226
                ("F1", "this list"),                  // 903
                ("Esc", "leave the editor"),
            }),
        };
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
