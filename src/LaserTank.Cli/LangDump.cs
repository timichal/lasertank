// `--lang-dump` / `--lang-list`: the language layer made observable.
//
// Step 6's own new output.  Every step of Phase 5 had to answer the question
// PROGRESS.md keeps asking -- what does this step emit, and how is it checked
// against something that is not itself? -- and the answer here cannot be the
// oracle, because the oracle would only tell us about a positional file format
// this port deliberately does not read (see Language.cs).  So the check is the
// sprite-sheet pattern instead: `tools/lang_check.py` rebuilds the same dump
// from the frozen `original/src/Setups/*/Language/Language.dat` in Python, and
// the two have to be identical string for string.
//
// The escaping is what makes that a byte comparison rather than a judgement
// call: one line per string, tabs and newlines escaped, so nothing in a
// translation can split a record.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LaserTank.Core;

namespace LaserTank.Cli
{
    public static class LangDump
    {
        /// Backslash, tab, CR and LF escaped C-style; everything else verbatim.
        /// UTF-8 goes out as itself -- the whole point of the conversion is that
        /// the port no longer has a codepage to get wrong.
        public static string Escape(string s)
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

        /// `--lang-list`: one `code<TAB>name` per installed language.
        public static int List(string dir, TextWriter w)
        {
            List<LanguageInfo> found = Language.Available(dir);
            if (found.Count == 0)
            {
                Console.Error.WriteLine("lasertank-core: no languages under " + dir);
                return 2;
            }
            foreach (LanguageInfo li in found)
                w.Write(li.Code + "\t" + Escape(li.Name) + "\n");
            return 0;
        }

        /// `--lang-dump CODE`: the whole resolved language, fallback applied.
        ///
        /// Resolved rather than raw on purpose.  What the gate has to check is
        /// what the UI would actually show, and for six of the ten files that
        /// includes strings inherited from the base language -- so dumping the
        /// file's own contents would leave the fallback, the one piece of logic
        /// in here, untested.
        public static int Dump(string dir, string code, TextWriter w)
        {
            Language lang = Language.Load(dir, code);
            if (lang == null)
            {
                Console.Error.WriteLine(
                    "lasertank-core: no " + Language.BaseCode + " language under " + dir);
                return 2;
            }

            w.Write("code\t" + Escape(lang.Code) + "\n");
            w.Write("name\t" + Escape(lang.Name) + "\n");
            w.Write("author\t" + Escape(lang.Author) + "\n");

            foreach (string key in lang.Keys)
                w.Write("S\t" + key + "\t" + Escape(lang[key]) + "\n");

            for (int i = 0; i < lang.About.Length; i++)
                w.Write("A\t" + i.ToString(CultureInfo.InvariantCulture)
                        + "\t" + Escape(lang.About[i]) + "\n");

            DumpMenu(w, "main", lang.MainMenu, "");
            DumpMenu(w, "editor", lang.EditorMenu, "");
            return 0;
        }

        /// The menu as one line per node, addressed the way the original's own
        /// `.dat` addresses it -- `00`, `00,05`, `02,07,01` -- so a reader can
        /// put any line back against the source file it came from.
        private static void DumpMenu(TextWriter w, string which, MenuNode[] items, string at)
        {
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
            {
                MenuNode n = items[i];
                string addr = (at.Length == 0 ? "" : at + ",")
                              + i.ToString("00", CultureInfo.InvariantCulture);
                w.Write("M\t" + which + "\t" + addr + "\t"
                        + n.Cmd.ToString(CultureInfo.InvariantCulture) + "\t"
                        + (n.Separator ? "SEP" : n.IsPopup ? "POPUP" : "ITEM") + "\t"
                        + Escape(n.Accel) + "\t" + Escape(n.Text) + "\n");
                DumpMenu(w, which, n.Items, addr);
            }
        }
    }
}
