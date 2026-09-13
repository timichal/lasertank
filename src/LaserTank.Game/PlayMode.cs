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

        // ---- the DeadBox's modality, as a criterion -------------------------
        /// `-- --check-deadbox --levels FILE.lvl --level N`
        ///
        /// **What this measures, and why the differential gates cannot.**  The
        /// claim behind `Session.AcceptsInput` is that a keystroke or a click
        /// arriving while the board is not the player's to drive changes nothing
        /// at all, because in the original it never reaches the board's window
        /// proc.  All three script drivers now apply that rule, which is what
        /// keeps a `--script` run comparable across them -- and is exactly why
        /// a trace diff between them cannot see this: they would agree just as
        /// well with the rule left out of all three.  So the criterion has to
        /// be a *differential inside the game*: run the same scenario twice,
        /// once with a burst of input while the box is up and once without, and
        /// require the two transcripts to be identical.
        ///
        /// Three resumes, because the port has three and they do not all hide
        /// an ungated key:
        ///
        ///   * `UndoDead` -- the DeadBox's own "Undo Last Move" (command 110
        ///     then GameOn(TRUE), LTANK.C:727).  This one is the **control**,
        ///     and it is why the report needed measuring rather than believing:
        ///     `UndoStep` clears both queues on its own (`RB_TOS = Game.RecP`,
        ///     "clear all keys not processed", and `MB_TOS = MB_SP = 0`), so
        ///     with the guard left out the resumed play is *identical* -- only
        ///     the `knocked` line, which is the queue itself, moves.  The way
        ///     out of the DeadBox that the report described is the one way that
        ///     cannot show the bug.
        ///   * `EditorResume` -- command 604's `if (CurLevel > 0) GameOn(TRUE)`
        ///     (LTANK.C:1263), reached by opening and leaving the editor.  No
        ///     UndoStep anywhere on that path, so the key survives it.
        ///   * `Replay` -- command 124 (LTANK.C:1047), which rewinds `RecP` and
        ///     **keeps `RB_TOS`** on purpose.  Keys pressed while a box was up
        ///     are then part of the keystream that replays -- which is the win
        ///     case as well as the death: idle arrows pressed at a finished
        ///     board replay as part of the solution.
        ///
        /// Both of those are unreachable in the 2010 binary for one reason: the
        /// box is modal, so the menu those commands live on cannot be opened
        /// while it is up.  Reported as "level 39 moves one cell after it dies"
        /// -- of which the visible move is quirk #8 and faithful, and this is
        /// the part that was not.
        public static int CheckDeadBox(string lvlPath, int level, string route)
        {
            int bad = 0;
            foreach (string how in new[] { "undodead", "editorresume", "replay" })
            {
                string quiet = Scenario(lvlPath, level, route, how, false);
                string noisy = Scenario(lvlPath, level, route, how, true);
                bool ok = quiet == noisy;
                if (!ok) bad++;
                // A passing run is 40 identical ticks twice over, so it prints
                // the two lines that carry the claim -- the state the box came
                // up in, and the queue right after the knock -- plus a hash of
                // the whole transcript.  A failing one prints both in full,
                // because then the tick it parts company on is the finding.
                GD.PrintRaw("deadbox " + how + (ok ? " OK" : " DIFFERS")
                            + " " + Hash(quiet) + NL);
                if (ok) Dump(how, Head(quiet));
                else { Dump("quiet " + how, quiet); Dump("noisy " + how, noisy); }
            }
            GD.PrintRaw((bad == 0 ? "deadbox OK" : "deadbox FAILED " + bad + "/3") + NL);
            return bad == 0 ? 0 : 1;
        }

        /// The transcript's line separator, as a char rather than an escape so
        /// that Split and PrintRaw cannot disagree about it.
        private const char NL = (char)10;

        private static string Hash(string transcript)
        {
            byte[] h = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(transcript));
            return Convert.ToHexString(h).ToLowerInvariant().Substring(0, 16);
        }

        /// The `box` and `knocked` lines, plus the first tick after the resume.
        private static string Head(string transcript)
        {
            string[] lines = transcript.Split(NL);
            int keep = Math.Min(4, lines.Length);
            return string.Join(NL, lines, 0, keep);
        }

        private static void Dump(string tag, string transcript)
        {
            foreach (string line in transcript.Split(NL))
                if (line.Length != 0) GD.PrintRaw("  " + tag + " " + line + NL);
        }

        /// One run: play `route` until a box is up, optionally knock on the
        /// dialog, resume, and transcribe what the board does next.
        ///
        /// The knock is deliberately more than the one key the report was
        /// about.  A *held* arrow can only get one byte in -- LTANK.C:574 drops
        /// auto-repeat while `RB_TOS > RecP` -- but a player pressing distinct
        /// keys is not auto-repeating, and neither is a player clicking, so on
        /// an ungated box the bound is the player's patience.  Five keys and two
        /// clicks is that, and small enough to read in the transcript.
        private static string Scenario(string lvlPath, int level, string route,
                                       string how, bool knock)
        {
            var s = new Session(lvlPath);          // no Options: posts no score
            if (!s.Load(level)) return "load failed: " + s.Error;
            var log = new System.Text.StringBuilder();

            // The human, pressing on drain, until a box comes up.
            byte[] keys = ParseKeys(route);
            int at = 0;
            while (s.Now == Session.State.Playing && s.Ticks < 400)
            {
                while (at < keys.Length && s.Pending < 1) s.Key(keys[at++], echo: false);
                if (!s.Step()) break;
                if (at >= keys.Length && s.Pending == 0 && s.E.Quiescent()
                    && s.E.Game_On) break;
            }
            log.Append("box now=" + s.Now + " ticks=" + s.Ticks
                       + " tank=" + s.E.Game.Tank.X + "," + s.E.Game.Tank.Y
                       + " dir=" + s.E.Game.Tank.Dir
                       + " recp=" + s.E.Game.RecP + " rbtos=" + s.E.RB_TOS
                       + " moves=" + s.E.Game.ScoreMove + NL);

            if (knock)
            {
                // Every one of these is refused, so the line below is itself the
                // assertion: RB_TOS and the mouse ring must not have moved.
                foreach (byte vk in new[] { Engine.VK_RIGHT, Engine.VK_RIGHT,
                                            Engine.VK_UP, Engine.VK_SPACE,
                                            Engine.VK_LEFT })
                    s.Key(vk, echo: false);
                s.Click(0, 0, 1);
                s.Click(8, 8, 2);
            }
            log.Append("knocked pending=" + s.Pending + " rbtos=" + s.E.RB_TOS
                       + " mb=" + s.E.MB_TOS + "," + s.E.MB_SP + NL);

            switch (how)
            {
                case "undodead":
                    log.Append("resume undodead=" + s.UndoDead() + NL);
                    break;
                case "editorresume":
                    s.EditorResume();
                    log.Append("resume editor" + NL);
                    break;
                case "replay":
                    s.Replay();
                    log.Append("resume replay" + NL);
                    break;
            }

            // No further input at all: whatever the board does from here was
            // either in the keystream or was never pressed.
            for (int t = 0; t < 40 && s.Now == Session.State.Playing; t++)
            {
                if (!s.Step()) break;
                log.Append("t" + t + " tank=" + s.E.Game.Tank.X + "," + s.E.Game.Tank.Y
                           + " dir=" + s.E.Game.Tank.Dir + " recp=" + s.E.Game.RecP
                           + " rbtos=" + s.E.RB_TOS + " moves=" + s.E.Game.ScoreMove
                           + " shots=" + s.E.Game.ScoreShot + " now=" + s.Now + NL);
            }
            return log.ToString();
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
                // The DeadBox has the keyboard, so the token is spent and
                // presses nothing -- driver.c's `script_box_up` and the CLI's
                // `BoxUp`, applied here so that the three drivers consume a
                // script token for token.  Session.Key refuses the press as
                // well; this is the *accounting*, and the refusal is
                // Session.AcceptsInput's own.
                if (!s.AcceptsInput) { at++; return; }
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
