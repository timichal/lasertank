// Phase 5, step 1: one level in progress, and the 20 Hz tick that drives it.
//
// This is the Godot replacement for LTANK.C's window proc -- the parts of it
// that are the *driver* rather than the game: the WM_TIMER that calls Tick(),
// the WM_KEYDOWN that appends to RecBuffer, WM_Dead, the flag case's
// bookkeeping, ReStart (command 105) and WM_SaveRec.  Every rule still lives
// in LaserTank.Core; nothing here decides what a tick does, only when one
// happens and which bytes were pressed.
//
// Two things in here are load-bearing and easy to get wrong:
//
//   * `Step()` is the whole 50 ms unit -- Tick() then Pump(), in that order,
//     because quirk #8's PostMessage deaths land *after* the tick and
//     SendMessage deaths land inside it.  The driver must not peek at state
//     between the two.
//
//   * ticks only run while `Game_On`.  In the original `GameOn()` is literally
//     SetTimer / KillTimer (LTANK2.C:881), so a dead or finished game receives
//     no ticks at all.  Calling Tick() on a finished game would not crash --
//     it would quietly keep playing.
using System;
using System.IO;
using LaserTank.Core;
using Engine = LaserTank.Core.Engine;

namespace LaserTank.Game
{
    public sealed class Session
    {
        /// LTANK.H:96.  The tick rate, not a frame rate: project.godot sets
        /// physics_ticks_per_second to 1000 / GameDelay and BoardView checks it.
        public const int GameDelayMs = 50;

        // LTANK2.C:51 and LTANK.H:104.  The recording buffer outlives the
        // level, exactly as it does in the original -- InitBuffers runs once at
        // WM_CREATE and every LoadLevel only resets RB_TOS.
        private const int RecBufSize0 = 10000;
        private const int RecBufStep = 10000;

        public enum State { Playing, Won, Dead }

        public Engine E { get; private set; }
        public State Now { get; private set; }
        public int Level { get; private set; }
        public long Ticks { get; private set; }
        public string Error { get; private set; }

        // ---- presentation-only bookkeeping, refreshed by Step() -------------
        // The renderer needs two things the engine does not keep, because the
        // original painted them *during* the tick and then forgot them:
        // where the tank was before this tick (to interpolate), and the laser's
        // incoming direction (UpDateLaserBounce's `a`).  Both are derived by
        // watching state across a tick -- no engine field is written here.
        public int PrevTankX, PrevTankY;
        public bool LaserBounced;
        public int LaserFromDir;

        /// The SoundPlay ids this tick asked for, in call order (Phase 5,
        /// step 3).  The engine appends; nothing here decides which sound
        /// fires or when -- Engine.SoundLog is the same list the CLI's --sound
        /// trace dumps, and tools/sound_check.py diffs that against the C
        /// oracle's own SoundPlay sequence.  Sfx.PlayTick decides what is
        /// *audible* out of it, which is a different question.
        private readonly System.Collections.Generic.List<int> _sounds =
            new System.Collections.Generic.List<int>();

        public System.Collections.Generic.IReadOnlyList<int> Sounds => _sounds;

        private readonly string _lvlPath;
        private readonly Options _opt;
        private int _recBufSize;
        private int _levelCount;

        /// `options` is the persisted state (Phase 5, step 2) and may be null --
        /// PlayMode's synthetic player has none, because a gate must not write
        /// the player's INI.  All it is used for here is [DATA] RLLFilename /
        /// RLLLevel, which LoadLevel writes (LTANK2.C:1035).
        public Session(string lvlPath, Options options = null)
        {
            _lvlPath = lvlPath;
            _opt = options;
            _levelCount = LevelFile.CountLevels(lvlPath);
        }

        public int LevelCount => _levelCount;
        public TLEVEL Rec => E?.CurRecData;

