// The UI strings, and the two menu trees they hang on.
//
// Presentation *data*, on GraphicsFile.cs's test: nothing here is reachable
// from Tick() and nothing here feeds a decision.  It lives in Core rather than
// in the Godot project only so the gate can dump it headless.
//
// This is the one part of the port that is deliberately NOT a transliteration
// of its C, and PROGRESS.md step 6 records why.  `LANGUAGE.C` exists to read a
// *positional* file -- 240 lines in six fixed-size sections, each translation
// in whatever 8-bit codepage its author's Windows happened to use, with the
// section boundaries implied by SIZE_MMENU / SIZE_EMENU / SIZE_BUTTON /
// SIZE_TEXT / SIZE_DIALOGS / SIZE_ABOUTMSG in LT32L_US.H.  Nothing about the
// rules depends on any of that, and the population of such files is closed:
// the ten that shipped in 2007 are all there will ever be.  So they were
// converted once, by `tools/convert_language.py`, into keyed UTF-8 JSON under
// `data/language/`, and this reads that.
//
// The keys are not invented either.  Every one is the original's own name,
// parsed by the converter out of the frozen LT32L_US.H -- `ButText1..9`,
// `txt001..txt045` (with the header's own two gaps: no txt003, no txt030),
// `REC_Title`, `help01..03`, `HelpFileName`, and the 96 `ID_*` dialog slots --
// and every menu node carries the command id from the frozen lt32l_us.inc.
// `tools/lang_check.py` re-derives all of it and round-trips every string back
// to the original bytes, so the JSON is checkable against the artifact rather
// than merely plausible.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LaserTank.Core
{
    /// One item of MAIN or MENU2 (lt32l_us.inc:17, :80).
    ///
    /// `Cmd` is the WM_COMMAND the original posts -- 101 New, 110 Undo, 201
    /// Editor, and so on -- which makes it the stable identity of a menu item
    /// across all ten languages.  `Accel` is the hint the original prints after
    /// a tab (`&New\tF2`); it is cosmetic in both, because the keys that
    /// actually fire come from the ACC1 / ACC2 tables that step 1 ported.  It
    /// is carried verbatim, typo included: Restore Position is labelled
    /// `Crtl+V` in all ten files.
    public sealed class MenuNode
    {
        public string Text;
        public string Accel;                 // null when the item has no hint
        public int Cmd = -1;                 // -1 for a popup or a separator
        public bool Separator;
        public MenuNode[] Items;             // null unless this is a popup

        public bool IsPopup => Items != null;
    }

    /// A short description of one installed language, for the picker.
    public readonly struct LanguageInfo
    {
        public LanguageInfo(string code, string name, string path)
        { Code = code; Name = name; Path = path; }

        public string Code { get; }
        public string Name { get; }
        public string Path { get; }
    }

    public sealed class Language
    {
        /// The code the original's own English file carries, and the fallback
        /// every other language falls back to key by key.
        ///
        /// Falling back is the original's behaviour rather than a convenience:
        /// `InitLanguage` pre-fills the button and text slots from
        /// `App_Strings` and only *clears* the dialog and about-message ones,
        /// and `LoadWindowText` leaves a dialog control alone when its slot is
        /// empty -- which leaves the English text compiled into the .rc.  Four
        /// of the ten translations are partial and lean on exactly that.
        public const string BaseCode = "US";

        public const string DirName = "language";
        private const string Ext = ".json";

        private readonly Dictionary<string, string> _strings;

        private Language(string code, string name, string author,
                         Dictionary<string, string> strings, string[] about,
                         MenuNode[] main, MenuNode[] editor)
        {
            Code = code; Name = name; Author = author;
            _strings = strings; About = about;
            MainMenu = main; EditorMenu = editor;
        }

        public string Code { get; }
        public string Name { get; }
        public string Author { get; }

        /// A language that answers nothing, so a caller with no `data/language/`
        /// still has an object.  Every lookup on it comes back as `[key]`,
        /// which is the visible-marker rule below taken to its limit: a port
        /// with no strings installed says so on every label rather than
        /// throwing out of a draw.
        public static Language Empty { get; } = new Language(
            "--", "", "",
            new Dictionary<string, string>(StringComparer.Ordinal),
            Array.Empty<string>(), Array.Empty<MenuNode>(), Array.Empty<MenuNode>());

        /// The 14 lines of the About box, concatenated by LTANK_D.C:132.
        ///
        /// The one section with no fallback.  A translation that stops early
        /// leaves the tail empty and the original simply concatenates nothing,
        /// so a short about message is the translator's decision; borrowing the
        /// English tail would put two languages in one paragraph.  Four files
        /// are short here -- Cs and Ct carry 9 of the 14, and even the English
        /// one carries 12.
        public string[] About { get; }

        public MenuNode[] MainMenu { get; }
        public MenuNode[] EditorMenu { get; }

        /// Every key this language resolves, base included, in sorted order.
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
        /// the deliberate choice: a missing key is a bug in the data, and a
        /// label reading `[ID_WINBOX_09]` on screen says so immediately, where
        /// a blank one would just look like a layout mistake.  `lang_check.py`
        /// is what keeps it from ever happening -- it asserts the ten files
        /// agree on the key set.
        public string this[string key]
            => _strings.TryGetValue(key, out string s) ? s : "[" + key + "]";

        public bool Has(string key)
            => _strings.TryGetValue(key, out string s) && s.Length != 0;

        /// `key` with the original's printf placeholders filled in, left to
        /// right.  One string in the corpus needs it -- txt042, "Completed %d
        /// of %d levels" (LTANK_D.C's difficulty box) -- and the translations
        /// keep both specifiers, so a positional pass is enough.  A `%%`
        /// escapes a literal percent, as it does in C.
        public string Format(string key, params object[] args)
        {
            string s = this[key];
            var sb = new StringBuilder(s.Length + 16);
            int next = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '%' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char c = s[i + 1];
                if (c == '%') { sb.Append('%'); i++; continue; }
                if (c != 'd' && c != 's' && c != 'u' && c != 'c')
                { sb.Append(s[i]); continue; }
                sb.Append(next < args.Length
                    ? Convert.ToString(args[next++], CultureInfo.InvariantCulture)
                    : string.Empty);
                i++;
            }
            return sb.ToString();
        }

        /// Find the item carrying WM_COMMAND `cmd`, at any depth.  The port
        /// dispatches on the command id, so this is how a screen label and the
        /// code behind it stay tied together across ten languages.
        public MenuNode FindCommand(int cmd)
            => FindCommand(MainMenu, cmd) ?? FindCommand(EditorMenu, cmd);

        private static MenuNode FindCommand(MenuNode[] items, int cmd)
        {
            if (items == null) return null;
            foreach (MenuNode it in items)
            {
                if (!it.Separator && !it.IsPopup && it.Cmd == cmd) return it;
                MenuNode hit = FindCommand(it.Items, cmd);
                if (hit != null) return hit;
            }
            return null;
        }

        /// The label for `cmd` with the `&` accelerator marker removed, which
        /// is what a port with no Windows menu bar wants nine times out of ten.
        public string Label(int cmd)
        {
            MenuNode n = FindCommand(cmd);
            return n == null ? "[" + cmd.ToString(CultureInfo.InvariantCulture) + "]"
                             : StripAmpersand(n.Text);
        }

        /// Drop the `&` that marks a menu mnemonic, doubling `&&` to one.
        public static string StripAmpersand(string s)
        {
            if (s == null || s.IndexOf('&') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '&') { sb.Append(s[i]); continue; }
                if (i + 1 < s.Length && s[i + 1] == '&') { sb.Append('&'); i++; }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // loading
        // ------------------------------------------------------------------
        /// Every `<code>.json` in `dir`, sorted with the base language first
        /// and the rest by name -- the order the picker shows them in.
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

        /// Load `code` out of `dir`, filling anything it does not answer from
        /// the base language.  Returns null when `dir` holds no base file at
        /// all, which is the only case a caller has to handle -- an unknown or
        /// misspelt `code` quietly resolves to the base.
        public static Language Load(string dir, string code)
        {
            Language baseLang = ReadOne(dir, BaseCode);
            if (code == null || code.Length == 0 || code == BaseCode) return baseLang;

            Language want = ReadOne(dir, code);
            if (want == null) return baseLang;
            if (baseLang == null) return want;

            foreach (KeyValuePair<string, string> kv in baseLang._strings)
                if (!want._strings.TryGetValue(kv.Key, out string s) || s.Length == 0)
                    want._strings[kv.Key] = kv.Value;

            return want;
        }

        private static Language ReadOne(string dir, string code)
        {
            if (dir == null) return null;
            string path = Path.Combine(dir, code + Ext);
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = doc.RootElement;

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string section in new[] { "buttons", "text", "dialogs" })
                if (root.TryGetProperty(section, out JsonElement obj))
                    foreach (JsonProperty p in obj.EnumerateObject())
                        strings[p.Name] = p.Value.GetString() ?? string.Empty;

            var about = new List<string>();
            if (root.TryGetProperty("about", out JsonElement ab))
                foreach (JsonElement line in ab.EnumerateArray())
                    about.Add(line.GetString() ?? string.Empty);

            MenuNode[] main = null, editor = null;
            if (root.TryGetProperty("menu", out JsonElement menu))
            {
                if (menu.TryGetProperty("main", out JsonElement m)) main = ReadNodes(m);
                if (menu.TryGetProperty("editor", out JsonElement e)) editor = ReadNodes(e);
            }

            return new Language(
                Str(root, "code", code), Str(root, "name", code), Str(root, "author", ""),
                strings, about.ToArray(),
                main ?? Array.Empty<MenuNode>(), editor ?? Array.Empty<MenuNode>());
        }

        private static string Str(JsonElement o, string name, string fallback)
            => o.TryGetProperty(name, out JsonElement v)
               ? (v.GetString() ?? fallback) : fallback;

        private static MenuNode[] ReadNodes(JsonElement arr)
        {
            var list = new List<MenuNode>();
            foreach (JsonElement e in arr.EnumerateArray())
            {
                var n = new MenuNode
                {
                    Text = Str(e, "text", null),
                    Accel = Str(e, "accel", null),
                    Separator = e.TryGetProperty("separator", out JsonElement s)
                                && s.ValueKind == JsonValueKind.True,
                };
                if (e.TryGetProperty("cmd", out JsonElement c)) n.Cmd = c.GetInt32();
                if (e.TryGetProperty("items", out JsonElement it)) n.Items = ReadNodes(it);
                list.Add(n);
            }
            return list.ToArray();
        }
    }
}
