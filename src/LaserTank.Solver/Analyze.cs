// Layer 6's instrument: the *read* -- what has to change, and what can change it.
//
// **Why this exists.**  Every layer so far ranks states.  Layer 4 measured how
// far that can go and the answer was "not much further": the state the winner
// went through is in the expansion's output 97.6% of the time and the sort
// loses it, and a fitted linear evaluation is still wrong four times in five.
// Meanwhile a human looks at `LaserTank.lvl` 4 for two seconds and says: the
// creek separates me from the flag, there is exactly one block, it is on the far
// bank so I cannot push it, therefore I have to shoot it in -- and the only line
// of fire that reaches it goes round two mirrors.  That is not a better ranking
// of two hundred successors.  It is a *derivation of which successors exist for
// a reason*, done before the search starts.
//
// **The two halves, and the discipline is layer 2's.**  Layer 2 already learned
// the hard way that a model of what the tank can do is a second implementation
// of the game: version 1 of its obstacle derivation priced cells with a Dijkstra
// and reported "no obstacle at all" on 62% of expansions, because a price list
// does not know the cell is covered by an anti-tank or which mouth a tunnel
// pairs with.  Version 2 ran the movement closure *first* and asked the engine.
// This file is that same split, one step further out:
//
//   * **what must change** is a model -- the priced Dijkstra from the flag,
//     stopped at the cells the tank actually stands in, i.e. exactly layer 2's
//     Heuristic.FrontierObstacles;
//   * **what can change it** is not a model at all.  Every board change the tank
//     can make right now is enumerated by *making* it: a PF-preserving pose
//     closure, then all five keys from every pose, and whatever `Game.PF` comes
//     back different is an effect, carrying the pose and key that produced it as
//     its witness.  Nothing here knows that a laser bounces off a mirror, that a
//     block sinks in water, or that shooting a movable block shoves it one cell.
//     Level 4's mirror route is discovered because firing up from column 0 was
//     tried and the block at (2,2) moved.
//
// So the mirror analysis costs no mirror code and cannot drift from the engine
// the way a hand-written laser model would.  It costs 5 x |poses| ApplyKey calls
// once -- a few thousand, against the 150,000 a campaign gives a whole level.
//
// **What it is not.**  It is a read, not a plan and not a search: one ply deep,
// from one state.  What it produces is the fact a beam ranking by WorkDistance
// cannot see and the ferry term approximates with a manhattan distance -- "these
// three effects move a block nearer the water that is in your way, and nothing
// else on this board does".  Turning that into a subgoal ordering the search
// commits to is the next thing; measuring what it derives against seven
// hand-recorded playthroughs comes first, which is what this file is for.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LaserTank.Core;

namespace LaserTank.Solver
{
    /// One board change the tank can make from the state analysed, with the pose
    /// and keypress that made it.  `Cells` is every cell of `Game.PF` that came
    /// back different -- two for a push (the square vacated and the square
    /// filled), one for a brick shot away, one for a mirror rotated.
    public sealed class Effect
    {
        public byte Key;
        public int PoseX, PoseY, PoseDir;
        public int Witnesses = 1;
        public int[] Cells = Array.Empty<int>();
        public byte[] Was = Array.Empty<byte>(), Now = Array.Empty<byte>();

        /// A shot whose changed cells are not on the straight ray from the pose.
        /// Purely a label: the effect is real either way, and this only says the
        /// laser must have turned a corner, i.e. that a mirror is load-bearing.
        public bool Indirect;

        /// Set when exactly one cell stopped being a movable block and exactly
        /// one started being one -- the shape of a push or a laser shove.  Left
        /// at -1 when the block did not survive the change, which is the *fill*:
        /// it went into water and both cells came back dirt.
        public int BlockFrom = -1, BlockTo = -1;

        /// Water cells this effect turned into something a tank can drive on.
        public List<int> Filled = new List<int>();

        public bool IsShot;

        /// What changed, as a string, so two effects can be compared without
        /// comparing the states that produced them.  This is what lets the
        /// instrument ask "is the board change the human made next one of the
        /// ones this read named?" -- the human's change is a board delta and
        /// nothing else, so a delta is what the read has to be keyed by.
        public string Sig = "";

        /// The state this effect leaves behind, kept only long enough to ask
        /// whether the tank can stand anywhere new afterwards.  Returned to the
        /// snapshot pool at the end of the read.
        internal EngineSnapshot After;

        /// The tank can stand on cells after this change that it could not
        /// stand on before -- or it can now reach the flag.  This is the one
        /// notion of progress that needs no idea of what the obstacle *is*, and
        /// it is the one the ferry terms below cannot express: stopping a
        /// conveyor ride, shooting the anti-tank that owns a corridor, blowing a
        /// brick out of a doorway all read the same way, as somewhere new to be.
        public int Opens;

        public static string SigOf(byte[] before, byte[] after, bool shot)
        {
            StringBuilder b = new StringBuilder(shot ? "s" : "m");
            for (int c = 0; c < 256; c++)
                if (after[c] != before[c])
                    b.Append(':').Append(c).Append('=').Append(after[c]);
            return b.ToString();
        }
    }

