// Console output for the batch harness.
//
// A campaign is thousands of levels and tens of minutes, so the default view is
// a single live line -- what it is working on, how much is left, how it is
// doing -- and a summary at the end that answers the question the run was for:
// which tiers fell, how close to the record, and where the rest got stuck.
//
// Everything degrades cleanly: no ANSI when stdout is redirected or NO_COLOR is
// set, and no carriage-return repainting either, so `> log.txt` gives readable
// lines rather than a wall of escape codes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LaserTank.Solver
{
    public static class Ansi
    {
        public static readonly bool On =
            !Console.IsOutputRedirected &&
            Environment.GetEnvironmentVariable("NO_COLOR") == null;

        public static string Dim(string s) => On ? "\u001b[2m" + s + "\u001b[0m" : s;
        public static string Green(string s) => On ? "\u001b[32m" + s + "\u001b[0m" : s;
        public static string Yellow(string s) => On ? "\u001b[33m" + s + "\u001b[0m" : s;
        public static string Red(string s) => On ? "\u001b[31m" + s + "\u001b[0m" : s;
        public static string Bold(string s) => On ? "\u001b[1m" + s + "\u001b[0m" : s;
    }

    /// The live line.  Repaints in place when it can; otherwise prints a plain
    /// line every few hundred levels so a redirected run still shows progress.
    public sealed class Progress
    {
        private readonly string _label;
        private readonly int _total;
        private readonly DateTime _t0 = DateTime.UtcNow;
        private DateTime _lastPaint = DateTime.MinValue;
        private int _width;

        public Progress(string label, int total)
        {
            _label = label;
            _total = total;
        }

        public void Paint(int done, int solved, string current, bool force = false)
        {
            DateTime now = DateTime.UtcNow;
            if (!force && (now - _lastPaint).TotalMilliseconds < 120) return;
            _lastPaint = now;

            double secs = (now - _t0).TotalSeconds;
            double rate = secs > 0 ? done / secs : 0;
            string eta = rate > 0 && done < _total
                ? Span((_total - done) / rate) : "--";

            if (!Ansi.On)
            {
                if (done % 250 == 0 || force)
                    Console.WriteLine("  {0}/{1} attempted, {2} solved, {3:F1}/s, eta {4}",
                                      done, _total, solved, rate, eta);
                return;
            }

            const int cells = 24;
            int filled = _total > 0 ? done * cells / _total : cells;
            string bar = new string('#', filled) + new string('.', cells - filled);
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0} [{1}] {2}/{3}  solved {4} ({5:F1}%)  {6:F1}/s  eta {7}  {8}",
                _label, bar, done, _total, solved,
                done > 0 ? 100.0 * solved / done : 0, rate, eta, current);

            if (line.Length > 118) line = line.Substring(0, 115) + "...";
            Console.Write("\r" + line.PadRight(Math.Max(_width, line.Length)));
            _width = line.Length;
        }

        public void Clear()
        {
            if (Ansi.On && _width > 0) Console.Write("\r" + new string(' ', _width) + "\r");
            _width = 0;
        }

        public static string Span(double seconds)
        {
            if (seconds < 90) return string.Format(CultureInfo.InvariantCulture, "{0:F0}s", seconds);
            if (seconds < 5400) return string.Format(CultureInfo.InvariantCulture,
                "{0:F0}m{1:00}s", Math.Floor(seconds / 60), seconds % 60);
            return string.Format(CultureInfo.InvariantCulture,
                "{0:F0}h{1:00}m", Math.Floor(seconds / 3600), (seconds % 3600) / 60);
        }
    }

    public static class Summary
    {
        private static readonly (int id, string name)[] Tiers =
        {
            (1, "Kids"), (2, "Easy"), (4, "Medium"), (8, "Hard"), (16, "Deadly"), (0, "unrated"),
        };

        /// The end-of-run report.  Three questions, in the order they matter:
        /// which tiers fell, how good the solutions are, and what stopped the
        /// rest -- the last one is what tells you which layer to build next.
        public static void Print(string collection, IReadOnlyList<Row> rows, double wallSecs,
                                 string outDir)
        {
            Console.WriteLine();
            Console.WriteLine(Ansi.Bold(collection) + "  "
                + Ansi.Dim(string.Format(CultureInfo.InvariantCulture,
                    "{0} levels attempted in {1}", rows.Count, Progress.Span(wallSecs))));
            Console.WriteLine();

            Console.WriteLine(Ansi.Dim("  tier      attempted   solved      rate    median ratio"));
            foreach ((int id, string name) in Tiers)
            {
                List<Row> t = rows.Where(r => r.Difficulty == id).ToList();
                if (t.Count == 0) continue;
                List<Row> ok = t.Where(r => r.Solved).ToList();
                string rate = string.Format(CultureInfo.InvariantCulture,
                    "{0,6:F1}%", 100.0 * ok.Count / t.Count);
                Console.WriteLine("  {0,-9} {1,9}   {2,6}   {3}   {4,12}",
                    name, t.Count, ok.Count,
                    ok.Count * 2 >= t.Count ? Ansi.Green(rate)
                        : ok.Count > 0 ? Ansi.Yellow(rate) : Ansi.Red(rate),
                    Ratio(ok));
            }

            List<Row> solved = rows.Where(r => r.Solved).ToList();
            Console.WriteLine();
            if (solved.Count > 0)
            {
                var ratios = solved.Where(r => r.Target > 0)
                                   .Select(r => (double)r.Keys / r.Target).OrderBy(v => v).ToList();
                Console.WriteLine("  solution quality (keypresses / .ghs moves+shots)");
                if (ratios.Count > 0)
                    Console.WriteLine("    p50 {0:F1}x   p90 {1:F1}x   worst {2:F1}x   "
                        + "over 10x: {3}   at or under the record: {4}",
                        Pick(ratios, .5), Pick(ratios, .9), ratios[^1],
                        ratios.Count(v => v > 10), ratios.Count(v => v <= 1.0));
                Console.WriteLine("    trimmed {0}   polished {1}   by method: {2}",
                    solved.Count(r => r.Trimmed),
                    solved.Count(r => r.Polished),
                    Tally(solved.Select(r => r.Method)));
                Console.WriteLine();
            }

            List<Row> stuck = rows.Where(r => !r.Solved).ToList();
            if (stuck.Count > 0)
            {
                Console.WriteLine("  {0} unsolved, by where the search stopped", stuck.Count);
                Console.WriteLine("    " + Tally(stuck.Select(r => r.Stop)));
                int errors = rows.Count(r => r.Error != null);
                if (errors > 0)
                    Console.WriteLine(Ansi.Red("    " + errors + " errored -- see the report"));
                Console.WriteLine();
            }

            Console.WriteLine("  " + Ansi.Dim("solutions -> " + outDir
                + "   verify: python tools/verify_solutions.py " + outDir));
        }

        private static string Ratio(List<Row> ok)
        {
            var r = ok.Where(x => x.Target > 0).Select(x => (double)x.Keys / x.Target)
                      .OrderBy(v => v).ToList();
            return r.Count == 0 ? "-"
                : string.Format(CultureInfo.InvariantCulture, "{0:F1}x", Pick(r, .5));
        }

        private static double Pick(List<double> sorted, double q) =>
            sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];

        private static string Tally(IEnumerable<string> xs)
        {
            var counts = new Dictionary<string, int>();
            foreach (string x in xs) counts[x] = counts.TryGetValue(x, out int c) ? c + 1 : 1;
            StringBuilder sb = new StringBuilder();
            foreach (var kv in counts.OrderByDescending(k => k.Value))
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(kv.Key).Append(' ').Append(kv.Value);
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        /// What the summary needs from an outcome, so Program's Outcome type
        /// does not have to be public.
        public sealed class Row
        {
            public int Level, Difficulty, Keys, Target;
            public bool Solved, Trimmed, Polished;
            public string Method = "-", Stop = "-", Error;
        }
    }
}
