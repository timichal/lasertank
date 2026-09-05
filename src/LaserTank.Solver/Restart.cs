// Layer 3: restarts.
//
// **The brief is layer 2's failure mode, not its rate.**  Layer 0's unsolved
// levels stop on budget 95.9% of the time -- a search that never gets near the
// end of a route, which is a depth problem and is what layers 1 and 2 were for.
// Layer 2's stop on budget 80.8% of the time and at `subgoal-dead-end` 19.1%,
// and that second number is a different failure with a different fix: a
// frontier that *emptied*, not a clock that ran out.  Measured over layer 2's
// pass on layer 0's 3,790 failures: 717 levels dead-ended, and they did it with
// a median of **84% of their node budget unspent** -- 507 of them below a third
// of it.  That is roughly 90 million ApplyKey calls the pass paid for and threw
// away.  Restarts are how you spend them.
//
// **What a dead-end actually looks like, because it decided the design.**
// --sg-trace over six of them (Beginner-I 101, 126, 611, 1131, 1206, 1226,
// 1856):
//
//     53-357 expansions, closure size p50 = 8 to 18 states, and
//     added = 0 on 81-99% of expansions, slack = 0 on 33-64% of them.
//
// Three things follow, and each one killed a plausible design:
//
//   * **The closure cap is not the problem.**  A tank that is boxed in reaches
//     eight states, not the 400 it is allowed.  So randomising *which* states a
//     truncated closure keeps -- the obvious diversifier, and the one that
//     matters in layer 1 -- buys almost nothing here.  It is still done
//     (ShuffleKeys), because it costs three lines and levels like 101 do
//     truncate at 403, but it is not the lever.
//   * **The search is running on slack.**  With `added = 0` on nine expansions
//     in ten, essentially every frontier node is a Tier 1 slack node, chosen as
//     the best SgSlack by WorkDistance.  *That* choice is the arbitrary one, so
//     that is where the noise goes: SgNoise jitters the ranking key after
//     acceptance has been decided, so a restart keeps a different handful and
//     never a wrong one.  Acceptance stays a board test; only the ordering is
//     randomised.
//   * **The frontier emptied because the run closed everything it saw.**  A
//     restart from the root re-derives all of it before it can reach new
//     ground.  So the default policy does not restart from the root: it keeps
//     the nodes the width trim discarded and restarts from *those*.
//
// **The reserve.**  Cut() sorts a depth's successors and throws away everything
// past the width; the best few of those are, by construction, the lines this
// beam declined rather than lines it refuted.  SgReservePerDepth of them are
// kept per depth in a bounded pool (SgReserve, random eviction when full), and
// a restart re-seeds its frontier with a random SgWidth-sized sample, removing
// them so successive restarts get different ones.  The cost is a bounded number
// of live snapshots; the saving is the whole re-derivation.  A node's keystream
// prefix travels with it (EngineSnapshot.Keys), so a win out of a re-seeded
// frontier is a complete solution from the level start like any other -- and it
// is replayed before it is written, like any other.
//
// **Restarts are strictly additive, which is what makes this layer different
// from the two below it.**  Attempt 0 is layer 2 exactly: same key ordering, no
// jitter, no re-seed.  A restart happens only when that attempt dies at a
// dead-end *and* budget remains -- never when it stops on budget, where there
// is nothing left to spend.  So a level layer 2 solves, layer 3 solves
// identically, and a level layer 2 loses on budget it loses in the same place.
// Layers 1 and 2 each had to be measured as a portfolio member and each lost,
// because a portfolio bets on every level in advance; this one only ever spends
// budget that was already forfeit.  It is still a pass rather than a portfolio
// member, for the reason those measurements gave -- but it is a pass that
// cannot tax the one it runs inside.
using System;
using System.Collections.Generic;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public sealed partial class Solver
    {
        /// xorshift32.  Zero is the fixed point, so a seed is never 0 -- and
        /// attempt 0 leaves _jitter at 0 instead, which is what keeps the first
        /// attempt bit-identical to layer 2.
        private uint _rng = 1;
        private int _jitter;

        /// The closure's key order for this expansion.  Shuffled only while a
        /// restart is running; MoveKeys itself is never touched.
        private readonly byte[] _keyOrder = { (byte)Engine.VK_UP, (byte)Engine.VK_RIGHT,
                                              (byte)Engine.VK_DOWN, (byte)Engine.VK_LEFT };

        /// Nodes the width trim discarded, kept for a restart to start from.
        private readonly List<Node> _reserve = new List<Node>();

        /// The subgoal beam's width and slack for the attempt in progress --
        /// fields rather than options because --sg-grow widens them per restart
        /// and one SolveOptions is shared by every level a worker solves.
        private int _sgWidth, _sgSlack;

        private uint NextRand()
        {
            uint x = _rng;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return _rng = x;
        }

        private int Rand(int n) => n <= 1 ? 0 : (int)(NextRand() % (uint)n);

        /// Ranking noise.  Added to a node's H *after* Offer() has decided it
        /// advanced, so it can reorder the frontier and can never widen or
        /// narrow the acceptance test.  0 on attempt 0.
        private int Jitter() => _jitter <= 0 ? 0 : Rand(_jitter + 1);

        private void ShuffleKeys()
        {
            if (_jitter <= 0) return;
            for (int i = 3; i > 0; i--)
            {
                int j = Rand(i + 1);
                (_keyOrder[i], _keyOrder[j]) = (_keyOrder[j], _keyOrder[i]);
            }
        }

        // ---- the restart driver --------------------------------------------

        /// Run the subgoal beam, and re-run it while it keeps dying of an empty
        /// frontier with budget still in hand.
        ///
        /// The stop reason is the gate, and it is the whole control law: only
        /// `subgoal-dead-end` is worth restarting.  A `budget` stop has nothing
        /// left to spend and a `subgoal-depth` stop still had a frontier, so in
        /// both cases the loop exits and the result stands.  OutOfBudget is the
        /// *stage's* budget, so a restart cannot overrun the share the
        /// portfolio gave this searcher.
        private SolveResult SubgoalSearch(EngineSnapshot root)
        {
            _sgWidth = _opt.SgWidth;
            _sgSlack = _opt.SgSlack;
            _rng = 1;
            _jitter = 0;
            DropReserve();                 // a Solver is one level, but Clear()
                                           // alone would strand pooled snapshots

            SolveResult r = SubgoalBeam(root, null);
            int attempts = 0;

            while (!r.Solved && attempts < _opt.SgRestarts
                   && r.Stop == "subgoal-dead-end" && !OutOfBudget)
            {
                attempts++;
                // A seed per attempt rather than a rolling state: two restarts
                // of the same level are then reproducible independently of how
                // many random draws the attempts before them happened to make.
                _rng = (uint)(2654435761u * (uint)attempts) | 1u;
                _jitter = _opt.SgNoise;
                if (_opt.SgGrow)
                {
                    // The frontier emptied, so keep more of it next time.  Both
                    // are capped: an unbounded doubling turns the last restart
                    // into a search that cannot finish a single depth.
                    _sgWidth = Math.Min(_sgWidth * 2, 64);
                    _sgSlack = Math.Min(_sgSlack * 2, 32);
                }

                List<Node> seed = _opt.SgReuse ? DrawSeed() : null;
                r = SubgoalBeam(root, seed);
            }

            r.Restarts = attempts;
            DropReserve();
            return r;
        }

        /// A random _sgWidth-sized sample of the reserve, removed from it.
        ///
        /// Removed rather than copied for two reasons: the next restart then
        /// gets *different* nodes instead of re-trying the same ones, and the
        /// snapshots are handed to the beam, which owns and recycles them like
        /// any other frontier node.  Returns null when the reserve is empty, in
        /// which case the restart falls back to the root -- which is correct
        /// rather than a failure: a run that never had a discarded successor is
        /// one whose width was never the constraint.
        private List<Node> DrawSeed()
        {
            if (_reserve.Count == 0) return null;
            List<Node> seed = new List<Node>();
            for (int i = 0; i < _sgWidth && _reserve.Count > 0; i++)
            {
                int k = Rand(_reserve.Count);
                seed.Add(_reserve[k]);
                _reserve.RemoveAt(k);
            }
            return seed;
        }

        private void DropReserve()
        {
            foreach (Node n in _reserve) Give(n.S);
            _reserve.Clear();
        }

        /// Cut for the subgoal beam: the same width trim, but the best
        /// SgReservePerDepth of what it discards are kept for a restart instead
        /// of being recycled.  They are taken from the top of the discarded
        /// range, which Cut has already sorted, so they are the nodes this beam
        /// declined by a hair -- not the ones it refuted.
        ///
        /// Eviction is random rather than worst-first on purpose.  The point of
        /// the reserve is *diversity*: keeping the globally best SgReserve
        /// nodes fills it with one depth's near-duplicates, which is the same
        /// beam again and restarts to the same dead end.
        private void CutKeep(List<Node> next, int width)
        {
            if (next.Count <= width) return;
            next.Sort(static (a, b) => a.Tier != b.Tier ? a.Tier - b.Tier
                                     : a.H != b.H ? a.H - b.H : a.G - b.G);

            int keep = _opt.SgReuse ? Math.Min(_opt.SgReservePerDepth, next.Count - width) : 0;
            for (int i = width; i < width + keep; i++) Reserve(next[i]);
            for (int i = width + keep; i < next.Count; i++) Give(next[i].S);
            next.RemoveRange(width, next.Count - width);
        }

        private void Reserve(Node n)
        {
            if (_opt.SgReserve <= 0) { Give(n.S); return; }
            if (_reserve.Count < _opt.SgReserve) { _reserve.Add(n); return; }
            int k = Rand(_reserve.Count);
            Give(_reserve[k].S);
            _reserve[k] = n;
        }
    }
}
