// Phase 5, step 2: the way in.  GraphBox (LTANK_D.C:1202), which is command 226
// on the Options menu.
//
// **Step 18 made it a section rather than a panel** (next-steps item 14).  It
// was `Ctrl+G`'s own dialog from step 2 until the merge; it is the Graphics
// section of `Ctrl+O`'s dialog now, and `Ctrl+G` is an unlisted alias that
// opens the panel on it -- see SettingsMenu's header for why that one key
// stayed when the other two came back.  Its old name in this tree was
// `GraphicsMenu`.
//
// The original is a modal dialog with two radio buttons, a listbox of the .ltg
// packs found in Graphics_Dir, the selected pack's author and description, and
// a Close button.  Three of its properties are load-bearing and are kept here
// -- they are now the whole merged panel's constraint, and SettingsMenu's
// header carries them:
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
// board; that is its own piece of drawing and it is next-steps item 11's).
using System.Collections.Generic;
using Godot;

namespace LaserTank.Game
{
    /// The **Graphics** section: the packs, and the three board-size presets.
    internal sealed class GraphicsSection : SettingsSection
    {
        private List<Pack> _packs;
        private int _sel;

        public GraphicsSection(BoardView view) : base(view) { }

        public override string TitleKey => "gfx.title";
        public override string Id => "graphics";
        public override string FooterKey => "gfx.footer";

        private string AuthorLabel => Lang["gfx.author"];

        /// The two built-in entries are named here rather than on disk -- they
        /// are GraphBox's own radio buttons, not files -- so they get a key each.
        /// A .ltg keeps the label Packs.Scan built out of its file name, because
        /// a file name is not a translatable string.
        private string LabelOf(Pack p) => p.Mode switch
        {
            0 => Lang["gfx.internal"],
            1 => Lang["gfx.user"]
                 + " (" + Packs.GameBmp + " + " + Packs.MaskBmp + ")",
            _ => p.Label,
        };

        public Pack Selected => _packs != null && _packs.Count > 0 ? _packs[_sel] : null;

        /// Open on the pack that is currently drawn, so the cursor starts where
        /// the eye is -- LB_SELECTSTRING on ltgCur.Name (LTANK_D.C:1234).
        public override void Enter()
        {
            _packs = View.PackList;
            _sel = _packs == null ? 0 : _packs.IndexOf(View.CurrentPack);
            if (_sel < 0) _sel = 0;
        }

        /// GraphBox's Close, which is also its Cancel (LTANK_D.C:1247).
        public override void Leave() => View.PersistGraphics();

