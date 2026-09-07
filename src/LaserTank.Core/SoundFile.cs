// Phase 5, step 3: the 16 WAVs, and the table that says which is which.
//
// Presentation data, like GraphicsFile.cs: nothing inside Tick() can reach any
// of it, and it lives in Core only because the .wav reader is plain byte
// pushing that the Godot layer should not have to own.  (The line is whether a
// rule could move, not which directory a file sits in.)
//
// **The id -> file mapping is not a naming convention, it is SFxInit's load
// order.**  lt_sfx.c:47 calls SoundLoad sixteen times; SoundLoad increments
// LastSFWord and stores the handle at that index, so the first call is id 1.
// That order is exactly the LT_Sound_Types enum (lt_sfx.h:13) and exactly
// Ltank.rc's RCDATA list, which is where the file names come from -- the
// resources are the same .wav files, converted to a character stream because
// lcc-win32 could not handle user-defined resources (lt_sfx.c's own header
// comment).  So Names[3] is "MOVE" is S_Move is move.wav.  Read the table.
//
// The files themselves are in original/src/Sounds/, which is frozen and
// read-only -- the same arrangement the internal sprite sheet already has
// (Paths.InternalGameBmp).  Their cases disagree (bricks.wav but ANTI1.WAV),
// and so do the .rc's spellings, so every lookup here is case-insensitive.
using System;
using System.IO;

namespace LaserTank.Core
{
    /// One decoded PCM sound: what a .wav in original/src/Sounds/ turns into.
    public sealed class SoundClip
    {
        public string Name;          // the resource name, e.g. "MOVE"
        public string Path;          // where it was read from
        public int Rate;             // samples per second: 11025, or 8000 for two
        public int Channels;         // 1 for all sixteen
        public int Bits;             // 8 for all sixteen
        public byte[] Pcm8;          // **signed** 8-bit frames -- see FromWav

        public int Frames => Pcm8.Length / Math.Max(Channels, 1);
        public double Seconds => Frames / (double)Math.Max(Rate, 1);
    }

    public static class SoundFile
    {
        /// lt_sfx.c:47, in order.  Index 0 is unused: SoundLoad's pre-increment
        /// means the first sound is id 1, and MaxSounds (20) leaves four spare
        /// slots that were never filled.
        public static readonly string[] Names =
        {
            null,
            "BRICKS", "FIRE", "MOVE", "HEAD", "TURN", "ENDLEV", "DIE", "ANTI1",
            "ANTI2", "DEFLB", "LASER2", "PUSH2", "PUSH1", "ROTATE", "PUSH3", "SINK",
        };

        /// The highest id the table defines.  lt_sfx.h's MaxSounds is 20; only
        /// these sixteen are ever loaded, and no SoundPlay call site names a
        /// higher one.
        public const int Count = 16;

        /// Ltank.rc:24-54.  The .wav file for a sound id.
        public static string FileName(int id) => Names[id] + ".wav";

        /// A RIFF/WAVE file -> a SoundClip with **signed** 8-bit samples.
        ///
        /// Two conversions, both silent-if-wrong and so worth naming:
        ///
        ///   * **8-bit WAV samples are unsigned** (0..255, silence at 128), and
        ///     every engine that takes raw 8-bit PCM -- Godot's AudioStreamWav
        ///     included -- wants them *signed* (-128..127, silence at 0).  The
        ///     conversion is one XOR with 0x80; getting it wrong produces a
        ///     loud DC-offset click rather than an error.
        ///   * **16-bit input is downshifted**, not resampled.  None of the
        ///     sixteen is 16-bit; the arm exists so that a replacement file is
        ///     a quieter sound rather than a crash.
        ///
        /// Anything that is not plain PCM throws: an ADPCM file decoded as PCM
        /// is noise, and noise that plays is worse than a file that does not.
        public static SoundClip FromWav(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 12 || Tag(d, 0) != "RIFF" || Tag(d, 8) != "WAVE")
                throw new IOException(path + ": not a RIFF/WAVE file");

