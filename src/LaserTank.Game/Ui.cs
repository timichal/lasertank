// The port's own visual language -- Phase 5, steps 7 and 10.
//
// **Nothing in this file is a transliteration.**  Every other drawing file in
// this project answers to something in the 1996-2007 source; this one answers
// to nothing, on purpose.  PROGRESS.md, "The UI redesign this whole approach
// was a prelude to", draws the line out loud: the mechanics of the puzzles must
// be exactly the same and the UI need not be.  So the board, the laser, the
// sprites and the grid stay pixel-faithful and everything *around* them is
// designed rather than copied.
//
// Why a design system rather than colours at the use sites: the port draws its
// whole interface in immediate mode (CanvasItem.Draw*), which has no theme, no
// stylesheet and no cascade -- so the only way a panel here can look like a
// panel there is for both to ask this file.  Nine dialogs, an editor palette, a
// HUD and an overlay went from eleven private palettes to one.  **That is also
// what made step 10 affordable**: six files draw this interface and not one of
// them names a colour, a radius or a face, so re-deciding all three is an edit
// to this file and a relayout of one column, rather than a sweep.
//
// Immediate mode is kept -- rather than rebuilding the UI out of Control nodes
// -- because the *dialogs* are the part of this port with observable rules
// (modality, what stops the clock, what reaches AddKBuff: see BoardView's input
// routing), and those rules live in the key router, not in a node tree.
//
// ---------------------------------------------------------------------------
// STEP 10: what was wrong with step 7, and the rule that replaced it
// ---------------------------------------------------------------------------
//
// Step 7 produced a competent interface that looked like every other interface
// produced in 2025: a cool slate ground, cards at a uniform 10 px radius with
// hairline borders and drop shadows, a row of bordered stat tiles for the two
// counters, small letter-spaced caps over each one, and type asked for by the
// names `Segoe UI` / `Inter` / `SF Pro`.  Named one at a time those are all
// defensible.  Together they are a template, and a player said so.
//
// **The cause was a single argument in this file, and it was wrong.**  The old
// palette note ran: the four sprite sheets are saturated primaries and they
// disagree about their ground, so a chrome in any hue would fight whichever
// pack is loaded, so the chrome must be desaturated.  The premise is true and
// the conclusion does not follow.  Measured, the four grounds are
// `#949410` (internal), `#109494` (Eye Saver), `#60C000` (Comix) and `#187B00`
// (Warcraft II) -- olive, teal, green, dark green.  They *all* sit in the
// yellow-green-cyan arc.  Half the wheel, red through violet, was never
// contested by anything, so "desaturate" was never the only way to avoid the
// fight; it was just the way that also removed every trace of a point of view.
//
// So the rule this file now follows, in four parts:
//
//   1. **One dominant hue, used lavishly.**  Ember-amber is the complement of
//      every pack's ground, it is not the laser (which is red), and it appears
//      in no sheet as a field colour.  Step 7 had it too and spent it on about
//      two per cent of the pixels -- a timid evenly-spread palette with an
//      accent is still a timid evenly-spread palette.  Here it carries the
//      wordmark, the level name, the rails, the section labels and the live
//      numerals, and the chrome is warm all the way down.
//   2. **Corners are hard.**  One radius, 2 px, everywhere -- see `Rad`.  A
//      uniform generous radius on every surface is the loudest tell there is.
//   3. **Hierarchy by size and space, not by boxes.**  The info column has no
//      cards in it any more.  What separates its groups is a rail, a rule and
//      an amount of air; what ranks them is type size.  Three identically
//      bordered boxes stacked at an identical gap rank nothing.
//   4. **Two real faces, shipped.**  See `fonts/README.md`.
//
// The one thing deliberately *not* done, because it was asked for and declined:
// none of this borrows the original's own art.  `Control.bmp` is a tiled brick
// wall with sunken 3-D wells and a serif -- a complete and genuinely
// unmistakable design language, sitting unread in `original/src/`.  Using it
// would have been the safest possible answer to "this looks generated" and it
// would have made the chrome a costume.  The chrome here is of today and simply
// has an opinion.
using Godot;

