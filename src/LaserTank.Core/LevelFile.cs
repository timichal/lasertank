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

        /// Write a .lpb: the same 66-byte header, then raw VK bytes.
        ///
        /// Byte-for-byte the shape ReadPlayback expects and the shape the 2010
        /// binary loads, so a solver's output is not a special file format --
        /// it is a recording, indistinguishable from a human one, replayable by
        /// the original game and by tools/replay_all.py.  Size is a u16 in the
        /// header (LTANK.H:105 caps a recording at RecMax = 65500 anyway), so a
        /// longer keystream is refused here rather than silently truncated.
        public static void WritePlayback(string path, string levelName, string author,
                                         int level, byte[] keys)
        {
            if (keys.Length > 65500)
                throw new ArgumentException(
                    "keystream of " + keys.Length + " exceeds RecMax (65500)", nameof(keys));

            byte[] buf = new byte[TRECORDREC.Size + keys.Length];
            Fixed(buf, 0, 31, levelName);
            Fixed(buf, 31, 31, author);
            buf[62] = (byte)(level & 0xFF); buf[63] = (byte)((level >> 8) & 0xFF);
            buf[64] = (byte)(keys.Length & 0xFF); buf[65] = (byte)((keys.Length >> 8) & 0xFF);
            Array.Copy(keys, 0, buf, TRECORDREC.Size, keys.Length);
            File.WriteAllBytes(path, buf);
        }

        /// A fixed-width latin-1 field, NUL-padded and NUL-terminated -- one
        /// byte of the width is reserved for the terminator, as the original's
        /// char[31] fields are used.
        private static void Fixed(byte[] into, int off, int len, string s)
        {
            byte[] b = Latin1.GetBytes(s ?? "");
            int n = Math.Min(b.Length, len - 1);
            Array.Copy(b, 0, into, off, n);
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

        /// The same record with the initials, which the two list dialogs show
        /// and CheckHighScore writes.  -> null when the file or the record is
        /// not there; a present record with `Moves == 0` is the blank marker and
        /// comes back as a THSREC, because "blank" and "absent" are different
        /// answers to the padding question below.
        public static THSREC ReadHS(string path, int level)
        {
            if (!File.Exists(path)) return null;
            byte[] data = File.ReadAllBytes(path);
            int at = (level - 1) * THSREC.Size;
            if (at < 0 || at + THSREC.Size > data.Length) return null;
            return new THSREC
            {
                Moves = U16(data, at),
                Shots = U16(data, at + 2),
                Name = Str(data, at + 4, THSREC.NameSize),
            };
        }

        /// How many 10-byte records a .hs / .ghs holds.
        public static int CountHighScores(string path) =>
            File.Exists(path) ? (int)(new FileInfo(path).Length / THSREC.Size) : 0;

        /// CheckHighScore's test (LTANK2.C:1088), the whole scoring rule of the
        /// game: fewer moves wins, and moves being equal, fewer shots.  A blank
        /// record -- `Moves == 0` -- is always beaten, which is also how a
        /// `null` (no file, or short of this level) reads.
        public static bool Beats(ushort moves, ushort shots, THSREC best) =>
            best == null || best.Moves == 0 || moves < best.Moves
            || (moves == best.Moves && shots < best.Shots);

        /// The thing that makes a .hs a file format rather than an array:
        /// **it is dense and positional, so beating level 500 of a collection
        /// first writes 499 records in front of it** (LTANK2.C:1080).
        ///
        ///     if ((CurLevel * sizeof(THSREC)) > (i = SetFilePointer(F2,0,NULL,FILE_END)))
        ///     {
        ///         HS.moves = 0;
        ///         for (x = (i / sizeof(THSREC)); x < CurLevel-1; x++)
        ///             WriteFile(F2, &HS, sizeof(THSREC), ...);
        ///     }
        ///
        /// Note what is *not* zeroed. Only `moves` is set; `HS.shots` and
        /// `HS.name` keep whatever the previous high score left in that global,
        /// and this runs **before** the read of the level's own record, so the
        /// leftovers come from the last level scored in this process rather than
        /// from this one. Reading back only ever tests `moves`, so the game
        /// cannot see them -- but they are in every .hs the 2010 binary has
        /// written, and constraint 2 says these files stay writable, not merely
        /// readable. So `pad` is that global, passed in, and the caller keeps it
        /// across calls. Zeroing it instead looked obviously right and is what
        /// tools/list_check.py's --check-scores sequence would have caught.
        ///
        /// `i / sizeof(THSREC)` truncates, so a file whose length is not a whole
        /// number of records is padded from the last *complete* one and the
        /// partial tail is overwritten.
        public static void PadHighScore(string path, int level, THSREC pad)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            using FileStream f = new FileStream(path, FileMode.OpenOrCreate,
                                                FileAccess.ReadWrite);
            if ((long)level * THSREC.Size <= f.Length) return;
            byte[] blank = Bytes(new THSREC
            {
                Moves = 0,                       // HS.moves = 0, and only that
                Shots = pad?.Shots ?? 0,
                Name = pad?.Name ?? "",
            });
            long first = f.Length / THSREC.Size;
            f.Seek(first * THSREC.Size, SeekOrigin.Begin);
            for (long x = first; x < level - 1; x++)
                f.Write(blank, 0, blank.Length);
        }

        /// CheckHighScore's own write, the last two lines of it (LTANK2.C:1092):
        /// seek to the level's slot and put the record there.  The file must
        /// already be long enough, which is what PadHighScore is for.
        public static void WriteHighScore(string path, int level, THSREC rec)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            using FileStream f = new FileStream(path, FileMode.OpenOrCreate,
                                                FileAccess.ReadWrite);
            f.Seek((long)(level - 1) * THSREC.Size, SeekOrigin.Begin);
            byte[] b = Bytes(rec);
            f.Write(b, 0, b.Length);
        }

        private static byte[] Bytes(THSREC r)
        {
            byte[] b = new byte[THSREC.Size];
            b[0] = (byte)(r.Moves & 0xFF); b[1] = (byte)(r.Moves >> 8);
            b[2] = (byte)(r.Shots & 0xFF); b[3] = (byte)(r.Shots >> 8);
            Fixed(b, 4, THSREC.NameSize, r.Name);
            return b;
        }

        /// Every level's name, author and difficulty in one pass, for the level
        /// picker and the two high-score lists (LTANK_D.C:311, :766, :843).
        /// Reading 576 bytes per level and keeping 3 fields of it is what the
        /// original's `while (BytesMoved == sizeof(TLEVEL))` loop does; the
        /// playfields are dropped because none of the three lists draws one.
        public static TLEVELINFO[] ReadLevelList(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int n = data.Length / TLEVEL.Size;
            var list = new TLEVELINFO[n];
            for (int i = 0; i < n; i++)
            {
                int at = i * TLEVEL.Size;
                list[i] = new TLEVELINFO
                {
                    Number = i + 1,
                    LName = Str(data, at + 256, 31),
                    Author = Str(data, at + 543, 31),
                    SDiff = U16(data, at + 574),
                };
            }
            return list;
        }

        // ---- the editor's half: reading and writing whole records -----------

        /// The raw 576 bytes of level `number`, or null past the end.
        ///
        /// `ReadLevel` decodes; this does not, and the difference is the whole
        /// point.  Command 603 writes back **the struct it read**, so every
        /// byte the editor did not deliberately change survives -- including
        /// the bytes after a name's terminator, which is where most `.lvl`
        /// files in the wild keep the tail of some *earlier*, longer name.  A
        /// writer that re-encodes from decoded strings would quietly rewrite
        /// those and every unedited level in the collection would come back
        /// different.  Constraint 2 says these files stay writable, and this is
        /// what writable has to mean.
        public static byte[] ReadRaw(string path, int number)
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
            return rec;
        }

        /// Command 603's writer (LTANK.C:1191): `CreateFile(OPEN_ALWAYS)`,
        /// seek to `(CurLevel-1) * sizeof(TLEVEL)`, write one record.
        ///
        /// **Saving level 8 into a file that holds three creates levels 4..7**,
        /// because seeking past the end and writing zero-fills the gap -- on
        /// Win32 by documented behaviour and here by the same behaviour in
        /// FileStream.  Those filler levels are a playfield of all dirt with no
        /// name, no author and difficulty 0, and the 2010 binary lists them.
        /// It is the same shape as the `.hs` padding quirk, and like that one
        /// it is kept because the file the original writes is the file this has
        /// to write.
        public static void WriteRaw(string path, int number, byte[] rec)
        {
            if (rec == null || rec.Length != TLEVEL.Size)
                throw new ArgumentException("a level record is " + TLEVEL.Size + " bytes",
                                            nameof(rec));
            if (number < 1)
                throw new ArgumentOutOfRangeException(nameof(number), "levels are 1-based");
            using (FileStream f = new FileStream(path, FileMode.OpenOrCreate,
                                                 FileAccess.ReadWrite, FileShare.Read))
            {
                f.Seek((long)(number - 1) * TLEVEL.Size, SeekOrigin.Begin);
                f.Write(rec, 0, rec.Length);
            }
        }
    }

    /// One 576-byte level record, held as bytes and edited in place --
    /// `CurRecData`, with the original's own write widths.
    ///
    /// **The widths are not cosmetic.**  `GetWindowText(Ed1, CurRecData.LName,
    /// 30)` copies at most 29 characters plus a terminator into a 31-byte
    /// field, so byte 30 is *never* written by the editor and the bytes between
    /// the new terminator and offset 29 keep whatever the record already held.
    /// The hint is the same shape with `GetWindowText(..., 255)` into
    /// `char[256]`.  Reproducing that is what makes "load a level, edit
    /// nothing, save" a byte-for-byte identity instead of a near miss.
    public sealed class LevelRecord
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public const int NameOff = 256, NameLen = 31;
        public const int HintOff = 287, HintLen = 256;
        public const int AuthorOff = 543, AuthorLen = 31;
        public const int DiffOff = 574;

        /// The original's `GetWindowText` counts, terminator included.
        public const int NameEntry = 30, HintEntry = 255;

        public readonly byte[] Raw;

        public LevelRecord() { Raw = new byte[TLEVEL.Size]; }

        public LevelRecord(byte[] raw)
        {
            if (raw == null || raw.Length != TLEVEL.Size)
                throw new ArgumentException("a level record is " + TLEVEL.Size + " bytes",
                                            nameof(raw));
            Raw = (byte[])raw.Clone();
        }

        public static LevelRecord Read(string path, int number)
        {
            byte[] raw = LevelFile.ReadRaw(path, number);
            return raw == null ? null : new LevelRecord(raw);
        }

        public void Write(string path, int number) => LevelFile.WriteRaw(path, number, Raw);

        private string Get(int off, int len)
        {
            int n = 0;
            while (n < len && Raw[off + n] != 0) n++;
            return Latin1.GetString(Raw, off, n);
        }

        /// `SetWindowText` then `GetWindowText(h, field, entry)`: at most
        /// `entry - 1` characters and one terminator, and **nothing after
        /// that** -- the rest of the field is left as it was found.
        private void Set(int off, int entry, string s)
        {
            byte[] b = Latin1.GetBytes(s ?? "");
            int n = Math.Min(b.Length, entry - 1);
            Array.Copy(b, 0, Raw, off, n);
            Raw[off + n] = 0;
        }

        public string Name { get => Get(NameOff, NameLen); set => Set(NameOff, NameEntry, value); }
        public string Hint { get => Get(HintOff, HintLen); set => Set(HintOff, HintEntry, value); }
        public string Author
        {
            get => Get(AuthorOff, AuthorLen);
            set => Set(AuthorOff, NameEntry, value);
        }

        /// The difficulty bitmask, 1/2/4/8/16.  `EditDiffSet` writes it whole
        /// (LTANK.C:103) after checking the menu item, so unlike the strings
        /// there is no partial-write subtlety here.
        public ushort Diff
        {
            get => (ushort)(Raw[DiffOff] | (Raw[DiffOff + 1] << 8));
            set { Raw[DiffOff] = (byte)(value & 0xFF); Raw[DiffOff + 1] = (byte)(value >> 8); }
        }

        /// The playfield, `PF[x][y]` flattened x-major -- the order
        /// `TGAMEREC.Flatten` and the oracle's trace both use.
        public void SetPlayfield(byte[] flat)
        {
            if (flat == null || flat.Length != 256)
                throw new ArgumentException("a playfield is 256 bytes", nameof(flat));
            Array.Copy(flat, 0, Raw, 0, 256);
        }

        public byte[] GetPlayfield()
        {
            var flat = new byte[256];
            Array.Copy(Raw, 0, flat, 0, 256);
            return flat;
        }

        /// Command 601's `CurRecData.Hint[0] = 0` -- **only the first byte.**
        /// Clear Field truncates the hint rather than erasing it, so the rest
        /// of the old hint is still in the record and is still written to disk.
        public void ClearHint() => Raw[HintOff] = 0;
    }
}