    /// The derived read of one level.  Every count in here is either a scan of
    /// `Game.PF` or something the engine did when it was asked to.
    public sealed class Read
    {
        public int Level;
        public string Name = "", Author = "", Hint = "";
        public int Diff;
        public int FlagCell = -1;
        public bool FlagReachable;
        public int Poses, RegionCells, Work;
        /// Heuristic.RouteObstacles: -1 when no priced route to the flag exists
        /// at all, which is a different thing from a route with nothing on it.
        public int RouteObstacles;
        public List<int> Barrier = new List<int>();      // cells priced above an empty step
        public List<int> Threats = new List<int>();      // anti-tanks named by the route
        public List<int> RouteWater = new List<int>();   // the subset of Barrier that is water
        public List<int> Blocks = new List<int>();
        public List<Effect> Effects = new List<Effect>();
        public List<Effect> OnBarrier = new List<Effect>();
        public List<Effect> Toward = new List<Effect>(); // move a block nearer route water
        public List<Effect> Opens = new List<Effect>();  // leave the tank able to stand somewhere new
        public string Verdict = "";
        public string Why = "";
    }

    public sealed partial class Solver
    {
        private static readonly string[] ObjName =
        {
            "dirt", "tank", "flag", "water", "solid", "block", "bricks",
            "anti-tank^", "anti-tank>", "anti-tankv", "anti-tank<",
            "mirror", "mirror", "mirror", "mirror",
            "conveyor", "conveyor", "conveyor", "conveyor",
            "crystal", "roto", "roto", "roto", "roto", "ice", "thin ice",
        };

        /// Game.Tank.Dir is 1..4, not 0..3 -- MoveTank's switch is `case 1:` up,
        /// `case 2:` right, `case 3:` down, `case 4:` left (Engine.cs:657).  The
        /// index-0 entry is there so a direction that never happens prints as
        /// itself instead of throwing, and Step() below refuses to walk a ray
        /// for it rather than stepping (0,0) forever.
        private static readonly string[] DirName = { "?", "up", "right", "down", "left" };

        private static bool Step(int dir, out int dx, out int dy)
        {
            dx = dir == 2 ? 1 : dir == 4 ? -1 : 0;
            dy = dir == 1 ? -1 : dir == 3 ? 1 : 0;
            return dx != 0 || dy != 0;
        }

        private static string NameOf(byte cell) =>
            Obj.IsTunnel(cell) ? "tunnel " + Obj.GetTunnelID(cell)
            : cell < ObjName.Length ? ObjName[cell] : "?" + cell;

        private static string At(int cell) => "(" + (cell >> 4) + "," + (cell & 15) + ")";

        /// The whole read.  Not a solve: it takes no budget and writes no .lpb.
        public Read Analyze(int level)
        {
            EngineSnapshot root = Root(level);
            _clock = System.Diagnostics.Stopwatch.StartNew();
            _nodes = 0;
            _stageNodes = long.MaxValue;
            _stageMs = long.MaxValue;

            Read r = AnalyzeAt(root);
            r.Level = level;
            r.Name = Level.LName;
            r.Author = Level.Author;
            r.Hint = Level.Hint;
            r.Diff = Level.SDiff;
            return r;
        }

        /// The same read from an arbitrary state, which is what makes it
        /// measurable: replay a winning recording, stop at each board change,
        /// and ask whether what the human did next is in what this derived.
        private Read AnalyzeAt(EngineSnapshot root)
        {
            Read r = new Read();

            _e.Restore(root);
            byte[] board = new byte[256];
            CopyBoard(board);
            for (int c = 0; c < 256; c++)
            {
                if (board[c] == Obj.Flag) r.FlagCell = c;
                if (board[c] == Obj.Block) r.Blocks.Add(c);
            }

            // ---- half one: where the tank stands, by standing there ---------
            bool[] region = new bool[256];
            List<EngineSnapshot> poses = PoseClosure(root, board, region, out bool won);
            r.Poses = poses.Count;
            r.FlagReachable = won;
            for (int c = 0; c < 256; c++) if (region[c]) r.RegionCells++;

            // ---- half two: what is in the way, by pricing it ----------------
            _e.Restore(root);
            r.Work = _h.WorkDistance(_e);
            r.RouteObstacles = _h.RouteObstacles;
            int[] into = new int[64];
            int n = _h.FrontierObstacles(_e, region, into);
            for (int i = 0; i < n; i++)
            {
                byte cell = board[into[i]];
                if (cell >= Obj.AntiTankUp && cell <= Obj.AntiTankLeft)
                {
                    if (!r.Threats.Contains(into[i])) r.Threats.Add(into[i]);
                }
                else
                {
                    r.Barrier.Add(into[i]);
                    if (cell == Obj.Water) r.RouteWater.Add(into[i]);
                }
            }

            // ---- half three: what can change it, by changing it -------------
            Enumerate(poses, board, region, r);
            Drain(poses);

            Classify(r);
            return r;
        }

