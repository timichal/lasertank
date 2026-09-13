// The scraped goal board, as a ranking key -- next actions item 6's --goal-board.
//
// **What it is.**  lasertanksolutions.blogspot.com posts one screenshot per
// flag showing the board at the moment that flag was reached.  tools/harvest.py
// decodes those to `PF` -- every tile derived from the game's own committed
// sprite sheet, 0 unknown cells over 173 boards -- and banks them.  This file
// reads that bank and turns one banked board into a number the push beam can
// rank by: how far the board in hand still is from the board the blogger
// finished on.
//
// **A level solved with one of these is hint-assisted and must never enter the
// solver's headline rate.**  That is not a convention this file can enforce on
// its own, so the harness enforces it three ways: --goal-board moves the default
// output directory (`solutions` -> `solutions-hint`, and `data/solutions` ->
// `data/solutions-hint` in the interactive driver, which is the one that writes
// into git), every report row it produces carries `"hint": "goal-board"`, and
// the run says so on stdout.  An explicit --out is honoured -- the user has said
// where -- and the row carries the stamp either way, which is the half a
// directory name cannot do.  What the hint-assisted solutions are *for* is
// stated in docs/solver/history.md, closed item 6:
// they are real recordings on off-distribution long levels, which is the sample
// layer 4 is fit on and the one nobody has twenty hours to play by hand.
//
// **Why it is not "cells still differing", which is how the item worded it.**
// Hamming distance to the goal board is *flat over exactly the choice the key
// exists to make*.  On `LaserTank.lvl` 10 the root board differs from the goal
// in 12 cells, and it still differs in 12 after each of the three root pushes
// --analyze offers, because every push vacates one cell and fills another:
//
//     root                12 cells differing        goal distance 30
//     push (1,13) up      12                                      29
//     push (13,14) left   12                                      31
//     push (13,14) up     12                                      31
//
// A count of misplaced objects cannot move while an object is *in transit*,
// which is the same failure RouteFerry exists to fix one level down: the whole
// carry scores the same as standing still and only the arrival scores anything.
// So the key is the *assignment*: match each misplaced object to a goal cell
// wanting that same object and sum how far each still has to travel.  That is
// the right-hand column above, and it separates the three pushes the way the
// item's acceptance test says they have to separate -- (1,13) up closes a
// distance of 2 to 1 and both pushes of (13,14) open theirs from 2 to 3.
//
// The 30 is also a reconciliation rather than a fresh number: session 34
// computed the minimum total push distance on level 10's goal board by
// exhaustive assignment, by hand, and got 30.  This agrees with it.
//
// **What it is not.**  It is not a lower bound on anything, and nothing here
// pretends it is: an object's manhattan distance to its goal cell ignores walls
// and ignores whether the tank can get behind it to push, exactly as RouteFerry
// does and for the same stated reason -- deciding that is a second
// implementation of the game.  It only orders successors the engine already
// produced, so an over- or under-estimate costs search order and can never
// admit a state the engine did not.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using LaserTank.Core;

namespace LaserTank.Solver
{
    /// One decoded goal board: the playfield, and the tank beside it.
    ///
    /// The tank is beside it rather than in it because that is what the engine
    /// does -- BuildBMField clears `PF` at the tank's cell on load
    /// (`Engine.cs:348`), so a cell the tank stands on carries the terrain
    /// under it and the tank is separate output.
    public sealed class GoalBoard
    {
        public string Collection = "";
        public int Level;
        public string Name = "", Tag = "", Url = "";
        public readonly byte[] PF = new byte[256];
        public int TankX = -1, TankY = -1, TankDir;

        /// The post's own panel counters, hand-read; -1 when nobody has read
        /// them.  See bench/goal-counters.json for why these are hand input
        /// when every tile on the board is derived.
        public int Moves = -1, Shots = -1;
    }

