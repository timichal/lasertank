// The sprite sheet as a Godot texture.
//
// Decoding is LaserTank.Core's (SpriteSheet); this is only the handoff to the
// renderer plus the BMA[] geometry as a Rect2.  The sheet stays native 32x32
// and is scaled at draw time -- the original StretchBlt'd it to the zoom at
// load, which is the same picture with a resample we do not want (Phase 5,
// step 2).
using System.IO;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    public sealed class Atlas
    {
        public readonly SpriteSheet Sheet;
        public readonly ImageTexture Texture;
        public readonly string Label;

        private Atlas(SpriteSheet sheet, string label)
        {
            Sheet = sheet;
            Label = label;
            Image img = Image.CreateFromData(Gfx.SheetW, Gfx.SheetH, false,
                                             Image.Format.Rgba8, sheet.Rgba);
            Texture = ImageTexture.CreateFromImage(img);
        }

        /// `path` is a .ltg, or "" for the internal Game.BMP / Mask.BMP pair.
        public static Atlas Load(string path)
        {
            if (string.IsNullOrEmpty(path))
                return new Atlas(SpriteSheet.FromBmpPair(Paths.InternalGameBmp, Paths.InternalMaskBmp),
                                 "internal");
            SpriteSheet s = SpriteSheet.FromLtg(path);
            return new Atlas(s, string.IsNullOrEmpty(s.Name) ? Path.GetFileName(path) : s.Name);
        }

        /// GFXInit's external branch, GraphM == 1 (LTANK2.C:743): a loose
        /// game.bmp / mask.bmp pair in Graphics_Dir, folded exactly as the
        /// internal pair is -- same reader, same mask rule, so a pack unpacked
        /// out of its .ltg renders identically to the .ltg.  That equality is
        /// what tools/options_check.py checks.
        public static Atlas External(string gamePath, string maskPath) =>
            new Atlas(SpriteSheet.FromBmpPair(gamePath, maskPath), "external");

        /// GFXInit's BMA[i]: row-major from i = 1, ten per row.  Returns false
        /// for a bitmap number outside the 10x6 grid, which BuildBMField cannot
        /// produce -- see tools/atlas_check.py, which asserts that over the
        /// whole corpus rather than trusting it.
        public bool Region(int bm, out Rect2 src)
        {
            src = default;
            if (!Gfx.CellOf(bm, out int cx, out int cy)) return false;
            src = new Rect2(cx * Gfx.SpriteW, cy * Gfx.SpriteH, Gfx.SpriteW, Gfx.SpriteH);
            return true;
        }
    }
}
