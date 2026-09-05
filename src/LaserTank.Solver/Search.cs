// The portfolio.  Three searchers now, run in order, because they fail in
// different directions.  Layer 1's macro beam is the third and lives in
// Macro.cs; the two here are layer 0's and are still the workhorses:
//
//   Beam    keeps the best `width` states at each depth and never backtracks.
//           Unbounded depth, bounded memory, no optimality claim -- which is
//           exactly the trade PROGRESS's Phase 4 note asks for ("the point is
//           not to find the absolute fastest solution but a viable one").  It
//           is the workhorse: it can produce a 200-keypress solution.
//   IDA*    complete within its bound and finds short solutions the beam's
//           greediness walks past, but its cost is exponential in depth, so it
//           only ever finishes on the shallow end.
//   Macro   (Macro.cs) searches Goto + Shoot instead of keypresses, so its
//           depth is the number of shots.  It wins on shallow, branchy levels
//           and loses on deep ones -- the measurement, and why, is in
//           PROGRESS.md's layer 1 section.  --macro-share splits the budget.
//
// All three drive Engine.ApplyKey, i.e. the real tick through the real
// RecBuffer.  There is no separate "model of the game" anywhere in the solver
// to drift from the engine -- that was the point of building the search
// surface into the engine instead.
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
        // ApplyKey calls -- the unit that actually costs, and the one a
        // *campaign* should be governed by, because seconds are not
        // reproducible on a machine that is doing anything else
        // (tools/campaign.sh passes an explicit --nodes and a large
        // --budget-ms for exactly that reason).
        //
        // The default is large so that a one-off interactive solve is governed
        // by the wall clock instead, which is the useful behaviour there.  It
        // is 5x layer 0's 400,000 because that figure was being spent in 1.2 s
        // of a 4 s budget: levels reported "budget" with two thirds of their
        // time unused.
        public long NodeBudget = 2000000;
        public int TimeBudgetMs = 4000;
        public int TickCap = 100000;       // per macro-step backstop; see ApplyKey
        public int IdaMaxDepth = 24;
        public bool RunIda = true;
        public bool RunBeam = true;

        // ---- layer 1: macro-actions (Macro.cs) ----------------------------
        //
        // **Off by default, and that is a measured decision rather than a
        // retreat.**  The macro beam wins decisively on levels the raw beam
        // cannot solve -- 18 -> 28, 23 -> 31, 33 -> 38 of 60, at 150k / 400k /
        // 1M nodes -- and loses over the corpus as a whole, three ways:
        //
        //     run first, a tenth of the budget    395 -> 381   (+21, -35)
        //     run last, beam capped at 0.6        395 -> 354   (+23, -64)
        //     off                                 395
        //
        // on 4,185 levels at 150k nodes each.  The reason is the same in both
        // directions: most solvable levels are ones the raw beam gets easily,
        // and every node the macro beam spends is a node taken from it.  A
        // portfolio has to make that bet on every level in advance and there is
        // no ordering or share that wins it.
        //
        // So layer 1 ships as a **second pass** instead (tools/second_pass.sh):
        // the first pass identifies the levels the beam failed, the second
        // attacks only those, and neither pays for the other.  --macro-first
        // is how that pass turns it on.
        public bool RunMacro = false;
        public int MacroBeamWidth = 24;    // macro-nodes kept per shot-depth
        public int MacroDepth = 128;       // shots in a solution, not keypresses
        public int ClosureNodes = 1500;    // states in one Goto closure
        public int ClosureDepth = 40;      // movement keys in one Goto
        public int MoveOnlyK = 6;          // pure-Goto successors kept per node

        // ---- layer 2: subgoal decomposition (Subgoal.cs) ------------------
        //
        // Same shape of decision as layer 1's: off by default, and the campaign
        // measures it as a second pass as well as a portfolio member.  What is
        // different is where the leverage comes from -- not a bigger action set
        // but a *derived* one: the priced route's obstacles are the subgoals,
        // and a successor is kept because it made one of them cheaper.
        public bool RunSubgoal = false;
        public int SgWidth = 4;            // subgoal-steps kept per depth
        public int SgDepth = 400;          // subgoal steps; a backstop, since the
                                           // node budget binds long before this
        public int SgClosureNodes = 400;   // states in one movement closure
        public int SgClosureDepth = 32;    // movement keys in one closure
        public int SgCandidates = 64;      // frontier obstacles treated as targets
        public int SgFallbackK = 2;        // pure-Goto states kept when a
                                           // truncated closure advanced nothing
        public int SgSlack = 4;            // board-changing successors kept per
                                           // expansion when nothing advanced
        public bool SgStrict = false;      // accept only on a cleared target

        // Off, and measured that way.  The idea is a superset -- cast the
        // tank's facing ray and fire only when it meets a target, a mirror or
        // an anti-tank -- but on 50 deep levels it takes the subgoal beam from
        // 6 solved to 2.  It is not in fact a superset of the *useful* shots:
        // firing at a brick or block that is not itself a target rearranges the
        // board in ways the next step needs, and a shot that only makes an
        // anti-tank turn is sometimes the whole trick.  Kept as a flag with the
        // number attached so it is not re-invented.
        public bool SgAim = false;         // fire only from plausibly-aiming poses

        /// Close a subgoal-beam state only when it is expanded, rather than
        /// when it is generated.  The opposite of layer 0's measured default,
        /// and measured separately here because the two searches are not alike:
        /// layer 0's beam is 600 wide and over-pruning buys it depth, while the
        /// subgoal beam is 4 wide and dies of an empty frontier.
        public bool SgCloseOnExpand = true;
        public bool SgTrace = false;       // per-expansion diagnostics to stderr

        // ---- layer 3: restarts (Restart.cs) -------------------------------
        //
        // **A restart is spending budget that was already forfeit, which is why
        // this one is on by default and layers 1 and 2 are not.**  Layer 2's
        // failures split 80.8% `budget` / 19.1% `subgoal-dead-end`, and the
        // dead-ends died with a median of 84% of their nodes unspent -- 90
        // million ApplyKey calls across one pass.  SubgoalSearch re-runs the
        // beam only on that stop reason and only while budget remains, so
        // attempt 0 is layer 2 exactly and nothing a level would have solved
        // can be taken away.  Restart.cs has the measurement that shaped it.
        // **What actually recovers a dead-end is width, not randomness, and the
        // two directions of that are the measurement worth keeping.**  A wider
        // subgoal beam from the start is a *loss* -- on the 50 deep levels,
        // width 4 solves 10, width 8 solves 8 and width 16 solves 6 -- because
        // narrow-and-deep is what buys the depth this layer exists for.  The
        // same width bought only after narrow has provably failed is a *win*:
        // 10, and 24 -> 27 on the 60 bench-1 levels.  So SgGrow doubles width
        // and slack per restart (4 -> 8 -> 16 ... capped at 64/32) and the
        // start stays narrow.
        public int SgRestarts = 6;         // extra attempts after a dead-end
        public int SgNoise = 3;            // ranking jitter on a restart; the
                                           // frontier of a dead-ending run is
                                           // almost all slack, so this is the
                                           // diversifier that acts (bench only:
                                           // 26 -> 28 on bench-1, untested on
                                           // the corpus)
        public bool SgGrow = true;         // double width and slack per restart

        // **Restarting from the nodes the width trim discarded loses to
        // restarting from the root, which inverts the design prior and is why
        // the flag survives with its numbers.**  Re-seeding from the reserve is
        // strictly cheaper -- it skips re-deriving the shallow part -- but it
        // also inherits every commitment the beam had already made, and a
        // grown beam wants to re-take those decisions wider.  Over layer 0's
        // 3,790 failures: root 44, reserve 43; on bench-1, 28 against 27.  Off,
        // so nothing is harvested and no snapshots are held.
        public bool SgReuse = false;       // restart from discarded nodes
        public int SgReserve = 64;         // discarded nodes held for a restart
        public int SgReservePerDepth = 2;  // ...and how many one depth may add
        public double SubgoalShare = 0.9;
        public bool SubgoalLast = true;

        // Where each stage must stop, as a *cumulative* fraction of the
        // level's budget -- IDA* until 0.2 of it, the macro beam until 0.3, the
        // raw beam to the end.  Without them one stage eats the level: IDA* on
        // a level it cannot finish spends the whole budget and the macro beam
        // never runs.
        //
        // 0.3 for the macro beam is measured, not guessed, and the shape of the
        // measurement is the point: the macro beam is worth most as a *cheap
        // first probe*, not as the main search.  On 60 Beginner-I levels layer 0
        // failed, letting it run to 0.9 of the budget solves 36; to 0.5, 36; to
        // 0.3 -- i.e. giving it a tenth of the budget after IDA*'s fifth --
        // solves **38**, against layer 0's 33.  On 50 deep levels (.ghs total
        // 40-150) a 0.9 share *costs* a level (12 against 13) while 0.5 and 0.3
        // cost nothing.  Cheap and early wins twice.
        public double IdaShare = 0.2;
        public double MacroShare = 0.3;
        public double BeamShare = 0.6;    // when the macro beam runs last

        /// When a beam closes a state: at generation, or only when the state
        /// survives the width trim and is actually expanded.  See Close().
        public bool CloseOnGenerate = true;

        /// Run the macro beam last, as a fallback for what the raw beam could
        /// not solve, rather than first as a cheap probe.  See Solve().
        public bool MacroLast = true;
    }

    public sealed class SolveResult
    {
        public bool Solved;
        public byte[] Keys = Array.Empty<byte>();
        public int Moves, Shots;
        public string Method = "-";
        public string Stop = "-";          // why it gave up, when it did
        public int Restarts;               // layer 3: extra attempts spent
        public long Nodes;
        public double Ms;
        public int Depth;                  // keypresses in the winning path
    }

    public sealed partial class Solver
    {
        private readonly string _lvlPath;
        private readonly SolveOptions _opt;
        private readonly Engine _e = new Engine();
        private readonly Stack<EngineSnapshot> _pool = new Stack<EngineSnapshot>();
        private readonly Heuristic _h = new Heuristic();
        private long _nodes;
        private Stopwatch _clock;
        private long _stageMs, _stageNodes;

        public Solver(string lvlPath, SolveOptions opt)
        {
            _lvlPath = lvlPath;
            _opt = opt;
        }

        public TLEVEL Level => _e.CurRecData;

        private EngineSnapshot Take() => _pool.Count > 0 ? _pool.Pop() : new EngineSnapshot();
        private void Give(EngineSnapshot s) { if (_pool.Count < 4096) _pool.Push(s); }

        /// Budget is asked about once per expanded node, so it is two integer
        /// compares against the *stage's* share rather than the run's total.
        private bool OutOfBudget =>
            _nodes >= _stageNodes || _clock.ElapsedMilliseconds >= _stageMs;

        /// Give the next stage everything up to `share` of the level's budget.
        /// Cumulative, not per-stage: a cheap IDA* that gave up after 50 ms
        /// hands the rest on rather than capping the macro beam at 90% of what
        /// is left.
        private void Stage(double share)
        {
            _stageMs = (long)(_opt.TimeBudgetMs * share);
            _stageNodes = (long)(_opt.NodeBudget * share);
        }

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
                Stage(_opt.IdaShare);
                r = Ida(root);
                if (r.Solved) return Finish(r, "ida");
            }
            // Order matters more than the shares do, and it took the campaign
            // to see why.  The tuning bench ran the macro beam *first*, as a
            // cheap probe, and measured a win -- but it was benched on levels
            // layer 0 had already failed.  Over the whole corpus most solvable
            // levels are ones the raw beam gets easily, and there a probe that
            // spends a tenth of the budget before the beam starts is a pure
            // tax: the first campaign lost 35 levels to gain 21.
            //
            // Run last, the macro beam only ever sees levels the raw beam
            // could not solve -- which is exactly the population it was
            // benched on -- and it costs the beam nothing.  MacroLast=false
            // restores the probe order.
            if (_opt.RunMacro && !_opt.MacroLast)
            {
                Stage(_opt.MacroShare);
                if (!OutOfBudget)
                {
                    SolveResult m = MacroBeam(root);
                    if (m.Solved) return Finish(m, "macro");
                    r = m;
                }
            }
            if (_opt.RunSubgoal && !_opt.SubgoalLast)
            {
                Stage(_opt.SubgoalShare);
                if (!OutOfBudget)
                {
                    SolveResult g = SubgoalSearch(root);
                    if (g.Solved) return Finish(g, "subgoal");
                    r = g;
                }
            }
            if (_opt.RunBeam)
            {
                Stage((_opt.RunMacro && _opt.MacroLast) || (_opt.RunSubgoal && _opt.SubgoalLast)
                      ? _opt.BeamShare : 1.0);
                if (!OutOfBudget)
                {
                    SolveResult b = Beam(root);
                    if (b.Solved) return Finish(b, "beam");
                    r = b;
                }
            }
            if (_opt.RunMacro && _opt.MacroLast)
            {
                Stage(_opt.RunSubgoal && _opt.SubgoalLast ? _opt.SubgoalShare : 1.0);
                if (!OutOfBudget)
                {
                    SolveResult m = MacroBeam(root);
                    if (m.Solved) return Finish(m, "macro");
                    r = m;
                }
            }
            if (_opt.RunSubgoal && _opt.SubgoalLast)
            {
                Stage(1.0);
                if (!OutOfBudget)
                {
                    SolveResult g = SubgoalSearch(root);
                    if (g.Solved) return Finish(g, "subgoal");
                    r = g;
                }
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
            public ulong Hash;             // StateHash(S), for the closed set

            /// 0 for a successor that advanced, 1 for one that is only there so
            /// the search has somewhere to go (layer 2's slack -- see
            /// Subgoal.cs).  Cut() sorts on it first, so slack never displaces
            /// progress; it only fills the width that progress left empty.
            /// Layer 0's beam and layer 1's macro beam leave it at 0 and are
            /// unaffected.
            public int Tier;
        }

        /// When a beam closes a state -- and it is a policy, not a bug, which
        /// is worth saying because it looks like one.
        ///
        /// `CloseOnGenerate` (the default) marks every successor the moment it
        /// is generated, so a state the width trim throws away is closed
        /// forever and no later depth can regenerate it.  That prunes far more
        /// than the width alone suggests, and it looks wrong: layer 1's macro
        /// beam ends 33 of 60 bench levels at `macro-dead-end`, the frontier
        /// having emptied because everything reachable was marked and binned.
        ///
        /// The other policy is to close a state only when it survives the trim
        /// and is actually expanded, which is what `layer` is for -- it
        /// deduplicates within one depth (where a duplicate really is the same
        /// state and the first copy is the cheaper one) while leaving a
        /// discarded state free to come back later.
        ///
        /// **Measured, both ways, at an equal node budget** on 60 Beginner-I
        /// levels layer 0 could not solve: closing on generate solves 33,
        /// closing on expand solves 27.  Over-pruning wins, because the budget
        /// is nodes and the cheaper policy spends them on depth instead of on
        /// re-deriving positions it has already rejected.  So the flag exists,
        /// the default is the measured winner, and the `macro-dead-end` count
        /// is a *symptom of the policy*, not evidence of a leak.
        private bool Fresh(ulong h, HashSet<ulong> seen, HashSet<ulong> layer) =>
            Fresh(h, seen, layer, _opt.CloseOnGenerate);

        private static bool Fresh(ulong h, HashSet<ulong> seen, HashSet<ulong> layer,
                                  bool closeOnGenerate) =>
            closeOnGenerate ? seen.Add(h) : !seen.Contains(h) && layer.Add(h);

        private static void Close(List<Node> frontier, HashSet<ulong> seen)
        {
            foreach (Node n in frontier) seen.Add(n.Hash);
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
            HashSet<ulong> layer = new HashSet<ulong>();

            for (int depth = 0; depth < _opt.MaxKeys && frontier.Count > 0; depth++)
            {
                layer.Clear();
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
                        ulong h = _e.StateHash();
                        if (!Fresh(h, seen, layer)) continue;

                        next.Add(new Node
                        {
                            S = _e.Snapshot(Take()),
                            G = (int)_e.Game.RecP,
                            H = _h.FlagDistance(_e),
                            Hash = h,
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
                Close(frontier, seen);
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
