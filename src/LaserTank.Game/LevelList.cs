// Phase 5, step 4: the level picker and the two high-score lists -- one table
// since step 8.
//
// The original has three dialogs here and they are one dialog three times:
//
//   LoadBox    command 106, accelerator L    (LTANK_D.C:311)  pick a level
//   HSList     command 113, accelerator V    (LTANK_D.C:766)  my scores
//   GHSList    command 906, accelerator G    (LTANK_D.C:916)  the posted bests
//
// All three build one string per level, prefix it with a difficulty digit, hand
// it to an owner-drawn listbox, and let DrawLevels colour the row by that digit
// (LTANK_D.C:674).  All three seek to `CurLevel - 1` on open, all three load the
// selected level on Enter, and all three share TransListKey.  They differ in one
// thing only: *which columns of the same row they print*.  LoadBox prints the
// name and the author, HSList prints the name and your score, GHSList prints the
// name, the posted best and your score again.
//
// **So step 8 prints all of them at once, in one table.**  Not three keys, and
// not three tabs either -- tabs would have been the same three lists with the
// same two of them hidden.  One row per level, every column the three dialogs
// between them had: number, name, author, the posted best, yours.  `L` opens it
// and that is the whole of it; `V` and `G` are unbound and free, which is what
// the merge was *for* -- ACC1 has no spare letters.
//
// Four things follow from the merge and are written down rather than left to be
// rediscovered:
//
//   * **The table is the level file's, so it has a row per level**, blank score
//     cells and all.  The original's two score dialogs stop where their score
//     file stops (`while (BytesMoved == sizeof(THSREC))` reads a score record
//     and *then* a level record), so a 2,030-level collection with a 79-record
//     .hs gave a 79-row list.  A table that did that would hide level 80
//     onwards from the only list there is.  BuildRows still has the original's
//     rule, because BuildRows is the gate's.
//   * **The rows are the port's own, and BuildRows is now only the record.**
//     What is drawn is BuildTable below -- one row, the columns side by side.
//     What tools/list_check.py diffs against Python, byte for byte, is still
//     BuildRows: the original's three `sprintf` formats, kept because they are
//     the transliteration and the only part of this file a diff against the C
//     can be taken of.  **The field widths are shared**: the table's name cell
//     is `%-30.30s` and its score cells are `%5d` and `%4s` because those are
//     the original's, so the merge changed the *arrangement* of the columns and
//     not one of them.
//   * **The clock stops for the panel.**  106 stops it (`x = Game_On;
//     GameOn(FALSE); DialogBox(...)`, LTANK.C:906) and 113/906 do not, so a
//     tank left in the open could die while you read your scores and could not
//     while you picked a level.  One panel cannot be both, and it is `L` --
//     106 -- that opens this one.  What is lost is being killed by a laser you
//     left in flight while reading a score list: the first observable behaviour
//     this port drops rather than reproduces, and this is where it is recorded.
//   * **The table has headers**, which the original's listboxes never had.  Six
//     columns of bare numbers is what the global list was, and nothing on
//     screen said which three were the posted best and which three were yours.
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
    /// The original's three dialogs, which are three column sets of one row.
    /// **Nothing draws these any more** -- the panel draws BuildTable's single
    /// row.  They are what BuildRows builds, and BuildRows is what
    /// tools/list_check.py diffs against Python: the transliteration, kept.
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
        /// ID_LOADLEV_00 (LT32L_US.H), the caption of the dialog command 106
        /// opens -- which is the dialog this panel is, since `L` is what opens
        /// it.  Step 6 took the English out: this is the key, and the caption
        /// comes from the loaded language.
        ///
        /// **ID_HIGHLIST_00 and ID_GHIGHLIST_00 are no longer read**, and that
        /// is a finding for the i18n audit rather than an oversight: they are
        /// the captions of two dialogs this port no longer has, because their
        /// columns are in this table.  The audit's rule is that a key is read by
        /// a widget or it goes, and these two are the first pair the rule has
        /// caught.  They are not deleted here -- deleting a key means editing
        /// all ten JSON files and lang_check.py's expectation, which is the
        /// audit's job and not this change's.
        private const string TitleKey = "ID_LOADLEV_00";

        /// DifCList (LTANK_D.C:24), which is COLORREF -- 0x00BBGGRR, so the
        /// bytes read backwards from an RGB literal.  Index is the difficulty
        /// digit, and the original's six values are:
        ///
        ///   0 unrated 0x00FFFFFF white     1 Kids   0x0000FFFF yellow
        ///   2 Easy    0x00FFFF00 cyan      3 Medium 0x0000FF00 green
        ///   4 Hard    0x00FF00FF magenta   5 Deadly 0x000000FF red
        ///
        /// **What is drawn is Ui.Diff** -- the same six ranks in the same order,
        /// re-picked in step 7 for a dark ground, because six saturated
        /// primaries were chosen against the original's light grey listbox and
        /// three of them vibrate on this one.  The values above stay written
        /// down: which *rank* each index is is the part that is load-bearing,
        /// and it is the same in both.
        private static Color[] DifCList => Ui.Diff;

        private readonly BoardView _view;
        private TLEVELINFO[] _levels = Array.Empty<TLEVELINFO>();
        private THSREC[] _mine = Array.Empty<THSREC>();
        private THSREC[] _posted = Array.Empty<THSREC>();
        /// The table, and the table without the author column -- see Fit.
        private string[] _wide = Array.Empty<string>();
        private string[] _narrow = Array.Empty<string>();
        private int _sel, _top;
        private string _lvlName = "";
        private int _solved;

        public bool Open { get; private set; }

        /// **The panel stops the clock.**  The original's own split -- 106 stops
        /// it, 113 and 906 do not -- is in this file's header along with why the
        /// merge could not keep both.
        public bool StopsClock => true;
        /// The level the player chose, or 0 -- BoardView reads and clears it,
        /// which is `EndDialog(Dialog, i + 100)` and the `if (i > 100)` that
        /// meets it (LTANK.C:910).
        public int Chosen { get; private set; }

        public LevelList(BoardView view) { _view = view; }

        public int Count => _wide.Length;

        /// Read the three files and build the table.  The original does this in
        /// WM_INITDIALOG, once per opening, and so does this: a .hs written by
        /// the win two seconds ago has to show up.
        public void Show(string lvlPath, int current)
        {
            Chosen = 0;
            var files = new ScoreFiles(lvlPath);
            _lvlName = Path.GetFileName(lvlPath);
            try { _levels = LevelFile.ReadLevelList(lvlPath); }
            catch (IOException) { _levels = Array.Empty<TLEVELINFO>(); }
            _mine = ReadAll(files.Hs);
            _posted = ReadAll(files.Ghs);
            _wide = BuildTable(_levels, _mine, _posted, Wide);
            _narrow = BuildTable(_levels, _mine, _posted, Narrow);

            // The count the collection picker shows beside each collection, for
            // the same reason it shows it: it is the only number in this game
            // that answers "where was I".
            _solved = 0;
            foreach (THSREC r in _mine) if (r.Moves > 0) _solved++;

            // LB_SETCURSEL on CurLevel - 1 (LTANK_D.C:359).
            _sel = Math.Clamp(current - 1, 0, Math.Max(0, _wide.Length - 1));
            _top = Math.Max(0, _sel - _rowsShown / 2);
            Open = true;
        }

        public void Close() { Open = false; }

        /// Every 10-byte record in a .hs / .ghs, in order.  The three dialogs
        /// read these files *sequentially* alongside the .lvl and stop when
        /// either runs out, so the arrays are naturally ragged: a 2,030-level
        /// collection can have a 12-record .hs, and levels 13 and up simply have
        /// no row data.  The table leaves those cells blank rather than stopping
        /// -- see the header.
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

        // ---- the table ------------------------------------------------------
        //
        // One row per level, in character columns, because the rows are drawn in
        // a fixed pitch and columns in a fixed pitch are just offsets.  The two
        // layouts are the same table with and without the author: a narrow
        // window drops the one column that is flavour rather than score, which
        // is the alternative to shrinking the type until none of it is legible.

        /// Where each cell starts, and how wide the row is.  `Author` is -1 in
        /// the narrow layout, which is the whole of the difference.
        private sealed class Cells
        {
            public int Num, Flag, Name, Author, BMoves, BShots, BWho, Gt,
                       MMoves, MShots, MWho, Width;
        }

        /// **Every width here is the original's.**  `%4d` on the number, the
        /// `%-30.30s` name that pads a short one to 30 and truncates a long one
        /// at 30, `%5d` on moves and shots -- which right-aligns in five and
        /// lets a sixth digit through -- and `%4s` on the initials, which is why
        /// four is the reachable width of a six-byte field.  What the merge
        /// changed is where the cells sit, not what fits in one.
        ///
        /// The author is the one cell with a width of the port's own choosing:
        /// the original prints it `%s`, unbounded, at the end of a row that has
        /// nothing after it.  Here it has two score groups after it, so it is
        /// cut at 20 -- which is past the last character of all but a handful of
        /// the corpus's authors.
        private static readonly Cells Wide = new()
        {
            Num = 0, Flag = 5, Name = 8, Author = 39,
            BMoves = 60, BShots = 66, BWho = 72, Gt = 77,
            MMoves = 79, MShots = 85, MWho = 91, Width = 95,
        };

        private static readonly Cells Narrow = new()
        {
            Num = 0, Flag = 5, Name = 8, Author = -1,
            BMoves = 39, BShots = 45, BWho = 51, Gt = 56,
            MMoves = 58, MShots = 64, MWho = 70, Width = 74,
        };

        /// Text at given character columns, space-filled between.  A cell that
        /// overruns its slot pushes the rest of the row right, which is the
        /// `%5d`-with-six-digits case and is what the original does too.
        private static string Cols(params (int at, string text)[] parts)
        {
            var sb = new System.Text.StringBuilder();
            foreach ((int at, string text) in parts)
            {
                if (text == null) continue;
                while (sb.Length < at) sb.Append(' ');
                sb.Append(text);
            }
            return sb.ToString();
        }

        /// The table, one row per level, difficulty digit and all.
        ///
        /// A level with no score record, or a record with `moves == 0` -- which
        /// is what the padding `BuildHSFile` writes in front of a high score on
        /// a later level -- leaves its three cells blank.  Blank is the honest
        /// rendering: the original's own test for "have I solved this" is
        /// `moves > 0` and nothing else.
        private static string[] BuildTable(TLEVELINFO[] levels, THSREC[] mine,
                                           THSREC[] posted, Cells c)
        {
            var rows = new string[levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                TLEVELINFO lv = levels[i];
                THSREC my = At(mine, i), best = At(posted, i);
                bool solved = my != null && my.Moves > 0;
                bool haveBest = best != null && best.Moves > 0;
                // BHS (LTANK_D.C:939): I have solved it and I am better than the
                // posted best -- and a best that is absent or blank counts as
                // beaten, which is LevelFile.Beats' own first clause.
                bool bhs = solved && LevelFile.Beats(my.Moves, my.Shots, best);

                string s = Cols(
                    (c.Num, $"{lv.Number,4}"),
                    // The original's own two markers, in the original's own
                    // places: `**` by the number and `>` between the posted best
                    // and yours, pointing at yours.
                    (c.Flag, bhs ? "**" : null),
                    (c.Name, Pad(lv.LName, 30)),
                    (c.Author, c.Author < 0 ? null : Pad(lv.Author, 20)),
                    (c.BMoves, haveBest ? $"{best.Moves,5}" : null),
                    (c.BShots, haveBest ? $"{best.Shots,5}" : null),
                    (c.BWho, haveBest ? best.Name : null),
                    (c.Gt, bhs ? ">" : null),
                    (c.MMoves, solved ? $"{my.Moves,5}" : null),
                    (c.MShots, solved ? $"{my.Shots,5}" : null),
                    (c.MWho, solved ? my.Name : null));
                rows[i] = lv.DiffDigit + s;
            }
            return rows;
        }

        /// The two header lines: the column names, and above them the two groups
        /// the six score columns fall into.  Without the group line the row
        /// reads `103 46 Duck 125 49 mz` and nothing says which three are the
        /// world's and which three are yours.
        private static string HeaderLine(Cells c) => Cols(
            (c.Num, "   #"), (c.Name, "level"),
            (c.Author, c.Author < 0 ? null : "author"),
            (c.BMoves, "moves"), (c.BShots, "shots"), (c.BWho, "who"),
            (c.MMoves, "moves"), (c.MShots, "shots"), (c.MWho, "who"));

        private static string GroupLine(Cells c) => Cols(
            (c.BMoves, "posted best"), (c.MMoves, "yours"));

        /// `%-30.30s`: pad to `w` with spaces, truncate at `w`.
        private static string Pad(string s, int w)
        {
            s ??= "";
            return s.Length >= w ? s.Substring(0, w) : s.PadRight(w);
        }

        // ---- the original's three rows, which nothing draws ------------------

        /// The row text of the original's three dialogs, digit prefix and all.
        ///
        /// **These are the original's format strings, not a rendering of the
        /// same idea**, and they are here to be diffed: `--check-lists` dumps
        /// every row and tools/list_check.py rebuilds them from the .lvl / .hs /
        /// .ghs bytes in Python.  `%-30.30s` pads a short name to 30 and
        /// truncates a long one at 30; `%4d` right-aligns the number in four and
        /// lets a fifth digit through; `%4s` on the initials is why four is the
        /// reachable width of a six-byte field.  Every one of those is
        /// observable, so the formats are carried here rather than approximated,
        /// in the same spirit as reading LaserOffset out of its table.
        ///
        /// The prefix is DrawLevels' colour key and is stripped before drawing
        /// (`TextOut(..., temps+1, lstrlen(temps)-1)`).
        ///
        /// **Step 8 stopped drawing these** -- the panel draws BuildTable -- and
        /// they stay because the gate is the reason they were written down.  The
        /// widths they carry are the widths the table uses.
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
                case Godot.Key.Home: Move(-_wide.Length); break;
                case Godot.Key.End: Move(_wide.Length); break;
                case Godot.Key.Enter:
                case Godot.Key.KpEnter:
                case Godot.Key.Space:
                    if (_wide.Length > 0 && _sel < _levels.Length)
                        Chosen = _levels[_sel].Number;
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
            if (_wide.Length == 0) return;
            _sel = Math.Clamp(_sel + d, 0, _wide.Length - 1);
            _top = Math.Clamp(_top, _sel - _rowsShown + 1, _sel);
            _top = Math.Clamp(_top, 0, Math.Max(0, _wide.Length - _rowsShown));
        }

        /// The wheel.  It moves the *selection*, not a second scroll position:
        /// Draw clamps the viewport to the cursor, so a list that scrolled away
        /// from its own cursor would snap back on the next arrow key.
        public void Scroll(int d) => Move(d);

        /// A click on a row.  **The first lands on it and the second loads it**
        /// -- a double-click that does not have to be fast.  Godot's own
        /// DoubleClick would do on a desktop and be a coin toss on a phone, and
        /// the reason a list needs two taps at all is that there is no hover on
        /// a touch screen: the selection *is* the preview here, and a 2,030-row
        /// table is not a place to load something on the first tap.
        private void Pick(int i)
        {
            if (_wide.Length == 0) return;
            if (i == _sel)
            {
                if (i < _levels.Length) Chosen = _levels[i].Number;
                Close();
                return;
            }
            _sel = Math.Clamp(i, 0, _wide.Length - 1);
        }

        // ---- drawing --------------------------------------------------------
        /// The row pitch, which follows the UI scale like every other length in
        /// the redesign -- and which the paging arithmetic reads, so a bigger
        /// window really does page by a bigger screenful rather than scrolling
        /// the same fifteen rows faster.
        private static int Line => Ui.Px(16);

        /// Which layout and which type size the window can actually hold.
        ///
        /// **A table's columns are only worth having if they are all on
        /// screen**, which is the whole point of the merge, so the fit is
        /// searched rather than assumed: the full table at the normal size
        /// first, then a step or two smaller, then the table without its author
        /// column, and only then is anything allowed to clip.  Dropping a column
        /// beats shrinking the type past legibility, and the author is the one
        /// column that is flavour rather than score.
        private (Cells cells, float size, float width) Fit(float room, Font mono)
        {
            foreach (Cells c in new[] { Wide, Narrow })
                for (float size = 12f; size >= 10.5f; size -= 0.5f)
                {
                    float w = Advance(mono, size) * c.Width;
                    if (w <= room) return (c, size, w);
                }
            return (Narrow, 10.5f, Advance(mono, 10.5f) * Narrow.Width);
        }

        /// One glyph of the fixed-pitch face, which is every column offset's
        /// unit.
        ///
        /// **Measured over a run, not over one character.**  A single `"0"`
        /// answers with that glyph's own measured width, which is not the
        /// advance the next glyph would be placed at -- here it came back 0.15
        /// px wide, and 0.15 px times the 77 characters from the left edge to
        /// the `>` column is a drifting column rule that crosses the text it is
        /// supposed to divide. Dividing a long run by its length is the advance
        /// itself, bearings and all.
        private const string Ruler = "00000000000000000000000000000000";

        private static float Advance(Font mono, float size)
            => mono.GetStringSize(Ruler, HorizontalAlignment.Left, -1,
                                  Ui.Px(size)).X / Ruler.Length;

        /// A centred dialog, unlike the graphics one which is a small box: a
        /// 2,030-row table is what this panel is for, and the original's is a
        /// big window too.
        public void Draw(Node2D n, Font font, Font mono, Rect2 host)
        {
            // Centred on the window rather than filling the board: step 7 made
            // the window bigger than the board, and a list that filled the
            // *board* on a wide one sat off to the left of everything else.
            Ui.Scrim(n, host);
            // "Any other key closes", for a pointer.  Registered before the
            // panel because the hit list is walked backwards -- see Hits.
            _view.Chrome.Add(host, "scrim", Close);

            float pad = Ui.Px(18);
            (Cells c, float size, float table) = Fit(host.Size.X - Ui.Px(40) - 2 * pad,
                                                     mono);
            float w = Mathf.Min(Mathf.Max(Ui.Px(420), table + 2 * pad),
                                host.Size.X - Ui.Px(40));
            float h = Mathf.Min(Ui.Px(620), host.Size.Y - Ui.Px(40));
            var panel = new Rect2(
                Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                Mathf.Round(host.Position.Y + (host.Size.Y - h) / 2f), w, h);
            Ui.Dialog(n, panel);
            // A miss inside the panel is not an answer, so it does not close it.
            _view.Chrome.Swallow(panel);

            float x = panel.Position.X + pad;
            w = panel.Size.X - 2 * pad;
            float y = panel.Position.Y + pad + Ui.Px(11);

            Ui.Caps(n, new Vector2(x, y), _view.Strings[TitleKey], Ui.Text, 12);
            // What file this is, how many levels are in it, and how many of them
            // you have solved -- which is the one number in this game that
            // answers "where was I", and the reason the collection picker shows
            // it too.
            Rect2 close = Ui.CloseRect(panel, pad);
            Ui.CloseX(n, close, _view.Chrome.Add(Ui.Touch(close), "close", Close));
            float cw = Ui.Px(280);
            Ui.Write(n, new Vector2(close.Position.X - Ui.Px(10) - cw, y),
                     $"{_lvlName}  ·  {_wide.Length} levels  ·  {_solved} solved",
                     11, Ui.Faint, cw, HorizontalAlignment.Right);
            y += Ui.Px(12);
            Ui.Rule(n, x, y, w);
            y += Ui.Px(16);

            if (_wide.Length == 0)
            {
                Ui.Write(n, new Vector2(x, y + Ui.Px(10)),
                         "the level file could not be read", 12, Ui.Bad, w);
                Footer(n, panel, x, w);
                return;
            }

            // ---- the two header lines, at the table's own columns.
            float chw = Advance(mono, size);
            float headTop = y - Line + 4;
            // **At the rows' own size, not a size down.**  The header cells are
            // placed by character column and a column is only a position if
            // every line in the table advances by the same glyph -- set one
            // point smaller, as these were on the first pass, "moves" sits a
            // finger to the left of the moves it names and the whole table
            // reads as broken.  Faint rather than small is what makes them
            // headers.
            n.DrawString(mono, new Vector2(x, y), GroupLine(c), HorizontalAlignment.Left,
                         w, Ui.Px(size), Ui.Faint * new Color(1, 1, 1, 0.7f));
            y += Line;
            n.DrawString(mono, new Vector2(x, y), HeaderLine(c),
                         HorizontalAlignment.Left, w, Ui.Px(size), Ui.Faint);
            y += Ui.Px(6);
            Ui.Rule(n, x, y, w);
            y += Line;

            // As many rows as the panel has room for, remembered so that a
            // page-up moves by exactly one screenful at every board size.
            int rows = Math.Max(1, (int)((panel.End.Y - y - Ui.Px(30)) / Line));
            _rowsShown = rows;
            int top = Math.Clamp(_top, 0, Math.Max(0, _wide.Length - rows));
            top = Math.Clamp(top, _sel - rows + 1, _sel);
            // Written back, not just used: Show() has to guess a page size
            // before the first frame, and a `_top` that disagrees with what was
            // drawn makes the next arrow key jump.
            _top = top = Math.Max(0, top);
            string[] table_ = c.Author < 0 ? _narrow : _wide;
            int shown = Math.Min(rows, table_.Length - top);

            // **Two hairlines, and they are what make it a table.**  Without
            // them the six score cells are one run of numbers -- `103 46 Duck
            // 125 49 mz` -- and the reader has to count from the header to find
            // where the world's three end and yours begin.  They are drawn
            // before the rows so the selected row's raised band passes over
            // them rather than being cut by them, and they stop at the last row
            // rather than running to the panel's floor, because a rule below
            // the last row is a rule around nothing.
            float bottom = y - Line + 4 + shown * Line + 3;
            foreach (int at in new[] { c.BMoves - 1, c.Gt - 1 })
                n.DrawRect(new Rect2(Mathf.Round(x + chw * (at + 0.5f)), headTop,
                                     Mathf.Max(1, Ui.Px(1)), bottom - headTop),
                           Ui.Border);

            for (int i = top; i < Math.Min(table_.Length, top + rows); i++)
            {
                string row = table_[i];
                Color tint = DifCList[row[0] - '0'];
                // The band the selection is drawn in *is* the hit box -- one
                // rectangle, passed to the draw call and to Add in the same
                // breath, which is the property the whole hit list is for.
                var band = new Rect2(x - Ui.Px(7), y - Line + 4,
                                     w + 2 * Ui.Px(7), Line + 3);
                int at = i;
                if (_view.Chrome.Add(band, "row:" + i, () => Pick(at)) && i != _sel)
                    Ui.Hot(n, band, 5f);
                if (i == _sel)
                {
                    // DrawLevels fills the selected row (0x00404080); here it is
                    // a rounded band plus a rule down its left edge in the row's
                    // own difficulty colour -- which says *which* row is
                    // selected and keeps saying what rank it is.
                    n.DrawStyleBox(Ui.Box(Ui.Raised, Ui.BorderLit, 5f, 1f),
                                   new Rect2(x - Ui.Px(7), y - Line + 4,
                                             w + 2 * Ui.Px(7), Line + 3));
                    n.DrawRect(new Rect2(x - Ui.Px(7), y - Line + 4,
                                         Mathf.Max(2, Ui.Px(2)), Line + 3), tint);
                }
                // temps + 1: the digit is the colour key, not text.
                n.DrawString(mono, new Vector2(x, y), row.Substring(1),
                             HorizontalAlignment.Left, w, Ui.Px(size),
                             i == _sel ? tint : tint * new Color(1, 1, 1, 0.78f));
                y += Line;
            }

            Footer(n, panel, x, w);
        }

        private static void Footer(Node2D n, Rect2 panel, float x, float w)
            => Ui.Write(n, new Vector2(x, panel.End.Y - Ui.Px(13)),
                        "↑↓ or wheel picks · Enter or a second click loads · "
                        + "any other key or a click outside closes "
                        + "·  ** and > beat the posted best",
                        11, Ui.Faint, w);
    }
}