        /// Every pose the tank can reach without touching the playfield.
        ///
        /// Layer 5's closure, minus its successor emission: a state whose PF
        /// still matches `board` is another pose, and one whose PF does not is
        /// left alone here and re-derived in Enumerate as an effect.  The bound
        /// is the pose count -- 16x16x4, times whatever slide state the engine
        /// carries -- which is why this terminates well inside its cap, and why
        /// layer 5's --push-trace has never reported a truncated closure.
        private List<EngineSnapshot> PoseClosure(EngineSnapshot root, byte[] board,
                                                 bool[] region, out bool won)
        {
            List<EngineSnapshot> closure = new List<EngineSnapshot>();
            HashSet<ulong> local = new HashSet<ulong>();
            won = false;

            _e.Restore(root);
            local.Add(_e.StateHash());
            closure.Add(CopyOf(root));
            region[_e.Game.Tank.X * 16 + _e.Game.Tank.Y] = true;

            byte[] now = new byte[256];
            for (int head = 0; head < closure.Count && closure.Count < 4096; head++)
            {
                EngineSnapshot s = closure[head];
                // ApplyKey writes RecBuffer[RecP] before it ticks, so a state
                // already at the keystream cap must never be expanded -- the
                // same guard Macro.cs and Push.cs carry, and it is not
                // theoretical here: --read-dump analyses from a boundary deep
                // inside a recording, so the closure starts with 800 of the
                // 1,200 default keys already spent.
                if (s.KeyLen >= _opt.MaxKeys) continue;
                foreach (byte key in MoveKeys)
                {
                    _e.Restore(s);
                    _nodes++;
                    StepResult step = _e.ApplyKey(key, _opt.TickCap);
                    if (step == StepResult.Win) { won = true; continue; }
                    if (step != StepResult.Ok) continue;
                    if (_e.Game.RecP >= (uint)_opt.MaxKeys) continue;
                    CopyBoard(now);
                    if (!now.AsSpan().SequenceEqual(board)) continue;   // a board change
                    if (!local.Add(_e.StateHash())) continue;
                    closure.Add(_e.Snapshot(Take()));
                    region[_e.Game.Tank.X * 16 + _e.Game.Tank.Y] = true;
                }
            }
            return closure;
        }

        /// Fire and drive from every pose; keep whatever came back different.
        ///
        /// Deduplicated by *what changed* and not by which pose changed it,
        /// because forty poses in a corridor all fire the same shot and the read
        /// wants one line for it with a witness, not forty.
        private void Enumerate(List<EngineSnapshot> poses, byte[] board, bool[] region,
                               Read r)
        {
            Dictionary<string, Effect> byChange = new Dictionary<string, Effect>();
            byte[] now = new byte[256];
            List<int> changed = new List<int>();
            byte[] keys = new byte[MoveKeys.Length + 1];
            Array.Copy(MoveKeys, keys, MoveKeys.Length);
            keys[MoveKeys.Length] = Fire;

            foreach (EngineSnapshot p in poses)
            {
                if (p.KeyLen >= _opt.MaxKeys) continue;
                foreach (byte key in keys)
                {
                    _e.Restore(p);
                    int px = _e.Game.Tank.X, py = _e.Game.Tank.Y, pd = _e.Game.Tank.Dir;
                    _nodes++;
                    StepResult step = _e.ApplyKey(key, _opt.TickCap);
                    if (step != StepResult.Ok) continue;
                    CopyBoard(now);

                    changed.Clear();
                    for (int c = 0; c < 256; c++) if (now[c] != board[c]) changed.Add(c);
                    if (changed.Count == 0) continue;

                    string k = Effect.SigOf(board, now, key == Fire);
                    if (byChange.TryGetValue(k, out Effect had)) { had.Witnesses++; continue; }

                    Effect e = new Effect
                    {
                        Sig = k,
                        Key = key,
                        IsShot = key == Fire,
                        PoseX = px, PoseY = py, PoseDir = pd,
                        Cells = changed.ToArray(),
                    };
                    e.Was = new byte[e.Cells.Length];
                    e.Now = new byte[e.Cells.Length];
                    for (int i = 0; i < e.Cells.Length; i++)
                    {
                        e.Was[i] = board[e.Cells[i]];
                        e.Now[i] = now[e.Cells[i]];
                        if (e.Was[i] == Obj.Water && e.Now[i] != Obj.Water) e.Filled.Add(e.Cells[i]);
                    }
                    Describe(e, px, py, pd);
                    e.After = _e.Snapshot(Take());
                    byChange[k] = e;
                }
            }

            r.Effects.AddRange(byChange.Values);
            r.Effects.Sort((a, b) => a.Cells[0].CompareTo(b.Cells[0]));

            HashSet<int> barrier = new HashSet<int>(r.Barrier);
            foreach (Effect e in r.Effects)
            {
                foreach (int c in e.Cells)
                    if (barrier.Contains(c)) { r.OnBarrier.Add(e); break; }
                if (e.BlockFrom >= 0 && e.BlockTo >= 0 && r.RouteWater.Count > 0
                    && Nearest(e.BlockTo, r.RouteWater) < Nearest(e.BlockFrom, r.RouteWater))
                    r.Toward.Add(e);
            }

            OpensUp(region, r);
        }

