// The structs LTANK.H declares, transliterated.  Field names are the
// original's on purpose -- see the header of Engine.cs.
namespace LaserTank.Core
{
    /// LTANK.H:161 tTankRec.  Used for the tank and for the laser.
    /// Good is overloaded: on the laser it means "still alive", on the tank it
    /// is the tunnel-wait flag.
    public struct TTANKREC
    {
        public int X, Y, Dir, Firing, Good;
    }

    /// LTANK.H:204 tIceRec.
    public struct TICEREC
    {
        public int x, y, dx, dy, s;
    }

    /// LTANK.H:213 tIceMem.  Entries are 1-based: IceMoveO walks the stack
    /// top-down and mutates it while iterating (quirk #6), and the cap of 15
    /// is silent (`if (SlideMem.count < MAX_TICEMEM-1)`).
    public sealed class TICEMEM
    {
        public const int MAX_TICEMEM = 16;
        public readonly TICEREC[] Objects = new TICEREC[MAX_TICEMEM];
        public int count;

        public void Clear()
        {
            System.Array.Clear(Objects, 0, Objects.Length);
            count = 0;
        }
    }

    /// LTANK.H:166 tGameRec.
    ///
    /// The four playfields are `char[16][16]` in C, indexed PF[x][y] with x the
    /// column.  They are stored here as byte, not sbyte: BuildBMField sanitises
    /// every cell to <= 0x19 or to a tunnel (0x40 | id&lt;&lt;1 | wait), so nothing
    /// above 0x7F survives a level load and the two signednesses cannot differ.
    /// The one place the distinction is visible in the original is
    /// `GetOBM(char)`'s `ob > -1` guard, which Obj.GetOBM keeps.
    public sealed class TGAMEREC
    {
        public const int W = 16, H = 16;

        public readonly byte[,] PF = new byte[W, H];    // game objects
        public readonly byte[,] PF2 = new byte[W, H];   // objects underneath
        public readonly byte[,] BMF = new byte[W, H];   // bitmaps  (cosmetic)
        public readonly byte[,] BMF2 = new byte[W, H];  // under-bitmaps (cosmetic)

        public ushort ScoreMove;
        public ushort ScoreShot;
        public uint RecP;                                // recording pointer
        public TTANKREC Tank;

        public void CopyPFFrom(byte[] flat)
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    PF[x, y] = flat[x * H + y];
        }

        /// The 256 bytes in the order the oracle's trace writes them
        /// (`put_field` dumps Game.PF[0], i.e. row-major with x major).
        public static void Flatten(byte[,] f, byte[] into)
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    into[x * H + y] = f[x, y];
        }
    }

    /// LTANK.H:136 tLevel -- one 576-byte record of a .lvl file.
    public sealed class TLEVEL
    {
        public const int Size = 576;

        public readonly byte[] PF = new byte[256];
        public string LName = "";
        public string Hint = "";
        public string Author = "";
        public ushort SDiff;
    }

    /// LTANK.H:145 tRecordRec -- the 66-byte header of a .lpb file.
    public sealed class TRECORDREC
    {
        public const int Size = 66;

        public string LName = "";
        public string Author = "";
        public ushort Level;
        public ushort DataSize;
    }
}
