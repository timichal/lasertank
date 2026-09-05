// The batch harness: solve a range of levels, write each solution as a .lpb,
// append one JSON line per level to a report.
//
// Design notes that are decisions rather than detail:
//
//   * Nothing here trusts itself.  A .lpb is written only after the solver's
//     own engine has replayed it from the level start and seen it win, and
//     tools/verify_solutions.py then replays every emitted file through the
//     *unmodified* C oracle as well.  The solver is not in the trust chain.
//   * Resumable by construction: a level whose .lpb already exists is skipped
//     unless --force.  20,914 levels is a run you will want to interrupt.
//   * Ordered by the .ghs target, cheapest first, not by level number -- that
//     is the curriculum, and it makes the solved-vs-budget curve readable.
//   * Per-level budgets, not a global one, so one hard level cannot eat the run.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public static class Program
    {
        private static void Usage()
        {
            Console.Error.WriteLine(
"usage: lasertank-solve FILE.lvl [--from N] [--to N]           <- interactive\n" +
"       lasertank-solve --levels FILE.lvl [--out DIR] [--report FILE.jsonl]\n" +
"\n" +
"  interactive -- a bare FILE.lvl, no other flag required.  Solves level by\n" +
"  level in *number* order, and spends as long on each as it takes: every\n" +
"  searcher that ships runs side by side on its own thread and the node budget\n" +
"  quadruples each round, until the level falls or you press a key to give up\n" +
"  on it (q quits).  A solution is kept only once tools/verify_solutions.py\n" +
"  has replayed it through both engines.  --from/--to/--out/--force/--author/\n" +
"  --trim-ratio/--jobs apply; --nodes sets round 0's budget, not a cap.\n" +
"\n" +
"  selection\n" +
"    --level N            just this level        --from N / --to N   a range\n" +
"    --levels-list FILE   one level number per line -- e.g. the levels a\n" +
"                         previous campaign report says are still unsolved\n" +
"    --difficulty LIST    comma list of 1,2,4,8,16 (Kids..Deadly) or 0 (unrated)\n" +
"    --order ghs|number   solve cheapest-by-.ghs first (default) or in file order\n" +
"    --limit N            stop after N levels attempted\n" +
"    --stride N           every Nth level only -- a whole-corpus campaign at a\n" +
"                         real budget is hours, and a 1-in-N sample of every\n" +
"                         collection measures the same rates (cf. sweep.py)\n" +
"    --force              re-solve levels whose .lpb already exists\n" +
"\n" +
"  budget (per level)\n" +
"    --budget-ms N        wall clock, default 4000\n" +
"    --nodes N            ApplyKey calls -- the unit that costs, and the one\n" +
"                         to govern a campaign by; wall clock is not reproducible\n" +
"    --beam N             beam width, default 600      --max-keys N\n" +
"    --ida-depth N        IDA* bound cap, default 24   --no-ida / --no-beam\n" +
"    --jobs N             parallel workers, default = processor count\n" +
"\n" +
"  layer 1 -- macro-actions (Goto + Shoot).  OFF by default: it wins on levels\n" +
"  the raw beam cannot solve and loses over the corpus, so it belongs in a\n" +
"  second pass (tools/second_pass.sh), not in the portfolio.  --macro enables it\n" +
"    --macro-beam N       macro-nodes kept per shot-depth, default 24\n" +
"    --macro-depth N      shots in a solution, default 128\n" +
"    --closure-nodes N    states in one Goto closure, default 1500\n" +
"    --closure-depth N    movement keys in one Goto, default 40\n" +
"    --move-only N        pure-Goto successors kept per node, default 6\n" +
"    --ida-share R        budget fraction IDA* may run until, default 0.2\n" +
"    --macro-share R      ...and the macro beam, default 0.3 (cumulative, so\n" +
"                         that is a tenth of the budget after IDA*)\n" +
"    --closed generate|expand  when a beam closes a state, default generate\n" +
"    --macro-first        run the macro beam before the raw beam, not after\n" +
"    --beam-share R       budget fraction the raw beam runs until when the\n" +
"                         macro beam is last, default 0.6\n" +
"\n" +
"  layer 2 -- subgoal decomposition (Subgoal.cs).  OFF by default, same reason,\n" +
"  and it belongs in the same second pass: the movement closure runs first, the\n" +
"  obstacles between where the tank actually got to and the flag are derived\n" +
"  from it, and a successor is kept because it made one of those cheaper --\n" +
"  not because a heuristic went down.  --subgoal enables it\n" +
"    --sg-width N         subgoal-steps kept per depth, default 4 (narrow and\n" +
"                         deep is the point; 12 measured worse)\n" +
"    --sg-depth N         subgoal steps, default 400 -- a backstop, the node\n" +
"                         budget binds long before it\n" +
"    --sg-closure N       states in one movement closure, default 400\n" +
"    --sg-closure-depth N movement keys in one closure, default 32\n" +
"    --sg-candidates N    derived obstacles treated as targets, default 64\n" +
"    --sg-slack N         board-changing successors kept per expansion when\n" +
"                         nothing advanced, default 4 -- what lets a two-move\n" +
"                         manoeuvre (turn the mirror, then shoot) be found\n" +
"    --sg-fallback N      pure-Goto states kept when nothing was derived or\n" +
"                         nothing advanced, default 2\n" +
"    --sg-closed generate|expand   default expand, the *opposite* of --closed:\n" +
"                         a 600-wide beam gains from over-pruning and a 4-wide\n" +
"                         one dies of it (both measured)\n" +
"    --sg-strict          accept only on a cleared obstacle, never on a shorter\n" +
"                         route.  Measured worse (3 against 10 on the deep bench)\n" +
"    --sg-aim             fire only from poses whose ray meets a target, mirror\n" +
"                         or anti-tank.  Measured much worse (2 against 10): it\n" +
"                         is a superset of the shots that hit a target and not\n" +
"                         of the shots worth firing\n" +
"    --sg-trace           per-expansion diagnostics to stderr -- obstacles\n" +
"                         derived, closure size, successors kept.  This is what\n" +
"                         found both of layer 2's wrong turns; keep it working\n" +
"    --subgoal-first      run it before the raw beam    --subgoal-share R (0.9)\n" +
"\n" +
"  layer 3 -- restarts (Restart.cs).  ON by default, unlike the two above,\n" +
"  because a restart only ever spends budget that was already forfeit: the\n" +
"  subgoal beam is re-run only when it died at subgoal-dead-end with nodes\n" +
"  still in hand (19.1% of layer 2's failures, with a median 84% of the\n" +
"  budget unspent).  Attempt 0 is layer 2 exactly, so nothing can be lost\n" +
"    --sg-restarts N      extra attempts after a dead-end, default 6; 0 is off\n" +
"    --sg-noise N         ranking jitter on a restart, default 3.  The frontier\n" +
"                         of a dead-ending run is almost all slack nodes, so\n" +
"                         this is the diversifier that acts; it is added after\n" +
"                         acceptance is decided and can only reorder\n" +
"    --sg-grow / --sg-no-grow  double width and slack on each restart, on by\n" +
"                         default: width bought up front is a loss (deep bench\n" +
"                         10 at width 4, 8 at 8, 6 at 16) and the same width\n" +
"                         bought after narrow has failed is a win\n" +
"    --sg-reuse root|reserve  where a restart begins, default root.  reserve\n" +
"                         re-seeds from the nodes the width trim discarded --\n" +
"                         cheaper, and measured worse (corpus 43 against 44)\n" +
"    --sg-reserve N       discarded nodes held for a restart, default 64\n" +
"    --sg-reserve-depth N ...and how many one depth may add, default 2\n" +
"\n" +
"  layer 4 -- a learned evaluation (Learn.cs).  A ranking change and nothing\n" +
"  else: acceptance stays layer 2's board test and only the order of what\n" +
"  survived is learned, so a model can never admit a state the search refused\n" +
"    --sg-eval work|learned   ranking key for the subgoal beam, default work\n" +
"                         (= layer 3).  The seed weight vector is WorkDistance\n" +
"                         exactly, so `learned` with it reproduces layer 3\n" +
"    --eval-weights FILE  one weight per line, in place of the built-in vector\n" +
"    --rank-dump FILE     instrument, not a solve: replay winning .lpb one key\n" +
"                         at a time, run the shipped subgoal expansion from\n" +
"                         each shot boundary, and write one row per candidate\n" +
"                         with its features and whether the winner went through\n" +
"                         it.  Needs --lpb-list; --levels names the collection\n" +
"    --lpb-list FILE      one .lpb path per line, for --rank-dump\n" +
"\n" +
"  output\n" +
"    --trim-ratio R       trim a solution longer than R x the .ghs total (10)\n" +
"    --author NAME        .lpb author field, default \"LTSolver\"\n" +
"    --quiet              summary only         --verbose   one line per level\n");
        }

        internal sealed class Args
        {
            public string Levels, Out = "solutions", Report;
            public string Author = "LTSolver";
            public int From = 1, To = int.MaxValue, Limit = int.MaxValue;
            public int Jobs = Environment.ProcessorCount;
            public double TrimRatio = 10.0;
            public bool Force, Quiet, Verbose, ByNumber;
            public bool Auto, NodesGiven, OutGiven;
            public int Stride = 1;
            public readonly HashSet<int> Difficulty = new HashSet<int>();
            public HashSet<int> Only;      // --levels-list, null when unused
            public string RankDump, LpbList;
            public readonly SolveOptions Opt = new SolveOptions();
        }

        public static int Main(string[] argv)
        {
            Args a = new Args();
            if (argv.Length == 0) { Usage(); return 2; }
            try
            {
                for (int i = 0; i < argv.Length; i++)
                {
                    string k = argv[i];
                    string V() => argv[++i];
                    switch (k)
                    {
                        case "--levels": a.Levels = V(); break;
                        case "--out": a.Out = V(); a.OutGiven = true; break;
                        case "--report": a.Report = V(); break;
                        case "--author": a.Author = V(); break;
                        case "--level": a.From = a.To = int.Parse(V()); break;
                        case "--levels-list":
                            a.Only = new HashSet<int>();
                            foreach (string line in File.ReadAllLines(V()))
                            {
                                string t = line.Trim();
                                if (t.Length > 0 && t[0] != '#') a.Only.Add(int.Parse(t));
                            }
                            break;
                        case "--from": a.From = int.Parse(V()); break;
                        case "--to": a.To = int.Parse(V()); break;
                        case "--limit": a.Limit = int.Parse(V()); break;
                        case "--stride": a.Stride = Math.Max(1, int.Parse(V())); break;
                        case "--jobs": a.Jobs = Math.Max(1, int.Parse(V())); break;
                        case "--trim-ratio": a.TrimRatio = double.Parse(V(), CultureInfo.InvariantCulture); break;
                        case "--budget-ms": a.Opt.TimeBudgetMs = int.Parse(V()); break;
                        case "--nodes": a.Opt.NodeBudget = long.Parse(V()); a.NodesGiven = true; break;
                        case "--beam": a.Opt.BeamWidth = int.Parse(V()); break;
                        case "--max-keys": a.Opt.MaxKeys = int.Parse(V()); break;
                        case "--ida-depth": a.Opt.IdaMaxDepth = int.Parse(V()); break;
                        case "--no-ida": a.Opt.RunIda = false; break;
                        case "--no-beam": a.Opt.RunBeam = false; break;
                        case "--macro": a.Opt.RunMacro = true; break;
                        case "--no-macro": a.Opt.RunMacro = false; break;
                        case "--macro-first": a.Opt.MacroLast = false; break;
                        case "--beam-share": a.Opt.BeamShare = double.Parse(V(), CultureInfo.InvariantCulture); break;
                        case "--closed": a.Opt.CloseOnGenerate = V() != "expand"; break;
                        case "--subgoal": a.Opt.RunSubgoal = true; break;
                        case "--subgoal-first": a.Opt.SubgoalLast = false; break;
                        case "--subgoal-share": a.Opt.SubgoalShare = double.Parse(V(), CultureInfo.InvariantCulture); break;
                        case "--sg-width": a.Opt.SgWidth = int.Parse(V()); break;
                        case "--sg-depth": a.Opt.SgDepth = int.Parse(V()); break;
                        case "--sg-closure": a.Opt.SgClosureNodes = int.Parse(V()); break;
                        case "--sg-closure-depth": a.Opt.SgClosureDepth = int.Parse(V()); break;
                        case "--sg-candidates": a.Opt.SgCandidates = int.Parse(V()); break;
                        case "--sg-fallback": a.Opt.SgFallbackK = int.Parse(V()); break;
                        case "--sg-slack": a.Opt.SgSlack = int.Parse(V()); break;
                        case "--sg-strict": a.Opt.SgStrict = true; break;
                        case "--sg-closed": a.Opt.SgCloseOnExpand = V() != "generate"; break;
                        case "--sg-aim": a.Opt.SgAim = true; break;
                        case "--sg-trace": a.Opt.SgTrace = true; break;
                        case "--sg-restarts": a.Opt.SgRestarts = int.Parse(V()); break;
                        case "--sg-noise": a.Opt.SgNoise = int.Parse(V()); break;
                        case "--sg-reuse": a.Opt.SgReuse = V() != "root"; break;
                        case "--sg-reserve": a.Opt.SgReserve = int.Parse(V()); break;
                        case "--sg-reserve-depth": a.Opt.SgReservePerDepth = int.Parse(V()); break;
                        case "--sg-grow": a.Opt.SgGrow = true; break;
                        case "--sg-eval": a.Opt.SgLearned = V() == "learned"; break;
                        case "--eval-weights": a.Opt.Eval = Eval.Load(V()); break;
                        case "--rank-dump": a.RankDump = V(); break;
                        case "--lpb-list": a.LpbList = V(); break;
                        case "--sg-no-grow": a.Opt.SgGrow = false; break;
                        case "--macro-beam": a.Opt.MacroBeamWidth = int.Parse(V()); break;
                        case "--macro-depth": a.Opt.MacroDepth = int.Parse(V()); break;
                        case "--closure-nodes": a.Opt.ClosureNodes = int.Parse(V()); break;
                        case "--closure-depth": a.Opt.ClosureDepth = int.Parse(V()); break;
                        case "--move-only": a.Opt.MoveOnlyK = int.Parse(V()); break;
                        case "--ida-share": a.Opt.IdaShare = double.Parse(V(), CultureInfo.InvariantCulture); break;
                        case "--macro-share": a.Opt.MacroShare = double.Parse(V(), CultureInfo.InvariantCulture); break;
                        case "--order": a.ByNumber = V() == "number"; break;
                        case "--difficulty":
                            foreach (string s in V().Split(',')) a.Difficulty.Add(int.Parse(s));
                            break;
                        case "--force": a.Force = true; break;
                        case "--quiet": a.Quiet = true; break;
                        case "--verbose": a.Verbose = true; break;
                        default:
                            // A bare FILE.lvl is the interactive driver's whole
                            // command line; everything else still needs a flag.
                            if (k.Length > 0 && k[0] != '-' && a.Levels == null)
                            {
                                a.Levels = k;
                                a.Auto = true;
                                break;
                            }
                            Usage();
                            return 2;
                    }
                }
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException || ex is FormatException)
            {
                Usage();
                return 2;
            }
            if (a.Levels == null || !File.Exists(a.Levels))
            {
                Console.Error.WriteLine("lasertank-solve: need an existing --levels FILE.lvl");
                return 2;
            }

            if (a.RankDump != null) return RankDumpAll(a);
            if (a.Auto) return Auto.Run(a);

            string ghsPath = Path.ChangeExtension(a.Levels, ".ghs");
            string collection = Path.GetFileNameWithoutExtension(a.Levels);
            string outDir = Path.Combine(a.Out, collection);
            Directory.CreateDirectory(outDir);

            List<Job> jobs = Plan(a, ghsPath, outDir);
            if (jobs.Count == 0)
            {
                Console.WriteLine("nothing to do (all solved? use --force)");
                return 0;
            }

            object gate = new object();
            StreamWriter report = a.Report == null ? null
                : new StreamWriter(a.Report, append: true, new UTF8Encoding(false));
            List<Summary.Row> rows = new List<Summary.Row>(jobs.Count);
            Progress bar = new Progress(collection, jobs.Count);
            int done = 0, solved = 0;
            DateTime t0 = DateTime.UtcNow;

            if (!a.Quiet)
                Console.WriteLine("{0}: {1} levels, {2} workers, {3} ms + {4} nodes each, beam {5}{6}",
                    Ansi.Bold(collection), jobs.Count, a.Jobs, a.Opt.TimeBudgetMs,
                    a.Opt.NodeBudget, a.Opt.BeamWidth,
                    a.Opt.RunMacro ? ", macro beam " + a.Opt.MacroBeamWidth
                                     + " x closure " + a.Opt.ClosureNodes : "");

            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = a.Jobs }, job =>
            {
                Outcome o = SolveOne(a, job, Clone(a.Opt));
                lock (gate)
                {
                    done++;
                    if (o.Solved) solved++;
                    rows.Add(o.Row());
                    if (report != null) { report.WriteLine(o.Json(collection)); report.Flush(); }
                    if (a.Verbose) { bar.Clear(); Console.WriteLine(o.Line()); }
                    else if (o.Error != null) { bar.Clear(); Console.WriteLine(o.Line()); }
                    if (!a.Quiet) bar.Paint(done, solved, "lv " + job.Level + " " + job.Name);
                }
            });

            bar.Paint(done, solved, "done", force: true);
            bar.Clear();
            report?.Dispose();
            Summary.Print(collection, rows, (DateTime.UtcNow - t0).TotalSeconds, outDir);
            return 0;
        }

        // ---- planning ------------------------------------------------------

        internal sealed class Job
        {
            public int Level;
            public string Name = "", Author = "";
            public int Diff;
            public int GhsMoves, GhsShots;
            public string LpbPath;
        }

        private static List<Job> Plan(Args a, string ghsPath, string outDir)
        {
            int n = LevelFile.CountLevels(a.Levels);
            List<Job> jobs = new List<Job>();
            int to = Math.Min(a.To, n);
            for (int lv = Math.Max(1, a.From); lv <= to; lv++)
            {
                TLEVEL info = LevelFile.ReadLevel(a.Levels, lv);
                if (info == null) break;
                if (a.Stride > 1 && (lv - 1) % a.Stride != 0) continue;
                if (a.Only != null && !a.Only.Contains(lv)) continue;
                if (a.Difficulty.Count > 0 && !a.Difficulty.Contains(info.SDiff)) continue;

                string lpb = Path.Combine(outDir, string.Format("{0:D5}.lpb", lv));
                if (!a.Force && File.Exists(lpb)) continue;

                LevelFile.ReadHighScore(ghsPath, lv, out ushort gm, out ushort gs);
                jobs.Add(new Job
                {
                    Level = lv,
                    Name = info.LName,
                    Author = info.Author,
                    Diff = info.SDiff,
                    GhsMoves = gm,
                    GhsShots = gs,
                    LpbPath = lpb,
                });
            }

            if (!a.ByNumber)
                jobs.Sort(static (p, q) =>
                {
                    int cp = Cost(p), cq = Cost(q);
                    return cp != cq ? cp - cq : p.Level - q.Level;
                });
            if (jobs.Count > a.Limit) jobs.RemoveRange(a.Limit, jobs.Count - a.Limit);
            return jobs;

            // A .ghs of 0 means "no target recorded"; 65500 is RecMax, the
            // recording cap, and appears exactly once in the whole corpus
            // (Special-I level 304, "Theoretical Max").  Both sort last.
            static int Cost(Job j)
            {
                int t = j.GhsMoves + j.GhsShots;
                return t <= 0 || j.GhsMoves >= 65500 ? int.MaxValue : t;
            }
        }

        // ---- one level -----------------------------------------------------

        internal sealed class Outcome
        {
            public Job J;
            public bool Solved, Trimmed;
            public int Keys, Moves, Shots, RawKeys, Depth, Restarts;
            public string Method = "-", Stop = "-";
            public long Nodes;
            public double Ms;
            public string Error;

            public double Ratio
            {
                get
                {
                    int target = J.GhsMoves + J.GhsShots;
                    return target > 0 && target < 65500 ? (double)Keys / target : 0.0;
                }
            }

            public string Line() => string.Format(CultureInfo.InvariantCulture,
                "{0,-8} lv={1,-5} d={2,-2} keys={3,-5} ({4}m/{5}s) ghs={6}/{7} ratio={8,5:F1} "
                + "{9,-5} {10,-14} {11:F0}ms  {12}",
                Solved ? "SOLVED" : (Error != null ? "ERROR" : "unsolved"),
                J.Level, J.Diff, Solved ? Keys : 0, Moves, Shots, J.GhsMoves, J.GhsShots,
                Ratio, Method, Stop, Ms, Error ?? J.Name);

            public Summary.Row Row() => new Summary.Row
            {
                Level = J.Level,
                Difficulty = J.Diff,
                Keys = Keys,
                Target = J.GhsMoves + J.GhsShots >= 65500 ? 0 : J.GhsMoves + J.GhsShots,
                Solved = Solved,
                Trimmed = Trimmed,
                Method = Method,
                Stop = Error != null ? "error" : Stop,
                Error = Error,
            };

            public string Json(string collection)
            {
                using MemoryStream ms = new MemoryStream();
                using (Utf8JsonWriter w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteString("collection", collection);
                    w.WriteNumber("level", J.Level);
                    w.WriteString("name", J.Name);
                    w.WriteNumber("difficulty", J.Diff);
                    w.WriteBoolean("solved", Solved);
                    w.WriteNumber("keys", Keys);
                    w.WriteNumber("raw_keys", RawKeys);
                    w.WriteNumber("moves", Moves);
                    w.WriteNumber("shots", Shots);
                    w.WriteNumber("ghs_moves", J.GhsMoves);
                    w.WriteNumber("ghs_shots", J.GhsShots);
                    w.WriteNumber("ratio", Math.Round(Ratio, 3));
                    w.WriteBoolean("trimmed", Trimmed);
                    w.WriteString("method", Method);
                    w.WriteString("stop", Stop);
                    w.WriteNumber("depth", Depth);
                    w.WriteNumber("restarts", Restarts);
                    w.WriteNumber("nodes", Nodes);
                    w.WriteNumber("ms", Math.Round(Ms, 1));
                    if (Error != null) w.WriteString("error", Error);
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        internal static Outcome SolveOne(Args a, Job job, SolveOptions opt)
        {
            // The caller owns the SolveOptions and must not share one between
            // workers: Solve() clamps IdaMaxDepth in place, and two threads
            // sharing an options object would race.
            Outcome o = new Outcome { J = job };
            try
            {
                Solver s = new Solver(a.Levels, opt);
                SolveResult r = s.Solve(job.Level);
                o.Method = r.Method;
                o.Stop = r.Stop;
                o.Restarts = r.Restarts;
                o.Nodes = r.Nodes;
                o.Ms = r.Ms;
                o.Depth = r.Depth;
                if (!r.Solved) return o;

                byte[] keys = r.Keys;
                o.RawKeys = keys.Length;

                int target = job.GhsMoves + job.GhsShots;
                if (a.TrimRatio > 0 && target > 0 && target < 65500
                    && keys.Length > a.TrimRatio * target)
                {
                    byte[] shorter = Trim.Shrink(a.Levels, job.Level, keys, opt.TickCap,
                                                 (int)(a.TrimRatio * target), 4000, out _);
                    if (shorter.Length < keys.Length) { keys = shorter; o.Trimmed = true; }
                }

                // Never write a .lpb we have not replayed ourselves.
                Engine check = new Engine();
                if (!Trim.Wins(check, a.Levels, job.Level, keys, opt.TickCap,
                               out int mv, out int sh))
                {
                    o.Error = "solution failed its own replay -- not written";
                    return o;
                }

                LevelFile.WritePlayback(job.LpbPath, job.Name, a.Author, job.Level, keys);
                o.Solved = true;
                o.Keys = keys.Length;
                o.Moves = mv;
                o.Shots = sh;
            }
            catch (Exception ex)
            {
                o.Error = ex.GetType().Name + ": " + ex.Message;
            }
            return o;
        }

        // ---- layer 4's instrument -------------------------------------------

        /// Replay every .lpb in --lpb-list and dump the ranking groups.
        ///
        /// Not a solve, so it shares none of the batch harness: no budget, no
        /// .lpb written, no report row.  What it produces is a TSV that
        /// tools/fit_eval.py reads twice -- once for the distribution that says
        /// whether ranking is the lever at all, and once to fit weights if it is.
        ///
        /// The level number comes from the .lpb header, so the pairing is the
        /// recording's own claim about which level it plays rather than
        /// something inferred from a filename.
        private static int RankDumpAll(Args a)
        {
            if (a.LpbList == null || !File.Exists(a.LpbList))
            {
                Console.Error.WriteLine("lasertank-solve: --rank-dump needs --lpb-list FILE");
                return 2;
            }
            List<string> files = new List<string>();
            foreach (string line in File.ReadAllLines(a.LpbList))
            {
                string t = line.Trim();
                if (t.Length > 0 && t[0] != '#') files.Add(t);
            }

            string collection = Path.GetFileNameWithoutExtension(a.Levels);
            object gate = new object();
            using StreamWriter w = new StreamWriter(a.RankDump, append: true,
                                                    new UTF8Encoding(false));
            int groups = 0, done = 0, skipped = 0;

            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = a.Jobs },
                             path =>
            {
                StringWriter buf = new StringWriter();
                int g = 0;
                string err = null;
                try
                {
                    TRECORDREC rec = LevelFile.ReadPlayback(path, out byte[] keys);
                    Solver s = new Solver(a.Levels, Clone(a.Opt));
                    g = s.RankDump(rec.Level, keys, buf, collection);
                }
                catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }

                lock (gate)
                {
                    done++;
                    groups += g;
                    if (g == 0) skipped++;
                    if (err != null) Console.Error.WriteLine(path + ": " + err);
                    else w.Write(buf.ToString());
                }
            });

            Console.WriteLine("{0}: {1} recordings, {2} with no win, {3} groups",
                              collection, done, skipped, groups);
            return 0;
        }

        internal static SolveOptions Clone(SolveOptions s) => new SolveOptions
        {
            MaxKeys = s.MaxKeys,
            BeamWidth = s.BeamWidth,
            RunMacro = s.RunMacro,
            MacroBeamWidth = s.MacroBeamWidth,
            MacroDepth = s.MacroDepth,
            ClosureNodes = s.ClosureNodes,
            ClosureDepth = s.ClosureDepth,
            MoveOnlyK = s.MoveOnlyK,
            IdaShare = s.IdaShare,
            MacroShare = s.MacroShare,
            CloseOnGenerate = s.CloseOnGenerate,
            MacroLast = s.MacroLast,
            BeamShare = s.BeamShare,
            NodeBudget = s.NodeBudget,
            TimeBudgetMs = s.TimeBudgetMs,
            TickCap = s.TickCap,
            IdaMaxDepth = s.IdaMaxDepth,
            RunIda = s.RunIda,
            RunBeam = s.RunBeam,
            RunSubgoal = s.RunSubgoal,
            SgWidth = s.SgWidth,
            SgDepth = s.SgDepth,
            SgClosureNodes = s.SgClosureNodes,
            SgClosureDepth = s.SgClosureDepth,
            SgCandidates = s.SgCandidates,
            SgFallbackK = s.SgFallbackK,
            SgSlack = s.SgSlack,
            SgStrict = s.SgStrict,
            SgCloseOnExpand = s.SgCloseOnExpand,
            SgAim = s.SgAim,
            SgTrace = s.SgTrace,
            SubgoalShare = s.SubgoalShare,
            SubgoalLast = s.SubgoalLast,
            SgRestarts = s.SgRestarts,
            SgNoise = s.SgNoise,
            SgReuse = s.SgReuse,
            SgReserve = s.SgReserve,
            SgReservePerDepth = s.SgReservePerDepth,
            SgGrow = s.SgGrow,
            SgLearned = s.SgLearned,
            Eval = s.Eval,
        };
    }
}
