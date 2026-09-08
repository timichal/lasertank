// Phase 5, step 4: the level picker and the two high-score lists.
//
// The original has three dialogs here and they are one dialog three times:
//
//   LoadBox    command 106, accelerator L    (LTANK_D.C:311)  pick a level
//   HSList     command 113, accelerator V    (LTANK_D.C:766)  my scores
//   GHSList    command 906, accelerator G    (LTANK_D.C:916)  the posted bests
//
// All three build one string per level, prefix it with a difficulty digit, hand
// it to an owner-drawn listbox, and let DrawLevels colour the row by that digit
// (LTANK_D.C:674).  All three seek the cursor to CurLevel - 1 on open, all three
// load the selected level on Enter, and all three share TransListKey.  So this
// is one class with three modes, and the *row formats are the original's own
// sprintf strings* -- including where they truncate, which is the only part of
// this file that a diff against the C can be taken of.  tools/list_check.py
// takes it: --check-lists dumps every row and Python rebuilds them from the
// .lvl / .hs / .ghs bytes.
//
// What is not here: the Search sub-dialog (SearchBox, LTANK_D.C:394 -- name or
// author substring, difficulty mask, skip-completed) and TransListKey's
// type-ahead.  Both are additive and neither changes a row.
using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    public enum ListMode
    {
        /// LoadBox: number, name, author.
        Levels,
        /// HSList: number, name, then my moves/shots/initials if I have solved it.
        MyScores,
        /// GHSList: number, name, the posted best, then mine -- and `**` and `>`
        /// on the rows where mine is better.
        GlobalScores,
    }

    public sealed class LevelList
    {
        // ID_LOADLEV_00, ID_HIGHLIST_00, ID_GHIGHLIST_00 (LT32L_US.H).  Step 6
        // took the English out: these are now the keys, and the caption comes
        // from the loaded language.  The Levels one also stops saying "Select a
        // Level" and starts saying what the original's dialog says, which is
        // "Pick Level to Load".
        private static readonly Dictionary<ListMode, string> TitleKeys = new()
        {
            [ListMode.Levels] = "ID_LOADLEV_00",
            [ListMode.MyScores] = "ID_HIGHLIST_00",
            [ListMode.GlobalScores] = "ID_GHIGHLIST_00",
        };

        /// DifCList (LTANK_D.C:24), which is COLORREF -- 0x00BBGGRR, so the
        /// bytes read backwards from an RGB literal.  Index is the difficulty
        /// digit: 0 unrated (white), then Kids yellow, Easy cyan, Medium green,
        /// Hard magenta, Deadly red.
        private static readonly Color[] DifCList =
        {
            new Color(1f, 1f, 1f),          // 0x00FFFFFF
            new Color(1f, 1f, 0f),          // 0x0000FFFF
            new Color(0f, 1f, 1f),          // 0x00FFFF00
            new Color(0f, 1f, 0f),          // 0x0000FF00
            new Color(1f, 0f, 1f),          // 0x00FF00FF
            new Color(1f, 0f, 0f),          // 0x000000FF
        };

        /// DrawLevels' selected background, 0x00404080.
        private static readonly Color SelBack = new Color(0.5f, 0.25f, 0.25f);

        private readonly BoardView _view;
        private TLEVELINFO[] _levels = Array.Empty<TLEVELINFO>();
        private THSREC[] _mine = Array.Empty<THSREC>();
        private THSREC[] _posted = Array.Empty<THSREC>();
        private string[] _rows = Array.Empty<string>();
        private int _sel, _top;
        private string _lvlName = "", _fileLabel = "";

        public bool Open { get; private set; }
        public ListMode Mode { get; private set; }

        /// **Command 106 stops the clock; 113 and 906 do not.**  LoadBox is
        /// opened by `x = Game_On; GameOn(FALSE); DialogBox(...)` and the old
        /// value is put back after (LTANK.C:906), while the two score lists just
        /// open -- so a tank left in the open can die while you read your
        /// scores, and cannot while you pick a level.  The same split as the
        /// Difficulty dialog (225, stops) against Graphics (226, does not); see
        /// GraphicsMenu.  BoardView reads this to decide whether to tick.
        public bool StopsClock => Mode == ListMode.Levels;
        /// The level the player chose, or 0 -- BoardView reads and clears it,
        /// which is `EndDialog(Dialog, i + 100)` and the `if (i > 100)` that
        /// meets it (LTANK.C:910).
        public int Chosen { get; private set; }

        public LevelList(BoardView view) { _view = view; }

        public int Count => _rows.Length;

        /// Read the three files and build every row.  The original does this in
        /// WM_INITDIALOG, once per opening, and so does this: a .hs written by
        /// the win two seconds ago has to show up.
        public void Show(ListMode mode, string lvlPath, int current)
        {
            Mode = mode;
            Chosen = 0;
            var files = new ScoreFiles(lvlPath);
            _lvlName = Path.GetFileName(lvlPath);
            _fileLabel = mode switch
            {
                ListMode.MyScores => Path.GetFileName(files.Hs),
                ListMode.GlobalScores => Path.GetFileName(files.Ghs),
                _ => _lvlName,
            };
            try { _levels = LevelFile.ReadLevelList(lvlPath); }
            catch (IOException) { _levels = Array.Empty<TLEVELINFO>(); }
            _mine = ReadAll(files.Hs);
            _posted = ReadAll(files.Ghs);
            _rows = BuildRows(mode, _levels, _mine, _posted);

            // LB_SETCURSEL on CurLevel - 1 (LTANK_D.C:359).
            _sel = Math.Clamp(current - 1, 0, Math.Max(0, _rows.Length - 1));
            _top = Math.Max(0, _sel - _rowsShown / 2);
            Open = true;
        }

        public void Close() { Open = false; }

        /// Every 10-byte record in a .hs / .ghs, in order.  The three dialogs
        /// read these files *sequentially* alongside the .lvl and stop when
        /// either runs out, so the arrays are naturally ragged: a 2,030-level
        /// collection can have a 12-record .hs, and levels 13 and up simply have
        /// no row data.
        private static THSREC[] ReadAll(string path)
        {
            if (!File.Exists(path)) return Array.Empty<THSREC>();
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (IOException) { return Array.Empty<THSREC>(); }
            int n = data.Length / THSREC.Size;
            var outv = new THSREC[n];
            for (int i = 0; i < n; i++)
            {
                int at = i * THSREC.Size;
                int len = 0;
                while (len < THSREC.NameSize && data[at + 4 + len] != 0) len++;
                outv[i] = new THSREC
                {
                    Moves = (ushort)(data[at] | (data[at + 1] << 8)),
                    Shots = (ushort)(data[at + 2] | (data[at + 3] << 8)),
                    Name = System.Text.Encoding.Latin1.GetString(data, at + 4, len),
                };
            }
            return outv;
        }

        private static THSREC At(THSREC[] a, int i) => i < a.Length ? a[i] : null;

        /// The row text, digit prefix and all.
        ///
        /// **These are the original's format strings, not a rendering of the
        /// same idea.** `%-30.30s` pads a short name to 30 and truncates a long
        /// one at 30; `%4d` right-aligns the number in four and lets a fifth
        /// digit through; `%4s` on the initials is why four is the reachable
        /// width of a six-byte field. Every one of those is observable, and
        /// tools/list_check.py rebuilds them from the file bytes in Python -- so
        /// the formats are carried here rather than approximated, in the same
        /// spirit as reading LaserOffset out of its table.
        ///
        /// The prefix is DrawLevels' colour key and is stripped before drawing
        /// (`TextOut(..., temps+1, lstrlen(temps)-1)`).
        public static string[] BuildRows(ListMode mode, TLEVELINFO[] levels,
                                         THSREC[] mine, THSREC[] posted)
        {
            // HSList and GHSList are driven by the *score* file, not the level
            // file: `while (BytesMoved == sizeof(THSREC))` reads a score record
            // and then a level record, so the list is as long as the score file
            // and stops there.  LoadBox is driven by the level file.
            int n = mode switch
            {
                ListMode.MyScores => Math.Min(levels.Length, mine.Length),
                ListMode.GlobalScores => Math.Min(levels.Length, posted.Length),
                _ => levels.Length,
            };
            var rows = new string[n];
            for (int i = 0; i < n; i++)
            {
                TLEVELINFO lv = levels[i];
                THSREC my = At(mine, i), best = At(posted, i);
                string s;
                switch (mode)
                {
                    case ListMode.MyScores:
                        // sprintf("%4d %-30.30s", i, LName), then the score.
                        s = $"{lv.Number,4} {Pad(lv.LName, 30)}";
                        if (my != null && my.Moves > 0)
                            s += $"{my.Moves,5}  {my.Shots,5}  {my.Name}";
                        break;

                    case ListMode.GlobalScores:
                        // BHS: I have solved it and I am better than the posted
                        // best -- which needs a posted best to exist, and a
                        // blank one (moves == 0) counts as beaten.
                        bool bhs = my != null && my.Moves > 0
                                   && LevelFile.Beats(my.Moves, my.Shots, best);
                        ushort bm = best?.Moves ?? 0, bs = best?.Shots ?? 0;
                        string bn = best?.Name ?? "";
                        s = bhs
                            // "%4d** %-28.28s %5d %5d  %4s>"
                            ? $"{lv.Number,4}** {Pad(lv.LName, 28)} {bm,5} {bs,5}  {bn,4}>"
                            // "%4d %-30.30s %5d %5d  %4s "
                            : $"{lv.Number,4} {Pad(lv.LName, 30)} {bm,5} {bs,5}  {bn,4} ";
                        if (my != null && my.Moves > 0)
                            s += $"{my.Moves,5}  {my.Shots,5}  {my.Name}";
                        break;

                    default:
                        // sprintf("%4d   %-30.30s %s", i, LName, Author)
                        s = $"{lv.Number,4}   {Pad(lv.LName, 30)} {lv.Author}";
                        break;
                }
                rows[i] = lv.DiffDigit + s;
            }
            return rows;
        }

        /// `%-30.30s`: pad to `w` with spaces, truncate at `w`.
        private static string Pad(string s, int w)
        {
            s ??= "";
            return s.Length >= w ? s.Substring(0, w) : s.PadRight(w);
        }

        // ---- keys -----------------------------------------------------------
        /// How many rows the last Draw actually fitted, which is what PageUp and
        /// PageDown move by.  A page that is not the visible page is the kind of
        /// small wrongness that makes a long list unusable, and the height
        /// depends on the board size, so Draw measures it and paging reads it.
        /// The initial value only matters for a key pressed before the first
        /// frame, which cannot happen -- Show is called from the key handler.
        private int _rowsShown = 20;

        public bool Key(Key k)
        {
            switch (k)
            {
                case Godot.Key.Up: Move(-1); break;
                case Godot.Key.Down: Move(1); break;
                case Godot.Key.Pageup: Move(-_rowsShown); break;
                case Godot.Key.Pagedown: Move(_rowsShown); break;
                case Godot.Key.Home: Move(-_rows.Length); break;
                case Godot.Key.End: Move(_rows.Length); break;
                case Godot.Key.Enter:
                case Godot.Key.KpEnter:
                case Godot.Key.Space:
                    // All three dialogs load the highlighted level and close.
                    if (_sel < _levels.Length) Chosen = _levels[_sel].Number;
                    Close();
                    break;
                default:
                    // Cancel (id 2) or the key that opened it.  A dialog eats
                    // everything else.
                    Close();
                    break;
            }
            return true;
        }

        private void Move(int d)
        {
            if (_rows.Length == 0) return;
            _sel = Math.Clamp(_sel + d, 0, _rows.Length - 1);
            _top = Math.Clamp(_top, _sel - _rowsShown + 1, _sel);
            _top = Math.Clamp(_top, 0, Math.Max(0, _rows.Length - _rowsShown));
        }

        // ---- drawing --------------------------------------------------------
        private const int Pad_ = 10;
        private const int Line = 15;

        /// Full-board, unlike the graphics dialog: a 2,030-row list is what this
        /// panel is for, and the original's is a big window too.
        public void Draw(Node2D n, Font font, Font mono, Rect2 board)
        {
            var panel = new Rect2(board.Position + new Vector2(4, 4),
                                  board.Size - new Vector2(8, 8));
            n.DrawRect(panel, new Color(0.04f, 0.05f, 0.07f, 0.97f));
            n.DrawRect(panel, new Color(0.55f, 0.60f, 0.70f), false, 1);

            float x = panel.Position.X + Pad_;
            float w = panel.Size.X - 2 * Pad_;
            float y = panel.Position.Y + Pad_ + 12;

            n.DrawString(font, new Vector2(x, y),
                         $"{_view.Strings[TitleKeys[Mode]]}   {_fileLabel}   "
                         + $"({_rows.Length})",
                         HorizontalAlignment.Left, w, 14, Colors.White);
            y += Line + 5;

            if (_rows.Length == 0)
            {
                n.DrawString(font, new Vector2(x, y),
                             Mode == ListMode.Levels
                                 ? "the level file could not be read"
                                 : $"no {_fileLabel} yet -- solve a level first",
                             HorizontalAlignment.Left, w, 12, Colors.OrangeRed);
                return;
            }

            // As many rows as the panel has room for, remembered so that a
            // page-up moves by exactly one screenful at every board size.
            int rows = Math.Max(1, (int)((panel.End.Y - y - 26) / Line));
            _rowsShown = rows;
            int top = Math.Clamp(_top, 0, Math.Max(0, _rows.Length - rows));
            top = Math.Clamp(top, _sel - rows + 1, _sel);
            // Written back, not just used: Show() has to guess a page size
            // before the first frame, and a `_top` that disagrees with what was
            // drawn makes the next arrow key jump.
            _top = top = Math.Max(0, top);
            for (int i = top; i < Math.Min(_rows.Length, top + rows); i++)
            {
                string row = _rows[i];
                Color tint = DifCList[row[0] - '0'];
                if (i == _sel)
                    n.DrawRect(new Rect2(x - 3, y - 11, w + 6, Line), SelBack);
                // temps + 1: the digit is the colour key, not text.
                n.DrawString(mono, new Vector2(x, y), row.Substring(1),
                             HorizontalAlignment.Left, w, 12, tint);
                y += Line;
            }

            n.DrawString(font, new Vector2(x, panel.End.Y - 12),
                         "up/down + PgUp/PgDn pick   Enter loads   any other key closes",
                         HorizontalAlignment.Left, w, 11, Colors.Gray);
        }
    }
}
