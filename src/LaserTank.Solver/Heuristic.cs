// Distance-to-flag, the one piece of domain knowledge layer 0 has.
//
// This is deliberately the weakest useful heuristic: a BFS from the flag over
// the cells the tank may currently enter.  It knows nothing about pushing
// blocks into water, shooting bricks, redirecting a laser off a mirror or
// riding a conveyor -- all of that is layer 1/2 work.  What it does give is a
// gradient, which is the difference between a beam search that wanders and one
// that closes in.
//
// Two deliberate departures from CheckArray:
//
//   Water is treated as impassable even though CheckArray says the tank may
//   enter it (index 3 is 1).  Entering water is drowning; a route "through" it
//   is not a route.  Levels that genuinely cross water do it by standing on a
//   block, which this heuristic cannot see -- it will report the flag as
//   unreachable and the search falls back on the manhattan term.
//
//   Tunnels are passable but their exits are not linked up, so a tunnel route
//   reads as a dead end.  Same fallback.  Linking them needs TranslateTunnel's
//   pairing rule and is layer 1.
//
// Layer 1 adds WorkDistance below, and needs to because FlagDistance goes flat
// exactly where the macro search lives: a Goto closure only ever ends on a
// state whose flag is *not* movement-reachable (if it were, the closure would
// have won there), so every macro successor scores Unreachable + manhattan and
// the beam is left ranking by tank position alone.  WorkDistance keeps a
// gradient by charging for obstacles instead of refusing to cross them.
using LaserTank.Core;

namespace LaserTank.Solver
{
    /// An instance, not a static helper, because it owns the two 256-entry
    /// scratch arrays the BFS needs.  Allocating those per node showed up as
    /// pure GC churn: the search calls this once per expanded state.
    public sealed class Heuristic
    {
        public const int Unreachable = 1000;

        private readonly int[] _dist = new int[256];
        private readonly int[] _queue = new int[256];
        private readonly int[] _cost = new int[256];
        private int[] _heap = new int[1024];
        private readonly int[] _tunnelHead = new int[8];
        private readonly int[] _tunnelNext = new int[256];

        private static readonly int[] Enterable =
        {
            //  0 Dirt  1 Tank  2 Flag  3 Water  4 Solid  5 Block  6 Bricks
                 1,       0,      1,      0,       0,       0,       0,
            //  7..10 anti-tanks         11..14 mirrors
                 0, 0, 0, 0,             0, 0, 0, 0,
            // 15..18 conveyors          19 Crystal
                 1, 1, 1, 1,             0,
            // 20..23 rotary mirrors     24 Ice  25 ThinIce
                 0, 0, 0, 0,             1,      1,
        };

        private static bool Passable(byte cell) =>
            Obj.IsTunnel(cell) ? true : (cell <= 25 && Enterable[cell] != 0);

        /// Shortest tank-step distance from the tank to the flag over currently
        /// passable cells; Unreachable + manhattan when no such path exists, so
        /// the value still decreases as the tank approaches and the search has
        /// something to follow even on levels this model cannot see through.
        public int FlagDistance(Engine e)
        {
            int fx = -1, fy = -1;
            for (int x = 0; x < 16 && fx < 0; x++)
                for (int y = 0; y < 16; y++)
                    if (e.Game.PF[x, y] == Obj.Flag) { fx = x; fy = y; break; }

            int tx = e.Game.Tank.X, ty = e.Game.Tank.Y;
            if (fx < 0) return 0;                       // no flag: nothing to steer by
            if (tx == fx && ty == fy) return 0;

            int[] dist = _dist, queue = _queue;
            for (int i = 0; i < 256; i++) dist[i] = -1;
            int head = 0, tail = 0;

            dist[fx * 16 + fy] = 0;
            queue[tail++] = fx * 16 + fy;
            while (head < tail)
            {
                int c = queue[head++];
                int cx = c >> 4, cy = c & 15, d = dist[c];
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 1 ? 1 : k == 3 ? -1 : 0);
                    int ny = cy + (k == 0 ? -1 : k == 2 ? 1 : 0);
                    if (nx < 0 || nx > 15 || ny < 0 || ny > 15) continue;
                    int n = nx * 16 + ny;
                    if (dist[n] >= 0) continue;
                    if (!Passable(e.Game.PF[nx, ny]) && !(nx == tx && ny == ty)) continue;
                    dist[n] = d + 1;
                    queue[tail++] = n;
                }
            }

            int at = dist[tx * 16 + ty];
            if (at >= 0) return at;

