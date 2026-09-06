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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
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
            // The conveyor rung: the laser ferry compressed, the stop cell
            // named, and the width to hold both.  It exists because
            // `LaserTank.lvl` 2 "Easy Level Conveyor" does, and the three
            // things it turns on are not three preferences -- level 2 is
            // unsolved at 60M nodes with any one of them off, and solved in
            // **2.03M nodes and 44 s** with all three:
            //
            //   * `--push-shot-run 16`.  PushRun for the laser.  The tank does
            //     not travel with what it shoots, so pushing a block k cells by
            //     laser was k depths of the beam; level 2's hand recording is
            //     32 board changes of which 26 are a repeat of the shot before
            //     them, on a ranking key that goes 13 -> 11 over the whole
            //     level.  Run-compressed it is six.
            //   * `--push-stop 1`.  Heuristic.RouteStop: to drive into the flag
            //     the tank has to be standing still on the cell next to it, so
            //     if that cell is a conveyor, the cell it discharges into needs
            //     a block -- and then the same question again about getting
            //     there.  On level 2 that names (13,1) and (13,2) from the root.
            //   * width 128.  Level 2 is unsolved at 64 and solved at 128 and
            //     256; a stop cell has one plan and the beam has to hold it
            //     alongside everything the free derivations are still ranking.
            //
            // **Its own rung, and the bench says why.**  Solo it is 18/50 on
            // the ferry bench and 18/50 on the deep one, against the plain push
            // rung's 19 and 21 -- a loss, read solo.  As a portfolio member,
            // which is the only honest way to read it, it adds **3 ferry levels
            // and 5 deep ones** to that rung's own solved set (union 22 and
            // 26).  Same shape as push-enables above, same conclusion: a
            // specialist costs a core, not a share of somebody's budget.
            ("push-stop", static (o, r) =>
            {
                o.RunPush = true;
                o.PushRead = true;
                o.PushStop = 1;
                o.PushShotRun = 16;
                o.PushBeamWidth = 128;
                o.PushRestarts += 6 * r;
            }),
        };

        // ---- lanes ---------------------------------------------------------

        /// One lane is one level being worked on: the searchers running on it
        /// right now, and the key that gives up on it.
        ///
        /// The driver used to be a single loop over levels, and a lane is what
        /// that loop became once there was more machine than one level could
        /// use.  Seven rungs on a sixteen-core box leaves nine cores idle, and
        /// a second lane is a whole further level to spend them on.
        ///
        /// **The lanes share one pool of `--jobs` slots rather than each
        /// claiming the ladder.**  That is the whole of the scheduling policy,
        /// and it is what keeps `--lanes` a free knob: four lanes against
        /// sixteen slots run four levels with about four searchers apiece, not
        /// twenty-eight compute-bound threads fighting over sixteen cores.  A
        /// rung that cannot get a slot waits, and if somebody wins the level
        /// while it waits it returns without expanding a node -- which is the
        /// same bargain the single-lane driver already struck whenever `--jobs`
        /// was below the ladder size.
        ///
        /// Everything a lane wants to *say* goes through the log queue rather
        /// than to Console, because two lanes writing at once would shred both
        /// the result block and the live display.  The painter on the main
        /// thread is the only writer there is.
        private sealed class Lane
        {
            public int Index;                   // 1-based: the key you press
            public volatile bool Skip;          // give up on this level
            public volatile bool Done;          // no levels left
            public volatile Snapshot Cur;       // what to paint, or null
            public string VerifyDir;
        }

        /// What the painter reads while a round runs.  Swapped wholesale at the
        /// top of each round, so the painter never sees one round's flags
        /// beside the next round's tasks.
        private sealed class Snapshot
        {
            public int Level, Round;
            public string Name = "";
            public string Note;                 // painted instead of a round
            public long Budget;
            public DateTime T0;
            public CancelFlag[] Flags;
            public Task<Program.Outcome>[] Tasks;
            // Set when a rung takes a slot, cleared when it gives it back.  A
            // started task and a queued one are both "not completed", and
            // painting them alike is how the footer came to claim fourteen
            // busy searchers on nine slots.
            public bool[] Running;
        }

        /// Everything a lane needs that is the same for every lane.
        private sealed class Ctx
        {
            public Program.Args A;
            public Func<int> Next;              // the level dispenser, -1 when done
            public int Count, Lanes;
            public string OutDir, Work, Ghs, Python, Gate, Root;
            public SemaphoreSlim Slots;
            public int Solved, Skipped, Already, Rejected;
            public readonly List<int> Unsolved = new List<int>();
        }

        // What Ctrl+C sets.  Static because the handler has to reach every lane
        // that is running now, and a lane is not reachable from it otherwise.
        private static volatile bool _quit;
        private static volatile Lane[] _lanes;

        // The single-writer console.  Lanes enqueue finished blocks; the
        // painter drains them between repaints.
        private static readonly ConcurrentQueue<string> _log =
            new ConcurrentQueue<string>();

        // What the live block is showing now: the plain text of each row (to
        // work out how many screen lines it occupies), the exact bytes that
        // were written (to notice a repaint that would change nothing), and the
        // width they were cut to (to notice a resize).
        private static List<string> _frame = new List<string>();
        private static string _painted = "";
        private static int _cols;

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

            int count = LevelFile.CountLevels(a.Levels);
            int from = Math.Max(1, a.From), to = (int)Math.Min(a.To, count);
            if (from > to)
            {
                Console.Error.WriteLine("{0} has {1} levels; --from {2}{3} selects none",
                                        collection, count, from,
                                        a.To == int.MaxValue ? "" : " --to " + a.To);
                return 2;
            }

            // No more lanes than there are levels to put in them: an empty lane
            // is a painted line that never says anything.
            int lanes = Math.Max(1, Math.Min(a.Lanes, to - from + 1));
            int jobs = Math.Max(1, a.Jobs);

            Lane[] pool = new Lane[lanes];
            for (int i = 0; i < lanes; i++)
            {
                // A lane verifies in a directory of its own, because Gate()
                // *empties* the one it is handed before staging into it: two
                // lanes sharing one would delete each other's candidate and,
                // worse, could hand a lane the other lane's solution to pass
                // off as its own.  The collection name stays the leaf, so the
                // staged file still sits where the gate expects a collection.
                pool[i] = new Lane
                {
                    Index = i + 1,
                    VerifyDir = Path.Combine(work, "l" + (i + 1), collection),
                };
                Directory.CreateDirectory(pool[i].VerifyDir);
            }
            _lanes = pool;

            Console.CancelKeyPress += static (_, e) =>
            {
                e.Cancel = true;            // unwind and print, do not die here
                _quit = true;
                StopAll();
            };

            Console.WriteLine("{0}  levels {1}-{2} of {3}   {4} searchers per level,"
                              + " {5}, budget x4 each round",
                              Ansi.Bold(collection), from, to, count, Ladder.Length,
                              // One lane can never have more than the ladder
                              // running however many slots it is given, and
                              // saying "16 at a time" of a 7-rung ladder is a
                              // number the display would then contradict.
                              lanes == 1
                                ? Math.Min(jobs, Ladder.Length) + " at a time"
                                : lanes + " levels at a time over " + jobs + " slots");
            Console.WriteLine(Ansi.Dim(!Interactive
                ? "  stdin is not a console, so there is no key to press here: "
                  + "Ctrl+C is the way out\n"
                : lanes == 1
                  ? "  any key gives up on the level and moves to the next; q quits\n"
                  : "  1-" + lanes + " gives up on that lane's level; q quits\n"));

            DateTime t0 = DateTime.UtcNow;
            int next = from - 1;            // Interlocked.Increment yields `from` first
            Ctx ctx = new Ctx
            {
                A = a, Count = count, Lanes = lanes, OutDir = outDir, Work = work,
                Ghs = ghsPath, Python = python, Gate = gate, Root = root,
                Slots = new SemaphoreSlim(jobs),
                Next = () =>
                {
                    int lv = Interlocked.Increment(ref next);
                    return lv <= to ? lv : -1;
                },
            };

            Task[] workers = new Task[lanes];
            for (int i = 0; i < lanes; i++)
            {
                Lane lane = pool[i];
                workers[i] = Task.Run(() => Worker(ctx, lane));
            }

            Task all = Task.WhenAll(workers);
            while (!all.Wait(120))
            {
                Poll(pool);
                Flush();
                Paint(pool, t0, ctx);
            }
            Erase();
            Flush();

            Sweep(work);
            Console.WriteLine("{0} solved, {1} skipped{2}{3}   in {4}",
                ctx.Solved, ctx.Skipped,
                ctx.Already > 0 ? ", " + ctx.Already + " already had a solution" : "",
                ctx.Rejected > 0
                    ? ", " + Ansi.Red(ctx.Rejected + " REJECTED by the gate") : "",
                Progress.Span((DateTime.UtcNow - t0).TotalSeconds));
            ctx.Unsolved.Sort();
            if (ctx.Unsolved.Count > 0)
                Console.WriteLine(Ansi.Dim("  still unsolved: "
                                           + string.Join(",", ctx.Unsolved)));
            return 0;
        }

        // ---- one lane ------------------------------------------------------

        /// Take levels until there are none left, and give each as many rounds
        /// as it takes.  This is the loop the driver used to be, with the level
        /// number coming from a dispenser instead of a `for`, and every
        /// Console.Write replaced by an enqueue.
        private static void Worker(Ctx ctx, Lane lane)
        {
            while (!_quit)
            {
                int lv = ctx.Next();
                if (lv < 0) break;

                TLEVEL info = LevelFile.ReadLevel(ctx.A.Levels, lv);
                if (info == null) break;

                string lpb = Path.Combine(ctx.OutDir, string.Format("{0:D5}.lpb", lv));
                if (!ctx.A.Force && File.Exists(lpb))
                {
                    Interlocked.Increment(ref ctx.Already);
                    Say(Head(lv, ctx.Count, info) + "  "
                        + Ansi.Dim("already solved -- --force re-solves it"));
                    continue;
                }

                LevelFile.ReadHighScore(ctx.Ghs, lv, out ushort gm, out ushort gs);

                // The header used to print before the first round and the
                // result after it.  With lanes it is held back and printed with
                // the result as one block: two lanes each writing half a level's
                // story would interleave into neither.  Nothing is lost -- what
                // a level is doing *now* is what the live block is for, and it
                // says it better than a header line did.
                List<string> block = new List<string>
                {
                    Head(lv, ctx.Count, info) + "  " + Ansi.Dim(Record(gm, gs)),
                };

                lane.Skip = false;
                bool won = false;
                DateTime lt0 = DateTime.UtcNow;
                for (int round = 0; !won && !lane.Skip && !_quit; round++)
                {
                    Program.Outcome o = Round(ctx, lane, info, lv, round, lt0);
                    if (o == null) continue;                       // nobody won

                    // The gate is a python process and takes a second or two on
                    // a long solution, which is a second or two of a lane's line
                    // saying nothing if it is not said here.
                    lane.Cur = new Snapshot
                    {
                        Level = lv, Name = info.LName.Trim(), Round = round,
                        T0 = lt0, Note = "verifying",
                    };
                    string why = Gate(ctx.Python, ctx.Gate, ctx.Root, ctx.A.Levels,
                                      o.J.LpbPath, lane.VerifyDir, lpb);
                    if (why == null)
                    {
                        won = true;
                        Interlocked.Increment(ref ctx.Solved);
                        block.Add("  " + Ansi.Green("SOLVED") + "  "
                                  + Detail(o, round, lt0));
                        block.Add("  " + Ansi.Dim(
                            "verified through both engines -> " + lpb));
                    }
                    else
                    {
                        // Not a near miss: the solver's own engine already
                        // replayed this and saw it win, so a gate failure is a
                        // divergence between the engines and wants shouting
                        // about.  Phase 3 says there are none left; this is how
                        // you would find out otherwise.
                        Interlocked.Increment(ref ctx.Rejected);
                        block.Add("  " + Ansi.Red("REJECTED") + "  " + why);
                        block.Add("  " + Ansi.Dim(
                            "discarded; searching on -- this is an engine divergence, "
                            + "please keep the level number"));
                    }
                }

                if (!won)
                {
                    lock (ctx.Unsolved) ctx.Unsolved.Add(lv);
                    if (lane.Skip) Interlocked.Increment(ref ctx.Skipped);
                    block.Add("  " + Ansi.Yellow(
                        (_quit ? "stopped" : "skipped") + " after "
                        + Progress.Span((DateTime.UtcNow - lt0).TotalSeconds)));
                }
                lane.Cur = null;
                Say(block);
            }
            lane.Cur = null;
            lane.Done = true;
        }

        // ---- one round -----------------------------------------------------

        /// Every searcher on the level at once; the first win, or null.
        ///
        /// Each rung writes to its own scratch .lpb, because two of them
        /// finishing together would otherwise race on one path.  The winner's
        /// file is the one the gate is handed; the rest are deleted here.
        private static Program.Outcome Round(Ctx ctx, Lane lane, TLEVEL info,
                                             int lv, int round, DateTime lt0)
        {
            Program.Args a = ctx.A;

            // Quadrupling has to stop being taken literally at some point or
            // the shift overflows.  Round 12 is 400k x 4^12, which is a search
            // no machine will finish; past it the rounds still differ, because
            // the searchers themselves keep widening.
            int r = Math.Min(round, 12);
            long nodes = (a.NodesGiven ? a.Opt.NodeBudget : BaseNodes) << (2 * r);
            int ms = (int)Math.Min((long)BaseMs << r, MaxMs);

            LevelFile.ReadHighScore(ctx.Ghs, lv, out ushort gm, out ushort gs);

            // The flags exist before any searcher does, so that a quit arriving
            // in the moment between starting the first rung and starting the
            // last one still reaches all of them.  For the same reason the
            // snapshot -- which is how the keyboard finds the flags at all --
            // is published before the loop rather than after it.
            CancelFlag[] flags = new CancelFlag[Ladder.Length];
            for (int i = 0; i < flags.Length; i++) flags[i] = new CancelFlag();
            Task<Program.Outcome>[] tasks = new Task<Program.Outcome>[Ladder.Length];
            bool[] running = new bool[Ladder.Length];
            lane.Cur = new Snapshot
            {
                Level = lv, Name = info.LName.Trim(), Round = round, Budget = nodes,
                T0 = lt0, Flags = flags, Tasks = tasks, Running = running,
            };
            if (_quit || lane.Skip) Stop(lane);

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
                    // The lane is in the name because two lanes on the same
                    // level -- which --force makes possible -- would otherwise
                    // write one path from two threads; the process id is there
                    // so that two drivers running at once neither collide here
                    // nor sweep each other's files afterwards.  See Sweep().
                    LpbPath = Path.Combine(ctx.Work,
                        string.Format("cand-{0}-l{1}-{2:D5}-{3}.lpb",
                                      Environment.ProcessId, lane.Index, lv,
                                      Ladder[i].Name)),
                };
                File.Delete(job.LpbPath);
                int rung = i;
                tasks[i] = Task.Run(() =>
                {
                    // The slots are the machine, and every lane draws on the
                    // same pool.  A rung that waits here and reaches the front
                    // after somebody has already won returns without expanding a
                    // node, because its stop bit was set while it queued.
                    ctx.Slots.Wait();
                    running[rung] = true;
                    try { return Program.SolveOne(a, job, o); }
                    finally { running[rung] = false; ctx.Slots.Release(); }
                });
            }

            // Redirected output gets no live block (Paint returns), so a long
            // level would otherwise log nothing at all between the header and
            // the result.  One line per round is the same bargain Progress
            // strikes in Report.cs.
            if (!Ansi.On)
                Say(string.Format(CultureInfo.InvariantCulture,
                                  "  {0}round {1}: {2} nodes to each of {3} searchers",
                                  ctx.Lanes > 1
                                    ? "lane " + lane.Index + ", lv " + lv + ": " : "",
                                  round, Num(nodes), Ladder.Length));

            Task all = Task.WhenAll(tasks);
            while (!all.Wait(120))
            {
                foreach (Task<Program.Outcome> t in tasks)
                    if (t.IsCompletedSuccessfully && t.Result.Solved) Stop(lane);
                if (_quit || lane.Skip) Stop(lane);
            }

            Program.Outcome win = null;
            for (int i = 0; i < tasks.Length; i++)
            {
                Program.Outcome o = tasks[i].Result;
                if (o.Error != null && !o.Solved)
                    Say("  " + Ansi.Red("error") + " " + o.Error);
                if (win == null && o.Solved)
                {
                    // The searcher names itself after its *layer*, so three
                    // rungs now report "push" and the result line stops saying
                    // which configuration actually did it -- which is the one
                    // thing a reader of this line wants when a level falls.
                    if (o.Method != Ladder[i].Name) o.Method = Ladder[i].Name;
                    win = o;
                    continue;
                }
                File.Delete(o.J.LpbPath);          // loser, or a duplicate win
            }
            return win;
        }

        /// Stop the searchers of one lane -- what a digit key means.
        private static void Stop(Lane lane)
        {
            Snapshot s = lane.Cur;
            CancelFlag[] f = s?.Flags;
            if (f == null) return;
            foreach (CancelFlag c in f) c.Stop = true;
        }

        /// Stop every lane -- what Ctrl+C and q mean.
        private static void StopAll()
        {
            Lane[] pool = _lanes;
            if (pool == null) return;
            foreach (Lane l in pool) Stop(l);
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

        /// q quits everything; a digit gives up on that lane's level.
        ///
        /// One lane keeps the old any-key meaning, because that is what the
        /// banner has always promised and there is nothing to disambiguate when
        /// there is only one thing a key could mean.  With lanes there is, so a
        /// key that names no lane is ignored rather than guessed at: throwing
        /// away an hour of the wrong lane's search is not a thing to do on a
        /// maybe.
        private static void Poll(Lane[] pool)
        {
            try
            {
                if (Console.IsInputRedirected) return;
                while (Console.KeyAvailable)
                {
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Q || k.Key == ConsoleKey.Escape)
                    {
                        _quit = true;
                        StopAll();
                        continue;
                    }
                    int n = k.KeyChar - '0';
                    Lane hit = n >= 1 && n <= pool.Length ? pool[n - 1]
                             : pool.Length == 1 ? pool[0] : null;
                    if (hit == null) continue;
                    hit.Skip = true;
                    Stop(hit);
                }
            }
            catch (InvalidOperationException) { }   // no console to read from
        }

        // ---- the live block ------------------------------------------------

        /// Hand the painter a finished level's lines, with the blank line that
        /// separates one level's block from the next.
        private static void Say(List<string> lines)
        {
            foreach (string s in lines) _log.Enqueue(s);
            _log.Enqueue("");
        }

        private static void Say(string line) => _log.Enqueue(line);

        /// Print what the lanes have queued.  Only ever called from the main
        /// thread, and only with the live block erased first, so a result never
        /// lands in the middle of the display.
        private static void Flush()
        {
            if (_log.IsEmpty) return;
            Erase();
            StringBuilder b = new StringBuilder();
            while (_log.TryDequeue(out string line)) b.Append(line).Append('\n');
            Console.Write(b.ToString());
        }

        /// One line per lane, plus the footer.
        ///
        /// A lane's line is rebuilt from scratch every repaint rather than
        /// edited in place, because the lane it describes changes level
        /// underneath it whenever one falls -- which is the swap the whole
        /// display is for.
        private static void Paint(Lane[] pool, DateTime t0, Ctx ctx)
        {
            if (!Ansi.On) return;               // redirected: the result lines suffice

            List<(string Text, bool Dim)> rows = new List<(string, bool)>();
            int busy = 0;
            foreach (Lane lane in pool)
            {
                Snapshot s = lane.Cur;
                string tag = pool.Length > 1 ? "  " + lane.Index + "  " : "  ";
                if (s == null)
                {
                    rows.Add((tag + (lane.Done ? "done" : "--"), true));
                    continue;
                }
                if (s.Note != null)
                {
                    rows.Add((string.Format(CultureInfo.InvariantCulture,
                        "{0}lv {1}  {2}  {3}", tag, s.Level, Cut(s.Name, 20), s.Note),
                        true));
                    continue;
                }

                long done = 0;
                int queued = 0;
                List<string> running = new List<string>();
                for (int i = 0; i < s.Flags.Length; i++)
                {
                    done += s.Flags[i].Nodes;
                    // A task the loop that starts the rungs has not reached yet
                    // reads as queued rather than as a null dereference: the
                    // painter sees this snapshot the moment it is published,
                    // which is before any rung has been started.
                    if (s.Tasks[i] != null && s.Tasks[i].IsCompleted) continue;
                    if (s.Running[i]) { running.Add(Ladder[i].Name); busy++; }
                    else queued++;
                }
                // What a lane is *doing* is the rungs holding a slot.  The ones
                // still queued are worth a count, because a lane showing two
                // searchers on a seven-rung ladder is the machine being full
                // rather than the level being nearly done.
                string what = running.Count > 0 ? string.Join(" ", running)
                            : queued > 0 ? "queued" : "finishing";
                if (running.Count > 0 && queued > 0) what += " +" + queued;
                rows.Add((string.Format(CultureInfo.InvariantCulture,
                    "{0}lv {1}  {2}  r{3}  {4}  {5}/{6}  {7}",
                    tag, s.Level, Cut(s.Name, 20), s.Round,
                    Progress.Span((DateTime.UtcNow - s.T0).TotalSeconds),
                    Num(done), Num(s.Budget * s.Flags.Length), what), false));
            }

            rows.Add((pool.Length > 1
                ? string.Format(CultureInfo.InvariantCulture,
                    "  1-{0} gives up on that lane, q quits   "
                    + "{1} solved, {2} searchers busy   {3}",
                    pool.Length, ctx.Solved, busy,
                    Progress.Span((DateTime.UtcNow - t0).TotalSeconds))
                : string.Format(CultureInfo.InvariantCulture,
                    "  any key skips, q quits   {0} solved   {1}",
                    ctx.Solved, Progress.Span((DateTime.UtcNow - t0).TotalSeconds)),
                true));

            Draw(rows);
        }

        /// Redraw the block in place.
        ///
        /// The invariant the cursor arithmetic rests on: after a draw the
        /// cursor sits at column 0 of the line *below* the last row, because
        /// every row went out with a newline.  So erasing is "up by the number
        /// of screen lines the block occupies, then clear" -- both moves
        /// relative to the cursor, so they stay correct when the terminal
        /// scrolled while we were away.
        ///
        /// Three things keep it from blinking:
        ///
        ///   * The whole frame -- the cursor-up, every row, the trailing clear
        ///     -- is one Console.Write.  Erasing and then writing the rows one
        ///     WriteLine at a time is what the blink was: each write reaches
        ///     the terminal separately, so it gets to paint the gap between the
        ///     erase and the rows that replace it.
        ///   * The old rows are overwritten in place and each new row clears to
        ///     the end of its own line, so nothing is blanked before it is
        ///     rewritten.  The screen-clear is only appended when the new block
        ///     is shorter than the old one, and only below the part that stays.
        ///   * A repaint that would produce the same bytes writes nothing at
        ///     all.  At eight repaints a second most of them are that.
        ///
        /// Rows are cut to the window rather than to a fixed 118: a row that
        /// wraps is two lines on screen and one in the row list, and from there
        /// the block walks up the scrollback a line per repaint.  The text is
        /// built plain and coloured only after the cut for the same reason --
        /// an escape sequence is characters that occupy no column, so cutting
        /// through one both miscounts the width and leaves the rest of the
        /// terminal dim.
        private static void Draw(List<(string Text, bool Dim)> rows)
        {
            int cols = Cols();
            List<string> plain = new List<string>(rows.Count);
            StringBuilder body = new StringBuilder();
            foreach ((string text, bool dim) in rows)
            {
                string s = text.Length > cols
                    ? text.Substring(0, Math.Max(0, cols - 3)) + "..." : text;
                plain.Add(s);
                // ESC[K after the row, not spaces up to the margin: padding a
                // row to the full width is what makes a block flash on a
                // terminal that is scrolling it.
                body.Append(dim ? Ansi.Dim(s) : s).Append("\u001b[K\n");
            }

            string want = body.ToString();
            if (cols == _cols && want == _painted) return;

            int was = Lines(_frame, cols);
            int now = Lines(plain, cols);
            StringBuilder f = new StringBuilder();
            f.Append("\u001b[?25l");                          // hide the cursor
            if (was > 0) f.Append("\u001b[").Append(was).Append("A\r");
            f.Append(want);
            if (now < was) f.Append("\u001b[J");              // block got shorter
            f.Append("\u001b[?25h");
            Console.Write(f.ToString());

            _frame = plain;
            _painted = want;
            _cols = cols;
        }

        private static void Erase()
        {
            if (!Ansi.On || _frame.Count == 0) return;
            Console.Write("\u001b[" + Lines(_frame, Cols()) + "A\r\u001b[J");
            _frame = new List<string>();
            _painted = "";
        }

        /// How many screen lines the drawn rows occupy *at the current width*.
        ///
        /// Not simply rows.Count, which is what left stale copies behind after
        /// a resize: the rows were cut to the width they were drawn at, so
        /// narrowing the window wraps each of them onto two or more lines, and
        /// a cursor-up of one line per row then stops short and redraws the
        /// block below its own tail.  Terminals that reflow on resize (Windows
        /// Terminal, xterm) land exactly on this count; one that does not still
        /// gets the count right whenever the width has not changed, which is
        /// every repaint but the one just after the drag.
        private static int Lines(List<string> rows, int cols)
        {
            if (cols <= 0) return rows.Count;
            int n = 0;
            foreach (string s in rows) n += Math.Max(1, (s.Length + cols - 1) / cols);
            return n;
        }

        private static int Cols()
        {
            try
            {
                int w = Console.WindowWidth;
                return w > 40 ? w - 1 : 118;
            }
            catch (IOException) { return 118; }
            catch (ArgumentOutOfRangeException) { return 118; }
        }

        /// Pad or cut a level name to a fixed width, so the columns after it
        /// line up down the block rather than moving with every level change.
        private static string Cut(string s, int n) =>
            s.Length <= n ? s.PadRight(n) : s.Substring(0, n - 1) + ".";


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
        ///
        /// **A run sweeps its own candidates and nobody else's**, which is why
        /// the process id is in the name.  It used to sweep `cand-*.lpb` --
        /// fine when a driver was the only thing running, and not fine now: a
        /// second driver started while the first is still going would delete a
        /// candidate out from under it between the searcher writing it and the
        /// gate reading it, and the first run would report a level it had
        /// actually solved as "the winning searcher wrote no file".  Two
        /// drivers at once is a normal thing to want -- one long campaign in
        /// one terminal, a quick look at a single level in another.
        ///
        /// A killed run leaves its own behind, so the day-old ones go too: that
        /// is the growing-forever the sweep was written for, and a candidate
        /// nothing has touched in a day belongs to no live process.
        private static void Sweep(string work)
        {
            DateTime stale = DateTime.UtcNow.AddDays(-1);
            string mine = "cand-" + Environment.ProcessId + "-";
            foreach (string f in Directory.GetFiles(work, "cand-*.lpb"))
            {
                try
                {
                    if (Path.GetFileName(f).StartsWith(mine, StringComparison.Ordinal)
                        || File.GetLastWriteTimeUtc(f) < stale)
                        File.Delete(f);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
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
