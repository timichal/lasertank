// Item 5: layer 2's decomposition one level out -- search for one phase, commit
// to the board it found, re-derive, repeat.
//
// **The measurement that shaped it, and it changed the design twice.**
// `tools/phases.py` cuts each of the 20 hand recordings at its *milestones* and
// reports the phase lengths against item 18's per-level horizon.  On
// `LaserTank.lvl` 6 "Cascade" -- the level this item exists for -- the 168-change
// line comes out as **six phases of 18, 26, 28, 30, 32 and 34 board changes**,
// one per hole filled, no tail, against a measured horizon of **50**.  Every
// phase of the human's own line is inside a reach the search has already been
// shown to have on that level, and the longest is 0.68 of it.  So the level is
// not out of reach; it is 3.4x too long to be reached in one go.
//
// Two things that measurement retired before a line of this was written:
//
//   * **A phase is not sized, it is *terminated*.**  Item 18 refuted a global
//     constant (horizons run 2 to 50, a 25x spread) and item 14 carries the
//     negative that nothing free predicts one.  A milestone needs no prediction:
//     it is a board test, and the phase is however long the search takes to
//     reach it.
//   * **A phase is not one carry.**  `--push-ferry-stage` bet on one (block,
//     hole) pair at a time and stalls, and the human record is why -- level 6's
//     first phase moves five different blocks before the one that sinks.  A
//     milestone says nothing about how many blocks move on the way to it.
//
// **What a milestone is, and it is a census rather than a model.**  Every board
// change in a recording is one of two kinds.  A push *moves* an object -- a
// block leaves one cell and arrives at another, an anti-tank is shoved back, a
// mirror is rearranged, a rotary mirror turns -- and the number of that object
// on the board is unchanged.  A fill, a shot brick, a destroyed mirror and a
// destroyed anti-tank *consume* one, and the number drops.  So:
//
//     a successor is a milestone when its board holds strictly fewer
//     consumable objects than the board the phase started from.
//
// One integer, a strict comparison, no weight and no threshold -- the same shape
// as `--push-fire-tier`'s test, and for the same reason: an ordering derived
// from a count cannot be wrong about the board, only silent.
//
// **What it is silent on, stated up front.**  `tools/phases.py` says the rule
// cuts 17 of the 20 recordings and is empty on three -- and one of the three is
// `LaserTank.lvl` 10, the other open level.  That is not a surprise and it is
// not a defect: 10 is a GAUNTLET, a GAUNTLET consumes nothing on its way to the
// flag, and Layer 9 already found the read naming a barrier on 0 of its 63,454
// expansions for the same structural reason.  **This item is for the half of
// the corpus that is a ferry.**  A level with no milestone runs as one phase,
// i.e. exactly as it does today, which is what makes the flag safe to hand to a
// rung that already works.
//
// Underlays are deliberately not counted.  A block pushed onto a conveyor reads
// as `conveyor->block`, and the conveyor is still there when the block leaves;
// counting conveyors, ice or tunnels would call an ordinary push a milestone and
// the search would commit to nothing.  Thin ice is the one consumable left out
// (it becomes water, so the count goes *up*), which errs towards a missed cut --
// a longer phase -- rather than towards a false one, which is the direction a
// commitment has to err in.
using System;
using System.Collections.Generic;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public sealed partial class Solver
    {
        /// The consumable census of a playfield.
        ///
        /// Named for what it counts rather than 'Census', which Analyze.cs
        /// already has as the human-readable board summary -- and which counts
        /// the same object families for the same reason, arrived at
        /// independently.  Conveyors and ice are in that one and not in this
        /// one: they are underlays, and a census that moved when a block was
        /// pushed onto a belt would call an ordinary push a milestone.
        ///
        /// Water is in it because a filled hole is the milestone this item was
        /// built for; blocks because a sunk block is the same event counted from
        /// the other side, and a block is never destroyed any other way; bricks,
        /// mirrors, rotary mirrors, crystals and anti-tanks because a laser
        /// removes each of them permanently.  Tunnels are not consumable and are
        /// not in the byte range this switch covers, so they fall through.
        internal static int Consumables(byte[] pf)
        {
            int n = 0;
            for (int i = 0; i < 256; i++)
            {
                byte c = pf[i];
                if (c == Obj.Water || c == Obj.Block || c == Obj.Bricks
                    || c == Obj.Crystal
                    || (c >= Obj.AntiTankUp && c <= Obj.AntiTankLeft)
                    || (c >= Obj.MirrorUL && c <= Obj.MirrorDL)
                    || (c >= Obj.RotoUL && c <= Obj.RotoDL)) n++;
            }
            return n;
        }

        /// The census of the board the current phase started from.  A successor
        /// below it has consumed something the phase had.
        private int _phaseBase;

        /// The best milestone successor this phase has seen, by the beam's own
        /// order -- a *copy* of its snapshot, because the one in `next` goes
        /// back to the pool at the end of the depth.
        private EngineSnapshot _phaseHit;
        private int _phaseHitTier, _phaseHitH, _phaseHitG, _phaseHitCensus;

        /// Phases committed to so far, reported so a stalled run says where it
        /// stalled rather than only that it did.
        private int _phaseCount;

        /// `_nodes` when this phase began, for --push-phase-nodes' share.
        private long _phaseNodesAt;

        public int PhaseCount => _phaseCount;

        private void PhaseBegin(EngineSnapshot at)
        {
            _phaseBase = Consumables(at.PF);
            _phaseNodesAt = _nodes;
            PhaseDrop();
        }

        private void PhaseDrop()
        {
            if (_phaseHit != null) Give(_phaseHit);
            _phaseHit = null;
        }

        /// This expansion's successors, `first` to the end of `next`, against
        /// the phase's baseline.  Called from ExpandPush after every derivation
        /// has run and after the engine is free -- see FireTier, which has the
        /// same contract and the same placement.
        ///
        /// **Deliberately before the width trim.**  A milestone board that the
        /// trim would throw away is exactly the board this item exists to keep:
        /// the whole diagnosis of level 6 is a beam that reaches a fill and then
        /// finds every successor of every board it holds worse, so a
        /// decomposition that could only commit to boards the ranking already
        /// liked would decompose nothing.
        private void PhaseMark(List<Node> next, int first)
        {
            for (int i = first; i < next.Count; i++)
            {
                Node n = next[i];
                int c = Consumables(n.S.PF);
                if (c >= _phaseBase) continue;
                // Among this phase's milestones, take the beam's own preference:
                // more consumed first -- a change that sinks a block *and* fills
                // a hole is two -- then Tier, H and G exactly as Cut sorts.
                if (_phaseHit != null
                    && !(c < _phaseHitCensus
                         || (c == _phaseHitCensus
                             && (n.Tier != _phaseHitTier ? n.Tier < _phaseHitTier
                                 : n.H != _phaseHitH ? n.H < _phaseHitH
                                 : n.G < _phaseHitG)))) continue;
                if (_phaseHit != null) Give(_phaseHit);
                _phaseHit = CopyOf(n.S);
                _phaseHitTier = n.Tier;
                _phaseHitH = n.H;
                _phaseHitG = n.G;
                _phaseHitCensus = c;
            }
        }

        /// True when the phase should be cut short and its milestone taken.
        ///
        /// **This is the third commit law and the first two are the finding.**
        ///
        /// *One:* let a phase run until the beam stops on its own.  Inert --
        /// on every level this item is for the beam stops on *budget*, so the
        /// first phase spent the whole run and nothing was ever committed.
        ///
        /// *Two:* end the phase at the depth that produced a milestone.  That
        /// needs no constant, which is why it was tried, and it **cost a level
        /// the plain beam solves**: `LaserTank.lvl` 20 goes from a win at 2.75M
        /// nodes to twelve committed phases and `push-dead-end` at 6M.  The
        /// cause is visible in the trace and `tools/phases.py` had already
        /// shown it from the other side -- a demolition level consumes
        /// something on nearly every board change (level 3's census starts at
        /// **77**, level 28's line is 82 milestones in 105 changes), so
        /// committing at the first milestone turns the beam into a greedy
        /// one-step hill-climb with no backtracking anywhere.  Layer 1's
        /// structural finding, restated: a specialist that bets on every level
        /// loses in a portfolio.
        ///
        /// *Three, and what ships:* **a phase ends where the search would
        /// otherwise give up.**  PushSearch already returns on a dead-end that
        /// has exhausted the restart ladder, and PushPhases commits there -- so
        /// the chain only ever spends a commitment on a frontier that was
        /// finished anyway, and a beam that is still descending is never
        /// interrupted.  On a level the plain beam solves the chain is
        /// therefore the plain beam, which is the property that makes the flag
        /// safe to hand to a rung that already works.
        ///
        /// What that costs is level 6, and it is the honest cost: level 6's
        /// beam neither wins nor dies, it wanders to budget, so under this law
        /// it never commits.  `--push-phase-nodes N` is the opt-in for exactly
        /// that shape -- give a phase a share and take its best milestone when
        /// the share is gone.  It is off by default because it is a global
        /// constant and item 18 refuted those for this quantity; it exists so
        /// `tools/phase_reach.py` can probe a level that needs one.
        ///
        /// Checked once per depth rather than per node: a depth of layer 5 is
        /// width x ~4,500 ApplyKey calls, so the granularity costs nothing and
        /// the check stays out of the hot loop.
        private bool PhaseDone =>
            _phaseHit != null && _opt.PushPhaseNodes > 0
            && _nodes - _phaseNodesAt >= _opt.PushPhaseNodes;

        /// The staged search: a chain of ordinary push searches, each starting
        /// from the board the previous one committed to.
        ///
        /// **The commitment is a real state, not a hint.**  The snapshot carries
        /// its own key prefix (layer 0's second bug, and the fix that made a
        /// breadth-first search's answers replayable), so a win in phase six
        /// reports the whole keystream from the level's start and goes through
        /// the two-engine gate like any other solution.  Nothing here is
        /// hint-assisted: the boards committed to are ones this search found.
        ///
        /// **Each phase gets a fresh closed set and a fresh frontier**, which is
        /// the half `--push-ferry-stage` did not have.  Staging a heuristic
        /// inside one beam leaves the frontier full of the phase that has just
        /// finished; re-entering PushBeam throws that away and re-derives from
        /// the one board that matters.  Layer 2's shape, one level out.
        ///
        /// The restart ladder stays *inside* a phase (PushSearch), so a phase
        /// that dead-ends buys width before the chain gives up on it.
        private SolveResult PushPhases(EngineSnapshot root)
        {
            EngineSnapshot at = CopyOf(root);
            SolveResult r;
            _phaseCount = 0;
            try
            {
                while (true)
                {
                    PhaseBegin(at);
                    r = PushSearch(at);
                    r.Phases = _phaseCount;
                    // A phase that found nothing to consume is where the chain
                    // ends, and the result is the plain beam's -- which is what
                    // makes this safe on a level with no milestone at all: it
                    // is one phase, and one phase is the search as it runs
                    // today.
                    if (r.Solved || _phaseHit == null || OutOfBudget) return r;

                    // Commit.  The old phase root goes back to the pool and the
                    // milestone board becomes the new one; _phaseHit is handed
                    // over rather than copied, so nothing is allocated per phase.
                    Give(at);
                    at = _phaseHit;
                    // Read before PhaseBegin's PhaseDrop clears them, because
                    // the next loop begins by resetting exactly these.
                    int hitCensus = _phaseHitCensus, hitH = _phaseHitH;
                    _phaseHit = null;
                    _phaseCount++;
                    if (_opt.PushTrace)
                    {
                        Console.Error.WriteLine(
                            "  phase {0} committed: census {1} -> {2}, keys {3}, nodes {4}",
                            _phaseCount, _phaseBase, hitCensus, at.KeyLen, _nodes);
                        TraceBoard(at, hitH / Eval.Scale);
                    }
                }
            }
            finally
            {
                PhaseDrop();
                Give(at);
            }
        }
    }
}
