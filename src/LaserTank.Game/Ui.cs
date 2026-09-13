// The port's own visual language -- Phase 5, step 7 (the UI redesign).
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
// HUD and an overlay went from eleven private palettes to one.
//
// Immediate mode is kept -- rather than rebuilding the UI out of Control nodes
// -- because the *dialogs* are the part of this port with observable rules
// (modality, what stops the clock, what reaches AddKBuff: see BoardView's input
// routing), and those rules live in the key router, not in a node tree.  A
// Control-node rewrite would have bought layout containers at the price of
// re-deriving all of that.  DrawStyleBox is what makes immediate mode enough:
// StyleBoxFlat carries corner radii, borders and drop shadows, which is every
// modern-chrome primitive this interface needs.
//
// Web and native are the same code path.  SystemFont falls through its name
// list to Godot's own face when a platform has none of them -- which is exactly
// what a browser export does -- so nothing here needs a per-platform branch,
// and no font file needs shipping.
using Godot;

namespace LaserTank.Game
{
    internal static class Ui
    {
        // ---- the palette ----------------------------------------------------
        //
        // Dark, cool-grey, one warm accent.  The hue choice is forced by the
        // thing in the middle of the window: the sprite sheets are saturated
        // primaries on cyan or grey ground, and the four packs disagree about
        // which.  A chrome in any primary would fight whichever pack is loaded,
        // so the chrome is desaturated and the *only* saturated thing in it is
        // the accent -- amber, which none of the four sheets uses for a whole
        // tile and which therefore reads as interface rather than as board.

        /// Behind everything.  Not black: a pure-black ground makes the dark
        /// packs' own black outlines disappear into it at the board's edge.
        public static readonly Color Bg = new Color(0.043f, 0.055f, 0.078f);
        /// Cards, dialogs, the top bar.
        public static readonly Color Surface = new Color(0.078f, 0.094f, 0.129f);
        /// A card on a card: the stat tiles, a selected row.
        public static readonly Color Raised = new Color(0.110f, 0.133f, 0.180f);
        public static readonly Color Border = new Color(0.165f, 0.204f, 0.267f);
        /// The border of the thing with focus.
        public static readonly Color BorderLit = new Color(0.290f, 0.353f, 0.451f);

        public static readonly Color Text = new Color(0.902f, 0.918f, 0.949f);
        /// Secondary copy: an author line, a unit beside a number.
        public static readonly Color Dim = new Color(0.549f, 0.592f, 0.671f);
        /// Tertiary: section labels, disabled keys, the hint's own frame.
        public static readonly Color Faint = new Color(0.353f, 0.392f, 0.475f);

        /// The one saturated colour in the chrome.
        public static readonly Color Accent = new Color(0.961f, 0.710f, 0.267f);
        public static readonly Color AccentDim = new Color(0.545f, 0.396f, 0.145f);
        /// The second accent, for things that are informational rather than
        /// actionable: playback, the editor's right-hand brush.
        public static readonly Color Cyan = new Color(0.302f, 0.816f, 0.882f);

        public static readonly Color Good = new Color(0.357f, 0.851f, 0.541f);
        public static readonly Color Bad = new Color(1.000f, 0.420f, 0.420f);

        /// The board's own ground, under the sprites and behind the gutter the
        /// coordinate labels live in.
        public static readonly Color BoardWell = new Color(0.055f, 0.067f, 0.094f);

        /// The five difficulty colours.  The original has its own
        /// (`DifCList`, LTANK.C:532) and they are pure RGB primaries on a grey
        /// panel; these are the same five *ranks* re-picked for this ground,
        /// which is what "written down rather than locked down" means -- the
        /// note that the original had a table stays, the values are ours.
        public static readonly Color[] Diff =
        {
            new Color(0.549f, 0.592f, 0.671f),   // unrated
            new Color(0.357f, 0.851f, 0.541f),   // Kids
            new Color(0.400f, 0.780f, 0.949f),   // Easy
            new Color(0.961f, 0.710f, 0.267f),   // Medium
            new Color(0.973f, 0.522f, 0.318f),   // Hard
            new Color(1.000f, 0.420f, 0.420f),   // Deadly
        };

        // ---- type -----------------------------------------------------------
        //
        // Three faces and one scale.  The name lists are ordered per platform
        // -- Windows, then macOS, then a Linux/browser fallback -- and
        // SystemFont walks the list and then falls through to Godot's own face,
        // so a machine (or a web export) with none of them still draws.

