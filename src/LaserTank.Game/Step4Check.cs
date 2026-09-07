// Phase 5, step 4's headless self-checks.
//
// Step 3's lesson, written down in PROGRESS.md as a rule: *"an exit criterion
// that only says nothing changed is not one"* -- every step has to ask what its
// own new output is, and make it something the C can be compared against.
//
// Step 4 has two new outputs and they are checkable in different ways.
//
//   The three list dialogs' rows are derived from bytes: a .lvl record's name,
//   author and SDiff, a .hs / .ghs record's moves, shots and initials, run
//   through the original's own sprintf formats.  Nothing about them needs the
//   engine, so the check is a cross-implementation one, exactly like the sprite
//   sheets and the WAVs: --check-lists dumps every row, tools/list_check.py
//   rebuilds them in Python from the same files, and the two must agree byte
//   for byte.  Two implementations of the original's format strings that agree
//   is evidence; one implementation agreeing with itself is not.
//
//   The .hs writer's output is a *file*, and its shape is the part with a quirk
//   in it -- a positional array that has to be padded out to the level being
//   written, with padding bytes the original leaves half-uninitialised.
//   --check-scores performs a scripted sequence of writes and dumps the file as
//   hex after each one; the same tool performs the same sequence in Python.
//
// Undo, Save/Restore Position and the playback speeds are *not* here.  They are
// engine behaviour, which means the oracle can run them: tools/undo_check.py
// diffs full traces against the 25-year-old C through --script.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    public static class Step4Check
    {
        /// `-- --check-lists FILE.lvl`
        ///
        /// One `row <mode> <index> <text>` line per row of all three lists, then
        /// a sha256 per mode over the rows joined by newlines.  The rows are
        /// printed with the difficulty digit still on the front, because the
        /// digit is data -- DrawLevels colours by it -- and it is exactly the
        /// kind of thing a rewrite would quietly drop.
        ///
        /// latin-1 in, UTF-8 out: level names carry bytes like 0xE9 and stdout
        /// re-encodes them, so the tool compares the *hash* of the latin-1 bytes
        /// as well as the text and trusts the hash.
        public static int CheckLists(string lvlPath)
        {
            if (!File.Exists(lvlPath))
            {
                GD.PrintErr("check-lists: no such file: " + lvlPath);
                return 2;
            }
            var files = new ScoreFiles(lvlPath);
            GD.PrintRaw($"lists file={Path.GetFileName(lvlPath)} "
                        + $"levels={LevelFile.CountLevels(lvlPath)} "
                        + $"hs={LevelFile.CountHighScores(files.Hs)} "
                        + $"ghs={LevelFile.CountHighScores(files.Ghs)}\n");

            var levels = LevelFile.ReadLevelList(lvlPath);
            var mine = ReadAll(files.Hs);
            var posted = ReadAll(files.Ghs);
            foreach (ListMode mode in new[] { ListMode.Levels, ListMode.MyScores,
                                              ListMode.GlobalScores })
            {
                string[] rows = LevelList.BuildRows(mode, levels, mine, posted);
                for (int i = 0; i < rows.Length; i++)
                    GD.PrintRaw($"row {mode} {i + 1} {rows[i]}\n");
                byte[] joined = Encoding.Latin1.GetBytes(string.Join("\n", rows));
                GD.PrintRaw($"hash {mode} {rows.Length} "
                            + Convert.ToHexString(SHA256.HashData(joined)).ToLowerInvariant()
                            + "\n");
            }
            GD.PrintRaw("lists OK\n");
            return 0;
        }

        /// LevelList's own reader, reachable from here.  Duplicated rather than
        /// shared because LevelList's copy is a Godot-side private and this is
        /// the check; if they ever disagree the hash says so.
        private static THSREC[] ReadAll(string path)
        {
            if (!File.Exists(path)) return Array.Empty<THSREC>();
            int n = LevelFile.CountHighScores(path);
            var outv = new THSREC[n];
            for (int i = 0; i < n; i++) outv[i] = LevelFile.ReadHS(path, i + 1);
            return outv;
        }

        /// `-- --check-scores DIR`
        ///
        /// The .hs writer, driven through the cases that make it a file format
        /// rather than an array, and dumped as hex after each write:
        ///
        ///   1. a first write at level 3 -- two blank records get padded in
        ///      front of it, and this is where the original's half-initialised
        ///      padding shows: `HS.moves = 0` with `HS.shots` and `HS.name`
        ///      still holding the record it just read;
        ///   2. a write at level 1, in front of everything, which must not
        ///      truncate the file;
        ///   3. a better score at level 3, replacing in place;
        ///   4. a worse score at level 3, which Beats() refuses -- so the file
        ///      must not change at all;
        ///   5. a write far past the end (level 8), padding five more;
        ///   6. a name longer than the field, which the 6-byte record truncates
        ///      with a NUL terminator kept.
        ///
        /// Each step prints the whole file, so a divergence localises to a step
        /// and a byte rather than to "the file differs".
        public static int CheckScores(string dir)
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex)
            {
                GD.PrintErr("check-scores: " + ex.Message);
                return 2;
            }
            string hs = Path.Combine(dir, "check.hs");
            if (File.Exists(hs)) File.Delete(hs);
            string ghs = Path.Combine(dir, "check.ghs");
            if (File.Exists(ghs)) File.Delete(ghs);
            var files = new ScoreFiles(Path.Combine(dir, "check.lvl"));
            // One ScoreState for the whole sequence, because the original has
            // one `HS` global for the whole process -- and that is precisely
            // what the padding bytes below depend on.
            var st = new ScoreState();

            void Step(string label, int level, ushort moves, ushort shots, string name)
            {
                ScoreResult r = HighScores.Check(files, level, moves, shots, name, st);
                byte[] b = File.Exists(hs) ? File.ReadAllBytes(hs) : Array.Empty<byte>();
                GD.PrintRaw($"score {label} level={level} m={moves} s={shots} "
                            + $"name={name} beats={(r.Personal ? 1 : 0)} "
                            + $"len={b.Length} "
                            + Convert.ToHexString(b).ToLowerInvariant()
                            + (r.Error == null ? "" : " ERROR=" + r.Error) + "\n");
            }

            Step("first-at-3", 3, 12, 4, "MZ");
            Step("in-front", 1, 7, 2, "MZ");
            Step("better", 3, 11, 4, "MZ");
            Step("worse", 3, 99, 1, "MZ");
            Step("equal-fewer-shots", 3, 11, 3, "MZ");
            // The padding case that carries state: level 8 pads records 4..7,
            // and their shots and initials are the ones the *previous* step left
            // in HS -- 3 and "MZ", not 0 and "".  Zeroing them passes every
            // read-back test there is.
            Step("far-past-end", 8, 30, 9, "ABCD");
            Step("long-name", 2, 5, 1, "TOOLONG");
            Step("pad-after-abcd", 12, 6, 2, "ZZ");
            GD.PrintRaw("scores OK\n");
            return 0;
        }
    }
}
