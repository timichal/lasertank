// ---------------------------------------------------------------------------
// The search surface: everything Phase 4's solver needs from the engine, and
// nothing the engine needs from the solver.
//
// Engine.cs is the transliteration and stays that way.  It gains exactly one
// word for this file to exist (`partial`); every line below is additive, no
// game logic is reachable from here, and nothing in here is called on the
// replay path that `lasertank-core.exe` and the oracle share.  A solver bug
// therefore cannot become a fidelity bug -- and in any case every solution the
// solver emits is a .lpb that gets replayed by the *unmodified* oracle before
// it counts (tools/verify_solutions.py).
//
// Three things live here:
//
//   Snapshot/Restore  the whole mutable engine, so a search can back up.  The
//                     rule is "restore everything, hash a subset": staleness is
//                     load-bearing in this engine (wasIce, quirk #3;
//                     WaitToTrans), so a restore that dropped a flag would
//                     silently change behaviour.
//   ApplyKey          one keypress, then tick until the world is quiescent --
//                     the macro-step the Phase 4 plan is built on.  It drives
//                     the *real* tick through the *real* RecBuffer, so a
//                     macro-step and a .lpb replay cannot diverge.
//   StateHash         a canonical key for a transposition table.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace LaserTank.Core
{
    /// What one macro-step ended in.
    public enum StepResult
    {
        Ok,          // the world settled and the game is still on
        Win,         // the tank is on the flag
        Dead,        // the tank died
        Spinning,    // never quiesced within the tick budget -- see below
    }

    /// The whole mutable engine, flat.  Deliberately not a byte blob: a blob
    /// would be smaller but would have to be kept in sync with the field list
    /// by hand, and getting that wrong is invisible until a search reports a
    /// solution that does not replay.
    public sealed class EngineSnapshot
    {
        public readonly byte[] PF = new byte[256];
        public readonly byte[] PF2 = new byte[256];
        public readonly byte[] BMF = new byte[256];
        public readonly byte[] BMF2 = new byte[256];

        public ushort ScoreMove, ScoreShot;
        public uint RecP;
        public TTANKREC Tank, laser;
        public TICEREC SlideT, SlideO;
        public readonly TICEREC[] SlideMem = new TICEREC[TICEMEM.MAX_TICEMEM];
        public int SlideMemCount;

        /// The consumed prefix of RecBuffer -- the path taken to this state.
        ///
        /// This is here because leaving it out is a trap with a long fuse.
        /// Restore rewinds RecP but the engine's RecBuffer is a single shared
        /// array, so without this a breadth-first search silently corrupts its
        /// own answers: every node at a given depth writes over its siblings'
        /// keys, and the winning node then reports whichever prefix happened to
        /// be written last.  A depth-first search never notices (its stack path
        /// *is* the buffer), which is exactly what makes it a fuse -- IDA* was
        /// green while the beam produced solutions that did not replay.
        public byte[] Keys = Array.Empty<byte>();
        public int KeyLen;

        public int AniLevel, AniCount, Deaths, RB_TOS, MB_TOS, MB_SP, Speed, SlowPB;
        public int UndoP, UndoBufSize, UndoRollOver;
        public bool Ani_On, ConvMoving, TankDirty, Game_On, FindTank, LaserBounceOnIce;
        public bool VHSOn, wasIce, WaitToTrans, BlackHole, PBOpen, PlayBack, PBHold;
    }

    public sealed partial class Engine
    {
        /// The five keys the tick's switch acts on (LTANK.C:616).  Any other
        /// byte falls through it and is a legal one-tick wait that still gives
        /// the anti-tanks their turn -- but no human recording in the corpus
        /// contains one (all 54,162 bytes of all 187 .lpb are these five), so
        /// the solver's action set is these five and nothing else.  See
        /// PROGRESS.md, "The wait".
        public static readonly byte[] ActionKeys = { VK_UP, VK_RIGHT, VK_DOWN, VK_LEFT, VK_SPACE };

        /// Put the engine in exactly the state the replay driver puts it in
        /// (LaserTank.Cli/Program.cs, which transliterates oracle_init).  The
        /// solver must search under the same configuration it will emit a .lpb
        /// for, or the two would be searching different games -- PBHold in
        /// particular is part of the key-consume condition.
        public void ConfigureForReplay()
        {
            Ani_On = true;
            PBOpen = true;
            PlayBack = true;
            Speed = 1;
            SlowPB = 1;
        }

        /// Point the engine at a scratch keystream buffer big enough for the
        /// deepest path a search will try.  ApplyKey writes one key at a time
        /// into it at RecP, so the buffer doubles as the path: after a win,
        /// RecBuffer[0..RecP) *is* the solution.
        public void BeginSearch(int maxKeys)
        {
            if (maxKeys < 1) throw new ArgumentOutOfRangeException(nameof(maxKeys));
            RecBuffer = new byte[maxKeys];
            RB_TOS = 0;
            Game.RecP = 0;
        }

        /// The keys consumed so far -- the path to the current state.
        public byte[] PathKeys()
        {
            byte[] k = new byte[Game.RecP];
            Array.Copy(RecBuffer, k, (int)Game.RecP);
            return k;
        }

        public bool OnFlag => Game.PF[Game.Tank.X, Game.Tank.Y] == Obj.Flag;

        /// Win is "the tank is standing on the flag", the same test
        /// LaserTank.Cli/Program.cs and oracle/driver.c both apply.
        public bool Won => Deaths == 0 && OnFlag;

        /// One macro-step: append `vk` to the keystream and tick until it has
        /// been consumed *and* the world has settled again.
        ///
        /// A macro-step is usually two or three ticks, but it is not bounded by
        /// anything cheap.  Level 1491 of the flagship collection ("Grand Prix
        /// 2", whose hint is *"get on the conveyor and watch"*) takes **3,652
        /// ticks** for a single keypress: the tank rides a closed conveyor
        /// circuit right around the board before the world settles.  A tick cap
        /// of a few hundred would call that a hang and throw the level away, so
        /// the cap is a backstop, not the mechanism.
        ///
        /// The mechanism is cycle detection, because some levels genuinely
        /// never settle -- Tutor 43 is called "Smallest eternal cycle" and
        /// Tutor 50 "Smallest eternal cycle 2"; a laser bouncing forever
        /// between two mirrors keeps Tank.Firing set, so the next key is never
        /// consumed. Deterministic engine, so a repeated state means a loop and
        /// nothing after it can differ.  Hashing every tick would cost more
        /// than the tick, so it starts only after `cycleAfter` ticks (long
        /// macro-steps are rare) and samples every 16th after that: a loop of
        /// period p is caught within 16p ticks, and the common case pays
        /// nothing at all.
        ///
        /// Spinning is a real outcome, not a failure -- a search has to be able
        /// to make that move and back out of it.
        public StepResult ApplyKey(byte vk, int tickCap = 100000, int cycleAfter = 256)
        {
            RecBuffer[Game.RecP] = vk;
            RB_TOS = (int)Game.RecP + 1;
            HashSet<ulong> seen = null;

            for (int i = 0; i < tickCap; i++)
            {
                Tick();
                Pump();                       // the driver's lt_stub_pump()
                if (Deaths != 0) return StepResult.Dead;
                if (!Game_On) return OnFlag ? StepResult.Win : StepResult.Dead;
                if (Game.RecP >= (uint)RB_TOS && Quiescent())
                    return OnFlag ? StepResult.Win : StepResult.Ok;

                if (i >= cycleAfter && (i & 15) == 0)
                {
                    seen ??= new HashSet<ulong>();
                    if (!seen.Add(StateHash())) return StepResult.Spinning;
                }
            }
            return StepResult.Spinning;
        }

        // ---- snapshot / restore -------------------------------------------

        public EngineSnapshot Snapshot(EngineSnapshot into = null)
        {
            if (_posted.Count != 0)
                throw new InvalidOperationException(
                    "Snapshot with a posted message pending: call Pump() first, "
                    + "or the restored engine would lose a deferred death (quirk #8).");

            EngineSnapshot s = into ?? new EngineSnapshot();
            Buffer.BlockCopy(Game.PF, 0, s.PF, 0, 256);
            Buffer.BlockCopy(Game.PF2, 0, s.PF2, 0, 256);
            Buffer.BlockCopy(Game.BMF, 0, s.BMF, 0, 256);
            Buffer.BlockCopy(Game.BMF2, 0, s.BMF2, 0, 256);
            s.ScoreMove = Game.ScoreMove;
            s.ScoreShot = Game.ScoreShot;
            s.RecP = Game.RecP;
            s.KeyLen = (int)Game.RecP;
            if (s.Keys.Length < s.KeyLen) s.Keys = new byte[Math.Max(s.KeyLen, 64)];
            Array.Copy(RecBuffer, s.Keys, s.KeyLen);
            s.Tank = Game.Tank;
            s.laser = laser;
            s.SlideT = SlideT;
            s.SlideO = SlideO;
            Array.Copy(SlideMem.Objects, s.SlideMem, TICEMEM.MAX_TICEMEM);
            s.SlideMemCount = SlideMem.count;

            s.AniLevel = AniLevel; s.AniCount = AniCount; s.Deaths = Deaths;
            s.RB_TOS = RB_TOS; s.MB_TOS = MB_TOS; s.MB_SP = MB_SP;
            s.Speed = Speed; s.SlowPB = SlowPB;
            s.UndoP = UndoP; s.UndoBufSize = UndoBufSize; s.UndoRollOver = UndoRollOver;

            s.Ani_On = Ani_On; s.ConvMoving = ConvMoving; s.TankDirty = TankDirty;
            s.Game_On = Game_On; s.FindTank = FindTank;
            s.LaserBounceOnIce = LaserBounceOnIce; s.VHSOn = VHSOn;
            s.wasIce = wasIce; s.WaitToTrans = WaitToTrans; s.BlackHole = BlackHole;
            s.PBOpen = PBOpen; s.PlayBack = PlayBack; s.PBHold = PBHold;
            return s;
        }

        /// The UndoBuffer's *contents* are not snapshotted, and that is a
        /// decision rather than an omission: nothing headless ever calls
        /// UndoStep, so no stored snapshot is ever read back.  UndoP,
        /// UndoBufSize and UndoRollOver are, because they are not dead --
        /// MoveObj's tunnel path decrements UndoP (quirk #7) and UpdateUndo's
        /// growth and roll-over arithmetic reads all three.  The array is only
        /// re-grown, never shrunk, so a restore to a smaller UndoBufSize still
        /// has room to write.
        public void Restore(EngineSnapshot s)
        {
            _posted.Clear();
            Buffer.BlockCopy(s.PF, 0, Game.PF, 0, 256);
            Buffer.BlockCopy(s.PF2, 0, Game.PF2, 0, 256);
            Buffer.BlockCopy(s.BMF, 0, Game.BMF, 0, 256);
            Buffer.BlockCopy(s.BMF2, 0, Game.BMF2, 0, 256);
            Game.ScoreMove = s.ScoreMove;
            Game.ScoreShot = s.ScoreShot;
            Game.RecP = s.RecP;
            Array.Copy(s.Keys, RecBuffer, s.KeyLen);
            Game.Tank = s.Tank;
            laser = s.laser;
            SlideT = s.SlideT;
            SlideO = s.SlideO;
            Array.Copy(s.SlideMem, SlideMem.Objects, TICEMEM.MAX_TICEMEM);
            SlideMem.count = s.SlideMemCount;

            AniLevel = s.AniLevel; AniCount = s.AniCount; Deaths = s.Deaths;
            RB_TOS = s.RB_TOS; MB_TOS = s.MB_TOS; MB_SP = s.MB_SP;
            Speed = s.Speed; SlowPB = s.SlowPB;
            UndoP = s.UndoP; UndoBufSize = s.UndoBufSize; UndoRollOver = s.UndoRollOver;
            if (UndoBuffer.Length < UndoBufSize) Array.Resize(ref UndoBuffer, UndoBufSize);

            Ani_On = s.Ani_On; ConvMoving = s.ConvMoving; TankDirty = s.TankDirty;
            Game_On = s.Game_On; FindTank = s.FindTank;
            LaserBounceOnIce = s.LaserBounceOnIce; VHSOn = s.VHSOn;
            wasIce = s.wasIce; WaitToTrans = s.WaitToTrans; BlackHole = s.BlackHole;
            PBOpen = s.PBOpen; PlayBack = s.PlayBack; PBHold = s.PBHold;
        }

        // ---- transposition key --------------------------------------------

        /// FNV-1a over everything that can change what happens next.
        ///
        /// Left out on purpose, with reasons, because a hash that is too narrow
        /// loses solutions and one that is too wide just loses dedup:
        ///
        ///   BMF, BMF2, AniLevel, AniCount  cosmetic.  Every read of BMF in the
        ///       whole program is a paint call or MoveObj's sprite carry; no
        ///       bitmap feeds a decision (quirk #2, verified by grep in Phase 1).
        ///   ScoreMove, ScoreShot, UndoP     counters.  Two paths to the same
        ///       board are the same position; the search keeps the cheaper one.
        ///   RecP, RB_TOS                    the path, not the position.
        ///   laser, SlideT, SlideO, SlideMem zero at a macro-step boundary by
        ///       construction -- ApplyKey only returns when Quiescent().  They
        ///       are folded in anyway (cheaply) so the hash is still correct if
        ///       someone hashes mid-step.
        ///
        /// Kept, and easy to think are cosmetic:
        ///   Tank.Good     the tunnel-wait flag, read by MoveObj.
        ///   wasIce        quirk #3: CheckLoc leaves it *stale* on an off-board
        ///                 probe, and MoveTank's `if (wasIce)` then reads what a
        ///                 previous tick left, so it is genuine live state.
        ///   WaitToTrans, BlackHole   same story, written only by TranslateTunnel
        ///                 and read after moves that never touched a tunnel.
        public ulong StateHash()
        {
            const ulong Prime = 0x100000001b3UL;
            ulong h = 0xcbf29ce484222325UL;

            for (int x = 0; x < TGAMEREC.W; x++)
                for (int y = 0; y < TGAMEREC.H; y++)
                {
                    h = (h ^ Game.PF[x, y]) * Prime;
                    h = (h ^ Game.PF2[x, y]) * Prime;
                }

            h = Mix(h, Game.Tank.X); h = Mix(h, Game.Tank.Y);
            h = Mix(h, Game.Tank.Dir); h = Mix(h, Game.Tank.Firing);
            h = Mix(h, Game.Tank.Good);
            h = Mix(h, laser.X); h = Mix(h, laser.Y);
            h = Mix(h, laser.Dir); h = Mix(h, laser.Firing); h = Mix(h, laser.Good);
            h = Mix(h, SlideT.x); h = Mix(h, SlideT.y);
            h = Mix(h, SlideT.dx); h = Mix(h, SlideT.dy); h = Mix(h, SlideT.s);
            h = Mix(h, SlideO.x); h = Mix(h, SlideO.y);
            h = Mix(h, SlideO.dx); h = Mix(h, SlideO.dy); h = Mix(h, SlideO.s);
            h = Mix(h, SlideMem.count);
            for (int i = 1; i <= SlideMem.count && i < TICEMEM.MAX_TICEMEM; i++)
            {
                h = Mix(h, SlideMem.Objects[i].x); h = Mix(h, SlideMem.Objects[i].y);
                h = Mix(h, SlideMem.Objects[i].dx); h = Mix(h, SlideMem.Objects[i].dy);
                h = Mix(h, SlideMem.Objects[i].s);
            }
            h = Mix(h, (ConvMoving ? 1 : 0) | (wasIce ? 2 : 0)
                     | (WaitToTrans ? 4 : 0) | (BlackHole ? 8 : 0)
                     | (Game_On ? 16 : 0) | (PBHold ? 32 : 0));
            return h;

            static ulong Mix(ulong acc, int v)
            {
                acc = (acc ^ (uint)(v & 0xFF)) * Prime;
                acc = (acc ^ (uint)((v >> 8) & 0xFF)) * Prime;
                return acc;
            }
        }
    }
}
