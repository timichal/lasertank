# Next actions — the open items in full

**Six items are open — 20-24, written on 2026-09-15 out of what used to be a section called *Further
out*, and 25, which [closed item 7](history.md#7-the-solved-vs-budget-curve--run-and-it-saturates-below-10m) handed over on its way out on 2026-09-16.** The
*Further out* gate — *only after the numbers above have moved* — was met when item 2's fourth pass ran
and item 10 closed, and what it had been holding back were fifth-pass questions behind a fourth-pass
list. **None of 20-24 is new work.** What is new is that each carries a recipe, a cost, and the
measurement that would refuse it, which is what the rest of this file means by an item; three of them
turned out to have a falsifier costing minutes that nobody had run, which is the argument for writing a
bullet out as an item rather than leaving it as one. The reasoning behind the list is in
[`SOLVER.md`](../../SOLVER.md#what-is-open); this file carries the recipes, the costs and the evidence.
Items keep their numbers because these files refer to them by number — the finished ones are in
[*Closed items*](history.md#closed-items--the-measurements-including-the-negative-ones), including the
negative results, because a negative result that is deleted gets re-run. **20-25 are new numbers**, not
reused ones.

**All of it runs on the solver machine.** The two halves split on 2026-09-08, and every recipe below
except item 21's reads a report out of `build/reports/`, which is gitignored — so a checkout on the
game machine has all of the tools and none of the inputs, and the one item that has to be built and
gated *there* says so in its own entry (item 24). **Nothing left on this list wants the machine for more than a couple
of hours** — item 7, the one item that did, has run and closed — so the habit sessions 42-51 proved is
now a convenience rather than a constraint: a node-governed pass costs a cheap item its wall clock and
none of its numbers, and **items 20 and 25 are the same question on two flags and should be run in one
sitting**.

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
in nodes **do not tie at 4M**, so the follow-up is a budget curve rather than any new width item, and the
five banked reports mean any rung of it can be read without re-running a control. Five-arm union
**101 of 138 (73.2%)**. **[Item 7](history.md#7-the-solved-vs-budget-curve--run-and-it-saturates-below-10m) was that curve, and it did not carry this
question with it** — it curved the *chain's* four searchers, not the push arms — so this is still open
and is not on the list below as an item, with one caveat item 7's result adds to it: **ask it below 40M**,
because two searchers that stop on structure rather than on nodes tie at every budget you can afford.
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
subgoal sequence nothing reads yet — [item 22](#22-4th--subgoal-chaining-over-board-changes), whose
acceptance test is already in a file. **What it does not give is human *routes***: a goal board names the destination, not
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
clock and none of its numbers**. **The flip landed in session 51 and re-gated `IDENTICAL`; session 53
then declined the board-keyed second layer off the code and closed item 10, so what was left was one
measurement** — item 7 — **and session 54 spent it in 100 minutes**: it wanted the whole machine and
[used a quarter of its price](history.md#7-the-solved-vs-budget-curve--run-and-it-saturates-below-10m), because the searchers it was buying nodes for had
already stopped searching. **That leaves nothing on this list costing hours.** Item 20 is the longest
at ~2 h and item 25 rides with it; the rest are minutes.

**`LaserTank.lvl` 6 is no longer an item** — closed item 5 measured that its line comes apart into six short
phases and that the search cannot walk two of them from *any* board, the human's included, so the line
being 3.4x the reach turns out not to be
short.

---

## 20 (1st) — `--push-depth` on the `enables` arm

**The cheapest unclaimed thing in these files, and closed item 2 turned it up without being asked.**
`enables` is the only arm of the fourth pass that stops on `push-depth` at scale — **177 levels of
3,691**, against `l8fire`'s 6 and `layer7`'s 3 — and those stops leave a median **10M of the 40M nodes
unspent** (p50 30.1M used). The flag is documented as *a backstop only*: `Search.cs:291` sets
`PushDepth = 1200` board changes and `Push.cs:705` consumes it as the frontier loop's bound. On that
arm, at that budget, the backstop is the thing that ends the search.

**The population is banked and the control is free.** Those 177 are exactly the rows of
`build/reports/l5-enables.jsonl` whose `stop` is `push-depth`, every one of them a failure — so the
control is **0 of 177** and any level that falls is a level the fourth pass left on the table. Filter
the banked report to those rows and re-attack them with the cap lifted and the arm otherwise identical;
`second_pass.sh` re-attacks the unsolved rows of whatever report it is given, so the filtered report
*is* the level list:

```bash
python - <<'PY'
import json
src, dst = "build/reports/l5-enables.jsonl", "build/reports/l5-enables-depth.jsonl"
rows = {}
for line in open(src, encoding="utf-8-sig"):
    line = line.strip()
    if line:
        r = json.loads(line); rows[(r["collection"], r["level"])] = r    # last line wins
keep = [r for r in rows.values() if not r["solved"] and r["stop"] == "push-depth"]
open(dst, "w").write("".join(json.dumps(r) + "\n" for r in keep))
print(len(keep), "levels ->", dst)                                       # expect 177
PY

BASE="--no-ida --no-beam --push --push-read --max-keys 5000 --max-keys-record"
NODES=40000000 BUDGET_MS=1800000 JOBS=16 bash tools/second_pass.sh \
      build/reports/l5-enables-depth.jsonl l5/enables-depth \
      build/reports/l5-enables-depth-out.jsonl $BASE --push-enables 8 --push-depth 100000
python tools/verify_solutions.py build/l5/enables-depth
```

**`--push-depth 100000` is uncapped rather than raised, and that is the whole design of the arm.** An
intermediate value asks *is 4,000 better than 1,200*, which nothing here wants to know; uncapped asks
*does this cap bind at all*, which is binary, and the node budget is still the real bound. Everything
else on the line is `l5_pass.sh`'s own `BASE` plus its `arm_flags enables`, copied rather than retyped,
so a difference against the banked rows is the cap and nothing else.

**Cost is arithmetic off closed item 2, not a `price` run, and it is labelled that way on purpose.**
The pass's three arms averaged 22.5 h of wall clock over 3,691 levels at 16 jobs; these 177 are 4.8% of
that population, so **~1.1 h** if they cost the arm's average — and they do not, because each has a
median 10M nodes it was not allowed to spend and will now spend it. **Call it two hours of wall clock,
derived**, and if that matters, `SAMPLE=4` on `second_pass.sh` prices it against a quarter of the list
first.

**What refuses it:** the 177 re-attacked uncapped and **0 solved**, in which case `push-depth` was the
symptom of an arm already lost rather than the wall, and the flag's own documentation was right. One
intermediate outcome is worth predicting before the run so that it is not read as a result: if the
stops move from `push-depth` to `budget` and nothing solves, that is the clean negative; if any level
stops on `push-depth` *again* at 100,000, the cap is not the quantity anyone thinks it is and this is a
bug report rather than a measurement.

**Run it with [item 25](#25-2nd--sg-depth-on-the-chains-subgoal-passes) in the same sitting.** That item
is this one's twin — `--sg-depth`, also documented as a backstop, also binding, on the *chain's* subgoal
passes rather than on a push arm — and it was found by closed item 7 nine days after this one was found
by closed item 2. **Two independent searchers have now been caught bounded by a depth constant nobody
sized**, which is worth more as a pair of readings than as two items a month apart.

---

## 25 (2nd) — `--sg-depth` on the chain's subgoal passes

**Closed item 7 turned this up on its way out, and it is item 20's question one flag over and one
searcher across.** `--sg-depth`'s own help text is the claim being refused: *"subgoal steps, default 400
— a backstop, the node budget binds long before it"* (`Program.cs:190`). The budget curve measured that
at 50M nodes and it is false. Of `l3`'s 233 misses, **156 stop on `subgoal-depth`** against 17 on
`budget`; of `l4`'s 232, **163 against 12**. `Subgoal.cs:159` sets that stop when
`for (int step = 0; step < _opt.SgDepth && frontier.Count > 0; step++)` falls out with a **live
frontier** — so those levels walked 400 subgoal steps, still had somewhere to go, and were cut. **They
had a median 2.78M of 50M nodes left, 94% of the budget unspent, after a median 13.8 s.** Item 20's
`enables` arm leaves a median 10M of 40M on the table; this leaves 47M of 50M.

**The population is banked and the control is free**, which is what makes this cheap: the 50M rung's
`l3` and `l4` reports are in `build/reports/` from session 54, so the arm is one re-run against rows that
already exist.

```bash
NODES=50000000 bash tools/second_pass.sh build/reports/curve-50000000-l0.jsonl solutions/sgdepth \
  build/reports/sgdepth-l3.jsonl --no-ida --no-beam --subgoal --sg-eval coarse --sg-depth 100000
python tools/verify_solutions.py build/solutions/sgdepth
```

**~30 min of wall clock at 16 jobs**, derived off the banked rung's own rate (2 h 09 m of job time for
`l3` at 50M) rather than a `price` run. **Run it in the same sitting as item 20** — same question, same
shape of answer, two flags — and read both against *does this cap bind*, which is binary, rather than
against *is 100,000 better than 400*, which is not a question.

**What refuses it:** the arm solves the same 10 levels and the stop reasons move from `subgoal-depth` to
`budget` with nothing else changing. That is the cap doing its job as a backstop after all, on a budget
big enough to reach it, and it closes negative in half an hour.

**The nuance that decides which fix is right, and it is why the arm is uncapped rather than raised.**
`--sg-slack` keeps board-changing successors that removed nothing, so a step is not necessarily an
obstacle gone and a beam can spend 400 steps making no progress. If lifting the cap converts
`subgoal-depth` into `budget` **without** solving anything, the cap was not the constraint — the slack
was — and the follow-up is `--sg-slack`, not a bigger number. **The two outcomes are distinguishable in
the same run**, which is the whole reason to run it uncapped: a raise to 4,000 would confound them.

---

## 21 (3rd) — the two bench-1 lists through one binary

**Minutes, and it decides whether a whole class of number in these files can be read at all.** Session
48 brought back the **originals** of all three level lists from the game machine's `build/` —
`bench/recovered/{bench,deep,ferry}-levels.txt`, the ones `bench/bench-levels.txt`'s header calls gone.
They rebase nothing and restore nothing: every pre-session-25 number was measured against those levels
*by a solver that no longer exists*, so re-running them today compares two code versions, not two
lists. The committed reconstructions stay the lists current numbers are quoted against.

What the pair makes possible is the one measurement neither list could make alone — **the same binary
over both bench-1 lists, which isolates the population from the code.** Every bench-1 delta this
project has ever argued about confounds the two. Both files are 60 Beginner-I levels and `bench.sh`
defaults to that collection, so neither run needs `LEVELS`:

```bash
bash tools/bench.sh b1-recon bench/bench-levels.txt           150000 --no-macro
bash tools/bench.sh b1-orig  bench/recovered/bench-levels.txt 150000 --no-macro
```

150k is the campaign's own budget and `--no-macro` is layer 0, which is the configuration both lists
were cut by — 60 levels of it is minutes. **If the two separate at layer 0, stop there**: they are
different populations and that is the answer. If they do not, the same pair through the chain's three
searchers (`--no-ida`; `--no-beam --subgoal --sg-eval coarse`; then `learned`) says whether they stay
together where the numbers people actually argue about were measured.

**It has already retired one guess for free.** `bench/bench-levels.txt`'s header supposes the
original's GAUNTLET-heavy label "was a pre-fix read", and read **today**, post-barrier-fix, the
original is still **GAUNTLET 18 / FERRY 7** against the reconstruction's **8 / 30** — the difference is
the population, not the read. That is the *read* agreeing with itself across the pair; this item asks
the same question of the *searcher*.

**The second use is held-out and it is the weaker of the two.** Those 18 GAUNTLETs are **16 disjoint
from `bench/gauntlet-tail.txt`**, so they are a near-independent GAUNTLET population for a fire tier
whose +9 has been measured on exactly one — but they are *layer-0* failures, not *chain* failures, so
how many the chain still fails is unknown until a report says so, and the standing rule applies: two
fifty-level lists agreeing is not a population. **Ask the cheap question first**; the held-out one
needs `chain.jsonl` and a filter before it means anything.

---

## 22 (4th) — subgoal chaining over board changes

**The pieces exist, the acceptance test is already in a file, and the first step is free.** Both open
levels ask for layer 2's decomposition one level out. The acceptance test in `Subgoal.Offer`
(`Subgoal.cs:344`) is a board test; for a gauntlet the board test is *the safe flood gained a named
route cell* (`--push-reach`'s flood, set inclusion rather than count), and for a Sokoban it is *block b
stands on the next cell of its maze path* (`--push-ferry-maze`'s BFS already produces the path).

**Run the arithmetic before writing a line of it**, which is what items 5, 17 and 19 were each closed
by and what this item has carried as a note since it was written:

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl \
      --levels-list <(printf '6\n10\n') --analyze
```

— how many subgoals does the read name on each level, and how deep is each one on the hand line? The
sizing that makes the item worth building is that a sub-search for one subgoal on level 10 is width 64
x depth <= 6 x ~5,100 nodes per expansion ~= **2M nodes**; ten shields is 20M, the budget one round of
the driver already spends. The outer search is over the *order* of subgoals, depth-first with
backtracking, re-deriving the read after each — at most 6! orderings on level 6 and mostly pruned by
the matching. **What refuses the item at this step, for the price of two `--analyze` runs:** a read
that names materially more subgoals than that, or a hand line whose subgoals run deeper than ~6 board
changes each, turns 20M into a number the driver does not have.

**Closed item 5 is the thing this has to beat, and it is the reason this is 4th rather than 2nd.**
Level 6's line does come apart into six short phases, `--push-phases` does commit to the right first
board — its first fill is (9,14), the cell the human fills first — and **phase 2 never lands at width
32, 128 or 512, and not from the human's own board either.** A subgoal chain is a better decomposition
than a phase, but it is still a decomposition, and what beat level 6 was the **ranking** in the middle
of a phase: `best=137` flat for 237 depths, the same signature as level 10's GAUNTLET. Build this
knowing that, or it will re-derive item 5's negative at a higher price.

**What it inherits that it did not have when it was written:** the goal-board bank's per-flag boards
are a subgoal sequence nothing reads yet — `bench/goal-boards.json`, **6,030 levels / 7,622 goal
boards**, 0 unknown tiles — which is a supply of acceptance tests rather than the two levels this item
was sized on. The bank is hint-assisted and outside the rate, which is the right side of that line for
a test.

---

## 23 (5th) — a FESS-shaped rung for the Sokoban/ferry half of the corpus

**The largest build on this list, and it has a free falsifier nobody has run.** FERRY + SOKOBAN is 53%
of the sample at 5.1% solved, and level 6's diagnosis — *a greedy level-synchronous beam in a region
where every successor of every held board is worse* — is the textbook failure of beam search on
Sokoban. The textbook answer is **FESS** (Shoham & Schaeffer, *The FESS Algorithm: A Feature Based
Approach to Single-Agent Search*, IEEE CoG 2020), the first solver to clear all 90 XSokoban levels. The
shape: project states into a small **feature space** (boxes packed, connectivity = the number of
regions the player is cut into, room connectivity, boxes out of plan); advance by *cycling through the
occupied feature cells* and expanding the best state in each; weight moves by "advisors" that say which
pushes serve which feature.

**Two things about it belong here.** It is the general form of two devices these files arrived at by
measurement — the per-board cap (diversity across boards) and *commit to one block-and-hole pair* (a
progress cell searched on its own) — so the fit is not speculative. And it respects the fidelity rule:
the engine still generates every state; the features are `Heuristic.cs` quantities that already exist
(holes filled, `TankRegion`, ferry-maze distance, `RouteDead`), and the advisors are the read's
derivations under another name.

**The free step is first, and it is free because the columns already exist.** `--analyze-tsv` carries
`work route_obst poses region barrier water blocks threats effects shots indirect on_barrier toward
opens flag_reachable alive mob_max mob_sum`, joinable against any report on `(collection, level)`, and
the whole corpus is one ~3-minute loop ([`instruments.md`](instruments.md)). So the feature space can
be *projected* before any search is written:

```bash
for c in Beginner-I Beginner-II Challenge-I Challenge-II Challenge-III Challenge-IV Challenge-V \
         Gary-I Gary-II LaserTank Sokoban-I Sokoban-II Special-I; do
  build/lasertank-solve.exe --levels "data/levels/$c.lvl" --analyze-tsv "build/reports/an-$c.tsv"
done
# then join against build/reports/chain5.jsonl and cross-tabulate the FERRY + SOKOBAN rows on
# (blocks x region) -- the two shipped columns closest to FESS's first two features
```

**What that refuses, for three minutes:** FESS *is* cycling through occupied feature cells, so it buys
nothing if the levels the chain fails all land in one cell, or if solved and unsolved land in the same
cells at the same rate. A feature space that does not separate the population it is aimed at is not a
feature space, and this is the one thing that can be known before the build.

**Its second gift is the packing order**, derived backwards from the goal: which hole must be filled
before which, from where a block can still be pushed *after* the others are down. `--push-ferry-match`
is the forward half of that; the backward half is what level 6's strip of six holes wants.
**Independent confirmation from the human record**, which is worth something for a device chosen out of
a paper: the series' advice for a level you cannot crack is to work *"either forward from the starting
position or backwards from the ending position"*, and its author — ~70 records, 120 Deadly levels
solved — names pure Sokoban with no clear phases as the shape he is personally worst at. Our
worst-solved non-trivial class is that one. See [`human-strategy.md`](human-strategy.md).

**Measured as a union, like every rung before it**, against the banked FERRY + SOKOBAN failures in
`build/reports/chain5.jsonl`: a rung that adds levels the five banked reports do not already hold, or
it does not ship. **No wall clock is quoted here and that is deliberate** — it is the one item on this
list whose build is larger than its run, and pricing a build before the projection above has run would
be the kind of number these files exist to avoid.

---

## 24 (parked) — parent pointers instead of copied keystreams

**Kept numbered because it has a trigger, and parked because the trigger has fired exactly once.**
`Snapshot` copies the whole consumed key prefix (`Engine.Search.cs:194`,
`Array.Copy(RecBuffer, s.Keys, s.KeyLen)`) and `Restore` copies it back at `:233`, so on a 900-key line
every node moves ~1.8 KB of keys on top of its 1 KB of boards, and a `Node` holds all of it — which is
why 76,800 wide was 1.1 GB. Each node keeping only the keys since its parent, with the path rebuilt on
a win, cuts both the copy and the residency several-fold.

**The trigger, written down so that the item does not have to be re-derived:** a run whose width is
bounded by memory rather than by nodes. Width was that wall exactly once, on level 9, and everything
measured since has argued the other way — closed item 14 lost every arm in the *wider* direction
(76 / 78 / 78 against a control's 85) and closed item 19 tied in the narrower one, 32 against 128 — so
**no open item on this list wants a wider beam**, and a saving that buys width buys nothing until one
does. Closed item 10 is the shape of the counter-argument: 918 KB a worker across sixteen workers
already costs more in shared cache than it returns, and keystream residency is the same currency.

**And it is the one item here that changes `LaserTank.Core`**, which is the standing objection and the
reason it is written down rather than left implicit. `Engine.Search.cs` is the file
[`PROGRESS.md`](../../PROGRESS.md) names as unchanged since the solver's first layer, with the rule
attached: *if a solver change seems to need an engine change, that is the signal to stop and re-read.*
It survives the re-read — nothing in `LaserTank.Game` or `LaserTank.Cli` references `Snapshot` or
`Restore`, so the pair cannot move a rule — but Core is gated on the **game** machine, so the order is
fixed: build there, run the fidelity gates, and only then let the solver machine trust the binary.

```bash
bash oracle/build.sh && bash src/build.sh
python tools/replay_all.py && python tools/test_difftrace.py && python tools/sweep.py
```

---

## Checked, and not opportunities

- **Raising the chain's node budget, as a way to buy levels:** the curve ran at 1M / 10M / 50M over item
  2's 1-in-15 stride and came back **12 / 20 / 21 of 253** — and **20 of those 21 are already inside the
  fourth pass's 1,087**, so the whole curve's marginal contribution is *one level*, had at the cheapest
  rung in 3.8 s. Above ~10M the chain is not budget-bound at all: `l0`'s misses have the same median node
  count at 10M and 50M to the node, and no solution in the run needed more than 13.2M. `tools/curve_pass.sh`
  stays as the instrument for *is this searcher still searching?*
  [Closed item 7](history.md#7-the-solved-vs-budget-curve--run-and-it-saturates-below-10m).
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
