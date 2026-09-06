// The instrument that says *why* a level the search cannot solve is hard.
//
// Everything else in here measures the search.  This measures the *level*, by
// replaying a winning recording -- a solver's or a human's -- and printing what
// the searchers' ranking keys do along it.  The question it answers is the one
// a beam's failure cannot: is the winning line ranked badly, or is it not
// ranked badly anywhere and simply *uphill*?
//
// A greedy beam of width W ranks its successors and keeps the best W.  It can
// therefore follow a trajectory whose heuristic descends, and it can follow one
// that goes uphill for as long as the uphill stretch's whole cross-section fits
// in W.  What it cannot do -- at any width a machine will hold -- is cross an
// excursion tens of keypresses long, because the number of states reachable in
// tens of keypresses is not a number a beam holds.  So the shape to read off
// this output is not the heuristic's value, it is the *length of the longest
// stretch that stays above the best value seen so far*, and tools/basin.py
// reads exactly that.
//
// Three heuristic columns, because the searchers rank by different things and
// fail differently: `flag` is FlagDistance, which the raw beam ranks by and
// which degenerates to 1000+manhattan the moment the flag is not reachable over
// currently-passable cells; `work` is WorkDistance, which prices water and
// blocks and so keeps a gradient across exactly that case; `ferry` is layer 5's
// term, how far the nearest block still is from the water on the route, which is
// the only one of the three that moves while a block is being *carried*.
// `board` marks the keypresses that changed the playfield, which is the
// granularity layer 5 searches at -- the same excursion measured in those units
// is the number that says whether a macro layer helps.
//
// The heuristics come from Heuristic.cs and the ticks from Engine.ApplyKey, so
// this is the searchers' own view of the trajectory rather than a model of it.
using System;
using System.IO;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public sealed partial class Solver
    {
        /// Replay `keys` from the level's start, one keypress at a time, and
        /// write one TSV row per keypress.  Returns the number of rows, or 0 if
        /// the recording does not win (a losing trajectory has nothing to say
        /// about what the winning line costs).
        public int Profile(int level, byte[] keys, TextWriter w, string collection)
        {
            EngineSnapshot root = Root(level);
            _clock = System.Diagnostics.Stopwatch.StartNew();
            _nodes = 0;
            _stageNodes = long.MaxValue;
            _stageMs = long.MaxValue;

            _e.Restore(root);
            byte[] board = Board();
            int rows = 0;
            bool won = false;
            StringWriter buf = new StringWriter();

            buf.Write("# collection\tlevel\tkey\tx\ty\tflag\twork\tferry\tboard\n");
            Row(buf, collection, level, 0, false);
            rows++;

            for (int i = 0; i < keys.Length; i++)
            {
                StepResult step = _e.ApplyKey(keys[i], _opt.TickCap);
                if (step == StepResult.Win) { won = true; break; }
                if (step != StepResult.Ok) break;

                byte[] now = Board();
                bool changed = !Same(board, now);
                board = now;
                Row(buf, collection, level, i + 1, changed);
                rows++;
            }

            if (!won) return 0;
            w.Write(buf.ToString());
            return rows;
        }

        private void Row(TextWriter w, string collection, int level, int key, bool changed)
        {
            // FlagDistance first: WorkDistance leaves its own fields on the
            // Heuristic and reading them is not what this wants, but the order
            // is the order the searchers use and keeping it costs nothing.
            int flag = _h.FlagDistance(_e);
            int work = _h.WorkDistance(_e);
            // Published by the WorkDistance that just ran, so it is read here
            // and nowhere else -- Heuristic.RouteFerry, layer 5's ferry term.
            int ferry = _h.RouteFerry;
            w.Write("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\n",
                    collection, level, key, _e.Game.Tank.X, _e.Game.Tank.Y,
                    flag, work, ferry, changed ? 1 : 0);
        }

        /// The playfield alone.  PF2 and the two bitmap fields are deliberately
        /// out: PF2 moves when the tank steps onto a conveyor and the bitmaps
        /// are cosmetic, so including either would mark almost every keypress as
        /// a board change and the count would mean nothing.
        private byte[] Board()
        {
            byte[] b = new byte[TGAMEREC.W * TGAMEREC.H];
            for (int x = 0; x < TGAMEREC.W; x++)
                for (int y = 0; y < TGAMEREC.H; y++)
                    b[x * TGAMEREC.H + y] = _e.Game.PF[x, y];
            return b;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
