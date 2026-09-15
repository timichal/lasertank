// Next-steps item 3: the two settings the original keeps in its INI and reads
// in `LoadNextLevel`, and the one surface this port gives them.
//
// **What the original has here is a menu item and a modal.**  Skip Completed
// Levels is `ToggleOpt(116, MMenu, &SkipCL, psSCL)` (LTANK.C:996) -- a checkmark
// on the Options menu, written to `[OPT] SkipComLev` the instant it is clicked.
// The difficulty mask is `DiffBox` (command 225, LTANK_D.C:258), five
// checkboxes over `[DATA] Diff_Setting`, and the original puts it in your way:
// `if (Difficulty == 0) SendMessage(MainH, WM_COMMAND, 225, 0)` is the first
// line of LoadNextLevel's body (LTANK2.C:990), so a fresh install answers a
// dialog before it sees a level.
//
// Both are the same *kind* of thing -- settings that govern where the next
// level comes from -- and neither has anything to do with the level on screen,
// so they are one panel rather than a checkmark and a box.  **That is not a
// merge of two questions the way step 14's name row was.**  These are two
// questions; what they share is a moment, and the reason they were a menu item
// and a modal in 1996 is that a menu item and a modal were the two surfaces
// there were.
//
// ---------------------------------------------------------------------------
// The three decisions in it
// ---------------------------------------------------------------------------
//
// **The five chips are not the level list's five chips**, and this is the one
// thing about this panel worth reading twice.  `LevelList._diff` is
// `SearchRec.Diff` -- which rows the table shows -- and it resets when you open
// a different collection, because a filter is about the rows in front of you.
// `Options.Difficulty` is the `Difficulty` global -- where `S`, `P` and a win
// take you -- and it is persisted, because it is how somebody wants to play.
// They were nearly made one value.  They are two because filtering a table to
// look something up is not a statement about the game, and a browse that
// silently changed where the next level came from would be impossible to
// connect to the chip that did it.  The chips look alike because they *are*
// alike -- the same five ranks in the same five colours out of `Ui.Diff` -- and
// the labels are what say which is which.
//
// **The 225 popup is dropped and the default is all five.**  Not a
// simplification: a zero mask is not a mask in the original either, it is a
// "never asked" sentinel, and the dialog is the asking.  `Options` answers it
// with all five ranks at the point it would have been asked, which is what the
// shipped `LaserTank.ini` does anyway (`Diff_Setting=31`), and the panel is
// then somewhere you go rather than somewhere you are sent.
//
// **It applies immediately and has no OK**, which is command 226's rule and the
// one next-steps item 14 names as the design constraint for the merged options
// dialog this is the first half of: GraphBox's Close and Cancel run the same
// code (LTANK_D.C:1247), because the choice was already live.  So do these --
// `ToggleOpt` writes on the click and so does this -- and the panel has a close
// rather than two answers.  It is the *opposite* of the name row's Save/Cancel,
// and the difference is real: a name is typed and a toggle is flipped, and the
// thing you cannot undo by flipping it back is the one that needs a Cancel.
//
// The clock keeps running underneath, as it does behind every other panel here.
// See NameMenu for that argument; it is not re-made per panel.
using System;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// `Ctrl+O` -- the game options panel: skip completed levels, and the five
    /// ranks the game will advance to.
    public sealed class OptionsMenu
    {
        private readonly BoardView _view;

        public bool Open { get; private set; }

        public OptionsMenu(BoardView view) { _view = view; }

        public void Show() { Open = true; }
        public void Close() { Open = false; }

        private Options Opt => _view.Options;

        /// -> true when the key was the panel's.
        ///
        /// **Everything is, while it is up**, which is the graphics picker's
        /// rule rather than the level list's: a dialog in the original takes the
        /// focus, so no `WM_KEYDOWN` reaches `AddKBuff` and there is no key this
        /// can decline without it turning into an accelerator behind an open
        /// panel.
        public bool Key(Godot.Key k)
        {
            if (!Open) return false;
            switch (k)
            {
                case Godot.Key.Escape:
                case Godot.Key.Enter:
                case Godot.Key.KpEnter:
                case Godot.Key.O: Close(); break;

                // `S` for the skip, which is the letter of the thing rather
                // than a position in a list -- there is no cursor here to move.
                case Godot.Key.S: Opt.SetSkipCompleted(!Opt.SkipCompleted); break;

                // 1..5 are the ranks and 0 is all of them, which is exactly the
                // level list's own `Ctrl+1`..`Ctrl+5` / `Ctrl+0` over its own
                // mask.  Bare here because nothing else in this panel wants a
                // digit, and the editor's palette spells the same five ranks
                // the same way (`1 - 5`, EditorKeys).
                case Godot.Key.Key0: Opt.SetDifficulty(Options.AllRanks); break;
                case Godot.Key.Key1: Opt.ToggleRank(1); break;
                case Godot.Key.Key2: Opt.ToggleRank(2); break;
                case Godot.Key.Key3: Opt.ToggleRank(4); break;
                case Godot.Key.Key4: Opt.ToggleRank(8); break;
                case Godot.Key.Key5: Opt.ToggleRank(16); break;
            }
            return true;
        }

        // ---- drawing --------------------------------------------------------
        private static int Pad => Ui.Px(16);
        private const float ChipSize = 10f;

        private static float ChipWidth(string text)
            => Ui.CapsWidth(text, ChipSize) + 2 * Ui.Px(7);

        private static float ChipHeight() => Ui.Px(ChipSize) + Ui.Px(8);

        /// The panel: a title, two settings, and a line under each saying what
        /// it does to the game rather than what it is called.
        ///
        /// The copy earns its place the way `name.scoresAs` does.  "Skip
        /// completed levels" is self-explanatory only until you ask *which*
        /// levels count as completed and *when* the skipping happens -- the
        /// answer is "the ones with a score in this collection's .hs" and "when
        /// the game moves you", not when you pick a level out of the list, and
        /// neither is guessable from three words on a chip.
        public void Draw(Node2D n, Font font, Rect2 host, Strings lang)
        {
            string title = lang["opt.title"];
            string skip = lang["opt.skip"];
            string skipAbout = lang["opt.skipAbout"];
            string ranks = lang["opt.ranks"];
            string ranksAbout = lang["opt.ranksAbout"];
            string footer = lang["opt.footer"];

            var rankNames = new string[5];
            for (int i = 0; i < 5; i++) rankNames[i] = lang[TLEVELINFO.RankKeys[i + 1]];

            float chips = 0f;
            foreach (string r in rankNames) chips += ChipWidth(r) + Ui.Px(6);
            float want = Mathf.Max(Ui.Width(skipAbout, 11.5f), Ui.Width(ranksAbout, 11.5f));
            want = Mathf.Max(want, chips);
            want = Mathf.Max(want, Ui.Width(footer, 10.5f));
            want = Mathf.Max(want, Ui.CapsWidth(title, 12) + Ui.Px(60));
            float w = Mathf.Min(Mathf.Max(Ui.Px(380), want + 2 * Pad),
                                host.Size.X - Ui.Px(40));

            float rowH = ChipHeight();
            float height = Pad + Ui.Px(12) + Ui.Px(18)
                           + rowH + Ui.Px(26) + Ui.Px(24)
                           + Ui.Px(16) + rowH + Ui.Px(26) + Ui.Px(22)
                           + Ui.Px(14) + Pad;
            height = Mathf.Min(height, host.Size.Y - Ui.Px(40));

            Ui.Scrim(n, host);
            // Click-off closes, which is what every panel that applies live
            // does -- there is nothing here a click outside could be declining.
            _view.Chrome.Add(host, "scrim", Close);
            var panel = new Rect2(
                Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                Mathf.Round(host.Position.Y + (host.Size.Y - height) / 2f), w, height);
            Ui.Dialog(n, panel);
            _view.Chrome.Swallow(panel);

            float x = panel.Position.X + Pad;
            float wInner = panel.Size.X - 2 * Pad;
            float y = panel.Position.Y + Pad + Ui.Px(11);

            Ui.Caps(n, new Vector2(x, y), title, Ui.Text, 12);
            Rect2 close = Ui.CloseRect(panel, Pad);
            Ui.CloseX(n, close, _view.Chrome.Add(Ui.Touch(close), "close", Close));
            y += Ui.Px(12);
            Ui.Rule(n, x, y, wInner);
            y += Ui.Px(18);

            // ---- the skip.  One chip, in the colour the level list gives its
            // own "only unsolved" chip -- the two say different things about the
            // same word and there is no reading under which one of them should
            // be the odd colour.
            Chip(n, x, y, skip, Opt.SkipCompleted, Ui.Good, "opt:skip",
                 () => Opt.SetSkipCompleted(!Opt.SkipCompleted));
            y += rowH + Ui.Px(8);
            Ui.Write(n, new Vector2(x, y + Ui.Px(10)), skipAbout, 11.5f, Ui.Dim, wInner);
            y += Ui.Px(18) + Ui.Px(24);

            // ---- the five ranks.
            Ui.Caps(n, new Vector2(x, y), ranks, Ui.Faint, 9);
            y += Ui.Px(16);
            float cx = x;
            for (int i = 0; i < 5; i++)
            {
                int bit = 1 << i;
                // Ui.Diff is indexed by the difficulty *digit*: bit position
                // plus one, 1/2/4/8/16 -> 1..5.  The level list reads it the
                // same way and for the same reason.
                cx = Chip(n, cx, y, rankNames[i], (Opt.Difficulty & bit) != 0,
                          Ui.Diff[i + 1], "opt:rank:" + (i + 1),
                          () => Opt.ToggleRank(bit));
            }
            y += rowH + Ui.Px(8);
            Ui.Write(n, new Vector2(x, y + Ui.Px(10)), ranksAbout, 11.5f, Ui.Dim, wInner);
            y += Ui.Px(18) + Ui.Px(22);

            Ui.Write(n, new Vector2(x, y), footer, 10.5f, Ui.Faint, wInner);
        }

        /// One switch: a square outlined tag, lit when it is on, in the colour
        /// of the thing it names.  The level list's own chip, copied rather than
        /// shared -- see below.
        ///
        /// **Two copies of fifteen lines, deliberately.**  `LevelList.Chip`
        /// closes over that panel's `Apply()` and its filter state, and the one
        /// thing this file has to keep saying is that these chips are *not*
        /// those chips.  A shared helper would be the place somebody later
        /// unifies the two masks by accident, which is the merge this panel's
        /// header exists to argue against.  If a third panel wants one, that is
        /// the moment it moves to Ui.
        ///
        /// -> the x past its right edge, so a row of them is a fold.
        private float Chip(Node2D n, float x, float y, string text, bool on,
                           Color c, string name, Action act)
        {
            float h = ChipHeight();
            var r = new Rect2(x, y, ChipWidth(text), h);
            // The hit box is grown and the drawing is not -- an 18 px tag is a
            // tag a thumb misses.  See Ui.Touch.
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
    }
}
