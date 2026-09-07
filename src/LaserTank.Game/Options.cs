// Phase 5, step 2: LaserTank.ini -- the options the original persists, in the
// file it persists them in and under its own key names.
//
// The original has no options object.  It calls GetPrivateProfileInt /
// WritePrivateProfileString at the point of use against LaserTank.ini next to
// the .exe (LTANK.C:1411, LTANK.H:85 `INIFileName`), and step 2's business is
// three of those call sites:
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
// library: first match wins, sections and keys compare case-insensitively,
// integers follow atoi, and **a write preserves every other line in the file.**
// That last one is not politeness -- the 2010 binary keeps a dozen more keys in
// this same file (PosX, Diff_Setting, Player, Animation, Sound...), and a
// rewrite that dropped them would quietly reset the player's other settings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// GetPrivateProfileInt / GetPrivateProfileString / WritePrivateProfileString,
    /// over a file this process holds in memory as lines.
    public sealed class Ini
    {
        /// LTANK.H:85.  The original puts it beside the .exe; Paths.Ini decides
        /// where that is here.
        public const string FileName = "LaserTank.ini";

        private readonly List<string> _lines = new List<string>();

        /// The file is ANSI, and level and pack names in it carry latin-1 bytes.
        private static readonly Encoding Latin1 = Encoding.Latin1;

        public string Path { get; }

        /// True for the instrument modes -- --shot, --play, --check-*, a
        /// --tick-rate run.  They read the options like the game does and then
        /// write nothing, because they are measurements: eight parallel
        /// atlas_check jobs must not race each other over one file, and a
        /// screenshot must not change what the next player sees.  An explicit
        /// --ini clears it, which is how tools/options_check.py exercises the
        /// writing half.
        public bool ReadOnly { get; set; }

        public Ini(string path, bool readOnly = false)
        {
            Path = path;
            ReadOnly = readOnly;
            if (!File.Exists(path)) return;
            // Read as bytes: text mode would translate the file's CRLFs and the
            // rewrite would then change every line the port never touched.
            foreach (string line in Latin1.GetString(File.ReadAllBytes(path))
                                          .Split('\n'))
                _lines.Add(line.TrimEnd('\r'));
            // A trailing newline leaves one empty element; drop it so a write
            // does not grow the file by a blank line each time.
            if (_lines.Count > 0 && _lines[_lines.Count - 1].Length == 0)
                _lines.RemoveAt(_lines.Count - 1);
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

        /// The line index of `key` inside the section starting at `at`, or -1;
        /// `end` comes back as the first line past the section, which is where a
        /// new key goes.
        private int FindKey(int at, string key, out int end)
        {
            end = _lines.Count;
            for (int i = at + 1; i < _lines.Count; i++)
            {
                if (IsSection(_lines[i], out _)) { end = i; break; }
                int eq = _lines[i].IndexOf('=');
                if (eq > 0 && Eq(_lines[i].Substring(0, eq).Trim(), key)) { end = i; return i; }
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
            int k = FindKey(s, key, out _);
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

        /// WritePrivateProfileString: set the key in place, add it at the end of
        /// its section, or append the section.  Every other line survives.
        /// Writes the file immediately, as the Win32 call does.
        public void Set(string section, string key, string value)
        {
            string line = key + "=" + value;
            int s = FindSection(section);
            if (s < 0)
            {
                _lines.Add("[" + section + "]");
                _lines.Add(line);
            }
            else
            {
                int k = FindKey(s, key, out int end);
                if (k >= 0) _lines[k] = line;
                else _lines.Insert(end, line);
            }
            Save();
        }

        public void SetInt(string section, string key, int value) =>
            Set(section, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// -> false when nothing was written (ReadOnly, or the directory is
        /// gone).  Failing to persist an option must never take the game down,
        /// so this swallows the IO error and reports it.
        public bool Save()
        {
            if (ReadOnly) return false;
            try
            {
                var sb = new StringBuilder();
                foreach (string l in _lines) sb.Append(l).Append("\r\n");
                File.WriteAllBytes(Path, Latin1.GetBytes(sb.ToString()));
                Error = null;
                return true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return false;
            }
        }

        public string Error { get; private set; }
    }

    /// The options step 2 owns, named after the profile keys in LTANK.H:113-128
    /// so a diff against the original is one lookup.  Each setter persists
    /// immediately, which is what every call site in the original does.
    public sealed class Options
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
        public const string PsPBA = "Record Author";
        public const string PsYes = "Yes";

        private readonly Ini _ini;

        public Ini Ini => _ini;

        public Options(Ini ini)
        {
            _ini = ini;

            // LTANK.C:1567 -- the default really is 1, the 24 px board, not the
            // 32 px one this port used before it read the file.
            Size = Math.Clamp(_ini.GetInt(SecScreen, PsSize, 1), 1, 3);
            GraphicsMode = _ini.GetInt(SecScreen, PsGM, 0);
            if (GraphicsMode < 0 || GraphicsMode > 2) GraphicsMode = 0;
            // GFXInit reads Graphics_File only in mode 2 (LTANK2.C:1781); read
            // it always, so switching to mode 2 in the menu and back does not
            // lose the pack the file names.
            GraphicsFile = _ini.Get(SecScreen, PsGFN);
            GraphicsDir = _ini.Get(SecScreen, PsGDN);

            // "We only do this once" (LTANK2.C:1785): an empty Graphics_Dir is
            // filled in and written back.  The original uses the current
            // directory; the packs that ship with this repo are in data/
            // graphics, so that is the sensible first value here.
            if (GraphicsDir.Length == 0)
            {
                GraphicsDir = Paths.DefaultGraphicsDir;
                _ini.Set(SecScreen, PsGDN, GraphicsDir);
            }

            // LTANK.C:411 -- Sound defaults to Yes, and the original's test is
            // `if (strcmp(temps, psYes)) Sound_On = FALSE;`, i.e. **exactly
            // "Yes" or the sound is off**.  Case-sensitive, so a hand-edited
            // `sound=yes` really does mute the 2010 binary.  Kept as it is: it
            // is one line, it is observable, and there is no argument for
            // leniency beyond taste.  (The RLL reader just below is the looser
            // reading -- it went in with step 2 and its gate now pins it.  The
            // two disagreeing is worth knowing about, not worth harmonising by
            // guess.)
            SoundOn = YesNo(PsSound, true);

            // LTANK.C:404 -- Animation defaults to Yes, same strict test.  It
            // is the one option in this list that moves a *trace*: Ani_On gates
            // AniCount, and AniLevel and AniCount are both trace fields.  Turning
            // it off is therefore a legitimate way to make the port disagree
            // with the oracle -- the 2010 binary disagrees with itself the same
            // way -- which is why every headless gate leaves it alone: PlayMode
            // builds its Session with no Options at all, and Engine.Ani_On
            // defaults to true.
            AnimationOn = YesNo(PsAni, true);

            // LTANK.C:439 -- Remember Last Level defaults to Yes and takes the
            // same strict test as Sound and Animation:
            // `if (strcmp(temps,psYes)) RLL = FALSE;`.  Step 2 read this key
            // loosely (anything but "No") on the grounds that the idiom was
            // ambiguous; LTANK.C:439 is not ambiguous, so the loose reading is
            // gone and tools/options_check.py pins the strict one.
            RememberLastLevel = YesNo(PsRllOn, true);
            LastLevelFile = _ini.Get(SecData, PsRllN);
            LastLevel = _ini.GetInt(SecData, PsRllL, 1);

            // LTANK.C:424 -- and here the default flips: Auto_Record, like
            // SkipComLev and DisableWarnings, defaults to **No** and is tested
            // `strcmp(temps,psYes) == 0`.  Same one-sided comparison, opposite
            // sense, so a missing key means off rather than on.
            AutoRecord = YesNo(PsARec, false);

            // [DATA] Player and [DATA] Record Author -- the initials that go
            // into a .hs record and the name that goes into a .lpb header.
            // Neither has a default: the original opens its dialogs with an
            // empty box (LTANK_D.C:634, :983) and writes whatever comes back.
            Player = _ini.Get(SecData, PsUser);
            RecordAuthor = _ini.Get(SecData, PsPBA);
        }

        /// The original's Yes/No test, which is a `strcmp` against `psYes` and
        /// therefore **case-sensitive and exact**: a hand-edited `Sound=yes`
        /// really does mute the 2010 binary.  The only thing that varies between
        /// the six keys that use it is the default, and hence which side of the
        /// comparison the missing case falls on -- so both senses are one
        /// function with a `dflt`.
        private bool YesNo(string key, bool dflt) =>
            _ini.Get(SecOpt, key, dflt ? PsYes : "No") == PsYes;

        /// 1, 2 or 3 -- the original's small / medium / large, not a pixel size.
        public int Size { get; private set; }
        public int GraphicsMode { get; private set; }
        public string GraphicsFile { get; private set; }
        public string GraphicsDir { get; private set; }
        /// lt_sfx.c:19 via LTANK.C:411.  Muting is the *player's* state, never
        /// the engine's: SoundPlay's ids are recorded whatever this says, so
        /// turning the sound off cannot move a trace.
        public bool SoundOn { get; private set; }
        /// Engine.Ani_On, [OPT] Animation, command 104.  The one option here
        /// that changes what a tick does.
        public bool AnimationOn { get; private set; }
        public bool RememberLastLevel { get; private set; }
        public string LastLevelFile { get; private set; }
        public int LastLevel { get; private set; }
        /// [OPT] Auto_Record, command 115.
        public bool AutoRecord { get; private set; }
        /// [DATA] Player -- the initials a .hs record carries.  HSBox reads it
        /// with `GetPrivateProfileString(..., 5, ...)`, so four characters is
        /// the width the original can store and this trims to the same.
        public string Player { get; private set; }
        /// [DATA] Record Author -- the name a .lpb header carries.  RecordBox
        /// reads 30 into a char[31].
        public string RecordAuthor { get; private set; }

        /// SetGameSize's own write (LTANK2.C:1737).
        public void SetSize(int size)
        {
            Size = Math.Clamp(size, 1, 3);
            _ini.SetInt(SecScreen, PsSize, Size);
        }

        /// GraphBox's Close (LTANK_D.C:1247): the mode always, the file name only
        /// in mode 2.  Cancel writes the same thing -- there is no way out of
        /// that dialog that does not persist the choice, because the choice was
        /// already applied live.
        public void SetGraphics(int mode, string file)
        {
            GraphicsMode = mode;
            if (mode == 2) GraphicsFile = file ?? "";
            _ini.SetInt(SecScreen, PsGM, GraphicsMode);
            if (mode == 2) _ini.Set(SecScreen, PsGFN, GraphicsFile);
        }

        public void SetGraphicsDir(string dir)
        {
            GraphicsDir = dir;
            _ini.Set(SecScreen, PsGDN, dir);
        }

        /// ToggleOpt (LTANK.C:1489) for command 102, "Toggle Sound": flip it,
        /// write "Yes" or "No" on the spot.  The same function does Animation,
        /// Skip Completed Levels, AutoRecord and Disable Warnings, so this is
        /// the shape the rest of the [OPT] section will take.
        public bool ToggleSound() => SetSound(!SoundOn);

        /// The write half on its own, so --sound yes|no can persist the same
        /// value through the same key the menu item does.
        public bool SetSound(bool on)
        {
            SoundOn = on;
            _ini.Set(SecOpt, PsSound, SoundOn ? PsYes : "No");
            return SoundOn;
        }

        /// ToggleOpt for command 104 (LTANK.C:886).  Same shape as the sound.
        public bool ToggleAnimation() => SetAnimation(!AnimationOn);

        public bool SetAnimation(bool on)
        {
            AnimationOn = on;
            _ini.Set(SecOpt, PsAni, on ? PsYes : "No");
            return on;
        }

        /// Command 115 (LTANK.C:978).  The write half; Recorder does the rest,
        /// because that command also turns the recorder itself on or off.
        public bool SetAutoRecord(bool on)
        {
            AutoRecord = on;
            _ini.Set(SecOpt, PsARec, on ? PsYes : "No");
            return on;
        }

        /// HSBox's write (LTANK_D.C:830): the initials, trimmed to the four
        /// characters `GetWindowText(..., 5)` can return, and written only when
        /// they changed -- `if (stricmp(temps, HS.name) != 0)`, a
        /// case-*insensitive* compare, so re-typing "MZ" as "mz" does not
        /// rewrite the key.
        public void SetPlayer(string name)
        {
            name ??= "";
            if (name.Length > THSREC.NameEntry - 1)
                name = name.Substring(0, THSREC.NameEntry - 1);
            if (string.Equals(name, Player, StringComparison.OrdinalIgnoreCase)) return;
            Player = name;
            _ini.Set(SecData, PsUser, name);
        }

        /// RecordBox's write (LTANK_D.C:990), unconditional there.
        public void SetRecordAuthor(string name)
        {
            RecordAuthor = name ?? "";
            _ini.Set(SecData, PsPBA, RecordAuthor);
        }

        /// LoadLevel's own write (LTANK2.C:1035), gated on RLL exactly there.
        public void RememberLevel(string lvlPath, int level)
        {
            if (!RememberLastLevel) return;
            LastLevelFile = lvlPath;
            LastLevel = level;
            _ini.Set(SecData, PsRllN, lvlPath);
            _ini.SetInt(SecData, PsRllL, level);
        }
    }
}
