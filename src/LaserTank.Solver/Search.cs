// Layer 0's searchers.  Two of them, run as a small portfolio, because they
// fail in opposite directions:
//
//   Beam    keeps the best `width` states at each depth and never backtracks.
//           Unbounded depth, bounded memory, no optimality claim -- which is
//           exactly the trade PROGRESS's Phase 4 note asks for ("the point is
//           not to find the absolute fastest solution but a viable one").  It
//           is the workhorse: it can produce a 200-keypress solution.
//   IDA*    complete within its bound and finds short solutions the beam's
//           greediness walks past, but its cost is exponential in depth, so it
//           only ever finishes on the shallow end.
//
// Both drive Engine.ApplyKey, i.e. the real tick through the real RecBuffer.
// There is no separate "model of the game" here to drift from the engine --
// that was the point of building the search surface into the engine instead.
//
// Trimming (the ">10x the record, trim it" rule) lives in Trim.cs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public sealed class SolveOptions
    {
        public int MaxKeys = 1200;         // keystream cap; also the depth cap
        public int BeamWidth = 600;
        public long NodeBudget = 400000;   // ApplyKey calls, the unit that costs
        public int TimeBudgetMs = 4000;
        public int TickCap = 100000;       // per macro-step backstop; see ApplyKey
        public int IdaMaxDepth = 24;
        public bool RunIda = true;
        public bool RunBeam = true;
    }

    public sealed class SolveResult
    {
        public bool Solved;
        public byte[] Keys = Array.Empty<byte>();
        public int Moves, Shots;
        public string Method = "-";
        public string Stop = "-";          // why it gave up, when it did
        public long Nodes;
        public double Ms;
        public int Depth;                  // macro-steps in the solution
    }

    public sealed class Solver
    {
        private readonly string _lvlPath;
        private readonly SolveOptions _opt;
        private readonly Engine _e = new Engine();
        private readonly Stack<EngineSnapshot> _pool = new Stack<EngineSnapshot>();
        private readonly Heuristic _h = new Heuristic();
        private long _nodes;
        private Stopwatch _clock;

        public Solver(string lvlPath, SolveOptions opt)
        {
            _lvlPath = lvlPath;
            _opt = opt;
        }

        public TLEVEL Level => _e.CurRecData;

        private EngineSnapshot Take() => _pool.Count > 0 ? _pool.Pop() : new EngineSnapshot();
        private void Give(EngineSnapshot s) { if (_pool.Count < 4096) _pool.Push(s); }

        private bool OutOfBudget =>
            _nodes >= _opt.NodeBudget || _clock.ElapsedMilliseconds >= _opt.TimeBudgetMs;

        /// Fresh engine at the level's start position, configured exactly as the
        /// replay driver configures it.
        private EngineSnapshot Root(int level)
        {
            _e.ConfigureForReplay();
            if (!_e.LoadLevel(_lvlPath, level))
                throw new ArgumentException("no level " + level + " in " + _lvlPath);
            _e.BeginSearch(_opt.MaxKeys);
            // The tick's FindTank/PutLevel pass has not run yet; ApplyKey's
            // first Tick does it, exactly as the driver's first tick does.
            return _e.Snapshot();
        }

        public SolveResult Solve(int level)
        {
            _clock = Stopwatch.StartNew();
            _nodes = 0;
            if (_opt.IdaMaxDepth >= _opt.MaxKeys) _opt.IdaMaxDepth = _opt.MaxKeys - 1;
            EngineSnapshot root = Root(level);
            SolveResult r = new SolveResult();

            if (_opt.RunIda)
            {
                r = Ida(root);
                if (r.Solved) return Finish(r, "ida");
            }
            if (_opt.RunBeam && !OutOfBudget)
            {
                SolveResult b = Beam(root);
                if (b.Solved) return Finish(b, "beam");
                r = b;
            }
            r.Nodes = _nodes;
            r.Ms = _clock.Elapsed.TotalMilliseconds;
            if (r.Stop == "-") r.Stop = OutOfBudget ? "budget" : "exhausted";
            return r;
        }

        private SolveResult Finish(SolveResult r, string method)
        {
            r.Method = method;
            r.Nodes = _nodes;
            r.Ms = _clock.Elapsed.TotalMilliseconds;
            return r;
        }

        // ---- beam ----------------------------------------------------------

        private sealed class Node
        {
            public EngineSnapshot S;
            public int G;                  // keypresses spent
            public int H;
        }

        private SolveResult Beam(EngineSnapshot root)
        {
            SolveResult r = new SolveResult();
            HashSet<ulong> seen = new HashSet<ulong>();

            _e.Restore(root);
            seen.Add(_e.StateHash());
            List<Node> frontier = new List<Node>
            {
                new Node { S = CopyOf(root), G = 0, H = _h.FlagDistance(_e) },
            };
            List<Node> next = new List<Node>();

            for (int depth = 0; depth < _opt.MaxKeys && frontier.Count > 0; depth++)
            {
                foreach (Node parent in frontier)
                {
                    if (OutOfBudget) { r.Stop = "budget"; Recycle(frontier, next); return r; }

                    foreach (byte key in Engine.ActionKeys)
                    {
                        _e.Restore(parent.S);
                        _nodes++;
                        StepResult step = _e.ApplyKey(key, _opt.TickCap);
                        if (step == StepResult.Win)
                        {
                            Recycle(frontier, next);
                            return Won(r);
                        }
                        if (step != StepResult.Ok) continue;      // dead or spinning
                        if (_e.Game.RecP >= (uint)_opt.MaxKeys) continue;
                        if (!seen.Add(_e.StateHash())) continue;

                        next.Add(new Node
                        {
                            S = _e.Snapshot(Take()),
                            G = (int)_e.Game.RecP,
                            H = _h.FlagDistance(_e),
                        });
                    }
                }

                // Rank by heuristic first, then by how cheaply we got here.  No
                // optimality claim -- this is the "any valid solution" contract.
                next.Sort(static (a, b) => a.H != b.H ? a.H - b.H : a.G - b.G);
                if (next.Count > _opt.BeamWidth)
                {
                    for (int i = _opt.BeamWidth; i < next.Count; i++) Give(next[i].S);
                    next.RemoveRange(_opt.BeamWidth, next.Count - _opt.BeamWidth);
                }

                foreach (Node n in frontier) Give(n.S);
                frontier.Clear();
                (frontier, next) = (next, frontier);
            }

            r.Stop = frontier.Count == 0 ? "beam-dead-end" : "depth";
            Recycle(frontier, next);
            return r;
        }

        private void Recycle(List<Node> a, List<Node> b)
        {
            foreach (Node n in a) Give(n.S);
            foreach (Node n in b) Give(n.S);
            a.Clear();
            b.Clear();
        }

        private EngineSnapshot CopyOf(EngineSnapshot s)
        {
            _e.Restore(s);
            return _e.Snapshot(Take());
        }

        // ---- IDA* ----------------------------------------------------------

        private SolveResult Ida(EngineSnapshot root)
        {
            SolveResult r = new SolveResult();
            _e.Restore(root);
            int bound = _h.FlagDistance(_e);
            if (bound >= Heuristic.Unreachable) { r.Stop = "ida-no-route"; return r; }

            EngineSnapshot[] stack = new EngineSnapshot[_opt.IdaMaxDepth + 1];
            for (int i = 0; i < stack.Length; i++) stack[i] = new EngineSnapshot();

            while (bound <= _opt.IdaMaxDepth)
            {
                if (OutOfBudget) { r.Stop = "budget"; return r; }
                Dictionary<ulong, int> seen = new Dictionary<ulong, int>();
                _e.Restore(root);
                int nextBound = int.MaxValue;
                if (Dfs(root, 0, bound, stack, seen, ref nextBound)) return Won(r);
                if (nextBound == int.MaxValue) { r.Stop = "ida-exhausted"; return r; }
                bound = nextBound;
            }
            r.Stop = "ida-depth";
            return r;
        }

        private bool Dfs(EngineSnapshot at, int g, int bound, EngineSnapshot[] stack,
                         Dictionary<ulong, int> seen, ref int nextBound)
        {
            if (OutOfBudget) return false;

            foreach (byte key in Engine.ActionKeys)
            {
                _e.Restore(at);
                _nodes++;
                StepResult step = _e.ApplyKey(key, _opt.TickCap);
                if (step == StepResult.Win) return true;
                if (step != StepResult.Ok) continue;

                int ng = (int)_e.Game.RecP;
                int h = _h.FlagDistance(_e);
                int f = ng + h;
                if (f > bound) { if (f < nextBound) nextBound = f; continue; }
                if (ng >= stack.Length) continue;

                ulong hash = _e.StateHash();
                if (seen.TryGetValue(hash, out int best) && best <= ng) continue;
                seen[hash] = ng;

                _e.Snapshot(stack[ng]);
                if (Dfs(stack[ng], ng, bound, stack, seen, ref nextBound)) return true;
                if (OutOfBudget) return false;
            }
            return false;
        }

        /// The engine is standing on the flag: its RecBuffer prefix is the
        /// solution, and its own counters are the score.
        private SolveResult Won(SolveResult r)
        {
            r.Solved = true;
            r.Keys = _e.PathKeys();
            r.Moves = _e.Game.ScoreMove;
            r.Shots = _e.Game.ScoreShot;
            r.Depth = r.Keys.Length;
            r.Stop = "win";
            return r;
        }
    }
}
