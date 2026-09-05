// Emits the per-tick trace in exactly the format oracle/driver.c writes.
//
// This file is a formatting contract, not a design decision: field order,
// spacing, widths and the fnv1a hashes must match `trace_tick()` byte for byte,
// because tools/difftrace.py compares the two textually.  Any prettier format
// would turn a real divergence into a parser artefact.  Newlines are "\n" only;
// driver.c opens the trace with "wb".
using System.Globalization;
using System.IO;
using System.Text;
using LaserTank.Core;

namespace LaserTank.Cli
{
    public sealed class TraceWriter
    {
        private readonly TextWriter _w;
        private readonly bool _field, _bmf;
        private readonly byte[] _flat = new byte[256];
        private readonly StringBuilder _sb = new StringBuilder(4096);

        public TraceWriter(string path, bool field, bool bmf)
        {
            // "\n" line endings and latin-1, matching fopen(path, "wb") + fprintf.
            _w = new StreamWriter(File.Create(path), Encoding.GetEncoding(28591))
            {
                NewLine = "\n",
            };
            _field = field;
            _bmf = bmf;
        }

        /// LTANK2.C has no hash of its own; this is driver.c's fnv1a over the
        /// 256 bytes of a playfield, printed as %08lx.
        public static uint Fnv1a(byte[] b)
        {
            uint h = 2166136261u;
            for (int i = 0; i < b.Length; i++)
            {
                h ^= b[i];
                h *= 16777619u;
            }
            return h;
        }

        private uint Hash(byte[,] f)
        {
            TGAMEREC.Flatten(f, _flat);
            return Fnv1a(_flat);
        }

        private void PutField(string tag, byte[,] f)
        {
            TGAMEREC.Flatten(f, _flat);
            _sb.Append(' ').Append(tag).Append('=');
            for (int i = 0; i < _flat.Length; i++) _sb.Append(_flat[i].ToString("x2"));
        }

        public void Header(string levels, int level, string name, string author, int keys)
        {
            // Line 1 names the engine; difftrace.py ignores it and reads
            // level/name/author/keys off line 2 to check both sides ran the
            // same input.  Line 2 is identical to the oracle's.
            _w.WriteLine("# lasertank core trace");
            _w.WriteLine("# levels={0} level={1} name={2} author={3} keys={4}",
                         levels, level, name, author, keys);
        }

        public void Tick(long tick, Engine e)
        {
            TGAMEREC g = e.Game;
            _sb.Clear();
            _sb.AppendFormat(CultureInfo.InvariantCulture,
                "t={0} T={1},{2},{3},{4},{5} L={6},{7},{8},{9},{10} " +
                "S={11},{12} P={13} C={14} SlT={15},{16},{17},{18},{19} " +
                "SlO={20},{21},{22},{23},{24} N={25} " +
                "A={26},{27} D={28} G={29} H={30:x8},{31:x8}",
                tick,
                g.Tank.X, g.Tank.Y, g.Tank.Dir, g.Tank.Firing, g.Tank.Good,
                e.laser.X, e.laser.Y, e.laser.Dir, e.laser.Firing, e.laser.Good,
                g.ScoreMove, g.ScoreShot,
                g.RecP, e.ConvMoving ? 1 : 0,
                e.SlideT.x, e.SlideT.y, e.SlideT.dx, e.SlideT.dy, e.SlideT.s,
                e.SlideO.x, e.SlideO.y, e.SlideO.dx, e.SlideO.dy, e.SlideO.s,
                e.SlideMem.count,
                e.AniLevel, e.AniCount, e.Deaths, e.Game_On ? 1 : 0,
                Hash(g.PF), Hash(g.PF2));

            // The sliding stack is game state, and its entries are 1-based
            // (quirk #6).
            for (int i = 1; i <= e.SlideMem.count && i < TICEMEM.MAX_TICEMEM; i++)
            {
                TICEREC o = e.SlideMem.Objects[i];
                _sb.AppendFormat(CultureInfo.InvariantCulture, " M{0}={1},{2},{3},{4},{5}",
                                 i, o.x, o.y, o.dx, o.dy, o.s);
            }

            if (_field) { PutField("PF", g.PF); PutField("PF2", g.PF2); }
            if (_bmf) { PutField("BMF", g.BMF); PutField("BMF2", g.BMF2); }

            _w.Write(_sb.ToString());
            _w.Write('\n');
        }

        public void Footer(string result, long ticks, int moves, int shots, uint keysUsed, int keys)
        {
            // dialogs= is the oracle's count of message boxes the stub swallowed.
            // Headless C# raises none, so it is always 0 -- and a non-zero value
            // on the oracle side is itself worth looking at.
            _w.WriteLine("# result={0} ticks={1} moves={2} shots={3} keys_used={4}/{5} dialogs=0",
                         result, ticks, moves, shots, keysUsed, keys);
        }

        public void Close() => _w.Dispose();
    }
}
