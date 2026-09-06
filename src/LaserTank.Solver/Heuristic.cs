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

        // ---- layer 4: what the passes below already know ------------------
        //
        // A learned evaluation wants more than the one scalar these return, and
        // re-running the Dijkstra to get it would double the cost of the one
        // thing the subgoal beam calls once per surviving successor.  So the
        // numbers that fall out of the existing passes are published instead,
        // set by the call that produced them and valid until the next call on
        // this instance.  An instance is per-Solver and a Solver is one level
        // on one thread, so there is no sharing to get wrong.
        public int RouteObstacles;      // cells on the WorkDistance route priced
                                        // above an empty step; -1 if no route
        public int Component;           // cells in the flag's passable component
        public bool FlagReachable;      // ...and whether the tank is one of them

        /// Layer 5's ferry term: summed over the water cells on the settled
        /// WorkDistance route, how far the nearest movable block still is from
        /// each of them.  -1 when no route settled, 0 when the route crosses no
        /// water or the board has no block left to push into it.
        ///
        /// **Why it exists, and it is the one number layer 5 could not do
        /// without.**  WorkDistance prices a water cell at 9 and does not move
        /// at all while a block is being carried towards it: the whole ferry --
        /// twenty to fifty keypresses of fetching and turning and pushing --
        /// scores exactly the same as standing still, and only the final push
        /// that fills the cell scores anything.  A beam ranking by it is doing
        /// breadth-first search across the entire ferry, which is precisely the
        /// 68-keypress ascent tools/basin.py measured on level 1.  This makes
        /// the carry itself visible: every push that brings a block one cell
        /// nearer the water it is destined for lowers the number by one.
        ///
        /// Manhattan, not a pushability search, and that is deliberate rather
        /// than lazy.  Whether a block can actually be pushed along a given
        /// route is a question about MoveObj, ice, conveyors and where the tank
        /// can stand behind it -- i.e. a second implementation of the game,
        /// which this project does not have and is not going to acquire for a
        /// tie-break.  The engine still decides every push that is actually
        /// made; this only orders the ones already generated.
        public int RouteFerry;

        /// The RIDE analogue of RouteFerry, and read the same way: valid only
        /// straight after WorkDistance, which is what publishes it.
        ///
        /// A ferry level's route crosses water, and the term that makes the
        /// search carry a block towards it is "how far is the nearest block
        /// from the hole".  A RIDE level's route crosses a *conveyor*, which
        /// is not an obstacle -- the price list charges 1 for it and the
        /// Dijkstra walks straight over it -- and yet the tank cannot follow
        /// the route, because arriving on a conveyor cell it is carried off it
        /// again.  Nothing in WorkDistance moves when a block gets nearer the
        /// cell that would stop that ride, so the whole manoeuvre is a plateau,
        /// exactly as a ferry is without RouteFerry.
        ///
        /// So: walk the route the tank has to travel, and for every conveyor
        /// cell on it that carries the tank somewhere *other* than the next
        /// cell of the route, price the block that would stop it -- the cell
        /// the conveyor discharges into, which has to stop being enterable.
        ///
        /// On LaserTank.lvl 2 "Easy Level Conveyor" that names (13,1) and
        /// (13,2), which are the two cells the hand recording spends 32 board
        /// changes filling, and it names them from the *root*.
        ///
        /// It is an estimate and it over-asks, deliberately: a conveyor that
        /// carries the tank off the priced route may still carry it somewhere
        /// useful by a longer way round -- level 2's own ride up column 15 does
        /// exactly that -- and this counts that as a cell needing a block.  Like
        /// RouteFerry it only orders states the expansion already produced, so
        /// over-asking costs search order and can never admit anything.
        public int RouteStop;

        private readonly int[] _dist = new int[256];
        private readonly int[] _queue = new int[256];
        private readonly int[] _cost = new int[256];
        private int[] _heap = new int[1024];
        private readonly int[] _tunnelHead = new int[8];
        private readonly int[] _tunnelNext = new int[256];
        private readonly int[] _pred = new int[256];

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

        /// How many cells the tank can walk to over currently-passable cells.
        ///
        /// Layer 6 asks one question of a candidate board change that needs no
        /// theory of what the obstacle was: *can the tank stand somewhere it
        /// could not stand before?*  Answered properly that is a second
        /// PF-preserving pose closure -- a few thousand ApplyKey calls -- and
        /// answered here it is a flood over 256 cells, which is what makes it
        /// affordable once per successor inside a beam.
        ///
        /// **The two are not the same question and the difference is measured.**
        /// The flood does not know about anti-tank fire, spent thin ice, tunnel
        /// pairing or a conveyor that carries the tank somewhere it could not
        /// walk, so it both over- and under-counts against the executed answer.
        /// It is used only to pick a *tier*: every successor in every tier was
        /// produced by the engine, so a wrong answer here costs search order and
        /// can never admit a state the expansion did not offer.  That is the
        /// line layer 2's first obstacle derivation crossed and this one does
        /// not -- it modelled *acceptance*, this orders what acceptance already
        /// passed.
        ///
        /// Note what it deliberately is not: the *flag's* component, which is
        /// what FlagDistance publishes as `Component`.  That number does not
        /// move at all during a ferry until the last block goes into the water,
        /// so tiering by it promotes nothing for the whole of the manoeuvre this
        /// layer exists to search -- measured, 0 of 248 successors at depth 3.
        /// The tank's own region moves on almost every push, because a block
        /// that leaves a square is a square the tank can now stand on.
        public int TankRegion(Engine e)
        {
            int tx = e.Game.Tank.X, ty = e.Game.Tank.Y;
            int[] dist = _dist, queue = _queue;
            for (int i = 0; i < 256; i++) dist[i] = -1;
            int head = 0, tail = 0, n = 1;

            dist[tx * 16 + ty] = 0;
            queue[tail++] = tx * 16 + ty;
            while (head < tail)
            {
                int c = queue[head++];
                int cx = c >> 4, cy = c & 15;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 1 ? 1 : k == 3 ? -1 : 0);
                    int ny = cy + (k == 0 ? -1 : k == 2 ? 1 : 0);
                    if (nx < 0 || nx > 15 || ny < 0 || ny > 15) continue;
                    int m = nx * 16 + ny;
                    if (dist[m] >= 0 || !Passable(e.Game.PF[nx, ny])) continue;
                    dist[m] = dist[c] + 1;
                    queue[tail++] = m;
                    n++;
                }
            }
            return n;
        }

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
            Component = 0;
            FlagReachable = true;
            // No flag on the board.  This used to return 0 -- the *best*
            // score any state can have -- on the reading "nothing to steer by".
            // A flag leaves PF for exactly one reason: something has been
            // pushed on top of it, which on most boards makes the level
            // unwinnable and on none of them makes it won.  Layer 5 with
            // --push-shot-run walks straight into it: on LaserTank.lvl 2 a
            // block goes up column 14 in one laser run and lands on the flag,
            // and at width 128 every board in the frontier was that board,
            // scoring 4 against the winning line's 11.
            if (fx < 0) { FlagReachable = false; return Unreachable; }
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

            int comp = 0;
            for (int i = 0; i < 256; i++) if (dist[i] >= 0) comp++;
            Component = comp;

            int at = dist[tx * 16 + ty];
            FlagReachable = at >= 0;
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
            RouteObstacles = 0;
            RouteFerry = 0;
            RouteStop = 0;
            if (fx < 0) return Unreachable;             // buried; see FlagDistance
            if (tx == fx && ty == fy) return 0;
            RouteObstacles = -1;                        // no route, until one settles
            RouteFerry = -1;
            RouteStop = WantStop ? StopPrice(e, fx * 16 + fy) : 0;

            int[] cost = _cost;
            for (int i = 0; i < 256; i++) { cost[i] = int.MaxValue; _pred[i] = -1; }
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
                if (c == tx * 16 + ty) { RouteObstacles = CountOnRoute(e, c); return d; }

                int cx = c >> 4, cy = c & 15;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 1 ? 1 : k == 3 ? -1 : 0);
                    int ny = cy + (k == 0 ? -1 : k == 2 ? 1 : 0);
                    if (nx < 0 || nx > 15 || ny < 0 || ny > 15) continue;
                    Relax(ref n, d, c, nx * 16 + ny, Price(e.Game.PF[nx, ny]), cost);
                }

                byte here = e.Game.PF[cx, cy];
                if (!Obj.IsTunnel(here)) continue;
                for (int t = _tunnelHead[Obj.GetTunnelID(here) & 7]; t >= 0; t = _tunnelNext[t])
                    if (t != c) Relax(ref n, d, c, t, 0, cost);
            }

            int man = (tx > fx ? tx - fx : fx - tx) + (ty > fy ? ty - fy : fy - ty);
            return Unreachable + man;
        }


        // ---- layer 2: the obstacles between here and the flag --------------

        /// The cells that stand between where the tank has *demonstrably* got
        /// to and the flag, nearest first.  `reached` is the movement closure's
        /// answer to "where can the tank go", one bool per cell; `into` is
        /// filled with the obstacle cells and its used length returned.
        ///
        /// This is layer 2's whole premise, and the first version of it was
        /// wrong in a way worth recording.  That version derived the obstacles
        /// from the *priced route*: run the Dijkstra from the flag to the tank
        /// and call every cell on it that costs more than an empty step an
        /// obstacle.  Traced over 384 expansions on levels layer 0 had failed,
        /// **240 of them -- 62% -- found no obstacle at all**: the price list
        /// said the flag was five cheap steps away while the tank plainly could
        /// not get there.  A price list only knows what a cell costs to enter.
        /// It does not know that the cell is covered by an anti-tank, that the
        /// thin ice on the way has already been used, or which mouth a tunnel
        /// actually pairs with -- and those are precisely what stops a tank on
        /// the levels a solver fails.
        ///
        /// So the reachable set is not modelled, it is *executed*: `reached` is
        /// the set of cells the Goto closure actually stood on, with death,
        /// ice, conveyors and tunnels resolved by having happened.  The
        /// Dijkstra then runs from the flag and stops at the first reached cell
        /// it settles, and the cells on the path between the two are what is in
        /// the way.  The model proposes only the *ordering*; every claim about
        /// what the tank can do came from the engine.
        ///
        /// Two kinds of cell come back, and the difference is the interesting
        /// half:
        ///
        ///   A cell that costs something to enter -- a brick, a block, a mirror,
        ///   water -- is its own subgoal: make it cheaper.
        ///
        ///   A cell that costs nothing to enter and is still not reached is a
        ///   cell the tank *died* in, or one an anti-tank covers.  There is
        ///   nothing to shoot at the cell itself, so the anti-tanks aligned with
        ///   it become the targets instead.  That is Tutor 75, "Pass the
        ///   anti-tanks", derived rather than recognised.
        public int FrontierObstacles(Engine e, bool[] reached, int[] into)
        {
            int fx = -1, fy = -1;
            for (int x = 0; x < 16 && fx < 0; x++)
                for (int y = 0; y < 16; y++)
                    if (e.Game.PF[x, y] == Obj.Flag) { fx = x; fy = y; break; }
            if (fx < 0) return 0;                       // this one returns a *count*

            int[] cost = _cost;
            for (int i = 0; i < 256; i++) { cost[i] = int.MaxValue; _pred[i] = -1; }
            for (int i = 0; i < 8; i++) _tunnelHead[i] = -1;
            for (int c = 0; c < 256; c++)
            {
                byte cell = e.Game.PF[c >> 4, c & 15];
                if (!Obj.IsTunnel(cell)) { _tunnelNext[c] = -1; continue; }
                int id = Obj.GetTunnelID(cell) & 7;
                _tunnelNext[c] = _tunnelHead[id];
                _tunnelHead[id] = c;
            }

            int n = 0, start = fx * 16 + fy, hit = -1;
            cost[start] = 0;
            Push(ref n, 0, start);

            while (n > 0)
            {
                int top = Pop(ref n);
                int d = top >> 8, c = top & 0xFF;
                if (d > cost[c]) continue;                      // a stale duplicate
                if (reached[c] && c != start) { hit = c; break; }

                int cx = c >> 4, cy = c & 15;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 1 ? 1 : k == 3 ? -1 : 0);
                    int ny = cy + (k == 0 ? -1 : k == 2 ? 1 : 0);
                    if (nx < 0 || nx > 15 || ny < 0 || ny > 15) continue;
                    Relax(ref n, d, c, nx * 16 + ny, Price(e.Game.PF[nx, ny]), cost);
                }

                byte here = e.Game.PF[cx, cy];
                if (!Obj.IsTunnel(here)) continue;
                for (int t = _tunnelHead[Obj.GetTunnelID(here) & 7]; t >= 0; t = _tunnelNext[t])
                    if (t != c) Relax(ref n, d, c, t, 0, cost);
            }

            if (hit < 0) return 0;                              // the flag is walled off

            // Walk back toward the flag.  `hit` is reached, everything after it
            // on the chain is not (a cheaper reached cell would have settled
            // first), so the chain is the obstacle list already in order.
            int used = 0;
            for (int c = _pred[hit]; c >= 0 && used < into.Length; c = _pred[c])
            {
                if (Price(e.Game.PF[c >> 4, c & 15]) > 1) into[used++] = c;
                else used = Threats(e, c, into, used);
                if (_pred[c] < 0) break;
            }
            return used;
        }

        /// The anti-tanks that can see `cell`, appended to `into`.  Scanned
        /// along the four rays and stopped only by Solid, so this is a superset:
        /// it can name an anti-tank that would not in fact have the shot, which
        /// costs a wasted target and never loses one.
        private static int Threats(Engine e, int cell, int[] into, int used)
        {
            int ox = cell >> 4, oy = cell & 15;
            for (int k = 0; k < 4 && used < into.Length; k++)
            {
                int dx = k == 1 ? 1 : k == 3 ? -1 : 0;
                int dy = k == 0 ? -1 : k == 2 ? 1 : 0;
                for (int x = ox + dx, y = oy + dy;
                     x >= 0 && x < 16 && y >= 0 && y < 16;
                     x += dx, y += dy)
                {
                    byte c = e.Game.PF[x, y];
                    if (c == Obj.Solid) break;
                    if (c >= Obj.AntiTankUp && c <= Obj.AntiTankLeft)
                    {
                        into[used++] = x * 16 + y;
                        break;
                    }
                }
            }
            return used;
        }

        /// How many cells on the settled route cost more than an empty step.
        ///
        /// The scalar WorkDistance returns folds "far" and "obstructed" into one
        /// number -- five bricks in a wall and a forty-step walk both read as
        /// 20 -- and those are not the same board.  Splitting them is free here
        /// because the pred chain is already built.
        private int CountOnRoute(Engine e, int at)
        {
            int n = 0;
            RouteFerry = 0;
            for (int c = at; c >= 0; c = _pred[c])
            {
                byte cell = e.Game.PF[c >> 4, c & 15];
                if (Price(cell) > 1) n++;
                if (cell == Obj.Water) RouteFerry += ToNearestBlock(e, c);
            }
            return n;
        }

        /// Manhattan distance from `cell` to the nearest movable block, or 0
        /// when the board has none -- a water crossing with nothing left to
        /// fill it with is not a distance the search can shorten, and scoring
        /// it as a large constant would only add noise to every sibling alike.
        ///
        /// Crystal blocks are not counted: `CheckLLoc` case 19 lets a laser
        /// straight through one and the price list already treats crystal as
        /// permanently blocking, so calling one a ferry candidate would promise
        /// a bridge the rest of the heuristic says cannot be built.
        private static int ToNearestBlock(Engine e, int cell)
        {
            int ox = cell >> 4, oy = cell & 15, best = int.MaxValue;
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                {
                    if (e.Game.PF[x, y] != Obj.Block) continue;
                    int d = (x > ox ? x - ox : ox - x) + (y > oy ? y - oy : oy - y);
                    if (d < best) best = d;
                }
            return best == int.MaxValue ? 0 : best;
        }

        /// How many cells the nearest block has to travel to reach `target`,
        /// or Unreachable when none can get there at all.
        ///
        /// ToNearestBlock's Manhattan is what RouteFerry uses and it says so:
        /// whether the block can *actually* be pushed there is a question about
        /// MoveObj, ice and where the tank can stand.  For a ferry that
        /// approximation is harmless -- water is usually open on several sides.
        /// For a stop cell it is not, and LaserTank.lvl 2 is the counterexample
        /// that forced this: the stop cell is (13,1) and the block sitting at
        /// (15,14) reaches (15,1) in one laser run, two cells away by Manhattan
        /// and *infinitely* far by push, because moving it left again needs the
        /// tank at x=16.  The whole width went into that ferry and stayed there.
        ///
        /// So: a backward BFS over block positions.  A block goes from A to
        /// A+d when A+d is enterable and A-d is enterable -- the cell whatever
        /// pushes it has to come from, which is the same approximation Macro.cs
        /// makes and, unlike Manhattan, gets column 15 right.  It over-counts a
        /// push a mirror could make and under-counts nothing, and like every
        /// other number in this file it only orders states the expansion
        /// already produced.
        private int PushDistanceToBlock(Engine e, int target)
        {
            int[] dist = _dist, queue = _queue;
            for (int i = 0; i < 256; i++) dist[i] = -1;
            int head = 0, tail = 0, best = Unreachable;
            BuildRays(e);

            dist[target] = 0;
            queue[tail++] = target;
            if (e.Game.PF[target >> 4, target & 15] == Obj.Block) return 0;

            while (head < tail)
            {
                int c = queue[head++];
                int cx = c >> 4, cy = c & 15;
                for (int k = 0; k < 4; k++)
                {
                    int dx = k == 1 ? 1 : k == 3 ? -1 : 0;
                    int dy = k == 0 ? -1 : k == 2 ? 1 : 0;
                    // The block was at `from` and was pushed to `c`; whatever
                    // pushed it stood at `behind`.
                    int fx = cx - dx, fy = cy - dy;
                    if (fx < 0 || fx > 15 || fy < 0 || fy > 15) continue;
                    int from = fx * 16 + fy;
                    if (!_rayOk[k][from]) continue;      // nothing can push it that way
                    if (dist[from] >= 0) continue;
                    byte cell = e.Game.PF[fx, fy];
                    if (cell == Obj.Block)
                    {
                        dist[from] = dist[c] + 1;
                        // A block already paying for another requirement is a
                        // wall here, not a candidate: spending it twice is what
                        // made a dead board score better than a winning one.
                        bool taken = false;
                        for (int r = 0; r < _stopResLen; r++)
                            if (_stopReserved[r] == from) { taken = true; break; }
                        if (!taken && dist[from] < best) best = dist[from];
                        continue;                       // a block is not walked through
                    }
                    if (!Passable(cell)) continue;
                    dist[from] = dist[c] + 1;
                    queue[tail++] = from;
                }
            }
            return best;
        }

        /// What it costs that the tank cannot *stop* where it has to stop.
        ///
        /// The first version of this priced every conveyor on the route and was
        /// junk: the priced route walks straight across a conveyor field, most
        /// of its cells discharge somewhere off it, and the term came out ~20
        /// requirements deep and *rose* along the hand recording of level 2 --
        /// 13 -> 36 -> 107 -- because placing a block reroutes the Dijkstra
        /// through fresh conveyors faster than it satisfies old ones.  Being
        /// carried off the priced route is not a failure; the ride usually
        /// arrives somewhere useful by a longer way round, which is what a
        /// conveyor level *is*.
        ///
        /// What the route cannot dodge is the last step.  The tank enters the
        /// flag by driving into it, and a drive is a key consumed while the
        /// world is quiescent -- so on the cell it drives from, it has to be
        /// standing still.  If that cell is a conveyor, the cell it discharges
        /// into has to stop being enterable, and no other term in this file
        /// moves when a block gets nearer that cell.
        ///
        /// Then the same question again, because stopping somewhere is not the
        /// same as getting there: a cell no conveyor feeds has to be *driven*
        /// into, from a neighbour the tank must in turn be able to stop on.  So
        /// this is a short backward chain, and on LaserTank.lvl 2 it is the
        /// whole level -- (13,1) so the tank can stop at (14,1) next to the
        /// flag, then (13,2) so it can stop at (14,2) to drive up from.  Two
        /// cells, named from the root, which is what the hand recording spends
        /// 32 board changes filling.
        ///
        /// Depth-capped at StopChain, and every branch is priced with the cells
        /// already on the chain excluded, so it can never ask for a block on a
        /// square it has just said the tank must stand on.
        private const int StopChain = 3;

        /// A chain branch with no way to arrive at all.  Not Unreachable: this
        /// is compared against sibling branches and then thrown away, never
        /// added to a score, so it only has to be bigger than any real chain.
        private const int StopImpossible = 1 << 20;

        /// What a requirement costs when no block on the board can reach the
        /// cell at all.  Bounded, unlike Unreachable: this is a number the beam
        /// sorts on, so it has to rank such a board last without opening a
        /// thousand-point crevasse that every other term then falls into.
        private const int StopNoWay = 64;

        private readonly int[] _stopPath = new int[16];
        private int _stopLen;

        /// Cells the chain has already found a block sitting on, i.e. a
        /// requirement that is *paid*.  A relaxation that forgets these prices
        /// one block against two requirements and calls the answer cheap, and
        /// on level 2 that is not a rounding error, it is the wrong plan: with
        /// a block on (13,2) the requirement for (13,1) costs one push -- push
        /// that very block up -- and (13,2) is then empty again.  The beam
        /// found that board, scored it better than a win and sat on it.
        ///
        /// Reservations accumulate across sibling branches of one chain rather
        /// than being unwound per branch.  That can over-reserve, which makes a
        /// state look dearer than it is; the opposite mistake is the one that
        /// cost a session.
        private readonly int[] _stopReserved = new int[64];
        private int _stopResLen;

        /// Whether RouteStop is wanted at all.  It costs a BFS per requirement
        /// and every other caller of WorkDistance predates it, so it is off
        /// unless the searcher using it says otherwise -- which also keeps
        /// every measurement taken before it byte-for-byte reproducible.
        public bool WantStop;

        /// _rayOk[d][c]: somewhere strictly behind `c` in direction `d` there is
        /// a cell the tank can *stand* on, with nothing in between.
        ///
        /// The legality test for "this block can be pushed one cell that way".
        /// Passable is not that test and level 2 says so twice: a block on
        /// (14,1) is one cell from (13,1) and can never get there, because the
        /// only square behind it is (15,1) -- a conveyor, which the tank can
        /// pass over and cannot stop on, so it can neither drive the block nor
        /// stand still long enough to shoot it.  The beam parked on that board
        /// for the same reason it parked on the last one: the term said two.
        ///
        /// One sweep per line per direction, so 4x256 for the whole board, and
        /// the edge test in the BFS is then a lookup.
        private readonly bool[][] _rayOk =
            { new bool[256], new bool[256], new bool[256], new bool[256] };

        /// Can the tank come to rest here?  Not the same as Passable: a
        /// conveyor, ice or a tunnel mouth all pass the tank through and none
        /// of them let it stop, which is the entire subject of this term.
        private static bool CanStand(byte cell) =>
            Passable(cell) && !Obj.IsTunnel(cell)
            && cell != Obj.ConveyorUp && cell != Obj.ConveyorRight
            && cell != Obj.ConveyorDown && cell != Obj.ConveyorLeft
            && cell != Obj.Ice && cell != Obj.ThinIce;

        private void BuildRays(Engine e)
        {
            for (int k = 0; k < 4; k++)
            {
                Step(k, out int dx, out int dy);
                bool[] ok = _rayOk[k];
                // Walk each line *along* d, so what has been seen so far is
                // exactly what lies behind the cell being written.
                for (int i = 0; i < 16; i++)
                {
                    int x = dx == 0 ? i : dx > 0 ? 0 : 15;
                    int y = dy == 0 ? i : dy > 0 ? 0 : 15;
                    bool seen = false;
                    for (int step = 0; step < 16; step++)
                    {
                        int cx = x + dx * step, cy = y + dy * step;
                        if (cx < 0 || cx > 15 || cy < 0 || cy > 15) break;
                        byte cell = e.Game.PF[cx, cy];
                        ok[cx * 16 + cy] = seen;
                        // A block is transparent here, and deliberately.  This
                        // sweep prices where a block *could* be pushed, and the
                        // block whose route is being priced is itself standing
                        // on the line: counting it as a wall made the term
                        // report a cell as unreachable exactly when a block was
                        // halfway to it, which read as a 1,000-point cliff in
                        // the middle of the winning line.
                        if (cell == Obj.Block) continue;
                        if (!Passable(cell)) seen = false;
                        else if (CanStand(cell)) seen = true;
                    }
                }
            }
        }

        private bool OnPath(int c)
        {
            for (int i = 0; i < _stopLen; i++) if (_stopPath[i] == c) return true;
            return false;
        }

        private static bool ConveyorDir(byte cell, out int dx, out int dy)
        {
            switch (cell)
            {
                case Obj.ConveyorUp:    dx =  0; dy = -1; return true;
                case Obj.ConveyorRight: dx =  1; dy =  0; return true;
                case Obj.ConveyorDown:  dx =  0; dy =  1; return true;
                case Obj.ConveyorLeft:  dx = -1; dy =  0; return true;
                default: dx = dy = 0; return false;
            }
        }

        private static int Step(int k, out int dx, out int dy)
        {
            dx = k == 1 ? 1 : k == 3 ? -1 : 0;
            dy = k == 0 ? -1 : k == 2 ? 1 : 0;
            return 0;
        }

        private int StopPrice(Engine e, int flag)
        {
            _stopLen = 0;
            _stopResLen = 0;
            _stopPath[_stopLen++] = flag;
            int fx = flag >> 4, fy = flag & 15, best = -1;
            for (int k = 0; k < 4; k++)
            {
                Step(k, out int dx, out int dy);
                int nx = fx + dx, ny = fy + dy;
                if (nx < 0 || nx > 15 || ny < 0 || ny > 15) continue;
                byte cell = e.Game.PF[nx, ny];
                int extra = 0;
                if (cell == Obj.Block)
                {
                    // A block parked on the one cell the tank has to stand on
                    // is not "nothing needed", and reading it that way is how
                    // the beam found a board it liked better than a win: the
                    // term went silent, so the state scored on WorkDistance
                    // alone and beat every state where the term could still
                    // see something to do.  It has to be pushed off -- one
                    // board change -- and then whatever is underneath still has
                    // to be stoppable, which PF2 is exactly what for.
                    extra = 1;
                    cell = e.Game.PF2[nx, ny];
                }
                else if (!Passable(cell)) continue;
                int v = StopNeedCell(e, nx, ny, cell, 0);
                if (v >= StopImpossible) continue;
                v += extra;
                if (best < 0 || v < best) best = v;
            }
            _stopLen = 0;
            // Every way in came back impossible, which means this model cannot
            // see the level rather than that the level cannot be played.  Say
            // nothing: a constant the size of StopImpossible added to every
            // sibling alike would only drown the terms that do know something.
            return best < 0 || best >= StopImpossible ? 0 : best;
        }

        /// What it costs for the tank to be able to occupy (x,y).
        private int StopNeed(Engine e, int x, int y, int depth) =>
            StopNeedCell(e, x, y, e.Game.PF[x, y], depth);

        /// ...reading the cell as `cell` rather than as whatever PF says, so a
        /// caller that has just decided a block is coming off it can ask about
        /// the terrain underneath.
        private int StopNeedCell(Engine e, int x, int y, byte cell, int depth)
        {
            int c = x * 16 + y;
            if (!ConveyorDir(cell, out int dx, out int dy))
                return 0;                                   // it can simply stand there

            // What the conveyor discharges into, and whether a block has to go
            // there.  The *price* waits until the rest of the chain has been
            // walked, because the walk is what discovers which blocks are
            // already spoken for.
            int into = -1;
            int ix = x + dx, iy = y + dy;
            if (ix >= 0 && ix <= 15 && iy >= 0 && iy <= 15 && !OnPath(ix * 16 + iy))
            {
                byte icell = e.Game.PF[ix, iy];
                if (icell == Obj.Block) _stopReserved[_stopResLen++] = ix * 16 + iy;
                else if (Passable(icell)) into = ix * 16 + iy;
                // anything else already stops the tank here, for free
            }

            int arrive = 0;
            if (!Fed(e, x, y) && depth < StopChain)
            {
                // Nothing delivers the tank here, so it has to drive in from a
                // neighbour it can stop on -- the same question, one cell out.
                _stopPath[_stopLen++] = c;
                arrive = -1;
                for (int k = 0; k < 4; k++)
                {
                    Step(k, out int ex, out int ey);
                    ex += x; ey += y;
                    if (ex < 0 || ex > 15 || ey < 0 || ey > 15) continue;
                    if (OnPath(ex * 16 + ey)) continue;
                    if (!Passable(e.Game.PF[ex, ey])) continue;
                    int v = StopNeed(e, ex, ey, depth + 1);
                    if (arrive < 0 || v < arrive) arrive = v;
                }
                _stopLen--;
                // No conveyor delivers the tank here and no neighbour it could
                // drive in from is even passable -- so this is not a route that
                // costs a lot, it is not a route.
                if (arrive < 0 || arrive >= StopImpossible) return StopImpossible;
            }

            int cost = 0;
            if (into >= 0)
            {
                int p = PushDistanceToBlock(e, into);
                cost = p >= Unreachable ? StopNoWay : 1 + p;
            }
            return cost + arrive;
        }

        /// Does a ride deliver the tank to (x,y)?
        ///
        /// A conveyor pointing into it, which is itself fed by a conveyor
        /// pointing into *that* -- the second half matters and level 2 is why.
        /// (15,1) points straight at (14,1), the cell next to the flag, and
        /// would answer this yes on the one-step test; nothing anywhere on the
        /// board points into (15,1), so no ride ever puts the tank there and
        /// the chain has to keep going.
        private static bool Fed(Engine e, int x, int y)
        {
            for (int k = 0; k < 4; k++)
            {
                Step(k, out int dx, out int dy);
                int ex = x + dx, ey = y + dy;
                if (ex < 0 || ex > 15 || ey < 0 || ey > 15) continue;
                if (!ConveyorDir(e.Game.PF[ex, ey], out int cx, out int cy)) continue;
                if (ex + cx != x || ey + cy != y) continue;
                for (int j = 0; j < 4; j++)
                {
                    Step(j, out int gx, out int gy);
                    int fx2 = ex + gx, fy2 = ey + gy;
                    if (fx2 < 0 || fx2 > 15 || fy2 < 0 || fy2 > 15) continue;
                    if (!ConveyorDir(e.Game.PF[fx2, fy2], out int ax, out int ay)) continue;
                    if (fx2 + ax == ex && fy2 + ay == ey) return true;
                }
            }
            return false;
        }

        /// The price list, for a caller that needs to ask whether a cell got
        /// cheaper -- which is layer 2's subgoal test.
        public static int PriceOf(byte cell) => Price(cell);

        private void Relax(ref int n, int d, int from, int to, int price, int[] cost)
        {
            if (price < 0) return;                          // permanently blocked
            int nd = d + price;
            if (nd >= cost[to] || nd > 0xFFFF) return;
            cost[to] = nd;
            _pred[to] = from;
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
