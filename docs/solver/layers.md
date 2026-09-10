# The layers

Layers 0-9 of the solver: what each is, what it measured, and what it ships as. The commands, the
rules and current status are in [`SOLVER.md`](../../SOLVER.md).

Each layer is a different attack on depth. The structural finding that shapes all of them arrived at
layer 1 and has held since: **a specialist that must bet on every level in advance loses in a
portfolio, because most solvable levels are ones the raw beam gets easily and every node the
specialist spends is a node taken from it.** So specialists ship either as a *second pass* over the
levels an earlier pass failed (batch) or as a *rung* of the interactive driver, where a specialist
costs a core rather than a share of anybody's budget.

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
corpus contain no block, ice, mirror, conveyor, anti-tank or tunnel. **Kids is not a shallow tier, it
is a small-branching one.** And keypresses are *worse* than these numbers: `ScoreMove` only increments
in `UpDateTankPos` (`Engine.cs:495`) while `MoveTank` spends a whole keypress on a turn without
scoring (`Engine.cs:491`), so "103 moves + 46 shots = 149 keypresses" is a lower bound — add one key
per direction change. A keypress-level exhaustive search reaches the ≤20 bucket and nothing else,
which is why the plan is layered.

**How far away the goal is, in the same units.** The median Easy level is 122 moves-plus-shots and the
median Deadly one 610, against a shipped chain that lives at 11.8% of a sample and fails 95.9% of its
levels on *budget*. So the way progress is reported is a **solved-count-vs-budget curve, Kids-first
ordered by `.ghs` cost** — a measurement of the distance, not a substitute for the target.

---

## Layer 0 — the search API and the harness  ☑

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
  silently corrupts its own answers: siblings overwrite each other's keys and the winning node reports
  whichever prefix was written last. A depth-first search never notices — IDA* was green while every
  beam solution failed to replay. The snapshot now carries its key prefix. Caught only because the
  harness replays each solution before writing it; that self-check earns its place.

**A closed-set policy that looks like a bug and is not.** Both beams mark a successor visited the
moment it is *generated*, so a state the width trim discards is closed forever. That reads as a
defect, and the fix was written and measured, and **it is a regression**: closing only on expansion
takes the raw beam from 33 to 27 on bench 1. Over-pruning wins, because the budget is nodes and the
greedy policy spends them on depth instead of on re-deriving positions it has already rejected. Kept
as `--closed generate|expand` with the measured default, and the reasoning sits in `Search.cs` so it
does not get "fixed" again. **This is a property of the search, not of the game:** layer 2's 4-wide
beam is *killed* by the same policy (10 against 6), hence `--sg-closed` defaulting to the opposite.

---

## Layer 1 — macro-actions  ☑ (ships as a second pass)

**The action set.** `Goto(x, y, dir)` — drive the tank somewhere, spending as many keys as that takes
— plus `Shoot`, one space bar. A solution is an alternation of the two, which is *complete rather than
restrictive*: any keystream is a run of direction keys, a space, a run of direction keys, … So
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

**A shot that changes nothing is dropped, and that is lossless.** If the state hash is identical after
the space bar then nothing happened at all — not even an anti-tank turn, because `AntiTank()` runs
inside the same key-consuming tick. So the successor **is** the state it was fired from, which is
already in this expansion's closure.

**The escape hatch:** the `--move-only` (default 6) closure states ending nearest the flag are kept as
pure-`Goto` successors. Without them a level needing no shot at all has no successors and the beam
dies at depth 1 — and 15% of sampled levels have `.ghs` shots = 0.