namespace LaserTank.Game
{
    internal static class Ui
    {
        // ---- the palette ----------------------------------------------------
        //
        // Ember on warm black.  Every neutral below is warm -- there is no blue
        // in the greys at all, which is most of what separates this from the
        // slate it replaced: a cool grey reads as software, a warm one reads as
        // a panel with a lamp behind it.  The saturated colours are the amber
        // that carries the interface, one hot vermilion kept in reserve for the
        // two states that are genuinely alarming, and a cyan for the states
        // that are merely unusual.

        /// Behind everything.  Not black: a pure-black ground makes the dark
        /// packs' own black outlines disappear into it at the board's edge.
        public static readonly Color Bg = new Color(0.051f, 0.043f, 0.035f);
        /// The ground of a modal, the editor palette and the board well.
        public static readonly Color Surface = new Color(0.086f, 0.071f, 0.051f);
        /// A surface on a surface: a selected row, a pressed cap.
        public static readonly Color Raised = new Color(0.133f, 0.110f, 0.078f);
        public static readonly Color Border = new Color(0.239f, 0.196f, 0.137f);
        /// The border of the thing with focus.
        public static readonly Color BorderLit = new Color(0.361f, 0.290f, 0.180f);

        public static readonly Color Text = new Color(0.949f, 0.918f, 0.867f);
        /// Secondary copy: an author line, a unit beside a number.
        public static readonly Color Dim = new Color(0.647f, 0.592f, 0.506f);
        /// Tertiary: section labels, disabled keys, a rule.
        public static readonly Color Faint = new Color(0.420f, 0.373f, 0.298f);

        /// **The dominant colour, not an accent.**  If a frame of this
        /// interface has no amber in it something has gone wrong.
        public static readonly Color Accent = new Color(1.000f, 0.659f, 0.157f);
        public static readonly Color AccentDim = new Color(0.541f, 0.353f, 0.078f);
        /// The second voice, for states that are informational rather than
        /// actionable: playback, the editor's right-hand brush.  Cyan is the
        /// one cool thing allowed in here and it is the complement of the
        /// dominant, which is why it can be this quiet and still be seen.
        public static readonly Color Cyan = new Color(0.302f, 0.816f, 0.882f);

        public static readonly Color Good = new Color(0.357f, 0.851f, 0.541f);
        /// **The sharp accent.**  Spent on two things only -- recording, and a
        /// dead tank -- so that it means something when it appears.
        public static readonly Color Bad = new Color(1.000f, 0.290f, 0.169f);

        /// The board's own ground, under the sprites and behind the gutter the
        /// coordinate labels live in.  Darker than `Bg` rather than lighter:
        /// the board is the lit thing in this window and everything else is the
        /// room, so the well is a hole, not a card.
        public static readonly Color BoardWell = new Color(0.039f, 0.031f, 0.024f);

        /// The five difficulty colours.  The original has its own
        /// (`DifCList`, LTANK.C:532) and they are pure RGB primaries on a grey
        /// panel; these are the same five *ranks* re-picked for this ground,
        /// which is what "written down rather than locked down" means -- the
        /// note that the original had a table stays, the values are ours.
        /// Read as a ramp: they are one, and rank is the only thing they say.
        public static readonly Color[] Diff =
        {
            new Color(0.647f, 0.592f, 0.506f),   // unrated
            new Color(0.357f, 0.851f, 0.541f),   // Kids
            new Color(0.400f, 0.780f, 0.949f),   // Easy
            new Color(1.000f, 0.659f, 0.157f),   // Medium
            new Color(1.000f, 0.478f, 0.239f),   // Hard
            new Color(1.000f, 0.290f, 0.169f),   // Deadly
        };

