// The interactive driver: one level at a time, in level order, for as long as
// you let it.
//
// The batch harness next door (Program.cs) answers a rate question -- how many
// of 4,185 levels fall at an equal budget -- and every knob it has exists
// because some campaign needed to vary it.  This answers the question you
// actually have in front of a single level: *solve this one*, spend whatever it
// takes, and let me press a key when I have had enough of it.  So the budget is
// not a number the caller picks, and the layer to use is not a decision the
// caller makes.
//
//   * **Every searcher runs at once, on its own thread.**  A round is layer 0's
//     beam (+ IDA* on the first round only, where it is a cheap probe), layer
//     3's subgoal beam, layer 4's learned ranking of it, and layer 1's macro
//     beam, side by side; the first win takes the round and the rest are
//     cancelled.  This is the portfolio the campaign could not afford: there,
//     every node a specialist spends is a node taken from the raw beam, which
//     is why tools/second_pass.sh exists at all.  Here a specialist spends a
//     *core*, and takes nothing from anybody.
//   * **If nobody wins, the node budget quadruples and the round repeats.**
//     Rounds also widen what only widening helps: the raw beam doubles its
//     width (a beam that stopped at beam-dead-end has nothing more to spend a
//     bigger budget on), and the subgoal beam gets more restarts.  Round 0 is
//     400k nodes, about a second; round 5 is 400M and about an hour.  There is
//     no last round.
//   * **Level order, not .ghs order.**  The campaign sorts by the record
//     because that makes its solved-vs-budget curve readable.  Somebody working
//     through a collection wants level 7 after level 6.
//   * **Nothing is kept that the two-engine gate has not passed.**  The solver
//     replaying its own solution proves only self-consistency -- it is the same
//     engine that produced it -- so the win goes to a scratch file, then to
//     tools/verify_solutions.py, which hands it to the frozen C oracle *and*
//     the C# core and requires that both win and that their traces agree tick
//     for tick.  Only then is it moved into the output directory.  A solution
//     that fails is deleted, said so loudly, and the search carries on.
//
// The gate is the python tool rather than a reimplementation of it in here on
// purpose: a second copy of the trust chain is a second thing to keep true.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LaserTank.Core;

namespace LaserTank.Solver
{
    internal static class Auto
    {
        // Layer 0's campaign budget, and about a second of work: small enough
        // that an easy level is solved before the status line has repainted.
        private const long BaseNodes = 400000;
        private const int BaseMs = 120000;      // per round, a backstop only
        private const int MaxMs = 3600000;

