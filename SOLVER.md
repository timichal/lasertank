# LaserTank solver — Phase 4

**Purpose:** single source of truth for the solver. The engine port, the fidelity gates, the file
formats and the quirk hazards are in [`PROGRESS.md`](PROGRESS.md); read that first if the question
is about the *game*.

**What the solver is for.** The goal is **to solve every solvable level in the corpus — 20,914 of
them, all with a non-zero `.ghs` entry and therefore all known-solvable — mostly for the sake of
proving it can be done.** No public automated LaserTank solver does this. That is the point of the
project's second half, and every layer below is a measured step toward it; the current honest number
is **11.3% of a 4,185-level sample** and the whole of the interesting work is in the 3,700-level tail.

Two useful side effects, secondary but real:

- **Corpus extension.** A solved level is a long, legal, *winning* path through the engine — coverage
  a fuzzer cannot reach, because random play drowns the tank in twenty keys.
- **A second differential test of the port.** Every solution is replayed through the frozen C oracle
  *and* the C# core and must win on both with byte-identical traces. Nine hundred-odd solver
  recordings have gone through that gate with zero divergences.

The gate is not a formality even so: it is the write path, and a solution that fails it is deleted
rather than banked.

**The standing engine claim, re-checked at the end of every session:** nine layers in,
`Engine.cs` still differs from a literal transliteration by the single word `partial`, and
`Engine.Search.cs` has not changed since layer 0. If a solver change seems to need an engine change,
stop and re-read.

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
the previous one failed. `STRIDE=5` gives the 4,185-level sample every number below is quoted
against; each pass over ~3,700 failures is 6-8 minutes at 14 jobs, and the campaign is the longer
half. `STRIDE=1` is the whole 20,914-level corpus and is hours:

```bash
STRIDE=5 NODES=150000 tools/campaign.sh solutions/l0 build/reports/l0.jsonl --no-macro
tools/second_pass.sh build/reports/l0.jsonl  solutions/l34 build/reports/l3n.jsonl \
                     --no-ida --no-beam --subgoal
tools/second_pass.sh build/reports/l3n.jsonl solutions/l34 build/reports/l34.jsonl \
                     --no-ida --no-beam --subgoal --sg-eval learned
tools/second_pass.sh build/reports/l34.jsonl solutions/l34 build/reports/l34pass4.jsonl \
                     --no-ida --no-beam --macro --macro-first
python tools/verify_solutions.py build/solutions/l0     # layer 0's own solutions
python tools/verify_solutions.py build/solutions/l34    # everything the three passes added
```

`report_stats.py` reads any of those reports (`--diff` compares two layers); the composite is the
*union* of the four, which is why the two verify runs are separate. A level solved by an earlier
pass is skipped by a later one, so no `.lpb` is ever written twice.

---

## Five rules the sessions each paid to learn

These are the reason the numbers in this file can be trusted.

- **Bench on the corpus, not on a filtered population.** `bench/bench-levels.txt` is levels
  layer 0 failed. It flattered layers 1 and 2, overstated layer 3's budget scaling and *understated*
  layer 4. A bench picks parameters; only a campaign decides what ships.
- **Govern by `--nodes`, never wall clock.** A node is one `Engine.ApplyKey`. Seconds are not
  reproducible on a machine that is also running the gates. (The first campaign was wall-clock
  budgeted and had to be thrown away.)
- **Instrument before theorising.** `--sg-trace` killed two of layer 2's designs and one of layer
  3's; `tools/rankdump.py` decided whether layer 4 was worth building at all; `--push-line` found
  the bug that had been costing layer 5 its whole budget. A new layer should report a distribution
  before it reports a solved count. And an instrument that measures the wrong *moment* says the
  layer does nothing — that has now happened twice.
- **A solo bench score is the wrong statistic for a portfolio member.** Every specialist since
  layer 6 looks like a one-to-two-level loss alone and adds three to six levels to the *union* with
  the rung beside it, because the levels it fails are not the levels that rung fails. Measure unions.
- **A penalty every board pays is not a penalty.** A weight that every successor of every held board
  incurs makes the beam's best score *rise* and steers nothing. Use a tier (an ordering that cannot
  refuse a state) instead. Same family of error as a heuristic that returns 0 — the best score there
  is — for a board it has nothing to say about; that one has now cost three separate bugs
  (buried flag, unmatched hole, unshieldable cell).

---

## Status

**Layers 0-4 ship in the batch chain. Layers 5-8 ship as rungs of the interactive driver.**

| | state |
|---|---|
| Layers 0-4, the chain | **472 of the 4,185-level stride sample (11.3%)**, every solution verified through both engines |
| Layer 5 — push macros | board-change search. Ferry bench **20/50**, deep **21/50** (from 11 and 14 before the pose-duplicate fix) |
| Layer 6 — the read | derives what is in the way and what can change it. Ships inside layer 5's rung (15/50 against 9/50 without it). Its anti-tank-on-the-route rule is `--read-antitank-wall`, **off by default** — see *Layer 8* |
| Layer 6's fourth derivation | *what does this change make possible?* — `--push-enables`; own rung, adds 4 ferry / 5 deep, **solves `LaserTank.lvl` 1 in 67 s with no flags** |
| Layer 7 — the stop cell | *what must be blocked before the tank can stand next to the flag?* — own rung with `--push-shot-run`, adds 3 ferry / 5 deep, **solves level 2 in 65 s with no flags** |
| Layer 8 — reading the board | six derivations (fire map, safe flood, frozen block, ferry assignment, ferry maze, shield). **Two rungs**, adding 3+1 ferry / 6+2 deep; **solves level 8** (57.5M nodes, width 512) |
| Layer 5 over the corpus | **15 of 255 (5.9%)** of the levels the whole chain fails, at 27x the campaign budget — an argument for a fourth pass, not for changing the chain |
| `LaserTank.lvl` 1-10 | **1-5 and 7-9 solved**, banked in `data/solutions/` (any one of them may be temporarily deleted for a manual re-run — see *Next actions*, item 3, and the shorter files for 8 and 9 it lists); **6 and 10 open**, and 10 is no longer a budget case — two 900M-node runs came back unsolved at depth 2 |

*One number moved for a reason worth knowing before trusting the rest: the ferry bench is **19/50**
where earlier sessions banked 20/50. That is session 20's buried-flag fix, attributed rather than
assumed — reverting it alone reproduces 20/50 exactly, so everything else since is inert at default
flags, and the level it costs (`Beginner-I` 1581) never buries a flag on its own line. A correct
ranking that costs one level is still the correct ranking; compare future runs against 19.*

The per-tier and per-collection curve at 150k nodes:

| tier | levels | layer 0 | + L1 pass | + L2 | + L3 | + L4 | median ratio |
|---|---:|---:|---:|---:|---:|---:|---:|
| Kids | 960 | 303 (31.6%) | 319 | 339 | 341 | **359 (37.4%)** | 1.6× |
| Easy | 2,118 | 84 (4.0%) | 89 | 94 | 95 | **104 (4.9%)** | 1.7× |
| Medium | 784 | 7 | 7 | 7 | 7 | **8 (1.0%)** | 1.6× |
| Hard | 257 | 0 | 0 | 0 | 0 | **0** | — |
| Deadly | 56 | 1 | 1 | 1 | 1 | **1** | — |
| **all** | **4,185** | 395 (9.4%) | 416 | 441 | 444 | **472 (11.3%)** | **1.6×** |

**The unsolved are unsolved on budget, not on structure:** 3,636 of layer 0's 3,790 failures stopped
at `budget` (95.9%), only 154 at a beam dead end. No errors, no `NOTPORTED`, no crashes in 8,370
level-solves. `Hard` at 0/257 is not the search failing to find a route, it is the search never
getting near the end of one. **Depth is the binding constraint** and every layer since has been an
attack on it.

---

## Next actions

> **Read this first — session 24.** `build/` is empty on this machine (Michal moved machines; it is
> gitignored, so nothing in it travelled). That makes **items 1, 2 and 3 below unrunnable exactly as
> written**: the level lists, `chain.jsonl` and every banked campaign solution are gone. The
> ordering has changed accordingly — **item 0 now comes before all of them**, because it is what
> makes the rest possible again.
>
> **0. Re-run the layer-0 campaign, and commit the three level lists to `bench/` this time.** The
> chain at the top of this file rebuilds `build/reports/*.jsonl` and `build/solutions/`;
> `chain.jsonl` is then the union of the four. `deep-levels.txt` is the only list derivable without
> a campaign (it is a `.ghs` total filter). Until this is done, no number in the tuning tables can
> be re-checked, and the *19/50 and 21/50* bench check in item 1 has nothing to run against. See
> `bench/README.md`.

**1. The fourth pass, at a budget that matches what layer 5 costs.** The decision pass came out
positive — 15 of 255 (5.9%) of the levels the shipped chain fails, all verified — so the open
question is not *whether* layer 5 pays but *how much of the corpus is worth spending on it*. One push
expansion is a whole closure, so this is the one pass that has to be budgeted in tens of millions of
nodes rather than hundreds of thousands.

```bash
# the whole population the chain fails, not a 1-in-15 sample of it, at 40M nodes
NODES=40000000 BUDGET_MS=1800000 JOBS=12 bash tools/second_pass.sh \
    build/reports/chain.jsonl solutions/l5 build/reports/l5.jsonl \
    --no-ida --no-beam --push --push-read
python tools/report_stats.py build/reports/l5.jsonl
python tools/verify_solutions.py build/solutions/l5
```

Budget it as hours: 3,713 levels at 40M nodes is 10x the sample pass at 10x the budget. `SAMPLE=15`
first if a rehearsal is wanted (~20 minutes at 12 jobs). If `build/reports/chain.jsonl` is gone,
rebuild it by unioning the four chain reports (`l0`, `l3n`, `l34`, `l34pass4`) on
`(collection, level) -> solved`.

**Run it in five arms and compare unions, not solo counts.** Layers 7 and 8 have *never* seen the
corpus — two benches and an ablation is all that is behind them — so those arms are the measurement
they are missing, not a nice-to-have:

| arm | flags on top of `--no-ida --no-beam --push --push-read` |
|---|---|
| plain | — |
| layer 6's fourth derivation | `--push-enables 8` |
| layer 7 | `--push-stop 1 --push-shot-run 16 --push-beam 128` |
| layer 8, learned key | `--push-reach --push-ferry-match --push-ferry-maze --push-dead 20 --push-fire 8 --push-shot-run 16 --push-beam 128 --max-keys 5000` |
| layer 8, work key | the same plus `--push-eval work` |

Note the raised `--max-keys`: 1,200 is a silent cap on any level whose solution runs long, which is
exactly the population this pass is.

**And the benches as a check, not as a decision:** `bench.sh` on `bench/ferry-levels.txt`
and `deep-levels.txt` at 4M nodes with `--no-ida --no-beam --push --push-read` should give **19/50**
and **21/50**; adding `--push-beam 48 --push-per-board 0 --push-eval work --push-depth 400` (the
session-17 configuration) should still give 11/50 and 14/50.

**2. Refresh the banked solutions.** *(Blocked on item 0: `build/solutions/` is empty.)*
`Trim.Polish` removes 47% of a subgoal solution's keypresses and
`Replan.Improve` another slice on top, so **every banked `.lpb` under `build/solutions/` is longer
than it needs to be** — they all predate the replan pass. `--polish DIR` runs both over each of them.
This is not cosmetic: shorter trajectories change the ascent statistics the whole layer-5 argument
rests on, and they are what layer 4 is fit on. Measured on the 416 solutions in
`build/solutions/l0`: 11,060 → 10,249 keypresses in about 150 s.

**3. Levels 8 and 9 — Michal re-banks these himself, and a missing `.lpb` is not a missing solution.**
**Read this before concluding anything from the contents of `data/solutions/`:** he deletes a banked
`.lpb` on purpose in order to re-run the solver by hand and watch the solution replay, and re-banks it
afterwards. That is the normal working loop, not a lost result — so a level named as banked in this
file may be absent from the directory at any given moment, and the way to check whether it is solved
is this file plus `build/`, never a directory listing.

~~The one thing worth acting on is that shorter verified solutions for both are sitting in `build/`~~
— **session 24: those files are gone with the rest of `build/`.** What they were:

| lvl | shortest verified file, now lost | keys | ratio | banked now |
|---:|---|---:|---:|---|
| 8 | `build/w/w8-2048/LaserTank/00008.lpb` | 308 (262 + 46) | 1.4x | 335 / 1.5x |
| 9 | `build/w/b9-b/LaserTank/00009.lpb` | 114 (81 + 33) | 1.9x | **127 / 2.2x, re-derived** |

Which configuration produced either is not recorded — the widths in the directory names were the only
clue — so **neither is reproducible and the 308 and 114 routes are lost**, not merely misplaced. That
is the same lesson as `bench/`: a verified result that lives only in a gitignored directory is not
banked.