        // ---- type -----------------------------------------------------------
        //
        // Two faces, both shipped under the OFL -- see `fonts/README.md` for
        // the files and for why the body of this interface is set in a
        // monospace rather than in a UI sans.  The short of it: the content is
        // fixed-pitch.  `LevelList` draws the original's own `%4d %-30.30s`
        // output and places its column rules in glyph units, the board is a
        // 16x16 grid labelled A1-P16, and the score is two counters against a
        // posted par.  Every number on screen is a measurement, so the
        // interface is set like an instrument and not like a product page.
        //
        // Shipping them rather than naming them also removes the last
        // platform-dependence in the drawing: step 7's `SystemFont` name lists
        // drew differently on Windows, on macOS and in a browser export, where
        // they fall through to Godot's own face -- which is proportional, so
        // the one thing in this interface that *must* be fixed-pitch was not,
        // on the one target the redesign's second pass wants to reach.

        private static Font _body, _bold, _display;

        private static Font Load(string file)
            => ResourceLoader.Load<Font>("res://fonts/" + file);

        /// The interface voice: IBM Plex Mono.  Labels, prose, numbers, rows.
        public static Font Sans => _body ??= Load("PlexMono-Regular.ttf");

        /// The same face, semibold.  Emphasis inside the body, never display.
        public static Font Bold => _bold ??= Load("PlexMono-SemiBold.ttf");

        /// An alias kept because the list panels ask for it by name, and asking
        /// for a monospace by name at the one place that genuinely depends on
        /// the pitch is worth keeping even now that it is the default.
        public static Font Mono => Sans;

        /// The display face: Archivo, variable, `wght` 100..900 and `wdth`
        /// 62..125 out of one file.  **Used for exactly two strings** -- the
        /// wordmark and the level's own name -- because a display face that
        /// turns up in a status bar is a UI sans with extra steps.
        public static Font Display => _display ??= Load("Archivo.ttf");

        /// A cut of the display face.  `wdth` below 100 is the condensed axis,
        /// which is what lets the level name be set large without a two-word
        /// title wrapping in a 300 px column.
        public static Font DisplayCut(int weight, int width = 100)
        {
            var v = new FontVariation { BaseFont = Display };
            v.VariationOpentype = new Godot.Collections.Dictionary
            {
                { Tag("wght"), weight },
                { Tag("wdth"), width },
            };
            return v;
        }

        /// An OpenType axis tag as the int Godot keys `variation_opentype` by:
        /// the four characters packed big-endian.  `TextServer.NameToTag` does
        /// this too, but it is an instance method on a singleton this file has
        /// no other reason to reach for, and the four axes in question have had
        /// the same four tags since 1994.
        private static int Tag(string s)
            => (s[0] << 24) | (s[1] << 16) | (s[2] << 8) | s[3];

        private static Font _h1, _mark;
        /// The level name.
        public static Font Title => _h1 ??= DisplayCut(700, 92);
        /// The wordmark.  Heavier and tighter than the title: it is a mark
        /// rather than a reading size, and it is the same three centimetres of
        /// the window every frame, so it can afford the weight.
        public static Font Mark => _mark ??= DisplayCut(800, 84);

        // ---- the scale ------------------------------------------------------
        //
        // One number, set once per frame from the window, that every size below
        // is expressed in.  The board scales continuously (BoardView.Layout),
        // and chrome that stayed at a fixed pixel size while it did would be a
        // postage stamp beside a 1200 px board and a wall beside a 380 px one.
        //
        // The range is deliberately narrow -- 0.85 to 1.35 against a 1.0 at the
        // middle preset -- because text is not a picture: doubling the board is
        // a zoom, and doubling the type is a different design.  Past the top of
        // the range the extra window goes to the board alone.

        private static float _scale = 1f;

        public static float Scale => _scale;

        /// Called once, at the top of a frame, before anything is drawn.
        public static void SetScale(Vector2 window)
        {
            // The short edge, against the size-2 preset's window height.  Width
            // alone would grow the type on a wide-and-short window, where there
            // is no vertical room for it.
            float s = Mathf.Min(window.X / 860f, window.Y / 660f);
            _scale = Mathf.Clamp(s, 0.85f, 1.35f);
        }

