// The level editor's board operations, transliterated.
//
// **Why these live in Core.**  `ChangeGO` is LTANK2.C:809 -- the same file the
// tick comes out of -- and it writes `Game.PF` and `Game.BMF` directly.  It is
// still not a rule: nothing inside `Tick()` can reach it, exactly as nothing
// inside `Tick()` can reach `GraphicsFile.cs`.  The line this phase draws is
// whether a rule could move, not which directory a file sits in, and putting
// the editor's arithmetic here is what lets `lasertank-core.exe --edit` run it
// headless and diff the result against the oracle's own copy of `ChangeGO`.
//
// The commands *around* it -- Clear Field, the four Shifts, entering and
// leaving -- are LTANK.C window-proc cases, so they are here for the same
// reason step 4's `z` / `c` / `v` are in both script drivers: two independent
// transliterations that can be diffed against each other, with the C in the
// middle for the one function that is actually in LTANK2.C.
using System;

namespace LaserTank.Core
{
    /// The editor's board state: LTANK2.C's two selector globals and the
    /// operations the mouse and the menu drive.  It owns no files and no
    /// dialogs -- `Modified`, `OKtoSave` and the name/author/hint fields belong
    /// to the window that hosts it, because in the original they are window
    /// state (`EM_GETMODIFY` on two edit controls) rather than game state.
    public sealed class Editor
    {
        private readonly Engine _e;

        public Editor(Engine e) { _e = e; }

        /// LTANK2.C:42, :43 -- the two selected objects.  3 is water and 0 is
        /// dirt, which is what a fresh editor paints with the two buttons.
        public int CurSelBM_L = 3;
        public int CurSelBM_R = 0;

        /// The tunnel id the `LoadTID` dialog would return.  `ChangeGO`'s
        /// tunnel arm opens a modal dialog to ask for it (LTANK2.C:826); there
        /// is no dialog inside a transliteration, so the answer is a field the
        /// caller sets -- the same shape `oracle/driver.c` gives the commands
        /// that are really `DialogBox` calls.
        public int TunnelId;

        /// How many times ChangeGO's tunnel arm asked for one.  The oracle's
        /// trace footer carries `dialogs=`, the count of message boxes and
        /// dialogs its stub swallowed, and inside LTANK2.C there is exactly one
        /// dialog: this one.  Counting it here is what makes the two footers
        /// comparable -- and it is an assertion in its own right, that both
        /// engines reached the tunnel arm the same number of times.
        public int TunnelDialogs;

        /// LTANK.C:18.  The rotate-in-place table Shift+click walks: it steps
        /// through each object's *family* and wraps -- the four mirrors
        /// 7,8,9,10 cycle among themselves, the four roto mirrors 11..14 do,
        /// the four one-ways 15..18 do, the three anti-tanks 20..23 do -- and
        /// everything with no family maps to itself.
        ///
        /// **The declaration is `[MaxObjects+1]` and the initialiser has 25
        /// entries**, so C zero-fills the last two: rotating ice (24) gives 24,
        /// and rotating thin ice (25) or a tunnel selector (26) gives **0**,
        /// i.e. turns the cell into dirt.  That is a real, defined behaviour of
        /// the 2010 binary and the zeros are written out here rather than left
        /// to a language rule, because a reader counting the initialiser would
        /// otherwise size the array at 25 and turn two defined cases into an
        /// out-of-range one.
        public static readonly int[] GetNextBMArray =
        {
            0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 7, 12, 13, 14, 11, 16, 17, 18, 15,
            19, 21, 22, 23, 20, 24, 0, 0,
        };