        /// Load a level, wrapping at both ends.
        ///
        /// A **fresh Engine per level**, which is a deliberate deviation from
        /// the original and the one place this file does not transliterate.
        /// LoadLevel does not reset `wasIce`, `WaitToTrans`, `ConvMoving` or
        /// `BlackHole` (quirk #12) -- faithfully, because the original never
        /// reloaded a level into a fresh process either.  But every keystream
        /// this session records is going to be replayed somewhere that *did*
        /// start clean: the C oracle, the 2010 binary, tools/verify_solutions.
        /// A clean start is also the only configuration the whole oracle
        /// equivalence was ever established for -- every fidelity gate builds a
        /// fresh engine per case.  So the port starts every level clean and a
        /// recording it writes always replays.  No rule moved: the four flags
        /// are uninitialised state, not a rule.
        public bool Load(int n)
        {
            if (n < 1) n = _levelCount;
            if (n > _levelCount) n = 1;

            var e = new Engine();
            // Opt in to the sound sink.  Null means SoundPlay does nothing at
            // all, which is what the solver and every headless trace get.
            e.SoundLog = _sounds;
            _sounds.Clear();
            if (!e.LoadLevel(_lvlPath, n))
            {
                Error = $"cannot load level {n} of {_lvlPath}";
                return false;
            }
            E = e;
            Level = n;
            Error = null;
            Ticks = 0;
            Now = State.Playing;
            LaserBounced = false;
            LaserFromDir = 0;
            PrevTankX = E.Game.Tank.X;
            PrevTankY = E.Game.Tank.Y;

            // InitBuffers (LTANK2.C:392): the recording buffer is allocated
            // once and reused.  LoadLevel already set RB_TOS = RecP = 0.
            _recBufSize = Math.Max(_recBufSize, RecBufSize0);
            E.RecBuffer = new byte[_recBufSize];

            // LTANK2.C:1035, at the same point in the same function: remember
            // the level so the next session starts here.  Gated on RLL there
            // and in Options.RememberLevel.
            _opt?.RememberLevel(_lvlPath, n);
            return true;
        }

        /// ReStart, LTANK.C:889 command 105.  The original restores
        /// CurRecData.PF in place and calls BuildBMField -- which does reset the
        /// tank and the scores -- leaving the same four flags standing that
        /// Load does.  Same reasoning as Load: reload clean instead.  (The
        /// `if (UndoP > 0) UpdateUndo()` the original does first belongs to the
        /// undo buffer, which is step 4.)
        public void Restart() => Load(Level);

        // ---- WM_KEYDOWN, LTANK.C:570 ---------------------------------------
        /// Feed one key press.  `vk` is a Win32 virtual-key code and `echo` is
        /// the auto-repeat bit (lparam & 0x40000000).
        ///
        /// The filter is the original's, and both halves matter:
        ///
        ///   * **32..40, not the five game keys.**  VK_SPACE is 32 and the four
        ///     arrows are 37..40, but 33..36 -- PageUp, PageDown, End, Home --
        ///     are inside the range too, and AddKBuff filters nothing.  The
        ///     tick's switch has no `default` and `RecP++` runs regardless
        ///     (LTANK.C:616), so those four record a legal one-tick **wait**
        ///     that still gives the anti-tanks their turn.  No human ever did:
        ///     all 54,162 bytes of all 187 corpus .lpb are the five.  It stays
        ///     reachable here because it is reachable there.
        ///
        ///   * **auto-repeat is dropped only while a key is still pending.**
        ///     `(RB_TOS > Game.RecP) && (lparam & 0x40000000)` is what keeps a
        ///     held-down arrow from flooding the buffer while the tank is busy,
        ///     and what lets it keep the tank moving once the buffer drains.
        ///     It is also the answer to hazard #10's "a 144 Hz display must not
        ///     consume 144 keys a second": the frame rate never enters into it,
        ///     the pending-key test does.
        public void Key(int vk, bool echo)
        {
            if (E == null) return;
            if (vk < 32 || vk > 40) return;
            if (E.RB_TOS > (int)E.Game.RecP && echo) return;
            AddKBuff((byte)vk);
        }

        /// AddKBuff, LTANK2.C:256.  Append and grow; the growth is a
        /// GlobalReAlloc there and cannot fail here, so the original's
        /// FileError / `RB_TOS = 0` arm has nothing to transliterate into.
        private void AddKBuff(byte zz)
        {
            E.RecBuffer[E.RB_TOS] = zz;
            E.RB_TOS++;
            if (E.RB_TOS >= _recBufSize)
            {
                int i = _recBufSize + RecBufStep;
                Array.Resize(ref E.RecBuffer, i);
                _recBufSize = i;
            }
        }

