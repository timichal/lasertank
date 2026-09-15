// Command 108, "Open Data File" (LTANK.C:924) -- the collection picker.
//
// The original's is a native file dialog: `OFN.Flags = OFN_HIDEREADONLY |
// OFN_FILEMUSTEXIST; if (GetOpenFileName(&OFN)) ...`, filtered on `*.LVL` with
// txt002 as the filter's label.  There is no file dialog in this port, and the
// content it would be browsing is *in the repo* -- 23 collections under
// data/levels/ and data/quirks/ -- so this lists those instead.  That is a UI
// decision and therefore written down rather than locked down: what the
// original did is above, and what is lost by not doing it is the ability to
// open a .lvl from anywhere on the disk, which `--levels` still does.
//
// The panel is LevelList's shape deliberately -- a full-board list of
// monospace rows, up/down/PgUp/PgDn, Enter loads, any other key closes -- so
// the two pickers in this game read as one thing.  What it is *not* is a
// fourth ListMode: LevelList's rows are the original's own sprintf formats and
// tools/list_check.py diffs them against Python byte for byte, and these rows
// have no original to be diffed against.  Keeping them out of that class keeps
// that gate meaning what it says.
//
// **Only Escape closes it, which is TransListKey (LTANK_D.C:87).**  That
// function answers Home / Up / Down / End / PgUp / PgDn and `VK_ESCAPE` and
// returns -2 -- *no action* -- for everything else, and step 11 read it and
// gave the level list that rule.  This panel kept "any other key closes" for
// one step longer on the argument that it has no filter field for a letter to
// fall into, which was true and beside the point: **the two pickers are one
// panel to look at, so they have to be one panel to use.**  A hand that has
// learned `O`, a glance, `Esc` should not discover that here the glance's
// stray keystroke took the panel away instead.  Enter and a second click still
// open; everything else is swallowed and does nothing.
//
// **The case trap is real and is why nothing here uses a `*.lvl` pattern.**
// The corpus mixes `.lvl` and `.LVL` -- `Tutor.LVL`, `Game-Objects-in-LT.LVL`,
// `Rotary Mirrors-Challenge.LVL` -- and Directory.GetFiles' pattern is only
// case-insensitive on Windows, so a pattern match would silently lose four
// collections on Linux.  Same rule as Paths.LtgFiles, for the same reason.
using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// One .lvl on offer.  `Solved` is the count of records in the player's own
    /// .hs with moves > 0, which is what makes the list worth reading twice --
    /// it is the only place in this port that answers "where was I".
    public sealed class Collection
    {
        public string Path = "";        // absolute
        public string Label = "";       // the file's stem, which is how the game names it
        public string Dir = "";         // the directory, relative to the repo root, with '/'
        public int Levels;
        public int Solved;
    }

    public sealed class CollectionList
    {
        private readonly BoardView _view;
        private Collection[] _all = Array.Empty<Collection>();
        private string[] _rows = Array.Empty<string>();
        private Item[] _items = Array.Empty<Item>();
        /// `_sel` and `_top` index `_items`, not `_all`: what the arrows move
        /// through and what scrolls is the list *as drawn*, headings included,
        /// and the collection a row stands for is one hop away through
        /// `Item.Coll`.  The alternative -- indexing `_all` and deriving the
        /// drawn position -- puts the sections in two places at once.
        private int _sel, _top, _current = -1;
        /// The row the pointer is over, as an `_items` index, resolved during
        /// the draw and read by the note strip at the foot of the panel.  -1
        /// when the pointer is elsewhere, which is the normal state on a
        /// keyboard and is why the strip falls back to the selection.
        private int _hover = -1;

        public bool Open { get; private set; }

        /// **Command 108 stops the clock**, exactly as 106 does: `x = Game_On;
        /// GameOn(FALSE); ... if (GetOpenFileName(&OFN)) ... else GameOn(x);`.
        /// The two score lists and the graphics dialog do not -- see
        /// LevelList.StopsClock for the same split.
        public bool StopsClock => true;

        /// The collection the player chose, or null.  BoardView takes and
        /// clears it, the way it does LevelList.Chosen.
        private string Chosen { get; set; }

        /// Read once and cleared, for the reason LevelList.TakeChosen spells
        /// out: a stale answer here re-opens a collection on a click that had
        /// nothing to do with this panel.
        public string TakeChosen() { string c = Chosen; Chosen = null; return c; }

        public CollectionList(BoardView view) { _view = view; }

        public int Count => _rows.Length;

        /// Rescan on every opening, for the reason LevelList re-reads its three
        /// files on every opening: a .hs written by the win two seconds ago has
        /// to show up in the solved count, and a level saved by the editor into
        /// out/levels/ has to show up at all.
        public void Show(string root, string currentLvl)
        {
            Chosen = null;
            _all = Scan(root);
            _rows = BuildRows(_all);
            _items = Layout(_all);
            _current = -1;
            for (int i = 0; i < _all.Length; i++)
                if (SamePath(_all[i].Path, currentLvl)) { _current = i; break; }
            // LoadBox seeks its cursor to the level that is loaded; the same
            // idea one level up -- open on the collection being played.  A
            // heading is never the selection, so the fallback is the first row
            // rather than item 0.
            _sel = First();
            for (int i = 0; i < _items.Length; i++)
                if (_items[i].Coll == _current && _current >= 0) { _sel = i; break; }
            _top = Math.Max(0, _sel - _rowsShown / 2);
            _hover = -1;
            Open = true;
        }

        // ---- the shelves ----------------------------------------------------
        /// One drawn line: a collection, or the heading of a shelf.
        ///
        /// **Why the sections are a display layer and nothing more.**  `Scan`
        /// and `BuildRows` are what tools/collections_check.py rebuilds in
        /// Python and diffs row by row and by sha256, and `--check-collections`
        /// dumps them in scan order.  Grouping happens *here*, downstream of
        /// both, so the gate keeps comparing the same two things it always did
        /// and the shelves cost it nothing.  See CollectionNotes for the
        /// catalogue and for why the names are this port's own.
        private struct Item
        {
            public int Coll;        // an index into _all; -1 for a heading
            public Shelf Shelf;
        }

        /// The shelves in the order they are drawn.  A shelf with nothing on it
        /// is not drawn at all -- out/levels/ is empty until the editor saves
        /// something, and a heading over no rows is worse than no heading.
        private static readonly Shelf[] Shelves =
        {
            Shelf.Collections, Shelf.Tutorials, Shelf.Walkthroughs, Shelf.Yours,
        };

        private static Item[] Layout(Collection[] all)
        {
            var items = new List<Item>();
            foreach (Shelf s in Shelves)
            {
                var group = new List<int>();
                for (int i = 0; i < all.Length; i++)
                    if (CollectionNotes.ShelfOf(all[i]) == s) group.Add(i);
                if (group.Count == 0) continue;
                // Scan order inside a shelf -- the sorted walk of the roots --
                // unless CollectionNotes.Order places the row.  Two shelves are
                // placed and one nearly: the tutorials run in teaching order,
                // the walkthroughs by the level of the original each opens out,
                // and the collections keep their alphabetical run with the
                // original file lifted to the head of it.  The tie-break is the
                // scan index, so the result is the same on every filesystem
                // whether or not List.Sort is stable.
                group.Sort((a, b) =>
                {
                    int by = CollectionNotes.OrderOf(all[a])
                             .CompareTo(CollectionNotes.OrderOf(all[b]));
                    return by != 0 ? by : a.CompareTo(b);
                });
                items.Add(new Item { Coll = -1, Shelf = s });
                foreach (int i in group) items.Add(new Item { Coll = i, Shelf = s });
            }
            return items.ToArray();
        }

        /// The first item that is a row rather than a heading, or 0 on an empty
        /// list.
        private int First()
        {
            for (int i = 0; i < _items.Length; i++)
                if (_items[i].Coll >= 0) return i;
            return 0;
        }

        public void Close() { Open = false; }

        // ---- the scan -------------------------------------------------------
        /// Where a collection can live.  The first two are the corpus; the
        /// third is where EditMode.Save puts a level that came out of data/,
        /// and listing it is what closes that loop -- edit, save, open, play.
        /// A root that is not there is skipped, which is the normal state of
        /// out/levels/ (it is gitignored and created on the first save).
        public static readonly string[] Roots =
        {
            "data/levels", "data/quirks", "out/levels",
        };

        public static Collection[] Scan(string root)
        {
            var hits = new List<Collection>();
            foreach (string rel in Roots)
            {
                string dir = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                var found = new List<string>();
                Walk(dir, found);
                // Sorted by path so the list is the same on every filesystem --
                // directory enumeration order is defined by nothing.
                found.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string f in found) hits.Add(Describe(root, f));
            }
            return hits.ToArray();
        }

        /// Every .lvl under `dir`, at any depth: data/levels/ is flat and
        /// data/quirks/ is one directory per quirk, and one walk covers both.
        private static void Walk(string dir, List<string> into)
        {
            string[] files, subs;
            try { files = Directory.GetFiles(dir); subs = Directory.GetDirectories(dir); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            foreach (string f in files)
                if (Path.GetExtension(f).Equals(".lvl", StringComparison.OrdinalIgnoreCase))
                    into.Add(f);
            foreach (string s in subs) Walk(s, into);
        }

        private static Collection Describe(string root, string path)
        {
            var c = new Collection
            {
                Path = path,
                Label = Path.GetFileNameWithoutExtension(path),
                Dir = RelDir(root, path),
            };
            try { c.Levels = LevelFile.CountLevels(path); }
            catch (IOException) { c.Levels = 0; }
            c.Solved = CountSolved(new ScoreFiles(path).Hs);
            return c;
        }

        /// The directory the file is in, relative to the repo root and with
        /// forward slashes -- so the rows a check dumps are the same string on
        /// Windows and on Linux.
        private static string RelDir(string root, string path)
        {
            string dir = Path.GetDirectoryName(path) ?? "";
            return Path.GetRelativePath(root, dir).Replace('\\', '/');
        }

        /// How many levels of this collection the player has beaten: records in
        /// the .hs with moves > 0.  A .hs is dense and positional, so the blanks
        /// CheckHighScore pads with are exactly the moves == 0 records this
        /// skips -- see LevelFile.PadHighScore.
        public static int CountSolved(string hsPath)
        {
            if (!File.Exists(hsPath)) return 0;
            byte[] data;
            try { data = File.ReadAllBytes(hsPath); }
            catch (IOException) { return 0; }
            int n = 0;
            for (int at = 0; at + THSREC.Size <= data.Length; at += THSREC.Size)
                if ((data[at] | (data[at + 1] << 8)) > 0) n++;
            return n;
        }

        /// The rows, and they are this port's own -- there is no format string
        /// in the original to read, because the original's list was drawn by
        /// comdlg32.  Three columns: what the collection is called, then
        /// solved/levels, then where the file is.
        /// `tools/collections_check.py` rebuilds them in Python from the same
        /// directories, which is the same two-implementations rule the sprite
        /// sheets and the three list dialogs are held to.
        ///
        /// **The name is CollectionNotes.NameOf and not the file's stem**, which
        /// is a difference on ten rows out of twenty-three -- the tutorials and
        /// the walkthroughs, whose file names are abbreviations nobody says out
        /// loud.  The thirteen collections are still their stems, because that
        /// is what they are called everywhere else.  The column went 24 -> 28 to
        /// hold `Level 179: Being an Inchworm`, the longest of the ten; the head
        /// line below and the gate's LABEL_W move with it.
        public static string[] BuildRows(Collection[] all)
        {
            var rows = new string[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                Collection c = all[i];
                rows[i] = $"{Pad(CollectionNotes.NameOf(c), 28)} {c.Solved,5}/{c.Levels,-5} {c.Dir}";
            }
            return rows;
        }

        /// LevelList's `%-30.30s` rule at a width of this port's choosing.
        private static string Pad(string s, int w)
        {
            s ??= "";
            return s.Length >= w ? s.Substring(0, w) : s.PadRight(w);
        }

        /// LevelList.Cols, for the head line: text at given character columns,
        /// space-filled between.  A label that overruns its slot pushes the next
        /// one right rather than being cut -- which is why the gate caps the
        /// three, and why a cap that is exceeded shows up as a shifted heading
        /// and not as a silent truncation.
        private static string Cols(params (int at, string text)[] parts)
        {
            var sb = new System.Text.StringBuilder();
            foreach ((int at, string text) in parts)
            {
                while (sb.Length < at) sb.Append(' ');
                sb.Append(text);
            }
            return sb.ToString();
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
        }


        // ---- keys -----------------------------------------------------------
        private int _rowsShown = 20;

        public bool Key(Key k)
        {
            switch (k)
            {
                case Godot.Key.Up: Move(-1); break;
                case Godot.Key.Down: Move(1); break;
                case Godot.Key.Pageup: Move(-_rowsShown); break;
                case Godot.Key.Pagedown: Move(_rowsShown); break;
                case Godot.Key.Home: Move(-_items.Length); break;
                case Godot.Key.End: Move(_items.Length); break;
                case Godot.Key.Enter:
                case Godot.Key.KpEnter:
                case Godot.Key.Space:
                    Take(_sel);
                    Close();
                    break;
                case Godot.Key.Escape:
                    // Cancel, which is GetOpenFileName returning FALSE: the old
                    // file name goes back (`strcpy(FileName,temps)`) and the
                    // clock resumes.  Nothing here has changed it yet, so
                    // closing *is* the restore.
                    Close();
                    break;
                default:
                    // **No action**, which is TransListKey's -2 and now this
                    // panel's answer too -- see the header.  The key is still
                    // swallowed, because a letter that fell through to the
                    // board would move a tank nobody can see.
                    break;
            }
            return true;
        }

        /// The selection is always a row, so a step of one is *the next item
        /// that is not a heading* rather than the next item.  A heading the
        /// arrows stopped on would be a cursor with nothing to open.
        private void Move(int d)
        {
            if (_items.Length == 0 || d == 0) return;
            int step = d > 0 ? 1 : -1;
            int at = _sel;
            for (int n = Math.Abs(d); n > 0; n--)
            {
                int next = -1;
                for (int j = at + step; j >= 0 && j < _items.Length; j += step)
                    if (_items[j].Coll >= 0) { next = j; break; }
                if (next < 0) break;            // the end of the list
                at = next;
            }
            _sel = at;
        }

        private void Take(int item)
        {
            if (item < 0 || item >= _items.Length) return;
            int c = _items[item].Coll;
            if (c >= 0 && c < _all.Length) Chosen = _all[c].Path;
        }

        /// The wheel, and a click on a row -- LevelList's two, on the same terms
        /// and for the same reasons.  The two pickers are one panel to look at
        /// and now one panel to point at.
        ///
        /// **The wheel moves the cursor, not the window**, which is what the
        /// sibling panel does and is the only thing that works here: the draw
        /// keeps the selection in view, so a wheel that moved `_top` on its own
        /// would be undone by the very next frame.  Moving the selection scrolls
        /// the window as a consequence, headings and all, which is the same
        /// picture by a route that cannot fight itself.
        public void Scroll(int d) => Move(d);

        private void Pick(int item)
        {
            if (item < 0 || item >= _items.Length || _items[item].Coll < 0) return;
            if (item == _sel)
            {
                Take(item);
                Close();
                return;
            }
            _sel = item;
        }

        // ---- drawing --------------------------------------------------------
        /// The row pitch, on the UI scale -- see LevelList.Line.
        private static int Line => Ui.Px(16);

        /// The air above a heading, which is what makes a shelf read as a shelf
        /// rather than as a row in a different colour.  Not applied to the
        /// item at index 0, because the air above the first heading is the
        /// panel's own padding.
        private static int Gap => Ui.Px(10);

        private float ItemH(int i)
            => i > 0 && _items[i].Coll < 0 ? Line + Gap : Line;

        /// How many items fit in `h` starting at `top`.  Variable heights are
        /// what force this to be a walk rather than a division -- a heading is
        /// a row plus its air.
        private int Fits(int top, float h)
        {
            float used = 0;
            int n = 0;
            for (int i = Math.Max(0, top); i < _items.Length; i++)
            {
                float ih = ItemH(i);
                if (used + ih > h) break;
                used += ih;
                n++;
            }
            return n;
        }

        private static readonly Color CurrentTint = Ui.Good;
        private static readonly Color PlainTint = Ui.Dim;

        public void Draw(Node2D n, Font font, Font mono, Rect2 host)
        {
            // Centred on the window, the same shape as LevelList -- the two are
            // deliberately not one class (see PROGRESS.md: these rows have no
            // original to be diffed against and LevelList's do), but they are
            // the same panel to look at, which is the part a player cares
            // about.
            Ui.Scrim(n, host);
            _view.Chrome.Add(host, "scrim", Close);

            // ---- the panel is sized to its contents, and this is where the
            // layout is written down.  Step 8's was a flat 560 square, which
            // was right when the list was 23 rows of one kind; with the shelves
            // between them and a line of prose at the foot it is either too
            // small to show the list or -- on a big window -- a third of a
            // panel of empty floor with the note stranded at the bottom of it.
            // So the three fixed bands are measured once here, added to what
            // the items actually need, and used again below to place them.  A
            // window that cannot give that much clamps it and the list scrolls.
            float pad = Ui.Px(18);
            // The panel's top edge down to the first row's baseline: the title,
            // its rule, the column heads, their rule.
            float head = pad + Ui.Px(11) + Ui.Px(12) + Ui.Px(20) + Ui.Px(6) + Line;
            // A fixed two lines whether or not the copy fills them: a strip
            // that grew with the sentence would move the list under the pointer
            // that is asking about it.
            float noteH = Ui.Px(11) * 2.6f;
            // The last row's floor down to the panel's: the air around the
            // note's rule, the note, the footer.
            float air = Ui.Px(16);
            float foot = air + noteH + Ui.Px(30);
            float body = 0;
            for (int i = 0; i < _items.Length; i++) body += ItemH(i);

            float w = Mathf.Min(Ui.Px(620), host.Size.X - Ui.Px(40));
            float h = Mathf.Min(
                Mathf.Max(Ui.Px(220), head - Line + 4 + body + foot),
                host.Size.Y - Ui.Px(40));
            var panel = new Rect2(
                Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                Mathf.Round(host.Position.Y + (host.Size.Y - h) / 2f), w, h);
            Ui.Dialog(n, panel);
            _view.Chrome.Swallow(panel);

            float x = panel.Position.X + pad;
            w = panel.Size.X - 2 * pad;
            float y = panel.Position.Y + pad + Ui.Px(11);

            // The title names what the panel *is* rather than what the original
            // called the command that opened it.  Command 108 is `&Open Data
            // File...` on a menu bar this port does not have, and "data file" is
            // 1996 for the thing every player here calls a collection.
            Strings lang = _view.Strings;
            Ui.Caps(n, new Vector2(x, y), lang["coll.title"], Ui.Text, 12);
            Rect2 close = Ui.CloseRect(panel, pad);
            Ui.CloseX(n, close, _view.Chrome.Add(Ui.Touch(close), "close", Close));
            Ui.Write(n, new Vector2(close.Position.X - Ui.Px(10) - Ui.Px(240), y),
                     lang.F("coll.count", _rows.Length), 11, Ui.Faint, Ui.Px(240),
                     HorizontalAlignment.Right);
            y += Ui.Px(12);
            Ui.Rule(n, x, y, w);
            y += Ui.Px(20);

            if (_rows.Length == 0)
            {
                Ui.Write(n, new Vector2(x, y + Ui.Px(10)),
                         lang.F("coll.empty", string.Join(", ", Roots)), 12, Ui.Bad, w);
                Footer(n, panel, x, w);
                return;
            }

            // ---- the column heads, on LevelList's rule and not its own.
            // **At the rows' own size, and with a rule under them.**  These were
            // a size down and separated by air, which is wrong twice over: the
            // cells are placed by character column and a column is only a
            // position if every line advances by the same glyph, and the first
            // row's hover band -- drawn from `y - Line + 4` -- reached up into
            // the head's baseline and lit it.  Faint rather than small is what
            // makes a head a head; the rule is what stops the rows.
            //
            // The head string is spaced to BuildRows' own fields: 28 for the
            // label, then `%5d/%-5d` at columns 29-39, then the directory at 41.
            //
            // **Placed by character column, so the labels are capped**, which is
            // the one place in this interface where a translation has a width it
            // must not exceed: these three sit over cells whose positions come
            // out of a printf, not out of a measurement, so a long word does not
            // reflow the table -- it lands on the next column's numbers.
            // strings_check.py holds them to 28 / 11 / 20 characters.
            string heads = Cols((0, lang["coll.colName"]),
                                (29, lang["coll.colSolved"]),
                                (41, lang["coll.colWhere"]));
            n.DrawString(mono, new Vector2(x, y), heads,
                         HorizontalAlignment.Left, w, Ui.Px(12), Ui.Faint);
            float headY = y;
            y += Ui.Px(6);
            Ui.Rule(n, x, y, w);
            y += Line;

            float noteTop = panel.End.Y - Ui.Px(30) - noteH;
            float rowsTop = y - Line + 4;
            float room = noteTop - air - rowsTop;

            int top = Math.Clamp(_top, 0, Math.Max(0, _items.Length - 1));
            // Bring the selection into view, and only then take up slack at the
            // bottom -- in that order, so a list scrolled to its end with room
            // to spare pulls back rather than showing blank floor.
            while (top < _sel && top + Fits(top, room) <= _sel) top++;
            if (_sel < top) top = _sel;
            while (top > 0 && (top - 1) + Fits(top - 1, room) >= _items.Length) top--;
            _top = top;
            int shown = Fits(top, room);
            // Remembered so a page-up moves by exactly one screenful at every
            // window size -- and never zero, which would wedge PgUp.
            _rowsShown = Math.Max(1, shown);

            // **There is no scrollbar here, so there has to be a count.**  The
            // panel sizes itself to its contents and at the smallest window the
            // contents lose by a row or two -- and a list that is one row short
            // with nothing saying so is a list a player believes.  LevelList's
            // track and thumb would be the richer answer and are not worth
            // their drag state for two rows; a number on the head line is.  It
            // counts collections and not items, because a heading is not
            // something anybody is looking for.
            int hiddenRows = 0;
            for (int i = 0; i < _items.Length; i++)
                if (_items[i].Coll >= 0 && (i < top || i >= top + shown)) hiddenRows++;
            if (hiddenRows > 0)
            {
                string more = lang.F("coll.more", hiddenRows);
                float mw = Ui.Width(more, 11);
                if (Ui.Width(heads, 12, mono) + mw + Ui.Px(20) <= w)
                    Ui.Write(n, new Vector2(x + w - mw, headY), more, 11, Ui.Dim);
            }

            _hover = -1;
            for (int i = top; i < Math.Min(_items.Length, top + shown); i++)
            {
                Item it = _items[i];
                if (it.Coll < 0)
                {
                    // A shelf heading: caps and a hairline out to the right
                    // margin, drawn at the top of its own line's air so the
                    // heading sits closer to what it names than to what it
                    // follows.  Not registered with the chrome -- there is
                    // nothing to click, and a live rectangle here would be one
                    // more thing overlapping the rows for chrome_check to
                    // catch.
                    float hy = y + (i > 0 ? Gap : 0);
                    (string nameKey, string blurbKey) = CollectionNotes.Head(it.Shelf);
                    string name = lang[nameKey], blurb = lang[blurbKey];
                    Ui.Caps(n, new Vector2(x, hy), name, Ui.Accent, 10);
                    // **Dropped rather than clipped.**  The blurb is the one
                    // thing on the line that is optional, and half of it -- the
                    // narrow window's `...most with the solutions recorde` --
                    // reads as a broken string rather than as a squeeze.  The
                    // name alone is still a heading.
                    float bx = x + Ui.CapsWidth(name, 10) + Ui.Px(12);
                    if (Ui.Width(blurb, 10) <= x + w - bx)
                        Ui.Write(n, new Vector2(bx, hy), blurb, 10, Ui.Faint);
                    y += ItemH(i);
                    continue;
                }

                Color tint = it.Coll == _current ? CurrentTint : PlainTint;
                var band = new Rect2(x - Ui.Px(7), y - Line + 4,
                                     w + 2 * Ui.Px(7), Line + 3);
                int at = i;
                // The hit name stays the collection's index in `_all` and not
                // the drawn position, so `row:0` is still the first collection
                // however the shelves are ordered -- which is what
                // tools/chrome_check.py aims at.
                if (_view.Chrome.Add(band, "row:" + it.Coll, () => Pick(at)))
                {
                    _hover = i;
                    if (i != _sel) Ui.Hot(n, band, 5f);
                }
                if (i == _sel)
                {
                    n.DrawStyleBox(Ui.Box(Ui.Raised, Ui.BorderLit, 5f, 1f), band);
                    n.DrawRect(new Rect2(band.Position.X, band.Position.Y,
                                         Mathf.Max(2, Ui.Px(2)), band.Size.Y),
                               it.Coll == _current ? Ui.Good : Ui.Accent);
                    if (it.Coll != _current) tint = Ui.Text;
                }
                n.DrawString(mono, new Vector2(x, y), _rows[it.Coll],
                             HorizontalAlignment.Left, w, Ui.Px(12), tint);
                y += Line;
            }

            // ---- what the row under the pointer is.
            // **Hover, falling back to the selection.**  The pointer is the
            // question this answers, but a player on the keyboard never moves
            // one, and a strip that was blank for them would be a dead third of
            // the panel.  So the arrows answer it too, and the pointer wins
            // while it is over a row.
            Ui.Rule(n, x, noteTop - air / 2, w);
            int show = _hover >= 0 ? _hover : _sel;
            if (show >= 0 && show < _items.Length && _items[show].Coll >= 0)
            {
                Collection c = _all[_items[show].Coll];
                string noteKey = CollectionNotes.KeyOf(c);
                string note = noteKey == null ? "" : lang[noteKey];
                // A collection nobody has written a line for -- one the editor
                // saved, or one upstream added since -- says where it is
                // instead.  A blank strip reads as a bug; a path is at least
                // true.
                Ui.Wrapped(n, new Vector2(x, noteTop + Ui.Px(9)),
                           note.Length > 0 ? note : c.Dir + "/" + c.Label + ".lvl",
                           11, note.Length > 0 ? Ui.Dim : Ui.Faint, w, 2);
            }

            Footer(n, panel, x, w);
        }

        /// **Sheds clauses rather than clipping**, which is LevelList's rule for
        /// its caption applied to the one line that tells a player how to work
        /// the panel: a sentence cut mid-word (`...or a click o`) reads as a
        /// bug, and the clause that goes is the one the next one implies.
        private void Footer(Node2D n, Rect2 panel, float x, float w)
        {
            Strings lang = _view.Strings;
            string s = lang["coll.footerFull"];
            if (Ui.Width(s, 11) > w) s = lang["coll.footerMid"];
            if (Ui.Width(s, 11) > w) s = lang["coll.footerShort"];
            Ui.Write(n, new Vector2(x, panel.End.Y - Ui.Px(13)), s, 11, Ui.Faint, w);
        }
    }
}