        /// One searcher, and how it changes as the rounds go by.
        ///
        /// `Tune` starts from the caller's options with every searcher off, so
        /// each entry says only what it turns on.  Nothing here is new tuning:
        /// the configurations are exactly the ones tools/second_pass.sh chains,
        /// in the order it chains them.
        private static readonly (string Name, Action<SolveOptions, int> Tune)[] Ladder =
        {
            // layer 0.  IDA* is a probe for shallow levels and its share is a
            // fifth of the budget, so it earns its place on round 0 and stops
            // earning it immediately after.  Width is what a dead-ended beam
            // needs, not nodes -- hence the doubling, capped where a round's
            // memory stops being reasonable.
            ("beam", static (o, r) =>
            {
                o.RunBeam = true;
                o.RunIda = r == 0;
                o.BeamWidth = (int)Math.Min((long)o.BeamWidth << r, 19200);
            }),
            // layer 2 + layer 3: the subgoal beam, restarting when it dies of
            // an empty frontier.  More restarts each round, because that is the
            // shape of its failure and the budget to pay for them is now there.
            ("subgoal", static (o, r) =>
            {
                o.RunSubgoal = true;
                o.SgRestarts += 6 * r;
            }),
            // layer 4: the same search ranked by the learned evaluation.  Worth
            // a thread of its own rather than replacing the one above -- it
            // solves levels layer 3 does not and loses three that layer 3 wins,
            // which is why second_pass.sh runs both passes too.
            ("learned", static (o, r) =>
            {
                o.RunSubgoal = true;
                o.SgLearned = true;
                o.SgRestarts += 6 * r;
            }),
            // layer 1: macro-actions.  Nothing else to run alongside it here,
            // so it goes first in its own thread rather than last in a shared
            // budget.
            ("macro", static (o, r) =>
            {
                o.RunMacro = true;
                o.MacroLast = false;
            }),
            // layer 5: push macros.  The rung that earns nothing on round 0 and
            // is here anyway, because what it is for only shows up later: one
            // expansion is a whole PF-preserving closure, some 4,500 ApplyKey
            // calls against layer 0's five per keypress, so round 0's 400k buys
            // it under a hundred board-change steps.  It starts paying at the
            // rounds where the others have already failed -- which is exactly
            // the level this ladder is for, and exactly why a *thread* rather
            // than a share is the right way to carry it.
            //
            // **What grows is restarts, not width**, and that is session 18's
            // correction rather than a preference.  The rung used to double the
            // width every round, which by round 5 had it running at 256 against
            // a default of 8 -- and the width sweep says 8 scores 19/50 on the
            // ferry bench where 300 scores 8/50.  Benched as the ladder
            // actually ran it, round 5 was **11/50 against the default's
            // 20/50**: the rung was getting weaker the longer it was left
            // alone, which is the opposite of what a ladder is for.  Restarts
            // are the layer's own dead-end control law and only ever spend
            // budget a dead-end had already forfeit, so growing them is free --
            // measured, 6 restarts and 36 both score 20/50 -- and the beam
            // widens itself on a dead-end anyway, which is the one place width
            // has been shown to pay.  The read goes on for the same reason it
            // is on in every bench above: 20/50 with it, 9/50 without.
            //
            // The fourth derivation is deliberately *not* here.  It costs this
            // configuration a level on the ferry bench and two on the deep one,
            // and a rung that has been tuned to its best measured setting is
            // the wrong place to spend that.  It gets a rung of its own below,
            // for the same reason `learned` is a rung rather than a change to
            // `subgoal`: it solves levels this does not, and loses levels this
            // wins, so the answer is a thread and not a preference.
            ("push", static (o, r) =>
            {
                o.RunPush = true;
                o.PushRead = true;
                o.PushRestarts += 6 * r;
            }),
            // layer 6's fourth derivation: *what does this change make
            // possible?*  The rung that exists because `LaserTank.lvl` 1 does.
            //
            // **Why a rung and not a flag on the one above.**  Session 19
            // measured it both ways: 19/50 on the ferry bench against that
            // rung's 20/50 and 19/50 on the deep bench against its 21/50, and
            // level 1 -- which four 800M-node runs of every other rung never
            // touched -- in 6.19M nodes.  A configuration that trades two
            // levels for a level nothing else reaches is a specialist, and the
            // driver's whole premise is that a specialist costs a *core* rather
            // than a share of somebody's budget.  Nobody running this should
            // have to know that a flag exists, let alone which level wants it.
            //
            // **The width is the one thing this rung changes with the round**,
            // and it is measured as a *union* with the rung above rather than
            // on its own -- which is the only honest way to pick a knob for a
            // portfolio member, and it gives a different answer from the
            // solo score.  All at 4M nodes, the plain rung's own solved set
            // against what each width adds to it:
            //
            //             solo   union with the plain rung   it adds
            //   ferry   w8  19            24                    4
            //           w48 14            21                    1
            //   deep    w8  19            26                    5
            //           w48 18            26                    5
            //
            // So the default width is the better partner and the wider one is
            // not redundant: all three configurations together are 24 and 28,
            // so w48 contributes two deep levels the other two never reach.
            // Early rounds take the width the bench prefers; from round 3 --
            // by which point the budget is 25.6M and everything either bench
            // solves at 4M has already had three chances at it -- the rung
            // widens, because that is the width `LaserTank.lvl` 1 wants: 6.19M
            // nodes and 18 s at 48, against 412 s at 8.  One rung, both widths,
            // and no core spent on the disagreement.
            ("push-enables", static (o, r) =>
            {
                o.RunPush = true;
                o.PushRead = true;
                o.PushEnables = 8;
                if (r >= 3) o.PushBeamWidth = 48;
                o.PushRestarts += 6 * r;
            }),
        };

        // What the keypress and Ctrl+C set.  Static because the Ctrl+C handler
        // has to reach the round that is running now.
        private static volatile bool _quit;
        private static volatile bool _skip;
        private static volatile CancelFlag[] _live;

