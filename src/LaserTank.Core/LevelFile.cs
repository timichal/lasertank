// Readers for the community file formats.  Layouts are documented in
// PROGRESS.md ("Data formats") and were decoded against the real files;
// tools/dump_level.py and tools/replay_all.py read the same bytes in Python.
//
// Strings are latin-1 and NUL-terminated inside fixed-width fields.  They are
// decoded byte-for-byte so a name can be written back unchanged -- 25 years of
// community content depends on these files staying round-trippable.
using System;
using System.IO;
using System.Text;

namespace LaserTank.Core
{
    public static class LevelFile
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private static string Str(byte[] b, int off, int len)
        {
            int n = 0;
            while (n < len && b[off + n] != 0) n++;
            return Latin1.GetString(b, off, n);
        }

        private static ushort U16(byte[] b, int off) => (ushort)(b[off] | (b[off + 1] << 8));

        /// Number of 576-byte level records in a .lvl file.
        public static int CountLevels(string path) => (int)(new FileInfo(path).Length / TLEVEL.Size);

        /// Read level `number` (1-based) from a .lvl.  Returns null past the end,
        /// which is how LoadNextLevel detects "no more levels" (LTANK2.C:1003).
        public static TLEVEL ReadLevel(string path, int number)
        {
            byte[] rec = new byte[TLEVEL.Size];
            using (FileStream f = File.OpenRead(path))
            {
                long at = (long)(number - 1) * TLEVEL.Size;
                if (at < 0 || at + TLEVEL.Size > f.Length) return null;
                f.Seek(at, SeekOrigin.Begin);
                int got = 0;
                while (got < rec.Length)
                {
                    int n = f.Read(rec, got, rec.Length - got);
                    if (n <= 0) return null;
                    got += n;
                }
            }

            TLEVEL lv = new TLEVEL();
            Array.Copy(rec, 0, lv.PF, 0, 256);
            lv.LName = Str(rec, 256, 31);
            lv.Hint = Str(rec, 287, 256);
            lv.Author = Str(rec, 543, 31);
            lv.SDiff = U16(rec, 574);
            return lv;
        }

        /// Read a .lpb: 66-byte header, then raw VK bytes.
        public static TRECORDREC ReadPlayback(string path, out byte[] keys)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < TRECORDREC.Size)
                throw new IOException(path + ": short header");

            TRECORDREC r = new TRECORDREC
            {
                LName = Str(data, 0, 31),
                Author = Str(data, 31, 31),
                Level = U16(data, 62),
                DataSize = U16(data, 64),
            };
            if (data.Length - TRECORDREC.Size < r.DataSize)
                throw new IOException(path + ": short keystream");

            keys = new byte[r.DataSize];
            Array.Copy(data, TRECORDREC.Size, keys, 0, r.DataSize);
            return r;
        }

        /// Read a .ghs / .hs: 10-byte records indexed by level - 1.
        public static bool ReadHighScore(string path, int level, out ushort moves, out ushort shots)
        {
            moves = shots = 0;
            if (!File.Exists(path)) return false;
            byte[] data = File.ReadAllBytes(path);
            int at = (level - 1) * 10;
            if (at < 0 || at + 10 > data.Length) return false;
            moves = U16(data, at);
            shots = U16(data, at + 2);
            return moves != 0;
        }
    }
}
