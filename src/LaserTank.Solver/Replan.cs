// Post-solve: re-derive the route through the board changes the solution
// already made, and let it skip the ones that cancel out.
//
// **The complaint.**  On `LaserTank.lvl` level 3 the solver ferries the C4
// block down to C6 and, forty keypresses later, shoots it back up to C4 before
// pushing it where it was always going to go.  `Trim.Polish` cannot see it:
// the excursion is not a round trip (the *board* is different afterwards --
// the other block has sunk into the water in the meantime), not a turn on the
// spot and not a shot at nothing.  Every key in it does something.  They just
// undo each other.
//
// **The observation this pass is built on.**  A solution is a sequence of
// board changes with movement between them, and the boards it passes through
// are a ladder of positions that are *known to lead to a win* -- the solution
// itself is the proof.  So the question "what is the least-move way to get the
// blocks where they end up" has a cheap, sound answer: search for the shortest
// keystream that climbs that ladder, allowed to **skip rungs**.  Nothing is
// modelled and nothing is assumed about what a block does; a rung is reached
// because `Engine.ApplyKey` was called and `Game.PF` came back equal to a
// playfield the original run stood on.
//
// Level 3 is one skip.  After the first block is on B7 the original walks off
// to fiddle with the second one and comes back; the replan instead pushes B7
// straight into the water, which lands on the playfield the original only
// reached five board changes later -- and the whole excursion, both pushes
// down and both shots back up, goes with it.
//
// Level 7 ("Jim's Wild Ride") is the same machine answering a different
// question.  There the tank rides a conveyor circuit to reach the one square
// it can shoot the mirror from, fires twice, rides the whole circuit again and
// fires twice more.  The ladder search fires four times: a shot run from one
// pose walks four rungs at a cost of four keys.  (`Trim.Decycle` can find that
// one too -- the ride really is a round trip -- but only if its round budget
// has not already gone elsewhere, and it pays a full replay per candidate to
// find out.  Here it falls out of the same sweep.)
//
// **Why it is cheap.**  The ladder is a DAG: every successor is at a strictly
// higher rung, so one forward sweep over the rungs is a topological order and
// each rung is expanded once.  Within a rung every state has, by construction,
// the same playfield, so all of them share **one** PF-preserving closure --
// a multi-source uniform-cost walk over tank poses, seeded at the keystream
// length each state arrived with.  Total cost is bounded by
// (rungs x closure poses x 5) `ApplyKey` calls, and in practice comes in far
// under that, against the millions a search spends.
//
// **What it cannot do,** and deliberately: it cannot leave the ladder.  A
// shortcut through a playfield the original never stood on is invisible to it.
// That restriction is what makes this a polish rather than a second solver --
// and what makes it cheap enough to run on every solution.
//
// Soundness is the contract the rest of the solver keeps: the result is a
// keystream the engine actually produced, and `Program.SolveOne` replays it
// from cold before it is written, as does tools/verify_solutions.py after.
using System;
using System.Collections.Generic;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public static class Replan
    {
        public sealed class Stats
        {
            public int Rungs;        // board changes in the input, plus the start
            public int RungsAfter;   // and in what came out
            public int Skipped;      // the difference: board changes that cancelled
            public long Nodes;       // ApplyKey calls spent
            public int Before, After;
            public string Why = "";  // done | nodes | poses | no-ladder
        }

        /// Shorten `keys` by re-deriving its route.  Returns `keys` itself when
        /// it finds nothing better, so the caller can compare by reference.
        ///
        /// `width` bounds the states kept per rung and `posesPerRung` the size
        /// of one closure; `nodeBudget` is the backstop over the whole pass.
        public static byte[] Improve(string lvlPath, int level, byte[] keys, int tickCap,
                                     int width, int posesPerRung, int runCap,
                                     long nodeBudget, out Stats st)
        {
            st = new Stats { Before = keys.Length, After = keys.Length, Why = "no-ladder" };

            if (!BuildLadder(lvlPath, level, keys, tickCap, out List<byte[]> boards,
                             out int winLen))
                return keys;

            st.Rungs = boards.Count;

            // A board the run stood on twice maps to its *last* rung, so a
            // plain round trip is skipped by the lookup alone.
            Dictionary<ulong, int> rungOf = new Dictionary<ulong, int>();
            for (int i = 0; i < boards.Count; i++) rungOf[BoardHash(boards[i])] = i;

            Sweep s = new Sweep
            {
                Lvl = lvlPath, Level = level, TickCap = tickCap, Width = width,
                Poses = posesPerRung, RunCap = runCap, Budget = nodeBudget,
                Boards = boards, RungOf = rungOf, BestLen = winLen,
            };
            if (!s.Run()) return keys;

            st.Nodes = s.Nodes;
            st.Why = s.Why;
            st.RungsAfter = boards.Count;
            if (s.Best == null || s.Best.Length >= keys.Length) return keys;

            st.After = s.Best.Length;

            // One more replay to say how many board changes actually survived.
            // The sweep cannot report it -- a rung is reached by whichever
            // state got there first and nothing carries how far that state had
            // already climbed -- and the honest number is worth the replay.
            st.RungsAfter = BuildLadder(lvlPath, level, s.Best, tickCap,
                                        out List<byte[]> after, out _)
                            ? after.Count : 0;
            st.Skipped = st.RungsAfter > 0 ? boards.Count - st.RungsAfter : 0;
            return s.Best;
        }

        // ---- the ladder ----------------------------------------------------

        /// Replay the solution and keep every playfield it stood on, in order.
        ///
        /// Index 0 is the level as loaded; index k is the playfield after the
        /// k'th board change.  `winLen` is how many keys the win actually took,
        /// which is what the replan has to beat: a solution's keys after the
        /// flag are slack that `Trim.Wins` already ignores.
        private static bool BuildLadder(string lvlPath, int level, byte[] keys, int tickCap,
                                        out List<byte[]> boards, out int winLen)
        {
            boards = new List<byte[]>();
            winLen = 0;
            if (keys.Length == 0) return false;

            Engine e = new Engine();
            e.ConfigureForReplay();
            if (!e.LoadLevel(lvlPath, level)) return false;
            e.BeginSearch(keys.Length);

            byte[] cur = Board(e);
            boards.Add(cur);
            for (int i = 0; i < keys.Length; i++)
            {
                StepResult step = e.ApplyKey(keys[i], tickCap);
                if (step == StepResult.Win)
                {
                    winLen = i + 1;
                    byte[] last = Board(e);
                    if (!Same(last, cur)) boards.Add(last);
                    return true;
                }
                if (step != StepResult.Ok) return false;
                byte[] nb = Board(e);
                if (Same(nb, cur)) continue;
                boards.Add(nb);
                cur = nb;
            }
            return false;                                   // never reached the flag
        }

        // ---- the sweep -----------------------------------------------------

        /// One sweep up the ladder.
        ///
        /// A class rather than a pile of arguments because the closure, the run
        /// and the win check all need the same eight things, and the
        /// alternative is threading them through four signatures.
        private sealed class Sweep
        {
            public string Lvl;
            public int Level, TickCap, Width, Poses, RunCap;
            public long Budget, Nodes;
            public List<byte[]> Boards;
            public Dictionary<ulong, int> RungOf;

            public int BestLen;                  // the length to beat, exclusive
            public byte[] Best;
            public string Why = "done";

            private Engine _e;
            private int _rung;
            private readonly byte[] _now = new byte[256];
            private readonly byte[] _prev = new byte[256];

            // bucket[i] = the states that reached rung i, deduped by state hash.
            private List<EngineSnapshot>[] _bucket;
            private HashSet<ulong>[] _seen;

            // The closure being walked: a uniform-cost queue indexed by
            // keystream length, so seeds that arrived at different lengths can
            // share one walk and still come out shortest-first.
            private readonly Dictionary<int, List<Seed>> _queue =
                new Dictionary<int, List<Seed>>();
            private readonly Dictionary<ulong, int> _local = new Dictionary<ulong, int>();

            /// The poses the walk finalised, in shortest-first order: the set
            /// the shot pass fires from.
            private readonly List<EngineSnapshot> _poses = new List<EngineSnapshot>();

            private static readonly byte[] MoveKeys =
                { (byte)Engine.VK_UP, (byte)Engine.VK_RIGHT,
                  (byte)Engine.VK_DOWN, (byte)Engine.VK_LEFT };
            private const byte Fire = (byte)Engine.VK_SPACE;

            private readonly struct Seed
            {
                public readonly EngineSnapshot S;
                public readonly ulong H;
                public Seed(EngineSnapshot s, ulong h) { S = s; H = h; }
            }

            public bool Run()
            {
                _e = new Engine();
                _e.ConfigureForReplay();
                if (!_e.LoadLevel(Lvl, Level)) return false;

                // Room for a keystream at worst the original's length; anything
                // at or past BestLen is pruned before the next key is written.
                _e.BeginSearch(Math.Max(BestLen + 2, 4));

                _bucket = new List<EngineSnapshot>[Boards.Count];
                _seen = new HashSet<ulong>[Boards.Count];
                for (int i = 0; i < Boards.Count; i++)
                {
                    _bucket[i] = new List<EngineSnapshot>();
                    _seen[i] = new HashSet<ulong>();
                }
                _bucket[0].Add(_e.Snapshot());
                _seen[0].Add(_e.StateHash());

                for (int i = 0; i < Boards.Count; i++)
                {
                    if (_bucket[i].Count == 0) { _bucket[i] = null; continue; }
                    if (Nodes >= Budget) { Why = "nodes"; break; }
                    _rung = i;
                    Expand(_bucket[i]);
                    _bucket[i] = null;                       // one sweep, one pass
                }
                return true;
            }

            /// Every pose reachable from `seeds` without changing the
            /// playfield, and every board change reachable from those poses.
            ///
            /// Two passes, and they are `Solver.ExpandPush`'s two for its
            /// reason: **the space bar does not belong in the closure.**  A
            /// shot that hits nothing still moves the laser record, so its
            /// state hash differs from the pose it was fired from and the walk
            /// happily files it as somewhere new to stand -- then fires from
            /// *that*, and the closure stops being bounded by the pose count.
            /// Measured before the split: level 3 spent its whole pose budget
            /// at rung 0, on an island of twenty-four cells.
            ///
            /// So the walk is movement only, and the shots come afterwards from
            /// the poses it found.  What that gives up is a shot kept for its
            /// *timing* -- burn a tick here so a gun fires early, then walk on
            /// -- which is not reachable from a closure that a shot cannot
            /// re-enter.  The search layer gives the same thing up in the same
            /// place, and here it costs at worst an improvement not found.
            private void Expand(List<EngineSnapshot> seeds)
            {
                seeds.Sort(static (a, b) => a.KeyLen - b.KeyLen);
                if (seeds.Count > Width) seeds.RemoveRange(Width, seeds.Count - Width);

                _queue.Clear();
                _local.Clear();
                _poses.Clear();
                int lo = int.MaxValue;
                foreach (EngineSnapshot s in seeds)
                {
                    ulong h = HashOf(s);
                    if (!Better(h, s.KeyLen)) continue;
                    Enqueue(s.KeyLen, new Seed(s, h));
                    if (s.KeyLen < lo) lo = s.KeyLen;
                }
                if (lo == int.MaxValue) return;

                byte[] board = Boards[_rung];
                for (int len = lo; len < BestLen && _poses.Count < Poses; len++)
                {
                    if (!_queue.TryGetValue(len, out List<Seed> at)) continue;
                    _queue.Remove(len);
                    foreach (Seed seed in at)
                    {
                        if (_local.TryGetValue(seed.H, out int best) && best < len) continue;
                        if (_poses.Count >= Poses) { Why = "poses"; break; }
                        _poses.Add(seed.S);

                        foreach (byte key in MoveKeys)
                        {
                            if (Nodes >= Budget) { Why = "nodes"; return; }
                            _e.Restore(seed.S);
                            Nodes++;
                            StepResult step = _e.ApplyKey(key, TickCap);
                            if (step == StepResult.Win) { Won(); continue; }
                            if (step != StepResult.Ok) continue;
                            int n = (int)_e.Game.RecP;
                            if (n >= BestLen) continue;

                            CopyBoard(_now);
                            if (Same(_now, board))
                            {
                                ulong h = _e.StateHash();
                                if (Better(h, n)) Enqueue(n, new Seed(_e.Snapshot(), h));
                                continue;
                            }
                            RunOn(key);
                        }
                    }
                }

                // One shot from each pose the walk found.  A shot that leaves
                // the playfield alone is not a rung and cannot become one, so
                // it is dropped rather than followed.
                foreach (EngineSnapshot pose in _poses)
                {
                    if (Nodes >= Budget) { Why = "nodes"; return; }
                    _e.Restore(pose);
                    Nodes++;
                    StepResult step = _e.ApplyKey(Fire, TickCap);
                    if (step == StepResult.Win) { Won(); continue; }
                    if (step != StepResult.Ok) continue;
                    if ((int)_e.Game.RecP >= BestLen) continue;
                    CopyBoard(_now);
                    if (Same(_now, board)) continue;
                    RunOn(Fire);
                }
            }

            /// The board just moved.  Offer this rung, then keep pressing the
            /// same key while the board keeps moving -- a drive push carries
            /// the tank along with the block and a shot pushes it another cell,
            /// and both are one key per rung climbed.  `Solver.PushRun` and
            /// `Solver.ShotRun` are the same idea inside the search.
            private void RunOn(byte key)
            {
                Buffer.BlockCopy(_now, 0, _prev, 0, 256);
                for (int k = 1; ; k++)
                {
                    Offer();
                    if (k >= RunCap || Nodes >= Budget) return;

                    Nodes++;
                    StepResult step = _e.ApplyKey(key, TickCap);
                    if (step == StepResult.Win) { Won(); return; }
                    if (step != StepResult.Ok) return;
                    if ((int)_e.Game.RecP >= BestLen) return;

                    CopyBoard(_now);
                    if (Same(_now, _prev)) return;           // the run ended
                    Buffer.BlockCopy(_now, 0, _prev, 0, 256);
                }
            }

            /// File the engine's current state under its rung, if the board it
            /// stands on is one the original run reached *later*.
            private void Offer()
            {
                if (!RungOf.TryGetValue(BoardHash(_now), out int j) || j <= _rung) return;
                ulong h = _e.StateHash();
                if (!_seen[j].Add(h)) return;
                _bucket[j]?.Add(_e.Snapshot());
            }

            private void Won()
            {
                byte[] k = _e.PathKeys();
                if (k.Length >= BestLen) return;
                BestLen = k.Length;
                Best = k;
            }

            private bool Better(ulong h, int len)
            {
                if (_local.TryGetValue(h, out int had) && had <= len) return false;
                _local[h] = len;
                return true;
            }

            private void Enqueue(int len, Seed s)
            {
                if (!_queue.TryGetValue(len, out List<Seed> at))
                    _queue[len] = at = new List<Seed>();
                at.Add(s);
            }

            /// The state hash of a snapshot.  The engine only hashes the state
            /// it is holding, so this restores it first -- once per seed, off
            /// the ApplyKey path.
            private ulong HashOf(EngineSnapshot s)
            {
                _e.Restore(s);
                return _e.StateHash();
            }

            private void CopyBoard(byte[] into) =>
                Buffer.BlockCopy(_e.Game.PF, 0, into, 0, 256);
        }

        // ---- playfields ----------------------------------------------------

        private static byte[] Board(Engine e)
        {
            byte[] b = new byte[256];
            Buffer.BlockCopy(e.Game.PF, 0, b, 0, 256);
            return b;
        }

        private static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

        private static ulong BoardHash(byte[] pf)
        {
            const ulong Prime = 0x100000001b3UL;
            ulong h = 0xcbf29ce484222325UL;
            for (int i = 0; i < 256; i++) h = (h ^ pf[i]) * Prime;
            return h;
        }
    }
}