**A new heuristic, because the old one goes flat exactly here.** `FlagDistance` is a BFS over cells
the tank may *currently* enter, which after a Goto closure is guaranteed useless: the closure only
ends on states whose flag is not movement-reachable, so every macro successor scores
`Unreachable + manhattan` and the beam ranks by tank position. `Heuristic.WorkDistance` keeps a
gradient by *charging* for obstacles instead of refusing to cross them: a Dijkstra from the flag where
an empty step costs 1, a brick 4, a block 6, an anti-tank 6, a mirror 7, water 9, a rotary mirror 12,
and only `Solid` and `Crystal` are impassable — crystal because `CheckLLoc` case 19 returns `true`
without touching the cell, so a laser goes straight through one and never clears it. Tunnel mouths
sharing an id are joined by zero-cost edges. Deliberately not admissible: a beam needs a gradient, not
a lower bound, and the admissible version of this (every price 1) *is* `FlagDistance`'s flat spot.
Pushing a block into water turns the cell to `Dirt` (`MoveObj`'s `obt == 5` arm), so the number really
does drop by 8 when the level's central puzzle is solved.

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

**And the deep-level result is real regardless of ordering, which is what pointed at layer 2.** A Goto
closure costs `5 × |closure|` `ApplyKey` calls where a raw-beam successor costs 1. Macro-actions cut
the *number* of decisions by an order of magnitude and multiply the *price* of each by two or three.
Worse: inside a Goto, movement is *exhausted* rather than searched, so the beam never ranks a movement
— it ranks *board changes*, and `WorkDistance` is a thin signal for those. A keypress beam has a
gradient to walk down; a shot beam has to guess which of two hundred available shots is the useful
one. **The reason to fire has to be derived, not scored.**

---

## Layer 2 — subgoal decomposition  ☑

**The derivation — and the first version of it was wrong, which is the part worth reading.**

*Version 1, from a model.* Walk `WorkDistance`'s predecessor chain from tank to flag and call every
cell costing more than an empty step an obstacle. `--sg-trace` over 384 expansions: **62% derived no
obstacle at all.** The price list said the flag was five cheap steps away while the tank plainly could
not get there. `Beginner-I` 101 ("BE the RABBIT") is the clean case: the flag is walled in by bricks
and reached through a tunnel, so a model that joins tunnel mouths at zero cost reports a clear
five-step run. A price list knows what a cell costs to *enter*. It does not know the cell is covered
by an anti-tank, that the thin ice on the way has already been used, or which mouth a tunnel actually
pairs with — and those are precisely what stops a tank on the levels a solver fails.

*Version 2, from the engine.* The movement closure runs **first**, and the cells it stood on are
recorded — an executed answer to "where can the tank get to", with death, spent thin ice, conveyors
and tunnel pairing all resolved by having happened. The Dijkstra then runs from the flag and stops at
the first of those cells it settles; what lies between is what is in the way. **The model proposes the
ordering, the engine supplies every claim about what the tank can do.** No-obstacle expansions fell
from 62% to 23%.

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
successors as **Tier 1**, which `Cut()` takes only after every successor that actually advanced. Deep
bench 6 → 9, whole budget spent, dead-ends 44 → 4.

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
the next step becomes possible, and a shot whose only effect is to make an anti-tank turn is sometimes
the whole trick.

**As a second pass it is worth about twice layer 1** — 40 levels against 21 over the same 3,790
failures — and the two are complementary rather than one superseding the other: they overlap on 15,
and the 6 levels layer 1 finds that layer 2 does not are *exactly* the 6 a third pass recovers. As a
portfolio member it is a smaller loss than layer 1 and still a loss (387 / 365 against 395).

Where the budget goes after it, which is the brief for layer 3: layer 0's failures stop on budget
95.9% of the time; layer 2's stop on budget 80.8% and at **`subgoal-dead-end` 19.1%** — a frontier
that emptied, not a clock that ran out.

---

## Layer 3 — restarts  ☑

**Priced before designed.** Over layer 2's pass, **717 levels dead-ended with a median of 84% of their
node budget unspent** — about 90 million `ApplyKey` calls paid for and thrown away. That is the
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
beam re-runs only when it stopped at `subgoal-dead-end` *and* budget remains. Attempt 0 has no jitter,
no re-seed and the canonical key order, so it **is** layer 2 exactly — verified, `--sg-restarts 0`
reproduces layer 2's benches to the level. It cannot tax the pass it runs inside, so the only question
is what the recovered budget buys.

**What recovers a dead-end is width, not randomness — and the two directions of that are the whole
layer:**

| subgoal beam | deep (50 @ 400k) | bench 1 (60 @ 150k) |
|---|---:|---:|
| width 4, no restarts (layer 2) | **10** | 24 |
| width 8 / 16 from the start | 8 / 6 | 25 / 25 |
| restarts, no growth | 10 | 25 |
| restarts + `--sg-grow` (4→8→16…) | **11** | **28** |

Narrow-and-deep is what buys layer 2 the depth it exists for, so widening up front costs; a restart is
the only way to have both, because by then the narrow search has *already reported* that it failed.
`--sg-grow` doubles width and slack per restart, capped at 64/32, on by default. Re-seeding from the
reserve loses (corpus 43 against root's 44) because it inherits every commitment the narrow beam made,
and a *grown* beam wants to re-take those wider. `--sg-reuse root` is default.

**The result, and the negative half is worth more than the positive half.** 717 levels spent a
restart, dead-ends fell 717 → 9, and it bought **four levels** (441 → 444 composite, none lost).
**Converting an emptied frontier into a spent budget mostly does not convert it into a solution.** The
19.1% marked where budget was being *wasted*, not where solutions were being *missed*. Depth remains
the binding constraint. (One of the four is `Beginner-I` 101, the level that killed layer 2's modelled
derivation; it now falls on the first restart.)

**Why not NRPA or nested Monte-Carlo, which is what the plan said.** Those adapt *which action* a
playout picks. The measurement says the subgoal search does not fail by picking the wrong successor
among many — it fails with `added = 0` nine expansions in ten, i.e. with almost nothing to pick from,
and giving it strictly more (16× width, six restarts, 84% more budget actually spent) moved 4 levels
of 3,790. **A policy that learns to order an empty list has nothing to learn.** That is the argument
for layer 4 being about depth rather than about restart policy.

---

## Layer 4 — a learned evaluation  ☑

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
the beam makes. The loss is a softmax within the group, because the beam keeps four of 395 and what is
worth fitting is *which four*, not the order of the 391 it throws away.

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

**That check is banked and it runs.** It lived as prose and as a vector that could not be handed to
`--eval-weights` as written; it is now **`bench/seed-weights.txt`**, in git beside the level lists, and
`--sg-eval learned --eval-weights bench/seed-weights.txt` reproduces plain layer 3 on **0 of 50**
deep-bench levels differing. The weights are written × `Eval.Scale`, which is not a workaround:
`fit_eval.py` writes `round(w * SCALE)` (line 307), so that is the convention any weight file is read
under, and `Rank()`'s non-learned branch is `work * Eval.Scale` for the same reason. The *unscaled*
`{1, 1000, 1}` differs on 2 of 50 — close, because the vector is right and only the jitter beside it
is then 1,024x too strong.

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
ranker learns the levels it was fed rather than the game**, and the total is a worse summary of it than
the split is. Round 1 ships. Note what that table also says about the headline: 41 of the 69 are levels
whose keystream was in the training set, so **28 is the size of the generalisation** — and the
composite gains 28 exactly.

**Where the ceiling is.** Even after fitting, the winner's successor is inside the width of 4 only
10.4% of the time on held-out human recordings. The headroom is not coverage (97.6%) and not the
acceptance test — it is entirely in the sort, and 80% of it is still on the table. That is the brief
for everything above this layer.

**Hints as landmarks was not built**, and the arithmetic says not to: only **175 of 20,914** hints are
recipe-grade (≥2 grid references or numbered steps), concentrated where search fails (0.4% of Kids,
7.3% of Deadly). A tail tool against a 3,700-level tail.

### The two defects that kept this layer inert, and the four ranking keys that came out of them

Nothing in `Weights.cs` changed and nothing was refit; the model was correct all along and two lines of
arithmetic between it and the beam were not.

**Defect one was the divide.** `Eval.Score` ended with `s /= Scale` to hand the beam a number in work
units. Integer, and the model's whole dynamic range is smaller than one unit of its own output — the
`work` weight is 157, i.e. 0.15 of a key per unit of `WorkDistance` — so the ranking rounded away and
the minimum tied in 786 of 815 instrument groups. `Eval.Score` now leaves the score in fixed point and
every caller works there: `Rank()`'s non-learned branch is `work * Eval.Scale`, layer 3's jitter is
`Eval.Scale * Jitter()`, and `PushH`'s hand-built addends are lifted by `--push-hand-scale`.
`--push-trace`'s `best=` column and `--push-line`'s `line-h` divide back before printing, so every
reading of them is still in work units.

**Defect two is why the divide looked like the whole story, and it is the larger one.** `Rank()` asked
`_eval != null`, not its caller's flag, and `Search.cs` builds `_eval` when *either* beam wants the
model while `PushLearned` defaulted to true. **So from `4765ae9` on, the subgoal beam ranked by the
learned key whatever `--sg-eval` said.** Two consequences beyond the pass: every "layer 2", "layer 3"
and "layer 4" number measured on this tree since is a number for the same ranking, and the driver ran
a duplicate rung (`Auto.cs`'s `layer 3` and `learned` rungs were the same search, so one lane of the
portfolio was spent twice). Both fixed.

**The accidental key is not a degenerate one, so it is now a key of its own.** Rounding a learned score
back to work units is *learned score, ties broken by fewest keypresses*. It is `coarse`, and both
`--sg-eval` and `--push-eval` now take `work|learned|coarse|none`:

| key | what it is |
|---|---|
| `work` | `WorkDistance` (× `Eval.Scale`). What layers 2-3 are documented to use |
| `coarse` | `Eval.Score` rounded to work units. What every run from `4765ae9` to session 27 actually used |
| `learned` | `Eval.Score` at full resolution. What layer 4 was fit to be |
| `none` | H = 0, so `Cut()` orders by `Tier` then `G`. The control that had never been run |

**`coarse` is the push default and reproduces the old binary to the node** — 0 of 50 differing on the
ferry bench, 0 of 50 on the deep bench, and `ferry 18/50 / deep 25/50` at the shipped flags, which are
the rebased baselines. **Nothing layers 5-8 measured has moved.** The subgoal default is `work`, which
restores what layers 2-3 say they do; the chain carries the flag it wants.

**The push side, benched on both lists at 4M nodes on top of `--no-ida --no-beam --push --push-read`:**

| push key, 4M | ferry solo | only it | deep solo | only it |
|---|---:|---:|---:|---:|
| `coarse` — the shipped key | **18** | 0 | 25 | 1 |
| `learned` — the fix | 15 | **0** | **28** | 2 |
| `work` | 15 | 1 | 19 | 0 |
| `none` — H = 0 | **18** | **6** | 19 | **3** |
| greedy union of the four | **25** | | **33** | |

- **`none` is the most complementary key on both lists and never wins solo** — and that prediction
  then failed on the corpus, where it is 37 solo of 255, the weakest of six arms, with one exclusive
  level. **This is the sharpest example of rule 1 in the project**: a bench over-reports
  complementarity as readily as strength.
- **`none` does not buy back wall clock.** 171k nodes/s against `work`'s 173k and `coarse`'s 163k. The
  tiers still need everything `PushH` derives — the flag Dijkstra, the fire map, the matching,
  `_lastDead` — so H = 0 turns off the *ranking*, not the cost.
- **`learned` splits by population and is dominated on one of them.** Ferry: 15 solo and **0
  exclusive** against `coarse`'s 18 — strictly worse. Deep: 28 against 25, best solo in the set. Two
  benches, opposite orders.

**The one scalar, swept — and `Eval.Scale` is the wrong value for it.** `--push-hand-scale` prices one
work unit of the ferry/stop/dead/shield terms against the learned score. `Eval.Scale` = 1024 is their
historic relation, but the learned key prices a work unit at its own `work` weight of **157**, so at
1024 the hand terms are 6.5x heavier than the key they are added to — the same units error as the
divide, one level out.

| `--push-hand-scale` under `--push-eval learned` | 128 | **157 — now the default** | 1024 (`Eval.Scale`) | 8192 |
|---|---:|---:|---:|---:|
| ferry-levels | **18** | **18** | 15 | 16 |
| deep-levels | **28** | 27 | **28** | — |

**157 is not a fitted number, it is `Weights.cs`'s `work` weight** — the value that makes a work unit
of the hand terms cost what the learned key itself charges for one. The default is derived as
`Weights.Default[work]` rather than written down, so that a refit moves it; verified node-identical to
`--push-hand-scale 157` on all 50 ferry levels. Two cautions before that reads as a result: it is two
benches of fifty, and the swept arms carry **0 exclusive levels** on either list, so what the scalar
buys is a better *single* arm and not a better union. **Worth setting; not worth a campaign.**

---

## Why the search fails on long levels — the measurement the rest of the layers answer

**The trigger** was `LaserTank.lvl` 1 "Boot Camp" surviving four rounds of the driver, and the answer
took three measurements rather than a level number.

1. **"Level 1" is a name, not a difficulty.** Its `.ghs` record is 149 keypresses, the 66th percentile
   of its own collection. Of 677 winning rows across every campaign report the median record is 19.
   Level 1 is longer than roughly 99% of everything the solver had ever solved.
2. **Layer 0's beam ranked it by a heuristic that is constant on it.** The flag at (0,0) sits in a
   water pocket, so `FlagDistance` returns `Unreachable + manhattan` for the whole level: `bestH`
   pinned at 1008 for **220 consecutive depths**. (Swapping the beam's key to `WorkDistance` fixes the
   trace and loses on the bench, 12/50 against 13/50 — so it was not shipped, and the *trace* is the
   finding, not the swap.)
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
gauntlet, mirror routing and a mixed siege all fail the same way, and none is close to the band a beam
can walk. The ascent belongs to *long levels*, not to level 1 and not to ferrying. That is not a budget
problem and no number of rounds fixes it.

**The lever the table suggests:** measured in *board changes* the same trajectories are 5x shorter,
which moves level 1 from 3.2x beyond anything ever solved to 2x the p90 — the range width and restarts
already reach. That is layer 5.

More hand recordings of long levels (record ≥100) remain the single most useful thing to add, for two
distinct reasons: as **instrument** (each is another row of the table above, and the only way to know
whether a new derivation generalises) and as **training data** off the distribution layer 4's
self-reinforcement trap is stuck in. Short recordings are not wanted — the solver already wins those.

---

## Layer 5 — push macros  ☑ (rung, not chain)

`Push.cs`, `--push`. The action set is one **PF-preserving movement closure** — everything the tank can
do without changing the playfield, so at most 16×16×4 poses — and then every board change reachable
from any pose in it, as a first-class successor. Fire from a pose (layer 1's rule and its lossless
prune, kept whole), or drive into something and keep driving while the board keeps changing, one
successor per changed cell. **Search depth is the board-change count**, which is the unit
`tools/basin.py` measures in.

Closures never truncate — `--push-trace` reports `trunc=0` on every expansion of every run made so
far, which is the layer's structural bet checked rather than assumed.

**The ferry term.** `WorkDistance` prices a water cell at 9 and does not move at all while a block is
being carried towards it: the whole ferry — fetch, turn, push, twenty to fifty keypresses — scores the
same as standing still, and only the final push scores anything. `Heuristic.RouteFerry` is the fix:
summed over the water cells on the settled route, how far the nearest movable block still is from each.
Manhattan, deliberately, because "can this block actually be pushed there" is a question about
`MoveObj`, ice and where the tank can stand — i.e. a second implementation of the game. Weight from a
sweep rather than a guess: at 1 the ascent is 68 → 49 keypresses, past 2 it gets shorter and *deeper*
(the term overpowering what it corrects). And it is **inert where it is not needed** — over the 402
solver recordings the excursion distribution is identical at weight 0, 1 and 2. A targeted term that
cannot cost anything on the population already solved is the right kind of heuristic to add.

**`--push-closed` defaults to `expand`, the opposite of layer 0.** Closing on generate binned level 1's
frontier entirely. Layer 0 spends five `ApplyKey` calls on a successor and can afford to; this layer
spends a closure.

**Restarts, carried over from layer 3** for a failure with the same shape: `--push-restarts` (6) re-runs
with double the width on `push-dead-end` only, capped at 9,600. Additive by construction, so it is on by
default while the layer as a whole is not.

### The width was being spent on tank poses — `--push-line`, and what it found

**The instrument first, and this one is new in kind.** Three sessions had explained level 1 from the
outside and none of them named *which line of code loses the level*. `--push-line FILE.lpb` (`Line.cs`)
asks directly: replay a winning recording, keep its state at every board change, then run the real beam
with those states in hand and report per depth whether the line's state was generated, what the ranking
key made of it, and whether the width trim kept it. The hashes are read and never given to `Cut`, so a
`--push-line` run *is* the run it is explaining.

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 1 \
    --push-line data/demos/LaserTank/00001.lpb --nodes 20000000 --budget-ms 600000 --jobs 1
```

Four outcomes, calling for different fixes: **CUT** (generated, ranked, outside the width — rank 60 at
width 48 is a tiebreak problem, rank 3,000 is a ranking problem), **STALE** (refused by the closed set,
a `--push-closed` finding), **cut-early** (dropped by the interim trim inside the depth), and **absent**
(never generated, which can only mean the parent was already gone). Aliveness is asked of the
*playfield*, not of the state: every pose in a closure offers the same board changes, so the exact state
routinely drops out at one depth and comes back two later. **Losing the board is the loss that does not
come back.** (The traps in reading its output are in
[`instruments.md`](instruments.md).)

**What it said about level 1 was one row long: the line dies at the first board change, ranked 130 of
156.** Not at the 12-event ascent this layer was built for — before the search had done anything at
all.

**The cause is that the trim was counting the wrong thing.** A successor here is (board change, the pose
it was fired from), and one board change is reachable from *every* pose in the closure: level 1's root
closure is 158 poses offering **4** distinct changes, so the expansion emits 156 successors that are
four boards wearing thirty-nine hats each. `Cut` ranks them by a heuristic that depends on where the
tank stands and fills all 48 slots with poses of one or two boards. A `boards=` column added to
`--push-trace` reads **1 to 9 distinct playfields at width 48**, and exactly one at three depths. Each
duplicate then bought its own ~4,500-call closure to re-derive what its twin had already produced.
**That is where the layer's budget had been going all along**, and it re-explains every "narrower is
better" result before it: narrow was buying fewer duplicates, never focus.

**`--push-per-board N`**, default 1, caps poses per playfield and lets the frontier come out narrower
than the width — a depth offering six distinct boards should cost six closures, not forty-eight. Poses
are not interchangeable in general (a board change can cut the map in two with the tank on one side),
which is why it is a cap rather than a dedupe. With the trim fixed, three defaults moved and each was
paid for by a measurement:

| | ferry bench | deep bench |
|---|---:|---:|
| layer 5+6 as session 17 shipped it (width 48, per-board 0, `work`) | 11/50 | 14/50 |
| per-board 1 | 14/50 | 17/50 |
| + the learned key (level 1's line becomes only **6** board changes uphill, inside p90) | 15/50 | 19/50 |
| + width **8** (300 gives 8/12; 4 gives 17/20; 16 gives 18/20) | **19/50** | **21/50** |
| + restarts buying width *alone* | **20/50** | 21/50 |
| *layer 0, for scale* | — | *13/50* |

Narrow and deep for the third time in this project. `--push-depth` went to `MaxKeys` after seven of the
ferry bench's fifty stopped at the old 400 cap. And the restart had to buy width *only*: doubling the
per-board cap with it had level 1 running at width 128 over eight distinct boards by the fourth restart
— the duplicates quietly back. **40/40 verified through both engines** across those two benches, and
the old configuration still reproduces its own two numbers from its flags, so the delta is the change
and not the machine.

**The interactive driver's push rung was getting *weaker* every round**, which is half of why "level 1
survives five rounds" kept coming back. The ladder doubles a rung's width per round, which was written
when the default was 300; against a default of 8 it meant round 5 ran at 256 with the read off —
benched as the ladder actually ran it, **11/50 against the default's 20/50**. It now grows restarts
instead (6 and 36 both score 20/50, so it is free) and turns the read on.

---

## Layer 6 — the read  ☑

**The brief was Michal's, and it is the design.** *"Level 4 is a great example of a very easy level: as
a player, I immediately see I have to make a bridge somehow, I see the block, I know I have to use the
mirrors and avoid the ATs. Or level 6: no antitanks, only blocks and water, I instantly know there will
be long sokoban shit. This kind of analysis is what we need over an application of five different Knuth
algorithms."*

Layer 4 measured how far ranking states goes and the answer was *not much further*. What a player does
in two seconds is not a better sort of two hundred successors; it is a derivation of **which successors
exist for a reason**, done before the search starts.

**The two halves, and the discipline is layer 2's:**

- **What must change** is a model: the priced Dijkstra from the flag, stopped at the cells the tank
  demonstrably stands in — layer 2's `FrontierObstacles`, unchanged.
- **What can change it** is not a model at all. Every board change the tank can make right now is
  enumerated by *making* it: a PF-preserving pose closure, then all five keys from every pose, and
  whatever `Game.PF` comes back different is an **effect**, carrying the pose and key that produced it
  as its witness.

Nothing in `Analyze.cs` knows that a laser bounces off a mirror, that a block sinks in water or that a
conveyor carries the tank. **Level 4's three-mirror bank shot is discovered because firing left from
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
killing the anti-tank that owns a corridor, blowing a brick out of a doorway all come back as *somewhere
new to be*. Capped (`--read-opens` 64) because it costs a closure per effect; past the cap the question
is simply not asked, so a missing label is never a wrong one.

**The verdict is a decision list, not a classifier**, and every fact it tests was derived rather than
pattern-matched — "no shot on this board does anything" is the enumeration coming back with zero shot
effects, not a scan for bricks. On `LaserTank.lvl` 1-9 it reads: 1 FERRY×4, 2 RIDE, 3 FERRY×2, 4
FERRY×1 (*"1 water cell, 1 block, moved by shooting and every shot that moves it is mirror-routed; 2
anti-tanks named by the route"*), 5 FERRY×2, 6 SOKOBAN×6, 7 FERRY×1, 8 FERRY×3, 9 GAUNTLET. That is
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
DEMOLITION → 457, SETUP → 391. The whole-corpus version of this table is in
[`history.md`](history.md#the-corpus-through-the-read).)*

- **OPEN at 96.6% is the sanity check** — when the flag is already movement-reachable the solver
  essentially always wins, and the read agrees about which levels those are.
- **Half the corpus is a ferry** (FERRY + SOKOBAN = 53%, solved at 5.1%): where the corpus is, and the
  worst-performing non-trivial class.
- **The number of fills is the difficulty**, the first quantity that predicts the rate monotonically:
  1 fill 9.9%, 2 fills 5.7%, 3-4 1.8%, 5-8 1.7%, 9+ **0.7%**.
- **How freely the blocks move is the second one, and its sign is the surprise** (session 43, item 16's
  third derivation; the columns are `alive`, `mob_max`, `mob_sum`). `mob_max` is the size of the area the
  freest block can be pushed over — `_alive`'s one-push test iterated. Over the same sample: **frozen
  16.0%, 1-4 cells 11.5%, 5-8 4.1%, 9+ 4.5%**. So the *more* mobile the blocks, the *fewer* levels fall
  — the opposite of the human claim it came from (*"it is generally easy to win if you have enough
  FMOs"*), and the reason is that a free block is a resource to a player and a branching factor to a
  beam: layer 5 searches board changes, and a block with a sixty-cell area is sixty of them at every
  depth. Two things make it more than a restatement of *big open board*. It **survives the strongest
  known proxy**: with the record's own length, the water count and the block count held fixed it
  separates 9.5% / 5.3% (**1.78x**) where `poses` — board openness — gives 1.17x. And its shape is a
  **cliff at four cells, not a gradient**: above 8 the column is flat, so what predicts is *whether the
  blocks are penned in*, not how far they can go. Crowding (`mob_sum / blocks`, the series' own caveat)
  adds nothing beyond it, and `alive` alone is weaker than the area — which is what makes the flood
  worth its scan.

**Measured against the humans, which is what decides whether to search by it.** `--read-dump` replays
each winning recording, stops at every board change, and asks whether the change the human made next
was one the read named. Between two board changes the tank only *moves*, so the answer is yes-or-no by
construction.

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

### The read inside the search — as a tier, never a term

`Cut()` sorts on `Tier` before `H`, so the read can say "these successors exist for a reason and the
rest are filler" without reordering anything inside either group and without being able to admit a
successor the expansion did not offer. Same contract layer 4's `Rank()` has. A tier rather than a number
because the read's answer *is* a set — "this shot lands on the brick that is in the way" is not three
points better than a shot that does not.

**The finding that made it work is a joint one about the filter and the width.** The corrected trace
said the read promotes 5-11% of successors at shallow depth into a beam of width **300** —
`Cut(next, 300)` on a frontier of 248 does nothing at all. So the read could not help, and the Dijkstra
it costs made it fractionally worse, which is exactly what the first benches said. At width 48 on a
banked ferry population it is worth **4/50 → 11/50**, and one level on the deep bench. **A tier that
names 5% of successors is useless at a width that keeps 100% of them, and a width that keeps 5% of them
is useless without something to say which 5%.** (This is also where `ferry-levels.txt` comes from: the
deep bench cannot decide a ferry question, and the read itself says so — it scores 3 ferries in the 8 of
its 50 that the stride sample covers.)

Two false starts, both ended by the same instrument reporting **0% promoted**: the cheap `opens` proxy
asked whether the *flag's* passable component grew where the 86% came from asking whether the *tank* can
stand somewhere new (`Heuristic.TankRegion` is the fix — during a ferry those are opposites), and then
the counter itself was incremented before the `opens` pass ran.

### `enables` — the fourth derivation, and it solves level 1

Level 1 turns three roto-mirrors five times before its first fill. Those five moves open nowhere new to
stand, land on no barrier and move no block, and **every ranking key this project has is flat across all
of them.** What they *do* is put shots on the board that did not exist a moment ago — a question the
engine can be asked without knowing what a mirror is. Deltas rather than resulting boards: "shoot the
brick at (4,3)" is the same change whether or not an unrelated roto has since turned.

Level 1, derivation by derivation, on its first ten board changes — and this is the finding:

| derivation | successors it names | times it named the human's move |
|---|---:|---:|
| on the barrier / toward | **0 of 7** | 0 of 10 |
| `opens` | 4 of 7 | 3 of 10 |
| **`enables`** | **3 of 7** | **9 of 10** |

So `opens` is not inert here, it is *wrong*, and when `enables` is on `opens` moves behind it. **A
derivation that is more selective *and* more accurate belongs in front of one that is neither.** It is a
tier of its own rather than a promotion because it names **79%** of the successors offered — folded into
`TierAdvance` it would only dilute one that works.

**That rule has since been applied once more and the tier it placed was refused, which qualifies it
rather than overturning it.** `rare` is more selective (5.1% offered) and more accurate (16.0% named,
**3.15x**) than all three of these, so by the rule it belongs at the front — and as `--push-rare` it cost
the ferry bench two levels and the deep one one. The rule ranks derivations *within a population*, and
the 3.15x was measured on twenty `LaserTank.lvl` recordings while the tier ran on the levels the chain
fails, where the same derivation names **0%** of successors on four ferry levels of five. So the rule
needs a precondition it did not have written down: **selectivity and accuracy are only comparable on the
population the tier will run on**, and a partition that is empty there cannot be either. `--push-rare`
ships off by default; the table is in
[closed item 17](history.md#17-the-rarity-tier--built-and-refused-on-the-bench-it-set-itself).

Two things had to be fixed before any of it could be seen, and both are the same lesson twice. The pass
was written after `ReadTier`'s two early returns, so only one path reached it and **it benched as
entirely inert through a whole round of measurements**. And at full price it cost ~4x an expansion and
took the ferry bench 20/50 → **12/50** — right shape, wrong price, for the third time in this layer's
life. The fix is session 18's finding turned round: **a tier can only matter at a width the tiers above
it have not already filled**, so the pass counts what the depth has promoted so far and returns if that
covers the width. On a ferry level the read fills a width of 8 inside the first parent or two and the
pass is skipped; on level 1, where the free derivations say nothing all level, it is asked every time.
12/50 → 19/50 on the gate alone.

**Then the level fell.** `--push-beam 48 --push-read --push-enables 8` solves `LaserTank.lvl` 1 in
**6.19M nodes and 17.9 seconds**, 294 keys against the record's 149, 1.97x, verified and banked. Against
four 800M-node runs that did not touch it. `--push-line` says why: the human line now survives to board
change **11** where session 18's configuration lost it at 5 and the pre-fix trim lost it at 1 — past all
five rotations and past the mirror shot they set up.

**It is not one level, and the control is what says so.** Over `LaserTank.lvl` 1-19 at 60M nodes, run
twice: the derivation solves 1 and **4 (The River Nile, 49.9M)** that the same budget cannot solve
without it, while 3, 7 and 11 come out either way. 4 of 19 against 3 of 19, so **the honest claim is two
levels**. 4/4 verified, p50 1.7x the record.

**What it costs, and how it ships anyway.** Solo it is 19/50 ferry and 19/50 deep against the plain
rung's 19 and 21 — a level or two worse, which is the whole case for `--push-enables` being off by
default. Then Michal made the objection that mattered: *"we have to somehow incorporate this into the
autosolver — the user won't know to fine tune random parameters for specific levels."* Switching it on
inside the existing push rung makes a rung tuned to its best measured setting worse on an argument with
nothing measured behind it. The driver is a **portfolio**, so the answer is its own rung — and the
moment it is a portfolio member the numbers have to be read as a union:

| | solo (ferry / deep) | union with the plain rung | it adds |
|---|---:|---:|---:|
| plain push rung | 20 / 21 | — | — |
| **+ `--push-enables 8` at width 8** | 19 / 19 | **24 / 26** | **+4 / +5** |
| + the same at width 48 | 14 / 18 | 21 / 26 | +1 / +5 |

The rung runs at width 8 while the rounds are cheap and widens to 48 from round 3, which is where level
1 lives. The test that matters is the one a user would run, with no flags anywhere:
`lasertank-solve.exe data/levels/LaserTank.lvl --from 1 --to 1` → **solved in 67 seconds at round 3**,
banked and verified without being asked.

`--push-enables-poses` (default 32) caps the child closure the question is asked from: truncating can
only *lose* poses, so it costs promotions and cannot invent them, which is what makes a small default
safe. At 32 the level-1 line is held only to board change 4 where the whole closure holds it to 11 — and
the level solves at either, because a search does not have to follow the human's line to win. Raise it
when reading a `--push-line`, leave it when solving.

---

## Layer 7 — the stop cell  ☑ (own rung)

**The trigger.** *"The solver is still stuck on level 2. From a human perspective this level is trivial:
I instantly see that I have to shoot the boxes to block the conveyor belt."*

`LaserTank.lvl` 2 is a single conveyor loop with the flag in the top wall and the tank penned into the
bottom two rows — every way out is a ride that never stops. The read calls it **RIDE**, which is 7.5% of
the corpus and the second-best-solved shape at 19.6%, so the shape was never the problem. Three things
were:

**One: a laser ferry cost a depth per cell.** `PushRun` compresses a *drive* push — the tank travels
with the block, so pressing the same key again continues it, and a k-cell ferry is k successors of
**one** expansion. A shot leaves the tank where it is, so nothing compressed it. Level 2's hand
recording is **32 board changes of which 26 are a repeat of the shot before**, on a `WorkDistance` that
goes 13 → 11 across the entire level: a 32-deep breadth-first search on a flat key. `--push-shot-run N`
is PushRun for the laser and makes it six.

**Two: `--push-line` was counting the wrong thing** (the `d=`/`at=` fix — see
[`instruments.md`](instruments.md)).

**Three, and this is the layer: no ranking key in the project moves when a block gets nearer the cell
that would stop the ride.** A ferry level's route crosses water and `RouteFerry` prices it. A RIDE
level's route crosses a *conveyor*, which the price list charges 1 for and the Dijkstra walks straight
over — and the tank still cannot follow it.

`Heuristic.RouteStop` is that term, and **getting it right took five wrong versions, every one of which
the beam found and sat on.** They are worth listing because each is a different way for a relaxation to
lie — and this is the most transferable thing in these files about writing one:

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
4. **Spending one block on two requirements.** A block already paying for a requirement is now reserved,
   and is a wall to the BFS rather than a candidate.
5. **`Passable` standing in for "the tank can stop here".** A block on (14,1) is one cell from (13,1)
   and can never get there: the only square behind it is a conveyor. The push test now needs a cell on
   the ray behind that the tank can actually **stand** on, precomputed as one sweep per line per
   direction.

**And a bug nobody put there this session.** `WorkDistance`/`FlagDistance` returned **0** — the best
score there is — when no flag is on the board, on the reading "nothing to steer by". A flag leaves PF
for exactly one reason: something was pushed onto it. With a shot run a block goes up column 14 in one
run and lands on the flag, and at width 128 *all 128 boards in the frontier were that board*, scoring 4
against the winning line's 11. Fixed to `Unreachable`. Cost: one ferry-bench level, attributed by
reverting it alone.

**New instrument: `--push-trace-board`**, which prints the best node's playfield under `--push-trace`.
Three of the five wrong versions above were diagnosed by *looking at the board the beam had settled on*;
`best=10` says the key has gone flat and says nothing about *what* the beam is looking at. It is what
found the buried flag, and it is the first thing to reach for on a flat key.

**The ablation at 60M nodes on level 2** — width 128 + `--push-stop 1` + `--push-shot-run 16` solves it
in 2.03M nodes and 44 s; without the shot run, without the stop term, at width 8 or at width 64 it is
unsolved at 60M (at width 256 it solves in 4.11M). All three ingredients and the width. Nothing here is
a preference.

Solo it is 18/50 on both benches against 19 and 21; as a portfolio member it **adds 3 ferry and 5 deep
levels**, so it is its own rung, `push-stop`. `--from 2 --to 2` solves level 2 at **round 2 in 65
seconds with no flags**, 90 keys against the record's 72 (1.2x). `--push-shot-run` is off by default for
its own measured reason rather than by association: solo it is ferry 15/50 (from 19) and deep 23/50
(from 21) — a wash, so it lives in the rung that needs it.

**What is left undone here.** The chain is conveyors only. `--analyze`'s RIDE verdict says "a conveyor, a
slide or a tunnel", and ice is the same shape — you slide until you hit something — but the direction of
travel on ice depends on how the tank entered, which is a second question. `StopChain` is capped at 3
and the reservation list is not unwound per branch (over-reserving makes a state look dearer, which is
the safe direction).

---

## Layer 8 — what a player reads off the board  ☑ (two rungs; solves level 8)

**The trigger.** *"Onwards with the level solver — I'm stuck on levels 6, 8, 9 and 10. All the levels
have manual playthroughs in demos."* Four levels, three shapes, and the first useful thing was to stop
treating them as one problem.

**The framing measurement, and it decided where to spend the session.** One push expansion is a whole
closure, so the honest question about a long level is whether its *budget* or its *ranking* is binding,
and `--push-trace` answers it directly: `closure~` × width × board-change count is what a perfect beam
would cost.

| lvl | board changes | closure | a perfect beam at width 8 | at 80M nodes |
|---:|---:|---:|---:|---|
| 6 Cascade | 168 | ~800 | 3.5M | unsolved |
| 8 castle siege | 52 | ~100 | 0.3M | unsolved |
| 9 Grid Lock | 27 | ~100 | 0.3M | unsolved |
| 10 Valley of Death | 53 | ~500 | 1.0M | unsolved |

**All four were ranking-limited by two orders of magnitude at the shipped width**, which is why sixteen
configurations at 80M nodes solved none of them — and one `--push-trace` column would have said so
before the eighty-million-node grid that did not. Read the last column as the warning it became, though:
it is the cost at *width 8*, and every level that fell in the end fell at a width between 512 and 19,200.
Ranking-limited was the right diagnosis; "therefore not budget-limited" was not the right corollary —
fixing the ranking moved the binding constraint to width and put it straight back into the budget.

**One: an anti-tank on the route is a wall, not a threat** (a correctness fix, and free).
`FrontierObstacles` returns two kinds of cell interleaved: a cell **of** the route that costs more than
an empty step, and — for a route cell that costs nothing and is still not reached — the anti-tanks
*aligned* with it, which are targets rather than terrain. Both callers dropped every anti-tank to keep
the second kind out of the barrier, and threw the first kind out with it. Level 9 makes it visible
because **every wall on it is an anti-tank**: a 4×4 grid of rooms whose partitions are rows of `^>v<`,
none of which can ever fire, because the only cells they look at are each other. The read said *nothing
at all for the whole level*. Fixed via an optional `onRoute` flag array: both benches unmoved,
`--read-dump` 667/800 against 666, and 179 of the 4,185-level sample (4.3%) change verdict. **It is a
flag, `--read-antitank-wall`, off by default**, and that is the rule it paid for: on `LaserTank.lvl` 1
it cost **272 keys in 22 s → 289 in 60 s** and moved the driver from round 3 / 6.19M nodes to round 4 /
33.5M, because the barrier set decides the tier and the tier decides the beam's order. Reverting that
hunk alone restores 272 exactly. **A derivation shared by every rung is a flag, not an improvement** —
the rungs below it were tuned against its absence, and "it is obviously more correct" is not a
measurement. `--analyze` keeps it unconditionally, because a mis-classified GAUNTLET is a wrong answer
rather than a tuning and no rung is fitted to it.

**Two: the fire map, which is `AntiTank()` asked of every cell at once** (`--push-fire N`). A ferry
level's obstacle is terrain and the price list sees it; a gauntlet's obstacle is fire, and nothing here
could see that at all — level 10 is an empty 16×16 field with ten anti-tanks along the walls,
`WorkDistance` 16 with **nothing in the way**, and the tank can stand in eighteen cells of the board.
`Engine.AntiTank()` decides whether an anti-tank fires by walking outward *from the tank's own cell*
with `CheckLoc` and asking whether the first cell the tank could not enter is an anti-tank pointing
back. That reads nothing but the board, so the answer for all 256 cells is four line sweeps.
`Heuristic.BuildFire` is that and nothing else, down to repeating `CheckArray` verbatim as `Enters` —
**`Passable` is the wrong table here and the difference is water**, which a tank may be told to drive
into, so an anti-tank scan looks straight over a lake. Deliberately not `Threats()`, which stops only at
Solid: that superset charges for fire that never comes and cannot see the mechanism the gauntlets are
built on — a block, a mirror or **another anti-tank** dropped into a lane stops the scan and shields
everything behind it. Level 10's hint is *"move the bottom right tank left 8 spaces then up 4"*, which is
a player saying exactly that. Checked against the engine rather than asserted: on levels 8, 9 and 10 the
fire-aware flood and `--analyze`'s *executed* pose closure agree exactly (3, 6 and 18 cells). Priced
*inside* the route Dijkstra so the route goes round the fire rather than reporting how much of it a blind
route crosses. Level 10's ascent: **29 board changes → 6**.

**Three: the safe flood, because a sum can be lowered without progress** (`--push-reach`). The fire price
alone is not enough and `--push-trace` says why in one column: level 10's best score went 28 → 23 in one
board change and then **sat at 23 for fifteen depths**, shuffling anti-tanks around the edges.
`--push-reach` prices the route from the flag to the nearest cell of the **fire-aware flood from the
tank** rather than to the cell the tank stands on — layer 2's premise as a ranking key that has to answer
per successor. It is a hard test: a swept cell is not in the flood at all, so the number moves only when
somewhere new becomes safe to stand. The flood is deliberately pessimistic (four-neighbour over
`Passable`, so ice, conveyors and tunnels are crossings it does not know about): under-counting the reach
costs search order, over-counting would report a level as good as won.

**Four: the frozen block** (`--push-dead N`, and a tier). Level 6 is a Sokoban with six blocks and six
holes, and **the beam lost it on its first expansion**: `--push-trace-board` printed a block shot into a
pocket with walls on three sides and, on the fourth, a cell no tank can ever stand behind — scored **68
against the root's 73**, because filling holes is all `WorkDistance` can see and a block in a pocket is
out of the way. *Can this block still be moved?* is answerable from two things already here: the far side
is `CheckLoc` (`Engine.cs:797` pushes with exactly that test) and the near side is `BuildRays`' `_rayOk`,
layer 7's sweep — somewhere behind the block, nothing impassable between, a cell the tank can come to
rest on. It errs towards *alive* everywhere it errs, which is the only safe direction for something that
says a level is lost. `RouteDead` is then the water cells on the route with no live block left, and it is
a **cliff rather than a gradient**, zero on nearly every board in the corpus, which is the point. **A
weight was not enough:** at `--push-dead 20` the beam's best score *rose* 155 → 300 over sixty depths,
because once all eight boards it held had frozen a block, so had every successor of every one of them.
`TierLost` — a tier below the truncation escape hatch, set at emission — is what actually acts, and it is
still only an ordering, so a conservative test can never refuse a level. With it the rise stops dead:
155 → 152, flat.

**Five: the ferry as an assignment, through the maze** (`--push-ferry-match`, `--push-ferry-maze`).
`RouteFerry` is "for each hole on the route, the Manhattan distance to the nearest movable block", and on
a Sokoban both halves are wrong. Nearest-per-hole lets **every hole name the same block** — six holes and
six blocks read as one carry, and finishing that carry barely moves the number; `--push-ferry-match`
spends each block once, greedy over the smallest remaining pair (the classic Sokoban lower bound; greedy
rather than a real minimum-cost matching because this only orders states the engine already produced).
And **Manhattan is wrong about walls** — level 6's block at (11,2) is thirteen cells from its hole as the
crow flies and forty through the corridors, so the term was rewarding shoves at the wall between them;
`--push-ferry-maze` measures it with a BFS over cells a block can occupy. That is still not a pushability
search — whether a block can *actually* be pushed along a route is the `MoveObj` question this project
will not answer for a tie-break — it just stops the estimate being wrong about the maze. Two ways to get
it wrong, both found by the beam: water is not traversable in that BFS (a block pushed into a lake
sinks), and level 6's six holes are a *strip*, so four of them had no block reachable at all and the term
collapsed to a constant; and a hole with no block matched to it returned **0**, which is the buried-flag
bug wearing a different hat (unmatched holes are now `Unfillable`). Worth on the level it was built for:
level 6's ascent **30 → 11**, deepest rise 35 → 7, the human line's score a descent 156 → 3, and
`--push-line` goes from losing the line at board change 2 to following it to **26**.

**Six: RouteFerry for fire** (`--push-shield N`) — **the one term that did not earn a rung**, recorded so
nobody re-derives it. `--push-fire` says what is wrong with a gauntlet's board and `--push-reach` refuses
to walk onto it; neither says anything about the twelve board changes it takes to *fix* it, and level
10's hint is a player spelling those out. So: take the first swept cell the route has to cross, ask which
anti-tank covers it (`AntiTank()`'s own scan order, right/left/down/up, first match wins, because quirk
#5 is that only the first one fires and the order is the rule rather than the distance), and price the
nearest pushable object against the nearest cell on the ray between the two. One requirement at a time,
which is version 1 of `RouteStop` paid for in advance. **It still went in wrong once, in the way these
files have now been bitten by three times:** an unshieldable cell priced at a constant, by false analogy
with `Unfillable`. The analogy is false — a hole *must* be filled, while a swept cell has alternatives
the route price already scores. At 40 it put a 40-point cliff in the middle of level 10's winning line
and the two halves of the measurement disagreed in the tell-tale way: ascent 12 → 6, deepest rise **1 →
34**. Zero is the answer. Corrected, at weight 1 it takes level 10's ascent 12 → 7 (past 1 it is worse
than nothing, the same shape the ferry weight has) — and it adds **zero** levels to the three-rung
portfolio on either bench while losing three ferry levels solo. A shorter ascent on one level and no
union movement is exactly the evidence that says *keep the flag, do not spend a core*.

**Seven: the ranking key splits per level.** `--push-eval` defaults to `coarse`, in which `work` is one
term at weight 157 and everything layers 5-8 add is added outside it; on `--push-eval work` the key is
`WorkDistance` plus the terms and therefore reaches 0 on a win.

| lvl | ascent, `coarse` | ascent, `work` + the terms |
|---:|---:|---:|
| 6 | **11** | 21 |
| 8 | **11** | 32 |
| 9 | 8 (of 84 → 70) | **7** (56 → 1) |
| 10 | 29 | **12** (15 → 0) |

6 and 8 want the learned key, 9 and 10 want the raw one — the same portfolio argument every layer since 4
has ended in, and why layer 8 ships as **two** rungs.

**What the whole of it is worth**, read as a portfolio member against the plain push rung's own solved set
at 4M nodes:

| configuration | ferry, solo | adds | deep, solo | adds |
|---|---:|---:|---:|---:|
| `push` (the shipped rung) | 19/50 | — | 21/50 | — |
| **`push-ferry`** — reach + fire + dead + match + maze, learned, width 128 | 17/50 | **+3** | 21/50 | **+6** |
| **`push-ferry-work`** — the same on `--push-eval work` | 18/50 | +4 | 21/50 | +6 |
| all three as a portfolio | **23/50** | | **29/50** | |

Six deep levels is the largest addition any specialist in this ladder makes, and the two keys are not
redundant: `push-ferry-work` adds one ferry and two deep on top of `push` and `push-ferry` together. Both
rungs widen from 128 to 512 at round 3 — that half is `--push-line`'s number rather than a bench's (level
8's human line survives to board change 7 at width 128 and to 15 at 512), because a bench at 4M nodes
cannot say anything about a width a round-3 budget pays for. And they raise `MaxKeys` from 1,200 to
5,000: level 6's *hand* recording is 904 keypresses and a solver route is longer than a human's, so on
this population the search was quietly discarding the end of the level and reporting `budget`.

### Levels 8 and 9 are solved, and the credit goes to different places

| lvl | what solves it | nodes | width | keys | ratio |
|---|---|---:|---:|---:|---:|
| 8 castle siege | `push-ferry-work` | **57.5M** | 512 | 335 | 1.5x |
| 9 Grid Lock | **layer 0's beam, round 5** | **94.3M** | 19,200 | 294 | 5.0x |
| 9 Grid Lock | `push-ferry-work` also gets it | 162.8M | 2048 | 127 | 2.2x |
| 9 Grid Lock | **`push-ferry` — the round nobody cancels** | **258.8M** | 2048 | **115** | **1.9x** |

Both verified and banked under `data/solutions/LaserTank/`. **The banked level-9 route is the last row**,
and the row above it is what this table credited for six sessions: with `--best-of-round` holding round 5
open, three rungs finish it and the **`coarse`**-key rung — not the `work`-key one this section is written
around — comes back twelve keys shorter. See [`next-actions.md`](next-actions.md) item 4.

**Level 8 is layer 8's** (unsolved at 400M at width 512 on the learned key, at 150M at width 128 on
either, and at 80M without the new terms), and `--from 8 --to 8` with no flags lands it at round 4 in
9m51s.

**Level 9 was never layer 8's, and that is worth saying loudly.** It falls to `--no-ida --beam 19200` with
**nothing else at all** in 94.3M nodes — measured directly, not inferred from which rung won the round —
which is the driver's round 5, exactly one round past where the run that asked for it had stopped.
**Before deriving anything for a level, let the driver finish a round.**

Both steps of the rungs' width ladder are still paid for by a level rather than an argument: 128 → 512
because level 8 needs 512, and 512 → 2048 at round 5 because layer 8's route to 9 needs 2048 and is
unsolved at 400M at 512 *and* at 100M at 8,192. That last pair is the part worth keeping — the width that
works is neither the smallest that does nor the largest available, because at 8,192 the same budget buys
a tenth of the depth.

**Level 9's 5.0x is the route, not missing polish.** 73 board changes against the hand recording's 26,
and `Replan.Improve` moves it by nothing at width 64 and 50M nodes (294 → 294, terminating on `done`
rather than on budget). The hand recording itself polishes to **96 keys / 1.63x** — better than either
solver route. **A 5.0x solution is a bad *route*, and no post-processing is a substitute for finding a
better one.**

### What has not fallen, stated plainly

**Levels 6 and 10 are not solved.** Twenty-two configurations at 60M-150M nodes, across every combination
of the derivations, five widths from 8 to 8,192, both ranking keys and the run compression — and 8 and 9
both needed 400M-node runs at widths those twenty-two never reached. What moved on the two that remain is
every number that says whether they are *reachable*:

| lvl | ascent, before | after | `--push-line` follows, before | after | |
|---:|---:|---:|---:|---:|---|
| 6 Cascade | 30 | **11** | 1 of 168 | **26** | open |
| 8 castle siege | 11 | 9 | 3 of 52 | **15** | solved |
| 9 Grid Lock | 8 | **7** | 2 of 27 | 5 | solved |
| 10 Valley of Death | 29 | **6** | 3 of 53 | **13** | open |

**The two solved rows are the warning about reading this table too hard.** Level 8's 3 → 15 was what a
level about to fall looked like hours in advance; level 9's 2 → 5 was the *least* movement of the four and
it fell too, on width. So the ascent and the line survival say when the ranking has stopped being the
binding constraint — **they do not rank what is left.**

And the level-9 lesson was applied to both: `--no-ida --beam 19200` — layer 0 alone at round 5's width
and budget — is **unsolved on 6 and on 10 at 409.6M nodes**. So neither is one more round. (Going wider is
not a thing the driver can do: the beam rung caps at 19,200, and a 76,800-wide run was 1.1 GB resident and
climbing.) Their **Kids and Easy labels are the pack's and they measure nothing the solver cares about**:
6 is a 142-push Sokoban with a 425+168 record and 10 is a GAUNTLET, the two longest records in the first
ten levels.

Where the two that are left now stop, measured rather than guessed:

- **6** is a 142-push Sokoban and the beam is a greedy level-synchronous beam. With the assignment key its
  own best descends 184 → 95 and fills two of the six holes, then it is in a region where every successor
  of every board it holds is worse. `--push-ferry-stage` (holes left first, cheapest carry as the
  tie-break) was the attempt at forcing one carry at a time; it fills one hole and stalls. The shape that
  is missing is layer 2's, one level out: **commit to one (block, hole) pair, search only for that, then
  re-derive** — a subgoal chain over board changes rather than one beam over the level. It was the one
  open item with no cheap falsifier; closed item 15 (`--push-seed K`, the search started from the K-th
  board change of the hand recording) built one and **ran it on this level**: at 40M nodes and width 512
  the beam closes the last **50** of the 168 board changes and not the last 51, monotone below that, and
  24 changes cost 3.8M nodes against 48's 36.1M. **Session 45 then built the decomposition and measured
  it, and it does not rescue the level** — the whole record is
  [closed item 5](history.md#5-level-6s-decomposition--built-and-refused-by-the-falsifier-it-set-itself).
  The line comes apart cleanly into **six phases of 18, 26, 28, 30, 32 and 34 board changes**, one per
  hole, every one inside the horizon; `--push-phases` commits to the right first board (the fill at
  **(9,14)**, the cell the human fills first); and the beam then cannot reach phase 2 at width 32, 128 or
  512, **nor from the human's own board after the human's own first fill**. Four of the six phases are
  reachable and the two that are not are the *middle*, though shorter than the three at the end. So the
  length of the line is not what defeats this level. What the trace says instead is `best=137` flat for
  237 depths — **level 10's signature on a ferry level**: distinct boards, a key that has stopped
  discriminating, and a beam wandering. That is a ranking problem in a Sokoban's middle game, and it is
  the one thing about level 6 that is now named rather than guessed.
- **10 is a GAUNTLET, and that is the whole of it.** The traced run at width 1024 with the layer-8 flags,
  `--push-restarts 0`, `--jobs 1`:

  | column | reading | what it retires |
  |---|---|---|
  | `d=` | **d=63 at 399,019,729 nodes in 26m53s** (247k nodes/s, ~6.2M a depth) | "depth 2 at 900M" — 900M is ~140 depths |
  | `trunc=` | **0 at every depth** | "the ~1,000-pose closure is what needs attacking" |
  | `front=`/`boards=` | **1024/1024 at every depth past d=1** | pose duplication, and a finer per-board key |
  | `closure~` | 891 to 1,274 — nowhere near `--push-closure-nodes` | the closure is the size layer 8's arithmetic assumes |
  | **`barrier`** | **0 of 63,454 expansions**; 4,652/79,767,664 successors advanced (**0.0058%**) | *everything else* |
  | `best=` | 62 → 22 by d=18, then **back to 28 at d=36 and flat there for the last 28 depths** | "the key is not the problem" |

  **The last two rows are the level.** `--analyze` names it for free and should have been the first thing
  run: *"GAUNTLET: the route to the flag crosses nothing that has to be cleared and 10 anti-tanks cover it
  — what is in the way is fire, not terrain"*, with `on the barrier: 0` and `in the way: nothing priced
  (no route settled)`. A GAUNTLET has no terrain to clear, so the barrier set is empty **by
  construction**, so every derivation in layers 6-8 that tiers a successor by its relation to the barrier
  has an empty predicate — and the beam is left ranking 1,024 distinct boards by `WorkDistance` alone. For
  contrast the same instrument reads **74.7%** of expansions with a barrier on the ferry bench and
  **72.1%** on the deep one.

  **So this level is not budget-limited, and it never was ranking-limited in the way session 22 meant.**
  It is *derivation*-limited: the read has nothing to say about it, `--read-antitank-wall` included (its
  rule promotes an anti-tank standing *on the route*, and no route settles). The five board changes it can
  reach are all shots, three of them pushing an anti-tank back a cell for **+33 cells to stand in** — the
  mechanic is generated and merely never preferred. **A search that spends its last 44% above a score it
  had already beaten is not out of budget, it is wandering.** The fix that diagnosis prescribed is Layer 9,
  it is built, and the level is still open.

---

## Layer 9 — exposure as a tier  ☑ (own rung; the population pays, the example does not)

**The trigger is a measurement rather than a level, which makes this the only layer here that was asked
for by an instrument.** The traced `LaserTank.lvl` 10 run above found the read naming a barrier on **0 of
63,454 expansions**, because a GAUNTLET's barrier set is empty by construction, and the beam then ranking
1,024 boards by work distance alone with `best=` *regressing* from 22 to 28 for the last 28 depths. The
prescription that fell out of it was the sixth rule read forwards: `--push-fire` prices exposure as an
**addend** inside `PushH`, every successor of every board on a ten-anti-tank board pays it, and a penalty
everybody pays steers nothing — so make it a **tier**, in layer 7's shape.

**`--push-fire-tier`, and the whole of it is a count.** `Heuristic.BuildFire` already answers *"would an
anti-tank fire the moment the tank stood here?"* for all 256 cells in four line sweeps; `FireCells` counts
the enterable cells it marks. A successor whose board leaves the anti-tanks sweeping **fewer** cells than
its parent's did is promoted to `TierFire`. No model, no weight, no threshold — a strict integer
comparison, and an ordering that cannot refuse a state.

Three decisions that are the design rather than the code:

* **It sits below all three of the read's derivations**, so it can reorder only the group the read was
  already silent about — which on a GAUNTLET is everything and on a ferry level is the leftovers. That is
  TierEnables' placement argument re-used, and it is what makes the flag safe to add to a rung that
  already reads well.
* **The count rides along with the successor** (`Node.Swept`, filled at emission from the fire map `PushH`
  has just built), so the pass costs one board scan per *expansion* and none per successor. Measured:
  **9% wall clock**, 201.9k against 184.8k nodes/s at `--jobs 1` over the same 6M nodes. That is what
  makes it worth having beside `opens`, which asks the better question and costs a pose closure apiece —
  rationed by `--push-read-opens`, `opens` promoted **2 of 526,164** on level 10.
* **A board no anti-tank covers returns immediately**, so it is free on the half of the corpus that is a
  ferry.

**The measurement, on the population the diagnosis named.** `bench/gauntlet-tail.txt` is the 138 unsolved
GAUNTLETs with a `.ghs` record of ≤ 60 — the tail of the 537, at the only record length a 40M-node budget
can reach. Two arms, identical but for the flag, on top of `--no-ida --no-beam --push --push-read` and the
layer-8 set with `--push-eval work`:

| arm | solo | rate | only it solves | greedy union |
|---|---:|---:|---:|---|
| the layer-8 work rung | 76 | 55.1% | 6 | +6 → 91 |
| **...plus `--push-fire-tier`** | **85** | **61.6%** | **15** | **85** |

**161 of 161 solutions through the two-engine gate**, union **91 of 138 (65.9%)**, and all 47 the union
misses still stop on `budget`. On the 70 both solve the routes are the same length (median 49 keys) for
1.13M nodes against 1.08M, so it is not buying its levels by spending more. Eight of its fifteen
exclusives are `Challenge-III`.

*(Read the population honestly, because rule 1 is about exactly this: these 138 are selected for the shape
the tier is for, so +9 solo and +15 exclusive is what it is worth **where it applies** and says nothing yet
about the corpus. It is a much stronger fourth-arm candidate than `--push-eval none` was — that one was 37
solo against `plain`'s 38 and one exclusive level in six arms — but the lesson of `none` is that a filtered
population over-reports complementarity, and 138 GAUNTLETs is a filtered population. The corpus test is
[`next-actions.md`](next-actions.md) item 2's fourth arm.)*

**It does not solve `LaserTank.lvl` 10, and that is worth stating first rather than last.** The level the
derivation came from is still unsolved at 400M nodes: the traced run reaches d=48 in 37m47s with `best=`
bottoming at **29** against the untiered run's 22, and the fire selectivity falls from 33% of successors at
d=0 to 0.6% by d=48 — 9,869 promoted against a width of 1,024, so the tier is still binding, still
steering, and still not winning. Two readings, and the second is the useful one:

* **`best=` is a work distance, so a tier that trades distance for exposure makes it worse by
  construction.** Reading the layer by that column would say it does harm. That is the instrument trap
  these files have now paid for three times, and it is why `--push-trace` gained `frontier sweeps N at
  best, M at least` — the column the tier actually moves. On level 10 the frontier's least-exposed board
  goes 131 → 115 → 99 over the first three depths: exposure does fall, and falling exposure is not by
  itself a win.
* **A derivation can be right about a population and wrong about its own example.** Level 10 is at the
  87th percentile of GAUNTLET threat counts with a record of 179; the 138 levels the tier wins on have
  records of ≤ 60. Nothing here retires level 10 as an open level — what it retires is the idea that the
  fire tier is the answer to it.

**The two commands every number above is quoted against.** The population list and its report are
regenerated by the snippet in
[closed item 5](history.md#5-level-6s-decomposition--built-and-refused-by-the-falsifier-it-set-itself).

```bash
# 1. the two arms over the 138 -- the 76 / 85 / 91 and the 161 gated solutions
LT_SOLVE=build/lasertank-solve.exe JOBS=16 bash tools/gauntlet_tail.sh

# 2. the traced level-10 run with the tier on -- d=48 at 400M in 37m47s, unsolved.
#    The session-29 command exactly, plus --push-fire-tier.  Output is stderr and is
#    ~1 KB a depth; it is *not* banked in bench/ the way trace10.err is, because it
#    settles nothing that a re-run would not settle again.
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 10 \
  --no-ida --no-beam --push --push-read --push-eval work --read-antitank-wall \
  --push-reach --push-ferry-match --push-ferry-maze \
  --push-dead 20 --push-fire 8 --push-shot-run 16 \
  --push-beam 1024 --push-restarts 0 --max-keys 5000 \
  --push-fire-tier --push-trace --nodes 400000000 --budget-ms 14400000 --jobs 1 \
  --out build/trace10-fire 2> build/trace10-fire.err
```

The 9% cost has to be read in seconds because the node count is identical by construction: run command 2
twice at `--nodes 6000000`, once without the flag, and read `ms` out of `--report`.

**Two things a re-run will not reproduce and should not try to.** The 9% figure was measured on a loaded
machine (both arms equally loaded, which is what makes the *ratio* fair and the absolute throughput
meaningless), and the `median seconds on an unsolved level` in the two arm reports — 209 s for the control
against 186 s for the tier — is contention noise, not a speedup: the control arm ran while another job held
nine cores. **Node counts from these runs are comparable; wall clocks across them are not.**