        /// Which of the effects leave the tank able to stand somewhere it could
        /// not stand before.
        ///
        /// This is the third derivation and the one that needs no theory of the
        /// obstacle at all: run the pose closure again on the board the effect
        /// produced and see whether the region grew.  Stopping a conveyor ride
        /// by dropping a block on it, shooting the anti-tank that owns a
        /// corridor, opening a doorway -- the read has no name for any of those
        /// and does not need one, because all three come back as *somewhere new
        /// to be*.  A change that opens nothing is not thereby useless (a ferry
        /// opens nothing until its last push), which is why this joins the other
        /// two rather than replacing them.
        ///
        /// It costs a closure per effect, and that is the reason for the cap:
        /// the median expansion offers four effects and the 90th percentile
        /// eleven, so the usual bill is four closures, but a board that offers
        /// two hundred would turn the read from cheap into a search.  Past the
        /// cap the question is simply not asked, and Opens stays 0 -- a missing
        /// label, never a wrong one.
        private void OpensUp(bool[] region, Read r)
        {
            byte[] after = new byte[256];
            if (r.Effects.Count <= _opt.ReadOpensCap)
            {
                foreach (Effect e in r.Effects)
                {
                    _e.Restore(e.After);
                    CopyBoard(after);
                    bool[] then = new bool[256];
                    List<EngineSnapshot> poses = PoseClosure(e.After, after, then, out bool won);
                    Drain(poses);

                    int grew = 0;
                    for (int c = 0; c < 256; c++) if (then[c] && !region[c]) grew++;
                    e.Opens = won ? grew + 1 : grew;
                    if (e.Opens > 0) r.Opens.Add(e);
                }
            }
            foreach (Effect e in r.Effects) { Give(e.After); e.After = null; }
        }

        /// The block bookkeeping and the mirror label, both read off the delta.
        private static void Describe(Effect e, int px, int py, int pd)
        {
            int from = -1, to = -1;
            for (int i = 0; i < e.Cells.Length; i++)
            {
                if (e.Was[i] == Obj.Block && e.Now[i] != Obj.Block) from = e.Cells[i];
                if (e.Was[i] != Obj.Block && e.Now[i] == Obj.Block) to = e.Cells[i];
            }
            e.BlockFrom = from;
            e.BlockTo = to;

            if (!e.IsShot) return;

            // Collinearity, and deliberately nothing more.  A shot every one of
            // whose changed cells lies on the straight line the tank is facing
            // is one the laser could have made without turning; a shot that
            // changed a cell off that line turned a corner somewhere, and the
            // only thing on a LaserTank board that turns a laser is a mirror.
            //
            // Note what is *not* here: any rule about where the beam stops.
            // Deciding that a shot is direct because the laser "would have hit"
            // the first non-dirt cell means owning CheckLLoc's case list -- the
            // crystal it passes through, the ice it bounces on, the anti-tank
            // that eats it -- which is the second implementation of the game
            // this project refuses to acquire.  Collinearity needs none of it,
            // and the label is only a label: the effect is real either way.
            if (!Step(pd, out int dx, out int dy)) { e.Indirect = true; return; }
            foreach (int c in e.Cells)
            {
                int ax = (c >> 4) - px, ay = (c & 15) - py;
                bool onLine = dx != 0 ? ay == 0 && ax * dx > 0 : ax == 0 && ay * dy > 0;
                if (!onLine) { e.Indirect = true; return; }
            }
        }

        private static int Nearest(int cell, List<int> targets)
        {
            int ox = cell >> 4, oy = cell & 15, best = int.MaxValue;
            foreach (int t in targets)
            {
                int d = Math.Abs((t >> 4) - ox) + Math.Abs((t & 15) - oy);
                if (d < best) best = d;
            }
            return best;
        }

