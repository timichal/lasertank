// Phase 5, step 3: the sound sink -- the whole of what sound costs the core.
//
// The tick already decides which sound fires and when.  lt_sfx.c's SoundPlay is
// called from twenty-five places in LTANK2.C and LTANK.C, and one of its
// arguments is load-bearing for the rules (FireLaser's `laser.Good = (sf == 2)`
// separates the tank's shot from an anti-tank's), so the ids are read out of
// the engine and never re-derived somewhere else.  That is the same contract
// the renderer keeps with Game.BMF: the presentation layer reads what the tick
// computed, it does not recompute it.
//
// **This file adds no rule and can change no trace.**  SoundPlay's body writes
// one list that nothing inside Tick() ever reads.
//
// Why a partial method and a nullable list, rather than an event or a field per
// sound:
//
//   * A partial method keeps every call site in Engine.cs byte-identical to the
//     C.  The body lives here, outside the transliteration, where it belongs.
//   * `SoundLog` is null unless a driver opts in, so the solver -- millions of
//     MoveTank / FireLaser calls, one Engine per search -- pays a null test per
//     SoundPlay and allocates nothing.  An event would cost a delegate
//     invocation; a per-tick array would cost an allocation per Engine.
//   * A *list*, not "the last id", because the order within a tick is the check
//     (tools/sound_check.py diffs it against the C oracle's own SoundPlay
//     sequence).  What a player *hears* is a different question, and the answer
//     there is the last one: see Sfx.cs on PlaySound's semantics.
using System.Collections.Generic;

namespace LaserTank.Core
{
    public sealed partial class Engine
    {
        /// Every SoundPlay id since the driver last cleared it, in call order.
        /// **null means nobody is listening**, which is every headless build and
        /// every solver search; the driver that wants the ids allocates it once
        /// and clears it per tick (Session.Step, LaserTank.Cli's loop).
        public List<int> SoundLog;

        /// lt_sfx.c:26.  The real one checks Sound_On and hands the resource to
        /// PlaySound; both of those are the presentation's business -- muting is
        /// a property of the player, not of the game, and the original's own
        /// `if (!Sound_On) return` would otherwise make a muted game trace
        /// differently from a loud one.  So the id is always recorded and
        /// Sfx.cs decides whether anything is audible.
        partial void SoundPlay(int sn) => SoundLog?.Add(sn);
    }
}
