# LaserTank solver — Phase 4

**Purpose:** the solver's entry point. The engine port, the fidelity gates, the file formats and the
quirk hazards are in [`PROGRESS.md`](PROGRESS.md); read that first if the question is about the
*game*.

**The goal is to solve every solvable level in the corpus — 20,914 of them, all with a non-zero
`.ghs` entry and therefore all known-solvable.** No public automated LaserTank solver does this.
The current honest number is **11.8% of a 4,185-level sample** (494 levels), and all of the
interesting work is in the 3,691-level tail.

Two secondary but real side effects: a solved level is a long, legal, *winning* path through the
engine — coverage a fuzzer cannot reach, because random play drowns the tank in twenty keys — and it
is a second differential test of the port. Every solution is replayed through the frozen C oracle
*and* the C# core and must win on both with byte-identical traces; nine hundred-odd solver
recordings have gone through that gate with zero divergences. The gate is the write path: a solution
that fails it is deleted rather than banked.

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

---

## Eight rules the sessions each paid to learn

These are the reason the numbers in these files can be trusted.

- **Bench on the corpus, not on a filtered population.** `bench/bench-levels.txt` is levels layer 0
  failed. It flattered layers 1 and 2, overstated layer 3's budget scaling and *understated* layer 4.
  A bench picks parameters; only a campaign decides what ships. **And a bench over-reports
  *complementarity* as readily as strength** — `--push-eval none` measured +6 on the ferry union and
  +4 on the deep one, the signature that makes layer 7 the best arm of the fourth pass, and over 255
  corpus levels came back the weakest arm of six with one exclusive level. Two independent
  fifty-level lists agreeing is not a population.
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

**Layers 0-4 ship in the batch chain. Layers 5-9 ship as rungs of the interactive driver.**

| | state |
|---|---|
| Layers 0-4, the chain | **494 of the 4,185-level stride sample (11.8%)**, with layer 4's two ranking defects fixed and the chain's middle pass carrying `--sg-eval coarse`. A **strict superset** of the earlier 476 and of every intermediate configuration measured; 96 of 96 new solutions through the gate |
| Layer 5 — push macros | board-change search. Ferry bench **18/50**, deep **25/50** (9 and 17 in the session-17 configuration) |
| Layer 6 — the read | derives what is in the way and what can change it. Ships inside layer 5's rung (15/50 against 9/50 without it). Its anti-tank-on-the-route rule is `--read-antitank-wall`, **off by default** |
| Layer 6's fourth derivation | *what does this change make possible?* — `--push-enables`; own rung, adds 4 ferry / 5 deep, **solves `LaserTank.lvl` 1 in 67 s with no flags** |
| Layer 7 — the stop cell | *what must be blocked before the tank can stand next to the flag?* — own rung with `--push-shot-run`, adds 3 ferry / 5 deep, **solves level 2 in 65 s with no flags** |
| Layer 8 — reading the board | six derivations (fire map, safe flood, frozen block, ferry assignment, ferry maze, shield). **Two rungs**, adding 3+1 ferry / 6+2 deep; **solves level 8** (57.5M nodes, width 512) |
| Layer 9 — exposure as a tier | `--push-fire-tier`, the fire map promoted from an addend to a tier; **tenth rung**. Over the 138 unsolved short-record GAUNTLETs at 40M it is **85 against the control's 76, with 15 exclusive levels against 6** (union 91 of 138, 65.9%; 161 of 161 gated). Costs **9% wall clock**. **Does not solve `LaserTank.lvl` 10**, the level it was derived from. **Now validated over the corpus** (session 33): as the fourth-pass arm `l8fire` it is **66 of 255 (25.9%)**, the best solo arm ever measured on that population, and it **retires `l8work`** — see the row below |
| Layer 5 over the corpus | **15 of 255 (5.9%)** of the levels the whole chain fails, at 27x the campaign budget — an argument for a fourth pass, not for changing the chain |
| The fourth pass, rehearsed | **87 of 255 (34.1%)** of the levels the chain fails, as the **union of seven arms at 40M nodes** on a 1-in-15 stride of the failure population; 372 of 372 gated. The pass is still a chain of arms rather than a configuration, but the arms changed: **the three that ship are now `l8fire` → `layer7` → `enables`, 84 (32.9%)**, against the 81 of the three it replaces. **`--push-fire-tier` does not add a fourth arm, it replaces the first** — `l8work` contributes **+0** to the seven-arm union and holds **1** exclusive level against `l8fire`'s 6. So the pass is better by 3 levels at the *same* three-arm cost (~54 h), not 72 h |
| `LaserTank.lvl` 1-10 | **1-5 and 7-9 solved**, banked in `data/solutions/`; **6 and 10 open**. Level 9 is banked at **115 keys / 1.9x** — the driver's own unattended round-5 run with `--best-of-round`, twelve keys shorter than the hand-run recipe that preceded it and one key off the 114 that was lost with `build/w/`. **Level 10 now has a named candidate** (session 34): it is a GAUNTLET, so `--push-read` finds a barrier on **0 of 63,454 expansions** and the whole layer-6/7/8 stack is inert; the fire tier that diagnosis prescribed is built, is worth +9 on the GAUNTLET tail, and leaves the level unsolved at 400M and d=48. Not budget, not the closure, not width, not the read — a traced run reached **d=63 on 399M nodes in 26m53s** with `trunc=0` throughout, past the 53 board changes of the hand line. What was missing is a *gradient*, and the harvested goal board supplies one: the blog's line wins in 179 moves / 52 shots by **pushing six of the ten anti-tanks and destroying none**, minimum total push distance 30, and it names the cells they end on — so it separates the three root pushes `--analyze` offers and cannot rank. **Level 6 "Cascade" has no post**, so it gains nothing from this and item 5 stands |

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

