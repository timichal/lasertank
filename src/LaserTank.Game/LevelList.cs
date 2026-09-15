// Phase 5, step 4: the level picker and the two high-score lists -- one table
// since step 8, and a table you can *search* since step 11.
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
// ---------------------------------------------------------------------------
// STEP 11: the five things a 2,030-row table needed and did not have
// ---------------------------------------------------------------------------
//
// Step 8 built the table and step 9 gave it a pointer.  What it still was, as a
// *list*, was one screenful of a two-thousand-row file with no way to narrow it,
// no way to see at a glance which rows were done, no way down it except holding
// a key or spinning a wheel, and a habit of closing itself whenever a finger
// slipped onto a letter.  Five changes, and four of them turn out to be the
// original's own behaviour rather than an invention:
//
//   1. **The columns have gutters.**  The two group hairlines were placed half a
//      character from the cells either side of them, so a four-character `who`
//      -- the commonest width in the corpus, `%4s` being the reachable width of
//      a six-byte field -- ran straight into the rule dividing it from your own
//      score.  The rules are floats in character units now and sit a character
//      and a half clear on both sides.  What paid for the width is the `>`
//      marker, below.
//
//   2. **Only Escape closes it.**  This is TransListKey (LTANK_D.C:87), read
//      rather than guessed: it answers Home / Up / Down / End / PgUp / PgDn and
//      `VK_ESCAPE`, and returns -2 -- *no action* -- for everything else.  The
//      port's "any other key closes" was a step-7 convenience that had no
//      warrant here and that a search field makes impossible anyway, since every
//      letter now has somewhere to go.  Space went with it: the original's Enter
//      is `WM_COMMAND` id 1 and space was never a second one.
//
//   3. **A filter bar, which is the Search sub-dialog inlined.**  `SearchBox`
//      (LTANK_D.C:197) is a modal child of the LoadBox behind the `&Filter`
//      button (ID_LOADLEV_03) with a substring field, a Title/Author radio pair,
//      a difficulty mask and an "only unsolved" checkbox.  All four are here, in
//      the panel itself rather than in a dialog over it, for the same reason
//      step 8 merged three dialogs into one table: a filter you cannot see while
//      you read the list is a filter you forget is on.  **The rules are the
//      C's**: the match is a case-insensitive substring of the name *or* the
//      author and never both (`mode` 1 or 2); the mask is the same five bits;
//      and an unrated level is promoted to 255 before the mask test
//      (`if (TempRecData.SDiff == 0) TempRecData.SDiff = 255;`, LTANK_D.C:414),
//      so it matches whatever is ticked rather than nothing.
//
//      Three departures, each deliberate.  **The field is always focused**, so a
//      letter filters rather than closing the panel -- which is the listbox's
//      own type-ahead generalised from a prefix to a substring, and it is what
//      change 2 frees the keyboard for.  **A digit query also matches the level
//      number**, which is ID_LOADLEV_02, the "or Direct Level Number Entry"
//      field the original put under its list; folding it into the one field is
//      cheaper than a second one and is how you reach level 1,840 of Tutor.
//      And **the filter survives the panel closing** but not the collection
//      changing: the original rebuilds the list unfiltered in WM_INITDIALOG
//      every time, which is right for a dialog you open to pick one level and
//      wrong for the only instrument this port has for traversing a collection.
//
//      The original's own bug here is *not* reproduced, and it is worth naming
//      so nobody re-finds it and thinks it was missed: the search branch never
//      resets `i` before its loop, so every row it lists is numbered from
//      wherever the unfiltered pass left the counter.  It is a display fault in
//      a dialog this port no longer draws, and the table's numbers come from the
//      level record.
//
//   4. **The mark column.**  Three ranks at the row's own left edge: `*` you
//      have solved it, `**` you matched the posted best exactly, `***` you beat
//      it.  This replaces the original's `**`-by-the-number and the `>` between
//      the two score groups, which were one bit of information (BHS,
//      LTANK_D.C:939) drawn twice.  **The predicate is narrower than BHS on
//      purpose**: `LevelFile.Beats` counts an *absent* posted best as beaten,
//      which is right for the original's marker and would put three stars on
//      every solved row of a collection with no `.ghs` beside it.  So the third
//      star wants a posted best to exist.  `Beats` itself is untouched --
//      BuildRows is the transliteration and still calls it.
//
//   5. **A scrollbar, and it is the one target in this interface that is
//      dragged.**  Everything step 9 registered is a click; the thumb follows
//      the pointer, so BoardView keeps a `_dragList` latch and feeds motion to
//      DragTo.  It moves `_top` and pulls the cursor into the viewport after it,
//      which is the mirror image of what Move does -- the panel still has one
//      position and not two, for the reason in Scroll's comment.
//
// What is still not here: `Backspace[]`'s ten-level history (118), and a grab
// offset on the thumb (it centres on the pointer, which is also what a click on
// the bare track should do).
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
        /// The caption, and a small monument to how the i18n audit went.
        ///
        /// This was `ID_LOADLEV_00`, the caption of the dialog command 106
        /// opens -- which is the dialog this panel is, since `L` is what opens
        /// it.  Step 8 merged the two high-score lists in here as well, which
        /// left `ID_HIGHLIST_00` and `ID_GHIGHLIST_00` -- their captions -- read
        /// by nothing, and that was recorded as the first pair the audit's rule
        /// had caught: *a key is read by a widget or it goes*.  Run against the
        /// whole file the rule caught 132 of 155, so what went was the file.
        /// The caption is the port's own key now and the panel is called what it
        /// is rather than what the menu item that opened it was called.
        private const string TitleKey = "levels.title";

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
        /// Both are indexed by *level*, not by visible row: the filter is an
        /// order, not a rebuild.
        private string[] _wide = Array.Empty<string>();
        private string[] _narrow = Array.Empty<string>();
        /// The mark column's rank per level: 0 none, 1 solved, 2 par, 3 beaten.
        private int[] _marks = Array.Empty<int>();
        /// The level indices the filter admits, in file order.  `_sel` and
        /// `_top` are positions *in here*, which is the whole of what makes a
        /// filtered list work: everything else stays indexed by level.
        private int[] _order = Array.Empty<int>();
        private int _sel, _top;
        private string _lvlName = "";
        private int _solved;

        public bool Open { get; private set; }

        /// **The panel stops the clock.**  The original's own split -- 106 stops
        /// it, 113 and 906 do not -- is in this file's header along with why the
        /// merge could not keep both.
        public bool StopsClock => true;
        /// The level the player chose, or 0 -- BoardView takes and clears it,
        /// which is `EndDialog(Dialog, i + 100)` and the `if (i > 100)` that
        /// meets it (LTANK.C:910).
        private int Chosen { get; set; }

        /// **The answer is read once.**  `EndDialog`'s return value reaches the
        /// one `DialogBox` call that asked for it and then it is gone; here the
        /// field outlives the panel, and the mouse path reads it after *any*
        /// chrome click rather than after this panel's own -- so a level picked
        /// an hour ago would be re-loaded by the next click on an unrelated
        /// chip, and the game would jump back to it.  Taking it clears it, so
        /// one commit loads one level.
        public int TakeChosen() { int c = Chosen; Chosen = 0; return c; }

        public LevelList(BoardView view) { _view = view; }

        /// How many rows the filter admits, and how many there are in all --
        /// the pair `--type` logs, because a count means nothing without the
        /// number it is a fraction of.
        public int Count => _order.Length;
        public int Total => _levels.Length;
        /// The query as it stands, upper-cased, for the same log.
        public string Query => _q.Text;

        // ---- the filter -----------------------------------------------------
        //
        // SearchRec (LTANK.H), which is a global in the original and so survives
        // the dialog -- the difference being that the original rebuilds its list
        // unfiltered every time the dialog opens, and this one does not.  See
        // the header, change 3.

        /// SearchRec.data, upper-cased as `strupr(SearchRec.data)` leaves it.
        /// A TextField since step 14 -- the box, the caret, the length cap and
        /// the character test are one widget now, shared with the editor's
        /// three fields and the name panel's one.
        private readonly TextField _q = new TextField(QueryMax, upper: true);
        /// SearchRec.mode: 1 is the title, 2 is the author.  One or the other,
        /// never both -- the original's two controls are a radio pair.
        private bool _byAuthor;
        /// SearchRec.Diff, the five bits 1/2/4/8/16.  All five is the unchecked
        /// "Filter by Difficulty" box, which the C spells 255.
        private int _diff = All;
        /// SearchRec.SkipComp.
        private bool _unsolvedOnly;
        private const int All = 1 | 2 | 4 | 8 | 16;
        /// GetWindowText(..., SearchRec.data, 60).
        private const int QueryMax = 60;

        /// Which collection the filter was last applied to, so that opening a
        /// different one starts clean.  A name filter is about the rows in front
        /// of you and means nothing once they are someone else's rows.
        private string _forPath = "";

        /// Whether anything is narrowing the list, which is what the caption
        /// has to say before it prints a count.  `_byAuthor` is not in here:
        /// it picks *which* field an empty query would be matched against and
        /// admits every row on its own, so tabbing the radio pair over would
        /// otherwise turn the caption into `2030 of 2030` with nothing filtered.
        public bool Filtering =>
            !_q.Empty || _diff != All || _unsolvedOnly;

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
            _marks = BuildMarks(_levels, _mine, _posted);
            _wide = BuildTable(_levels, _mine, _posted, Wide);
            _narrow = BuildTable(_levels, _mine, _posted, Narrow);

            // The count the collection picker shows beside each collection, for
            // the same reason it shows it: it is the only number in this game
            // that answers "where was I".
            _solved = 0;
            foreach (int m in _marks) if (m > 0) _solved++;

            if (!string.Equals(_forPath, lvlPath, StringComparison.OrdinalIgnoreCase))
            {
                _q.Set("");
                _byAuthor = false;
                _diff = All;
                _unsolvedOnly = false;
                _forPath = lvlPath;
            }

            // LB_SETCURSEL on CurLevel - 1 (LTANK_D.C:359) -- through the filter,
            // because with one on there may be no row for the level you are on.
            Apply(current);
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

        /// The mark column, one rank per level.
        ///
        /// **Not `LevelFile.Beats`, and the difference is the point.**  Beats
        /// answers the original's BHS question -- "is this a high score worth
        /// posting" -- and a missing or blank posted best counts as beaten,
        /// because there is nothing there to lose to.  A *rating* cannot say
        /// that: three stars for beating a par that does not exist would put
        /// three stars on every solved row of any collection shipped without a
        /// `.ghs`, which is most of them.  So ranks 2 and 3 both require a
        /// posted best, and a solved level with nothing to compare against stops
        /// at one star.
        private static int[] BuildMarks(TLEVELINFO[] levels, THSREC[] mine,
                                        THSREC[] posted)
        {
            var marks = new int[levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                THSREC my = At(mine, i), best = At(posted, i);
                // The original's own test for "have I solved this" is
                // `moves > 0` and nothing else.
                if (my == null || my.Moves == 0) continue;
                marks[i] = 1;
                if (best == null || best.Moves == 0) continue;
                if (my.Moves < best.Moves
                    || (my.Moves == best.Moves && my.Shots < best.Shots))
                    marks[i] = 3;
                else if (my.Moves == best.Moves && my.Shots == best.Shots)
                    marks[i] = 2;
            }
            return marks;
        }

        /// `*`, `**`, `***` -- and "" for a level with no score record.
        private static string Stars(int rank) => rank switch
        {
            1 => "*", 2 => "**", 3 => "***", _ => "",
        };

        // ---- the filter, applied --------------------------------------------

        /// Rebuild `_order` from the four filter fields, and keep the cursor on
        /// the level it was on if that level survived.
        ///
        /// `keepLevel` is a *level number* (1-based) when the caller has one --
        /// Show passes CurLevel -- and 0 when it wants the current cursor kept,
        /// which is every filter keystroke.
        private void Apply(int keepLevel = 0)
        {
            // Which level the cursor is on now, so a narrowing filter does not
            // drop the player back at row 0 of a list they were halfway down.
            int want = keepLevel > 0 ? keepLevel
                     : _sel < _order.Length ? _levels[_order[_sel]].Number : 0;

            int qnum = 0;
            bool numeric = !_byAuthor && int.TryParse(_q.Text, out qnum) && qnum > 0;

            var ord = new List<int>(_levels.Length);
            for (int i = 0; i < _levels.Length; i++)
            {
                TLEVELINFO lv = _levels[i];
                // LTANK_D.C:414: an unrated level is promoted to 255 before the
                // mask test, so it passes whatever is ticked.  Without this line
                // every unrated level vanishes the moment any rank is unticked,
                // which is most of the community corpus.
                int sdiff = lv.SDiff == 0 ? 255 : lv.SDiff;
                if ((sdiff & _diff) == 0) continue;
                // SearchRec.SkipComp, which the C tests as `TempHSData.moves == 0`.
                if (_unsolvedOnly && _marks[i] > 0) continue;
                if (!_q.Empty)
                {
                    string hay = (_byAuthor ? lv.Author : lv.LName) ?? "";
                    bool hit = hay.ToUpperInvariant()
                                  .Contains(_q.Text, StringComparison.Ordinal);
                    // ID_LOADLEV_02, the direct level-number entry, folded into
                    // the one field: a query that is only digits also matches
                    // the number itself.
                    if (!hit && numeric && lv.Number == qnum) hit = true;
                    if (!hit) continue;
                }
                ord.Add(i);
            }
            _order = ord.ToArray();
            SelectLevel(want);
        }

        /// Put the cursor on level `number`, or on the nearest row the filter
        /// still admits -- the list is in file order, so "nearest" is the first
        /// row at or past it, and the last row when there is none.
        private void SelectLevel(int number)
        {
            _sel = 0;
            for (int i = 0; i < _order.Length; i++)
            {
                _sel = i;
                if (_levels[_order[i]].Number >= number) break;
            }
            _top = Math.Max(0, _sel - _rowsShown / 2);
        }

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
            public int Mark, Num, Name, Author, BMoves, BShots, BWho,
                       MMoves, MShots, MWho, Width;
            /// Where the two group hairlines go, in character units -- **floats,
            /// because a rule that divides two columns belongs between two
            /// character cells and not inside one.**  They used to be derived as
            /// `cell - 1` and drawn at `+ 0.5`, which put them half a glyph from
            /// the cells either side: the commonest `who` in the corpus is four
            /// characters wide and it touched the rule.
            public float Rule1, Rule2;
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
        ///
        /// **The three characters at the far left are the mark column**, and
        /// what paid for the gutters is the original's `**` beside the number
        /// and its `>` between the score groups: one bit drawn twice, now three
        /// ranks drawn once.  See the header, change 4.
        private static readonly Cells Wide = new()
        {
            Mark = 0, Num = 4, Name = 9, Author = 40,
            Rule1 = 61.5f, BMoves = 63, BShots = 69, BWho = 75,
            Rule2 = 80.5f, MMoves = 82, MShots = 88, MWho = 94, Width = 98,
        };

        private static readonly Cells Narrow = new()
        {
            Mark = 0, Num = 4, Name = 9, Author = -1,
            Rule1 = 40.5f, BMoves = 42, BShots = 48, BWho = 54,
            Rule2 = 59.5f, MMoves = 61, MShots = 67, MWho = 73, Width = 77,
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
        ///
        /// **The mark column is left blank here and drawn separately**, because
        /// it is not in the row's colour: the row is tinted by difficulty and
        /// the marks are a second channel, so setting them in the rank's colour
        /// would be two meanings in one hue.
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

                string s = Cols(
                    (c.Num, $"{lv.Number,4}"),
                    (c.Name, Pad(lv.LName, 30)),
                    (c.Author, c.Author < 0 ? null : Pad(lv.Author, 20)),
                    (c.BMoves, haveBest ? $"{best.Moves,5}" : null),
                    (c.BShots, haveBest ? $"{best.Shots,5}" : null),
                    (c.BWho, haveBest ? best.Name : null),
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
        /// **These labels are placed by character column, so they are capped.**
        /// Every other string in this interface is measured and the layout moves
        /// around it; these sit over cells whose positions come out of the
        /// original's own printf widths, so a label that is one character too
        /// long does not reflow anything -- it lands on the next column's
        /// numbers.  strings_check.py holds the eleven files to the widths the
        /// Cells table leaves: 30 for the name, 20 for the author, 5 each for
        /// moves / shots / who, 18 and 16 for the two group heads.
        private static string HeaderLine(Cells c, Strings L) => Cols(
            (c.Num, L["levels.colNumber"].PadLeft(4)), (c.Name, L["levels.colName"]),
            (c.Author, c.Author < 0 ? null : L["levels.colAuthor"]),
            (c.BMoves, L["levels.colMoves"]), (c.BShots, L["levels.colShots"]),
            (c.BWho, L["levels.colWho"]),
            (c.MMoves, L["levels.colMoves"]), (c.MShots, L["levels.colShots"]),
            (c.MWho, L["levels.colWho"]));

        private static string GroupLine(Cells c, Strings L) => Cols(
            (c.BMoves, L["levels.groupBest"]), (c.MMoves, L["levels.groupYours"]));

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
        /// widths they carry are the widths the table uses, and **`Beats` is
        /// still the BHS predicate here**: step 11's mark column wants a
        /// narrower one (see BuildMarks) and did not get it by changing this.
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

        /// **TransListKey (LTANK_D.C:87), read rather than guessed.**  Home, Up,
        /// Down, End, PgUp, PgDn and Escape are the keys the original's listbox
        /// answers; everything else returns -2, which is *no action*.  So a
        /// letter does not close this panel, which is what leaves the alphabet
        /// free for the filter field -- and that is the trade: the port's step-7
        /// "any other key closes" was a convenience with no warrant in the C,
        /// and a panel with a text field in it cannot have both.
        ///
        /// PgUp and PgDn move by a screenful rather than by the original's flat
        /// ten, because the panel measures its own height and a page that is not
        /// the visible page is worse than no page key at all.
        public bool Key(InputEventKey k)
        {
            Key code = k.Keycode;

            // The filter's own chords.  Ctrl, because every unmodified printable
            // key is the field now -- and digits are exactly what a player types
            // to reach a level number.
            if (k.CtrlPressed)
            {
                switch (code)
                {
                    case Godot.Key.Key0: _diff = All; Apply(); break;
                    case Godot.Key.Key1: Rank(1); break;
                    case Godot.Key.Key2: Rank(2); break;
                    case Godot.Key.Key3: Rank(4); break;
                    case Godot.Key.Key4: Rank(8); break;
                    case Godot.Key.Key5: Rank(16); break;
                    case Godot.Key.U: _unsolvedOnly = !_unsolvedOnly; Apply(); break;
                }
                // A chord this panel does not use is still eaten: it is modal.
                return true;
            }

            switch (code)
            {
                case Godot.Key.Escape: Close(); break;
                case Godot.Key.Up: Move(-1); break;
                case Godot.Key.Down: Move(1); break;
                case Godot.Key.Pageup: Move(-_rowsShown); break;
                case Godot.Key.Pagedown: Move(_rowsShown); break;
                case Godot.Key.Home: Move(-_order.Length); break;
                case Godot.Key.End: Move(_order.Length); break;
                // SearchRec.mode, the original's Title/Author radio pair.  Tab
                // because it is the one navigation key the field does not want,
                // and because the editor's three fields already spend Tab on
                // moving between controls.
                case Godot.Key.Tab: _byAuthor = !_byAuthor; Apply(); break;
                case Godot.Key.Enter:
                case Godot.Key.KpEnter:
                    Commit();
                    break;
                default:
                    // The field, which is Backspace and every printable key.
                    // This is the listbox's own type-ahead widened from a
                    // prefix to a substring -- and `strupr` is why the query is
                    // stored upper-cased (TextField.Upper).
                    if (_q.Key(k)) Apply();
                    break;
            }
            return true;
        }

        /// Toggle one bit of SearchRec.Diff, and read "none of them" as "all of
        /// them" -- an empty mask is a list with nothing in it, which is never
        /// what the keystroke meant.
        private void Rank(int bit)
        {
            _diff ^= bit;
            if (_diff == 0) _diff = All;
            Apply();
        }

        private void Commit()
        {
            if (_sel < _order.Length) Chosen = _levels[_order[_sel]].Number;
            Close();
        }

        private void Move(int d)
        {
            if (_order.Length == 0) return;
            _sel = Math.Clamp(_sel + d, 0, _order.Length - 1);
            _top = Math.Clamp(_top, _sel - _rowsShown + 1, _sel);
            _top = Math.Clamp(_top, 0, Math.Max(0, _order.Length - _rowsShown));
        }

        /// The wheel.  It moves the *selection*, not a second scroll position:
        /// Draw clamps the viewport to the cursor, so a list that scrolled away
        /// from its own cursor would snap back on the next arrow key.
        public void Scroll(int d) => Move(d);

        /// The scrollbar, which is the same argument run the other way: it sets
        /// the viewport and then pulls the cursor into it, so there is still one
        /// position and not two.
        private void ScrollTo(int top)
        {
            if (_order.Length == 0) return;
            _top = Math.Clamp(top, 0, Math.Max(0, _order.Length - _rowsShown));
            _sel = Math.Clamp(_sel, _top,
                              Math.Min(_order.Length - 1, _top + _rowsShown - 1));
        }

        /// A pointer at window y, on the track.  **The one target in this
        /// interface that is dragged** -- BoardView latches the press and feeds
        /// every motion here until the button comes up.  The thumb centres on
        /// the pointer rather than keeping a grab offset, which makes a click on
        /// the bare track a jump to that place and a press-and-move a drag, out
        /// of one rule.
        public void DragTo(float y)
        {
            int n = _order.Length;
            if (_trackH <= 0f || n <= _rowsShown) return;
            float span = _trackH - _thumbH;
            if (span <= 0f) return;
            float f = Mathf.Clamp((y - _thumbH / 2f - _trackY) / span, 0f, 1f);
            ScrollTo(Mathf.RoundToInt(f * (n - _rowsShown)));
        }

        /// A click on a row.  **The first lands on it and the second loads it**
        /// -- a double-click that does not have to be fast.  Godot's own
        /// DoubleClick would do on a desktop and be a coin toss on a phone, and
        /// the reason a list needs two taps at all is that there is no hover on
        /// a touch screen: the selection *is* the preview here, and a 2,030-row
        /// table is not a place to load something on the first tap.
        private void Pick(int i)
        {
            if (_order.Length == 0) return;
            if (i == _sel) { Commit(); return; }
            _sel = Math.Clamp(i, 0, _order.Length - 1);
        }

        // ---- drawing --------------------------------------------------------
        /// The row pitch, which follows the UI scale like every other length in
        /// the redesign -- and which the paging arithmetic reads, so a bigger
        /// window really does page by a bigger screenful rather than scrolling
        /// the same fifteen rows faster.
        private static int Line => Ui.Px(16);

        /// The scrollbar's column, taken out of the table's width rather than
        /// laid over it: a track that overlapped the rows would steal the right
        /// end of every one of them from the pointer.
        private static int Gutter => Ui.Px(20);

        /// The track, remembered from the last Draw so that DragTo can map a
        /// window y back to a row -- the same one-rectangle rule the hit list is
        /// built on, applied to a thing that is dragged instead of clicked.
        private float _trackY, _trackH, _thumbH;

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
        /// the last column is a drifting column rule that crosses the text it is
        /// supposed to divide.  Dividing a long run by its length is the advance
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
            // A click outside closes, which is the pointer's form of Escape --
            // and since step 11 it is the *only* form of "not this one" the
            // mouse has, because every letter belongs to the filter now.
            // Registered before the panel because the hit list is walked
            // backwards -- see Hits.
            _view.Chrome.Add(host, "scrim", Close);

            float pad = Ui.Px(18);
            (Cells c, float size, float table) = Fit(
                host.Size.X - Ui.Px(40) - 2 * pad - Gutter, mono);
            float w = Mathf.Min(Mathf.Max(Ui.Px(460), table + 2 * pad + Gutter),
                                host.Size.X - Ui.Px(40));
            float h = Mathf.Min(Ui.Px(700), host.Size.Y - Ui.Px(40));
            var panel = new Rect2(
                Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                Mathf.Round(host.Position.Y + (host.Size.Y - h) / 2f), w, h);
            Ui.Dialog(n, panel);
            // A miss inside the panel is not an answer, so it does not close it.
            _view.Chrome.Swallow(panel);

            float x = panel.Position.X + pad;
            w = panel.Size.X - 2 * pad;
            float y = panel.Position.Y + pad + Ui.Px(11);

            Strings L = _view.Strings;
            Ui.Caps(n, new Vector2(x, y), L[TitleKey], Ui.Text, 12);
            // What file this is, how many levels are in it, and how many of them
            // you have solved -- which is the one number in this game that
            // answers "where was I", and the reason the collection picker shows
            // it too.
            Rect2 close = Ui.CloseRect(panel, pad);
            Ui.CloseX(n, close, _view.Chrome.Add(Ui.Touch(close), "close", Close));
            // **Measured, not reserved.**  This was a flat `Ui.Px(280)` of
            // reserved width, which was enough for the proportional UI sans step
            // 7 set the chrome in and is not enough for the monospace step 10
            // replaced it with -- the caption came out as `... 21 solve`, a
            // clip that reads as a typo rather than as a truncation.  A caption
            // that names a file has no width a layout can assume anyway, so it
            // asks for what it needs and sheds a clause when the answer is more
            // than the line between the title and the close button.
            float capL = x + Ui.CapsWidth(L[TitleKey], 12) + Ui.Px(20);
            float capR = close.Position.X - Ui.Px(10);
            // With a filter on, the first number is what you can see and the
            // second is what is in the file: a bare `12 levels` under a filter
            // would be a lie about the collection.
            string count = Filtering
                ? L.F("levels.countFiltered", _order.Length, _levels.Length)
                : L.F("levels.count", _levels.Length);
            string solved = L.F("levels.solved", _solved);
            string cap = $"{_lvlName}  ·  {count}  ·  {solved}";
            if (Ui.Width(cap, 11) > capR - capL)
                cap = $"{count}  ·  {solved}";
            if (Ui.Width(cap, 11) > capR - capL)
                cap = $"{_solved}/{_levels.Length}";
            Ui.Write(n, new Vector2(capL, y), cap, 11, Ui.Faint, capR - capL,
                     HorizontalAlignment.Right);
            y += Ui.Px(12);
            Ui.Rule(n, x, y, w);
            y += Ui.Px(20);

            y = FilterBar(n, L, x, y, w);
            Ui.Rule(n, x, y, w);
            y += Ui.Px(18);

            if (_levels.Length == 0)
            {
                Ui.Write(n, new Vector2(x, y + Ui.Px(10)),
                         L["levels.unreadable"], 12, Ui.Bad, w);
                Footer(n, L, panel, x, w);
                return;
            }

            // ---- the two header lines, at the table's own columns.
            float tw = w - Gutter;
            float chw = Advance(mono, size);
            float headTop = y - Line + 4;
            // **At the rows' own size, not a size down.**  The header cells are
            // placed by character column and a column is only a position if
            // every line in the table advances by the same glyph -- set one
            // point smaller, as these were on the first pass, "moves" sits a
            // finger to the left of the moves it names and the whole table
            // reads as broken.  Faint rather than small is what makes them
            // headers.
            n.DrawString(mono, new Vector2(x, y), GroupLine(c, L), HorizontalAlignment.Left,
                         tw, Ui.Px(size), Ui.Faint * new Color(1, 1, 1, 0.7f));
            y += Line;
            n.DrawString(mono, new Vector2(x, y), HeaderLine(c, L),
                         HorizontalAlignment.Left, tw, Ui.Px(size), Ui.Faint);
            y += Ui.Px(6);
            Ui.Rule(n, x, y, w);
            y += Line;

            // As many rows as the panel has room for, remembered so that a
            // page-up moves by exactly one screenful at every board size.
            int rows = Math.Max(1, (int)((panel.End.Y - y - Ui.Px(30)) / Line));
            _rowsShown = rows;
            float rowsTop = y - Line + 4;

            if (_order.Length == 0)
            {
                // A filter that matches nothing has to say so: an empty panel
                // under a bar full of switches reads as a broken collection.
                Ui.Write(n, new Vector2(x, y + Ui.Px(4)),
                         L["levels.noMatch"], 12, Ui.Dim, tw);
                Scrollbar(n, panel.End.X - pad - Gutter + Ui.Px(6), rowsTop,
                          rows * Line + 3, rows);
                Footer(n, L, panel, x, w);
                return;
            }

            int top = Math.Clamp(_top, 0, Math.Max(0, _order.Length - rows));
            top = Math.Clamp(top, _sel - rows + 1, _sel);
            // Written back, not just used: Show() has to guess a page size
            // before the first frame, and a `_top` that disagrees with what was
            // drawn makes the next arrow key jump.
            _top = top = Math.Max(0, top);
            string[] table_ = c.Author < 0 ? _narrow : _wide;
            int shown = Math.Min(rows, _order.Length - top);

            // **Two hairlines, and they are what make it a table.**  Without
            // them the six score cells are one run of numbers -- `103 46 Duck
            // 125 49 mz` -- and the reader has to count from the header to find
            // where the world's three end and yours begin.  They are drawn
            // before the rows so the selected row's raised band passes over
            // them rather than being cut by them, and they stop at the last row
            // rather than running to the panel's floor, because a rule below
            // the last row is a rule around nothing.
            float bottom = rowsTop + shown * Line + 3;
            foreach (float at in new[] { c.Rule1, c.Rule2 })
                n.DrawRect(new Rect2(Mathf.Round(x + chw * at), headTop,
                                     Mathf.Max(1, Ui.Px(1)), bottom - headTop),
                           Ui.Border);

            for (int i = top; i < Math.Min(_order.Length, top + rows); i++)
            {
                int lvi = _order[i];
                string row = table_[lvi];
                Color tint = DifCList[row[0] - '0'];
                // The band the selection is drawn in *is* the hit box -- one
                // rectangle, passed to the draw call and to Add in the same
                // breath, which is the property the whole hit list is for.
                // It stops at the gutter so the scrollbar has the right-hand
                // end of the panel to itself.
                var band = new Rect2(x - Ui.Px(7), y - Line + 4,
                                     tw + Ui.Px(7), Line + 3);
                int at = i;
                if (_view.Chrome.Add(band, "row:" + i, () => Pick(at)) && i != _sel)
                    Ui.Hot(n, band, 5f);
                if (i == _sel)
                {
                    // DrawLevels fills the selected row (0x00404080); here it is
                    // a rounded band plus a rule down its left edge in the row's
                    // own difficulty colour -- which says *which* row is
                    // selected and keeps saying what rank it is.
                    n.DrawStyleBox(Ui.Box(Ui.Raised, Ui.BorderLit, 5f, 1f), band);
                    n.DrawRect(new Rect2(band.Position.X, band.Position.Y,
                                         Mathf.Max(2, Ui.Px(2)), band.Size.Y), tint);
                }
                // temps + 1: the digit is the colour key, not text.
                n.DrawString(mono, new Vector2(x, y), row.Substring(1),
                             HorizontalAlignment.Left, tw, Ui.Px(size),
                             i == _sel ? tint : tint * new Color(1, 1, 1, 0.78f));
                // The mark column, in the chrome's own accent rather than in the
                // row's difficulty tint: the rank is one channel and the marks
                // are another, and three stars that changed colour with the rank
                // would read as a sixth rank.  A star is worth more the more of
                // them there are, so the alpha climbs with the count.
                if (_marks[lvi] > 0)
                    n.DrawString(mono, new Vector2(x + chw * c.Mark, y),
                                 Stars(_marks[lvi]), HorizontalAlignment.Left,
                                 chw * 4, Ui.Px(size),
                                 Ui.Accent with { A = 0.45f + 0.185f * _marks[lvi] });
                y += Line;
            }

            Scrollbar(n, panel.End.X - pad - Gutter + Ui.Px(6), rowsTop,
                      rows * Line + 3, rows);
            Footer(n, L, panel, x, w);
        }

        // ---- the filter bar -------------------------------------------------

        /// The Search dialog's four controls, on two lines inside the panel.
        /// -> the y its closing rule belongs at.
        ///
        /// **The labels used to be the original Search dialog's**, and this is
        /// where the audit that replaced them can be read off.  Nine of its keys
        /// had found a widget here (ID_SEARCH_01, _03, _04, _08, _10-_14) and six
        /// never would: _00 is a caption and there is no dialog, _02 is a group
        /// box, _05 and _06 are Cancel and Ok and this bar commits as you type,
        /// _09 is the "Filter by Difficulty" master checkbox that greys the five
        /// rank buttons -- which a row of chips you can simply click does not
        /// need -- and ID_LOADLEV_03 is the button that opened the dialog.  Nine
        /// of fifteen is roughly the ratio the whole file ran at, which is what
        /// decided next-steps item 1 the way it went: the keys are the port's own
        /// now, and the widget that draws one is what names it.
        private float FilterBar(Node2D n, Strings L, float x, float y, float w)
        {
            string byTitle = L["levels.byName"];
            string byAuthor = L["levels.byAuthor"];

            // The two mode chips sit on the field's own line, hard right.
            float tgW = ChipWidth(byTitle) + ChipWidth(byAuthor) + Ui.Px(6);
            float fieldH = Ui.Px(26);
            var field = new Rect2(x, y, Mathf.Max(Ui.Px(120), w - tgW - Ui.Px(10)),
                                  fieldH);
            // A swallow, not a button: the field is always focused, so there is
            // nothing for a click on it to do -- and a live target here would be
            // a box that has to clear the touch floor for no behaviour.
            _view.Chrome.Swallow(field, "search");

            // A placeholder rather than a label beside the box: there is no room
            // for one, and an empty field that says what it wants is the same
            // instruction in the space there is.  **The border is lit by the
            // query, not by the focus** -- this field always has the caret, so
            // the thing worth saying about it is whether a filter is on.
            _q.Draw(n, field, L["levels.search"], caret: true,
                    border: _q.Empty ? Ui.Border : Ui.Accent);

            float cx = field.End.X + Ui.Px(10);
            float chipY = y + (fieldH - ChipHeight()) / 2f;
            cx = Chip(n, cx, chipY, byTitle, !_byAuthor, Ui.Cyan, "by:title",
                      () => { _byAuthor = false; Apply(); });
            Chip(n, cx, chipY, byAuthor, _byAuthor, Ui.Cyan, "by:author",
                 () => { _byAuthor = true; Apply(); });

            // ---- line two: the mask and the skip.
            //
            // **Shed to initials rather than clipped**, the same rule the
            // caption and the footer follow: six localised labels are six
            // strings whose width nobody here decides, and a row of chips that
            // runs off the panel is worse than a row of letters that does not.
            y += Ui.Px(42);
            // The five ranks are the same five words the level block and the
            // editor's chip draw, out of TLEVELINFO.RankKeys -- one rank, one
            // key, three widgets.
            string[] ranks = new string[5];
            for (int i = 0; i < 5; i++) ranks[i] = L[TLEVELINFO.RankKeys[i + 1]];
            string skip = L["levels.unsolved"];
            float need = ChipWidth(skip) + Ui.Px(16);
            foreach (string r in ranks) need += ChipWidth(r) + Ui.Px(6);
            if (need > w)
            {
                for (int i = 0; i < ranks.Length; i++) ranks[i] = First(ranks[i]);
                skip = First(skip);
            }

            cx = x;
            for (int i = 0; i < 5; i++)
            {
                int bit = 1 << i;
                // Ui.Diff is indexed by the difficulty *digit*, and the digit is
                // the bit's position plus one -- 1/2/4/8/16 -> 1..5.
                cx = Chip(n, cx, y, ranks[i], (_diff & bit) != 0, Ui.Diff[i + 1],
                          "diff:" + (i + 1), () => Rank(bit));
            }
            cx += Ui.Px(10);
            Chip(n, cx, y, skip, _unsolvedOnly, Ui.Good, "unsolved",
                 () => { _unsolvedOnly = !_unsolvedOnly; Apply(); });

            return y + ChipHeight() + Ui.Px(12);
        }

        /// The first character of a label, for the compact row.  Upper-cased by
        /// Ui.Caps anyway; taken here so the width measurement sees what will be
        /// drawn.
        private static string First(string s)
            => string.IsNullOrEmpty(s) ? "?" : s.Substring(0, 1).ToUpperInvariant();

        private const float ChipSize = 10f;

        private static float ChipWidth(string text)
            => Ui.CapsWidth(text, ChipSize) + 2 * Ui.Px(7);

        private static float ChipHeight() => Ui.Px(ChipSize) + Ui.Px(8);

        /// One switch in the filter bar: a square outlined tag, lit when it is
        /// on, in the colour of the thing it names.  Drawn as Ui.Pill is drawn
        /// -- the chrome has one tag shape and this is it -- but registered as a
        /// button, which is what Pill has no way to be.
        ///
        /// -> the x past its right edge, so a row of them is a fold.
        private float Chip(Node2D n, float x, float y, string text, bool on,
                           Color c, string name, Action act)
        {
            float h = ChipHeight();
            var r = new Rect2(x, y, ChipWidth(text), h);
            // **The hit box is grown and the drawing is not** -- an 18 px tag is
            // a tag a thumb misses.  See Ui.Touch.
            bool hot = _view.Chrome.Add(Ui.Touch(r), name, act);
            n.DrawStyleBox(Ui.Box(on ? c with { A = 0.15f }
                                     : hot ? Ui.Raised : new Color(0, 0, 0, 0),
                                  on ? c with { A = 0.70f }
                                     : hot ? Ui.BorderLit : Ui.Border, 2f, 1f), r);
            Ui.Caps(n, new Vector2(x + Ui.Px(7),
                                   y + h - Ui.Px(ChipSize) * 0.30f - Ui.Px(3)),
                    text, on ? c : Ui.Faint, ChipSize);
            return r.End.X + Ui.Px(6);
        }

        // ---- the scrollbar --------------------------------------------------

        /// A track and a thumb down the gutter the table left for them.
        ///
        /// **It is always drawn, even when everything fits**, because a bar that
        /// appears and disappears moves the table under the pointer; when there
        /// is nothing to scroll the thumb simply fills the track, which says so.
        private void Scrollbar(Node2D n, float x, float y, float h, int rows)
        {
            _trackY = y;
            _trackH = h;
            int total = Math.Max(1, _order.Length);
            _thumbH = Mathf.Max(Ui.Px(26),
                                h * Mathf.Clamp(rows / (float)total, 0f, 1f));
            _thumbH = Mathf.Min(_thumbH, h);
            float span = Mathf.Max(0f, h - _thumbH);
            float at = total <= rows ? 0f : _top / (float)(total - rows);
            float ty = y + span * Mathf.Clamp(at, 0f, 1f);

            float wTrack = Ui.Px(8);
            var track = new Rect2(x, y, wTrack, h);
            // One target for the whole track: the press positions the thumb and
            // latches the drag, and both go through DragTo, so there is a single
            // rule for "the thumb is where the pointer is".  The action is empty
            // because the pointer's *position* is what matters and Hits carries
            // only the fact of the click -- BoardView calls DragTo on the press
            // and on every motion after it.  It has to be live all the same, or
            // the press would fall through to the scrim and close the panel.
            bool hot = _view.Chrome.Add(Ui.Touch(track), "scroll", () => { });
            n.DrawStyleBox(Ui.Box(Ui.Bg, Ui.Border, 4f, 1f), track);
            // The thumb is filled a step brighter than a raised surface would
            // be: it is the only thing in this panel that has to be findable
            // *before* the pointer is on it, and Raised on Bg was a difference
            // a screenshot could not show.
            n.DrawStyleBox(Ui.Box(hot ? Ui.BorderLit : Ui.Border,
                                  hot ? Ui.Accent with { A = 0.6f } : Ui.BorderLit,
                                  4f, 1f),
                           new Rect2(x, ty, wTrack, _thumbH));
        }

        /// The footer legend, **shed a clause at a time rather than clipped**.
        ///
        /// Same cause as the caption above: in the monospace the full line is
        /// wider than the panel's inside, and Godot's `DrawString` answers an
        /// overrun by cutting mid-word -- which lost the marker legend that is
        /// the only thing on screen explaining the stars in the rows.  So the
        /// clauses are ranked instead.  The marker legend is last to go because
        /// nothing else documents it; the chords go first because the chips they
        /// name are on screen and clickable.
        private static void Footer(Node2D n, Strings L, Rect2 panel, float x, float w)
        {
            string marks = L["levels.marks"];
            string[] forms =
            {
                L["levels.footerFull"] + "  " + marks,
                L["levels.footerMid"] + "  " + marks,
                L["levels.footerShort"] + "  " + marks,
                marks,
            };
            string s = forms[^1];
            foreach (string f in forms)
                if (Ui.Width(f, 11) <= w) { s = f; break; }
            Ui.Write(n, new Vector2(x, panel.End.Y - Ui.Px(13)), s, 11, Ui.Faint, w);
        }
    }
}
