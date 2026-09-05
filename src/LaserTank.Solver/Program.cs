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
"usage: lasertank-solve --levels FILE.lvl [--out DIR] [--report FILE.jsonl]\n" +
"\n" +
"  selection\n" +
"    --level N            just this level        --from N / --to N   a range\n" +
"    --difficulty LIST    comma list of 1,2,4,8,16 (Kids..Deadly) or 0 (unrated)\n" +
"    --order ghs|number   solve cheapest-by-.ghs first (default) or in file order\n" +
"    --limit N            stop after N levels attempted\n" +
"    --force              re-solve levels whose .lpb already exists\n" +
"\n" +
"  budget (per level)\n" +
"    --budget-ms N        wall clock, default 4000     --nodes N   macro-steps\n" +
"    --beam N             beam width, default 600      --max-keys N\n" +
"    --ida-depth N        IDA* bound cap, default 24   --no-ida / --no-beam\n" +
"    --jobs N             parallel workers, default = processor count\n" +
"\n" +
"  output\n" +
"    --trim-ratio R       trim a solution longer than R x the .ghs total (10)\n" +
"    --author NAME        .lpb author field, default \"LTSolver\"\n" +
"    --quiet              summary only         --verbose   one line per level\n");
        }

        private sealed class Args
        {
            public string Levels, Out = "solutions", Report;
            public string Author = "LTSolver";
            public int From = 1, To = int.MaxValue, Limit = int.MaxValue;
            public int Jobs = Environment.ProcessorCount;
            public double TrimRatio = 10.0;
            public bool Force, Quiet, Verbose, ByNumber;
            public readonly HashSet<int> Difficulty = new HashSet<int>();
            public readonly SolveOptions Opt = new SolveOptions();
        }

        public static int Main(string[] argv)
        {
            Args a = new Args();
            try
            {
                for (int i = 0; i < argv.Length; i++)
                {
                    string k = argv[i];
                    string V() => argv[++i];
                    switch (k)
                    {
                        case "--levels": a.Levels = V(); break;
                        case "--out": a.Out = V(); break;
                        case "--report": a.Report = V(); break;
                        case "--author": a.Author = V(); break;
                        case "--level": a.From = a.To = int.Parse(V()); break;
                        case "--from": a.From = int.Parse(V()); break;
                        case "--to": a.To = int.Parse(V()); break;
                        case "--limit": a.Limit = int.Parse(V()); break;
                        case "--jobs": a.Jobs = Math.Max(1, int.Parse(V())); break;
                        case "--trim-ratio": a.TrimRatio = double.Parse(V(), CultureInfo.InvariantCulture); break;
                        case "--budget-ms": a.Opt.TimeBudgetMs = int.Parse(V()); break;
                        case "--nodes": a.Opt.NodeBudget = long.Parse(V()); break;
                        case "--beam": a.Opt.BeamWidth = int.Parse(V()); break;
                        case "--max-keys": a.Opt.MaxKeys = int.Parse(V()); break;
                        case "--ida-depth": a.Opt.IdaMaxDepth = int.Parse(V()); break;
                        case "--no-ida": a.Opt.RunIda = false; break;
                        case "--no-beam": a.Opt.RunBeam = false; break;
                        case "--order": a.ByNumber = V() == "number"; break;
                        case "--difficulty":
                            foreach (string s in V().Split(',')) a.Difficulty.Add(int.Parse(s));
                            break;
                        case "--force": a.Force = true; break;
                        case "--quiet": a.Quiet = true; break;
                        case "--verbose": a.Verbose = true; break;
                        default: Usage(); return 2;
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
                : new StreamWriter(a.Report, append: true, Encoding.UTF8);
            List<Summary.Row> rows = new List<Summary.Row>(jobs.Count);
            Progress bar = new Progress(collection, jobs.Count);
            int done = 0, solved = 0;
            DateTime t0 = DateTime.UtcNow;

            if (!a.Quiet)
                Console.WriteLine("{0}: {1} levels, {2} workers, {3} ms + {4} nodes each, beam {5}",
                    Ansi.Bold(collection), jobs.Count, a.Jobs, a.Opt.TimeBudgetMs,
                    a.Opt.NodeBudget, a.Opt.BeamWidth);

            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = a.Jobs }, job =>
            {
                Outcome o = SolveOne(a, job, outDir);
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

        private sealed class Job
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

        private sealed class Outcome
        {
            public Job J;
            public bool Solved, Trimmed;
            public int Keys, Moves, Shots, RawKeys;
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
                    w.WriteNumber("nodes", Nodes);
                    w.WriteNumber("ms", Math.Round(Ms, 1));
                    if (Error != null) w.WriteString("error", Error);
                    w.WriteEndObject();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static Outcome SolveOne(Args a, Job job, string outDir)
        {
            Outcome o = new Outcome { J = job };
            // Each worker gets its own SolveOptions: Solve() clamps IdaMaxDepth
            // in place, and two threads sharing one options object would race.
            SolveOptions opt = Clone(a.Opt);
            try
            {
                Solver s = new Solver(a.Levels, opt);
                SolveResult r = s.Solve(job.Level);
                o.Method = r.Method;
                o.Stop = r.Stop;
                o.Nodes = r.Nodes;
                o.Ms = r.Ms;
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

        private static SolveOptions Clone(SolveOptions s) => new SolveOptions
        {
            MaxKeys = s.MaxKeys,
            BeamWidth = s.BeamWidth,
            NodeBudget = s.NodeBudget,
            TimeBudgetMs = s.TimeBudgetMs,
            TickCap = s.TickCap,
            IdaMaxDepth = s.IdaMaxDepth,
            RunIda = s.RunIda,
            RunBeam = s.RunBeam,
        };
    }
}
