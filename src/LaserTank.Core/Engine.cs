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
// STATUS: skeleton.  Ported so far: BuildBMField, PutLevel, GameOn, Animate,
// LoadLevel and the tick loop's frame.  Everything not yet transliterated
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
        public const int S_Push2 = 12, S_Push1 = 13, S_Push3 = 15, S_Sink = 16;

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
        private void MoveTank(int dir) => throw new NotPortedException("MoveTank");
        private void FireLaser(int x, int y, int dir, int snd) => throw new NotPortedException("FireLaser");
        private void MoveLaser() => throw new NotPortedException("MoveLaser");
        private void AntiTank() => throw new NotPortedException("AntiTank");
        private void IceMoveO() => throw new NotPortedException("IceMoveO");
        private void IceMoveT() => throw new NotPortedException("IceMoveT");
        private void ConvMoveTank(int dx, int dy, bool b) => throw new NotPortedException("ConvMoveTank");
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
                        FireLaser(Game.Tank.X, Game.Tank.Y, Game.Tank.Dir, 0);
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