Level 9's was re-derived in session 24 and is banked again at **127 keys / 2.2x**, verified through
both engines, from the `push-ferry-work` rung at its round-5 settings run directly — 16m36s:

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 9   --no-ida --no-beam --push --push-read --push-eval work --read-antitank-wall   --push-reach --push-ferry-match --push-ferry-maze   --push-dead 20 --push-fire 8 --push-shot-run 16   --push-beam 2048 --max-keys 5000 --push-restarts 30   --nodes 250000000 --budget-ms 3600000 --out build/short9
```

**This is the command to use for level 9, not the driver.** An unattended driver run banks the beam's
**294-key / 5.0x** route instead, because the beam reaches its win at 94.3M nodes and
`push-ferry-work` needs 162.8M, so the beam always gets there first and cancels it. Level 8's 308 has
not been re-derived.

**4. Levels 6 and 10** — see *What has not fallen, stated plainly*. Both now want the same thing, and
it is not budget. 6 wants layer 2's decomposition one level out: commit to one block-and-hole pair,
search only for that, re-derive. **10 was the "just buy the nodes" case until session 23, when the two
900M-node runs session 22 left going came back unsolved at board-change depth 2** — so its ~1,000-pose
closure, not its width, is what needs attacking. Do not spend another overnight run on it as-is.

**5. Still worth doing, no longer blocking: the `lasertanksolutions.blogspot.com` goal-board
harvester.** See *Open question* at the end.

---

## The bar, measured before building anything

Best-known solution cost from the 13 `.ghs` files, by the difficulty rating in each level record:

| tier | levels | `.ghs` moves+shots p10 / p50 / p90 | median pushables |
|---|---:|---|---:|
| Kids | 4,720 | 7 / **46** / 215 | 12 |
| Easy | 10,572 | 34 / **122** / 607 | 19 |
| Medium | 4,058 | 68 / 265 / 1204 | 22 |
| Hard | 1,233 | 124 / 403 / 1353 | 20 |
| Deadly | 289 | 225 / 610 / 2169 | 21 |

Only 1,771 of 20,914 levels have a best-known total ≤20, and 4,750 ≤50. Eleven levels in the whole
corpus contain no block, ice, mirror, conveyor, anti-tank or tunnel. **Kids is not a shallow tier,
it is a small-branching one.** And keypresses are *worse* than these numbers: `ScoreMove` only
increments in `UpDateTankPos` (`Engine.cs:495`) while `MoveTank` spends a whole keypress on a turn
without scoring (`Engine.cs:491`), so "103 moves + 46 shots = 149 keypresses" is a lower bound — add
one key per direction change. A keypress-level exhaustive search reaches the ≤20 bucket and nothing
else, which is why the plan is layered.

**How far away the goal is, stated in the same units.** Solving all 20,914 is what this half of the
project is for, and this table is why it is not a matter of running the existing chain for longer: the
median Easy level is 122 moves-plus-shots and the median Deadly one 610, against a shipped chain that
lives at 11.3% of a sample and fails 95.9% of its levels on *budget*. So the way progress is reported
is a **solved-count-vs-budget curve, Kids-first ordered by `.ghs` cost** — a measurement of the
distance, not a substitute for the target.

---

## The layers

Each layer is a different attack on depth. The structural finding that shapes all of them arrived at
layer 1 and has held since: **a specialist that must bet on every level in advance loses in a
portfolio, because most solvable levels are ones the raw beam gets easily and every node the
specialist spends is a node taken from it.** So specialists ship either as a *second pass* over the
levels an earlier pass failed (batch) or as a *rung* of the interactive driver, where a specialist
costs a core rather than a share of anybody's budget.

### Layer 0 — the search API and the harness  ☑

- **`src/LaserTank.Core/Engine.Search.cs`** — `Snapshot`/`Restore` of the whole mutable engine,
  `ApplyKey` (one keypress, then tick to quiescence), `StateHash` for a transposition table, and
  `ActionKeys`. The rule is **restore everything, hash a subset**: staleness is load-bearing here
  (`wasIce`, quirk #3; `WaitToTrans`), so the hash keeps them while dropping `BMF`, the counters and
  the path.
- **`src/LaserTank.Solver/`** → `build/lasertank-solve.exe`. Weighted beam + IDA* over macro-steps, a
  flag-distance heuristic, the trimmer, and a parallel batch harness that writes each solution as a
  **`.lpb`** — a real recording, playable in the 2010 binary, not a private format.
- **`tools/verify_solutions.py`** — replays every produced `.lpb` through the *unmodified* oracle and
  the core with `--field --bmf` and requires WIN on both plus byte-identical traces.

**Two bugs the harness found on itself, both worth not re-discovering:**

- **A macro-step is not bounded by anything cheap.** Level 1491 ("Grand Prix 2", hint: *"get on the
  conveyor and watch"*) takes **3,652 ticks for one keypress** — the tank rides a closed conveyor
  circuit around the whole board. A 512-tick cap called that a hang and threw the level away.
  `ApplyKey` now detects *cycles* (sampled state hashes, started only after 256 ticks so the common
  case pays nothing) and keeps the tick cap as a backstop. Genuine eternal cycles exist and must stay
  reportable: Tutor 43 is literally "Smallest eternal cycle".
- **`Restore` rewinds `RecP` but the keystream is one shared array.** A breadth-first search then
  silently corrupts its own answers: siblings overwrite each other's keys and the winning node
  reports whichever prefix was written last. A depth-first search never notices — IDA* was green
  while every beam solution failed to replay. The snapshot now carries its key prefix. Caught only
  because the harness replays each solution before writing it; that self-check earns its place.

**A closed-set policy that looks like a bug and is not.** Both beams mark a successor visited the
moment it is *generated*, so a state the width trim discards is closed forever. That reads as a
defect, and the fix was written and measured, and **it is a regression**: closing only on expansion
takes the raw beam from 33 to 27 on bench 1. Over-pruning wins, because the budget is nodes and the
greedy policy spends them on depth instead of on re-deriving positions it has already rejected. Kept
as `--closed generate|expand` with the measured default, and the reasoning sits in `Search.cs` so it
does not get "fixed" again. **This is a property of the search, not of the game:** layer 2's 4-wide
beam is *killed* by the same policy (10 against 6), hence `--sg-closed` defaulting to the opposite.

### Layer 1 — macro-actions  ☑ (ships as a second pass)

**The action set.** `Goto(x, y, dir)` — drive the tank somewhere, spending as many keys as that takes
— plus `Shoot`, one space bar. A solution is an alternation of the two, which is *complete rather
than restrictive*: any keystream is a run of direction keys, a space, a run of direction keys, … So
**search depth becomes the shot count.** In the unbiased 1-in-5 sample of `Beginner-I` the median
level needs 16 shots against 27 moves, and 15% need no shot at all.

**The Goto is a sub-search *in* the engine, not a model of it.** A grid A* would have to re-derive
`MoveTank`'s turn-costs-a-key rule, `IceMoveT`'s slide, `ConvMoveTank`, `TranslateTunnel`'s pairing
and the fact that `AntiTank()` runs on every key-consuming tick — i.e. it would be a second
implementation of the game, free to drift from the one being ported. So `Goto` is a **breadth-first
closure over `Engine.ApplyKey` with the four direction keys, deduplicated by `StateHash`**: ice,
conveyors, tunnels, pushed blocks and anti-tank turns are "resolved" by being *executed*. It costs
more per node than a grid A* and it cannot be wrong. The closure cap (`--closure-nodes` 1500,
`--closure-depth` 40) is the one place layer 1 gives up completeness — a knob with a number on it
rather than a hidden constant.

**A shot that changes nothing is dropped, and that is lossless.** If the state hash is identical
after the space bar then nothing happened at all — not even an anti-tank turn, because `AntiTank()`
runs inside the same key-consuming tick. So the successor **is** the state it was fired from, which
is already in this expansion's closure.

**The escape hatch:** the `--move-only` (default 6) closure states ending nearest the flag are kept
as pure-`Goto` successors. Without them a level needing no shot at all has no successors and the beam
dies at depth 1 — and 15% of sampled levels have `.ghs` shots = 0.

**A new heuristic, because the old one goes flat exactly here.** `FlagDistance` is a BFS over cells
the tank may *currently* enter, which after a Goto closure is guaranteed useless: the closure only
ends on states whose flag is not movement-reachable, so every macro successor scores
`Unreachable + manhattan` and the beam ranks by tank position. `Heuristic.WorkDistance` keeps a
gradient by *charging* for obstacles instead of refusing to cross them: a Dijkstra from the flag
where an empty step costs 1, a brick 4, a block 6, an anti-tank 6, a mirror 7, water 9, a rotary
mirror 12, and only `Solid` and `Crystal` are impassable — crystal because `CheckLLoc` case 19
returns `true` without touching the cell, so a laser goes straight through one and never clears it.
Tunnel mouths sharing an id are joined by zero-cost edges. Deliberately not admissible: a beam needs
a gradient, not a lower bound, and the admissible version of this (every price 1) *is* `FlagDistance`'s
flat spot. Pushing a block into water turns the cell to `Dirt` (`MoveObj`'s `obt == 5` arm), so the
number really does drop by 8 when the level's central puzzle is solved.

**Measured three ways, and the first two measurements were misleading** — this is where the
bench-population rule comes from:

| | bench 1 (60 levels layer 0 failed) | deep bench (50 levels) | corpus (4,185) |
|---|---:|---:|---:|
| layer 0 | 18/60 @150k, 33/60 @1M | 13/50 | **395** |
| layer 1 | **28/60** @150k, 38/60 @1M | 12-13/50 | 381 (+21, **−35**) |

Bench 1 lied because it is a population where the raw beam is 0% by construction. On the corpus,
layer 1 as a portfolio member is a net loss either way round (macro-first 381, macro-last 354) and no
share or ordering fixes it. **So it ships as a second pass** (`tools/second_pass.sh`, `--macro-first
--no-ida --no-beam`), which adds **21 levels**, none at layer 0's expense.

**And the deep-level result is real regardless of ordering, which is what pointed at layer 2.** A
Goto closure costs `5 × |closure|` `ApplyKey` calls where a raw-beam successor costs 1. Macro-actions
cut the *number* of decisions by an order of magnitude and multiply the *price* of each by two or
three. Worse: inside a Goto, movement is *exhausted* rather than searched, so the beam never ranks a
movement — it ranks *board changes*, and `WorkDistance` is a thin signal for those. A keypress beam
has a gradient to walk down; a shot beam has to guess which of two hundred available shots is the
useful one. **The reason to fire has to be derived, not scored.**

### Layer 2 — subgoal decomposition  ☑

**The derivation — and the first version of it was wrong, which is the part worth reading.**

*Version 1, from a model.* Walk `WorkDistance`'s predecessor chain from tank to flag and call every
cell costing more than an empty step an obstacle. `--sg-trace` over 384 expansions: **62% derived no
obstacle at all.** The price list said the flag was five cheap steps away while the tank plainly
could not get there. `Beginner-I` 101 ("BE the RABBIT") is the clean case: the flag is walled in by
bricks and reached through a tunnel, so a model that joins tunnel mouths at zero cost reports a clear
five-step run. A price list knows what a cell costs to *enter*. It does not know the cell is covered
by an anti-tank, that the thin ice on the way has already been used, or which mouth a tunnel actually
pairs with — and those are precisely what stops a tank on the levels a solver fails.

*Version 2, from the engine.* The movement closure runs **first**, and the cells it stood on are
recorded — an executed answer to "where can the tank get to", with death, spent thin ice, conveyors
and tunnel pairing all resolved by having happened. The Dijkstra then runs from the flag and stops at
the first of those cells it settles; what lies between is what is in the way. **The model proposes
the ordering, the engine supplies every claim about what the tank can do.** No-obstacle expansions
fell from 62% to 23%.

Two kinds of cell come back. One that costs something to enter (brick, block, mirror, water) is its
own subgoal: make it cheaper. One that costs *nothing* and is still not reached is one the tank died
in, so there is nothing at the cell to shoot and the anti-tanks aligned with it become the targets.
That is Tutor 75, "Pass the anti-tanks", derived rather than recognised.

**Acceptance is a board test; ranking is a position test.** A successor survives because a derived
obstacle got cheaper — not because a number went down. `WorkDistance` only *orders* what already
survived. Clearing a brick usually leaves the tank somewhere awkward, so the two tests disagree
constantly, and layer 1 could not see past their sum.

**Slack, and why a search that only accepts progress gets stuck.** A derived subgoal is often two
moves away — rotate the mirror so the laser turns, *then* shoot the brick — and the first clears
nothing. Accepting only progress dies: 44 of 50 deep levels ended at `subgoal-dead-end` having spent
20,810 of 400,000 nodes. So each expansion keeps its best `--sg-slack` (default 4) board-changing
successors as **Tier 1**, which `Cut()` takes only after every successor that actually advanced.
Deep bench 6 → 9, whole budget spent, dead-ends 44 → 4.

**Four things measured that did *not* work**, kept as flags with their numbers (50 deep levels, 400k,
subgoal beam alone; shipped defaults = 10):

| | solved |
|---|---:|
| `--sg-aim` (fire only from poses whose ray meets a target) | 2 |
| `--sg-strict` (accept only on a cleared obstacle) | 3 |
| `--sg-closed generate` (layer 0's measured default) | 6 |
| `--sg-width 12` instead of 4 | 8 |

`--sg-aim` is the instructive one: the ray *is* a superset of the shots that hit a target, and it is
**not** a superset of the shots worth firing. Rearranging a brick that is not itself a target is how
the next step becomes possible, and a shot whose only effect is to make an anti-tank turn is
sometimes the whole trick.

**As a second pass it is worth about twice layer 1** — 40 levels against 21 over the same 3,790
failures — and the two are complementary rather than one superseding the other: they overlap on 15,
and the 6 levels layer 1 finds that layer 2 does not are *exactly* the 6 a third pass recovers. As a
portfolio member it is a smaller loss than layer 1 and still a loss (387 / 365 against 395).

Where the budget goes after it, which is the brief for layer 3: layer 0's failures stop on budget
95.9% of the time; layer 2's stop on budget 80.8% and at **`subgoal-dead-end` 19.1%** — a frontier
that emptied, not a clock that ran out.

### Layer 3 — restarts  ☑

**Priced before designed.** Over layer 2's pass, **717 levels dead-ended with a median of 84% of
their node budget unspent** — about 90 million `ApplyKey` calls paid for and thrown away. That is the
resource this layer spends.

**What a dead-end actually looks like, which killed the design that was going to be built.**
`--sg-trace` over six of them: closure size p50 **8-18 states** where the cap is 400; **81-99%** of
expansions derive `added = 0`; a third to two thirds offer no slack either. Three readings, each of
which cost a plausible idea:

- **The closure cap is not the problem.** A boxed-in tank reaches eight states, not the 400 it is
  allowed, so randomising which states a truncated closure keeps buys almost nothing here.
- **The search is running on slack.** With `added = 0` nine expansions in ten, essentially every
  frontier node is a Tier 1 slack node picked by `WorkDistance`. *That* choice is the arbitrary one,
  so that is where the noise goes: `--sg-noise` jitters the ranking key **after `Offer()` has decided
  the successor advanced**. Acceptance stays a board test; only the ordering is randomised.
- **The frontier emptied because the run had closed everything it saw**, which suggested re-seeding
  from the discarded reserve. That is the idea the corpus refuted.

**Restarts are strictly additive, and that is the structural difference from layers 1 and 2.** The
beam re-runs only when it stopped at `subgoal-dead-end` *and* budget remains. Attempt 0 has no
jitter, no re-seed and the canonical key order, so it **is** layer 2 exactly — verified,
`--sg-restarts 0` reproduces layer 2's benches to the level. It cannot tax the pass it runs inside,
so the only question is what the recovered budget buys.

**What recovers a dead-end is width, not randomness — and the two directions of that are the whole
layer:**

| subgoal beam | deep (50 @ 400k) | bench 1 (60 @ 150k) |
|---|---:|---:|
| width 4, no restarts (layer 2) | **10** | 24 |
| width 8 / 16 from the start | 8 / 6 | 25 / 25 |
| restarts, no growth | 10 | 25 |
| restarts + `--sg-grow` (4→8→16…) | **11** | **28** |

Narrow-and-deep is what buys layer 2 the depth it exists for, so widening up front costs; a restart
is the only way to have both, because by then the narrow search has *already reported* that it
failed. `--sg-grow` doubles width and slack per restart, capped at 64/32, on by default.
Re-seeding from the reserve loses (corpus 43 against root's 44) because it inherits every commitment
the narrow beam made, and a *grown* beam wants to re-take those wider. `--sg-reuse root` is default.

**The result, and the negative half is worth more than the positive half.** 717 levels spent a
restart, dead-ends fell 717 → 9, and it bought **four levels** (441 → 444 composite, none lost).
**Converting an emptied frontier into a spent budget mostly does not convert it into a solution.**
The 19.1% marked where budget was being *wasted*, not where solutions were being *missed*. Depth
remains the binding constraint. (One of the four is `Beginner-I` 101, the level that killed layer 2's
modelled derivation; it now falls on the first restart.)

**Why not NRPA or nested Monte-Carlo, which is what the plan said.** Those adapt *which action* a
playout picks. The measurement says the subgoal search does not fail by picking the wrong successor
among many — it fails with `added = 0` nine expansions in ten, i.e. with almost nothing to pick from,
and giving it strictly more (16× width, six restarts, 84% more budget actually spent) moved 4 levels
of 3,790. **A policy that learns to order an empty list has nothing to learn.** That is the argument
for layer 4 being about depth rather than about restart policy.

### Layer 4 — a learned evaluation  ☑

**The instrument decided the layer, and it runs inside the shipped expansion.** Replaying a winning
`.lpb` one key at a time gives the exact sequence of states a perfect search would have visited. From
each of its shot boundaries the *real* `ExpandSubgoal` is run, and every candidate it offers is
recorded with its board features and with whether the winner in fact went through it. `_collect` is a
hook in `Offer()`, not a second copy of the expansion — a look-alike written to observe the search
would be free to drift from it, and the distribution would then be a fact about the look-alike.
**652 recordings, 20,148 groups, 5.9M candidates.**

| | all | human recordings | solver solutions |
|---|---:|---:|---:|
| candidates per group, p50 | 395 | 399 | 66 |
| **the winner's successor is in the group** | **97.6%** | 97.4% | 98.4% |
| ...and the board test calls it *slack*, not progress | 62.4% | 68.2% | 30.6% |
| `WorkDistance` rank of it, p50 | **100** | 128 | 6 |
| ...inside the beam's width of 4 | 10.0% | **4.1%** | 41.8% |

- **Coverage is not the constraint.** The closure reaches the winner's state and the acceptance test
  admits it. Everything lost is lost in the sort.
- **The right move is usually one the board test does not call progress** (62.4% are slack nodes).
  Layer 3 found the search *runs* on slack; this says the slack pick is usually wrong — which is what
  makes it worth learning rather than jittering.

**What is fit, and what is deliberately not.** The datum is a *group*: one expansion, every successor
it offered, and which was right. A cost-to-go regression over trajectory states is the wrong shape —
every state on a winning trajectory is a good state, so a model fit to those alone has never seen a
bad one. Here positives and negatives come out of the same expansion, which is exactly the comparison
the beam makes. The loss is a softmax within the group, because the beam keeps four of 395 and what
is worth fitting is *which four*, not the order of the 391 it throws away.

Seventeen features (`Feat`), all functions of the *board* and never of the path to it — two routes to
the same state must score the same, which rules out the obvious-looking keys-spent and shots-fired.
Held out **by recording, never by group** (groups from one recording share a board, so a group-wise
split reports a training score); groups weighted by 1/(groups from the same recording), because two
quirk packs supply 16,599 of the 20,148.

| held out, top-4 | `WorkDistance` | learned |
|---|---:|---:|
| all (121 recordings) | 13.6% | **18.2%** |
| human (33) | 5.7% | **10.4%** |

**It is a ranking change and nothing else, and that is enforced rather than intended.** `Rank()` is
consulted only after `Offer()` has settled whether a successor advanced, so a model can reorder the
frontier but can never admit a state the shipped search refused. The check: the seed vector
`{work: 1, work_far: 1000, far_man: 1}` **is** `WorkDistance` written in these features, and
`--sg-eval learned` with it reproduces layer 3 exactly — identical keystreams, node counts and stop
reasons on all 50 deep-bench levels. `far_man` exists only so that equivalence can be exact.

**The campaign: 69 against layer 3's 44 over the same 3,790 failures — and it is not a superset.**
Three of layer 3's are lost, which is the structural difference: a *restart* is additive by
construction, a *re-ranking* is a different search from the first expansion. So layer 4 does not
replace layer 3's pass, it **follows** it: 444 → 469, then layer 1's macro beam adds 3 → **472**.

**Feeding the newly solved levels back in makes it worse, and the way it is worse is the finding.**

| | pass | levels whose own solution was in training | levels never seen |
|---|---:|---:|---:|
| round 1 — 187 human + 465 solver | 69 | 41 of 49 | **28** |
| round 2 — 187 human + 539 solver | 72 | 58 of 77 | **14** |

The pass goes up by 3 and the *discovery* halves; both benches move the other way. **A self-trained
ranker learns the levels it was fed rather than the game**, and the total is a worse summary of it
than the split is. Round 1 ships. Note what that table also says about the headline: 41 of the 69 are
levels whose keystream was in the training set, so **28 is the size of the generalisation** — and the
composite gains 28 exactly.

**Where the ceiling is.** Even after fitting, the winner's successor is inside the width of 4 only
10.4% of the time on held-out human recordings. The headroom is not coverage (97.6%) and not the
acceptance test — it is entirely in the sort, and 80% of it is still on the table. That is the brief
for everything above this layer.

**Hints as landmarks was not built**, and the arithmetic says not to: only **175 of 20,914** hints are
recipe-grade (≥2 grid references or numbered steps), concentrated where search fails (0.4% of Kids,
7.3% of Deadly). A tail tool against a 3,700-level tail.

### Why the search fails on long levels — the measurement the rest of the layers answer

**The trigger** was `LaserTank.lvl` 1 "Boot Camp" surviving four rounds of the driver, and the answer
took three measurements rather than a level number.

1. **"Level 1" is a name, not a difficulty.** Its `.ghs` record is 149 keypresses, the 66th
   percentile of its own collection. Of 677 winning rows across every campaign report the median
   record is 19. Level 1 is longer than roughly 99% of everything the solver had ever solved.
2. **Layer 0's beam ranked it by a heuristic that is constant on it.** The flag at (0,0) sits in a
   water pocket, so `FlagDistance` returns `Unreachable + manhattan` for the whole level: `bestH`
   pinned at 1008 for **220 consecutive depths**. (Swapping the beam's key to `WorkDistance` fixes
   the trace and loses on the bench, 12/50 against 13/50 — so it was not shipped, and the *trace* is
   the finding, not the swap.)
3. **The winning line goes uphill, and by how much is measurable.** `--profile` (`Profile.cs`) replays
   a winning recording and prints every ranking key at every keypress; `tools/basin.py` reads it. The
   number that matters is **the longest stretch that stays at or above the best heuristic value seen
   so far**, because a beam follows a descent for free and an ascent only while the ascent's whole
   cross-section fits in its width.

| | p50 | p90 | max |
|---|---:|---:|---:|
| 402 solver solutions, longest ascent in keypresses | 6 | 21 | 457 |
| 402 solver solutions, in board changes | 1 | 8 | 227 |
| **20 hand recordings of `LaserTank.lvl` 1-19** | **50** | 200 | 297 |
| the same, in board changes | **15** | 33 | 33 |

**Twenty levels, six structures, one failure mode** — ferry, sokoban, conveyor ring, anti-tank
gauntlet, mirror routing and a mixed siege all fail the same way, and none is close to the band a
beam can walk. The ascent belongs to *long levels*, not to level 1 and not to ferrying. That is not a
budget problem and no number of rounds fixes it.

**The lever the table suggests:** measured in *board changes* the same trajectories are 5x shorter,
which moves level 1 from 3.2x beyond anything ever solved to 2x the p90 — the range width and
restarts already reach. That is layer 5.

More hand recordings of long levels (record ≥100) remain the single most useful thing to add, for two
distinct reasons: as **instrument** (each is another row of the table above, and the only way to know
whether a new derivation generalises) and as **training data** off the distribution layer 4's
self-reinforcement trap is stuck in. Short recordings are not wanted — the solver already wins those.

### Layer 5 — push macros  ☑ (rung, not chain)

`Push.cs`, `--push`. The action set is one **PF-preserving movement closure** — everything the tank
can do without changing the playfield, so at most 16×16×4 poses — and then every board change
reachable from any pose in it, as a first-class successor. Fire from a pose (layer 1's rule and its
lossless prune, kept whole), or drive into something and keep driving while the board keeps changing,
one successor per changed cell. **Search depth is the board-change count**, which is the unit
`tools/basin.py` measures in.

Closures never truncate — `--push-trace` reports `trunc=0` on every expansion of every run made so
far, which is the layer's structural bet checked rather than assumed.

**The ferry term.** `WorkDistance` prices a water cell at 9 and does not move at all while a block is
being carried towards it: the whole ferry — fetch, turn, push, twenty to fifty keypresses — scores
the same as standing still, and only the final push scores anything. `Heuristic.RouteFerry` is the
fix: summed over the water cells on the settled route, how far the nearest movable block still is
from each. Manhattan, deliberately, because "can this block actually be pushed there" is a question
about `MoveObj`, ice and where the tank can stand — i.e. a second implementation of the game.
Weight from a sweep rather than a guess: at 1 the ascent is 68 → 49 keypresses, past 2 it gets
shorter and *deeper* (the term overpowering what it corrects). And it is **inert where it is not
needed** — over the 402 solver recordings the excursion distribution is identical at weight 0, 1 and
2. A targeted term that cannot cost anything on the population already solved is the right kind of
heuristic to add.

**`--push-closed` defaults to `expand`, the opposite of layer 0.** Closing on generate binned level
1's frontier entirely. Layer 0 spends five `ApplyKey` calls on a successor and can afford to; this
layer spends a closure.

**Restarts, carried over from layer 3** for a failure with the same shape: `--push-restarts` (6)
re-runs with double the width on `push-dead-end` only, capped at 9,600. Additive by construction, so
it is on by default while the layer as a whole is not.

#### The width was being spent on tank poses — `--push-line`, and what it found

**The instrument first, and this one is new in kind.** Three sessions had explained level 1 from the
outside and none of them named *which line of code loses the level*. `--push-line FILE.lpb`
(`Line.cs`) asks directly: replay a winning recording, keep its state at every board change, then run
the real beam with those states in hand and report per depth whether the line's state was generated,
what the ranking key made of it, and whether the width trim kept it. The hashes are read and never
given to `Cut`, so a `--push-line` run *is* the run it is explaining.

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 1 \
    --push-line data/demos/LaserTank/00001.lpb --nodes 20000000 --budget-ms 600000 --jobs 1
```

