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
else: it walks the collection in level order, runs every searcher at once, quadruples the budget each
round, and gives up on a level after `--max-round` — the iteration's, see *What is open* — or when you
press a key. Solutions are banked to `data/solutions/` only after the two-engine gate passes, and every
level it finishes gets a row in `data/reports/solutions.jsonl` whether it fell or not.

```bash
build/lasertank-solve.exe data/levels/Beginner-I.lvl          # one collection
bash tools/iteration.sh                                        # all of them, resumable
bash tools/iteration.sh status                                 # the table, free, no solver
```

`--lanes N` works N levels at once and the lanes share the `--jobs` slots; it defaults to `--jobs / 5`,
because a lane is ten searchers and several of them finish in milliseconds, so one lane cannot keep the
machine full. Press a lane's number to give up on the level it holds.

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
| `LaserTank.lvl` 1-10 | **1-5 and 7-9 fell to the driver before iterations existed; 6 and 10 never did.** The banked `.lpb` files were cleared when iteration 1 started, so what the corpus holds now is whatever that pass re-derives — the ledger, not this row, is where to read it. Level 9 is banked at **115 keys / 1.9x** — the driver's own unattended round-5 run with `--best-of-round`, twelve keys shorter than the hand-run recipe that preceded it and one key off the 114 that was lost with `build/w/` — **recovered and gated in session 48** as `bench/recovered/LaserTank/00009.lpb`, and **level 8 is now banked at 305 keys / 1.37x** — item 4's stage `acc` seeded the driver with that recovered 308 as an acceptance bar for `--beat-banked` and the ladder beat it, byte-identically in all four arms, so the 305 is **re-derived rather than restored** and thirty keys under the 335 it replaced ([closed item 4](docs/solver/history.md#4-the-campaign-that-decided-that---best-of-round-is-a-default--and-the-shot-rule-won-it)). **Level 10 now has a named candidate** (session 34): it is a GAUNTLET, so `--push-read` finds a barrier on **0 of 63,454 expansions** and the whole layer-6/7/8 stack is inert; the fire tier that diagnosis prescribed is built, is worth +9 on the GAUNTLET tail, and leaves the level unsolved at 400M and d=48. Not budget, not the closure, not width, not the read — a traced run reached **d=63 on 399M nodes in 26m53s** with `trunc=0` throughout, past the 53 board changes of the hand line. What was missing is a *gradient*, and the harvested goal board supplies one: the blog's line wins in 179 moves / 52 shots by **pushing six of the ten anti-tanks and destroying none**, minimum total push distance 30, and it names the cells they end on — so it separates the three root pushes `--analyze` offers and cannot rank. **Level 6 "Cascade" has no post** and gains nothing from the bank, and **session 45 closed item 5 on it, negative**: its 168-change line comes apart into **six phases of 18 to 34 board changes**, every one inside the horizon of 50 already measured there, and `--push-phases` commits to the *right* first board — the fill at (9,14), the same cell the human fills first — and then **cannot reach phase 2 at width 32 or 128, nor from the human's own board**. Four of the six phases are reachable and the two that are not are the *middle*, though they are shorter than the three at the end. So the line's length is not what defeats the level; `tools/phase_reach.py`'s trace says `best=137` flat for 237 depths, which is level 10's GAUNTLET signature — **a ranking that has stopped discriminating, in the middle of a Sokoban** |

### The two caps the driver lifts, and why the globals did not move

**Items 20 and 25 measured two depth constants and found two bounds; the answer is not the same in the
two halves of the tree, so it is not the same default.** `Auto.Uncap` lifts `--push-depth` to 100,000 on
the six push rungs and `--sg-depth` to 100,000 on the two subgoal rungs, and raises `--max-keys` to a
floor of 5,000 on all eight — the same 5,000 the three ferry rungs had been carrying by hand since
session 22, now in one place and as a *floor*, so a caller raising it is no longer quietly lowered by a
rung. A caller who names any of the three on argv gets the number they typed, and the lift is reported
in the `rung` column like any other setting.

**In a campaign the lift is a bill and the globals stay at 1,200 and 400.** The two items priced it at
**1.41x and 3.66x the nodes** for 1.7% and 1.6% of their own populations, and both handed the list
nothing — every level either of them gained was already inside item 2's 1,087. A node-capped pass pays
that and buys a rounding error.

**In the driver the capped version is broken rather than merely expensive, and that is the asymmetry.**
Only `push-dead-end` and `subgoal-dead-end` restart (`Push.cs:601`, `Restart.cs:137`): a rung that ends
on a *depth* returns with a live frontier and its budget unspent, so the next round hands it four times
the nodes it cannot spend and six more restarts that cannot fire. Eight of the ten rungs, on every later
round, for as long as the lane holds the level — which is the one consumer in this tree with unbounded
budget and a core per rung. The campaign's own numbers say the same thing from the other side: item 20's
median stop left **10M of 40M** unspent and item 25's left **47.2M of 50M**.

**The flag documentation both items called wrong is corrected** (`Program.cs`, `Search.cs`): neither is a
backstop, both carry their measurement, and both say the driver lifts them and a batch pass has to ask.
The rungs themselves are in [`docs/solver/driver.md`](docs/solver/driver.md).

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

**Iteration 1 of the autosolver is running over the whole 20,914-level corpus, and what comes next is
decided from its ledger.** The driver's configuration is versioned rather than passed — `Auto.Iteration`
and `Auto.IterationMaxRound` in `src/LaserTank.Solver/Auto.cs`, declared together so that changing the
settings without changing the number is an edit somebody has to make on purpose. **Iteration 1 is
`--max-round 2`**: three rounds ending at 6.4M nodes, 8.5M cumulative per rung, which is past where
[closed item 7](docs/solver/history.md#7-the-solved-vs-budget-curve--run-and-it-saturates-below-10m)'s
curve flattens — 1M / 10M / 50M bought **12 / 20 / 21 of 253**, so round 2 buys all but one level of
what round 5 would at a sixteenth of the nodes, and over 20,914 levels that is the difference between
weeks and a year.

```bash
bash tools/iteration.sh                 # the pass; rerun the same command to resume
bash tools/iteration.sh status          # the table, free, no solver
python tools/ledger.py data/reports/solutions.jsonl --stops    # why the unsolved fail
```

`data/reports/solutions.jsonl` is the state — append-only, one row per level with the last row winning,
carrying the per-rung detail of each level's last round — and `SOLUTIONS.md` is a view of it, rewritten
after every collection. **`--stops` is what picks iteration 2**: a rung ending on `push-dead-end`
everywhere is asking for width, one ending on `budget` everywhere is asking for a better ranking, and
item 7 has already priced the third answer. Bumping `Auto.Iteration` is the act of saying *this is a new
approach* — every level still unsolved is attempted again, every level already solved stays solved.

**Nothing new is proposed until that pass has a ledger to read.** One numbered item survives from the
list that preceded it, and it is parked:

| # | order | what it is | cost |
|---|---|---|---|
| **24** | parked | **Parent pointers instead of copied keystreams.** `Snapshot` copies the whole key prefix and `Restore` copies it back, which is why 76,800 wide was 1.1 GB. **The trigger is written down**: a run bounded by memory rather than nodes, which has happened once (level 9) and which items 14 and 19 between them argue against wanting again. It is also the one item that changes Core, so it is built and gated on the **game** machine before the solver machine trusts the binary | — |

**The rule the list is ordered by is *cheapest falsifier first, whatever kind of work it is*.** Second
key: **prefer work that produces a property or a level over work that produces a number** — twenty-five
hours of machine time once went into items that report percentages while the two levels in front of the
project did not move and one banked solution got worse. Item 6 is what that second key bought: three
sessions, and what came out was a decoded goal board for `LaserTank.lvl` 10 and a ranking key that
separates its root pushes, rather than a percentage.

**Everything else that was ever on this list has closed, and the measurements — including the negative
ones, which is most of them — are in
[*Closed items*](docs/solver/history.md#closed-items--the-measurements-including-the-negative-ones).**
That file is the record and this section is only what is open; items keep their numbers because these
files refer to them by number. Full recipes, costs and evidence for what remains:
[`docs/solver/next-actions.md`](docs/solver/next-actions.md).
