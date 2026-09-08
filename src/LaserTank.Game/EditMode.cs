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
        /// dialog edits.  Strings here; the *write widths* are LevelRecord's,
        /// because they are the original's `GetWindowText` counts and belong
        /// with the record they truncate into.
        private string _name = "", _author = "", _hint = "";
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
        public string Name => _name;
        public string Author => _author;
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
            _name = _rec.Name;
            _author = _rec.Author;
            _hint = _rec.Hint;
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
            Status = "editor -- F9 leaves, ctrl+S saves";
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
                _s.E.CurRecData.LName = _name;
                _s.E.CurRecData.Author = _author;
                _s.E.CurRecData.SDiff = _rec.Diff;
                _s.EditorResume();
            }
            Status = Modified ? "left the editor -- unsaved changes are still on the board"
                              : "left the editor";
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
        public bool Cell(Vector2 pos, out int x, out int y)
        {
            int cell = BoardView.CellOf(_view.Size);
            x = (int)Math.Floor((pos.X - BoardView.MarginPx) / cell);
            y = (int)Math.Floor((pos.Y - BoardView.MarginPx) / cell);
            return x >= 0 && x <= 15 && y >= 0 && y <= 15;
        }

        // ---- the palette -----------------------------------------------------

        /// `EditBMWidth = 5` (LTANK2.C:48) and a `SpBm + 4` pitch
        /// (LTANK2.C:1697), which is the original's own grid.  Only its
        /// *position* is this port's, because there is no 180-pixel control
        /// panel to hang it in.
        public const int Across = 5;
        public const int Gap = 4;

        private int Pitch => BoardView.CellOf(_view.Size) + Gap;
        public int PanelWidth => Across * Pitch + 2 * BoardView.MarginPx;
        private float PanelX => BoardView.MarginPx + 16 * BoardView.CellOf(_view.Size)
                                + BoardView.MarginPx;
        private float PanelY => BoardView.MarginPx;

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
                    Status = "tunnel id " + _tunnel;
                    return true;

                // Swap the two selections, which the original does not have and
                // which costs nothing: the palette is on screen either way.
                case Godot.Key.X:
                    (_ed.CurSelBM_L, _ed.CurSelBM_R) = (_ed.CurSelBM_R, _ed.CurSelBM_L);
                    return true;

                // Ctrl+G, the graphics dialog, is in *both* tables (ACC1 and
                // ACC2) -- so it stays live in the editor and BoardView keeps
                // it.  Everything else in ACC1 is not.
                case Godot.Key.G when ctrl: return false;
                case Godot.Key.Z: _view.SetSize(_view.Size % 3 + 1); return true;
                case Godot.Key.Escape: Leave(); return true;
            }
            return true;              // the editor swallows the rest
        }

        /// An edit control with the caret.  Tab walks Name -> Author -> Hint ->
        /// board, Enter and Escape drop out, Backspace deletes.  Everything
        /// else that has a character goes in -- including the letters that are
        /// accelerators outside a field, which is exactly what ACC2's comment
        /// is about.
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
                case Godot.Key.Backspace:
                    Set(Text().Length > 0 ? Text()[..^1] : "");
                    return true;
            }
            long u = k.Unicode;
            // latin-1 and printable: the fields are `char[31]` in a file the
            // 2010 binary reads back, so a character it cannot store is not
            // accepted here either.
            if (u >= 32 && u < 256) Set(Text() + (char)u);
            return true;
        }

        private string Text() => _focus switch
        {
            1 => _name, 2 => _author, 3 => _hint, _ => "",
        };

        private void Set(string v)
        {
            switch (_focus)
            {
                case 1: _name = v; break;
                case 2: _author = v; break;
                case 3: _hint = v; _hintTyped = true; break;
                default: return;
            }
            Modified = true;
        }

        /// Commands 701..705 through EditDiffSet (LTANK.C:84), whose last line
        /// is `CurRecData.SDiff = t` -- the difficulty is a property of the
        /// record and changes the moment the menu item is picked, not at save.
        private void SetDiff(ushort t)
        {
            _rec.Diff = t;
            Modified = true;
            Status = "difficulty " + new TLEVELINFO { SDiff = t }.DiffName.TrimStart(' ', '-');
        }

        /// Command 601, "Clear Field" (LTANK.C:1135): the board, the two edit
        /// controls, and `CurRecData.Hint[0] = 0` -- **one NUL**, so the rest of
        /// the old hint stays in the record and would be written to disk behind
        /// an empty one.  OKtoSave goes false, which is what sends the next save
        /// to Save As.
        private void ClearField()
        {
            _ed.ClearField();
            _name = "";
            _author = "";
            _hint = "";
            _hintTyped = false;
            _rec.ClearHint();
            Modified = true;
            OkToSave = false;
            Status = "cleared -- ctrl+S saves it as a new level";
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
                _rec.Name = _name;
                _rec.Author = _author;
                if (_hintTyped) _rec.Hint = _hint;
                _rec.SetPlayfield(_ed.PlayfieldForSave());
                _rec.Write(dest, _s.Level);
                Modified = false;
                OkToSave = true;
                Status = "saved level " + _s.Level + " to "
                         + Shorten(dest) + (copied ? "  (a copy -- data/ is the corpus)" : "");
            }
            catch (Exception ex)                 // FileError(), LTANK.C:1206
            {
                Status = "save failed: " + ex.Message;
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
            int cell = BoardView.CellOf(_view.Size);
            Vector2 At(int x, int y) =>
                new Vector2(BoardView.MarginPx + x * cell + cell / 2f,
                            BoardView.MarginPx + y * cell + cell / 2f);
            Vector2 Slot(int i) =>
                new Vector2(PanelX + (i % Across) * Pitch + cell / 2f,
                            PanelY + (i / Across) * Pitch + cell / 2f);

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
                    case '<': if (a >= 0 && b >= 0) Click(Slot(a * 16 + b), 1, false); break;
                    case '>': if (a >= 0 && b >= 0) Click(Slot(a * 16 + b), 2, false); break;
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

        /// `PutSelectors` (LTANK2.C:1692) plus the two edit controls under it.
        /// Every slot gets a frame, the two selections get theirs in the
        /// original's own two colours (`JKSelFrame(..., 1)` and `(..., 2)`), and
        /// the 28th slot is drawn empty because it is clickable and would
        /// otherwise be invisible.
        public void Draw(Node2D n, Font font, Atlas atlas)
        {
            if (!Open || _ed == null) return;
            int cell = BoardView.CellOf(_view.Size);
            float x0 = PanelX, y0 = PanelY;

            for (int i = 0; i <= Obj.MaxObjects + 1; i++)
            {
                float x = x0 + (i % Across) * Pitch;
                float y = y0 + (i / Across) * Pitch;
                var r = new Rect2(x, y, cell, cell);
                n.DrawRect(r, new Color(0.10f, 0.11f, 0.13f));
                if (i <= Obj.MaxObjects && atlas.Region(Obj.GetOBM(i), out Rect2 src))
                    n.DrawTextureRectRegion(atlas.Texture, r, src);
                n.DrawRect(r.Grow(1), new Color(0.35f, 0.37f, 0.42f), false, 1);
                if (i == _ed.CurSelBM_L)
                    n.DrawRect(r.Grow(2), Colors.Yellow, false, 2);
                if (i == _ed.CurSelBM_R)
                    n.DrawRect(r.Grow(4), Colors.DeepSkyBlue, false, 2);
                if (i == Obj.MaxObjects)          // the tunnel: show its id
                    n.DrawString(font, new Vector2(x + 3, y + cell - 3),
                                 _tunnel.ToString(), HorizontalAlignment.Left, -1, 11,
                                 Colors.Black);
            }

            float y2 = y0 + (Obj.MaxObjects + Across) / Across * Pitch + 12;
            float w = Across * Pitch;
            n.DrawString(font, new Vector2(x0, y2),
                         "L " + Label(_ed.CurSelBM_L) + "    R " + Label(_ed.CurSelBM_R),
                         HorizontalAlignment.Left, w, 12, Colors.Gainsboro);
            y2 += 20;

            y2 = Field(n, font, x0, y2, w, "name", _name, _focus == 1);
            y2 = Field(n, font, x0, y2, w, "by", _author, _focus == 2);
            y2 = Field(n, font, x0, y2, w, "hint", _hint, _focus == 3);

            var info = new TLEVELINFO { SDiff = _rec.Diff };
            n.DrawString(font, new Vector2(x0, y2),
                         "diff " + (info.DiffName == "" ? "unrated" : info.DiffName.Substring(3))
                         + (Modified ? "   *modified*" : ""),
                         HorizontalAlignment.Left, w, 12,
                         Modified ? Colors.Khaki : Colors.Gainsboro);
            y2 += 22;

            string[] legend =
            {
                "click paints L, right paints R",
                "shift+click rotates in place",
                "drag paints (never a tunnel)",
                "T tunnel id   X swap L/R",
                "ctrl+arrows shift the board",
                "ctrl+C clear   ctrl+S save",
                "tab edits name/by/hint",
                "1-5 difficulty   F9 leaves",
            };
            foreach (string s in legend)
            {
                n.DrawString(font, new Vector2(x0, y2), s,
                             HorizontalAlignment.Left, w, 11, Colors.Gray);
                y2 += 14;
            }
        }

        private static float Field(Node2D n, Font font, float x, float y, float w,
                                   string label, string value, bool focused)
        {
            n.DrawString(font, new Vector2(x, y), label,
                         HorizontalAlignment.Left, w, 11, Colors.Gray);
            n.DrawString(font, new Vector2(x + 34, y),
                         (value.Length == 0 ? "-" : value) + (focused ? "_" : ""),
                         HorizontalAlignment.Left, w - 34, 12,
                         focused ? Colors.Yellow : Colors.White);
            return y + 18;
        }

        /// The object names, LTANK.H's own table read top to bottom.  Index 26
        /// is the tunnel selector and 27 is the slot past the end.
        private static readonly string[] Names =
        {
            "dirt", "tank", "flag", "water", "solid", "block", "bricks",
            "mirror ul", "mirror ur", "mirror dr", "mirror dl",
            "roto ul", "roto ur", "roto dr", "roto dl",
            "one-way up", "one-way rt", "one-way dn", "one-way lt",
            "crystal", "anti-tank up", "anti-tank rt", "anti-tank dn",
            "anti-tank lt", "ice", "thin ice", "tunnel", "(27)",
        };

        private static string Label(int i) =>
            i >= 0 && i < Names.Length ? Names[i] : i.ToString();
    }
}
