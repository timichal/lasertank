// Phase 5, step 16: the port's own settings, typed, at `user://settings.json`.
//
// Step 2 put the options where the original puts them -- `LaserTank.ini`, the
// original's own sections and key names -- and that was right for step 2, which
// was about reading the 2010 binary's file correctly.  Three separate jobs had
// collected in that one class by step 15 and only one of them was a settings
// mechanism:
//
//   1. a *fidelity artifact*: atoi, the case-sensitive `strcmp(temps, psYes)`,
//      "only a missing key gives the default".  Recorded findings about the
//      2010 binary, pinned by tools/options_check.py.  That is research output
//      and it survives untouched -- see `Ini` and `IniImport` in Options.cs;
//   2. *interop* with the 2010 binary's own file, which is what the
//      preserve-every-other-line write rule existed for;
//   3. the *port's own settings*, which had already drifted: `[DATA] Language`
//      is invented and says so, and the chrome has settings coming that will
//      never have an original key name at all.
//
// This file is job 3, and the split is the one `data/language/` already made:
// the artifact's reader stays complete and re-runnable while the runtime moves
// on.  `Ini` is a **one-way importer** now -- read once, on the first run, and
// never written again -- and job 2 retires with the write half.  The reasoning
// is in docs/game/history.md; the short of it is that nothing in this tree has
// ever been pointed at a live 2010 install, and the code that existed for the
// possibility was the most awkward in the class.
//
// **Where it lives is the bug this step also fixes.**  `Paths.Ini` wrote to the
// repo root, which is fine for a dev checkout and wrong the moment an exported
// build lands somewhere unwritable -- every `Program Files` install, and the
// web export next-steps item 4 is circling.  `user://` is Godot's answer and it
// is per-platform: `%APPDATA%\Godot\app_userdata\LaserTank` here, IndexedDB in
// a browser.
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaserTank.Game
{
    /// **The port's settings, as one typed record.**  One class, no parser, and
    /// room for the chrome settings the original never had.
    ///
    /// Every default here is the value a fresh install gets, and for the
    /// thirteen fields that came from the INI it is deliberately *the
    /// original's own default* -- `IniImport` then reads each key with the
    /// original's semantics and passes the field itself as the fallback, so
    /// "a missing key gives the default" is one value in one place rather than
    /// the same constant written twice and drifting.
    ///
    /// Names are the port's, not `LTANK.H`'s: the key names live in `Options`
    /// where the importer needs them, and a settings file nobody has to read
    /// `LTANK.H` to understand is the point of the move.
    public sealed class Settings
    {
        /// Bumped when a field changes meaning, not when one is added --
        /// System.Text.Json ignores a field it does not know and leaves a field
        /// the file does not carry at its default, so growing the record is
        /// already compatible both ways.  `Migrate` is where a real change
        /// would land.
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        // ---- what [SCREEN] used to hold ----
        /// 1, 2 or 3 -- SetGameSize's small / medium / large, not a pixel size.
        /// **The default really is 1**, the 24 px board (LTANK.C:1567).
        public int Size { get; set; } = 1;
        /// 0 internal, 1 external pair, 2 a .ltg (LTANK2.C:1780).
        public int GraphicsMode { get; set; }
        public string GraphicsFile { get; set; } = "";
        /// Filled in from Paths.DefaultGraphicsDir on the first run that finds
        /// it empty -- GFXInit's own "We only do this once" (LTANK2.C:1785).
        public string GraphicsDir { get; set; } = "";

        // ---- what [OPT] used to hold ----
        public bool Sound { get; set; } = true;
        /// The one setting here that changes what a tick does (Engine.Ani_On).
        public bool Animation { get; set; } = true;
        public bool AutoRecord { get; set; }
        public bool RememberLastLevel { get; set; } = true;
        /// Walk past levels this player has already beaten -- [OPT] SkipComLev.
        public bool SkipCompleted { get; set; }

        // ---- what [DATA] used to hold ----
        public string LastLevelFile { get; set; } = "";
        public int LastLevel { get; set; } = 1;
        /// The five-bit rank mask.  **All five by default, where the original
        /// defaults to a 0 that means "never asked"** -- see Options.Difficulty
        /// for why that sentinel is not reproduced.
        public int Difficulty { get; set; } = Options.AllRanks;
        /// One value where the original keeps two ([DATA] Player and [DATA]
        /// Record Author).  See Options.Name.
        public string Name { get; set; } = "";
        /// The ISO code the picker writes.  Empty means the base language, and
        /// so does anything Strings.Load does not recognise.
        public string Language { get; set; } = "";

        /// A settings file written by an older version of this port, brought up
        /// to `CurrentVersion`.  There is only one version so far, so this is
        /// the hook and not yet a migration -- but the hook is the whole reason
        /// the `version` field is in the file, and adding the field after the
        /// fact would have meant guessing what a file without one was.
        public void Migrate()
        {
            // if (Version < 2) { ... }
            Version = CurrentVersion;
        }
    }

    /// `Settings` on disk: read it, write it, and never take the game down over
    /// either.
    public sealed class SettingsStore
    {
        /// Pretty-printed on purpose.  This is a file a player may well open in
        /// an editor -- the INI it replaces was one -- and a diff of it should
        /// be one line per setting.
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        /// **UTF-8 with no byte-order mark**, which is not the default:
        /// `Encoding.UTF8` emits one, and a BOM in front of a `{` is a file
        /// half the JSON readers in the world refuse -- Python's `json.loads`
        /// among them, which is how this was caught.
        private static readonly System.Text.UTF8Encoding Utf8 =
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public string Path { get; }

        /// The same rule `Ini.ReadOnly` was, and for the same reasons: an
        /// instrument run -- `--shot`, `--play`, `--check-*`, a `--tick-rate`
        /// run -- reads the settings like the game does and then writes
        /// nothing, because eight parallel gate jobs must not race over one
        /// file and a screenshot must not change what the next player sees.
        public bool ReadOnly { get; set; }

        /// The last write's failure, or null.
        public string Error { get; private set; }

        public SettingsStore(string path, bool readOnly = false)
        {
            Path = path;
            ReadOnly = readOnly;
        }

        public bool Exists => File.Exists(Path);

        /// -> the stored record, or **null when there is nothing usable there**
        /// -- no file, an empty one, or JSON that will not parse.  A corrupt
        /// store is deliberately the same case as an absent one: the caller
        /// then imports and rewrites, which is the only recovery a player who
        /// has just hand-edited a brace out of the file would want.  `why` says
        /// which it was, for the log.
        public Settings Load(out string why)
        {
            why = null;
            try
            {
                if (!File.Exists(Path)) return null;
                // Read with the BOM-eating decoder even though nothing here
                // writes one: a file a player has round-tripped through an
                // editor may have grown one.
                string text = File.ReadAllText(Path, System.Text.Encoding.UTF8);
                if (text.Trim().Length == 0) { why = "the file is empty"; return null; }
                Settings s = JsonSerializer.Deserialize<Settings>(text, Json);
                if (s == null) { why = "the file holds a bare null"; return null; }
                s.Migrate();
                return s;
            }
            catch (Exception ex)
            {
                why = ex.Message;
                return null;
            }
        }

        public Settings Load() => Load(out _);

        /// -> false when nothing was written (ReadOnly, or the write failed).
        /// Failing to persist a setting must never take the game down, so this
        /// swallows the IO error and reports it -- exactly what `Ini.Save` did.
        ///
        /// **Written through a temporary and moved into place**, which the INI
        /// did not bother with and JSON has to: a half-written INI loses the
        /// lines after the cut, a half-written JSON object will not parse at
        /// all, and the settings a crash mid-write would cost are the player's.
        public bool Save(Settings s)
        {
            if (ReadOnly) return false;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = Path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json) + "\n", Utf8);
                // Move-with-overwrite is the atomic replace on every platform
                // this ships to; Delete-then-Move would leave a window with no
                // settings file at all.
                File.Move(tmp, Path, overwrite: true);
                Error = null;
                return true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return false;
            }
        }
    }
}
