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
    }
}