        /// The section's own keys.  **Left and right are gone** -- they moved a
        /// six-row radio group in either axis because it had no second one, and
        /// the merged panel does: they pick the section now.  Up and down are
        /// what the footer has always named.
        public override void Key(InputEventKey k)
        {
            switch (k.Keycode)
            {
                case Godot.Key.Up: Move(-1); break;
                case Godot.Key.Down: Move(1); break;
                case Godot.Key.Home: _sel = 0; Apply(); break;
                case Godot.Key.End: _sel = _packs.Count - 1; Apply(); break;
                // The three size commands, 120/121/122 on the original's own
                // menu (LTANK.C:1019) rather than in this dialog -- but the
                // board's own look is what this section is about, so they are
                // on it.
                case Godot.Key.Key1: View.SetSize(1); break;
                case Godot.Key.Key2: View.SetSize(2); break;
                case Godot.Key.Key3: View.SetSize(3); break;
                case Godot.Key.Z: View.SetSize(View.Size % 3 + 1); break;
            }
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
        public override void Scroll(int d) => Move(d > 0 ? 1 : -1);

        /// A click on a row.  The first applies it -- 226 *is* the dialog that
        /// applies immediately, which is the whole reason the board keeps
        /// drawing behind it.
        ///
        /// **A second click on the row already showing no longer closes the
        /// panel**, which step 18 took away: it closed a dialog that was only
        /// about packs, and this one has three other sections behind it, so the
        /// same gesture would now shut a settings panel the player was halfway
        /// through.  The close button and the scrim are the ways out.
        private void Pick(int i)
        {
            if (_packs == null || i < 0 || i >= _packs.Count || i == _sel) return;
            _sel = i;
            Apply();
        }

        /// SetUpGraphicsBox: load the pack now, so the board behind the panel is
        /// the answer to "what does this one look like".
        private void Apply() => View.ApplyPack(_packs[_sel]);

        // ---- drawing --------------------------------------------------------
        /// The row pitch.  `Px(18)` until the band overlap was fixed -- see
        /// Ui.RowBand for why the pitch had to grow rather than the band
        /// shrink.
        private static int Line => Ui.Px(20);

        /// The .ltg Info field, with its CRLFs normalised -- Godot's multiline
        /// layout wants \n and the field carries whatever the pack's author
        /// typed.
        private static string InfoText(Pack p) =>
            p.Info.Replace("\r\n", "\n").Replace('\r', '\n');

        private static float InfoHeight(Pack p, float w)
            => p.Info.Length == 0 ? 0
               : Mathf.Min(6 * Ui.Px(15),
                           Ui.Sans.GetMultilineStringSize(InfoText(p),
                                                          HorizontalAlignment.Left,
                                                          w, Ui.Px(12)).Y);

        private List<Pack> List => _packs ?? View.PackList;

        /// **Measured over every pack, not over the selected one.**  The panel
        /// is one width and one height for all four sections (SettingsMenu.Draw
        /// says why), so a section whose body changed size as its cursor moved
        /// would move the frame under the pointer.  The old panel re-measured
        /// itself per selection and could, because it was alone on screen.
        public override float Want()
        {
            float want = 0f;
            foreach (Pack p in List)
            {
                want = Mathf.Max(want, Ui.Width(LabelOf(p) + "   "
                                                + Lang["gfx.notFound"], 12.5f));
                if (p.Author.Length > 0)
                    want = Mathf.Max(want,
                                     Ui.Width(AuthorLabel + " " + p.Author, 11.5f));
            }
            return want;
        }

        public override float Height(float w)
        {
            List<Pack> packs = List;
            bool author = false;
            float info = 0f;
            foreach (Pack p in packs)
            {
                if (p.Author.Length > 0) author = true;
                info = Mathf.Max(info, InfoHeight(p, w));
            }
            return packs.Count * Line + Ui.Px(8)
                   + (author ? Line : 0)
                   + (info > 0 ? info + Ui.Px(8) : 0)
                   + Ui.Px(8) + Line;
        }

        public override void Draw(Node2D n, float x, float y, float w)
        {
            List<Pack> packs = List;
            if (packs == null || packs.Count == 0) return;
            if (_sel >= packs.Count) _sel = 0;

            // The rows are written on a baseline and their hover bands hang
            // above it, so the first baseline is one line down from the block's
            // top -- which is what keeps row 0's band inside the space Height
            // asked the frame for.
            float b = y + Line;
            for (int i = 0; i < packs.Count; i++)
            {
                Pack p = packs[i];
                bool cur = i == _sel;
                string name = LabelOf(p)
                              + (p.Available ? "" : "   " + Lang["gfx.notFound"]);
                Color tint = !p.Available ? Ui.Faint : cur ? Ui.Text : Ui.Dim;
                Rect2 band = Ui.RowBand(x - Ui.Px(8), b, w + 2 * Ui.Px(8), Line);
                int at = i;
                if (View.Chrome.Add(band, "row:" + i, () => Pick(at)) && !cur)
                {
                    Ui.Hot(n, band, 6f);
                    if (p.Available) tint = Ui.Text;
                }
                if (cur)
                    n.DrawStyleBox(Ui.Box(Ui.Raised, Ui.Accent, 6f, 1f), band);
                Ui.Write(n, new Vector2(x, b), name, 12.5f, tint, w);
                b += Line;
            }

            b += Ui.Px(8);
            Pack sel = packs[_sel];
            if (sel.Author.Length > 0)
            {
                Ui.Write(n, new Vector2(x, b), AuthorLabel + " " + sel.Author, 11.5f,
                         Ui.Cyan, w);
            }
            // The author line's space is reserved whether or not this pack has
            // one, because the snap row below it must not walk up and down as
            // the cursor moves -- same rule as the width.
            bool anyAuthor = false;
            foreach (Pack p in packs) if (p.Author.Length > 0) anyAuthor = true;
            if (anyAuthor) b += Line;

            float infoH = 0f;
            foreach (Pack p in packs) infoH = Mathf.Max(infoH, InfoHeight(p, w));
            if (infoH > 0)
            {
                // The .ltg Info field is 245 bytes of free text with its own
                // line breaks; wrap what does not fit rather than clipping it.
                if (sel.Info.Length > 0)
                    Ui.Wrapped(n, new Vector2(x, b), InfoText(sel), 11.5f, Ui.Dim, w, 6);
                b += infoH + Ui.Px(8);
            }

            // The board size, which is what the pack is drawn at.  The three
            // sizes are *presets* since step 7 -- the board fits the window at
            // any cell size and these snap it to a crisp one -- so the label
            // says so rather than pretending they are the only three.
            b += Ui.Px(8);
            float sx = x;
            Ui.Write(n, new Vector2(sx, b), Lang["gfx.snap"], 11.5f, Ui.Faint,
                     Ui.Px(40));
            sx += Ui.Px(44);
            for (int sz = 1; sz <= 3; sz++)
            {
                bool on = sz == View.Size;
                string text = Lang.F("pill.snap", BoardView.CellOf(sz));
                int z = sz;
                // The three pills are the three size commands (120/121/122), so
                // they are buttons: the keys 1/2/3 named in the footer are what
                // these pills are about, and a pill is a bigger target than a
                // digit is a memory.
                var box = new Rect2(sx, b - Ui.Px(11),
                                    Ui.CapsWidth(text, 10) + 2 * Ui.Px(7),
                                    Ui.Px(10) + Ui.Px(8));
                bool hot = View.Chrome.Add(Ui.Touch(box), "snap:" + sz,
                                           () => View.SetSize(z));
                sx = Ui.Pill(n, sx, b - Ui.Px(11), text,
                             on || hot ? Ui.Accent : Ui.Faint,
                             on || hot ? Ui.Raised : Ui.Bg);
            }
        }
    }
}
