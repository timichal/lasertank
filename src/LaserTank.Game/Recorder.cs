// Phase 5, step 4: recording and playback.
//
// The recording half is nearly free -- Session already writes a .lpb the C
// oracle replays byte-identically, which is what step 1's gate proves.  What is
// missing is the *state* around it: whether the game is recording at all
// (command 123, F5, which retitles the window to "LaserTank *** RECORDING ***"),
// the author name it stamps into the header ([DATA] Record Author, asked for
// once by RecordBox), and Auto_Record, which starts a recording on every level
// load (LTANK2.C:1044).
//
// The playback half is *also* nearly free, and that is the surprising part: the
// engine has carried the whole thing since Phase 2.  `PBOpen`, `PlayBack`,
// `PBHold`, `Speed` and `SlowPB` are read by the tick itself (Engine.cs:1294,
// transliterating LTANK.C:593) -- the block that decides whether this tick may
// take a key, throttles Slow to one key in five, and re-pauses after every key
// in Single Step.  So playback needs no new rules, only the four buttons of
// PBWindow (LTANK_D.C:1003) wired to those five fields:
//
//   Play / Pause   PlayBack = !PlayBack               ID_PLAYBOX_02
//   Reset          command 105, then RecP = 0,        ID_PLAYBOX_03
//                  RB_TOS = PBRec.Size, PBHold = FALSE
//   Fast/Slow/Step Speed = 1 / 2 / 3                  ID_PLAYBOX_04..06
//   Close          RB_TOS = Game.RecP, and offer to   ID_PLAYBOX_01
//                  keep recording from here
//
// Two things about that list are load-bearing:
//
//   * **Close rewinds RB_TOS to Game.RecP** -- "incase we stop short".  Stopping
//     a playback halfway and then playing on by hand gives a keystream that is
//     the prefix you watched plus what you did next, which is exactly what
//     "Resume Recording" (command 125) is for.
//   * **Single Step is implemented by the tick pausing itself.**  Speed 3 posts
//     the Play/Pause command back at the dialog from inside the tick, so one
//     key is consumed and playback stops again.  The engine's transliteration
//     does not do the posting -- it cannot, it has no dialog -- so `Speed == 3`
//     is handled here, after the tick, by clearing PlayBack.  That is the one
//     place this file substitutes for a line of the original rather than
//     configuring it, and it is called out in Session.Step.
using System;
using System.IO;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// The three speeds of PBWindow's radio group, as the original numbers them.
    public enum PbSpeed { Fast = 1, Slow = 2, Step = 3 }

    /// The recorder's own state, which is the window title in the original and
    /// two INI keys.
    public sealed class Recorder
    {
        /// `Recording` (LTANK2.C:33) -- command 123 toggles it, and command 117
        /// (F6, Save Recording) does nothing without it.
        public bool Recording { get; private set; }

        /// `ARecord`, [OPT] Auto_Record: start recording on every level load.
        /// Command 115 keeps the two in step -- turning Auto_Record on posts 123
        /// if recording is off, and off posts it if on (LTANK.C:978).
        public bool AutoRecord { get; private set; }

        /// `PBSRec.Author`, [DATA] Record Author.  The original asks for it once,
        /// in RecordBox, on the first save of a session (`if (PBSRec.Author[0]
        /// == 0)`) and remembers it in the INI from then on.  There is no dialog
        /// here yet, so the INI value is used and an empty one stays empty --
        /// which the C also allows: RecordBox's edit box can be left blank.
        public string Author { get; private set; }

        private readonly Options _opt;

        public Recorder(Options opt)
        {
            _opt = opt;
            AutoRecord = opt != null && opt.AutoRecord;
            Author = opt?.RecordAuthor ?? "";
            // LTANK2.C:1044: `if (ARecord && !PBOpen) SendMessage(123)`.  The
            // first level load happens right after this, so starting here is
            // the same thing one call earlier.
            Recording = AutoRecord;
        }

        /// Command 123 (LTANK.C:1031).  The original's checkmark is the window
        /// title; here it is the HUD.
        public bool Toggle() => Recording = !Recording;

        /// Command 115 (LTANK.C:978), including the half that is easy to miss:
        /// it does not merely persist a flag, it turns the *recorder* on or off
        /// with it.
        public bool ToggleAutoRecord()
        {
            AutoRecord = !AutoRecord;
            _opt?.SetAutoRecord(AutoRecord);
            Recording = AutoRecord;
            return AutoRecord;
        }

        /// LTANK2.C:1044, called from Session.Load: a level load starts a
        /// recording when Auto_Record is on and nothing is being played back.
        public void OnLevelLoaded(bool playbackOpen)
        {
            if (AutoRecord && !playbackOpen) Recording = true;
        }

        public void SetAuthor(string name)
        {
            Author = name ?? "";
            _opt?.SetRecordAuthor(Author);
        }
    }

    /// One loaded .lpb and the controls over it -- LoadPlayback (LTANK.C:106)
    /// and PBWindow (LTANK_D.C:1003).
    public sealed class Playback
    {
        /// `PBRec`, the 66-byte header of the file being played.
        public TRECORDREC Rec { get; private set; }
        public byte[] Keys { get; private set; } = Array.Empty<byte>();
        public string Path { get; private set; }
        public string Error { get; private set; }

        /// True between LoadPlayback and Close -- `PBOpen`.  The engine reads
        /// its own copy; this is the UI's.
        public bool Open { get; private set; }

        /// Whether the panel is on screen.  A playback can be open with the
        /// panel hidden (command 125, Resume Recording, runs the whole
        /// keystream with no dialog at all), so these are two flags.
        public bool PanelUp { get; set; }

        public PbSpeed Speed { get; private set; } = PbSpeed.Fast;

        /// LoadPlayback (LTANK.C:106): read the file, and let the caller load
        /// the level its header names.  The original's "hard file search for the
        /// level name" fallback -- for when the levels have moved inside the
        /// .lvl -- is not here: `Session.Load` plus the name check below give
        /// the same answer for a matching file and a clear error for a
        /// mismatched one, and searching 2,030 records to guess what the player
        /// meant is a feature, not a rule.
        public bool Load(string path)
        {
            Error = null;
            try
            {
                Rec = LevelFile.ReadPlayback(path, out byte[] keys);
                Keys = keys;
                Path = path;
                Open = true;
                return true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Open = false;
                return false;
            }
        }

        /// Install the keystream into the engine, the way LoadPlayback does
        /// *after* the level is loaded (LoadNextLevel resets both of these).
        ///
        /// `RecBuffer` is replaced rather than copied into, which is
        /// LoadPlayback's GlobalReAlloc when `RecBufSize <= PBRec.Size`: the
        /// buffer grows to fit the recording and stays that size.  Session then
        /// keeps appending to it if the player plays on past the end.
        public void Install(Engine e)
        {
            byte[] buf = new byte[Math.Max(Keys.Length + 1, 10000)];
            Array.Copy(Keys, buf, Keys.Length);
            e.RecBuffer = buf;
            e.RB_TOS = Keys.Length;
            e.Game.RecP = 0;
            e.PBOpen = true;
            e.PlayBack = false;      // the dialog opens paused; Play starts it
            e.PBHold = true;
            e.Speed = (int)Speed;
            e.SlowPB = 1;
        }

        public void SetSpeed(Engine e, PbSpeed s)
        {
            Speed = s;
            if (e != null) e.Speed = (int)s;
        }

        /// ID_PLAYBOX_02.
        public bool TogglePlay(Engine e)
        {
            if (e == null) return false;
            e.PlayBack = !e.PlayBack;
            return e.PlayBack;
        }

        /// ID_PLAYBOX_01 / id 1.  `RB_TOS = Game.RecP` -- "incase we stop
        /// short": what was watched becomes the recording, and the rest of the
        /// file is dropped.  The original then offers to carry on recording from
        /// there if the whole thing was watched (txt016); that offer is a
        /// message box, so here the recorder is simply left as it was and the
        /// player can press F5.
        public void Close(Engine e)
        {
            Open = false;
            PanelUp = false;
            if (e == null) return;
            e.PlayBack = false;
            e.PBOpen = false;
            e.PBHold = false;
            e.RB_TOS = (int)e.Game.RecP;
        }

        /// The one line of PBWindow that has to live outside the engine.
        ///
        /// In Single Step the original's tick posts ID_PLAYBOX_02 back to the
        /// dialog from inside the PBOpen block (LTANK.C:606), so the tick that
        /// released a key immediately pauses playback again.  Engine.Tick has
        /// the same block and the same test, but no dialog to post to, so it
        /// leaves PlayBack alone -- and the pause happens here, after the tick,
        /// where the driver can see that a key was consumed.
        ///
        /// `recpBefore` is Game.RecP as it was before the tick: the original
        /// pauses when the *block* ran (i.e. it was going to release a key), and
        /// a key having been consumed is the observable form of that.
        public void AfterTick(Engine e, uint recpBefore)
        {
            if (e != null && Open && Speed == PbSpeed.Step && e.Game.RecP != recpBefore)
                e.PlayBack = false;
        }

        /// Where to look for a recording of this level, in order, standing in for
        /// the Open dialog command 114 puts up.
        ///
        /// The first two are BuildPB_Name's own name (`LaserTank_0001.lpb`,
        /// LTANK.C:373) in the two places this port writes: `out/recordings/`,
        /// where F6 has written since step 1, and beside the `.lvl`, where the
        /// original's Save dialog opens. The third is this repo's own layout --
        /// `data/demos/<collection>/00001.lpb`, five digits and no stem, which is
        /// how the 187 hand-made recordings of the corpus are stored and
        /// therefore the only place F7 can find anything on a fresh checkout.
        /// A real file dialog makes all three moot; until there is one, a list
        /// beats a single guess.
        public static string[] Candidates(string root, string lvlPath, int level)
        {
            string stem = new ScoreFiles(lvlPath).PbName(level);
            string coll = System.IO.Path.GetFileNameWithoutExtension(lvlPath);
            return new[]
            {
                System.IO.Path.Combine(root, "out", "recordings",
                                       System.IO.Path.GetFileName(stem)),
                stem,
                System.IO.Path.Combine(root, "data", "demos", coll,
                                       level.ToString("D5") + ".lpb"),
            };
        }
    }
}
