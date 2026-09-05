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

            Game.RecP = 0;
            RB_TOS = 0;
            SlideT.s = 0;
            SlideO.s = 0;
            SlideMem.count = 0;
            return true;
        }

        // ---- not transliterated yet ------------------------------------------
        // Ported in this order (PROGRESS.md, Phase 2 step 1..n):
        //   CheckLoc -> MoveObj -> CheckLLoc -> MoveLaser -> AntiTank
        //   -> IceMoveT/IceMoveO -> conveyor -> tick loop
        private bool CheckLoc(int x, int y) => throw new NotPortedException("CheckLoc");
        private void MoveTank(int dir) => throw new NotPortedException("MoveTank");
        private void FireLaser(int x, int y, int dir, int snd) => throw new NotPortedException("FireLaser");
        private void UpdateUndo() => throw new NotPortedException("UpdateUndo");
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
