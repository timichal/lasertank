// Phase 5, step 2: the graphics sets on offer, which is GFXInit's three modes
// plus the listbox GraphBox fills.
//
// GFXInit (LTANK2.C:730) has exactly three branches, and this file has the same
// three:
//
//   GraphM 0  internal   the pair the .exe carries as resources, GAMEBM/MASKBM
//   GraphM 1  external   game.bmp + mask.bmp in Graphics_Dir (LT32L_US.H:18)
//   GraphM 2  ltg        Graphics_File in Graphics_Dir, LoadLTG (LTANK2.C:688)
//
// and the fallback in each is the original's: a pack that will not load leaves
// the game running on the internal sheet rather than taking it down
// (`if (!(LoadLTG(...))) GraphM = 0;`).
//
// The list itself is GetLTGFiles (LTANK_D.C:1170): scan Graphics_Dir for *.ltg,
// read each 324-byte header, and offer it by the *name in the header* rather
// than the file name -- which is why "Lasertank_Comix.ltg" appears in the menu
// as "LaserTank Comix".  We sort by file name; the original takes FindFirstFile
// order, which on NTFS is the same thing and on nothing else is defined.
using System;
using System.Collections.Generic;
using System.IO;
using LaserTank.Core;

namespace LaserTank.Game
{
    /// One entry in the graphics menu: a GraphM mode and, in mode 2, the file
    /// it names.  Author and Info come from the .ltg header and are what the
    /// dialog's two read-only fields show (ID_GRAPHBOX_07 and _11).
    public sealed class Pack
    {
        public int Mode;                    // GraphM
        public string File = "";            // GraphFN -- a bare file name, mode 2 only
        public string Label = "";           // what the menu lists it as
        public string Author = "";
        public string Info = "";

        /// Mode 1 only: whether game.bmp and mask.bmp are actually there.  The
        /// entry is offered either way, because the original offers the "User
        /// Graphics" radio either way.
        public bool Available = true;

        public override string ToString() => Label;
    }

    public static class Packs
    {
        // LT32L_US.H:18.  Lower case in the source, and matched case-insensitively
        // here: the packs in this repo mix cases the way the level files do
        // (Game.BMP / mask.bmp), and a case-sensitive filesystem would silently
        // fall back to the internal sheet.
        public const string GameBmp = "game.bmp";
        public const string MaskBmp = "mask.bmp";

        /// The menu's list, in the dialog's own order: the two radio buttons
        /// first (internal, then user graphics), then the .ltg files.
        ///
        /// The `--pack N` numbering that step 0 documented -- 0 the internal
        /// sheet, 1..n the .ltg files sorted by name -- predates the external
        /// entry and is kept: PackByIndex skips it.
        public static List<Pack> Scan(string dir)
        {
            var list = new List<Pack>
            {
                new Pack { Mode = 0, Label = "Internal Graphics" },
                new Pack
                {
                    Mode = 1,
                    Label = "User Graphics (" + GameBmp + " + " + MaskBmp + ")",
                    Info = dir,
                    Available = Paths.FindFile(dir, GameBmp) != null
                                && Paths.FindFile(dir, MaskBmp) != null,
                },
            };

            foreach (string path in Paths.LtgFiles(dir))
            {
                var p = new Pack { Mode = 2, File = Path.GetFileName(path) };
                try
                {
                    // The header alone, not the sheet: the dialog reads
                    // sizeof(TLTGREC) and stops, so a pack whose bitmaps are
                    // broken still appears in the list by name.
                    SpriteSheet.LtgHeader(path, out string name, out string author,
                                          out string info);
                    p.Label = name.Length > 0 ? name : p.File;
                    p.Author = author;
                    p.Info = info;
                }
                catch (Exception ex)
                {
                    p.Label = p.File;
                    p.Info = ex.Message;
                    p.Available = false;
                }
                list.Add(p);
            }
            return list;
        }

        /// Step 0's `--pack N`: 0 the internal sheet, 1..n the .ltg files in
        /// Graphics_Dir sorted by name.  Mode 1 has no number and is reached by
        /// `--pack external`.
        public static Pack ByIndex(List<Pack> packs, int i)
        {
            var indexed = new List<Pack>();
            foreach (Pack p in packs) if (p.Mode != 1) indexed.Add(p);
            if (indexed.Count == 0) return packs[0];
            return indexed[((i % indexed.Count) + indexed.Count) % indexed.Count];
        }

        /// `--pack` also takes a name: `internal`, `external` / `ext`, or a
        /// .ltg file name (with or without the extension, case-insensitively).
        /// -> null if nothing matches, so the caller can say so rather than
        /// silently rendering the wrong sheet.
        public static Pack ByName(List<Pack> packs, string s)
        {
            if (string.Equals(s, "internal", StringComparison.OrdinalIgnoreCase))
                return packs[0];
            if (string.Equals(s, "external", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "ext", StringComparison.OrdinalIgnoreCase))
                return packs[1];
            string bare = Path.GetFileNameWithoutExtension(s);
            foreach (Pack p in packs)
                if (p.Mode == 2
                    && (string.Equals(p.File, s, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetFileNameWithoutExtension(p.File), bare,
                                         StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p.Label, s, StringComparison.OrdinalIgnoreCase)))
                    return p;
            return null;
        }

        /// What the persisted [SCREEN] Graphics_Mode / Graphics_File name.  A
        /// mode 2 whose file has since been deleted falls back to the internal
        /// sheet, which is GFXInit's own `if (!LoadLTG(...)) GraphM = 0`.
        public static Pack FromOptions(List<Pack> packs, Options o)
        {
            if (o.GraphicsMode == 1) return packs[1];
            if (o.GraphicsMode == 2)
            {
                Pack p = ByName(packs, o.GraphicsFile);
                if (p != null) return p;
            }
            return packs[0];
        }

        /// GFXInit's three branches.  `fallback` is set when the pack could not
        /// be loaded and the internal sheet was used instead -- the original
        /// puts up a MessageBox for the LTG case (LTANK_D.C's txt031) and
        /// silently drops to mode 0 for the rest; this reports it to the HUD.
        ///
        /// One deliberate deviation, and it is a bug we are not reproducing:
        /// GFXInit's external branch tests `if (!(Mh || Gh)) GraphM = 0;` --
        /// **`||` where it means `&&`** -- so with exactly one of game.bmp and
        /// mask.bmp present it stays in mode 1 and hands a NULL bitmap to
        /// SelectObject, and what happens then is undefined GDI, not a picture.
        /// There is nothing to transliterate: a decoder that throws cannot
        /// produce half a sheet.  Either file missing means the internal sheet
        /// here, which is what the working half of that line intends.
        public static Atlas Load(Pack p, string dir, out string fallback)
        {
            fallback = null;
            try
            {
                switch (p.Mode)
                {
                    case 1:
                        string g = Paths.FindFile(dir, GameBmp);
                        string m = Paths.FindFile(dir, MaskBmp);
                        if (g == null || m == null)
                            throw new FileNotFoundException(
                                "no " + (g == null ? GameBmp : MaskBmp) + " in " + dir);
                        return Atlas.External(g, m);
                    case 2:
                        string path = Paths.FindFile(dir, p.File);
                        if (path == null)
                            throw new FileNotFoundException("no " + p.File + " in " + dir);
                        return Atlas.Load(path);
                    default:
                        return Atlas.Load("");
                }
            }
            catch (Exception ex) when (p.Mode != 0)
            {
                fallback = ex.Message + " -- using the internal graphics";
                return Atlas.Load("");
            }
        }
    }
}
