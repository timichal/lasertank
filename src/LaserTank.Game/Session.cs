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

        // ---- step 4: the game around the game -------------------------------
        /// The .hs / .ghs / recording names AssignHSFile derives from the .lvl
        /// (LTANK2.C:1055).
        public ScoreFiles Files { get; }

        /// Recording state (command 123) and the playback being watched
        /// (command 114).  Both are the driver's, not the engine's -- but the
        /// *playback* fields the tick reads (PBOpen, PlayBack, PBHold, Speed,
        /// SlowPB) are the engine's and have been since Phase 2.
        public Recorder Rec2 { get; }
        public Playback Pb { get; } = new Playback();

        /// What CheckHighScore decided on the last win, or null.  The original
        /// puts this in a dialog (HSBox) the instant the flag is reached; here
        /// the renderer shows it and it survives until the next level.
        public ScoreResult Score { get; private set; }

        /// `OKtoHS` (LTANK2.C:31): set by every level load, cleared when a level
        /// is loaded in a way that must not post a score.  The editor clears it
        /// (LTANK.C:1091); nothing else does, so nothing here does either.
        public bool OkToHS { get; private set; } = true;

        /// **A Session with no Options never writes a .hs**, which is the same
        /// rule `Ini.ReadOnly` is and for the same reasons: eight parallel gate
        /// jobs must not race over one file, and an instrument run must not
        /// change what the next player sees.  `PlayMode` builds its Sessions
        /// without Options, so `tick_check.py` replaying all 208 winning
        /// recordings posts nothing -- it wrote `.hs` files across six
        /// collections of `data/` once, which is how this got noticed.  A
        /// read-only INI (`--shot`, `--check-*`) is held to the same rule.
        private readonly bool _postScores;

        /// The `HS` global (LTANK.H:239), which outlives a level for a reason --
        /// see ScoreState.
        private readonly ScoreState _hs = new ScoreState();

        /// `options` is the persisted state (Phase 5, step 2) and may be null --
        /// PlayMode's synthetic player has none, because a gate must not write
        /// the player's INI.  All it is used for here is [DATA] RLLFilename /
        /// RLLLevel, which LoadLevel writes (LTANK2.C:1035).
        public Session(string lvlPath, Options options = null)
        {
            _lvlPath = lvlPath;
            _opt = options;
            _levelCount = LevelFile.CountLevels(lvlPath);
            Files = new ScoreFiles(lvlPath);
            Rec2 = new Recorder(options);
            _postScores = options != null && !options.Ini.ReadOnly;
        }

        public string LevelPath => _lvlPath;
        public Options Opt => _opt;

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
            // [OPT] Animation, LTANK.C:404.  The only persisted option that
            // changes what a tick does -- AniCount and AniLevel are both trace
            // fields -- so it is set from the file here and nowhere else, and a
            // Session built without Options (PlayMode's synthetic player, every
            // headless gate) keeps Engine's default of true.
            if (_opt != null) e.Ani_On = _opt.AnimationOn;
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

            // LTANK2.C:1025: OKtoHS goes back on with every level.
            OkToHS = true;
            Score = null;

            // LTANK2.C:1035, at the same point in the same function: remember
            // the level so the next session starts here.  Gated on RLL there
            // and in Options.RememberLevel.
            _opt?.RememberLevel(_lvlPath, n);

            // LTANK2.C:1044, the last thing LoadLevel does:
            // `if (ARecord && !PBOpen) SendMessage(WM_COMMAND, 123)`.
            Rec2.OnLevelLoaded(Pb.Open);

            // A playback survives a level load only when the load *is* the
            // playback's own (LoadPlayback sets CurLevel from the header and
            // then loads).  Any other load ends it, because the keystream in
            // RecBuffer belongs to a level that is no longer on screen.
            if (Pb.Open && Pb.Rec != null && Pb.Rec.Level == n) Pb.Install(E);
            else if (Pb.Open) Pb.Close(E);
            return true;
        }

        // ---- the three commands no keystream can reach ----------------------
        /// Command 110 (LTANK.C:946).  -> false when the buffer is spent, which
        /// is what grays the menu item there.
        ///
        /// Undoing a *death* is the DeadBox's "Undo Last Move"
        /// (ID_DEADBOX_UNDO, LTANK.C:727): command 110 and then GameOn(TRUE),
        /// the only path in the original that resumes a dead game.  Undoing a
        /// *win* has no original at all -- the flag case calls LoadNextLevel
        /// immediately, so by the time a player could ask, the next level is
        /// already up. This port waits on a win instead (so the recording is
        /// still there to save), which would make undo-after-win reachable; it
        /// is refused rather than invented.
        /// Command 110 on its own: undo, and leave the timer exactly as it was.
        /// A dead game stays dead here, because that is all command 110 does --
        /// the DeadBox is what adds the GameOn(TRUE), and `UndoDead` is that.
        /// Keeping the two apart is what lets the script drivers in
        /// oracle/driver.c and LaserTank.Cli express the same two things and be
        /// diffed against this one.
        public bool Undo()
        {
            if (E == null || Now == State.Won) return false;
            if (!E.CanUndo) return false;
            E.UndoStep();
            AfterRewind();
            return true;
        }

        /// ID_DEADBOX_UNDO (LTANK.C:727): `SendMessage(WM_COMMAND, 110);
        /// GameOn(TRUE);`.  The only path in the original that brings a dead
        /// game back, and the reason the undo buffer exists at all.
        public bool UndoDead()
        {
            if (!Undo()) return false;
            E.GameOn(true);
            E.Deaths = 0;
            Now = State.Playing;
            return true;
        }

        /// The presentation state that a wholesale replacement of `Game`
        /// invalidates: the tank has teleported, so there is nothing to
        /// interpolate from and no bounce in flight.
        private void AfterRewind()
        {
            LaserBounced = false;
            PrevTankX = E.Game.Tank.X;
            PrevTankY = E.Game.Tank.Y;
        }

        public bool CanUndo => E != null && Now != State.Won && E.CanUndo;

        // ---- what the editor needs of the session ---------------------------

        /// `OKtoHS = FALSE`, which command 201 does on the way into the editor
        /// (LTANK.C:1091).  An edited level is not the level the high-score
        /// file is about, so nothing it does can post a score until the next
        /// load turns the flag back on (LTANK2.C:1025).
        public void NoHighScore() => OkToHS = false;

        /// Command 604's last line: `if (CurLevel > 0) GameOn(TRUE)`
        /// (LTANK.C:1263).  **The board is not reloaded** -- leaving the editor
        /// drops you onto the level you just drew, which is how the original
        /// lets one be tested without saving it first.  A death or a win before
        /// the editor was opened is cleared with it, because the board those
        /// happened on is gone.
        public void EditorResume()
        {
            if (E == null) return;
            E.Deaths = 0;
            E.GameOn(true);
            Now = State.Playing;
            Score = null;
            PrevTankX = E.Game.Tank.X;
            PrevTankY = E.Game.Tank.Y;
            LaserBounced = false;
        }

        /// Commands 111 and 112 (LTANK.C:955).  Restore is grayed until Save has
        /// been used, and that guard is not politeness: see Engine.SaveGame.
        public void SavePos() => E?.SavePosition();

        public bool CanRestore => E != null && E.CanRestore;

        /// **Restore does not resume a dead game.**  Command 112 is three lines
        /// and none of them is GameOn(TRUE) -- the DeadBox offers Undo and
        /// Restart and nothing else, so the position you saved is simply out of
        /// reach once you have drowned.  Adding the resume looked like the only
        /// sane reading of "restore the position I saved" and made this port's
        /// driver disagree with the oracle's on the third case
        /// tools/roundtrip_check.py tried: level 1719, script
        /// `lulf...zfufclllvlldlf..zfrrzzzzfrlfuu...zuffd`, where the C stops at
        /// the death after 18 ticks and a resurrecting `v` played on for 47.
        /// Undo is the way back from a death; that is what the buffer is for.
        public bool RestorePos()
        {
            if (E == null || !E.CanRestore) return false;
            E.RestorePosition();
            AfterRewind();
            return true;
        }

        // ---- command 114, PlayBack Recording --------------------------------
        /// LoadPlayback (LTANK.C:106) then the level its header names.
        /// -> null on success, else the reason.
        public string LoadPlayback(string path)
        {
            if (!Pb.Load(path)) return Pb.Error;
            if (!Load(Pb.Rec.Level))
                return $"cannot load level {Pb.Rec.Level} of {Path.GetFileName(_lvlPath)}";
            // LoadPlayback's own check, and the reason it has a name-search
            // fallback: a .lpb records the level *name* as well as its number,
            // so a collection whose levels have moved is detectable.
            if (E.CurRecData.LName != Pb.Rec.LName)
            {
                string bad = $"\"{Pb.Rec.LName}\" is not level {Pb.Rec.Level} of "
                             + $"{Path.GetFileName(_lvlPath)} (that is "
                             + $"\"{E.CurRecData.LName}\")";
                Pb.Close(E);
                return bad;
            }
            Pb.PanelUp = true;
            return null;
        }

        /// Command 124, RePlay (LTANK.C:1047): rewind the board and replay the
        /// keystream already in the buffer.  Unlike ReStart it *keeps* RB_TOS,
        /// which is what makes it a replay rather than a restart, and it resets
        /// the undo buffer because the old snapshots describe a run that is
        /// being thrown away.
        /// It doubles as the playback panel's Reset (ID_PLAYBOX_03), which is
        /// literally `SendMessage(WM_COMMAND, 105)` followed by `Game.RecP = 0;
        /// RB_TOS = PBRec.Size; PBHold = FALSE` (LTANK_D.C:1093) -- the same
        /// rewind, with the keystream taken from the loaded file rather than
        /// from the buffer.  Load() reinstalls the playback itself when one is
        /// open, so the two cases differ only in where the keys come from.
        public void Replay()
        {
            if (E == null) return;
            int keys = E.RB_TOS;
            byte[] buf = E.RecBuffer;
            bool wasPb = Pb.Open;
            if (!Load(Level)) return;
            if (!wasPb)
            {
                E.RecBuffer = buf;
                E.RB_TOS = keys;
                E.Game.RecP = 0;
            }
        }

        /// ReStart, LTANK.C:889 command 105.  The original restores
        /// CurRecData.PF in place and calls BuildBMField -- which does reset the
        /// tank and the scores -- leaving the same four flags standing that
        /// Load does.  Same reasoning as Load: reload clean instead.
        ///
        /// **The undo buffer does not survive a restart here, and in the
        /// original it does.**  Command 105 never calls ResetUndoBuffer -- it
        /// restores the playfield in place and pushes one more snapshot first
        /// ("Without this we loose the last move"), so in the 2010 binary R and
        /// then U walks back *into the attempt you just abandoned*, resuming its
        /// recording from the middle of a RecBuffer that command 105 rewound but
        /// did not clear.  Reproducing that means keeping the Engine across a
        /// restart, and keeping the Engine is what Load argues against: the four
        /// flags of quirk #12 would carry over, and a recording saved after a
        /// restart would then not replay in a process that started clean --
        /// which every replay of it does, step 4's exit criterion included.  So
        /// the restart is clean and the undo history goes with it.  A documented
        /// loss, not an oversight.
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

        /// AddKBuff, LTANK2.C:256 -- which lives in Engine since step 5,
        /// because MouseOperation calls it and MouseOperation is LTANK2.C code.
        /// This copy is gone rather than kept: unlike the sound-id table and
        /// the WM_KEYDOWN filter above it, there was never anything to check by
        /// carrying it twice -- both copies would have been written from the
        /// same fifteen lines and neither is a decision.
        private void AddKBuff(byte zz)
        {
            E.AddKBuff(zz);
            _recBufSize = E.RecBuffer.Length;
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
            uint oRecP = E.Game.RecP;

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

            // Single Step: the original's tick posts ID_PLAYBOX_02 back at the
            // playback dialog from inside the PBOpen block (LTANK.C:606).  The
            // engine has the block but no dialog, so the pause happens here.
            Pb.AfterTick(E, oRecP);

            // The flag case calls GameOn(FALSE) and a death calls it too, so
            // "the timer stopped" is the signal; which of the two it was is the
            // same test lasertank-core's driver makes.
            if (E.Deaths != 0) Now = State.Dead;
            else if (!E.Game_On)
            {
                Now = E.Game.PF[E.Game.Tank.X, E.Game.Tank.Y] == 2 ? State.Won : State.Dead;
                if (Now == State.Won) OnWin();
            }
            return true;
        }

        /// The flag case's bookkeeping, LTANK.C:646.
        ///
        ///     if (!PBOpen) {
        ///         if (Recording) SendMessage(WM_SaveRec);
        ///         CheckHighScore();
        ///         LoadNextLevel(FALSE, FALSE);
        ///     }
        ///
        /// **`!PBOpen` gates all three**, which is the rule that keeps a
        /// playback from posting the recording's author's score as the player's
        /// own -- watch someone's 12-move solution and it is not yours.  It is
        /// also why the oracle can replay the whole corpus without ever writing
        /// a .hs: `PBOpen` is TRUE for every run it makes.
        ///
        /// Two of the three differ here.  Saving is not automatic -- the
        /// original opens a file dialog (WM_SaveRec) and this port's F6 is that
        /// dialog, so an auto-save would write a file the player did not name.
        /// And LoadNextLevel is not called at all: a Godot win waits, so the
        /// keystream is still there to save, and Enter advances.
        private void OnWin()
        {
            if (Pb.Open || !OkToHS || !_postScores) return;
            Score = HighScores.Check(Files, Level, E.Game.ScoreMove, E.Game.ScoreShot,
                                     _opt.Player, _hs);
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

        /// The interactive save -- command 117 (F6) reaching WM_SaveRec.
        ///
        /// Two things the gates' `Save` above does not do, because they belong
        /// to a player rather than to a measurement: the file is named the way
        /// BuildPB_Name names it (`LaserTank_0001.lpb`, which is the shape every
        /// recording in data/demos/ has), and the author is `[DATA] Record
        /// Author` rather than a fixed string.
        ///
        /// `if (Recording)` is command 117's own guard (LTANK.C:998): F6 does
        /// nothing at all when the recorder is off.  -> the path written, or
        /// null when the recorder is off.
        public string SaveRecording(string dir)
        {
            if (!Rec2.Recording) return null;
            return Save(dir, Rec2.Author,
                        Path.GetFileNameWithoutExtension(Files.PbName(Level)));
        }
    }
}
