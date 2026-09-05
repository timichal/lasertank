// Layer 4: a learned evaluation, and the instrument that has to justify one.
//
// **The brief is layer 3's result.**  After the restart driver the subgoal pass
// fails on budget 99.7% of the time (3,734 of 3,746): there is exactly one
// failure mode left and it is depth.  Layer 3 also measured *why* more search
// does not fix it -- `added = 0` on nine expansions in ten, so almost every
// frontier node is a Tier 1 slack node picked as the best `--sg-slack` by
// WorkDistance.  That pick is the arbitrary one.  Layer 3 randomised it and
// bought four levels; this layer tries to make it *right* instead.
//
// So layer 4 is a ranking change and nothing else.  Acceptance stays the board
// test layer 2 built -- a successor still survives because a derived obstacle
// got cheaper -- and only the order of what survived is learned.  That keeps
// the separation layers 2 and 3 depend on, and it means a bad model can lose
// levels but can never admit a state the shipped search would have refused.
//
// **What is labelled, and what it is labelled with.**  652 winning
// trajectories through the real engine: 187 human recordings and 465 verified
// solver .lpb.  A trajectory is a keystream, so replaying it one key at a time
// through Engine.ApplyKey gives the exact sequence of states a perfect search
// would have visited -- which turns "which successor should the beam keep?"
// into a supervised question with a known answer.
//
// The label is *not* a cost-to-go regression, and that is a deliberate
// decision rather than a simplification.  Every state on a winning trajectory
// is a good state; a model fit only to those has never seen a bad one and
// cannot be asked to tell them apart.  The beam's actual job is to order a
// group of siblings, so the training datum is a *group*: one subgoal expansion
// from a state on the trajectory, every successor it offered, and which of
// those successors the trajectory in fact went through.  Positives and
// negatives come from the same expansion, so the model is fit to exactly the
// comparison it will be asked to make.
//
// **The instrument runs inside the shipped expansion, not beside it.**  The
// same argument as --sg-trace: a second copy of ExpandSubgoal written to
// observe it would be free to drift from it, and then the distribution would
// be a fact about the copy.  `_collect`, when non-null, makes Offer() record
// every candidate it was handed before deciding anything about it.  The hot
// path pays one null check.
//
// What the instrument is for, before any model exists: **is the successor the
// winner went through even in the group?**  If it usually is and ranks badly,
// ranking is the lever and a learned evaluation is the right tool.  If it is
// usually absent, the closure or the acceptance test is the constraint and no
// ordering can help.  Those are different layers, and one distribution
// separates them.
//
// **The answer, over 20,148 groups from 646 winning trajectories.**  The
// successor the winner used is in the group **97.6%** of the time -- so the
// closure and the acceptance test are not the constraint -- and WorkDistance
// ranks it **100th of a median 395**.  The beam keeps four.  It is therefore
// still on the winner's line one step later 10.0% of the time overall and
// **4.1% on the human recordings**, which are the hard population.
//
// The third number is what ties this to layer 3.  **62.4% of the time the
// winner's successor is a *slack* node** -- the board test does not call it
// progress at all, and it survives only because Slack() kept the best
// --sg-slack of them by WorkDistance.  Layer 3 measured that the search runs on
// slack and randomised that pick; this measures that the pick is usually
// wrong, which is what makes it worth learning rather than jittering.
//
// Fit on those groups (tools/fit_eval.py, a softmax within the group), the
// learned evaluation takes held-out top-4 from 13.6% to 18.2% overall and from
// 5.7% to 10.4% on held-out human recordings.  Over the corpus that is worth
// **+28 levels and none lost**: 444 -> 472 of the 4,185-level stride sample.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LaserTank.Core;

namespace LaserTank.Solver
{
    /// The board features a learned evaluation is fit over.
    ///
    /// All of them are functions of the *state*, never of the path taken to it:
    /// two routes to the same board must score the same, or the beam's closed
    /// set and its ranking disagree about what a state is.  That rules out the
    /// obvious-looking keys-spent and shots-fired, which are properties of the
    /// search rather than of the position.
    ///
    /// Cost matters here in a way it does not for a one-off: this is evaluated
    /// once per surviving successor, i.e. wherever WorkDistance was.  So the
    /// list is confined to what the two existing passes already computed (the
    /// Dijkstra's route and the BFS's component, published by Heuristic as
    /// fields) plus one linear scan of the 256 cells.  Measured, that is about
    /// 1.4x WorkDistance alone -- and a campaign is governed by ApplyKey calls,
    /// which this does not change at all.  The --budget-ms backstop is the one
    /// thing to keep an eye on.
    public static class Feat
    {
        public const int N = 17;

