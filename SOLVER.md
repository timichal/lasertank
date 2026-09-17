# LaserTank solver — Phase 4

**Purpose:** the solver's entry point. The engine port and the fidelity gates are in
[`PROGRESS.md`](PROGRESS.md), with the file formats, the quirk hazards and the rest under
[`docs/game/`](docs/game/); read those first if the question is about the *game*.

**The goal is to solve every solvable level in the corpus — 20,914 of them, all with a non-zero
`.ghs` entry and therefore all known-solvable.** No public automated LaserTank solver does this.
The current honest number is **37.8% of a 4,185-level sample** (1,581 levels) — **11.8% (494) from the
shipped 150k chain, and the rest from the fourth pass**, three push arms at 40M nodes that cost 67 h 30 m
of wall clock and closed [item 2](docs/solver/history.md#2-the-fourth-pass-run-over-the-corpus--1087-levels-and-the-stride-ranked-the-wrong-second-arm)
on 2026-09-15. Quote whichever of the two the question is about and say which: the chain is what runs in
minutes, the 37.8% is what this tree can do when given days. **2,604 levels are still in the tail**, and
99.6% of them stop on `budget`.

Two secondary but real side effects: a solved level is a long, legal, *winning* path through the
engine — coverage a fuzzer cannot reach, because random play drowns the tank in twenty keys — and it
is a second differential test of the port. Every solution is replayed through the frozen C oracle
*and* the C# core and must win on both with byte-identical traces; **the fourth pass alone put 2,249
recordings through that gate in one run, and there has still never been a divergence.** The gate is
the write path: a solution that fails it is deleted rather than banked.

**The standing engine claim, re-checked every session:** ten layers in, `Engine.cs` still differs
from a literal transliteration by the word `partial` — on the class, and (Phase 5 step 3) on
`SoundPlay`, whose empty body moved to `Engine.Sound.cs` — and `Engine.Search.cs` has not changed
since layer 0. **If a solver change seems to need an engine change, stop and re-read.** `SoundPlay`'s
body is `SoundLog?.Add(sn)` with `SoundLog` null unless a driver opts in, which the solver never
does, so a search pays one null test per call and allocates nothing. The sound ids are a trace field
the oracle emits too (`--sound`), gated by `tools/sound_check.py`.

---

## Where things are

| file | what is in it |
|---|---|
| **this file** | the commands, the rules, current status, and what is open |
| [`docs/solver/layers.md`](docs/solver/layers.md) | layers 0-9 — what each is, what it measured, what it ships as. The design record |
| [`docs/solver/driver.md`](docs/solver/driver.md) | the interactive driver (rungs, rounds, lanes, the gate) and the post-solve passes |
| [`docs/solver/instruments.md`](docs/solver/instruments.md) | every flag that reports rather than searches, in cost order, plus the source map, the tools and the bench lists |
| [`docs/solver/next-actions.md`](docs/solver/next-actions.md) | the open items in full: recipes, costs, and the evidence behind each |
| [`docs/solver/history.md`](docs/solver/history.md) | closed items with their measurements, the session log, and the state of the tree |
| [`docs/solver/human-strategy.md`](docs/solver/human-strategy.md) | the blog's ten-post *Lyf Series* on how a strong human plays, read against the layers — what it confirms, what it adds, and the three measurements it produced |

---

## Start here

**Solve one level and watch it:**

```bash
build/lasertank-solve.exe --levels data/levels/Beginner-I.lvl --level 44 --verbose \
  --out build/try --nodes 400000
python tools/verify_solutions.py build/try      # both engines, byte-identical, or it did not happen
```

**Or use the interactive driver, which is how a human uses this thing.** A bare `.lvl` and nothing
else: it walks the collection in level order, runs every searcher at once (one per core), quadruples
the budget each round, and stays on a level until it falls or you press a key. Solutions are banked
to `data/solutions/` only after the two-engine gate passes.

```bash
build/lasertank-solve.exe data/levels/Beginner-I.lvl --from 1 --to 40 --lanes 4
```

`--lanes N` works N levels at once; the lanes share the `--jobs` slots, so raising `--lanes` fills
the machine without changing the core budget. Press a lane's number to give up on the level it holds.

**The whole shipped batch chain** — a layer-0 campaign, then three passes that each attack only what
the previous one failed. `STRIDE=5` gives the 4,185-level sample every number in these files is
quoted against; each pass over ~3,700 failures is 6-8 minutes at 14 jobs, and the campaign is the
longer half. `STRIDE=1` is the whole 20,914-level corpus and is hours.

```bash
STRIDE=5 NODES=150000 tools/campaign.sh solutions/l0 build/reports/l0.jsonl --no-macro
NODES=150000 tools/second_pass.sh build/reports/l0.jsonl  solutions/l34 build/reports/l3c.jsonl \
                     --no-ida --no-beam --subgoal --sg-eval coarse
NODES=150000 tools/second_pass.sh build/reports/l3c.jsonl solutions/l34 build/reports/l34.jsonl \
                     --no-ida --no-beam --subgoal --sg-eval learned
NODES=150000 tools/second_pass.sh build/reports/l34.jsonl solutions/l34 build/reports/l34pass4.jsonl \
                     --no-ida --no-beam --macro --macro-first
python tools/verify_solutions.py build/solutions/l0     # layer 0's own solutions
python tools/verify_solutions.py build/solutions/l34    # everything the three passes added
```

**Two things in that recipe are load-bearing and read like defaults.**

- **`NODES=150000` is not optional.** `second_pass.sh` defaults to **1M**, 6.7x the campaign budget,
  and every pass number in these files is measured at 150k. Session 25 ran the first pass at the
  default over the whole population by accident and it came back **147 of 3,787** against the table's
  44 — 3.3x the levels for 6.7x the nodes, which is a different budget rather than a better chain and
  is comparable with nothing here. (Kept as `build/reports/l3n-1m.jsonl`: the cheapest evidence on
  file that the chain is budget-bound rather than structure-bound, and an argument for
  *next actions* item 7.)
- **`--sg-eval coarse` on the middle pass is not a tuning knob** — it is the key that pass had been
  using since `4765ae9` without saying so. `Rank()` read `_eval != null` rather than its caller's
  flag, so a bare `--subgoal` ranked by the learned evaluation rounded to work units. Naming it made
  the *third* pass a different searcher for the first time, which is where **476 → 494** comes from.

`report_stats.py` reads any of those reports (`--diff` compares two layers); the composite is the
*union* of the four, which is why the two verify runs are separate. A level solved by an earlier pass
is skipped by a later one, so no `.lpb` is ever written twice.

**The fourth pass is the same idea with days instead of minutes, and it is where 494 becomes 1,581.**
Three push arms at 40M nodes, one at a time because each wants the whole machine, over every level the
chain fails. It is `tools/l5_pass.sh` and nothing else — resumable from its own reports, gated per arm,
and it ends on `arms_union.py`. **67 h 30 m of wall clock**, so do not start it on a machine that is
needed for anything with a deadline; the same command restarts it wherever it stopped.

```bash
bash tools/l5_pass.sh                   # all three arms in greedy order, ~67 h
bash tools/l5_pass.sh status            # the table, from any other shell, any time
python tools/chain_union.py build/reports/chain5.jsonl build/reports/chain.jsonl \
    build/reports/l5-l8fire.jsonl build/reports/l5-enables.jsonl build/reports/l5-layer7.jsonl
```

`chain5.jsonl` is the chain and the pass folded into one report — **1,581 of 4,185** — and it is what a
fifth pass has to be pointed at, the same way the fourth pass was pointed at `chain.jsonl`. The whole
run is [closed item 2](docs/solver/history.md#2-the-fourth-pass-run-over-the-corpus--1087-levels-and-the-stride-ranked-the-wrong-second-arm).

**A fifth pass is more arms, and not more nodes for the chain — that was measured on 2026-09-16 and
refused.** `tools/curve_pass.sh` curved the chain's four searchers at 1M, 10M and 50M over a 1-in-15
stride and solved **12 / 20 / 21 of 253**, of which **20 of 21 were already inside the fourth pass's
1,087**. Above ~10M the chain is not budget-bound at all — `l0`'s misses have the same median node count
at 10M and 50M, to the node — so the script is kept as the instrument for *is this searcher still
searching?* rather than as a way to buy levels. [Closed item 7](docs/solver/history.md#7-the-solved-vs-budget-curve--run-and-it-saturates-below-10m).

```bash
bash tools/curve_pass.sh price          # what a curve costs, measured, ~4 min
bash tools/curve_pass.sh                # three rungs over the stride, ~1 h 40 m
bash tools/curve_pass.sh status         # the table, from any other shell
```

---

## Eight rules the sessions each paid to learn

These are the reason the numbers in these files can be trusted.

- **Bench on the corpus, not on a filtered population.** `bench/bench-levels.txt` is levels layer 0
  failed. It flattered layers 1 and 2, overstated layer 3's budget scaling and *understated* layer 4.
  A bench picks parameters; only a campaign decides what ships. **And a bench over-reports
  *complementarity* as readily as strength** — `--push-eval none` measured +6 on the ferry union and
  +4 on the deep one, the signature of a strongly complementary arm, and over 255 corpus levels came
  back the weakest arm of six with one exclusive level. Two independent fifty-level lists agreeing is
  not a population.
- **And a *stride sample* is not a population either, for the same statistic.** This is item 2's finding
  and it is the sharper form of the rule above, because a 1-in-15 stride of the real corpus is exactly
  what a bench is not. It sized the fourth pass's union to within **7.8%** of the 3,691-level run — and
  then ranked two arms that are within **1%** of each other as **3.5x** apart, in the wrong order:
  `layer7` was the rehearsal's clear second at +14 against `enables`'s +4, and over the corpus the two
  swap by ten levels out of 1,087, with their exclusive counts inverting (15 / 11 / 4 → 173 / 91 / 101).
  Exclusivity is what a small sample destroys — two arms that overlap on 15 sampled levels are free to
  diverge across the other fourteen-fifteenths. **A stride prices a union; it does not order the arms
  inside it, and the decision it would have licensed here — drop the +4 arm — costs 101 levels, more
  than dropping the arm ranked above it.**
- **Govern by `--nodes`, never wall clock.** A node is one `Engine.ApplyKey`. Seconds are not
  reproducible on a machine that is also running the gates. (The first campaign was wall-clock
  budgeted and had to be thrown away.)
- **Instrument before theorising — and respect the instruments' cost order.** `--sg-trace` killed two
  of layer 2's designs and one of layer 3's; `tools/rankdump.py` decided whether layer 4 was worth
  building at all; `--push-line` found the bug that had been costing layer 5 its whole budget. A new
  layer should report a distribution before it reports a solved count. **An instrument that measures
  the wrong *moment* — or the wrong *quantity* — says the layer does nothing, and that has now
  happened three times**; the fix is the same every time: print the thing the layer moves. The cost
  order is the other half — a 27-minute traced level-10 run arrived at the diagnosis that
  `--analyze`, which runs no search and takes a second, prints in one word. **Run instrument 2 before
  instrument 4, always.**
- **A solo bench score is the wrong statistic for a portfolio member.** Every specialist since layer
  6 looks like a one-to-two-level loss alone and adds three to six levels to the *union* with the
  rung beside it, because the levels it fails are not the levels that rung fails. Measure unions.
- **A flag that gates nothing looks exactly like a feature that does nothing.** `--sg-eval` gated
  nothing for nine commits: `Rank()` asked `_eval != null` and `_eval` is built when *either* beam
  wants the model, so plain layer 3 and `--sg-eval learned` were one searcher. Session 25 measured
  them identical *to the node* — the right observation — attributed it to the divide in `Eval.Score`,
  which was also real and also a defect, and stopped there. **When a searcher and its control come
  out node-identical, at least one of them is not what its name says; check both ends before
  believing the feature is inert.** The cheap version of that check is layer 4's documented
  equivalence test, run against a control you have separately proved is a different searcher.
- **Before costing work as hand input, check whether the repo can derive it.** `bench/` exists because
  nothing re-derives a human's answer — but the corollary is that nothing should *ask* a human for an
  answer the tree already contains, and the two are easy to confuse because both end in a committed
  file. Item 6's goal-tile residue was costed at half a session of eyeballing a contact sheet on
  correct reasoning about what a start screenshot can and cannot show; the sprites that draw those
  states and the rules that assign their `PF` were both already committed, in `original/src/Game.BMP`
  and in `LTANK2.C`, and putting them together labels all 116 exactly. **What hid it is worth knowing
  on its own, because it will hide the next one too: the derivation was one wrong transform away from
  looking impossible.** A nearest-neighbour shrink of that sheet gets the palette *exactly* right and
  the pixels wrong, which reads as "different artwork" rather than "wrong scaler" — and the real answer
  was GDI's default `STRETCH_ANDSCANS`, which **ANDs** the rows a shrink eliminates. When a derivation
  is nearly right, the residue is a transform, not a different input. Two checks make this cheap:
  ask what committed input could produce the thing, and reconcile against a quantity you did not
  derive — here the start board's own `PF` at each cell, which agreed on all 95 first-pass sprites and
  then independently confirmed `KillAtank`'s four junk bitmaps.
- **Write the acceptance test before the build, because it can falsify the *specification* rather
  than the code.** Item 6 wrote its own key down a session ahead: *rank by cells-still-differing*. It
  also wrote down the test — `LaserTank.lvl` 10's three root pushes, which `--analyze` offers and cannot
  rank — and that test is the exact case the specified key cannot see, because a push vacates one cell
  and fills another and the count is 12 before and 12 after all three. One measurement, before any
  tuning, said the spec was flat; the key that works is the *assignment* (which object goes where), and
  it separates them 29 / 31 / 31. **A progress measure that counts what is still wrong is flat while it
  is being fixed; one that measures how far it is from right is not** — and that is the third time this
  shape has been paid for, after `WorkDistance` over a ferry and `--push-fire` over a gauntlet.
- **A penalty every board pays is not a penalty.** A weight that every successor of every held board
  incurs makes the beam's best score *rise* and steers nothing. Use a tier (an ordering that cannot
  refuse a state) instead. Same family as a heuristic returning 0 — the best score there is — for a
  board it has nothing to say about; that one has cost three separate bugs (buried flag, unmatched
  hole, unshieldable cell). It is also a design item and not only a bug class: `--push-fire` prices
  exposure as an addend inside `PushH`, and on a GAUNTLET — where every board is covered by ten
  anti-tanks — that is a penalty every board pays. The fix is a tier, and that is Layer 9.

---

## Status

**Layers 0-4 ship in the batch chain. Layers 5-9 ship as rungs of the interactive driver — and, since
2026-09-15, as the batch fourth pass that took the corpus from 494 to 1,581.**

| | state |
|---|---|
| Layers 0-4, the chain | **494 of the 4,185-level stride sample (11.8%)**, with layer 4's two ranking defects fixed and the chain's middle pass carrying `--sg-eval coarse`. A **strict superset** of the earlier 476 and of every intermediate configuration measured; 96 of 96 new solutions through the gate |
| Layer 5 — push macros | board-change search. Ferry bench **18/50**, deep **25/50** (9 and 17 in the session-17 configuration) |
| Layer 6 — the read | derives what is in the way and what can change it. Ships inside layer 5's rung (15/50 against 9/50 without it). Its anti-tank-on-the-route rule is `--read-antitank-wall`, **off by default** |
| Layer 6's fourth derivation | *what does this change make possible?* — `--push-enables`; own rung, adds 4 ferry / 5 deep, **solves `LaserTank.lvl` 1 in 67 s with no flags** |
| Layer 7 — the stop cell | *what must be blocked before the tank can stand next to the flag?* — own rung with `--push-shot-run`, adds 3 ferry / 5 deep, **solves level 2 in 65 s with no flags** |
| Layer 8 — reading the board | six derivations (fire map, safe flood, frozen block, ferry assignment, ferry maze, shield). **Two rungs**, adding 3+1 ferry / 6+2 deep; **solves level 8** (57.5M nodes, width 512) |
| Layer 9 — exposure as a tier | `--push-fire-tier`, the fire map promoted from an addend to a tier; **tenth rung**. Over the 138 unsolved short-record GAUNTLETs at 40M it is **85 against the control's 76, with 15 exclusive levels against 6** (union 91 of 138, 65.9%; 161 of 161 gated). Costs **9% wall clock**. **Does not solve `LaserTank.lvl` 10**, the level it was derived from. **Now validated over the corpus** (session 33): as the fourth-pass arm `l8fire` it is **66 of 255 (25.9%)**, the best solo arm ever measured on that population, and it **retires `l8work`** — see the row below |
| Item 16's last two derivations (session 43) | **FMO mobility ships as a level column and the per-carry constant does not ship at all.** `Heuristic.Mobility` is `_alive`'s transitive closure — `--analyze-tsv`'s `alive` / `mob_max` / `mob_sum`, free at 63 s over the stride sample — and it predicts the solve rate **with the sign the series does not have**: frozen 16.0%, 1-4 cells 11.5%, 9+ **4.5%**, holding at **1.78x** with the record's own length, the water count and the block count fixed, where `poses` collapses to 1.17x. A free block is a player's resource and a beam's branching factor. The per-carry constant is **refused**: ten values of K over the 20 recordings move **no** level's ascent, and three levels' deepest rise gets worse |
| The read's derivations, scored against the human | Over the 20 hand recordings' **800 board changes**, each derivation as *named / offered* against *named / what the human did*: `advance` 58.5% → 83.4% (1.43x), `opens` 56.2% → 78.9% (1.40x), `enables` 68.7% → 92.1% (1.34x), and session 42's two: **`rare` 5.1% → 16.0% (3.15x)** and **`spend` 7.6% → 1.9% (0.25x**, i.e. the human avoids it — 0.3% and 0.04x with the anti-tank kill exempted, which is the form it would ship in). `clears`, the fifth derivation the spend column turned up by failing, is **0.88x and does not ship** |
| Beam width, per level (item 14, closed negative) | **The record does not size the beam.** `--push-width-record F` scales the push beam by `budget / (poses x .ghs shots x F)`, raise-only, and over the 138-level GAUNTLET tail at 40M it is **76 / 78 / 78 at F = 4.6 / 14 / 60 against the global width's 85**. The widths it chose ran to a median of 854, 2,667 and 6,820 against 128, so what was measured is *wider*, and wider loses — `Challenge-IV` 176 falls to the control in 781,566 nodes and costs the two widest arms 35.7M and 31.6M. Ships **off**. The free calibration behind it stands and is the reason not to try again: `nodes / (poses x width x ghs_shots)` over 66 solved levels is p10 1.9 / p50 **14.2** / p90 446, so the arithmetic sizes an order of magnitude and never a width |
| Layer 5 over the corpus | **15 of 255 (5.9%)** of the levels the whole chain fails, at 27x the campaign budget — an argument for a fourth pass, not for changing the chain |
| The fourth pass, **run** (item 2, closed 2026-09-15) | **1,087 of 3,691 (29.5%)** of the levels the chain fails, as the union of **three push arms at 40M nodes over the whole failure population** — `l8fire` 805 solo / 173 exclusive, `enables` 718 / 101, `layer7` 726 / 91. **2,249 of 2,249 gated, zero divergences**; 67 h 30 m wall, 879 h of job time at 13.0x, six calendar days and **seven restarts**. **Composite 494 → 1,581 of 4,185 (37.8%)**, and `build/reports/chain5.jsonl` is that union. Two firsts: **`Hard` falls, 5 of 257** (the tier was 0 of 257 in the chain, and the rehearsal's sample held 21 Hard-and-Deadly levels and solved none of them in any arm), and **8 solutions come in under the `.ghs` record**. The ceiling did not move — of the 2,604 nothing solved, 99.6% stop on `budget` and **five** are structural in all three arms |
| The budget curve (item 7, closed 2026-09-16) | **Raising the chain's own budget is a subset of the fourth pass, not a cheaper one.** Three rungs at **1M / 10M / 50M** over item 2's 1-in-15 stride: **12 / 20 / 21 of 253** (4.7% / 7.9% / 8.3%), strictly nested, **21 of 21 gated**, 1 h 39 m wall and 11 h 09 m of job time against a priced 45 h. **20 of the 21 are already inside the 1,087**, so the whole curve's marginal contribution over the pass is **one level** (`Challenge-IV` 736, solved at the *cheapest* rung in 3.8 s), and **no solution in the run needed more than 13.2M nodes**. Above ~10M the chain has stopped being budget-bound: `l0`'s misses have the same median node count at 10M and 50M (**1,323,180**, to the node) and **0 of 243 reach the cap at 50M**; `l3`/`l4` stop on **`subgoal-depth` 156 and 163 times** against 17 and 12 on budget, with a median **94% of the budget unspent**; `l1`'s macro beam solves **0** at every rung. **150k stays the attribution budget and nothing about the chain changes** |
| What the rehearsal got right, and what it could not do | The 1-in-15 stride **priced** the pass — 84 of 255 was written up as ~1,172 union and ~40% composite against the real 1,087 and 37.8%, **7.8% and 5.6% optimistic on a 67-hour run** — and then **put arms 2 and 3 in the wrong order and 3.5x apart when they are within 1%**. Greedy on the stride: `l8fire` +66 → `layer7` +14 → `enables` +4. On the corpus, in that same order, `layer7` adds **+181** and `enables` **+101**; greedily, `enables` adds **+191** and `layer7` **+91** — the union is 1,087 either way and ten levels decide the pick. Their exclusive counts invert (**15 / 11 / 4** against **173 / 91 / 101**), and **dropping the arm the stride wrote off at +4 costs 101 levels, more than dropping the one ranked above it (91)**. So *compare unions* gets a limit clause: **a stride sizes a union; it cannot order the arms inside it, and it must not be used to drop one** |
| `LaserTank.lvl` 1-10 | **1-5 and 7-9 solved**, banked in `data/solutions/`; **6 and 10 open**. Level 9 is banked at **115 keys / 1.9x** — the driver's own unattended round-5 run with `--best-of-round`, twelve keys shorter than the hand-run recipe that preceded it and one key off the 114 that was lost with `build/w/` — **recovered and gated in session 48** as `bench/recovered/LaserTank/00009.lpb`, and **level 8 is now banked at 305 keys / 1.37x** — item 4's stage `acc` seeded the driver with that recovered 308 as an acceptance bar for `--beat-banked` and the ladder beat it, byte-identically in all four arms, so the 305 is **re-derived rather than restored** and thirty keys under the 335 it replaced ([closed item 4](docs/solver/history.md#4-the-campaign-that-decided-that---best-of-round-is-a-default--and-the-shot-rule-won-it)). **Level 10 now has a named candidate** (session 34): it is a GAUNTLET, so `--push-read` finds a barrier on **0 of 63,454 expansions** and the whole layer-6/7/8 stack is inert; the fire tier that diagnosis prescribed is built, is worth +9 on the GAUNTLET tail, and leaves the level unsolved at 400M and d=48. Not budget, not the closure, not width, not the read — a traced run reached **d=63 on 399M nodes in 26m53s** with `trunc=0` throughout, past the 53 board changes of the hand line. What was missing is a *gradient*, and the harvested goal board supplies one: the blog's line wins in 179 moves / 52 shots by **pushing six of the ten anti-tanks and destroying none**, minimum total push distance 30, and it names the cells they end on — so it separates the three root pushes `--analyze` offers and cannot rank. **Level 6 "Cascade" has no post** and gains nothing from the bank, and **session 45 closed item 5 on it, negative**: its 168-change line comes apart into **six phases of 18 to 34 board changes**, every one inside the horizon of 50 already measured there, and `--push-phases` commits to the *right* first board — the fill at (9,14), the same cell the human fills first — and then **cannot reach phase 2 at width 32 or 128, nor from the human's own board**. Four of the six phases are reachable and the two that are not are the *middle*, though they are shorter than the three at the end. So the line's length is not what defeats the level; `tools/phase_reach.py`'s trace says `best=137` flat for 237 depths, which is level 10's GAUNTLET signature — **a ranking that has stopped discriminating, in the middle of a Sokoban** |

**A missing `.lpb` under `data/solutions/` is not a missing solution.** Michal deletes a banked
`.lpb` on purpose in order to re-run the solver by hand and watch the replay, and re-banks it
afterwards. That is the normal working loop, not a lost result — so a level named as banked here may
be absent from the directory at any moment, and the way to check whether it is solved is this file
plus `build/`, never a directory listing.

*One number moved for a reason worth knowing before trusting the rest: the ferry bench is 19/50 where
earlier sessions banked 20/50. That is the buried-flag fix, attributed rather than assumed —
reverting it alone reproduces 20/50 exactly, so everything else since is inert at default flags, and
the level it costs never buries a flag on its own line. A correct ranking that costs one level is
still the correct ranking. That list was then lost and the regenerated one gives **18**, so 18 is the
number to compare against from here; the two are not the same fifty levels and the difference is not
a code change.*

**The shipped chain per tier** (session 27, `coarse` → `learned`, `build/reports/fix2-chain.jsonl`):

| tier | levels | solved | rate | median ratio |
|---|---:|---:|---:|---:|
| Kids | 960 | **374** | 39.0% | 1.5× |
| Easy | 2,118 | **110** | 5.2% | 1.6× |
| Medium | 784 | **9** | 1.1% | 1.5× |
| Hard | 257 | 0 | — | — |
| Deadly | 56 | 1 | 1.8% | — |
| **all** | **4,185** | **494** | **11.8%** | **1.5×** |

**The same tiers after the fourth pass** (item 2, `build/reports/chain5.jsonl`) — the chain's shape
stretched rather than a new one, except in the row that had never moved at all:

| tier | levels | chain | + the fourth pass | rate | median ratio |
|---|---:|---:|---:|---:|---:|
| Kids | 960 | 374 | **785** | 81.8% | 1.5× |
| Easy | 2,118 | 110 | **705** | 33.3% | 1.7× |
| Medium | 784 | 9 | **85** | 10.8% | 1.8× |
| Hard | 257 | 0 | **5** | 1.9% | 1.3× |
| Deadly | 56 | 1 | **1** | 1.8% | — |
| **all** | **4,185** | **494** | **1,581** | **37.8%** | **1.6×** |

**`Hard` is the row worth reading twice.** It was 0 of 257 in the chain, and the rehearsal's 1-in-15
sample held **21 Hard-and-Deadly levels and solved none of them in any of seven arms**, which is what
licensed the sentence *this does not touch the two tiers that have never fallen*. Over the whole
population it is 5 — four in `Challenge-I` (306 *Robinson Crusoë* at 91
keys / 1.30×, 1346 *Fort Knox II*, 1101 *Mr. Ping-Pong VI*, 1596 *It's a Snap*) and `LaserTank` 901
*fission* at 66 keys / 1.32×. Two of the five are `l8fire`'s alone. **`Deadly` did not move**: 1 of 56, the
level the chain already had.

**How the 494 is made, and why all three rows below it are real.** Same 150k budget, same
`build/reports/l0.jsonl` throughout — layer 0 never touches `Rank()`, so only the two middle passes
move. These are three *different searchers*, not one searcher measured three times:

| chain | layer 0 | + pass 2 | + pass 3 | + L1 pass | composite |
|---|---:|---:|---:|---:|---:|
| the four passes as banked (pre-`4765ae9`) | 395 | +44 | +30 | +3 | **472 (11.3%)** |
| `--subgoal` then `--sg-eval learned` (session 25) | **398** | **+73** | **+0** | +5 | **476 (11.4%)** |
| `work` → `learned` | 398 | **+44** | **+39** | +3 | **484 (11.6%)** |
| **`coarse` → `learned` — shipped** | 398 | **+73** | **+20** | +3 | **494 (11.8%)** |

Two things fall out of it that are worth more than the composite. **The accidental key is the best of
the three** — 73 against `WorkDistance`'s 44 over the same 3,787 failures, 33 levels only it solves
against 4; it ships as `--sg-eval coarse`. And **`WorkDistance` is now dominated outright**: a
`--sg-eval work` pass appended after `coarse` → `learned` adds **0**, every level it can reach being
already in the union. Layer 2's own key survives as the acceptance test's `work` input and as
`coarse`'s largest term; as a *ranking* for this pass it is retired.

**The unsolved are unsolved on budget, not on structure:** 3,636 of layer 0's 3,790 failures stopped
at `budget` (95.9%), only 154 at a beam dead end. No errors, no `NOTPORTED`, no crashes in 8,370
level-solves. `Hard` at 0/257 is not the search failing to find a route, it is the search never
getting near the end of one. **Depth is the binding constraint** and every layer since has been an
attack on it.

**267x the budget did not change that sentence, which is the strongest thing the fourth pass measured.**
At 40M nodes on three different push arms, of the **2,604** levels none of them solved, 99.6% still stop
on `budget`, no solved level reached the node cap (max 39.98M of 40M), and **five levels — five — are
structural in all three arms at once.** The two sweeps that could have shown a ceiling instead showed a
price list, so the honest reading of 37.8% is *this is what 67 hours buys*, not *this is what the tree
can do*.

**On the chain, the same sentence came back as a measurement rather than an inference.** At 333x the
150k budget the chain's beam stops on `beam-dead-end` on **243 of 243** misses and its subgoal passes
stop on **`subgoal-depth`** — a constant, not a budget — with 94% of the nodes unspent. *Depth is the
binding constraint* is no longer a reading of where the stop reasons point; it is what two of the four
searchers literally stop on when nothing else is in their way. [Closed item 7](docs/solver/history.md#7-the-solved-vs-budget-curve--run-and-it-saturates-below-10m).

---

## What is open

**Three items — 22-24, the old *Further out* bullets written out on 2026-09-15.** None is new work; each
one carries a recipe, a cost and the measurement that would refuse it. The gate that section carried (*only after the numbers above have
moved*) was met when item 2's fourth pass ran and item 10 closed. **Nothing on this list costs hours any
more** — items 20 and 25, the last two that did, both ran on 2026-09-17 and closed, and item 21 closed
the same day in eight four-second bench runs. Numbers are kept
because these files refer to
items by number; the finished ones are in
[*Closed items*](docs/solver/history.md#closed-items--the-measurements-including-the-negative-ones).
Full recipes, costs and evidence: [`docs/solver/next-actions.md`](docs/solver/next-actions.md).

| # | order | what it is | cost |
|---|---|---|---|
| **22** | **1st** | **Subgoal chaining over board changes.** The acceptance test is already in `Subgoal.Offer`, and the first step is not a build: run the arithmetic against `--analyze` on levels 6 and 10 — how many subgoals, how deep each is on the hand line — because 20M nodes is the sizing and a deeper read makes it a number the driver does not have. **Closed item 5 is what it has to beat**: level 6's phases are short, the commitment is right, and the beam still cannot walk phase 2 from any board | **free** to refuse; a build to confirm |
| **23** | 2nd | **A FESS-shaped rung for the Sokoban/ferry half.** FERRY + SOKOBAN is 53% of the sample at 5.1% solved and level 6's diagnosis is the textbook failure of beam search on Sokoban. **It now has a free falsifier**: `--analyze-tsv`'s `blocks` and `region` columns already exist, so the feature space can be projected over `chain5.jsonl` in ~3 minutes, and a space that does not separate solved from unsolved is not one | **~3 min** to refuse; the largest build on the list to confirm |
| **24** | parked | **Parent pointers instead of copied keystreams.** `Snapshot` copies the whole key prefix and `Restore` copies it back, which is why 76,800 wide was 1.1 GB. **The trigger is written down**: a run bounded by memory rather than nodes, which has happened once (level 9) and which items 14 and 19 between them argue against wanting again. It is also the one item that changes Core, so it is built and gated on the **game** machine before the solver machine trusts the binary | — |

**Item 21 closed on 2026-09-17 in under a minute of machine time, and the answer is the population.** Over
each list's own layer-0 remainder — which makes both *levels this binary's layer 0 misses* — the chain's
three searchers score **11 of 60 on the committed reconstruction against 17 of 41 on the recovered
original**, Fisher two-sided **p = 0.014**; only 3 levels are common to the two lists. **Two thirds of
that gap is the read**: the reconstruction's remainder is **FERRY 30 of 60** where the original's is
**GAUNTLET 14 of 41**, FERRY is the worst verdict the arms face (**6 of 37**) and GAUNTLET among the best
(**8 of 22**), and standardising by the pooled per-verdict rates closes **16 of the 23 points**. **Where
the code is what varies the two lists agree exactly** — `--no-ida` is +1 on both and the learned key −1
on both, on two disjoint populations. **The recipe's own first step was the wrong one and that is the
transferable half**: *stop if they separate at layer 0* would have stopped on **0 of 60 against 19 of
60**, and the 0 is forced — `bench/bench-levels.txt` was *cut* by the arm being measured
(`STRIDE=5 NODES=150000 --no-macro`, `tools/bench.sh`'s configuration to the flag), so **a list selected
by a failure cannot be asked about that failure**. The committed reconstruction stays the list current
numbers are quoted against and the pre-session-25 numbers stay uncorrectable. The held-out GAUNTLET use
refused itself for free: the chain leaves **8** of the original's 18 and **6** are disjoint from
`bench/gauntlet-tail.txt`, so the fire tier's +9 still rests on one population.
[Closed item 21](docs/solver/history.md#21-the-two-bench-1-lists-through-one-binary--they-separate-and-the-read-explains-two-thirds-of-it).

**Item 25 closed on 2026-09-17 beside its twin, positive on the flag and worth nothing to the corpus.**
`--sg-depth 400` carries the same *backstop* claim one searcher across, and against closed item 7's
banked 50M rung the uncapped arm scores **14 of 243 against 10 — a strict superset, 14 of 14 gated** —
with `subgoal-depth` **156 → 0** and the control stopping on `subgoal-depth` on **all four** levels
gained. It cost **3.66x the nodes**: 2 h 09 m of job time became 11 h 57 m and **161 of 243 now burn the
whole 50M** against 17 before. **Its marginal contribution to the corpus is zero and that is structural**
— its population is item 2's own 1-in-15 stride, so all 14 are already inside the fourth pass's 1,087 and
no result it could have produced would have moved the composite. It resolves the `--sg-slack` fork it set
itself, against slack: the cap was a real constraint, so the follow-up is not slack, and it is not budget
either. **With item 20 it makes one reading — two searchers, two constants nobody sized, 1.7% and 1.6% of
their own populations** — and what is left is a decision rather than a measurement: `PushDepth = 1200` and
`SgDepth = 400` are both now measured and both wrong as defaults.
[Closed item 25](docs/solver/history.md#25---sg-depth-was-a-bound-too--four-levels-at-two-and-a-half-times-item-20s-price).

**Item 20 closed on 2026-09-17, positive, and it is the smallest positive in these files.** The 177
`enables` rows that stopped on `push-depth` — a control of **0 of 177** by construction — were
re-attacked with the cap lifted from 1,200 board changes to 100,000, and **3 solved, 3 of 3 gated**:
`Challenge-II` 291 *Full insight*, `Gary-I` 1051 *SlipBridge-325*, `Sokoban-I` 991 *The marathon*. Two
are new, so **the composite is 1,583 of 4,185**. The cap is gone as a stop — **177 → 0**, 170 moving to
`budget` and 4 to `push-dead-end` — and **174 of 177 now reach the 40M node cap** against a median 30.1M
before, at **1.41x the nodes**. So `--push-depth`'s *backstop* documentation was wrong and the default
was a bound; what it was **not** is a wall, on 98% of the searches it was ending. **A default should be a
measurement** — `PushDepth`'s 1,200 never was, and neither is `SgDepth`'s 400, which is item 25.
The predicted bug report did not materialise: no level stopped on `push-depth` again at 100,000.
[Closed item 20](docs/solver/history.md#20---push-depth-was-the-wall-on-three-levels-and-a-symptom-on-a-hundred-and-seventy).

**Item 7 closed on 2026-09-16, negative, in 100 minutes, and it is the last of the fourth-pass list.**
The solved-vs-budget curve ran the chain's four searchers at 1M, 10M and 50M over item 2's own 1-in-15
stride and solved **12 / 20 / 21 of 253** — strictly nested, 21 of 21 gated, **1 h 39 m of wall clock and
11 h 09 m of job time against a priced 45 h**. The underrun is the finding rather than a lucky break:
the levels were not burning their budgets. **Above ~10M the chain has stopped being budget-bound** —
`l0`'s misses have the same median node count at 10M and 50M (1,323,180, to the node), **0 of 243 reach
the cap at 50M**, and no solution in the whole run needed more than **13.2M** nodes, which prices the
50M rung's 36.5 h at nothing. And the question the item was written to answer gets a clean no: **20 of
the 21 levels it buys are already inside item 2's 1,087**, so the curve's entire marginal contribution
over the fourth pass is **one level**, had at the *cheapest* rung in 3.8 s. **Raising the chain's budget
is not a cheaper fourth pass; it is a subset of one.** What it hands the list is item 25 — a second
depth constant documented as a backstop and measured binding — and what it hands item 19's still-open
width question is a caveat: **ask it below 40M**, because two searchers that stop on structure rather
than on nodes tie at every budget you can afford.
[Closed item 7](docs/solver/history.md#7-the-solved-vs-budget-curve--run-and-it-saturates-below-10m).

**Item 2 closed on 2026-09-15 and it is the largest single result in these files.** The fourth pass ran
its three arms over all 3,691 of the chain's failures at 40M nodes and came back with **1,087 (29.5%)**,
taking the composite to **1,581 of 4,185 (37.8%)**; 2,249 of 2,249 gated, 67 h 30 m of wall clock over six
calendar days and seven restarts, and `build/reports/chain5.jsonl` is what a fifth pass points at.
Its numbers are in the *Status* table above. **What it changed about this list is the rule, not the
order:** the rehearsal's 1-in-15 stride priced the run to within **7.8%** of its own published estimate
and then put arms 2 and 3 in the wrong order, 3.5x apart when they are within 1% — dropping the `enables`
it wrote off at *+4* would have cost **101 levels**, more than dropping the `layer7` ranked above it (91)
— so *compare unions* now carries a limit clause: **a stride sizes a union; it cannot order the arms
inside it, and it must not be used to drop one.** It also freed the
machine, which has been holding sixteen jobs since 2026-09-10, and unblocked `build/lasertank-solve.exe`,
which has since been republished with item 10's two flags in it.
[Closed item 2](docs/solver/history.md#2-the-fourth-pass-run-over-the-corpus--1087-levels-and-the-stride-ranked-the-wrong-second-arm).

**Item 10 closed on 2026-09-15, positive on its build and negative on the piece left over.** The item
asked why the shipped push rung runs at 166k nodes/s against layer 0's 1.4M, and it got its answer twice
— once wrong. `dotnet-trace` put **64% of a 6M-node run in the budget check**; a build with the clock
read deleted runs no faster and the call measures 21.8 ns, which prices that check at **0.4%**, so the
profile was an artefact of where a suspended thread can be walked. `--push-time`, the run's own
timestamps at 1-3% overhead, says the cost is **`PushH` at 41-48% of the expansion** against `ApplyKey`'s
15-20%. The fix is `--push-memo`, keyed on the **pose** and not the board the item had named — the
playfield, the tank's cell, and `PF2` only under `--push-stop`, whose `StopPrice` is the one thing in the
heuristic that reads beneath a block. A direct-mapped 4,096-slot table with a 128-bit content hash hits
**84.6-98.3%** and is worth **1.31x / 1.56x / 1.33x** on the three rungs, `IDENTICAL` on all three and
**on by default since session 51**. **The board-keyed layer underneath it is declined, not deferred**:
its key is free — `MemoProbe` already computes the board hash before mixing the tank cell in — but its
entry is **2.5 KB against the pose cell's 56 bytes**, and the pose table's own sizing measurement is that
918 KB a worker across sixteen workers costs more in shared cache than the 1.8 points it buys. That
prices the whole remaining headroom at **~1.08x**, for the expensive half of the build, in the one
dimension already shown not to pay. **`BUDGET_MS` is the trap in measuring any of it** — at a 4-second
budget the memo arm does not finish sooner, it searches 1.70x further, and the two arms then walk
different boards.
[Closed item 10](docs/solver/history.md#10-wall-clock-on-the-push-rungs--the-memo-shipped-and-the-layer-underneath-it-is-declined).

**Item 14 closed in session 46, negative on a clean sweep.** Three arms at the calibration's own
p25 / p50 / p75 over the 138-level GAUNTLET tail at 40M — **76 / 78 / 78 against a banked control's 85**,
and the widths they chose ran to a median of **854, 2,667 and 6,820 against the global 128**. The flag is
raise-only, so what the item measured is the beam getting *wider*, which is the direction its own
evidence argued against; the arms hold 4 / 1 / 1 exclusive levels and the union reaches 97 (70.3%), but
that is three extra 40M passes for 12 levels. `Challenge-IV` 176 is the whole result in one row: the
control solves it in **781,566 nodes** and the two widest arms need **35.7M and 31.6M**.
[Closed item 14](docs/solver/history.md#14-per-level-width-from-the-record--built-swept-and-beaten-by-the-global-width).

**Item 19 closed in session 47 and it is the first item here to close neither positive nor negative.**
It was item 14's successor — the same 138 levels, the same banked control, one arm in the direction
raise-only had forbidden — and `--push-beam 32` **tied at 84 against 85**, 84/84 gated, 100 min wall at
four jobs beside the running pass. **The tie is not the finding.** The item was written on four
measurements that all said a narrow beam should hold the **deep** levels, and its 8 exclusive levels
split **4 long / 4 short** about the population's median `ghs_shots` of 13 — so the record does not
predict which width a level wants, in *either* direction, and per-level width is now closed from both
sides. What is real is **cost**: 32 is cheaper on 46 of the 76 levels both solve, **1.03M nodes median
against 1.61M**, and four of its exclusive wins land at **1.8M-3.4M on levels the control burned 40M and
failed** — level 6's twelfth-of-the-budget result, seen off level 6 for the first time. Two arms that
tie at 40M and differ 1.6x in nodes do not tie at 4M, so **the follow-up is item 7's budget curve, not a
width item**, and the five banked reports price any rung of it without a new control. Five-arm union
**101 of 138 (73.2%)**; `tools/gtw_b32.sh` is the whole recipe.
[Closed item 19](docs/solver/history.md#19-the-narrow-beam--run-on-a-population-at-last-and-it-ties).

**Item 5 closed in session 45 and it closed negative on its own example**, which is the second running
that the item at the front of this list has been refused by a falsifier it built for itself. What was
built is real and ships off by default: `--push-phases`, a phase **terminated by a milestone** — a
successor whose board holds strictly fewer consumable objects than the phase's root — rather than sized
by a constant, which item 18 had refuted in advance. `tools/phases.py` sized it for free and came back
positive: `LaserTank.lvl` 6's 168-change line is **six phases of 18, 26, 28, 30, 32 and 34 board
changes**, one per hole filled, the longest **0.68** of the horizon already measured there. Then
`tools/phase_reach.py` — seed at each milestone, ask whether the beam reaches the next — refused it:
**4 of 6 phases are reachable and the two that are not are the middle**, though phase 2 is *shorter*
than the three at the end that all land. It is not width — at a fixed 8M nodes a phase, width 32 reaches
4, 128 reaches 2 and 512 reaches 1, and phases 2 and 3 fail at all three — and **it is not the
commitment**: hand the chain the human's own board after the human's own first fill and it still
does not reach fill 2. So level 6's phases are short, the chain commits correctly, and the search cannot
walk the middle of the line — **what defeats the level is not the length of its line**, which is the
premise the item was written on.
[Closed item 5](docs/solver/history.md#5-level-6s-decomposition--built-and-refused-by-the-falsifier-it-set-itself).

**Two things it produced are worth more than the item was, and the first became item 19.** The same
table solves level 6 from **K = 102, 66 board changes from the end, at width 32 on 3.4M nodes**, where
item 18 measured its horizon of **50** at width 512 on 40M — deeper on a twelfth of the budget, so **the
horizon is width-dependent and 50 is not the searcher's best**. Item 19 took that to a population and it
**half survived**: width 32 does not solve more levels than 128, but the levels it does solve cost a
twelfth to two-thirds of the nodes. And phase 2's trace is `best=137` flat for 237 depths, the *same signature* as level
10's GAUNTLET: a beam ranking distinct boards by a key that has stopped discriminating. **What is
binding in the middle of a Sokoban is the ranking, not the depth** — layers 4 and 6's territory, not a
decomposition's, and not an item until something measures it on a population.

**The commit law is the engineering, and the two that were refused are why the flag is safe.** Ending a
phase at the depth that produced a milestone needs no constant, which is why it was tried, and it
**cost `LaserTank.lvl` 20 — a level the plain beam solves** — because a demolition level consumes
something on nearly every board change (level 3's census starts at 77) and committing at the first
milestone turns the beam into a greedy one-step hill-climb. Layer 1's founding finding, restated. What
ships instead commits only where the search would otherwise give up, and over levels 3, 4, 7, 11 and 20
the flag on is **node-identical and keystream-identical** to the flag off.

**Items 17 and 18 closed in session 44.**
Item 17 is the cleanest negative these files hold (below); item 18 is the measurement item 5 has been
**Items 16, 15 and 13 closed in session 43** and they are the clearest run the ordering rule has had:
three falsifiers costing minutes each, spent beside item 2's machine time, coming back as **one column
that ships with the sign inverted** (FMO mobility predicts the rate — freer blocks are *harder*, 1.78x
with the record's own length held fixed, where the openness control gives 1.17x), **one derivation
refused** (a per-carry constant in `MatchFerry` changes no level's ascent, because the ascent is inside a
carry and the drop at a fill is already in the term), **one horizon measured** (`--push-seed`: level 6
finishes from 48 board changes out, not from 51, monotone below that -- item 18 has since narrowed it to 50) and **one free report column** (the
shot test, reproducing 67 / 1.85x against 298 / 1.41x from any report). Their measurements are in
[closed items 13, 15 and 16](docs/solver/history.md#closed-items--the-measurements-including-the-negative-ones).

**The rule they are ordered by is *cheapest falsifier first, whatever kind of work it is*.**
**Second key: prefer work that produces a property or a level over work that produces a number** —
twenty-five hours of machine time once went into items that report percentages while the two levels
in front of the project did not move and one banked solution got worse. Item 6 is what that second key
bought: three sessions, and what came out was a decoded goal board for `LaserTank.lvl` 10 and a ranking
key that separates its root pushes, rather than a percentage.

**Item 17 closed in session 44 and it is the cleanest negative this list has produced.** Session 42's
falsifier had qualified it at **3.15x** — the largest lift the read has ever measured, and by the rule
that placed `TierEnables` the most selective *and* the most accurate of four derivations, so what was
left was ~40 lines and four benches rather than a question. The benches refused it: **ferry 18 → 16 with
zero exclusive levels, deep 25 → 24**, and at `--read-rare 1` it is inert (the same eighteen ferry
levels, not merely the same count). **The 3.15x is not wrong — it was a property of the twenty hand
recordings**, and `--push-trace`'s new `rare` column says so: on four of five ferry levels probed the
tier names **0%** of successors, because the census is of the authored board and a ferry level is
authored with three or more of everything a ferry push touches. It is layer 4's result from the other
side — the sort is not where the wins are — and the item's own prior said as much. The one number that
looks like an opportunity, a deep union of **28** against 25, is the exact reading the first of the eight
rules forbids: `--push-eval none` measured a bigger union on these two lists and came back the weakest
arm of six over 255 corpus levels. `--push-rare` ships off by default so the table can be re-run;
the whole record is
[closed item 17](docs/solver/history.md#17-the-rarity-tier--built-and-refused-on-the-bench-it-set-itself).
**Items 13-16 were new in session 37 and they all came from one source**, which
is the second key paying out again: the harvest blog's ten-post *Lyf Series* on how a strong human plays,
read against the layers in [`docs/solver/human-strategy.md`](docs/solver/human-strategy.md). All three of
the ones that were questions have now been answered (session 43) and **the way they answered is the
argument for reading prose against a codebase**: of the six derivations the reading proposed, two shipped
as measured distributions, two were refused on their own falsifiers, one became a report column and one
became item 17 — and that last one has now closed too, refused as a tier while its distribution stands.
Reading it produced three measurements before it produced any proposal — the record's shot count **is** layer 5's search depth
(p50 1.00 over the 20 hand recordings, exact on level 6's 168), **66% of the solver's 452 solved rows
already use the record's exact shot count** while the median keystream is 1.47x, and a win above the
record's shot count is a measurably worse route. That last one reproduces the series' own first rule on
our data, and it re-reads the whole `ratio` column: **1.5x is mostly the right plan driven badly, not
half a solution.** All four of them landed ahead of item 5 on the ordering rule and all four have now
closed, one of them (15) being the cheap falsifier item 5 had been open without — and item 5 has since
closed too, on that falsifier's descendant. **No open item now names a level**: 6 and 10 are both open
levels with the same diagnosis and no named candidate, which is a truer statement of where the project
is than a list with a level-shaped item on it.

**The chain then ran end to end, clean, and the bank is done** — `python tools/harvest.py complete`,
one funnel and one table of every post the decode did not finish on its own. `bench/goal-boards.json`
carries **6,030 levels / 7,622 goal boards** decoded from the whole 6,218-post index with **no unknown
cell on any banked board** (4 filled from a post fixup, 1 tank likewise, 2 boards read against a `.ltg`
graphics pack), 0 codebook/derivation clashes and **nothing waiting on a human**: the 38 remaining
rows are all read and all intentional — 5 occluded cells stated in `bench/post-fixups.json`, 9 posts
whose filenames name another level, 11 levels re-authored since their post, 8 posts with no start
screenshot, 5 start frames that are play states. Getting there took four sessions of *the instrument was
wrong, not the blog*, and the last pass is the cleanest statement of it: **which graphics a screenshot is
of is a property of the screenshot** — the zoom is in the board frame, the pack is in the tiles — and
four readers assumed it independently. Two good start boards were called `NOT A START` and nine tile
labels were requested that were all already answered, one of the four gates reporting 48 of 106 codebook
entries "not derived" where the honest number is 3. Each symptom looked like a finding about the blog.
The sessions in order:
[38](docs/solver/history.md#session-38--the-corpus-scale-run-and-what-its-two-complaints-were) (the
corpus-scale run: 46 "conflicting cells" that were re-authored levels, and two `NOFRAME`s that were
Blogger serving a downscale),
[39](docs/solver/history.md#session-39--the-logs-drops-and-the-two-that-were-the-instruments-fault) (the
drops: the sprite pitch is *measured* off the frame, the shipped `.ltg` packs are decodable, the mid-blit
capture derives like any other composite, and an information-free cell is stated by hand rather than
inferred from a scratch recording),
[40](docs/solver/history.md#session-40--the-whole-chain-ran-clean-and-its-two-complaints-were-one-repair-and-one-instrument-bug)
(one repair, one instrument bug, and a gate that had never run), and
[41](docs/solver/history.md#session-41--the-filenames-the-harvester-could-not-read-and-the-four-readers-that-assumed-the-graphics)
(the filenames, the 21 posts naming another level, and the four readers above).

Nothing on the list is blocked on any of it and three items inherit from it — `--goal-board`'s
hint-assisted bootstrap becomes a corpus-scale supply of real recordings on long levels, which is the
off-distribution sample layer 4 is fit on and `--profile` / `basin.py`'s only input; the per-flag boards
are a subgoal sequence for item 22's chaining; and every falsifier items 15-17 were measured
on reads only the 20 hand recordings. **What it does not supply is human routes** — a goal board names the
destination, not the path.

**Item 6 took three sessions and closed in session 36; the whole record is
[closed item 6](docs/solver/history.md#6-the-blogspot-goal-board-harvester-and-the-goal-board-as-a-ranking-key).**
Session 33 moved it to the top on a ~2 h spike, 34 ran the spike and shipped `tools/harvest.py`, 35
derived the goal-only tiles instead of labelling them, and 36 built `--goal-board`. What it leaves:
`LaserTank.lvl` 10's goal board decoded with **no undecoded cells**, the tank at (6,0) facing right, the
post's own 179 moves / 52 shots against the `.ghs` record's 124/55 — and a ranking key that **separates
the three root pushes `--analyze` offers and cannot rank** (30 → 29 for the one that helps, 31 for the
two that do not), which is the acceptance test that was written before the build. Over the population it
is **15 → 21 of the 141 levels the bank covers**, +9/−3, all 21 through the two-engine gate. Every one of
those is **hint-assisted and outside the solver's rate**, which is enforced in code rather than by
convention: the flag moves the output directory and stamps every report row. Level 6 "Cascade" has **no
post at all** — checked against the whole 6,218-post index — so closed item 5 owed it nothing.

**Session 35 deleted item 6's remaining half-session instead of spending it, and the way it did is
worth more than the hours.** The goal-only sprites were costed as hand input on sound reasoning — a start
screenshot only ever shows authored states, so nothing labels the states play produces. But the 2010
binary's own graphics are **committed in this tree** (`original/src/Game.BMP`, `Mask.BMP`), and
`UpDateSprite` is thirty lines of `LTANK2.C`, so compositing the two labels every such state: **116 of
116 residual sprites, 0 unknown tiles of 44,288 over 173 goal boards.** What hid it for a session is that
`GFXInit` shrinks the 320x192 sheet to 24 px cells under GDI's *default* stretch mode, which **ANDs** the
eliminated rows and columns rather than dropping them — so a nearest-neighbour shrink reproduces the
palette exactly and the pixels not at all, and the sheet reads as different artwork. **The rule this
inverts:** `bench/` exists because nothing re-derives a human's answer, and the corollary is that nothing
should *ask* a human for an answer the repo can already derive. Checking that cost half an hour and the
inputs were sitting in `original/src/`.

**Item 2 held the machine on and off from 2026-09-10 to 2026-09-15 — 67 h 30 m of wall clock across
seven restarts — and nine items closed while it did, which is the habit to keep.** Its arms were
node-governed, so extra load moved wall-clock readings and nothing else —
sessions 42 and 43 spent the whole of items 16, 15 and 13 beside it, and session 44 spent all of item 17
(a build and five benches, four jobs against the pass's sixteen, ~3.5 min a bench) the same way; the pass
saw none of it except in wall clock. Session 44 then spent item 18 the same way — 96
`--push-seed` probes at four jobs, resumable from their own reports, and session 45 spent all of item 5
beside it — two free sizing tools and a build. **Session 46 broke the pattern once and it is worth
naming**: item 14's third arm was run with the pass *stopped*, so its wall clock is the only figure in
these files measured on an idle machine, and four hours of arm 1 are outstanding that were not spent.
No measured number moves — the arms are node-governed — and **session 47 put the pattern back**: item 19
ran its whole arm beside the restarted pass, four jobs against its sixteen on twenty cores, and the one
hazard that pairing actually carries was checked rather than assumed. `BUDGET_MS` is **wall clock, not
nodes**, so contention can truncate a level and silently spoil an arm; it did not come close — every one
of the 54 failures stopped on `budget` and the worst level took **501 s of 1,800 s**, in line with the
542 s and 429 s the two item-14 arms that ran beside the pass had already recorded. **With item 2 closed
nothing on this list needs the machine to itself**, and item 7 can have all sixteen jobs while items
20-23 run beside it —
but the rule the pass proved is the one to reach for next time it does: **a node-governed pass costs a
cheap item its wall clock and none of its numbers.**
