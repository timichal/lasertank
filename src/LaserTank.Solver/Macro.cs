// Layer 1: macro-actions.
//
// Layer 0 searched keypresses.  That put the whole solution length into the
// search depth, and the .ghs targets say the median flagship level needs 122
// moves+shots -- so the honest reading of layer 0's campaign is that its
// unsolved levels are not "nearly there", they are the wrong shape.  This layer
// changes the shape.
//
// The action set becomes
//
//     Goto(x, y, dir)   drive the tank somewhere, spending as many keys as it
//                       takes, and
//     Shoot             one space bar.
//
// and a solution becomes an alternation of the two.  That is complete, not a
// restriction: *any* keystream is a run of direction keys, a space, a run of
// direction keys, a space, ... so searching (Goto, Shoot) pairs can express
// every solution layer 0 could.  What it buys is depth -- the search depth is
// now the number of *shots*.  In an unbiased 1-in-5 sample of Beginner-I the
// median level needs 16 shots against 27 moves, and 15% need no shot at all.
// (An earlier figure of 10 shots came from a cheapest-first campaign that was
// stopped part-way, i.e. from the cheapest levels rather than from a sample.)
//
// **The Goto is a sub-search in the engine, not a model of it.**  The plan in
// PROGRESS said "A* over tank movement (ice slides, conveyors and tunnels
// resolved by a deterministic sub-search)", and the deliberate reading of that
// here is: the sub-search *is* the engine.  A hand-written A* over a grid would
// have to re-derive MoveTank's turn-costs-a-key rule, IceMoveT's slide,
// ConvMoveTank, TranslateTunnel's pairing and the fact that AntiTank() runs on
// every key-consuming tick -- i.e. it would be a second implementation of the
// game, free to drift from the one being ported.  This project has spent four
// phases making sure there is exactly one.  So Goto is a breadth-first closure
// over Engine.ApplyKey with the four direction keys, deduplicated by
// StateHash; ice, conveyors, tunnels and anti-tanks are "resolved" by being
// *executed*.  It costs more per node than a grid A* and it cannot be wrong.
//
// Two prunes, and the difference between them matters:
//
//   A shot that changes nothing is dropped, and that is lossless.  If the board
//   hash is identical after the space bar then nothing happened at all -- not
//   even an anti-tank turn, since AntiTank() runs inside the same key-consuming
//   tick and any move it made would show -- so the successor is the state we
//   fired from, which is already in this expansion's closure, and every
//   continuation through it is some other (Goto, Shoot) pair of the same
//   parent.  Dropping it saves a snapshot per reachable pose and costs nothing.
//
//   The closure is capped (ClosureNodes / ClosureDepth), and that is *not*
//   lossless.  It is the one place layer 1 gives up completeness, so it is a
//   knob with a number on it rather than a hidden constant.
//
// The escape hatch: MoveOnlyK pure-Goto successors, the ones ending nearest the
// flag, are kept alongside the shot successors.  Without them a level whose
// solution is longer than one closure and needs no shot at all -- 15% of
// Beginner-I's sampled levels have .ghs shots = 0 -- would have no successors
// to expand and the beam would die at depth 1.
//
// **And it is worth saying plainly at the top of the file: over the corpus this
// loses to the raw beam, and it does not ship in the portfolio.**  On levels
// layer 0 cannot solve it wins decisively (18 -> 28, 23 -> 31, 33 -> 38 of 60,
// at 150k / 400k / 1M nodes).  Over 4,185 real levels it loses -- 395 -> 381
// run first, 395 -> 354 run last -- because most solvable levels are ones the
// raw beam gets easily and every node spent here is a node taken from it.  So
// RunMacro is off by default and this runs as a *second pass* over a campaign's
// failures instead (tools/second_pass.sh): 395 -> 416, nothing lost.  The full
// measurement is in PROGRESS.md's layer 1 section.
using System;
using System.Collections.Generic;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public sealed partial class Solver
    {
        /// ActionKeys minus the space bar.  Spelled out rather than sliced out
        /// of Engine.ActionKeys so that reordering that array cannot silently
        /// turn a movement closure into one that fires.
        private static readonly byte[] MoveKeys =
        {
            (byte)Engine.VK_UP, (byte)Engine.VK_RIGHT,
            (byte)Engine.VK_DOWN, (byte)Engine.VK_LEFT,
        };

        private const byte Fire = (byte)Engine.VK_SPACE;

        private readonly List<EngineSnapshot> _closure = new List<EngineSnapshot>();
        private readonly HashSet<ulong> _local = new HashSet<ulong>();

        // ---- the beam over macro-steps ------------------------------------

        private SolveResult MacroBeam(EngineSnapshot root)
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

            for (int depth = 0; depth < _opt.MacroDepth && frontier.Count > 0; depth++)
            {
                layer.Clear();
                foreach (Node parent in frontier)
                {
                    if (OutOfBudget) { r.Stop = "budget"; Recycle(frontier, next); return r; }
                    if (Expand(parent.S, seen, layer, next, fx, fy))
                    {
                        Recycle(frontier, next);
                        return Won(r);
                    }
                    // One expansion can offer a successor per reachable pose,
                    // so a full layer would be beam x closure snapshots live at
                    // once -- tens of thousands, times --jobs workers.  Trim as
                    // we go instead: later parents still compete against the
                    // survivors, and the live set stays O(beam).
                    if (next.Count > 4 * _opt.MacroBeamWidth) Cut(next);
                }

                Cut(next);

                foreach (Node n in frontier) Give(n.S);
                frontier.Clear();
                (frontier, next) = (next, frontier);
                Close(frontier, seen);
            }

            r.Stop = frontier.Count == 0 ? "macro-dead-end" : "macro-depth";
            Recycle(frontier, next);
            return r;
        }

        /// Rank by work-to-flag, then by the cheaper keystream, and keep the
        /// best MacroBeamWidth.  Same contract as the layer-0 beam: this is a
        /// beam, so there is no optimality claim to lose.
        private void Cut(List<Node> next)
        {
            if (next.Count <= _opt.MacroBeamWidth) return;
            next.Sort(static (a, b) => a.H != b.H ? a.H - b.H : a.G - b.G);
            for (int i = _opt.MacroBeamWidth; i < next.Count; i++) Give(next[i].S);
            next.RemoveRange(_opt.MacroBeamWidth, next.Count - _opt.MacroBeamWidth);
        }

        // ---- one macro-expansion: the Goto closure, then a shot from each --

        /// Returns true when the engine is left standing on the flag, in which
        /// case its RecBuffer prefix is the solution and the caller must not
        /// touch the engine before reading it.
        private bool Expand(EngineSnapshot at, HashSet<ulong> seen, HashSet<ulong> layer,
                            List<Node> next, int fx, int fy)
        {
            List<EngineSnapshot> closure = _closure;
            HashSet<ulong> local = _local;
            closure.Clear();
            local.Clear();

            _e.Restore(at);
            local.Add(_e.StateHash());
            closure.Add(CopyOf(at));
            int startKeys = at.KeyLen;

            for (int head = 0; head < closure.Count && closure.Count < _opt.ClosureNodes; head++)
            {
                EngineSnapshot s = closure[head];
                if (s.KeyLen - startKeys >= _opt.ClosureDepth) continue;

                foreach (byte key in MoveKeys)
                {
                    if (OutOfBudget) { Drain(closure); return false; }
                    _e.Restore(s);
                    _nodes++;
                    StepResult step = _e.ApplyKey(key, _opt.TickCap);
                    if (step == StepResult.Win) { Drain(closure); return true; }
                    if (step != StepResult.Ok) continue;        // dead or spinning
                    if (_e.Game.RecP >= (uint)_opt.MaxKeys) continue;
                    if (!local.Add(_e.StateHash())) continue;
                    closure.Add(_e.Snapshot(Take()));
                }
            }

            // A shot from every pose the closure reached.
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
                if (after == before) continue;                 // the lossless prune
                if (!Fresh(after, seen, layer)) continue;
                next.Add(new Node
                {
                    S = _e.Snapshot(Take()),
                    G = (int)_e.Game.RecP,
                    H = _h.WorkDistance(_e),
                    Hash = after,
                });
            }

            KeepNearest(closure, seen, layer, next, fx, fy);
            Drain(closure);
            return false;
        }

        /// The pure-Goto successors: the MoveOnlyK closure states that end
        /// nearest the flag.  Ranked by manhattan rather than by WorkDistance,
        /// because WorkDistance is a Dijkstra and this would then run one over
        /// every state in the closure instead of over the handful that survive.
        private void KeepNearest(List<EngineSnapshot> closure, HashSet<ulong> seen,
                                 HashSet<ulong> layer, List<Node> next, int fx, int fy)
        {
            int k = _opt.MoveOnlyK;
            if (k <= 0 || fx < 0 || closure.Count <= 1) return;
            if (k > 16) k = 16;

            Span<int> bestAt = stackalloc int[16];
            Span<int> bestD = stackalloc int[16];
            int have = 0;

            for (int i = 1; i < closure.Count; i++)          // 0 is the parent itself
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
                if (!Fresh(h, seen, layer)) continue;
                next.Add(new Node
                {
                    S = _e.Snapshot(Take()), G = s.KeyLen, H = _h.WorkDistance(_e), Hash = h,
                });
            }
        }

        private void Drain(List<EngineSnapshot> closure)
        {
            foreach (EngineSnapshot s in closure) Give(s);
            closure.Clear();
        }

        /// The flag never moves, so this runs once per level rather than once
        /// per state.  -1 means the level has none, in which case the macro beam
        /// still runs -- ApplyKey's Win test is Engine.OnFlag, not this.
        private void FindFlag(out int fx, out int fy)
        {
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                    if (_e.Game.PF[x, y] == Obj.Flag) { fx = x; fy = y; return; }
            fx = fy = -1;
        }
    }
}
