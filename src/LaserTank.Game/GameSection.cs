// Next-steps item 3: the two settings the original keeps in its INI and reads
// in `LoadNextLevel`, and the one surface this port gives them.
//
// **Step 18 made it a section rather than a panel** (next-steps item 14).  It
// was `Ctrl+O`'s whole dialog from step 15 until the merge; it is the first
// section of `Ctrl+O`'s dialog now, and nothing about what it draws or argues
// changed -- the frame it used to carry is SettingsMenu's.  Its old name in
// this tree was `OptionsMenu`.
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
// so they are one section rather than a checkmark and a box.  **That is not a
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
// thing about this file worth reading twice.  `LevelList._diff` is
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
// shipped `LaserTank.ini` does anyway (`Diff_Setting=31`), and the section is
// then somewhere you go rather than somewhere you are sent.
//
// **It applies immediately and has no OK**, which is command 226's rule and the
// one next-steps item 14 named as the design constraint for the merged panel
// this was the first half of: GraphBox's Close and Cancel run the same code
// (LTANK_D.C:1247), because the choice was already live.  So do these --
// `ToggleOpt` writes on the click and so does this -- and the panel has a close
// rather than two answers.  It was the *opposite* of the name row's
// Save/Cancel; step 18 settled that disagreement in this file's favour, and
// SettingsMenu's header says why.
//
// The clock keeps running underneath, as it does behind every other panel here.
// See NameSection for that argument; it is not re-made per section.
using System;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// The **Game options** section: skip completed levels, and the five ranks
    /// the game will advance to.
    internal sealed class GameSection : SettingsSection
    {
        public GameSection(BoardView view) : base(view) { }

        public override string TitleKey => "opt.title";
        public override string Id => "game";
        public override string FooterKey => "opt.footer";

        private Options Opt => View.Options;

        /// The section's own keys.
        ///
        /// **Everything reaches here that the frame did not take**, which is the
        /// graphics picker's rule rather than the level list's: a dialog in the
        /// original takes the focus, so no `WM_KEYDOWN` reaches `AddKBuff` and
        /// there is no key this can decline without it turning into an
        /// accelerator behind an open panel.
        public override void Key(InputEventKey k)
        {
            switch (k.Keycode)
            {
                // `S` for the skip, which is the letter of the thing rather
                // than a position in a list -- there is no cursor here to move.
                case Godot.Key.S: Opt.SetSkipCompleted(!Opt.SkipCompleted); break;

                // 1..5 are the ranks and 0 is all of them, which is exactly the
                // level list's own `Ctrl+1`..`Ctrl+5` / `Ctrl+0` over its own
                // mask.  Bare here because nothing else in this section wants a
                // digit, and the editor's palette spells the same five ranks
                // the same way (`1 - 5`, EditorKeys).  The graphics section's
                // own `1`/`2`/`3` are the board sizes and do not collide,
                // because one section has the keys at a time.
                case Godot.Key.Key0: Opt.SetDifficulty(Options.AllRanks); break;
                case Godot.Key.Key1: Opt.ToggleRank(1); break;
                case Godot.Key.Key2: Opt.ToggleRank(2); break;
                case Godot.Key.Key3: Opt.ToggleRank(4); break;
                case Godot.Key.Key4: Opt.ToggleRank(8); break;
                case Godot.Key.Key5: Opt.ToggleRank(16); break;
            }
        }

        // ---- drawing --------------------------------------------------------
        private const float ChipSize = 10f;
        private const float AboutSize = 11.5f;

        private static float ChipWidth(string text)
            => Ui.CapsWidth(text, ChipSize) + 2 * Ui.Px(7);

        private static float ChipHeight() => Ui.Px(ChipSize) + Ui.Px(8);

        private string[] RankNames()
        {
            var r = new string[5];
            for (int i = 0; i < 5; i++) r[i] = Lang[TLEVELINFO.RankKeys[i + 1]];
            return r;
        }

        /// The chips and nothing else.
        ///
        /// **The two lines of copy are deliberately not measured**, and that is
        /// a change step 18 made rather than inherited.  They used to be single
        /// `Ui.Write` lines whose width the panel asked for, which made one
        /// English sentence the reason the dialog was 550 px wide and made the
        /// Czech one clip anyway once the window clamped it.  They wrap now, so
        /// what they cost the frame is height and not width -- which is the
        /// answer next-steps item 4 is still owed on the F1 overlay.
        public override float Want()
        {
            float chips = 0f;
            foreach (string r in RankNames()) chips += ChipWidth(r) + Ui.Px(6);
            return Mathf.Max(chips, ChipWidth(Lang["opt.skip"]));
        }

        public override float Height(float w)
            => ChipHeight() + Ui.Px(10)
               + Ui.WrappedHeight(Lang["opt.skipAbout"], AboutSize, w, 3) + Ui.Px(26)
               + Ui.Px(16) + ChipHeight() + Ui.Px(10)
               + Ui.WrappedHeight(Lang["opt.ranksAbout"], AboutSize, w, 3);

        /// Two settings, and a line under each saying what it does to the game
        /// rather than what it is called.
        ///
        /// The copy earns its place the way `name.scoresAs` does.  "Skip
        /// completed levels" is self-explanatory only until you ask *which*
        /// levels count as completed and *when* the skipping happens -- the
        /// answer is "the ones with a score in this collection's .hs" and "when
        /// the game moves you", not when you pick a level out of the list, and
        /// neither is guessable from three words on a chip.
        public override void Draw(Node2D n, float x, float y, float w)
        {
            string skipAbout = Lang["opt.skipAbout"], ranksAbout = Lang["opt.ranksAbout"];

            // ---- the skip.  One chip, in the colour the level list gives its
            // own "only unsolved" chip -- the two say different things about the
            // same word and there is no reading under which one of them should
            // be the odd colour.
            Chip(n, x, y, Lang["opt.skip"], Opt.SkipCompleted, Ui.Good, "opt:skip",
                 () => Opt.SetSkipCompleted(!Opt.SkipCompleted));
            y += ChipHeight() + Ui.Px(10);
            Ui.Wrapped(n, new Vector2(x, y + Ui.Px(AboutSize)), skipAbout, AboutSize,
                       Ui.Dim, w, 3);
            y += Ui.WrappedHeight(skipAbout, AboutSize, w, 3) + Ui.Px(26);

            // ---- the five ranks.
            Ui.Caps(n, new Vector2(x, y), Lang["opt.ranks"], Ui.Faint, 9);
            y += Ui.Px(16);
            float cx = x;
            string[] names = RankNames();
            for (int i = 0; i < 5; i++)
            {
                int bit = 1 << i;
                // Ui.Diff is indexed by the difficulty *digit*: bit position
                // plus one, 1/2/4/8/16 -> 1..5.  The level list reads it the
                // same way and for the same reason.
                cx = Chip(n, cx, y, names[i], (Opt.Difficulty & bit) != 0,
                          Ui.Diff[i + 1], "opt:rank:" + (i + 1),
                          () => Opt.ToggleRank(bit));
            }
            y += ChipHeight() + Ui.Px(10);
            Ui.Wrapped(n, new Vector2(x, y + Ui.Px(AboutSize)), ranksAbout, AboutSize,
                       Ui.Dim, w, 3);
        }

        /// One switch: a square outlined tag, lit when it is on, in the colour
        /// of the thing it names.  The level list's own chip, copied rather than
        /// shared -- see below.
        ///
        /// **Two copies of fifteen lines, deliberately.**  `LevelList.Chip`
        /// closes over that panel's `Apply()` and its filter state, and the one
        /// thing this file has to keep saying is that these chips are *not*
        /// those chips.  A shared helper would be the place somebody later
        /// unifies the two masks by accident, which is the merge this file's
        /// header exists to argue against.  (Step 18's section chips are a third
        /// copy and are the frame's; if a *fourth* wants one, that is the moment
        /// it moves to Ui.)
        ///
        /// -> the x past its right edge, so a row of them is a fold.
        private float Chip(Node2D n, float x, float y, string text, bool on,
                           Color c, string name, Action act)
        {
            float h = ChipHeight();
            var r = new Rect2(x, y, ChipWidth(text), h);
            // The hit box is grown and the drawing is not -- an 18 px tag is a
            // tag a thumb misses.  See Ui.Touch.
            bool hot = View.Chrome.Add(Ui.Touch(r), name, act);
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
