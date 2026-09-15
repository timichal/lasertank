// Phase 5, step 6's headless self-checks -- rewritten by the i18n pass, which
// replaced the thing they used to check.
//
// Step 6 converted the original's ten `Language.dat` files and drew twenty-three
// of their strings.  What replaced it is `data/language/*.json`, eleven keyed
// catalogues of this port's own text (Core/Strings.cs says why), so these two
// checks now aim at that -- the claims are the same shape, the subject is new.
//
//   `--check-strings CODE` dumps the catalogue the *game* resolved, one escaped
//   `key<TAB>value` per line, so tools/strings_check.py can diff it against its
//   own read of the JSON.  Two readers agreeing proves the data is intact, and
//   only the game proves that what the UI reads *is* that data: it resolves
//   through `BoardView.Strings`, the same property every label on screen goes
//   through, rather than calling Strings.Load itself -- a check that built its
//   own object would pass while the UI drew eleven languages of nothing.
//
//   `--check-strings-ini DIR` is the INI half, and is untouched in substance:
//   the picker's choice has to survive a restart, an unknown code has to degrade
//   to the base language rather than to `[quit.title]` on every label, and a
//   foreign key in the file has to come back untouched.  Same shape as
//   options_check's INI half, and it runs in a directory the tool hands it,
//   because **an instrument must not write the player's state.**
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    public static class Step6Check
    {
        /// Backslash, tab, CR and LF escaped C-style, so one string cannot split
        /// a record however a translation is punctuated.  strings_check.py
        /// rebuilds the same escaping in Python, which is what makes the diff a
        /// byte comparison rather than a judgement call.
        private static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    default: sb.Append(c); break;
                }
            return sb.ToString();
        }

        /// `-- --check-strings CODE`
        public static int CheckStrings(string code, Strings lang)
        {
            if (lang == null)
            {
                GD.PrintErr("check-strings: no catalogue loaded from "
                            + Paths.Data(Strings.DirName));
                return 2;
            }

            var sb = new StringBuilder();
            sb.Append("code\t").Append(Escape(lang.Code)).Append('\n');
            sb.Append("name\t").Append(Escape(lang.Name)).Append('\n');
            sb.Append("credit\t").Append(Escape(lang.Credit)).Append('\n');
            foreach (string key in lang.Keys)
                sb.Append("S\t").Append(key).Append('\t')
                  .Append(Escape(lang[key])).Append('\n');

            GD.PrintRaw(sb.ToString());

            // An unknown code resolves to the base language by design, so this
            // only fails when a code the tool believes is installed did not
            // load -- which would mean the game and the tool disagree about
            // where data/language/ is, not about what is in it.
            if (code != null && lang.Code != code)
            {
                GD.PrintErr($"check-strings: asked for {code}, resolved {lang.Code}");
                return 1;
            }
            return 0;
        }

        /// `-- --check-strings-ini DIR`
        ///
        /// One `ini <name> <detail> ok|FAIL` line per check.  Every one goes
        /// through Options, not through Ini, because the question is not
        /// whether a key can be written -- options_check settled that -- but
        /// whether the language the picker sets is the language the next run
        /// loads.
        public static int CheckStringsIni(string dir)
        {
            if (!Directory.Exists(dir))
            {
                GD.PrintErr("check-strings-ini: no such directory: " + dir);
                return 2;
            }
            string path = Path.Combine(dir, Ini.FileName);
            string langDir = Paths.Data(Strings.DirName);
            var results = new List<string>();

            void Check(string name, string detail, bool ok)
                => results.Add($"ini\t{name}\t{detail}\t{(ok ? "ok" : "FAIL")}");

            // 1. A file with no [DATA] Language at all resolves to the base.
            var fresh = new Options(new Ini(path));
            Check("default", fresh.LanguageCode,
                  fresh.LanguageCode == Strings.BaseCode);

            // 2. The picker's write, and what the *next run* reads back.  Two
            //    Options objects over one file is the restart: the second one
            //    parses the bytes the first one wrote.
            List<LanguageInfo> have = Strings.Available(langDir);
            string other = null;
            foreach (LanguageInfo li in have)
                if (li.Code != Strings.BaseCode) { other = li.Code; break; }
            if (other == null)
            {
                Check("restart", "only the base language is installed", false);
                foreach (string r in results) GD.PrintRaw(r + "\n");
                return 1;
            }

            fresh.SetLanguage(other);
            var restarted = new Options(new Ini(path));
            Check("restart", $"{other} -> {restarted.LanguageCode}",
                  restarted.LanguageCode == other);

            // 3. And the catalogue really loads under that code.
            Strings loaded = Strings.Load(langDir, restarted.LanguageCode);
            Check("load", loaded == null ? "-" : loaded.Code,
                  loaded != null && loaded.Code == other);

            // 4. An unknown code degrades to the base language.  This is the
            //    one that matters for a hand-edited file: Strings.Load returns
            //    the base rather than null, so nothing downstream has to guard.
            restarted.SetLanguage("Klingon");
            Strings bogus = Strings.Load(langDir, "Klingon");
            Check("unknown", bogus == null ? "null" : bogus.Code,
                  bogus != null && bogus.Code == Strings.BaseCode);

            // 5. The per-key fallback, which the shipped corpus never exercises
            //    because the gate holds all eleven files to one key set.  So the
            //    check builds the file the corpus does not contain: a catalogue
            //    with one key blanked and one removed outright, both of which
            //    have to come back in English, and one key left translated,
            //    which must *not* be overwritten.  Both ways of being absent are
            //    separate lines in Strings.Load and both are tested.
            Check("fallback", FallbackProbe(langDir, out string why), why == null);
            if (why != null) results[results.Count - 1] += "  " + why;

            // 6. A key this port does not know survives being rewritten -- the
            //    same promise options_check makes for the rest of the file.
            File.AppendAllText(path, "\r\n[DATA]\r\nMystery=keep me\r\n");
            var again = new Options(new Ini(path));
            again.SetLanguage(other);
            bool kept = File.ReadAllText(path).Contains("Mystery=keep me");
            Check("foreign", "Mystery", kept);

            foreach (string r in results) GD.PrintRaw(r + "\n");
            return results.Exists(r => r.Contains("\tFAIL")) ? 1 : 0;
        }

        /// Writes a deliberately partial `zz.json` into a scratch directory
        /// beside the real catalogues, loads it, and reports what came back.
        /// The scratch copy is a *copy* of the base file, so nothing here writes
        /// into `data/language/`.
        private static string FallbackProbe(string langDir, out string why)
        {
            why = null;
            string tmp = Path.Combine(Path.GetTempPath(),
                                      "lt-fallback-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                string basePath = Path.Combine(langDir, Strings.BaseCode + ".json");
                if (!File.Exists(basePath)) { why = "no base file"; return "-"; }
                File.Copy(basePath, Path.Combine(tmp, Strings.BaseCode + ".json"));

                // One key blanked, one dropped, one given a value of its own.
                File.WriteAllText(Path.Combine(tmp, "zz.json"),
                    "{\"code\":\"zz\",\"name\":\"Probe\",\"strings\":{"
                    + "\"quit.yes\":\"\",\"quit.no\":\"nope\"}}");

                Strings probe = Strings.Load(tmp, "zz");
                Strings en = Strings.Load(tmp, Strings.BaseCode);
                bool blanked = probe["quit.yes"] == en["quit.yes"];   // filled in
                bool missing = probe["quit.title"] == en["quit.title"];
                bool kept = probe["quit.no"] == "nope";               // not clobbered
                if (!blanked) why = "an empty value was not filled from the base";
                else if (!missing) why = "an absent key was not filled from the base";
                else if (!kept) why = "a translated key was overwritten";
                return "blank/absent/kept";
            }
            catch (Exception ex) { why = ex.Message; return "-"; }
            finally
            {
                try { Directory.Delete(tmp, true); } catch (Exception) { }
            }
        }
    }
}
