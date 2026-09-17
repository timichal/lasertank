// Item 23's feature space, computed *per node* instead of per level.
//
// `tools/fess_project.py` ran the item's own two refusals on 2026-09-17 and
// neither of them landed: over the 2,234 FERRY + SOKOBAN rows of the stride,
// `blocks x region` binned log2 puts the fourth pass's 1,475 failures across 64
// of 66 cells and separates solved from unsolved at Cramer's V 0.375,
// p < 0.0005, inside strata of the size proxy as well.  So the space is not
// degenerate over the population the item is aimed at.
//
// **That measurement could not say the one thing FESS needs, and said so.**
// `--analyze-tsv` reads the *authored* board, so every number in it is a
// root-state, per-level projection: one point per level.  FESS
// (Shoham & Schaeffer, IEEE CoG 2020) does not cycle cells over levels, it
// cycles them over the *states of one level* -- it keeps the best state in each
// occupied feature cell and advances them in turn, so that a cell holding a
// state the ranking key scores badly still gets expanded.  Whether a state
// *moves* between cells as the search pushes a block is therefore the whole
// premise, and it needs the features computed at every node.
//
// This is that computation, and it is deliberately the cheap half of the build.
// Both features already exist as per-node quantities:
//
//   * `blocks` -- the movable blocks left on the playfield, one scan of PF;
//   * `region` -- Heuristic.TankRegion, the flood whose own comment says
//     "affordable once per successor inside a beam" and "the tank's own region
//     moves on almost every push, because a block that leaves a square is a
//     square the tank can now stand on".  That sentence is a *prediction* about
//     the number below, and this file is what checks it.
//
// **Both binnings are reported, and that is not hedging.**  The projection bins
// log2 (`0`, `1`, `2-3`, `4-7`...) because *across* levels both columns are
// counts with a long tail, and the two halves of one item must not be measured
// on two different spaces -- so the binned cell is the one that joins back to
// `tools/fess_project.py`.  But log2 bins are wide, a single level's states vary
// over a far smaller range than the corpus does, and FESS's own Sokoban
// features are small integers used *raw*.  So a binned reading of "the feature
// never moves" has two readings -- the feature does not move, or the bin is too
// coarse to see it move -- and only the raw count tells them apart.  `cells=` is
// the binned space, `raw=` the unbinned one, and a build would be sized on the
// second.
//
// **It is an instrument and changes no search.**  The cell is carried on the
// Node beside `Swept`, computed only when --push-fess-trace is on, read only by
// the report below, and never by Cut, PushCut or any ranking key.  A run
// carrying the flag searches what the same run without it searches; what it
// costs is one flood per emitted successor.
//
// The four numbers per depth, and what each one would refuse:
//
//   * `moved=` -- successors whose cell differs from the parent's, binned and
//     raw.  **Raw near zero refuses the item outright**: a feature the search
//     does not move cannot be cycled, and the projection's separation would be
//     a fact about levels rather than about states.
//   * `cells=` -- distinct cells the depth's successors occupied, counted at
//     emission so that the intra-depth trim cannot hide one.
//   * `kept=` -- distinct cells still occupied after the width trim.
//   * `lost=` -- cells that were offered and hold no survivor, which is the
//     number that prices the build.  **`lost=0` refuses it too, from the other
//     side**: the beam is already spread across the space and cycling restores
//     nothing.  FESS is worth building on the gap between `cells` and `kept`,
//     and on nothing else.
//
// The level line then repeats all of that as a per-depth mean and adds the one
// number none of the above can give: **the two columns' movement counted
// apart.**  It is there because test 3 of `tools/fess_project.py` can retire
// one half of a pair, and a binning at which `region` adds nothing inside
// `blocks` strata is a binning whose cell is the single column `blocks` -- at
// which point whether *that* column moves is the only question left.  Session
// 59 is what asked it: `region` moves on a median 19% of successors and
// `blocks` on 1%, never once on 22 of 50 bench levels.  See
// docs/solver/history.md, closed item 23.
using System;
using System.Collections.Generic;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public sealed partial class Solver
    {
        /// Distinct cells offered at this depth -> how many successors landed
        /// in each.  Counted at emission, so a successor thrown away by the
        /// intra-depth cut is still counted as offered -- which is the honest
        /// denominator for "what did the trim do to the space".  Keyed by the
        /// *raw* packed cell, because the binned one is a function of it: one
        /// census answers both questions and the binned reading cannot drift
        /// from the raw one it coarsens.
        private readonly Dictionary<int, int> _fxOffered = new Dictionary<int, int>();
        private readonly HashSet<int> _fxKept = new HashSet<int>();
        private readonly HashSet<int> _fxBins = new HashSet<int>();
        private readonly HashSet<int> _fxKeptBins = new HashSet<int>();
        private int _fxSucc, _fxMoved, _fxMovedRaw;

        /// The two columns' movement counted *separately*, because test 3 of
        /// tools/fess_project.py can retire one of them: a binning at which
        /// `region` adds nothing inside `blocks` strata is a binning whose
        /// cell is the one column `blocks`, and then what matters is whether
        /// *that* column moves, not whether the pair does.
        private int _fxMovedB, _fxMovedR;

        /// Per level, for the closing line: depths seen, and the sums the means
        /// are taken over.
        private int _fxDepths, _fxOfferedAll, _fxKeptAll, _fxLostAll;
        private int _fxOfferedRawAll, _fxKeptRawAll, _fxLostRawAll;
        private long _fxSuccAll, _fxMovedAll, _fxMovedRawAll;
        private long _fxMovedBAll, _fxMovedRAll;

        /// log2, the bin fess_project.py uses: 0 -> 0, 1 -> 1, 2-3 -> 2, ...
        private static int Log2Bin(int n)
        {
            int b = 0;
            while (n > 0) { b++; n >>= 1; }
            return b;
        }

        private static string BinLabel(int b)
        {
            if (b == 0) return "0";
            int lo = 1 << (b - 1), hi = (1 << b) - 1;
            return lo == hi ? lo.ToString() : lo + "-" + hi;
        }

        /// The *raw* (blocks, region) pair of whatever state the engine is
        /// standing in, packed, +1 so that a Node nobody has projected reads 0
        /// rather than a real cell.  The offset is belt and braces -- `region`
        /// counts the tank's own square, so the pair is never (0,0) -- but a
        /// sentinel that depends on an invariant elsewhere in the file is how
        /// these things rot.
        ///
        /// Raw rather than binned because the bin is a pure function of it, so
        /// one int on the Node answers both questions and the binned reading
        /// cannot drift from the raw one it is supposed to be a coarsening of.
        /// Both counts are bounded by the 256-cell playfield, hence 9 bits.
        private int FessCell()
        {
            int blocks = 0;
            byte[,] pf = _e.Game.PF;
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 16; y++)
                    if (pf[x, y] == Obj.Block) blocks++;
            return 1 + (blocks << 9) + _h.TankRegion(_e);
        }

        private static int RawBlocks(int cell) => (cell - 1) >> 9;
        private static int RawRegion(int cell) => (cell - 1) & 511;

        /// The projection's own cell, so that a reading here joins back to
        /// tools/fess_project.py's table -- log2 by default, which is the
        /// binning its V 0.375 was measured in, or fixed width under
        /// --push-fess-bin.  The two binnings pack differently -- log2 into
        /// five bits a column, fixed width into nine -- because a fixed-width
        /// bin index is only as small as the width makes it; the value is a
        /// dictionary key and never a cell index, so the two need not agree.
        private int Binned(int cell)
        {
            int w = _opt.PushFessBin;
            if (w <= 0)
                return (Log2Bin(RawBlocks(cell)) << 5) + Log2Bin(RawRegion(cell));
            int b = RawBlocks(cell) / w, r = RawRegion(cell) / w;
            return (b << 9) + r;
        }

        private string BinnedLabel(int cell)
        {
            int w = _opt.PushFessBin;
            if (w <= 0)
            {
                int b = Binned(cell);
                return BinLabel(b >> 5) + "b/" + BinLabel(b & 31) + "r";
            }
            int lo = RawBlocks(cell) / w * w, ro = RawRegion(cell) / w * w;
            return (w == 1 ? lo.ToString() : lo + "-" + (lo + w - 1)) + "b/"
                   + (w == 1 ? ro.ToString() : ro + "-" + (ro + w - 1)) + "r";
        }

        /// At an emission site, where the engine already stands on the successor
        /// -- so this costs the flood and no Restore.  The shape is SweptNow's
        /// on purpose: off by default, zero when off, and last in the object
        /// initializer so that every derivation that wants Heuristic's scratch
        /// arrays has had them first.
        private int FessNow() => _opt.PushFessTrace ? FessCell() : 0;

        /// A binned cell as the projection prints it, plus the raw pair of the
        /// successor it was taken from -- so a line that says "one cell" can be
        /// read against the counts that produced it without a second run.
        private string CellLabel(int cell)
        {
            if (cell <= 0) return "?";
            return BinnedLabel(cell)
                   + " (" + RawBlocks(cell) + "," + RawRegion(cell) + ")";
        }

        /// One expansion's successors, against the cell their parent sat in.
        /// Called from PushBeam rather than from ExpandPush because the parent
        /// is a Node there and a snapshot here, and the cell lives on the Node.
        private void FessExpanded(Node parent, List<Node> next, int first)
        {
            for (int i = first; i < next.Count; i++)
            {
                int c = next[i].Cell;
                if (c <= 0) continue;                 // never projected; see Node.Cell
                _fxOffered.TryGetValue(c, out int had);
                _fxOffered[c] = had + 1;
                _fxSucc++;
                if (parent.Cell <= 0) continue;
                if (Binned(c) != Binned(parent.Cell)) _fxMoved++;
                if (c != parent.Cell) _fxMovedRaw++;
                if (RawBlocks(c) != RawBlocks(parent.Cell)) _fxMovedB++;
                if (RawRegion(c) != RawRegion(parent.Cell)) _fxMovedR++;
            }
        }

        /// The depth's line, after the width trim has run.  Two columns of
        /// everything: `cells/kept/lost/moved` over the projection's log2 bins,
        /// `raw` over the unbinned counts.
        private void FessDepth(int depth, List<Node> next)
        {
            _fxKept.Clear();
            _fxKeptBins.Clear();
            foreach (Node n in next)
            {
                if (n.Cell <= 0) continue;
                _fxKept.Add(n.Cell);
                _fxKeptBins.Add(Binned(n.Cell));
            }

            _fxBins.Clear();
            int lostRaw = 0, top = 0, topCell = 0;
            foreach (KeyValuePair<int, int> kv in _fxOffered)
            {
                _fxBins.Add(Binned(kv.Key));
                if (!_fxKept.Contains(kv.Key)) lostRaw++;
                if (kv.Value > top) { top = kv.Value; topCell = kv.Key; }
            }
            int lost = 0;
            foreach (int b in _fxBins) if (!_fxKeptBins.Contains(b)) lost++;

            Console.Error.WriteLine(
                "  fess d={0,3} succ={1,6} cells={2,3} kept={3,3} lost={4,3} "
                + "moved={5,3}%  raw={6,4} kept={7,4} lost={8,4} moved={9,3}%  "
                + "top={10,3}% {11}",
                depth, _fxSucc, _fxBins.Count, _fxKeptBins.Count, lost,
                _fxSucc > 0 ? 100 * _fxMoved / _fxSucc : 0,
                _fxOffered.Count, _fxKept.Count, lostRaw,
                _fxSucc > 0 ? 100 * _fxMovedRaw / _fxSucc : 0,
                _fxSucc > 0 ? 100 * top / _fxSucc : 0, CellLabel(topCell));

            _fxDepths++;
            _fxOfferedAll += _fxBins.Count;
            _fxKeptAll += _fxKeptBins.Count;
            _fxLostAll += lost;
            _fxOfferedRawAll += _fxOffered.Count;
            _fxKeptRawAll += _fxKept.Count;
            _fxLostRawAll += lostRaw;
            _fxSuccAll += _fxSucc;
            _fxMovedAll += _fxMoved;
            _fxMovedRawAll += _fxMovedRaw;
            _fxMovedBAll += _fxMovedB;
            _fxMovedRAll += _fxMovedR;

            _fxOffered.Clear();
            _fxSucc = _fxMoved = _fxMovedRaw = _fxMovedB = _fxMovedR = 0;
        }

        /// One line per level, so a bench of these greps into a table.  Printed
        /// from PushSearch beside TimeReport, i.e. after every restart, because
        /// a restart searches the same level and its depths belong to the same
        /// question.
        private void FessReport()
        {
            if (!_opt.PushFessTrace || _fxDepths == 0) return;
            Console.Error.WriteLine(
                "  fess level: depths={0} succ={1} "
                + "binned cells/depth={2:F1} kept={3:F1} lost={4:F1} moved={5}%  "
                + "raw cells/depth={6:F1} kept={7:F1} lost={8:F1} moved={9}%",
                _fxDepths, _fxSuccAll,
                _fxOfferedAll / (double)_fxDepths,
                _fxKeptAll / (double)_fxDepths,
                _fxLostAll / (double)_fxDepths,
                _fxSuccAll > 0 ? 100 * _fxMovedAll / _fxSuccAll : 0,
                _fxOfferedRawAll / (double)_fxDepths,
                _fxKeptRawAll / (double)_fxDepths,
                _fxLostRawAll / (double)_fxDepths,
                _fxSuccAll > 0 ? 100 * _fxMovedRawAll / _fxSuccAll : 0);

            // The two columns apart, because test 3 of fess_project.py can
            // retire one of them and then only the survivor's number counts.
            Console.Error.WriteLine(
                "  fess level: of those successors, blocks moves {0}% and region moves {1}%",
                _fxSuccAll > 0 ? 100 * _fxMovedBAll / _fxSuccAll : 0,
                _fxSuccAll > 0 ? 100 * _fxMovedRAll / _fxSuccAll : 0);

            _fxDepths = _fxOfferedAll = _fxKeptAll = _fxLostAll = 0;
            _fxOfferedRawAll = _fxKeptRawAll = _fxLostRawAll = 0;
            _fxSuccAll = _fxMovedAll = _fxMovedRawAll = 0;
            _fxMovedBAll = _fxMovedRAll = 0;
        }
    }
}
