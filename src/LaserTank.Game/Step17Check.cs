// Phase 5, step 17's headless self-check: the level history (command 118).
//
// **The same shape as step 15's, and for the same reason.**  What the key does
// is not a thing a screenshot can settle -- it is a *sequence of level
// numbers*, and the stack behind it is state no frame draws.  So it is printed
// instead, one line per operation:
//
//   `--check-history` runs a script of navigation commands from the level
//   `--level` names and prints, after each, whether it worked, the level on
//   screen, the level Backspace would go to next, and the whole stack.  The
//   script is the ops the router can reach:
//
//     123   Load(123)        -- the level list, and `[` / `]`
//     +     Advance(+1)      -- command 107, filtered
//     -     Advance(-1)      -- command 119, filtered
//     <     Back()           -- command 118, the thing under test
//     r     Restart()        -- command 105, which is *not* a load
//     o     OpenDataFile     -- command 108, on the file `--history-open` names
//
//   Commas separate them, which is why the second collection is a flag rather
//   than an argument to `o`: a path in the script would be a path with a comma
//   in it one day, and the check needs exactly one other file.
//
// It is an instrument, so it writes nothing: the settings store is forced
// read-only for the duration, which stops the remembered level following the
// walk and stops the Session posting a score it did not earn.
//
// The output is the numbers and not a verdict, for the reason `--check-lists`
// established: tools/options_check.py rebuilds the expected stack in Python
// from the same `.lvl` and `.hs` bytes, so the two sides are two readers of one
// file rather than the game agreeing with itself.
using System;
using System.Globalization;
using Godot;

namespace LaserTank.Game
{
    public static class Step17Check
    {
        /// -> 0, or 2 when the collection will not open or the script has a
        /// token in it that is not an op.
        public static int CheckHistory(string lvlPath, int from, string script,
                                       string openPath, Options opt)
        {
            var inv = CultureInfo.InvariantCulture;
            bool wasReadOnly = opt.ReadOnly;
            opt.ReadOnly = true;
            try
            {
                var s = new Session(lvlPath, opt);
                if (!s.Load(from))
                {
                    GD.PrintErr(s.Error ?? ("cannot load " + lvlPath));
                    return 2;
                }
                GD.PrintRaw(string.Format(inv,
                    "history file={0} levels={1} start={2} stack={3}\n",
                    System.IO.Path.GetFileName(lvlPath), s.LevelCount, s.Level,
                    Stack(s)));

                foreach (string tok in script.Split(',',
                             StringSplitOptions.RemoveEmptyEntries))
                {
                    string op = tok.Trim();
                    bool ok;
                    switch (op)
                    {
                        case "+": ok = s.Advance(+1, out _); break;
                        case "-": ok = s.Advance(-1, out _); break;
                        case "<": ok = s.Back(); break;
                        // Restart is here to be *seen* doing nothing to the
                        // stack: it is the one command that puts the level back
                        // the way it was without loading it, and a history that
                        // grew an entry per restart would look right until
                        // somebody pressed R twice.
                        case "r": s.Restart(); ok = true; break;
                        case "o":
                            if (openPath == null)
                            {
                                GD.PrintErr("--check-history: o wants --history-open");
                                return 2;
                            }
                            ok = s.OpenDataFile(openPath);
                            break;
                        default:
                            if (!int.TryParse(op, NumberStyles.Integer, inv,
                                              out int n))
                            {
                                GD.PrintErr("--check-history: not an op: " + op);
                                return 2;
                            }
                            ok = s.Load(n);
                            break;
                    }
                    // `file` is on every line and not just the header because
                    // `o` is on the list: command 108 changes the collection
                    // *and* clears the stack, and a gate that could only see
                    // the second half would pass a 108 that cleared the history
                    // without opening anything.
                    GD.PrintRaw(string.Format(inv,
                        "history op={0} ok={1} file={2} level={3} back={4} stack={5}\n",
                        op, ok ? 1 : 0,
                        System.IO.Path.GetFileName(s.LevelPath),
                        s.Level, s.BackLevel, Stack(s)));
                }
                GD.PrintRaw("history end\n");
                return 0;
            }
            finally { opt.ReadOnly = wasReadOnly; }
        }

        /// The stack as one field: oldest first, the level on screen last, and
        /// `-` for the empty one -- which is a state only a Session nobody has
        /// loaded a level into is in, and so should never be printed.
        private static string Stack(Session s)
            => s.History.Count == 0
               ? "-"
               : string.Join(";", s.History);
    }
}
