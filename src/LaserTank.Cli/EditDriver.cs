// `--edit`: the level editor's board operations, driven by a token stream and
// traced step by step, so they can be diffed against oracle/driver.c's
// edit_run() -- and, for the one function that is really in LTANK2.C
// (`ChangeGO`), against the 2002 C itself.
//
// **This file is the third implementation of the fifteen-line rule again**, and
// deliberately so: `Core.Editor` is the port, `oracle/driver.c`'s edit_token is
// the driver-side transliteration of LTANK.C's window-proc cases, and this is
// what the *game's* menu and mouse will call.  The argument is step 4's --
// two engines proving the arithmetic, a third proving that what the UI invokes
// is that arithmetic and not something adjacent to it.
using System;
using LaserTank.Core;

namespace LaserTank.Cli
{
    /// One edit script.  Nothing here ticks: command 201 calls `GameOn(FALSE)`
    /// before it does anything else (LTANK.C:1086), so an edited board is a
    /// still picture and the trace's `t=` counts edits rather than 50 ms slices.
    public sealed class EditDriver
    {
        private readonly Engine _e;
        private readonly TraceWriter _tr;
        private readonly Editor _ed;

        /// `CurRecData` -- the 576 bytes the level was read from, edited in
        /// place and written back.  Command 603 writes the *record*, not a
        /// freshly encoded one, which is what keeps an unedited level
        /// byte-identical through a save.
        private readonly LevelRecord _rec;

        /// The two edit controls (`Ed1`, `Ed2`).  Command 201 fills them from
        /// the record and command 603 reads them back with `GetWindowText`, so
        /// they are strings here and the *widths* live in LevelRecord.
        private string _name, _author;

        /// Set only when the Hint dialog was opened and OK'd: `HintBox` writes
        /// `CurRecData.Hint` on its OK arm and nowhere else, so a save with the
        /// dialog untouched leaves the hint bytes exactly as they were found.
        private string _hint;
        private bool _hintTyped;

        public EditDriver(Engine e, TraceWriter tr, LevelRecord rec = null)
        {
            _e = e;
            _tr = tr;
            _ed = new Editor(e);
            _rec = rec ?? new LevelRecord();
            _name = _rec.Name;
            _author = _rec.Author;
        }

        public string Name { get => _name; set => _name = value; }
        public string Author { get => _author; set => _author = value; }
        public string Hint
        {
            get => _hintTyped ? _hint : _rec.Hint;
            set { _hint = value; _hintTyped = true; }
        }

        /// Command 603, "Save Level" (LTANK.C:1191).  Everything it does, in
        /// its order: read the two edit controls into the record, copy the
        /// playfield over, **stamp the tank into it**, then write the record at
        /// `(level-1) * 576`.
        ///
        /// The difficulty is already in the record -- `EditDiffSet` writes it
        /// when the menu item is picked (LTANK.C:103), not at save time -- and
        /// so is the hint, which only `HintBox`'s OK arm touches.
        public void Save(string path, int level)
        {
            _rec.Name = _name;
            _rec.Author = _author;
            if (_hintTyped) _rec.Hint = _hint;
            _rec.SetPlayfield(_ed.PlayfieldForSave());
            _rec.Write(path, level);
        }

        /// The token set is documented on `--edit` in Usage() and, at length,
        /// in oracle/driver.c above `edit_run`.  Two hex digits spell a cell,
        /// one spells a small number, and an unknown character is skipped --
        /// the same rule `--keys` and `--script` follow.
        public long Run(string script)
        {
            long step = 0;
            _ed.Enter();
            _e.GameOn(false);
            _tr?.Tick(0, _e);
            int at = 0;
            while (at < script.Length)
            {
                int n = Token(script, at);
                if (n <= 0) break;
                at += n;
                _tr?.Tick(++step, _e);
            }
            return step;
        }

        private int Token(string s, int at)
        {
            char c = s[at];
            switch (c)
            {
                case '<':
                case '>':
                    if (at + 2 >= s.Length) return s.Length - at;
                    {
                        int v = Hex(s[at + 1]) * 16 + Hex(s[at + 2]);
                        if (Hex(s[at + 1]) >= 0 && Hex(s[at + 2]) >= 0)
                        {
                            if (c == '<') _ed.CurSelBM_L = v; else _ed.CurSelBM_R = v;
                        }
                    }
                    return 3;

                case 't':
                    if (at + 1 >= s.Length) return 1;
                    {
                        int v = Hex(s[at + 1]);
                        if (v >= 0) _ed.TunnelId = v & 7;
                    }
                    return 2;

                case 'l':
                case 'r':
                case 's':
                case 'p':
                case 'q':
                case 'P':
                    if (at + 2 >= s.Length) return s.Length - at;
                    {
                        int x = Hex(s[at + 1]), y = Hex(s[at + 2]);
                        if (x < 0 || y < 0) return 3;
                        switch (c)
                        {
                            case 'l': _ed.LeftClick(x, y, false); break;
                            case 's': _ed.LeftClick(x, y, true); break;   // rotate
                            case 'r': _ed.RightClick(x, y); break;
                            case 'p': _ed.Drag(x, y, 1, false); break;
                            case 'q': _ed.Drag(x, y, 2, false); break;
                            case 'P': _ed.Drag(x, y, 1, true); break;     // a no-op
                        }
                    }
                    return 3;

                case 'R': _ed.Shift(1, 0); return 1;
                case 'L': _ed.Shift(-1, 0); return 1;
                case 'U': _ed.Shift(0, -1); return 1;
                case 'D': _ed.Shift(0, 1); return 1;
                case 'C':
                    // Command 601 clears the field *and* the two edit controls,
                    // and truncates the hint to nothing by writing one NUL --
                    // `CurRecData.Hint[0] = 0`, which leaves the rest of the old
                    // hint in the record and on disk.
                    _ed.ClearField();
                    _name = "";
                    _author = "";
                    _rec.ClearHint();
                    _hintTyped = false;
                    return 1;
                case 'E': _ed.Enter(); return 1;

                // Commands 701..705 (LTANK.C:1268).  EditDiffSet checks a menu
                // item and then assigns `CurRecData.SDiff = t` -- so the
                // difficulty is a property of the *record*, changed the moment
                // the menu item is picked, and it never touches the board.
                case '1': _rec.Diff = 1; return 1;
                case '2': _rec.Diff = 2; return 1;
                case '3': _rec.Diff = 4; return 1;
                case '4': _rec.Diff = 8; return 1;
                case '5': _rec.Diff = 16; return 1;

                default: return 1;                    // unknown: skipped
            }
        }

        /// The tunnel-id prompts ChangeGO opened -- the oracle's `dialogs=`.
        public int Dialogs => _ed.TunnelDialogs;

        private static int Hex(char c) =>
            c >= '0' && c <= '9' ? c - '0'
            : c >= 'a' && c <= 'f' ? c - 'a' + 10
            : c >= 'A' && c <= 'F' ? c - 'A' + 10
            : -1;
    }
}
