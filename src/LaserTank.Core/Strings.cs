// Every word this port puts on screen, and the eleven files it reads them from.
//
// **This is not the original's language layer, and the difference is the point.**
// `LANGUAGE.C` reads a *positional* file -- `Language\Language.dat`, 240 lines
// in six fixed-size sections, each translation in whichever 8-bit codepage its
// author's Windows happened to use -- whose 155 slots describe a Windows menu
// bar, a nine-button control panel and sixteen dialogs.  This port has none of
// those.  Step 7 replaced the control panel with a column, step 8 merged the
// three list dialogs into one table, step 9 made every label a button and step
// 11 turned the Search dialog into a filter bar; by the end of it twenty-three
// of the hundred and fifty-five slots had a widget reading them and the rest
// described an interface that no longer exists.
//
// So the audit next-steps item 1 asked for was run, and it answered *throw them
// away*: the frozen `original/src/Setups/*/Language/Language.dat` files are
// still in the tree and `tools/convert_language.py` still decodes all ten (run
// it with `--check`), which is where the codepage findings live -- but nothing
// at runtime reads a 2007 string any more.  What replaced them is this: one
// flat keyed catalogue per language, keys named after the widget that draws
// them, prose written rather than inherited.
//
// **The rule the gate enforces, both ways.**  `tools/strings_check.py` fails if
// a key in `en.json` appears in no source file, and fails if a lookup names a
// key `en.json` does not have.  That is next-steps item 1's rule -- *a key is
// read by a widget or it goes* -- turned from an audit anyone has to remember
// into a check nobody can forget.  It also holds the eleven files to one key
// set, checks that every `{0}` in English survives translation, and caps the
// handful of labels that are placed by character column rather than measured.
//
// **What is not in here.**  Instrument output.  Every `--check-*` line, every
// `--play` and `--options` dump, every string tools/ parses: those are
// measurements, and a measurement that moves when the player picks a language
// is not one.  The rule predates this file -- see HighScores.Describe, which
// has been two methods since step 6 for exactly this reason -- and the split is
// the same as an instrument not writing the player's state.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LaserTank.Core
{
    /// A short description of one installed language, for the picker.
    public readonly struct LanguageInfo
    {
        public LanguageInfo(string code, string name, string path)
        { Code = code; Name = name; Path = path; }

        public string Code { get; }
        public string Name { get; }
        public string Path { get; }
    }

    public sealed class Strings
    {
        /// The base language every other one falls back to, key by key.
        ///
        /// ISO 639-1 codes, not the 2007 installer's directory names: those were
        /// never language codes -- `Du` is Dutch, `Sp` Spanish -- and two of them
        /// were actively wrong, with `Cs`/`Ct` turning out to be Simplified and
        /// Traditional Chinese rather than Czech.  Which is why `cs.json` here is
        /// the first Czech LaserTank has ever had.
        public const string BaseCode = "en";

        public const string DirName = "language";
        private const string Ext = ".json";

        private readonly Dictionary<string, string> _strings;

        private Strings(string code, string name, string credit,
                        Dictionary<string, string> strings)
        { Code = code; Name = name; Credit = credit; _strings = strings; }

        public string Code { get; }

        /// The English name of the language, not the endonym -- the picker draws
        /// in a face with no CJK glyphs, so the two Chinese rows would be boxes.
        /// The endonyms are worth having the day this ships a font that can
        /// render them.
        public string Name { get; }

        /// Who wrote this translation, shown under the picker when it is set.
        /// Empty in every file that ships today, and that is honest: they were
        /// written with the port rather than contributed to it.
        public string Credit { get; }

        /// What the game is drawing with right now.
        ///
        /// A static because the catalogue is read from places that have no view
        /// to reach through -- Session's load errors are the reason -- and
        /// because there is exactly one of it: the picker swaps the whole
        /// object, and every draw after that reads the new one.  Instruments
        /// must not touch it; see the header.
        /// **Resolved rather than initialised**, and that is not a style
        /// preference: static field initialisers run in declaration order, so
        /// `= Empty` here reads `Empty` before its own initialiser has run and
        /// pins `null` for the life of the process.  It cost one gate --
        /// `collections_check` opened a collection before `_Ready` had loaded a
        /// language and got a NullReferenceException out of a *load error
        /// message*, which is about as far from the failure as a symptom gets.
        public static Strings Cur => _cur ?? Empty;

        private static Strings _cur;

        public static void SetCurrent(Strings s) => _cur = s ?? Empty;

        /// A catalogue that answers nothing, so a caller with no `data/language/`
        /// still has an object.  Every lookup comes back as `[key]`, which is the
        /// visible-marker rule below taken to its limit: a port with no strings
        /// installed says so on every label rather than throwing out of a draw.
        public static Strings Empty { get; } = new Strings(
            "--", "", "", new Dictionary<string, string>(StringComparer.Ordinal));

        /// Every key this catalogue resolves, in sorted order.
        public IEnumerable<string> Keys
        {
            get
            {
                var keys = new List<string>(_strings.Keys);
                keys.Sort(StringComparer.Ordinal);
                return keys;
            }
        }

        // ------------------------------------------------------------------
        // lookup
        // ------------------------------------------------------------------
        /// The string for `key`, or the key itself in brackets if unknown.
        ///
        /// Returning a visible marker rather than throwing or returning null is
        /// deliberate: a missing key is a bug in the data, and a label reading
        /// `[quit.title]` on screen says so immediately where a blank one would
        /// look like a layout mistake.  The gate is what keeps it from ever
        /// reaching a player -- it asserts the eleven files agree on the key set
        /// and that every lookup in the source names a key that exists.
        public string this[string key]
            => _strings.TryGetValue(key, out string s) ? s : "[" + key + "]";

        public bool Has(string key)
            => _strings.TryGetValue(key, out string s) && s.Length != 0;

        /// `key` with `{0}`, `{1}` ... replaced by `args`, left to right.
        ///
        /// Not `string.Format`: a translation is data, and data that can throw
        /// out of a draw call over a stray brace is a crash waiting for the one
        /// language nobody tested.  An index with no argument, and any brace run
        /// that is not a placeholder, is left exactly as written -- which puts
        /// the mistake on screen where it can be seen, next to the text it
        /// belongs to.  `strings_check.py` compares the placeholder sets across
        /// the eleven files, so a translation that drops one fails the gate
        /// before it can reach here.
        public string F(string key, params object[] args)
        {
            string s = this[key];
            if (s.IndexOf('{') < 0 || args == null || args.Length == 0) return s;

            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '{') { sb.Append(s[i]); continue; }
                int j = i + 1, n = 0;
                while (j < s.Length && s[j] >= '0' && s[j] <= '9')
                { n = n * 10 + (s[j] - '0'); j++; }
                if (j == i + 1 || j >= s.Length || s[j] != '}' || n >= args.Length)
                { sb.Append(s[i]); continue; }
                sb.Append(Convert.ToString(args[n],
                                           System.Globalization.CultureInfo.InvariantCulture));
                i = j;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // loading
        // ------------------------------------------------------------------
        /// Every `<code>.json` in `dir`, sorted with the base language first and
        /// the rest by name -- the order the picker shows them in.
        public static List<LanguageInfo> Available(string dir)
        {
            var found = new List<LanguageInfo>();
            if (dir == null || !Directory.Exists(dir)) return found;

            foreach (string path in Directory.GetFiles(dir, "*" + Ext))
            {
                string code = Path.GetFileNameWithoutExtension(path);
                string name = code;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                    if (doc.RootElement.TryGetProperty("name", out JsonElement n))
                        name = n.GetString() ?? code;
                    if (doc.RootElement.TryGetProperty("code", out JsonElement c))
                        code = c.GetString() ?? code;
                }
                catch (Exception)
                {
                    continue;               // not one of ours; the picker skips it
                }
                found.Add(new LanguageInfo(code, name, path));
            }

            found.Sort((a, b) =>
                a.Code == BaseCode ? (b.Code == BaseCode ? 0 : -1)
                : b.Code == BaseCode ? 1
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        /// Load `code` out of `dir`, filling anything it does not answer from the
        /// base language.  Returns null when `dir` holds no base file at all,
        /// which is the only case a caller has to handle -- an unknown or
        /// misspelt `code` quietly resolves to the base.
        ///
        /// **The fallback never fires against the shipped files**, because the
        /// gate holds all eleven to one key set.  It is kept for the file that is
        /// hand-edited or half-finished, and for the alternative, which is
        /// `[quit.title]` on a button.
        public static Strings Load(string dir, string code)
        {
            Strings baseLang = ReadOne(dir, BaseCode);
            if (code == null || code.Length == 0 || code == BaseCode) return baseLang;

            Strings want = ReadOne(dir, code);
            if (want == null) return baseLang;
            if (baseLang == null) return want;

            foreach (KeyValuePair<string, string> kv in baseLang._strings)
                if (!want._strings.TryGetValue(kv.Key, out string s) || s.Length == 0)
                    want._strings[kv.Key] = kv.Value;

            return want;
        }

        private static Strings ReadOne(string dir, string code)
        {
            if (dir == null) return null;
            string path = Path.Combine(dir, code + Ext);
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = doc.RootElement;

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("strings", out JsonElement obj))
                foreach (JsonProperty p in obj.EnumerateObject())
                    strings[p.Name] = p.Value.GetString() ?? string.Empty;

            return new Strings(Str(root, "code", code), Str(root, "name", code),
                               Str(root, "credit", ""), strings);
        }

        private static string Str(JsonElement o, string name, string fallback)
            => o.TryGetProperty(name, out JsonElement v)
               ? (v.GetString() ?? fallback) : fallback;
    }
}
