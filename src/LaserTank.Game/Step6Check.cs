// Phase 5, step 6's headless self-checks.
//
// Step 6's new output is a screen full of translated labels, and the rule this
// phase keeps re-learning is that "it looks right" is not a criterion.  So the
// step gets two observable outputs, and they are checked in different ways --
// because they are different kinds of claim.
//
//   `--check-lang CODE` dumps the language the *game* resolved, in
//   LangDump's format, so tools/lang_check.py can diff it against both the C#
//   CLI's dump and its own Python rebuild.  This is the third implementation,
//   for step 4's reason: two loaders agreeing proves the data is intact, and
//   only the game proves that what the UI reads *is* that data.  It resolves
//   through `BoardView.Strings`, the same property every label on screen goes
//   through, rather than calling Language.Load itself -- a check that built its
//   own object would pass while the UI drew nine languages of nothing.
//
//   `--check-lang-ini DIR` is the INI half: the picker's choice has to survive
//   a restart, an unknown code has to degrade to the base language rather than
//   to `[ID_WINBOX_03]` on every label, and a foreign key in the file has to
//   come back untouched.  Same shape as options_check's INI half, and it runs
//   in a directory the tool hands it, because **an instrument must not write
//   the player's state.**
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
        /// LangDump.Escape, which lang_check.py rebuilds in Python.  Kept
        /// here rather than referenced out of LaserTank.Cli because the Godot
        /// project does not reference the CLI -- and a third copy of four
        /// substitutions is cheaper than a project dependency whose only job is
        /// to share them.
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

        /// `-- --check-lang CODE`
        public static int CheckLang(string code, Language lang)
        {
            if (lang == null)
            {
                GD.PrintErr("check-lang: no language loaded from "
                            + Paths.Data(Language.DirName));
                return 2;
            }

            var sb = new StringBuilder();
            sb.Append("code\t").Append(Escape(lang.Code)).Append('\n');
            sb.Append("name\t").Append(Escape(lang.Name)).Append('\n');
            sb.Append("author\t").Append(Escape(lang.Author)).Append('\n');
            foreach (string key in lang.Keys)
                sb.Append("S\t").Append(key).Append('\t')
                  .Append(Escape(lang[key])).Append('\n');
            for (int i = 0; i < lang.About.Length; i++)
                sb.Append("A\t").Append(i).Append('\t')
                  .Append(Escape(lang.About[i])).Append('\n');
            Menu(sb, "main", lang.MainMenu, "");
            Menu(sb, "editor", lang.EditorMenu, "");

            GD.PrintRaw(sb.ToString());

            // An unknown code resolves to the base language by design, so this
            // only fails when a code the tool believes is installed did not
            // load -- which would mean the game and the CLI disagree about
            // where data/language/ is, not about what is in it.
            if (code != null && lang.Code != code)
            {
                GD.PrintErr($"check-lang: asked for {code}, resolved {lang.Code}");
                return 1;
            }
            return 0;
        }

        private static void Menu(StringBuilder sb, string which, MenuNode[] items, string at)
        {
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
            {
                MenuNode n = items[i];
                string addr = (at.Length == 0 ? "" : at + ",") + i.ToString("00");
                sb.Append("M\t").Append(which).Append('\t').Append(addr).Append('\t')
                  .Append(n.Cmd).Append('\t')
                  .Append(n.Separator ? "SEP" : n.IsPopup ? "POPUP" : "ITEM").Append('\t')
                  .Append(Escape(n.Accel)).Append('\t')
                  .Append(Escape(n.Text)).Append('\n');
                Menu(sb, which, n.Items, addr);
            }
        }

        /// `-- --check-lang-ini DIR`
        ///
        /// One `ini <name> <detail> ok|FAIL` line per check.  Every one goes
        /// through Options, not through Ini, because the question is not
        /// whether a key can be written -- options_check settled that -- but
        /// whether the language the picker sets is the language the next run
        /// loads.
        public static int CheckLangIni(string dir)
        {
            if (!Directory.Exists(dir))
            {
                GD.PrintErr("check-lang-ini: no such directory: " + dir);
                return 2;
            }
            string path = Path.Combine(dir, Ini.FileName);
            string langDir = Paths.Data(Language.DirName);
            var results = new List<string>();

            void Check(string name, string detail, bool ok)
                => results.Add($"ini\t{name}\t{detail}\t{(ok ? "ok" : "FAIL")}");

            // 1. A file with no [DATA] Language at all resolves to the base.
            var fresh = new Options(new Ini(path));
            Check("default", fresh.LanguageCode,
                  fresh.LanguageCode == Language.BaseCode);

            // 2. The picker's write, and what the *next run* reads back.  Two
            //    Options objects over one file is the restart: the second one
            //    parses the bytes the first one wrote.
            List<LanguageInfo> have = Language.Available(langDir);
            string other = null;
            foreach (LanguageInfo li in have)
                if (li.Code != Language.BaseCode) { other = li.Code; break; }
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

            // 3. And the language really loads under that code.
            Language loaded = Language.Load(langDir, restarted.LanguageCode);
            Check("load", loaded == null ? "-" : loaded.Code,
                  loaded != null && loaded.Code == other);

            // 4. An unknown code degrades to the base language.  This is the
            //    one that matters for a hand-edited file: Language.Load returns
            //    the base rather than null, so nothing downstream has to guard.
            restarted.SetLanguage("Klingon");
            Language bogus = Language.Load(langDir, "Klingon");
            Check("unknown", bogus == null ? "null" : bogus.Code,
                  bogus != null && bogus.Code == Language.BaseCode);

            // 5. A key this port does not know survives being rewritten -- the
            //    same promise options_check makes for the rest of the file, now
            //    that step 6 adds a writer to [DATA].
            File.AppendAllText(path, "\r\n[DATA]\r\nMystery=keep me\r\n");
            var again = new Options(new Ini(path));
            again.SetLanguage(other);
            bool kept = File.ReadAllText(path).Contains("Mystery=keep me");
            Check("foreign", "Mystery", kept);

            foreach (string r in results) GD.PrintRaw(r + "\n");
            return results.Exists(r => r.EndsWith("FAIL")) ? 1 : 0;
        }
    }
}
