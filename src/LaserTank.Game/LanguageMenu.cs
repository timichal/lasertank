// Phase 5, step 6: the language picker.
//
// **This dialog does not exist in the original**, and that is the whole of
// step 6's invented behaviour.  The 2007 program has no language setting: it
// reads a fixed `Language\Language.dat` beside the INI (LTANK.C:1421), and you
// changed language by installing a different one of the ten `Setups/` trees
// over your copy.  Ten installers was the 2007 answer to a problem this port
// does not have -- all ten translations sit in `data/language/` at once -- so
// the choice becomes an option like any other, stored in `[DATA] Language` and
// picked here.  IniImport.PsLang records the same argument from the INI's side.
//
// It is modelled on GraphicsMenu, and for a reason rather than for symmetry:
// the graphics dialog *is* the original's own Options-menu entry (command 226,
// GraphBox), so copying its shape puts the new dialog where a player already
// expects to find this kind of choice, on the same modifier -- Ctrl+L beside
// Ctrl+G.  The three properties step 2 called load-bearing are kept:
//
//   * **the choice applies immediately.**  Moving the cursor reloads the
//     language and every label behind the panel changes, which is the only
//     honest way to pick one: the rows read `fr` and `hr`, and the point of
//     the preview is that you can read the answer rather than the code.
//
//   * **there is no Cancel.**  Esc persists exactly as Enter does, because the
//     language it would be cancelling is already the one on screen.
//
//   * **the game keeps ticking underneath**, and keys go to the dialog.  Both
//     halves are BoardView's, the same way they are for GraphicsMenu.
using System.Collections.Generic;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    public sealed class LanguageMenu
    {
        private readonly BoardView _view;
        private List<LanguageInfo> _langs;
        private int _sel;

        public bool Open { get; private set; }

        public LanguageMenu(BoardView view) { _view = view; }

        public LanguageInfo Selected => _langs != null && _langs.Count > 0
                                        ? _langs[_sel] : default;

        /// Open on the language that is currently loaded, so the cursor starts
        /// where the eye is -- GraphBox's LB_SELECTSTRING (LTANK_D.C:1234).
        public void Show(List<LanguageInfo> langs, string current)
        {
            _langs = langs;
            _sel = 0;
            for (int i = 0; i < langs.Count; i++)
                if (langs[i].Code == current) { _sel = i; break; }
            Open = true;
        }

        public void Close() { Open = false; _view.PersistLanguage(); }

        /// -> true when the key was the menu's.  Everything is, while it is up.
        public bool Key(Key k)
        {
            if (!Open) return false;
            switch (k)
            {
                case Godot.Key.Up: Move(-1); break;
                case Godot.Key.Down: Move(+1); break;
                case Godot.Key.Home: Jump(0); break;
                case Godot.Key.End: Jump(_langs.Count - 1); break;
                default:
                    if (k == Godot.Key.Escape || k == Godot.Key.Enter
                        || k == Godot.Key.KpEnter || k == Godot.Key.Space
                        || k == Godot.Key.L)
                        Close();
                    break;
            }
            return true;
        }

        private void Move(int d)
        {
            if (_langs == null || _langs.Count == 0) return;
            Jump((_sel + d + _langs.Count) % _langs.Count);
        }

        private void Jump(int i)
        {
            if (_langs == null || _langs.Count == 0) return;
            _sel = Mathf.Clamp(i, 0, _langs.Count - 1);
            _view.ApplyLanguage(_langs[_sel].Code);
        }

        /// The wheel and the click, on the graphics menu's terms -- this panel
        /// is modelled on 226 deliberately (see the class header) and the
        /// pointer half is modelled on it too: a click previews, a second click
        /// on the row already showing closes.
        public void Scroll(int d) => Move(d > 0 ? 1 : -1);

        private void Pick(int i)
        {
            if (_langs == null || i < 0 || i >= _langs.Count) return;
            if (i == _sel) { Close(); return; }
            Jump(i);
        }

        // ---- drawing --------------------------------------------------------
        private static int Pad => Ui.Px(16);
        private static int Line => Ui.Px(18);
        /// Wide enough for "zh-Hans" set in the mono face at 11.5 px.
        private static int CodeCol => Ui.Px(66);

        /// A dialog-shaped box over the board, sized from its content.
        ///
        /// The labels here come out of the language being previewed, not out of
        /// the one that was loaded when the panel opened -- so the title and the
        /// footer change as the cursor moves.  That is the preview: a row that
        /// says "Traditional Chinese" is worth less than a panel that has
        /// already redrawn itself in it.
        ///
        /// The rows are `<code>   <name>` -- an ISO code and the display name
        /// out of the catalogue.  The name is deliberately the English one
        /// rather than the endonym, and the reason has changed shape since step
        /// 6 wrote it down: the chrome sets in IBM Plex Mono, which has no CJK
        /// glyphs, and what draws the two Chinese rows is Godot falling through
        /// to whatever the *system* has.  That works on a desktop with a CJK
        /// font installed and is not something this panel can promise -- a web
        /// export especially.  An English name is legible to everyone and to
        /// every font here; the endonyms are worth having the day this ships a
        /// face that covers them.
        public void Draw(Node2D n, Font font, Rect2 host, Strings lang)
        {
            if (_langs == null || _langs.Count == 0) return;

            // **Measured, not reserved.**  This was a flat `Ui.Px(360)`, which
            // English fits inside and Czech does not: the footer came out as
            // `Enter, Esc nebo klik mimo z`, a cut mid-word that reads as a
            // rendering fault rather than as a squeeze.  The panel is a list of
            // eleven short rows and one line of instruction, so there is nothing
            // in it to shed -- it asks for what it needs and the window is the
            // only limit.  Same rule as LevelList's caption; same bug the F1
            // overlay had.
            float want = Mathf.Max(Ui.Width(lang["lang.footer"], 11),
                                   Ui.CapsWidth(lang["lang.title"], 12) + Ui.Px(60));
            foreach (LanguageInfo li in _langs)
                want = Mathf.Max(want, CodeCol + Ui.Width(li.Name, 12.5f));
            if (lang.Credit.Length > 0)
                want = Mathf.Max(want, Ui.Width(lang.F("lang.credit", lang.Credit), 11.5f));
            float w = Mathf.Min(Mathf.Max(Ui.Px(360), want + 2 * Pad),
                                host.Size.X - Ui.Px(40));
            float height = Pad + Ui.Px(12) + Ui.Px(16)
                           + _langs.Count * Line + Ui.Px(8)
                           + Line + Ui.Px(6) + Line + Pad;
            height = Mathf.Min(height, host.Size.Y - Ui.Px(40));

            Ui.Scrim(n, host);
            _view.Chrome.Add(host, "scrim", Close);
            var panel = new Rect2(
                Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                Mathf.Round(host.Position.Y + (host.Size.Y - height) / 2f), w, height);
            Ui.Dialog(n, panel);
            _view.Chrome.Swallow(panel);

            float x = panel.Position.X + Pad;
            float wInner = panel.Size.X - 2 * Pad;
            float y = panel.Position.Y + Pad + Ui.Px(11);

            // The title is drawn out of the language being *previewed*, like
            // everything else on this panel: moving the cursor loads the
            // catalogue, so the panel is its own sample of what it is offering.
            Ui.Caps(n, new Vector2(x, y), lang["lang.title"], Ui.Text, 12);
            Rect2 close = Ui.CloseRect(panel, Pad);
            Ui.CloseX(n, close, _view.Chrome.Add(Ui.Touch(close), "close", Close));
            y += Ui.Px(12);
            Ui.Rule(n, x, y, wInner);
            y += Ui.Px(16);

            // Two columns rather than one padded string: the codes are 2 to 7
            // characters wide (`en`, `zh-Hans`) and this font is proportional,
            // so spaces would not line the names up.
            for (int i = 0; i < _langs.Count; i++)
            {
                bool cur = i == _sel;
                var band = new Rect2(x - Ui.Px(8), y - Line + Ui.Px(4),
                                     wInner + 2 * Ui.Px(8), Line + Ui.Px(3));
                int at = i;
                bool hot = _view.Chrome.Add(band, "row:" + i, () => Pick(at)) && !cur;
                if (hot) Ui.Hot(n, band, 6f);
                if (cur)
                    n.DrawStyleBox(Ui.Box(Ui.Raised, Ui.Accent, 6f, 1f), band);
                Ui.Write(n, new Vector2(x, y), _langs[i].Code, 11.5f,
                         cur ? Ui.Accent : Ui.Faint, CodeCol, HorizontalAlignment.Left,
                         Ui.Mono);
                Ui.Write(n, new Vector2(x + CodeCol, y), _langs[i].Name, 12.5f,
                         cur || hot ? Ui.Text : Ui.Dim, wInner - CodeCol);
                y += Line;
            }

            // Only when somebody has one to claim.  The eleven that ship today
            // were written with the port rather than contributed to it, so their
            // `credit` is empty and this line is simply absent -- which is the
            // honest rendering, and leaves the field ready for the first file
            // that is somebody's.
            y += Ui.Px(8);
            if (lang.Credit.Length > 0)
                Ui.Write(n, new Vector2(x, y), lang.F("lang.credit", lang.Credit),
                         11.5f, Ui.Cyan, wInner);
            y += Line + Ui.Px(6);
            Ui.Write(n, new Vector2(x, y), lang["lang.footer"], 11, Ui.Faint, wInner);
        }
    }
}