        /// How many keys are pressed but not yet consumed.  The renderer shows
        /// it; the synthetic player in PlayMode presses on drain.
        public int Pending => E == null ? 0 : E.RB_TOS - (int)E.Game.RecP;

        // ---- WM_TIMER, LTANK.C:579 -----------------------------------------
        /// One 50 ms tick.  Returns false when the timer is off -- i.e. when
        /// GameOn(FALSE) has been called, which is what winning and dying do.
        public bool Step()
        {
            if (E == null || !E.Game_On) return false;

            PrevTankX = E.Game.Tank.X;
            PrevTankY = E.Game.Tank.Y;
            _sounds.Clear();          // SF is per tick (driver.c: sf_n = 0)
            int oDir = E.laser.Dir, oX = E.laser.X, oY = E.laser.Y;
            bool wasFiring = E.Game.Tank.Firing != 0;

            E.Tick();
            E.Pump();               // quirk #8: the deferred deaths land here
                                    // -- and with them S_Die, so the sounds of
                                    // a tick are only complete after the pump

            Ticks++;

            // UpDateLaserBounce's `a` argument, recovered by observation.  The
            // paint call itself is the core's -- it sets LaserBounceOnIce
            // (hazard #1) and the renderer must never call, skip or
            // reimplement it.  Watching laser.Dir change across a tick reads
            // the same fact without touching it.
            //
            // The distance test is what hazard #1 costs a retained-mode
            // renderer.  When the laser bounces off a mirror that is itself
            // sliding on ice, UpDateLaserBounce sets LaserBounceOnIce and
            // MoveLaser `goto`s back for a *second* step in the same tick, so
            // the bounce happened one cell back and the laser is now somewhere
            // it went straight through -- painting the two half-bars at its
            // current cell would draw a bend that is not there.  Two cells of
            // travel is exactly that case, so the bounce glyph is suppressed
            // and the ordinary straight bar drawn instead.  What is lost is the
            // bend itself, for one 50 ms frame; what is avoided is drawing it
            // in the wrong cell.  Tutor-with-Playbacks 93 (tick 527) and 94
            // (tick 206) are the only recordings in the corpus that get here.
            int moved = Math.Abs(E.laser.X - oX) + Math.Abs(E.laser.Y - oY);
            LaserBounced = wasFiring && E.Game.Tank.Firing != 0
                           && E.laser.Dir != oDir && moved == 1;
            LaserFromDir = oDir;

            // The flag case calls GameOn(FALSE) and a death calls it too, so
            // "the timer stopped" is the signal; which of the two it was is the
            // same test lasertank-core's driver makes.
            if (E.Deaths != 0) Now = State.Dead;
            else if (!E.Game_On)
                Now = E.Game.PF[E.Game.Tank.X, E.Game.Tank.Y] == 2 ? State.Won : State.Dead;
            return true;
        }

        // ---- WM_SaveRec, LTANK.C:702 ---------------------------------------
        /// Write the recording as a .lpb.
        ///
        /// `PBSRec.Size = Game.RecP` -- the keys **consumed**, not RB_TOS.  Keys
        /// pressed and not yet acted on are not part of the recording, which is
        /// what makes a recording saved the instant a level is won end exactly
        /// at the winning move.  (RecMax caps it at 65500; WritePlayback
        /// refuses a longer one rather than truncating.)
        public string Save(string dir, string author = "LTGodot", string stem = null)
        {
            if (E == null) throw new InvalidOperationException("no level loaded");
            Directory.CreateDirectory(dir);
            var keys = new byte[(int)E.Game.RecP];
            Array.Copy(E.RecBuffer, keys, keys.Length);
            string path = Path.Combine(dir, (stem ?? $"{Level:D5}") + ".lpb");
            LevelFile.WritePlayback(path, E.CurRecData.LName, author, Level, keys);
            return path;
        }
    }
}
