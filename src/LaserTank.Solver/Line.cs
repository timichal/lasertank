// The instrument that says *where* a beam loses a winning line it was given.
//
// Everything before this measured the level (Profile.cs: how far uphill the
// winning line goes) or the enumeration (Analyze.cs's --read-dump: is the
// human's next board change among the successors at all -- 800/800, yes).
// Neither answers the question layer 5 actually fails on, which is a question
// about the *width trim*: the human's next state is offered at every step, so
// the line dies because some depth's Cut() throws it away.  Which depth, and
// beaten by what.
//
// So: replay a winning recording, keep the StateHash at every board change --
// which is exactly the granularity layer 5 searches at, one hash per push beam
// depth -- then run the real beam with those hashes in hand and report, per
// depth, whether the line's state was generated, what the ranking key made of
// it, and where it came in the sort.  Nothing here changes the search: the
// hashes are read, never given to Cut, so a --push-line run is the same search
// as the run it is explaining.
//
// Three outcomes per depth, and they call for different fixes:
//
//   * **CUT** -- generated, ranked, and outside the width.  Its rank against
//     the width is the number: rank 60 at width 48 is a tiebreak problem and
//     rank 3,000 is a ranking problem.
//   * **STALE** -- generated and refused by the closed set, i.e. the search had
//     already been here and binned it.  That is a closing-policy finding, not a
//     ranking one (cf. --push-closed).
//   * **absent** -- never generated, which after --read-dump's 800/800 should
//     mean only one thing: the *parent* was already gone, so the loss is at an
//     earlier depth and this row is downstream of it.
//
// `board=` is the same question asked of the playfield alone.  A beam that has
// lost the exact state but holds one with the same board is on an equivalent
// line and has lost nothing; one that has lost the board too is somewhere else
// entirely.  The two together separate "the pose differs" from "the search
// left".
using System;
using System.Collections.Generic;
using LaserTank.Core;

namespace LaserTank.Solver
{
    public sealed partial class Solver
    {
        /// One entry per board change along the recording, index 0 the root.
        private ulong[] _lineHash;
        private byte[][] _linePF;
        private int[] _lineH;
        private string[] _lineWhat;

        /// The hash this depth is looking for, 0 when the line has run out.
        private ulong _lineTarget;
        private bool _lineGen, _lineStale;

        // What LineBefore measured, printed by LineAfter once the trim has run.
        private ulong _lineWant;
        private int _lineRank, _lineTier, _lineOffered, _lineBoardsBefore;
        private int _lineBestH, _lineCutH;

        /// The depth at which the line first left the frontier, -1 while it is
        /// still being followed.
        private int _lineLostAt = -1;

        /// The deepest depth the report actually reached.  Without it a run
        /// that spent its budget at depth 3 is indistinguishable from one that
        /// followed the line to the end -- which is exactly the wrong thing for
        /// an instrument to be vague about.
        private int _lineReached;

        public bool HasLine => _lineHash != null;
        public int LineLostAt => _lineLostAt;
        public int LineReached => _lineReached;
        public int LineLength => _lineHash == null ? 0 : _lineHash.Length - 1;

        // Handed to the instance that searches -- see SetLine.
        public ulong[] LineHashes => _lineHash;
        public byte[][] LineBoards => _linePF;
        public int[] LineHs => _lineH;

        /// One line per board change: the keypress, where the tank was and
        /// which cells changed.  Printed before the search, because "the line
        /// is lost at depth 1" is only useful next to what depth 1 *is*.
        public string[] LineWhat => _lineWhat;

        /// Replay `keys` and record the state at every board change.  Returns
        /// false if the recording does not win -- a losing line has nothing to
        /// say about what a winning one costs.
        ///
        /// Uses this Solver's engine and leaves it dirty, so the caller builds
        /// the line on a throwaway instance and hands the arrays to the one
        /// that searches (quirk #12: LoadLevel does not reset wasIce and
        /// friends, so a replayed candidate must never share an engine with
        /// what comes after it).
        public bool TraceLine(int level, byte[] keys)
        {
            EngineSnapshot root = Root(level);
            _clock = System.Diagnostics.Stopwatch.StartNew();
            _nodes = 0;
            _stageNodes = long.MaxValue;
            _stageMs = long.MaxValue;

            _e.Restore(root);
            List<ulong> hashes = new List<ulong> { _e.StateHash() };
            List<byte[]> boards = new List<byte[]> { Board() };
            List<int> hs = new List<int> { PushH() };
            List<string> what = new List<string> { "start" };
            byte[] board = boards[0];
            bool won = false;

            for (int i = 0; i < keys.Length; i++)
            {
                StepResult step = _e.ApplyKey(keys[i], _opt.TickCap);
                if (step == StepResult.Win) { won = true; break; }
                if (step != StepResult.Ok) break;

                byte[] now = Board();
                if (Same(board, now)) continue;
                what.Add(Describe(keys[i], board, now));
                board = now;
                hashes.Add(_e.StateHash());
                boards.Add(now);
                hs.Add(PushH());
            }

            if (!won) return false;
            _lineHash = hashes.ToArray();
            _linePF = boards.ToArray();
            _lineH = hs.ToArray();
            _lineWhat = what.ToArray();
            return true;
        }

