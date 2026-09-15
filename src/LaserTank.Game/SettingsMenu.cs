// Phase 5, step 18: four panels become one.  Next-steps item 14.
//
// **Ctrl+G, Ctrl+L, Ctrl+N and Ctrl+O were the same panel four times.**  The
// graphics dialog is the original's own (command 226, GraphBox, LTANK_D.C:1202);
// the other three are this port's, and each was built by copying it -- step 6
// said so out loud ("so copying its shape puts the new dialog where a player
// already expects to find this kind of choice, on the same modifier"), step 14
// added the first field, and step 15 copied 226's *rule* rather than its shape.
// Four dialogs on four modifiers, all of them settings, all of them centred
// over the board with a title, a rule, a body and a footer.
//
// This is the merge, and it is a merge rather than a rewrite: the four bodies
// are the four files beside this one, unchanged in what they draw and in what
// they argue.  What they lost is the frame -- the scrim, the box, the title,
// the close button and the width arithmetic -- which is now here, once.
//
// ---------------------------------------------------------------------------
// The design constraint, which is 226's three properties
// ---------------------------------------------------------------------------
//
// GraphBox is load-bearing in three ways and every panel that copied it kept
// them, so the merged panel keeps them too:
//
//   * **the choice applies immediately.**  Every branch of 226's WM_COMMAND
//     ends in SetUpGraphicsBox; ToggleOpt writes on the click; the language
//     preview is the point of the language list.  Moving a cursor or flipping a
//     chip here does the thing.
//
//   * **there is no Cancel**, and therefore no OK.  A dialog whose choice is
//     already on screen has nothing to cancel, which is why 226's Close and its
//     Cancel run the same code (LTANK_D.C:1247).  **So this panel has no commit
//     button and cannot grow one** -- sections, not tabs-with-a-commit.
//
//   * **the game keeps ticking underneath.**  226 does not call GameOn(FALSE)
//     the way DiffBox (225) does, and DialogBox's modal loop still dispatches
//     the WM_TIMER posted to the main window, so an exposed tank can die while
//     the panel is up.  Keys, though, are the dialog's: the main window has
//     lost focus, so nothing reaches AddKBuff.  BoardView reproduces both.
//
// ---------------------------------------------------------------------------
// The one section that had a Cancel, and why it does not any more
// ---------------------------------------------------------------------------
//
// Step 14's name row was the exception and said so: it applied nothing live, so
// Esc could mean *leave the name alone* and Enter *save it*, drawn as two
// answers.  Inside a panel where every other control commits on touch, one
// field that needs Enter is the odd one -- and "no Cancel" is the constraint
// item 14 named, not a preference.  So the field commits when it is left: on
// Esc, on Enter, on a click outside, and on a move to another section.  Nothing
// is lost that the player cannot see, because what the field shows is what will
// be stored, and `Options.SetName` still declines a write when the text has not
// changed -- so opening the panel to look at the name writes no file, which was
// the half of step 14's argument that was actually load-bearing.
//
// ---------------------------------------------------------------------------
// The keys, and the one that did not come back
// ---------------------------------------------------------------------------
//
// `Ctrl+O` is the panel, which step 15 settled by spending it: it is the one of
// the four with original command ids behind it (116 and 225).  `Ctrl+L` and
// `Ctrl+N` are unbound now and free for next-steps item 13 -- both were this
// port's own invention, so nothing of the original's goes with them.
//
// **`Ctrl+G` stays, unlisted**, and that is one key fewer than item 14 predicted
// would come back.  It is the original's own accelerator for 226 (ACC1 and ACC2
// both, lt32l_us.inc), and this project's standing rule is that when the
// original has a table you read the table -- inventing bindings instead of
// reading ACC1/ACC2 is on the list of mistakes that cost something.  So it
// opens this panel on the Graphics section and does not appear in the F1 list,
// which is item 13's own device for `S`: an unlisted alias costs nothing and
// old fingers know it.
using System;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// One section of the options panel.
    ///
    /// **A section is a body and nothing else.**  It measures the column it
    /// wants, reports how tall it is in that column, draws into it, and takes
    /// the keys the frame did not.  It owns no scrim, no box and no title,
    /// because a panel with four titles is four panels.
    internal abstract class SettingsSection
    {
        protected readonly BoardView View;

        protected SettingsSection(BoardView view) { View = view; }

        /// The catalogue key of the chip that selects this section.  It is the
        /// title the section had as a panel of its own, which is why the merge
        /// needed no new labels for the four things being merged.
        public abstract string TitleKey { get; }

        /// The section's own word, for `--panel`, `--dump-state` and the hit
        /// name of its chip.  One spelling, so the three cannot drift.
        public abstract string Id { get; }

        /// The catalogue key of the line that describes this section's *own*
        /// keys, or null for a section that has none of its own.  How to leave
        /// the panel is the frame's line and is not repeated per section.
        public virtual string FooterKey => null;

        /// The content column this body would like, in pixels and without the
        /// padding.  **Measured over everything the section can ever show, not
        /// over what it is showing** -- the panel is one width for all four
        /// sections, and a width that moved with the cursor would be a dialog
        /// that resized as you read it.
        public abstract float Want();

        /// The tallest this body can be in a column `w` wide.  Same rule as
        /// Want, and for a sharper reason: the frame pins its footers to the
        /// bottom, so a body that grew past what it declared would draw through
        /// them.
        public abstract float Height(float w);

        public abstract void Draw(Node2D n, float x, float y, float w);

        /// The keys the frame did not take.  Nothing is returned: the panel is
        /// modal, so every key is the panel's whether or not the section
        /// wanted it.
        public virtual void Key(InputEventKey k) { }

        public virtual void Scroll(int d) { }

        /// The section became the visible one.  Where a section reads the world
        /// -- the packs on disk, the languages in data/language/, the name in
        /// the store -- it reads it here rather than in a constructor that ran
        /// once at startup.
        public virtual void Enter() { }

        /// The section stopped being the visible one, by a chip, by a key or by
        /// the panel closing.  This is where a choice that is *live on screen*
        /// becomes a choice that is *in the store* -- GraphBox's Close, which
        /// is the same code as its Cancel.
        public virtual void Leave() { }

        protected Strings Lang => View.Strings;
    }

    /// `Ctrl+O` -- the options panel: the game's own settings, the graphics
    /// pack, the language and the player's name, in one frame with four
    /// sections and no commit button.
    public sealed class SettingsMenu
    {
        private readonly BoardView _view;
        private readonly SettingsSection[] _sections;
        private int _at;

        public bool Open { get; private set; }

        public SettingsMenu(BoardView view)
        {
            _view = view;
            // The order is the order they are worth finding in: the game's own
            // rules first (and it is the section Ctrl+O has opened on since
            // step 15), then the two that change what the board looks like and
            // what it says, then the one piece of the player's own identity.
            _sections = new SettingsSection[]
            {
                new GameSection(view),
                new GraphicsSection(view),
                new LanguageSection(view),
                new NameSection(view),
            };
        }

        internal SettingsSection Current => _sections[_at];

        /// The section showing, for `--dump-state`.  `""` when the panel is
        /// down, so the state line says which of five things is true rather
        /// than carrying four booleans of which at most one could be.
        public string Section => Open ? Current.Id : "";

        /// True while the visible section is a text field, which is the one
        /// thing `--type` has to know: see BoardView.Type.
        public bool Typing => Open && Current is NameSection;

        internal NameSection NameBody
        {
            get
            {
                foreach (SettingsSection s in _sections)
                    if (s is NameSection n) return n;
                return null;
            }
        }

        /// Open, or move, to a section by its word.  An unknown or absent word
        /// leaves the panel where it was, which is what `Ctrl+O` means: reopen
        /// on the section you were last on.
        public void Show(string id = null)
        {
            int want = _at;
            if (id != null)
                for (int i = 0; i < _sections.Length; i++)
                    if (_sections[i].Id == id) { want = i; break; }

            if (Open) { Go(want); return; }
            _at = want;
            Open = true;
            Current.Enter();
        }

        /// Esc, Enter, the close button and a click outside.  All four are the
        /// same act, because there is no Cancel: leaving persists whatever is
        /// on screen, which is GraphBox's own arrangement.
        public void Close()
        {
            if (!Open) return;
            Current.Leave();
            Open = false;
        }

        /// Move to section `i`.  The leaving section commits and the arriving
        /// one reads the world, in that order -- a name typed and then left for
        /// the graphics chip is a name that was typed.
        private void Go(int i)
        {
            i = ((i % _sections.Length) + _sections.Length) % _sections.Length;
            if (i == _at) return;
            Current.Leave();
            _at = i;
            Current.Enter();
        }

        /// -> true when the key was the panel's.  Everything is, while it is
        /// up: a dialog in the original takes the focus, so no WM_KEYDOWN
        /// reaches AddKBuff, and one of these sections is a text field, which
        /// can decline no key without it turning into an accelerator behind a
        /// caret (lt32l_us.inc:149).
        public bool Key(InputEventKey k)
        {
            if (!Open) return false;
            switch (k.Keycode)
            {
                // The three ways out, and Ctrl+O among them because the key
                // that opens a panel closing it is what every panel here has
                // done since step 2.  **The bare letters are gone** -- `G`, `L`
                // and `O` each closed their own panel, and a bare letter that
                // closes a panel with a text field in it is a letter the field
                // never sees.
                case Godot.Key.Escape:
                case Godot.Key.Enter:
                case Godot.Key.KpEnter:
                    Close();
                    return true;
                case Godot.Key.O when k.CtrlPressed:
                    Close();
                    return true;

                // **Left and right are the frame's, up and down are the
                // body's**, and that split is why the graphics list lost the
                // arrows it used to take in pairs: it moved on Up *or* Left
                // because a six-row radio group has no second axis, and now the
                // panel does.  Tab is the same move for a hand that expects a
                // dialog to have one.
                case Godot.Key.Left: Go(_at - 1); return true;
                case Godot.Key.Right: Go(_at + 1); return true;
                case Godot.Key.Tab: Go(_at + (k.ShiftPressed ? -1 : 1)); return true;

                // The unlisted alias -- see the header.  It selects rather than
                // toggles, because it names a section and not the panel.
                case Godot.Key.G when k.CtrlPressed: Go(1); return true;
            }
            Current.Key(k);
            return true;
        }

        public void Scroll(int d) => Current.Scroll(d);

        // ---- drawing --------------------------------------------------------
        private static int Pad => Ui.Px(16);
        private const float ChipSize = 10f;
        private const float FootSize = 10.5f;
        private const float SectFootSize = 11f;

        private static float ChipWidth(string text)
            => Ui.CapsWidth(text, ChipSize) + 2 * Ui.Px(9);

        private static float ChipHeight() => Ui.Px(ChipSize) + Ui.Px(10);

        private Strings Lang => _view.Strings;

        /// The chip row, folded into `w`.  -> the number of rows it takes, and
        /// `place` is called with (index, dx, dy) for each chip if it is given.
        ///
        /// **It folds rather than shrinking**, which is the answer next-steps
        /// item 4 is still looking for on the F1 overlay.
        ///
        /// **No shipped language makes it fold**, and that was measured rather
        /// than assumed: the panel's width is `Draw`'s maximum over the section
        /// footers and bodies, every one of which is wider than four chips, so
        /// the four sit on one line at every window size `--window` reaches --
        /// 400 px in German included. It stays because the alternative to
        /// folding is a chip that shrinks or clips, which is a button you
        /// cannot read, and because a fifth section or a longer title is the
        /// ordinary way this starts firing.
        private int Fold(float w, Action<int, float, float> place)
        {
            int rows = 1;
            float x = 0, gap = Ui.Px(6);
            for (int i = 0; i < _sections.Length; i++)
            {
                float cw = ChipWidth(Lang[_sections[i].TitleKey]);
                if (x > 0 && x + cw > w) { rows++; x = 0; }
                place?.Invoke(i, x, (rows - 1) * (ChipHeight() + gap));
                x += cw + gap;
            }
            return rows;
        }

        private float ChipsHeight(float w)
            => Fold(w, null) * (ChipHeight() + Ui.Px(6)) - Ui.Px(6);

        /// A dialog-shaped box over the board, sized from the widest thing any
        /// of the four sections can show and as tall as the tallest of them.
        ///
        /// **One size for all four sections, deliberately.**  The four panels
        /// this replaced each measured themselves, which was right when each
        /// was the only thing on screen and is wrong now: a frame that resized
        /// as the chips were clicked would move the chips out from under the
        /// pointer that was clicking them.  So the arithmetic below takes the
        /// maximum over the sections, and the body is top-aligned in a box that
        /// does not move.
        public void Draw(Node2D n, Font font, Rect2 host)
        {
            Strings lang = Lang;
            string title = lang["set.title"];
            string foot = lang["set.footer"];

            // The frame's own footer is measured like every other label here --
            // "measured, not reserved", which is the rule step 6 wrote down
            // after the Czech footer came out cut mid-word.  It still wraps
            // when the window will not give it the width; asking is what keeps
            // it on one line when the window would have.
            float want = Mathf.Max(Ui.CapsWidth(title, 12) + Ui.Px(60),
                                   Ui.Width(foot, FootSize));
            foreach (SettingsSection s in _sections)
            {
                want = Mathf.Max(want, s.Want());
                if (s.FooterKey != null)
                    want = Mathf.Max(want, Ui.Width(lang[s.FooterKey], SectFootSize));
            }
            float w = Mathf.Min(Mathf.Max(Ui.Px(420), want + 2 * Pad),
                                host.Size.X - Ui.Px(40));
            float wInner = w - 2 * Pad;

            // The two footers are wrapped rather than written, and that is the
            // one lesson next-steps item 4 hands this panel for free: a footer
            // that is measured and then clamped by the window is a footer that
            // clips mid-word in German, so it folds instead.
            float footH = Ui.WrappedHeight(foot, FootSize, wInner, 3);
            float sectFootH = 0;
            foreach (SettingsSection s in _sections)
                if (s.FooterKey != null)
                    sectFootH = Mathf.Max(sectFootH,
                                          Ui.WrappedHeight(lang[s.FooterKey],
                                                           SectFootSize, wInner, 2));

            float chipsH = ChipsHeight(wInner);
            float bodyH = 0;
            foreach (SettingsSection s in _sections)
                bodyH = Mathf.Max(bodyH, s.Height(wInner));

            // The same walk the drawing below makes, in the same order.  Keep
            // the two in step, which is GraphicsSection's own warning and is
            // worth more here: four bodies share this arithmetic.
            //
            // **The leading `Px(11)` is the title's own line box**, and it is
            // the one term the four panels this replaced all left out -- they
            // opened `Pad + Px(12) + Px(16)` and then drew the title on a
            // baseline `Pad + Px(11)` down, so every one of them was eleven
            // pixels tighter at the bottom than it had asked for.  Harmless
            // while a panel measured only itself; not something to inherit into
            // one box that has to hold the tallest of four bodies.
            float height = Pad + Ui.Px(11) + Ui.Px(12) + Ui.Px(16)  // title + rule
                           + chipsH + Ui.Px(16)                 // the chips
                           + bodyH + Ui.Px(16)                  // the body
                           + Ui.Px(12) + sectFootH + Ui.Px(6) + footH + Pad;
            height = Mathf.Min(height, host.Size.Y - Ui.Px(40));

            Ui.Scrim(n, host);
            _view.Chrome.Add(host, "scrim", Close);
            var panel = new Rect2(
                Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                Mathf.Round(host.Position.Y + (host.Size.Y - height) / 2f), w, height);
            Ui.Dialog(n, panel);
            _view.Chrome.Swallow(panel);

            float x = panel.Position.X + Pad;
            float y = panel.Position.Y + Pad + Ui.Px(11);

            Ui.Caps(n, new Vector2(x, y), title, Ui.Text, 12);
            Rect2 close = Ui.CloseRect(panel, Pad);
            Ui.CloseX(n, close, _view.Chrome.Add(Ui.Touch(close), "close", Close));
            y += Ui.Px(12);
            Ui.Rule(n, x, y, wInner);
            y += Ui.Px(16);

            // ---- the four chips.
            float chipTop = y;
            Fold(wInner, (i, cx, cy) => DrawChip(n, i, x + cx, chipTop + cy));
            y += chipsH + Ui.Px(16);

            Current.Draw(n, x, y, wInner);

            // ---- the two footer lines, pinned to the bottom of the box rather
            // than laid after the body: the body is whatever this section needs
            // and the frame is the same in all four.
            float fy = panel.End.Y - Pad - footH;
            Ui.Wrapped(n, new Vector2(x, fy + Ui.Px(FootSize)), foot, FootSize,
                       Ui.Faint, wInner, 3);
            float ruleY = fy - Ui.Px(12);
            if (Current.FooterKey is string fk)
            {
                float sy = fy - Ui.Px(6) - sectFootH;
                Ui.Wrapped(n, new Vector2(x, sy + Ui.Px(SectFootSize)), lang[fk],
                           SectFootSize, Ui.Dim, wInner, 2);
                ruleY = sy - Ui.Px(12);
            }
            Ui.Rule(n, x, ruleY, wInner);
        }

        /// One chip: the section's own title, lit when it is the one showing.
        ///
        /// It is the level list's and the game section's tag at their size and
        /// with their hit floor.  A fourth hand-rolled copy would be a fourth
        /// answer to a question this interface has answered three times, so
        /// this one is the frame's and the bodies keep theirs -- the moment a
        /// *fifth* wants one is the moment it moves to Ui, which is the note
        /// GameSection.Chip already carries.
        private void DrawChip(Node2D n, int i, float x, float y)
        {
            string text = Lang[_sections[i].TitleKey];
            bool on = i == _at;
            var r = new Rect2(x, y, ChipWidth(text), ChipHeight());
            int at = i;
            bool hot = _view.Chrome.Add(Ui.Touch(r), "sect:" + _sections[i].Id,
                                        () => Go(at));
            n.DrawStyleBox(Ui.Box(on ? Ui.Accent with { A = 0.15f }
                                     : hot ? Ui.Raised : new Color(0, 0, 0, 0),
                                  on ? Ui.Accent with { A = 0.70f }
                                     : hot ? Ui.BorderLit : Ui.Border, 2f, 1f), r);
            Ui.Caps(n, new Vector2(x + Ui.Px(9),
                                   y + r.Size.Y - Ui.Px(ChipSize) * 0.30f - Ui.Px(4)),
                    text, on ? Ui.Accent : hot ? Ui.Text : Ui.Faint, ChipSize);
        }
    }
}