Four outcomes, calling for different fixes: **CUT** (generated, ranked, outside the width — rank 60
at width 48 is a tiebreak problem, rank 3,000 is a ranking problem), **STALE** (refused by the closed
set, a `--push-closed` finding), **cut-early** (dropped by the interim trim inside the depth), and
**absent** (never generated, which can only mean the parent was already gone). Aliveness is asked of
the *playfield*, not of the state: every pose in a closure offers the same board changes, so the exact
state routinely drops out at one depth and comes back two later. **Losing the board is the loss that
does not come back.**

Two traps in reading its output:

- **Each row carries two numbers**, `d=` (beam depth) and `at=` (how far along the *line* the frontier
  is), and they are not the same — a run emits several board changes at once. The report was
  depth-indexed until session 20, which meant that under `--push-shot-run` it called a line it was
  following perfectly `STALE`. Read the last line (*followed to depth N of M, lost at K*), then the
  row at K.
- **The `line-h` column is not a distance** unless you say `--push-eval work`. This layer's default
  key is layer 4's learned model, so the number is a seventeen-feature score in which `work` is one
  term and everything layers 5-8 add is added outside it. The tell: a line that ends on the flag does
  not end at 0 — level 9's winning line ends at 70.
- **`--budget-ms` matters here** and its default of 4 s will bite: without it the instrument stops
  after a quarter of a million nodes and reports the line lost at depth 2 when nothing of the sort
  happened.

