// Phase 5, step 5: the level editor, as the game's own mode.
//
// The original turns the *same window* into an editor -- command 201 swaps the
// menu bar, hides the nine buttons, shows two edit controls, and repaints the
// right-hand control panel as a palette of every object (LTANK.C:1084).  This
// keeps that shape: one window, a palette beside the board, the board itself
// edited with the mouse, and `EditorOn` deciding which of the two key tables is
// live.
//
// **What is transliterated and what is not.**  Everything that touches the
// board is `LaserTank.Core.Editor` -- `ChangeGO`, the four Shifts, Clear Field,
// the tunnel-wait strip -- and it is diffed against the C by
// `tools/editor_check.py`.  Everything in this file is the *window*: where the
// palette is, which key opens which command, and what a text field does while
// it has focus.  That is the same split step 4 drew, and the same reason: the
// mechanics must not move, the interface is written down rather than locked
// down.
//
// **The keys are ACC2**, the original's editor accelerator table
// (lt32l_us.inc:150), read rather than invented: F9 leaves (and enters, from
// ACC1), Ctrl+C clears the field, Ctrl+L loads, Ctrl+S saves, Ctrl+H is the
// hint, Ctrl+arrows shift the board.  The table carries a comment worth
// keeping -- *"DONT use Keys that can be entered in the Author & Level Name
// field"* -- which is a statement about focus: while an edit control has the
// caret, the accelerators must not fire.  This file has the same rule, and it
// is why Tab is what moves in and out of the text fields.
//
// **One deliberate deviation, and it is a safety one.**  Command 603 saves in
// place, over the .lvl the level came from.  In this repo `data/` is the
// regression corpus -- 20,914 levels that every fidelity gate replays -- so an
// editor that can silently rewrite it is a hazard rather than a feature.
// Saving a level that came from `data/` therefore writes a working copy under
// `out/levels/` and says so on screen; a collection opened from anywhere else
// saves in place, as the original does.  Same rule as "an instrument must not
// write the player's state", pointed at the corpus instead of the INI.
using System;
using System.IO;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    public sealed class EditMode
    {
        /// `EditorOn` (LTANK.C:20).
        public bool Open { get; private set; }

        /// `Modified` (LTANK2.C:65) and `OKtoSave` -- the two flags the save
        /// path reads.  OKtoSave is false for a level that has never had a file
        /// (Clear Field clears it, LTANK.C:1154), which is what makes command
        /// 603 fall through to Save As.
        public bool Modified { get; private set; }
        public bool OkToSave { get; private set; }

        private readonly BoardView _view;
        private Session _s;
        private Editor _ed;
        private LevelRecord _rec;

        /// `Ed1` and `Ed2`, the two edit controls, plus the hint the HintBox
        /// dialog edits -- one `TextField` each since step 14, which is where
        /// the three of them stopped being three copies of a box with a caret
        /// in it.
        ///
        /// **The widths are LevelRecord's own**, because they are the
        /// original's `GetWindowText` counts, and they are now a *cap on the
        /// field* rather than a truncation at save time: this file used to
        /// claim they clamped and they did not -- `LevelRecord.Set` dropped the
        /// surplus on the way to disk, out of sight of the player typing it.
        private readonly TextField _name = new TextField(LevelRecord.NameEntry - 1);
        private readonly TextField _author = new TextField(LevelRecord.NameEntry - 1);
        private readonly TextField _hint = new TextField(LevelRecord.HintEntry - 1);
        private bool _hintTyped;

        /// Which control has the caret: 0 none, 1 name, 2 author, 3 hint.
        /// While it is non-zero the accelerators are off -- see the header.
        private int _focus;

        /// The id `ChangeGO`'s LoadTID dialog would answer with.  The dialog is
        /// a spinner over 0..7 in the original; here it is the palette's own
        /// tunnel slot, cycled with `T`.
        private int _tunnel;

        /// Where the last save went, and what to say about it.
        public string Status { get; private set; }

        public EditMode(BoardView view) { _view = view; }

        public Editor Core => _ed;
        public int TunnelId => _tunnel;
        public string Name => _name.Text;
        public string Author => _author.Text;
        public ushort Diff => _rec?.Diff ?? 0;

        // ---- entering and leaving -------------------------------------------

        /// Command 201 (LTANK.C:1084).  The parts that survive having no menu
        /// bar: stop the clock, stop any recording, take the high-score post
        /// off the table, fill the two edit controls from the record, and strip
        /// the tunnel wait bits off the board.
        public void Enter(Session s)
        {
            _s = s;
            if (_s?.E == null) return;
            _ed = new Editor(_s.E);
            _rec = LevelRecord.Read(_s.LevelPath, _s.Level) ?? new LevelRecord();
            _name.Set(_rec.Name);
            _author.Set(_rec.Author);
            _hint.Set(_rec.Hint);
            _hintTyped = false;
            _focus = 0;
            _tunnel = 0;
            Modified = false;
            // A level that came from a file can be saved back over itself; one
            // that did not (Clear Field) has to be told where to go.
            OkToSave = true;
            Open = true;

            _s.E.GameOn(false);
            if (_s.Rec2.Recording) _s.Rec2.Toggle();       // command 123
            _s.NoHighScore();                             // OKtoHS = FALSE
            _ed.Enter();
            Status = _view.Strings["ed.enter"];
        }

        /// Command 604 (LTANK.C:1240).  It does *not* reload the level: the
        /// board you edited is the board you play, which is how the original
        /// lets you test a level without saving it first.  `CurRecData.PF` is
        /// updated on the way out, tank stamped back in, so the level picker
        /// and the recorder see what is on screen.
        public void Leave()
        {
            if (!Open) return;
            Open = false;
            _focus = 0;
            if (_s?.E != null)
            {
                Array.Copy(_ed.PlayfieldForSave(), _s.E.CurRecData.PF, 256);
                _s.E.CurRecData.LName = _name.Text;
                _s.E.CurRecData.Author = _author.Text;
                _s.E.CurRecData.SDiff = _rec.Diff;
                _s.EditorResume();
            }
            Status = _view.Strings[Modified ? "ed.leftModified" : "ed.left"];
        }

        // ---- the mouse, which is the editor's real input ---------------------

        /// WM_LBUTTONDOWN / WM_RBUTTONDOWN (LTANK.C:785, :825).  The original
        /// splits on `LOWORD(lparam) > ContXPos` -- left of the divider is the
        /// board, right of it is the palette -- and so does this.
        public void Click(Vector2 pos, int button, bool shift)
        {
            if (!Open || _ed == null) return;
            _focus = 0;                       // SetFocus(Window), LTANK.C:788

            if (Palette(pos, out int sel))
            {
                if (button == 1) _ed.CurSelBM_L = sel; else _ed.CurSelBM_R = sel;
                return;
            }
            if (!Cell(pos, out int x, out int y)) return;
            _ed.TunnelId = _tunnel;
            if (button == 1) _ed.LeftClick(x, y, shift);
            else _ed.RightClick(x, y);
            Modified = true;                  // LTANK.C:810, :845
        }

        /// WM_MOUSEMOVE with a button held (LTANK.C:764).  Note which guards
        /// are the original's: Shift cancels the drag, a drag never places a
        /// tunnel (it would open the id dialog once per cell crossed), and the
        /// palette side is not draggable at all -- the test there is on the
        /// button-down message only.
        ///
        /// **`Modified` is not set by a drag**, which is the original's own
        /// oversight: only the two button-down cases set it.  A level changed
        /// entirely by dragging and then closed is not offered the "save?"
        /// prompt.  It is a defined effect, so it stays.
        public void Drag(Vector2 pos, int button, bool shift)
        {
            if (!Open || _ed == null) return;
            if (!Cell(pos, out int x, out int y)) return;
            _ed.TunnelId = _tunnel;
            _ed.Drag(x, y, button, shift);
        }

        /// The board cell under a window position, or false when outside it.
        /// **The arithmetic is the view's** -- step 7 made the board a box that
        /// floats in the window rather than one that starts at a constant
        /// margin, so this cannot be re-derived from two static fields and does
        /// not try.
        public bool Cell(Vector2 pos, out int x, out int y)
            => _view.CellAt(pos, out x, out y);

        // ---- the palette -----------------------------------------------------

        /// `EditBMWidth = 5` (LTANK2.C:48) and a `SpBm + 4` pitch
        /// (LTANK2.C:1697), which is the original's own grid.
        ///
        /// **Step 7 moved the palette rather than the grid.**  The original
        /// hangs it in the 180-pixel control panel command 201 repaints
        /// (ContXPos, LTANK2.C:47); this port had no such panel, so until step 7
        /// it *widened the whole window* by a palette's worth when the editor
        /// opened and narrowed it again on the way out -- which was the one
        /// thing in the UI that moved the window out from under the player.  The
        /// redesign gave the window a column of its own, so the palette now goes
        /// where the original's went: beside the board, in the panel, with the
        /// window left alone.
        ///
        /// The five-across grid is kept because it is the original's, but the
        /// *slot size* is now the panel's rather than the board's: a 40 px board
        /// cell in a 276 px column would fit five with nothing left for the
        /// frames, and the palette does not have to be drawn at the size the
        /// board happens to be at.
        public const int Across = 5;
        public const int Gap = 4;

        /// Where the palette is this frame.  Read from the view's layout, and
        /// clamped so five slots and their gaps always fit the column.
        private int Slot
        {
            get
            {
                float w = _view.SidePanel.Size.X - 2 * Pad;
                return Math.Clamp((int)((w - (Across - 1) * Gap) / Across), 16, 44);
            }
        }

        private int Pitch => Slot + Gap;
        private static float Pad => Ui.Px(14);
        private float PanelX => _view.SidePanel.Position.X + Pad;
        private float PanelY => _view.SidePanel.Position.Y + Pad + Ui.Px(22);

        /// The palette slot under a window position.
        ///
        /// **The bound is the original's: `i > MaxObjects+1` rejects, so 27 is
        /// accepted** -- one slot past the last drawn sprite, in the sixth row.
        /// `GetOBM(27)` falls through its range test to bitmap 1, so selecting
        /// it paints a cell with an object id no table knows; the next level
        /// load sanitises it into tunnel 5.  The slot is left reachable because
        /// it is reachable there, and the panel draws it as an empty frame so
        /// it is at least visible.
        public bool Palette(Vector2 pos, out int sel)
        {
            sel = -1;
            // **The panel is the bound, not just the grid inside it.**  The
            // original's test is `LOWORD(lparam) > ContXPos` -- a half-plane,
            // because its palette and its board were either side of a fixed
            // divider and could not overlap.  Here both are laid out from the
            // window, so "could not overlap" has to be asserted rather than
            // assumed; without this a bad layout silently turns board clicks
            // into palette selections instead of missing loudly.
            if (!_view.SidePanel.HasPoint(pos)) return false;
            int col = (int)Math.Floor((pos.X - PanelX) / Pitch);
            int row = (int)Math.Floor((pos.Y - PanelY) / Pitch);
            if (col < 0 || col >= Across || row < 0) return false;
            int i = col + row * Across;
            if (i > Obj.MaxObjects + 1 || i < 0) return false;
            sel = i;
            return true;
        }

        // ---- the keys, which are ACC2 ----------------------------------------

        /// -> true when the key was the editor's.  While a text field has the
        /// caret this swallows nearly everything, which is the focus rule the
        /// accelerator table's own comment describes.
        public bool Key(InputEventKey k)
        {
            if (!Open) return false;
            if (_focus != 0) return Typing(k);

            bool ctrl = k.CtrlPressed;
            switch (k.Keycode)
            {
                case Godot.Key.F9: Leave(); return true;             // 604
                case Godot.Key.C when ctrl: ClearField(); return true;   // 601
                case Godot.Key.S when ctrl: Save(); return true;         // 603
                case Godot.Key.H when ctrl: _focus = 3; return true;     // 605
                case Godot.Key.Tab: _focus = 1; return true;             // Ed1

                // Ctrl + a direction: commands 710/711/712/713.  Without Ctrl
                // the arrows do nothing here -- there is no tank to drive.
                case Godot.Key.Right when ctrl: _ed.Shift(1, 0); Modified = true; return true;
                case Godot.Key.Left when ctrl: _ed.Shift(-1, 0); Modified = true; return true;
                case Godot.Key.Up when ctrl: _ed.Shift(0, -1); Modified = true; return true;
                case Godot.Key.Down when ctrl: _ed.Shift(0, 1); Modified = true; return true;

                // Commands 701..705, which have no accelerator in ACC2 -- they
                // are menu items, and this port has no menu bar.  The digits are
                // ours and are outside the text fields by the same rule the
                // table's comment states.
                case Godot.Key.Key1: SetDiff(1); return true;
                case Godot.Key.Key2: SetDiff(2); return true;
                case Godot.Key.Key3: SetDiff(4); return true;
                case Godot.Key.Key4: SetDiff(8); return true;
                case Godot.Key.Key5: SetDiff(16); return true;

                // The tunnel id ChangeGO will ask for.  The original opens a
                // dialog per placement (LoadTID); a modal prompt in the middle
                // of painting is worse here than a mode, so the id is picked
                // once and shown on the palette's tunnel slot.
                case Godot.Key.T:
                    _tunnel = (_tunnel + 1) & 7;
                    Status = _view.Strings.F("ed.tunnel", _tunnel);
                    return true;

                // Swap the two selections, which the original does not have and
                // which costs nothing: the palette is on screen either way.
                case Godot.Key.X:
                    (_ed.CurSelBM_L, _ed.CurSelBM_R) = (_ed.CurSelBM_R, _ed.CurSelBM_L);
                    return true;

                // Ctrl+G, the graphics dialog, is in *both* tables (ACC1 and
                // ACC2) -- so it stays live in the editor and BoardView keeps
                // it.  Everything else in ACC1 is not.
                //
                // **And Ctrl+O with it since step 18**, which is the key 226
                // lives behind now: the four settings dialogs are one panel and
                // Ctrl+G is its unlisted alias, so both spellings of the one
                // ACC2 share have to fall through here.  `EditorKeys` lists
                // `ctrl O`, and a key the editor swallowed while its own footer
                // advertised it is exactly the bug the F1 case below records.
                case Godot.Key.O when ctrl: return false;
                case Godot.Key.G when ctrl: return false;
                // **And F1, which was being swallowed here.**  The router hands
                // this switch the key first and only acts on the ones it
                // declines, so a key that falls through to the `return true`
                // below is a key the editor has eaten -- and F1 fell through,
                // which meant command 903 did nothing at all in the editor
                // while the panel's own footer advertised it.  Found by
                // wiring that footer up to the pointer in step 9: the click
                // worked and the key did not.
                case Godot.Key.F1: return false;
                case Godot.Key.Z: _view.SetSize(_view.Size % 3 + 1); return true;
                // The coordinate grid, this port's own and useful in both
                // modes -- a level's hint is written in A1-P16 and this is
                // where one gets written.  Plain C is free here: ACC2's own
                // VK_C is Ctrl+C (601, Clear Field), just above.
                case Godot.Key.C:
                    Status = _view.Strings[_view.ToggleGrid() ? "status.gridOn"
                                                              : "status.gridOff"];
                    return true;
                case Godot.Key.Escape: Leave(); return true;
            }
            return true;              // the editor swallows the rest
        }

        /// An edit control with the caret.  Tab walks Name -> Author -> Hint ->
        /// board and Enter and Escape drop out; everything else is the field's,
        /// including the letters that are accelerators outside one, which is
        /// exactly what ACC2's comment is about.
        ///
        /// The character rules -- Backspace, latin-1, printable, and the
        /// record's own width -- are TextField's, because they are the same
        /// rules as the filter field's and the name panel's.
        private bool Typing(InputEventKey k)
        {
            switch (k.Keycode)
            {
                case Godot.Key.Tab:
                    _focus = _focus >= 3 ? 0 : _focus + 1;
                    return true;
                case Godot.Key.Enter:
                case Godot.Key.KpEnter:
                case Godot.Key.Escape:
                    _focus = 0;
                    return true;
            }
            TextField f = Focused();
            if (f != null && f.Key(k))
            {
                // The hint is the one field whose emptiness is not the same as
                // never having been touched: command 601 writes one NUL over
                // the old hint and leaves the rest of it in the record, so a
                // save only rewrites the hint when somebody typed in this one.
                if (_focus == 3) _hintTyped = true;
                Modified = true;
            }
            return true;
        }

        private TextField Focused() => _focus switch
        {
            1 => _name, 2 => _author, 3 => _hint, _ => null,
        };

        /// The five ranks, in the order the five digits set them.  701..705 is
        /// a menu in the original and a menu is a list you walk; the chip is the
        /// same list with one item showing.
        private static readonly ushort[] Ranks = { 1, 2, 4, 8, 16 };

        private void CycleDiff()
        {
            int at = Array.IndexOf(Ranks, _rec.Diff);
            SetDiff(Ranks[(at + 1) % Ranks.Length]);
        }

        /// Commands 701..705 through EditDiffSet (LTANK.C:84), whose last line
        /// is `CurRecData.SDiff = t` -- the difficulty is a property of the
        /// record and changes the moment the menu item is picked, not at save.
        private void SetDiff(ushort t)
        {
            _rec.Diff = t;
            Modified = true;
            Status = _view.Strings.F("ed.difficulty",
                        _view.Strings[new TLEVELINFO { SDiff = t }.RankKey]);
        }

        /// Command 601, "Clear Field" (LTANK.C:1135): the board, the two edit
        /// controls, and `CurRecData.Hint[0] = 0` -- **one NUL**, so the rest of
        /// the old hint stays in the record and would be written to disk behind
        /// an empty one.  OKtoSave goes false, which is what sends the next save
        /// to Save As.
        private void ClearField()
        {
            _ed.ClearField();
            _name.Set("");
            _author.Set("");
            _hint.Set("");
            _hintTyped = false;
            _rec.ClearHint();
            Modified = true;
            OkToSave = false;
            Status = _view.Strings["ed.cleared"];
        }

        // ---- command 603, and where it is allowed to write --------------------

        /// Command 603, "Save Level" (LTANK.C:1191), plus the corpus rule from
        /// this file's header: a level that came out of `data/` is written to a
        /// working copy under `out/levels/` instead of over the regression
        /// corpus.  The copy is seeded from the original collection the first
        /// time, so the levels around the edited one are still there and the
        /// file the 2010 binary opens is a whole collection rather than one
        /// record with a hole in front of it.
        public void Save()
        {
            if (_s == null || _ed == null) return;
            try
            {
                string dest = Destination(out bool copied);
                _rec.Name = _name.Text;
                _rec.Author = _author.Text;
                if (_hintTyped) _rec.Hint = _hint.Text;
                _rec.SetPlayfield(_ed.PlayfieldForSave());
                _rec.Write(dest, _s.Level);
                Modified = false;
                OkToSave = true;
                Status = _view.Strings.F(copied ? "ed.savedCopy" : "ed.saved",
                                         _s.Level, Shorten(dest));
            }
            catch (Exception ex)                 // FileError(), LTANK.C:1206
            {
                Status = _view.Strings.F("ed.saveFailed", ex.Message);
            }
        }

        /// Where a save may land.  Under `data/` it is a working copy in
        /// `out/levels/`, created from the collection on first use; anywhere
        /// else it is the file itself.
        private string Destination(out bool copied)
        {
            copied = false;
            string src = Path.GetFullPath(_s.LevelPath);
            string data = Path.GetFullPath(Paths.Data());
            if (!src.StartsWith(data + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase))
                return src;

            string dir = Path.Combine(Paths.Root, "out", "levels");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(src));
            if (!File.Exists(dest))
            {
                File.Copy(src, dest);
                copied = true;
            }
            return dest;
        }

        private static string Shorten(string p)
        {
            string root = Paths.Root;
            return p.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? p.Substring(root.Length).TrimStart('/', '\\') : p;
        }

        // ---- the same token language the headless driver takes ---------------

        /// `--editor --edit STR`: run an edit script through *this* object, so
        /// a screenshot can show a particular board and so the game's own
        /// editing path is reachable from a command line.
        ///
        /// **It goes through Click and Drag rather than through Core.Editor
        /// directly**, which is the point: `tools/editor_check.py` proves the
        /// arithmetic against the C, and this proves that what the game's mouse
        /// invokes is that arithmetic.  Same argument as carrying the sound-id
        /// table twice, and as step 4's three script drivers.
        public void Script(string script)
        {
            if (!Open || _ed == null || script == null) return;
            int cell = _view.Cell;
            Vector2 o = _view.Origin;
            int sl = Slot;
            Vector2 At(int x, int y) =>
                new Vector2(o.X + x * cell + cell / 2f, o.Y + y * cell + cell / 2f);
            Vector2 SlotAt(int i) =>
                new Vector2(PanelX + (i % Across) * Pitch + sl / 2f,
                            PanelY + (i / Across) * Pitch + sl / 2f);

            int at = 0;
            while (at < script.Length)
            {
                char c = script[at];
                int need = c is '<' or '>' or 'l' or 'r' or 's' or 'p' or 'q' or 'P' ? 3
                         : c == 't' ? 2 : 1;
                if (at + need > script.Length) break;
                int a = need >= 2 ? Hex(script[at + 1]) : -1;
                int b = need >= 3 ? Hex(script[at + 2]) : -1;
                at += need;
                switch (c)
                {
                    case '<': if (a >= 0 && b >= 0) Click(SlotAt(a * 16 + b), 1, false); break;
                    case '>': if (a >= 0 && b >= 0) Click(SlotAt(a * 16 + b), 2, false); break;
                    case 't': if (a >= 0) _tunnel = a & 7; break;
                    case 'l': if (a >= 0 && b >= 0) Click(At(a, b), 1, false); break;
                    case 's': if (a >= 0 && b >= 0) Click(At(a, b), 1, true); break;
                    case 'r': if (a >= 0 && b >= 0) Click(At(a, b), 2, false); break;
                    case 'p': if (a >= 0 && b >= 0) Drag(At(a, b), 1, false); break;
                    case 'q': if (a >= 0 && b >= 0) Drag(At(a, b), 2, false); break;
                    case 'P': if (a >= 0 && b >= 0) Drag(At(a, b), 1, true); break;
                    case 'R': _ed.Shift(1, 0); Modified = true; break;
                    case 'L': _ed.Shift(-1, 0); Modified = true; break;
                    case 'U': _ed.Shift(0, -1); Modified = true; break;
                    case 'D': _ed.Shift(0, 1); Modified = true; break;
                    case 'C': ClearField(); break;
                    case 'E': _ed.Enter(); break;
                    case '1': SetDiff(1); break;
                    case '2': SetDiff(2); break;
                    case '3': SetDiff(4); break;
                    case '4': SetDiff(8); break;
                    case '5': SetDiff(16); break;
                }
            }
        }

        private static int Hex(char c) =>
            c >= '0' && c <= '9' ? c - '0'
            : c >= 'a' && c <= 'f' ? c - 'a' + 10
            : c >= 'A' && c <= 'F' ? c - 'A' + 10
            : -1;

        // ---- drawing ---------------------------------------------------------
        /// `PutSelectors` (LTANK2.C:1692) plus the two edit controls under it,
        /// in the column step 7 gave the window -- which is where the original
        /// draws them too, and the first time this port has had a panel to put
        /// them in rather than a widened window.
        ///
        /// Every slot gets a frame, the two selections get theirs in the
        /// original's own two roles (`JKSelFrame(..., 1)` and `(..., 2)` -- left
        /// brush and right brush), and the 28th slot is drawn empty because it
        /// is clickable and would otherwise be invisible.
        ///
        /// The eight grey legend lines that used to sit under the fields are
        /// gone: they are the F1 overlay's editor list now (BoardView.EditorKeys),
        /// which is both more room for the fields and the same key the original
        /// binds help to in ACC2 (903).
        public void Draw(Node2D n, Font font, Atlas atlas, Rect2 panel)
        {
            if (!Open || _ed == null) return;
            Ui.Card(n, panel);

            int slot = Slot;
            float x0 = PanelX, y0 = PanelY;
            float w = panel.Size.X - 2 * Pad;

            Strings L = _view.Strings;
            Ui.Caps(n, new Vector2(x0, panel.Position.Y + Pad + Ui.Px(9)),
                    L["ed.palette"], Ui.Faint);

            for (int i = 0; i <= Obj.MaxObjects + 1; i++)
            {
                float x = x0 + (i % Across) * Pitch;
                float y = y0 + (i / Across) * Pitch;
                var r = new Rect2(x, y, slot, slot);
                n.DrawStyleBox(Ui.Box(Ui.Raised, Ui.Border, 4f, 1f), r);
                if (i <= Obj.MaxObjects && atlas.Region(Obj.GetOBM(i), out Rect2 src))
                    n.DrawTextureRectRegion(atlas.Texture, r, src);
                // The two brushes, in the two accents: amber is the left button
                // (the one a click uses), cyan the right.  Drawn outside the
                // slot rather than over it so the sprite stays readable.
                if (i == _ed.CurSelBM_L)
                    n.DrawStyleBox(Ui.Box(new Color(0, 0, 0, 0), Ui.Accent, 6f, 2f),
                                   r.Grow(3));
                if (i == _ed.CurSelBM_R)
                    n.DrawStyleBox(Ui.Box(new Color(0, 0, 0, 0), Ui.Cyan, 7f, 2f),
                                   r.Grow(5));
                if (i == Obj.MaxObjects)          // the tunnel: show its id
                    n.DrawString(Ui.Bold, new Vector2(x + 3, y + slot - 3),
                                 _tunnel.ToString(), HorizontalAlignment.Left, -1,
                                 Ui.Px(10), Colors.Black);
            }

            float y2 = y0 + ((Obj.MaxObjects + Across) / Across) * Pitch + Ui.Px(10);

            // Which object each brush holds, spelled out: the sprites are 32 px
            // and several pairs (the four mirrors, the four rotos) differ by a
            // diagonal.
            y2 += Brush(n, x0, y2, w, L["ed.brushLeft"], Label(L, _ed.CurSelBM_L),
                        Ui.Accent);
            y2 += Brush(n, x0, y2, w, L["ed.brushRight"], Label(L, _ed.CurSelBM_R),
                        Ui.Cyan);
            y2 += Ui.Px(8);

            Ui.Rule(n, x0, y2, w);
            y2 += Ui.Px(14);

            y2 = DrawField(n, x0, y2, w, L["ed.fieldName"], _name, 1);
            y2 = DrawField(n, x0, y2, w, L["ed.fieldAuthor"], _author, 2);
            y2 = DrawField(n, x0, y2, w, L["ed.fieldHint"], _hint, 3);
            y2 += Ui.Px(6);

            var info = new TLEVELINFO { SDiff = _rec.Diff };
            Color dc = Ui.Diff[Math.Clamp((int)_rec.Diff, 0, 5)];
            string rankText = L[info.RankKey];
            // Commands 701..705 are five menu items and the digits 1-5 stand in
            // for them; the chip that shows the answer cycles through the same
            // five, which is the pointer's version of a menu with no menu bar.
            var chip = new Rect2(x0, y2, Ui.CapsWidth(rankText, 10) + 2 * Ui.Px(7),
                                 Ui.Px(10) + Ui.Px(8));
            if (_view.Chrome.Add(Ui.Touch(chip), "diff", CycleDiff)) Ui.Hot(n, chip, 9f);
            float px = Ui.Pill(n, x0, y2, rankText, dc,
                               dc * new Color(1, 1, 1, 0.16f));
            if (Modified)
                Ui.Pill(n, px, y2, L["ed.modified"], Ui.Accent,
                        new Color(0.20f, 0.14f, 0.05f));

            // The footer mirrors the play column's: one key, the one that opens
            // everything else.
            float fy = panel.End.Y - Ui.Px(34) + Ui.Px(6);
            if (fy > y2 + Ui.Px(24))
            {
                var foot = new Rect2(x0 - Ui.Px(6), fy - Ui.Px(4), w + Ui.Px(12),
                                     Ui.KeycapHeight() + Ui.Px(8));
                bool hot = _view.Chrome.Add(foot, BoardView.KeyName(Godot.Key.F1, false),
                                            () => _view.Press(Godot.Key.F1));
                if (hot) Ui.Hot(n, foot, 8f);
                float fx = Ui.Keycap(n, x0, fy, "F1") + Ui.Px(9);
                Ui.Write(n, new Vector2(fx, fy + Ui.Px(15)), L["ed.keys"], 11.5f,
                         hot ? Ui.Text : Ui.Faint, panel.End.X - fx - Pad);
            }
        }

        /// One brush row: a small swatch of the accent, the role, the object.
        private static float Brush(Node2D n, float x, float y, float w, string role,
                                   string what, Color c)
        {
            float d = Ui.Px(6);
            n.DrawCircle(new Vector2(x + d / 2f, y + Ui.Px(7)), d / 2f, c);
            float lx = x + d + Ui.Px(7);
            Ui.Write(n, new Vector2(lx, y + Ui.Px(11)), role, 10.5f, Ui.Faint);
            Ui.Write(n, new Vector2(lx + Ui.Px(34), y + Ui.Px(11)), what, 11.5f,
                     Ui.Text, w - Ui.Px(34) - d - Ui.Px(7));
            return Ui.Px(18);
        }

        /// One of the three text fields.  The box, the label, the value and the
        /// caret are `Ui.Field`'s since step 14; what is left here is the
        /// layout and the hit target.
        ///
        /// **A click puts the caret in it**, which is what a box shaped like a
        /// text field promises and what Tab was the only way to do until step 9.
        /// It goes through the hit list rather than through the editor's own
        /// mouse arm because that arm is a transliteration of `LOWORD(lparam) >
        /// ContXPos` -- a half-plane split between board and palette, with
        /// nothing in it about fields the original drew as Windows controls.
        private float DrawField(Node2D n, float x, float y, float w,
                                string label, TextField f, int which)
        {
            bool focused = _focus == which;
            float h = Ui.Px(28);
            var r = new Rect2(x, y, w, h);
            bool hot = _view.Chrome.Add(r, "field:" + which, () => _focus = which);
            f.Draw(n, r, "—", focused,
                   focused ? Ui.Accent : hot ? Ui.BorderLit : Ui.Border, hot, label);
            return y + h + Ui.Px(6);
        }

        /// The object names as catalogue keys, LTANK.H's own table read top to
        /// bottom.  Index 26 is the tunnel selector; 27 is the slot past the end
        /// and has no name, which is why it draws as its own number.
        ///
        /// The four mirrors, the four rotaries, the four one-ways and the four
        /// anti-tanks differ only by a direction, and this port spells that
        /// direction with an arrow rather than with `ul`/`rt`: an arrow is the
        /// same glyph in every language and is what the sprite is actually
        /// showing.
        private static readonly string[] NameKeys =
        {
            "obj.dirt", "obj.tank", "obj.flag", "obj.water", "obj.solid",
            "obj.block", "obj.bricks",
            "obj.mirrorUL", "obj.mirrorUR", "obj.mirrorDR", "obj.mirrorDL",
            "obj.rotoUL", "obj.rotoUR", "obj.rotoDR", "obj.rotoDL",
            "obj.onewayUp", "obj.onewayRight", "obj.onewayDown", "obj.onewayLeft",
            "obj.crystal",
            "obj.antitankUp", "obj.antitankRight", "obj.antitankDown",
            "obj.antitankLeft",
            "obj.ice", "obj.thinIce", "obj.tunnel",
        };

        private static string Label(Strings L, int i) =>
            i >= 0 && i < NameKeys.Length ? L[NameKeys[i]] : i.ToString();
    }
}