        /// The verdict, and it is a decision list rather than a classifier.
        ///
        /// Each arm names the thing a player would say out loud, and every fact
        /// it tests was derived above rather than pattern-matched on the level
        /// data: "no shot on this board does anything" is the enumeration coming
        /// back with zero shot effects, not a scan for bricks.
        private static void Classify(Read r)
        {
            int shots = 0, pushes = 0;
            foreach (Effect e in r.Effects) if (e.IsShot) shots++; else pushes++;

            if (r.FlagReachable)
            {
                r.Verdict = "OPEN";
                r.Why = "the flag is reachable without changing the board at all";
                return;
            }
            if (r.Barrier.Count == 0)
            {
                // Three different boards come out of layer 2's obstacle
                // derivation as "nothing", and calling them all walled off was
                // wrong on two of the first nine levels.  RouteObstacles tells
                // them apart: it is -1 only when the Dijkstra never settled the
                // tank at all.
                if (r.RouteObstacles < 0)
                {
                    r.Verdict = "WALLED";
                    r.Why = "no priced route to the flag exists on this board at all -- "
                          + "every way in is closed to the price list, so the route has "
                          + "to be made rather than found";
                    return;
                }
                if (r.Threats.Count > 0)
                {
                    r.Verdict = "GAUNTLET";
                    r.Why = "the route to the flag crosses nothing that has to be cleared "
                          + "and " + r.Threats.Count + " anti-tanks cover it -- what is in "
                          + "the way is fire, not terrain";
                    return;
                }
                r.Verdict = "RIDE";
                r.Why = "the route to the flag crosses nothing that has to be cleared and "
                      + "the tank still cannot get there: it runs over cells the tank "
                      + "cannot stop on, i.e. a conveyor, a slide or a tunnel";
                return;
            }

            if (r.RouteWater.Count > 0)
            {
                bool mirrors = r.Toward.Count > 0;
                foreach (Effect e in r.Toward) if (!e.Indirect) { mirrors = false; break; }
                string how =
                    r.Toward.Count == 0 ? "and nothing the tank can do right now brings a "
                                          + "block nearer one of them"
                    : mirrors ? "moved by shooting, and every shot that moves one is "
                                + "mirror-routed"
                    : "moved by shooting";
                // "Sokoban" is not a size, it is a shape: nothing in the way
                // but holes, and exactly as many blocks as holes, so every
                // block is spoken for and no shot is a spare.  Level 6 is that
                // exactly; level 1 has bricks on the route as well and is not.
                bool pure = r.RouteWater.Count == r.Barrier.Count
                            && r.Blocks.Count == r.RouteWater.Count
                            && r.Threats.Count == 0;
                r.Verdict = (pure ? "SOKOBAN x" : "FERRY x") + r.RouteWater.Count;
                r.Why = r.RouteWater.Count + " water cell" + (r.RouteWater.Count == 1 ? "" : "s")
                      + " in the way, " + r.Blocks.Count + " block"
                      + (r.Blocks.Count == 1 ? "" : "s") + " on the board, " + how
                      + (r.Blocks.Count == r.RouteWater.Count
                         ? " -- as many blocks as holes, so every one of them is needed" : "");
                return;
            }

            if (r.OnBarrier.Count > 0)
            {
                r.Verdict = "DEMOLITION";
                r.Why = r.OnBarrier.Count + " of the " + r.Effects.Count
                      + " available board changes land on the barrier";
                return;
            }

            r.Verdict = "SETUP";
            r.Why = "nothing the tank can do right now touches the barrier -- " + shots
                  + " shot effects and " + pushes + " by driving, none of them on it"
                  + (r.Opens.Count > 0
                     ? "; " + r.Opens.Count + " of them do put the tank somewhere new to stand, "
                       + "which is where the first move has to come from"
                     : ", and none of them opens anywhere new to stand either");
        }

        // ---- the report ----------------------------------------------------

        public static string Format(Read r, string collection, byte[] board)
        {
            StringBuilder b = new StringBuilder();
            b.Append(collection).Append(' ').Append(r.Level)
             .Append("  \"").Append(r.Name).Append("\"  by ").Append(r.Author)
             .Append("  (").Append(Tier(r.Diff)).Append(")\n");
            foreach (string line in r.Hint.Replace("\r", "").Split('\n'))
                if (line.Trim().Length > 0) b.Append("  hint: ").Append(line.Trim()).Append('\n');
            b.Append('\n');

            b.Append("  board     ").Append(Census(board)).Append('\n');
            b.Append("  reach     the tank stands in ").Append(r.RegionCells)
             .Append(" cells / ").Append(r.Poses).Append(" poses; the flag ")
             .Append(r.FlagCell < 0 ? "is missing" : At(r.FlagCell))
             .Append(r.FlagReachable ? " IS among them" : " is not among them").Append('\n');

            b.Append("  route     WorkDistance ").Append(r.Work).Append("; in the way: ");
            if (r.Barrier.Count == 0) b.Append("nothing priced (no route settled)");
            else for (int i = 0; i < r.Barrier.Count; i++)
                b.Append(i > 0 ? ", " : "").Append(NameOf(board[r.Barrier[i]]))
                 .Append(' ').Append(At(r.Barrier[i]));
            b.Append('\n');

            if (r.Threats.Count > 0)
            {
                b.Append("  threats   ");
                for (int i = 0; i < r.Threats.Count; i++)
                    b.Append(i > 0 ? ", " : "").Append(NameOf(board[r.Threats[i]]))
                     .Append(' ').Append(At(r.Threats[i]));
                b.Append("  -- named by the route, not by the board\n");
            }

            int shots = 0, pushes = 0, indirect = 0;
            foreach (Effect e in r.Effects)
                if (e.IsShot) { shots++; if (e.Indirect) indirect++; } else pushes++;
            b.Append("  effects   ").Append(r.Effects.Count)
             .Append(" distinct board changes reachable right now -- ").Append(shots)
             .Append(" by shooting (").Append(indirect).Append(" mirror-routed), ")
             .Append(pushes).Append(" by driving\n");

            b.Append("     on the barrier: ").Append(r.OnBarrier.Count).Append('\n');
            foreach (Effect e in Head(r.OnBarrier, 4)) b.Append("        ").Append(Line(e));
            b.Append("     open somewhere new to stand: ").Append(r.Opens.Count).Append('\n');
            foreach (Effect e in Head(r.Opens, 4)) b.Append("        ").Append(Line(e));

            if (r.RouteWater.Count > 0)
            {
                b.Append("     ferry: ").Append(r.RouteWater.Count).Append(" water cell")
                 .Append(r.RouteWater.Count == 1 ? "" : "s").Append(" to fill, ")
                 .Append(r.Blocks.Count).Append(" block")
                 .Append(r.Blocks.Count == 1 ? "" : "s").Append(" on the board at");
                foreach (int c in Head(r.Blocks, 8)) b.Append(' ').Append(At(c));
                if (r.Blocks.Count > 8) b.Append(" ...");
                b.Append('\n');
                b.Append("            ").Append(r.Toward.Count)
                 .Append(" available effects move a block nearer the water it has to fill\n");
                foreach (Effect e in Head(r.Toward, 4)) b.Append("        ").Append(Line(e));
            }

            b.Append("  verdict   ").Append(r.Verdict).Append(": ").Append(r.Why).Append('\n');
            return b.ToString();
        }

