// Phase 5, step 14: the one place the player says who they are.
//
// **Step 18 made it a section rather than a panel** (next-steps item 14).  It
// was `Ctrl+N`'s own dialog from step 14 until the merge; it is the Player
// section of `Ctrl+O`'s dialog now, and `Ctrl+N` is unbound and free -- it was
// this port's own invention, so nothing of the original's goes with it.  Its
// old name in this tree was `NameMenu`.
//
// **Two dialogs in the original, one row here, and the merge is the point.**
// The 2007 program asks for a name twice:
//
//   HSBox      LTANK_D.C:634.  Opens the instant a level is beaten, over a
//              board that has just turned green, and wants *initials* for the
//              score it is about to post.  `[DATA] Player`.
//   RecordBox  LTANK_D.C:983.  Opens on the first recording saved in a
//              session and wants an *author* for the `.lpb` header.
//              `[DATA] Record Author`.
//
// They are the same question.  Nothing in either box is about the level just
// played -- neither asks what you thought of it, neither offers a choice, and
// neither can be answered differently without the two files disagreeing about
// who you are.  They are two because a 1996 dialog was the only surface the
// program had: there was no settings screen to put a name on, so the name was
// asked for at the moment it was needed, twice, by whichever feature needed it.
//
// So: **one field, and it lives here rather than in the way.**  `Options.Name`
// is the value and writes both of the original's keys, so a `LaserTank.ini`
// this port wrote is one the 2010 binary reads back with both of its dialogs
// already answered.  What a `.hs` record can hold is four characters
// (`THSREC.NameEntry`), which this section shows as it is typed -- the one place
// the merge could surprise somebody is the moment a long name becomes four
// letters on a score line, so it is drawn rather than explained.
//
// ---------------------------------------------------------------------------
// The Save/Cancel pair, and why step 18 took it away
// ---------------------------------------------------------------------------
//
// Step 14 gave this panel two answers drawn as the keys that give them, and the
// argument was sound while it was a panel of its own: the graphics and language
// pickers apply their choice *live*, so an Esc that undid it would be undoing
// what is already on screen; nothing here was applied live, and a text field
// with no way out that does not commit is a field you cannot open to look at.
//
// **What changed is the surroundings, not the argument.**  This is one section
// of a panel whose whole design constraint is 226's -- applies immediately, no
// Cancel, no OK (SettingsMenu's header) -- and one field that needs Enter
// inside it is the exception that makes the rule unreadable.  So the field
// commits when it is *left*: Esc, Enter, the close button, a click outside, or
// a move to another section.  The half of step 14's argument that was actually
// load-bearing survives untouched, because it was never about Cancel:
// `Options.SetName` declines a write when the text has not changed, so opening
// the panel to see what the name is still writes no file -- and a
// `LaserTank.ini` shared with the 2010 binary still does not gain a
// `Record Author` line because somebody looked.
//
// **The clock keeps running underneath**, as it does behind every panel here,
// and for the reason recorded in step 2: command 226 never calls `GameOn(FALSE)`
// and `DialogBox`'s loop still dispatches `WM_TIMER`, so an exposed tank can die
// while a dialog is up.  This panel is the port's own and could have chosen
// otherwise; it does not, because "every dialog here behaves that way except the
// settings one" is a rule with an exception in it.
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// The **Player** section: the one place the player says who they are.
    internal sealed class NameSection : SettingsSection
    {
        private readonly TextField _field = new TextField(Options.NameMax);

        public NameSection(BoardView view) : base(view) { }

        public override string TitleKey => "name.title";
        public override string Id => "player";

        /// What is in the field right now -- for `--type`'s log, which is the
        /// only instrument that can reach a text field (see BoardView.Type).
        public string Text => _field.Text;

        /// The four characters a `.hs` record would carry, as the section draws
        /// them.  Read off the field rather than off Options, because the point
        /// of showing them is to show them *before* the name is stored.
        public string Initials => _field.Text.Length > THSREC.NameEntry - 1
                                  ? _field.Text.Substring(0, THSREC.NameEntry - 1)
                                  : _field.Text;

        /// Open on the name as it stands, with the caret at the end of it.
        public override void Enter() => _field.Set(View.Options.Name);

        /// The commit, and the only one: `SetName` does nothing when the text
        /// has not changed, so a section entered and left writes no file.
        public override void Leave() => View.Options.SetName(_field.Text);

        /// Every key the frame did not take is the field's.  There is no key it
        /// can decline without it turning into an accelerator behind a caret --
        /// which is exactly what ACC2's own comment warns about
        /// (lt32l_us.inc:149) -- and it is why the frame's own way out is Esc
        /// and the arrows rather than a bare letter.
        public override void Key(InputEventKey k) => _field.Key(k);

        // ---- drawing --------------------------------------------------------
        private const float AboutSize = 11.5f;

        private static float FieldH => Ui.Px(32);

        /// The `scores as` line is what this section has to be wide enough for;
        /// the copy above it wraps, so it costs height rather than width.
        public override float Want()
            => Ui.CapsWidth(Lang["name.scoresAs"], 9) + Ui.Px(10)
               + Ui.Width("MMMM", 12.5f, Ui.Mono) + Ui.Px(40);

        public override float Height(float w)
            => FieldH + Ui.Px(22)
               + Ui.WrappedHeight(Lang["name.about"], AboutSize, w, 3) + Ui.Px(24);

        /// A field, a line of copy, and the four characters a score line gets.
        ///
        /// The `scores as` line is the one piece of copy that has to earn its
        /// place, and it does: four characters is all a `.hs` record holds, and
        /// the one moment the merge could surprise somebody is when a long name
        /// turns into four letters on a score line.  So it is shown as it is
        /// typed rather than explained.
        public override void Draw(Node2D n, float x, float y, float w)
        {
            string about = Lang["name.about"], scoresAs = Lang["name.scoresAs"];

            // The field.  A swallow rather than a target, for the level list's
            // reason: it always has the caret while this section is up, so a
            // click on it has nothing to do -- and a live target here would be a
            // box that has to clear the touch floor for no behaviour.
            var field = new Rect2(x, y, w, FieldH);
            View.Chrome.Swallow(field, "name");
            _field.Draw(n, field, Lang["name.placeholder"], caret: true,
                        border: Ui.Accent, size: 12.5f);
            y += FieldH + Ui.Px(22);

            Ui.Wrapped(n, new Vector2(x, y), about, AboutSize, Ui.Dim, w, 3);
            y += Ui.WrappedHeight(about, AboutSize, w, 3) + Ui.Px(14);

            // The four characters, in the accent, beside a faint label: the
            // value is the thing being said and the words are the caption.
            string initials = Initials;
            Ui.Caps(n, new Vector2(x, y), scoresAs, Ui.Faint, 9);
            Ui.Write(n, new Vector2(x + Ui.CapsWidth(scoresAs, 9) + Ui.Px(10),
                                    y + Ui.Px(1)),
                     initials.Length > 0 ? initials : "—", 12.5f,
                     initials.Length > 0 ? Ui.Accent : Ui.Faint,
                     w, HorizontalAlignment.Left, Ui.Mono);
        }
    }
}
