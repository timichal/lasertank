# `bench/recovered/` — artefacts retrieved from a `build/` that was about to be deleted

Six files that the solver docs describe as **lost**, recovered on **2026-09-13** from the gitignored
`build/` of the *port* machine — the box that runs the game half — in the session before that
directory was deleted. Nothing here is new work. Every one of them is a file some other session
already produced, measured, quoted in the docs, and then wrote off.

**Why they survived.** The solver moved to the other box on 2026-09-08. The machine move that
`bench/README.md` was written about took the solver's `build/` with it, and the docs reasoned from
there that the files were gone. They were gone *from the solver's machine*. This machine's `build/`
was never part of that move and still held its own copies, two days older and never overwritten —
which is the failure this directory's parent exists to prevent, arriving from the one direction the
rule did not cover: not "the file was not committed", but **"the file was not committed and the
second copy was not known about."**

The lesson `bench/README.md` already states is the right one and this does not amend it. The
addition is narrower: **before concluding a gitignored artefact is lost, check the other machine's
`build/` — and check it while that `build/` still exists.** The cost of not checking here would have
been two verified solutions and three measurement populations, permanently.

## What is here

| file | what it is | the doc that calls it lost |
|---|---|---|
| `LaserTank/00008.lpb` | `LaserTank.lvl` 8 in **308 keys (262 + 46), 1.4x** | `docs/solver/history.md` closed item 4 |
| `LaserTank/00009.lpb` | `LaserTank.lvl` 9 in **114 keys (81 + 33), 1.9x** | item 4; `docs/solver/driver.md:98`; `SOLVER.md:188` |
| `bench-levels.txt` | the original bench 1 — 60 `Beginner-I` levels layer 0 failed | `bench/bench-levels.txt` header, `bench/README.md` |
| `deep-levels.txt` | the original deep bench — 50 levels, `.ghs` total 40-150 | `bench/deep-levels.txt` header |
| `ferry-levels.txt` | the original ferry bench — 50 FERRY/SOKOBAN levels the chain fails | `bench/ferry-levels.txt` header |

Each list carries its own provenance, its recovered-vs-reconstructed evidence and its current read in
its header, which is this directory's parent's rule and the thing whose absence caused all of this.

## The two solutions, and what they are worth

`docs/solver/history.md` closed item 4 banks these two as *"A lost result worth knowing about"*:

> The shortest verified files for levels 8 and 9 once lived in a gitignored `build/w/` and went with
> it. […] Which configuration produced either is not recorded — the widths in the directory names
> were the only clue — so **neither is reproducible as a recipe.** Level 9's 114 has effectively been
> re-derived […]. **Level 8's 308 has not.**

Both files are here, and **both pass the gate** — `python tools/verify_solutions.py bench/recovered`,
each replayed through the frozen C oracle and the C# core with `--field --bmf`, both engines
reporting WIN with byte-identical traces. They are solutions in the only sense this project accepts.

- **Level 9's 114** is the smaller of the two results, and item 4 is right that it is nearly retired:
  the driver re-derives **115** at the same 1.9x with no flags aimed at the level, so this file is
  worth one key and the confirmation that the 114 was real.
- **Level 8's 308 / 1.4x was the find.** It was **27 keys shorter than the 335 / 1.5x** banked in
  `data/solutions/LaserTank/00008.lpb` at the time, it had not been re-derived, and it was the shortest
  verified level-8 route this project had.

**It is not banked into `data/solutions/` here, and that is deliberate** — re-banking is Michal's
call, and the docs record a preference worth honouring: session 42 moved level 9 from 127 to 115 and
noted it was *"re-derived rather than restored"*. A shorter route with no recipe behind it is exactly
the thing that preference is about. The file is preserved; the decision is not pre-empted.

