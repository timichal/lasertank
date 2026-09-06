// Layer 5: push macros -- a beam whose depth is the number of *board changes*.
//
// **Why, in one measurement.**  Michal's hand recording of `LaserTank.lvl`
// level 1 (data/demos/LaserTank/00001.lpb) is 264 keypresses, and `--profile`
// plus tools/basin.py say the winning line spends **68 consecutive keypresses
// at or above its own best WorkDistance** -- against a p90 of 21 over the 402
// recordings the solver itself has produced.  A greedy beam follows a descent
// for free and an ascent only while the whole cross-section of the ascent fits
// in the width, and no width a machine holds covers the states reachable in 68
// keypresses.  That is why four rounds of the interactive driver do not touch
// the level and a fifth would not either: it is not a budget problem.
//
// The same trajectory has only **50 board-changing keypresses**, and measured in
// *those* units the ascent is **16 events deep**, against a solved-population
// p90 of 8.  So the level is 5.3x shorter and 4.3x less uphill in a search whose
// step is "change the board once" -- from 3.2x beyond anything ever solved to
// 2x the p90, which is the range width and restarts already reach.  That is the
// whole argument for this layer, and it is an argument about *this level*; the
// instrument now exists to check it on others (PROGRESS.md, Phase 4 addendum).
//
// **What the action set is.**  From a state, everything the tank can do without
// changing the playfield is one movement closure -- and unlike layer 1's Goto,
// that closure now *excludes* board-changing moves, which is the whole
// difference between the two layers:
//
//   * Layer 1's closure mixes movement and pushes, so it enumerates block
//     configurations rather than tank poses and blows its 1,500-node cap on
//     permutations.  What comes out of it is shot successors plus MoveOnlyK
//     pure-Goto states picked by *manhattan distance to the flag* -- which is
//     exactly the wrong selector during a ferry, where the tank spends fifty
//     keypresses walking away from the flag to fetch a block.
//   * Here the closure is PF-preserving, so it is the set of poses the tank can
//     stand in -- at most 16x16x4 of those, times whatever slide state -- and it
//     terminates on its own well inside the cap.  Every board change reachable
//     from any pose in it is then emitted as a first-class successor, ranked on
//     its merits rather than filtered by how far the tank walked.
//
// So the successors of a state are: **fire** from any pose (layer 1's rule, kept
// whole, including its lossless "a shot that changed nothing is not a move"
// prune), and **push** -- drive into something and keep driving while the board
// keeps changing, emitting one successor per changed cell.  A k-cell ferry is k
// successors, which is deliberate: it makes search depth equal the board-change
// count that was measured, so the numbers above are the numbers this searches.
//
// **Pushes are not modelled, they are executed.**  Nothing here knows that a
// block sinks in water or that bricks stop a laser.  A successor exists because
// `Engine.ApplyKey` was called and `Game.PF` came back different -- the same
// contract layer 1 states at the top of Macro.cs, and the reason a bug in this
// file can cost solutions but cannot produce a wrong one.
//
// **PF only, not PF2.**  "The board changed" is the playfield and not the
// objects underneath it, because PF2 moves when the tank merely steps onto a
// conveyor: counting that would mark almost every keypress as a board change
// and the compression this layer exists for would vanish.  Profile.cs uses the
// same definition, so the 50 events measured there are the 50 steps searched
// here.  A PF2-only change is therefore absorbed into the closure rather than
// emitted -- which costs a successor, never correctness, since every state in
// the closure is one the engine actually produced.
using System;
using System.Collections.Generic;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public sealed partial class Solver
    {
        // Two 256-byte boards, so a run of pushes can ask "did *this* key change
        // anything" rather than only "is this different from where we started".
        private readonly byte[] _boardNow = new byte[256];
        private readonly byte[] _boardPrev = new byte[256];
        private readonly List<EngineSnapshot> _pushClosure = new List<EngineSnapshot>();
        private readonly HashSet<ulong> _pushLocal = new HashSet<ulong>();

        /// WorkDistance memoised by tank cell, valid for one expansion.
        ///
        /// Only the move-only escape hatch needs it, and only when a closure
        /// truncated -- but when it does need it, it needs a *usable* ordering
        /// over up to a few thousand closure states, and layer 1's manhattan is
        /// the selector this layer exists to stop using.  Inside one closure the
        /// board is fixed by construction, so WorkDistance depends on the tank
        /// cell alone: at most 256 Dijkstras instead of one per state.
        private readonly int[] _workAt = new int[256];
        private int _workEpoch;
        private readonly int[] _workStamp = new int[256];

        // --push-trace only: what one depth cost and what it bought.  Closure
        // size is the number to watch -- this layer's whole bet is that a
        // PF-preserving closure is bounded by the pose count and so does not
        // truncate, and `trunc` is that bet being checked rather than assumed.
        private long _pxClosure, _pxCount, _pxTrunc;

        // --push-trace with --push-read: how the read tiered a depth's
        // successors.  The number to watch is _pxAdv against _pxSucc -- a tier
        // that promotes almost everything is a no-op that costs a Dijkstra, and
        // one that promotes almost nothing is a filter the beam cannot use.
        private long _pxSucc, _pxAdv, _pxBarrier, _pxNoBarrier;

        /// `Game.PF` flattened, in the same layout `EngineSnapshot.PF` uses.
        private void CopyBoard(byte[] into) => Buffer.BlockCopy(_e.Game.PF, 0, into, 0, 256);

        private bool BoardIs(byte[] board)
        {
            CopyBoard(_boardNow);
            return _boardNow.AsSpan().SequenceEqual(board);
        }

        /// This layer's closing policy, kept separate from layer 0's.
        ///
        /// Layer 0 measured close-on-generate a clear win (33 against 27 of 60)
        /// and layer 1 inherited it.  Both spend a handful of ApplyKey calls per
        /// successor; this one spends a whole PF-preserving closure, so a state
        /// binned by the width trim and then closed forever is a far more
        /// expensive thing to have thrown away -- and level 1 says so out loud,
        /// ending at `push-dead-end` with 21M nodes at width 300 and 78M at
        /// width 1200, the frontier having emptied because everything reachable
        /// had been marked and binned.  So the policy is a knob here with its
        /// own default rather than layer 0's shared one.
        private bool PushFresh(ulong h, HashSet<ulong> seen, HashSet<ulong> layer) =>
            Fresh(h, seen, layer, !_opt.PushCloseOnExpand);

        /// The ranking key.
        ///
        /// WorkDistance, plus PushFerry times Heuristic.RouteFerry -- the term
        /// that makes carrying a block towards water score better than not
        /// carrying it, without which this layer ranks an entire ferry as a
        /// plateau and searches it breadth-first.  Read straight after
        /// WorkDistance, which is what publishes it; the same coupling
        /// Subgoal.cs's Rank() has, for the same reason.
        private int PushH()
        {
            int work = _h.WorkDistance(_e);
            int ferry = _opt.PushFerry > 0 && _h.RouteFerry > 0
                      ? _opt.PushFerry * _h.RouteFerry : 0;
            return _opt.PushLearned ? Rank(work) + ferry : work + ferry;
        }

        // ---- restarts ------------------------------------------------------

        /// The width for the attempt in progress.  A field rather than the
        /// option because a restart widens it and one SolveOptions is shared by
        /// every level a worker solves.
        private int _pushWidth;

        /// Run the beam, and re-run it wider while it keeps dying of an empty
        /// frontier with budget still in hand.
        ///
        /// Layer 3's control law, for a failure with the same shape and the same
        /// argument for it being free: attempt 0 is the plain beam exactly, and
        /// a restart only ever spends budget the dead-end had already forfeit.
        /// Level 1 is what asked for it -- ranked by the learned evaluation with
        /// the ferry term it reaches board-change depth 66 and a WorkDistance of
        /// 15, one ferry from the end, and then dies with **556M of its 600M
        /// nodes unspent**.  Restarting is not a way to spend more; it is the
        /// only way to spend what is already there.
        ///
        /// Only `push-dead-end` restarts.  A `budget` stop has nothing left and
        /// a `push-depth` stop still had a frontier, so in both cases the result
        /// stands.  OutOfBudget is the *stage's* budget, so this cannot overrun
        /// the share the portfolio gave the searcher.
        private SolveResult PushSearch(EngineSnapshot root)
        {
            _pushWidth = _opt.PushBeamWidth;
            SolveResult r = PushBeam(root);
            int attempts = 0;

            while (!r.Solved && attempts < _opt.PushRestarts
                   && r.Stop == "push-dead-end" && !OutOfBudget)
            {
                attempts++;
                // The frontier emptied, so keep more of it next time.  Capped,
                // because an unbounded doubling turns the last restart into a
                // search that cannot finish one depth: a layer-5 depth costs
                // width x ~4,500 ApplyKey calls.
                _pushWidth = Math.Min(_pushWidth * 2, 9600);
                r = PushBeam(root);
            }

            r.Restarts = attempts;
            return r;
        }

        // ---- the beam over board changes -----------------------------------

        private SolveResult PushBeam(EngineSnapshot root)
        {
            SolveResult r = new SolveResult();
            HashSet<ulong> seen = new HashSet<ulong>();

            _e.Restore(root);
            seen.Add(_e.StateHash());
            FindFlag(out int fx, out int fy);

            List<Node> frontier = new List<Node>
            {
                new Node { S = CopyOf(root), G = 0, H = PushH() },
            };
            List<Node> next = new List<Node>();
            HashSet<ulong> layer = new HashSet<ulong>();

            for (int depth = 0; depth < _opt.PushDepth && frontier.Count > 0; depth++)
            {
                layer.Clear();
                foreach (Node parent in frontier)
                {
                    if (OutOfBudget) { r.Stop = "budget"; Recycle(frontier, next); return r; }
                    if (ExpandPush(parent.S, seen, layer, next))
                    {
                        Recycle(frontier, next);
                        return Won(r);
                    }
                    // One expansion offers a successor per (pose x direction),
                    // so a full layer would be width x closure snapshots live at
                    // once.  Trim as we go, exactly as layer 1 does: later
                    // parents still compete against the survivors and the live
                    // set stays O(width).
                    if (next.Count > 4 * _pushWidth) Cut(next, _pushWidth);
                }

                Cut(next, _pushWidth);
                if (_opt.PushTrace)
                {
                    Console.Error.WriteLine(
                        "  push d={0,3} front={1,5} best={2,5} closure~{3,5} trunc={4,4} nodes={5}",
                        depth, next.Count, next.Count > 0 ? next[0].H : -1,
                        _pxCount > 0 ? _pxClosure / _pxCount : 0, _pxTrunc, _nodes);
                    if (_opt.PushRead)
                        Console.Error.WriteLine(
                            "        read: {0}/{1} successors advanced ({2}%), "
                            + "expansions with a barrier {3}, without {4}",
                            _pxAdv, _pxSucc, _pxSucc > 0 ? 100 * _pxAdv / _pxSucc : 0,
                            _pxBarrier, _pxNoBarrier);
                    _pxClosure = _pxCount = _pxTrunc = 0;
                    _pxSucc = _pxAdv = _pxBarrier = _pxNoBarrier = 0;
                }

                foreach (Node n in frontier) Give(n.S);
                frontier.Clear();
                (frontier, next) = (next, frontier);
                Close(frontier, seen);
            }

            r.Stop = frontier.Count == 0 ? "push-dead-end" : "push-depth";
            Recycle(frontier, next);
            return r;
        }

        // ---- one expansion --------------------------------------------------

        /// The PF-preserving closure from `at`, and every board change reachable
        /// from any pose in it.  True when the engine is left standing on the
        /// flag, in which case its RecBuffer prefix is the solution and the
        /// caller must not touch the engine before reading it.
        private bool ExpandPush(EngineSnapshot at, HashSet<ulong> seen,
                                HashSet<ulong> layer, List<Node> next)
        {
            List<EngineSnapshot> closure = _pushClosure;
            HashSet<ulong> local = _pushLocal;
            closure.Clear();
            local.Clear();
            _workEpoch++;

            byte[] board = at.PF;                  // the parent's playfield
            _e.Restore(at);
            local.Add(_e.StateHash());
            closure.Add(CopyOf(at));
            int startKeys = at.KeyLen;
            bool truncated = false;
            // Where this expansion's successors start in `next`, so the read can
            // tier them afterwards.  It has to be afterwards: the successors are
            // emitted while the closure is still being walked, and the read
            // needs the *finished* closure -- that set is what tells layer 2's
            // FrontierObstacles where the tank demonstrably got to.
            int first = next.Count;

            // Pass one and pass two are the same loop.  A movement key from a
            // closure pose either leaves PF alone -- in which case the result is
            // another pose, i.e. closure -- or does not, in which case it is a
            // board change and the head of a push run.  Doing both here rather
            // than re-driving every pose afterwards is what keeps this layer's
            // cost per expansion close to layer 1's despite the larger closure:
            // no ApplyKey is spent twice.
            for (int head = 0; head < closure.Count; head++)
            {
                if (closure.Count >= _opt.PushClosureNodes) { truncated = true; break; }
                EngineSnapshot s = closure[head];
                if (s.KeyLen - startKeys >= _opt.PushClosureDepth) { truncated = true; continue; }

                foreach (byte key in MoveKeys)
                {
                    if (OutOfBudget) { Drain(closure); return false; }
                    _e.Restore(s);
                    _nodes++;
                    StepResult step = _e.ApplyKey(key, _opt.TickCap);
                    if (step == StepResult.Win) { Drain(closure); return true; }
                    if (step != StepResult.Ok) continue;       // dead or spinning
                    if (_e.Game.RecP >= (uint)_opt.MaxKeys) continue;

                    if (BoardIs(board))
                    {
                        // Movement only.  A pose, and a place to push from.
                        if (!local.Add(_e.StateHash())) continue;
                        closure.Add(_e.Snapshot(Take()));
                        continue;
                    }

                    // The board moved: this key is a board change, and the same
                    // key pressed again may well be the next cell of the same
                    // ferry.  Emit each changed cell as its own successor.
                    if (PushRun(key, seen, layer, next)) { Drain(closure); return true; }
                }
            }

            // A shot from every pose, with layer 1's prune: if the state hash is
            // unchanged after the space bar then nothing happened at all -- not
            // even an anti-tank turn, since AntiTank() runs inside the same
            // key-consuming tick -- so the successor is the pose we fired from,
            // which is already in this closure.
            foreach (EngineSnapshot c in closure)
            {
                if (OutOfBudget) break;
                _e.Restore(c);
                ulong before = _e.StateHash();
                _nodes++;
                StepResult step = _e.ApplyKey(Fire, _opt.TickCap);
                if (step == StepResult.Win) { Drain(closure); return true; }
                if (step != StepResult.Ok) continue;
                if (_e.Game.RecP >= (uint)_opt.MaxKeys) continue;

                ulong after = _e.StateHash();
                if (after == before) continue;
                if (!PushFresh(after, seen, layer)) continue;
                next.Add(new Node
                {
                    S = _e.Snapshot(Take()), G = (int)_e.Game.RecP, H = PushH(), Hash = after,
                });
            }

            if (truncated) KeepBestPoses(closure, seen, layer, next);
            if (_opt.PushRead) ReadTier(at, closure, next, first);
            if (_opt.PushTrace)
            {
                _pxClosure += closure.Count;
                _pxCount++;
                if (truncated) _pxTrunc++;
            }
            Drain(closure);
            return false;
        }

        /// The engine has just made a board change with `key`.  Emit it, then
        /// keep pressing the same key while the board keeps changing.
        ///
        /// The run is what makes a multi-cell ferry cheap: pushing a block three
        /// squares is three successors from *one* closure, not three expansions
        /// each paying for its own.  It stops the moment a key changes nothing,
        /// because at that point the tank is pushing against something that will
        /// not move and every continuation is some other pose's business.
        private bool PushRun(byte key, HashSet<ulong> seen, HashSet<ulong> layer,
                             List<Node> next)
        {
            CopyBoard(_boardPrev);
            for (int k = 1; ; k++)
            {
                ulong h = _e.StateHash();
                if (PushFresh(h, seen, layer))
                    next.Add(new Node
                    {
                        S = _e.Snapshot(Take()), G = (int)_e.Game.RecP, H = PushH(), Hash = h,
                    });

                if (k >= _opt.PushRun) return false;
                if (OutOfBudget) return false;

                _nodes++;
                StepResult step = _e.ApplyKey(key, _opt.TickCap);
                if (step == StepResult.Win) return true;
                if (step != StepResult.Ok) return false;
                if (_e.Game.RecP >= (uint)_opt.MaxKeys) return false;
                if (BoardIs(_boardPrev)) return false;          // the push ended
                Buffer.BlockCopy(_boardNow, 0, _boardPrev, 0, 256);
            }
        }

        /// The escape hatch, and it fires *only when the closure truncated*.
        ///
        /// An untruncated closure is the complete set of poses reachable without
        /// touching the board, so every way forward from it is a board change
        /// and every one of those has already been emitted -- a pure-movement
        /// successor would be a state the next expansion re-derives for free.
        /// Layer 1 keeps its MoveOnlyK unconditionally because its closure is
        /// depth-bounded and almost always short; this one is bounded by the
        /// pose count and usually is not, so the hatch is the exception rather
        /// than the rule and says so by being conditional.
        ///
        /// Tier 1, so Cut() lets these fill only the width that real board
        /// changes left empty -- the same contract layer 2's slack has.
        private void KeepBestPoses(List<EngineSnapshot> closure, HashSet<ulong> seen,
                                   HashSet<ulong> layer, List<Node> next)
        {
            int k = _opt.PushMoveOnlyK;
            if (k <= 0 || closure.Count <= 1) return;

            // Best-k by WorkDistance, memoised by tank cell: inside one closure
            // the board does not change, so the value depends on the cell alone.
            Span<int> bestAt = stackalloc int[16];
            Span<int> bestD = stackalloc int[16];
            if (k > 16) k = 16;
            int have = 0;

            for (int i = 1; i < closure.Count; i++)          // 0 is the parent itself
            {
                EngineSnapshot s = closure[i];
                int cell = s.Tank.X * 16 + s.Tank.Y;
                if (_workStamp[cell] != _workEpoch)
                {
                    _e.Restore(s);
                    _workAt[cell] = _h.WorkDistance(_e);
                    _workStamp[cell] = _workEpoch;
                }
                int d = _workAt[cell];

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
                if (!PushFresh(h, seen, layer)) continue;
                next.Add(new Node
                {
                    S = _e.Snapshot(Take()), G = s.KeyLen, H = PushH(), Hash = h,
                    Tier = TierPose,
                });
            }
        }

        // ---- layer 6: the read, as this beam's first sort key ---------------

        /// The three tiers, and the numbering is the contract with Cut().
        ///
        /// TierPose stays above every board change whether the read is on or
        /// off, so turning the read off leaves the ordering layer 5 shipped
        /// with: every board change at TierAdvance, poses behind them.  That
        /// equivalence is checked rather than asserted -- `--push-read off`
        /// reproduces layer 5's bench keystream for keystream.
        private const int TierAdvance = 0;   // the read says this exists for a reason
        private const int TierOther = 1;     // a board change the read is silent about
        private const int TierPose = 2;      // the truncation escape hatch

        /// Tier this expansion's successors by the read.
        ///
        /// Three derivations, in the order they cost.  `on the barrier` and the
        /// ferry are one Dijkstra for the whole expansion plus a board compare
        /// per successor, so they are effectively free against an expansion of
        /// ~4,500 ApplyKey calls.  `opens` is a second pose closure *each*, so
        /// only the best `--push-read-opens` still-untiered successors are asked
        /// -- best by H, which is the ordering that would otherwise have decided
        /// them anyway.  Past that cap the question is not asked and the
        /// successor stays at TierOther: a missing promotion, never a wrong one.
        ///
        /// The cap is the whole risk of this layer and it is why it is a number
        /// on the command line.  Layer 5's problem was never that it ranked
        /// badly, it was that one expansion costs two orders of magnitude more
        /// than a raw-beam successor; a read that doubles the expansion has to
        /// buy more than it spends, and that is a measurement, not an argument.
        private void ReadTier(EngineSnapshot at, List<EngineSnapshot> closure,
                              List<Node> next, int first)
        {
            if (next.Count <= first) return;
            ReadDerive(at, closure);
            if (_opt.PushTrace)
            {
                if (_readBarrier.Count > 0) _pxBarrier++; else _pxNoBarrier++;
            }

            byte[] before = at.PF;
            int untiered = 0;
            for (int i = first; i < next.Count; i++)
            {
                Node n = next[i];
                if (n.Tier == TierPose) continue;
                n.Tier = ReadAdvances(before, n.S.PF) ? TierAdvance : TierOther;
                if (n.Tier == TierOther) untiered++;
            }

            int k = _opt.PushReadOpens;
            if (k == 0 || untiered == 0) { ReadCount(next, first); return; }

            // k < 0: the cheap derivation, on every untiered successor, because
            // at a BFS apiece there is no reason to ration it.
            if (k < 0)
            {
                _e.Restore(at);
                int was = _h.TankRegion(_e);
                for (int i = first; i < next.Count; i++)
                    if (next[i].Tier == TierOther && ReadOpensCheap(next[i].S, was))
                        next[i].Tier = TierAdvance;
                ReadCount(next, first);
                return;
            }

            // k > 0: the executed derivation, rationed. Ask the best `k` of
            // them in H order -- the ordering that would otherwise have decided
            // them anyway, so the ration costs nothing when it guesses right.
            List<int> ask = new List<int>();
            for (int i = first; i < next.Count; i++)
                if (next[i].Tier == TierOther) ask.Add(i);
            ask.Sort((a, b) => next[a].H - next[b].H);
            if (ask.Count > k) ask.RemoveRange(k, ask.Count - k);

            foreach (int i in ask)
            {
                if (OutOfBudget) break;
                if (ReadOpens(next[i].S)) next[i].Tier = TierAdvance;
            }
            ReadCount(next, first);
        }

        /// --push-trace only: the tiering as it finally stands, counted after
        /// every derivation has run.  Counting it before the `opens` pass was
        /// the first version and it reported 0% where the truth was 40% -- an
        /// instrument that measures the wrong moment says the layer does
        /// nothing, which is exactly the conclusion it nearly bought.
        private void ReadCount(List<Node> next, int first)
        {
            if (!_opt.PushTrace) return;
            for (int i = first; i < next.Count; i++)
            {
                if (next[i].Tier == TierPose) continue;
                _pxSucc++;
                if (next[i].Tier == TierAdvance) _pxAdv++;
            }
        }
    }
}
