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
            Engine e = new Engine();
            replays = 0;
            byte[] best = keys;

            // 1. shortest winning prefix
            int lo = 0, hi = best.Length;
            while (lo < hi && replays < maxReplays)
            {
                int mid = (lo + hi) / 2;
                replays++;
                if (Wins(e, lvlPath, level, Slice(best, 0, mid), tickCap, out _, out _)) hi = mid;
                else lo = mid + 1;
            }
            if (hi < best.Length && replays < maxReplays)
            {
                byte[] cand = Slice(best, 0, hi);
                replays++;
                if (Wins(e, lvlPath, level, cand, tickCap, out _, out _)) best = cand;
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
                        if (!Wins(e, lvlPath, level, cand, tickCap, out _, out _)) continue;
                        best = cand;
                        again = true;
                        at--;                                  // retry at the same offset
                    }
                }
            }
            return best;
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