> **2026-09-14 — and the preference is what paid.** Item 4's stage `acc` used this file as an
> acceptance bar rather than as a source: seeded with the 308, the driver was made to beat it or report
> `unsolved`. It beat it, at **305 keys / 1.37x**, in all four arms byte-identically
> (`d371e931a857`), and a driver run of its own then wrote that into
> `data/solutions/LaserTank/00008.lpb`. So level 8's banked route is **re-derived, not restored**, and
> the 308 kept its value by never being copied. Level 9's 114 is unchanged and still retired at 115.
> Nothing here was copied into `data/solutions/` — see
> [closed item 4](../../docs/solver/history.md#4-the-campaign-that-decided-that---best-of-round-is-a-default--and-the-shot-rule-won-it).

## What the recovery adds to item 4's "not reproducible as a recipe"

Item 4 says the widths in the directory names were the only surviving clue. The **run logs** came
back with the solutions and they carry the budget as well, which is more than a directory name:

```
build/w/w8-2048.log   1 worker, 5400000 ms + 60000000 nodes, beam 600
                      SOLVED lv=8 keys=308 ratio=1.4  in 5m51s      -> level 8's 308
build/w/b9-b.log      1 worker, 7200000 ms + 150000000 nodes, beam 600
                      SOLVED lv=9 keys=114 ratio=1.9  in 19m02s     -> level 9's 114
```

Both are driver runs at `--lanes 1` (the round/trim/replan/polish summary is the driver's), and
`w8-2048` says its own width. So the residue is **`--push-beam 2048`, ~60M nodes, under six minutes**
for a route 27 keys better than the banked one — a cheap thing to attempt rather than an unrecorded
one. That is a candidate recipe, not the recipe: the per-level flags are still not recorded anywhere,
and the report rows (`build/w/*.jsonl`) do not carry them either — they record `keys`, `nodes`, `ms`,
`method`, `stop` and nothing about configuration, which is worth fixing at the source.

One loose end closed while checking: **`build/w/d8-w512w/LaserTank/00008.lpb` is byte-identical to the
banked `data/solutions/LaserTank/00008.lpb`**, so the banked level-8 route is attributable to that run
(400M nodes, beam 600, width 512) rather than to an unknown one.

## Why the three lists are believed to be the originals

`bench/bench-levels.txt`'s header says *"the report that named them is gone"*. The evidence that these
are it:

- **They predate the reconstruction.** mtimes 2026-09-05/06; the reconstruction was committed
  2026-09-07 in `21e223b "bench recompute"`.
- **The counts are exactly the documented 60 / 50 / 50.**
- **They are not copies of the reconstruction at any stage** — the overlaps are 3/60, 6/50 and 16/50.
- **The instrument was calibrated before it was used.** Read through the current solver binary,
  `bench/bench-levels.txt` returns exactly the mix its own header records (FERRY 30, DEMOLITION 9,
  GAUNTLET 8, SETUP 4, RIDE 4, SOKOBAN 3, WALLED 1, OPEN 1). The same binary on `bench-levels.txt`
  here returns GAUNTLET 18, FERRY 7 — *GAUNTLET-heavy and ferry-poor*, which is how the original was
  described and precisely what the reconstruction could not reproduce.
- **`ferry-levels.txt` reads its own rule straight back**: FERRY 48, SOKOBAN 2, 50 of 50 inside the
  stated population. `deep-levels.txt` contains 1488 and `ferry-levels.txt` contains 1581, the two
  anchor levels the docs name.

**One guess in the docs is retired by this.** `bench/bench-levels.txt`'s header supposes the original's
GAUNTLET label *"was a pre-fix read"*, layer 8's barrier fix having moved 164 rows out of GAUNTLET
corpus-wide. Read **today**, post-fix, this list is still GAUNTLET 18 against the reconstruction's 8,
and FERRY 7 against its 30. The difference between the two lists is the **population**, not the read.

**And one thing they do not do: they un-rebase nothing.** Every bench number quoted before session 25
was measured against these levels *by a solver that no longer exists*. Re-running them on today's
binary compares two code versions, not two lists. What is restored is the ability to run the original
populations at all, and to ask how much of a bench-1 delta was ever the levels.
**`bench/bench-levels.txt`, `deep-levels.txt` and `ferry-levels.txt` remain the lists current numbers
are quoted against; nothing in this directory replaces them.**

## Gate

```bash
python tools/verify_solutions.py bench/recovered     # 2/2, both engines, byte-identical
```