        // ---- the read as a ranking key, for layer 5 -------------------------
        //
        // Everything above this line is an instrument.  This is the part a
        // search calls, and it is deliberately a *tier* rather than a score:
        // Cut() sorts on Tier before H, so the read can say "these successors
        // exist for a reason and the rest are filler" without being able to
        // reorder anything inside either group, and without being able to admit
        // a successor the expansion did not already offer.  Same contract layer
        // 4's Rank() has -- consulted after the successor exists, never before.
        //
        // Why a tier and not a term added to H: the read's answer is a set, not
        // a number.  "This shot lands on the brick that is in the way" is not
        // three points better than a shot that does not; it is a different kind
        // of move.  Layer 5's ferry term is the version of this that *is* a
        // number, and the measurement it comes from (PROGRESS, layer 5) says a
        // weight past 2 makes the ascent shorter and deeper -- which is what
        // trying to express a set membership as a distance looks like.

        private readonly bool[] _readRegion = new bool[256];
        private readonly int[] _readInto = new int[64];
        private readonly List<int> _readBarrier = new List<int>();
        private readonly List<int> _readWater = new List<int>();

        /// Derive, from the parent state and the poses its closure reached, the
        /// barrier and the water on the route.  One Dijkstra per expansion,
        /// against an expansion that costs some 4,500 ApplyKey calls.
        ///
        /// `poses` is layer 5's PF-preserving closure, which is exactly the
        /// `reached` set layer 2's FrontierObstacles wants and for exactly the
        /// same reason: it is where the tank demonstrably got to, not where a
        /// price list thinks it could go.
        private void ReadDerive(EngineSnapshot at, List<EngineSnapshot> poses)
        {
            _readBarrier.Clear();
            _readWater.Clear();
            Array.Clear(_readRegion, 0, 256);
            foreach (EngineSnapshot s in poses) _readRegion[s.Tank.X * 16 + s.Tank.Y] = true;

            _e.Restore(at);
            int n = _h.FrontierObstacles(_e, _readRegion, _readInto);
            for (int i = 0; i < n; i++)
            {
                byte cell = _e.Game.PF[_readInto[i] >> 4, _readInto[i] & 15];
                if (cell >= Obj.AntiTankUp && cell <= Obj.AntiTankLeft) continue;
                _readBarrier.Add(_readInto[i]);
                if (cell == Obj.Water) _readWater.Add(_readInto[i]);
            }
        }

        /// Does the change from `before` to `after` advance, by the read?
        ///
        /// Two of the three derivations, and they are the two that are free
        /// here -- the third (`opens`) needs a second pose closure and is priced
        /// separately in Push.cs.  The delta is read off the two boards rather
        /// than tracked through the engine, so this cannot disagree with what
        /// the engine actually did.
        private bool ReadAdvances(byte[] before, byte[] after)
        {
            if (_readBarrier.Count == 0) return false;

            int from = -1, to = -1;
            bool onBarrier = false;
            for (int c = 0; c < 256; c++)
            {
                if (after[c] == before[c]) continue;
                if (!onBarrier && _readBarrier.Contains(c)) onBarrier = true;
                if (before[c] == Obj.Block && after[c] != Obj.Block) from = c;
                if (before[c] != Obj.Block && after[c] == Obj.Block) to = c;
            }
            if (onBarrier) return true;

            // The ferry: a block that ended nearer the water it has to fill than
            // it started.  Manhattan, for the reason Heuristic.RouteFerry gives
            // -- whether the block can *actually* be pushed there is a question
            // about MoveObj, ice and where the tank can stand, i.e. the second
            // implementation of the game this project does not have.
            if (from < 0 || to < 0 || _readWater.Count == 0) return false;
            return NearestOf(to, _readWater) < NearestOf(from, _readWater);
        }

        private static int NearestOf(int cell, List<int> targets)
        {
            int ox = cell >> 4, oy = cell & 15, best = int.MaxValue;
            foreach (int t in targets)
            {
                int d = Math.Abs((t >> 4) - ox) + Math.Abs((t & 15) - oy);
                if (d < best) best = d;
            }
            return best;
        }