        private static Font _sans, _bold, _mono;

        public static Font Sans => _sans ??= Make(400,
            "Segoe UI Variable Text", "Segoe UI", "Inter", "SF Pro Text",
            "Helvetica Neue", "DejaVu Sans", "Arial");

        public static Font Bold => _bold ??= Make(700,
            "Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI", "Inter",
            "SF Pro Display", "Helvetica Neue", "DejaVu Sans", "Arial");

        /// The list panels' rows are the original's own `%4d %-30.30s` sprintf
        /// output (LevelList), so their padding only lines up in a fixed pitch.
        /// The score numerals borrow it for the same reason: a moves counter
        /// that changes width as it counts makes the tile beside it twitch.
        public static Font Mono => _mono ??= Make(400,
            "Cascadia Mono", "Consolas", "SF Mono", "Menlo",
            "DejaVu Sans Mono", "Courier New");

        private static Font Make(int weight, params string[] names)
        {
            var f = new SystemFont
            {
                FontNames = names,
                FontWeight = weight,
                Antialiasing = TextServer.FontAntialiasing.Gray,
                SubpixelPositioning = TextServer.SubpixelPositioning.Auto,
            };
            return f;
        }

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

        /// A filled, rounded, optionally bordered box.  `shadow` is the drop
        /// shadow's radius in design pixels; 0 is flat.
        public static StyleBoxFlat Box(Color bg, Color border, float radius,
                                       float borderWidth = 1f, float shadow = 0f)
        {
            var s = new StyleBoxFlat { BgColor = bg, AntiAliasing = true };
            s.SetCornerRadiusAll(Px(radius));
            if (borderWidth > 0f)
            {
                s.BorderColor = border;
                s.SetBorderWidthAll(Mathf.Max(1, Px(borderWidth)));
            }
            if (shadow > 0f)
            {
                // Black rather than a tinted shadow: the ground is already near
                // black, so a coloured one reads as a glow instead of a lift.
                s.ShadowColor = new Color(0f, 0f, 0f, 0.45f);
                s.ShadowSize = Px(shadow);
                s.ShadowOffset = new Vector2(0, Px(shadow * 0.35f));
            }
            return s;
        }

        /// The standard card: a surface panel, hairline border, 10 px radius.
        public static void Card(CanvasItem ci, Rect2 r, float radius = 10f)
            => ci.DrawStyleBox(Box(Surface, Border, radius), r);

        /// A card that sits *on* a card.
        public static void Tile(CanvasItem ci, Rect2 r, float radius = 8f)
            => ci.DrawStyleBox(Box(Raised, Border, radius), r);

        /// A dialog: the same surface, lifted off the board with a shadow and a
        /// brighter border, because it is modal and should look it.
        public static void Dialog(CanvasItem ci, Rect2 r, float radius = 14f)
            => ci.DrawStyleBox(Box(Surface, BorderLit, radius, 1f, 18f), r);

        /// The dim wash a modal dialog puts over what it covers.  Without it a
        /// panel over the board reads as part of the board.
        public static void Scrim(CanvasItem ci, Rect2 r)
            => ci.DrawRect(r, new Color(0.02f, 0.03f, 0.04f, 0.62f));

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
                                   Color c, float w, int maxLines = 6)
        {
            ci.DrawMultilineString(Sans, at, s, HorizontalAlignment.Left, w,
                                   Px(size), maxLines, c);
        }

        /// How tall `Wrapped` will be, so a card can size itself to its copy
        /// rather than reserving a fixed block that is usually empty.
        public static float WrappedHeight(string s, float size, float w, int maxLines = 6)
        {
            int px = Px(size);
            Vector2 v = Sans.GetMultilineStringSize(s, HorizontalAlignment.Left, w,
                                                    px, maxLines);
            return v.Y;
        }

        /// A section label: small, letter-spaced, upper case, faint.  The
        /// tracking is drawn by hand because DrawString has no tracking and
        /// these are the strings that need it -- at 10 px, caps set solid are a
        /// smear.
        public static void Caps(CanvasItem ci, Vector2 at, string s, Color c,
                                float size = 10f)
        {
            int px = Px(size);
            float track = Mathf.Max(1f, px * 0.09f);
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
            float track = Mathf.Max(1f, px * 0.09f);
            float x = 0;
            foreach (char ch in s.ToUpperInvariant())
                x += Bold.GetStringSize(ch.ToString(), HorizontalAlignment.Left, -1, px).X
                     + track;
            return x;
        }

