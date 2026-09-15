// Phase 5, step 14: one text field, for the three places that take typing.
//
// **This is a refactor with one new caller.**  Step 11 gave the level list a
// filter field; step 3 had already given the editor three; step 14 wanted a
// fourth for the player's name, and four hand-rolled edit controls in one
// interface is three too many.  What was actually duplicated was small and
// tedious and wrong in slightly different ways each time: which characters a
// field accepts, what Backspace does, how long the value may get, where the
// caret sits when the value is empty, and whether the box is lit.
//
// Two of those were *wrong* rather than merely repeated, and both are fixed
// here by there being one copy:
//
//   * **The editor's fields had no length cap at all.**  Their own comment
//     said they "clamp to what a `char[31]` in a file the 2010 binary reads
//     back can hold" and they did no such thing -- `LevelRecord.Set` truncates
//     on the way to disk, so the surplus was silently dropped at save time
//     rather than refused at the keystroke.  `Max` is that cap, and it is the
//     file's own number (`NameEntry - 1`, `HintEntry - 1`), so what the field
//     shows is what the file will hold.
//
//   * **The filter took characters the corpus cannot contain.**  Its test was
//     `u >= 32 && u != 127` with no upper bound, so a Czech player typing `č`
//     got a query that could never match: every level name in every `.lvl` is
//     latin-1, and so is everything else these fields are compared against or
//     written into.  The bound is latin-1 here for all three, which is the
//     editor's own rule applied to the one field that had missed it.
//
// What is deliberately *not* here: a selection, a cursor that moves, Home/End,
// Delete, or a clipboard.  Every one of these fields is a short single-line
// value typed in one go -- a substring, a level name, a player's name -- and
// the caret is at the end because there is nowhere else it goes.  A real
// caret model is the day one of these grows long enough to need one, and none
// of them has.
using Godot;

namespace LaserTank.Game
{
    /// The state and the keys of one edit control.  `Ui.Field` is the drawing.
    internal sealed class TextField
    {
        /// What is in it.  Never null, never longer than `Max`.
        public string Text { get; private set; } = "";

        /// The most characters the field will hold, which is the most the
        /// thing behind it can store -- a `.lvl` field's `entry - 1`, a `.hs`
        /// record's four, the `.lpb` header's thirty.
        public int Max { get; }

        /// `strupr` (LTANK_D.C:229): the level list's own search folds the
        /// query and the row to upper case before comparing, so the field that
        /// feeds it stores what it compares.  False everywhere else -- a name
        /// is the player's to capitalise.
        public bool Upper { get; }

        public TextField(int max, bool upper = false)
        {
            Max = max;
            Upper = upper;
        }

        public bool Empty => Text.Length == 0;

        /// Load the field, clamped to `Max` and folded if it folds.  Used when
        /// a panel opens on a value it did not type -- the editor on a level's
        /// own name, the name panel on the INI's.
        public void Set(string s)
        {
            s ??= "";
            if (Upper) s = s.ToUpperInvariant();
            Text = s.Length > Max ? s.Substring(0, Max) : s;
        }

        /// One keystroke.  -> true when the text changed, which is the caller's
        /// cue to re-filter, mark the level modified, or whatever the field is
        /// wired to; the key itself is always the field's while it has focus,
        /// which is the accelerator table's own rule (`DONT use Keys that can
        /// be entered in the Author & Level Name field`, lt32l_us.inc:149).
        ///
        /// Backspace and the printable range, and nothing else: Tab, Enter and
        /// Escape are the *panel's* -- they move focus or close it -- so this
        /// never sees them.
        public bool Key(InputEventKey k)
        {
            if (k.Keycode == Godot.Key.Backspace)
            {
                if (Text.Length == 0) return false;
                Text = Text.Substring(0, Text.Length - 1);
                return true;
            }
            return Add((char)k.Unicode);
        }

        /// The character half on its own, for `--type`.
        ///
        /// latin-1 and printable, and **127 is not printable** -- it is what
        /// Godot reports for a Delete on some layouts, and a DEL byte in a
        /// level name is a byte the 2010 binary draws as a box.
        public bool Add(char c)
        {
            if (c < 32 || c == 127 || c > 255) return false;
            if (Text.Length >= Max) return false;
            Text += Upper ? char.ToUpperInvariant(c) : c;
            return true;
        }

        /// The box, the value or its placeholder, and the caret -- `Ui.Field`,
        /// with this field's own text.  `lit` is the border: a field with the
        /// caret in it is amber, one the pointer is over takes the lit border,
        /// and the level list's is amber whenever it holds a query, because
        /// that field always has the caret and the thing worth saying about it
        /// is whether a filter is on.
        public void Draw(CanvasItem ci, Rect2 r, string placeholder, bool caret,
                         Color border, bool hot = false, string label = null,
                         float size = 11.5f)
            => Ui.Field(ci, r, Text, placeholder, caret, border, hot, label, size);
    }
}
