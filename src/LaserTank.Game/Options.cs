// Phase 5, step 2: LaserTank.ini -- the options the original persists, in the
// file it persists them in and under its own key names.
//
// **Step 16 demoted this file to an importer.**  The original has no options
// object.  It calls GetPrivateProfileInt / WritePrivateProfileString at the
// point of use against LaserTank.ini next to the .exe (LTANK.C:1411, LTANK.H:85
// `INIFileName`), and step 2's business was three of those call sites:
//
//   [SCREEN] Size                       SetGameSize, LTANK2.C:1737 / LTANK.C:1567
//   [SCREEN] Graphics_Mode              GFXInit's three branches, LTANK2.C:1780
//            Graphics_File                and GraphBox's Close, LTANK_D.C:1247
//            Graphics_Dir
//   [DATA]   RLLFilename / RLLLevel     the level LoadLevel remembers,
//                                         LTANK2.C:1035, read back by command
//                                         101 (New Game), LTANK.C:866
//
// `Ini` is a stand-in for the Win32 profile calls and not a general INI
// library: first match wins, sections and keys compare case-insensitively, and
// integers follow atoi.  **What is gone since step 16 is the write half** --
// WritePrivateProfileString and the rule that a write preserves every other
// line in the file.  That rule existed for one job, sharing a file with a live
// 2010 install, and nothing in this tree has ever been pointed at one; the
// port's settings are a typed record at `user://settings.json` now (see
// Settings.cs), and this class reads a `LaserTank.ini` **once**, on the first
// run, and never writes it again.
//
// Everything that made it a *finding* rather than plumbing survives here
// untouched, because it is research output about the 2010 binary and it is
// still pinned by tools/options_check.py: atoi's rules, the case-sensitive
// `strcmp(temps, psYes)` test, and "only a missing key gives the default".
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// GetPrivateProfileInt / GetPrivateProfileString, over a file this process
    /// holds in memory as lines.  **Read-only since step 16** -- see the header.
    public sealed class Ini
    {
        /// LTANK.H:85.  The original puts it beside the .exe; Paths.Ini decides
        /// where that is here.
        public const string FileName = "LaserTank.ini";

        private readonly List<string> _lines = new List<string>();

        /// The file is ANSI, and level and pack names in it carry latin-1 bytes.
        private static readonly Encoding Latin1 = Encoding.Latin1;

        public string Path { get; }

        /// True when there was a file there to read.  An absent one is not an
        /// error -- it is the ordinary case for every install that never had a
        /// 2010 LaserTank on it -- so it reads as a file with no keys in it and
        /// every default stands.
        public bool Exists { get; }

        public Ini(string path)
        {
            Path = path;
            if (!File.Exists(path)) return;
            Exists = true;
            // Read as bytes: text mode would translate the file's CRLFs, and
            // this reader is the only thing in the port that still looks at
            // them.
            foreach (string line in Latin1.GetString(File.ReadAllBytes(path))
                                          .Split('\n'))
                _lines.Add(line.TrimEnd('\r'));
        }

        private static bool IsSection(string line, out string name)
        {
            name = null;
            string s = line.Trim();
            if (s.Length < 2 || s[0] != '[' || s[s.Length - 1] != ']') return false;
            name = s.Substring(1, s.Length - 2).Trim();
            return true;
        }

        private static bool Eq(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// The line index of `section`'s header, or -1.
        private int FindSection(string section)
        {
            for (int i = 0; i < _lines.Count; i++)
                if (IsSection(_lines[i], out string n) && Eq(n, section)) return i;
            return -1;
        }

        /// The line index of `key` inside the section starting at `at`, or -1.
        private int FindKey(int at, string key)
        {
            for (int i = at + 1; i < _lines.Count; i++)
            {
                if (IsSection(_lines[i], out _)) return -1;
                int eq = _lines[i].IndexOf('=');
                if (eq > 0 && Eq(_lines[i].Substring(0, eq).Trim(), key)) return i;
            }
            return -1;
        }

        /// GetPrivateProfileString.  Missing section or key -> `dflt`; a present
        /// but empty value -> "".  Surrounding double quotes are stripped, which
        /// is what the Win32 call does.
        public string Get(string section, string key, string dflt = "")
        {
            int s = FindSection(section);
            if (s < 0) return dflt;
            int k = FindKey(s, key);
            if (k < 0) return dflt;
            string v = _lines[k].Substring(_lines[k].IndexOf('=') + 1).Trim();
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
                v = v.Substring(1, v.Length - 2);
            return v;
        }

        /// GetPrivateProfileInt, atoi rules and all: a key that is present but
        /// not a number reads as 0, and only a *missing* key gives `dflt`.
        public int GetInt(string section, string key, int dflt)
        {
            string v = Get(section, key, null);
            return v == null ? dflt : Atoi(v);
        }

        /// atoi: optional space, optional sign, digits, stop at the first thing
        /// that is not one.  No overflow check, because atoi has none either and
        /// every value this port reads is a small integer.
        public static int Atoi(string s)
        {
            int i = 0, sign = 1, v = 0;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) sign = s[i++] == '-' ? -1 : 1;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') v = v * 10 + (s[i++] - '0');
            return sign * v;
        }
    }

    /// **The one-way importer**: a `LaserTank.ini` folded into a `Settings`,
    /// once, on the first run.
    ///
    /// This is the whole of what step 2 learned about the 2010 binary's file,
    /// and it is deliberately the only place that knows the original's key
    /// names.  Each read passes *the record's own field* as the default, so the
    /// "a missing key gives the original's default" rule is expressed once and
    /// the two halves cannot drift: Settings holds the value, this holds the
    /// semantics that decide whether the file overrides it.
    public static class IniImport
    {
        // LTANK.H:113-128, verbatim.
        public const string SecScreen = "SCREEN", SecOpt = "OPT", SecData = "DATA";
        public const string PsSize = "Size";                    // 1 = small, 3 = large
        public const string PsGM = "Graphics_Mode";             // 0 int, 1 ext, 2 ltg
        public const string PsGFN = "Graphics_File";
        public const string PsGDN = "Graphics_Dir";
        public const string PsSound = "Sound";                  // Yes / No
        public const string PsAni = "Animation";                // Yes / No
        public const string PsARec = "Auto_Record";             // Yes / No
        public const string PsRllOn = "RLL";
        public const string PsRllN = "RLLFilename";
        public const string PsRllL = "RLLLevel";
        public const string PsUser = "Player";                  // the initials
        public const string PsPBA = "Record Author";            // the same person
        public const string PsSCL = "SkipComLev";               // Yes / No
        public const string PsDiff = "Diff_Setting";            // the five-bit mask
        public const string PsYes = "Yes";

        /// `[DATA] Language` -- the one key here the original does not have, and
        /// the only invented behaviour in step 6.
        ///
        /// The 2007 program has no language setting at all: `LANGFile` is a
        /// fixed `Language\Language.dat` beside the INI (LTANK.C:1421), and you
        /// changed language by installing a different one of the ten `Setups/`
        /// trees over your copy.  That is not a design decision worth
        /// reproducing -- it is what shipping ten installers in 2007 forced --
        /// and this port has all ten files at once, so it stores the choice like
        /// every other option and shows a picker.  It was named for the section
        /// the original's other file-and-content keys live in, and **step 16 is
        /// what it was always pointing at**: a setting with no original key name
        /// belongs in the port's own store, and it has one now.
        public const string PsLang = "Language";

        /// The original's Yes/No test, which is a `strcmp` against `psYes` and
        /// therefore **case-sensitive and exact**: a hand-edited `Sound=yes`
        /// really does mute the 2010 binary.  The only thing that varies between
        /// the six keys that use it is the default, and hence which side of the
        /// comparison the missing case falls on -- so both senses are one
        /// function with a `dflt`.
        ///
        /// Two layers compose and it is worth keeping them apart: the
        /// *comparison* is strict, and the *reader* trims.
        /// GetPrivateProfileString strips surrounding whitespace and quotes
        /// before anything sees the value, so `RLL=Yes ` is "Yes" and stays on
        /// -- while `Yes!` and any change of case reach the strcmp intact.
        private static bool YesNo(Ini ini, string key, bool dflt) =>
            ini.Get(SecOpt, key, dflt ? PsYes : "No") == PsYes;

        /// Fold `ini` into `s`.  Every field it does not find a key for is left
        /// exactly as it was.
        public static void Apply(Ini ini, Settings s)
        {
            // LTANK.C:1567 -- the default really is 1, the 24 px board, not the
            // 32 px one this port used before it read the file.
            s.Size = Math.Clamp(ini.GetInt(SecScreen, PsSize, s.Size), 1, 3);
            int mode = ini.GetInt(SecScreen, PsGM, s.GraphicsMode);
            s.GraphicsMode = mode >= 0 && mode <= 2 ? mode : 0;
            // GFXInit reads Graphics_File only in mode 2 (LTANK2.C:1781); read
            // it always, so switching to mode 2 in the menu and back does not
            // lose the pack the file names.
            s.GraphicsFile = ini.Get(SecScreen, PsGFN, s.GraphicsFile);
            s.GraphicsDir = ini.Get(SecScreen, PsGDN, s.GraphicsDir);

            // LTANK.C:411 -- Sound defaults to Yes, and the original's test is
            // `if (strcmp(temps, psYes)) Sound_On = FALSE;`, i.e. **exactly
            // "Yes" or the sound is off**.  Case-sensitive, so a hand-edited
            // `sound=yes` really does mute the 2010 binary.  Kept as it is: it
            // is one line, it is observable, and there is no argument for
            // leniency beyond taste.
            s.Sound = YesNo(ini, PsSound, s.Sound);

            // LTANK.C:404 -- Animation defaults to Yes, same strict test.  It
            // is the one option in this list that moves a *trace*: Ani_On gates
            // AniCount, and AniLevel and AniCount are both trace fields.  Turning
            // it off is therefore a legitimate way to make the port disagree
            // with the oracle -- the 2010 binary disagrees with itself the same
            // way -- which is why every headless gate leaves it alone: PlayMode
            // builds its Session with no Options at all, and Engine.Ani_On
            // defaults to true.
            s.Animation = YesNo(ini, PsAni, s.Animation);

            // LTANK.C:439 -- Remember Last Level defaults to Yes and takes the
            // same strict test as Sound and Animation:
            // `if (strcmp(temps,psYes)) RLL = FALSE;`.  Step 2 read this key
            // loosely (anything but "No") on the grounds that the idiom was
            // ambiguous; LTANK.C:439 is not ambiguous, so the loose reading is
            // gone and tools/options_check.py pins the strict one.
            s.RememberLastLevel = YesNo(ini, PsRllOn, s.RememberLastLevel);
            s.LastLevelFile = ini.Get(SecData, PsRllN, s.LastLevelFile);
            s.LastLevel = ini.GetInt(SecData, PsRllL, s.LastLevel);

            // LTANK.C:424 -- and here the default flips: Auto_Record, like
            // SkipComLev and DisableWarnings, defaults to **No** and is tested
            // `strcmp(temps,psYes) == 0`.  Same one-sided comparison, opposite
            // sense, so a missing key means off rather than on.
            s.AutoRecord = YesNo(ini, PsARec, s.AutoRecord);

            // LTANK.C:418 -- Skip Completed Levels, `SkipCL`, command 116.  The
            // same one-sided test as Auto_Record and the same default: a
            // missing key means off.
            s.SkipCompleted = YesNo(ini, PsSCL, s.SkipCompleted);

            // LTANK.C:439 -- the five-bit difficulty mask, and **the one place
            // in this import where the default is deliberately not the
            // original's.**
            //
            // `GetPrivateProfileInt("DATA", psDiff, 0, ...)` defaults to *zero*
            // there, and zero is not a mask -- it is a sentinel meaning "never
            // asked", which LoadNextLevel answers by posting command 225 and
            // putting the Difficulty dialog in your way before the first level
            // loads (LTANK2.C:990).  That is a 1996 first-run wizard, and the
            // shipped LaserTank.ini answers it with 31 before anybody sees it.
            // This port has no such dialog -- 225 is the options panel's five
            // chips, opened when the player wants them -- so there is nothing
            // to post, and a game that filtered *every* level out on a fresh
            // install would simply look broken.  **An absent key is all five
            // ranks, and so is an explicit 0**, because a file the 2010 binary
            // left at zero is one whose owner never answered the question
            // either.
            //
            // Masked to the five bits that exist.  128 is `SDiff`'s own
            // "completed" sentinel (LTANK2.C:1016) and nothing else may collide
            // with it: a hand-edited `Diff_Setting=255` that kept its high bits
            // would match every skipped level and quietly undo SkipComLev.
            s.Difficulty = ini.GetInt(SecData, PsDiff, s.Difficulty) & Options.AllRanks;
            if (s.Difficulty == 0) s.Difficulty = Options.AllRanks;

            // **[DATA] Player and [DATA] Record Author are one value here.**
            // See Options.Name for the whole argument; the read is the half
            // this class still has a job in: the long key wins when it has
            // anything in it, because it is the one that can hold a name rather
            // than an abbreviation of one, and `Player` is the fallback for a
            // file written by the 2010 binary, which asks for the initials
            // first (HSBox) and for the author only on the first recording
            // anyone saves.
            //
            // Neither key has a default: the original opens both dialogs with
            // an empty box (LTANK_D.C:634, :983) and writes whatever comes
            // back, so an empty name is legal and stays empty.
            string author = ini.Get(SecData, PsPBA);
            string name = author.Length > 0 ? author : ini.Get(SecData, PsUser, s.Name);
            s.Name = name.Length > Options.NameMax
                     ? name.Substring(0, Options.NameMax) : name;

            // [DATA] Language.  An absent key means the base language, and
            // Strings.Load resolves an unknown code to it too, so a
            // hand-edited `Language=Klingon` degrades to English rather than to
            // `[quit.title]` on every label.
            s.Language = ini.Get(SecData, PsLang, s.Language);
        }
    }

    /// The settings the game reads and the panels write.  **Step 16 changed
    /// what is under it and nothing above it**: the properties and setters are
    /// the ones every caller already had, and what moved is that a setter now
    /// persists a typed record to `user://settings.json` instead of a key to an
    /// INI.  Each setter still persists immediately, which is what every call
    /// site in the original does.
    public sealed class Options
    {
        private readonly Settings _s;
        private readonly SettingsStore _store;

        /// Where the settings live.
        public string StorePath => _store.Path;

        /// The `LaserTank.ini` this run imported from, or null -- for
        /// `--check-options` to print and for nothing else.  **It is a path, not
        /// a handle**: after Open returns, nothing here can write it.
        public string ImportedFrom { get; private set; }

        /// The store's read-only flag, which is what `--save-options` clears.
        /// See SettingsStore.ReadOnly for the rule.
        public bool ReadOnly
        {
            get => _store.ReadOnly;
            set => _store.ReadOnly = value;
        }

        private Options(Settings s, SettingsStore store)
        {
            _s = s;
            _store = store;
        }

        /// **The first-run rule, in one place.**  Load the store; if there is
        /// nothing usable there, start from the defaults, fold in a
        /// `LaserTank.ini` if `iniPath` names one that exists, and write the
        /// store out so the next run reads it instead.  A store that is already
        /// there wins outright -- **the INI is not consulted again**, which is
        /// what "one-way importer" means and is why editing `LaserTank.ini`
        /// after the first run does nothing.
        ///
        /// A read-only run (an instrument) does the same reading and skips the
        /// write, so it sees exactly what a player would and leaves the tree as
        /// it was.
        public static Options Open(string storePath, string iniPath, bool readOnly)
        {
            var store = new SettingsStore(storePath, readOnly);
            Settings s = store.Load(out string why);
            string imported = null;
            bool fresh = s == null;
            if (fresh)
            {
                // A store that would not parse is the same case as one that is
                // not there: import and rewrite.  It is said out loud because
                // the alternative -- refusing to start -- costs the player
                // every setting over one stray brace.
                if (why != null)
                    Godot.GD.PrintErr($"settings: {storePath} unreadable ({why}); "
                                      + "starting from the defaults");
                s = new Settings();
                if (iniPath != null)
                {
                    var ini = new Ini(iniPath);
                    if (ini.Exists) { IniImport.Apply(ini, s); imported = iniPath; }
                }
            }

            // "We only do this once" (LTANK2.C:1785): an empty Graphics_Dir is
            // filled in.  The original uses the current directory; the packs
            // that ship with this repo are in data/graphics, so that is the
            // sensible first value here.
            if (s.GraphicsDir.Length == 0) s.GraphicsDir = Paths.DefaultGraphicsDir;
            if (s.Language.Length == 0) s.Language = Core.Strings.BaseCode;

            var opt = new Options(s, store) { ImportedFrom = imported };
            // The first run writes the store, so the second one has something
            // to read and the INI's job is over after exactly one launch.
            if (fresh) store.Save(s);
            return opt;
        }

        private void Save() => _store.Save(_s);

        /// 1, 2 or 3 -- the original's small / medium / large, not a pixel size.
        public int Size => _s.Size;
        public int GraphicsMode => _s.GraphicsMode;
        public string GraphicsFile => _s.GraphicsFile;
        public string GraphicsDir => _s.GraphicsDir;
        /// lt_sfx.c:19 via LTANK.C:411.  Muting is the *player's* state, never
        /// the engine's: SoundPlay's ids are recorded whatever this says, so
        /// turning the sound off cannot move a trace.
        public bool SoundOn => _s.Sound;
        /// Engine.Ani_On, command 104.  The one option here that changes what a
        /// tick does.
        public bool AnimationOn => _s.Animation;
        public bool RememberLastLevel => _s.RememberLastLevel;
        public string LastLevelFile => _s.LastLevelFile;
        public int LastLevel => _s.LastLevel;
        /// Command 115.
        public bool AutoRecord => _s.AutoRecord;

        /// **`SkipCL`, command 116** -- walk past levels this player has
        /// already beaten when advancing.
        ///
        /// The test is `TempHSData.moves > 0` against the collection's own
        /// `.hs` (LTANK2.C:961, :1010), which is the only definition of
        /// *solved* anywhere in the C and the one HighScores.FirstUnsolved
        /// already borrows.  What it governs here is Session.Advance, not the
        /// level list -- the list's own "only unsolved" chip is
        /// `SearchRec.SkipComp`, a different control over a different thing,
        /// and the two are separate in the original too.
        public bool SkipCompleted => _s.SkipCompleted;

        /// **The `Difficulty` global** -- which ranks the game advances *to*:
        /// 1 Kids, 2 Easy, 4 Medium, 8 Hard, 16 Deadly (LTANK.C:1268's five
        /// `EditDiffSet` calls).
        ///
        /// **Not the level list's five chips**, which are `SearchRec.Diff` and
        /// filter the table in front of you.  Same five bits, two jobs: one
        /// says which rows you are reading, this one says where `S`, `P` and a
        /// win take you.  They were very nearly made one value -- and were not,
        /// because filtering a table to look something up is not a statement
        /// about how you want to play, and a browse that silently changed the
        /// game is the kind of thing nobody would connect to the chip they
        /// clicked a minute earlier.
        ///
        /// A level's own `SDiff` of 0 is unfilterable whatever this holds --
        /// see Session.Advance, where the original's `CurRecData.SDiff > 0`
        /// guard is.
        public int Difficulty => _s.Difficulty;

        /// All five ranks: the mask the Difficulty dialog writes with every box
        /// ticked, and what `original/bin/LaserTank.ini` ships with.
        public const int AllRanks = 1 | 2 | 4 | 8 | 16;

        /// **Who is playing -- one value, where the original has two.**
        ///
        /// The 2007 program asks twice.  `HSBox` (LTANK_D.C:634) wants
        /// *initials* for the score it is about to post and keeps them in
        /// `[DATA] Player`; `RecordBox` (LTANK_D.C:983) wants an *author* for
        /// the recording it is about to write and keeps it in `[DATA] Record
        /// Author`.  Two dialogs, two keys, two moments -- and one answer,
        /// because they are the same question asked of the same person.  There
        /// is no reading of either box under which a player would want them to
        /// disagree, and a 1996 dialog per field is what made them two in the
        /// first place.
        ///
        /// **Step 16 retired the write half of that merge.**  Step 14 wrote the
        /// one name into both of the original's keys, so a `LaserTank.ini` this
        /// port had written was one the 2010 binary opened with both dialogs
        /// already answered; the INI is read-only now, so what survives is the
        /// *read* -- `IniImport` still takes either key, long one first.
        /// `Initials` is the four characters a `.hs` record can hold, cut from
        /// the same string rather than typed a second time, and that is the half
        /// that was ever load-bearing: it is what reaches a file the community
        /// shares.
        ///
        /// Thirty is `RecordBox`'s own width and the wider of the two, so it is
        /// the one the field takes -- see NameMax.
        public string Name => _s.Name;

        /// `GetWindowText(..., PBSRec.Author, 31)` (LTANK_D.C:990) into a
        /// `char Author[31]` (LTANK.H:148): thirty characters and a
        /// terminator, which is also exactly what `LevelFile.WritePlayback`
        /// puts in a `.lpb` header.
        ///
        /// **The original loses the thirtieth on the way back**, and it is a
        /// finding rather than something to reproduce: RecordBox *reads* the
        /// INI with `GetPrivateProfileString(..., 30, ...)` (LTANK_D.C:983),
        /// which is 29 characters and a terminator -- so a 30-character name
        /// the 2010 binary itself wrote reopens its own dialog one character
        /// short.  Nothing here reads the key that way, so nothing here has to
        /// be short by one.
        public const int NameMax = 30;

        /// **Derived, and the reason the merge costs nothing.**  `HSBox` reads
        /// `[DATA] Player` with `GetPrivateProfileString(..., 5, ...)` and the
        /// score record's field is `char[6]` with four of them reachable
        /// (THSREC.NameEntry), so a name longer than four characters posts its
        /// first four -- which is what initials are, and what `%4s` in both list
        /// dialogs prints.
        public string Initials => Name.Length > THSREC.NameEntry - 1
                                  ? Name.Substring(0, THSREC.NameEntry - 1)
                                  : Name;

        /// SetGameSize's own write (LTANK2.C:1737).
        public void SetSize(int size)
        {
            _s.Size = Math.Clamp(size, 1, 3);
            Save();
        }

        /// GraphBox's Close (LTANK_D.C:1247): the mode always, the file name only
        /// in mode 2.  Cancel writes the same thing -- there is no way out of
        /// that dialog that does not persist the choice, because the choice was
        /// already applied live.
        public void SetGraphics(int mode, string file)
        {
            _s.GraphicsMode = mode;
            if (mode == 2) _s.GraphicsFile = file ?? "";
            Save();
        }

        public void SetGraphicsDir(string dir)
        {
            _s.GraphicsDir = dir;
            Save();
        }

        /// ToggleOpt (LTANK.C:1489) for command 102, "Toggle Sound": flip it,
        /// persist it on the spot.  The same function does Animation, Skip
        /// Completed Levels, AutoRecord and Disable Warnings, so this is the
        /// shape the rest of that menu takes.
        public bool ToggleSound() => SetSound(!SoundOn);

        /// The write half on its own, so --sound yes|no can persist the same
        /// value the menu item does.
        public bool SetSound(bool on)
        {
            _s.Sound = on;
            Save();
            return on;
        }

        /// ToggleOpt for command 104 (LTANK.C:886).  Same shape as the sound.
        public bool ToggleAnimation() => SetAnimation(!AnimationOn);

        public bool SetAnimation(bool on)
        {
            _s.Animation = on;
            Save();
            return on;
        }

        /// Command 115 (LTANK.C:978).  The write half; Recorder does the rest,
        /// because that command also turns the recorder itself on or off.
        public bool SetAutoRecord(bool on)
        {
            _s.AutoRecord = on;
            Save();
            return on;
        }

        /// ToggleOpt for command 116, "Skip Completed Levels" (LTANK.C:996).
        /// The same shape as the sound and the animation, which is what that
        /// function being one function for five menu items means.
        ///
        /// `persist: false` is the run-only form `--skip-completed` and
        /// `--name` share: the value applies to this session and the store is
        /// not touched unless `--save-options` says so.
        public bool SetSkipCompleted(bool on, bool persist = true)
        {
            _s.SkipCompleted = on;
            if (persist) Save();
            return on;
        }

        /// DiffBox's Close (LTANK_D.C:286): rebuild the mask out of the five
        /// boxes and persist it.
        ///
        /// **What is not carried is the `if (Difficulty > 0) EndDialog(...)`
        /// under it** -- the original refuses to close a dialog with nothing
        /// ticked, which is how it guarantees the mask it hands LoadNextLevel
        /// can match something.  There is no dialog to refuse to close here, so
        /// the guarantee is made in the value instead: an empty mask reads as a
        /// full one, exactly as the level list's own `Rank` reads it, because a
        /// mask with nothing in it is never what the click meant.
        public int SetDifficulty(int mask, bool persist = true)
        {
            mask &= AllRanks;
            if (mask == 0) mask = AllRanks;
            _s.Difficulty = mask;
            if (persist) Save();
            return mask;
        }

        /// One rank on or off, which is what a chip click is.
        public int ToggleRank(int bit) => SetDifficulty(Difficulty ^ (bit & AllRanks));

        /// **The one name.**  `persist: false` is `--name`'s run-only form, the
        /// same arrangement `--sound` and `--zoom` have.
        ///
        /// **One of the original's own rules is deliberately not reproduced,
        /// and it is recorded rather than kept.**  HSBox writes its key only
        /// when the initials changed under a *case-insensitive* compare
        /// (`if (stricmp(temps, HS.name) != 0)`), so re-typing "MZ" as "mz"
        /// does not rewrite it there; RecordBox writes unconditionally.  Both
        /// are properties of a box that opens with one field and closes on OK.
        /// Here there is one field and one write, so the test is whether the
        /// text changed at all -- typing "mz" over "MZ" is an edit, and a
        /// settings row that quietly declined it would be the odd one.  The
        /// `stricmp` survives where it is still load-bearing: HighScores.Check
        /// applies it to the *record*, which is what the file actually carries.
        public void SetName(string name, bool persist = true)
        {
            name ??= "";
            if (name.Length > NameMax) name = name.Substring(0, NameMax);
            if (name == _s.Name) return;
            _s.Name = name;
            if (persist) Save();
        }

        /// The language picker's choice.  See IniImport.PsLang for why this
        /// port has one at all.
        public string LanguageCode => _s.Language;

        public void SetLanguage(string code)
        {
            _s.Language = string.IsNullOrEmpty(code) ? Core.Strings.BaseCode : code;
            Save();
        }

        /// LoadLevel's own write (LTANK2.C:1035), gated on RLL exactly there.
        public void RememberLevel(string lvlPath, int level)
        {
            if (!RememberLastLevel) return;
            _s.LastLevelFile = lvlPath;
            _s.LastLevel = level;
            Save();
        }
    }
}
