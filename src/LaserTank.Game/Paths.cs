// Where the game content lives.
//
// The corpus in data/ is the repo's, not the project's: it is the regression
// corpus first and the game's content second, so it is not copied under res://.
// From a run inside the editor res:// is src/LaserTank.Game, so walk up until a
// directory has data/levels in it.  An exported build will ship its own copy
// and set LT_DATA instead.
using System;
using System.IO;
using Godot;

namespace LaserTank.Game
{
    public static class Paths
    {
        private static string _root;

        /// The repo root: the nearest ancestor of res:// that contains data/levels.
        public static string Root
        {
            get
            {
                if (_root != null) return _root;

                string env = System.Environment.GetEnvironmentVariable("LT_DATA");
                if (!string.IsNullOrEmpty(env) && Directory.Exists(Path.Combine(env, "levels")))
                    return _root = Path.GetDirectoryName(env.TrimEnd('/', '\\'));

                string d = ProjectSettings.GlobalizePath("res://");
                for (int up = 0; up < 8 && !string.IsNullOrEmpty(d); up++)
                {
                    if (Directory.Exists(Path.Combine(d, "data", "levels"))) return _root = d;
                    d = Path.GetDirectoryName(d.TrimEnd('/', '\\'));
                }
                throw new DirectoryNotFoundException(
                    "cannot find the repo's data/ directory above " +
                    ProjectSettings.GlobalizePath("res://") + " -- set LT_DATA");
            }
        }

        public static string Data(params string[] parts)
        {
            string p = Path.Combine(Root, "data");
            foreach (string s in parts) p = Path.Combine(p, s);
            return p;
        }

        /// The flagship collection: 2,030 levels, every one with a .ghs target.
        public static string Flagship => Data("levels", "LaserTank.lvl");

        /// The graphics packs that ship with the game, plus the internal pair
        /// the 2007 build carries as resources.  Index 0 is the default.
        ///
        /// This is the enumeration tools/atlas_check.py cross-checks -- the
        /// internal pair and every .ltg, by file name -- and the numbering
        /// `--pack N` has used since step 0.  The graphics *menu* has its own
        /// list (Packs.Scan), which also carries the external entry.
        public static string[] GraphicsPacks(string dir = null)
        {
            var list = new System.Collections.Generic.List<string> { "" };   // "" = internal
            list.AddRange(LtgFiles(dir ?? DefaultGraphicsDir));
            return list.ToArray();
        }

        /// Where the .ltg packs live until [SCREEN] Graphics_Dir says otherwise.
        /// The original's first value is the current directory (LTANK2.C:1785);
        /// in this repo the packs are content, so they are in data/.
        public static string DefaultGraphicsDir => Data("graphics");

        /// GetLTGFiles' `FindFirstFile("*.ltg")` (LTANK_D.C:1177), sorted by
        /// name so the menu order and `--pack N` are stable, and matched
        /// case-insensitively for the same reason the level loaders are: the
        /// content in this repo mixes `.ltg` and `.LTG` cases the way it mixes
        /// `.lvl` and `.LVL`, and Directory.GetFiles' pattern is only
        /// case-insensitive on Windows.
        public static string[] LtgFiles(string dir)
        {
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            var hits = new System.Collections.Generic.List<string>();
            foreach (string f in Directory.GetFiles(dir))
                if (Path.GetExtension(f).Equals(".ltg", StringComparison.OrdinalIgnoreCase))
                    hits.Add(f);
            hits.Sort(StringComparer.OrdinalIgnoreCase);
            return hits.ToArray();
        }

        /// One file in `dir` by name, case-insensitively -- GFXInit asks for
        /// "game.bmp" and "mask.bmp" in lower case (LT32L_US.H:18) and the
        /// packs people ship are not that careful.  -> null if it is not there.
        public static string FindFile(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;
            if (!Directory.Exists(dir)) return null;
            string direct = Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;
            foreach (string f in Directory.GetFiles(dir))
                if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
                    return f;
            return null;
        }

        /// LaserTank.ini -- **the import source, and nothing else since step
        /// 16.**  The original keeps it beside the .exe (LTANK.C:1411); the
        /// equivalent here is the repo root, which .gitignore's `/*.ini` already
        /// expects.  `$LT_INI` overrides it, and so does `--ini`.  Nothing in
        /// the port writes this file any more -- see Options.Open.
        public static string Ini
        {
            get
            {
                string env = System.Environment.GetEnvironmentVariable("LT_INI");
                return !string.IsNullOrEmpty(env)
                    ? env
                    : Path.Combine(Root, LaserTank.Game.Ini.FileName);
            }
        }

        /// The file name of the typed store, under `user://`.
        public const string SettingsFileName = "settings.json";

        /// **Where the player's settings live: `user://settings.json`.**
        ///
        /// This is the bug next-steps item 7 carried independently of the
        /// format.  The INI was written to `Root`, which is the repo in a dev
        /// checkout and the install directory in an exported build -- read-only
        /// in every `Program Files` install, and not a filesystem at all in a
        /// browser.  `user://` is the one place Godot guarantees is writable on
        /// every target it exports to, and it is per-user rather than
        /// per-install, which is what a settings file wants anyway.
        ///
        /// `$LT_SETTINGS` overrides it, and so does `--settings`; `--ini` moves
        /// it too, by the sibling rule in `SettingsBeside`.
        public static string Settings
        {
            get
            {
                string env = System.Environment.GetEnvironmentVariable("LT_SETTINGS");
                return !string.IsNullOrEmpty(env)
                    ? env
                    : ProjectSettings.GlobalizePath("user://" + SettingsFileName);
            }
        }

        /// The store that goes with an explicit `--ini FILE`: `FILE` with its
        /// extension swapped for `.settings.json`.
        ///
        /// **Named after the INI rather than fixed per directory** so that a
        /// gate which keeps a dozen probe INIs in one scratch directory gets a
        /// dozen independent stores -- tools/options_check.py does exactly that,
        /// and a shared `settings.json` would have let one case's write decide
        /// the next case's defaults.  An instrument that wants to say it
        /// outright passes `--settings`.
        public static string SettingsBeside(string iniPath) =>
            Path.ChangeExtension(iniPath, ".settings.json");

        /// original/src/Sounds/ -- the sixteen WAVs Ltank.rc compiles into the
        /// .exe as RCDATA (Phase 5, step 3).  Same arrangement as the internal
        /// sheet below: original/ is frozen, and this only ever reads it.
        public static string SoundsDir => Path.Combine(Root, "original", "src", "Sounds");

        /// original/src/Game.BMP + Mask.BMP -- the internal sheet.  original/ is
        /// read-only and frozen; this only ever reads it.
        public static string InternalGameBmp => Path.Combine(Root, "original", "src", "Game.BMP");
        public static string InternalMaskBmp => Path.Combine(Root, "original", "src", "Mask.BMP");
    }
}