        /// `key` at the tank's current pose turned `was` into `now`.
        private string Describe(byte key, byte[] was, byte[] now)
        {
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            b.Append(KeyName(key)).Append(" tank(").Append(_e.Game.Tank.X)
             .Append(',').Append(_e.Game.Tank.Y).Append(") ");
            int shown = 0;
            for (int i = 0; i < 256; i++)
            {
                if (was[i] == now[i]) continue;
                if (++shown > 6) { b.Append("..."); break; }
                b.Append(At(i)).Append(' ').Append(NameOf(was[i]))
                 .Append("->").Append(NameOf(now[i])).Append("  ");
            }
            return b.ToString();
        }

        private static string KeyName(byte k) =>
            k == Fire ? "fire "
            : k == (byte)Engine.VK_UP ? "up   "
            : k == (byte)Engine.VK_DOWN ? "down "
            : k == (byte)Engine.VK_LEFT ? "left "
            : k == (byte)Engine.VK_RIGHT ? "right" : "k" + k;

        /// Hand a traced line to the instance that will search.
        public void SetLine(ulong[] hashes, byte[][] boards, int[] hs)
        {
            _lineHash = hashes;
            _linePF = boards;
            _lineH = hs;
            _lineLostAt = -1;
            _lineReached = 0;
        }

        /// Called from PushBeam at the top of every depth.
        private void LineBegin(int depth)
        {
            if (_lineHash == null) return;
            int at = depth + 1;
            _lineTarget = at < _lineHash.Length ? _lineHash[at] : 0UL;
            _lineGen = _lineStale = false;
        }

        /// Called from PushFresh, which every successor passes through.
        private void LineSaw(ulong h, bool fresh)
        {
            if (_lineTarget == 0UL || h != _lineTarget) return;
            if (fresh) _lineGen = true; else _lineStale = true;
        }

        /// Called from PushBeam with the depth's successors *before* the width
        /// trim.  Read-only: the report says what the trim is about to do, it
        /// does not change it.
        private void LineBefore(int depth, List<Node> next, int width)
        {
            if (_lineHash == null) return;
            int at = depth + 1;
            if (at >= _lineHash.Length) { _lineWant = 0; return; }

            _lineWant = _lineHash[at];
            _lineOffered = next.Count;
            Node hit = null;
            int boards = 0;
            foreach (Node n in next)
            {
                if (n.Hash == _lineWant) hit = n;
                if (SamePF(n.S.PF, _linePF[at])) boards++;
            }
            _lineRank = -1;
            _lineTier = -1;
            if (hit != null)
            {
                _lineTier = hit.Tier;
                _lineRank = 0;
                foreach (Node n in next) if (n != hit && Better(n, hit)) _lineRank++;
            }
            _lineBoardsBefore = boards;

            // The cutoff the trim is about to apply, so a rank reads against
            // the state that will hold the last kept slot rather than against
            // the width alone.
            _lineBestH = _lineCutH = -1;
            if (next.Count > 0)
            {
                List<Node> sorted = new List<Node>(next);
                sorted.Sort(static (a, b) => a.Tier != b.Tier ? a.Tier - b.Tier
                                           : a.H != b.H ? a.H - b.H : a.G - b.G);
                _lineBestH = sorted[0].H;
                _lineCutH = sorted[Math.Min(width, sorted.Count) - 1].H;
            }
        }

        /// Called from PushBeam with what the trim actually kept, which is what
        /// decides whether the line is still alive.
        ///
        /// Alive is asked of the *playfield*, not of the state.  A successor is
        /// (board change, the pose it was fired from) and every pose in a
        /// closure offers the same board changes, so a frontier holding the
        /// line's board at some other pose can still play the line's next move
        /// -- and the runs show it doing exactly that, the exact state dropping
        /// out at one depth and reappearing two depths later.  Losing the
        /// *board* is the loss that does not come back.
        private void LineAfter(int depth, List<Node> next)
        {
            if (_lineHash == null || _lineWant == 0) return;
            int at = depth + 1;

            bool state = false;
            int boards = 0;
            foreach (Node n in next)
            {
                if (n.Hash == _lineWant) state = true;
                if (SamePF(n.S.PF, _linePF[at])) boards++;
            }

            _lineReached = at;
            if (_lineLostAt < 0 && boards == 0) _lineLostAt = at;

            string how = state ? "kept"
                       : _lineRank >= 0 ? "CUT"
                       : _lineStale ? "STALE"
                       : _lineGen ? "cut-early"
                       : "absent";
            Console.Error.WriteLine(
                "  line d={0,3} {1,-9} rank={2,6}/{3,-6} line-h={4,5} tier={5,2} "
                + "best-h={6,5} cut-h={7,5} boards {8}->{9}{10}",
                at, how, _lineRank, _lineOffered, _lineH[at], _lineTier,
                _lineBestH, _lineCutH, _lineBoardsBefore, boards,
                boards == 0 && _lineLostAt == at ? "  <- board lost" : "");
        }

        private static bool Better(Node a, Node b) =>
            a.Tier != b.Tier ? a.Tier < b.Tier : a.H != b.H ? a.H < b.H : a.G < b.G;

        private static bool SamePF(byte[] a, byte[] b)
        {
            for (int i = 0; i < 256; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