        /// A design-space length in real pixels.
        public static int Px(float n) => Mathf.RoundToInt(n * _scale);

        // ---- boxes ----------------------------------------------------------

        /// **One radius for the whole interface, and it is nearly square.**
        ///
        /// Callers still pass the radius they want -- 10 for a card, 14 for a
        /// dialog, `h/2` for a pill -- and every one of them lands here and
        /// comes out as 2 px.  Compressing at the primitive rather than at the
        /// forty-odd call sites is deliberate: it is one decision in one place,
        /// it cannot drift back, and the numbers the callers pass still record
        /// what each surface *was*, which is worth more in a diff than a column
        /// of zeroes would be.
        ///
        /// 2 rather than 0 because a pure square corner on a 1 px border at
        /// fractional scale aliases into a visible nick; 2 px reads as square
        /// and resolves cleanly at every scale in the range.
        private static int Rad(float requested) => requested <= 0f ? 0 : Mathf.Max(2, Px(2));

        /// A filled, bordered box.  `shadow` is the drop shadow's radius in
        /// design pixels; 0 is flat, and **everything except a true modal is
        /// flat** -- a lift on a surface that is not floating over anything is
        /// the other half of the tell that `Rad` handles.
        public static StyleBoxFlat Box(Color bg, Color border, float radius,
                                       float borderWidth = 1f, float shadow = 0f)
        {
            var s = new StyleBoxFlat { BgColor = bg, AntiAliasing = true };
            s.SetCornerRadiusAll(Rad(radius));
            if (borderWidth > 0f)
            {
                s.BorderColor = border;
                s.SetBorderWidthAll(Mathf.Max(1, Px(borderWidth)));
            }
            if (shadow > 0f)
            {
                // Hard and close rather than wide and soft: a wide blur under a
                // panel is a material metaphor, and this interface is not
                // pretending to be paper.  This one exists to prove the panel
                // is in front of the board, and stops there.
                s.ShadowColor = new Color(0f, 0f, 0f, 0.70f);
                s.ShadowSize = Px(Mathf.Min(shadow, 7f));
                s.ShadowOffset = new Vector2(0, Px(3));
            }
            return s;
        }

        /// A plain surface.  Kept for the two places that genuinely need a
        /// ground under them -- the board well and the editor's palette -- and
        /// **no longer used by the info column**, which is what step 10 was
        /// mostly about: the three stacked bordered cards are gone and the
        /// column is laid out on a rail instead.  See BoardView.DrawInfoColumn.
        public static void Card(CanvasItem ci, Rect2 r, float radius = 10f)
            => ci.DrawStyleBox(Box(Surface, Border, radius), r);

        /// A surface on a surface.
        public static void Tile(CanvasItem ci, Rect2 r, float radius = 8f)
            => ci.DrawStyleBox(Box(Raised, Border, radius), r);

        /// A dialog: lifted off the board, and **capped with a 2 px amber rule
        /// along its top edge**.  That cap is the one piece of chrome shared by
        /// every modal in the port, and it is doing the job the drop shadow
        /// used to do alone -- saying *this is in front, and it is the thing
        /// you are talking to* -- in the dominant colour rather than in a blur.
        public static void Dialog(CanvasItem ci, Rect2 r, float radius = 14f)
        {
            ci.DrawStyleBox(Box(Surface, BorderLit, radius, 1f, 7f), r);
            ci.DrawRect(new Rect2(r.Position.X, r.Position.Y,
                                  r.Size.X, Mathf.Max(2, Px(2))), Accent);
        }

        /// The dim wash a modal dialog puts over what it covers.  Without it a
        /// panel over the board reads as part of the board.
        public static void Scrim(CanvasItem ci, Rect2 r)
            => ci.DrawRect(r, new Color(0.02f, 0.015f, 0.01f, 0.70f));

