// Phase 5, step 2: the way in.  GraphBox (LTANK_D.C:1202), which is command 226
// on the Options menu, as an overlay over the board.
//
// The original is a modal dialog with two radio buttons, a listbox of the .ltg
// packs found in Graphics_Dir, the selected pack's author and description, and
// a Close button.  Three of its properties are load-bearing and are kept here:
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
// board; that is its own piece of drawing and it is not step 2's).
using System.Collections.Generic;
using Godot;

namespace LaserTank.Game
{
    public sealed class GraphicsMenu
    {
        // IDM_GRAPHBOX_00 and _05, the US English strings (LT32L_US.H:176).
        // Step 6 replaces these with language.dat's.
        private const string Title = "Select Graphics Set";
        private const string AuthorLabel = "Author :";

        private readonly BoardView _view;
        private List<Pack> _packs;
        private int _sel;

        public bool Open { get; private set; }

        public GraphicsMenu(BoardView view) { _view = view; }

        public Pack Selected => _packs != null && _packs.Count > 0 ? _packs[_sel] : null;

        /// Open on the pack that is currently drawn, so the cursor starts where
        /// the eye is -- LB_SELECTSTRING on ltgCur.Name (LTANK_D.C:1234).
        public void Show(List<Pack> packs, Pack current)
        {
            _packs = packs;
            _sel = packs.IndexOf(current);
            if (_sel < 0) _sel = 0;
            Open = true;
        }

        public void Close() { Open = false; _view.PersistGraphics(); }

        /// -> true when the key was the menu's.  Everything is, while it is up:
        /// a modal dialog does not pass keystrokes to the window behind it, and
        /// that is the only reason it is safe for this to use the arrows and
        /// space at all -- they are VK 37..40 and 32, the keys that would
        /// otherwise be recorded moves.
        public bool Key(Key k)
        {
            switch (k)
            {
                case Godot.Key.Up:
                case Godot.Key.Left:
                    Move(-1);
                    break;
                case Godot.Key.Down:
                case Godot.Key.Right:
                    Move(1);
                    break;
                case Godot.Key.Home:
                    _sel = 0;
                    Apply();
                    break;
                case Godot.Key.End:
                    _sel = _packs.Count - 1;
                    Apply();
                    break;
                // The three size commands, 120/121/122 on the original's own
                // menu (LTANK.C:1019) rather than in this dialog -- but this
                // dialog is the only menu this port has, so they are on it.
                case Godot.Key.Key1: _view.SetSize(1); break;
                case Godot.Key.Key2: _view.SetSize(2); break;
                case Godot.Key.Key3: _view.SetSize(3); break;
                case Godot.Key.Z: _view.SetSize(_view.Size % 3 + 1); break;
                default:
                    // Close (ID_GRAPHBOX_04), Cancel (id 2), or the key that
                    // opened it.  Anything else is swallowed, as a dialog does.
                    if (k == Godot.Key.Escape || k == Godot.Key.Enter
                        || k == Godot.Key.KpEnter || k == Godot.Key.Space
                        || k == Godot.Key.G)
                        Close();
                    break;
            }
            return true;
        }

        private void Move(int d)
        {
            if (_packs == null || _packs.Count == 0) return;
            _sel = (_sel + d + _packs.Count) % _packs.Count;
            Apply();
        }

        /// SetUpGraphicsBox: load the pack now, so the board behind the panel is
        /// the answer to "what does this one look like".
        private void Apply() => _view.ApplyPack(_packs[_sel]);

        // ---- drawing --------------------------------------------------------
        private const int Pad = 10;
        private const int Line = 17;

        private static string InfoText(Pack p) =>
            p.Info.Replace("\r\n", "\n").Replace('\r', '\n');

        /// A dialog-shaped box rather than a full-board overlay: the original is
        /// a small window over the game, and the game is still running behind
        /// it, so covering the whole board would hide the thing the pack is
        /// being chosen for.  The height is measured from the content, which is
        /// also what SetUpGraphicsBox does -- it resizes the dialog to 100 or
        /// 160 dialog base units depending on whether a .ltg is selected
        /// (LTANK_D.C:1160).
        public void Draw(Node2D n, Font font, Rect2 board)
        {
            if (_packs == null) return;
            Pack sel0 = _packs[_sel];
            float wInner = board.Size.X - 12 - 2 * Pad;
            float infoH = sel0.Info.Length == 0 ? 0
                : Mathf.Min(6 * 15,
                            font.GetMultilineStringSize(InfoText(sel0),
                                                        HorizontalAlignment.Left,
                                                        wInner, 12).Y);
            // The same arithmetic the drawing below walks through, in the same
            // order.  Keep the two in step: a height that disagrees with the
            // flow puts the footer through the middle of the description.
            float height = Pad + 13 + (Line + 4)              // title
                           + _packs.Count * Line + 6          // the list
                           + (sel0.Author.Length > 0 ? Line : 0)
                           + (infoH > 0 ? infoH + 6 : 0)
                           + 4 + Line + Line + Pad;           // size + footer

            var panel = new Rect2(board.Position + new Vector2(6, 6),
                                  new Vector2(board.Size.X - 12,
                                              Mathf.Min(height, board.Size.Y - 12)));
            n.DrawRect(panel, new Color(0.05f, 0.06f, 0.08f, 0.96f));
            n.DrawRect(panel, new Color(0.55f, 0.60f, 0.70f), false, 1);

            float x = panel.Position.X + Pad;
            float w = panel.Size.X - 2 * Pad;
            float y = panel.Position.Y + Pad + 13;

            n.DrawString(font, new Vector2(x, y), Title, HorizontalAlignment.Left, w, 15,
                         Colors.White);
            y += Line + 4;

            for (int i = 0; i < _packs.Count; i++)
            {
                Pack p = _packs[i];
                bool cur = i == _sel;
                string mark = cur ? ">" : " ";
                string name = p.Label + (p.Available ? "" : "   (not found)");
                Color tint = !p.Available ? Colors.DimGray : cur ? Colors.Yellow : Colors.Gainsboro;
                n.DrawString(font, new Vector2(x, y), mark + " " + name,
                             HorizontalAlignment.Left, w, 13, tint);
                y += Line;
            }

            y += 6;
            Pack sel = _packs[_sel];
            if (sel.Author.Length > 0)
            {
                n.DrawString(font, new Vector2(x, y), AuthorLabel + " " + sel.Author,
                             HorizontalAlignment.Left, w, 12, Colors.LightSteelBlue);
                y += Line;
            }
            if (infoH > 0)
            {
                // The .ltg Info field is 245 bytes of free text with its own
                // line breaks; wrap what does not fit rather than clipping it.
                n.DrawMultilineString(font, new Vector2(x, y), InfoText(sel),
                                      HorizontalAlignment.Left, w, 12, 6,
                                      new Color(0.72f, 0.72f, 0.75f));
                y += infoH + 6;
            }

            // The last two lines: the size, and how to leave.
            y += 4;
            string sizes = "";
            for (int s = 1; s <= 3; s++)
                sizes += (s == _view.Size ? "[" + BoardView.CellOf(s) + "]"
                                          : " " + BoardView.CellOf(s) + " ") + " ";
            n.DrawString(font, new Vector2(x, y), "Size  " + sizes.TrimEnd(),
                         HorizontalAlignment.Left, w, 12, Colors.Gainsboro);
            n.DrawString(font, new Vector2(x, y + Line),
                         "up/down picks   1/2/3 or Z sizes   Enter or Esc closes",
                         HorizontalAlignment.Left, w, 11, Colors.Gray);
        }
    }
}
