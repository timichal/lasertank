// Phase 5, step 9: the chrome answers the mouse.
//
// **What this is for.**  Step 7 drew a whole interface -- a top bar, an info
// column, four list panels, a help overlay, a quit prompt -- and every piece of
// it was a *label for a key*.  The keycaps were pictures of keys, the pills were
// read-outs, the rows were things the arrows moved a cursor through.  Nothing
// was clickable, because the port was keyboard-driven and had been since Phase
// 5 step 1.  This file is the half that was missing.
//
// **Why a hit list rather than Control nodes.**  Ui.cs's header says why the UI
// is immediate mode -- the dialogs' observable rules (modality, what stops the
// clock, what reaches AddKBuff) live in the key router, not in a node tree, and
// a Control-node rewrite would have to re-derive all of them.  That argument
// did not change when the mouse arrived, so the mouse is fitted to immediate
// mode instead: **every clickable thing registers the rectangle it just drew**,
// and the click is tested against what the last frame put on screen.
//
// That inverts the usual bug.  In a retained UI the hit box and the drawing are
// two descriptions of one rectangle and they drift; here there is one
// rectangle, passed to the draw call and to Add in the same breath, so a
// button that moves takes its hit box with it and a button that is not drawn is
// not clickable.  The cost is one frame of staleness, which at 60+ fps and with
// _Process queueing a redraw every frame is not observable: input is delivered
// before _Process and _Draw, so a click that closes a panel is followed by a
// frame that rebuilds the list before any further click can land.
//
// **Headless draws nothing, so the list is empty**, and clicks fall straight
// through to the two arms the window proc already had -- which is exactly what
// tools/mouse_check.py and tools/editor_check.py drive.  Neither gate had to
// learn that the chrome exists.
//
// **Draw order is z order**, so Click walks the list backwards: the last thing
// drawn is the thing on top, and a modal panel registers its scrim first so
// everything under it is covered by one rectangle rather than by a rule.
using System;
using System.Collections.Generic;
using Godot;

namespace LaserTank.Game
{
    /// The chrome's clickable rectangles for one frame.
    internal sealed class Hits
    {
        /// A null action is a *swallow*: the region consumes the click and does
        /// nothing.  That is what a modal panel's own body is -- clicking the
        /// middle of a dialog must not reach the board behind it, and must not
        /// close the dialog either.
        /// **The name is not decoration.**  It is what `--dump-hits` prints
        /// and what tools/chrome_check.py drives: a target called `key:U` says
        /// that clicking it is supposed to be pressing U, so the gate can click
        /// its centre, press the key, and diff the two -- which is the same
        /// two-implementations rule the sprite sheets and the list dialogs are
        /// held to, applied to the third arm.  A target whose name is not a key
        /// (`close`, `row:12`, `field:1`) is checked for being live and inside
        /// its panel and nothing more.
        private readonly List<(Rect2 R, string Name, Action Act)> _items = new();

        /// Where the pointer was last seen.  Off-screen until something moves,
        /// so a touch-only session never shows a hover state it cannot undo.
        private Vector2 _at = new Vector2(-1e6f, -1e6f);

        /// The region the pointer is in, resolved at the *end* of the previous
        /// frame.  Hover has to be one frame behind for the same reason clicks
        /// are: whether a rectangle is on top is only known once everything has
        /// been registered, and Add is called while the answer is still being
        /// built.  Comparing rectangles rather than indices survives a frame
        /// where the list grows or shrinks around it.
        private Rect2 _hot;
        private bool _hasHot;

        /// The pointer moved.  Also called on a click, so a tap on a touch
        /// screen leaves the hover where the finger was -- which is what the
        /// emulated mouse does anyway.
        public void Point(Vector2 p) => _at = p;

        /// Top of the frame, before anything is drawn.
        public void Begin() => _items.Clear();

        /// Register what was just drawn.  -> true when the pointer is in it,
        /// which is the caller's cue to draw itself hot -- so the highlight and
        /// the hit box cannot disagree.
        public bool Add(Rect2 r, string name, Action act)
        {
            _items.Add((r, name, act));
            return _hasHot && act != null && r == _hot;
        }

        /// A region that eats a click and does nothing: a dialog's own body.
        public void Swallow(Rect2 r, string name = "body") => _items.Add((r, name, null));

        /// `--dump-hits`: what this frame registered, in draw order, so a gate
        /// can aim at the chrome without a copy of the layout in Python.  The
        /// centre is what it clicks; the rectangle is there so the gate can
        /// check that a target is inside the window and does not sit under
        /// something later in the list.
        public string Dump()
        {
            var sb = new System.Text.StringBuilder();
            foreach ((Rect2 r, string name, Action act) in _items)
                sb.Append($"hit {name} {(int)r.Position.X},{(int)r.Position.Y},"
                          + $"{(int)r.Size.X},{(int)r.Size.Y} "
                          + $"{(act == null ? "swallow" : "live")}\n");
            return sb.ToString();
        }

        /// The third arm of the window proc, and the one that comes first.
        /// -> true when the chrome took the click, which is the caller's cue to
        /// stop: nothing here may reach MBuffer or the editor's brush.
        public bool Click(Vector2 p)
        {
            _at = p;
            for (int i = _items.Count - 1; i >= 0; i--)
                if (_items[i].R.HasPoint(p))
                {
                    Took = _items[i].Name;
                    _items[i].Act?.Invoke();
                    return true;
                }
            Took = null;
            return false;
        }

        /// Which target the last click landed on, for --click's log: a gate
        /// that clicks the centre of `key:U` and is told it hit `key:R` has
        /// found two rectangles that overlap, which is the failure this arm is
        /// most likely to have and the one a screenshot hides.
        public string Took { get; private set; }

        /// End of the frame: resolve the hover, and say so with the cursor.
        /// The pointing hand is the only affordance a *desktop* player gets for
        /// free -- everything else in this interface still looks like a label,
        /// because that is what it is.
        public void End()
        {
            _hasHot = false;
            for (int i = _items.Count - 1; i >= 0; i--)
                if (_items[i].R.HasPoint(_at))
                {
                    _hot = _items[i].R;
                    _hasHot = _items[i].Act != null;
                    break;
                }
            Input.SetDefaultCursorShape(_hasHot ? Input.CursorShape.PointingHand
                                                : Input.CursorShape.Arrow);
        }
    }
}