        /// ChangeGO (LTANK2.C:809), with the GDI stripped out.
        ///
        /// **Three arms and each has something in it.**
        ///
        ///   * The **tank** (id 1) is not painted into `PF` at all -- it moves
        ///     `Game.Tank` and leaves `PF` at 0, which is the same invariant
        ///     BuildBMField sets up.  So there is always exactly one tank and
        ///     no way to place a second; the old cell keeps whatever `BMF` it
        ///     had, and the tank is drawn from `Tank.X`/`Tank.Y`.
        ///   * A **tunnel** (id 26 = MaxObjects) writes `0x40 | (id &lt;&lt; 1)`,
        ///     wait bit clear, and forces the bitmap to 55.  The id comes from
        ///     `TunnelId`, which is the dialog's return value.
        ///   * Anything else writes the id and `GetOBM(id)`.  Note what that
        ///     admits: the palette's click bound is `i > MaxObjects+1`, so
        ///     **27 is selectable** -- one past the last drawn sprite -- and
        ///     `GetOBM(27)` falls through its range test to 1.  The cell then
        ///     holds an object id no table knows, and the *next* level load
        ///     sanitises it into tunnel 5 (BuildBMField's `pt > 0x19` arm).
        ///     That is a defined effect, so it stays.
        ///
        /// `PF2` and `BMF2` are not touched by any arm, which is why an edited
        /// board's under-layer is whatever the last BuildBMField left.
        public void ChangeGO(int x, int y, int CurSelBM)
        {
            if (CurSelBM == 1)                       // Tank
            {
                _e.Game.BMF[x, y] = 1;
                _e.Game.PF[x, y] = 0;
                _e.Game.Tank.X = x; _e.Game.Tank.Y = y;
            }
            else
            {
                if (CurSelBM == Obj.MaxObjects)
                {
                    // Tunnel: i = DialogBox(LoadTID) << 1;  PF = i + 0x40
                    TunnelDialogs++;
                    _e.Game.PF[x, y] = (byte)((TunnelId << 1) + 0x40);
                    _e.Game.BMF[x, y] = 55;
                }
                else
                {
                    _e.Game.PF[x, y] = (byte)CurSelBM;
                    _e.Game.BMF[x, y] = (byte)Obj.GetOBM(CurSelBM);
                }
            }
        }

        /// WM_LBUTTONDOWN's game-window arm (LTANK.C:800).  Plain click paints
        /// the left selection, Shift+click rotates whatever is already there.
        /// Both are guarded by `Game.PF[x][y] != CurSelBM_L` -- painting a cell
        /// with what it already holds is a no-op, which matters for the tunnel
        /// arm because it is what stops the dialog reopening.
        public void LeftClick(int x, int y, bool shift)
        {
            if (x < 0 || x > 15 || y < 0 || y > 15) return;
            if (shift)
            {
                // The range test is this port's, not the original's -- see
                // Rotate.  It has to be a *skip* rather than a rotate-to-itself:
                // calling ChangeGO with the cell's own value still rewrites BMF
                // through GetOBM, which answers 1 for a tunnel and would erase
                // the tunnel's sprite.  editor_check.py found exactly that, on
                // `<1alcasca` -- place a tunnel, then Shift+click it.
                int pf = _e.Game.PF[x, y];
                if (pf <= Obj.MaxObjects) ChangeGO(x, y, GetNextBMArray[pf]);
            }
            else if (_e.Game.PF[x, y] != CurSelBM_L) ChangeGO(x, y, CurSelBM_L);
        }

        /// WM_RBUTTONDOWN's game-window arm (LTANK.C:840).  No Shift case: the
        /// rotate is the left button's alone.
        public void RightClick(int x, int y)
        {
            if (x < 0 || x > 15 || y < 0 || y > 15) return;
            if (_e.Game.PF[x, y] != CurSelBM_R) ChangeGO(x, y, CurSelBM_R);
        }

        /// WM_MOUSEMOVE with a button held (LTANK.C:764) -- the drag that
        /// paints.  **It has one guard the clicks do not:** a selection of
        /// `MaxObjects` (the tunnel) is skipped entirely, because dragging it
        /// would open the id dialog once per cell the pointer crossed.
        /// Shift held cancels the drag, which is how the original keeps
        /// Shift+click from painting on its way to rotating.
        public void Drag(int x, int y, int button, bool shift)
        {
            if (shift) return;
            if (x < 0 || x > 15 || y < 0 || y > 15) return;
            int sel = button == 1 ? CurSelBM_L : CurSelBM_R;
            if (sel == Obj.MaxObjects) return;
            if (_e.Game.PF[x, y] != sel) ChangeGO(x, y, sel);
        }

