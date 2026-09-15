// Phase 5, step 14: the one place the player says who they are.
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
// (`THSREC.NameEntry`), which this panel shows as it is typed -- the one place
// the merge could surprise somebody is the moment a long name becomes four
// letters on a score line, so it is drawn rather than explained.
//
// ---------------------------------------------------------------------------
// The two properties that differ from the other panels, and why
// ---------------------------------------------------------------------------
//
// **It has a Cancel, where GraphicsMenu and LanguageMenu deliberately do not.**
// Those two apply their choice *live* -- the pack is loaded, the labels are
// already in the new language -- so an Esc that undid it would be undoing what
// is already on screen, and the original's own 226 has no Cancel either
// (LTANK_D.C:1247: Close and Cancel run the same code).  Nothing here is
// applied live: a name is a string, and the board behind the panel does not
// change as it is typed.  A text field with no way out that does not commit is
// a field you cannot open to look at.  So Enter saves, Esc does not, and both
// of them are drawn as buttons carrying their own key.
//
// **The clock keeps running underneath**, as it does behind the graphics and
// language pickers, and for the reason recorded there: command 226 never calls
// `GameOn(FALSE)` and `DialogBox`'s loop still dispatches `WM_TIMER`, so an
// exposed tank can die while a dialog is up.  This panel is the port's own and
// could have chosen otherwise; it does not, because "every dialog here behaves
// that way except the settings one" is a rule with an exception in it.
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// `Ctrl+N` -- the name panel.  The first settings surface in the port, and
    /// the shape next-steps item 7 will grow when the typed store lands.
    public sealed class NameMenu
    {
        private readonly BoardView _view;
        private readonly TextField _field = new TextField(Options.NameMax);

        public bool Open { get; private set; }

        public NameMenu(BoardView view) { _view = view; }

        /// What is in the field right now -- for `--type`'s log, which is the
        /// only instrument that can reach a text field (see BoardView.Type).
        public string Text => _field.Text;

        /// The four characters a `.hs` record would carry, as the panel draws
        /// them.  Read off the field rather than off Options, because the point
        /// of showing them is to show them *before* the name is saved.
        public string Initials => _field.Text.Length > THSREC.NameEntry - 1
                                  ? _field.Text.Substring(0, THSREC.NameEntry - 1)
                                  : _field.Text;

        /// Open on the name as it stands, with the caret at the end of it.
        public void Show(string current)
        {
            _field.Set(current);
            Open = true;
        }

        /// Esc, the close button and a click outside: the panel goes and the
        /// INI is not touched.
        public void Cancel() { Open = false; }

        /// Enter and the Save button.  `Options.SetName` does nothing when the
        /// text has not changed, so opening the panel and closing it again
        /// writes no file -- which matters more here than it looks: a
        /// `LaserTank.ini` shared with the 2010 binary should not gain a
        /// `Record Author` line because somebody pressed Ctrl+N to see what it
        /// was.
        public void Save()
        {
            Open = false;
            _view.Options.SetName(_field.Text);
        }

        /// -> true when the key was the panel's.  Everything is, while it is
        /// up: this is a text field, so there is no key it can decline without
        /// it turning into an accelerator behind a caret -- which is exactly
        /// what ACC2's own comment warns about (lt32l_us.inc:149).
        public bool Key(InputEventKey k)
        {
            if (!Open) return false;
            switch (k.Keycode)
            {
                case Godot.Key.Escape: Cancel(); break;
                case Godot.Key.Enter:
                case Godot.Key.KpEnter: Save(); break;
                default: _field.Key(k); break;
            }
            return true;
        }

        // ---- drawing --------------------------------------------------------
        private static int Pad => Ui.Px(16);

        /// A dialog over the board, sized from its content.
        ///
        /// **It is the quit prompt's shape, not the list panels'** -- a title,
        /// a field, two lines of copy and two answers drawn as the keys that
        /// give them.  The list panels carry a footer because they are things
        /// you navigate; this is a question with two answers, and DrawQuitAsk
        /// already settled what one of those looks like here.  Its own header
        /// names this panel as the thing it was the first of: *"what they need
        /// past this is a text field and a third button"* -- this is the text
        /// field, and it needed no third button, because the merge took one of
        /// the two questions away.
        ///
        /// The `scores as` line is the one piece of copy that has to earn its
        /// place, and it does: four characters is all a `.hs` record holds, and
        /// the one moment the merge could surprise somebody is when a long name
        /// turns into four letters on a score line.  So it is shown as it is
        /// typed rather than explained.
        public void Draw(Node2D n, Font font, Rect2 host, Strings lang)
        {
            string about = lang["name.about"];
            string scoresAs = lang["name.scoresAs"];
            string save = lang["name.save"], cancel = lang["name.cancel"];

            float buttons = Ui.KeycapWidth("Enter", 11f) + Ui.Width(save, 11.5f)
                            + Ui.KeycapWidth("Esc", 11f) + Ui.Width(cancel, 11.5f)
                            + Ui.Px(64);
            float want = Mathf.Max(Ui.Width(about, 11.5f), buttons);
            want = Mathf.Max(want, Ui.CapsWidth(lang["name.title"], 12) + Ui.Px(60));
            float w = Mathf.Min(Mathf.Max(Ui.Px(360), want + 2 * Pad),
                                host.Size.X - Ui.Px(40));

            float fieldH = Ui.Px(32);
            float capH = Ui.KeycapHeight(11f);
            float height = Pad + Ui.Px(12) + Ui.Px(16) + fieldH + Ui.Px(22)
                           + Ui.Px(20) + Ui.Px(24) + capH + Ui.Px(4) + Pad;
            height = Mathf.Min(height, host.Size.Y - Ui.Px(40));

            Ui.Scrim(n, host);
            // Click-off is Cancel, which is the pointer's form of the key rule
            // -- Esc leaves the name as it was, so every other *place* does too.
            _view.Chrome.Add(host, "scrim", Cancel);
            var panel = new Rect2(
                Mathf.Round(host.Position.X + (host.Size.X - w) / 2f),
                Mathf.Round(host.Position.Y + (host.Size.Y - height) / 2f), w, height);
            Ui.Dialog(n, panel);
            _view.Chrome.Swallow(panel);

            float x = panel.Position.X + Pad;
            float wInner = panel.Size.X - 2 * Pad;
            float y = panel.Position.Y + Pad + Ui.Px(11);

            Ui.Caps(n, new Vector2(x, y), lang["name.title"], Ui.Text, 12);
            Rect2 close = Ui.CloseRect(panel, Pad);
            Ui.CloseX(n, close, _view.Chrome.Add(Ui.Touch(close), "close", Cancel));
            y += Ui.Px(12);
            Ui.Rule(n, x, y, wInner);
            y += Ui.Px(16);

            // The field.  A swallow rather than a target, for the level list's
            // reason: it always has the caret, so a click on it has nothing to
            // do -- and a live target here would be a box that has to clear the
            // touch floor for no behaviour.
            var field = new Rect2(x, y, wInner, fieldH);
            _view.Chrome.Swallow(field, "name");
            _field.Draw(n, field, lang["name.placeholder"], caret: true,
                        border: Ui.Accent, size: 12.5f);
            y += fieldH + Ui.Px(22);

            Ui.Write(n, new Vector2(x, y), about, 11.5f, Ui.Dim, wInner);
            y += Ui.Px(20);

            // The four characters, in the accent, beside a faint label: the
            // number is the thing being said and the words are the caption.
            string initials = Initials;
            Ui.Caps(n, new Vector2(x, y), scoresAs, Ui.Faint, 9);
            Ui.Write(n, new Vector2(x + Ui.CapsWidth(scoresAs, 9) + Ui.Px(10), y + Ui.Px(1)),
                     initials.Length > 0 ? initials : "\u2014", 12.5f,
                     initials.Length > 0 ? Ui.Accent : Ui.Faint,
                     wInner, HorizontalAlignment.Left, Ui.Mono);
            y += Ui.Px(24);

            // Enter first, Esc second, in DrawQuitAsk's own vocabulary: the
            // answer keys drawn as keys, far enough apart that no single
            // repeated gesture reaches both.
            float sx = Answer(n, x, y, "Enter", save, true, "name:save", Save);
            Answer(n, sx + Ui.Px(14), y, "Esc", cancel, false, "name:cancel", Cancel);
        }

        /// One answer: a keycap, its word, and the pair as one target.
        /// -> the x past its right edge.
        private float Answer(Node2D n, float x, float y, string cap, string text,
                             bool primary, string name, System.Action act)
        {
            float w = Ui.KeycapWidth(cap, 11f) + Ui.Px(9) + Ui.Width(text, 11.5f);
            var r = new Rect2(x - Ui.Px(8), y - Ui.Px(4), w + 2 * Ui.Px(8),
                              Ui.KeycapHeight(11f) + Ui.Px(8));
            bool hot = _view.Chrome.Add(r, name, act);
            if (hot) Ui.Hot(n, r);
            float kx = Ui.Keycap(n, x, y, cap, 11f) + Ui.Px(9);
            Ui.Write(n, new Vector2(kx, y + Ui.Px(15)), text, 11.5f,
                     primary || hot ? Ui.Text : Ui.Dim);
            return r.End.X;
        }
    }
}
