# Next actions — the open items in full

Three items are open. The order and the reasoning behind it are in
[`SOLVER.md`](../../SOLVER.md#what-is-open); this file carries the recipes, the costs and the evidence.
Items keep their numbers because these files refer to them by number — the finished ones are in
[*Closed items*](history.md#closed-items--the-measurements-including-the-negative-ones), including the
negative results, because a negative result that is deleted gets re-run.

Order: **2, 7, 10**. **[Item 4 closed on 2026-09-14](history.md#4-the-campaign-that-decided-that---best-of-round-is-a-default--and-the-shot-rule-won-it)**
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
in nodes **do not tie at 4M**, so the follow-up is [item 7](#7-2nd--the-solved-vs-budget-curve)'s budget
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

**Item 2 is the machine commitment and everything below it can run beside it** (started 2026-09-10,
interrupted in session 46 partway through arm 1, resumable — see the item).
It is a machine commitment rather than a build, and its arms are node-governed, so extra load moves
wall-clock readings and nothing else. Sessions 42 and 43 spent the whole of items 16, 15 and 13 beside
it — a two-second replay, an offline sweep, a 63-second corpus pass, a flag and ten seeded runs — and the
running pass saw none of it except in wall clock, and session 44 spent item 17's build and its five
benches the same way — four jobs beside the pass's sixteen, ~3.5 min a bench, node-governed throughout.
**Items 17 and 18 were both spent beside it in session 44** — a build and five benches, then 96 probes
at four jobs against the pass's sixteen — and it saw neither except in wall clock. **Session 45 spent
item 5 the same way**: two instruments that cost no search worth speaking of, a build, and ~20 probes at
one job against the pass's sixteen. **Session 46 is the exception and it is worth naming**: item 14's
third arm was run with the pass *stopped* rather than beside it, so its wall clock is the only one in
these files measured on an idle machine. No measured number moves — the arms are node-governed — but
the four hours of arm 1 that are outstanding are four hours that were not spent. **What is left after
it is machine time and one default flip.** Session 50 spent items 7 and 10 beside the pass the same way
and both came back with the thing they were missing: item 7 has `tools/curve_pass.sh` and, from the
script's own `price`, **45 h of job time / ~7 h wall** instead of an estimate, and item 10's memo is
**built, gated `IDENTICAL` on three rungs and 1.67x on the arm the pass is running** — `--push-memo`,
off by default, `bash tools/push_memo.sh`. The one thing session 50 changed about the *list* is that
item 10's remaining work is no longer a build: it is a one-line default flip, plus an optional second
memo layer worth ~1.12x more.

**`LaserTank.lvl` 6 is no longer an item** — closed item 5 measured that its line comes apart into six short
phases and that the search cannot walk two of them from *any* board, the human's included, so the line
being 3.4x the reach turns out not to be
short.

---

## 2 (1st) — the fourth pass, and its fourth arm is rehearsed

**Started on this machine 2026-09-10, and interrupted in session 46 with arm 1 four hours from the
end** — `bash tools/l5_pass.sh`, the recipe under [*The full run*](#the-full-run) below;
`bash tools/l5_pass.sh status` says where it is. As of the interrupt, `l8fire` is
**3,092 of 3,691 attempted, 770 solved (24.9%)**, 20 h 29 m wall and 267 h 54 m of job time at 13.1x
parallelism, ETA **4 h 44 m** to finish the arm; `layer7` and `enables` have not started. **That puts
arm 1 at ~25 h against the ~18 h the rehearsal priced**, so the ~54 h below is optimistic by about half
an arm each — call the whole pass ~75 h, and the remaining two arms ~50 h of it. The pass is
resumable from its own report, so the same command picks up at level 3,093 rather than at the top —
that is what `RESUME=1` is for and it is the default. **The 24.9% is the solo rate of the best arm on
the whole population, not the pass's number**, which is `arms_union.py` over three arms at the end; the
stride rehearsal put the same arm at 25.9% and the three-arm union at 32.9%, so this is tracking.

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

## 7 (2nd) — the solved-vs-budget curve

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

### Instrumented in session 49 — and the profiler the item asked for was the wrong one ☑

**Where this sits: in the working tree, unstaged.** `--push-time` is `Push.cs`, `Heuristic.cs`,
`Search.cs` and `Program.cs`, built and checked but not committed, and `build/lasertank-solve.exe` does
*not* have it — item 2's pass holds that file open, so the only build of the flag is the project's own
`src/LaserTank.Solver/bin/Release/net8.0/lasertank-solve.exe` (`LT_SOLVE`, the same route
`bor_campaign.sh` takes). Rebuild it with
`dotnet build src/LaserTank.Solver/LaserTank.Solver.csproj -c Release`. Nothing below is banked in a
report: these are single runs kept in this file, and every one of them was taken beside the pass.

**`dotnet-trace` cannot measure this loop, and the way it fails is worth keeping.** It was installed
for this (`dotnet tool install --global dotnet-trace`) and the answer it gave was an artefact — do not
reach for it again on the search loop; `--push-time` is what replaced it. Its sampled stacks
are taken where a suspended thread can be walked, so on a tight search loop they cluster at safepoints:
over one 6M-node run of the shipped rung it attributed **64% of the time to the budget check**
(`Solver.get_OutOfBudget` to `Stopwatch.ElapsedMilliseconds`) and put `PushH` at **5.9%**. Both are
wrong, and cheaply shown to be: a build with the clock read deleted outright runs **no faster** (33.8 /
32.4 s against 33.0 / 31.6 s with it, the same 6M nodes), and `Stopwatch.ElapsedMilliseconds` measures
**21.8 ns** a call on this machine, which prices the whole per-node check at **0.4%**. Disabling
inlining (`DOTNET_JitNoInline=1`) moved the same 64% off `ExpandPush`'s self time and onto
`get_OutOfBudget` by name, which is what made the artefact legible rather than merely large.

**`--push-time` is the instrument instead**, and it is the run's own timestamps rather than a sampler's
guess at them: one line per level, the buckets nested the way the code is, plus the count of timestamps
taken and what one costs so its own share can be subtracted. Off by default, like every other instrument
in `Push.cs`. A timestamp is ~20 ns against ~5 us an expansion, and the flag costs **1-3%**, inside the
noise band of an unmeasured run (33.25 / 32.27 s without it, 34.16 s with). **Inert when off, checked
rather than asserted**: 30 `LaserTank.lvl` levels (20 solved) through the build before it and the build
after, same nodes, same keys, same stops, and the 20 `.lpb` files **byte-identical**.

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 10 \
    --jobs 1 --nodes 6000000 --no-ida --no-beam --push --push-read --push-beam 8 \
    --out build/pt --report build/reports/pt.jsonl --force --quiet --push-time
```

**Where the seconds go.** `LaserTank.lvl` 10, 6M nodes, one thread, beside the running pass — the loads
match within each column, and the split is node-identical by construction:

| bucket | shipped rung, width 8 | `l8fire`, width 128 (the pass's arm) |
|---|---:|---:|
| the whole expansion | 33.01 s | 47.90 s |
| `ApplyKey` — the engine, all 6M nodes | 6.50 s (20%) | 7.18 s (15%) |
| **`PushH` — per emitted successor** | **13.67 s (41%)** | **22.81 s (48%)** |
| `ReadTier` to `Opens` to `TankRegion` | 4.55 s (14%) | 5.73 s (12%) |
| the fire tier | — | 0.03 s (0%) |
| the expansion's own book-keeping | 8.29 s (25%) | 12.15 s (25%) |
| the width trim, outside the expansion | 0.24 s | 0.16 s |

**So the item's premise is right and the sampler's answer was noise: `PushH` is the largest bucket in
both configurations, and on the arm the pass is actually running it is nearly half the clock.** The
engine is 15-20% and is not the problem; the read is 12-14%.

**Inside `PushH`, sorted by the key each part would memoise under** — the number that decides the design:

| part of `PushH` | shipped rung | `l8fire` | memo key |
|---|---:|---:|---|
| the priced Dijkstra from the flag | 8.45 s | 9.15 s | board |
| `BuildAlive`, the frozen-block test (`--push-dead`) | 0.02 s | **7.87 s** | board |
| `BuildFire`, the fire map | 0.01 s | 3.41 s | board |
| `BuildReach`, the safe flood | 0.01 s | 2.00 s | **board + tank cell** |
| `Rank` — `Feat.Extract` and `FlagDistance` | 4.89 s | 0.02 s | board (+ tank cell) |
| **board-only, as a share of `PushH`** | **62%** (8.48 s) | **90%** (20.44 s) | |
| from the tank, as a share of `PushH` | 0% | 9% (2.00 s) | |

**And the multiplier the memo would buy, measured rather than guessed.** Per expansion, over the same
runs: **1,115,019 successors on 4,797 distinct boards — 232x** at width 8, and **1,364,612 on 50,029 —
27.3x** on `l8fire`. Keyed by `(board, tank cell)` instead it is **10.9x** and **6.5x**. The item's
"four boards wearing thirty-nine hats" is right about the boards and an order of magnitude low about the
hats: on `l8fire` it is **51 boards wearing 1,400 hats**, every one of them re-deriving the same fire
map, the same frozen-block test and the same Dijkstra table.

**What that prices the fix at, and it is not two to four times.** On `l8fire`, memoising the board-only
terms saves 20.44 x (1 - 1/27.3) = **19.7 s of 47.90** and the reach flood a further 1.7 s, so the
expansion goes to ~26.5 s: **~1.8x**. At width 8 the board-only 8.48 s at 232x is worth 8.44 s of 33.01
and `Rank`'s 4.89 s is mostly board-only too, so **~1.3x to 1.7x** depending on how much of `Feat` is
lifted. `ApplyKey`'s 15-20%, the read's 12-14% and the expansion's own 25% are untouched by any of it,
and they are what caps the whole item **below 2x**. The 8.4x gap to layer 0 does not close here.

**`BuildAlive` is the surprise and the first thing to memoise.** It was not in the item's list at all,
and on the arm the pass runs it is **7.87 s — 16% of the whole expansion**, second only to the Dijkstra
inside `PushH` and the purest board function of the lot: `--push-dead` re-derives which blocks are
frozen for all 1,400 hats of each of 51 boards.

**One measurement that does not match the table above it.** The table says `--push-eval none` runs at
171k against `coarse`'s 163k and concludes the ranking is not the cost; `--push-time` prices `coarse`'s
`Rank` at **4.89 s of 33.01 — 15%** on the same level, where `work` costs 0.02 s. Both were measured;
they are not the same run — the ablation's two arms walked different boards, which is the caveat the
table already carries — so what stands is the `--push-time` number and what falls is the inference drawn
from the pair.

### Then memoise — built and gated in session 50, and the key is not the one this item named ☑

**`--push-memo` ships off by default, and on the arm the fourth pass runs it is 1.67x.** Three rungs,
the same level list and the same node budget under both arms, `bash tools/push_memo.sh`:

| rung | job time, memo off | on | speedup | nodes/s off → on | memo hit rate |
|---|---:|---:|---:|---|---:|
| `rung8` — the shipped rung, width 8 | 16.8 s | 12.0 s | **1.40x** | 166,627 → 232,698 | 98.3% |
| `l8fire` — the pass's arm, width 128 | 177.4 s | 106.5 s | **1.67x** | 103,594 → 172,647 | 84.8% |
| `layer7` — the one arm with `--push-stop` | 111.4 s | 78.5 s | **1.42x** | 152,744 → 216,604 | 84.6% |

50 levels of `bench/deep-levels.txt` at 400k nodes, four jobs beside item 2's pass. The `166,627` is
this item's own opening number reproduced to three figures, which is the cheapest evidence that the two
measurements are of the same thing. On `LaserTank.lvl` 10 at 6M nodes and one thread the expansion goes
**53.50 s → 31.31 s** on `l8fire` (1.71x), **34.18 s → 24.20 s** at width 8 (1.41x) and **41.46 s →
27.16 s** on `layer7` (1.53x) — so the bench and the single level agree, and the item's predicted
"**~1.8x** on the push rung" and "capped below 2x" were both right.

**The key is the pose, not the board, and that is the one thing this item had wrong.** The census prices
a board-keyed memo of the *board-only* terms at 27.3x and a pose-keyed memo of *everything* at 6.5x, and
the second is the larger saving: 90% of `PushH` at 27.3x saves 87% of it, 100% of it at 6.5x saves 85% —
near enough the same number — and the pose key also collects `Rank`, the reach flood and the ferry
matching, which the board key cannot. It is also a far cheaper build. The two complications this item
listed for a board-keyed memo — the Dijkstra's early exit, which forces a shared table to be run to
completion, and the six `Route*` side effects `WorkDistance` publishes — **both disappear**, because the
pose memo never splits `WorkDistance` open at all: it caches what `PushH` returns and the seven fields it
publishes, and calls the whole thing when it misses.

`PushH` is a pure function of `Game.PF`, the tank's cell, and `Game.PF2` — **only** under `--push-stop`,
whose `StopPrice` is the single thing in the heuristic that reads what is underneath a block. So PF2 is
hashed only for that one arm, and `layer7` is in the gate above for exactly that reason: it is the one
configuration whose key has that branch in it.

Direct-mapped, 4,096 slots, a 128-bit content hash (two independent FNV-1a chains over the same bytes)
and no stored copy to verify against — a 200,000-pose level collides at about 1e-28, and a slot that
holds some other board simply misses. The table is cleared per *level*, not per expansion: the census
counts distinct boards **within** one expansion, so its ratios are a floor on the reuse rather than the
whole of it, and a table that outlives the expansion collects the next depth's revisits and a restart's
whole re-run too. At width 8 that is the difference between the census's 10.9x ceiling and the **60.6x**
the memo actually collected. 4,096 slots is 229 KB a worker and was measured, not chosen: 1,024 slots hit
83.7% and 16,384 hit 86.6% against this table's 84.8% on `l8fire`, and neither moved the expansion outside
the noise.

**The gate is the acceptance test this item set itself, and it is in the script.** Each rung runs twice
over the same levels at the same node budget, and the two runs have to agree on `solved`, `keys`,
`raw_keys`, `moves`, `shots`, `ratio`, `trimmed`, `polished`, `replanned`, `method`, `stop`, `depth`,
`restarts` and `nodes`, and on the **bytes of every `.lpb`**. All three rungs: `IDENTICAL`. The table of
seconds is not printed at all if the gate fails, because a run that changed a number is not a slower or a
faster run, it is a wrong one.

```bash
bash tools/push_memo.sh          # the gate and the table, all three rungs, ~12 min
bash tools/push_memo.sh gate     # equality only
bash tools/push_memo.sh bench    # seconds only
bash tools/push_memo.sh time     # the --push-time split, one level a rung
```

**`BUDGET_MS` in that script is an hour and it has to be.** At the 4-second default the memo arm does not
finish sooner, it searches *further* — 1.70x the `ApplyKey` calls of the control in the same wall clock —
and the two arms then walk different boards, so neither the gate nor the seconds mean anything. That is
the speedup showing up in the one form this measurement cannot read.

**What is left on the table, measured.** On `l8fire` the memo still misses 207,139 of 1,364,613 calls, and
`PushH` is still 4.67 s of a 31.31 s expansion, of which 80% is board-only. Those misses fall on 50,029
distinct boards, so a *second* layer — the board-keyed memo this item originally described, underneath the
pose memo — would divide that 3.73 s by the remaining 27.3 / 6.5 = **4.2x** and save ~2.8 s: the expansion
goes to ~28.5 s and the rung to **~1.88x**. That is the whole of the remaining headroom, and it is the
expensive half of the build (the early-exit Dijkstra and the six side effects are still waiting there).
`ApplyKey`'s 24%, the read's 20% and the expansion's own 41% are untouched by any of it and are what caps
the item below 2x, exactly as this item said.

**Two things not to read into the numbers above.** They were taken beside item 2's pass, so the seconds
are a loaded machine's; the ratios are the point and both arms carried the same load. And the memo is
**off by default** — flipping it on is a one-line change and the gate above is the evidence for it, but
nothing in these files has been re-measured with it on, and the seconds in every table above it are the
searcher without it.

**Both alternative explanations for the 166k are already ruled out**, which is why this item is now the
whole of the wall-clock story rather than one of three guesses at it: `--push-eval none` runs at 171k
against `coarse`'s 163k (but see the mismatch above); and `sterile=` is 0.05%, so wasted expansions are
not the cost either. **The 8.4x gap to layer 0 is `PushH` itself** — measured at 41-48% of the
expansion, which is most of what separates the two rungs but not all of it.

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
