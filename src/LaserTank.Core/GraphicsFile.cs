// Readers for the graphics formats, and the two rendering tables that go with
// them.  Presentation *data*: nothing here is reachable from Tick() and nothing
// here feeds a decision (hazard #2) -- but the tables are the original's, not
// ours, so they live beside the other decoded formats rather than in the Godot
// project.  Kept free of Godot for the same reason the rest of Core is: it has
// to be checkable headless.
//
//   .ltg   324-byte TLTGREC header then two ordinary Windows BMPs
//          (LoadLTG, LTANK2.C:688)
//   .bmp   the internal pair, original/src/Game.BMP + Mask.BMP, which the 2007
//          build carries as resources ("GAMEBM"/"MASKBM", GFXInit LTANK2.C:745)
//
// Both are 320x192: a 10x6 grid of 32x32 sprites, BMA[] filled row-major from
// i = 1 (GFXInit, LTANK2.C:782).  The original StretchBlts the whole sheet to
// the current zoom at load; we keep it native and scale at draw time -- same
// picture without the resample, and no logic reads the sprite size.
using System;
using System.IO;
using System.Text;

namespace LaserTank.Core
{
    /// The rendering tables from the top of LTANK2.C.
    public static class Gfx
    {
        public const int MaxBitMaps = 58;               // LTANK.H:92
        public const int SpriteW = 32, SpriteH = 32;    // LTANK2.C:44 -- native, not the zoom
        public const int Cols = 10, Rows = 6;
        public const int SheetW = SpriteW * Cols, SheetH = SpriteH * Rows;

        public const int DirtBM = 1;                    // PutSprite's "add grass behind"
        public const int TunnelBM = 55;                 // painted over a solid colour

        /// LTANK2.C:80.  1 = the sprite has transparent pixels, so PutSprite and
        /// UpDateSprite paint a background first and then blit mask-SRCAND +
        /// bitmap-SRCPAINT; 0 = a plain SRCCOPY that ignores the mask entirely.
        /// Index 0 is the original's own padding ("Pad the beggining with junk").
        ///
        /// The declaration is `[MaxBitMaps+1]` = 59 entries but the initializer
        /// lists only 58, so C zero-fills the last one: BMSTA[58] = 0.  Carried
        /// rather than trimmed -- the array has to be indexable by every bitmap
        /// number up to MaxBitMaps, and the trailing zero is what the original
        /// reads there.  (The highest number the object table yields is 57.)
        public static readonly int[] BMSTA =
        {
            0,0,1,1,1,1,0,0,0,0,0,0,1,0,1,0,1,1,1,0,1,1,1,1,0,0,0,0,0,0,
            0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,0,0,0,0,0,0,0,1,1,1,0,0,0,
            0,
        };

        /// LTANK2.C:81 -- tunnel colours by tunnel id, as COLORREF (0x00BBGGRR).
        public static readonly uint[] ColorList =
        {
            0x000000FF, 0x0000FF00, 0x00FF0000, 0x00FFFF00,
            0x0000FFFF, 0x00FF00FF, 0x00FFFFFF, 0x00808080,
        };

        /// COLORREF is BGR; hand back the R,G,B a renderer wants.
        public static void Rgb(uint colorref, out byte r, out byte g, out byte b)
        {
            r = (byte)(colorref & 0xFF);
            g = (byte)((colorref >> 8) & 0xFF);
            b = (byte)((colorref >> 16) & 0xFF);
        }

        /// Which sprites are ever blitted through the mask.
        ///
        /// BMSTA's, plus the tunnel: UpDateSprite's tunnel branch (LTANK2.C:498)
        /// paints a coloured rectangle and then does mask-SRCAND +
        /// bitmap-SRCPAINT over it *regardless* of BMSTA[55] being 0, which is
        /// the only reason a tunnel's colour is visible at all.  Miss this and
        /// every tunnel renders as an opaque black disc -- the eight colours
        /// that tell one tunnel pair from another simply vanish.
        public static bool Masked(int bm) =>
            bm == TunnelBM || (bm >= 0 && bm <= MaxBitMaps && BMSTA[bm] == 1);

