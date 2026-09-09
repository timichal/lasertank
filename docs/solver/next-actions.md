# Next actions — the open items in full

Nine items are open. The order and the reasoning behind it are in
[`SOLVER.md`](../../SOLVER.md#what-is-open); this file carries the recipes, the costs and the evidence.
Items keep their numbers because these files refer to them by number — the finished ones are in
[*Closed items*](history.md#closed-items--the-measurements-including-the-negative-ones), including the
negative results, because a negative result that is deleted gets re-run.

Order: **2, 16, 15, 13, 5, 14, 4, 7, 10**. Items 13-16 are new in session 37 and they come from one
source — the harvest blog's ten-post *Lyf Series* on how a strong human plays, read against the layers in
[`human-strategy.md`](human-strategy.md). **They are not a wish list: three of the four arrive with their
falsifier already run or costed in minutes**, which is what moves them past item 5 under this list's own
ordering rule (*cheapest falsifier first, whatever kind of work it is*). What that rule does to the old
order is worth stating: **item 5 moves from 2nd to 5th and gains a cheap falsifier it did not have**
(item 15), which is a better outcome for it than staying at the front without one. Items 4, 7 and 10 keep
their relative order and their reasoning; only their ordinals moved.

**The complete harvest ran** (session 37's fetch, session 38's diagnosis, session 39's repairs).
`tools/harvest.py fetch` over the whole 6,218-post index rather than the 150-post sample item 6 measured
on, so the goal-board bank goes from **141 levels to as many of those as decode cleanly**. Session 38's
run gave 5,779 start boards and 6,708 goal frames; session 39 worked through everything that run had
dropped and the harvester now reaches **6,023 start boards and ~7,484 goal frames**, with **1** level
contributing nothing where 246 did. The diagnosis and the five fixes are in
[closed item 6](history.md#session-39--the-logs-drops-and-the-two-that-were-the-instruments-fault); the
corpus-scale numbers from the first run are the [session 38 section](history.md#session-38--the-corpus-scale-run-and-what-its-two-complaints-were)
above it.

**The chain has run** (session 40, `build/harvest-full.log`): 7,484 goal boards decoded with **4 unknown
tiles in 1,915,904**, and `bank` wrote **5,975 levels / 7,462 boards** to `bench/goal-boards.json`. Three
of the four unknowns were already answered by `bench/post-fixups.json`; **one cell is open** —
`Sokoban-I` 1060 `b` at O2 — and it is the only thing standing between that run and a bank with no
refusal in it but the 11 stale levels. The reading of it, and the six start boards the same run turned
out to have been throwing away, are in
[session 40](history.md#session-40--the-whole-chain-ran-clean-and-its-two-complaints-were-one-repair-and-one-instrument-bug).
Rerunning costs almost no network — the 12,487 images are cached and `fetch` skips what is on disk; no
`--refetch` — and it is now **one command**, ~65 min:

```bash
python tools/harvest.py complete            # the whole chain -> bench/goal-boards.json
python tools/harvest.py complete --report   # that table again, free
```

The phases are still separate commands (`index`, `map`, `fetch`, `codebook`, `tiles`, `bank`), which is
what to reach for when one of them is what is being worked on. Running them one at a time costs ~2h40
rather than ~65 min, because three of them decode all 7,484 goal boards at 37 minutes each and `complete`
needs only the one that banks them.

**What to bring back from it**: any `NOT A START ... [order-picked]` line — that is the one guess in the
chain, zero of them on the 15-level sample it was falsified against — plus `tiles`' unclean-board list and
`bank`'s refusals. A board with an *occluded* cell is the one thing no derivation reaches: it needs a line
in `bench/post-fixups.json`, stating the `PF` symbol, and only Michal can supply it. Both lists now say
which rows are already answered there, so a row with no note is a row to go and look at.

Three items inherit from it and
none is blocked on it: `--goal-board`'s hint-assisted bootstrap becomes a corpus-scale supply of **real
recordings on long levels** — the off-distribution sample layer 4 is fit on and `--profile` /
`basin.py`'s only input, which is 20 hand recordings today; the per-flag boards are a subgoal sequence for
*Further out*'s chaining note; and item 16's falsifiers are all measured over those same 20 recordings, so
a wider bank widens every one of them. **What it does not give is human *routes*** — a goal board names
the destination, not the path, so "what did the human do next" stays a question only a recording answers.

**Item 6 is closed** (session 36): the harvester, the derived tile table and `--goal-board` all shipped,
and the full record with its measurements is
[closed item 6](history.md#6-the-blogspot-goal-board-harvester-and-the-goal-board-as-a-ranking-key).
It leaves two things behind that the items below now inherit. `--goal-board` is a **hint-assisted**
instrument — 15 → 21 of the 141 levels the fetched bank covers, all gated, none of it in the headline
rate — so what it produces is a supply of real recordings on off-distribution long levels, which is
exactly the sample layer 4 is fit on and the one nothing else here can obtain. And the per-flag goal
boards it banks are a subgoal sequence nothing reads yet, which is *Further out*'s subgoal-chaining note
with its acceptance test already in a file.

**Item 2 stays first, and it is a machine commitment rather than a build.** Its rehearsal is done and
its 54-hour run is fully specified, so it is ready to launch whenever the machine is free — and because
its arms are node-governed, everything below it can run beside it. Items 16, 15 and 13 are what to do
while it runs: **minutes, a flag and eight short runs, and a free report column.** Item 5 is still the
only open item that would move a level in front of the project — `LaserTank.lvl` 6, which has no blogspot
post at all and so was never in item 6's reach nor in the running harvest's.

---

## 2 (1st) — the fourth pass, and its fourth arm is rehearsed

**Rehearsed, positive, and not started; it is a multi-day machine commitment.** The decision pass came
out at 15 of 255 (5.9%) of the levels the shipped chain fails, so the open question was never *whether*
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
# three arms, in greedy order, each into its own report so the union can be recomputed.
# Run them one at a time: each wants the whole machine, and 16 jobs is already past
# the point where more parallelism buys anything.
L8="--push-reach --push-ferry-match --push-ferry-maze --push-dead 20 --push-fire 8
    --push-shot-run 16 --push-beam 128 --max-keys 5000"
run () {   # run <arm> <flags...>
  arm=$1; shift
  NODES=40000000 BUDGET_MS=1800000 JOBS=16 bash tools/second_pass.sh \
      build/reports/chain.jsonl "l5/$arm" "build/reports/l5-$arm.jsonl" \
      --no-ida --no-beam --push --push-read \
      --max-keys 5000 --max-keys-record "$@"
  python tools/verify_solutions.py "build/l5/$arm"
}
run l8fire  $L8 --push-eval work --push-fire-tier   # was l8work; see the table above
run layer7  --push-stop 1 --push-shot-run 16 --push-beam 128
run enables --push-enables 8

python tools/arms_union.py l8fire=build/reports/l5-l8fire.jsonl \
    layer7=build/reports/l5-layer7.jsonl enables=build/reports/l5-enables.jsonl
```

**`--max-keys 5000 --max-keys-record` on every arm is the one difference from the arms tabled above**,
and it is not in the rehearsal's numbers: `layer7` and `enables` ran at the default 1,200 and could not
cross it. It is free and can only raise a cap. (What that cost: `Challenge-IV` 641 is banked at 1,764 and
1,876 keys by the two `--max-keys 5000` arms and is unreachable *in principle* by the other three, whose
longest banked solutions are 623, 1,074 and 1,152 — two of them within 15% of a cap they cannot cross.)

Run `l8fire` first — it is the largest single result (66 of 255 on the stride), so it lands earliest if
the run is interrupted.
`build/reports/chain.jsonl` is what all three are pointed at; if it is gone, `tools/chain_union.py`
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

## 16 (2nd) — four derivations the read does not have

**Source:** [`human-strategy.md`](human-strategy.md), the blog's *Lyf Series* read against the layers.
Four things a strong human says out loud that no derivation in layers 6-8 computes. They are one item
because **two of them share a falsifier and all four are `--read-dump` questions before they are tiers** —
*a new layer should report a distribution before it reports a solved count*, and this is the cheapest
place on the list to obey that rule.

**The shared falsifier is two columns and one replay.** `--read-dump` already replays each winning
recording, stops at every board change and asks whether the change the human made next was one the read
named. Add two columns to that dump and it answers the first two derivations at once, in seconds:

```bash
ls data/demos/LaserTank/*.lpb > build/demos.txt
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl \
    --lpb-list build/demos.txt --read-dump build/read.tsv     # 800 board changes, no search
```

- **The author's intent** — *"Objects were put on the map by the author for 4 reasons: useful, beautiful,
  misleading, special hobby… especially when some object is weird on the map. Ask yourself 'Why is it
  there?'"* A lone mirror, a single `*`, one crystal on an otherwise plain board is load-bearing far more
  often than chance. The derivation is a count of each element's multiplicity in the level and a tier for
  a board change that **first touches** an element the level has few of. **The falsifier: do the human's
  800 board changes preferentially touch rare elements?** If the rate is flat against the offered
  successors, the derivation is folklore and the item ends here for free.
- **Spending a resource for no reason** — the series' *reversibility* section is a precise list of what
  play consumes: a brick shot, a piece of thin ice walked, an anti-tank killed or pushed the way it faces,
  a rotary mirror that cannot be turned back. The search knows two of these facts today (`_alive`'s frozen
  block, `RouteDead`'s holes-against-live-blocks) and prices *spending* nothing at all. The derivation is
  a tier **below** filler for a change that destroys something while naming no barrier, no `opens` and no
  `enables`. **The falsifier is the same run: how often is the human's own change destructive with no
  derived reason?** If that is common the tier would demote the human's move and is wrong. Note the
  direction of error that is safe — a tier can only reorder, never refuse, which is the rule Layer 8's
  `TierLost` was built on.
- **FMO mobility as a quantity** — *"Some object is 'free' if it can be moved in a relatively large
  area… It is generally easy to win if you have enough FMOs (except they are too crowded like
  lasertank.lvl#0250)"*. `_alive[c]` is the boolean form of exactly this and `MazeFill` already computes
  the BFS that would give it a size. **The falsifier is an `--analyze-tsv` column joined against the
  solved set**, the way *number of fills* became the first quantity to predict the rate monotonically
  (1 fill 9.9% → 9+ fills 0.7%): 64 seconds over the stride sample, and a column that does not predict is
  a column that does not ship.
- **A per-carry constant in `MatchFerry`** — the series defines a *unit* as six moves, the go-and-return
  a knight's-move detour costs, and reasons about ferries in units rather than in cells. `MatchFerry`
  sums push distance only, so it cannot see that two carries cost more than one carry of twice the
  length — which is the whole content of the human's *"push blocks two by two, not one by one"*.
  **Falsifier: `--ferry-weight`, which sweeps a weight offline against a recording without re-running the
  solver, plus level 6's ascent (11 today).**

**Cost:** two dump columns and a replay decides the first two; a `--analyze-tsv` column and a join decides
the third; an offline sweep decides the fourth. Nothing here is a rung until its own distribution says so.

---

## 15 (3rd) — `--push-seed K`, the editor trick as an instrument

**This is item 5's missing cheap falsifier, which is the reason it sits this high.** Item 5 is *"the one
open item with no cheap falsifier"*; this is one, and it costs a flag and eight short runs.

**The idea is the human method, mechanised.** *"Many have said the importance of using editor to remove
objects to the places you want. Then you can start to try if some trick can work… The pivotal situation
that you designed can help you a lot to save your resource of brain."* A player does not attack a
168-push Sokoban from move one — they build the position they are unsure about and play from there.
`--push-line` already replays a recording and keeps its state at **every board change** (`Line.cs`,
`TraceLine`); nothing starts a search from one of them.

**What it needs:** `TraceLine` records the board at each change but not the *key index* it happened at.
Keep that, replay `keys[0..idx]` into a fresh engine (quirk #12 — never share an engine with a replayed
candidate) and hand that snapshot to the search as its root. The `.lpb` writer must **prepend the
replayed prefix**, or `verify_solutions.py` cannot replay the file from the level start and the gate
cannot see it.

```bash
# level 6 has 168 board changes; sweep coarsely, then bisect the boundary
for K in 0 24 48 72 96 120 144 160; do
  build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 6 \
      --push-seed data/demos/LaserTank/00006.lpb:$K \
      --push --push-read --push-reach --push-ferry-match --push-ferry-maze \
      --push-dead 20 --push-beam 512 --max-keys 5000 \
      --nodes 40000000 --jobs 1 --out build/seed6/$K
done
```

**How to read it.** The smallest K the beam finishes from is **the search's horizon in board changes**, in
the unit `basin.py` and layer 5 already measure — the quantity every layer since 5 has attacked and none
has measured directly. The K where it stops finishing is a **phase boundary candidate**, which is what
item 5 needs to know before it decides what a phase is. Expect it monotone; a non-monotone reading is
itself the finding — a *longer* suffix that solves where a shorter one does not means the line passes
through a board the ranking key hates, and `--push-trace-board` at that K says which.

**Two by-products, and one discipline.** Run over all 20 recordings it gives the horizon per level rather
than per open level, which is a table this project has never had. And a win from a seed is a **real
keystream** — human prefix, solver suffix — so it passes the two-engine gate. That makes it
**hint-assisted**, and it takes `--goal-board`'s discipline exactly: its own output directory, a stamp on
every report row, and **never in the headline rate**. The rule is enforced in code there rather than by
convention, and the same code path is the one to reuse.

---

## 13 (4th) — the shot test: shots against the record as a route diagnostic

**The falsifier is already run and it passed**, which is why this item is a build rather than a
measurement. Over the 452 solved rows carrying a record in `fix2-chain.jsonl` + `gt-fire.jsonl`
([`human-strategy.md`](human-strategy.md), measurements 2 and 3):

| | n | median keys/record | p90 |
|---|---:|---:|---:|
| solution shots **>** record shots | 67 | **1.85** | **3.20** |
| shots **==** record | 298 (66% of all) | 1.41 | 1.80 |
| shots **<** record | 87 | 1.46 | 2.11 |

The series' first rule is *"watch the number of shots. The more shots the more tasks… If GHS's shots are
remarkably less than yours, it probably means you need to find another strategy"* — shots are a property
of the **strategy**, moves of the **execution**. It reproduces here: a win that spends more shots than the
record is a measurably worse route, and **66% of the solver's solutions already use the record's exact
shot count while the median keystream is 1.47x**. That re-reads the whole `ratio` column — 1.5x is
mostly the right plan driven badly, not half a solution.

**Two halves, and the free one first.**

- **A `report_stats.py` column** (`shots − ghs_shots`, and the count above the record). *The levels the
  solver solves with the wrong strategy* is a named population nobody has looked at, it is where the
  ratio tail lives, and it costs nothing — every report row already carries `shots` and `ghs_shots`.
  It is also the population `Replan.Improve` provably cannot help, which is level 9's lesson turned into
  a filter.
- **The test inside `--best-of-round`**, which is where the verdict belongs to [item 4](#4-7th--the-campaign-that-decides-whether---best-of-round-is-a-default).
  That flag judges a win by `keys / record ≤ 2.0`; the shot test and the ratio test **disagree on 58 of
  the 452 rows** — 41 wins the ratio test closes the round on although their shot count says the strategy
  is wrong, and 17 it keeps open although the shot count says the strategy is already right, so those
  rounds can only buy polish. The change is *keep the round open when `shots > ghs_shots` whatever the
  ratio; close it when `shots == ghs_shots` and the ratio is inside a looser bound*. **A level with no
  record keeps the round open, as it does today.**

---

## 5 (5th) — level 6's decomposition

*Level 10's half of this item is done: it is `--push-fire-tier`, it is
[Layer 9](layers.md#layer-9--exposure-as-a-tier---own-rung-the-population-pays-the-example-does-not), it
is worth +9 on the GAUNTLET tail, and it does not solve level 10. What is left of 10 is an open level with
no named candidate — session 29 cleared budget, closure, width and depth, and Layer 9 cleared the
derivation that diagnosis prescribed. **Do not spend another overnight run on it without a new
hypothesis**, and the cheapest place to look for one is the `frontier sweeps` column the tier added: it
says whether exposure stops falling, and if it does, where.*

**Level 6 wants layer 2's decomposition one level out:** commit to one (block, hole) pair, search only for
that, re-derive. It is the half furthest from falling on its own numbers — 142 pushes, six holes, and a
beam that fills two of them and then finds every successor of every board worse. Nothing above it is
blocked on it, and it **was** the one open item with no cheap falsifier — [item
15](#15-3rd----push-seed-k-the-editor-trick-as-an-instrument) is one, and it should be run first.

**Two things the human record says about this item's *design*, and they pull in opposite directions**
([`human-strategy.md`](human-strategy.md)). The series defines a phase precisely — *"divide a level into
phases, that is, your savings in one phase won't cause sacrifices in another"* — so **a phase is an
independence property**, which is testable off the maze BFS this item is going to run anyway: two carries
are in different phases when neither's block-reach region nor its tank route touches the other's. That
is the definition to build against, and level 6's six holes being a *strip* is the reason to expect it to
say *one* phase, which would be a real answer rather than a null one. But the same author pushes blocks
**two by two, not one by one**, because a lone carry pays the whole go-and-return, and level 6's hand line
moves four blocks in its first five board changes — so *commit to one (block, hole) pair* is exactly the
bet `--push-ferry-stage` already lost. **A phase has to be allowed to be a group of carries**, and item 15
is what says how big a group.

**Size it before building it, because the obvious sizing argument cuts the other way.** `barrier == 0` is
26.2% of the corpus but is solved at **25.2% against 7.0%** — an artifact of the bucket holding OPEN and a
mass of 12-key GAUNTLETs, since the sampled GAUNTLETs split 127 solved at a median record of 12 against
537 unsolved at a median of 138. **The population worth aiming at is those 537** (138 of them with a
record ≤ 60), and the cheap test is a rung over that tail, not a campaign. The full verdict table is in
[`history.md`](history.md#the-corpus-through-the-read).

**That cheap test has been run once, for Layer 9, and it is the reason the sizing paragraph is worth
keeping.** Two things it said that the sizing argument did not predict: **the tail is not as hard as its
median record suggests** (the control arm alone solves 76 of 138 at 40M, against the 23.9% the best
fourth-pass arm scores on the general failure population), and the fire tier is worth +9 solo and +15
exclusive on top of it. Regenerate the population list and its report with:

```bash
python - <<'PYEOF'
import json, io
an = {}
for line in io.open("build/reports/analyze-corpus.tsv", encoding="utf-8"):
    if not line.startswith("#"):
        f = line.rstrip("\n").split("\t"); an[(f[0], int(f[1]))] = f[3]
rows = [json.loads(l) for l in io.open("build/reports/chain.jsonl", encoding="utf-8-sig")]
keep = [r for r in rows
        if an.get((r["collection"], r["level"])) == "GAUNTLET"
        and not r["solved"] and 0 < r["ghs_moves"] + r["ghs_shots"] <= 60]
with io.open("bench/gauntlet-tail.txt", "w", newline="\n") as f:
    f.write("# item 5's population: chain.jsonl failures --analyze calls GAUNTLET\n"
            "# with a .ghs record of <= 60 moves+shots.\n")
    for r in sorted(keep, key=lambda r: (r["collection"], r["level"])):
        f.write("%s\t%d\n" % (r["collection"], r["level"]))
with io.open("build/reports/gauntlet-tail.jsonl", "w", newline="\n") as f:
    for r in keep: f.write(json.dumps(r) + "\n")
PYEOF
LT_SOLVE=build/lasertank-solve.exe JOBS=16 bash tools/gauntlet_tail.sh
```

`analyze-corpus.tsv` regenerates in ~3 minutes by the loop in [`instruments.md`](instruments.md) if
`build/` has been cleared.

**The corpus question the tier opened was item 2's, not this one's, and session 33 answered it:**
`$L8 --push-eval work --push-fire-tier` on the same 255-level stride is **66 of 255**, the best solo arm
measured, and it retires `l8work` rather than joining it. Nothing in that changes this item — the tier is
level 10's half of it and level 10 is still unsolved.

---

## 14 (6th) — per-level width from the record, and the calibration that sizes it

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

**The decision run** is item 5's 138-level GAUNTLET tail at a fixed 40M — the population is already
committed and the control has already been run twice — global width against record-derived width, read as
solved count and exclusive levels. **Raise-only, like `--max-keys-record`, so nothing that terminates
today stops terminating.**

**What not to re-derive:** *record shots = 0 as a licence to drop the space bar* was checked and is worth
nothing (17 levels of 3,709), and **parity** — the series' part 3, a chessboard colouring that fixes the
move count's parity — is an *optimality* tool and prunes nothing in a satisficing search. The shot test in
[item 13](#13-4th--the-shot-test-shots-against-the-record-as-a-route-diagnostic) is the same diagnostic
without parity's exceptions (ice, tunnels, tank movers, several flags).

---

## 4 (7th) — the campaign that decides whether `--best-of-round` is a default

**The acceptance half is done and it passed better than its bar.** The mechanism, the transcript and the
115-keys-against-294 result are in [`driver.md`](driver.md#not-settling-for-the-first-win----best-of-round-and---beat-banked).

**What is left is a number.** The measurement is a stride campaign with `--best-of-round` against one
without it, read as *keys* rather than as solved count — the solved set should be identical and the routes
shorter, and how much shorter is what decides whether this becomes a default. **Every ratio quoted in
these files was measured under first-win-cancels, so that campaign rebases them.**

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

## 7 (8th) — the solved-vs-budget curve

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
  13](#13-4th--the-shot-test-shots-against-the-record-as-a-route-diagnostic), and that one is measured.
- **"GHS shots ≪ the author's shots means the level has a shortcut"** — the human record's own rule for
  finding easy levels, and **the input is not in the files.** `TLEVEL` is 576 bytes of `PF`, level name,
  hint, author *name* and `SDiff` (`GameState.cs:118`); the author's own score is nowhere in a `.lvl`, and
  the only score the corpus ships is the `.ghs` record. Nothing to compare it against.
