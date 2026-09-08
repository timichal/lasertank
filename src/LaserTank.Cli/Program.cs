// Headless driver for the C# core.  Takes the oracle's command line and emits
// the oracle's trace, so the two engines can be diffed directly:
//
//   oracle/build/oracle.exe --levels L.lvl --lpb R.lpb --trace a --field
//   lasertank-core.exe      --levels L.lvl --lpb R.lpb --trace b --field
//   python tools/difftrace.py a b
//
// The run loop is a transliteration of the same loop in oracle/driver.c's
// main(), including where the message pump is drained relative to the trace.
using System;
using System.IO;
using LaserTank.Core;

namespace LaserTank.Cli
{
    public static class Program
    {
        private static void Usage()
        {
            Console.Error.WriteLine(
"usage: lasertank-core --levels FILE.lvl (--lpb FILE.lpb | --level N (--keys|--script) STR)\n" +
"                      [--trace FILE] [--field] [--bmf] [--sound] [--max-ticks N]\n" +
"                      [--quiet]\n" +
"\n" +
"  --lpb FILE     replay a recorded solution; level number comes from its header\n" +
"  --level N      1-based level number (with --keys / --script)\n" +
"  --keys STR     keystream as characters: u d l r f\n" +
"  --script STR   one token per tick: u d l r f press, . idles, z undoes,\n" +
"                 Z undoes a death and resumes, c/v save/restore position,\n" +
"                 mXY / nXY left/right click cell XY (two hex digits)\n" +
"  --field        include full PF / PF2 hex in the trace\n" +
"  --bmf          include BMF / BMF2 (cosmetic: nothing in the logic reads them)\n" +
"  --sound        include SF, the SoundPlay ids the tick asked for\n" +
"  --edit STR     level editor, one token per traced step; no tick runs:\n" +
"                 <oo />oo pick the left/right object, tN the tunnel id,\n" +
"                 lXY rXY click, sXY Shift+click (rotate), pXY qXY drag,\n" +
"                 PXY Shift+drag, R L U D shift the board, C clear, E enter,\n" +
"                 1..5 set the difficulty (Kids/Easy/Medium/Hard/Deadly)\n" +
"  --save FILE    with --edit: write the edited level as a .lvl record\n" +
"  --save-level N  ... at this 1-based level number (default: --level)\n" +
"  --set-name S   type S into the editor's Level Name box before saving\n" +
"  --set-author S  ... and S into the Author box\n" +
"  --set-hint S   ... and S into the Hint dialog (only then is Hint rewritten)\n");
        }