        /// `GetNextBMArray[PF[x][y]]`, LTANK.C:806.  The table covers 0..26 and
        /// a cell can also hold a **tunnel**, `0x40 | id << 1 | wait` = 64..79,
        /// which indexes past the end of a 27-int const array.  That read is
        /// whatever the linker put next and has no defined effect, so there is
        /// nothing to transliterate: Shift+click on a tunnel does nothing here,
        /// and this comment is the record of the difference.  (`GFXInit`'s
        /// `!(Mh || Gh)` is the same argument -- a bug with an undefined effect
        /// is the one class of bug this port does not keep.)
        ///
        /// `-1` for a cell the table does not cover, so a caller cannot mistake
        /// "no defined answer" for "rotates to itself": those are different,
        /// because `ChangeGO(x, y, PF[x][y])` is not a no-op -- it rewrites the
        /// bitmap through `GetOBM`, which does not know a tunnel.
        public static int Rotate(int pf) =>
            pf >= 0 && pf < GetNextBMArray.Length ? GetNextBMArray[pf] : -1;

        // ---- the menu commands, LTANK.C ------------------------------------

        /// Command 201's board half (LTANK.C:1110): **strip the tunnel wait
        /// bits.**  A tunnel cell is `0x40 | id << 1 | wait`, and the wait bit
        /// is runtime state -- it is set by a tank that arrived and has not
        /// left -- so a board about to be edited and saved must not carry one.
        /// Everything else command 201 does is menus, buttons and edit
        /// controls.
        public void Enter()
        {
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                    if (Obj.IsTunnel(_e.Game.PF[x, y]))
                        _e.Game.PF[x, y] &= 0xFE;
        }

        /// Command 601, "Clear Field" (LTANK.C:1135).  Note the tank's landing
        /// spot: 7,15 facing up, which is BuildBMField's default and so the
        /// same place a level with no tank in it puts one.
        public void ClearField()
        {
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    _e.Game.PF[x, y] = 0;
                    _e.Game.BMF[x, y] = 1;
                    _e.Game.BMF2[x, y] = 1;
                    _e.Game.PF2[x, y] = 0;
                }
            _e.Game.Tank.X = 7;
            _e.Game.Tank.Y = 15;
            _e.Game.Tank.Dir = 1;
            _e.Game.Tank.Firing = 0;
        }

        /// The playfield as commands 603 and 604 hand it to a level record:
        /// `memcpy(CurRecData.PF, Game.PF)` then `PF[Tank.X][Tank.Y] = 1`.
        /// **The tank goes back into the playfield on the way out**, which is
        /// the inverse of BuildBMField taking it out, and it is why a saved
        /// level has a tank in it at all.
        public byte[] PlayfieldForSave()
        {
            var flat = new byte[256];
            TGAMEREC.Flatten(_e.Game.PF, flat);
            flat[_e.Game.Tank.X * 16 + _e.Game.Tank.Y] = 1;
            return flat;
        }

        /// Commands 710/711/712/713 (LTANK.C:1281..1348), which shift the board
        /// by one cell and **wrap**, carrying the tank with it.
        ///
        /// Two things the C does that are easy to lose.  It moves `BMF` as well
        /// as `PF` rather than rebuilding it -- so a shifted board keeps its
        /// animation phase, and the two stay in step only because both are
        /// moved.  And it wraps the *tank* separately, by increment and
        /// compare, so a tank at column 15 shifted right lands at column 0 with
        /// whatever cell arrived there.  `PF2`/`BMF2` are not shifted at all.
        ///
        /// dx/dy are +1 or -1 and exactly one is non-zero.
        public void Shift(int dx, int dy)
        {
            var pf = new byte[16, 16];
            var bmf = new byte[16, 16];
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    int sx = ((x - dx) % 16 + 16) % 16;
                    int sy = ((y - dy) % 16 + 16) % 16;
                    pf[x, y] = _e.Game.PF[sx, sy];
                    bmf[x, y] = _e.Game.BMF[sx, sy];
                }
            Array.Copy(pf, _e.Game.PF, pf.Length);
            Array.Copy(bmf, _e.Game.BMF, bmf.Length);

            _e.Game.Tank.X += dx;
            if (_e.Game.Tank.X == 16) _e.Game.Tank.X = 0;
            if (_e.Game.Tank.X < 0) _e.Game.Tank.X = 15;
            _e.Game.Tank.Y += dy;
            if (_e.Game.Tank.Y == 16) _e.Game.Tank.Y = 0;
            if (_e.Game.Tank.Y < 0) _e.Game.Tank.Y = 15;
        }
    }
}
