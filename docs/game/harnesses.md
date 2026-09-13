# The differential harnesses, and the corpus they run on

How equivalence is *demonstrated* rather than asserted: the trace differ, the input languages that
exist because a feature looked untestable, the fuzzer and its shrinker, and the three tiers of test
data they all run against. The gate commands themselves — which four must be green before anything
is believed — are in [`PROGRESS.md`](../../PROGRESS.md#build-then-check-nothing-rotted); what each
tool proves, one line apiece, is in [`repo.md`](repo.md).

---

## The differential harnesses, and how to debug with them

### Traces

`replay_all.py --engine` is what makes both sides one script, and it works because **the C# CLI
takes the oracle's arguments and emits byte-identical trace lines** — same field order, same
spacing, same `%08lx` hashes, same `#` header and result footer (`oracle/driver.c`, `trace_tick`).
Do not invent a nicer format: the differ is textual on purpose, so any drift shows up as a
divergence rather than as a parser bug. Trace line 1 differs by design (`# lasertank core trace` vs
`# lasertank oracle trace`); line 2 and every tick line are byte-identical, and `difftrace.py` reads
level/name/author/keys off line 2 to check both sides ran the same input.

```bash
python tools/test_difftrace.py                                  # trust the differ first
python tools/replay_all.py --traces build/t-oracle --field --bmf
python tools/replay_all.py --traces build/t-csharp --field --bmf \
       --engine build/lasertank-core.exe
python tools/difftrace.py build/t-oracle build/t-csharp -q      # -q: failures only
```

**Run both engines with `--field`** (and `--bmf` while touching `Animate`) so the diff has the whole
playfield to bite on, not just the hashes. Add `--pack game-objects` for a 16-recording fast loop
(one level per object); run the whole 187 before believing anything. The full pair takes a few
minutes and about 327 MB per side.

`difftrace.py` exits **1** for a logic divergence, **3** for a cosmetic-only one (`BMF`/`AniLevel`
— [hazard #2](quirks.md)), and `--strict` holds the cosmetic line too. What it gives you:

```
=== first divergence: tick 90 (line 91) ===
first field:  T.dir 1 -> 4                 [tank]
also:         T.firing 1 -> 0, L.y 11 -> 12, S.shots 23 -> 22
    PF[x=4,y=9]       0d mirror dr      -> 19 thin ice
```

**The field name *is* the localisation** — `S.moves` sends you to `ScoreMove`, `SlO.dy` to
`IceMoveO`, `P` to the key-consume test at `LTANK.C:613`. The summary counts how many ticks each
field diverges on, which separates "one wrong cell" from "everything after tick 90".

**The cheapest first milestone for any future port:** run with an empty keystream (`--keys ""`).
Both engines trace exactly two lines, `t=0` and `t=1`. Matching those two means the `.lvl` parser,
the `TGAMEREC` layout, `BuildBMField`, `PutLevel`, `Animate`, the fnv1a hashes and the trace
formatting are already right.

**When a trace stops early:** the CLI catches `NotPortedException`, closes the trace after the last
*complete* tick, writes `# result=NOTPORTED`, names the function and tick on stderr and exits **4**.
`difftrace.py` then says `DIVERGE length mismatch after N ticks`, which is the useful reading.
`NOTPORTED` is not a result.

### The input languages, and why they exist

Every one of these was added because a feature looked untestable — and the lesson generalised:
**when the oracle has no way to be asked, the question is what input language is missing, not
whether the C can answer.**

| flag | tokens | reaches |
|---|---|---|
| `--keys` | `u d l r f` | the tick, from a keystream |
| `--lpb FILE` | — | a recording |
| `--sound` | *(trace field `SF=`)* | `SoundPlay`'s per-tick id stream, in call order, `-` for silence |
| `--script` | `u d l r f` (press, only when the buffer has drained — the original's own pending rule), `.` (idle one tick), `z` (110), `Z` (the DeadBox's Undo Last Move: 110 + `GameOn(TRUE)`), `c` (111), `v` (112), `mXY`/`nXY` (a left/right click on cell XY, two hex digits) | `UndoStep`, Save/Restore Position, `MouseOperation` — all `WM_COMMAND` or `MBuffer` cases no keystream can reach |
| `--edit` | one token per editor step (`<NN` select, `lXY`/`rXY` paint, shifts, clear…) | `ChangeGO` and the editor's `LTANK.C` cases |

```bash
oracle/build/oracle.exe  --levels data/levels/LaserTank.lvl --level 7 \
    --script "uufz.zc.v" --trace a.tr --field
build/lasertank-core.exe --levels data/levels/LaserTank.lvl --level 7 \
    --script "uufz.zc.v" --trace b.tr --field
python tools/difftrace.py a.tr b.tr
```

`oracle/driver.c` implements each of these against the **real** 2002 C: `oracle/build.sh` compiles
`LTANK2.C` verbatim, so `UndoStep`, `MouseOperation`, `FindTarget` and `ChangeGO` on that side were
written in 2002 and this project has never read them into anything.

**Where a rule is implemented three times, that is on purpose.** For `LTANK.C` window-proc cases
(the undo commands, the editor's Clear/Shift, the mouse's `MBuffer` push) there is `oracle/driver.c`,
`LaserTank.Cli`'s own driver, and the *game's* path — `PlayMode` / `EditMode`. The first two prove
the arithmetic; the third proves that what the UI invokes **is** that arithmetic.

### Fuzzing

```bash
python tools/test_fuzz.py                            # trust the fuzzer      25 cases, ~90 s
python tools/sweep.py                                # 2,347 levels, empty keystream, ~50 s
python tools/fuzz.py --each 3 --seed 1               # 6,090 cases over the flagship, ~140 s
```

**Run `test_fuzz.py` before believing any green fuzz run.** A fuzzer that has never gone red is
untested, and "20,626 cases, no divergence" is then a claim about the fuzzer rather than about the
port. It patches `Engine.cs` with two faults a transliteration would plausibly make, rebuilds, and
requires that each is found, shrunk and independently reproducible — then restores `Engine.cs` in
bytes and requires green again.

**The shrinker is the deliverable, not an extra.** A divergence at key 300 of 400 on level 1,712 is
not a bug report; `level 1712, keys "rrfud"` is. `fuzz.reduce_keys` is three passes: shortest
diverging prefix by binary search, delta debugging (ddmin) over what is left, and then *measuring*
1-minimality rather than claiming it — delete each remaining key and check the divergence goes away.
All three hold the divergence **signature** fixed (the name of the first field that moved), so what
comes out is a reduction of the bug you started with. `--shrink-any` relaxes that. Findings are
deduped by signature, so a systematic bug reports once rather than 9,000 times; each gets a
directory with the minimal keystream, both traces re-run with `--field --bmf`, the full report, the
level as ASCII, the exact commands, and `findings.json`.

**Fuzz without `--field`/`--bmf`** — they add 512 hex bytes per tick each, and the default trace
already carries `H=fnv1a(PF),fnv1a(PF2)`, so a playfield divergence still shows up as a hash. The
minimal repro is then re-run *with* both, and the tool warns if the wider trace does not reproduce
the same signature.

**Consumed keys, not generated keys, is the honest coverage number**: random play drowns or shoots
the tank early, so the two differ by nearly half. The first campaign was six keystream shapes
(varying length and the fire/turn weights, because one shape is one bias) over the flagship and all
ten quirk packs: **20,626 cases, 3,751,638 tick-lines, 0 divergences**, 20–100% of keys consumed
depending on shape.

**Random keystreams are shallow.** Over half of all runs end `DEAD`, and the flagship's own level 1
needs 149 keypresses to win. That is the argument for the solver being the *other* kind of coverage:
a solved level is a long, legal, non-random path through the engine.

---

## Test corpus

**A trap for the day a `.lpb` will not replay:** `data/levels/LaserTank-2016-snapshot.zip` is a third
vintage of the flagship collection, **59 of its 2,030 levels differing** from the current one. A
recording made against one vintage cannot replay against another, so before debugging the engine,
check the level bytes.

**Tier 1 — `data/quirks/`, 317 quirk-focused levels, 187 recorded playbacks.** Upstream deliberately
withholds `.lpb` for the main collections; these help-section packs are the only recorded human
solutions in existence.

| Directory | Levels | LPB | Note |
|---|---:|---:|---|
| `tutor-with-playbacks` | 112 | 112 | + bundled `.ghs` — the only pack where recorded counts can be checked against a target |
| `tutor` | 92 | 0 | **the quirk specification** — each hint documents its trick |
| `rotary-mirrors` | 39 | 39 | 6 of these do not reach the flag — see below |
| `tricks` | 26 | 0 | |
| `pono-trick` | 18 | 20 | more LPBs than levels (alternate solutions) |
| `game-objects` | 16 | 16 | one level per object — **best first target for any new check** |
| `4triang`, `telek-1`, `l40`, `inchworm` | 14 | 0 | |

**Tier 2 — `data/levels/`, 20,914 levels across 13 collections**, every one with a non-zero `.ghs`
entry. No keystreams, but a solvability guarantee and a (moves, shots) target for each. The fuzzing
surface, and the population `SOLVER.md` measures against.

**Tier 3 — `data/demos/`, 20 hand playthroughs of `LaserTank.lvl` 1-19**, recorded by Michal and
verified through both engines. They cannot be regenerated and they are the only long winning lines
that are not solver output.

### The six non-winning recordings, and why they are the recordings

All six are in `rotary-mirrors`, and five of the six are the only files in that pack whose `.lpb`
author field reads `Ihab` rather than `Ihab-Ihab`. They consume their entire keystream and stop short
of the flag. `replay_all.py` pins each one's expected outcome, asserting the exact numbers wherever a
level hint documents them — so the suite is a real regression gate rather than a permanently red run.
`_0036` replays 621 keys / 419 shots against a hint that says *"stopped at step 621 (after 419
shots)"*; `_0009` gives 39 moves against *"blocked at 39 steps"*; `_0021` gives 148 moves / 257 shots
against *"it has a solution : 148/257 or better"*. `_0011`, `_0013` and `_0017` have no hint text.

**The zero-bump result.** A key that produces neither a move, a turn, nor a shot means the tank
walked into something solid. Across all 187 replays — **54,162 keypresses — not one bump.** A
desynced engine puts the tank in the wrong place and blocked moves pile up immediately.
`tools/bump_rate.py` computes this.

**The level-21 confirmation.** Its hint says *"it has a solution : 148/257 or better."* Replaying
gives exactly 148/257 and leaves the tank one cell above the flag, facing it — then its final two
keys turn the tank around and drive it into water. Replace that trailing `uu` with `dd` and the
oracle **wins at exactly 148/257**, the documented optimum. So the engine reproduces a known-good
solution precisely and the distributed recording's tail is simply wrong. (Level 21's playback ships
as `_0021.txt`, a Text-Converter base64 wrapper; decoded with `unpack_lpb_txt.py` and the only
derived file under `data/`, documented in `data/SOURCES.md` and regenerable from the `.txt` beside
it.)

