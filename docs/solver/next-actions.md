# Next actions — the open items in full

**One item is open.** The reasoning behind it is in
[`SOLVER.md`](../../SOLVER.md#what-is-open); this file carries the recipes, the costs and the evidence.
Items keep their numbers because these files refer to them by number — the finished ones are in
[*Closed items*](history.md#closed-items--the-measurements-including-the-negative-ones), including the
negative results, because a negative result that is deleted gets re-run.

**[Item 10 closed on 2026-09-15](history.md#10-wall-clock-on-the-push-rungs--the-memo-shipped-and-the-layer-underneath-it-is-declined)**
— positive on its build and negative on the piece left over. `PushH` is **41-48% of the expansion**, which
is the whole of the 166k-against-1.4M gap this item was written about, and `--push-memo` collects
**1.31x / 1.56x / 1.33x** on the three rungs — `IDENTICAL` on all three and **on by default since session
51**. The second, board-keyed layer underneath it is **declined rather than deferred**: its key is free
(`MemoProbe` already computes the board hash before mixing in the tank cell), but its entry is **2.5 KB
against the pose cell's 56**, and the pose table's own sizing measurement is that 918 KB a worker across
sixteen workers costs more in shared cache than it returns. ~1.08x, for the expensive half of the build,
in the footprint already shown not to pay.
**[Item 2 closed on 2026-09-15](history.md#2-the-fourth-pass-run-over-the-corpus--1087-levels-and-the-stride-ranked-the-wrong-second-arm)**
— the fourth pass finished its third arm after six calendar days and it is the largest single result in
these files: **1,087 of 3,691 (29.5%)** of the levels the shipped chain fails, composite
**494 → 1,581 of 4,185 (11.8% → 37.8%)**, **2,249 of 2,249 gated**, 67 h 30 m wall. The rehearsal's
estimate held to within **7.8%** on the union and then **put arms 2 and 3 in the wrong order, and 3.5x
apart when they are within 1%** — `enables` was written up here at *+4* and nearly dropped, and dropping
it costs **101 levels**, more than dropping the `layer7` ranked above it (91). That is the limit clause
*compare unions* was missing: **a stride sizes a union; it cannot order the arms inside it, and it must
not be used to drop one.** `build/reports/chain5.jsonl` is the new *what is still open* report and is what
any fifth pass points at.
**[Item 4 closed on 2026-09-14](history.md#4-the-campaign-that-decided-that---best-of-round-is-a-default--and-the-shot-rule-won-it)**
— the shot rule ships as the driver's default, all three stages are in, and the two instrument defects
the campaign turned up (an unattended run's stdin could give up on a level; a gate that passed on an
empty directory) are fixed. It also re-derived `LaserTank` 8 at **305 keys / 1.37x**, which is now what
`data/solutions/` holds.

**Items 14 and 19 both closed on the same 138 levels, and between them they
retire per-level width from both sides.** Item 14 swept the beam *wider* (raise-only is what the flag
allows) at the calibration's own p25 / p50 / p75 and lost every arm — **76 / 78 / 78 against a banked
control's 85 of 138**, at median chosen widths of 854 to 6,820 against the global 128. Item 19 then ran
the one arm in the narrowing direction, `--push-beam 32`, against that same control in session 47, and
**tied: 84 of 138**, 84/84 gated, 100 min wall at four jobs beside item 2's pass.

**The tie is not the finding; the failed prediction is.** Item 19 was written on four measurements that
all said a narrow beam should hold the **deep** levels — the ones whose record is long. Its 8 exclusive
levels split **4 long / 4 short** about the population's median `ghs_shots` of 13, so whatever decides a
level between width 32 and 128, **it is not the length of the record**, and
`budget / (poses x ghs_shots x F)` — the free per-level estimate that survived item 14 — is not the way
to size width in either direction. What *is* real is **cost**: on the 76 levels both widths solve, 32 is
cheaper on 46 at a median of **1.03M nodes against 1.61M**, and four of its exclusive wins land in 1.8M
to 3.4M on levels the control burned the whole 40M and failed. Two arms that tie at 40M and differ 1.6x
in nodes **do not tie at 4M**, so the follow-up is [item 7](#7-1st--the-solved-vs-budget-curve)'s budget
curve rather than any new width item, and the five banked reports mean any rung of that curve can be read
without re-running a control. Five-arm union **101 of 138 (73.2%)**.
[Closed item 14](history.md#14-per-level-width-from-the-record--built-swept-and-beaten-by-the-global-width),
[closed item 19](history.md#19-the-narrow-beam--run-on-a-population-at-last-and-it-ties).

**Item 5 closed in session 45 and it closed negative on its own example**,
which is the second time running that the item at the front of the list has been refused by a falsifier
it built for itself (item 17 was the first). The decomposition is real — `--push-phases`, a phase
terminated by a **milestone** rather than sized by a constant, and `LaserTank.lvl` 6's 168-change line
comes apart into **six phases of 18 to 34 board changes against a horizon of 50**, every one inside the
reach already measured on that level. The chain then commits to the *right* board — its first fill is
(9,14), the same cell the human fills first — and **phase 2 never lands at width 32, 128 or 512, and
not from the human's own board either**. So the phases are short, the commitment is sound, and the search
still cannot walk the middle of the line: what defeats level 6 is **not** the length of its line, which
is the premise this item was written on.
[Closed item 5](history.md#5-level-6s-decomposition--built-and-refused-by-the-falsifier-it-set-itself).

**Two things came out of it that are worth more than the item was, and the first one became item 19 and
has now closed.** `tools/phase_reach.py` solves level 6 from **K = 102, 66 board changes from the end, at
width 32 on 3.4M nodes**, where item 18 measured its horizon of **50** at width 512 on 40M — the narrow
arm goes deeper on a twelfth of the budget, so **the horizon is width-dependent and 50 is not the
searcher's best**. Item 18's number stands as measured; what it no longer supports is "50 is the reach".
Item 19 then took that reading to a population and it **half survived**: width 32 does not solve more
levels than 128, but it solves the ones it solves at a twelfth to two-thirds of the nodes, which is the
same shape as level 6's result and the first time it has been seen off that level.
And phase 2's trace is `best=137` flat for 237
depths, the *same signature* as level 10's GAUNTLET: a beam ranking distinct boards by a key that has
stopped discriminating. **What is binding in the middle of a Sokoban is the ranking, not the depth.**
That is layers 4 and 6's territory and it is not on this list as an item, because nothing has measured it
on a population yet — and item 14's sweep did not touch it, because changing a beam's width does not
change what its ranking key discriminates.

**Items 17 and 18 closed in session 44.** Item 17 is the cleanest negative these files hold (below);
item 18 is the measurement item 5 was built on, and it inverted what item 15's number looked like. The
horizon — the board changes of suffix the search can close — runs **2 to 50 over the 12 recordings the
arm cannot solve unseeded, a 25x spread**, so it is *not* the searcher's own reach; it is not a constant
fraction of the line either (0.07 to 0.78); and it is not a function of the line's length, which two
levels refute outright (`LaserTank.lvl` 9 and 23 both have 27 board changes and horizons of **2 and
21**). **Level 6's 50 is the deepest horizon measured anywhere** — level 8 solves its whole 52-change
recording from the root — so the reach on level 6 is the best the search achieves and what beats the
level is a line **3.4x** that reach. A decomposition sized from a global constant was therefore refuted
before it was built, and item 5 duly built one that is sized by nothing.
[Closed item 18](history.md#18-the-horizon-per-level--a-level-property-and-not-a-searchers-reach).

**Items 16, 15 and 13 closed in session 43** and they are the clearest run this list's ordering rule has
had: three items whose falsifiers cost minutes were spent beside item 2's machine time, and they came
back **one column that ships with the sign inverted** (FMO mobility predicts the rate, and freer blocks
are *harder*), **one derivation refused** (the per-carry constant changes no level's ascent), **one
horizon measured** (level 6 finishes from 50 board changes out and not from 51) and **one free report
column** (the shot test). Their measurements are in
[closed items 13, 15 and 16](history.md#closed-items--the-measurements-including-the-negative-ones).
**Item 17 then closed in session 44, on the bench it set itself**, and it is the cleanest negative this
list has produced: the derivation with the largest lift the read has ever measured (**3.15x**) is
**-2 on ferry with zero exclusive levels and -1 on deep** as a tier, because its 5.1%-of-successors
selectivity was a property of the twenty hand recordings and not of the population the chain fails — on
four of five ferry levels probed it names **0%**. The code ships off by default and the table is in
[closed item 17](history.md#17-the-rarity-tier--built-and-refused-on-the-bench-it-set-itself).
Items 13-16 were all new in session 37 and all came from one source — the harvest
blog's ten-post *Lyf Series* on how a strong human plays, read against the layers in
[`human-strategy.md`](human-strategy.md). **They were not a wish list, and the way they closed is the
argument for reading prose against a codebase**: of the six derivations the reading proposed, two shipped
as measured distributions, two were refused on their own falsifiers, one turned into a report column and
one became item 17 — which has itself now closed as the sixth. What the ordering rule did to item 5 is
the part worth keeping: it **moved from 2nd to 6th and gained a cheap falsifier it did not have** (item
15), rose back to the front as the items ahead of it closed, and then closed *negative* on that
falsifier's descendant. It would have been built either way; what the demotion bought was that it was
built after item 18 had refuted a global phase size, so what got built is sized by nothing and the
negative is about the search rather than about a constant. Items 4, 7 and 10 keep their relative order
and their reasoning; only their ordinals moved.

**The goal-board bank is done, and it is an input now rather than an item.** `bench/goal-boards.json`
carries **6,030 levels / 7,622 goal boards**, decoded from the blog's whole 6,218-post index with **0
unknown tiles**, 0 codebook/derivation clashes and **nothing waiting on a human** — the 38 posts the
decode does not finish on its own are all read and all intentional. It is committed, because re-deriving
it needs nine years of blog. Item 6 built it (session 36) and sessions 37-41 took it to corpus scale; the
measurements, the four instrument bugs the last pass found, and the reasoning behind every refusal are in
[closed item 6](history.md#6-the-blogspot-goal-board-harvester-and-the-goal-board-as-a-ranking-key) and
[session 41](history.md#session-41--the-filenames-the-harvester-could-not-read-and-the-four-readers-that-assumed-the-graphics).
Re-deriving it needs no network — every image is cached:

```bash
python tools/harvest.py complete --offline   # -> bench/goal-boards.json, ~85 min
python tools/harvest.py complete --report    # its table again, free
python tools/harvest.py sheet                # the goal residual, ~41 min; tiles' 2nd gate reads it
```

**What the bank gives the open items, and the one thing it does not.** `--goal-board` is a
**hint-assisted** instrument — gated, and none of it in the headline rate — so what it produces is a
supply of **real recordings on off-distribution long levels**: the sample layer 4 is fit on and
`--profile` / `basin.py`'s only input, which is 20 hand recordings today. Item 16's falsifiers are all
measured over those same 20, so a wider bank widens every one of them, and the per-flag boards are a
subgoal sequence nothing reads yet — *Further out*'s subgoal-chaining note, with its acceptance test
already in a file. **What it does not give is human *routes***: a goal board names the destination, not
the path, so "what did the human do next" stays a question only a recording answers. Three items inherit
from it and none is blocked on it.

**The machine is free for the first time since 2026-09-10, and the habit the pass forced is worth
keeping.** Item 2 held sixteen jobs on and off from 2026-09-10 to 2026-09-15 — 67 h 30 m of wall clock
across seven restarts — and **nine items closed while it did**. Its arms are node-governed, so extra
load moves wall-clock readings and
nothing else. Sessions 42 and 43 spent items 16, 15 and 13 that way (a two-second replay, an offline
sweep, a 63-second corpus pass, a flag and ten seeded runs); session 44 spent items 17 and 18 (a build,
five benches at ~3.5 min, then 96 probes at four jobs against the pass's sixteen); session 45 spent item
5 the same way; session 47 ran item 19's whole 138-level arm at four jobs in 100 minutes; and session 50
came back with the two things the remaining items were missing — `tools/curve_pass.sh` and its own
`price` for item 7 (**45 h of job time / ~7 h wall**, not an estimate), and item 10's memo **built, gated
`IDENTICAL` on three rungs and 1.67x on the arm the pass was running**. **Session 46 is the one
exception and it is worth naming**: item 14's third arm was run with the pass *stopped*, so its wall
clock is the only reading in these files taken on an idle machine. The rule that made all of that
possible is the one to carry forward — **a long pass is node-governed, so it costs a cheap item its wall
clock and none of its numbers** — and with the pass done, the one remaining item has the whole machine
if it wants it. **The flip landed in session 51 and re-gated `IDENTICAL`; session 53 then declined the
board-keyed second layer off the code and closed item 10, so what is left is one measurement** — item 7.

**`LaserTank.lvl` 6 is no longer an item** — closed item 5 measured that its line comes apart into six short
phases and that the search cannot walk two of them from *any* board, the human's included, so the line
being 3.4x the reach turns out not to be
short.

---

## 7 (1st) — the solved-vs-budget curve

**This is the production number the whole project is measured by, and it has never been run above 150k
except by accident.** Every number in these files is quoted at 150k so that layers can be *attributed*;
that is the right measurement budget and the wrong production budget, and the two have been conflated.
The chain fails 95.9% of its levels on `budget`, and the one accidental 1M run came back 3.3x the levels.

Counted against the 494 chain: **314** of the 3,691 unsolved levels in the sample have a `.ghs` record of
≤ 40, **687** of ≤ 60, 1,037 of ≤ 80, and `stop` is `budget` on **3,540** of 3,691 (95.9%) against
`beam-dead-end` on 151. The ≤ 60 list is committed as **`bench/short-record-failures.txt`** with its rule
in its header. **`tools/curve_pass.sh` is the recipe, and it is priced** — session 50 built it and ran
its own `price` over a 1-in-150 stride, so the cost below is measured rather than guessed:

```bash
bash tools/curve_pass.sh price        # what the full run costs, measured, ~4 min
bash tools/curve_pass.sh              # the whole curve, three rungs, resumable
bash tools/curve_pass.sh status       # the table, from any other shell, any time
tail -f build/reports/curve-run.log   # everything the run has printed

nohup bash tools/curve_pass.sh > /dev/null 2>&1 &   # to survive a closed terminal
```

**What it costs, from `price` rather than from an estimate: 45 h of job time, ~7 h of wall clock at 16
jobs**, for the three rungs over the stride's 253 levels. Per rung: 0.9 h at 1M, 7.4 h at 10M, **36.5 h
at 50M** — the curve is almost entirely its top rung, which is the shape to expect when an unsolved
level burns its whole node budget.

| stage | at the node cap | 1M | 10M | 50M |
|---|---:|---:|---:|---:|
| `l0` — `--no-ida` | 15 of 32 | 3.0 s | 20.2 s | 96.6 s |
| `l3` — `--sg-eval coarse` | 28 of 31 | 4.4 s | 41.9 s | 208.5 s |
| `l4` — `--sg-eval learned` | 28 of 30 | 4.4 s | 42.9 s | 214.2 s |
| `l1` — `--macro --macro-first` | **0 of 30** | 0.8 s | 0.8 s | 0.8 s |

**The macro beam is free and the price run is why that is known rather than assumed.** Nought of its 30
priced levels reached the node cap — every one stopped on `macro-dead-end` — so its pass costs the same
0.1 h at 50M as at 1M, and a projection off its *median* would have invented two and a half hours that
do not exist. The projection is therefore per level and not per median: a level that reached the cap
burns the whole of a bigger one, a level that stopped on a dead end does not.

**`SAMPLE=15` is the default and the default is the point.** The stride is the same 1-in-15 item 2's
arms are measured on, so the two passes are read against each other, and it is the same **253** levels at
every rung, which is what makes the rungs comparable. `SAMPLE=1` is the whole 3,691 and about **15x** the
above — call it 105 h of wall clock, which is item 2's commitment again for a question a stride answers.

**`BUDGET_MS` defaults to 30 minutes and that is a guard, not a budget.** This measures *nodes*; a level
that stops on the clock is a level measured at some budget other than the one its column names. The
table reports, per stage, how many of its misses reached the node cap — that column is the run's own
honesty, and the price table above already shows `l0` at 15 of 32.

**One deviation from the recipe below, and it is deliberate:** `--max-keys 5000 --max-keys-record` is
carried on *every* rung rather than from 10M up. It can only raise a cap, never lower one, so carrying it
everywhere makes the three rungs the same searcher and a difference between them the budget rather than
the key cap. `MAXKEYS=` drops it.

The loop the script runs, for the record:

```bash
# the chain's own four searchers, in its order, each pass over what the previous one failed
for N in 1000000 10000000 50000000; do
  R=build/reports/curve-$N; S=solutions/curve-$N
  NODES=$N bash tools/second_pass.sh build/reports/chain.jsonl $S $R-l0.jsonl  --no-ida
  NODES=$N bash tools/second_pass.sh $R-l0.jsonl  $S $R-l3.jsonl  --no-ida --no-beam --subgoal --sg-eval coarse
  NODES=$N bash tools/second_pass.sh $R-l3.jsonl  $S $R-l4.jsonl  --no-ida --no-beam --subgoal --sg-eval learned
  NODES=$N bash tools/second_pass.sh $R-l4.jsonl  $S $R-l1.jsonl  --no-ida --no-beam --macro --macro-first
  python tools/verify_solutions.py build/$S
done
```

What the script adds over the loop, and all four matter at 45 h: it is **resumable** (a finished stage
leaves a stamp and is skipped; the interrupted one picks up at the levels its own report has not
attempted, `RESUME=1` to `second_pass.sh`), it **strides the first stage only** (every later stage reads
a report that is already the stride, and striding it again would take a fifteenth of a fifteenth), it
**says where it is** (a line every `TICK` seconds to stdout and to `build/reports/curve-run.log`, and
`status` prints the table from any other shell), and it **gates each rung** through `verify_solutions.py`
before moving on.

**The `--sg-eval coarse` on the second pass is not optional, and without it this run measures the wrong
chain.** A bare `--subgoal` means `--sg-eval work`, which is the ranking that pass is *retired* for —
appended after `coarse` → `learned` it adds 0. The three searchers the shipped 494 is made of are
`--no-ida`, `--subgoal --sg-eval coarse` and `--subgoal --sg-eval learned`, in that order.

`second_pass.sh` re-attacks a report's failures, so this is the whole 3,691 and `--order ghs` (the
default) puts the short records first; `SAMPLE=` or a list through `bench.sh` if only the ≤ 60 population
is wanted. Report the solved count per budget as a table in `SOLVER.md`'s *Status*. **Keep 150k for
attribution; this is a different question.**

**Closed item 2 sharpened what this item is for, and made it a comparison rather than a curve.** The
fifth pass has now *run* — three push arms at 40M over the whole 3,691, **+1,087 for 67 h 30 m of wall
clock** — so "what does a big budget buy?" is answered for the push family and the open question is the
one this curve owns: **does the same wall clock buy more when it is spent raising the chain's own
budget instead?** The arithmetic off `price` says the two are the same order: the 50M rung is 519 s of
job time a level over the stride's 253, which over all 3,691 is ~532 h of job time and **~41 h of wall
clock at the 13.0x the pass actually sustained** — cheaper than item 2's three arms, on a different
family of searchers, for an unknown number of levels. That comparison is worth more than either number
on its own, and it is why this item is now first.

**Carry `--max-keys 5000 --max-keys-record` from 10M up:** at these budgets a solution can outrun the
default 1,200 cap. 1,367 of the chain's 3,691 failures have a record long enough to lift them past 1,200
and 302 past 5,000, and `Challenge-IV` 641 needed 1,876 keys against a record of 143. **Item 2 measured
what that is worth over a population: 11 of its 1,087 solutions are longer than 1,200 keys** — ten in
`Special-I`, longest 4,681 — **and none came within 300 keys of 5,000**, so the raise buys about 1% of a
pass and the new cap is not binding either.

---

## Further out, and only after the numbers above have moved

- **`--push-depth` is not a backstop on the `enables` arm, and this is the cheapest unclaimed thing
  here.** Closed item 2 turned it up without asking: `enables` is the only arm that stops on `push-depth`
  at scale — **177 levels of 3,691**, against `l8fire`'s 6 and `layer7`'s 3 — and those stops leave a
  median **10M of the 40M nodes unspent** (p50 30.1M used). The flag is documented as *a backstop only*
  at 1,200 board changes; on that arm, at that budget, it is the thing that ends the search. Raising it
  is one number on one arm and the population that would answer it is already banked, so this is an hour
  of machine time, not a day — but it is a *fifth*-pass question and the items above are fourth-pass
  ones, which is why it is here and not numbered.
- **A FESS-shaped rung for the Sokoban/ferry half of the corpus.** FERRY + SOKOBAN is 53% of the sample
  at 5.1% solved, and level 6's diagnosis — *a greedy level-synchronous beam in a region where every
  successor of every held board is worse* — is the textbook failure of beam search on Sokoban. The
  textbook answer is **FESS** (Shoham & Schaeffer, *The FESS Algorithm: A Feature Based Approach to
  Single-Agent Search*, IEEE CoG 2020), the first solver to clear all 90 XSokoban levels. The shape:
  project states into a small **feature space** (boxes packed, connectivity = number of regions the
  player is cut into, room connectivity, boxes out of plan); advance by *cycling through the occupied
  feature cells* and expanding the best state in each; weight moves by "advisors" that say which pushes
  serve which feature. Two things about it belong here. It is the general form of two devices these
  files arrived at by measurement — the per-board cap (diversity across boards) and *commit to one
  block-and-hole pair* (a progress cell searched on its own) — so the fit is not speculative. And it
  respects the fidelity rule: the engine still generates every state; the features are `Heuristic.cs`
  quantities that already exist (holes filled, `TankRegion`, ferry-maze distance, `RouteDead`), and the
  advisors are the read's derivations under another name. A rung, measured as a union. Its second gift is
  the **packing order**, derived backwards from the goal: which hole must be filled before which, from
  where a block can still be pushed *after* the others are down. `--push-ferry-match` is the forward half
  of that; the backward half is what level 6's strip of six holes wants. **Independent confirmation from
  the human record**, which is worth something for a device chosen out of a paper: the series' advice for
  a level you cannot crack is to work *"either forward from the starting position or backwards from the
  ending position"*, and its author — ~70 records, 120 Deadly levels solved — names pure Sokoban with no
  clear phases as the shape he is personally worst at. Our worst-solved non-trivial class is that one
  (FERRY + SOKOBAN, 53% of the sample at 5.1%). See [`human-strategy.md`](human-strategy.md).
- **Subgoal chaining over board changes, with the acceptance test already written.** Both open levels ask
  for layer 2's decomposition one level out, and the pieces exist. The acceptance test in `Subgoal.Offer`
  (`Subgoal.cs:344`) is a board test; for a gauntlet the board test is *the safe flood gained a named
  route cell* (`--push-reach`'s flood, set inclusion rather than count), and for a Sokoban it is *block b
  stands on the next cell of its maze path* (`--push-ferry-maze`'s BFS already produces the path). A
  sub-search for one such subgoal on level 10 is width 64 × depth ≤ 6 × ~5,100 nodes per expansion ≈
  **2M nodes**; ten shields is 20M, the budget one round of the driver already spends. The outer search
  is over the *order* of subgoals, depth-first with backtracking, re-deriving the read after each — at
  most 6! orderings on level 6 and mostly pruned by the matching. Cheap enough that the first thing to do
  is not build it but run the arithmetic against `--analyze`'s output on the two levels: how many
  subgoals, and how deep each is on the hand line.
- **The recovered bench lists, and the one question only they can answer.** Session 48 also brought back
  the **originals** of all three level lists from the same `build/` — `bench/recovered/{bench,deep,ferry}-levels.txt`,
  the ones `bench/bench-levels.txt`'s header calls gone. They rebase nothing and restore nothing: every
  pre-session-25 number was measured against these levels *by a solver that no longer exists*, so
  re-running them today compares two code versions, not two lists. The committed reconstructions stay
  the lists current numbers are quoted against. What the pair makes possible is one measurement that was
  not possible with either alone — **the same binary over both bench-1 lists, which isolates the
  population from the code.** Every bench-1 delta this project has ever argued about confounds the two;
  this separates them once, cheaply, and the answer is worth knowing before the next list is trusted.
  It already retired one guess for free: `bench/bench-levels.txt`'s header supposes the original's
  GAUNTLET-heavy label "was a pre-fix read", and read **today**, post-barrier-fix, the original is still
  **GAUNTLET 18 / FERRY 7** against the reconstruction's **8 / 30** — the difference is the population,
  not the read. The second use is held-out, and it is the weaker of the two: those 18 GAUNTLETs are
  **16 disjoint from `bench/gauntlet-tail.txt`**, so they are a near-independent GAUNTLET population for
  a fire tier whose +9 has been measured on exactly one — but they are *layer-0* failures, not *chain*
  failures, so how many the chain still fails is unknown until a report says so, and the standing rule
  applies: two fifty-level lists agreeing is not a population. Ask the cheap question first; the
  held-out one needs `chain.jsonl` and a filter before it means anything.
- **Parent pointers instead of copied keystreams, if width is ever the wall again.** `Snapshot` copies
  the whole consumed key prefix (`Engine.Search.cs`, `Array.Copy(RecBuffer, s.Keys, s.KeyLen)`) and
  `Restore` copies it back, so on a 900-key line every node moves ~1.8 KB of keys on top of its 1 KB of
  boards, and a `Node` holds all of it — which is why 76,800 wide was 1.1 GB. Each node keeping only the
  keys since its parent, with the path rebuilt on a win, cuts both the copy and the residency
  several-fold. Width was the wall exactly once: level 9.
## Checked, and not opportunities

- **Duplicate boards across the corpus:** 20,914 levels, 20,914 distinct playfields. No solution
  transfers for free.
- **Record shots = 0 as a licence to drop the space bar:** only 17 of the 3,709 unsolved have a zero-shot
  record, and several of those are 0/0, i.e. no record at all.
- **Per-level push beam width from the record, in *either* direction:** raising it loses over 138 levels
  at three values of the calibration's own spread (76 / 78 / 78 against the global width's 85), and
  narrowing it globally to 32 ties (84) while splitting its exclusive levels 4 long / 4 short about the
  population's median record length — so the record does not predict which width a level wants. The flag
  (`--push-width-record`) is built and ships off.
  [Closed item 14](history.md#14-per-level-width-from-the-record--built-swept-and-beaten-by-the-global-width),
  [closed item 19](history.md#19-the-narrow-beam--run-on-a-population-at-last-and-it-ties).
- **The read's `opens` as level 10's hidden cost:** the default `--push-read-opens -1` is the cheap
  flood, and a run with `-1` and one with `0` are node-identical, so the read is not a node multiplier at
  defaults.
- **A closure-dominance prune** (`seen.UnionWith(local)` after an untruncated expansion) is lossless and
  worth nothing: `sterile=` is 0.05% / 0.07% on the two benches against a stated bar of tens of percent.
  See [`history.md`](history.md#closed-items--the-measurements-including-the-negative-ones) item 11 for
  the full reasoning, which is sound — it simply has almost nothing to prune.
- **Parity — the chessboard colouring** the human record spends a whole part on
  ([`human-strategy.md`](human-strategy.md), the series' part 3): colour the 16x16 field like a
  chessboard and the tank's move count to a given cell has a fixed parity, so your score and the record's
  must agree on it — except with several flags, tank movers, ice or tunnels. **It is an *optimality*
  tool and prunes nothing in a satisficing search**, which is what this solver runs: it says your move
  count cannot *equal* the record's, never that a state is unreachable. The same diagnostic without
  parity's four exceptions is the shot test, [item
  13](history.md#closed-items--the-measurements-including-the-negative-ones), and it is
  measured and shipped as a `report_stats.py` column.
- **"GHS shots ≪ the author's shots means the level has a shortcut"** — the human record's own rule for
  finding easy levels, and **the input is not in the files.** `TLEVEL` is 576 bytes of `PF`, level name,
  hint, author *name* and `SDiff` (`GameState.cs:118`); the author's own score is nowhere in a `.lvl`, and
  the only score the corpus ships is the `.ghs` record. Nothing to compare it against.