            int fmt = 0, channels = 0, rate = 0, bits = 0;
            int dataAt = -1, dataLen = 0;

            // Chunks are <id:4><size:4><payload>, the payload padded to even.
            // Walk all of them: these files carry fact/LIST/DISP chunks between
            // fmt and data, and several put LIST *after* data.
            for (int i = 12; i + 8 <= d.Length; )
            {
                string id = Tag(d, i);
                long size = U32(d, i + 4);
                int body = i + 8;
                if (size < 0 || body + size > d.Length) size = d.Length - body;
                if (id == "fmt " && size >= 16)
                {
                    fmt = U16(d, body);
                    channels = U16(d, body + 2);
                    rate = (int)U32(d, body + 4);
                    bits = U16(d, body + 14);
                }
                else if (id == "data")
                {
                    dataAt = body;
                    dataLen = (int)size;
                }
                i = body + (int)size + ((size & 1) != 0 ? 1 : 0);
            }

            if (dataAt < 0 || channels <= 0 || rate <= 0)
                throw new IOException(path + ": no fmt/data chunk");
            if (fmt != 1)
                throw new IOException(path + ": WAVE format " + fmt +
                                      ", only PCM (1) is read");

            byte[] pcm;
            if (bits == 8)
            {
                pcm = new byte[dataLen];
                for (int i = 0; i < dataLen; i++) pcm[i] = (byte)(d[dataAt + i] ^ 0x80);
            }
            else if (bits == 16)
            {
                pcm = new byte[dataLen / 2];
                for (int i = 0; i < pcm.Length; i++)
                    pcm[i] = (byte)(U16(d, dataAt + 2 * i) >> 8);
            }
            else
            {
                throw new IOException(path + ": " + bits +
                                      "-bit PCM, only 8 and 16 are read");
            }

            return new SoundClip
            {
                Path = path,
                Name = System.IO.Path.GetFileNameWithoutExtension(path).ToUpperInvariant(),
                Rate = rate,
                Channels = channels,
                Bits = 8,
                Pcm8 = pcm,
            };
        }

        /// SFxInit (lt_sfx.c:47): load all sixteen from `dir`, by the table.
        /// -> clips[1..16]; index 0 stays null, as SFx[] does.
        ///
        /// The original's failure mode is one MessageBox and SFXError, after
        /// which SoundPlay returns immediately and the whole game is silent --
        /// so a missing file must not be fatal here either.  `missing` collects
        /// what could not be read; the caller decides how loudly to say so.
        public static SoundClip[] LoadAll(string dir, out string missing)
        {
            var clips = new SoundClip[Count + 1];
            string bad = null;
            for (int id = 1; id <= Count; id++)
            {
                try
                {
                    string p = Find(dir, FileName(id));
                    if (p == null) throw new FileNotFoundException(FileName(id) + " missing");
                    clips[id] = FromWav(p);
                    clips[id].Name = Names[id];
                }
                catch (Exception ex)
                {
                    bad = bad == null ? ex.Message : bad + "; " + ex.Message;
                }
            }
            missing = bad;
            return clips;
        }

        /// Case-insensitive lookup, for the same reason Paths.FindFile is:
        /// bricks.wav and ANTI1.WAV sit in one directory.
        private static string Find(string dir, string name)
        {
            if (!Directory.Exists(dir)) return null;
            string direct = System.IO.Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;
            foreach (string f in Directory.GetFiles(dir))
                if (string.Equals(System.IO.Path.GetFileName(f), name,
                                  StringComparison.OrdinalIgnoreCase))
                    return f;
            return null;
        }

        private static string Tag(byte[] d, int at) =>
            System.Text.Encoding.ASCII.GetString(d, at, 4);

        private static long U32(byte[] d, int at) =>
            d[at] | ((long)d[at + 1] << 8) | ((long)d[at + 2] << 16) | ((long)d[at + 3] << 24);

        private static int U16(byte[] d, int at) => d[at] | (d[at + 1] << 8);
    }
}