        internal static int Run(Program.Args a)
        {
            string root = RepoRoot();
            string collection = Path.GetFileNameWithoutExtension(a.Levels);
            // data/solutions in the repo, deliberately, and not under build/.
            //
            // A campaign's output is disposable -- it is regenerated by
            // tools/campaign.sh and it is thousands of files -- so it lives in
            // gitignored build/.  The interactive driver's output is the
            // opposite kind of thing: one level at a time, each one already
            // through the two-engine gate, on levels the batch solver could not
            // do.  Those are worth committing, so they go where git can see
            // them -- next to the rest of the committed game content under
            // data/, alongside data/demos/ (the human playthroughs), which is
            // the same kind of thing: a recording that cannot be regenerated on
            // demand.  The scratch directory the verifier stages through stays
            // under build/, because that part really is disposable.
            string outRoot = a.OutGiven
                ? a.Out : Path.Combine(root, "data", "solutions");
            string outDir = Path.Combine(outRoot, collection);
            string work = Path.Combine(root, "build", "solutions", ".auto");
            string verifyDir = Path.Combine(work, collection);
            string ghsPath = Path.ChangeExtension(a.Levels, ".ghs");

            // Preflight the gate, not the search.  A run that solves six levels
            // and only then finds it cannot verify any of them has wasted the
            // one thing this tool spends freely.
            string gate = Path.Combine(root, "tools", "verify_solutions.py");
            string python = FindPython();
            foreach ((string path, string how) in new[]
            {
                (Path.Combine(root, "oracle", "build", "oracle.exe"), "bash oracle/build.sh"),
                (Path.Combine(root, "build", "lasertank-core.exe"), "bash src/build.sh"),
                (gate, null),
            })
            {
                if (File.Exists(path)) continue;
                Console.Error.WriteLine("cannot verify solutions: {0} is missing{1}",
                    path, how == null ? "" : "\nrun: " + how);
                return 2;
            }
            if (python == null)
            {
                Console.Error.WriteLine(
                    "cannot verify solutions: no python on PATH (set LT_PYTHON to one)");
                return 2;
            }

            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(verifyDir);

            int count = LevelFile.CountLevels(a.Levels);
            int from = Math.Max(1, a.From), to = (int)Math.Min(a.To, count);
            if (from > to)
            {
                Console.Error.WriteLine("{0} has {1} levels; --from {2}{3} selects none",
                                        collection, count, from,
                                        a.To == int.MaxValue ? "" : " --to " + a.To);
                return 2;
            }

            Console.CancelKeyPress += static (_, e) =>
            {
                e.Cancel = true;            // unwind and print, do not die here
                _quit = true;
                StopLive();
            };

            int jobs = Math.Max(1, Math.Min(a.Jobs, Ladder.Length));
            Console.WriteLine("{0}  levels {1}-{2} of {3}   {4} searchers per round"
                              + "{5}, budget x4 each round",
                              Ansi.Bold(collection), from, to, count, Ladder.Length,
                              jobs < Ladder.Length ? " (" + jobs + " at a time)" : "");
            Console.WriteLine(Ansi.Dim(Interactive
                ? "  any key gives up on the level and moves to the next; q quits\n"
                : "  stdin is not a console, so there is no key to press here: "
                  + "Ctrl+C is the way out\n"));

            DateTime t0 = DateTime.UtcNow;
            int solved = 0, skipped = 0, already = 0, rejected = 0;
            List<int> unsolved = new List<int>();

            for (int lv = from; lv <= to && !_quit; lv++)
            {
                TLEVEL info = LevelFile.ReadLevel(a.Levels, lv);
                if (info == null) break;

                string lpb = Path.Combine(outDir, string.Format("{0:D5}.lpb", lv));
                if (!a.Force && File.Exists(lpb))
                {
                    already++;
                    Console.WriteLine("{0}  {1}", Head(lv, count, info),
                                      Ansi.Dim("already solved -- --force re-solves it"));
                    continue;
                }

                LevelFile.ReadHighScore(ghsPath, lv, out ushort gm, out ushort gs);
                Console.WriteLine(Head(lv, count, info) + "  " + Ansi.Dim(Record(gm, gs)));

                _skip = false;
                bool won = false;
                DateTime lt0 = DateTime.UtcNow;
                for (int round = 0; !won && !_skip && !_quit; round++)
                {
                    Program.Outcome o = Round(a, info, lv, round, jobs, work, lt0);
                    if (o == null) continue;                       // nobody won

                    string why = Gate(python, gate, root, a.Levels, o.J.LpbPath,
                                      verifyDir, lpb);
                    if (why == null)
                    {
                        won = true;
                        solved++;
                        Console.WriteLine("  {0}  {1}",
                            Ansi.Green("SOLVED"), Detail(o, round, lt0));
                        Console.WriteLine("  {0}", Ansi.Dim(
                            "verified through both engines -> " + lpb));
                    }
                    else
                    {
                        // Not a near miss: the solver's own engine already
                        // replayed this and saw it win, so a gate failure is a
                        // divergence between the engines and wants shouting
                        // about.  Phase 3 says there are none left; this is how
                        // you would find out otherwise.
                        rejected++;
                        Console.WriteLine("  {0}  {1}", Ansi.Red("REJECTED"), why);
                        Console.WriteLine("  {0}", Ansi.Dim(
                            "discarded; searching on -- this is an engine divergence, "
                            + "please keep the level number"));
                    }
                }

                if (!won)
                {
                    unsolved.Add(lv);
                    if (_skip) skipped++;
                    Console.WriteLine("  {0}", Ansi.Yellow(
                        (_quit ? "stopped" : "skipped") + " after "
                        + Progress.Span((DateTime.UtcNow - lt0).TotalSeconds)));
                }
                Console.WriteLine();
            }

            Sweep(work);
            Console.WriteLine("{0} solved, {1} skipped{2}{3}   in {4}",
                solved, skipped,
                already > 0 ? ", " + already + " already had a solution" : "",
                rejected > 0 ? ", " + Ansi.Red(rejected + " REJECTED by the gate") : "",
                Progress.Span((DateTime.UtcNow - t0).TotalSeconds));
            if (unsolved.Count > 0)
                Console.WriteLine(Ansi.Dim("  still unsolved: "
                                           + string.Join(",", unsolved)));
            return 0;
        }

