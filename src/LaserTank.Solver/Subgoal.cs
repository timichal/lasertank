// Layer 2: subgoal decomposition.
//
// Layer 1's measurement is this layer's brief, so it is worth restating what it
// found rather than only what it scored.  The macro beam turned a keypress
// search into a (Goto, Shoot) search, so depth became the shot count -- and it
// still lost on deep levels, at every width, closure cap and budget share
// tried.  The reason was not the depth and not the parameters:
//
//     Inside a Goto, movement is *exhausted* rather than searched, so the beam
//     never ranks a movement.  It ranks board changes -- and it ranks them with
//     WorkDistance, a number that also moves when the tank merely walks.
//
// A closure of 1,500 states offers ~1,500 shots.  Most of them change
// *something* (so the lossless prune keeps them) and almost none of them change
// the route to the flag, so they score identically and the beam keeps whichever
// twenty-four sorted first.  That is a beam ranking nothing.
//
// **So this layer derives the reason to fire instead of scoring it.**  The
// movement closure runs first and records the cells the tank actually stood on
// -- an executed answer to "where can it get to", with anti-tank fire, spent
// thin ice, conveyors and tunnel pairing resolved by having happened.
// Heuristic.FrontierObstacles then runs the priced Dijkstra from the flag and
// stops at the first of those cells it settles: what lies between the two is
// what is in the way.  "Shoot this brick" is then a *derived* subgoal -- it is
// on the cheapest way in -- and a successor is accepted because the board got
// cheaper at a cell that mattered, not because a number went down.
//
// Deriving it from a priced route to the *tank* instead was tried first and
// measured: on 384 expansions over levels layer 0 had failed, 62% of them found
// no obstacle at all, because a price list does not know what actually stops a
// tank.  The note in Heuristic.FrontierObstacles has the numbers.
//
// Two consequences, and they are the whole design:
//
//   **Acceptance is a board test, ranking is a position test.**  Clearing a
//   blocker usually leaves the tank somewhere awkward, so the two disagree
//   constantly -- which is exactly the conflation layer 1 could not see past.
//   Here the board test decides what survives and WorkDistance only orders what
//   already survived.
//
//   **The frontier is tiny, so the chain can be long.**  Where layer 1 kept 24
//   near-identical states per shot-depth, a derived expansion typically offers
//   a handful of candidates, most expansions offer one or two, and many offer
//   none at all (that branch is genuinely dead).  A budget of 150,000 ApplyKey
//   calls buys ~50 subgoal steps at a 400-state closure, which is the
//   arithmetic that puts a 400-keypress solution inside a campaign budget for
//   the first time.  Layer 1 spent the same budget maintaining 24 parallel
//   lines of play six shots deep.
//
// What is deliberately *not* here: any model of the laser.  Whether a brick can
// be shot from a given pose is settled the way everything is settled in this
// solver -- by pressing the key and looking at the board.  The route model
// proposes; the engine disposes.  Its price list is a price list, not a claim
// about the rules, so a blocker is a candidate and nothing more.
//
// The one place completeness is given up, as in layer 1, is a cap with a number
// on it: SgClosureNodes / SgClosureDepth bound the movement closure -- and here
// that cap does double duty, because a truncated closure also makes the
// derivation pessimistic (it will call an unexplored cell an obstacle).  That is
// what SgFallbackK exists to survive.
using System;
using System.Collections.Generic;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public sealed partial class Solver
    {
        /// The derived subgoals of one expansion -- the obstacles between the
        /// cells the closure reached and the flag, nearest first -- and what
        /// each cost on the parent's board, so "did this get cheaper?" has
        /// something to compare against.
        private readonly int[] _targets = new int[64];
        private readonly int[] _targetCost = new int[64];

        /// One bool per cell: where the movement closure actually stood.  This
        /// is the executed half of the derivation -- see
        /// Heuristic.FrontierObstacles for why a modelled one was not enough.
        private readonly bool[] _reached = new bool[256];

        /// The best few board-changing successors of one expansion that did not
        /// advance anything.  See Offer().
        private readonly List<Node> _slack = new List<Node>();

        /// A cell's entry price with "permanently blocked" folded in as a large
        /// finite number.  Heuristic.PriceOf returns -1 for Solid and Crystal,
        /// and a raw `<` against that would read a cell *turning into* solid as
        /// progress -- which happens: a block pushed into water becomes Dirt,
        /// but a laser-cleared cell can end up holding something worse.
        private const int Blocked = 1000;

        private static int CostOf(byte cell)
        {
            int p = Heuristic.PriceOf(cell);
            return p < 0 ? Blocked : p;
        }

        // ---- the beam over subgoal steps -----------------------------------

        /// One "step" is: exhaust movement, derive what is still in the way,
        /// and keep the successors that made one of those things cheaper.  So a
        /// step is normally an obstacle removed rather than a shot fired, which
        /// is what makes the depth small enough to be worth searching: a
        /// 400-keypress solution is tens of steps, not hundreds of keypresses.
        /// (Slack successors, below, are the exception -- they are steps that
        /// removed nothing, kept so that a two-move manoeuvre can be found.)
        private SolveResult SubgoalBeam(EngineSnapshot root)
        {
            SolveResult r = new SolveResult();
            HashSet<ulong> seen = new HashSet<ulong>();

            _e.Restore(root);
            seen.Add(_e.StateHash());
            FindFlag(out int fx, out int fy);

            List<Node> frontier = new List<Node>
            {
                new Node { S = CopyOf(root), G = 0, H = _h.WorkDistance(_e) },
            };
            List<Node> next = new List<Node>();
            HashSet<ulong> layer = new HashSet<ulong>();

            for (int step = 0; step < _opt.SgDepth && frontier.Count > 0; step++)
            {
                layer.Clear();
                foreach (Node parent in frontier)
                {
                    if (OutOfBudget) { r.Stop = "budget"; Recycle(frontier, next); return r; }
                    if (ExpandSubgoal(parent.S, seen, layer, next, fx, fy))
                    {
                        Recycle(frontier, next);
                        return Won(r);
                    }
                    if (next.Count > 4 * _opt.SgWidth) Cut(next, _opt.SgWidth);
                }

                Cut(next, _opt.SgWidth);
                foreach (Node n in frontier) Give(n.S);
                frontier.Clear();
                (frontier, next) = (next, frontier);
                Close(frontier, seen);
            }

            r.Stop = frontier.Count == 0 ? "subgoal-dead-end" : "subgoal-depth";
            Recycle(frontier, next);
            return r;
        }

        // ---- one subgoal step ----------------------------------------------

        /// Returns true when the engine is left standing on the flag, in which
        /// case its RecBuffer prefix is the solution and the caller must not
        /// touch the engine before reading it.
        ///
        /// The order here is the layer.  The movement closure runs *first*,
        /// because it is what makes the derivation trustworthy: the set of
        /// cells it stood on is an executed answer to "where can the tank get
        /// to", with anti-tank fire, spent thin ice, conveyors and tunnel
        /// pairing all resolved by having happened.  Only then are the
        /// obstacles derived, from the gap between that set and the flag.
        private bool ExpandSubgoal(EngineSnapshot at, HashSet<ulong> seen, HashSet<ulong> layer,
                                   List<Node> next, int fx, int fy)
        {
            List<EngineSnapshot> closure = _closure;
            HashSet<ulong> local = _local;
            closure.Clear();
            local.Clear();
            _slack.Clear();
            Array.Clear(_reached, 0, 256);

            _e.Restore(at);
            int baseWork = _h.WorkDistance(_e);
            local.Add(_e.StateHash());
            _reached[at.Tank.X * 16 + at.Tank.Y] = true;
            closure.Add(CopyOf(at));
            int startKeys = at.KeyLen;

            for (int head = 0; head < closure.Count && closure.Count < _opt.SgClosureNodes; head++)
            {
                EngineSnapshot s = closure[head];
                if (s.KeyLen - startKeys >= _opt.SgClosureDepth) continue;

                foreach (byte key in MoveKeys)
                {
                    if (OutOfBudget) { DrainStep(closure); return false; }
                    _e.Restore(s);
                    _nodes++;
                    StepResult step = _e.ApplyKey(key, _opt.TickCap);
                    if (step == StepResult.Win) { DrainStep(closure); return true; }
                    if (step != StepResult.Ok) continue;            // dead or spinning
                    if (_e.Game.RecP >= (uint)_opt.MaxKeys) continue;
                    if (!local.Add(_e.StateHash())) continue;
                    _reached[_e.Game.Tank.X * 16 + _e.Game.Tank.Y] = true;
                    closure.Add(_e.Snapshot(Take()));
                }
            }

            // The derivation, on the parent's board: what is between the cells
            // the tank actually got to and the flag.
            _e.Restore(at);
            int nt = _h.FrontierObstacles(_e, _reached, _targets);
            if (nt > _opt.SgCandidates) nt = _opt.SgCandidates;
            for (int i = 0; i < nt; i++)
                _targetCost[i] = CostOf(at.PF[_targets[i]]);
            int added = 0;

            // A closure state can satisfy the subgoal without firing: pushing
            // the blocking block aside is a *move*.  Testing that reads the
            // snapshot's own board, so the ones that changed nothing cost a
            // handful of array reads and no restore at all.
            for (int i = 1; i < closure.Count; i++)             // 0 is the parent
            {
                EngineSnapshot c = closure[i];
                if (!Cleared(c.PF, nt)) continue;
                _e.Restore(c);
                if (Offer(true, nt, baseWork, _e.StateHash(), seen, layer, next)) added++;
            }

            // A shot from every pose the closure reached.  --sg-aim narrows
            // that to the poses whose facing ray meets a target, a mirror or an
            // anti-tank, and it is off because it was measured: 6 solved to 2
            // on the deep bench.  The ray is a superset of the shots that hit a
            // *target*, but not of the shots that are worth firing -- see
            // SolveOptions.SgAim.
            for (int i = 0; i < closure.Count; i++)
            {
                if (OutOfBudget) break;
                EngineSnapshot c = closure[i];
                if (_opt.SgAim && !CanAim(c, nt)) continue;

                _e.Restore(c);
                ulong before = _e.StateHash();
                _nodes++;
                StepResult step = _e.ApplyKey(Fire, _opt.TickCap);
                if (step == StepResult.Win) { DrainStep(closure); return true; }
                if (step != StepResult.Ok) continue;
                if (_e.Game.RecP >= (uint)_opt.MaxKeys) continue;

                ulong after = _e.StateHash();
                if (after == before) continue;                     // layer 1's lossless prune
                if (Offer(Cleared(_e, nt), nt, baseWork, after, seen, layer, next)) added++;
            }

            // The escape hatch, which is also where this layer admits its
            // limits.  When the derivation named nothing there is no subgoal to
            // be directed by, and when it named something no reachable pose
            // could touch, the branch would otherwise die here -- so a few
            // pure-Goto states nearest the flag are kept and the search carries
            // on undirected.  That is layer 1's escape hatch, used as a
            // fallback rather than on every expansion.
            int slack = _slack.Count;
            foreach (Node n in _slack) next.Add(n);
            _slack.Clear();

            if ((nt == 0 || added == 0) && _opt.SgFallbackK > 0)
                KeepNearestN(closure, seen, layer, next, fx, fy, _opt.SgFallbackK);

            if (_opt.SgTrace)
                Console.Error.WriteLine(
                    "  expand keys={0} work={1} closure={2} obstacles={3} added={4} slack={5}",
                    at.KeyLen, baseWork, closure.Count, nt, added, slack);

            DrainStep(closure);
            return false;
        }

        /// Give back everything one expansion is holding: the closure and any
        /// slack successors that were not emitted (a win can return from the
        /// middle of either loop).
        private void DrainStep(List<EngineSnapshot> closure)
        {
            foreach (Node n in _slack) Give(n.S);
            _slack.Clear();
            Drain(closure);
        }

        /// Did any derived obstacle get cheaper?  This is the subgoal test, and
        /// it is a question about the *board* -- not about where the tank ended
        /// up, which is what layer 1's WorkDistance kept conflating it with.
        private bool Cleared(byte[] pf, int nt)
        {
            for (int i = 0; i < nt; i++)
                if (CostOf(pf[_targets[i]]) < _targetCost[i]) return true;
            return false;
        }

        private bool Cleared(Engine e, int nt)
        {
            for (int i = 0; i < nt; i++)
            {
                int c = _targets[i];
                if (CostOf(e.Game.PF[c >> 4, c & 15]) < _targetCost[i]) return true;
            }
            return false;
        }

        /// Keep a successor.
        ///
        /// **Derive when you can, score when you cannot**, and `nt` is which of
        /// the two this expansion is in.  With obstacles derived, acceptance is
        /// the board test above; failing that, a strictly shorter priced route,
        /// which catches progress the obstacle list did not predict (a mirror
        /// turned, a tunnel opened) at the cost of re-admitting some of layer
        /// 1's positional signal -- --sg-strict drops that second clause.
        /// WorkDistance is then used for *ranking* only: ordering what already
        /// survived, never deciding it.
        ///
        /// With nothing derived (nt == 0) there is no subgoal to be directed by
        /// and the expansion falls back to layer 1's rule -- keep every
        /// successor that changed the board and let the width sort them out.
        /// That case is not rare and pretending otherwise was this layer's
        /// second wrong turn: on levels layer 0 failed, 23% of expansions name
        /// no obstacle, because on those levels progress is not cell-clearing
        /// at all.  Beginner-I 1919 "Fourntyrnet" is the pure case -- a board of
        /// conveyors and rotary mirrors with no brick, block or anti-tank
        /// anywhere between the tank and the flag.  A specialist that dies
        /// there is worse than one that shrugs and searches.
        private bool Offer(bool cleared, int nt, int baseWork, ulong hash, HashSet<ulong> seen,
                           HashSet<ulong> layer, List<Node> next)
        {
            int work = cleared || nt == 0 ? -1 : _h.WorkDistance(_e);
            bool advanced = cleared || nt == 0 || (!_opt.SgStrict && work < baseWork);
            if (!advanced && _opt.SgSlack <= 0) return false;
            if (!Fresh(hash, seen, layer, !_opt.SgCloseOnExpand)) return false;
            if (work < 0) work = _h.WorkDistance(_e);

            Node n = new Node
            {
                S = _e.Snapshot(Take()),
                G = (int)_e.Game.RecP,
                H = work,
                Hash = hash,
                Tier = advanced ? 0 : 1,
            };
            if (advanced) { next.Add(n); return true; }
            Slack(n);
            return false;
        }

        /// Keep the best SgSlack non-advancing successors of this expansion.
        ///
        /// **Why a search that only accepts progress gets stuck, and this is the
        /// cheapest honest way out.**  A derived subgoal is often two moves
        /// away, not one: rotate the mirror so the laser turns, *then* shoot the
        /// brick.  The first of those clears nothing and leaves the route no
        /// shorter, so a strict accept-on-progress rule throws it away and the
        /// brick is never reachable.  Measured, that rule dies with 95% of the
        /// budget unspent -- 44 of 50 deep levels ended at subgoal-dead-end
        /// having used 20,000 of 400,000 nodes.
        ///
        /// Slack successors are Tier 1, so Cut() takes them only after every
        /// successor that actually advanced: they fill the width that progress
        /// left empty and never displace it.  That is the difference between
        /// this and simply widening the acceptance test back to layer 1's --
        /// which was tried, and is the --sg-strict/no-derivation end of the
        /// scale.
        private void Slack(Node n)
        {
            if (_slack.Count < _opt.SgSlack) { _slack.Add(n); return; }
            int worst = 0;
            for (int i = 1; i < _slack.Count; i++)
                if (_slack[i].H > _slack[worst].H) worst = i;
            if (_slack[worst].H <= n.H) { Give(n.S); return; }
            Give(_slack[worst].S);
            _slack[worst] = n;
        }

        /// Could a shot from this pose reach anything worth reaching?
        ///
        /// The ray is cast from the tank in its facing direction and stops only
        /// at the board edge: obstacles are deliberately ignored, so this can
        /// only ever say "yes" too often.  It says yes for a target cell (the
        /// derived subgoal), for any mirror or rotary mirror (the laser may turn
        /// there and reach a target the straight ray cannot) and for any
        /// anti-tank (killing one is how "Pass the anti-tanks" is played, and a
        /// threat is not on the route by definition).
        ///
        /// Off by default: it is a node saving, not a search improvement, and
        /// the two want measuring separately.
        private bool CanAim(EngineSnapshot s, int nt)
        {
            int dx = s.Tank.Dir == 2 ? 1 : s.Tank.Dir == 4 ? -1 : 0;
            int dy = s.Tank.Dir == 3 ? 1 : s.Tank.Dir == 1 ? -1 : 0;
            if (dx == 0 && dy == 0) return true;               // no facing: do not filter

            for (int x = s.Tank.X + dx, y = s.Tank.Y + dy;
                 x >= 0 && x < 16 && y >= 0 && y < 16;
                 x += dx, y += dy)
            {
                int cell = s.PF[x * 16 + y];
                if (cell >= Obj.MirrorUL && cell <= Obj.MirrorDL) return true;
                if (cell >= Obj.RotoUL && cell <= Obj.RotoDL) return true;
                if (cell >= Obj.AntiTankUp && cell <= Obj.AntiTankLeft) return true;
                for (int i = 0; i < nt; i++)
                    if (_targets[i] == x * 16 + y) return true;
            }
            return false;
        }

        /// The k closure states ending nearest the flag, as pure-Goto
        /// successors.  Layer 1's KeepNearest with the width passed in rather
        /// than read from MoveOnlyK, because layer 2 uses it as a rare fallback
        /// and layer 1 uses it on every expansion.
        private void KeepNearestN(List<EngineSnapshot> closure, HashSet<ulong> seen,
                                  HashSet<ulong> layer, List<Node> next,
                                  int fx, int fy, int k)
        {
            if (k <= 0 || fx < 0 || closure.Count <= 1) return;
            if (k > 16) k = 16;

            Span<int> bestAt = stackalloc int[16];
            Span<int> bestD = stackalloc int[16];
            int have = 0;

            for (int i = 1; i < closure.Count; i++)            // 0 is the parent itself
            {
                EngineSnapshot s = closure[i];
                int d = Math.Abs(s.Tank.X - fx) + Math.Abs(s.Tank.Y - fy);
                if (have < k) { bestAt[have] = i; bestD[have] = d; have++; }
                else
                {
                    int worst = 0;
                    for (int j = 1; j < have; j++) if (bestD[j] > bestD[worst]) worst = j;
                    if (d < bestD[worst]) { bestD[worst] = d; bestAt[worst] = i; }
                }
            }

            for (int i = 0; i < have; i++)
            {
                EngineSnapshot s = closure[bestAt[i]];
                _e.Restore(s);
                ulong h = _e.StateHash();
                if (!Fresh(h, seen, layer, !_opt.SgCloseOnExpand)) continue;
                next.Add(new Node
                {
                    S = _e.Snapshot(Take()), G = s.KeyLen, H = _h.WorkDistance(_e), Hash = h,
                });
            }
        }
    }
}