        public static readonly string[] Names =
        {
            "bias", "work", "work_far", "flagdist", "flag_unreach", "manhattan",
            "route_obs", "component", "bricks", "blocks", "mirrors", "antitanks",
            "water", "thin_ice", "block_by_water", "threats", "far_man",
        };

        /// `work` is passed in rather than recomputed: the caller has just paid
        /// for it, and Heuristic's published fields describe *that* call.
        public static void Extract(Engine e, Heuristic h, int work, int[] f)
        {
            // Read before FlagDistance runs: the fields are the property of the
            // last call, and keeping that order true here means a future edit
            // to either pass cannot quietly change what this vector means.
            int routeObs = h.RouteObstacles;
            int flagDist = h.FlagDistance(e);
            int component = h.Component;
            bool reach = h.FlagReachable;

            int fx = -1, fy = -1;
            byte[,] pf = e.Game.PF;
            for (int x = 0; x < 16 && fx < 0; x++)
                for (int y = 0; y < 16; y++)
                    if (pf[x, y] == Obj.Flag) { fx = x; fy = y; break; }

            int bricks = 0, blocks = 0, mirrors = 0, anti = 0, water = 0, thin = 0, byWater = 0;
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    byte c = pf[x, y];
                    switch (c)
                    {
                        case Obj.Bricks: bricks++; break;
                        case Obj.Water: water++; break;
                        case Obj.ThinIce: thin++; break;
                        case Obj.Block:
                            blocks++;
                            // A block that already touches water is one push
                            // from turning that cell into Dirt, which is the
                            // single biggest drop the price list can make.
                            if ((x > 0 && pf[x - 1, y] == Obj.Water)
                                || (x < 15 && pf[x + 1, y] == Obj.Water)
                                || (y > 0 && pf[x, y - 1] == Obj.Water)
                                || (y < 15 && pf[x, y + 1] == Obj.Water)) byWater++;
                            break;
                        default:
                            if (c >= Obj.AntiTankUp && c <= Obj.AntiTankLeft) anti++;
                            else if (c >= Obj.MirrorUL && c <= Obj.MirrorDL) mirrors++;
                            else if (c >= Obj.RotoUL && c <= Obj.RotoDL) mirrors++;
                            break;
                    }
                }

            int tx = e.Game.Tank.X, ty = e.Game.Tank.Y;
            int man = fx < 0 ? 0 : Math.Abs(tx - fx) + Math.Abs(ty - fy);

            // Split "far" from "obstructed", which WorkDistance folds into one
            // number: a forty-step walk and five bricks in a wall both read as
            // 20 and are not the same board.  The unreachable case is a flag
            // rather than a large value, so a linear model is not forced to
            // price 1000 on the same scale as 20.
            bool far = work >= Heuristic.Unreachable;

            f[0] = 1;
            f[1] = far ? 0 : work;
            f[2] = far ? 1 : 0;
            f[3] = reach ? flagDist : 0;
            f[4] = reach ? 0 : 1;
            f[5] = man;
            f[6] = routeObs < 0 ? 0 : routeObs;
            f[7] = component;
            f[8] = bricks;
            f[9] = blocks;
            f[10] = mirrors;
            f[11] = anti;
            f[12] = water;
            f[13] = thin;
            f[14] = byWater;
            f[15] = Threats(pf, tx, ty);
            // The manhattan term WorkDistance itself adds when it has no route.
            // It exists so that the weight vector {work: 1, work_far: 1000,
            // far_man: 1} *is* WorkDistance exactly -- which makes "the learned
            // evaluation with the old numbers reproduces layer 3 to the level"
            // a check that can actually be run, the same way --sg-restarts 0
            // reproduces layer 2.
            f[16] = far ? man : 0;
        }