        // ---- text -----------------------------------------------------------

        /// Body text.  `w` clips (Godot truncates rather than spilling); -1 is
        /// unclipped.  Returns the advance to the next baseline.
        public static float Write(CanvasItem ci, Vector2 at, string s, float size,
                                  Color c, float w = -1,
                                  HorizontalAlignment align = HorizontalAlignment.Left,
                                  Font font = null)
        {
            int px = Px(size);
            ci.DrawString(font ?? Sans, at, s, align, w, px, c);
            return px * 1.45f;
        }

        /// Wrapped body text, for the one string in this interface whose length
        /// is a level author's to decide: the hint.
        public static void Wrapped(CanvasItem ci, Vector2 at, string s, float size,
                                   Color c, float w, int maxLines = 6, Font font = null)
        {
            ci.DrawMultilineString(font ?? Sans, at, s, HorizontalAlignment.Left, w,
                                   Px(size), maxLines, c);
        }

        /// How tall `Wrapped` will be, so a block can size itself to its copy
        /// rather than reserving a fixed space that is usually empty.
        public static float WrappedHeight(string s, float size, float w,
                                          int maxLines = 6, Font font = null)
        {
            int px = Px(size);
            Vector2 v = (font ?? Sans).GetMultilineStringSize(s, HorizontalAlignment.Left, w,
                                                              px, maxLines);
            return v.Y;
        }

        /// A section label: small, letter-spaced, upper case.  The tracking is
        /// drawn by hand because DrawString has no tracking and these are the
        /// strings that need it -- at 10 px, caps set solid are a smear.
        ///
        /// The tracking is wider than step 7's now (0.16 em against 0.09): in a
        /// monospace the glyphs already carry their own even rhythm, so a
        /// timid track reads as an accident rather than as a decision.
        public static void Caps(CanvasItem ci, Vector2 at, string s, Color c,
                                float size = 10f)
        {
            int px = Px(size);
            float track = Mathf.Max(1f, px * 0.16f);
            float x = at.X;
            Font f = Bold;
            foreach (char ch in s.ToUpperInvariant())
            {
                string g = ch.ToString();
                ci.DrawString(f, new Vector2(x, at.Y), g, HorizontalAlignment.Left,
                              -1, px, c);
                x += f.GetStringSize(g, HorizontalAlignment.Left, -1, px).X + track;
            }
        }

        public static float CapsWidth(string s, float size = 10f)
        {
            int px = Px(size);
            float track = Mathf.Max(1f, px * 0.16f);
            float x = 0;
            foreach (char ch in s.ToUpperInvariant())
                x += Bold.GetStringSize(ch.ToString(), HorizontalAlignment.Left, -1, px).X
                     + track;
            return x;
        }

        public static float Width(string s, float size, Font font = null)
            => (font ?? Sans).GetStringSize(s, HorizontalAlignment.Left, -1, Px(size)).X;

        // ---- rails, rules and tags ------------------------------------------

        /// A horizontal rule.
        public static void Rule(CanvasItem ci, float x, float y, float w)
            => ci.DrawRect(new Rect2(x, y, w, Mathf.Max(1, Px(1))), Border);

        /// **The info column's spine.**  A vertical hairline that every group
        /// in the column hangs its left edge off, with the active group's span
        /// of it lit amber.  This is what replaced the borders of three cards:
        /// one line, continuous down the column, so the groups read as one
        /// list with ranks rather than as three peer objects -- and it is the
        /// asymmetry the layout was missing, because a rail has a side.
        public static void Rail(CanvasItem ci, float x, float y, float h, Color c)
            => ci.DrawRect(new Rect2(x, y, Mathf.Max(1, Px(2)), h), c);

