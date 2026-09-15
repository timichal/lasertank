// Phase 5, step 15's headless self-check: the filtered walk.
//
// **The one thing item 3 added that a screenshot cannot settle.**  The panel is
// two rows of chips and `--shot --panel options` reviews it; what the chips
// change is where `S`, `P` and a win take you, and that is a *sequence of level
// numbers* -- invisible in any single frame, and reachable from the keyboard
// only through `--press`, which needs a window.  So it is printed instead:
//
//   `--check-advance` walks the collection the way command 107 does, from the
//   level `--level` names, and prints one line per stop until the walk runs
//   out.  `--advance-dir -1` walks it the way 119 does.  The mask and the skip
//   come from the same place the game reads them -- `Options`, which means
//   `--difficulty` and `--skip-completed` steer it, and so does a real store.
//
// It is an instrument, so it writes nothing: the settings store is forced
// read-only for the duration, which stops the remembered level following the
// walk and stops the Session posting a score it did not earn.  The test the
// rest of the gates are held to applies here too -- run it eight times in
// parallel and the tree is as it was.
//
// The output is deliberately the *numbers* and not a verdict.  What
// tools/options_check.py asserts against it is built in Python out of the same
// `.lvl` and `.hs` bytes, so the two sides are two readers of one file rather
// than the game agreeing with itself -- which is the rule `--check-lists` was
// written to and the reason this prints a sequence rather than "ok".
using System;
using System.Globalization;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    public static class Step15Check
    {
        /// -> 0, or 2 when the collection will not open.
        ///
        /// `steps` caps the walk so a corpus-sized collection cannot print
        /// 2,030 lines into a gate's pipe by accident; the default is the whole
        /// of any collection in `data/`.
        public static int CheckAdvance(string lvlPath, int from, int dir,
                                       int steps, Options opt)
        {
            var inv = CultureInfo.InvariantCulture;
            bool wasReadOnly = opt.ReadOnly;
            // **An instrument must not write the player's state**, and a walk
            // is the one check here that would: every stop is a Load, and Load
            // remembers the level it landed on when [OPT] RLL is on.
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
                    "advance file={0} levels={1} from={2} dir={3} " +
                    "difficulty={4} skip_completed={5}\n",
                    lvlPath, s.LevelCount, s.Level, dir,
                    opt.Difficulty, opt.SkipCompleted ? "Yes" : "No"));

                for (int i = 0; i < steps; i++)
                {
                    if (!s.Advance(dir, out bool filtered))
                    {
                        // The two endings, named apart for the same reason the
                        // status line names them apart: "the collection ends
                        // here" and "your settings left nothing" are different
                        // facts, and only one of them is about the file.
                        GD.PrintRaw(string.Format(inv, "advance end={0}\n",
                                                  filtered ? "filtered" : "eof"));
                        return 0;
                    }
                    // The rank digit with the number, because it is what the
                    // mask was matched against and a gate that had to re-derive
                    // it from the .lvl would be re-deriving the thing under
                    // test.  `sdiff` is the raw word; 0 is the unfilterable
                    // case and prints as itself.
                    GD.PrintRaw(string.Format(inv, "advance stop={0} sdiff={1}\n",
                                              s.Level, s.Rec == null ? 0 : s.Rec.SDiff));
                }
                GD.PrintRaw("advance end=steps\n");
                return 0;
            }
            finally { opt.ReadOnly = wasReadOnly; }
        }
    }
}
