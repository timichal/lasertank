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
        public static string[] GraphicsPacks()
        {
            var list = new System.Collections.Generic.List<string> { "" };   // "" = internal
            string dir = Data("graphics");
            if (Directory.Exists(dir))
            {
                string[] packs = Directory.GetFiles(dir, "*.ltg");
                Array.Sort(packs, StringComparer.OrdinalIgnoreCase);
                list.AddRange(packs);
            }
            return list.ToArray();
        }

        /// original/src/Game.BMP + Mask.BMP -- the internal sheet.  original/ is
        /// read-only and frozen; this only ever reads it.
        public static string InternalGameBmp => Path.Combine(Root, "original", "src", "Game.BMP");
        public static string InternalMaskBmp => Path.Combine(Root, "original", "src", "Mask.BMP");
    }
}
