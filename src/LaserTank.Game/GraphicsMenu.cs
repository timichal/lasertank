// Phase 5, step 2: the way in.  GraphBox (LTANK_D.C:1202), which is command 226
// on the Options menu, as an overlay over the board.
//
// The original is a modal dialog with two radio buttons, a listbox of the .ltg
// packs found in Graphics_Dir, the selected pack's author and description, and
// a Close button.  Three of its properties are load-bearing and are kept here:
//
//   * **the choice applies immediately.**  Every branch of its WM_COMMAND ends
//     in SetUpGraphicsBox, which is `if (GFXOn) GFXKill(); GFXInit();` -- with
//     the parens, unlike hazard #11's line in SetGameSize -- so moving the
//     selection reloads the sheet and the board behind the dialog redraws in
//     the new pack.  Moving the cursor here loads the pack.
//
//   * **there is no Cancel.**  Both Close (ID_GRAPHBOX_04) and Cancel (id 2)
//     run the same code and both write [SCREEN] Graphics_Mode to the INI
//     (LTANK_D.C:1247).  Esc here persists exactly as Enter does, because the
//     pack it would be cancelling is already the one on screen.
//
//   * **the game keeps ticking underneath.**  Command 226 does not call
//     GameOn(FALSE) the way the Difficulty dialog (225) does, and DialogBox's
//     modal loop still dispatches the WM_TIMER that SetTimer posts to the main
//     window -- so the clock runs, the anti-tanks fire, and a tank left in the
//     open can die while the dialog is up.  Keys, though, go to the dialog:
//     the main window has focus taken away, so its WM_KEYDOWN never fires and
//     nothing reaches AddKBuff.  BoardView reproduces both halves.
//
// What is *not* here: "Change Directory" (ID_GRAPHBOX_09, a shell folder
// browser -- the key is persisted and --gfx-dir sets it) and "View Opening
// Screen" (ID_GRAPHBOX_08, which toggles QHELP and paints Opening.bmp over the
// board; that is its own piece of drawing and it is not step 2's).
using System.Collections.Generic;
using Godot;

namespace LaserTank.Game
{
    public sealed class GraphicsMenu
    {
        private string Title => _view.Strings["gfx.title"];
        private string AuthorLabel => _view.Strings["gfx.author"];

        /// The two built-in entries are named here rather than on disk -- they
        /// are GraphBox's own radio buttons, not files -- so they get a key each.
        /// A .ltg keeps the label Packs.Scan built out of its file name, because
        /// a file name is not a translatable string.
        private string LabelOf(Pack p) => p.Mode switch
        {
            0 => _view.Strings["gfx.internal"],
            1 => _view.Strings["gfx.user"]
                 + " (" + Packs.GameBmp + " + " + Packs.MaskBmp + ")",
            _ => p.Label,
        };

        private readonly BoardView _view;
        private List<Pack> _packs;
        private int _sel;

        public bool Open { get; private set; }

        public GraphicsMenu(BoardView view) { _view = view; }

        public Pack Selected => _packs != null && _packs.Count > 0 ? _packs[_sel] : null;

        /// Open on the pack that is currently drawn, so the cursor starts where
        /// the eye is -- LB_SELECTSTRING on ltgCur.Name (LTANK_D.C:1234).
        public void Show(List<Pack> packs, Pack current)
        {
            _packs = packs;
            _sel = packs.IndexOf(current);
            if (_sel < 0) _sel = 0;
            Open = true;
        }

        public void Close() { Open = false; _view.PersistGraphics(); }

        /// -> true when the key was the menu's.  Everything is, while it is up:
        /// a modal dialog does not pass keystrokes to the window behind it, and
        /// that is the only reason it is safe for this to use the arrows and
        /// space at all -- they are VK 37..40 and 32, the keys that would
        /// otherwise be recorded moves.
        public bool Key(Key k)
        {
            switch (k)
            {
                case Godot.Key.Up:
                case Godot.Key.Left:
                    Move(-1);
                    break;
                case Godot.Key.Down:
                case Godot.Key.Right:
                    Move(1);
                    break;
                case Godot.Key.Home:
                    _sel = 0;
                    Apply();
                    break;
                case Godot.Key.End:
                    _sel = _packs.Count - 1;
                    Apply();
                    break;
                // The three size commands, 120/121/122 on the original's own
                // menu (LTANK.C:1019) rather than in this dialog -- but this
                // dialog is the only menu this port has, so they are on it.
                case Godot.Key.Key1: _view.SetSize(1); break;
                case Godot.Key.Key2: _view.SetSize(2); break;
                case Godot.Key.Key3: _view.SetSize(3); break;
                case Godot.Key.Z: _view.SetSize(_view.Size % 3 + 1); break;
                default:
                    // Close (ID_GRAPHBOX_04), Cancel (id 2), or the key that
                    // opened it.  Anything else is swallowed, as a dialog does.
                    if (k == Godot.Key.Escape || k == Godot.Key.Enter
                        || k == Godot.Key.KpEnter || k == Godot.Key.Space
                        || k == Godot.Key.G)
                        Close();
                    break;
            }
            return true;
        }