        /// A status tag: `REC`, `MUTED`, a difficulty rank.  Draws at `x` and
        /// returns the x past its right edge, so a row of them is a fold.
        ///
        /// Square, and bordered rather than filled.  Step 7 drew these as
        /// filled rounded pills, which is the badge every component library
        /// ships; the shape is the only thing that changed and it is most of
        /// why the top bar no longer reads as a header component.
        public static float Pill(CanvasItem ci, float x, float y, string text,
                                 Color fg, Color bg, float size = 10f)
        {
            float tw = CapsWidth(text, size);
            float padX = Px(7), h = Px(size) + Px(8);
            var r = new Rect2(x, y, tw + 2 * padX, h);
            ci.DrawStyleBox(Box(bg, fg with { A = 0.55f }, 2f, 1f), r);
            Caps(ci, new Vector2(x + padX, y + h - Px(size) * 0.30f - Px(3)), text, fg, size);
            return r.End.X + Px(6);
        }

        /// A key on a legend line, drawn as a key.  This is the piece that
        /// makes the help overlay scannable -- a wall of `F5 rec  F6 save`
        /// is a sentence, and a column of caps is a list.
        public static float Keycap(CanvasItem ci, float x, float y, string key,
                                   float size = 11f)
        {
            int px = Px(size);
            var r = new Rect2(x, y, KeycapWidth(key, size), KeycapHeight(size));
            ci.DrawStyleBox(Box(Raised, BorderLit, 5f, 1f), r);
            ci.DrawString(Bold, new Vector2(x, y + r.Size.Y - Px(6)), key,
                          HorizontalAlignment.Center, r.Size.X, px, Accent);
            return r.End.X;
        }

        /// What Keycap will take, for the callers that have to know the box
        /// before they draw it -- a hit test registers the rectangle, and a
        /// hover has to paint under the cap rather than over it.
        public static float KeycapWidth(string key, float size = 11f)
            => Mathf.Max(Width(key, size, Bold), Px(size) * 0.75f) + 2 * Px(6);

        public static float KeycapHeight(float size = 11f) => Px(size) + Px(9);

        // ---- what the pointer needs ------------------------------------------
        //
        // Step 9's two additions.  Everything above is drawn; these are drawn
        // *and pointed at*, so they have a hot state -- and the rectangle they
        // are drawn in is the same one Hits.Add is given, which is the whole
        // trick (see Hits).

        /// The wash behind a hovered row.  A warm veil rather than a colour
        /// change: rows in this interface are already tinted by difficulty, by
        /// being current and by being selected, and a fourth signal in the same
        /// channel would be a fourth thing to tell apart.  It is drawn *under*
        /// the row's own content, so nothing it highlights changes shape.
        ///
        /// Amber at 7% rather than white at 5.5%: on a warm ground a white veil
        /// greys what it lifts, which is the wrong direction for a highlight.
        public static void Hot(CanvasItem ci, Rect2 r, float radius = 6f)
            => ci.DrawStyleBox(Box(Accent with { A = 0.070f }, AccentDim, radius, 1f), r);

        // ---- the text field ---------------------------------------------------