---

## What is open

Five items, **in this order and not in numeric order**. Numbers are kept because these files refer to
items by number; the finished ones are in
[*Closed items*](docs/solver/history.md#closed-items--the-measurements-including-the-negative-ones).
Full recipes, costs and evidence: [`docs/solver/next-actions.md`](docs/solver/next-actions.md).

| # | order | what it is | cost |
|---|---|---|---|
| **2** | **1st** | the fourth pass. **Rehearsed; the fourth arm turned out to replace the first** — `--push-fire-tier` retires `l8work`, so the run is three arms for 84 (32.9%) and still ~54 h. Recipe updated, not started | ~54 h |
| **5** | 2nd | **level 6's decomposition** — level 10's fire tier is done and is Layer 9 | a layer-sized build |
| **4** | 3rd | the campaign that decides whether `--best-of-round` is a **default** | a stride campaign with the flag against one without |
| **7** | 4th | the solved-vs-budget curve, three budgets over the 687 short-record failures | comparable to one arm per budget |
| 10 | last | wall clock: profile, then memoise `PushH` per board (166k nodes/s against layer 0's 1.4M) | code, instrument first |

**The rule they are ordered by is *cheapest falsifier first, whatever kind of work it is*.**
**Second key: prefer work that produces a property or a level over work that produces a number** —
twenty-five hours of machine time once went into items that report percentages while the two levels
in front of the project did not move and one banked solution got worse. Item 6 is what that second key
bought: three sessions, and what came out was a decoded goal board for `LaserTank.lvl` 10 and a ranking
key that separates its root pushes, rather than a percentage. With it closed the list is a 54-hour
campaign, a three-budget curve, two campaigns that decide a default, and **one build** — item 5, which is
also the only open item that would move a level.

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
post at all** — checked against the whole 6,218-post index — so item 5 is unchanged by any of it.

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

**Item 2 wants the whole machine and nothing else on this list is waiting on it.** Its arms are
node-governed, so extra load moves wall-clock readings and nothing else: launch the run and work item 5
beside it.
