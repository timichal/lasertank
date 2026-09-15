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
        /// is what survives of the column when there is no room for a column --
        /// plus, since step 9, the row of chips that is the only way into the
        /// rest of the game on a window this narrow.  It grew from 86 to make
        /// room for them, which is 14 px the board gives up on a phone-shaped
        /// window for the ability to be played on one at all.
        private const float StripD = 106;
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

        /// The options, for the one panel that writes one -- see NameSection.
        /// Everything else in this project reaches them through a method here,
        /// and that stays the rule: a panel that needs a *setting* gets a
        /// property on this class, not a second writer of the INI.
        internal Options Options => _opt;

        /// The UI strings -- Core/Strings.cs, one keyed catalogue per language.
        /// Read through the `Strings` property, never directly.
        private Strings _lang;
        private System.Collections.Generic.List<Pack> _packs;
        private Pack _pack;

        /// The packs on disk and the one that is drawn, for the graphics
        /// section: it reads them on the way in rather than being handed them,
        /// because a section is entered many times in a session.
        internal System.Collections.Generic.List<Pack> PackList => _packs;
        internal Pack CurrentPack => _pack;

        /// **One settings panel since step 18** -- next-steps item 14.  It was
        /// four fields here (`_menu`, `_langMenu`, `_nameMenu`, `_optMenu`) on
        /// four modifiers, which is four copies of one modality rule; the four
        /// bodies are sections of this now.
        private SettingsMenu _settings;
        private LevelList _list;
        private CollectionList _collections;
        private EditMode _edit;
        private Sfx _sfx;
        private string _error;

        /// The chrome's clickable rectangles, rebuilt every frame by _Draw and
        /// read by the mouse -- see Hits, and see MouseButton for where in the
        /// window proc's two arms this one goes (before both of them).
        private readonly Hits _hits = new();

        /// The panels reach it through here: they draw themselves and register
        /// what they drew in the same function, which is the only way the two
        /// can be kept in step.
        internal Hits Chrome => _hits;

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

        /// F1, which is command 907 in ACC1 (lt32l_us.inc:145) and 903 in ACC2
        /// -- two help ids for one key.  **Only 903 is WinHelp**: 907 is the
        /// Quick About Box, which sets QHELP and paints Opening.bmp over the
        /// board (LTANK.C:1368), and the real help is 902 (HELP_INDEX) plus
        /// 903/904/905 (HELP_KEY on help01/02/03).  Neither ships here, so F1
        /// is the key list instead: the answer this port can actually give,
        /// and the one F1 is most often asked for.  See DrawHelp.
        private bool _help;

        /// Esc, and the one thing in this UI that stands between the player and
        /// something irreversible.
        ///
        /// **No command id: the original has no quit accelerator at all.**  Its
        /// way out is the window's own close box and the File menu's Exit
        /// (LTANK.C's 103), both of which are two deliberate acts with a menu or
        /// a title bar in between; Esc is this port's, added because a
        /// keyboard-driven game wants a keyboard way out, and Esc is also the
        /// key that closes every panel here.  That is exactly what makes it
        /// dangerous: one press too many after closing a list and the session is
        /// gone, mid-level, with the moves since the last save unrecorded.  So
        /// the key raises this and a second, *different* key confirms -- Enter
        /// or Y, never Esc again, so a double-tap of the same key cannot quit.
        ///
        /// Unconditional rather than clever: it asks on a won board and an
        /// untouched one too.  "Only when there is something to lose" needs the
        /// game to know what a player would call a loss, and the cost of being
        /// wrong about that is the whole session against one keystroke.
        private bool _quitAsk;

        /// Command 301 (VK_H, :140) -- the Hint dialog.  **Off by default, and
        /// that is the point of it**: before step 7 the hint was drawn under the
        /// board on every frame, which spoils every level that has one.  Not
        /// persisted and reset by nothing: asking for a hint on one level is not
        /// a standing request for them on all of them, but it is also not worth
        /// making the player ask twice on the level they asked about.
        private bool _hint;

        /// **A throw out of _Ready used to hang the run rather than end it.**
        /// Godot logs a C# exception and keeps the main loop turning, so the
        /// rest of Start -- including the branch that calls Quit for --shot --
        /// never runs, and a scripted run sits there forever having drawn
        /// nothing and printed nothing but the trace.  The easy way in is a
        /// --levels value that is not a readable collection (`--levels --shot
        /// out.png` eats the next flag as the file name and Session's
        /// constructor throws), but the two "fail loudly" guards in Start throw
        /// too, and loudly is not what they got.
        ///
        /// **An instrument run ends on it**, because a batch that cannot start
        /// must not outlive its own error message -- a gate reads the exit code
        /// and a hang has none.  A player's run is left as it was: the window is
        /// broken either way, and one that stays with the trace behind it says
        /// more than one that vanishes.
        public override void _Ready()
        {
            string[] args = OS.GetCmdlineUserArgs();
            try { Start(args); }
            catch (Exception e)
            {
                GD.PrintErr("start failed: " + e);
                if (Instrument(args)) GetTree().Quit(1);
            }
        }

        private void Start(string[] args)
        {
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
            // safe to run eight at a time.  --check-strings goes through _lang
            // and the Strings property, so it reads what a label would read.
            if (ArgStr(args, "--check-strings") is string clg)
            {
                _lang = LoadLanguage(clg);
                GetTree().Quit(Step6Check.CheckStrings(clg, _lang));
                return;
            }
            if (ArgStr(args, "--check-strings-ini") is string cli)
            {
                GetTree().Quit(Step6Check.CheckStringsIni(cli));
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
            // **Two paths since step 16, not one.**  `--ini` names the file the
            // *importer* reads on a first run; `--settings` names the typed
            // store the game actually lives in.  Either one given explicitly
            // says "this state is yours".  When only `--ini` is given the store
            // is its sibling, so every instrument that used to hand the game a
            // scratch INI still gets a scratch settings file with it -- see
            // Paths.SettingsBeside.
            string ini = ArgStr(args, "--ini");
            string settings = ArgStr(args, "--settings");
            bool instrument = Instrument(args);
            // One rule, used twice: **an explicit --ini or --settings makes the
            // options live** -- writable, and allowed to choose the level --
            // while an instrument left to find the files on its own gets the
            // settings read-only and starts wherever it was told to.  That is
            // what keeps `--shot` reproducible and lets the gate exercise both
            // halves.
            bool live = !instrument || ini != null || settings != null;
            _opt = Options.Open(
                settings ?? (ini != null ? Paths.SettingsBeside(ini) : Paths.Settings),
                ini ?? Paths.Ini, readOnly: !live);

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

            // --skip-completed yes|no and --difficulty N, on exactly those
            // terms.  The player's way in is Ctrl+O; these are here because a
            // gate cannot press it, and because the filtered walk is the one
            // thing in item 3 that has to be measured rather than looked at.
            bool? skipArg = ParseYesNo(ArgStr(args, "--skip-completed"));
            string diffArg = ArgStr(args, "--difficulty");
            // Applied here rather than where --sound is, because --sound's
            // run-only half lands on the *sink* (Sfx.SoundOn) and these two
            // land on Options: Session.Advance reads them off it, and the
            // Session does not exist yet.  `persist: false` is what keeps the
            // file out of it until --save-options asks.
            if (skipArg.HasValue) _opt.SetSkipCompleted(skipArg.Value, persist: false);
            if (diffArg != null) _opt.SetDifficulty(Ini.Atoi(diffArg), persist: false);

            // The language before anything that could want a label.  --lang
            // overrides the INI for this run only, the way --pack and --zoom do;
            // the player's way in is the options panel's Language section,
            // which has no command id because the original has no such dialog
            // (IniImport.PsLang).
            _lang = LoadLanguage(ArgStr(args, "--lang") ?? _opt.LanguageCode);

            // --name STRING, this run only unless --save-options is given --
            // the same arrangement --sound, --pack and --zoom have.  The
            // player's way in is Ctrl+O.  It is set before the Session exists
            // because the recorder and the score post both read it.
            string nameArg = ArgStr(args, "--name");
            if (nameArg != null && Array.IndexOf(args, "--save-options") < 0)
                _opt.SetName(nameArg, persist: false);

            _packs = Packs.Scan(_opt.GraphicsDir);
            _settings = new SettingsMenu(this);
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
                _opt.ReadOnly = false;
                _opt.SetSize(_size);
                if (soundArg.HasValue) _opt.SetSound(soundArg.Value);
                if (skipArg.HasValue) _opt.SetSkipCompleted(skipArg.Value);
                if (diffArg != null) _opt.SetDifficulty(Ini.Atoi(diffArg));
                // The persisting half of --name, which is why the run-only
                // half above stands down when this flag is present: SetName is
                // a no-op once the text matches, so setting it twice would
                // write nothing.
                if (nameArg != null) _opt.SetName(nameArg);
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

            // --check-advance: the filtered walk as a sequence of level
            // numbers.  Here, with --check-deadbox, because it wants the same
            // two things that one does -- a collection and a level -- and
            // because it must run after the option overrides above have landed
            // on Options: the mask it walks is the one the game would walk.
            if (Array.IndexOf(args, "--check-advance") >= 0)
            {
                _driving = false;
                GetTree().Quit(Step15Check.CheckAdvance(
                    levels, level, Arg(args, "--advance-dir", 1),
                    Arg(args, "--advance-steps", 4096), _opt));
                return;
            }

            // --check-history: command 118's stack as a sequence of level
            // numbers, and here for --check-advance's reasons exactly -- it
            // wants a collection, a level and the options the walk reads,
            // because half the script is `+` and `-`.
            if (ArgStr(args, "--check-history") is string hist)
            {
                _driving = false;
                GetTree().Quit(Step17Check.CheckHistory(
                    levels, level, hist, ArgStr(args, "--history-open"), _opt));
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
            // `--menu` opens the options panel on its Graphics section, which
            // is the only way to review it with --shot rather than by hand.
            // **The flag is older than the section** -- it opened the graphics
            // *dialog* until step 18 merged it -- and it is kept pointing at the
            // same body for the reason the three --panel spellings below are
            // kept: a flag that used to open something should not start
            // printing usage.
            if (Array.IndexOf(args, "--menu") >= 0) _settings.Show("graphics");

            // `--panel levels|scores|global|playback` is the same idea for step
            // 4's overlays: with --shot it is the only way to review one without
            // a window and a hand on the keyboard.
            switch (ArgStr(args, "--panel"))
            {
                // One panel since step 8, so these are three names for it.
                // The two older ones are kept because the review instruments
                // are named in PROGRESS.md and in this project's shell history,
                // and a flag that used to open something should not start
                // printing usage.
                case "levels":
                case "scores":
                case "global": OpenList(); break;
                case "collections": OpenCollections(); break;
                case "playback": OpenPlayback(); break;
                // Step 7's two.  `help` is command 907's overlay and `hint` is
                // 301's card, and both are here for the reason the rest are: a
                // panel nothing can screenshot is a panel nothing reviews.
                case "help": _help = true; break;
                case "hint": _hint = true; break;
                // Step 8's, on the same terms.
                case "quit": _quitAsk = true; break;
                // **Step 18's one panel, under six names.**  `settings` is
                // what it is; `game`, `graphics`, `language` and `player` are
                // its four sections; and `name` and `options` are what step 14
                // and item 3 called their own panels before the merge, kept
                // pointing at the sections those became.  Same rule as the
                // three spellings of the level list above.
                case "settings": _settings.Show(); break;
                case "game":
                case "options": _settings.Show("game"); break;
                case "graphics": _settings.Show("graphics"); break;
                case "language": _settings.Show("language"); break;
                case "player":
                case "name": _settings.Show("player"); break;
                case null: break;
                default:
                    GD.PrintErr("--panel wants "
                                + "levels|scores|global|collections|playback|help|hint|quit|"
                                + "settings|game|graphics|language|player|name|options");
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
            // It is `--panel language` since step 18 and kept under its own
            // name, for `--menu`'s reason.
            if (Array.IndexOf(args, "--open-lang") >= 0) _settings.Show("language");

            // `--hover X,Y` parks the pointer before the frame is captured, so
            // a screenshot can show what the chrome looks like under one --
            // which is a state no key can put the UI into and which `--shot`
            // could therefore not review.
            if (ArgStr(args, "--hover") is string hs && Point(hs, out Vector2 hp))
                _hits.Point(hp);

            string clicks = ArgStr(args, "--click");

            // `--press key:U;key:ctrl+G` -- the *other* half of the
            // differential.  chrome_check.py clicks a target called `key:U` and
            // presses this, and the two StateLines have to be the same line;
            // without it the gate could only assert that a click did
            // *something*.  It goes through Press, which is the same function
            // the chrome click goes through -- what is being checked is that
            // the rectangle is over the command it claims, not that Press
            // works.
            string press = ArgStr(args, "--press");

            // `--type STRING` -- the level list's filter field, from a command
            // line.  **It is here because neither of the other two can reach
            // it.**  `--press` goes through Press, which is the accelerator
            // table, and a text field is not an accelerator; `--click` reaches
            // only what the hit list holds, and the field is a swallow because
            // it is always focused.  So the one arm of step 11 with no
            // instrument would have been the one it was mostly for -- which is
            // the "make the thing being added observable" rule, and the sound
            // that shipped silent is what it was learned from.
            string typed = ArgStr(args, "--type");

            string shot = ArgStr(args, "--shot");
            // The three step-9 flags run as one coroutine, and it takes `--shot`
            // over: presses, then the dump, then the clicks, each with frames
            // drawn in between -- so a capture has to be the last step of that
            // sequence rather than a second one racing it.
            if (clicks != null || press != null || typed != null
                || Array.IndexOf(args, "--dump-hits") >= 0)
            {
                _driving = false;
                RunTicks(script, Arg(args, "--ticks", 0));
                ClickScript(clicks, press, typed, shot,
                            Array.IndexOf(args, "--dump-hits") >= 0);
                return;
            }
            if (shot != null)
            {
                _driving = false;
                RunTicks(script, Arg(args, "--ticks", 0));
                Shot(shot);
            }
        }

        /// `\\b` -> Backspace, `\\t` -> Tab, `\\\\` -> one backslash;
        /// anything else is itself.  See --type's comment for why.
        private static string Unescape(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char c = s[++i];
                sb.Append(c switch
                {
                    'b' => '\b',
                    't' => '\t',
                    '\\' => '\\',
                    _ => c,
                });
            }
            return sb.ToString();
        }

        /// One character into whichever panel with a field is open, through
        /// exactly the `Key(InputEventKey)` the router calls -- the field's own
        /// arm, not a back door into it.  `\b` and `\t` carry the two editing
        /// keys the fields answer.
        private void Type(char ch)
        {
            Key code = ch switch
            {
                '\b' => Key.Backspace,
                '\t' => Key.Tab,
                _ => Key.None,
            };
            var k = new InputEventKey
            {
                Pressed = true,
                Keycode = code,
                Unicode = code == Key.None ? ch : 0,
            };
            // The same order the router tests them in, so `--type` cannot
            // reach a field the keyboard could not have reached.  `Typing` is
            // the settings panel's answer to "is the section showing a field",
            // which is the one thing this has to know about it.
            if (_settings != null && _settings.Typing) _settings.Key(k);
            else if (_list != null && _list.Open) _list.Key(k);
        }

        /// What the field that was typed into is showing, for `--type`'s log.
        ///
        /// For the list that is the query, how many rows survived it and how
        /// many there were -- a gate can assert on this without a copy of the
        /// filter in Python, and the *count* is the assertion, because it is
        /// the one number the four filter fields all land in.  For the name
        /// panel it is the text and the four characters a `.hs` would carry,
        /// which is the one thing about that field a screenshot would not
        /// settle.
        private string TypedLine()
        {
            // **`text=` is last on purpose.**  A name has spaces in it and
            // this line is read by splitting on them, so the one field whose
            // value can contain one has to be the field nothing follows.
            if (_settings != null && _settings.Typing)
                return $"name=True initials={_settings.NameBody.Initials} "
                       + $"text={_settings.NameBody.Text}";
            return _list == null || !_list.Open
                ? "list=False"
                : $"list=True rows={_list.Count} of={_list.Total} "
                  + $"filtering={_list.Filtering} q={_list.Query}";
        }

        /// `key:U`, `key:ctrl+G` -- KeyName read backwards.
        private static bool ParseKey(string s, out Key code, out bool ctrl)
        {
            code = Key.None;
            ctrl = false;
            if (s == null || !s.StartsWith("key:")) return false;
            string rest = s.Substring(4);
            if (rest.StartsWith("ctrl+")) { ctrl = true; rest = rest.Substring(5); }
            return Enum.TryParse(rest, true, out code) && code != Key.None;
        }

        /// `X,Y`, invariant, for --click and --hover.
        private static bool Point(string s, out Vector2 at)
        {
            at = Vector2.Zero;
            string[] xy = (s ?? "").Split(',');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (xy.Length != 2
                || !float.TryParse(xy[0], System.Globalization.NumberStyles.Float, inv,
                                   out float x)
                || !float.TryParse(xy[1], System.Globalization.NumberStyles.Float, inv,
                                   out float y))
                return false;
            at = new Vector2(x, y);
            return true;
        }

        /// **`--click X,Y;X,Y;...` -- the chrome, pressed from a command line.**
        ///
        /// Every other instrument in this file exists because a panel nothing
        /// can screenshot is a panel nothing reviews; this one exists because
        /// the *hit list is built by _Draw and by nothing else*, so a headless
        /// run has an empty one (see Hits) and there is no way at all to
        /// exercise the third arm without a window.  `tools/chrome_check.py` is
        /// what drives it.
        ///
        /// Two properties it is worth being exact about, because a gate that
        /// got either wrong would pass while the game was broken:
        ///
        /// * **It pushes through `MouseButton`, not through the hit list.**  So
        ///   what is exercised is the whole arm -- the wheel branch, the chrome,
        ///   the six modality guards, the editor brush, MouseOperation -- in
        ///   the order the window proc has them, and not just the lookup.
        /// * **A frame is drawn before every click.**  The list a click is
        ///   tested against is the last frame's, so a click that opens a panel
        ///   has to let that panel draw before the next click can land on it.
        ///   That is the real timing a player gets, spelled out.
        private async void ClickScript(string spec, string press, string typed,
                                       string shot, bool dump)
        {
            // Named in the order the coroutine runs them, so the message names
            // a flag the caller actually typed.
            if (NeedsWindow(press != null ? "--press" : typed != null ? "--type"
                            : dump ? "--dump-hits" : "--click")) return;
            Measure();
            GD.PrintRaw(ChromeLine());

            // The keys first, so `--press` and `--click` in one run mean "put
            // the UI here, then click that" rather than racing.
            foreach (string k in (press ?? "").Split(';',
                                 StringSplitOptions.RemoveEmptyEntries))
            {
                await ToSignal(RenderingServer.Singleton,
                               RenderingServer.SignalName.FramePostDraw);
                if (!ParseKey(k, out Key code, out bool ctrl))
                {
                    GD.PrintErr("--press wants key:U or key:ctrl+G");
                    GetTree().Quit(2);
                    return;
                }
                Press(code, ctrl);
                GD.PrintRaw($"press {k} {StateLine()}" + "\n");
                QueueRedraw();
            }

            // The characters next, so `--press key:L --type sokoban` reads as
            // "open the list, then type into it".
            //
            // **`\\b` and `\\t` are two characters here, not control
            // codes**, and that is the whole reason Unescape exists: a real
            // `\b` in an argument does not survive the trip through the shell,
            // Python's argument quoting and Godot's own command-line split --
            // measured, not assumed, and the symptom was a backspace that
            // silently did nothing while the gate went green on the digits
            // beside it.  A filter that cannot be corrected is half an
            // instrument, so the two editing keys get a spelling that travels.
            if (typed != null)
            {
                foreach (char ch in Unescape(typed))
                {
                    await ToSignal(RenderingServer.Singleton,
                                   RenderingServer.SignalName.FramePostDraw);
                    Type(ch);
                    QueueRedraw();
                }
                GD.PrintRaw($"type {typed} {TypedLine()}\n");
            }

            if (dump)
            {
                await ToSignal(RenderingServer.Singleton,
                               RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton,
                               RenderingServer.SignalName.FramePostDraw);
                GD.PrintRaw(_hits.Dump());
            }

            foreach (string one in (spec ?? "").Split(';',
                                   StringSplitOptions.RemoveEmptyEntries))
            {
                await ToSignal(RenderingServer.Singleton,
                               RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton,
                               RenderingServer.SignalName.FramePostDraw);
                // `X,Y` is the left button; `X,Y,r` the right, `X,Y,u` and
                // `X,Y,d` the wheel -- the editor's second brush and the lists'
                // scroll are part of this arm too.
                string[] f = one.Split(',');
                bool ok = f.Length >= 2 && Point(f[0] + "," + f[1], out Vector2 at);
                if (!ok)
                {
                    GD.PrintErr("--click wants X,Y[,r|u|d];X,Y...");
                    GetTree().Quit(2);
                    return;
                }
                Point(f[0] + "," + f[1], out Vector2 p);
                Godot.MouseButton b = f.Length > 2 ? f[2] switch
                {
                    "r" => Godot.MouseButton.Right,
                    "u" => Godot.MouseButton.WheelUp,
                    "d" => Godot.MouseButton.WheelDown,
                    _ => Godot.MouseButton.Left,
                } : Godot.MouseButton.Left;
                bool took = MouseButton(new InputEventMouseButton
                { ButtonIndex = b, Pressed = true, Position = p });
                MouseButton(new InputEventMouseButton
                { ButtonIndex = b, Pressed = false, Position = p });
                GD.PrintRaw($"click {f[0]},{f[1]} {b} took={took} " +
                            $"on={_hits.Took} {StateLine()}\n");
                QueueRedraw();
            }
            if (shot != null) { Shot(shot); return; }
            await ToSignal(RenderingServer.Singleton,
                           RenderingServer.SignalName.FramePostDraw);
            GetTree().Quit(0);
        }

        /// Where the chrome is, for a tool that has to aim at it.  The board's
        /// own geometry is `shot-geometry`, which predates step 9 and is about
        /// cells; this is the rest of the window, which is what a click lands
        /// in.  A gate reads this and computes its own coordinates rather than
        /// hardcoding a layout that is measured from the window every frame and
        /// has no constants left to hardcode.
        private string ChromeLine()
            => $"chrome top={R(_l.Top)} side={R(_l.Side)} well={R(_l.Well)} " +
               $"board={R(_l.Board)} status={R(_l.Status)} window={R(_l.Window)} " +
               $"stacked={_l.Stacked}\n";

        private static string R(Rect2 r)
            => $"{(int)r.Position.X},{(int)r.Position.Y},{(int)r.Size.X},{(int)r.Size.Y}";

        /// What the click changed, in one line a gate can diff against the same
        /// line after the *key* that is supposed to be equivalent.  Deliberately
        /// coarse: the question is whether clicking `undo` undid, not what the
        /// chrome looked like while it did.
        private string StateLine() =>
            $"level={_s?.Level} moves={_s?.E?.Game.ScoreMove} " +
            $"shots={_s?.E?.Game.ScoreShot} help={_help} quit={_quitAsk} " +
            $"hint={_hint} list={_list?.Open} coll={_collections?.Open} " +
            $"settings={_settings?.Section} " +
            $"editor={_edit?.Open} " +
            $"pb={_s?.Pb.PanelUp} rec={_s?.Rec2.Recording} sound={_opt?.SoundOn} " +
            $"ani={_opt?.AnimationOn} cell={Cell} pack={_atlas?.Label} " +
            $"lvlfile={(_s == null ? "" : Path.GetFileName(_s.LevelPath))}";

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

        /// **Is this run an instrument rather than a player?**  Two callers
        /// want the same list and must not drift apart: the options in Start
        /// read the INI without writing it for these, and _Ready ends the
        /// process on a failed start for these.
        ///
        /// **Step 9's three and step 11's belong on this list and were not on
        /// it.**  They are as much instruments as --shot is, and the gap never
        /// showed because tools/chrome_check.py always passes --ini and an
        /// explicit --ini makes the options live anyway.  What it cost was an
        /// ad-hoc `--click`/`--dump-hits` run by hand: it rewrote the player's
        /// LaserTank.ini, and a session of them left [DATA] RLLFilename on a
        /// different collection than the one the player had open.  Same rule as
        /// everything else here -- an instrument must not write the player's
        /// state, and the test is whether it could run eight times over and
        /// leave the tree as it was.
        ///
        /// **`--edit` and `--save` were the next two off the same list**, found
        /// by step 16 for the same reason step 9's were found late: nothing was
        /// looking.  `editor_check.py`'s game arm drives the editor with
        /// `--editor --edit SCRIPT --save --levels <a copy in /tmp>`, none of
        /// which was an instrument flag -- so six runs of it left the player's
        /// remembered level pointing at a temp file that no longer exists.  The
        /// game degrades gracefully from that (Start checks File.Exists before
        /// it reopens), which is exactly why nobody noticed.  Bare `--editor`
        /// is deliberately *not* here: opening the editor is something a player
        /// might reasonably ask for on a command line, and driving it is not.
        private static bool Instrument(string[] args)
            => ArgStr(args, "--shot") != null
               || Array.IndexOf(args, "--play") >= 0
               || ArgStr(args, "--replay") != null
               || Array.IndexOf(args, "--check-options") >= 0
               || Array.IndexOf(args, "--check-deadbox") >= 0
               || ArgStr(args, "--click") != null
               || ArgStr(args, "--press") != null
               || ArgStr(args, "--type") != null
               || Array.IndexOf(args, "--dump-hits") >= 0
               || ArgStr(args, "--edit") != null
               || Array.IndexOf(args, "--save") >= 0
               || Arg(args, "--tick-rate", 0) > 0;

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

        /// **The frame-driven instruments need a window, and now say so
        /// instead of waiting for one that is never coming.**  --shot waits on
        /// FramePostDraw for the two frames it captures and ClickScript for the
        /// frame each click is tested against; the headless rendering driver
        /// never emits it, so the await never resumes, Quit is never reached,
        /// and the run hangs until something kills it -- no PNG, no output, no
        /// exit code.  Four probes in one session were lost to that before it
        /// was a message.  (--dump-hits is doubly dead headless: the hit list is
        /// built by _Draw and by nothing else.)
        ///
        /// Exit 2, which is what the other usage errors in this file use: the
        /// arguments were wrong, the game was not.
        private bool NeedsWindow(string flag)
        {
            if (DisplayServer.GetName() != "headless") return false;
            GD.PrintErr(flag + " needs a window -- the headless driver draws no "
                        + "frames, so there is nothing to capture and nothing to "
                        + "click.  Drop --headless.");
            GetTree().Quit(2);
            return true;
        }

        private async void Shot(string path)
        {
            if (NeedsWindow("--shot")) return;
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
            GD.PrintRaw(ChromeLine());
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
        private static Strings LoadLanguage(string code)
        {
            string dir = Paths.Data(Strings.DirName);
            Strings got = Strings.Load(dir, code);
            if (got == null)
                GD.PrintErr("no language catalogue under " + dir);
            Strings.SetCurrent(got);
            return got;
        }

        /// Every label on screen goes through here.
        ///
        /// Never null once _Ready has run: Strings.Load falls back to the base
        /// language for an unknown code and this falls back again to an empty
        /// catalogue if data/language/ is missing outright, so a label is at
        /// worst `[quit.title]` and never a NullReferenceException in the middle
        /// of a draw.  `--check-strings` reads this same property, which is the
        /// point of it being a property.
        internal Strings Strings => _lang ?? Strings.Empty;

        /// The language picker's live preview, and step 6's whole apply path.
        /// Nothing is reloaded but the strings -- no sheet, no level, no tick --
        /// because nothing else depends on them.
        internal void ApplyLanguage(string code)
        {
            Strings got = Strings.Load(Paths.Data(Strings.DirName), code);
            if (got != null) { _lang = got; Strings.SetCurrent(got); }
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
        /// tools/options_check.py to compare against the files it wrote.  The
        /// sheet hash is the part that proves the *pixels* followed the option
        /// and not just the label -- it is the same sha256 --check-sheets
        /// prints, so an external pack unpacked out of a .ltg must match that
        /// .ltg exactly.
        private int CheckOptions(string levels, int level)
        {
            byte[] h = System.Security.Cryptography.SHA256.HashData(_atlas.Sheet.Rgba);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            GD.PrintRaw(string.Format(inv,
                "options ini={0} settings={20}\n" +
                "options size={1} cell={2} laser_offset={3}\n" +
                "options graphics_mode={4} graphics_file={5} graphics_dir={6}\n" +
                "options pack={7} label={8} sha256={9}\n" +
                "options rll={10} rll_file={11} rll_level={12}\n" +
                "options sound={13} animation={14} auto_record={15}\n" +
                "options skip_completed={18} difficulty={19}\n" +
                // **name= is last on its line** because a name has spaces
                // in it, and every reader of these lines splits on
                // whitespace -- the same arrangement `pack ... label=`
                // has, and for the same reason.
                "options initials={17} name={16}\n",
                // **The preset, not the live layout.**  `--check-options` runs
                // before there is a window or a Session, so `Cell` is whatever
                // the unmeasured layout holds -- zero.  What this line is about
                // is the *option*: [SCREEN] Size, and the cell it means.  Since
                // step 7 that is a preset the window snaps to rather than the
                // only size the board can be, so it is read straight off the
                // size the same way the store holds it.
                _opt.ImportedFrom ?? "-", _size, CellOf(_size),
                LaserOffsetFor(CellOf(_size)),
                _pack.Mode, _pack.File.Length > 0 ? _pack.File : "-", _opt.GraphicsDir,
                _pack.Mode == 1 ? "external" : _pack.Mode == 0 ? "internal" : _pack.File,
                _atlas.Label, Convert.ToHexString(h).ToLowerInvariant(),
                _opt.RememberLastLevel ? "Yes" : "No",
                _opt.LastLevelFile.Length > 0 ? _opt.LastLevelFile : "-", _opt.LastLevel,
                _opt.SoundOn ? "Yes" : "No",
                // Step 4's three: the option that moves a trace, the one that
                // starts the recorder, and the name -- which was two lines here
                // until step 14 merged [DATA] Player and [DATA] Record Author
                // into one value.  Both are printed still, because `initials`
                // is what a .hs will carry and it is derived rather than typed.
                _opt.AnimationOn ? "Yes" : "No", _opt.AutoRecord ? "Yes" : "No",
                _opt.Name.Length > 0 ? _opt.Name : "-",
                _opt.Initials.Length > 0 ? _opt.Initials : "-",
                // Item 3's two.  The mask is printed as the number the file
                // carries rather than as five names, because what the gate is
                // checking is the *value* -- that a 2010 binary's Diff_Setting
                // imports, and that the 0 it can hold reads as all five.
                _opt.SkipCompleted ? "Yes" : "No", _opt.Difficulty,
                // **Both paths, because they are two different files now**:
                // `ini=` is what this run imported from, or `-` when it
                // imported from nothing, and `settings=` is the store it
                // actually reads and writes.  A gate that wants to know whether
                // the importer ran at all asks the first one.
                _opt.StorePath));
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
            // Command 106 stops the clock, because the original stops it for
            // that one: `x = Game_On; GameOn(FALSE); DialogBox(...)`
            // (LTANK.C:906).  The graphics dialog (226) does not, so the tank
            // can die while it is up -- see GraphicsSection.
            //
            // **The list panel stops it on every tab since step 8**, which is
            // where it stopped matching the original exactly: 113 and 906 did
            // not stop the clock, and the merged panel cannot honour both rules
            // without starting and stopping the board as the tabs change.  The
            // reasoning, and what it costs, is in LevelList's header.
            if (_list != null && _list.Open && _list.StopsClock) return;
            // The quit prompt freezes it too, for a plainer reason than any of
            // the above: it is a question about the session, and a tank that
            // dies while the player decides whether to leave has been killed by
            // the interface.
            if (_quitAsk) return;
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

        /// The pointer left the window, so nothing is hovered any more.  Without
        /// this the last target under the cursor stays lit after the mouse has
        /// gone somewhere else entirely, which reads as a button waiting to be
        /// pressed.
        public override void _Notification(int what)
        {
            if (what == NotificationWMMouseExit)
                _hits.Point(new Vector2(-1e6f, -1e6f));
        }

        public override void _UnhandledInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton mb)
            {
                // **SetInputAsHandled moved out here in step 9.**  It may only
                // be called while an input event is being dispatched, and
                // `--click` drives MouseButton from a coroutine instead -- so
                // the arm answers whether it took the click and the caller,
                // which knows whether it is the OS, marks it.
                if (MouseButton(mb)) GetViewport().SetInputAsHandled();
                return;
            }
            if (ev is InputEventMouseMotion mm) { MouseMotion(mm); return; }
            if (ev is not InputEventKey k || !k.Pressed) return;

            // The quit prompt is tested before every other panel because it is
            // the most modal thing here -- it can only be raised from play, but
            // once it is up nothing else may take a key from it.  Enter or Y
            // quits; every other key, Esc included, is No, so the answer a
            // mistaken keypress lands on is always the safe one.
            if (_quitAsk)
            {
                if (k.Keycode is Key.Enter or Key.KpEnter or Key.Y) GetTree().Quit();
                _quitAsk = false;
                GetViewport().SetInputAsHandled();
                return;
            }

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

            // **The settings panel, which was four of these blocks until step
            // 18.**  It is a modal dialog: while it is up the main window has no
            // focus, so no WM_KEYDOWN fires and nothing reaches AddKBuff -- not
            // even the arrows and space, which is what makes it safe for the
            // panel to navigate with them.  The *timer* is not modal, though:
            // command 226 never calls GameOn(FALSE) and DialogBox's loop still
            // dispatches WM_TIMER, so the game below keeps ticking and an
            // exposed tank can die while you pick a pack.
            //
            // It takes the whole event rather than the keycode, because one of
            // its four sections is a text field and a field needs the unicode.
            if (_settings != null && _settings.Key(k))
            {
                GetViewport().SetInputAsHandled();
                return;
            }

            // Same for the level picker and the two score lists.  On Enter they
            // hand back a level number, which is `EndDialog(Dialog, i + 100)`
            // and the `if (i > 100)` that meets it (LTANK.C:910).
            if (_list != null && _list.Open)
            {
                _list.Key(k);
                int pickedLevel = _list.TakeChosen();
                if (pickedLevel > 0) _s?.Load(pickedLevel);
                GetViewport().SetInputAsHandled();
                return;
            }

            // The collection picker, which is GetOpenFileName: modal for keys
            // like every dialog here, and the one thing on the far side of it
            // is command 108's own body -- see Session.OpenDataFile.
            if (_collections != null && _collections.Open)
            {
                _collections.Key(k.Keycode);
                string pickedFile = _collections.TakeChosen();
                if (pickedFile != null) OpenDataFile(pickedFile);
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
                    // The ACC2 shares with ACC1.  **Ctrl+O is the options
                    // panel and Ctrl+G is its unlisted alias** -- 226 is on both
                    // accelerator tables, so the editor reaches the graphics
                    // section by the key the original gave it (SettingsMenu's
                    // header says why that one key stayed).  F1 is 903 in the
                    // editor's table and 907 in the game's, two help ids for the
                    // same key; the overlay answers both and swaps its list.
                    if (k.Keycode == Key.O && k.CtrlPressed) _settings.Show();
                    else if (k.Keycode == Key.G && k.CtrlPressed)
                        _settings.Show("graphics");
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

            if (Command(k.Keycode, k.CtrlPressed)) GetViewport().SetInputAsHandled();
        }

        /// **The accelerator table as a function, which is what step 9 needed.**
        /// It was the tail of _UnhandledInput and nothing else could reach it,
        /// so every clickable thing the redesign drew would have had to carry
        /// its own copy of what the key does.  Pulling it out means the chrome
        /// *presses the key*: a row in the F1 overlay, a keycap in the column
        /// and the key itself are three ways into one switch, and a binding
        /// added here is reachable from all three or from none.
        ///
        /// -> false when the code is not bound, which is the caller's cue to
        /// leave the event alone.
        internal bool Command(Key code, bool ctrl)
        {
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
            switch (code)
            {
                // ---- levels -------------------------------------------------
                // **106, and 113 and 906 with it.**  The level picker and the
                // two high-score lists were three keys onto three column sets
                // of one row; step 8 prints all three at once, so `L` opens the
                // table and there is nothing else to press -- see LevelList.
                // `V` and `G` are unbound as a result and free for something,
                // which is the *reason* the merge was worth doing rather than a
                // side effect of it: ACC1 has no spare letters.  Ctrl+V (112,
                // restore position) and Ctrl+G (226, graphics) are untouched --
                // they are different accelerators, and both are still below.
                case Key.L when !ctrl: OpenList(); break;              // 106
                case Key.O when !ctrl: OpenCollections(); break;       // 108

                // **107 and 119, and they are the two filtered ones.**
                // `LoadNextLevel(FALSE, FALSE)` is command 107's whole body
                // (LTANK.C:923) and `LoadLastLevel` is 119's, and both walk the
                // collection through [DATA] Diff_Setting and [OPT] SkipComLev
                // rather than stepping one record -- see Session.Advance.
                case Key.S: Advance(+1); break;                       // 107
                case Key.P: Advance(-1); break;                       // 119

                // **Ours, and deliberately *not* filtered.**  The original has
                // no such pair, so nothing is being deviated from; what they
                // are for is the case the filter creates -- a mask that hides
                // the level you actually wanted is a setting you now have to go
                // and change before you can look at it.  One bracket is one
                // record, always, which is also what makes them the pair to
                // reach for when something looks wrong with the walk itself.
                case Key.Bracketright: _s?.Load(_s.Level + 1); break;
                case Key.Bracketleft: _s?.Load(_s.Level - 1); break;

                // **118, and it is the original's own key.**  `VK_BACK` in ACC1
                // (lt32l_us.inc:134) is free in this port -- it reaches nothing
                // but a text field, and every text field here is inside a panel
                // the router has already answered by this line.  It is also
                // outside VK 32..40, so it is an accelerator and only an
                // accelerator, the same as every letter below.
                //
                // **It earns the key, which next-steps item 9 asked to decide
                // before building it.**  Step 11 gave the level list a filter
                // and direct number entry, so the argument against was that a
                // back stack is redundant when the list is one keystroke away.
                // It is not the same question: the list answers *which level*
                // and this answers *the one I was just on*, which is a level
                // whose number you looked away from.  The case it is actually
                // for is the one the brackets and the filtered walk create --
                // step off to look at something, come back -- and it costs one
                // row and one key nothing else wants.
                case Key.Backspace: Back(); break;                    // 118
                case Key.Enter:
                    // The original's flag case calls LoadNextLevel straight
                    // away (LTANK.C:655); a Godot win waits, so the recording
                    // is still there to save.  It is the same filtered call the
                    // original makes there -- `LoadNextLevel(FALSE, FALSE)` --
                    // so winning the last Deadly level in a Kids-only mask ends
                    // the collection rather than dropping you on level 2.
                    if (_s != null && _s.Now == Session.State.Won) Advance(+1);
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
                    if (!undone) _error = Strings["status.nothingToUndo"];
                    break;
                case Key.C when ctrl:
                    _s?.SavePos();
                    _error = Strings["status.positionSaved"];
                    break;
                case Key.V when ctrl:                                 // 112
                    if (_s != null && !_s.RestorePos())
                        _error = Strings["status.noSavedPosition"];
                    break;

                // ---- recording and playback ---------------------------------
                case Key.F5: ToggleRecording(); break;                // 123
                case Key.F6: SaveRecording(); break;                  // 117
                case Key.F7: OpenPlayback(); break;                   // 114
                case Key.F4: _s?.Replay(); break;                     // 124

                // ---- options ------------------------------------------------
                // **One panel since step 18** (next-steps item 14).  It was
                // four: Ctrl+G (226, GraphBox), Ctrl+L (step 6's language
                // picker), Ctrl+N (step 14's name row) and Ctrl+O (step 15's
                // game options, which is 116 and 225).  Four dialogs of one
                // shape on four modifiers is one dialog with four sections, and
                // step 15 settled which modifier: `Ctrl+O` is the one of the
                // four with original command ids behind it, and it is free in
                // both accelerator tables -- ACC1 binds VK_O bare (108, Open
                // Data File, which is why the `when !ctrl` above arrived with
                // this) and ACC2 does not bind it at all.
                //
                // **Ctrl+L and Ctrl+N are unbound and free**, both of them this
                // port's own invention, which is what next-steps item 13 was
                // promised.
                case Key.O when ctrl: _settings.Show(); break;        // 116, 225
                // **Ctrl+G stays, and is not in the F1 list.**  It is the
                // original's own accelerator for 226 on *both* tables, and the
                // rule in this project is that when the original has a table
                // you read the table -- so it survives as an unlisted alias onto
                // the section 226 became, which is item 13's own device for `S`.
                // See SettingsMenu's header.
                case Key.G when ctrl: _settings.Show("graphics"); break;  // 226
                // Commands 120/121/122, the Options menu's three sizes.
                case Key.Z: SetSize(_size % 3 + 1); break;
                case Key.I: _interpolate = !_interpolate; break;
                // Ours, and no command id: the original has no grid to toggle.
                // Plain `C` is free in both accelerator tables -- ACC1 binds
                // VK_C only with CONTROL (111, Save Position) and so does ACC2
                // (601, Clear Field) -- so this takes no key the original used.
                case Key.C:
                    _error = Strings[ToggleGrid() ? "status.gridOn" : "status.gridOff"];
                    break;
                // Command 102, "Sound" (LTANK.C:875).  The checkmark is the INI
                // here; ToggleOpt writes it immediately.
                case Key.N when !ctrl:
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
                    _error = Strings[ani ? "status.animationOn"
                                         : "status.animationOff"];
                    break;

                // Command 301, "Hint" (LTANK.C's ButText7 button, VK_H in ACC1).
                // The original raises a dialog; this is a card in the column, or
                // a panel over the board when the window is too narrow for one.
                case Key.H:
                    if (string.IsNullOrEmpty(_s?.Rec.Hint))
                        _error = Strings["status.noHint"];
                    else
                        _hint = !_hint;
                    break;

                // F1: command 907 here, 903 in the editor.  903 is WinHelp on
                // LaserTank.hlp and 907 is the Quick About Box -- neither ships,
                // and the keys are what this port can answer with.
                case Key.F1: _help = true; break;
                // Command 115, "Auto Record" (LTANK.C:978), which also turns the
                // recorder itself on or off.
                case Key.F8:
                    _error = Strings[_s != null && _s.Rec2.ToggleAutoRecord()
                                     ? "status.autoRecordOn"
                                     : "status.autoRecordOff"];
                    break;

                // Command 201, "Editor" -- VK_F9 in ACC1 (lt32l_us.inc:132).
                case Key.F9:
                    if (_s?.E != null) { _edit.Enter(_s); Resize(); }
                    break;

                // Ours, and it asks first -- see _quitAsk.
                case Key.Escape: _quitAsk = true; break;
                default: return false;
            }
            return true;
        }

        /// The accelerators that repeat while the key is held.  Undo is the
        /// one so far: walking a mistake back is a *rate*, not an event, and
        /// the original's accelerator repeated it -- see the echo test above.
        /// The repeat rate is the OS's, not the game's 20 Hz tick, which is
        /// right: UndoStep is not a tick, it is a WM_COMMAND, and the original
        /// took it as fast as Windows sent it.
        private static bool RepeatsOnHold(Key k) => k == Key.U;

        // ---- the chrome, which is step 9's whole subject --------------------

        /// **A chrome click is an accelerator by another route**, so it goes to
        /// whichever table is live -- ACC1 while playing, ACC2 in the editor --
        /// by walking the same two branches the key router walks.  That is the
        /// one rule this arm has to keep: the chrome may not reach a command
        /// the keyboard could not have reached from where the player is
        /// standing.  Clicking `muted` in the top bar while the editor is open
        /// is therefore swallowed exactly as pressing N there is.
        ///
        /// Named for what it does: the chrome *presses the key*.  `Chrome` next
        /// to it is the hit list, which is a different noun.
        internal void Press(Key code, bool ctrl = false)
        {
            if (_edit != null && _edit.Open)
            {
                if (!_edit.Key(new InputEventKey
                    { Keycode = code, CtrlPressed = ctrl, Pressed = true }))
                {
                    // ACC2's shares with ACC1, as in the router.
                    if (code == Key.O && ctrl) _settings.Show();
                    else if (code == Key.G && ctrl) _settings.Show("graphics");
                    else if (code == Key.F1) _help = true;
                }
                if (!_edit.Open) Resize();
                return;
            }
            // The status line is the last thing the player *did*, and a click is
            // as much a thing done as a key is -- the router clears it on every
            // key for that reason and this is the same reason.
            _error = null;
            Command(code, ctrl);
        }

        /// Register a rectangle the board's own chrome just drew.
        ///
        /// **This is the modality rule applied to the third arm.**  The key
        /// router tests six panels before it reaches the accelerators, so a key
        /// pressed under a dialog never gets to them; a click has to answer to
        /// the same list, or the mouse becomes a way around a modality the
        /// keyboard respects -- muting the game from the top bar while a dialog
        /// holds the keyboard.  The panels themselves register through
        /// `Chrome` (the Hits instance) directly, because they *are* the modal
        /// thing.  The editor is not on the list, because the editor is a mode
        /// of the window rather than a dialog over it -- the same reason it is
        /// not in that half of the router.
        private bool Hit(Rect2 r, string name, Action act)
            => ChromeLive && _hits.Add(r, name, act);

        /// The common case: a rectangle that presses a key.  Naming it after
        /// the key is what lets tools/chrome_check.py diff the click against
        /// the keystroke it is drawn as -- see Hits.
        private bool HitKey(Rect2 r, Key code, bool ctrl = false)
            => Hit(r, KeyName(code, ctrl), () => Press(code, ctrl));

        /// How a binding is spelled in --dump-hits and --press.  One function,
        /// so the two cannot drift.
        internal static string KeyName(Key code, bool ctrl)
            => "key:" + (ctrl ? "ctrl+" : "") + code;

        private bool ChromeLive =>
            !_quitAsk && !_help
            && !(_settings != null && _settings.Open)
            && !(_list != null && _list.Open)
            && !(_collections != null && _collections.Open)
            && !(_s != null && _s.Pb.PanelUp);

        /// The wheel, over whichever list is open.  The panels keep one cursor
        /// and clamp the viewport to it (LevelList.Move), so scrolling here
        /// *moves the selection* rather than introducing a second, independent
        /// scroll position that the next arrow key would jump away from.
        private bool Scroll(int d)
        {
            if (_list != null && _list.Open) { _list.Scroll(d); return true; }
            if (_collections != null && _collections.Open) { _collections.Scroll(d); return true; }
            if (_settings != null && _settings.Open) { _settings.Scroll(d); return true; }
            return false;
        }

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
        /// -> true when this arm took the click, which is the caller's cue to
        /// mark the event handled.
        private bool MouseButton(InputEventMouseButton mb)
        {
            _hits.Point(mb.Position);

            // The wheel is not a click and never reaches the board: the
            // original's non-editor arm takes a *cell*, and a wheel has no
            // position to give it.  It scrolls whichever list is up and does
            // nothing when none is.
            if (mb.ButtonIndex is Godot.MouseButton.WheelUp
                              or Godot.MouseButton.WheelDown)
            {
                return mb.Pressed
                       && Scroll(mb.ButtonIndex == Godot.MouseButton.WheelUp ? -3 : 3);
            }
            // The one drag in this interface is released here too -- see
            // the `scroll` target below and LevelList.DragTo.
            if (!mb.Pressed) { _held = 0; _dragList = false; return false; }

            // **The third arm, and it comes before the other two.**  Everything
            // the last frame drew as clickable registered the rectangle it drew
            // itself in (see Hits); if the click landed on any of it, that is
            // what the click was, and nothing below may see it.  A chrome click
            // must never reach MBuffer -- a tank that moved because the player
            // pressed `undo` is the interface driving the game.
            if (_hits.Click(mb.Position))
            {
                // **The one target that is dragged rather than clicked.**  The
                // level list's scrollbar wants the pointer's *position*, which
                // a hit list of rectangles-and-actions has no way to carry, and
                // it wants every motion until the button comes up.  So the
                // press latches here and MouseMotion feeds LevelList.DragTo --
                // the rest of the chrome stays click-only, which is the whole
                // reason Hits can be as simple as it is.
                _dragList = _hits.Took == "scroll" && _list != null && _list.Open;
                if (_dragList) _list.DragTo(mb.Position.Y);
                // The two pickers answer in a field rather than in a return
                // value -- `EndDialog(Dialog, i + 100)` and the `if (i > 100)`
                // that meets it -- so the click path has to collect it in
                // exactly the place the key path does.
                // **Taken, not read.**  This arm runs after *every* chrome
                // click -- a chip in the options panel, a keycap on the top bar
                // -- because the click that commits a picker is an ordinary
                // chrome click and there is no return value to catch it in.  A
                // picker that left its answer behind would therefore re-load
                // the level it was last given on the next click anywhere, which
                // is the one bug this shape can have: see LevelList.TakeChosen.
                int picked = _list != null ? _list.TakeChosen() : 0;
                if (picked > 0) _s?.Load(picked);
                string pickedColl = _collections?.TakeChosen();
                if (pickedColl != null) OpenDataFile(pickedColl);
                return true;
            }

            // The same guards the key router has, in the same order and for the
            // same reason: a modal window over the board takes the mouse with
            // it.  They sit *behind* the chrome rather than in front of it
            // because every panel registers its own scrim, so while one is up
            // the test above has already answered -- these are what answer on
            // the frame a panel was opened and not yet drawn, and headless,
            // where nothing draws at all and the hit list is always empty.
            if (_quitAsk) return false;
            if (_settings != null && _settings.Open) return false;
            if (_list != null && _list.Open) return false;
            if (_collections != null && _collections.Open) return false;
            if (_s != null && _s.Pb.PanelUp) return false;

            int button = mb.ButtonIndex switch
            {
                Godot.MouseButton.Left => 1,
                Godot.MouseButton.Right => 2,
                _ => 0,
            };
            if (button == 0) return false;
            _held = button;

            if (_edit != null && _edit.Open)
            {
                _edit.Click(mb.Position, button, mb.ShiftPressed);
                return true;
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
            if (_s?.E == null) return false;
            if (!CellAt(mb.Position, out int x, out int y)) return false;
            _s.Click(x, y, button);
            return true;
        }

        /// WM_MOUSEMOVE (LTANK.C:764).  **Only the editor has one**: out of the
        /// editor the original's WM_MOUSEMOVE case does nothing at all, so
        /// dragging across the board while playing queues nothing.
        private void MouseMotion(InputEventMouseMotion mm)
        {
            // Hover, which is the only affordance the chrome gives a desktop
            // player for free -- see Hits.End.  It is taken from every motion
            // event, including the ones the two arms below ignore.
            _hits.Point(mm.Position);
            // The level list's scrollbar, which is the one thing here that reads
            // a motion the editor did not ask for.  It comes first because a
            // drag that started on the chrome must not also paint.
            if (_dragList && _list != null && _list.Open)
            {
                _list.DragTo(mm.Position.Y);
                return;
            }
            if (_held == 0 || _edit == null || !_edit.Open) return;
            _edit.Drag(mm.Position, _held, mm.ShiftPressed);
        }

        /// Which button is down, for the drag.  The original reads it out of
        /// `wparam`'s MK_LBUTTON / MK_RBUTTON on every WM_MOUSEMOVE; Godot
        /// delivers press and release, so it is kept here instead.
        private int _held;

        /// Whether the press that is down started on the level list's
        /// scrollbar.  Cleared on release and ignored once the panel is gone,
        /// so a drag cannot outlive the thing it was dragging.
        private bool _dragList;

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
                _error = p == null ? Strings["status.notRecording"]
                                   : Strings.F("status.recordingSaved",
                                               Path.GetFileName(p));
            }
            catch (Exception ex) { _error = ex.Message; }
        }

        /// Command 123 (F5).  The original's checkmark is the window title --
        /// `SetWindowText(MainH, REC_Title)` -- so it is the window title here
        /// too, and the status line as well, because a title bar is easy to
        /// miss.
        private void ToggleRecording()
        {
            if (_s == null) return;
            bool on = _s.Rec2.Toggle();
            _error = Strings[on ? "status.recordingOn" : "status.recordingOff"];
            DisplayServer.WindowSetTitle(
                on ? Strings.F("app.titleRecording", AppTitle) : AppTitle);
        }

        /// LT32L_US.H:10.  Not a translatable string: `App_Title` is a
        /// compile-time constant in the original, outside the 240 lines, none of
        /// the ten files translated it, and it is the name of the game.
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

        private void OpenList()
        {
            if (_s == null) return;
            _list.Show(_s.LevelPath, _s.Level);
        }

        /// Commands 107 and 119 -- Skip Level and Previous Level -- which are
        /// `LoadNextLevel(FALSE, FALSE)` and `LoadLastLevel` and therefore the
        /// two filtered ones.  Session.Advance is the walk; this is what it
        /// looks like when the walk finds nothing.
        ///
        /// **The end of the collection and the end of the filter are two
        /// different sentences**, and that is the whole reason `filtered` comes
        /// back out of Advance.  The original says neither -- it puts up
        /// WM_GameOver's box and leaves you on the level you were on -- but the
        /// original also cannot reach the second case without having answered
        /// the Difficulty dialog on the way in, so the mask is never a surprise
        /// there.  Here it is a setting two keystrokes away that somebody set
        /// last week, so when it is the mask that stopped the walk the status
        /// line names the mask.  Nothing moves in either case, which is
        /// `CurLevel = SavedLevelNum`.
        private void Advance(int dir)
        {
            if (_s == null) return;
            if (_s.Advance(dir, out bool filtered)) return;
            // A level that will not load is the third way this comes back
            // false, and it is the only one with something to say -- Advance
            // clears `Error` so a non-null one here is this walk's own.
            _error = _s.Error
                     ?? Strings[filtered ? "status.noneMatch"
                                         : dir > 0 ? "status.lastLevel"
                                                   : "status.firstLevel"];
        }

        /// Command 118, Last Level Played -- Session.Back is the stack and this
        /// is what an empty one looks like.
        ///
        /// **The message names the fact and not the key**, the way
        /// `status.noSavedPosition` does: "there is nowhere back" is a thing
        /// about this session, and a player who just pressed Backspace does not
        /// need to be told which key they pressed.  The original says nothing
        /// at all here -- it greys the menu item, which is a thing a menu bar
        /// can do and a status line cannot.
        private void Back()
        {
            if (_s == null) return;
            if (!_s.Back()) _error = _s.Error ?? Strings["status.noLastLevel"];
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
                _error = Strings.F("status.collectionOpened",
                                   Path.GetFileName(lvlPath), _s.LevelCount);
            else
                _error = _s.Error ?? Strings.F("status.cannotOpen",
                                               Path.GetFileName(lvlPath));
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
                _error = bad ?? Strings.F("status.playing", Path.GetFileName(cand));
                return;
            }
            _error = Strings["status.noRecording"];
        }

        public override void _Draw()
        {
            Measure();
            // **The frame is also the hit test.**  Every clickable thing below
            // registers the rectangle it draws in, in draw order, and the mouse
            // is tested against the list this leaves behind -- see Hits, and
            // MouseButton for where that test goes.
            _hits.Begin();
            DrawFrame();
            _hits.End();
        }

        private void DrawFrame()
        {
            Font font = Ui.Sans;

            // The ground, always -- the window is resizable now, so there is
            // usually more of it than there is board.
            DrawRect(_l.Window, Ui.Bg);

            if (_s?.E == null || _atlas == null)
            {
                DrawTopBar();
                Ui.Card(this, _l.Well);
                Ui.Write(this, _l.Well.Position + new Vector2(Ui.Px(20), Ui.Px(36)),
                         _error ?? Strings["status.noLevel"], 15, Ui.Bad,
                         _l.Well.Size.X - Ui.Px(40));
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
            if (_settings.Open) _settings.Draw(this, font, host);
            if (_list.Open) _list.Draw(this, font, _mono, host);
            if (_collections.Open) _collections.Draw(this, font, _mono, host);
            // The stacked layout has no column to hold the hint card, so command
            // 301 becomes a panel there instead -- under the dialogs, because it
            // is content rather than a prompt.
            if (_hint && _l.Stacked && !string.IsNullOrEmpty(_s.Rec.Hint))
                DrawHintOverlay(host);
            if (_s.Pb.PanelUp) DrawPlaybackPanel(host);
            if (_help) DrawHelp(host);
            // Over everything, including the help overlay: it is a question,
            // and a question that something else can cover is a question the
            // player answers blind.
            if (_quitAsk) DrawQuitAsk(host);
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
        /// one, and either the posted best or -- when the posted best has been
        /// beaten -- a congratulation.  What the dialog also does, ask for the
        /// initials, is `[DATA] Player` here, read before the write rather than
        /// after.
        ///
        /// **Assembled from clauses rather than from one format string**, which
        /// is deliberate: which clauses appear depends on four independent facts
        /// (a playback, a world best, a personal best, a `.hs` that would not
        /// write), and a single string with eight optional halves is not
        /// something a translator can work on.  Each clause is a key and the
        /// separator is the chrome's own middot.
        ///
        /// **And it sheds a clause at a time rather than being clipped**, which
        /// is the rule the list footers already follow and the one line of the
        /// interface that had escaped it.  English fits at the default window by
        /// about a word; German does not (`Enter für das nächste Level, F6
        /// sichert die Aufzeichnung` is half again as long as its English), and
        /// a status line cut mid-word reads as a fault rather than as a squeeze.
        ///
        /// The ranking is the argument: **par goes first** because the info
        /// column is showing it three inches away and this is the only clause in
        /// the line that is duplicated on screen; **the prompt goes second**,
        /// because `Solved` in green already says the level is over and F6 is on
        /// F1's list; the verdict and the `.hs` error never go, because nothing
        /// else on screen carries either.
        private string WinLine(float maxW)
        {
            const string Sep = "  ·  ";
            ScoreResult r = _s.Score;
            string solved = Strings["win.solved"];
            string next = Strings["win.next"];
            if (r == null)
                return _s.Pb.Open ? Strings["win.playback"]
                                  : Fit(maxW, solved + Sep + next, solved);

            string head = solved + Sep
                        + (r.Global ? Strings["win.worldBest"]
                         : r.Personal ? Strings["win.personalBest"]
                         : Strings.F("win.standing",
                                     HighScores.Describe(r.Old, Strings)));
            string par = r.Target != null && !r.Global
                ? "   (" + Strings.F("win.par", r.Target.Moves, r.Target.Shots) + ")"
                : "";
            string err = r.Error != null
                ? "   [" + Strings.F("win.notWritten", r.Error) + "]" : "";

            return Fit(maxW, head + par + err + Sep + next,
                            head + err + Sep + next,
                            head + err);
        }

        /// The first of `forms` that fits in `w`, or the last of them.  Same
        /// shape as LevelList.Footer and CollectionList.Footer, which is the
        /// point: there is one answer in this interface to "the measurement says
        /// no", and it is to drop a whole clause rather than a few characters.
        private static string Fit(float w, params string[] forms)
        {
            foreach (string f in forms)
                if (Ui.Width(f, 12) <= w) return f;
            return forms[forms.Length - 1];
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
            // **Not a filled bar.**  Step 7 drew this as a surface panel with a
            // hairline under it, which is the header component every framework
            // ships and reads as one.  The ground is simply the window's, and
            // what separates the bar from the board is the rule -- a line the
            // eye takes as an edge rather than a slab it takes as a widget.
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
            // The wordmark, and **the only other string in the port set in the
            // display face** (the level name is the first -- see DrawLevelBlock).
            // Amber rather than white: it is the first thing in the window and
            // the dominant colour should be established there rather than
            // discovered later beside a number.
            DrawString(Ui.Mark, new Vector2(x, mid + Ui.Px(6)), "LASERTANK",
                       HorizontalAlignment.Left, -1, Ui.Px(19), Ui.Accent);
            x += Ui.Width("LASERTANK", 19, Ui.Mark) + Ui.Px(14);

            // The collection, which the level number alone does not say: every
            // one of the 23 opens at level 1 and three of those level 1s are the
            // same tutorial screen (see OpenDataFile).
            if (_s != null)
            {
                DrawRect(new Rect2(x, mid - Ui.Px(9), Mathf.Max(1, Ui.Px(1)), Ui.Px(18)),
                         Ui.Border);
                x += Ui.Px(14);
                // **The top bar's one button.**  It names what is loaded, and
                // command 108 is how another gets loaded, so the label and the
                // command are one place.  This is the pointing route worth
                // having most: `O` is the least guessable letter in ACC1, and
                // the name beside it is the only thing on screen that says what
                // it would change.
                string coll = Path.GetFileNameWithoutExtension(_s.LevelPath);
                float cwid = Mathf.Min(Ui.Width(coll, 12.5f), r.Size.X * 0.4f);
                var box = Ui.Touch(new Rect2(x - Ui.Px(7), mid - Ui.Px(12),
                                             cwid + 2 * Ui.Px(7), Ui.Px(24)));
                bool hot = HitKey(box, Key.O);
                if (hot) Ui.Hot(this, box);
                Ui.Write(this, new Vector2(x, mid + Ui.Px(4)), coll, 12.5f,
                         hot ? Ui.Text : Ui.Dim, r.Size.X * 0.4f);
            }

            // The pills, right to left: the exceptional states first, so the
            // one that matters is always in the same place -- hard against the
            // edge -- rather than wherever the ones before it happened to end.
            DrawTopPills(r, pad, mid);
        }

        /// **Each pill is a state, and since step 9 clicking one is how you
        /// leave it** -- the key that put it there, pressed for you.  That the
        /// affordance is one-way is the honest shape of it rather than a gap:
        /// the pills are drawn only for the states that are *exceptional*, so
        /// there is no `muted` chip to click while the sound is on.  The way in
        /// is still the key; the way out is the thing on screen saying you are
        /// in.
        private void DrawTopPills(Rect2 r, float pad, float mid)
        {
            var pills = new System.Collections.Generic.List<(string, Color, Color, Key)>();
            if (_s != null && _s.Rec2.Recording)
                pills.Add((Strings["pill.rec"], Ui.Bad,
                           new Color(0.24f, 0.09f, 0.09f), Key.F5));
            if (_s != null && _s.Pb.Open)
                pills.Add((Strings["pill.playback"], Ui.Cyan,
                           new Color(0.07f, 0.16f, 0.18f), Key.F7));
            if (_edit != null && _edit.Open)
                pills.Add((Strings["pill.editor"], Ui.Accent,
                           new Color(0.20f, 0.14f, 0.05f), Key.F9));
            if (_opt != null && !_opt.SoundOn)
                pills.Add((Strings["pill.muted"], Ui.Faint, Ui.Raised, Key.N));
            if (_opt != null && !_opt.AnimationOn)
                pills.Add((Strings["pill.still"], Ui.Faint, Ui.Raised, Key.A));
            // The pinned cell size, because it is the one piece of state the
            // window itself does not show: a board that exactly fills its well
            // and a board snapped to 32 px look the same until you drag.  `Z`
            // cycles the three, which is what a click on it does.
            pills.Add((_pinCell > 0 ? Strings.F("pill.snap", _pinCell)
                                    : Strings.F("pill.snapFit", Cell),
                       Ui.Faint, Ui.Raised, Key.Z));

            float h = Ui.Px(10) + Ui.Px(8);
            float x = r.End.X - pad;
            for (int i = pills.Count - 1; i >= 0; i--)
            {
                (string text, Color fg, Color bg, Key code) = pills[i];
                float w = Ui.CapsWidth(text, 10) + 2 * Ui.Px(7);
                x -= w;
                bool hot = HitKey(Ui.Touch(new Rect2(x, mid - h / 2f, w, h)), code);
                Ui.Pill(this, x, mid - h / 2f, text, hot ? Ui.Text : fg,
                        hot ? Ui.Raised : bg);
                x -= Ui.Px(6);
            }
        }

        // ---- the info column ------------------------------------------------

        /// The wide layout's right-hand column.
        ///
        /// **Step 7 built this as three cards and step 10 took the cards away.**
        /// The cards were the single most template-shaped thing in the port:
        /// three bordered surfaces at one radius, one border colour and one gap,
        /// stacked, each opening with the same small letter-spaced caps label.
        /// Three peer boxes rank nothing -- and ranking is the whole job of this
        /// column, because a player glances at it for the two counters and reads
        /// the rest once.
        ///
        /// What replaced them is a **rail**: one vertical hairline down the left
        /// of the column that every group hangs off, with the level's own span of
        /// it lit in the dominant colour.  The groups are separated by a rule and
        /// by air, and they are ranked by type size -- the level name is set in
        /// the display face at better than twice the body, the counters are the
        /// only other large thing, and everything else is 11 px mono.  Nothing
        /// here is boxed, which is also what buys the counters their size: a
        /// number inside a bordered tile has to leave room for the tile.
        ///
        /// The rail is the asymmetry the old layout had none of.  Three centred
        /// cards in a column are symmetric about their own axis and read as a
        /// component stack; a rail has a side, so the column has a spine and a
        /// reading edge, and the eye starts in the same place every time.
        private void DrawInfoColumn()
        {
            Rect2 s = _l.Side;
            float railX = s.Position.X;
            float x = railX + Ui.Px(16);
            float w = s.End.X - x;
            float y = s.Position.Y + Ui.Px(4);

            // The rail's full span first, in the quiet colour; the lit section is
            // painted over it once the title block knows how tall it is.
            Ui.Rail(this, railX, s.Position.Y, s.Size.Y, Ui.Border);

            float titleTop = y;
            y = DrawLevelBlock(x, y, w);
            // **The lit span is the level, not the column.**  It marks where the
            // thing this window is currently about begins and ends, which is the
            // one piece of state worth spending the dominant colour on when the
            // board itself is already carrying four saturated hues.
            Ui.Rail(this, railX, titleTop, y - titleTop, Ui.Accent);

            y += Ui.Px(18);
            Ui.Rule(this, x, y, w);
            y += Ui.Px(18);

            y = DrawScoreBlock(x, y, w);

            // The hint is the only block whose height is an author's to decide,
            // so it is measured rather than reserved -- and it is only here at
            // all once `H` has asked for it (command 301).
            if (_hint && !string.IsNullOrEmpty(_s.Rec.Hint))
            {
                y += Ui.Px(18);
                Ui.Rule(this, x, y, w);
                y += Ui.Px(22);
                y = DrawHintBlock(x, y, w);
            }

            // **The column is read from both ends.**  What the level *is* flows
            // down from the top and grows with the content; what a player can
            // *do* is anchored to the bottom and never moves.  Step 7 flowed all
            // four blocks from the top, which left a third of the column blank
            // between the last card and the footer -- and blank space at the end
            // of a stack is not composition, it is just what the stack ran out
            // at.  Split, the same space becomes the gap between two groups that
            // genuinely are different in kind, and the keys sit where a hand
            // already is: beside the footer that opens the rest of them.
            //
            // It is still conditional, because the two ends can collide: a long
            // name and an open hint can reach the bottom group, and when they do
            // it is the keys that go.  The rest of them are on F1, which stays.
            float fh = Ui.Px(34);
            float actH = 5 * Ui.Px(25);
            float actY = s.End.Y - fh - Ui.Px(10) - actH;
            if (actY - Ui.Px(20) > y)
            {
                Ui.Rule(this, x, actY - Ui.Px(20), w);
                DrawActionRows(x, actY, w);
            }

            // The keys footer, pinned to the bottom of the column rather than
            // flowing after the blocks: it is a permanent affordance, and a
            // permanent thing that moves is worse than one that is out of the
            // way.
            var foot = new Rect2(x - Ui.Px(7), s.End.Y - fh, w + Ui.Px(7), fh);
            if (foot.Position.Y > y)
            {
                bool hot = HitKey(foot, Key.F1);
                if (hot) Ui.Hot(this, foot, 2f);
                float fx = x;
                float fy = foot.Position.Y + Ui.Px(6);
                fx = Ui.Keycap(this, fx, fy, "F1") + Ui.Px(10);
                Ui.Write(this, new Vector2(fx, fy + Ui.Px(15)),
                         Strings["info.allKeys"], 11f,
                         hot ? Ui.Text : Ui.Faint, foot.End.X - fx);
            }
        }

        /// The level name's point size.  Large enough that it is unambiguously
        /// the first thing in the column and not merely the boldest -- step 7's
        /// 17 px was one step up from the body and read as a card heading.
        private const float TitlePt = 25f;

        /// Which level, out of how many, by whom, at what difficulty.  Returns
        /// the y it finished at so the column can flow.
        ///
        /// **This is the one block in the interface set in the display face**,
        /// and it is the reason there is one: a level name is written by a
        /// person, it is different every level, and it is the answer to "what am
        /// I looking at".  Everything else in this window is a measurement and is
        /// set in the mono accordingly.  A display face used on more than this
        /// would be a UI sans with extra steps -- see Ui.Display.
        private float DrawLevelBlock(float x, float y, float w)
        {
            TLEVEL lv = _s.Rec;
            var info = new TLEVELINFO { SDiff = lv.SDiff };
            string name = string.IsNullOrEmpty(lv.LName) ? Strings["info.untitled"]
                                                         : lv.LName;

            // Measured, not reserved: the name is the author's and wraps to two
            // lines often enough that a fixed block clips real level names.
            float nameH = Ui.WrappedHeight(name, TitlePt, w, 2, Ui.Title);

            float top = y;

            // The level number reads as a fraction, not as a sentence: in a
            // column whose every other line is a reading, the counter should be
            // one too.
            Ui.Caps(this, new Vector2(x, y + Ui.Px(9)),
                    Strings.F("info.levelOf", _s.Level, _s.LevelCount), Ui.Faint, 9.5f);
            y += Ui.Px(9) + Ui.Px(13);

            Ui.Wrapped(this, new Vector2(x, y + Ui.Px(TitlePt) * 0.80f), name,
                       TitlePt, Ui.Text, w, 2, Ui.Title);
            y += nameH + Ui.Px(6);

            // Author and rank on one line, divided by a middot.  Two lines and a
            // chip was three vertical decisions for a fact that fits on one --
            // and the rank is a *word* here rather than a filled badge, because a
            // pill beside a name is the component-library reflex this pass is
            // trying to get out of.  The colour still carries the rank (the
            // original colours its level number by exactly this table,
            // `DifCList`, LTANK.C:532); the box around it was never carrying
            // anything.
            string rank = Strings[info.RankKey];
            Color dc = Ui.Diff[Math.Clamp((int)lv.SDiff, 0, 5)];
            const string Sep = "  ·  ";
            float ax = x;
            if (!string.IsNullOrEmpty(lv.Author))
            {
                float maxBy = w - Ui.Width(rank, 11f) - Ui.Width(Sep, 11f);
                Ui.Write(this, new Vector2(ax, y + Ui.Px(11)), lv.Author, 11f,
                         Ui.Dim, maxBy);
                ax += Mathf.Min(Ui.Width(lv.Author, 11f), maxBy);
                Ui.Write(this, new Vector2(ax, y + Ui.Px(11)), Sep, 11f, Ui.Faint);
                ax += Ui.Width(Sep, 11f);
            }
            Ui.Write(this, new Vector2(ax, y + Ui.Px(11)), rank, 11f, dc);
            y += Ui.Px(15);

            // The block says which level this is; `L` is how another gets picked.
            // Same pairing as the collection name in the top bar: the label of a
            // thing is the button that changes it.
            var hit = new Rect2(x - Ui.Px(8), top - Ui.Px(4), w + Ui.Px(8),
                                y - top + Ui.Px(6));
            if (HitKey(hit, Key.L)) Ui.Hot(this, hit, 2f);
            return y;
        }

        /// Moves, shots, and the `.ghs` par, as a readout: label hard left,
        /// number hard right, the two of them tied by the space between.
        ///
        /// **The counters are still the largest thing in the column** -- that was
        /// step 7's one genuinely good decision about this panel and it survives
        /// the cards it arrived in.  What is gone is the two bordered tiles they
        /// sat in, which is the stat tile of every analytics dashboard ever
        /// shipped, and which cost the numbers most of their size to draw.
        private float DrawScoreBlock(float x, float y, float w)
        {
            TGAMEREC g = _s.E.Game;
            bool hasPar = LevelFile.ReadHighScore(_s.Files.Ghs, _s.Level,
                                                  out ushort tm, out ushort ts);
            Row(Strings["info.moves"], g.ScoreMove, hasPar ? tm : (ushort)0, hasPar);
            y += Ui.Px(36);
            Row(Strings["info.shots"], g.ScoreShot, hasPar ? ts : (ushort)0, hasPar);
            y += Ui.Px(36);

            // The par is set small and dim on purpose: it is the *other*
            // player's number, it never changes while this level is open, and
            // the two above it are what the eye comes back to.  The gap before
            // it is wider than the gap between them for the same reason -- it
            // belongs to the pair without being one of them.
            if (hasPar)
            {
                y += Ui.Px(4);
                Ui.Caps(this, new Vector2(x, y + Ui.Px(9)), Strings["info.par"],
                        Ui.Faint, 9.5f);
                Ui.Write(this, new Vector2(x, y + Ui.Px(9)), tm + " / " + ts, 11f,
                         Ui.Dim, w, HorizontalAlignment.Right);
                y += Ui.Px(13);
            }
            return y;

            void Row(string label, int value, ushort par, bool compare)
            {
                Ui.Caps(this, new Vector2(x, y + Ui.Px(21)), label, Ui.Faint, 9.5f);
                // Amber once the count is past the posted par: the player has
                // spent the budget, which is the one thing these numbers are
                // ever compared against.  Not red -- being over par is not a
                // failure, it is just no longer a record.
                Color c = compare && par > 0 && value > par ? Ui.Accent : Ui.Text;
                DrawString(Ui.Bold, new Vector2(x, y + Ui.Px(24)), value.ToString(),
                           HorizontalAlignment.Right, w, Ui.Px(26), c);
            }
        }

        /// The handful of keys that are pressed on every level.
        ///
        /// Which five is a judgement and worth writing down: undo and restart
        /// are the two a player reaches for without looking (and are the
        /// DeadBox's own two buttons, LTANK_D.C:159); the hint is the one this
        /// redesign *hid*, so it has to be visible as an affordance or it is
        /// simply gone; and the level pair is how you leave a level you have
        /// given up on.  Everything else is F1's.
        private void DrawActionRows(float x, float y, float w)
        {
            (string, string, Key)[] rows =
            {
                ("U", Strings["action.undo"], Key.U),
                ("R", Strings["action.restart"], Key.R),
                ("H", Strings["action.hint"], Key.H),
                // Since step 8 this one panel is also both high-score lists,
                // which is what the label has to say: V and G are gone and a
                // player who used them looks here first.
                ("L", Strings["action.levels"], Key.L),
                ("O", Strings["action.collections"], Key.O),
            };
            float rowH = Ui.Px(25);
            // **Step 9 made these five rows do what they name.**  They were
            // drawn as keycaps because a keycap is a picture of a key -- and a
            // picture of a key beside the word `undo` is exactly the thing a
            // player tries to click.  The row is the target rather than the cap:
            // clicking the word is clicking the key.
            foreach ((string key, string label, Key code) in rows)
            {
                var row = new Rect2(x - Ui.Px(8), y - Ui.Px(3), w + Ui.Px(8),
                                    rowH - Ui.Px(2));
                bool hot = HitKey(row, code);
                if (hot) Ui.Hot(this, row, 2f);
                Ui.Keycap(this, x, y, key, 10.5f);
                Ui.Write(this, new Vector2(x + Ui.Px(38), y + Ui.Px(14)), label, 11f,
                         hot ? Ui.Text : Ui.Dim, w - Ui.Px(38));
                y += rowH;
            }
        }

        /// Command 301's content, on demand.  The *frame* is the original's
        /// reason for existing: a hint is a spoiler, and a spoiler on screen by
        /// default is not a hint.
        ///
        /// It is the one block that still tints its ground, and it earns that by
        /// being the only thing in the column that is not there most of the time
        /// -- a block that appears has to say so.  A flat amber wash and no
        /// border: the rail is already drawing this column's left edge.
        private float DrawHintBlock(float x, float y, float w)
        {
            string hint = _s.Rec.Hint.Replace("\r\n", " ").Replace("\n", " ");
            float tw = w - Ui.Px(4);
            float th = Ui.WrappedHeight(hint, 11.5f, tw, 8);
            var r = new Rect2(x - Ui.Px(11), y - Ui.Px(12), w + Ui.Px(11),
                              th + Ui.Px(24) + Ui.Px(16));
            DrawRect(r, Ui.Accent with { A = 0.075f });
            // The caption already says what closes it; clicking the block is the
            // same instruction for a player with no H to press.
            if (HitKey(r, Key.H)) Ui.Hot(this, r, 2f);
            Ui.Caps(this, new Vector2(x, y + Ui.Px(8)), Strings["info.hint"],
                    Ui.Accent, 9.5f);
            y += Ui.Px(8) + Ui.Px(13);
            Ui.Wrapped(this, new Vector2(x, y + Ui.Px(10)), hint, 11.5f,
                       new Color(0.92f, 0.86f, 0.74f), tw, 8);
            return y + th;
        }

        /// The narrow layout's replacement for the column: one strip under the
        /// board with the name and the two counters.  What is dropped is what a
        /// small window cannot afford and can be asked for -- the author, the
        /// difficulty chip and the hint, which is still on `H` and appears as an
        /// overlay instead (see DrawHintOverlay).
        private void DrawInfoStrip()
        {
            Rect2 r = _l.Side;
            // **The strip is the column turned on its side, and it follows the
            // same rule**: no card, no tiles, a rule for the edge and the
            // readout hard right.  Step 7 boxed this one too, which in a strip
            // that already has the whole window's width for a border was a box
            // drawn around the only thing on the row.
            Ui.Rule(this, r.Position.X, r.Position.Y, r.Size.X);
            TLEVEL lv = _s.Rec;
            TGAMEREC g = _s.E.Game;
            float pad = Ui.Px(14);
            float x = r.Position.X + pad, y = r.Position.Y + pad;

            // The level line opens the level table, as the column's title does.
            var name = new Rect2(x - Ui.Px(6), y - Ui.Px(2),
                                 r.Size.X * 0.55f + Ui.Px(12), Ui.Px(42));
            if (HitKey(name, Key.L)) Ui.Hot(this, name, 2f);
            Ui.Caps(this, new Vector2(x, y + Ui.Px(9)),
                    Strings.F("info.levelOf", _s.Level, _s.LevelCount), Ui.Faint, 9.5f);
            Ui.Wrapped(this, new Vector2(x, y + Ui.Px(34)),
                       string.IsNullOrEmpty(lv.LName) ? Strings["info.untitled"]
                                                      : lv.LName,
                       17, Ui.Text, r.Size.X * 0.55f, 1, Ui.Title);

            // **The narrow layout's only way in, and the reason step 9 exists.**
            // There is no column here, so no actions card and no F1 footer --
            // and a window this shape is exactly the one that is likeliest to
            // have no keyboard behind it either.  So the five keys the column
            // spells out become chips, plus F1 for the rest: six targets, each
            // a whole keycap wide, which is the smallest thing a finger should
            // be asked to hit.
            float cx = x, cy = y + Ui.Px(42);
            foreach ((string cap, Key code) in new[]
                     { ("U", Key.U), ("R", Key.R), ("H", Key.H), ("L", Key.L),
                       ("O", Key.O), ("F1", Key.F1) })
            {
                // Set larger than the column's caps and hit larger still: this
                // row is the one place in the port that has to work under a
                // thumb, so it is drawn at 13 and tested through Ui.Touch.
                var box = Ui.Touch(new Rect2(cx, cy, Ui.KeycapWidth(cap, 13f),
                                             Ui.KeycapHeight(13f)), 34f);
                if (HitKey(box, code)) Ui.Hot(this, box, 8f);
                Ui.Keycap(this, cx, cy, cap, 13f);
                cx += Ui.KeycapWidth(cap, 13f) + Ui.Px(9);
            }

            // The two counters, stacked hard against the right edge in the same
            // label-left / number-right readout the column uses.  No tiles: in
            // a strip whose height is already the row, a bordered box around
            // each number was two more edges saying what the edge of the strip
            // had said.
            float tw = Ui.Px(96);
            float tx = r.End.X - pad - tw;
            Readout(y + Ui.Px(4), Strings["info.moves"], g.ScoreMove);
            Readout(y + Ui.Px(27), Strings["info.shots"], g.ScoreShot);

            void Readout(float ry, string label, int value)
            {
                Ui.Caps(this, new Vector2(tx, ry + Ui.Px(14)), label, Ui.Faint, 9.5f);
                DrawString(Ui.Bold, new Vector2(tx, ry + Ui.Px(16)),
                           value.ToString(), HorizontalAlignment.Right, tw,
                           Ui.Px(17), Ui.Text);
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

            // The room a line has, measured before one is asked for: the dot,
            // its gap, and the padding either side.  WinLine sheds against it.
            float textW = r.Size.X - 2 * Ui.Px(PadD) - Ui.Px(6) - Ui.Px(9);

            bool editing = _edit != null && _edit.Open;
            (string what, Color tint) = editing
                ? (_edit.Status ?? "", Ui.Accent)
                : _s.Now switch
                {
                    Session.State.Won => (WinLine(textW), Ui.Good),
                    // ID_DEADBOX_DEAD is the dialog's own headline ("YOU ARE
                    // DEAD ! ! !"); the two keys after it are this port's
                    // legend, because DeadBox offers them as buttons and there
                    // are no buttons here.
                    // DeadBox's own headline is "YOU ARE DEAD ! ! !", which is
                    // 1996 shareware and does not survive being read twice.  The
                    // two keys after it are this port's legend, because DeadBox
                    // offers them as buttons and there are no buttons here.
                    Session.State.Dead => (Strings["status.dead"], Ui.Bad),
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
                Ui.Write(this, new Vector2(pad, base_), Strings["status.default"],
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
            // The transport row under the track is keycaps with room to be hit
            // since step 9, not a legend line, which is why this is 84 and was
            // 74.
            float h = Ui.Px(16) * 2 + Ui.Px(84);
            var panel = new Rect2(Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                                  Mathf.Round(_l.Status.Position.Y - h - Ui.Px(14)), w, h);
            Ui.Dialog(this, panel, 12f);
            // No scrim: 114 is a dialog beside the game, not over it, and the
            // board keeps ticking behind.  So the panel swallows its own clicks
            // and everything outside it is left to the PanelUp guard in
            // MouseButton -- the same split the key router makes.
            _hits.Swallow(panel);

            float pad = Ui.Px(16);
            float x = panel.Position.X + pad;
            float y = panel.Position.Y + pad;

            // The original builds a MessageBox body out of four pieces --
            // "Playback Level : ", the name, "\nRecorded by ", the author, with
            // the newline carried inside the third -- which is a layout smuggled
            // into a string.  Here they are two lines on a panel and two keys,
            // and the panel decides where the second one goes.
            Ui.Write(this, new Vector2(x, y + Ui.Px(11)),
                     Strings.F("pb.title", pb.Rec.LName), 12.5f, Ui.Text,
                     panel.Size.X - 2 * pad);
            Ui.Write(this, new Vector2(x, y + Ui.Px(28)),
                     Strings.F("pb.recordedBy", pb.Rec.Author), 11.5f,
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
            foreach ((string key, string label, bool on, Key code) in new[]
            {
                ("3", Strings["pb.step"], pb.Speed == PbSpeed.Step, Key.Key3),
                ("2", Strings["pb.slow"], pb.Speed == PbSpeed.Slow, Key.Key2),
                ("1", Strings["pb.fast"], pb.Speed == PbSpeed.Fast, Key.Key1),
            })
            {
                float lw = Ui.Width(label, 10.5f);
                kx -= lw;
                float labelX = kx;
                kx -= Ui.Px(24) + Ui.Px(5);
                var box = new Rect2(kx - Ui.Px(6), panel.Position.Y + Ui.Px(8),
                                    labelX + lw - kx + Ui.Px(12),
                                    Ui.KeycapHeight(10.5f) + Ui.Px(8));
                bool hot = _hits.Add(box, "pb:" + key, () => PlaybackKey(code));
                if (hot) Ui.Hot(this, box);
                Ui.Write(this, new Vector2(labelX, panel.Position.Y + Ui.Px(26)), label,
                         10.5f, on ? Ui.Cyan : hot ? Ui.Text : Ui.Faint);
                Ui.Keycap(this, kx, panel.Position.Y + Ui.Px(12), key, 10.5f);
                kx -= Ui.Px(12);
            }

            // **The transport, as three targets rather than a sentence.**  A
            // playback is *watched*, which is exactly the state in which the
            // hands are not on the keyboard -- so the three things PBWindow's
            // buttons do (ID_PLAYBOX_02 Play/Pause, _03 Reset, _01 Close) are
            // the three things under the track, each a keycap wide.
            float tx = x, ty = panel.End.Y - pad - Ui.KeycapHeight(10.5f) + Ui.Px(4);
            foreach ((string cap, string label, Key code) in new[]
            {
                ("space", Strings[_s.E.PlayBack ? "pb.pause" : "pb.play"], Key.Space),
                ("R", Strings["pb.reset"], Key.R),
                ("Esc", Strings["pb.close"], Key.Escape),
            })
            {
                float cwid = Ui.KeycapWidth(cap, 10.5f), lw = Ui.Width(label, 10.5f);
                var box = new Rect2(tx - Ui.Px(6), ty - Ui.Px(4),
                                    cwid + Ui.Px(8) + lw + Ui.Px(14),
                                    Ui.KeycapHeight(10.5f) + Ui.Px(8));
                bool hot = _hits.Add(box, "pb:" + cap, () => PlaybackKey(code));
                if (hot) Ui.Hot(this, box);
                Ui.Keycap(this, tx, ty, cap, 10.5f);
                Ui.Write(this, new Vector2(tx + cwid + Ui.Px(8), ty + Ui.Px(14)),
                         label, 10.5f, hot ? Ui.Text : Ui.Faint);
                tx = box.End.X + Ui.Px(4);
            }
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
                    Strings["info.hint"], Ui.Accent);
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
            // Click-off closes, which is the pointer's form of "any other key"
            // -- the rule every panel in this port already states in its own
            // footer.  Registered first because it is drawn first: the hit list
            // is walked backwards, so draw order is z order.
            _hits.Add(host, "scrim", () => _help = false);

            bool editing = _edit != null && _edit.Open;
            (string, Binding[])[] groups = editing ? EditorKeys : PlayKeys;

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
            foreach ((string _, Binding[] items) in groups)
                total += headH + items.Length * rowH + groupGap;

            // **The column is as wide as the widest row in it, measured.**
            // It used to be a flat `Ui.Px(300)`, which is reserved width -- the
            // thing step 7 wrote down a rule against and which English passes
            // by luck.  The first language to be drawn through it said so:
            // German cut four labels mid-word (`... starten oder be`, `... oder
            // ausblende`, `... hart b`, `auf 24 / 32 / 40 px einraste`), because
            // `den letzten Zug zurücknehmen` is half again as wide as `undo the
            // last move` and the panel did not know.  A key list is a table of
            // short phrases and there is nothing in it to shed, so the panel
            // grows instead -- up to what the window can hold, which is the one
            // limit that is real.
            float widest = 0;
            foreach ((string _, Binding[] items) in groups)
                foreach (Binding b in items)
                    widest = Mathf.Max(widest, LabelIndent(b)
                                               + Ui.Width(Strings[b.LabelKey], 11.5f));
            float wantCol = Mathf.Max(Ui.Px(300), widest + Ui.Px(14));

            int cols = 1;
            if (host.Size.X >= Ui.Px(700)) cols = 2;
            else if (total > availH && host.Size.X >= Ui.Px(500)) cols = 2;
            // Two columns of a width nobody asked for is worse than one of the
            // right width: if the pair will not fit, fall back before sizing.
            if (cols == 2 && 2 * wantCol + colGap + 2 * pad > host.Size.X - Ui.Px(32)
                && total <= availH)
                cols = 1;

            float w = Mathf.Min(cols * wantCol + (cols - 1) * colGap + 2 * pad,
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
            // The panel's own body eats the click the scrim would otherwise
            // have taken as "close": a miss inside a dialog is not an answer.
            _hits.Swallow(r);

            float x = r.Position.X + pad, y = r.Position.Y + pad + Ui.Px(12);
            Ui.Caps(this, new Vector2(x, y),
                    Strings[editing ? "help.titleEditor" : "help.title"], Ui.Text, 13);
            // Right-aligned *inside* the panel: DrawString lays a right-aligned
            // string out in the box [at.X, at.X + w], so the box has to start a
            // width back from the edge rather than at it.
            Rect2 close = Ui.CloseRect(r, pad);
            float cw = Ui.Px(150);
            Ui.Write(this, new Vector2(close.Position.X - Ui.Px(10) - cw, y),
                     Strings["help.close"], 11, Ui.Faint, cw,
                     HorizontalAlignment.Right);
            Ui.CloseX(this, close, _hits.Add(Ui.Touch(close), "close", () => _help = false));
            y += Ui.Px(14);
            Ui.Rule(this, x, y, r.Size.X - 2 * pad);
            y += Ui.Px(14);

            float[] colY = new float[cols];
            for (int i = 0; i < cols; i++) colY[i] = y;
            for (int i = 0; i < groups.Length; i++)
            {
                (string titleKey, Binding[] items) = groups[i];
                int ci = colOf[i];
                float cx = x + ci * (colW + colGap);
                Ui.Caps(this, new Vector2(cx, colY[ci] + Ui.Px(9)), Strings[titleKey],
                        Ui.Accent, 9.5f);
                colY[ci] += headH;
                foreach (Binding b in items)
                {
                    // **The overlay is a command list now, not a legend.**  A
                    // row whose Cmd is set presses that key and closes -- which
                    // is what a player who came here to find out how to restart
                    // wanted to happen anyway.  F1's own row only closes: it
                    // would otherwise shut the panel and open it again.
                    var row = new Rect2(cx - Ui.Px(6), colY[ci] - Ui.Px(2),
                                        colW + Ui.Px(12), rowH);
                    bool hot = b.Cmd != Key.None
                               && _hits.Add(row, KeyName(b.Cmd, b.Ctrl), () =>
                                  {
                                      _help = false;
                                      if (b.Cmd != Key.F1) Press(b.Cmd, b.Ctrl);
                                  });
                    if (hot) Ui.Hot(this, row);
                    float kx = cx;
                    foreach (string k in b.Caps.Split(' '))
                        kx = Ui.Keycap(this, kx, colY[ci], k, 10.5f) + Ui.Px(4);
                    // The labels line up at a fixed indent, except where the
                    // caps are wider than it -- four arrows are, and ran into
                    // "move the tank" on the first pass.  LabelIndent is the
                    // same sum, measured ahead of the draw so the panel can be
                    // sized from it.
                    float lx = cx + LabelIndent(b);
                    Ui.Write(this, new Vector2(lx, colY[ci] + Ui.Px(14)),
                             Strings[b.LabelKey], 11.5f, hot ? Ui.Text : Ui.Dim,
                             cx + colW - lx);
                    colY[ci] += rowH;
                }
                colY[ci] += groupGap;
            }
        }

        /// Esc's second half: the smallest dialog in this port, and the only one
        /// that asks rather than offers.
        ///
        /// It is deliberately not the shape of the list panels -- no rows, no
        /// scrolling, one line of copy and two keycaps -- because the answer is
        /// a reflex and anything that reads like a list invites reading.  The
        /// two caps are the answer keys drawn as keys, which is the vocabulary
        /// the help overlay established: Enter is the act and Esc is the way
        /// back, everywhere in this interface.
        ///
        /// This is the shape the port's remaining modal prompts want -- the
        /// editor's "save changes?", the RecordBox and HSBox name fields, the
        /// Difficulty dialog -- and it is the first of them to be built.  What
        /// they need past this is a text field and a third button.
        private void DrawQuitAsk(Rect2 host)
        {
            Ui.Scrim(this, host);
            // Click-off is No, which is the pointer's form of the key rule --
            // "every other key including Esc keeps playing", so every other
            // *place* does too.  A mistaken click lands on the safe answer for
            // the same reason a mistaken keypress does.
            _hits.Add(host, "scrim", () => _quitAsk = false);

            float pad = Ui.Px(22);
            string title = Strings["quit.title"];
            string body = Strings[_s != null && _s.Now == Session.State.Won
                                  ? "quit.bodyWon" : "quit.body"];
            string yesLabel = Strings["quit.yes"], noLabel = Strings["quit.no"];

            // Wide enough for the copy *and* for the two buttons side by side
            // -- which is what the answers became in step 9, and which is a
            // longer line than the question on a short body.
            float buttons = Ui.KeycapWidth("Enter", 11f) + Ui.Width(yesLabel, 11.5f)
                            + Ui.KeycapWidth("Esc", 11f)
                            + Ui.Width(noLabel, 11.5f) + Ui.Px(64);
            float w = Mathf.Min(Mathf.Max(Ui.Px(320),
                                          Mathf.Max(Ui.Width(body, 12), buttons)
                                          + 2 * pad),
                                host.Size.X - Ui.Px(40));
            // Measured from the same advances the draw below uses, rather than
            // a round number that is nearly right: this box is small enough
            // that a dozen spare pixels at the bottom read as a mistake.
            float capH = Ui.Px(11) + Ui.Px(9);
            float h = pad + Ui.Px(12) + Ui.Px(14) + Ui.Px(26) + Ui.Px(22)
                      + capH + pad;
            var r = new Rect2(Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                              Mathf.Round(host.Position.Y + (host.Size.Y - h) / 2f),
                              w, h);
            Ui.Dialog(this, r, 14f);
            _hits.Swallow(r);

            float x = r.Position.X + pad, y = r.Position.Y + pad + Ui.Px(12);
            Ui.Caps(this, new Vector2(x, y), title, Ui.Text, 13);
            y += Ui.Px(14);
            Ui.Rule(this, x, y, r.Size.X - 2 * pad);
            y += Ui.Px(26);
            Ui.Write(this, new Vector2(x, y), body, 12, Ui.Dim, r.Size.X - 2 * pad);
            y += Ui.Px(22);

            // Enter first: it is the one the question is about.  Esc is drawn
            // second and labelled with what it does rather than with "no",
            // because "no" to a quit prompt is not a state a player pictures --
            // staying is.
            //
            // **As two buttons they keep the property the two keys have**: the
            // answers are far apart, so no single repeated gesture can reach
            // both.  A double-click that opened the prompt cannot also confirm
            // it, which is the pointer's version of "Esc is never the key that
            // quits".
            float kh = Ui.KeycapHeight(11f);
            float yesW = Ui.KeycapWidth("Enter", 11f) + Ui.Px(9)
                         + Ui.Width(yesLabel, 11.5f);
            var yes = new Rect2(x - Ui.Px(8), y - Ui.Px(4),
                                yesW + 2 * Ui.Px(8), kh + Ui.Px(8));
            if (_hits.Add(yes, "quit:yes", () => { _quitAsk = false; GetTree().Quit(); }))
                Ui.Hot(this, yes);
            float kx = Ui.Keycap(this, x, y, "Enter", 11f) + Ui.Px(9);
            Ui.Write(this, new Vector2(kx, y + Ui.Px(15)), yesLabel, 11.5f, Ui.Text);

            float nx = yes.End.X + Ui.Px(14);
            float noW = Ui.KeycapWidth("Esc", 11f) + Ui.Px(9)
                        + Ui.Width(noLabel, 11.5f);
            var no = new Rect2(nx, y - Ui.Px(4), noW + 2 * Ui.Px(8), kh + Ui.Px(8));
            bool noHot = _hits.Add(no, "quit:no", () => _quitAsk = false);
            if (noHot) Ui.Hot(this, no);
            kx = Ui.Keycap(this, nx + Ui.Px(8), y, "Esc", 11f) + Ui.Px(9);
            Ui.Write(this, new Vector2(kx, y + Ui.Px(15)), noLabel, 11.5f,
                     noHot ? Ui.Text : Ui.Dim);
        }

        /// Where a row's label starts, relative to the column's left edge: the
        /// fixed indent, or past the keycaps when they are wider than it.
        ///
        /// It exists because the *draw* needs it and the *measure* needs it
        /// first, and a second copy of this sum would be the kind of thing that
        /// agrees until one of them is edited.
        private static float LabelIndent(Binding b)
        {
            float kx = 0;
            foreach (string k in b.Caps.Split(' '))
                kx += Ui.KeycapWidth(k, 10.5f) + Ui.Px(4);
            return Mathf.Max(Ui.Px(96), kx + Ui.Px(10) - Ui.Px(4));
        }

        /// One row of the key list.  **Since step 9 it carries the command as
        /// well as the caption**, because the overlay is the port's only full
        /// list of what it can do and a list of things you cannot press is a
        /// worse answer to F1 than it looks: on a touch build it is the *whole*
        /// interface.  `Key.None` marks the rows that are not a key at all --
        /// the arrows and space, which the router takes through the original's
        /// own WM_KEYDOWN filter and which a click cannot stand in for, and the
        /// editor's three mouse verbs, which describe the pointer rather than
        /// name a key for it to press.
        ///
        /// `Caps` is the keycap glyphs, which are not translated -- a key is
        /// called `Esc` on the keyboard in front of the player whatever language
        /// the chrome is in.  `LabelKey` is what the row says it does, and that
        /// is a catalogue key, resolved at draw time so the overlay follows
        /// Ctrl+L like everything else.
        private readonly record struct Binding(string Caps, string LabelKey,
                                               Key Cmd = Key.None, bool Ctrl = false);

        /// The bindings, as data -- so the overlay and the router cannot drift.
        /// The command ids in the comments are the original's; every key here
        /// except the four marked "ours" is out of ACC1 (lt32l_us.inc:120).
        private static readonly (string, Binding[])[] PlayKeys =
        {
            ("help.groupPlay", new Binding[]
            {
                new("← ↑ → ↓", "keys.move"),
                new("space", "keys.fire"),
                new("U", "keys.undo", Key.U),                   // 110
                new("R", "keys.restart", Key.R),                // 105
                new("H", "keys.hint", Key.H),                   // 301
                new("ctrl C", "keys.savePos", Key.C, true),     // 111
                new("ctrl V", "keys.restorePos", Key.V, true),  // 112
            }),
            // The Scores group used to be a group: V and G had a row each, and
            // step 8 merged both lists into L's panel and unbound the two keys.
            // The one row left says so -- `keys.levels` is what the panel is,
            // and a player who knew the old keys has to be told where they went
            // by the list that used to carry them.
            ("help.groupLevels", new Binding[]
            {
                new("L", "keys.levels", Key.L),                 // 106, 113, 906
                new("O", "keys.collections", Key.O),            // 108
                new("S", "keys.nextLevel", Key.S),              // 107
                new("P", "keys.prevLevel", Key.P),              // 119
                // 118, and the label is the original's own menu text ("Last
                // Level Playe&d") rather than an invented one, because nine of
                // the ten 2007 catalogues already translate that sentence and
                // agreeing with them costs nothing.  The cap is spelled out
                // rather than the menu's `BkSp`: this port writes `space` and
                // `tab` in full and there is no menu column to fit.
                new("backspace", "keys.lastPlayed", Key.Backspace),  // 118
                // Ours, and here because item 3 gave them a job of their own:
                // S and P walk the difficulty mask and the skip, these two step
                // one record whatever those say.  A row each, because an escape
                // hatch nobody can find is not one.
                new("[ ]", "keys.stepLevel"),
                new("F2", "keys.newGame", Key.F2),              // 101
            }),
            ("help.groupRecording", new Binding[]
            {
                new("F5", "keys.record", Key.F5),               // 123
                new("F6", "keys.saveRecording", Key.F6),        // 117
                new("F7", "keys.playback", Key.F7),             // 114
                new("F4", "keys.replay", Key.F4),               // 124
                // 115, and **not** the original's F8, which is 125, Resume
                // Recording -- a command this port has not built yet and which
                // will arrive to find its key taken.  See next-steps.md item 10.
                new("F8", "keys.autoRecord", Key.F8),           // 115
            }),
            ("help.groupView", new Binding[]
            {
                new("Z", "keys.snap", Key.Z),                   // 120-122
                new("C", "keys.grid", Key.C),                   // ours
                new("I", "keys.interpolate", Key.I),            // ours
                new("N", "keys.sound", Key.N),                  // 102
                new("A", "keys.animation", Key.A),              // 104
                // **One row since step 18**, where four dialogs became four
                // sections of one (next-steps item 14).  `ctrl G` is still
                // bound -- it is 226's own accelerator and opens this panel on
                // the Graphics section -- and is deliberately not listed: an
                // unlisted alias is what item 13 keeps `S` as, and a key list
                // that offers two ways into one panel is a key list saying the
                // merge did not happen.
                new("ctrl O", "keys.options", Key.O, true),     // 116, 225, 226
            }),
            ("help.groupSession", new Binding[]
            {
                new("F9", "keys.editor", Key.F9),               // 201
                new("F1", "keys.thisList", Key.F1),             // 907
                new("Esc", "keys.quit", Key.Escape),            // ours
            }),
        };

        /// ACC2 (lt32l_us.inc:150) plus the palette's own, which the editor
        /// panel used to have to list itself in eight grey lines.
        private static readonly (string, Binding[])[] EditorKeys =
        {
            ("help.groupPaint", new Binding[]
            {
                new("click", "keys.paintLeft"),
                new("right", "keys.paintRight"),
                new("shift", "keys.rotate"),
                new("X", "keys.swapBrushes", Key.X),
                new("T", "keys.tunnelId", Key.T),
            }),
            ("help.groupBoard", new Binding[]
            {
                new("ctrl ←→", "keys.shiftBoard"),              // 710/711
                new("ctrl ↑↓", "keys.shiftBoard"),              // 712/713
                new("ctrl C", "keys.clearField", Key.C, true),  // 601
                new("1 - 5", "keys.difficulty"),
            }),
            ("help.groupFile", new Binding[]
            {
                new("ctrl S", "keys.saveLevel", Key.S, true),   // 603
                new("tab", "keys.fields", Key.Tab),
                new("F9", "keys.leaveEditor", Key.F9),          // 604
            }),
            ("help.groupView", new Binding[]
            {
                new("Z", "keys.snap", Key.Z),
                new("C", "keys.grid", Key.C),
                // ACC2's own 226, on the key the merge put it behind -- see
                // PlayKeys above, and the editor reaches Ctrl+G as an alias
                // there too.
                new("ctrl O", "keys.options", Key.O, true),     // 226
                new("F1", "keys.thisList", Key.F1),             // 903
                new("Esc", "keys.leaveEditor", Key.Escape),
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
