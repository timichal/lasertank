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
"usage: lasertank-core --levels FILE.lvl (--lpb FILE.lpb | --level N --keys STR)\n" +
"                      [--trace FILE] [--field] [--bmf] [--max-ticks N] [--quiet]\n" +
"\n" +
"  --lpb FILE     replay a recorded solution; level number comes from its header\n" +
"  --level N      1-based level number (with --keys)\n" +
"  --keys STR     keystream as characters: u d l r f\n" +
"  --field        include full PF / PF2 hex in the trace\n" +
"  --bmf          include BMF / BMF2 (cosmetic: nothing in the logic reads them)\n");
        }

        public static int Main(string[] argv)
        {
            string levels = null, lpb = null, keys = null, tracePath = null;
            int level = 0;
            bool quiet = false, field = false, bmf = false;
            long maxTicks = 200000, tick = 0;

            for (int i = 0; i < argv.Length; i++)
            {
                switch (argv[i])
                {
                    case "--levels" when i + 1 < argv.Length: levels = argv[++i]; break;
                    case "--lpb" when i + 1 < argv.Length: lpb = argv[++i]; break;
                    case "--keys" when i + 1 < argv.Length: keys = argv[++i]; break;
                    case "--trace" when i + 1 < argv.Length: tracePath = argv[++i]; break;
                    case "--level" when i + 1 < argv.Length: level = int.Parse(argv[++i]); break;
                    case "--max-ticks" when i + 1 < argv.Length: maxTicks = long.Parse(argv[++i]); break;
                    case "--field": field = true; break;
                    case "--bmf": bmf = true; break;
                    case "--quiet": quiet = true; break;
                    default: Usage(); return 2;
                }
            }
            if (levels == null || (lpb == null && keys == null)) { Usage(); return 2; }
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

            // LoadLevel resets RecP/RB_TOS, so install the keystream after it.
            e.RecBuffer = keystream;
            e.RB_TOS = keystream.Length;
            e.Game.RecP = 0;

            TraceWriter tr = null;
            if (tracePath != null)
            {
                try { tr = new TraceWriter(tracePath, field, bmf); }
                catch (IOException ex)
                {
                    Console.Error.WriteLine("lasertank-core: " + ex.Message);
                    return 2;
                }
                tr.Header(levels, level, e.CurRecData.LName, e.CurRecData.Author, e.RB_TOS);
            }

            // ---- run ----
            tr?.Tick(0, e);
            while (e.Game_On && e.Deaths == 0 && tick < maxTicks)
            {
                tick++;
                e.Tick();
                e.Pump();                    // dispatch anything posted this tick
                tr?.Tick(tick, e);
                // Out of keys and the world has settled: nothing further can happen.
                if (e.Game.RecP >= (uint)e.RB_TOS && e.Quiescent() && e.Game_On) break;
            }

            bool won = e.Deaths == 0 && e.Game.PF[e.Game.Tank.X, e.Game.Tank.Y] == 2;
            string result = won ? "WIN" : (e.Deaths != 0 ? "DEAD" : "UNFINISHED");

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
            return won ? 0 : 1;
        }

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