        private void Move(int d)
        {
            if (_packs == null || _packs.Count == 0) return;
            _sel = (_sel + d + _packs.Count) % _packs.Count;
            Apply();
        }

        /// The wheel.  Move wraps, which is right for a six-row radio group and
        /// would be wrong for a 2,030-row table -- see LevelList.Scroll, which
        /// clamps.
        public void Scroll(int d) => Move(d > 0 ? 1 : -1);

        /// A click on a row.  The first applies it -- 226 *is* the dialog that
        /// applies immediately, which is the whole reason the board keeps
        /// drawing behind it -- and a click on the row already showing is
        /// ID_GRAPHBOX_04, Close.
        private void Pick(int i)
        {
            if (_packs == null || i < 0 || i >= _packs.Count) return;
            if (i == _sel) { Close(); return; }
            _sel = i;
            Apply();
        }

        /// SetUpGraphicsBox: load the pack now, so the board behind the panel is
        /// the answer to "what does this one look like".
        private void Apply() => _view.ApplyPack(_packs[_sel]);

        // ---- drawing --------------------------------------------------------
        private static int Pad => Ui.Px(16);
        private static int Line => Ui.Px(18);

        /// The .ltg Info field, with its CRLFs normalised -- Godot's multiline
        /// layout wants \n and the field carries whatever the pack's author
        /// typed.
        private static string InfoText(Pack p) =>
            p.Info.Replace("\r\n", "\n").Replace('\r', '\n');