        /// GFXInit (LTANK2.C:782): x starts at 0, steps by one sprite, wraps
        /// every ten -- so bitmap i is grid cell ((i-1) % 10, (i-1) / 10).
        /// Bitmap numbers are 1-based; 0 is not a sprite.
        public static bool CellOf(int bm, out int cx, out int cy)
        {
            cx = cy = 0;
            if (bm < 1 || bm > MaxBitMaps) return false;
            cx = (bm - 1) % Cols;
            cy = (bm - 1) / Cols;
            return cy < Rows;
        }
    }

    /// A decoded 320x192 sprite sheet: straight RGBA8, top-down, mask already
    /// folded into the alpha channel.
    public sealed class SpriteSheet
    {
        public readonly byte[] Rgba = new byte[Gfx.SheetW * Gfx.SheetH * 4];
        public string Name = "", Author = "", Info = "";

        public const int LtgHeaderSize = 324;           // LTANK.H TLTGREC
        private const string LtgId = "LTG1";            // LTG_ID

        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private static string Str(byte[] b, int off, int len)
        {
            int n = 0;
            while (n < len && b[off + n] != 0) n++;
            return Latin1.GetString(b, off, n);
        }

        /// TLTGREC's three strings, without decoding either bitmap: `Name[40]`,
        /// `Author[30]`, `Info[245]`, then `ID[5]` and the MaskOffset DWORD.
        /// GetLTGFiles (LTANK_D.C:1170) fills the graphics dialog's listbox with
        /// exactly this -- one ReadFile of sizeof(TLTGREC) per pack and no
        /// bitmap work at all, so a pack with broken bitmaps still lists by
        /// name.  -> the MaskOffset, which is the one field a caller may want to
        /// sanity-check before reading the rest.
        public static uint LtgHeader(string path, out string name, out string author,
                                     out string info)
        {
            byte[] d = new byte[LtgHeaderSize];
            using (FileStream f = File.OpenRead(path))
                if (f.Read(d, 0, LtgHeaderSize) != LtgHeaderSize)
                    throw new IOException(path + ": short header");
            return LtgHeader(d, path, out name, out author, out info);
        }

        private static uint LtgHeader(byte[] d, string path, out string name,
                                      out string author, out string info)
        {
            string id = Str(d, 315, 5);
            if (id != LtgId) throw new IOException(path + ": not an LTG file (ID \"" + id + "\")");
            name = Str(d, 0, 40);
            author = Str(d, 40, 30);
            info = Str(d, 70, 245);
            return BitConverter.ToUInt32(d, 320);
        }

        /// LoadLTG (LTANK2.C:688): header, then the game bitmap up to
        /// MaskOffset, then the mask bitmap to EOF.  The original checks only
        /// the ID string, and so do we -- everything after it went to
        /// CreateDIBitmap, which is what Bmp.Decode stands in for.
        public static SpriteSheet FromLtg(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < LtgHeaderSize) throw new IOException(path + ": short header");

            long maskOffset = LtgHeader(d, path, out string name, out string author,
                                        out string info);
            if (maskOffset <= LtgHeaderSize || maskOffset >= d.Length)
                throw new IOException(path + ": MaskOffset " + maskOffset + " outside the file");

            SpriteSheet sheet = Build(
                Bmp.Decode(d, LtgHeaderSize, (int)(maskOffset - LtgHeaderSize), path + " (game)"),
                Bmp.Decode(d, (int)maskOffset, (int)(d.Length - maskOffset), path + " (mask)"));
            sheet.Name = name;
            sheet.Author = author;
            sheet.Info = info;
            return sheet;
        }

        /// The internal pair, as GFXInit loads it in graphics mode 0 and 1.
        public static SpriteSheet FromBmpPair(string gamePath, string maskPath)
        {
            byte[] g = File.ReadAllBytes(gamePath), m = File.ReadAllBytes(maskPath);
            return Build(Bmp.Decode(g, 0, g.Length, gamePath),
                         Bmp.Decode(m, 0, m.Length, maskPath));
        }