        public static float Width(string s, float size, Font font = null)
            => (font ?? Sans).GetStringSize(s, HorizontalAlignment.Left, -1, Px(size)).X;

        // ---- pills and keycaps ----------------------------------------------

        /// A status chip: `REC`, `MUTED`, a difficulty rank.  Draws at `x` and
        /// returns the x past its right edge, so a row of them is a fold.
        public static float Pill(CanvasItem ci, float x, float y, string text,
                                 Color fg, Color bg, float size = 10f)
        {
            float tw = CapsWidth(text, size);
            float padX = Px(7), h = Px(size) + Px(8);
            var r = new Rect2(x, y, tw + 2 * padX, h);
            ci.DrawStyleBox(Box(bg, fg with { A = 0.45f }, h / 2f, 1f), r);
            Caps(ci, new Vector2(x + padX, y + h - Px(size) * 0.30f - Px(3)), text, fg, size);
            return r.End.X + Px(6);
        }

        /// A key on a legend line, drawn as a key: a raised, rounded cap with
        /// the glyph in it.  This is the piece that makes the help overlay
        /// scannable -- a wall of `F5 rec  F6 save  F7 play` is a sentence, and
        /// a column of caps is a list.
        public static float Keycap(CanvasItem ci, float x, float y, string key,
                                   float size = 11f)
        {
            int px = Px(size);
            var r = new Rect2(x, y, KeycapWidth(key, size), KeycapHeight(size));
            ci.DrawStyleBox(Box(Raised, BorderLit, 5f, 1f), r);
            ci.DrawString(Bold, new Vector2(x, y + r.Size.Y - Px(6)), key,
                          HorizontalAlignment.Center, r.Size.X, px, Text);
            return r.End.X;
        }

        /// What Keycap will take, for the callers that have to know the box
        /// before they draw it -- a hit test registers the rectangle, and a
        /// hover has to paint under the cap rather than over it.
        public static float KeycapWidth(string key, float size = 11f)
            => Mathf.Max(Width(key, size, Bold), Px(size) * 0.75f) + 2 * Px(6);

        public static float KeycapHeight(float size = 11f) => Px(size) + Px(9);

        /// A horizontal rule inside a card.
        public static void Rule(CanvasItem ci, float x, float y, float w)
            => ci.DrawRect(new Rect2(x, y, w, Mathf.Max(1, Px(1))), Border);

        // ---- what the pointer needs ------------------------------------------
        //
        // Step 9's two additions.  Everything above is drawn; these are drawn
        // *and pointed at*, so they have a hot state -- and the rectangle they
        // are drawn in is the same one Hits.Add is given, which is the whole
        // trick (see Hits).

        /// The wash behind a hovered row.  A light veil rather than a colour
        /// change: rows in this interface are already tinted by difficulty, by
        /// being current and by being selected, and a fourth signal in the same
        /// channel would be a fourth thing to tell apart.  It is drawn *under*
        /// the row's own content, so nothing it highlights changes shape.
        public static void Hot(CanvasItem ci, Rect2 r, float radius = 6f)
            => ci.DrawStyleBox(Box(new Color(1f, 1f, 1f, 0.055f), Border, radius, 1f), r);

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

        /// The button itself.  **Every panel here closes on "any other key"**,
        /// which is a complete answer with a keyboard and no answer at all
        /// without one -- this is that key, for a finger.  Drawn as an outline
        /// rather than a filled control so it stays quieter than the panel's
        /// own title beside it.
        public static void CloseX(CanvasItem ci, Rect2 r, bool hot)
        {
            ci.DrawStyleBox(Box(hot ? Raised : new Color(0f, 0f, 0f, 0f),
                                hot ? BorderLit : Border, 6f, 1f), r);
            float m = r.Size.X * 0.33f;
            Color c = hot ? Text : Dim;
            float t = Mathf.Max(1f, Px(1.4f));
            ci.DrawLine(r.Position + new Vector2(m, m), r.End - new Vector2(m, m), c, t, true);
            ci.DrawLine(new Vector2(r.End.X - m, r.Position.Y + m),
                        new Vector2(r.Position.X + m, r.End.Y - m), c, t, true);
        }
    }
}