        /// **One field, drawn once.**  Three places in this port take typing --
        /// the level list's filter, the editor's three level fields and the
        /// name panel -- and until step 14 each drew its own box, its own
        /// placeholder and its own caret, three times, with three different
        /// answers to where the caret sits when the field is empty.  This is
        /// the drawing half; `TextField` is the state and the keys, and it is
        /// the only caller that is not a panel.
        ///
        /// The caret is a **block**, not a bar, and that is the one decision
        /// worth stating: at 11.5 px a one-pixel bar after a string is easy to
        /// miss, and in a field that has the keyboard whether or not anyone
        /// clicked it, "where does what I type go" is the whole question the
        /// widget has to answer.
        ///
        /// `label` is the editor's inline caps tag (`name`, `by`, `hint`) and
        /// is null for a field that has a title over it instead.
        /// `placeholder` is drawn in its place when the value is empty, **past
        /// the caret rather than under it** -- a block caret sitting on the
        /// first glyph of a hint reads as a rendering fault.
        public static void Field(CanvasItem ci, Rect2 r, string value,
                                 string placeholder, bool caret, Color border,
                                 bool hot = false, string label = null,
                                 float size = 11.5f)
        {
            ci.DrawStyleBox(Box(hot || caret ? Raised : Bg, border, 6f, 1f), r);

            float tx = r.Position.X + Px(9);
            if (label != null)
            {
                Caps(ci, new Vector2(tx - Px(1), r.Position.Y + r.Size.Y / 2f + Px(3)),
                     label, Faint, 9);
                // Measured rather than a fixed indent: `name` and `hint` set
                // wider than `by` at this size, and a constant that cleared
                // `by` ran `name` straight into its own value.
                tx += CapsWidth(label, 9) + Px(9);
            }

            bool empty = value.Length == 0;
            float baseline = r.Position.Y + r.Size.Y / 2f + Px(4);
            Write(ci, new Vector2(empty ? tx + Px(10) : tx, baseline),
                  empty ? placeholder : value, size, empty ? Faint : Text,
                  r.End.X - tx - Px(9));
            if (!caret) return;
            float cw = empty ? 0 : Width(value, size);
            ci.DrawRect(new Rect2(Mathf.Min(tx + cw + 2, r.End.X - Px(9)),
                                  r.Position.Y + Px(6),
                                  Mathf.Max(2, Px(2)), r.Size.Y - Px(12)),
                        Accent with { A = 0.85f });
        }

        /// **A finger is not a cursor.**  A target drawn as a 17-pixel keycap
        /// is a target a thumb misses, so the *hit* rectangle is grown to a
        /// floor and the drawing is left alone -- the chrome does not have to
        /// become chunky for it to be tappable.
        ///
        /// 32 design pixels, not the 44 the platform guidelines ask for, and
        /// that is a deliberate compromise with a chrome whose rows are 24
        /// apart: past the gap, neighbours overlap, and since draw order is z
        /// order the right-hand one would quietly steal the left one's edge.
        /// Where they do overlap it is only outside the drawn caps, so the
        /// nearest cap still wins -- which is the behaviour a thumb expects
        /// anyway.
        public static Rect2 Touch(Rect2 r, float min = 32f)
        {
            float w = Mathf.Max(r.Size.X, Px(min)), h = Mathf.Max(r.Size.Y, Px(min));
            return new Rect2(r.Position.X - (w - r.Size.X) / 2f,
                             r.Position.Y - (h - r.Size.Y) / 2f, w, h);
        }

        /// Where a panel's close button goes: the top-right corner, inside the
        /// padding, on the title's own line.
        public static Rect2 CloseRect(Rect2 panel, float pad)
        {
            float d = Px(22);
            return new Rect2(panel.End.X - pad - d, panel.Position.Y + pad - Px(2), d, d);
        }

        /// The button itself.  **Every panel here closes on a key** -- "any
        /// other key" for most of them, and Escape alone for the level list,
        /// whose alphabet belongs to its filter field (see LevelList's header,
        /// change 2).  Either way that is a complete answer with a keyboard and
        /// no answer at all without one, and this is that key for a finger.  Drawn as an outline
        /// rather than a filled control so it stays quieter than the panel's
        /// own title beside it.
        public static void CloseX(CanvasItem ci, Rect2 r, bool hot)
        {
            ci.DrawStyleBox(Box(hot ? Raised : new Color(0f, 0f, 0f, 0f),
                                hot ? BorderLit : Border, 6f, 1f), r);
            float m = r.Size.X * 0.33f;
            Color c = hot ? Accent : Dim;
            float t = Mathf.Max(1f, Px(1.4f));
            ci.DrawLine(r.Position + new Vector2(m, m), r.End - new Vector2(m, m), c, t, true);
            ci.DrawLine(new Vector2(r.End.X - m, r.Position.Y + m),
                        new Vector2(r.Position.X + m, r.End.Y - m), c, t, true);
        }
    }
}