        // ---- one round -----------------------------------------------------

        /// Every searcher on the level at once; the first win, or null.
        ///
        /// Each rung writes to its own scratch .lpb, because two of them
        /// finishing together would otherwise race on one path.  The winner's
        /// file is the one the gate is handed; the rest are deleted here.
        private static Program.Outcome Round(Program.Args a, TLEVEL info, int lv,
                                             int round, int jobs, string work,
                                             DateTime lt0)
        {
            // Quadrupling has to stop being taken literally at some point or
            // the shift overflows.  Round 12 is 400k x 4^12, which is a search
            // no machine will finish; past it the rounds still differ, because
            // the searchers themselves keep widening.
            int r = Math.Min(round, 12);
            long nodes = (a.NodesGiven ? a.Opt.NodeBudget : BaseNodes) << (2 * r);
            int ms = (int)Math.Min((long)BaseMs << r, MaxMs);

            LevelFile.ReadHighScore(Path.ChangeExtension(a.Levels, ".ghs"), lv,
                                    out ushort gm, out ushort gs);

            // The flags exist before any searcher does, so that a Ctrl+C in the
            // moment between starting the first rung and starting the last one
            // still reaches all of them.
            CancelFlag[] flags = new CancelFlag[Ladder.Length];
            for (int i = 0; i < flags.Length; i++) flags[i] = new CancelFlag();
            _live = flags;

            // --jobs is the number of rungs that may run at once.  Below the
            // ladder size they queue -- and a rung that reaches the front of
            // the queue after somebody has already won returns immediately,
            // because its stop bit is set before it expands a single node.
            SemaphoreSlim slots = new SemaphoreSlim(jobs);
            Task<Program.Outcome>[] tasks = new Task<Program.Outcome>[Ladder.Length];
            for (int i = 0; i < Ladder.Length; i++)
            {
                SolveOptions o = Program.Clone(a.Opt);
                o.RunIda = o.RunBeam = o.RunMacro = o.RunSubgoal = false;
                o.NodeBudget = nodes;
                o.TimeBudgetMs = ms;
                o.Cancel = flags[i];
                Ladder[i].Tune(o, r);

                Program.Job job = new Program.Job
                {
                    Level = lv,
                    Name = info.LName,
                    Author = info.Author,
                    Diff = info.SDiff,
                    GhsMoves = gm,
                    GhsShots = gs,
                    LpbPath = Path.Combine(work,
                        string.Format("cand-{0:D5}-{1}.lpb", lv, Ladder[i].Name)),
                };
                File.Delete(job.LpbPath);
                tasks[i] = Task.Run(() =>
                {
                    slots.Wait();
                    try { return Program.SolveOne(a, job, o); }
                    finally { slots.Release(); }
                });
            }

            // Redirected output gets no live line (Paint returns), so a long
            // level would otherwise log nothing at all between the header and
            // the result.  One line per round is the same bargain Progress
            // strikes in Report.cs.
            if (!Ansi.On)
                Console.WriteLine("  round {0}: {1} nodes to each of {2} searchers",
                                  round, Num(nodes), Ladder.Length);

            Task all = Task.WhenAll(tasks);
            int width = 0;
            while (!all.Wait(120))
            {
                foreach (Task<Program.Outcome> t in tasks)
                    if (t.IsCompletedSuccessfully && t.Result.Solved) StopLive();
                Poll();
                if (_skip || _quit) StopLive();
                Paint(ref width, info, lv, round, nodes, flags, tasks, lt0);
            }
            Clear(ref width);
            _live = null;

            Program.Outcome win = null;
            foreach (Task<Program.Outcome> t in tasks)
            {
                Program.Outcome o = t.Result;
                if (o.Error != null && !o.Solved)
                    Console.WriteLine("  {0} {1}", Ansi.Red("error"), o.Error);
                if (win == null && o.Solved) { win = o; continue; }
                File.Delete(o.J.LpbPath);          // loser, or a duplicate win
            }
            return win;
        }

