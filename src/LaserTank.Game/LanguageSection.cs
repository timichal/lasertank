// Phase 5, step 6: the language picker.
//
// **Step 18 made it a section rather than a panel** (next-steps item 14).  It
// was `Ctrl+L`'s own dialog from step 6 until the merge; it is the Language
// section of `Ctrl+O`'s dialog now, and `Ctrl+L` is unbound and free -- it was
// this port's own invention, so nothing of the original's goes with it.  Its
// old name in this tree was `LanguageMenu`.
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
// It was modelled on the graphics dialog, and for a reason rather than for
// symmetry: the graphics dialog *is* the original's own Options-menu entry
// (command 226, GraphBox), so copying its shape put the new dialog where a
// player already expects to find this kind of choice, on the same modifier.
// **Step 18 is where that argument finished**: two panels that are the same
// shape on two modifiers are one panel with two sections, which is what item 14
// said the whole time.  The three properties step 2 called load-bearing are
// kept, and they are SettingsMenu's constraint now:
//
//   * **the choice applies immediately.**  Moving the cursor reloads the
//     language and every label behind and *around* the section changes, which
//     is the only honest way to pick one: the rows read `fr` and `hr`, and the
//     point of the preview is that you can read the answer rather than the
//     code.  Since the merge the frame previews too -- the panel's own title,
//     its chips and its footer are drawn out of the language under the cursor.
//
//   * **there is no Cancel.**  Esc persists exactly as Enter does, because the
//     language it would be cancelling is already the one on screen.
//
//   * **the game keeps ticking underneath**, and keys go to the dialog.  Both
//     halves are BoardView's, as they are for every section here.
using System.Collections.Generic;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// The **Language** section: the eleven catalogues in `data/language/`.
    internal sealed class LanguageSection : SettingsSection
    {
        private List<LanguageInfo> _langs;
        private int _sel;

        public LanguageSection(BoardView view) : base(view) { }

        public override string TitleKey => "lang.title";
        public override string Id => "language";
        public override string FooterKey => "lang.footer";

        public LanguageInfo Selected => _langs != null && _langs.Count > 0
                                        ? _langs[_sel] : default;

        /// Open on the language that is currently loaded, so the cursor starts
        /// where the eye is -- GraphBox's LB_SELECTSTRING (LTANK_D.C:1234).
        ///
        /// The directory is read here rather than once at startup, because a
        /// section is entered and left many times in a session and the list is
        /// eleven files.
        public override void Enter()
        {
            _langs = Strings.Available(Paths.Data(Strings.DirName));
            _sel = 0;
            for (int i = 0; i < _langs.Count; i++)
                if (_langs[i].Code == Lang.Code) { _sel = i; break; }
        }

        public override void Leave() => View.PersistLanguage();

        public override void Key(InputEventKey k)
        {
            switch (k.Keycode)
            {
                case Godot.Key.Up: Move(-1); break;
                case Godot.Key.Down: Move(+1); break;
                case Godot.Key.Home: Jump(0); break;
                case Godot.Key.End: Jump(_langs.Count - 1); break;
            }
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
            View.ApplyLanguage(_langs[_sel].Code);
        }

        /// The wheel and the click, on the graphics section's terms -- a click
        /// previews.  A second click on the row already showing used to close
        /// the panel and no longer does, for GraphicsSection.Pick's reason.
        public override void Scroll(int d) => Move(d > 0 ? 1 : -1);

        private void Pick(int i)
        {
            if (_langs == null || i < 0 || i >= _langs.Count || i == _sel) return;
            Jump(i);
        }

        // ---- drawing --------------------------------------------------------
        /// The row pitch -- GraphicsSection's, and see Ui.RowBand.
        private static int Line => Ui.Px(20);
        /// Wide enough for "zh-Hans" set in the mono face at 11.5 px.
        private static int CodeCol => Ui.Px(66);

        private List<LanguageInfo> List
            => _langs ??= Strings.Available(Paths.Data(Strings.DirName));

        /// **Measured, not reserved.**  This was a flat `Ui.Px(360)`, which
        /// English fits inside and Czech does not: the footer came out as
        /// `Enter, Esc nebo klik mimo z`, a cut mid-word that reads as a
        /// rendering fault rather than as a squeeze.  The section is a list of
        /// eleven short rows, so there is nothing in it to shed -- it asks for
        /// what it needs and the window is the only limit.  Same rule as
        /// LevelList's caption; same bug the F1 overlay still has.
        public override float Want()
        {
            float want = 0f;
            foreach (LanguageInfo li in List)
                want = Mathf.Max(want, CodeCol + Ui.Width(li.Name, 12.5f));
            if (Lang.Credit.Length > 0)
                want = Mathf.Max(want, Ui.Width(Lang.F("lang.credit", Lang.Credit), 11.5f));
            return want;
        }

        /// The credit line's space is reserved for every language whether or
        /// not the one under the cursor claims one, because the frame is one
        /// height for all four sections and a body that breathed as the cursor
        /// moved would be the thing that height exists to prevent.
        public override float Height(float w) => List.Count * Line + Ui.Px(8) + Line;

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
        public override void Draw(Node2D n, float x, float y, float w)
        {
            List<LanguageInfo> langs = List;
            if (langs == null || langs.Count == 0) return;
            if (_sel >= langs.Count) _sel = 0;

            // Two columns rather than one padded string: the codes are 2 to 7
            // characters wide (`en`, `zh-Hans`) and this font is proportional,
            // so spaces would not line the names up.
            float b = y + Line;
            for (int i = 0; i < langs.Count; i++)
            {
                bool cur = i == _sel;
                Rect2 band = Ui.RowBand(x - Ui.Px(8), b, w + 2 * Ui.Px(8), Line);
                int at = i;
                bool hot = View.Chrome.Add(band, "row:" + i, () => Pick(at)) && !cur;
                if (hot) Ui.Hot(n, band, 6f);
                if (cur)
                    n.DrawStyleBox(Ui.Box(Ui.Raised, Ui.Accent, 6f, 1f), band);
                Ui.Write(n, new Vector2(x, b), langs[i].Code, 11.5f,
                         cur ? Ui.Accent : Ui.Faint, CodeCol, HorizontalAlignment.Left,
                         Ui.Mono);
                Ui.Write(n, new Vector2(x + CodeCol, b), langs[i].Name, 12.5f,
                         cur || hot ? Ui.Text : Ui.Dim, w - CodeCol);
                b += Line;
            }

            // Only when somebody has one to claim.  The eleven that ship today
            // were written with the port rather than contributed to it, so their
            // `credit` is empty and this line is simply absent -- which is the
            // honest rendering, and leaves the field ready for the first file
            // that is somebody's.
            //
            // **`b` is already the next row's baseline** when the loop ends --
            // the rows are written on `y + Line`, `y + 2 * Line` and so on --
            // so the gap is all that is added here.  Adding a `Line` as well
            // put this baseline one row below the `Line` that `Height` reserves
            // for it, which is the footer rule's space: invisible today because
            // all eleven catalogues ship an empty `credit`, and a line through
            // the rule the first time one did not.
            b += Ui.Px(8);
            if (Lang.Credit.Length > 0)
                Ui.Write(n, new Vector2(x, b), Lang.F("lang.credit", Lang.Credit),
                         11.5f, Ui.Cyan, w);
        }
    }
}