    /// tools/harvest.py bank -> GoalBoard, keyed (collection, level).
    public static class GoalBank
    {
        /// dump_level.py's symbols, which are what the bank stores.  The digits
        /// are tunnel ids: `cell()` in tools/harvest.py prints `(v & 0x0F) >> 1`
        /// for anything at or above Obj.Tunnel, so the inverse is the id back in
        /// its bits with the "waiting to transport" low bit clear -- which is
        /// the right way round, because a screenshot cannot show that bit and a
        /// goal board has no business asserting it.
        private const string Symbols = ".TF~#Bb^>v<mnopURDLCqwerIi";

        private static bool Parse(char c, out byte pf)
        {
            int i = Symbols.IndexOf(c);
            if (i >= 0) { pf = (byte)i; return true; }
            if (c >= '0' && c <= '7') { pf = (byte)(Obj.Tunnel | ((c - '0') << 1)); return true; }
            pf = 0;
            return false;
        }

        private static int Dir(string s) =>
            s == "up" ? 1 : s == "right" ? 2 : s == "down" ? 3 : s == "left" ? 4 : 0;

        /// Load the bank.  Throws with the offending row named -- a goal board
        /// is a ranking key, and a key that half-parsed is worse than none.
        public static Dictionary<string, GoalBoard> Load(string path)
        {
            Dictionary<string, GoalBoard> bank = new Dictionary<string, GoalBoard>();
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("levels", out JsonElement levels))
                throw new InvalidDataException(path + ": no \"levels\" array"
                    + " -- is this a tools/harvest.py bank file?");

            foreach (JsonElement r in levels.EnumerateArray())
            {
                string coll = r.GetProperty("coll").GetString();
                int level = r.GetProperty("level").GetInt32();
                JsonElement goals = r.GetProperty("goals");
                int n = goals.GetArrayLength();
                if (n == 0) continue;

                // The bank orders the sequence so that the board the level
                // *ends* on is last; the earlier ones are the per-flag
                // subgoals, which nothing reads yet.
                JsonElement g = goals[n - 1];
                GoalBoard b = new GoalBoard
                {
                    Collection = coll,
                    Level = level,
                    Name = Str(r, "name"),
                    Url = Str(r, "url"),
                    Tag = Str(g, "tag"),
                    Moves = Num(r, "moves"),
                    Shots = Num(r, "shots"),
                };

                JsonElement pf = g.GetProperty("pf");
                if (pf.GetArrayLength() != 16)
                    throw new InvalidDataException(GoalBank.Key(coll, level) + ": "
                        + pf.GetArrayLength() + " rows, want 16");
                for (int y = 0; y < 16; y++)
                {
                    string row = pf[y].GetString() ?? "";
                    if (row.Length != 16)
                        throw new InvalidDataException(GoalBank.Key(coll, level)
                            + " row " + y + ": " + row.Length + " cells, want 16");
                    for (int x = 0; x < 16; x++)
                    {
                        if (!Parse(row[x], out byte v))
                            throw new InvalidDataException(GoalBank.Key(coll, level)
                                + " (" + x + "," + y + "): '" + row[x]
                                + "' is not a PF symbol -- an undecoded cell has"
                                + " no business in a ranking key");
                        b.PF[x * 16 + y] = v;
                    }
                }

                if (g.TryGetProperty("tank", out JsonElement t)
                    && t.ValueKind == JsonValueKind.Object)
                {
                    b.TankX = t.GetProperty("x").GetInt32();
                    b.TankY = t.GetProperty("y").GetInt32();
                    b.TankDir = Dir(t.GetProperty("dir").GetString() ?? "");
                }
                bank[Key(coll, level)] = b;
            }
            return bank;
        }

        public static string Key(string collection, int level) =>
            collection + ":" + level.ToString(CultureInfo.InvariantCulture);

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : "";

