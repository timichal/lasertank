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

        // ---- step 4: scripts, and replaying what they recorded --------------
        /// `--script STR --level N`: the token stream of oracle/driver.c's
        /// `script_feed` and LaserTank.Cli's `Feed`, run through **the game's own
        /// command path** -- Session.Undo, Session.UndoDead, Session.SavePos,
        /// Session.RestorePos -- rather than through Engine directly.
        ///
        /// This is the third implementation of a fifteen-line rule, which is
        /// deliberate and is the same argument the id -> sound-name table is
        /// carried twice for: the other two prove the *engine* undoes correctly,
        /// and only this one proves that the thing the U key calls does.  All
        /// three must produce the same trace, and tools/undo_check.py and
        /// tools/roundtrip_check.py are where that is checked.
        ///
        ///   godot --headless --path src/LaserTank.Game -- \
        ///       --play --script "uufz.zllZ" --level 7 --out DIR
        ///
        /// `options` is null for a gate run and the real thing for a `--ini`
        /// one, which is the only way to exercise the *whole* win path headless:
        /// a Session with no Options posts no high score (see Session's
        /// `_postScores`), so `--play --script --ini FILE` is what proves that
        /// reaching the flag writes a `.hs`.
        public static int RunScript(string lvlPath, int level, string script,
                                    string outDir, int maxTicks, string author,
                                    string stem, Options options)
        {
            var s = new Session(lvlPath, options);
            if (!s.Load(level))
            {
                GD.PrintErr("play: " + s.Error);
                return 2;
            }
            // A script's keys go in one at a time and the buffer must be able to
            // hold them all; Session grows it, as AddKBuff does.
            int at = 0;
            long ticks = 0;
            while (ticks < maxTicks)
            {
                int before = at;
                Feed(s, script, ref at);
                if (s.Now == Session.State.Playing && s.E.Game_On)
                {
                    if (!s.Step()) break;
                    ticks++;
                }
                else if (at == before)
                {
                    break;      // no tick to advance the clock, no token taken
                }
                if (at >= script.Length && s.Pending == 0 && s.E.Quiescent()
                    && s.E.Game_On) break;
            }
            int rc = Report(s, script.Length, outDir, author, stem, "script");
            // What CheckHighScore decided, when it was allowed to decide
            // anything.  One line, so a gate can assert the write happened
            // without reading the file back the same way the writer wrote it.
            if (s.Score != null)
                GD.PrintRaw(string.Format(
                    "highscore level={0} personal={1} global={2} old={3} target={4}{5}\n",
                    s.Level, s.Score.Personal ? 1 : 0, s.Score.Global ? 1 : 0,
                    HighScores.Describe(s.Score.Old),
                    HighScores.Describe(s.Score.Target),
                    s.Score.Error == null ? "" : " error=" + s.Score.Error));
            return rc;
        }

        /// One script token through the game's own driver.  Mirrors
        /// LaserTank.Cli.Program.Feed; see RunScript on why there are three.
        private static void Feed(Session s, string script, ref int at)
        {
            if (at >= script.Length) return;
            char c = script[at];
            byte vk = c switch
            {
                'u' or 'U' => Engine.VK_UP,
                'd' or 'D' => Engine.VK_DOWN,
                'l' or 'L' => Engine.VK_LEFT,
                'r' or 'R' => Engine.VK_RIGHT,
                'f' or 'F' => Engine.VK_SPACE,
                _ => (byte)0,
            };
            if (vk != 0)
            {
                if (s.Pending != 0) return;         // still pending: wait
                s.Key(vk, echo: false);
                at++;
                return;
            }
            at++;
            switch (c)
            {
                case '.': break;
                case 'z': s.Undo(); break;
                case 'Z': s.UndoDead(); break;
                case 'c': s.SavePos(); break;
                // The menu's own guard, as in the other two drivers: restoring
                // a position that was never saved is unreachable in the original
                // and reaches C undefined behaviour.  See Engine.SaveGame.
                case 'v': s.RestorePos(); break;
                default: break;
            }
        }

        /// `--replay FILE.lpb`: watch a recording through the game's *playback*
        /// path -- Session.LoadPlayback, then the engine's own PBOpen block --
        /// rather than by pressing its keys.
        ///
        /// This is the half of step 4's exit criterion that step 1's gate could
        /// not make: `--play --lpb` presses the keystream as a player would,
        /// which is not what watching a recording does.  Playback runs the same
        /// tick with PBOpen set, PBHold gating the key, and Speed throttling it,
        /// and none of that had ever been exercised outside the CLI's fixed
        /// Speed = 1.
        public static int RunReplay(string lvlPath, string lpbPath, int maxTicks,
                                    PbSpeed speed)
        {
            var s = new Session(lvlPath);
            string bad = s.LoadPlayback(lpbPath);
            if (bad != null)
            {
                GD.PrintErr("replay: " + bad);
                return 2;
            }
            s.Pb.SetSpeed(s.E, speed);
            s.Pb.TogglePlay(s.E);            // the Play button
            long ticks = 0;
            while (s.Now == Session.State.Playing && ticks < maxTicks)
            {
                if (!s.Step()) break;
                ticks++;
                // Single Step pauses itself after each key (see
                // Playback.AfterTick), so press Play again -- which is what a
                // human holding the button down would be doing.
                if (speed == PbSpeed.Step && !s.E.PlayBack) s.Pb.TogglePlay(s.E);
                if (s.E.Game.RecP >= (uint)s.E.RB_TOS && s.E.Quiescent()
                    && s.E.Game_On) break;
            }
            GD.PrintRaw(string.Format(
                "replay {0,-10} level={1,-5} ticks={2,-6} moves={3,-4} shots={4,-4} "
                + "keys={5}/{6} speed={7}  {8}\n",
                Result(s), s.Level, s.Ticks, s.E.Game.ScoreMove, s.E.Game.ScoreShot,
                s.E.Game.RecP, s.Pb.Rec.DataSize, (int)speed, s.Rec.LName));
            return Result(s) == "WIN" ? 0 : 1;
        }

        private static string Result(Session s) => s.Now switch
        {
            Session.State.Won => "WIN",
            Session.State.Dead => "DEAD",
            _ => "UNFINISHED",
        };

        private static int Report(Session s, int offered, string outDir, string author,
                                  string stem, string tag)
        {
            string result = Result(s);
            string saved = outDir != null ? s.Save(outDir, author, stem) : "";
            GD.PrintRaw(string.Format(
                "{0} {1,-10} level={2,-5} ticks={3,-6} moves={4,-4} shots={5,-4} "
                + "keys={6}/{7} out={8}  {9}\n",
                tag, result, s.Level, s.Ticks, s.E.Game.ScoreMove, s.E.Game.ScoreShot,
                s.E.Game.RecP, offered,
                saved == "" ? "-" : Path.GetFileName(saved), s.Rec.LName));
            return result == "WIN" ? 0 : 1;
        }

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