        /// The third derivation, priced: after this change, can the tank stand
        /// somewhere it could not stand before?
        ///
        /// This is the one that carries the levels where the other two are empty
        /// by construction -- no water on the route, nothing on the barrier to
        /// land on -- which the read-dump measured as RIDE, GAUNTLET and SETUP,
        /// 81-97% of their board changes named by this and none by the others.
        /// It costs a whole second pose closure, so the caller decides how many
        /// successors are worth asking about; here it only answers.
        /// The cheap half of the same question, for when a pose closure per
        /// successor is not affordable: did the tank's reachable region grow?
        ///
        /// See Heuristic.TankRegion for what it approximates, why a flood is
        /// allowed to stand in for a closure when the answer only picks a tier,
        /// and -- the part that cost a measurement -- why it floods from the
        /// *tank* rather than from the flag.
        private bool ReadOpensCheap(EngineSnapshot after, int wasRegion)
        {
            _e.Restore(after);
            return _h.TankRegion(_e) > wasRegion;
        }

        private bool ReadOpens(EngineSnapshot after)
        {
            byte[] board = new byte[256];
            _e.Restore(after);
            CopyBoard(board);
            bool[] then = new bool[256];
            List<EngineSnapshot> poses = PoseClosure(after, board, then, out bool won);
            Drain(poses);
            if (won) return true;
            for (int c = 0; c < 256; c++) if (then[c] && !_readRegion[c]) return true;
            return false;
        }

        /// The read, measured the way layer 4's ranking was measured.
        ///
        /// A winning recording is a keystream, so replaying it one key at a time
        /// gives the exact sequence of boards a perfect search would have
        /// visited.  Between two board changes the tank only *moves*, so the
        /// next change is by construction reachable from the pose closure of the
        /// state the previous one left behind -- which means the read taken at
        /// that state can be asked a question with a yes-or-no answer: is the
        /// change the human made next one of the ones you named?
        ///
        /// Two answers, and they are different questions:
        ///
        ///   `in_effects` -- was it in the enumeration at all.  This is the
        ///   coverage number, layer 4's 97.6% in this layer's units, and it is
        ///   also a check on the enumeration itself: a *no* means either the
        ///   closure truncated or the analysis is not seeing something the
        ///   engine did, and both are bugs rather than findings.
        ///
        ///   `in_advance` -- was it in the subset the read calls advancing,
        ///   i.e. it lands on the barrier or it moves a block nearer the water
        ///   the route crosses.  This is the number that decides whether the
        ///   read is worth searching by: `advance` against `effects` is the
        ///   branching factor a search that committed to it would pay, and
        ///   `in_advance` is what that commitment would cost.
        public int ReadDump(int level, byte[] keys, TextWriter w, string collection)
        {
            // The recording is replayed into the same RecBuffer the closures
            // spend, and the default cap is 1,200 keys against Cascade's 904 --
            // so the guard in PoseClosure would be doing the truncating rather
            // than protecting against it.  Measured, it never binds (the two
            // runs either side of this line are byte-identical), which is
            // exactly why it is worth removing the hazard instead of relying on
            // that: a read emits no .lpb, so the cap protects nothing here.
            // _opt is a per-Solver clone, so this cannot leak into a solve.
            _opt.MaxKeys = Math.Max(_opt.MaxKeys, keys.Length + 4096);
            EngineSnapshot root = Root(level);
            _clock = System.Diagnostics.Stopwatch.StartNew();
            _nodes = 0;
            _stageNodes = long.MaxValue;
            _stageMs = long.MaxValue;

            _e.Restore(root);
            byte[] prev = new byte[256], now = new byte[256];
            CopyBoard(prev);
            EngineSnapshot boundary = CopyOf(root);

            StringWriter buf = new StringWriter();
            int rows = 0;
            bool won = false;

            for (int i = 0; i < keys.Length; i++)
            {
                StepResult step = _e.ApplyKey(keys[i], _opt.TickCap);
                if (step == StepResult.Win) { won = true; break; }
                if (step != StepResult.Ok) break;
                CopyBoard(now);
                if (now.AsSpan().SequenceEqual(prev)) continue;

                string sig = Effect.SigOf(prev, now, keys[i] == Fire);
                EngineSnapshot here = _e.Snapshot(Take());

                Read r = AnalyzeAt(boundary);
                HashSet<string> advance = new HashSet<string>();
                HashSet<string> opens = new HashSet<string>();
                foreach (Effect e in r.OnBarrier) advance.Add(e.Sig);
                foreach (Effect e in r.Toward) advance.Add(e.Sig);
                foreach (Effect e in r.Opens) opens.Add(e.Sig);
                bool inEffects = false;
                foreach (Effect e in r.Effects) if (e.Sig == sig) { inEffects = true; break; }
                int bt = advance.Count;
                bool inBt = advance.Contains(sig);
                advance.UnionWith(opens);

                buf.Write(string.Join("\t", new object[]
                {
                    collection, level, rows, r.Verdict.Split(' ')[0], r.Effects.Count,
                    advance.Count, inEffects ? 1 : 0, advance.Contains(sig) ? 1 : 0,
                    r.Barrier.Count, r.RouteWater.Count, r.Poses,
                    opens.Count, opens.Contains(sig) ? 1 : 0, bt, inBt ? 1 : 0,
                }) + "\n");
                rows++;

                Give(boundary);
                _e.Restore(here);
                boundary = here;
                Buffer.BlockCopy(now, 0, prev, 0, 256);
            }

            Give(boundary);
            if (!won) return 0;          // a losing line says nothing about the winning one
            w.Write(buf.ToString());
            return rows;
        }

