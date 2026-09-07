// Phase 5, step 4: the score files.
//
// Two files sit beside every .lvl, both arrays of the same 10-byte record
// indexed by level - 1 (THSREC, LTANK.H:188):
//
//   FOO.ghs   ships with the collection.  The best anyone has posted, with
//             their initials -- a target, never written by the game.
//   FOO.hs    the player's own.  Written by CheckHighScore when a level is
//             beaten, read by the level picker to know what is done, and by
//             LoadLevel's SkipCL branch to skip what is done.
//
// Both names are derived from the .lvl by AssignHSFile (LTANK2.C:1055), which
// also builds the default recording name -- three things from one file name, so
// they are one function here too.
//
// **The interesting part is not the comparison, it is the file.**  A .hs is
// dense and positional: beating level 500 of a 2,030-level collection first
// writes 499 blank records in front of it.  LevelFile.WriteHighScore carries
// that, including which bytes the original leaves in the padding; this file
// decides *when* to call it, which is the flag case's `CheckHighScore()`
// (LTANK.C:654) and nothing else.
using System;
using System.IO;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// The three names AssignHSFile derives from the level file (LTANK2.C:1055).
    ///
    /// `strcpy(strrchr(GHFileName,'.'),".ghs")` replaces the text from the
    /// **last** dot, so a collection called `Gary-I.lvl` gives `Gary-I.ghs` --
    /// and a file with no dot at all would have crashed the original on a NULL
    /// return.  Path.ChangeExtension is the same rule for every name that has an
    /// extension, which every .lvl in this corpus does; a name without one keeps
    /// its whole self and gains the suffix, which is the sane reading of a case
    /// the original could not survive.
    public sealed class ScoreFiles
    {
        public string Lvl { get; }
        public string Hs { get; }
        public string Ghs { get; }
        /// `strcat(PBFileName,"_0000.lpb")` -- the stem the Save Recording
        /// dialog opens with.  The four digits are a level-number slot
        /// BuildPB_Name fills in (LTANK.C:373), so this is the prefix only.
        public string PbStem { get; }

        public ScoreFiles(string lvlPath)
        {
            Lvl = lvlPath;
            string stem = Path.Combine(
                Path.GetDirectoryName(lvlPath) ?? "",
                Path.GetFileNameWithoutExtension(lvlPath));
            Hs = stem + ".hs";
            Ghs = stem + ".ghs";
            PbStem = stem;
        }

        /// BuildPB_Name (LTANK.C:373): the stem, an underscore, the level in
        /// four digits.  The original's recordings are `LaserTank_0001.lpb` and
        /// the corpus in data/demos is the same shape.
        public string PbName(int level) => $"{PbStem}_{level:D4}.lpb";
    }

    /// What CheckHighScore decided, so the caller can both persist it and say
    /// something about it.  The original's answer is a dialog (HSBox); this is
    /// the same information as a value.
    public sealed class ScoreResult
    {
        /// The player's previous best, or null when there was none.  A record
        /// that exists with Moves == 0 is the blank marker and reads as null
        /// here, because "never solved" is what both mean.
        public THSREC Old;
        /// The .ghs target, or null when the collection ships none for this
        /// level.
        public THSREC Target;
        /// Did this run beat the player's own best?  (Then it was written.)
        public bool Personal;
        /// Did this run beat the collection's posted best?  This is the one the
        /// original prints "Congratulation's You beat it !!" for (txt012).
        public bool Global;
        /// Set when the write failed -- a read-only corpus, say.  Never fatal:
        /// losing a high score must not take the game down.
        public string Error;
    }

    /// `THSREC HS` (LTANK.H:239) -- one record, global, zeroed at program start
    /// and then reused by every CheckHighScore and every HSBox.
    ///
    /// It looks like a scratch variable and it is not: CheckHighScore's padding
    /// loop writes it to disk *before* refreshing it from the file, so the bytes
    /// that pad out a sparse .hs are the previous level's shots and initials.
    /// A per-call local would zero them, produce a file the 2010 binary never
    /// would, and be invisible in every test that reads the file back -- because
    /// reading only ever looks at `moves`.  So the global is a global here too,
    /// and Session owns one for the lifetime of a run.
    public sealed class ScoreState
    {
        public THSREC HS = new THSREC();
    }

    public static class HighScores
    {
        /// CheckHighScore (LTANK2.C:1071) and the part of HSBox that fills in
        /// the record (LTANK_D.C:826), in the original's order -- which matters,
        /// because the padding happens before the read and the read happens
        /// before the comparison.
        ///
        /// The comparison is the whole scoring rule of LaserTank: fewer moves
        /// wins, and moves being equal, fewer shots.  Note what is *not* in it
        /// -- no tick count, no time.  A level is a puzzle with a par, and the
        /// par is two integers.
        ///
        /// One structural difference from the original, and it is in the
        /// original's favour: the C reads the .ghs inside the *dialog*
        /// (LTANK_D.C:635), so the "did I beat the world" question is only asked
        /// when the personal best has already fallen.  Here it is asked either
        /// way, because there is no dialog and the answer is worth showing.  It
        /// is a read of a file the game never writes, so nothing can come of it.
        public static ScoreResult Check(ScoreFiles files, int level,
                                        ushort moves, ushort shots, string player,
                                        ScoreState st)
        {
            var r = new ScoreResult();
            THSREC target = LevelFile.ReadHS(files.Ghs, level);
            r.Target = target != null && target.Moves > 0 ? target : null;
            r.Global = LevelFile.Beats(moves, shots, target);

            try
            {
                // `HS.moves = 0` and the padding loop, in that order and before
                // anything is read.  See LevelFile.PadHighScore.
                st.HS.Moves = 0;
                LevelFile.PadHighScore(files.Hs, level, st.HS);

                // `ReadFile(F2, &HS, ...)` with **BytesMoved unchecked**: a read
                // past the end leaves HS exactly as the padding left it, which
                // is moves == 0, which is why a brand-new level always counts as
                // beaten.
                THSREC got = LevelFile.ReadHS(files.Hs, level);
                if (got != null) st.HS = got;

                r.Old = st.HS.Moves > 0
                    ? new THSREC { Moves = st.HS.Moves, Shots = st.HS.Shots,
                                   Name = st.HS.Name }
                    : null;
                r.Personal = LevelFile.Beats(moves, shots, st.HS);
                if (!r.Personal) return r;

                // HSBox's OK button: the two scores always, the initials only
                // when they changed -- `if (stricmp(temps, HS.name) != 0)`, a
                // case-insensitive compare.
                st.HS.Moves = moves;
                st.HS.Shots = shots;
                if (!string.Equals(player ?? "", st.HS.Name,
                                   StringComparison.OrdinalIgnoreCase))
                    st.HS.Name = player ?? "";

                LevelFile.WriteHighScore(files.Hs, level, st.HS);
            }
            catch (Exception ex)
            {
                // FileError() takes the original down; losing a high score must
                // not take this down.  A read-only corpus directory is the
                // ordinary way to get here.
                r.Personal = false;
                r.Error = ex.Message;
            }
            return r;
        }

        /// One line of the "old score" text HSBox builds from txt008/010/011
        /// ("Old High Score > M:", "M: ", " S: ", " I: ") -- the three labels
        /// with the record's three fields.  Kept because those strings are the
        /// original's and step 6 will read them out of language.dat.
        public static string Describe(THSREC r) =>
            r == null ? "-" : $"M: {r.Moves} S: {r.Shots} I: {r.Name}";
    }
}
