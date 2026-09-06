// "Any valid solution is fine, but if it is ten times the official record, try
// to trim it down."
//
// Same shape as tools/fuzz.py's shrinker, and deliberately so -- that one holds
// a *divergence signature* fixed while it deletes keys; this one holds "still
// reaches the flag" fixed.  Both are delta debugging with a predicate, and the
// predicate is the only difference.
//
// It is not trying to reach the record.  It runs only when a solution is over
// the ratio, stops the moment it is under, and gives up on a budget -- a 12x
// solution trimmed to 9x is a finished job here.
using System;
using System.Collections.Generic;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public static class Trim
    {
        /// Replay a keystream from the level start, one key at a time through
        /// the same Engine.ApplyKey the search uses.
        ///
        /// Feeding the keys one at a time is equivalent to handing the engine
        /// the whole buffer -- the tick consumes exactly one key per quiescent
        /// tick either way, and RB_TOS only bounds how many are on offer -- but
        /// it inherits ApplyKey's cycle detection, so a candidate keystream
        /// that puts the tank on a perpetual conveyor circuit is rejected in
        /// milliseconds instead of running to a tick cap.  The shrinker tries
        /// thousands of candidates; that difference is the whole runtime.
        /// The same predicate on a *fresh* engine, which is the only version a
        /// candidate search may use.
        ///
        /// **This is a trap with a long fuse and it cost real reductions.**
        /// `LoadLevel` resets the playfield, the tank and the slide records --
        /// and deliberately does *not* reset `wasIce`, `WaitToTrans`,
        /// `ConvMoving` or `BlackHole`, because that staleness is quirk #3 and
        /// the engine is a faithful transliteration of a program that never
        /// reloaded a level into a fresh process either.  So replaying two
        /// candidates through one Engine is not the same as replaying each from
        /// cold: the second inherits the first's leftovers, and a keystream that
        /// wins from cold can be reported as losing.
        ///
        /// That is exactly what happened here.  The shrinker and the polisher
        /// both reused one Engine, and on `Challenge-I` level 1 the polisher
        /// reported a 71-key solution as irreducible while an identical search
        /// on fresh engines took it to 51 -- the five keys Michal pointed at
        /// (aim south, fire at nothing, aim west, step into a wall, fire at
        /// nothing) came straight out.
        ///
        /// Note what the bug could and could not do: it made the trimmer *miss*
        /// deletions, and it could in principle have made one look acceptable
        /// that was not -- but nothing reaches a .lpb without a fresh-engine
        /// replay in Program.SolveOne and another in tools/verify_solutions.py,
        /// so no wrong solution was ever written.  The cost was entirely in
        /// reductions not found.
        public static bool Wins(string lvlPath, int level, byte[] keys, int tickCap,
                                out int moves, out int shots) =>
            Wins(new Engine(), lvlPath, level, keys, tickCap, out moves, out shots);

        public static bool Wins(Engine e, string lvlPath, int level, byte[] keys,
                                int tickCap, out int moves, out int shots)
        {
            moves = shots = 0;
            e.ConfigureForReplay();
            if (!e.LoadLevel(lvlPath, level)) return false;
            if (keys.Length == 0) return false;
            e.BeginSearch(keys.Length);

            foreach (byte k in keys)
            {
                StepResult step = e.ApplyKey(k, tickCap);
                if (step == StepResult.Win) break;              // trailing keys are slack
                if (step != StepResult.Ok) return false;        // dead, or spinning
            }
            moves = e.Game.ScoreMove;
            shots = e.Game.ScoreShot;
            return e.Won;
        }

        /// Delete as much as possible while it still wins.  Two passes:
        ///
        ///   1. shortest winning prefix, by binary search -- O(log n) replays,
        ///      and it is where a beam solution's tail usually goes;
        ///   2. shrinking-window deletion (ddmin's granularity ladder) down to
        ///      single keys, restarting whenever something comes out.
        ///
        /// Returns the shortest keystream it found; never returns one that does
        /// not win, because every candidate is replayed before it is accepted.
        public static byte[] Shrink(string lvlPath, int level, byte[] keys, int tickCap,
                                    int targetLen, int maxReplays, out int replays)
        {
            replays = 0;
            byte[] best = keys;

            // 1. shortest winning prefix
            int lo = 0, hi = best.Length;
            while (lo < hi && replays < maxReplays)
            {
                int mid = (lo + hi) / 2;
                replays++;
                if (Wins(lvlPath, level, Slice(best, 0, mid), tickCap, out _, out _)) hi = mid;
                else lo = mid + 1;
            }
            if (hi < best.Length && replays < maxReplays)
            {
                byte[] cand = Slice(best, 0, hi);
                replays++;
                if (Wins(lvlPath, level, cand, tickCap, out _, out _)) best = cand;
            }

            // 2. shrinking-window deletion
            for (int w = Math.Max(1, best.Length / 4); w >= 1 && replays < maxReplays; w /= 2)
            {
                bool again = true;
                while (again && replays < maxReplays)
                {
                    again = false;
                    for (int at = 0; at + w <= best.Length && replays < maxReplays; at++)
                    {
                        if (best.Length <= targetLen) return best;
                        byte[] cand = Without(best, at, w);
                        replays++;
                        if (!Wins(lvlPath, level, cand, tickCap, out _, out _)) continue;
                        best = cand;
                        again = true;
                        at--;                                  // retry at the same offset
                    }
                }
            }
            return best;
        }

        /// Take the machine noise out of a solution: turns that turn on the
        /// spot and shots that hit nothing.
        ///
        /// **Why this is separate from Shrink.**  Shrink is delta debugging --
        /// it will try to delete anything, costs thousands of replays, and so it
        /// runs only on a solution that is already over `--trim-ratio`.  This
        /// runs on *every* solution, because the complaint it answers is not
        /// about length.  A beam solving a level in 1.6x the record still spells
        /// its route "face north, face west, face south, move south", and it
        /// still fires at empty air, because nothing in layer 0's action set
        /// charges for either -- `MoveTank` (Engine.cs:657) spends a whole
        /// keypress turning when the key does not match the way the tank already
        /// faces, and `ScoreMove` only increments in `UpDateTankPos`.  Those
        /// keys are free to the search and they are what makes a replay look
        /// computer-generated.
        ///
        /// **Why the candidates still have to be replayed.**  A wasted turn is
        /// not a no-op: `AntiTank()` runs inside every key-consuming tick, so a
        /// turn on the spot gives every anti-tank on the board a move, and there
        /// are levels whose solution *is* that -- burn a tick here so the gun
        /// two rows down fires early.  The same goes for a shot that changes
        /// nothing on this tick.  So this proposes deletions from a marked
        /// replay and accepts one only when the shortened keystream still wins,
        /// exactly as Shrink does; the difference is that it proposes tens of
        /// candidates rather than thousands.
        ///
        /// Cheap by construction: one replay to mark, one to try the whole set
        /// at once -- which is what usually happens -- and only then a pass of
        /// one-at-a-time deletions for the levels where some of the noise turns
        /// out to be load-bearing.
        /// The deletion widths the sweep tries, widest first.  Contiguous from
        /// 12 down to 1 rather than a halving ladder -- see the loop that uses
        /// it for why 5 in particular has to be in the list.
        private static readonly int[] Widths =
            { 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };

        public static byte[] Polish(string lvlPath, int level, byte[] keys, int tickCap,
                                    int maxReplays, out int replays)
        {
            replays = 0;
            byte[] best = keys;

            // The three passes below, repeated while anything comes out.
            //
            // **The loop is not belt and braces, it is a bug fix.**  Decycle
            // cuts at most one round trip per round and gives up after sixteen
            // of them (a state hash is only a subset of the state, so a cut has
            // to be replayed, and a pass that kept going would be unbounded).
            // A raw beam solution can hold far more than sixteen -- and the
            // sweep in pass 3 tops out at twelve keys, so every round trip
            // longer than that which Decycle did not reach stayed in.
            //
            // `data/solutions/LaserTank/00007.lpb` was written at 81 keys with
            // polishing on, and a second `--polish` over the file took it to 68
            // by cutting one 13-key round trip: the tank rides the conveyor
            // circuit a second time to fire two more shots from the square it
            // was already standing on.  Nothing about that needed a bigger
            // budget -- only a second look after the earlier passes had used
            // theirs up.
            for (int pass = 0; pass < 4 && replays < maxReplays; pass++)
            {
                int was = best.Length;
                best = PolishOnce(lvlPath, level, best, tickCap, ref replays, maxReplays);
                if (best.Length == was) break;
            }
            return best;
        }

        private static byte[] PolishOnce(string lvlPath, int level, byte[] keys, int tickCap,
                                         ref int replays, int maxReplays)
        {
            byte[] best = keys;

            // 1. Round trips.  Longest first, and eliding one can turn the
            //    keys either side of it into an adjacent pair of turns, so this
            //    goes before the key passes rather than after.
            best = Decycle(lvlPath, level, best, tickCap, ref replays, maxReplays);

            // 2. The marked keys, deleted in bulk.  One replay can take twenty
            //    of them when the noise is all genuinely idle, which is the
            //    common case and the reason this pass exists at all next to the
            //    sweep below -- it is the cheap way to get most of the way.
            for (int round = 0; round < 4 && replays < maxReplays; round++)
            {
                List<int> junk = Mark(lvlPath, level, best, tickCap);
                replays++;
                if (junk.Count == 0) break;
                byte[] cand = WithoutAll(best, junk);
                replays++;
                if (!Wins(lvlPath, level, cand, tickCap, out _, out _)) break;
                best = cand;
            }

            // 3. Every remaining window of 1, 2 and 3 keys, back to front.
            //
            //    Single keys are what the marked pass cannot do, and the
            //    difference is most of the complaint: a shot that *looks*
            //    useless usually is not a no-op -- it knocks out a brick nothing
            //    needed, or makes an anti-tank turn -- so its state hash changes
            //    and no marking rule will name it.  The only test that catches
            //    it is the honest one: take the key out and see whether the
            //    level is still won.
            //
            //    Windows of 2 and 3 are the rest of it, and they are not
            //    optional.  A pointless shot is almost always preceded by the
            //    turn that aimed it, and *neither key is removable alone* --
            //    drop the shot and the turn leaves the tank facing a new way for
            //    everything after it; drop the turn and the shot goes off in the
            //    wrong direction.  Only the pair comes out.  That is why a
            //    solution can be single-key minimal and still read as machine
            //    output, which is exactly what `data/solutions/Challenge-I/00001.lpb`
            //    was when this was written.
            //
            //    Back to front so an accepted deletion cannot move an index
            //    still to be tried, and repeated while anything comes out --
            //    removing one window can make its neighbour removable.
            // Widest first, and *every* small width rather than a halving
            // ladder.  ddmin's usual 1/2/4/8/16 was the first version and it is
            // not enough here: the artifact Michal actually pointed at in
            // `data/solutions/Challenge-I/00001.lpb` -- aim south, fire at nothing,
            // aim west, step, fire at nothing, before the one shot that counts
            // -- is *five* keys long, and 5 is precisely what a halving ladder
            // skips.  A run of pointless play is as long as it is; there is no
            // reason for its length to be a power of two.
            foreach (int w in Widths)
            {
                if (replays >= maxReplays) break;
                bool again = true;
                while (again && replays < maxReplays)
                {
                    again = false;
                    for (int i = best.Length - w; i >= 0 && replays < maxReplays; i--)
                    {
                        if (w > best.Length) break;
                        byte[] cand = Without(best, i, w);
                        replays++;
                        if (!Wins(lvlPath, level, cand, tickCap, out _, out _)) continue;
                        best = cand;
                        again = true;                  // a removal can expose another
                        if (i > best.Length - w) i = best.Length - w + 1;
                    }
                }
            }
            return best;
        }

        /// Cut out the round trips: stretches that end in a state the run had
        /// already been in.
        ///
        /// This is the third thing that makes a solver replay read as machine
        /// output, and it is the one the eye notices first -- `> > &lt; &lt; > > &lt; &lt;`,
        /// the tank stepping right and back and right and back. The beam does
        /// it because `Cut` breaks a tie by cheapest keystream and a state it
        /// has already left is exactly as good as one it has not, so wandering
        /// out and back costs the search nothing.
        ///
        /// Detecting it needs no pattern matching, only the search's own
        /// `StateHash`: if the state after key *j* equals the state after key
        /// *i*, then keys i+1..j did nothing that lasted, and deleting them
        /// leaves every later key facing the state it already faced. Longest
        /// first, because one long elision is worth many short ones and the
        /// scan is over the same array either way.
        ///
        /// **It is still replayed, and the reason is a real one rather than
        /// caution.** `StateHash` deliberately hashes a *subset* -- it keeps the
        /// staleness that is load-bearing (`wasIce`, `WaitToTrans`) and drops
        /// `BMF`, the counters and the path -- so two states that hash alike are
        /// equal in everything the rules read, which is what makes this sound.
        /// But "the rules read" is a claim about the engine, and this file is
        /// not where that claim gets to go unchecked.
        private static byte[] Decycle(string lvlPath, int level, byte[] keys,
                                      int tickCap, ref int replays, int maxReplays)
        {
            byte[] best = keys;
            for (int round = 0; round < 16 && replays < maxReplays; round++)
            {
                List<ulong> h = Hashes(lvlPath, level, best, tickCap);
                replays++;
                if (h.Count < 3) break;

                // Every (i, j) with the same state, longest first.  Trying
                // only the longest and giving up when it fails was the first
                // version and it is too brittle: a state hash is a subset of
                // the state, so the longest match is the one most likely to be
                // the one where the dropped fields mattered -- and abandoning
                // the pass there leaves every shorter round trip in place.
                Dictionary<ulong, int> firstAt = new Dictionary<ulong, int>();
                for (int i = 0; i < h.Count; i++)
                    if (!firstAt.ContainsKey(h[i])) firstAt[h[i]] = i;

                List<(int at, int len)> cycles = new List<(int, int)>();
                for (int j = h.Count - 1; j > 0; j--)
                    if (firstAt.TryGetValue(h[j], out int i) && i < j)
                        cycles.Add((i, j - i));
                cycles.Sort((p, q) => q.len - p.len);

                bool cut = false;
                foreach ((int at, int len) in cycles)
                {
                    if (replays >= maxReplays) break;
                    if (at + len > best.Length) continue;
                    byte[] cand = Without(best, at, len);
                    replays++;
                    if (!Wins(lvlPath, level, cand, tickCap, out _, out _)) continue;
                    best = cand;
                    cut = true;
                    break;                       // the hashes are stale now
                }
                if (!cut) break;
            }
            return best;
        }

        /// The state after each key, from one replay.  Index k is the state
        /// after k keys, so index 0 is the start position.
        private static List<ulong> Hashes(string lvlPath, int level, byte[] keys, int tickCap)
        {
            Engine e = new Engine();
            List<ulong> h = new List<ulong>();
            e.ConfigureForReplay();
            if (!e.LoadLevel(lvlPath, level) || keys.Length == 0) return h;
            e.BeginSearch(keys.Length);
            h.Add(e.StateHash());
            foreach (byte k in keys)
            {
                if (e.ApplyKey(k, tickCap) != StepResult.Ok) break;
                h.Add(e.StateHash());
            }
            return h;
        }

        /// One replay, marking the keys that *look* idle.  Nothing here decides
        /// anything -- Polish replays every proposal -- so the marking is
        /// allowed to be optimistic.
        private static List<int> Mark(string lvlPath, int level, byte[] keys, int tickCap)
        {
            Engine e = new Engine();
            List<int> junk = new List<int>();
            e.ConfigureForReplay();
            if (!e.LoadLevel(lvlPath, level) || keys.Length == 0) return junk;
            e.BeginSearch(keys.Length);

            byte[] board = new byte[256];
            Board(e, board);
            byte[] now = new byte[256];

            for (int i = 0; i < keys.Length; i++)
            {
                int x = e.Game.Tank.X, y = e.Game.Tank.Y;
                ulong before = e.StateHash();
                if (e.ApplyKey(keys[i], tickCap) != StepResult.Ok) break;
                Board(e, now);
                bool moved = e.Game.Tank.X != x || e.Game.Tank.Y != y;
                bool boardChanged = !Same(board, now);

                if (keys[i] == (byte)Engine.VK_SPACE)
                {
                    // A shot that left the entire state identical hit nothing
                    // and turned nothing -- not even an anti-tank, since
                    // AntiTank() runs in the same key-consuming tick and any
                    // move it made would be in the hash.  Layers 1 and 5 prune
                    // these during the search; layer 0's raw beam does not.
                    if (e.StateHash() == before) junk.Add(i);
                }
                else if (!moved && !boardChanged
                         && i + 1 < keys.Length && keys[i + 1] != (byte)Engine.VK_SPACE
                         && keys[i + 1] != keys[i])
                {
                    // A direction key that moved nothing, followed by a
                    // *different* direction key.  That is the "face north, face
                    // west, face south, move south" run: only the last turn of
                    // such a run can matter, so every earlier one is a proposal.
                    // Requiring the successor to differ is what keeps the final
                    // turn -- the one the move needs -- out of the list.
                    junk.Add(i);
                }

                byte[] swap = board; board = now; now = swap;
            }
            return junk;
        }

        private static void Board(Engine e, byte[] into)
        {
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++) into[x * 16 + y] = e.Game.PF[x, y];
        }

        private static bool Same(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static byte[] WithoutAll(byte[] src, List<int> at)
        {
            byte[] d = new byte[src.Length - at.Count];
            int j = 0, k = 0;
            for (int i = 0; i < src.Length; i++)
            {
                if (k < at.Count && at[k] == i) { k++; continue; }
                d[j++] = src[i];
            }
            return d;
        }

        private static byte[] Slice(byte[] src, int at, int len)
        {
            byte[] d = new byte[len];
            Array.Copy(src, at, d, 0, len);
            return d;
        }

        private static byte[] Without(byte[] src, int at, int len)
        {
            byte[] d = new byte[src.Length - len];
            Array.Copy(src, 0, d, 0, at);
            Array.Copy(src, at + len, d, at, src.Length - at - len);
            return d;
        }
    }
}