        private static void StopLive()
        {
            CancelFlag[] f = _live;
            if (f == null) return;
            foreach (CancelFlag c in f) c.Stop = true;
        }

        // ---- the keyboard --------------------------------------------------

        /// Whether there is a keyboard to poll at all.  A pipe, a `> log` and
        /// a terminal that hands .NET a redirected stdin (mintty does) all read
        /// as "no console", and such a run has only Ctrl+C -- which is worth
        /// saying in the banner rather than leaving to be discovered halfway
        /// through a level that will not end.
        private static bool Interactive
        {
            get
            {
                try
                {
                    if (Console.IsInputRedirected) return false;
                    _ = Console.KeyAvailable;      // throws if there is no console
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }
        }

        private static void Poll()
        {
            try
            {
                if (Console.IsInputRedirected) return;
                while (Console.KeyAvailable)
                {
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Q || k.Key == ConsoleKey.Escape) _quit = true;
                    else _skip = true;
                }
            }
            catch (InvalidOperationException) { }   // no console to read from
        }

        // ---- the live line -------------------------------------------------

        private static void Paint(ref int width, TLEVEL info, int lv, int round,
                                  long budget, CancelFlag[] flags,
                                  Task<Program.Outcome>[] tasks, DateTime lt0)
        {
            if (!Ansi.On) return;               // redirected: the result lines suffice
            long done = 0;
            List<string> running = new List<string>();
            for (int i = 0; i < flags.Length; i++)
            {
                done += flags[i].Nodes;
                if (!tasks[i].IsCompleted) running.Add(Ladder[i].Name);
            }
            string line = string.Format(CultureInfo.InvariantCulture,
                "  lv {0}  round {1}  {2}  {3} of {4} nodes  {5}  [{6}]",
                lv, round, Progress.Span((DateTime.UtcNow - lt0).TotalSeconds),
                Num(done), Num(budget * flags.Length),
                running.Count > 0 ? string.Join(" ", running) : "finishing",
                _skip || _quit ? "stopping" : "any key skips, q quits");

            if (line.Length > 118) line = line.Substring(0, 115) + "...";
            Console.Write("\r" + line.PadRight(Math.Max(width, line.Length)));
            width = line.Length;
        }

        private static void Clear(ref int width)
        {
            if (Ansi.On && width > 0) Console.Write("\r" + new string(' ', width) + "\r");
            width = 0;
        }

        // ---- the gate ------------------------------------------------------

