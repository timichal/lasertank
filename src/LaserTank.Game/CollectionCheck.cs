// The headless self-check for command 108, "Open Data File".
//
// The rule this phase keeps re-learning, from PROGRESS.md: *an exit criterion
// that only says nothing changed is not one.*  Command 108 has two new outputs
// and they are different kinds of claim, so they are checked differently:
//
//   the list      what the picker offers is derived from the filesystem -- a
//                 walk of two directories, a file size, and a count of solved
//                 records in a .hs.  Nothing about it needs the engine, so it
//                 is a cross-implementation check like the sprite sheets and
//                 the three list dialogs: this dumps every row and
//                 tools/collections_check.py rebuilds them in Python from the
//                 same directories.
//
//   the switch    what the *game* does with the file that comes back, which is
//                 AssignHSFile and `CurLevel = 0; LoadNextLevel(TRUE,FALSE)`.
//                 That one is behaviour, and the part of it worth a gate is the
//                 part PROGRESS.md calls load-bearing: **the .hs and .ghs must
//                 follow the collection.**  A picker that changed the level
//                 file and not the score files would post this collection's
//                 scores into the last one's .hs, and a .hs is positional, so
//                 it would overwrite a real score rather than append a wrong
//                 one.  Nothing on screen would say so.  The level it lands
//                 on is checked alongside it, because this port's answer is no
//                 longer the C's constant 1 but the first level with no record
//                 in the player's .hs -- HighScores.FirstUnsolved -- which is a
//                 claim about a *file* and so is recomputed in Python too.
//
// So this opens **every collection in the list, in order, through one Session**
// -- which is also the strongest cheap claim available here: all 23 shipped
// collections load.  The Session is built with no Options, which is what keeps
// an instrument from writing a .hs or moving [DATA] RLLFilename; the same rule
// PlayMode's synthetic player is held to.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    public static class CollectionCheck
    {
        /// `-- --check-collections`
        public static int Run()
        {
            string root;
            try { root = Paths.Root; }
            catch (DirectoryNotFoundException ex)
            {
                GD.PrintErr("check-collections: " + ex.Message);
                return 2;
            }

            Collection[] all = CollectionList.Scan(root);
            string[] rows = CollectionList.BuildRows(all);
            GD.PrintRaw($"collections root={root.Replace('\\', '/')} n={all.Length}\n");
            for (int i = 0; i < all.Length; i++)
                GD.PrintRaw($"coll {i} path={Rel(root, all[i].Path)} row={rows[i]}\n");
            byte[] joined = Encoding.Latin1.GetBytes(string.Join("\n", rows));
            GD.PrintRaw($"hash {rows.Length} "
                        + Convert.ToHexString(SHA256.HashData(joined)).ToLowerInvariant()
                        + "\n");

            if (all.Length == 0)
            {
                GD.PrintErr("check-collections: nothing under "
                            + string.Join(", ", CollectionList.Roots));
                return 1;
            }

            // One Session for the whole walk, because the thing being checked
            // is a *change* of collection and a fresh Session per file would
            // check a first load instead.  No Options: an instrument writes no
            // .hs and moves no INI key.
            var s = new Session(all[0].Path);
            if (!s.Load(1))
            {
                GD.PrintErr("check-collections: " + s.Error);
                return 1;
            }

            int bad = 0;
            foreach (Collection c in all)
            {
                bool ok = s.OpenDataFile(c.Path);
                if (!ok) bad++;
                GD.PrintRaw(
                    $"open path={Rel(root, c.Path)} ok={(ok ? 1 : 0)} "
                    + $"level={s.Level} levels={s.LevelCount} "
                    + $"lname={s.Rec?.LName} "
                    + $"hs={Rel(root, s.Files.Hs)} ghs={Rel(root, s.Files.Ghs)} "
                    + $"pb={Rel(root, s.Files.PbName(s.Level))}\n");
            }

            // The playback rule.  `Load` keeps an open playback when the level
            // number matches, which was a sufficient identity until a Session
            // could change collection: a recording of level 1 followed by a
            // change of data file would otherwise leave that keystream playing
            // over a level 1 it has nothing to do with.  Checked here because
            // it is one line in `OpenDataFile` guarding a case a player reaches
            // in two keystrokes -- F7 then O -- and nothing else in this tree
            // opens a playback and then loads a level a different way.
            string lpb = Path.Combine(root, "data", "demos", "LaserTank", "00001.lpb");
            string flagship = Path.Combine(root, "data", "levels", "LaserTank.lvl");
            if (File.Exists(lpb) && File.Exists(flagship) && s.OpenDataFile(flagship))
            {
                string why = s.LoadPlayback(lpb);
                bool up = why == null && s.Pb.Open;
                s.OpenDataFile(all[0].Path);
                bool closed = !s.Pb.Open && !s.E.PBOpen;
                GD.PrintRaw($"playback loaded={(up ? 1 : 0)} "
                            + $"closed-by-108={(closed ? 1 : 0)}"
                            + (why == null ? "" : " why=" + why) + "\n");
                if (!up || !closed) bad++;
            }
            else
            {
                GD.PrintRaw("playback skipped=1\n");
            }

            // The restore.  `OpenDataFile` -> false must leave the collection,
            // its two score files and the board exactly where they were -- the
            // half of the C's behaviour this port substitutes for (the original
            // has no restore for 108; LoadNextLevel's failure path re-posts the
            // dialog instead, which is not a thing a keystroke-driven port can
            // do).  Left unchecked it is silent: the board would still show the
            // old level while Files pointed at a .hs that does not exist.
            string keep = s.LevelPath, keepHs = s.Files.Hs;
            int keepLevels = s.LevelCount, keepLevel = s.Level;
            string gone = Path.Combine(root, "data", "levels", "no-such-collection.lvl");
            bool opened = s.OpenDataFile(gone);
            bool restored = !opened && s.LevelPath == keep && s.Files.Hs == keepHs
                            && s.LevelCount == keepLevels && s.Level == keepLevel;
            GD.PrintRaw($"missing ok={(opened ? 1 : 0)} restored={(restored ? 1 : 0)} "
                        + $"kept={Rel(root, s.LevelPath)} level={s.Level} "
                        + $"levels={s.LevelCount}\n");
            if (!restored) bad++;

            GD.PrintRaw(bad == 0 ? "collections OK\n" : $"collections FAILED ({bad})\n");
            return bad == 0 ? 0 : 1;
        }

        /// A path relative to the repo root with forward slashes, so a dumped
        /// line is the same string on Windows and on Linux.
        private static string Rel(string root, string path)
        {
            if (string.IsNullOrEmpty(path)) return "-";
            try { return Path.GetRelativePath(root, path).Replace('\\', '/'); }
            catch (ArgumentException) { return path.Replace('\\', '/'); }
        }
    }
}