        public static int Main(string[] argv)
        {
            string levels = null, lpb = null, keys = null, script = null,
                   edit = null, tracePath = null, save = null,
                   setName = null, setAuthor = null, setHint = null;
            int saveLevel = 0;
            int level = 0;
            bool quiet = false, field = false, bmf = false, sound = false;
            long maxTicks = 200000, tick = 0;

            for (int i = 0; i < argv.Length; i++)
            {
                switch (argv[i])
                {
                    case "--levels" when i + 1 < argv.Length: levels = argv[++i]; break;
                    case "--lpb" when i + 1 < argv.Length: lpb = argv[++i]; break;
                    case "--keys" when i + 1 < argv.Length: keys = argv[++i]; break;
                    case "--script" when i + 1 < argv.Length: script = argv[++i]; break;
                    case "--edit" when i + 1 < argv.Length: edit = argv[++i]; break;
                    case "--save" when i + 1 < argv.Length: save = argv[++i]; break;
                    case "--save-level" when i + 1 < argv.Length:
                        saveLevel = int.Parse(argv[++i]); break;
                    case "--set-name" when i + 1 < argv.Length: setName = argv[++i]; break;
                    case "--set-author" when i + 1 < argv.Length: setAuthor = argv[++i]; break;
                    case "--set-hint" when i + 1 < argv.Length: setHint = argv[++i]; break;
                    case "--trace" when i + 1 < argv.Length: tracePath = argv[++i]; break;
                    case "--level" when i + 1 < argv.Length: level = int.Parse(argv[++i]); break;
                    case "--max-ticks" when i + 1 < argv.Length: maxTicks = long.Parse(argv[++i]); break;
                    case "--field": field = true; break;
                    case "--bmf": bmf = true; break;
                    case "--sound": sound = true; break;
                    case "--quiet": quiet = true; break;
                    default: Usage(); return 2;
                }
            }
            if (levels == null
                || (lpb == null && keys == null && script == null && edit == null))
            {
                Usage();
                return 2;
            }
            if (script != null && (lpb != null || keys != null))
            {
                Console.Error.WriteLine(
                    "lasertank-core: --script cannot be combined with --lpb or --keys");
                return 2;
            }
            if (edit != null && (lpb != null || keys != null || script != null))
            {
                Console.Error.WriteLine(
                    "lasertank-core: --edit is a mode of its own; it runs no ticks");
                return 2;
            }
            if (edit == null && (save != null || setName != null
                                 || setAuthor != null || setHint != null))
            {
                Console.Error.WriteLine(
                    "lasertank-core: --save and the --set-* flags belong to --edit");
                return 2;
            }
            if (!File.Exists(levels))
            {
                Console.Error.WriteLine("lasertank-core: cannot open " + levels);
                return 2;
            }

            Engine e = new Engine();
            // Replay configuration, matching oracle_init(): exactly the state
            // the real program is in while playing back a .lpb.
            e.Ani_On = true;
            e.PBOpen = true;
            e.PlayBack = true;
            e.Speed = 1;
            e.SlowPB = 1;

            TRECORDREC rec = null;
            byte[] keystream;
            if (lpb != null)
            {
                try { rec = LevelFile.ReadPlayback(lpb, out keystream); }
                catch (IOException ex) { Console.Error.WriteLine("lasertank-core: " + ex.Message); return 2; }
                level = rec.Level;
            }
            else if (script != null)
            {
                // A script presses one key at a time, so the buffer starts
                // empty and grows.  10000 is InitBuffers' RecBufSize
                // (LTANK2.C:51); the script driver refuses to overrun it rather
                // than reallocating, because no script this is used for is that
                // long and a silent grow would hide a runaway.
                keystream = new byte[10000];
            }
            else if (edit != null)
            {
                keystream = new byte[10000];   // unused: the editor presses nothing
            }
            else
            {
                keystream = ParseKeys(keys);
            }

            if (!e.LoadLevel(levels, level))
            {
                Console.Error.WriteLine(
                    $"lasertank-core: failed to load level {level} from {levels}");
                return 2;
            }

            if (rec != null && e.CurRecData.LName != rec.LName)
            {
                Console.Error.WriteLine(
                    $"lasertank-core: level name mismatch: lpb says \"{rec.LName}\", " +
                    $"lvl {level} is \"{e.CurRecData.LName}\"");
                return 3;
            }

            // Phase 5, step 3: opt in to the sound sink.  Null means nobody is
            // listening and SoundPlay does nothing at all, which is what every
            // run without --sound (and the whole solver) gets.
            if (sound) e.SoundLog = new System.Collections.Generic.List<int>();

            // LoadLevel resets RecP/RB_TOS, so install the keystream after it.
            e.RecBuffer = keystream;
            e.RB_TOS = (script != null || edit != null) ? 0 : keystream.Length;
            e.Game.RecP = 0;

            TraceWriter tr = null;
            if (tracePath != null)
            {
                try { tr = new TraceWriter(tracePath, field, bmf, sound); }
                catch (IOException ex)
                {
                    Console.Error.WriteLine("lasertank-core: " + ex.Message);
                    return 2;
                }
                // `keys` is how much input this run was given -- the script's
                // token count when there is one, since RB_TOS is still 0.
                tr.Header(levels, level, e.CurRecData.LName, e.CurRecData.Author,
                          script?.Length ?? edit?.Length ?? e.RB_TOS);
            }

            // ---- run ----
            if (edit != null)
            {
                // The editor is not the game: GameOn(FALSE) is the first thing
                // command 201 does (LTANK.C:1086), so nothing here ticks and
                // the trace's `t=` counts *edits*.  Same format, same
                // difftrace, new input language -- which is the whole trick.
                var ed = new EditDriver(e, tr, LevelRecord.Read(levels, level));
                tick = ed.Run(edit);
                // The two edit controls and the hint dialog, in the order the
                // player would reach them: typed while the editor is open,
                // read back by command 603 at save time.
                if (setName != null) ed.Name = setName;
                if (setAuthor != null) ed.Author = setAuthor;
                if (setHint != null) ed.Hint = setHint;
                if (save != null) ed.Save(save, saveLevel != 0 ? saveLevel : level);
                tr?.Footer("EDIT", tick, e.Game.ScoreMove, e.Game.ScoreShot,
                           e.Game.RecP, e.RB_TOS, ed.Dialogs);
                tr?.Close();
                if (!quiet)
                    Console.WriteLine("{0,-10} level={1,-5} edits={2,-6} tank={3},{4}  {5}",
                                      "EDIT", level, tick, e.Game.Tank.X, e.Game.Tank.Y,
                                      e.CurRecData.LName);
                return 0;
            }

            string notPorted = null;
            tr?.Tick(0, e);
            try
            {
                if (script != null)
                {
                    // The script loop, transliterated from driver.c's.  Three
                    // differences from the keystream loop below and no others:
                    // a token is fed before each tick, a dead or finished game
                    // still consumes tokens (so `Z` can resume it) but takes no
                    // tick, and "nothing further can happen" also requires the
                    // script to be spent.
                    int at = 0;
                    while (tick < maxTicks)
                    {
                        int before = at;
                        Feed(e, script, ref at);
                        if (e.Game_On && e.Deaths == 0)
                        {
                            tick++;
                            e.SoundLog?.Clear();
                            e.Tick();
                            e.Pump();
                            tr?.Tick(tick, e);
                        }
                        else if (at == before)
                        {
                            // No tick to advance the clock and no token
                            // consumed: a key waiting on a buffer a dead game
                            // will never drain.  maxTicks cannot stop a loop
                            // that does not tick, so stop here.
                            break;
                        }
                        // ... and the mouse buffer counts as "keys left": a
                        // click is drained by the *next* tick, so a script
                        // ending in one would otherwise stop before
                        // MouseOperation ever ran.
                        if (at >= script.Length && e.Game.RecP >= (uint)e.RB_TOS
                            && e.MB_TOS == e.MB_SP
                            && e.Quiescent() && e.Game_On) break;
                    }
                }
                else
                while (e.Game_On && e.Deaths == 0 && tick < maxTicks)
                {
                    tick++;
                    e.SoundLog?.Clear();         // SF is per tick, driver.c:sf_n = 0
                    e.Tick();
                    e.Pump();                    // dispatch anything posted this tick
                    tr?.Tick(tick, e);
                    // Out of keys and the world has settled: nothing further can happen.
                    if (e.Game.RecP >= (uint)e.RB_TOS && e.Quiescent() && e.Game_On) break;
                }
            }
            catch (NotPortedException ex)
            {
                // Stop, but flush the ticks that did run.  Porting one function
                // at a time is only checkable if the prefix survives: difftrace
                // then reports "identical for N ticks, then B stops", which is a
                // real regression gate on the already-ported half and pins the
                // tick where the port has to continue.  Nothing here softens the
                // failure -- the trace ends at the last *complete* tick, the
                // footer says NOTPORTED rather than a plausible outcome, stderr
                // names the function and the tick, and the exit code is its own.
                notPorted = ex.Message;
                Console.Error.WriteLine(
                    $"lasertank-core: tick {tick}: {ex.Message}");
            }

            bool won = notPorted == null && e.Deaths == 0
                       && e.Game.PF[e.Game.Tank.X, e.Game.Tank.Y] == 2;
            string result = notPorted != null ? "NOTPORTED"
                          : won ? "WIN" : (e.Deaths != 0 ? "DEAD" : "UNFINISHED");

            if (tr != null)
            {
                tr.Footer(result, tick, e.Game.ScoreMove, e.Game.ScoreShot,
                          e.Game.RecP, e.RB_TOS);
                tr.Close();
            }
            if (!quiet)
            {
                // Same shape as the oracle's, so tools/replay_all.py parses both.
                Console.WriteLine("{0,-10} level={1,-5} ticks={2,-6} moves={3,-4} shots={4,-4} "
                                  + "keys={5}/{6}  {7}",
                                  result, level, tick, e.Game.ScoreMove, e.Game.ScoreShot,
                                  e.Game.RecP, e.RB_TOS, e.CurRecData.LName);
            }
            if (notPorted != null) return 4;
            return won ? 0 : 1;
        }

