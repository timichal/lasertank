// Phase 5, step 1's gate, made reproducible.
//
// Step 1's exit criterion is a human playing a level in Godot, winning it,
// saving the keystream, and that .lpb replaying byte-identically through the
// unmodified C oracle.  That is a real proof and it is also a ceremony: it
// cannot be re-run in CI and it covers one level.
//
// This is the same proof with the human replaced by a script.  A synthetic
// player presses the next key from a keystream **whenever the buffer has
// drained**, through Session.Key -- the same WM_KEYDOWN filter and the same
// AddKBuff a keyboard reaches -- and Session.Step runs the tick.  Then the
// recording is saved by the same Save() a human's F6 calls.
//
// Why pressing on drain reproduces the recording exactly: the tick consumes a
// key only when the world is quiescent (LTANK.C:613), so with one key pending
// at a time, consumption happens on precisely the first quiescent tick after
// the previous one -- the same tick a fully preloaded buffer would be consumed
// on.  So the tick sequence matches lasertank-core's --lpb replay, and the
// bytes AddKBuff appended match the bytes that were pressed.  tools/
// tick_check.py then hands the result to the oracle, which knows nothing about
// any of this.
//
//   godot --headless --path src/LaserTank.Game -- --play FILE.lpb --out DIR
//   godot --headless --path src/LaserTank.Game -- --keys uurf --level 7 --out DIR
using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using LaserTank.Core;
using Engine = LaserTank.Core.Engine;

namespace LaserTank.Game
{
    public static class PlayMode
    {
        /// The CLI's --keys alphabet (LaserTank.Cli/Program.cs, driver.c): the
        /// five game keys and nothing else.  A wait cannot be expressed here
        /// because neither engine's parser can express one either -- if that
        /// ever changes, all three change together.
        public static byte[] ParseKeys(string s)
        {
            var outv = new List<byte>(s.Length);
            foreach (char c in s)
                switch (char.ToLowerInvariant(c))
                {
                    case 'u': outv.Add(Engine.VK_UP); break;
                    case 'd': outv.Add(Engine.VK_DOWN); break;
                    case 'l': outv.Add(Engine.VK_LEFT); break;
                    case 'r': outv.Add(Engine.VK_RIGHT); break;
                    case 'f': outv.Add(Engine.VK_SPACE); break;
                }
            return outv.ToArray();
        }

        /// One `<lvl>\t<lpb>\t<stem>` triple per line, played in sequence in
        /// this one process; `stem` names the saved recording, and the caller
        /// keeps stems unique because two collections both have a 00001.lpb.
        ///
        /// 187 recordings as 187 Godot launches is a minute of
        /// process startup; as one launch it is seconds, and it exercises what
        /// the interactive game actually does -- many levels through one
        /// process, each with the fresh Engine Session.Load builds (quirk #12).
        ///
        /// -> 0 if every entry ran; 2 on a setup error.  Whether an entry
        /// *should* have won is not this side's business: tools/tick_check.py
        /// compares each result line with what the C oracle says.
        public static int RunList(string listPath, string outDir, int maxTicks,
                                  int pending, string author)
        {
            var sessions = new Dictionary<string, Session>();
            foreach (string line in File.ReadLines(listPath))
            {
                if (line.Trim().Length == 0) continue;
                string[] f = line.Split('\t');
                if (f.Length != 3)
                {
                    GD.PrintErr("play: expected \"<lvl>\\t<lpb>\\t<stem>\": " + line);
                    return 2;
                }
                TRECORDREC r = LevelFile.ReadPlayback(f[1], out byte[] script);
                if (!sessions.TryGetValue(f[0], out Session s))
                    sessions[f[0]] = s = new Session(f[0]);
                if (Play(s, r.Level, script, r.LName, outDir, maxTicks, pending,
                         author, f[2]) == 2)
                    return 2;
            }
            return 0;
        }

        /// -> process exit code: 0 won, 1 played but did not win, 2 setup error.
        public static int Run(string lvlPath, int level, byte[] script, string lpbName,
                              string outDir, int maxTicks, int pending, string author)
            => Play(new Session(lvlPath), level, script, lpbName, outDir, maxTicks,
                    pending, author, null);

        private static int Play(Session s, int level, byte[] script, string lpbName,
                                string outDir, int maxTicks, int pending, string author,
                                string stem)
        {
            if (!s.Load(level))
            {
                GD.PrintErr("play: " + s.Error);
                return 2;
            }
            if (lpbName != null && s.Rec.LName != lpbName)
            {
                GD.PrintErr($"play: level name mismatch: lpb says \"{lpbName}\", " +
                            $"level {level} is \"{s.Rec.LName}\"");
                return 2;
            }

            int at = 0;
            while (s.Now == Session.State.Playing && s.Ticks < maxTicks)
            {
                // The human: press the next key when there is room for it.
                while (at < script.Length && s.Pending < pending)
                    s.Key(script[at++], echo: false);

                if (!s.Step()) break;

                // lasertank-core's break: out of keys and the world has settled,
                // so nothing further can happen.
                if (at >= script.Length && s.Pending == 0 && s.E.Quiescent()
                    && s.E.Game_On) break;
            }

            string result = s.Now switch
            {
                Session.State.Won => "WIN",
                Session.State.Dead => "DEAD",
                _ => "UNFINISHED",
            };
            // The level name goes last and is never parsed by the tool -- names
            // carry latin-1 bytes that this stdout re-encodes as UTF-8, so
            // tools/tick_check.py reads the name out of the .lvl and the .lpb
            // instead and this field is only for a human reading the log.
            string saved = outDir != null ? s.Save(outDir, author, stem) : "";
            GD.PrintRaw(string.Format(
                "play {0,-10} level={1,-5} ticks={2,-6} moves={3,-4} shots={4,-4} " +
                "keys={5}/{6} out={7}  {8}\n",
                result, level, s.Ticks, s.E.Game.ScoreMove, s.E.Game.ScoreShot,
                s.E.Game.RecP, script.Length,
                saved == "" ? "-" : Path.GetFileName(saved), s.Rec.LName));
            return result == "WIN" ? 0 : 1;
        }
    }
}