        /// Anti-tanks with a clear line to the tank, stopped by anything the
        /// laser does not pass through.  Cheaper and stricter than
        /// Heuristic.Threats, which deliberately over-reports because it is
        /// naming targets; here an over-report would be a wrong feature.
        private static int Threats(byte[,] pf, int tx, int ty)
        {
            int n = 0;
            for (int k = 0; k < 4; k++)
            {
                int dx = k == 1 ? 1 : k == 3 ? -1 : 0;
                int dy = k == 0 ? -1 : k == 2 ? 1 : 0;
                for (int x = tx + dx, y = ty + dy;
                     x >= 0 && x < 16 && y >= 0 && y < 16; x += dx, y += dy)
                {
                    byte c = pf[x, y];
                    if (c >= Obj.AntiTankUp && c <= Obj.AntiTankLeft) { n++; break; }
                    if (c == Obj.Dirt || c == Obj.Flag || c == Obj.ThinIce || c == Obj.Ice
                        || (c >= Obj.ConveyorUp && c <= Obj.ConveyorLeft)) continue;
                    break;                                  // opaque to a laser
                }
            }
            return n;
        }
    }

    /// The learned evaluation itself: a linear score over Feat, in fixed point.
    ///
    /// Integer weights at a fixed scale rather than doubles, because the score
    /// is a beam's sort key and two runs of the same configuration have to
    /// produce the same order.  Ties broken by G, exactly as WorkDistance's are.
    public sealed class Eval
    {
        public const int Scale = 1024;

        public readonly int[] W;
        public readonly string Source;

        public Eval(int[] w, string source) { W = w; Source = source; }

        /// The shipped weights, or WorkDistance-equivalent if none were fit yet.
        public static Eval Default() => new Eval(Weights.Default, Weights.Source);

        public static Eval Load(string path)
        {
            int[] w = new int[Feat.N];
            int i = 0;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                // "name value" or bare "value"
                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string v = parts[parts.Length - 1];
                if (i >= Feat.N) throw new IOException(path + ": more than " + Feat.N + " weights");
                w[i++] = (int)Math.Round(double.Parse(v, CultureInfo.InvariantCulture));
            }
            if (i != Feat.N) throw new IOException(path + ": expected " + Feat.N + " weights, got " + i);
            return new Eval(w, Path.GetFileName(path));
        }

        public int Score(int[] f)
        {
            long s = 0;
            for (int i = 0; i < Feat.N; i++) s += (long)W[i] * f[i];
            s /= Scale;
            // The beam sorts ascending and Cut() compares ints, so clamp rather
            // than let a pathological weight set wrap.
            if (s < -1000000) s = -1000000;
            if (s > 1000000) s = 1000000;
            return (int)s;
        }
    }

    /// One candidate the expansion offered, as the instrument sees it.
    public sealed class RankRow
    {
        public ulong Hash;
        public int Work;
        public int Tier;                 // 0 advanced, 1 slack, 2 fallback Goto
        public readonly int[] F = new int[Feat.N];
    }

    public sealed partial class Solver
    {
        /// Non-null only while the instrument is running.  Offer() and
        /// KeepNearestN() append every candidate they are handed; the search
        /// itself is unchanged and still decides what it decides.
        private List<RankRow> _collect;

        /// True while collecting, so a winning ApplyKey inside an expansion
        /// records the win as the best possible candidate and carries on rather
        /// than unwinding: the group at the last parent of a trajectory is the
        /// one with the clearest label in it.
        private bool Collecting => _collect != null;

        private readonly int[] _feat = new int[Feat.N];

        /// The learned evaluation, or null for layer 2's WorkDistance.
        private Eval _eval;

        /// The subgoal beam's ranking key.
        ///
        /// Consulted only *after* Offer() has settled whether a successor
        /// advanced, so this is an ordering and nothing more: a learned
        /// evaluation can move a node up or down the frontier and can never
        /// admit one the shipped acceptance test refused.  That is the same
        /// contract layer 3's jitter has, and for the same reason -- it is what
        /// keeps layers 2 and 3 intact underneath.
        private int Rank(int work)
        {
            if (_eval == null) return work;
            Feat.Extract(_e, _h, work, _feat);
            return _eval.Score(_feat);
        }

        private readonly HashSet<ulong> _collectSeen = new HashSet<ulong>();

        /// One row per *position*, not per way of reaching it.  The expansion
        /// can offer the same board twice -- a shot from two poses that leaves
        /// the same state -- and the beam's Fresh() test collapses those, so a
        /// group that counted them twice would not be the group the beam ranks.
        private void CollectRow(int work, int tier, ulong hash)
        {
            if (!_collectSeen.Add(hash)) return;
            RankRow r = new RankRow { Hash = hash, Work = work, Tier = tier };
            Feat.Extract(_e, _h, work, r.F);
            _collect.Add(r);
        }

        // ---- the instrument -------------------------------------------------

        /// Walk one winning trajectory and dump, per shot boundary, the group of
        /// successors the shipped subgoal expansion offered there and which of
        /// them the winner actually went through.
        ///
        /// Parents are the quiesced state after each fire key (and the root),
        /// which is exactly the shape of state the subgoal beam holds in its
        /// frontier.  Every group is taken from the *trajectory*, not from the
        /// previous group's best successor, so a group where the search would
        /// have gone wrong does not poison the ones after it -- the coverage
        /// number stays a per-expansion fact.
        ///
        /// A successor is on the winner's path when its StateHash equals the
        /// hash of some later trajectory state.  StateHash drops the path and
        /// the counters and keeps the load-bearing staleness, so that is an
        /// identity test on the position rather than on how it was reached --
        /// which is the right test: a successor that reaches the winning
        /// position by a different route is the same successor.
        public int RankDump(int level, byte[] keys, TextWriter w, string collection)
        {
            EngineSnapshot root = Root(level);
            _clock = System.Diagnostics.Stopwatch.StartNew();
            _nodes = 0;
            _stageNodes = long.MaxValue;
            _stageMs = long.MaxValue;
            _sgWidth = _opt.SgWidth;
            _sgSlack = _opt.SgSlack;
            _jitter = 0;

            // Pass one: replay the trajectory, one key at a time, and record the
            // furthest index each position occurs at.
            Dictionary<ulong, int> onPath = new Dictionary<ulong, int>();
            List<int> fireAt = new List<int>();
            List<EngineSnapshot> parents = new List<EngineSnapshot>();

            _e.Restore(root);
            onPath[_e.StateHash()] = 0;
            parents.Add(CopyOf(root));
            fireAt.Add(-1);
            bool won = false;
            for (int i = 0; i < keys.Length; i++)
            {
                StepResult step = _e.ApplyKey(keys[i], _opt.TickCap);
                if (step == StepResult.Win)
                {
                    // The winning position is a label too, and the best one
                    // there is: it is the successor every group before the last
                    // is ultimately trying to reach.
                    onPath[_e.StateHash()] = keys.Length + 1;
                    won = true;
                    break;
                }
                if (step != StepResult.Ok) break;
                onPath[_e.StateHash()] = i + 1;
                if (keys[i] == Fire)
                {
                    parents.Add(_e.Snapshot(Take()));
                    fireAt.Add(i);
                }
            }
            if (!won)
            {
                foreach (EngineSnapshot s in parents) Give(s);
                return 0;                                   // not a winning trajectory
            }

            // Pass two: one group per parent.
            List<RankRow> rows = new List<RankRow>();
            _collect = rows;
            int groups = 0;
            StringBuilder sb = new StringBuilder();

            for (int p = 0; p < parents.Count; p++)
            {
                rows.Clear();
                _collectSeen.Clear();
                HashSet<ulong> seen = new HashSet<ulong>();
                HashSet<ulong> layer = new HashSet<ulong>();
                List<Node> next = new List<Node>();

                _e.Restore(parents[p]);
                FindFlag(out int fx, out int fy);
                seen.Add(_e.StateHash());
                ExpandSubgoal(parents[p], seen, layer, next, fx, fy);
                foreach (Node n in next) Give(n.S);

                int atKey = fireAt[p] + 1;
                int best = -1;
                foreach (RankRow r in rows)
                    if (onPath.TryGetValue(r.Hash, out int idx) && idx > atKey && idx > best)
                        best = idx;

                foreach (RankRow r in rows)
                {
                    int idx = onPath.TryGetValue(r.Hash, out int j) && j > atKey ? j : -1;
                    sb.Clear();
                    sb.Append(collection).Append('\t').Append(level).Append('\t')
                      .Append(p).Append('\t').Append(atKey).Append('\t')
                      .Append(idx).Append('\t').Append(idx >= 0 && idx == best ? 1 : 0)
                      .Append('\t').Append(r.Tier).Append('\t').Append(r.Work);
                    for (int k = 0; k < Feat.N; k++) sb.Append('\t').Append(r.F[k]);
                    w.WriteLine(sb.ToString());
                }
                groups++;
            }

            _collect = null;
            foreach (EngineSnapshot s in parents) Give(s);
            return groups;
        }
    }
}