        private static int Num(JsonElement e, string name) =>
            e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32() : -1;
    }

    /// The distance from a board in hand to a banked goal board.
    ///
    /// One instance per Solver, like Heuristic, and for the same reason: it
    /// holds scratch arrays and a Solver is one level on one thread.
    public sealed class GoalMetric
    {
        private readonly byte[] _goal;

        /// The price of an object that has to be created or destroyed rather
        /// than moved -- a brick shot away, a block sunk in water, an anti-tank
        /// left as a wreck.  A cliff and not a gradient, deliberately: nothing
        /// here knows *which* block is going to fill which hole, and inventing
        /// an answer would put a wrong gradient on top of RouteFerry's right
        /// one.  RouteFerry is the term that carries the block; this one says
        /// which hole is on the blogger's list.
        private readonly int _miss;

        /// Cells of one PF value that are misplaced, held and wanted.  256 is
        /// the honest bound, and the arrays are per-instance, so a scan
        /// allocates nothing.
        private readonly int[] _have = new int[256];
        private readonly int[] _want = new int[256];
        private readonly int[] _types = new int[256];
        private readonly bool[] _seenType = new bool[256];
        private readonly bool[] _usedRow = new bool[256];

        /// `Game.PF` flattened, so that the two entry points below can share
        /// one implementation.  An Effect is a board delta and has no engine
        /// state behind it once --analyze has drained its snapshots, so the
        /// board -- not the engine -- has to be the thing this measures.
        private readonly byte[] _cur = new byte[256];

        // Hungarian scratch, sized for MaxAssign.  See Assign.
        private const int MaxAssign = 32;
        private readonly int[,] _cost = new int[MaxAssign + 1, MaxAssign + 1];
        private readonly int[] _u = new int[MaxAssign + 1];
        private readonly int[] _v = new int[MaxAssign + 1];
        private readonly int[] _p = new int[MaxAssign + 1];
        private readonly int[] _way = new int[MaxAssign + 1];
        private readonly int[] _minv = new int[MaxAssign + 1];
        private readonly bool[] _used = new bool[MaxAssign + 1];

        /// Greedy's, not the Hungarian's: it runs above MaxAssign, so it is the
        /// one scratch array that has to be sized for the whole board.
        private readonly bool[] _taken = new bool[256];

        public GoalMetric(byte[] goal, int miss)
        {
            _goal = goal;
            _miss = miss;
        }

        /// Cells of `PF` that do not match the goal board.  The item's own
        /// wording for the key, kept because it is the number to *report* --
        /// "this run got the board to within four cells of the blogger's" is
        /// what a human wants to hear -- and not the number to rank by.  See
        /// the header.
        public int Differing(Engine e) => Differing(Flatten(e));

        public int Differing(byte[] pf)
        {
            int n = 0;
            for (int c = 0; c < 256; c++) if (pf[c] != _goal[c]) n++;
            return n;
        }

        private byte[] Flatten(Engine e)
        {
            for (int c = 0; c < 256; c++) _cur[c] = e.Game.PF[c >> 4, c & 15];
            return _cur;
        }

        /// The ranking key: summed over every misplaced object, how far it
        /// still is from a goal cell that wants one like it, plus `_miss` for
        /// each object that has to appear or disappear instead of move.
        ///
        /// Cells that already agree are dropped from both sides before the
        /// assignment, which is exact rather than an approximation: the costs
        /// are a metric, so if a cell already holds the object the goal wants
        /// there is an optimal assignment that matches it to itself (swapping
        /// a->c, c->b for c->c, a->b cannot cost more, by the triangle
        /// inequality).  It is also what keeps this cheap -- on a board that is
        /// nearly right the assignment is over a handful of cells rather than
        /// over the corpus's twenty-block Sokobans.
        public int Distance(Engine e) => Distance(Flatten(e));

        public int Distance(byte[] pf)
        {
            int nt = 0;
            for (int c = 0; c < 256; c++)
            {
                byte now = pf[c], want = _goal[c];
                if (now == want) continue;
                // Dirt is the background, not an object: "this cell must become
                // dirt" is already priced as the object standing on it being
                // surplus, and matching two hundred dirt cells to each other
                // would cost more than the search it is ordering.
                if (now != Obj.Dirt && !_seenType[now]) { _seenType[now] = true; _types[nt++] = now; }
                if (want != Obj.Dirt && !_seenType[want]) { _seenType[want] = true; _types[nt++] = want; }
            }

            int total = 0;
            for (int i = 0; i < nt; i++)
            {
                int t = _types[i];
                _seenType[t] = false;
                int nh = 0, nw = 0;
                for (int c = 0; c < 256; c++)
                {
                    byte now = pf[c], want = _goal[c];
                    if (now == want) continue;
                    if (now == t) _have[nh++] = c;
                    if (want == t) _want[nw++] = c;
                }
                total += Assign(nh, nw) + _miss * Math.Abs(nh - nw);
            }
            return total;
        }

        /// Minimum total manhattan distance matching `_have[0..nh)` to
        /// `_want[0..nw)`, the surplus on either side left unmatched.
        ///
        /// Hungarian (Jonker-Volgenant potentials, the e-maxx formulation),
        /// O(n^2 m).  Above MaxAssign objects of one kind it falls back to
        /// nearest-first, which is a worse *order* and never a wrong answer --
        /// and the cap is a long way past anything the corpus contains once the
        /// agreeing cells have been dropped.
        private int Assign(int nh, int nw)
        {
            int n = Math.Min(nh, nw);
            if (n == 0) return 0;
            int m = Math.Max(nh, nw);
            if (m > MaxAssign) return Greedy(nh, nw);
            bool flip = nh > nw;          // rows must be the smaller side

            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                    _cost[i, j] = Dist(flip ? _have[j - 1] : _have[i - 1],
                                       flip ? _want[i - 1] : _want[j - 1]);

            Array.Clear(_u, 0, n + 1);
            Array.Clear(_v, 0, m + 1);
            Array.Clear(_p, 0, m + 1);
            for (int i = 1; i <= n; i++)
            {
                _p[0] = i;
                int j0 = 0;
                for (int j = 0; j <= m; j++) { _minv[j] = int.MaxValue; _used[j] = false; }
                do
                {
                    _used[j0] = true;
                    int i0 = _p[j0], delta = int.MaxValue, j1 = 0;
                    for (int j = 1; j <= m; j++)
                    {
                        if (_used[j]) continue;
                        int cur = _cost[i0, j] - _u[i0] - _v[j];
                        if (cur < _minv[j]) { _minv[j] = cur; _way[j] = j0; }
                        if (_minv[j] < delta) { delta = _minv[j]; j1 = j; }
                    }
                    for (int j = 0; j <= m; j++)
                    {
                        if (_used[j]) { _u[_p[j]] += delta; _v[j] -= delta; }
                        else _minv[j] -= delta;
                    }
                    j0 = j1;
                } while (_p[j0] != 0);
                do { int j1 = _way[j0]; _p[j0] = _p[j1]; j0 = j1; } while (j0 != 0);
            }
            return -_v[0];
        }

        /// The fallback above MaxAssign: repeatedly take the closest still-free
        /// pair.  Only ever reached on a board with more than 32 *misplaced*
        /// objects of one single kind.
        private int Greedy(int nh, int nw)
        {
            int n = Math.Min(nh, nw), total = 0;
            for (int j = 0; j < nw; j++) _taken[j] = false;
            for (int i = 0; i < nh; i++) _usedRow[i] = false;
            for (int k = 0; k < n; k++)
            {
                int best = int.MaxValue, bi = -1, bj = -1;
                for (int i = 0; i < nh; i++)
                {
                    if (_usedRow[i]) continue;
                    for (int j = 0; j < nw; j++)
                    {
                        if (_taken[j]) continue;
                        int d = Dist(_have[i], _want[j]);
                        if (d < best) { best = d; bi = i; bj = j; }
                    }
                }
                _usedRow[bi] = true;
                _taken[bj] = true;
                total += best;
            }
            return total;
        }

        private static int Dist(int a, int b) =>
            Math.Abs((a >> 4) - (b >> 4)) + Math.Abs((a & 15) - (b & 15));
    }
}