        /// Fold the 1-bit mask into an alpha channel.
        ///
        /// The original blits mask-SRCAND then bitmap-SRCPAINT: where the mask
        /// is white the destination survives (dest AND 0xFFFFFF) and the sprite
        /// is ORed onto it -- which reads as transparency only because a
        /// well-formed pack keeps those sprite pixels black.  So alpha 0 where
        /// the mask is white, 255 elsewhere.
        ///
        /// Only the cells Gfx.Masked names get that treatment; for the others
        /// the original does a plain SRCCOPY and never touches the mask, so
        /// their alpha stays 255 whatever the mask happens to say -- and it does
        /// say plenty: in the internal pair every opaque sprite's mask cell is
        /// solid white, which applied blindly would erase the whole sheet.
        private static SpriteSheet Build(Bmp.Image game, Bmp.Image mask)
        {
            if (game.W != Gfx.SheetW || game.H != Gfx.SheetH)
                throw new IOException($"game bitmap is {game.W}x{game.H}, expected {Gfx.SheetW}x{Gfx.SheetH}");
            if (mask.W != Gfx.SheetW || mask.H != Gfx.SheetH)
                throw new IOException($"mask bitmap is {mask.W}x{mask.H}, expected {Gfx.SheetW}x{Gfx.SheetH}");

            bool[] transparentCell = new bool[Gfx.Cols * Gfx.Rows];
            for (int bm = 1; bm <= Gfx.MaxBitMaps; bm++)
                if (Gfx.Masked(bm) && Gfx.CellOf(bm, out int cx, out int cy))
                    transparentCell[cy * Gfx.Cols + cx] = true;

            SpriteSheet s = new SpriteSheet();
            for (int y = 0; y < Gfx.SheetH; y++)
            {
                for (int x = 0; x < Gfx.SheetW; x++)
                {
                    int i = (y * Gfx.SheetW + x) * 4;
                    s.Rgba[i] = game.Rgba[i];
                    s.Rgba[i + 1] = game.Rgba[i + 1];
                    s.Rgba[i + 2] = game.Rgba[i + 2];
                    bool white = mask.Rgba[i] > 127 && mask.Rgba[i + 1] > 127 && mask.Rgba[i + 2] > 127;
                    bool cut = white && transparentCell[(y / Gfx.SpriteH) * Gfx.Cols + (x / Gfx.SpriteW)];
                    s.Rgba[i + 3] = (byte)(cut ? 0 : 255);
                }
            }
            return s;
        }
    }

    /// A Windows BMP reader covering exactly what this game ships: 1, 4, 8 and
    /// 24 bpp, BI_RGB / BI_RLE8 / BI_RLE4.  Godot has its own BMP loader, but
    /// the internal sheet is RLE and every mask is 1 bpp, and a decoder we can
    /// run headless is worth more here than one we cannot test without a window.
    internal static class Bmp
    {
        internal sealed class Image
        {
            public int W, H;
            public byte[] Rgba;             // top-down, alpha 255
        }

        private static ushort U16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
        private static int I32(byte[] b, int o) =>
            b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);

        internal static Image Decode(byte[] d, int off, int len, string what)
        {
            if (len < 54 || off + len > d.Length) throw new IOException(what + ": truncated");
            if (d[off] != 'B' || d[off + 1] != 'M') throw new IOException(what + ": not a BMP");

            int dataOff = I32(d, off + 10);
            int hdrSize = I32(d, off + 14);
            if (hdrSize < 40) throw new IOException(what + ": BITMAPCOREHEADER not supported");
            int w = I32(d, off + 18), h = I32(d, off + 22);
            int bpp = U16(d, off + 28);
            int comp = I32(d, off + 30);
            int clrUsed = I32(d, off + 46);
            bool topDown = h < 0;
            if (topDown) h = -h;
            if (w <= 0 || h <= 0) throw new IOException(what + $": {w}x{h}");

            // Palette: biClrUsed entries, or the full 1 << bpp for indexed depths.
            int palOff = off + 14 + hdrSize;
            int palCount = clrUsed != 0 ? clrUsed : (bpp <= 8 ? 1 << bpp : 0);
            byte[] pal = new byte[Math.Max(palCount, 1) * 4];
            for (int i = 0; i < palCount && palOff + i * 4 + 3 < off + len; i++)
            {
                pal[i * 4] = d[palOff + i * 4 + 2];      // stored BGRX
                pal[i * 4 + 1] = d[palOff + i * 4 + 1];
                pal[i * 4 + 2] = d[palOff + i * 4];
                pal[i * 4 + 3] = 255;
            }

            Image img = new Image { W = w, H = h, Rgba = new byte[w * h * 4] };
            for (int i = 3; i < img.Rgba.Length; i += 4) img.Rgba[i] = 255;

            int px = off + dataOff;
            if (comp == 0) Rgb(d, px, off + len, img, bpp, pal, topDown, what);
            else if (comp == 1 && bpp == 8) Rle(d, px, off + len, img, pal, false, topDown);
            else if (comp == 2 && bpp == 4) Rle(d, px, off + len, img, pal, true, topDown);
            else throw new IOException(what + $": unsupported {bpp} bpp / compression {comp}");
            return img;
        }

        /// Uncompressed rows, bottom-up unless biHeight is negative, each row
        /// padded to a 4-byte boundary.
        private static void Rgb(byte[] d, int px, int end, Image img, int bpp,
                                byte[] pal, bool topDown, string what)
        {
            int stride = ((img.W * bpp + 31) / 32) * 4;
            if (px + stride * img.H > end) throw new IOException(what + ": short pixel data");
            for (int row = 0; row < img.H; row++)
            {
                int src = px + row * stride;
                int y = topDown ? row : img.H - 1 - row;
                for (int x = 0; x < img.W; x++)
                {
                    int idx;
                    switch (bpp)
                    {
                        case 1: idx = (d[src + (x >> 3)] >> (7 - (x & 7))) & 1; break;
                        case 4: idx = (x & 1) == 0 ? d[src + (x >> 1)] >> 4 : d[src + (x >> 1)] & 0x0F; break;
                        case 8: idx = d[src + x]; break;
                        case 24:
                            Put(img, x, y, d[src + x * 3 + 2], d[src + x * 3 + 1], d[src + x * 3]);
                            continue;
                        default: throw new IOException(what + $": unsupported {bpp} bpp");
                    }
                    PutIdx(img, x, y, pal, idx);
                }
            }
        }

        /// BI_RLE8 and BI_RLE4.  (count, value) runs, with count == 0 selecting
        /// an escape: 0 end of line, 1 end of bitmap, 2 a delta, >= 3 an
        /// absolute run padded to a word boundary.
        private static void Rle(byte[] d, int p, int end, Image img, byte[] pal,
                                bool four, bool topDown)
        {
            int x = 0, row = 0;
            while (p + 1 < end)
            {
                int count = d[p++], val = d[p++];
                if (count > 0)
                {
                    for (int i = 0; i < count && x < img.W; i++, x++)
                    {
                        int idx = !four ? val : ((i & 1) == 0 ? val >> 4 : val & 0x0F);
                        PutIdx(img, x, Y(img, row, topDown), pal, idx);
                    }
                    continue;
                }
                if (val == 0) { x = 0; row++; }
                else if (val == 1) return;
                else if (val == 2)
                {
                    if (p + 1 >= end) return;
                    x += d[p++]; row += d[p++];
                }
                else
                {
                    for (int i = 0; i < val; i++)
                    {
                        int at = four ? p + (i >> 1) : p + i;
                        if (at >= end) return;
                        int idx = !four ? d[at] : ((i & 1) == 0 ? d[at] >> 4 : d[at] & 0x0F);
                        if (x < img.W) PutIdx(img, x, Y(img, row, topDown), pal, idx);
                        x++;
                    }
                    int bytes = four ? (val + 1) / 2 : val;
                    p += bytes + (bytes & 1);            // padded to a word
                }
            }
        }

        private static int Y(Image img, int row, bool topDown) =>
            topDown ? row : img.H - 1 - row;

        private static void PutIdx(Image img, int x, int y, byte[] pal, int idx)
        {
            if (idx * 4 + 2 >= pal.Length) return;
            Put(img, x, y, pal[idx * 4], pal[idx * 4 + 1], pal[idx * 4 + 2]);
        }

        private static void Put(Image img, int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || y < 0 || x >= img.W || y >= img.H) return;
            int i = (y * img.W + x) * 4;
            img.Rgba[i] = r; img.Rgba[i + 1] = g; img.Rgba[i + 2] = b;
        }
    }
}