        /// A dialog-shaped box rather than a full-window overlay: the original
        /// is a small window over the game, and the game is still running behind
        /// it, so covering the board would hide the thing the pack is being
        /// chosen for.  The height is measured from the content, which is also
        /// what SetUpGraphicsBox does -- it resizes the dialog to 100 or 160
        /// dialog base units depending on whether a .ltg is selected
        /// (LTANK_D.C:1160).
        public void Draw(Node2D n, Font font, Rect2 host)
        {
            if (_packs == null) return;
            Pack sel0 = _packs[_sel];
            // Measured rather than reserved, for LanguageMenu's reason: the
            // footer is one line of instruction with nothing in it to shed, and
            // a flat width is a width that happens to fit English.
            float want = Mathf.Max(Ui.Width(_view.Strings["gfx.footer"], 11),
                                   Ui.CapsWidth(Title, 12) + Ui.Px(60));
            foreach (Pack p in _packs)
                want = Mathf.Max(want, Ui.Width(LabelOf(p) + "   "
                                                + _view.Strings["gfx.notFound"], 12.5f));
            if (sel0.Author.Length > 0)
                want = Mathf.Max(want,
                                 Ui.Width(AuthorLabel + " " + sel0.Author, 11.5f));
            float w = Mathf.Min(Mathf.Max(Ui.Px(400), want + 2 * Pad),
                                host.Size.X - Ui.Px(40));
            float wInner = w - 2 * Pad;
            float infoH = sel0.Info.Length == 0 ? 0
                : Mathf.Min(6 * Ui.Px(15),
                            Ui.Sans.GetMultilineStringSize(InfoText(sel0),
                                                           HorizontalAlignment.Left,
                                                           wInner, Ui.Px(12)).Y);
            // The same arithmetic the drawing below walks through, in the same
            // order.  Keep the two in step: a height that disagrees with the
            // flow puts the footer through the middle of the description.
            float height = Pad + Ui.Px(12) + Ui.Px(16)        // title + rule
                           + _packs.Count * Line + Ui.Px(8)   // the list
                           + (sel0.Author.Length > 0 ? Line : 0)
                           + (infoH > 0 ? infoH + Ui.Px(8) : 0)
                           + Ui.Px(8) + Line + Line + Pad;    // size + footer

            Ui.Scrim(n, host);
            _view.Chrome.Add(host, "scrim", Close);
            var panel = new Rect2(
                Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                Mathf.Round(host.Position.Y
                            + (host.Size.Y - Mathf.Min(height, host.Size.Y - Ui.Px(40)))
                              / 2f),
                w, Mathf.Min(height, host.Size.Y - Ui.Px(40)));
            Ui.Dialog(n, panel);
            _view.Chrome.Swallow(panel);

            float x = panel.Position.X + Pad;
            float y = panel.Position.Y + Pad + Ui.Px(11);

            Ui.Caps(n, new Vector2(x, y), Title, Ui.Text, 12);
            Rect2 close = Ui.CloseRect(panel, Pad);
            Ui.CloseX(n, close, _view.Chrome.Add(Ui.Touch(close), "close", Close));
            y += Ui.Px(12);
            Ui.Rule(n, x, y, wInner);
            y += Ui.Px(16);

            for (int i = 0; i < _packs.Count; i++)
            {
                Pack p = _packs[i];
                bool cur = i == _sel;
                string name = LabelOf(p)
                              + (p.Available ? "" : "   " + _view.Strings["gfx.notFound"]);
                Color tint = !p.Available ? Ui.Faint : cur ? Ui.Text : Ui.Dim;
                var band = new Rect2(x - Ui.Px(8), y - Line + Ui.Px(4),
                                     wInner + 2 * Ui.Px(8), Line + Ui.Px(3));
                int at = i;
                if (_view.Chrome.Add(band, "row:" + i, () => Pick(at)) && !cur)
                {
                    Ui.Hot(n, band, 6f);
                    if (p.Available) tint = Ui.Text;
                }
                if (cur)
                    n.DrawStyleBox(Ui.Box(Ui.Raised, Ui.Accent, 6f, 1f), band);
                Ui.Write(n, new Vector2(x, y), name, 12.5f, tint, wInner);
                y += Line;
            }

            y += Ui.Px(8);
            Pack sel = _packs[_sel];
            if (sel.Author.Length > 0)
            {
                Ui.Write(n, new Vector2(x, y), AuthorLabel + " " + sel.Author, 11.5f,
                         Ui.Cyan, wInner);
                y += Line;
            }
            if (infoH > 0)
            {
                // The .ltg Info field is 245 bytes of free text with its own
                // line breaks; wrap what does not fit rather than clipping it.
                Ui.Wrapped(n, new Vector2(x, y), InfoText(sel), 11.5f, Ui.Dim,
                           wInner, 6);
                y += infoH + Ui.Px(8);
            }

            // The last two lines: the size, and how to leave.  The three sizes
            // are *presets* since step 7 -- the board fits the window at any
            // cell size and these snap it to a crisp one -- so the label says
            // so rather than pretending they are the only three.
            y += Ui.Px(8);
            float sx = x;
            Ui.Write(n, new Vector2(sx, y), _view.Strings["gfx.snap"], 11.5f, Ui.Faint,
                     Ui.Px(40));
            sx += Ui.Px(44);
            for (int sz = 1; sz <= 3; sz++)
            {
                bool on = sz == _view.Size;
                string text = _view.Strings.F("pill.snap", BoardView.CellOf(sz));
                int z = sz;
                // The three pills are the three size commands (120/121/122), so
                // they are buttons: the keys 1/2/3 named in the footer are what
                // these pills are about, and a pill is a bigger target than a
                // digit is a memory.
                var box = new Rect2(sx, y - Ui.Px(11),
                                    Ui.CapsWidth(text, 10) + 2 * Ui.Px(7),
                                    Ui.Px(10) + Ui.Px(8));
                bool hot = _view.Chrome.Add(Ui.Touch(box), "snap:" + sz,
                                            () => _view.SetSize(z));
                sx = Ui.Pill(n, sx, y - Ui.Px(11), text,
                             on || hot ? Ui.Accent : Ui.Faint,
                             on || hot ? Ui.Raised : Ui.Bg);
            }
            Ui.Write(n, new Vector2(x, y + Line), 
                     _view.Strings["gfx.footer"], 11, Ui.Faint, wInner);
        }
    }
}
