// Phase 5, step 3: lt_sfx.c, as a Godot node.
//
// The whole sound unit of the original is 62 lines, and two of its properties
// are load-bearing for how the game *sounds*, both of them hiding in one
// argument:
//
//     PlaySound(p, 0, 5);        // 5 = SND_MEMORY | SND_ASYNC
//
//   * **It is monophonic.**  PlaySound owns the waveform device for the
//     process, and a second call while a sound is playing *stops the first*
//     and starts the new one (that is what SND_NOSTOP exists to prevent, and
//     it is not passed).  So the original never mixes two effects: the tank
//     moving over a laser bounce cuts the bounce off.  One AudioStreamPlayer,
//     Play() every time, reproduces that exactly -- sixteen players, or one
//     per sound, would be a *nicer* game that does not sound like this one.
//   * **It is asynchronous**, so the 50 ms tick never waits for a 2.6 s DIE.
//     Godot's playback is async anyway; the thing to not do is await it.
//
// Consequence for a tick that asks for several sounds -- and MoveObj, AntiTank
// and the laser routinely do -- **only the last one is ever heard**, because
// the earlier ones are cut off microseconds later.  Session hands over the
// whole per-tick list in call order and this file plays the last of it; the
// list, not the last id, is what tools/sound_check.py diffs against the C
// oracle, so the audible-behaviour choice and the fidelity check stay separate.
//
// Muting is here rather than in the engine.  lt_sfx.c:29 returns early on
// !Sound_On, so a muted original makes no PlaySound call at all -- but it makes
// exactly the same *decisions*, and the port records decisions.  Turning the
// sound off must not change a trace, so it cannot be a property of the engine.
using System;
using Godot;
using LaserTank.Core;

namespace LaserTank.Game
{
    public sealed partial class Sfx : Node
    {
        private readonly SoundClip[] _clips;      // [1..16], index 0 unused
        private readonly AudioStreamWav[] _streams;
        private AudioStreamPlayer _player;

        /// lt_sfx.c:17.  Set when any file failed to load, after which the
        /// original goes silent for the rest of the session rather than
        /// half-playing.  Kept because the failure mode is observable.
        public string Error { get; }

        /// lt_sfx.c:19, [OPT] Sound.  The menu's "Toggle Sound" (command 102).
        public bool SoundOn { get; set; } = true;

        /// The id of the last sound actually handed to the player, for the HUD.
        public int Last { get; private set; }

        public Sfx(string dir)
        {
            Name = "Sfx";
            _clips = SoundFile.LoadAll(dir, out string missing);
            Error = missing;
            _streams = new AudioStreamWav[SoundFile.Count + 1];
            for (int id = 1; id <= SoundFile.Count; id++)
                if (_clips[id] != null) _streams[id] = ToStream(_clips[id]);
        }

        /// A decoded clip -> the Godot stream.  The sheet-loading rule applies
        /// here too: no resampling, no re-encoding.  The files are 8-bit mono
        /// at 11025 Hz (8000 for MOVE and PUSH3) and Godot plays them at their
        /// own rate, so what comes out is the same waveform the 2001 build fed
        /// to the wave device.
        private static AudioStreamWav ToStream(SoundClip c)
        {
            var s = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format8Bits,
                MixRate = c.Rate,
                Stereo = c.Channels > 1,
                LoopMode = AudioStreamWav.LoopModeEnum.Disabled,
                Data = c.Pcm8,
            };
            return s;
        }

        public override void _Ready()
        {
            _player = new AudioStreamPlayer { Name = "Player" };
            AddChild(_player);
        }

        /// SoundPlay (lt_sfx.c:26), including both of its early returns.
        public void Play(int id)
        {
            if (!SoundOn) return;                      // lt_sfx.c:29
            if (Error != null) return;                 // lt_sfx.c:30, SFXError
            if (id < 1 || id > SoundFile.Count) return;
            AudioStreamWav s = _streams[id];
            if (s == null || _player == null) return;
            // Assigning and playing is the SND_ASYNC-without-SND_NOSTOP
            // behaviour: whatever was playing stops here.
            _player.Stream = s;
            _player.Play();
            Last = id;
        }

        /// The whole of a tick, in call order.  Only the last is audible; see
        /// the file header for why that is the faithful answer and not a
        /// shortcut.
        public void PlayTick(System.Collections.Generic.IReadOnlyList<int> ids)
        {
            if (ids == null || ids.Count == 0) return;
            Play(ids[ids.Count - 1]);
        }

        public string Label(int id) =>
            id >= 1 && id <= SoundFile.Count ? SoundFile.Names[id] : "-";

        /// Headless self-check for step 3, in the shape --check-sheets already
        /// established: decode all sixteen and print what they decoded to, so
        /// tools/sound_check.py can compare against its own Python reader.  One
        /// decoder agreeing with itself would not be a check.
        ///
        ///   godot --headless --path src/LaserTank.Game -- --check-sounds
        public static int Check()
        {
            SoundClip[] clips = SoundFile.LoadAll(Paths.SoundsDir, out string missing);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int bad = 0;
            for (int id = 1; id <= SoundFile.Count; id++)
            {
                SoundClip c = clips[id];
                if (c == null)
                {
                    bad++;
                    GD.PrintRaw(string.Format(inv, "sound {0} {1} FAIL\n",
                                              id, SoundFile.Names[id]));
                    continue;
                }
                byte[] h = System.Security.Cryptography.SHA256.HashData(c.Pcm8);
                GD.PrintRaw(string.Format(inv,
                    "sound {0} {1} file={2} rate={3} channels={4} bits={5} " +
                    "frames={6} sha256={7}\n",
                    id, SoundFile.Names[id], System.IO.Path.GetFileName(c.Path),
                    c.Rate, c.Channels, c.Bits, c.Frames,
                    Convert.ToHexString(h).ToLowerInvariant()));
            }
            if (missing != null) GD.PrintRaw("sounds missing: " + missing + "\n");
            GD.PrintRaw(bad == 0 ? "sounds OK\n" : string.Format(inv, "sounds FAILED ({0})\n", bad));
            return bad == 0 ? 0 : 1;
        }
    }
}
