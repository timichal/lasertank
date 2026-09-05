// Object IDs and the tunnel encoding, from LTANK.H.
//
// Names follow the original.  The table at the top of LTANK.H is the
// authority; the bitmap numbers in GetOBMArray come from LTANK2.C:77.
namespace LaserTank.Core
{
    public static class Obj
    {
        public const int MaxObjects = 26;      // LTANK.H:91

        public const int Dirt = 0;
        public const int Tank = 1;             // only ever present in level data
        public const int Flag = 2;
        public const int Water = 3;            // LTANK.H:108  Obj_Water
        public const int Solid = 4;
        public const int Block = 5;
        public const int Bricks = 6;
        public const int AntiTankUp = 7;
        public const int AntiTankRight = 8;
        public const int AntiTankDown = 9;
        public const int AntiTankLeft = 10;
        public const int MirrorUL = 11;
        public const int MirrorUR = 12;
        public const int MirrorDR = 13;
        public const int MirrorDL = 14;
        public const int ConveyorUp = 15;
        public const int ConveyorRight = 16;
        public const int ConveyorDown = 17;
        public const int ConveyorLeft = 18;
        public const int Crystal = 19;
        public const int RotoUL = 20;
        public const int RotoUR = 21;
        public const int RotoDR = 22;
        public const int RotoDL = 23;
        public const int Ice = 24;             // LTANK.H:109  Obj_Ice
        public const int ThinIce = 25;         // LTANK.H:110  Obj_ThinIce

        public const int Tunnel = 0x40;        // LTANK.H:111  Obj_Tunnel, 01dddddX

        // LTANK2.C:77 -- object id to bitmap number.
        private static readonly int[] GetOBMArray =
        {
            1, 2, 6, 9, 13, 14, 15, 16, 36, 39, 42, 20, 21, 22, 23, 24, 27, 30,
            33, 45, 47, 48, 49, 50, 56, 57, 55,
        };

        /// LTANK2.C:929.  The original takes a (signed) char, so the -1 guard is
        /// how it rejects the high-bit values that never survive BuildBMField.
        public static int GetOBM(int ob)
        {
            if (ob > -1 && ob <= MaxObjects) return GetOBMArray[ob];
            return 1;
        }

        // LTANK.H:308,314.  The low bit of a tunnel cell is the "waiting to
        // transport" flag (quirk #4), which is why the id is a >> 1 and why
        // callers strip it with & 0xFE.
        public static int GetTunnelID(int cell) => (cell & 0x0F) >> 1;

        public static bool IsTunnel(int cell) => (cell & Tunnel) == Tunnel;
    }
}