            int man = (tx > fx ? tx - fx : fx - tx) + (ty > fy ? ty - fy : fy - ty);
            return Unreachable + man;
        }

        // ---- layer 1: work-to-flag ----------------------------------------

        /// What it costs to *enter* a cell, in units where an empty step is 1.
        ///
        /// This is not a claim about the rules, it is a price list, and its only
        /// job is to keep the beam's ranking monotone in the right direction:
        /// removing an obstacle must lower the number.  So a brick (one laser
        /// shot, then walk through) is cheaper than a block (shoot or push it
        /// somewhere it fits) which is cheaper than water (needs a block pushed
        /// into it first).  Two cells are genuinely permanent and stay blocked:
        /// Solid, and Crystal -- CheckLLoc case 19 returns true without touching
        /// the cell, so a laser passes straight through a crystal and never
        /// clears it.
        ///
        /// Deliberately not admissible.  The beam does not need a lower bound,
        /// it needs a gradient, and an admissible version of this (every price
        /// 1) is exactly FlagDistance's flat spot.
        private static readonly int[] EntryCost =
        {
            //  0 Dirt  1 Tank  2 Flag  3 Water  4 Solid  5 Block  6 Bricks
                 1,       1,      1,      9,      -1,       6,       4,
            //  7..10 anti-tanks         11..14 mirrors
                 6, 6, 6, 6,             7, 7, 7, 7,
            // 15..18 conveyors          19 Crystal
                 1, 1, 1, 1,             -1,
            // 20..23 rotary mirrors     24 Ice  25 ThinIce
                 12, 12, 12, 12,         1,      1,
        };

        private static int Price(byte cell) =>
            Obj.IsTunnel(cell) ? 1 : (cell <= 25 ? EntryCost[cell] : -1);

        /// Cheapest priced route from the flag back to the tank, with tunnel
        /// mouths joined at zero cost.
        ///
        /// Dijkstra rather than the BFS above, because the prices differ; 256
        /// cells and at most a few hundred pushes, so a binary heap of packed
        /// (cost, cell) ints is well under the cost of the ApplyKey that
        /// produced the state being scored.
        ///
        /// The tunnel edges are the other half of layer 1's "tunnels resolved":
        /// TranslateTunnel pairs cells by (cell &amp; 0x0F) &gt;&gt; 1, so every mouth
        /// with the same id is one step from every other, and a route that goes
        /// in one and out another is a real route rather than the dead end
        /// FlagDistance sees.
        public int WorkDistance(Engine e)
        {
            int fx = -1, fy = -1;
            for (int x = 0; x < 16 && fx < 0; x++)
                for (int y = 0; y < 16; y++)
                    if (e.Game.PF[x, y] == Obj.Flag) { fx = x; fy = y; break; }

            int tx = e.Game.Tank.X, ty = e.Game.Tank.Y;
            if (fx < 0) return 0;
            if (tx == fx && ty == fy) return 0;

            int[] cost = _cost;
            for (int i = 0; i < 256; i++) cost[i] = int.MaxValue;
            for (int i = 0; i < 8; i++) _tunnelHead[i] = -1;
            for (int c = 0; c < 256; c++)
            {
                byte cell = e.Game.PF[c >> 4, c & 15];
                if (!Obj.IsTunnel(cell)) { _tunnelNext[c] = -1; continue; }
                int id = Obj.GetTunnelID(cell) & 7;
                _tunnelNext[c] = _tunnelHead[id];
                _tunnelHead[id] = c;
            }

            int n = 0;
            int start = fx * 16 + fy;
            cost[start] = 0;
            Push(ref n, 0, start);

            while (n > 0)
            {
                int top = Pop(ref n);
                int d = top >> 8, c = top & 0xFF;
                if (d > cost[c]) continue;                  // a stale duplicate
                if (c == tx * 16 + ty) return d;

                int cx = c >> 4, cy = c & 15;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 1 ? 1 : k == 3 ? -1 : 0);
                    int ny = cy + (k == 0 ? -1 : k == 2 ? 1 : 0);
                    if (nx < 0 || nx > 15 || ny < 0 || ny > 15) continue;
                    Relax(ref n, d, nx * 16 + ny, Price(e.Game.PF[nx, ny]), cost);
                }

                byte here = e.Game.PF[cx, cy];
                if (!Obj.IsTunnel(here)) continue;
                for (int t = _tunnelHead[Obj.GetTunnelID(here) & 7]; t >= 0; t = _tunnelNext[t])
                    if (t != c) Relax(ref n, d, t, 0, cost);
            }

            int man = (tx > fx ? tx - fx : fx - tx) + (ty > fy ? ty - fy : fy - ty);
            return Unreachable + man;
        }

        private void Relax(ref int n, int d, int to, int price, int[] cost)
        {
            if (price < 0) return;                          // permanently blocked
            int nd = d + price;
            if (nd >= cost[to] || nd > 0xFFFF) return;
            cost[to] = nd;
            Push(ref n, nd, to);
        }

        private void Push(ref int n, int d, int c)
        {
            if (n == _heap.Length) System.Array.Resize(ref _heap, n * 2);
            int i = n++;
            int v = (d << 8) | c;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_heap[p] <= v) break;
                _heap[i] = _heap[p];
                i = p;
            }
            _heap[i] = v;
        }

        private int Pop(ref int n)
        {
            int top = _heap[0];
            int v = _heap[--n];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < n && _heap[l] < v) m = l;
                if (r < n && _heap[r] < (m == i ? v : _heap[l])) m = r;
                if (m == i) break;
                _heap[i] = _heap[m];
                i = m;
            }
            if (n > 0) _heap[i] = v;
            return top;
        }
    }
}