        /// One script token, matching driver.c's `script_feed` (Phase 5, step 4).
        ///
        /// The tokens are the five game keys, `.` for an idle tick, the four
        /// commands no keystream can express -- `z` = 110 Undo, `Z` = the
        /// DeadBox's Undo (110 then GameOn(TRUE), the only path that resumes a
        /// dead game), `c` = 111 Save Position, `v` = 112 Restore Position --
        /// and, since step 5, the two mouse clicks: `mXY` left, `nXY` right,
        /// where XY is a cell as two hex digits.  A click is a window message
        /// rather than a keystroke, so it is posted whatever the key buffer is
        /// doing; the tick drains one per tick when the world is quiescent, and
        /// MouseOperation turns it into keys.
        ///
        /// A key is pressed only when the buffer has drained, which is the
        /// original's own pending-key rule (LTANK.C:573) and the reason one key
        /// at a time traces identically to a preloaded keystream: the tick takes
        /// a key only when the world is quiescent anyway.
        private static void Feed(Engine e, string script, ref int at)
        {
            if (at >= script.Length) return;
            char c = script[at];
            if (c == 'm') { Click(e, script, ref at, 1); return; }
            if (c == 'n') { Click(e, script, ref at, 2); return; }
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
                if (e.RB_TOS != (int)e.Game.RecP) return;        // still pending: wait
                if (e.RB_TOS >= e.RecBuffer.Length)
                    throw new InvalidOperationException(
                        "script pressed more than " + e.RecBuffer.Length + " keys");
                e.RecBuffer[e.RB_TOS++] = vk;
                at++;
                return;
            }
            at++;
            switch (c)
            {
                case '.': break;                                  // idle one tick
                case 'z': e.UndoStep(); break;                    // command 110
                case 'Z':                                         // DeadBox Undo
                    e.UndoStep();
                    e.GameOn(true);
                    e.Deaths = 0;
                    break;
                case 'c': e.SavePosition(); break;                // command 111
                // command 112, behind the guard Windows applies to it:
                // EnableMenuItem(112) is off until Save Position has been used.
                // Not politeness -- SaveGame starts blank, and restoring a blank
                // record drops the tank at 0,0 on an empty board while whatever
                // ice slide was running keeps going, which walks ConvMoveTank
                // off the end of Game.PF.  See Engine.SaveGame and
                // oracle/driver.c's can_restore.
                case 'v': if (e.CanRestore) e.RestorePosition(); break;
                default: break;         // unknown: skipped, exactly as --keys does
            }
        }

