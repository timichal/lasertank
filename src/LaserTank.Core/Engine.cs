// ---------------------------------------------------------------------------
// The LaserTank game core, transliterated from original/src/LTANK2.C and the
// WM_TIMER handler at original/src/LTANK.C:579.
//
// TRANSLITERATE LITERALLY.  Names, control flow and ordering follow the C, not
// C# convention: `Game`, `ScoreMove`, `SlideO`, `wasIce`, `IceMoveO`.  The ugly
// parts are the point -- wasIce as a hidden return channel (quirk #3), the
// 1-based slide stack that IceMoveO mutates while iterating it (quirk #6),
// MoveObj decrementing ScoreMove (quirk #7).  Idiomatic rewriting is how quirks
// die.  Every deviation gets a comment saying why.
//
// Correctness is defined by the trace, not by reading: oracle.exe and this
// engine must emit byte-identical traces on the whole corpus.  See PROGRESS.md,
// "The Phase 2 harness".
//
// STATUS: Phase 2 complete.  Every function the keystream can reach is
// transliterated, and all 187 recorded playbacks trace byte-identically to the
// oracle with --field --bmf.  MouseOperation is the one remaining stub: the
// mouse buffer is empty headless, so nothing can reach it from a keystream.
// It still throws rather than no-opping, so if that premise ever breaks the
// run stops instead of inventing a plausible answer.  Everything not yet
// transliterated
// throws NotPortedException rather than doing nothing -- a silent no-op would
// produce a plausible wrong trace, which is exactly the failure mode this whole
// approach exists to prevent.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace LaserTank.Core
{
    /// Thrown when the tick reaches a function that has not been transliterated
    /// yet.  Loud on purpose: see the file header.
    public sealed class NotPortedException : Exception
    {
        public NotPortedException(string what)
            : base(what + " is not transliterated yet (Phase 2, step 1..n)") { }
    }

    public sealed class Engine
    {
        // Virtual-key codes the tick loop switches on (LTANK.C:615).
        public const int VK_SPACE = 32, VK_LEFT = 37, VK_UP = 38, VK_RIGHT = 39, VK_DOWN = 40;

        public const int ani_delay = 4;      // LTANK.H:95

        // lt_sfx.h:13 -- only the ones the ported logic names so far.  sf is a
        // real parameter that MoveObj reassigns, so it is carried; SoundPlay
        // itself does nothing headless (lt_sfx.c:26 returns immediately when
        // !Sound_On, which is what a headless build is).
        public const int S_Bricks = 1, S_Fire = 2, S_Head = 4, S_Turn = 5,
                        S_Anti1 = 8, S_Anti2 = 9, S_Deflb = 10, S_LaserHit = 11,
                        S_Push2 = 12, S_Push1 = 13, S_Rotate = 14, S_Push3 = 15,
                        S_Sink = 16;

        private static void SoundPlay(int sn) { }

        // ---- globals owned by LTANK2.C ------------------------------------
        public readonly TGAMEREC Game = new TGAMEREC();
        public TTANKREC laser;
        public TICEREC SlideT, SlideO;
        public readonly TICEMEM SlideMem = new TICEMEM();

        public int AniLevel, AniCount;
        public bool Ani_On = true;
        public bool ConvMoving;
        public bool TankDirty;
        public bool Game_On;
        public bool FindTank;
        public bool LaserBounceOnIce;

        /// LTANK.C:26 -- the "VHS" (demo) playback flag.  Always FALSE headless,
        /// exactly as oracle/driver.c:37 has it.  Kept as a field so MoveLaser's
        /// `if (Game_On || VHSOn)` reads the way the original does.
        public bool VHSOn;

        /// LTANK2.C:68 -- "CheckLoc will set this to true if Ice".  Quirk #3:
        /// CheckLoc's real second return value, read after the call by
        /// MoveTank (LTANK2.C:1270), IceMoveT (:1381), IceMoveO (:1416) and
        /// ConvMoveTank (:1206).  It stays a field rather than becoming an
        /// `out` parameter because that would change behaviour: CheckLoc
        /// returns early on an off-board coordinate *without* writing it, so a
        /// move blocked at the edge of the board leaves whatever the previous
        /// call left there, and MoveTank's `if (wasIce)` then reads that.
        public bool wasIce;

        /// LTANK2.C:69 and :56, both written only by TranslateTunnel.  They are
        /// globals rather than out-parameters because their *staleness* is
        /// load-bearing: UpDateTankPos and ConvMoveTank read WaitToTrans after
        /// a move that was not into a tunnel, in which case nobody assigned it
        /// this tick and the value is whatever the last tunnel translation left.
        /// MoveObj is the only caller that clears it on the non-tunnel path.
        public bool WaitToTrans;
        public bool BlackHole;

        // ---- the undo buffer (LTANK2.C:52, :83, :390, :402, :423) ---------
        // Nothing headless ever calls UndoStep, so the stored snapshots are
        // never read back.  They are kept anyway because UndoP is *not* dead:
        // MoveObj's tunnel path decrements it (quirk #7), and getting UndoP's
        // arithmetic right means having the growth and the roll-over it can
        // grow into.
        public const int UndoBufStep = 200;      // LTANK.H:102
        public const int UndoMax = 10000;        // LTANK.H:103
        public int UndoBufSize = 3200;           // LTANK2.C:52
        public int UndoP;                        // LTANK2.C:83
        public int UndoRollOver = UndoMax;       // InitBuffers, LTANK2.C:399
        private TGAMEREC[] UndoBuffer = new TGAMEREC[3200];

        public int CurLevel;
        public TLEVEL CurRecData;

        // ---- globals owned by LTANK.C / LTANK_D.C -------------------------
        public byte[] RecBuffer = Array.Empty<byte>();
        public int RB_TOS;                   // number of keys in RecBuffer
        public bool PBOpen, PlayBack, PBHold;
        public int Speed = 1, SlowPB = 1;
        public const int SlowPBSet = 5;      // LTANK.H:106

        // The mouse buffer is empty headless, so the block at the end of the
        // tick never fires.  Kept for shape; MouseOperation throws if reached.
        public int MB_TOS, MB_SP;

        // ---- death, and the SendMessage / PostMessage distinction ---------
        // Quirk #8: CheckLLoc uses SendMessage(WM_Dead) (synchronous, lands
        // mid-tick); drowning and black holes use PostMessage (queued, lands
        // after it).  Deliberately changed in 4.0.6, and the ordering is
        // observable, so the two paths stay distinct here too.
        public int Deaths;
        private readonly Queue<Action> _posted = new Queue<Action>();

        private void SendDead()                      // SendMessage(WM_Dead)
        {
            GameOn(false);
            Deaths++;
        }

        private void PostDead() => _posted.Enqueue(SendDead);

        /// Drain what the tick posted.  driver.c calls lt_stub_pump() here.
        public void Pump()
        {
            while (_posted.Count > 0) _posted.Dequeue()();
        }

        // ---- LTANK2.C:111..193  the SlideO <-> SlideMem stack -------------
        // MGY's 2002 rewrite of the sliding-object code.  SlideO is a single
        // "current object" register and SlideMem is the stack of everything
        // sliding; every helper moves values between the two, so SlideO is
        // clobbered by almost all of them.  That is why IceMoveO reloads it
        // every iteration and why CheckLLoc's ice arm writes SlideO *then*
        // calls add_SlideO_to_Mem.
        //
        // Entries are 1-based (index 0 is never used), and the cap is quirk #6:
        // `if (SlideMem.count < MAX_TICEMEM-1)` silently drops the 16th
        // sliding object rather than growing or reporting.
        //
        // Both Mem_to_SlideO and SlideO_to_Mem no-op when handed an index past
        // count.  IceMoveO leans on that: `Mem_to_SlideO(SlideMem.count)` with
        // count == 0 leaves SlideO holding whatever the loop left in it, and
        // only the `SlideO.s = (count > 0)` after it is guaranteed.

        private void Mem_to_SlideO(int iSlideObj)
        {
            if (iSlideObj <= SlideMem.count)
            {
                SlideO.x = SlideMem.Objects[iSlideObj].x;
                SlideO.y = SlideMem.Objects[iSlideObj].y;
                SlideO.dx = SlideMem.Objects[iSlideObj].dx;
                SlideO.dy = SlideMem.Objects[iSlideObj].dy;
                SlideO.s = SlideMem.Objects[iSlideObj].s;
            }
        }

        private void SlideO_to_Mem(int iSlideObj)
        {
            if (iSlideObj <= SlideMem.count)
            {
                SlideMem.Objects[iSlideObj].x = SlideO.x;
                SlideMem.Objects[iSlideObj].y = SlideO.y;
                SlideMem.Objects[iSlideObj].dx = SlideO.dx;
                SlideMem.Objects[iSlideObj].dy = SlideO.dy;
                SlideMem.Objects[iSlideObj].s = SlideO.s;
            }
        }

        /// Add an object in the stack for sliding objects.  But, if this object
        /// is already in this stack, just change dir and don't increase the
        /// counter.
        private void add_SlideO_to_Mem()
        {
            int iSlideObj;

            if (SlideMem.count < TICEMEM.MAX_TICEMEM - 1)    // quirk #6, silent
            {
                for (iSlideObj = 1; iSlideObj <= SlideMem.count; iSlideObj++)
                {
                    if ((SlideMem.Objects[iSlideObj].x == SlideO.x) &&
                        (SlideMem.Objects[iSlideObj].y == SlideO.y))
                    {
                        SlideO_to_Mem(iSlideObj);            // Update the stack
                        return;                              // don't inc the counter
                    }
                }
                // Add this object to the stack
                SlideMem.count++;
                SlideO_to_Mem(SlideMem.count);
                SlideO.s = (SlideMem.count > 0) ? 1 : 0;
            }
            // No else: over the cap, the object simply never starts sliding and
            // SlideO.s is left alone.
        }

        /// Delete a sliding object from the stack by index, shuffling the rest
        /// down.  Note the shuffle goes through SlideO, so this clobbers it.
        private void sub_SlideO_from_Mem(int iSlideObj)
        {
            int i;
            for (i = iSlideObj; i < SlideMem.count; i++)
            {
                Mem_to_SlideO(i + 1);
                SlideO_to_Mem(i);
            }
            SlideMem.count--;
            SlideO.s = (SlideMem.count > 0) ? 1 : 0;
        }

        /// If an object is sliding and is hit by a laser, delete it from the
        /// stack.  The `return` inside the loop is why only the topmost match
        /// goes, and why the trailing `SlideO.s = ...` runs *only* when nothing
        /// matched -- a found-and-removed call leaves SlideO holding the
        /// shuffled-down entry instead.
        private void del_SlideO_from_Mem(int x, int y)
        {
            int iSlideObj;
            for (iSlideObj = SlideMem.count; iSlideObj >= 1; iSlideObj--)
            {
                if ((SlideMem.Objects[iSlideObj].x == x) &&
                    (SlideMem.Objects[iSlideObj].y == y))
                {
                    // remove this object
                    sub_SlideO_from_Mem(iSlideObj);
                    return;
                }
            }
            SlideO.s = (SlideMem.count > 0) ? 1 : 0;
        }

        // ---- LTANK2.C:202  TestIfConvCanMoveTank ---------------------------
        // "Used to handle a bug: the speed bug.  MGY - 22-nov-2002."
        // Read by MoveLaser when the shot dies.  Four CheckLoc calls, so it is
        // also a wasIce writer (quirk #3) on the conveyor cases -- and *not* on
        // the default, where it returns without probing anything.
        private bool TestIfConvCanMoveTank()
        {
            switch (Game.PF[Game.Tank.X, Game.Tank.Y])
            {
                case 15:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y - 1))   // Conveyor Up
                        return true;
                    break;
                case 16:
                    if (CheckLoc(Game.Tank.X + 1, Game.Tank.Y))
                        return true;
                    break;
                case 17:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y + 1))
                        return true;
                    break;
                case 18:
                    if (CheckLoc(Game.Tank.X - 1, Game.Tank.Y))
                        return true;
                    break;
            }
            return false;
        }

        // ---- LTANK2.C:882  GameOn ------------------------------------------
        public void GameOn(bool b) => Game_On = b;

        // ---- LTANK2.C:843  BuildBMField -------------------------------------
        public void BuildBMField()
        {
            int i = 0;
            byte pt;

            Game.Tank.X = 7; Game.Tank.Y = 15;
            Game.Tank.Dir = 1; Game.Tank.Firing = 0;

            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    // --- mgy 18-05-2003, only legal pieces ---
                    pt = Game.PF[x, y];
                    if (pt > 0x19)
                    {
                        pt = (byte)Obj.GetTunnelID(Game.PF[x, y]);
                        Game.PF[x, y] = (byte)((pt << 1) + 0x40);
                    }
                    // --- end of 18-05-2003 ---

                    if (Game.PF[x, y] == 1)
                    {
                        i = 1;
                        Game.Tank.X = x;
                        Game.Tank.Y = y;
                        Game.PF[x, y] = 0;
                    }
                    else
                    {
                        // Quirk #9: the original leaves `i` uninitialised when a
                        // cell is >= 64 and not a tunnel.  The sanitisation above
                        // makes that unreachable -- every value > 0x19 becomes a
                        // tunnel -- so `i` here always carries a fresh value.  It
                        // is initialised to 0 above only because C# demands it;
                        // do not "fix" the original's shape.
                        if (Game.PF[x, y] < 64) i = Obj.GetOBM(Game.PF[x, y]);
                        else if (Obj.IsTunnel(Game.PF[x, y])) i = 55;
                    }
                    Game.BMF[x, y] = (byte)i;
                    Game.BMF2[x, y] = 1;
                    Game.PF2[x, y] = 0;
                }

            Game.ScoreMove = 0;
            Game.ScoreShot = 0;
        }

        // ---- LTANK2.C:537  UpDateTank ---------------------------------------
        // A paint routine, but it clears TankDirty, so it is state.  Quirk #1's
        // smaller cousin: do not delete paint calls, stub their innards.
        private void UpDateTank() => TankDirty = false;

        // ---- LTANK2.C:608  PutLevel -----------------------------------------
        private void PutLevel()
        {
            // for (y) for (x) UpDateSprite(x, y) -- pure paint, reads BMF only.
            UpDateTank();
            TankDirty = false;
            // The FindTank crosshair is Rectangle() calls; nothing to carry.
        }

        // ---- LTANK2.C:1098  Animate -----------------------------------------
        // Cosmetic apart from AniLevel, AniCount and the TankDirty at the end:
        // every other write is to BMF/BMF2, and no bitmap feeds a decision
        // anywhere in the program (hazard #2, corrected in Phase 1).
        // Transliterated in full anyway -- a BMF divergence is a cheap tripwire
        // for a transliteration slip, and TankDirty is real state.
        // The UpDateSprite(x, y) after each assignment is pure paint.
        public void Animate()
        {
            AniLevel++;
            AniCount = 0;
            if (AniLevel == 3) AniLevel = 0;

            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    // Animate Conveyor Belts & Flag if under something
                    switch (Game.PF2[x, y])
                    {
                        case 2: Game.BMF2[x, y] = (byte)(6 + AniLevel); break;
                        case 15: Game.BMF2[x, y] = (byte)(24 + AniLevel); break;
                        case 16: Game.BMF2[x, y] = (byte)(27 + AniLevel); break;
                        case 17: Game.BMF2[x, y] = (byte)(30 + AniLevel); break;
                        case 18: Game.BMF2[x, y] = (byte)(33 + AniLevel); break;
                    }
                    // Now animate all top sprites
                    switch (Game.PF[x, y])
                    {
                        case 2: Game.BMF[x, y] = (byte)(6 + AniLevel); break;
                        case 3: Game.BMF[x, y] = (byte)(9 + AniLevel); break;
                        case 7: Game.BMF[x, y] = (byte)(16 + AniLevel); break;
                        case 8: Game.BMF[x, y] = (byte)(36 + AniLevel); break;
                        case 9: Game.BMF[x, y] = (byte)(39 + AniLevel); break;
                        case 10: Game.BMF[x, y] = (byte)(42 + AniLevel); break;
                        case 15: Game.BMF[x, y] = (byte)(24 + AniLevel); break;
                        case 16: Game.BMF[x, y] = (byte)(27 + AniLevel); break;
                        case 17: Game.BMF[x, y] = (byte)(30 + AniLevel); break;
                        case 18: Game.BMF[x, y] = (byte)(33 + AniLevel); break;
                    }
                }
            TankDirty = true;
        }

        // ---- LTANK2.C:974  LoadNextLevel ------------------------------------
        /// The logic-carrying part of LoadNextLevel with DirectLoad = TRUE: read
        /// the record, copy its playfield, BuildBMField, GameOn, reset the
        /// recording pointer and the slide state.  The difficulty / completed
        /// filter, the menus, the INI writes and the undo buffer are UI.
        public bool LoadLevel(string lvlPath, int number)
        {
            GameOn(false);
            CurRecData = LevelFile.ReadLevel(lvlPath, number);
            if (CurRecData == null) return false;    // eof -> WM_GameOver
            CurLevel = number;

            Game.CopyPFFrom(CurRecData.PF);
            BuildBMField();
            GameOn(true);
            FindTank = true;
            ResetUndoBuffer();               // LTANK2.C:1033, in this position

            Game.RecP = 0;
            RB_TOS = 0;
            SlideT.s = 0;
            SlideO.s = 0;
            SlideMem.count = 0;
            return true;
        }

        // ---- LTANK2.C:402  ResetUndoBuffer ----------------------------------
        // The GlobalReAlloc down to one block cannot fail here, so the
        // FileError() branch is unreachable and is not carried.
        private void ResetUndoBuffer()
        {
            Array.Resize(ref UndoBuffer, UndoBufStep);
            UndoBufSize = UndoBufStep;
            UndoP = 0;
            UndoBuffer[0] = new TGAMEREC();      // UndoBuffer->Tank.Dir = 0
            // Lets also init the Mouse Buffer
            MB_TOS = MB_SP = 0;
        }

        // ---- LTANK2.C:423  UpdateUndo ---------------------------------------
        private void UpdateUndo()        // Come here whenever we move or shoot
        {
            int i;

            UndoP++;
            if (UndoP >= UndoBufSize)
            {
                if (UndoP >= UndoMax)
                {
                    UndoRollOver = (UndoP - 1);          // Save Where we rolled Over
                    UndoP = 0;
                }
                else
                {
                    i = UndoBufSize + UndoBufStep;
                    // GlobalReAlloc cannot return NULL here, so the original's
                    // allocation-failure branch (which also rolls UndoP over)
                    // is unreachable and is not carried.
                    Array.Resize(ref UndoBuffer, i);
                    UndoBufSize = i;
                }
            }
            UndoBuffer[UndoP] = Game.Clone();    // UndoBuffer[UndoP] = Game
            // EnableMenuItem / EnableWindow are UI.
        }

        // ---- LTANK2.C:1164  TranslateTunnel ---------------------------------
        // Given a tunnel cell, find its twin.  Three outcomes, and the two flags
        // are how the callers tell them apart (quirk #4): warped (both false),
        // blocked because something is sitting on the exit -- PF2 holds the twin
        // there, so the exit is occupied -- (WaitToTrans), or no twin at all
        // (BlackHole).  Note the first scan matches the *whole* cell including
        // the low wait bit, so a twin already marked waiting is not an exit.
        private void TranslateTunnel(ref int x, ref int y)
        {
            int cx, cy;
            byte bb;

            bb = Game.PF[x, y];              // bb is ID #
            WaitToTrans = false;
            BlackHole = false;
            for (cy = 0; cy < 16; cy++) for (cx = 0; cx < 16; cx++)
                if ((Game.PF[cx, cy] == bb) && (!((x == cx) && (y == cy))))
                {
                    x = cx;                  // We found an exit YEA !!!
                    y = cy;
                    return;
                }
            // check for blocked hole - something is over the exit
            // Scan the 2nd layer any matches are blocked holes
            for (cy = 0; cy < 16; cy++) for (cx = 0; cx < 16; cx++)
                if ((Game.PF2[cx, cy] == bb) && (!((x == cx) && (y == cy))))
                {
                    // We found one so we will set the flag
                    WaitToTrans = true;
                    return;                  // Blocked so no warp
                }
            // There is no match, so it is a black hole
            BlackHole = true;
        }

        // ---- LTANK2.C:1216  UpDateTankPos -----------------------------------
        // SoundPlay, the SetTextAlign/TextOut score readout and UpDateSprite are
        // paint; UpdateUndo, ScoreMove, the position, Tank.Good and TankDirty
        // are not.  Called with (0,0) from MoveObj's tunnel path, where "moving
        // the tank by nothing" exists purely to re-run the tunnel check on the
        // cell it is already standing on.
        private void UpDateTankPos(int x, int y)
        {
            UpdateUndo();
            Game.ScoreMove++;
            Game.Tank.Y += y;
            Game.Tank.X += x;
            Game.Tank.Good = 0;              // we need it somewhere if we move off a tunnel
            if (Obj.IsTunnel(Game.PF[Game.Tank.X, Game.Tank.Y]))
            {
                int tx = Game.Tank.X, ty = Game.Tank.Y;
                TranslateTunnel(ref tx, ref ty);     // We moved into a tunnel
                Game.Tank.X = tx; Game.Tank.Y = ty;
                if (BlackHole) PostDead();           // The tunnel was a black hole
            }
            // No else: WaitToTrans keeps whatever the last TranslateTunnel left
            // in it.  See the field's comment -- this is the original's shape.
            if (WaitToTrans) Game.Tank.Good = 1;
            TankDirty = true;
        }

        // ---- LTANK2.C:1287  MoveObj -----------------------------------------
        // used by CheckLLoc
        //
        // Moves the object at (x,y) by (dx,dy), restoring whatever it was
        // standing on and saving whatever it lands on.  Quirk #7 is in the first
        // half: when the object being moved is sitting in a tunnel exit that
        // nothing else is waiting for, the thing that was blocked may be the
        // tank, and it is released by a zero-distance UpDateTankPos -- whose
        // UpdateUndo and ScoreMove++ are then undone by the ScoreMove-- and
        // UndoP-- bracketing the call.  That is the "Bartok Bug.lvl" workaround
        // (MGY, 2003/05/18, v408b15), and the net effect on the score is zero
        // only because both halves are there: port one without the other and the
        // move counter drifts.
        private void MoveObj(int x, int y, int dx, int dy, int sf)
        {
            int obt, bm, bb, ok;
            // cx/cy are uninitialised in the original and are only read when ok
            // is true, i.e. when the search below has assigned them.  Zero here
            // is C#'s demand, not a behaviour change.
            int cx = 0, cy = 0;

            obt = Game.PF[x, y];                                // Get Object type
            bm = Game.BMF[x, y];
            if ((Game.PF2[x, y] & Obj.Tunnel) == Obj.Tunnel)    // Check if Tunnel
            {
                bb = Game.PF2[x, y] | 1;                        // bb is ID # w/ 1 set
                ok = 0;
                for (cy = 0; cy < 16; cy++) for (cx = 0; cx < 16; cx++)
                    if ((Game.PF2[cx, cy] == bb) && (!((x == cx) && (y == cy))))
                    {
                        ok = 1;
                        goto MoveObj1;
                        // Ok if something wants to move here; cx & cy set to orig
                    }
MoveObj1:
                if (ok != 0)                                // We are Moving an Object
                {
                    Game.PF[x, y] = Game.PF[cx, cy];        // Transfer Blocked Object
                    Game.BMF[x, y] = Game.BMF[cx, cy];
                    Game.PF[cx, cy] = (byte)(Game.PF2[cx, cy] & 0xFE);  // Return Saved State
                    Game.PF2[cx, cy] = 0;
                    Game.BMF[cx, cy] = Game.BMF2[cx, cy];
                    // UpDateSprite(cx, cy) -- paint.
                }
                else
                {                                           // Not Blocked Anymore
                    Game.PF[x, y] = (byte)(Game.PF2[x, y] & 0xFE);  // Return Saved State strip
                    Game.PF2[x, y] = 0;
                    Game.BMF[x, y] = Game.BMF2[x, y];
                    // We didn't find a match so maybe the tank is it
                    if ((Game.PF[Game.Tank.X, Game.Tank.Y] == (bb & 0xFE)) && Game.Tank.Good != 0)
                    {
                        Game.ScoreMove--; // MGY - 2003/05/18 - v408b15 -  Bartok Bug.lvl
                        UpDateTankPos(0, 0);
                        UndoP--;
                    }
                }
            }
            else
            {               // If not a tunnel
                Game.PF[x, y] = Game.PF2[x, y];             // Return Saved State
                Game.PF2[x, y] = 0;
                Game.BMF[x, y] = Game.BMF2[x, y];
            }
            // UpDateSprite(x, y) -- paint.
            x += dx;
            y += dy;
            if (Obj.IsTunnel(Game.PF[x, y]))
            {
                TranslateTunnel(ref x, ref y);  // We moved into a tunnel
                if (BlackHole) return;          // The tunnel was a black hole
            }
            else WaitToTrans = false;

            Game.PF2[x, y] = Game.PF[x, y];                 // Save Return State
            if (WaitToTrans) Game.PF2[x, y] |= 1;           // Set bit 1 if we are waiting to transport
            Game.BMF2[x, y] = Game.BMF[x, y];
            if (Game.PF[x, y] != 3)
            {
                Game.PF[x, y] = (byte)obt;
                Game.BMF[x, y] = (byte)bm;
            }
            else
            {
                sf = S_Sink;
                if (obt == 5)
                {
                    Game.PF[x, y] = 0;
                    Game.PF2[x, y] = 0;  // Pushing Block into Water }
                    Game.BMF[x, y] = 19;
                    Game.BMF2[x, y] = 19;
                }
            }
            // UpDateSprite(x, y) -- paint.
            if ((x == Game.Tank.X) && (y == Game.Tank.Y)) TankDirty = true;
            SoundPlay(sf);
        }

        // ---- not transliterated yet ------------------------------------------
        // Ported in this order (PROGRESS.md, Phase 2 step 1..n):
        //   CheckLoc -> MoveObj -> CheckLLoc -> MoveLaser -> AntiTank
        //   -> IceMoveT/IceMoveO -> conveyor -> tick loop
        // ---- LTANK2.C:78  CheckArray -----------------------------------
        // Object id -> may the tank enter.  Indexed by the raw PF cell, so in
        // the original a cell outside 0..25 reads past the array; the
        // BuildBMField sanitisation makes that unreachable (every such value
        // becomes a tunnel, and tunnels return before the lookup).  C# throws
        // instead of reading whatever followed the array, which is the loud
        // failure we want if the premise ever stops holding.
        private static readonly int[] CheckArray =
        {
            1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1,
        };

        // ---- LTANK2.C:1278  CheckLoc -------------------------------------
        private bool CheckLoc(int x, int y)
        {
            // Check if Tank can move
            if ((x < 0) || (x > 15) || (y < 0) || (y > 15)) return false;
            wasIce = ((Game.PF[x, y] == Obj.Ice) || (Game.PF[x, y] == Obj.ThinIce));
            if ((Game.PF[x, y] & Obj.Tunnel) == Obj.Tunnel) return true;
            return CheckArray[Game.PF[x, y]] != 0;
        }

        // ---- LTANK2.C:1241  MoveTank ------------------------------------
        // Two behaviours in one function, and the first one is a quirk in its
        // own right: a key whose direction differs from the tank's *turns and
        // returns*, spending the keypress without moving and without touching
        // SlideT.  Only a key that repeats the way the tank already faces
        // actually tries to move.
        //
        // The rest is the shape to keep verbatim:
        //  - SlideT.dx/dy are written on *both* arms of each `if`, so a move
        //    blocked by a wall still records the direction it was blocked in.
        //    IceMoveT reads them later, so a bump is not a no-op.
        //  - `if (wasIce)` reads CheckLoc's hidden second return value
        //    (quirk #3), and CheckLoc returns early on an off-board coordinate
        //    *without writing it* -- so a move blocked at the edge of the board
        //    reads whatever the previous call, possibly in a previous tick,
        //    left there.  That is the original's behaviour, not an accident to
        //    be tidied: keep the field, keep the read.
        //  - SlideT.x/y are set from the tank's position *after* the move, so
        //    the slide starts from where it landed.
        private void MoveTank(int d)
        {
            if (Game.Tank.Dir != d)              // The Tank is Turning
            {
                Game.Tank.Dir = d;
                // UpDateSprite(Game.Tank.X, Game.Tank.Y) -- paint.
                TankDirty = true;
                SoundPlay(S_Turn);
                return;
            }
            switch (d)
            {
                case 1:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y - 1)) UpDateTankPos(0, -1);
                    else SoundPlay(S_Head);      // Ouch we are hitting something hard
                    SlideT.dy = -1; SlideT.dx = 0;
                    break;
                case 2:
                    if (CheckLoc(Game.Tank.X + 1, Game.Tank.Y)) UpDateTankPos(1, 0);
                    else SoundPlay(S_Head);
                    SlideT.dy = 0; SlideT.dx = 1;
                    break;
                case 3:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y + 1)) UpDateTankPos(0, 1);
                    else SoundPlay(S_Head);
                    SlideT.dy = 1; SlideT.dx = 0;
                    break;
                case 4:
                    if (CheckLoc(Game.Tank.X - 1, Game.Tank.Y)) UpDateTankPos(-1, 0);
                    else SoundPlay(S_Head);
                    SlideT.dy = 0; SlideT.dx = -1;
                    break;
            }
            if (wasIce)
            {
                SlideT.x = Game.Tank.X;
                SlideT.y = Game.Tank.Y;
                SlideT.s = 1;                    // TRUE
            }
        }


        // ---- LTANK2.C:1634  FireLaser ------------------------------------
        // Arm the laser and immediately step it once -- there is no tick
        // between firing and the first MoveLaser, so a shot that hits an
        // adjacent wall is over inside the same tick it started.
        //
        // `sf` is not just a sound id.  `laser.Good = (sf == 2)` -- S_Fire --
        // is how the rest of the engine tells the tank's own shot from an
        // anti-tank's (S_Anti2 = 9), so the argument has to be the real
        // constant at every call site.  The score readout (itoa/TextOut) and
        // the LaserColorG/R choice above it are paint; laser.Good is the same
        // condition as the colour, kept because CheckLLoc reads it.
        //
        // Note laser.Firing (the laser's own "I have been moved" flag) starts
        // FALSE and is distinct from Game.Tank.Firing ("a shot is in flight"),
        // which is what the tick's key-consume test and MoveLaser's exit read.
        private void FireLaser(int x, int y, int d, int sf)
        {
            Game.Tank.Firing = 1;                // TRUE
            laser.Dir = d;
            laser.X = x;
            laser.Y = y;
            laser.Firing = 0;                    // true if laser has been moved
            SoundPlay(sf);
            // SetTextAlign / SetTextColor / SetBkColor / itoa / TextOut -- the
            // shot-count readout, paint.
            // LaserColor = (sf == 2) ? LaserColorG : LaserColorR -- paint, and
            // exactly the condition on the next line.
            laser.Good = (sf == 2) ? 1 : 0;
            MoveLaser();
        }


        // ---- LTANK2.C:1449  KillAtank ------------------------------------
        // An anti-tank shot from the front becomes a *solid* block, not empty
        // dirt: the wreck keeps blocking the square.  `bm` is the junk bitmap
        // for the direction it was facing (54/52/12/53), cosmetic but traced.
        private void KillAtank(int x, int y, int bm)
        {
            Game.PF[x, y] = 4;                   // Solid Object}
            Game.BMF[x, y] = (byte)bm;           // Junk Bitmap}
            // UpDateSprite(x, y) -- paint.
            SoundPlay(S_Anti1);
        }

        // ---- LTANK2.C:1459  CheckLLoc ------------------------------------
        // "this is were the laser does it's damage.  returns true if laser
        // didn't hit anything."
        //
        // The switch is the object table read as "what does a laser do to
        // this", and its shape carries several things worth keeping literally:
        //
        //  - The tank's own cell is checked *before* the switch, and death is
        //    SendMessage (quirk #8) -- immediate, mid-tick, unlike drowning.
        //    A shot can therefore kill the tank in the same tick it was fired.
        //  - `wasIce = FALSE` here overwrites whatever CheckLoc last left in
        //    the flag, and then the case 5/7/8/9/10/11..14 arms call CheckLoc,
        //    which writes it again.  So the `if (wasIce)` at the bottom asks
        //    "was the square the object is being pushed *into* ice", not
        //    anything about the square that was hit.  That is the whole
        //    mechanism by which a laser starts an object sliding.
        //  - An anti-tank only dies when shot in the face: the `dy == 1` /
        //    `dx == -1` / `dy == -1` / `dx == 1` guards match a laser
        //    travelling *toward* the direction it faces.  Shot in the side or
        //    the back it is pushed like a block instead.
        //  - Mirrors (11..14) and rotary mirrors (20..23) return TRUE for the
        //    two directions they deflect and are pushed/rotated for the other
        //    two, which is why the deflection itself lives in MoveLaser rather
        //    than here.
        //  - `case 19` (crystal block) is two PutSprite calls picked by
        //    laser.Good and then `return TRUE` -- pure paint, no state.  It is
        //    the only place laser.Good is read.
        //  - The `default` arm falls *through* to the wasIce tail when the cell
        //    is not a tunnel.  BuildBMField's sanitisation makes that
        //    unreachable, but the fallthrough is the original's shape.
        private bool CheckLLoc(int x, int y, int dx, int dy)
        {
            if ((x < 0) || (x > 15) || (y < 0) || (y > 15))
            {
                return false;
            }
            if ((x == Game.Tank.X) && (y == Game.Tank.Y))
            {
                SendDead();                      // SendMessage(MainH, WM_Dead)
                return false;
            }
            wasIce = false;
            switch (Game.PF[x, y])
            {
                case 0:
                case 2:
                case 3:
                case 15:
                case 16:
                case 17:
                case 18: return true;
                case 4:
                    SoundPlay(S_LaserHit);
                    break;
                case 5:
                    if (CheckLoc(x + dx, y + dy)) MoveObj(x, y, dx, dy, S_Push1);
                    else SoundPlay(S_LaserHit);
                    break;
                case 6:
                    Game.PF[x, y] = 0;
                    Game.BMF[x, y] = 1;
                    // UpDateSprite(x, y) -- paint.
                    SoundPlay(S_Bricks);
                    break;
                case 7:
                    if (dy == 1) { KillAtank(x, y, 54); return false; }
                    else if (CheckLoc(x + dx, y + dy)) MoveObj(x, y, dx, dy, S_Push3);
                    else SoundPlay(S_LaserHit);
                    break;
                case 8:
                    if (dx == -1) { KillAtank(x, y, 52); return false; }
                    else if (CheckLoc(x + dx, y + dy)) MoveObj(x, y, dx, dy, S_Push3);
                    else SoundPlay(S_LaserHit);
                    break;
                case 9:
                    if (dy == -1) { KillAtank(x, y, 12); return false; }
                    else if (CheckLoc(x + dx, y + dy)) MoveObj(x, y, dx, dy, S_Push3);
                    else SoundPlay(S_LaserHit);
                    break;
                case 10:
                    if (dx == 1) { KillAtank(x, y, 53); return false; }
                    else if (CheckLoc(x + dx, y + dy)) MoveObj(x, y, dx, dy, S_Push3);
                    else SoundPlay(S_LaserHit);
                    break;
                case 11:
                    if ((laser.Dir == 2) || (laser.Dir == 3)) return true;
                    if (CheckLoc(x + dx, y + dy)) MoveObj(x, y, dx, dy, S_Push2);
                    else SoundPlay(S_LaserHit);
                    break;
                case 12:
                    if ((laser.Dir == 3) || (laser.Dir == 4)) return true;
                    if (CheckLoc(x + dx, y + dy)) MoveObj(x, y, dx, dy, S_Push2);
                    else SoundPlay(S_LaserHit);
                    break;
                case 13:
                    if ((laser.Dir == 1) || (laser.Dir == 4)) return true;
                    if (CheckLoc(x + dx, y + dy)) MoveObj(x, y, dx, dy, S_Push2);
                    else SoundPlay(S_LaserHit);
                    break;
                case 14:
                    if ((laser.Dir == 1) || (laser.Dir == 2)) return true;
                    if (CheckLoc(x + dx, y + dy)) MoveObj(x, y, dx, dy, S_Push2);
                    else SoundPlay(S_LaserHit);
                    break;
                case 19:
                    // PutSprite(laser.Good ? 46 : 51, ...) -- paint, both arms.
                    return true;
                case 20:
                    if ((laser.Dir == 2) || (laser.Dir == 3)) return true;
                    Game.PF[x, y] = 21; Game.BMF[x, y] = 48;
                    // UpDateSprite(x, y) -- paint.
                    SoundPlay(S_Rotate);
                    break;
                case 21:
                    if ((laser.Dir == 3) || (laser.Dir == 4)) return true;
                    Game.PF[x, y] = 22; Game.BMF[x, y] = 49;
                    SoundPlay(S_Rotate);
                    break;
                case 22:
                    if ((laser.Dir == 1) || (laser.Dir == 4)) return true;
                    Game.PF[x, y] = 23; Game.BMF[x, y] = 50;
                    SoundPlay(S_Rotate);
                    break;
                case 23:
                    if ((laser.Dir == 1) || (laser.Dir == 2)) return true;
                    Game.PF[x, y] = 20; Game.BMF[x, y] = 47;
                    SoundPlay(S_Rotate);
                    break;
                case 24:    // Ice
                case 25:    // thin Ice
                    return true;
                default:
                    if (Obj.IsTunnel(Game.PF[x, y])) return true;
                    break;      // falls through to the wasIce tail, as in the C
            }
            if (wasIce)
            {
                // If is already sliding, del it !
                del_SlideO_from_Mem(x, y);
                // and add a new slide in a new dirrection
                SlideO.x = x + dx;
                SlideO.y = y + dy;
                SlideO.s = 1;                    // TRUE
                SlideO.dx = dx;
                SlideO.dy = dy;
                add_SlideO_to_Mem();
            }
            // MGY
            else
            {
                // SlideO.s = FALSE;   // in case we side hit off of the ice
                del_SlideO_from_Mem(x, y);
            }
            return false;
        }

        // ---- LTANK2.C:565  UpDateLaserBounce -----------------------------
        // HAZARD #1, and the reason the oracle compiles LTANK2.C verbatim
        // instead of stubbing its drawing.  This is a *paint* routine -- eight
        // Rectangle calls drawing the two halves of a deflected beam -- but the
        // loop above them sets LaserBounceOnIce, and MoveLaser reads that to
        // `goto LaserMoveJump` and take a second step in the same tick
        // (LTANK2.C:1631).  Delete the paint function and laser-on-a-sliding-
        // mirror behaviour changes.  Only the loop is carried here; the
        // Rectangles are not.
        //
        // What it means: the beam has just been deflected by a mirror that is
        // itself sliding on ice.  The scan is over the whole slide stack, and
        // note it tests `.s` on the *stored* entry, so an object left in the
        // stack with s == 0 does not trigger it.  a and b -- the incoming and
        // outgoing directions -- are used only by the drawing, and are kept as
        // parameters so the call site reads like the original's.
        private void UpDateLaserBounce(int a, int b)
        {
            int iSlideObj;

            // we need to stop advance the LaserShot if sliding on ice & hit
            for (iSlideObj = 1; iSlideObj <= SlideMem.count; iSlideObj++)
                if (SlideMem.Objects[iSlideObj].s != 0
                    && (SlideMem.Objects[iSlideObj].x == laser.X)
                    && (SlideMem.Objects[iSlideObj].y == laser.Y)) LaserBounceOnIce = true;
            // The two `switch (a)` / `switch (b)` Rectangle blocks are paint.
        }

        // ---- LTANK2.C:1572  MoveLaser ------------------------------------
        // One step of the beam per tick -- except when it is not: the trailing
        // `if (LaserBounceOnIce) goto LaserMoveJump` restarts the whole
        // function, so a deflection off a sliding mirror advances the beam
        // twice (or more) within one tick.  The goto is kept as a goto: the
        // label is also where LaserBounceOnIce is cleared, and that ordering is
        // the quirk.
        //
        // The dead-shot arm is where much of the tick's remaining behaviour
        // lives: clearing Game.Tank.Firing is what lets the *next* tick consume
        // a key, AntiTank() runs there (so an anti-tank can answer a shot the
        // instant it expires), and TestIfConvCanMoveTank sets ConvMoving --
        // MGY's 2002 "speed bug" handling, which blocks the key consume for one
        // more tick.
        private void MoveLaser()
        {
            int x, y, oDir;

        LaserMoveJump:
            LaserBounceOnIce = false;
            x = 0;
            y = 0;
            switch (laser.Dir)
            {
                case 1: y = -1; break;
                case 2: x = +1; break;
                case 3: y = +1; break;
                case 4: x = -1; break;
            }
            if (CheckLLoc(laser.X + x, laser.Y + y, x, y))
            {
                // if (laser.Firing) UpDateSprite(laser.X, laser.Y) -- paint.
                laser.Y += y;
                laser.X += x;
                if (((Game.PF[laser.X, laser.Y] > 10) && (Game.PF[laser.X, laser.Y] < 15))
                    || ((Game.PF[laser.X, laser.Y] > 19) && (Game.PF[laser.X, laser.Y] < 24)))
                {
                    oDir = laser.Dir;
                    switch (Game.PF[laser.X, laser.Y])
                    {
                        case 11:
                        case 20:
                            if (laser.Dir == 2) laser.Dir = 1;
                            else laser.Dir = 4;
                            break;
                        case 12:
                        case 21:
                            if (laser.Dir == 3) laser.Dir = 2;
                            else laser.Dir = 1;
                            break;
                        case 13:
                        case 22:
                            if (laser.Dir == 1) laser.Dir = 2;
                            else laser.Dir = 3;
                            break;
                        case 14:
                        case 23:
                            if (laser.Dir == 1) laser.Dir = 4;
                            else laser.Dir = 3;
                            break;
                    }
                    UpDateLaserBounce(oDir, laser.Dir);   // hazard #1 lives here
                    SoundPlay(S_Deflb);
                }
                else { /* UpDateLaser() -- paint. */ }
                laser.Firing = 1;                // TRUE
            }
            else
            {
                Game.Tank.Firing = 0;            // FALSE
                // if (laser.Firing) UpDateSprite(laser.X, laser.Y) -- paint.
                if (Game_On || VHSOn) AntiTank();

                // SpeedBug - MGY - 22-11-2002
                if (TestIfConvCanMoveTank())
                    ConvMoving = true;
            }
            if (LaserBounceOnIce) goto LaserMoveJump;
        }


        // ---- LTANK2.C:1655  AntiTank -------------------------------------
        // Run after every consumed key (LTANK.C:617) and from three places in
        // the ice/conveyor code.  Quirk #5 lives here: the four scans go
        // right -> left -> down -> up and the *first* match returns, so with
        // two anti-tanks lined up on the same tank only one of them ever
        // fires, and which one is decided by this order rather than by
        // distance.  Tutor level 42 is literally "Inverse A-T's shooting
        // order", i.e. a level that exists to test it.  Do not reorder the
        // scans and do not turn the returns into a loop over four directions.
        //
        // Each scan walks *from the tank's own cell* outward with CheckLoc,
        // which means the terminating cell is the first thing the tank could
        // not enter -- the anti-tank itself, a wall, or one past the board.
        // Three consequences worth keeping:
        //  - the guard is `x < 16` / `x >= 0`, not a CheckLoc result, so a scan
        //    that walked off the board is rejected by the bound, and the
        //    Game.PF read never happens.
        //  - `Game.Tank.X != x` rejects the degenerate case where the tank is
        //    standing on the anti-tank's cell.
        //  - the scans leave `wasIce` holding whatever the last CheckLoc in the
        //    last scan wrote (quirk #3 again).  Nothing here reads it, but
        //    whoever runs next does -- so AntiTank is a wasIce writer even
        //    though it never mentions the flag.
        //
        // The direction handed to FireLaser is the one pointing back at the
        // tank: the anti-tank found to the right fires left (4), and so on.
        private void AntiTank()
        {
            int x, y;
            // Program Anti tank seek }

            if (Game.Tank.Firing != 0) return;

            x = Game.Tank.X;    // Look to the right
            while (CheckLoc(x, Game.Tank.Y)) x++;
            if ((x < 16) && (Game.PF[x, Game.Tank.Y] == 10) && (Game.Tank.X != x))
            {
                FireLaser(x, Game.Tank.Y, 4, S_Anti2);
                return;
            }
            x = Game.Tank.X;    // Look to the left
            while (CheckLoc(x, Game.Tank.Y)) x--;
            if ((x >= 0) && (Game.PF[x, Game.Tank.Y] == 8) && (Game.Tank.X != x))
            {
                FireLaser(x, Game.Tank.Y, 2, S_Anti2);
                return;
            }
            y = Game.Tank.Y;    // Look Down
            while (CheckLoc(Game.Tank.X, y)) y++;
            if ((y < 16) && (Game.PF[Game.Tank.X, y] == 7) && (Game.Tank.Y != y))
            {
                FireLaser(Game.Tank.X, y, 1, S_Anti2);
                return;
            }
            y = Game.Tank.Y;    // Look Up
            while (CheckLoc(Game.Tank.X, y)) y--;
            if ((y >= 0) && (Game.PF[Game.Tank.X, y] == 9) && (Game.Tank.Y != y))
            {
                FireLaser(Game.Tank.X, y, 3, S_Anti2);
                return;
            }
        }


        // ---- LTANK2.C:1394  IceMoveO -------------------------------------
        // Move an Object on the Ice -- every sliding object, one cell, per tick.
        //
        // QUIRK #6 IS THE LOOP ITSELF.  It walks the stack top-down from
        // SlideMem.count, and both arms of the body can call
        // sub_SlideO_from_Mem, which decrements count and shuffles the entries
        // above the removed one *down*.  So the collection is mutated while it
        // is being iterated, and the original's answer to that is the
        // `if (iSlideObj <= SlideMem.count)` guard -- MGY's own comment is
        // "just in case ...".  Walking top-down is what makes it survive at
        // all: a removal only ever affects indices at or above the cursor.
        // Do not rewrite this as a filtered list or a downward `for` over a
        // snapshot; the shuffling is observable through which object moves next.
        //
        // The rest of the shape, all load-bearing:
        //  - `Mem_to_SlideO` at the top of every iteration, because almost
        //    everything in the body clobbers SlideO -- MoveObj, AntiTank and
        //    sub_SlideO_from_Mem's shuffle all run through it.
        //  - `savei = wasIce` is captured before MoveObj and AntiTank, both of
        //    which overwrite wasIce (quirk #3).  Same reason as IceMoveT.
        //  - The melt is on PF2 here, not PF: the ice is *under* the sliding
        //    object, so it is the second layer that turns to water.  IceMoveT's
        //    equivalent writes PF, because nothing is under the tank.
        //  - The block arm tests the tank's square separately from CheckLoc:
        //    CheckLoc does not know about the tank, so an object slides into
        //    the tank's cell without this test.
        //  - `SlideO_to_Mem(i)` immediately followed by `sub_SlideO_from_Mem(i)`
        //    writes a slot that the shuffle then overwrites -- dead for every
        //    i < count, live only when i == count.  Kept verbatim.
        //  - AntiTank() runs on *both* arms, with MGY's comment on the second
        //    ("incase an anti-tank is behind a block"), and the commented-out
        //    `//return;` beside it is his -- the loop deliberately continues.
        //
        // The tail is subtler than it looks: with count == 0,
        // `Mem_to_SlideO(0)` passes its own `0 <= count` guard and copies slot
        // 0 -- which nothing ever writes, so it is still zeroed -- into SlideO.
        // Emptying the stack therefore *clears* SlideO rather than leaving it
        // stale.  That holds here because TICEMEM.Objects starts zeroed and
        // LoadLevel resets only `count`, exactly as the C global does.
        private void IceMoveO()
        {
            int savei;
            int iSlideObj; // MGY

            for (iSlideObj = SlideMem.count; iSlideObj >= 1; iSlideObj--) // MGY
            {
                Mem_to_SlideO(iSlideObj);            // Get from memory

                if (iSlideObj <= SlideMem.count)     // just in case ... MGY
                {
                    if (Game.PF2[SlideO.x, SlideO.y] == Obj.ThinIce)
                    {
                        Game.BMF2[SlideO.x, SlideO.y] = 9;
                        Game.PF2[SlideO.x, SlideO.y] = Obj.Water;   // Ice to Water
                    }

                    if (CheckLoc(SlideO.x + SlideO.dx, SlideO.y + SlideO.dy) &&
                        (!((SlideO.x + SlideO.dx == Game.Tank.X) && (SlideO.y + SlideO.dy == Game.Tank.Y))))
                    {
                        savei = wasIce ? 1 : 0;      // before MoveObj/AntiTank clobber it
                        MoveObj(SlideO.x, SlideO.y, SlideO.dx, SlideO.dy, S_Push2);
                        AntiTank();

                        SlideO.x += SlideO.dx;       // Update Position
                        SlideO.y += SlideO.dy;       // Update Position
                        if (savei == 0)
                        {
                            SlideO.s = 0;            // The ride is over
                            SlideO_to_Mem(iSlideObj);        // update memory
                            sub_SlideO_from_Mem(iSlideObj);
                        }
                        else
                        {
                            SlideO_to_Mem(iSlideObj);        // update memory
                        }
                    }
                    else
                    {
                        if (Game.PF2[SlideO.x, SlideO.y] == Obj.Water)
                            MoveObj(SlideO.x, SlideO.y, 0, 0, 0);  // Drop Object in the water (was thin ice)
                        SlideO.s = 0;
                        SlideO_to_Mem(iSlideObj);            // update memory
                        sub_SlideO_from_Mem(iSlideObj);
                        AntiTank();                  // incase an anti-tank is behind a block
                        //return; // MGY
                    }
                }
            }

            Mem_to_SlideO(SlideMem.count);   // Get from memory the last object of the list
            SlideO.s = (SlideMem.count > 0) ? 1 : 0;
        }


        // ---- LTANK2.C:1367  IceMoveT -------------------------------------
        // Move the tank on the Ice.  One cell per tick, re-entered from the
        // tick loop for as long as SlideT.s holds.
        //
        // `savei` is the load-bearing local.  It captures wasIce from the
        // CheckLoc on the line above *before* ConvMoveTank runs, and
        // ConvMoveTank ends with AntiTank(), whose scans overwrite wasIce
        // (quirk #3 -- see step 3's note).  Read wasIce after the call instead
        // of saving it and the slide would end on whatever the anti-tank scan
        // happened to probe last.  So `savei` is not a convenience, it is what
        // makes the slide terminate correctly.
        //
        // The thin-ice melt happens at the *start*, on the cell the tank is
        // leaving, and it writes PF (not PF2): thin ice the tank slid off
        // becomes water, which is irreversible and is what the solver's pruning
        // will lean on later.
        //
        // The early `return` on a blocked destination skips the position
        // update, so SlideT.x/y stay pointing at the cell the tank stopped on.
        private void IceMoveT()
        {
            int savei;

            if (Game.PF[SlideT.x, SlideT.y] == Obj.ThinIce)
            {
                Game.BMF[SlideT.x, SlideT.y] = 9;
                Game.PF[SlideT.x, SlideT.y] = Obj.Water;    // Ice to Water
                // UpDateSprite(SlideT.x, SlideT.y) -- paint.
            }

            if (CheckLoc(SlideT.x + SlideT.dx, SlideT.y + SlideT.dy))
            {
                savei = wasIce ? 1 : 0;                     // before AntiTank clobbers it
                ConvMoveTank(SlideT.dx, SlideT.dy, false);  // use this insted of UpDateTank
            }
            else
            {
                SlideT.s = 0;                               // FALSE
                return;
            }

            SlideT.x += SlideT.dx;                          // Update Position
            SlideT.y += SlideT.dy;                          // Update Position
            if (savei == 0) SlideT.s = 0;                   // The ride is over
        }


        // ---- LTANK2.C:1193  ConvMoveTank ---------------------------------
        // Move the tank *without it being a move*.  Compare UpDateTankPos
        // directly above: same position update, same tunnel translation, same
        // Tank.Good handling -- but no UpdateUndo, no ScoreMove++, and no
        // S_Move.  A tank carried by a conveyor or sliding on ice does not
        // spend a move and cannot be undone step-by-step, and that asymmetry
        // between the two functions is the whole reason both exist.
        //
        // Two more differences from UpDateTankPos worth not "harmonising":
        //  - It never clears Tank.Good first.  UpDateTankPos opens with
        //    `Game.Tank.Good = FALSE` ("we need it somewhere if we move off a
        //    tunnel"); this one only ever *sets* it, so a Good left over from a
        //    previous tunnel wait survives a conveyor ride.
        //  - It sets ConvMoving = TRUE, which blocks the next key consume
        //    (LTANK.C:613) -- so being carried costs the player a tick.
        //
        // `check` is FALSE only from IceMoveT, which is already sliding and
        // updates SlideT itself; TRUE from the tick's conveyor arm, where
        // running onto ice has to start a new slide.  wasIce is whatever the
        // caller's CheckLoc left (quirk #3) -- both call sites probe the
        // destination immediately before calling.
        private void ConvMoveTank(int x, int y, bool check)
        {
            // UpDateSprite(Game.Tank.X, Game.Tank.Y) -- paint.
            Game.Tank.Y += y;
            Game.Tank.X += x;
            if (Obj.IsTunnel(Game.PF[Game.Tank.X, Game.Tank.Y]))
            {
                int tx = Game.Tank.X, ty = Game.Tank.Y;
                TranslateTunnel(ref tx, ref ty);     // We moved into a tunnel
                Game.Tank.X = tx; Game.Tank.Y = ty;
                if (BlackHole) PostDead();           // The tunnel was a black hole
            }
            // No else: WaitToTrans keeps whatever the last TranslateTunnel left
            // in it, exactly as in UpDateTankPos.
            if (WaitToTrans) Game.Tank.Good = 1;
            TankDirty = true;
            ConvMoving = true;
            if (wasIce && check)
            {
                SlideT.x = Game.Tank.X;
                SlideT.y = Game.Tank.Y;
                SlideT.s = 1;                        // TRUE
                SlideT.dx = x;
                SlideT.dy = y;
            }
            AntiTank();
        }

        private bool MouseOperation(int sp) => throw new NotPortedException("MouseOperation");

        // ---- LTANK.C:579  the WM_TIMER handler -- this *is* the spec ----------
        // Line-by-line, in the same order as oracle/driver.c's LT_Tick().
        public void Tick()
        {
            if (FindTank)
            {
                FindTank = false;
                PutLevel();
            }
            if (Ani_On) AniCount++;
            if (AniCount == ani_delay) Animate();
            if (Game.Tank.Firing != 0) MoveLaser();

            if (PBOpen)
            {
                if (Speed == 2)
                {
                    SlowPB++;
                    if (SlowPB == SlowPBSet) SlowPB = 1;
                }
                if (PlayBack && !(ConvMoving || SlideO.s != 0 || SlideT.s != 0)
                    && (Speed != 2 || (Speed == 2 && SlowPB == 1)))
                    PBHold = false;
                else
                    PBHold = true;
            }

            // Check Key Press
            if (Game.RecP < (uint)RB_TOS
                && !(Game.Tank.Firing != 0 || ConvMoving || SlideO.s != 0 || SlideT.s != 0 || PBHold))
            {
                switch (RecBuffer[Game.RecP])
                {
                    case VK_UP: MoveTank(1); break;
                    case VK_RIGHT: MoveTank(2); break;
                    case VK_DOWN: MoveTank(3); break;
                    case VK_LEFT: MoveTank(4); break;
                    case VK_SPACE:
                        UpdateUndo();
                        Game.ScoreShot++;            // here, Not in FireLaser
                        // S_Fire, not 0: FireLaser's `laser.Good = (sf == 2)` is
                        // what separates the tank's own shot from an anti-tank's
                        // (S_Anti2), so the sound id is load-bearing here.
                        FireLaser(Game.Tank.X, Game.Tank.Y, Game.Tank.Dir, S_Fire);
                        break;
                }
                Game.RecP++;
                AntiTank();
            }
            if (SlideO.s != 0) IceMoveO();
            if (SlideT.s != 0) IceMoveT();
            if (TankDirty) UpDateTank();
            ConvMoving = false;                      // disables the laser on a conveyor
            switch (Game.PF[Game.Tank.X, Game.Tank.Y])
            {
                case 2:
                    if (Game_On)                     // reached the flag
                    {
                        GameOn(false);
                        // PBOpen is TRUE during playback, so the original skips
                        // CheckHighScore() and LoadNextLevel() here.
                    }
                    break;
                case 3:
                    PostDead();                      // water
                    break;
                case 15:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y - 1)) ConvMoveTank(0, -1, true);
                    break;
                case 16:
                    if (CheckLoc(Game.Tank.X + 1, Game.Tank.Y)) ConvMoveTank(1, 0, true);
                    break;
                case 17:
                    if (CheckLoc(Game.Tank.X, Game.Tank.Y + 1)) ConvMoveTank(0, 1, true);
                    break;
                case 18:
                    if (CheckLoc(Game.Tank.X - 1, Game.Tank.Y)) ConvMoveTank(-1, 0, true);
                    break;
            }

            // Check the mouse Buffer
            if (Game.RecP == (uint)RB_TOS && MB_TOS != MB_SP
                && !(Game.Tank.Firing != 0 || ConvMoving || SlideO.s != 0 || SlideT.s != 0))
            {
                if (MouseOperation(MB_SP))
                {
                    MB_SP++;
                    if (MB_SP == 20) MB_SP = 0;      // MaxMBuffer, LTANK.H:107
                }
                else MB_SP = MB_TOS;
            }
            if (TankDirty) UpDateTank();
        }

        /// The key-consume condition at LTANK.C:613, which is also what
        /// "the world has settled" means for the driver and for the solver.
        public bool Quiescent() =>
            !(Game.Tank.Firing != 0 || ConvMoving || SlideO.s != 0 || SlideT.s != 0);
    }
}