**What it said about level 1 was one row long: the line dies at the first board change, ranked
130 of 156.** Not at the 12-event ascent this layer was built for — before the search had done
anything at all.

**The cause is that the trim was counting the wrong thing.** A successor here is (board change, the
pose it was fired from), and one board change is reachable from *every* pose in the closure: level 1's
root closure is 158 poses offering **4** distinct changes, so the expansion emits 156 successors that
are four boards wearing thirty-nine hats each. `Cut` ranks them by a heuristic that depends on where
the tank stands and fills all 48 slots with poses of one or two boards. A `boards=` column added to
`--push-trace` reads **1 to 9 distinct playfields at width 48**, and exactly one at three depths.
Each duplicate then bought its own ~4,500-call closure to re-derive what its twin had already
produced. **That is where the layer's budget had been going all along**, and it re-explains every
"narrower is better" result before it: narrow was buying fewer duplicates, never focus.

**`--push-per-board N`**, default 1, caps poses per playfield and lets the frontier come out narrower
than the width — a depth offering six distinct boards should cost six closures, not forty-eight.
Poses are not interchangeable in general (a board change can cut the map in two with the tank on one
side), which is why it is a cap rather than a dedupe. With the trim fixed, three defaults moved and
each was paid for by a measurement:

| | ferry bench | deep bench |
|---|---:|---:|
| layer 5+6 as session 17 shipped it (width 48, per-board 0, `work`) | 11/50 | 14/50 |
| per-board 1 | 14/50 | 17/50 |
| + the learned key (level 1's line becomes only **6** board changes uphill, inside p90) | 15/50 | 19/50 |
| + width **8** (300 gives 8/12; 4 gives 17/20; 16 gives 18/20) | **19/50** | **21/50** |
| + restarts buying width *alone* | **20/50** | 21/50 |
| *layer 0, for scale* | — | *13/50* |

Narrow and deep for the third time in this project. `--push-depth` went to `MaxKeys` after seven of
the ferry bench's fifty stopped at the old 400 cap. And the restart had to buy width *only*: doubling
the per-board cap with it had level 1 running at width 128 over eight distinct boards by the fourth
restart — the duplicates quietly back.

**40/40 verified through both engines** across those two benches, and the old configuration still
reproduces its own two numbers from its flags, so the delta is the change and not the machine.

**The interactive driver's push rung was getting *weaker* every round**, which is half of why "level 1
survives five rounds" kept coming back. The ladder doubles a rung's width per round, which was
written when the default was 300; against a default of 8 it meant round 5 ran at 256 with the read
off — benched as the ladder actually ran it, **11/50 against the default's 20/50**. It now grows
restarts instead (6 and 36 both score 20/50, so it is free) and turns the read on.

### Layer 6 — the read  ☑

**The brief was Michal's, and it is the design.** *"Level 4 is a great example of a very easy level:
as a player, I immediately see I have to make a bridge somehow, I see the block, I know I have to use
the mirrors and avoid the ATs. Or level 6: no antitanks, only blocks and water, I instantly know
there will be long sokoban shit. This kind of analysis is what we need over an application of five
different Knuth algorithms."*

Layer 4 measured how far ranking states goes and the answer was *not much further*. What a player
does in two seconds is not a better sort of two hundred successors; it is a derivation of **which
successors exist for a reason**, done before the search starts.

**The two halves, and the discipline is layer 2's:**

- **What must change** is a model: the priced Dijkstra from the flag, stopped at the cells the tank
  demonstrably stands in — layer 2's `FrontierObstacles`, unchanged.
- **What can change it** is not a model at all. Every board change the tank can make right now is
  enumerated by *making* it: a PF-preserving pose closure, then all five keys from every pose, and
  whatever `Game.PF` comes back different is an **effect**, carrying the pose and key that produced
  it as its witness.

Nothing in `Analyze.cs` knows that a laser bounces off a mirror, that a block sinks in water or that
a conveyor carries the tank. **Level 4's three-mirror bank shot is discovered because firing left from
(7,15) was tried and the block at (2,2) moved.** It cannot drift from the engine because it *is* the
engine.

**Four derivations of "this change advances":**

| | what it is | derived from |
|---|---|---|
| `on the barrier` | the change lands on a cell the Dijkstra named | cell intersection |
| `toward` | it moves a block nearer a water cell the route crosses | block delta + manhattan |
| `opens` | after it, the tank can stand somewhere it could not stand before | a second pose closure |
| **`enables`** | after it, the tank can make a board change it could not make before | one more ply of the same enumeration |

`opens` needs no theory of the obstacle at all — stopping a conveyor ride by shooting a block onto it,
killing the anti-tank that owns a corridor, blowing a brick out of a doorway all come back as
*somewhere new to be*. Capped (`--read-opens` 64) because it costs a closure per effect; past the cap
the question is simply not asked, so a missing label is never a wrong one.

**The verdict is a decision list, not a classifier**, and every fact it tests was derived rather than
pattern-matched — "no shot on this board does anything" is the enumeration coming back with zero shot
effects, not a scan for bricks. On `LaserTank.lvl` 1-9 it reads: 1 FERRY×4, 2 RIDE, 3 FERRY×2,
4 FERRY×1 (*"1 water cell, 1 block, moved by shooting and every shot that moves it is mirror-routed;
2 anti-tanks named by the route"*), 5 FERRY×2, 6 SOKOBAN×6, 7 FERRY×1, 8 FERRY×3, 9 GAUNTLET. That is
Michal's read of 4 and 6 in the machine's own words, derived in 166 ms, process start included.

**What the whole corpus looks like through it** (`--analyze-tsv` over the same 4,185-level stride
sample, 64 seconds, joined against the chain's solved set):

| verdict | levels | solved | rate |
|---|---:|---:|---:|
| FERRY | 2,073 | 104 | 5.0% |
| GAUNTLET | 828 | 138 | 16.7% |
| DEMOLITION | 360 | 40 | 11.1% |
| SETUP | 324 | 30 | 9.3% |
| RIDE | 312 | 61 | 19.6% |
| SOKOBAN | 161 | 10 | 6.2% |
| OPEN | 87 | **84** | **96.6%** |
| WALLED | 40 | 5 | 12.5% |
| **all** | **4,185** | **472** | **11.3%** |

*(Layer 8's barrier fix later moved 179 of these rows, 164 of them out of GAUNTLET: 828 → 664,
DEMOLITION → 457, SETUP → 391.)*

- **OPEN at 96.6% is the sanity check** — when the flag is already movement-reachable the solver
  essentially always wins, and the read agrees about which levels those are.
- **Half the corpus is a ferry** (FERRY + SOKOBAN = 53%, solved at 5.1%): where the corpus is, and
  the worst-performing non-trivial class.
- **The number of fills is the difficulty**, the first quantity that predicts the rate monotonically:
  1 fill 9.9%, 2 fills 5.7%, 3-4 1.8%, 5-8 1.7%, 9+ **0.7%**.

**Measured against the humans, which is what decides whether to search by it.** `--read-dump` replays
each winning recording, stops at every board change, and asks whether the change the human made next
was one the read named. Between two board changes the tank only *moves*, so the answer is yes-or-no
by construction.

| over 20 hand recordings | |
|---|---:|
| board changes | 800 |
| the human's change was in the enumeration at all | **800/800** |
| named by barrier / toward / opens | 666 = **83.2%** |
| **+ `enables`** | 777 = **97.1%** |
| of the 134 nothing named, `enables` names | **111** |

100% coverage is a check on the enumeration, not a finding — the enumeration *is* the engine, so
anything else would have been a bug (and it also says the pose closures never truncated). Before
`opens` the read named 53.7%.

#### The read inside the search — as a tier, never a term

`Cut()` sorts on `Tier` before `H`, so the read can say "these successors exist for a reason and the
rest are filler" without reordering anything inside either group and without being able to admit a
successor the expansion did not offer. Same contract layer 4's `Rank()` has. A tier rather than a
number because the read's answer *is* a set — "this shot lands on the brick that is in the way" is not
three points better than a shot that does not.

**The finding that made it work is a joint one about the filter and the width.** The corrected trace
said the read promotes 5-11% of successors at shallow depth into a beam of width **300** —
`Cut(next, 300)` on a frontier of 248 does nothing at all. So the read could not help, and the
Dijkstra it costs made it fractionally worse, which is exactly what the first benches said. At width
48 on a banked ferry population it is worth **4/50 → 11/50**, and one level on the deep bench. **A
tier that names 5% of successors is useless at a width that keeps 100% of them, and a width that
keeps 5% of them is useless without something to say which 5%.** (This is also where
`ferry-levels.txt` comes from: the deep bench cannot decide a ferry question, and the read itself says
so — it scores 3 ferries in the 8 of its 50 that the stride sample covers.)

Two false starts, both ended by the same instrument reporting **0% promoted**: the cheap `opens` proxy
asked whether the *flag's* passable component grew where the 86% came from asking whether the *tank*
can stand somewhere new (`Heuristic.TankRegion` is the fix — during a ferry those are opposites), and
then the counter itself was incremented before the `opens` pass ran.

#### `enables` — the fourth derivation, and it solves level 1

Level 1 turns three roto-mirrors five times before its first fill. Those five moves open nowhere new
to stand, land on no barrier and move no block, and **every ranking key this project has is flat
across all of them.** What they *do* is put shots on the board that did not exist a moment ago — a
question the engine can be asked without knowing what a mirror is. Deltas rather than resulting
boards: "shoot the brick at (4,3)" is the same change whether or not an unrelated roto has since
turned.

Level 1, derivation by derivation, on its first ten board changes — and this is the finding:

| derivation | successors it names | times it named the human's move |
|---|---:|---:|
| on the barrier / toward | **0 of 7** | 0 of 10 |
| `opens` | 4 of 7 | 3 of 10 |
| **`enables`** | **3 of 7** | **9 of 10** |

So `opens` is not inert here, it is *wrong*, and when `enables` is on `opens` moves behind it. **A
derivation that is more selective *and* more accurate belongs in front of one that is neither.**
It is a tier of its own rather than a promotion because it names **79%** of the successors offered —
folded into `TierAdvance` it would only dilute one that works.

Two things had to be fixed before any of it could be seen, and both are the same lesson twice. The
pass was written after `ReadTier`'s two early returns, so only one path reached it and **it benched as
entirely inert through a whole round of measurements**. And at full price it cost ~4x an expansion and
took the ferry bench 20/50 → **12/50** — right shape, wrong price, for the third time in this layer's
life. The fix is session 18's finding turned round: **a tier can only matter at a width the tiers
above it have not already filled**, so the pass counts what the depth has promoted so far and returns
if that covers the width. On a ferry level the read fills a width of 8 inside the first parent or two
and the pass is skipped; on level 1, where the free derivations say nothing all level, it is asked
every time. 12/50 → 19/50 on the gate alone.

**Then the level fell.** `--push-beam 48 --push-read --push-enables 8` solves `LaserTank.lvl` 1 in
**6.19M nodes and 17.9 seconds**, 294 keys against the record's 149, 1.97x, verified and banked.
Against four 800M-node runs that did not touch it. `--push-line` says why: the human line now
survives to board change **11** where session 18's configuration lost it at 5 and the pre-fix trim
lost it at 1 — past all five rotations and past the mirror shot they set up.

**It is not one level, and the control is what says so.** Over `LaserTank.lvl` 1-19 at 60M nodes, run
twice: the derivation solves 1 and **4 (The River Nile, 49.9M)** that the same budget cannot solve
without it, while 3, 7 and 11 come out either way. 4 of 19 against 3 of 19, so **the honest claim is
two levels**. 4/4 verified, p50 1.7x the record.

**What it costs, and how it ships anyway.** Solo it is 19/50 ferry and 19/50 deep against the plain
rung's 19 and 21 — a level or two worse, which is the whole case for `--push-enables` being off by
default. Then Michal made the objection that mattered: *"we have to somehow incorporate this into the
autosolver — the user won't know to fine tune random parameters for specific levels."* Switching it on
inside the existing push rung makes a rung tuned to its best measured setting worse on an argument
with nothing measured behind it. The driver is a **portfolio**, so the answer is its own rung — and
the moment it is a portfolio member the numbers have to be read as a union:

| | solo (ferry / deep) | union with the plain rung | it adds |
|---|---:|---:|---:|
| plain push rung | 20 / 21 | — | — |
| **+ `--push-enables 8` at width 8** | 19 / 19 | **24 / 26** | **+4 / +5** |
| + the same at width 48 | 14 / 18 | 21 / 26 | +1 / +5 |

The rung runs at width 8 while the rounds are cheap and widens to 48 from round 3, which is where
level 1 lives. The test that matters is the one a user would run, with no flags anywhere:
`lasertank-solve.exe data/levels/LaserTank.lvl --from 1 --to 1` → **solved in 67 seconds at round 3**,
banked and verified without being asked.

`--push-enables-poses` (default 32) caps the child closure the question is asked from: truncating can
only *lose* poses, so it costs promotions and cannot invent them, which is what makes a small default
safe. At 32 the level-1 line is held only to board change 4 where the whole closure holds it to 11 —
and the level solves at either, because a search does not have to follow the human's line to win.
Raise it when reading a `--push-line`, leave it when solving.

### Layer 7 — the stop cell  ☑ (own rung)

**The trigger.** *"The solver is still stuck on level 2. From a human perspective this level is
trivial: I instantly see that I have to shoot the boxes to block the conveyor belt."*

`LaserTank.lvl` 2 is a single conveyor loop with the flag in the top wall and the tank penned into the
bottom two rows — every way out is a ride that never stops. The read calls it **RIDE**, which is 7.5%
of the corpus and the second-best-solved shape at 19.6%, so the shape was never the problem. Three
things were:

**One: a laser ferry cost a depth per cell.** `PushRun` compresses a *drive* push — the tank travels
with the block, so pressing the same key again continues it, and a k-cell ferry is k successors of
**one** expansion. A shot leaves the tank where it is, so nothing compressed it. Level 2's hand
recording is **32 board changes of which 26 are a repeat of the shot before**, on a `WorkDistance`
that goes 13 → 11 across the entire level: a 32-deep breadth-first search on a flat key.
`--push-shot-run N` is PushRun for the laser and makes it six.

**Two: `--push-line` was counting the wrong thing** (the `d=`/`at=` fix, above).

**Three, and this is the layer: no ranking key in the project moves when a block gets nearer the cell
that would stop the ride.** A ferry level's route crosses water and `RouteFerry` prices it. A RIDE
level's route crosses a *conveyor*, which the price list charges 1 for and the Dijkstra walks straight
over — and the tank still cannot follow it.

`Heuristic.RouteStop` is that term, and **getting it right took five wrong versions, every one of
which the beam found and sat on.** They are worth listing because each is a different way for a
relaxation to lie — and this is the most transferable thing in this file about writing one:

1. **Price every conveyor on the route.** It *rose* along the winning line, 13 → 36 → 107, because
   placing a block reroutes the Dijkstra through fresh conveyors faster than it satisfies old ones.
   Being carried off the priced route is not a failure — the ride usually arrives somewhere useful the
   long way round, which is what a conveyor level *is*. What the route cannot dodge is the **last
   step**: a drive is a key consumed while the world is quiescent, so on the cell it drives into the
   flag from, the tank has to be standing still. Scoped to that one cell, the line became a descent,
   25 → 12.
2. **Manhattan to the nearest block.** The beam shoved a block up column 15 to a cell two away and
   infinitely far by push, because moving it back needs the tank off the board. Replaced by a backward
   BFS over block positions.
3. **A dead-end branch of the chain returning "free".** Stopping next to the flag is not the same as
   *getting* there: a cell no conveyor feeds has to be driven into, from a neighbour the tank must in
   turn stop on. A branch with nowhere to arrive from returned 0, and the beam parked on a board whose
   two spare blocks were both stuck in row 2 with every way in priced at nothing.
4. **Spending one block on two requirements.** A block already paying for a requirement is now
   reserved, and is a wall to the BFS rather than a candidate.
5. **`Passable` standing in for "the tank can stop here".** A block on (14,1) is one cell from (13,1)
   and can never get there: the only square behind it is a conveyor. The push test now needs a cell on
   the ray behind that the tank can actually **stand** on, precomputed as one sweep per line per
   direction.

**And a bug nobody put there this session.** `WorkDistance`/`FlagDistance` returned **0** — the best
score there is — when no flag is on the board, on the reading "nothing to steer by". A flag leaves PF
for exactly one reason: something was pushed onto it. With a shot run a block goes up column 14 in one
run and lands on the flag, and at width 128 *all 128 boards in the frontier were that board*, scoring
4 against the winning line's 11. Fixed to `Unreachable`. Cost: one ferry-bench level, attributed by
reverting it alone.

**New instrument: `--push-trace-board`**, which prints the best node's playfield under `--push-trace`.
Three of the five wrong versions above were diagnosed by *looking at the board the beam had settled
on*; `best=10` says the key has gone flat and says nothing about *what* the beam is looking at. It is
what found the buried flag, and it is the first thing to reach for on a flat key.

**The ablation at 60M nodes on level 2** — width 128 + `--push-stop 1` + `--push-shot-run 16` solves
it in 2.03M nodes and 44 s; without the shot run, without the stop term, at width 8 or at width 64 it
is unsolved at 60M (at width 256 it solves in 4.11M). All three ingredients and the width. Nothing
here is a preference.

Solo it is 18/50 on both benches against 19 and 21; as a portfolio member it **adds 3 ferry and 5 deep
levels**, so it is its own rung, `push-stop`. `--from 2 --to 2` solves level 2 at **round 2 in 65
seconds with no flags**, 90 keys against the record's 72 (1.2x). `--push-shot-run` is off by default
for its own measured reason rather than by association: solo it is ferry 15/50 (from 19) and deep
23/50 (from 21) — a wash, so it lives in the rung that needs it.

**What is left undone here.** The chain is conveyors only. `--analyze`'s RIDE verdict says "a
conveyor, a slide or a tunnel", and ice is the same shape — you slide until you hit something — but
the direction of travel on ice depends on how the tank entered, which is a second question.
`StopChain` is capped at 3 and the reservation list is not unwound per branch (over-reserving makes a
state look dearer, which is the safe direction).

### Layer 8 — what a player reads off the board  ☑ (two rungs; solves level 8)

**The trigger.** *"Onwards with the level solver — I'm stuck on levels 6, 8, 9 and 10. All the levels
have manual playthroughs in demos."* Four levels, three shapes, and the first useful thing was to stop
treating them as one problem.

**The framing measurement, and it decided where to spend the session.** One push expansion is a whole
closure, so the honest question about a long level is whether its *budget* or its *ranking* is
binding, and `--push-trace` answers it directly: `closure~` × width × board-change count is what a
perfect beam would cost.

| lvl | board changes | closure | a perfect beam at width 8 | at 80M nodes |
|---:|---:|---:|---:|---|
| 6 Cascade | 168 | ~800 | 3.5M | unsolved |
| 8 castle siege | 52 | ~100 | 0.3M | unsolved |
| 9 Grid Lock | 27 | ~100 | 0.3M | unsolved |
| 10 Valley of Death | 53 | ~500 | 1.0M | unsolved |

**All four were ranking-limited by two orders of magnitude at the shipped width**, which is why
sixteen configurations at 80M nodes solved none of them — and one `--push-trace` column would have
said so before the eighty-million-node grid that did not. Read the last column as the warning it
became, though: it is the cost at *width 8*, and every level that fell in the end fell at a width
between 512 and 19,200. Ranking-limited was the right diagnosis; "therefore not budget-limited" was
not the right corollary — fixing the ranking moved the binding constraint to width and put it straight
back into the budget.

**One: an anti-tank on the route is a wall, not a threat** (a correctness fix, and free).
`FrontierObstacles` returns two kinds of cell interleaved: a cell **of** the route that costs more
than an empty step, and — for a route cell that costs nothing and is still not reached — the
anti-tanks *aligned* with it, which are targets rather than terrain. Both callers dropped every
anti-tank to keep the second kind out of the barrier, and threw the first kind out with it. Level 9
makes it visible because **every wall on it is an anti-tank**: a 4×4 grid of rooms whose partitions
are rows of `^>v<`, none of which can ever fire, because the only cells they look at are each other.
The read said *nothing at all for the whole level*. Fixed via an optional `onRoute` flag array: both
benches unmoved, `--read-dump` 667/800 against 666, and 179 of the 4,185-level sample (4.3%) change
verdict.

**Two: the fire map, which is `AntiTank()` asked of every cell at once** (`--push-fire N`). A ferry
level's obstacle is terrain and the price list sees it; a gauntlet's obstacle is fire, and nothing here
could see that at all — level 10 is an empty 16×16 field with ten anti-tanks along the walls,
`WorkDistance` 16 with **nothing in the way**, and the tank can stand in eighteen cells of the board.
`Engine.AntiTank()` decides whether an anti-tank fires by walking outward *from the tank's own cell*
with `CheckLoc` and asking whether the first cell the tank could not enter is an anti-tank pointing
back. That reads nothing but the board, so the answer for all 256 cells is four line sweeps.
`Heuristic.BuildFire` is that and nothing else, down to repeating `CheckArray` verbatim as `Enters` —
**`Passable` is the wrong table here and the difference is water**, which a tank may be told to drive
into, so an anti-tank scan looks straight over a lake. Deliberately not `Threats()`, which stops only
at Solid: that superset charges for fire that never comes and cannot see the mechanism the gauntlets
are built on — a block, a mirror or **another anti-tank** dropped into a lane stops the scan and
shields everything behind it. Level 10's hint is *"move the bottom right tank left 8 spaces then up
4"*, which is a player saying exactly that. Checked against the engine rather than asserted: on levels
8, 9 and 10 the fire-aware flood and `--analyze`'s *executed* pose closure agree exactly (3, 6 and 18
cells). Priced *inside* the route Dijkstra so the route goes round the fire rather than reporting how
much of it a blind route crosses. Level 10's ascent: **29 board changes → 6**.

**Three: the safe flood, because a sum can be lowered without progress** (`--push-reach`). The fire
price alone is not enough and `--push-trace` says why in one column: level 10's best score went 28 → 23
in one board change and then **sat at 23 for fifteen depths**, shuffling anti-tanks around the edges.
`--push-reach` prices the route from the flag to the nearest cell of the **fire-aware flood from the
tank** rather than to the cell the tank stands on — layer 2's premise as a ranking key that has to
answer per successor. It is a hard test: a swept cell is not in the flood at all, so the number moves
only when somewhere new becomes safe to stand. The flood is deliberately pessimistic (four-neighbour
over `Passable`, so ice, conveyors and tunnels are crossings it does not know about): under-counting
the reach costs search order, over-counting would report a level as good as won.

**Four: the frozen block** (`--push-dead N`, and a tier). Level 6 is a Sokoban with six blocks and six
holes, and **the beam lost it on its first expansion**: `--push-trace-board` printed a block shot into
a pocket with walls on three sides and, on the fourth, a cell no tank can ever stand behind — scored
**68 against the root's 73**, because filling holes is all `WorkDistance` can see and a block in a
pocket is out of the way. *Can this block still be moved?* is answerable from two things already here:
the far side is `CheckLoc` (`Engine.cs:797` pushes with exactly that test) and the near side is
`BuildRays`' `_rayOk`, layer 7's sweep — somewhere behind the block, nothing impassable between, a cell
the tank can come to rest on. It errs towards *alive* everywhere it errs, which is the only safe
direction for something that says a level is lost. `RouteDead` is then the water cells on the route
with no live block left, and it is a **cliff rather than a gradient**, zero on nearly every board in
the corpus, which is the point. **A weight was not enough:** at `--push-dead 20` the beam's best score
*rose* 155 → 300 over sixty depths, because once all eight boards it held had frozen a block, so had
every successor of every one of them. `TierLost` — a tier below the truncation escape hatch, set at
emission — is what actually acts, and it is still only an ordering, so a conservative test can never
refuse a level. With it the rise stops dead: 155 → 152, flat.

**Five: the ferry as an assignment, through the maze** (`--push-ferry-match`, `--push-ferry-maze`).
`RouteFerry` is "for each hole on the route, the Manhattan distance to the nearest movable block", and
on a Sokoban both halves are wrong. Nearest-per-hole lets **every hole name the same block** — six
holes and six blocks read as one carry, and finishing that carry barely moves the number;
`--push-ferry-match` spends each block once, greedy over the smallest remaining pair (the classic
Sokoban lower bound; greedy rather than a real minimum-cost matching because this only orders states
the engine already produced). And **Manhattan is wrong about walls** — level 6's block at (11,2) is
thirteen cells from its hole as the crow flies and forty through the corridors, so the term was
rewarding shoves at the wall between them; `--push-ferry-maze` measures it with a BFS over cells a
block can occupy. That is still not a pushability search — whether a block can *actually* be pushed
along a route is the `MoveObj` question this project will not answer for a tie-break — it just stops
the estimate being wrong about the maze. Two ways to get it wrong, both found by the beam: water is
not traversable in that BFS (a block pushed into a lake sinks), and level 6's six holes are a *strip*,
so four of them had no block reachable at all and the term collapsed to a constant; and a hole with no
block matched to it returned **0**, which is the buried-flag bug wearing a different hat (unmatched
holes are now `Unfillable`). Worth on the level it was built for: level 6's ascent **30 → 11**, deepest
rise 35 → 7, the human line's score a descent 156 → 3, and `--push-line` goes from losing the line at
board change 2 to following it to **26**.

**Six: RouteFerry for fire** (`--push-shield N`) — **the one term that did not earn a rung**, recorded
so nobody re-derives it. `--push-fire` says what is wrong with a gauntlet's board and `--push-reach`
refuses to walk onto it; neither says anything about the twelve board changes it takes to *fix* it, and
level 10's hint is a player spelling those out. So: take the first swept cell the route has to cross,
ask which anti-tank covers it (`AntiTank()`'s own scan order, right/left/down/up, first match wins,
because quirk #5 is that only the first one fires and the order is the rule rather than the distance),
and price the nearest pushable object against the nearest cell on the ray between the two. One
requirement at a time, which is version 1 of `RouteStop` paid for in advance. **It still went in wrong
once, in the way this file has now been bitten by three times:** an unshieldable cell priced at a
constant, by false analogy with `Unfillable`. The analogy is false — a hole *must* be filled, while a
swept cell has alternatives the route price already scores. At 40 it put a 40-point cliff in the
middle of level 10's winning line and the two halves of the measurement disagreed in the tell-tale
way: ascent 12 → 6, deepest rise **1 → 34**. Zero is the answer. Corrected, at weight 1 it takes level
10's ascent 12 → 7 (past 1 it is worse than nothing, the same shape the ferry weight has) — and it
adds **zero** levels to the three-rung portfolio on either bench while losing three ferry levels solo.
A shorter ascent on one level and no union movement is exactly the evidence that says *keep the flag,
do not spend a core*.

**Seven: the ranking key splits per level.** `--push-eval` defaults to `learned`, in which `work` is
one term at weight 157 and everything layers 5-8 add is added outside it; on `--push-eval work` the
key is `WorkDistance` plus the terms and therefore reaches 0 on a win.

| lvl | ascent, `learned` | ascent, `work` + the terms |
|---:|---:|---:|
| 6 | **11** | 21 |
| 8 | **11** | 32 |
| 9 | 8 (of 84 → 70) | **7** (56 → 1) |
| 10 | 29 | **12** (15 → 0) |

6 and 8 want the learned key, 9 and 10 want the raw one — the same portfolio argument every layer since
4 has ended in, and why layer 8 ships as **two** rungs.

**What the whole of it is worth**, read as a portfolio member against the plain push rung's own solved
set at 4M nodes:

| configuration | ferry, solo | adds | deep, solo | adds |
|---|---:|---:|---:|---:|
| `push` (the shipped rung) | 19/50 | — | 21/50 | — |
| **`push-ferry`** — reach + fire + dead + match + maze, learned, width 128 | 17/50 | **+3** | 21/50 | **+6** |
| **`push-ferry-work`** — the same on `--push-eval work` | 18/50 | +4 | 21/50 | +6 |
| all three as a portfolio | **23/50** | | **29/50** | |

Six deep levels is the largest addition any specialist in this ladder makes, and the two keys are not
redundant: `push-ferry-work` adds one ferry and two deep on top of `push` and `push-ferry` together.
Both rungs widen from 128 to 512 at round 3 — that half is `--push-line`'s number rather than a
bench's (level 8's human line survives to board change 7 at width 128 and to 15 at 512), because a
bench at 4M nodes cannot say anything about a width a round-3 budget pays for. And they raise
`MaxKeys` from 1,200 to 5,000: level 6's *hand* recording is 904 keypresses and a solver route is
longer than a human's, so on this population the search was quietly discarding the end of the level
and reporting `budget`.

#### Levels 8 and 9 are solved, and the credit goes to different places

| lvl | what solves it | nodes | width | keys | ratio |
|---|---|---:|---:|---:|---:|
| 8 castle siege | `push-ferry-work` | **57.5M** | 512 | 335 | 1.5x |
| 9 Grid Lock | **layer 0's beam, round 5** | **94.3M** | 19,200 | 294 | 5.0x |
| 9 Grid Lock | `push-ferry-work` also gets it | 162.8M | 2048 | **127** | **2.2x** |

Both verified and banked under `data/solutions/LaserTank/` — level 9 from the layer-8 run, because 127
keys against 294 is not close. (Either may be temporarily absent from that directory: see *Next
actions*, item 3.)

> **Session 24: the shorter files item 3 pointed at are gone.** `build/w/` is empty — `build/` is
> gitignored and did not survive the reorg — so `build/w/b9-b/…` (114 keys) and `build/w/w8-2048/…`
> (308 keys) no longer exist and the 114-key level-9 route is lost unless it is re-derived. **And an
> unattended driver run re-banks the *worse* level-9 route**, because the beam reaches its win at
> 94.3M nodes while `push-ferry-work` needs 162.8M: the beam always gets there first and cancels the
> rest. That is now the one case where the driver cannot produce the solution this table credits, and
> it is why the round's winner is chosen by *length* rather than by ladder order — see *The
> interactive driver*. Measured while re-checking it: the beam's route is **73 board changes** and
> `Replan.Improve` cannot touch it (294 → 294 at width 64 and 50M nodes, terminating on `done` rather
> than on budget), against **26** for the hand recording in `data/demos/`, which itself polishes to
> **96 keys / 1.63x** — better than either solver route. A 5.0x solution is a bad *route*, and no
> post-processing is a substitute for finding a better one.
**Level 8 is layer 8's** (unsolved at 400M at width 512 on
the learned key, at 150M at width 128 on either, and at 80M without the new terms), and
`--from 8 --to 8` with no flags lands it at round 4 in 9m51s.

**Level 9 was never layer 8's, and that is worth saying loudly.** It falls to `--no-ida --beam 19200`
with **nothing else at all** in 94.3M nodes — measured directly, not inferred from which rung won the
round — which is the driver's round 5, exactly one round past where the run that asked for it had
stopped. **Before deriving anything for a level, let the driver finish a round.**

Both steps of the rungs' width ladder are still paid for by a level rather than an argument: 128 → 512
because level 8 needs 512, and 512 → 2048 at round 5 because layer 8's route to 9 needs 2048 and is
unsolved at 400M at 512 *and* at 100M at 8,192. That last pair is the part worth keeping — the width
that works is neither the smallest that does nor the largest available, because at 8,192 the same
budget buys a tenth of the depth.

#### What has not fallen, stated plainly

**Levels 6 and 10 are not solved.** Twenty-two configurations at 60M-150M nodes, across every
combination of the derivations, five widths from 8 to 8,192, both ranking keys and the run compression
— and 8 and 9 both needed 400M-node runs at widths those twenty-two never reached. What moved on the
two that remain is every number that says whether they are *reachable*:

| lvl | ascent, before | after | `--push-line` follows, before | after | |
|---:|---:|---:|---:|---:|---|
| 6 Cascade | 30 | **11** | 1 of 168 | **26** | open |
| 8 castle siege | 11 | 9 | 3 of 52 | **15** | solved |
| 9 Grid Lock | 8 | **7** | 2 of 27 | 5 | solved |
| 10 Valley of Death | 29 | **6** | 3 of 53 | **13** | open |

**The two solved rows are the warning about reading this table too hard.** Level 8's 3 → 15 was what a
level about to fall looked like hours in advance; level 9's 2 → 5 was the *least* movement of the four
and it fell too, on width. So the ascent and the line survival say when the ranking has stopped being
the binding constraint — **they do not rank what is left.** Level 6's numbers are the best of the two
that remain and it is the one furthest from falling.

And the level-9 lesson was applied to both: `--no-ida --beam 19200` — layer 0 alone at round 5's width
and budget — is **unsolved on 6 and on 10 at 409.6M nodes**. So neither is one more round. (Going
wider is not a thing the driver can do: the beam rung caps at 19,200, and a 76,800-wide run was 1.1 GB
resident and climbing.)

Where the two that are left now stop, measured rather than guessed:

- **6** is a 142-push Sokoban and the beam is a greedy level-synchronous beam. With the assignment key
  its own best descends 184 → 95 and fills two of the six holes, then it is in a region where every
  successor of every board it holds is worse. `--push-ferry-stage` (holes left first, cheapest carry as
  the tie-break) was the attempt at forcing one carry at a time; it fills one hole and stalls. The
  shape that is missing is layer 2's, one level out: **commit to one (block, hole) pair, search only
  for that, then re-derive** — a subgoal chain over board changes rather than one beam over the level.
- **10** has the cleanest ranking profile of the four — a descent 15 → 0 with a deepest rise of 1 — and
  is now most clearly **budget**-limited. Its closures are ~1,000 poses against level 9's ~150, so
  width 2048 costs ~10M nodes a depth and one pass over the level is 360M; three 400M runs came back
  never having finished a pass. Width 512 is ~90M for the level and fails on ranking.

  **And "it wants about a billion nodes" has now been tested and is not enough.** Two runs left going
  at the end of session 22 came back in session 23: **900M nodes each, 1h37m each, both `budget`,
  both unsolved**, and the frontier had reached board-change depth **2**
  (`build/w/h10-1k.jsonl`, `build/w/h10-1ks.jsonl`; the directory names are the only surviving record
  of their flags, so read them as width 1024 with and without `--push-shield` rather than as an exact
  configuration). Depth 2 of a 53-change level after 900M nodes is the same failure the 400M runs at
  width 2048 had, one width down: **the pass never finishes, so the budget arithmetic above is the
  thing that is wrong, not the size of the budget.** At ~10M nodes a depth a single pass at width 1024
  is already ~500M, and a beam that has to hold 53 depths of that is not a purchase, it is a different
  search. The honest next move on this level is therefore *not* a bigger run: it is either the
  decomposition level 6 wants (commit to one shield-and-anti-tank pair, search only that, re-derive)
  or a cheaper closure, because the ~1,000-pose closure is what makes every width expensive here.

---

## The interactive driver

`build/lasertank-solve.exe FILE.lvl [--from N] [--to N] [--lanes N]` — a bare `.lvl` and nothing else
required. It walks the collection in level-number order and stays on each level until it falls or you
press a key.

**It is the portfolio the campaign could not afford.** A round runs every searcher *at once, one per
thread* — layer 0's beam (with IDA* on round 0, where a probe is cheap), layer 3's subgoal beam, layer
4's learned ranking of it, layer 1's macro beam, and layers 5-8's five push rungs — and the first win
cancels the rest. In a campaign that trade is a loss, because every node a specialist spends is a node
taken from the raw beam; here a specialist spends a *core*, and one level at a time means the cores are
there. If nobody wins, the node budget quadruples and the round repeats — 400k on round 0, about a
second; 400M on round 5, about an hour — and rounds also widen what only widening helps: the raw beam
doubles its width (a `beam-dead-end` has nothing to do with a bigger budget), the subgoal beam gets six
more restarts, and the push rungs step their widths where a level has paid for the step.

**The round's winner is the shortest solution, not the first one in ladder order.** More than one
rung crossing the line in the same round is common — the stop bit is polled every 120 ms and a rung
already past its search and inside `Clean()` never sees it — and the routes they bring back are not
equally good. Choosing by ladder index made that a coin toss dressed as a policy; everything compared
here has already been through `Clean()`, so it is two finished solutions being compared on the number
the result line prints. Ties keep the earlier rung, which keeps the choice deterministic. *This does
not rescue `LaserTank.lvl` 9* — there the beam is the only rung that solves at all before it cancels
the others — but it stops the ladder's shape from silently deciding which of two winners is banked.

**The two-engine gate is not optional here, it is the write path.** A win goes to a scratch `.lpb`,
then to `tools/verify_solutions.py` (which grew a `--levels` argument so one candidate can be checked
against a named `.lvl`), and is moved into the output directory only if the frozen C oracle and the C#
core both report WIN with byte-identical traces. A solution that fails is deleted and the search
carries on — loudly, because after Phase 3 that can only mean an engine divergence. Missing engines or
no python is a startup error, not a discovery made six levels in.

**The driver writes to `data/solutions`, not `build/`.** A campaign's output is disposable
(regenerated by `tools/campaign.sh`, thousands of files, gitignored); the driver's output is one
hand-supervised level at a time, already through the gate, on levels the batch solver could not do.
Those are worth committing, so they go where git can see them, next to `data/demos/`.

**`--lanes N` works N levels at once** (default 1, so a bare run is exactly what it always was). The
ladder is nine rungs and this machine has sixteen cores, so one level left several idle — and a second
level is a better thing to spend them on than a wider anything, because the rounds already widen what
widening helps. The scheduling policy is one sentence: **every lane draws on the same pool of `--jobs`
slots.** A rung that cannot get a slot waits, and if the level falls while it waits it returns without
expanding a node. The only thing `--lanes` trades is portfolio breadth per level against levels in
flight, and *which* is worth more is a property of the levels, not of the driver. The lane number is
the key that gives up on it; a key naming no lane is ignored rather than guessed at, because throwing
away an hour of the wrong lane's search is not a thing to do on a maybe.

Two latent races had to be closed for lanes: the gate *empties* the directory it stages through, so
each lane verifies in one of its own (two lanes sharing one would delete each other's candidate and,
worse, could hand a lane the other lane's solution to pass off as its own); and `Sweep` deleted
`cand-*.lpb` wholesale, so a second driver could delete a candidate between the searcher writing it and
the gate reading it. Candidates now carry the process id and a run sweeps only its own, plus anything a
day old.

*One trap worth keeping, because it is the whole of why the display is written the way it is:* **a lane
may not write to `Console`.** Two lanes each printing half a level's story interleave into neither, so
a level's lines are built up in the lane and handed to a queue, and the painter on the main thread is
the only writer there is. The block is erased with one *relative* escape (`ESC[<rows>A ESC[J`), which
is what keeps it correct after the terminal has scrolled, and every row is cut to the window before it
is coloured — a row that wraps is two lines on screen and one in the row count, and from there the
block walks up the scrollback a line per repaint.

---

## Post-solve: polish and replan

Both are on by default (`--no-polish`, `--no-replan`) and both run inside `Program.Clean`, shared by
`SolveOne` and `--polish DIR`. The pipeline is **polish, replan, polish again, and keep the replan only
when what comes out is shorter** — measured, because replan-first was a net win that *lost keys on two
collections*: a re-derived route is a different starting point for delta debugging.

### `Trim.Polish` — making a solution read like a person played it

**The complaint, and it is not about length.** *"Get rid of repeated turns in place (like facing north,
facing west, facing south, moving south) and shooting at nothing. They look very computery in the
replays."* All three artifacts are free to the search and so it emits them freely: `MoveTank` spends a
whole keypress turning when the key does not match the way the tank faces and `ScoreMove` only
increments in `UpDateTankPos`, so a turn on the spot costs the *record* nothing; a shot that hits
nothing is one keypress for one node; and `Cut` breaks a tie by cheapest keystream, which makes a state
the beam has already left exactly as good as one it has not — so wandering out and back is free too.

| the artifact | how it is found |
|---|---|
| a round trip | `StateHash` after key *j* equals the state after key *i* — so keys *i..j-1* left nothing behind. Longest first |
| a turn on the spot | a direction key after which the tank did not move and the board did not change, *followed by a different direction key* — which is what keeps the last turn of a run, the one the move needs |
| a shot at nothing | a space bar after which the whole state hash is unchanged |

**Every deletion is replayed before it is accepted, and that is not caution.** A wasted turn is not a
no-op: `AntiTank()` runs inside every key-consuming tick, so a turn on the spot gives every gun on the
board a move, and there are levels whose solution *is* burning a tick so a gun fires early. Measured —
on `Beginner-I` 1488 the run `> > < < > > < <` **survives** the polish, because those round trips
really are the anti-tank timing, while `> v FIRE ^ ^ < FIRE ^ v` collapses to `v FIRE < FIRE`.

It is separate from `Trim.Shrink` on purpose: Shrink is delta debugging, costs thousands of replays and
runs only past `--trim-ratio` (default 10x), whereas this runs on **every** solution because a 1.6x
solution can be just as ugly as a 12x one. An exhaustive contiguous-deletion sweep at every width 12
down to 1 — contiguous rather than a halving ladder because the run Michal actually pointed at is
*five* keys long, which is exactly what 1/2/4/8/16 skips. `Polish` repeats its passes while anything
comes out (`Decycle` cuts one round trip per round and gives up after sixteen, so on a raw solution
with more than sixteen, the long ones nobody reached used to stay in).

**And the bug that made all of it look like a no-op is the most useful thing in this section.** `Trim`
replayed every candidate through *one reused* `Engine`, and `LoadLevel` deliberately does not reset
`wasIce` / `WaitToTrans` / `ConvMoving` / `BlackHole` (quirk #12), so each candidate inherited the
previous one's leftovers and **keystreams that win from cold were reported as losing**. The polisher
was declaring solutions irreducible that were not, and said so twice before Michal pushed back with
the replay: *"it really is not single-key minimal at 71 — step 2 is a useless turn south, step 3 is
visually a noop…"* He was right. A fresh engine per candidate took that solution **71 → 51 keys and
23 → 14 shots** and the deep bench from 27.4% removed to **47.3%** (`Beginner-I` 1488: 831 → 212
keys). Nothing wrong ever shipped — every `.lpb` passes a fresh-engine replay in `SolveOne` and another
in `verify_solutions.py` — the cost was entirely in reductions not found. Recorded as quirk hazard #12
because the rule generalises past `Trim`: **anything replaying candidate keystreams must build its own
`Engine`.**

The split between searchers is the other half: the raw beam barely improves (2.4%), because its `Cut`
already breaks ties by cheapest keystream; the **subgoal beam loses nearly half its keypresses**,
because it searches in shot-space and the movement between shots is whatever the closure happened to
execute.

### `Replan.Improve` — re-deriving the route instead of deleting from it

**The complaint the polisher is structurally unable to answer.** *"In the first part the tank moves the
C4 block to C6 and later back to C4 to move it correctly, which is a waste — I'm thinking there could
be a step like 'now that we know where the blocks are going to end up, what's the least-move way to get
them there?'"* On `LaserTank.lvl` 3 the excursion is four shots and about seventeen keypresses, and
`Polish` cannot see any of it: it is not a round trip (the board is different afterwards — the other
block has sunk in between), not a turn on the spot, not a shot at nothing. Every key in it does
something. They just undo each other. **Deletion cannot find that; only re-derivation can.**

**The observation it is built on is Michal's own** — *"the solution is basically a series of state
changes with walking in between"*. If that is what a solution *is*, the playfields it passes through
are a **ladder of positions already proved to lead to a win** — the solution is the proof. So the
least-move question has a cheap and sound answer: search for the shortest keystream that climbs that
ladder, **allowed to skip rungs**. Nothing is modelled: a rung is reached because `ApplyKey` was called
and `Game.PF` came back equal to a playfield the original run stood on.

| step | what it does |
|---|---|
| the ladder | replay the solution, keep every playfield it stood on, in order. A board it stood on twice maps to its *last* rung, so a plain round trip is skipped by the lookup alone |
| the sweep | one forward pass over the rungs. Every successor is at a strictly higher rung, so the ladder is a DAG and rung order is a topological order: each rung is expanded exactly once |
| one closure per rung | every state at a rung has the same playfield by construction, so they share one PF-preserving movement closure — a multi-source uniform-cost walk over tank poses, each seeded at the keystream length it arrived with |
| the runs | a board change is offered to its rung and then the same key is pressed again while the board keeps moving, so a k-cell ferry and a k-shot mirror push are k rungs for k keys |

Level 3 is one skip: with the first block on B7 the replan pushes it straight into the water and lands
on the playfield the original only reached five board changes later. **80 → 57 keys**, and the replay
now reads the way a person would play it. Level 7's second lap of the conveyor circuit disappears
because a shot run fires four times from the one square it can be fired from: **81 → 65**.

**The one thing that had to be got right: the space bar does not belong in a movement closure.** A shot
that hits nothing still moves the laser record, so its `StateHash` differs from the pose it was fired
from; a walk that treats a new hash as a new place to stand fires from *that*, and again, and is no
longer bounded by the pose count. The first build spent its whole pose budget at rung 0 of level 3 — an
island of twenty-four cells — and found nothing on any level. `Solver.ExpandPush` had the answer
already and had said so in a comment: walk on movement keys, then fire once from each pose the walk
found. With it level 3 finishes in **25,014 `ApplyKey` calls**. What that gives up is a shot kept for
its *timing*; the search layer gives up the same thing in the same place.

| 416 solutions, `build/solutions/l0` | keys | time |
|---|---:|---:|
| as banked | 11,060 | — |
| polish only | 10,327 | 126 s |
| **polish, replan, polish** | **10,249** | 152 s |

No collection worse than polish alone, **416/416 verified through both engines**. l0 is layer 0's raw
beam, which is the *unfavourable* population; on the seven hand-supervised `data/solutions/LaserTank/`
recordings the same pass is 821 → 725, with levels 3, 4 and 7 at 80 → 57, 92 → 61 and 81 → 65.

`--replan-width` (8) bounds the states kept per rung and `--replan-nodes` (1.5M) is the backstop —
level 5 of `LaserTank.lvl` is the worst in the corpus at 412,882, which is why the default is not the
400,000 the first build shipped.

---

## The instruments, and which question each answers

In the order you should reach for them on a level that will not fall.

**1. Ask why the level is hard, before asking the solver to try harder.** `--profile` measures the
*level*: replay a winning recording — a solver's or a human's — and print what the ranking keys do
along it.

```bash
ls data/demos/LaserTank/*.lpb > build/demos.txt
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl \
    --lpb-list build/demos.txt --profile build/prof.tsv
python tools/basin.py build/prof.tsv --per-level            # in keypresses
python tools/basin.py build/prof.tsv --per-level --events   # in board changes
```

The number to read is the **longest stretch that stays at or above the best heuristic value seen so
far** — see *Why the search fails on long levels*. `--ferry-weight` sweeps layer 5's ferry weight
offline against a recording instead of by re-running the solver.

**2. Ask what the board is.** `--analyze` prints the read: what is in the way, every board change the
tank can make right now, which of those advance, and a verdict naming the level's shape.
`--analyze-tsv FILE` is one row per level for joining against a campaign report; `--read-dump FILE`
(with `--lpb-list`) scores the read against what humans actually did next.

**3. Ask where the *searcher* loses it**, which is a different question. `--push-line` replays a
recording, keeps its state at every board change, and runs the real beam with those in hand, reporting
per depth whether the line was generated, how the ranking key placed it, and whether the width trim
kept it. Read the last line (*followed to depth N of M, lost at K*), then the row at K. If the row at K
is a *setup* move — one that neither opens anywhere new to stand nor touches the barrier, across which
every key is flat — the flag to add is `--push-enables 8`. Mind the two traps in its output: `d=` vs
`at=`, and `line-h` not being a distance unless `--push-eval work`.

**4. Look at the board the beam settled on, not only at its score.** `--push-trace-board` prints the
best node's playfield each depth under `--push-trace`. `best=10` says the ranking key has gone flat and
says nothing about *what* the beam is looking at — and on a flat key the answer is usually that it has
found something the key likes and a player would not. It is what caught a beam that had buried the flag
under a block and scored it better than a win. `--push-trace`'s own columns matter too: `trunc=`
(closures truncating), `boards=` (distinct playfields in the frontier — the column that found the pose
duplicates), `closure~` (for the framing arithmetic in layer 8).

**5. Ask whether ranking or budget is binding, before spending either.** `closure~` × width × board
changes is what a *perfect* beam would cost. On all four of layer 8's levels it was two orders of
magnitude under the budget already spent, which is why sixteen configurations at 80M nodes were the
wrong way to spend a session.

### Solver source map

```
src/LaserTank.Solver/
  Search.cs      the beam and IDA*; Cut(), tiers, the closed-set policies
  Heuristic.cs   FlagDistance, WorkDistance, FrontierObstacles, RouteFerry,
                 RouteStop, RouteDead, BuildFire, BuildRays, TankRegion
  Macro.cs       layer 1: Goto (a movement closure over ApplyKey) + Shoot
  Subgoal.cs     layer 2: the obstacles between the closure and the flag,
                 derived; a successor is kept because it cleared one
  Restart.cs     layer 3: re-run the subgoal beam when it dies of an empty
                 frontier with budget in hand, growing width each time
  Learn.cs       layer 4: board features, the learned evaluation that orders
                 the beam, and RankDump, the instrument behind it
  Weights.cs     layer 4's fitted vector, compiled in — so the solver is fully
                 functional from a fresh clone with nothing to regenerate
  Push.cs        layer 5: a PF-preserving movement closure, then every board
                 change reachable from it; PushRun / ShotRun
  Analyze.cs     layer 6: the read.  ReadDerive / ReadAdvances / ReadOpens /
                 the enables pass, as an instrument and as a ranking tier
  Line.cs        --push-line: replay a winning line against the real beam
  Profile.cs     --profile: every ranking key at every keypress of a recording
  Trim.cs        Shrink (delta debugging, past --trim-ratio) and Polish
  Replan.cs      post-solve: re-derive the route through the ladder of boards
                 the solution already proved
  Auto.cs        the interactive driver: rungs, rounds, lanes, the gate
  Report.cs      the live display and the JSONL reports
  Program.cs     the CLI; Clean() is the polish/replan pipeline
```

### Solver tools

```
campaign.sh       one solver campaign over all 13 collections into one report.
                    Node-governed, not wall-clock.  STRIDE=N samples every Nth level
second_pass.sh    re-attack a campaign's unsolved levels with a different searcher,
                    into the same solutions dir.  SAMPLE=N takes every Nth failure
bench.sh          one labelled configuration over one banked level list.  Its header
                    repeats the warning: a bench picks parameters, a campaign ships
report_stats.py   read a campaign .jsonl: per-tier and per-collection rates, stop
                    reasons.  --diff compares two layers
rankdump.py       layer 4's instrument: replay every winning .lpb and dump the group
                    of successors the shipped expansion offered at each shot boundary
fit_eval.py       read that dump.  Bare: the distribution.  --fit: fit and regenerate
                    Weights.cs (rebuild after — the vector is compiled in).
                    The one tool here that is not stdlib-only: needs numpy
basin.py          read a --profile dump: how far uphill a winning line goes, per level,
                    in keypresses and in board changes
verify_solutions.py  the gate.  Both engines, WIN on each, byte-identical traces
```

> **Session 24: `build/` is empty on this machine, and it held more than artefacts.** Michal moved
> machines; `build/` is gitignored, so nothing in it travelled. `build/reports/` and
> `build/solutions/` are both gone — the three level lists, `chain.jsonl`, and every banked campaign
> solution with them. That makes the *19/50 and 21/50* check in *Next actions* item 1, the fourth
> pass (which needs `chain.jsonl`), the `--polish` pass in item 2, and the shorter `.lpb` files in
> item 3 all **unrunnable as written**, and the tuning tables below not reproducible on this tree
> until a layer-0 campaign has been re-run.
>
> **The rule it pays for: a list that only lives in a gitignored directory is not banked.** The
> curated lists are small, hand-picked and are what every number in this file is measured against —
> a machine move should not be able to take them. They now belong in **`bench/`**, committed; see
> the README there for what does and does not go in it.

The banked level lists live in **`bench/`**, committed — see the README there for why, and for what
belongs beside them. The reports they are compared through stay in `build/reports/`, which is
gitignored and machine-local. The lists: `bench-levels.txt` (60 levels layer 0 failed,
GAUNTLET-heavy), `deep-levels.txt` (50 `Beginner-I` levels with a `.ghs` total of 40-150),
and `ferry-levels.txt` (50 the chain fails that the read calls FERRY or SOKOBAN — banked because the
two older lists contain almost no ferry). `build/reports/chain.jsonl` is *not* one of them: it is the
shipped chain's final per-level state, so `second_pass.sh` can be pointed at everything it still
fails, and it is an output tied to one code version rather than a curated input.

---

## Open question — the blogspot goal-board harvester

**Checked far enough to cost, not started.** Michal raised it; what one post actually contains was
verified rather than assumed (`Challenge-II-100`): a **start screenshot**, one **screenshot per flag
showing the board at the moment of reaching it**, and — the part that makes it interesting — the game's
own **Moves and Shots counters visible in the panel**. No move list, no keystream, no prose. Roughly
2,000-3,000 posts across 2016-2024. The blog runs in level order from `LaserTank.lvl` 1
(`/2016/06/1-boot-camp.html`), which makes the index trivial for the one collection the demos cover.

**What it would buy is not solutions, it is a goal.** A final board says which blocks were moved where
and which bricks were destroyed, so "reach the flag" becomes "reach *this* board" — a progress measure
that decreases with every push in the right direction, which is exactly the gradient `RouteFerry` and
its four successors are hand-rolled approximations of. The counters are a second gift: an exact cost
target to bound a search by and to check a solution against.

**Feasibility, measured on two downloaded images.** They are full-window PNGs (609x463, 619x473), not
board crops: the 16x16 board sits at a fixed offset at roughly **24px per cell**, i.e. the 32x32
sprites scaled down, so this is template matching and not OCR. The codebook does not have to be built
by hand — **for every post we already know the collection and level, so the start screenshot is 256
labelled tiles for free**, and the goal images decode against a codebook bootstrapped from the starts.
Unknowns worth checking before committing: whether the window geometry is stable across nine years of
posts, and which of the three `.ltg` packs is in use.

Sequence, roughly: scrape post URL + collection + level + image URLs; decode a 16x16 board by template
matching; bank `(collection, level, goal PF, moves, shots)`; then a `--goal-board` mode that ranks by
cells-still-differing.

**The honesty condition, and it is not optional.** A level solved with a scraped goal board is
*hint-assisted* and must never enter the solver's headline rate. Its value is as a bootstrap:
hint-assisted solutions are real recordings, and real recordings are what `--profile` / `basin.py`
measure and what layer 4 is fit on — the off-distribution long-level sample that layer 4's
self-reinforcement trap needs, obtained without anyone playing twenty levels by hand.

It is no longer a prerequisite for measuring anything: the ferry population was n=2 when this was
agreed, and it is n=20 hand-recorded now. The harvester is a way to make that 200.

---

## Session log

Kept short on purpose; where a finding is still load-bearing it lives in the layer that measured it.
The engine port's own log is in `PROGRESS.md`.

**session 9 — layer 0.** The search API, the batch harness, `verify_solutions.py`. The harness caught
two bugs in itself (the unbounded macro-step; `Restore` rewinding `RecP` while `RecBuffer` is one
shared array).

**session 10 — the campaign, and layer 1.** Threw away a wall-clock-budgeted campaign and re-ran it
node-governed, which is where that rule comes from. Layer 1 wins on levels layer 0 fails and *loses*
over the corpus in both orderings, so it ships as a second pass — the finding that shaped every layer
after it. 395 → 416.

**session 11 — layer 2, subgoal decomposition.** Derive what is in the way from the *executed*
movement closure rather than from the price list; accept on a board test, rank on a position test. The
modelled first version found no obstacle on 62% of expansions and is kept as the thing `--sg-trace`
killed. 416 → 441.

**session 12 — layer 3, restarts.** Priced the dead-end failure mode before designing for it (717
levels, 84% of budget unspent), then found that what recovers a dead-end is *width bought after narrow
has failed*, not randomness. The negative half is the larger half: dead-ends 717 → 9 bought four
levels. 441 → 444.

**session 13 — layer 4, a learned evaluation.** Built the instrument first, and it authorised the layer
rather than redesigning it: the winner's successor is in the expansion **97.6%** of the time and
`WorkDistance` ranks it **100th of 395**, so the loss was entirely in the sort. Fit as a ranking
problem within a group. **444 → 472, none lost.** Two results kept because they are the useful kind: a
re-ranking is not additive the way a restart is, and feeding the newly solved levels back in **halves
what the model discovers**.

**session 15 — why level 1 is unsolved, and the instrument that says so.** Started from a complaint and
refused to answer it from the level number. Three measurements, each of which changed the answer: the
record is the 66th percentile of its own collection; layer 0 ranks the level by Manhattan distance for
its entire life; and the winning line spends **68 keypresses above its own best `WorkDistance`**
against a p90 of 21. Built `--profile` and `tools/basin.py`. Reverted the `WorkDistance`-ranked beam —
12/50 against 13/50, exactly the kind of result the benches exist to catch.

**session 16 — layer 5, push macros.** Built what the measurement argued for. Structural claims held
(closures never truncate, level 1 runs at depth 50 instead of 264) and the cost claim decided it for
then: one expansion is ~4,500 `ApplyKey` calls, so 3/50 at 400k against layer 0's 13/50. Did not ship
in the chain. The keeper is `RouteFerry`. Also: the second hand recording (level 2, a conveyor level
with no water, so the ferry term is provably inert) fails the same way — **two levels, two structures,
one failure mode.**

**session 17 — layer 6 (the read), the polisher, and a trimmer bug worth the session.** Michal
hand-recorded `LaserTank.lvl` 1-19 and asked for the thing a player does before searching. `Analyze.cs`
is layer 2's discipline one step further out. Inside layer 5 it turned into a width experiment and both
halves are keepers: **layer 5's width was simply wrong** (300 → 48 takes the deep bench 7 → 13) and
**the read is worth 4/50 → 11/50 on ferries at width 48**. The polisher landed with the reused-`Engine`
bug that made it look like a no-op — quirk hazard #12.

**session 18 — the beam was ranking tank poses, and the instrument that said so.** Built `--push-line`,
which answered in one row: level 1's line dies at the *first* board change, because 156 successors were
four boards wearing thirty-nine hats each. `--push-per-board` plus the learned key plus width 8 took
layer 5 from 11/50 and 14/50 to **20/50 and 21/50**. Also ran the corpus pass session 17 left undone
(15 of 255) and found the driver's push rung was getting *weaker* every round.

**session 19 — level 1 is solved.** The fourth derivation, *"after this change the tank can make a
board change it could not make before"*. Measured as an instrument first (coverage 83.2% → 97.1%),
which is what decided it goes in as a tier of its own rather than a promotion. Two false starts, both
session 17's lesson. Then `--push-beam 48 --push-read --push-enables 8` solves level 1 in **6.19M nodes
and 17.9 s** against four 800M-node runs that did not touch it. Michal's objection — *"the user won't
know to fine tune random parameters"* — turned it into a rung, and that is where the
**solo-score-is-the-wrong-statistic** rule comes from.

**session 20 — level 2 is solved, and one old bug was scoring a destroyed board as perfect.** A laser
ferry cost a depth per cell (`--push-shot-run`); no key moved when a block got nearer the cell that
would stop a *ride* (`--push-stop`, five wrong versions, every one found by the beam); `--push-line`
was depth-indexed. And `WorkDistance` returned 0 for a board with no flag — the buried-flag bug. Level
2 falls in 2.03M nodes; its own rung; ferry bench rebased to 19/50.

**session 21 — a post-solve pass that re-derives the route instead of deleting from it.**
`Replan.Improve`: the playfields a solution stood on are a ladder of positions already proved to win,
so find the shortest keystream that climbs it, free to skip rungs. Levels 3 and 7: 80 → 57 and 81 → 65.
Cheap because the ladder is a DAG. Also `--lanes N` for the driver.

**session 22 — six derivations for four levels, and two of the four fell.** `LaserTank.lvl` **8 and 9
are solved**; 6 and 10 are not. Every one of the six is a *derivation* rather than a model — the fire
map is `AntiTank()`'s own scan asked of all 256 cells at once, the frozen block is `CheckLoc` on one
side and layer 7's `_rayOk` on the other. Three things worth more than the flags: the framing
measurement (all four levels were ranking-limited by two orders of magnitude, and one `--push-trace`
column would have said so before the eighty-million-node grid that did not); the number `--push-line`
prints is not a distance; and a penalty every board pays is not a penalty. Level 9 turned out to fall
to layer 0's beam alone at round 5 — **let the driver finish a round before deriving anything.**

**session 23 — the file split, and one claim that did not survive it.** This document and
`PROGRESS.md`. No code; the same facts with the narrative of superseded reasoning removed. Two things
came out of re-reading the claims against the tree and the machine:

- **A missing `.lpb` under `data/solutions/` means nothing**, and this session got that wrong before
  Michal said so: levels 8 and 9 were absent from the directory, which looked like session 22 having
  claimed a banking it never did. It had. **He deletes a banked solution to re-run the solver by hand
  and watch the replay, and re-banks it after — that is the standing working loop.** Never infer a
  level's status from a directory listing. What did survive the check is a footnote: the shortest
  *verified* file for each of 8 and 9 is shorter than the banked one (308 keys / 1.4x and 114 / 1.9x
  against 335 / 1.5x and 127 / 2.2x), all four re-verified through both engines. *Next actions*, item 3.
- **Level 10's "wants about a billion nodes" was tested and is wrong.** The two runs session 22 left
  going returned 900M nodes and 1h37m each, both `budget`, both unsolved, frontier at board-change
  depth **2** of 53. The pass never finishes, so the constraint is the ~1,000-pose closure rather
  than the budget, and the next move on that level is a cheaper closure or a decomposition — not a
  bigger run. Written up in the level-10 bullet of *What has not fallen*.

The general form of the second one is worth keeping, because it is this file's own framing arithmetic
turned back on it: **`closure~` × width × board changes says what a perfect beam costs, and when that
product exceeds the budget the answer is to shrink a factor, not to raise the budget.**

**session 24 — an ungated derivation, and a tie-break that was choosing by accident.** Started from
two complaints, and both were real:

- **Level 1 regressed and the attribution is exact.** Session 22's read correction — an anti-tank
  standing *on* the route is a barrier, not only a threat — was the one thing in that commit not
  behind a flag, and `PushRead` is on for all five push rungs. At identical flags it cost level 1
  **272 keys in 22 s → 289 in 60 s**, and in the driver **round 3 and 6.19M nodes → round 4 and
  33.5M**, because the barrier set decides the tier and the tier decides the beam's order. Reverting
  that hunk alone restores 272 exactly; it is now `--read-antitank-wall`, off by default, on in the
  two rungs that were measured with it. The `--analyze` instrument keeps it unconditionally, because
  a mis-classified GAUNTLET is a wrong answer rather than a tuning and no rung is fitted to it.
  **The rule this pays for: a derivation shared by every rung is a flag, not an improvement** — the
  rungs below it were tuned against its absence, and "it is obviously more correct" is not a
  measurement.
- **The driver was choosing between two winners by ladder index.** Which is to say by accident. It
  now keeps the shortest, which is free and which the level-9 numbers argue for even though it does
  not rescue that level.

And one thing that is not a bug and was worth measuring anyway: **level 9's 5.0x is the route, not
missing polish.** 73 board changes against the hand recording's 26, and `Replan.Improve` moves it by
nothing at width 64 and 50M nodes. Post-processing does not substitute for a better search.
