// Phase 5, step 6: the language picker.
//
// **This dialog does not exist in the original**, and that is the whole of
// step 6's invented behaviour.  The 2007 program has no language setting: it
// reads a fixed `Language\Language.dat` beside the INI (LTANK.C:1421), and you
// changed language by installing a different one of the ten `Setups/` trees
// over your copy.  Ten installers was the 2007 answer to a problem this port
// does not have -- all ten translations sit in `data/language/` at once -- so
// the choice becomes an option like any other, stored in `[DATA] Language` and
// picked here.  Options.PsLang records the same argument from the INI's side.
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

        // ---- drawing --------------------------------------------------------
        private const int Pad = 10;
        private const int Line = 17;
        private const int CodeCol = 76;      // wide enough for "> zh-Hans" at 13 px

        /// A dialog-shaped box over the board, sized from its content.
        ///
        /// The labels here come out of the language being previewed, not out of
        /// the one that was loaded when the panel opened -- so the title and the
        /// footer change as the cursor moves.  That is the preview: a row that
        /// says "Traditional Chinese" is worth less than a panel that has
        /// already redrawn itself in it.
        ///
        /// The rows are `<code>   <name>` -- an ISO code and the display name
        /// out of the JSON.  The name is deliberately the English one rather
        /// than the endonym: this draws in ThemeDB.FallbackFont, which has no
        /// CJK glyphs, so the two Chinese rows would be boxes.  The endonyms
        /// are worth having the day this gets a font that can render them.
        public void Draw(Node2D n, Font font, Rect2 board, Language lang)
        {
            if (_langs == null || _langs.Count == 0) return;

            float height = Pad + 13 + (Line + 4)
                           + _langs.Count * Line + 6
                           + Line + 4 + Line + Pad;

            var panel = new Rect2(board.Position + new Vector2(6, 6),
                                  new Vector2(board.Size.X - 12,
                                              Mathf.Min(height, board.Size.Y - 12)));
            n.DrawRect(panel, new Color(0.05f, 0.06f, 0.08f, 0.96f));
            n.DrawRect(panel, new Color(0.55f, 0.60f, 0.70f), false, 1);

            float x = panel.Position.X + Pad;
            float w = panel.Size.X - 2 * Pad;
            float y = panel.Position.Y + Pad + 13;

            // There is no string in the original for "pick a language" -- there
            // was nothing to pick.  ID_GRAPHBOX_01, "Select One", is the
            // nearest thing the corpus has that is not about graphics, and it
            // is translated in all ten files, so the panel's own title is in
            // the language being previewed like everything else on it.
            n.DrawString(font, new Vector2(x, y), lang["ID_GRAPHBOX_01"],
                         HorizontalAlignment.Left, w, 15, Colors.White);
            y += Line + 4;

            // Two columns rather than one padded string: the codes are now 2
            // to 7 characters wide (`en`, `zh-Hans`) and this font is
            // proportional, so spaces would not line the names up.
            for (int i = 0; i < _langs.Count; i++)
            {
                bool cur = i == _sel;
                Color col = cur ? Colors.Yellow : Colors.Gainsboro;
                n.DrawString(font, new Vector2(x, y),
                             (cur ? "> " : "  ") + _langs[i].Code,
                             HorizontalAlignment.Left, w, 13, col);
                n.DrawString(font, new Vector2(x + CodeCol, y), _langs[i].Name,
                             HorizontalAlignment.Left, w - CodeCol, 13, col);
                y += Line;
            }

            y += 6;
            if (lang.Author.Length > 0)
            {
                n.DrawString(font, new Vector2(x, y),
                             lang["ID_GRAPHBOX_05"] + " " + lang.Author,
                             HorizontalAlignment.Left, w, 12, Colors.LightSteelBlue);
            }
            y += Line + 4;
            n.DrawString(font, new Vector2(x, y),
                         "up/down picks   Enter or Esc closes",
                         HorizontalAlignment.Left, w, 11, Colors.Gray);
        }
    }
}