        /// `mXY` / `nXY`, matching driver.c's script_click: two hex digits for
        /// the cell, and a truncated token at the end of the script consumes
        /// the rest and posts nothing.  Engine.MouseClick is the ring-buffer
        /// push out of the window proc's non-editor arm; the bounds test lives
        /// there because it is the original's.
        private static void Click(Engine e, string script, ref int at, int z)
        {
            if (at + 2 >= script.Length) { at = script.Length; return; }
            int x = Hex(script[at + 1]), y = Hex(script[at + 2]);
            at += 3;
            if (x < 0 || y < 0) return;
            e.MouseClick(x, y, z);
        }

        private static int Hex(char c) =>
            c >= '0' && c <= '9' ? c - '0'
            : c >= 'a' && c <= 'f' ? c - 'a' + 10
            : c >= 'A' && c <= 'F' ? c - 'A' + 10
            : -1;

        /// Characters to VK codes, matching driver.c: anything else is skipped.
        private static byte[] ParseKeys(string s)
        {
            var outv = new System.Collections.Generic.List<byte>(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case 'u': case 'U': outv.Add(Engine.VK_UP); break;
                    case 'd': case 'D': outv.Add(Engine.VK_DOWN); break;
                    case 'l': case 'L': outv.Add(Engine.VK_LEFT); break;
                    case 'r': case 'R': outv.Add(Engine.VK_RIGHT); break;
                    case 'f': case 'F': outv.Add(Engine.VK_SPACE); break;
                }
            }
            return outv.ToArray();
        }
    }
}
