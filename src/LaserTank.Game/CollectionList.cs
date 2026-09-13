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
        private int _sel, _top, _current = -1;

        public bool Open { get; private set; }

        /// **Command 108 stops the clock**, exactly as 106 does: `x = Game_On;
        /// GameOn(FALSE); ... if (GetOpenFileName(&OFN)) ... else GameOn(x);`.
        /// The two score lists and the graphics dialog do not -- see
        /// LevelList.StopsClock for the same split.
        public bool StopsClock => true;

        /// The collection the player chose, or null.  BoardView reads and
        /// clears it, the way it does LevelList.Chosen.
        public string Chosen { get; private set; }

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
            _current = -1;
            for (int i = 0; i < _all.Length; i++)
                if (SamePath(_all[i].Path, currentLvl)) { _current = i; break; }
            // LoadBox seeks its cursor to the level that is loaded; the same
            // idea one level up -- open on the collection being played.
            _sel = Math.Max(0, _current);
            _top = Math.Max(0, _sel - _rowsShown / 2);
            Open = true;
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
        /// comdlg32.  Three columns: the name the game knows the collection by,
        /// then solved/levels, then where the file is.
        /// `tools/collections_check.py` rebuilds them in Python from the same
        /// directories, which is the same two-implementations rule the sprite
        /// sheets and the three list dialogs are held to.
        public static string[] BuildRows(Collection[] all)
        {
            var rows = new string[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                Collection c = all[i];
                rows[i] = $"{Pad(c.Label, 24)} {c.Solved,5}/{c.Levels,-5} {c.Dir}";
            }
            return rows;
        }

        /// LevelList's `%-30.30s` rule at a width of this port's choosing.
        private static string Pad(string s, int w)
        {
            s ??= "";
            return s.Length >= w ? s.Substring(0, w) : s.PadRight(w);
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
                case Godot.Key.Home: Move(-_rows.Length); break;
                case Godot.Key.End: Move(_rows.Length); break;
                case Godot.Key.Enter:
                case Godot.Key.KpEnter:
                case Godot.Key.Space:
                    if (_sel < _all.Length) Chosen = _all[_sel].Path;
                    Close();
                    break;
                default:
                    // Cancel, which is GetOpenFileName returning FALSE: the old
                    // file name goes back (`strcpy(FileName,temps)`) and the
                    // clock resumes.  Nothing here has changed it yet, so
                    // closing *is* the restore.
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

        /// The wheel, and a click on a row -- LevelList's two, on the same terms
        /// and for the same reasons.  The two pickers are one panel to look at
        /// and now one panel to point at.
        public void Scroll(int d) => Move(d);

        private void Pick(int i)
        {
            if (_rows.Length == 0) return;
            if (i == _sel)
            {
                if (i < _all.Length) Chosen = _all[i].Path;
                Close();
                return;
            }
            _sel = Math.Clamp(i, 0, _rows.Length - 1);
        }

        // ---- drawing --------------------------------------------------------
        /// The row pitch, on the UI scale -- see LevelList.Line.
        private static int Line => Ui.Px(16);

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
            float w = Mathf.Min(Ui.Px(560), host.Size.X - Ui.Px(40));
            float h = Mathf.Min(Ui.Px(560), host.Size.Y - Ui.Px(40));
            var panel = new Rect2(
                Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                Mathf.Round(host.Position.Y + (host.Size.Y - h) / 2f), w, h);
            Ui.Dialog(n, panel);
            _view.Chrome.Swallow(panel);

            float pad = Ui.Px(18);
            float x = panel.Position.X + pad;
            w = panel.Size.X - 2 * pad;
            float y = panel.Position.Y + pad + Ui.Px(11);

            // Both halves of the title are the loaded language's: the menu
            // item's own label (`&Open Data File...`, command 108, out of
            // Language.MainMenu) and txt002, which is the string the original
            // hands GetOpenFileName as its `*.LVL` filter.  Step 6 converted
            // both and nothing had read either until now.
            Language lang = _view.Strings;
            Ui.Caps(n, new Vector2(x, y), lang.Label(108), Ui.Text, 12);
            Rect2 close = Ui.CloseRect(panel, pad);
            Ui.CloseX(n, close, _view.Chrome.Add(Ui.Touch(close), "close", Close));
            Ui.Write(n, new Vector2(close.Position.X - Ui.Px(10) - Ui.Px(240), y),
                     $"{lang["txt002"]}  ·  {_rows.Length}", 11, Ui.Faint, Ui.Px(240),
                     HorizontalAlignment.Right);
            y += Ui.Px(12);
            Ui.Rule(n, x, y, w);
            y += Ui.Px(16);

            if (_rows.Length == 0)
            {
                Ui.Write(n, new Vector2(x, y + Ui.Px(10)),
                         "no .lvl under " + string.Join(", ", Roots), 12, Ui.Bad, w);
                return;
            }

            n.DrawString(mono, new Vector2(x, y + Ui.Px(9)),
                         Pad("collection", 24) + " solved/total where",
                         HorizontalAlignment.Left, w, Ui.Px(11), Ui.Faint);
            y += Line + Ui.Px(4);

            int rows = Math.Max(1, (int)((panel.End.Y - y - Ui.Px(30)) / Line));
            _rowsShown = rows;
            int top = Math.Clamp(_top, 0, Math.Max(0, _rows.Length - rows));
            top = Math.Clamp(top, _sel - rows + 1, _sel);
            _top = top = Math.Max(0, top);
            for (int i = top; i < Math.Min(_rows.Length, top + rows); i++)
            {
                Color tint = i == _current ? CurrentTint : PlainTint;
                var band = new Rect2(x - Ui.Px(7), y - Line + 4,
                                     w + 2 * Ui.Px(7), Line + 3);
                int at = i;
                if (_view.Chrome.Add(band, "row:" + i, () => Pick(at)) && i != _sel)
                    Ui.Hot(n, band, 5f);
                if (i == _sel)
                {
                    n.DrawStyleBox(Ui.Box(Ui.Raised, Ui.BorderLit, 5f, 1f),
                                   new Rect2(x - Ui.Px(7), y - Line + 4,
                                             w + 2 * Ui.Px(7), Line + 3));
                    n.DrawRect(new Rect2(x - Ui.Px(7), y - Line + 4,
                                         Mathf.Max(2, Ui.Px(2)), Line + 3),
                               i == _current ? Ui.Good : Ui.Accent);
                    if (i != _current) tint = Ui.Text;
                }
                n.DrawString(mono, new Vector2(x, y), _rows[i],
                             HorizontalAlignment.Left, w, Ui.Px(12), tint);
                y += Line;
            }

            Ui.Write(n, new Vector2(x, panel.End.Y - Ui.Px(13)),
                     "↑↓ or wheel picks · Enter or a second click opens · "
                     + "any other key or a click outside closes",
                     11, Ui.Faint, w);
        }
    }
}