        /// Hand one candidate .lpb to tools/verify_solutions.py, and move it
        /// into place only if it comes back clean.  -> null on success, or why.
        ///
        /// The tool verifies a *collection directory*, so the candidate is
        /// moved alone into a scratch directory named for the collection and
        /// that directory is emptied first: a leftover from a previous level
        /// would otherwise be re-verified, and worse, could fail the run for a
        /// solution this one did not produce.
        private static string Gate(string python, string gate, string root,
                                   string levels, string candidate,
                                   string verifyDir, string finalPath)
        {
            if (!File.Exists(candidate)) return "the winning searcher wrote no file";
            foreach (string old in Directory.GetFiles(verifyDir, "*.lpb"))
                File.Delete(old);
            string staged = Path.Combine(verifyDir, Path.GetFileName(finalPath));
            File.Move(candidate, staged, overwrite: true);

            ProcessStartInfo psi = new ProcessStartInfo(python)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(gate);
            psi.ArgumentList.Add(verifyDir);
            psi.ArgumentList.Add("--levels");
            psi.ArgumentList.Add(Path.GetFullPath(levels));
            psi.ArgumentList.Add("--jobs");
            psi.ArgumentList.Add("1");

            string output;
            int rc;
            using (Process p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                rc = p.ExitCode;
            }
            if (rc == 0)
            {
                File.Move(staged, finalPath, overwrite: true);
                return null;
            }
            File.Delete(staged);
            foreach (string line in output.Split('\n'))
                if (line.Contains("FAIL") || line.Contains("NOT checked"))
                    return line.Trim();
            return "verify_solutions.py exited " + rc + "\n" + output.Trim();
        }

        // ---- odds and ends --------------------------------------------------

        /// The repo root, from the executable rather than the working
        /// directory: build/lasertank-solve.exe is one level down, and the
        /// point of this tool is that it can be run from anywhere.
        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            return Path.GetFullPath(Path.Combine(dir, ".."));
        }

        private static string FindPython()
        {
            string env = Environment.GetEnvironmentVariable("LT_PYTHON");
            foreach (string cand in new[] { env, "python", "python3", "py" })
            {
                if (string.IsNullOrEmpty(cand)) continue;
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(cand, "--version")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    using Process p = Process.Start(psi);
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0) return cand;
                }
                catch (Exception) { }
            }
            return null;
        }

        /// Scratch candidates outlive an interrupted run; nothing reads them,
        /// so clear them rather than leave a directory that grows forever.
        private static void Sweep(string work)
        {
            try
            {
                foreach (string f in Directory.GetFiles(work, "cand-*.lpb"))
                    File.Delete(f);
            }
            catch (IOException) { }
        }

        private static readonly (int Id, string Name)[] Tiers =
        {
            (1, "Kids"), (2, "Easy"), (4, "Medium"), (8, "Hard"), (16, "Deadly"),
        };

        private static string Tier(int diff)
        {
            foreach ((int id, string name) in Tiers) if (id == diff) return name;
            return "unrated";
        }

        private static string Head(int lv, int count, TLEVEL info) =>
            string.Format(CultureInfo.InvariantCulture, "lv {0}/{1}  {2}  {3}",
                          lv, count, Ansi.Bold(info.LName.Trim()), Tier(info.SDiff));

        private static string Record(ushort moves, ushort shots) =>
            moves == 0 || moves >= 65500 ? "no record"
                : string.Format(CultureInfo.InvariantCulture,
                                "record {0} moves + {1} shots", moves, shots);

        private static string Detail(Program.Outcome o, int round, DateTime lt0)
        {
            string ratio = o.Ratio > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:F1}x the record", o.Ratio)
                : "no record to compare";
            return string.Format(CultureInfo.InvariantCulture,
                "{0} keys ({1} moves, {2} shots), {3}   {4}, round {5}, {6}, {7} nodes{8}",
                o.Keys, o.Moves, o.Shots, ratio, o.Method, round,
                Progress.Span((DateTime.UtcNow - lt0).TotalSeconds), Num(o.Nodes),
                o.Trimmed ? ", trimmed" : "");
        }

        private static string Num(long n) =>
            n >= 1000000000 ? (n / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + "G"
            : n >= 1000000 ? (n / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "M"
            : n >= 1000 ? (n / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "k"
            : n.ToString(CultureInfo.InvariantCulture);
    }
}