        public const string ReadDumpHeader =
            "# collection\tlevel\tevent\tverdict\teffects\tadvance\tin_effects\t"
            + "in_advance\tbarrier\twater\tposes\topens\tin_opens\tbt\tin_bt\n";

        /// One row per level, for the question a printed read cannot answer:
        /// which *shapes* does the solver fail on.  Joined against a campaign
        /// report by (collection, level).
        public static string Tsv(Read r, string collection)
        {
            int shots = 0, indirect = 0;
            foreach (Effect e in r.Effects) if (e.IsShot) { shots++; if (e.Indirect) indirect++; }
            return string.Join("	", new object[]
            {
                collection, r.Level, r.Diff, r.Verdict.Split(' ')[0], r.Work, r.RouteObstacles,
                r.Poses, r.RegionCells, r.Barrier.Count, r.RouteWater.Count, r.Blocks.Count,
                r.Threats.Count, r.Effects.Count, shots, indirect,
                r.OnBarrier.Count, r.Toward.Count, r.Opens.Count, r.FlagReachable ? 1 : 0,
            }) + "\n";
        }

        public const string TsvHeader =
            "# collection	level	diff	verdict	work	route_obst	poses	region	"
            + "barrier	water	blocks	threats	effects	shots	indirect	"
            + "on_barrier\ttoward\topens\tflag_reachable\n";

        private static string Line(Effect e)
        {
            StringBuilder b = new StringBuilder();
            b.Append(e.IsShot ? "shoot " : "drive ")
             .Append(e.PoseDir >= 0 && e.PoseDir < DirName.Length ? DirName[e.PoseDir] : "?")
             .Append(" from ").Append(At(e.PoseX * 16 + e.PoseY));
            if (e.Witnesses > 1) b.Append(" (+").Append(e.Witnesses - 1).Append(" poses)");
            b.Append("  ->  ");
            if (e.BlockFrom >= 0 && e.BlockTo >= 0)
                b.Append("block ").Append(At(e.BlockFrom)).Append(" -> ").Append(At(e.BlockTo));
            else
                for (int i = 0; i < e.Cells.Length; i++)
                    b.Append(i > 0 ? ", " : "").Append(NameOf(e.Was[i])).Append(' ')
                     .Append(At(e.Cells[i])).Append(" -> ").Append(NameOf(e.Now[i]));
            if (e.Filled.Count > 0) b.Append("  [FILLS WATER]");
            if (e.Opens > 0) b.Append("  [+").Append(e.Opens).Append(" cells to stand in]");
            if (e.Indirect) b.Append("  [mirror-routed]");
            return b.Append('\n').ToString();
        }

        private static List<T> Head<T>(List<T> xs, int k) =>
            xs.GetRange(0, Math.Min(k, xs.Count));

        private static string Census(byte[] board)
        {
            int[] n = new int[32];
            int tunnels = 0;
            foreach (byte c in board)
            {
                if (Obj.IsTunnel(c)) { tunnels++; continue; }
                if (c < n.Length) n[c]++;
            }
            StringBuilder b = new StringBuilder();
            void Add(string name, int k)
            {
                if (k > 0) b.Append(b.Length > 0 ? ", " : "").Append(k).Append(' ').Append(name);
            }
            Add("block", n[Obj.Block]);
            Add("bricks", n[Obj.Bricks]);
            Add("water", n[Obj.Water]);
            Add("anti-tank", n[Obj.AntiTankUp] + n[Obj.AntiTankRight]
                             + n[Obj.AntiTankDown] + n[Obj.AntiTankLeft]);
            Add("mirror", n[Obj.MirrorUL] + n[Obj.MirrorUR] + n[Obj.MirrorDR] + n[Obj.MirrorDL]);
            Add("roto", n[Obj.RotoUL] + n[Obj.RotoUR] + n[Obj.RotoDR] + n[Obj.RotoDL]);
            Add("conveyor", n[Obj.ConveyorUp] + n[Obj.ConveyorRight]
                            + n[Obj.ConveyorDown] + n[Obj.ConveyorLeft]);
            Add("crystal", n[Obj.Crystal]);
            Add("ice", n[Obj.Ice] + n[Obj.ThinIce]);
            Add("tunnel", tunnels);
            return b.ToString();
        }

        private static string Tier(int d) =>
            d <= 1 ? "Kids" : d == 2 ? "Easy" : d == 3 ? "Medium"
            : d == 4 ? "Hard" : d == 5 ? "Deadly" : "unrated";

        /// The board as the analysis saw it, for the report.
        public byte[] StartBoard(int level)
        {
            _e.Restore(Root(level));
            byte[] b = new byte[256];
            CopyBoard(b);
            return b;
        }
    }
}
