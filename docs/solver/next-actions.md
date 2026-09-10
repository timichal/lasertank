# Next actions — the open items in full

Five items are open. The order and the reasoning behind it are in
[`SOLVER.md`](../../SOLVER.md#what-is-open); this file carries the recipes, the costs and the evidence.
Items keep their numbers because these files refer to them by number — the finished ones are in
[*Closed items*](history.md#closed-items--the-measurements-including-the-negative-ones), including the
negative results, because a negative result that is deleted gets re-run.

Order: **2, 14, 4, 7, 10**. **Item 5 closed in session 45 and it closed negative on its own example**,
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

**Two things came out of it that are worth more than the item was, and the first one lands on item
14.** `tools/phase_reach.py` solves level 6 from **K = 102, 66 board changes from the end, at width 32
on 3.4M nodes**, where item 18 measured its horizon of **50** at width 512 on 40M — the narrow arm goes
deeper on a twelfth of the budget, so **the horizon is width-dependent and 50 is not the searcher's
best**. Narrow-and-deep for the fourth time, and **item 14's per-level lever has a second dimension it
had not measured**. Item 18's number stands as measured; what it no longer supports is "50 is the
reach". And phase 2's trace is `best=137` flat for 237
depths, the *same signature* as level 10's GAUNTLET: a beam ranking distinct boards by a key that has
stopped discriminating. **What is binding in the middle of a Sokoban is the ranking, not the depth.**
That is layers 4 and 6's territory and it is not on this list as an item, because nothing has measured it
on a population yet — the cheapest way in is item 14's own run, which now has a second reason to exist.

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

**Item 2 is running on this machine (started 2026-09-10) and everything below it can run beside it.**
It is a machine commitment rather than a build, and its arms are node-governed, so extra load moves
wall-clock readings and nothing else. Sessions 42 and 43 spent the whole of items 16, 15 and 13 beside
it — a two-second replay, an offline sweep, a 63-second corpus pass, a flag and ten seeded runs — and the
running pass saw none of it except in wall clock, and session 44 spent item 17's build and its five
benches the same way — four jobs beside the pass's sixteen, ~3.5 min a bench, node-governed throughout.
**Items 17 and 18 were both spent beside it in session 44** — a build and five benches, then 96 probes
at four jobs against the pass's sixteen — and it saw neither except in wall clock. **Session 45 spent
item 5 the same way**: two instruments that cost no search worth speaking of, a build, and ~20 probes at
one job against the pass's sixteen. **What is left after it is machine time and no build**, and
`LaserTank.lvl` 6 is no longer an item — closed item 5 measured that its line comes apart into six short
phases and that the search cannot walk two of them from *any* board, the human's included, so the line
being 3.4x the reach turns out not to be
short.

---

## 2 (1st) — the fourth pass, and its fourth arm is rehearsed

**Running on this machine since 2026-09-10** — `bash tools/l5_pass.sh`, the recipe under
[*The full run*](#the-full-run) below; `bash tools/l5_pass.sh status` says where it is.

**Rehearsed and positive; it is a multi-day machine commitment.** The decision pass came out at
15 of 255 (5.9%) of the levels the shipped chain fails, so the open question was never *whether*
layer 5 pays but *how much of the corpus is worth spending on it*. One push expansion is a whole
closure, so this is the one pass budgeted in tens of millions of nodes rather than hundreds of
thousands.

**All arms were run at `SAMPLE=15`** — a 1-in-15 stride over the whole 3,709-level failure population,
**255** levels every arm was given, `NODES=40000000 BUDGET_MS=1800000 JOBS=16`, each arm on the whole
machine in turn. **306 of 306 solutions through the two-engine gate, zero divergences.** Six arms, in
recomputed greedy order (`tools/arms_union.py` over the banked reports):

**Seven arms now** — `l8fire` was rehearsed in session 33 and it reordered the whole set. Solo, exclusive
levels and greedy union over the same 255 levels (`tools/arms_union.py` over the banked reports):

| arm | flags on top of `--no-ida --no-beam --push --push-read` | solo | only it | greedy union |
|---|---|---:|---:|---|
| l8fire | the layer-8 set plus `--push-eval work --push-fire-tier` | **66** (25.9%) | 3 | 66 |
| layer7 | `--push-stop 1 --push-shot-run 16 --push-beam 128` | 55 | **8** | +14 → 80 |
| enables | `--push-enables 8` | 52 | 1 | +4 → **84** |
| l8none | the layer-8 set plus `--push-eval none` | 37 (14.5%) | 1 | +2 → 86 |
| l8learned | `--push-reach --push-ferry-match --push-ferry-maze --push-dead 20 --push-fire 8 --push-shot-run 16 --push-beam 128 --max-keys 5000` | 57 | 1 | +1 → 87 (34.1%) |
| plain | — | 38 | **0** | +0 → 87 |
| l8work | the layer-8 set plus `--push-eval work` | 61 (23.9%) | **0** | +0 → 87 |

**`l8work` is retired as an arm, and that is the session-33 result.** It was the best solo arm and the
head of the greedy chain; against `l8fire` it holds **1** exclusive level to `l8fire`'s 6 (60 levels in
common), and across all seven arms it contributes **+0** and holds **0** exclusive levels. The fire tier
is not a fourth arm — **it is a strictly better version of the first**, for +7.3% wall clock (19.98 h of
job time against 18.62 h; median unsolved level 327 s against 301 s). So the pass stays three arms and
~54 h, and gets 84 instead of 81.

**Four things the arms say that their solo counts do not:**

* **The best single arm is not the pass.** `l8work` solves 61; the chain of three solves 81. Shipping
  the winner alone costs a quarter of the result. This is layer 1's founding finding a fourth time, and
  it is why the instruction is *compare unions*.
* **Three arms are dead weight — and it took all six to know which.** `plain` contributes **0**
  exclusive levels and +0 to the union; `l8learned`, second-best solo at 57, adds **+1** because it
  overlaps `l8work` in 50 of 68; `l8none` adds +2 (`Gary-I` 1541 and `LaserTank` 161, and `l8learned`
  finds 161 too, so `none`'s unique contribution across all six arms is **one level**). After two arms
  `plain` still held 4 exclusive levels and after three it held 1. **The full run wants three arms —
  `l8work`, `layer7`, `enables` — for 81 of the 84.**
* **`--push-eval work` beats the learned key on the push side over the corpus**, 61 against 57 solo and
  11 exclusive against 7 head-to-head. `Push.cs:167` mixes `Rank()`'s output with work-unit addends, and
  this says that mixing is already costing the learned key today, at the shipped weights.
* **Layers 7 and 8 are validated over the corpus for the first time.** They were carrying two benches
  and an ablation. Layer 7 is the most complementary arm in the set.

**What the rehearsal does not show, stated plainly:** `Hard` and `Deadly` are **0 of 21 in every arm**,
so none of this touches the two tiers that have never fallen; and **98.8% of the 171 the union misses
still stop on `budget`**, so even the best arm is nowhere near a structural ceiling at 40M. Extrapolated
over the 494 chain's 3,691 failures the three-arm union is **~1,172 levels** and the composite near
**1,670 of 4,185 (~40%)** against 494 today — a stride-sample estimate from 255 levels, not a promise.

**Read the union against the 5.9% honestly.** The 15/255 was `Beginner-I` alone at ~4M nodes; this is
all thirteen collections at 40M. Both the population and the budget differ, so what it licenses is *40M
buys much more than 4M*, not a 5.5x improvement in the searcher.

### The full run

Priced from the rehearsal rather than guessed: an unsolved level at 40M nodes costs a median **275 s** at
16 jobs (117 s near-solo — the search is bandwidth-bound, and effective parallelism is only ~6.4x at 16
jobs, so buying more jobs does not recover it). That is **~18 h per arm**, so the three-arm pass is
**~54 h**.

```bash
bash tools/l5_pass.sh                   # all three arms in greedy order, ~54 h
bash tools/l5_pass.sh status            # the table, from any other shell, any time
tail -f build/reports/l5-run.log        # everything the run has printed

nohup bash tools/l5_pass.sh > /dev/null 2>&1 &   # to survive a closed terminal
```

**`tools/l5_pass.sh` is the recipe, not a wrapper around it.** It runs the three arms one at a time
(each wants the whole machine), each into its own report so the union can be recomputed, gates each
arm through `verify_solutions.py` as it finishes, and ends on `arms_union.py` over the three. The
flags are the table above plus `--max-keys 5000 --max-keys-record`; `NODES BUDGET_MS JOBS TICK CHAIN
PREFIX` are the knobs and they default to the rehearsal's values, because the numbers here are only
comparable at those.

Two things it adds that a hand-rolled loop does not, and both matter at 54 h:

* **It is resumable.** `RESUME=1` goes to `second_pass.sh`, which now drops the levels an arm's own
  report has already attempted. Ctrl-C, reboot, power cut — start it again with the same command and
  each arm picks up where it stopped rather than at the top of the corpus. (Solved levels were always
  skipped, `Plan()` sees the `.lpb`; what resume recovers is the **failures**, which is where all of
  the time goes.)
* **It says where it is.** A line every `TICK` seconds (default 300) to stdout and to
  `build/reports/l5-run.log` — attempted/3,691, solved, wall, job time, ETA — and the full table at
  every arm boundary. `status` prints that table from any other shell without touching the run, and
  marks the live arm **stale** if its report has not grown in fifteen minutes, which is how a dead run
  is told from a slow one.

**`--max-keys 5000 --max-keys-record` on every arm is the one difference from the arms tabled above**,
and it is not in the rehearsal's numbers: `layer7` and `enables` ran at the default 1,200 and could not
cross it. It is free and can only raise a cap. (What that cost: `Challenge-IV` 641 is banked at 1,764 and
1,876 keys by the two `--max-keys 5000` arms and is unreachable *in principle* by the other three, whose
longest banked solutions are 623, 1,074 and 1,152 — two of them within 15% of a cap they cannot cross.)

The script runs `l8fire` first — it is the largest single result (66 of 255 on the stride), so it
lands earliest if the run is interrupted. `build/reports/chain.jsonl` is what all three arms are
pointed at; if it is gone, `tools/chain_union.py`
rebuilds it. **It now reads 494, not the 476 the rehearsal was measured against**, so the full run
attacks 18 fewer levels than the rehearsal did and the extrapolation above is very slightly optimistic.

**Interaction with item 7:** that item draws the solved-vs-budget curve for the *chain's* four searchers
and notes the push rungs become affordable as a fifth pass at 50M. These arms are that fifth pass,
already measured at 40M — run item 7 first if the question is the production curve, this if the question
is what layer 5 adds.

### The fourth arm, rehearsed — `--push-fire-tier` ☑ (session 33)

**The answer is yes, and it is not the yes that was expected: the tier does not join the chain, it takes
over the head of it.** 66 of 255 solo, the best solo arm ever measured on that population; **66 of 66
through the two-engine gate**; seven-arm union **87 (34.1%)** against six arms' 84; and `l8work`, which it
replaces, drops to **+0 and 0 exclusive levels**. The three-arm pass is now `l8fire` → `layer7` →
`enables` for **84 (32.9%)** against the old three's 81, at the same ~54 h.

**The GAUNTLET-tail bench over-reported the *shape* of the win, not its size, and that is worth keeping.**
The tail said 85 against 76 with 15 exclusive against 6 — a strong complementary arm. Over the corpus
stride it is 66 against 61 with **6 exclusive against 1** — the same direction, a third of the margin, and
*less* complementary rather than more: the tier turns out to solve nearly everything `l8work` solves
(60 of 61 in common) plus six more. So the bench's rule-1 warning fired again, but for once in the
project's favour — the arm survived the corpus and the thing it lost was its claim to be a *fourth* arm.

**What it still does not do:** `LaserTank.lvl` 10, the level it was derived from, and the ceiling is
unmoved — of the 168 levels no arm solves, **166 (98.8%) still stop on `budget`**, one on `push-dead-end`
and one on `push-depth`. Same as the six-arm figure. 40M is nowhere near a structural limit.

The rehearsal as run (2 h 15 m wall clock at 16 jobs, 19.98 h of job time):

```bash
SAMPLE=15 NODES=40000000 BUDGET_MS=1800000 JOBS=16 bash tools/second_pass.sh \
    build/reports/chain-s25.jsonl l5-s15/l8fire build/reports/l5-s15-l8fire.jsonl \
    --no-ida --no-beam --push --push-read \
    --push-reach --push-ferry-match --push-ferry-maze \
    --push-dead 20 --push-fire 8 --push-shot-run 16 \
    --push-beam 128 --max-keys 5000 --push-eval work --push-fire-tier
python tools/verify_solutions.py build/l5-s15/l8fire
python tools/arms_union.py l8work=build/reports/l5-s15-l8work.jsonl \
    layer7=build/reports/l5-s15-layer7.jsonl enables=build/reports/l5-s15-enables.jsonl \
    l8fire=build/reports/l5-s15-l8fire.jsonl \
    l8none=build/reports/l5-s15-l8none.jsonl \
    l8learned=build/reports/l5-s15-l8learned.jsonl plain=build/reports/l5-s15-plain.jsonl
```

Three things in that command are the point of it: `SAMPLE=15`, and **`chain-s25.jsonl`** rather than
`chain.jsonl` so a new arm is comparable with the six already banked (the 494 chain's stride sample is a
different 253 levels and would have been comparable with nothing), and the `l5-s15` naming
`arms_union.py` globs. Budget: **2 h 15 m at 16 jobs, median 316 s a level, 40M nodes on 216 of the 255.**

---

## 14 (2nd) — per-level width from the record, and the calibration that sizes it

**The record's shot count is this project's search depth.** Over the 20 hand recordings, board changes
divided by `.ghs` shots is p25 0.96 / **p50 1.00** / p75 1.09, exact on six of twenty, and level 6 is
**168 board changes against a record of 425 moves / 168 shots**
([`human-strategy.md`](human-strategy.md), measurement 1). Layer 8's framing arithmetic —
`closure × width × board changes` against the node budget — has until now needed a **hand recording** to
supply its third factor, and there are 20 of those against 20,914 levels with a record. The record
supplies it for the whole corpus, and `--analyze-tsv`'s `poses` column supplies the closure for free.

**The free half is done, and it tempers the item rather than supporting it.** Over `l8fire`'s 66 solved
levels at a known width of 128, `nodes / (poses × width × ghs_shots)` reads:

| the perfect-beam estimate against the nodes actually spent | p10 | p25 | **p50** | p75 | p90 |
|---|---:|---:|---:|---:|---:|
| factor | 1.9 | 4.6 | **14.2** | 59.9 | 446 |

Levels 8 and 9 gave 22x and 47x and looked like a constant; over 66 levels the spread is **two and a half
orders of magnitude**. **So the arithmetic sizes an order of magnitude, not a width** — which kills the
version of this item that solves for the width exactly and leaves the version that is still worth a run:
the driver ladders width *globally* (8 → 48 → 128 → 512 → 2,048, and 19,200 for the raw beam), so a level
whose record says 12 board changes and one that says 168 are searched at the same width in the same
round. Scaling the **round-1** width per level by `budget / (poses × ghs_shots)`, clamped, with the ladder
still doubling from there, is a policy change the estimate is accurate enough to make.

**The decision run** is [closed item 5](history.md#5-level-6s-decomposition--built-and-refused-by-the-falsifier-it-set-itself)'s
138-level GAUNTLET tail at a fixed 40M — the population is already
committed and the control has already been run twice — global width against record-derived width, read as
solved count and exclusive levels. **Raise-only, like `--max-keys-record`, so nothing that terminates
today stops terminating.**

**Closed item 18 promoted this item and constrained it in the same breath.** The horizon is a *level*
property — 2 to 50 board changes over 12 recordings, a 25x spread, and not a constant fraction of the
line either — which is the measurement that says a per-level lever is the right kind of thing at all,
and it is why this item is now 3rd. But the horizon is also the quantity a per-level width most wants to
be sized from, and **nothing free predicts it**: joined against every `--analyze-tsv` column over the 12
levels, `poses`, `region`, `mob_max` and `mob_sum` are flat (+0.06 to +0.08) — and `poses` is a *factor
in this item's own arithmetic*. The two positives are `changes` (+0.64) and `water` (+0.62), and
`changes` is refuted directly: levels 9 and 23 have 27 board changes each and horizons of 2 and 21.
Measuring the horizon instead of deriving it costs ~7 probes at 40M nodes per level, so it is not the
input for 20,914 of them. **The one lead with a mechanism points the other way** — `effects`/`shots` at
**−0.41**, i.e. the more board changes the root offers the shorter the reach, which is closed item 16's
mobility finding a second time (freer is harder). If this item's width policy is ever more than the
`budget / (poses × ghs_shots)` clamp above, that is where to look. n = 12, so all of it is a lead.
[Closed item 18](history.md#18-the-horizon-per-level--a-level-property-and-not-a-searchers-reach).

**What not to re-derive:** *record shots = 0 as a licence to drop the space bar* was checked and is worth
nothing (17 levels of 3,709), and **parity** — the series' part 3, a chessboard colouring that fixes the
move count's parity — is an *optimality* tool and prunes nothing in a satisficing search. The shot test in
[item 13](history.md#closed-items--the-measurements-including-the-negative-ones) is the same
diagnostic without parity's exceptions (ice, tunnels, tank movers, several flags).

---

## 4 (3rd) — the campaign that decides whether `--best-of-round` is a default

**The acceptance half is done and it passed better than its bar.** The mechanism, the transcript and the
115-keys-against-294 result are in [`driver.md`](driver.md#not-settling-for-the-first-win----best-of-round-and---beat-banked).

**What is left is a number.** The measurement is a stride campaign with `--best-of-round` against one
without it, read as *keys* rather than as solved count — the solved set should be identical and the routes
shorter, and how much shorter is what decides whether this becomes a default. **Every ratio quoted in
these files was measured under first-win-cancels, so that campaign rebases them.**

**And it inherits closed item 13's second half, which is a rule rather than a number.** The flag judges a
win by `keys / record ≤ 2.0`; the shot test says a win that spends *more shots* than the record is a
different and worse route, and the two disagree on **58 of the 452 rows** — 41 wins the ratio test closes
the round on although their shot count says the strategy is wrong, and 17 it keeps open although the
shot count says the strategy is already right, so those rounds can only buy polish. The change to make
with the campaign: *keep the round open when `shots > ghs_shots` whatever the ratio; close it when
`shots == ghs_shots` and the ratio is inside a looser bound*, and **a level with no record keeps the
round open, as it does today.** The measurement is already run and the column is in `report_stats.py` —
what is not decided is whether the rule pays for the rounds it keeps open, which is this campaign's
question and not a separate item's.

Note what level 9 says about its price: **44m46s for one level**, against the 16m36s of the hand-run it
beat, because a round nobody cancels is a round every rung spends in full.

*Two things not to re-derive.* What was tried first and is not the answer: making the gate refuse the
longer write — it fixes the file and hides the run. And the recipe this item replaces: for six sessions
these files said *"this is the command to use for level 9, not the driver"* — a hand-run of
`push-ferry-work` at `--push-beam 2048 --push-restarts 30`, 16m36s, 127 keys / 2.2x — because an
unattended driver run found the beam's 294-key route instead and cancelled the good one 68.5M nodes early.
`--best-of-round` is exactly the removal of that cancel, and with it the driver beats the hand-run by
twelve keys.

**A lost result worth knowing about, because it is what this item is really for.** The shortest verified
files for levels 8 and 9 once lived in a gitignored `build/w/` and went with it:

| lvl | the lost file | keys | ratio | banked now |
|---:|---|---:|---:|---|
| 8 | `build/w/w8-2048/LaserTank/00008.lpb` | 308 (262 + 46) | 1.4x | 335 / 1.5x |
| 9 | `build/w/b9-b/LaserTank/00009.lpb` | 114 (81 + 33) | 1.9x | **115 / 1.9x** |

Which configuration produced either is not recorded — the widths in the directory names were the only
clue — so **neither is reproducible as a recipe.** Level 9's 114 has effectively been re-derived (the
acceptance run comes back at 115, one key longer, at the same ratio, from the driver with no flags aimed
at the level). **Level 8's 308 has not.**

---

## 7 (4th) — the solved-vs-budget curve

**This is the production number the whole project is measured by, and it has never been run above 150k
except by accident.** Every number in these files is quoted at 150k so that layers can be *attributed*;
that is the right measurement budget and the wrong production budget, and the two have been conflated.
The chain fails 95.9% of its levels on `budget`, and the one accidental 1M run came back 3.3x the levels.

Counted against the 494 chain: **314** of the 3,691 unsolved levels in the sample have a `.ghs` record of
≤ 40, **687** of ≤ 60, 1,037 of ≤ 80, and `stop` is `budget` on **3,540** of 3,691 (95.9%) against
`beam-dead-end` on 151. The ≤ 60 list is committed as **`bench/short-record-failures.txt`** with its rule
in its header, so this item starts at the loop:

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

**The `--sg-eval coarse` on the second pass is not optional, and without it this run measures the wrong
chain.** A bare `--subgoal` means `--sg-eval work`, which is the ranking that pass is *retired* for —
appended after `coarse` → `learned` it adds 0. The three searchers the shipped 494 is made of are
`--no-ida`, `--subgoal --sg-eval coarse` and `--subgoal --sg-eval learned`, in that order.

`second_pass.sh` re-attacks a report's failures, so this is the whole 3,691 and `--order ghs` (the
default) puts the short records first; `SAMPLE=` or a list through `bench.sh` if only the ≤ 60 population
is wanted. Report the solved count per budget as a table in `SOLVER.md`'s *Status*. **Keep 150k for
attribution; this is a different question.** At 50M the push rungs (item 2's arms) become affordable as
a fifth pass.

**Carry `--max-keys 5000 --max-keys-record` from 10M up:** at these budgets a solution can outrun the
default 1,200 cap. 1,367 of the chain's 3,691 failures have a record long enough to lift them past 1,200
and 302 past 5,000, and `Challenge-IV` 641 needed 1,876 keys against a record of 143.

---

## 10 (last) — wall clock on the push rungs

**166k nodes/s on the shipped push rung against 1.4M on layer 0**, and the difference is heuristic work
repeated for every pose of the same playfield.

| configuration, `LaserTank.lvl` 10, width 8, 6M nodes, one thread | seconds | nodes / s |
|---|---:|---:|
| layer 0 beam, `Beginner-I` 1581, 4M nodes (process start included) | 2.8 | **~1.4M** |
| `--push --push-eval work` (no read) | 16.6 | 360k |
| `--push --push-eval coarse` (no read) | 29.9 | 200k |
| `--push --push-read` (coarse, the shipped rung) | 36.2 | **166k** |

The same node count costs **2-8x the seconds**, and none of it is the engine: it is `PushH` per emitted
successor — a Dijkstra from the flag, the fire map, the reach flood, the ferry matching and maze BFS,
`Feat.Extract`'s seventeen features — plus a `TankRegion` flood per untiered successor in the cheap
`opens`. (The `work`/`coarse` pair is not a clean ablation: the two beams walked different boards,
closure ~466 against ~1,022, so the split between key cost and board cost needs a profiler, not this
table.)

**Where it is recoverable.** The successors of one expansion are four boards wearing thirty-nine hats.
Everything in that list except the tank's own cell is a function of the playfield alone — the Dijkstra
runs *from the flag* and reads the tank cell off its table (`Heuristic.cs:379-432`) — and it is
recomputed for every hat. Memoise the table by `BoardKey` within an expansion (by `(BoardKey, tank cell)`
under `--push-reach`, whose flood starts from the tank) and the per-successor cost collapses to a lookup
for every duplicate. Also cache the fire map, the matching and the `Feat` board terms the same way.

**Instrument first:** `dotnet-trace` one `--push --push-read --push-beam 8 --nodes 6000000 --jobs 1` run
on `LaserTank.lvl` 10 to see the split, then memoise. **Measure in seconds, never nodes** — the node
count is identical by construction, so every bench in these files will report no change. Two to four
times on the rungs that solve the hard levels is the plausible size.

**Both alternative explanations for the 166k are already ruled out**, which is why this item is now the
whole of the wall-clock story rather than one of three guesses at it: `--push-eval none` runs at 171k
against `coarse`'s 163k, so the *ranking* is not the cost (the tiers still need everything `PushH`
derives); and `sterile=` is 0.05%, so wasted expansions are not the cost either. **The 8.4x gap to layer
0 is `PushH` itself.**

---

## Further out, and only after the numbers above have moved

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
