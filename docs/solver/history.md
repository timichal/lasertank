# Closed items, the corpus tables, and the session log

Cold storage for the solver. **Nothing here is open** — it is kept because a measurement that is deleted
gets re-measured and a negative result that is deleted gets re-run, which is `bench/`'s own lesson. What
is open is in [`next-actions.md`](next-actions.md); the design record is in [`layers.md`](layers.md).

---

## Closed items — the measurements, including the negative ones

Numbered as [`next-actions.md`](next-actions.md) refers to them. **Every item from 1 to 19 is done
except 7, and nine of those closed *negative* or half-negative** — which is the ordering rule
paying off rather than failing. The largest of the positives is
[item 2](#2-the-fourth-pass-run-over-the-corpus--1087-levels-and-the-stride-ranked-the-wrong-second-arm),
which took the corpus from **494 to 1,581 of 4,185** and is the only item here that cost days of machine
time rather than hours. Two of the negatives (17, then 5) are items that reached
the front of the list and were **refused by falsifiers they built for themselves**, which is the rule
working at its most expensive and most useful; item 14 is the third kind, an item whose decision run
came back clean and negative, and which **handed the list a better-aimed successor on its way out**.

### 1. Layer 4's learned evaluation did not act. Two defects, both fixed.

`Eval.Score`'s integer divide, and `Rank()` testing `_eval != null` instead of its caller's flag. The
composite went **476 → 494**, a strict superset, 96 of 96 new solutions gated. The full account — the two
defects, the four ranking keys (`work` / `coarse` / `learned` / `none`) that came out of them, the push
bench that splits by population, and the `--push-hand-scale` sweep that found `Eval.Scale` to be the
wrong value for it — is in
[layer 4](layers.md#the-two-defects-that-kept-this-layer-inert-and-the-four-ranking-keys-that-came-out-of-them).

**What it cost, stated plainly:** nothing in `Weights.cs` changed and nothing was refit. The model was
correct all along and two lines of arithmetic between it and the beam were not. The +30 these files once
credited layer 4 with was never measured through the path that ships (`Weights.cs` and the divide arrived
in the same commit, `8752317`), and it is still not recovered — **+20 is what the learned key is worth on
this tree**, on top of a pass 2 that is itself a learned key.

**The push side stayed open and became item 2's question**, which is where it was answered over a
population rather than over two benches.

### 2. The fourth pass, run over the corpus — 1,087 levels, and the stride ranked the wrong second arm.

**The composite is 494 → 1,581 of 4,185 (11.8% → 37.8%).** Three push arms at 40M nodes over every one of
the **3,691** levels the shipped chain fails: union **1,087 (29.5%)**, **2,249 of 2,249 solutions through
the two-engine gate, zero divergences**, 67 h 30 m wall and 879 h of job time at 13.0x parallelism.
`build/reports/chain5.jsonl` is the union of the chain and the three arms and is what a fifth pass has to
be pointed at; `tools/l5_pass.sh` is the recipe, unchanged from the one the item shipped.

| arm | wall | solo | rate | exclusive — what dropping it costs | greedy |
|---|---:|---:|---:|---:|---|
| l8fire | 27 h 18 m | **805** | 21.8% | **173** | 805 (21.8%) |
| enables | 18 h 52 m | 718 | 19.5% | **101** | +191 → 996 (27.0%) |
| layer7 | 21 h 20 m | 726 | 19.7% | 91 | +91 → **1,087 (29.5%)** |

**The stride priced the pass. What it could not do is separate two arms that are close.** The rehearsal's
84 of 255 was written up here as ~1,172 union and ~1,670 composite (~40%); the corpus came back with
**1,087 and 1,581 (37.8%)** — **7.8% and 5.6% optimistic** against that published prediction, or 12%
against a naive 84/255 scaling, and the three solo rates landed **4.1 / 1.9 / 0.9 pp** high. **For sizing a
67-hour run off 255 levels that is a good instrument.**

**Where it failed is the ordering of arms 2 and 3, and it failed in both directions at once.** The stride's
greedy chain was `l8fire` **+66** → `layer7` **+14** → `enables` **+4** — the second arm three and a half
times the third, which is the reading that put `enables` last and nearly retired it. Over the corpus the
two are inside **1%** of each other: run in the stride's own order, `layer7` adds **+181** and `enables`
then adds **+101**; run greedily, `enables` adds **+191** and `layer7` then adds **+91**. The union is
1,087 either way and **the greedy pick between them is decided by ten levels out of 1,087.** Their
exclusive counts invert outright — **15 / 11 / 4** on the stride against **173 / 91 / 101** on the corpus.

**The decision that ordering would have licensed is the thing to measure, and it is the wrong one.**
*Drop the arm that adds +4* was a live reading of the rehearsal; dropping `enables` costs **101 levels**,
which is **more** than dropping `layer7` (91) — the arm the stride ranked above it. So the rule is narrower
than *compare unions*: **a stride sizes a union; it cannot order the arms inside it, and it must not be
used to drop one.** Exclusivity is the statistic a small sample destroys, because two arms that overlap on
15 sampled levels are free to diverge across the other fourteen-fifteenths, and these two did. **All three
arms are load-bearing**: only 440 of the 1,087 (40.5%) are solved by all three, and **365 (33.6%) are
solved by exactly one.**

**`Hard` falls for the first time.** The tier was 0 of 257 in the chain, and the rehearsal's 1-in-15
sample held **21 Hard-and-Deadly levels and solved none of them in any of seven arms**, which is what
licensed the sentence *none of this touches the two tiers that have never fallen*. Over the corpus it
is **5 of 257**:

| level | name | keys | ratio | arms |
|---|---|---:|---:|---|
| `Challenge-I` 306 | Robinson Crusoë | 91 | 1.30x | all three |
| `Challenge-I` 1346 | Fort Knox II | 55 | 1.31x | all three |
| `Challenge-I` 1101 | Mr. Ping-Pong VI | 51 | 1.38x | layer7 + enables |
| `Challenge-I` 1596 | It's a Snap | 82 | 1.95x | l8fire only |
| `LaserTank` 901 | fission | 66 | 1.32x | l8fire only |

**`Deadly` does not move** — 1 of 56, the level the chain already had. And the per-tier shape is the
chain's shape stretched, not a new one: Kids **39.0% → 81.8%**, Easy **5.2% → 33.3%**, Medium
**1.1% → 10.8%**, Hard **0 → 1.9%**.

**The ceiling is still budget, and it is not close.** Of the **2,604** levels nothing solved, **99.6%** of
`l8fire`'s misses stop on `budget`, and **5 levels** — five — are structural in all three arms at once. No
solved level reached the node cap (max 39.98M of 40M), so nothing was won on the last percent of the
budget either. 40M is a price, not a wall.

**`--max-keys 5000` was worth at least 11 levels**, which is the one thing in the run that was never
rehearsed. Eleven of the 1,087 have solutions longer than the old 1,200-key default — ten in `Special-I`,
one in `LaserTank` — longest `Special-I` 311 *even longerer* at **4,681 keys / 1.12x**. Nothing came within
300 keys of the new cap, so the raise is free and is not binding either. This is
[closed item 12](#12-per-level-maxkeys-from-the-record----max-keys-record)'s flag earning its keep on a
population for the first time.

**What the 1,087 look like against the records:** ratio p25 **1.36** / p50 **1.56** / p90 **2.63**;
**249 of them (22.9%) match the `.ghs` record's moves and shots exactly**, and **8 come in under it**, the
furthest `Challenge-I` 1941 *Invitation to temptation* at **0.328** — a third of the recorded line. A
record in `.ghs` is somebody's score, not a proof of optimality, and this is the first population large
enough to say how often it is beatable: about **0.7%** of the time.

**The economics, because the third arm is the argument.** Marginal cost per new level, in greedy order:
`l8fire` **122 s**, `enables` **356 s**, `layer7` **844 s**. The last arm is 21 h 20 m of the 67 h 30 m for
**91 levels, 8.4% of the union** — that is what the union rule costs when you actually pay it, and it is
still the right call, because those 91 levels have no other route on this tree.

**One follow-up the pass turned up and nobody asked for.** `enables` is the only arm that stops on
`push-depth` at scale — **177 levels**, against `l8fire`'s 6 and `layer7`'s 3 — and those stops leave a
median **10M of the 40M nodes unspent** (p50 30.1M used). `--push-depth` is documented as *a backstop
only*, 1,200 board changes; on this arm, at this budget, it is not a backstop. Raising it costs nothing to
try and is the cheapest unclaimed thing in these files.

**The instrument the item built was worth more than the numbers say.** The pass ran from 2026-09-10 11:28
to 2026-09-15 20:29 — **67 h 30 m of wall clock across seven restarts**, the longest of them a
two-and-a-half-day stop (session 46's). Every arm still finished at 3,691 of 3,691,
because `RESUME=1` drops the levels an arm's own report has already attempted. A 67-hour run that cannot
be interrupted is a run that never finishes on a machine somebody also uses.

**What it did not touch:** `LaserTank.lvl` 6 *Cascade* is in the 4,185-level sample and all three arms fail it
(`enables` on `push-depth`, the other two on `budget`), and level 10 is not in the sample at all. The two
open hand-supervised levels are exactly as
[closed item 5](#5-level-6s-decomposition--built-and-refused-by-the-falsifier-it-set-itself) and
[closed item 8](#8-level-10-one-traced-run--and-it-answered-a-question-the-item-did-not-ask) left them.

### 3. Refresh the banked solutions — a no-op, because the premise expired.

The item read: *every banked `.lpb` is longer than it needs to be — they all predate the replan pass.*
Run over all 494:

```
494 winning recordings, 0 shortened, 14,518 keys -> 14,518 (0.0% removed)
```

**Zero, on every one of the 26 collection directories.** `--polish` and `--replan` are both **ON by
default during a solve**, so the rebuild banked these already polished and already replanned. The 11,060
→ 10,249 the item quotes was measured on solutions banked *before* that default and there is now no path
in the tree that reproduces it.

Two things worth keeping out of a run that removed nothing. **The baseline is measurable without the
solver:** an `.lpb` is `TRECORDREC` — `name[31], author[31], u16 level, u16 size` — so keypresses are the
`u16` at offset 64, and the corpus total is a six-line script rather than a 26-command sweep (`l0` is 398
files / 9,436 keys, `l34` is 96 / 5,082). And **398 + 96 = 494**, the composite arrived at from the
directory rather than from a report — worth one line because two sessions each paid for reading
`data/solutions/` as a status board, while reading `build/solutions/` as an *inventory* does hold.

*(What this does not retire is the item's reasoning. Shorter trajectories really do change the ascent
statistics layer 5 rests on and really are what layer 4 is fit on — the point is that the solver already
emits them that way, so the debt was paid at the source rather than owed.)*

### 4. The campaign that decided that `--best-of-round` is a default — and the shot rule won it.

> **RUN AND DECIDED, 2026-09-14. The shot rule is the default.** All three stages are in — A, B and the
> acceptance bars — and `--best-of-shots` won both populations. The driver now ships with the round rule
> **on** and the shot test as the rule; `--no-best-of-round` is the opt-out and a bare `--best-of-round`
> is the ratio rule on its own, which is how the campaign's arms are told apart. Every report row's
> `config` ends in `[round-rule shots 3|ratio 2|off …]`, from the default as well as from a flag, because
> a default has no token in argv and two rules would otherwise produce indistinguishable rows.
>
> | stage | arm | fired | keys saved where it fired | net | nodes | wall | shots over the record |
> |---|---|---:|---:|---:|---:|---:|---:|
> | **A** (494) | `bor` 2.0 | 14 (2.8%) | 53 | 35 | 1.05x | 0.99x | 28 → 24 |
> | **A** | `shots` 3.0 | **28** (5.7%) | **95** | 95 | 1.15x | 1.06x | 28 → **21** |
> | **B** (90 of 121) | `bor` | 27 (30.0%) | 65 | 49 | 1.16x | 1.31x | 31 → 32 |
> | **B** | `shots` | **32** (35.6%) | **71** | 57 | 1.22x | 1.34x | 31 → 31 |
>
> **What makes it safe as a default rather than merely profitable: neither rule came back longer on a
> single level where it fired — 0 of 60 across both stages.** Every regression in either table is on a
> level the rule was never consulted on, which is the instrument's own noise floor (below), and the
> signal is larger than the floor in both stages. The price is real and is the thing to watch: **1.34x
> the wall on the deep population.**
>
> **Nothing is left** — the three threads the first run left open are closed in
> [*What is left*](#what-was-left-after-the-decision) at the end of the item, two of them instrument
> defects worth not rebuilding. **The item is ready to move to
> [`history.md`](history.md#closed-items--the-measurements-including-the-negative-ones).**

The mechanism, the transcript and the 115-keys-against-294 result are in
[`driver.md`](driver.md#not-settling-for-the-first-win----best-of-round-and---beat-banked), along with
the acceptance stage's two results.

`bash tools/bor_campaign.sh` is the campaign; `bash tools/bor_campaign.sh report` prints
its table again for free from the banked reports. The measurement is a campaign with `--best-of-round`
against one without it, read as *keys* rather than as solved count — the solved set should be identical
and the routes shorter, and how much shorter is what decides whether this becomes a default. **Every ratio
quoted in these files was measured under first-win-cancels, so that campaign rebases them.**

**And it inherits closed item 13's second half, which is a rule rather than a number.** The flag judges a
win by `keys / record ≤ 2.0`; the shot test says a win that spends *more shots* than the record is a
different and worse route, and the two disagree on **58 of the 452 rows** — 41 wins the ratio test closes
the round on although their shot count says the strategy is wrong, and 17 it keeps open although the
shot count says the strategy is already right, so those rounds can only buy polish. The change to make
with the campaign: *keep the round open when `shots > ghs_shots` whatever the ratio; close it when
`shots == ghs_shots` and the ratio is inside a looser bound*, and **a level with no record keeps the
round open, as it does today.** The measurement is already run and the column is in `report_stats.py` —
what is not decided is whether the rule pays for the rounds it keeps open, which is this campaign's
question and not a separate item's. **It is now a flag, `--best-of-shots [R]`**, and it is the campaign's
third arm: more shots than the record keeps the round open whatever the ratio, otherwise the ratio decides
against a looser `R`, default **3.0** — looser because the shot test has already said the plan is right,
and the rows whose shots match the record are p90 1.79x, so 3.0 closes nearly all of them.

Note what level 9 says about its price: **44m46s for one level**, against the 16m36s of the hand-run it
beat, because a round nobody cancels is a round every rung spends in full.

#### Stage A is run, and the shot rule wins it

**494 levels, three arms, 2026-09-13.** Every level fell in round 0, so this is the flag's behaviour on
the population a default would apply to on nearly every run.

| arm | the rule fired on | shorter | longer | **keys saved where it fired** | nodes | shots over the record |
|---|---:|---:|---:|---:|---:|---:|
| `bor` — ratio 2.0 | 14 of 494 (2.8%) | 7 | **0** | **53** | 1.05x | 28 → 24 |
| `shots` — the shot test at 3.0 | **28** of 494 (5.7%) | 9 | **0** | **95** | 1.15x | 28 → **21** |

**Closed item 13's rule is the better one and it is not close.** It fires on twice as many levels, saves
**1.8x the keys** for 2.3x the extra nodes, and it moves the population it was designed to move — the
levels solved with the wrong strategy — from 28 to 21 where the ratio rule manages 24. Its worst case is
`Sokoban-II` 46, **40 keys → 23 at 2.0x → 1.1x**, a round the ratio rule closes on the nose.

**The finding that decides how every number here is read, and it came out of a contradiction.** Nine
stage-A levels came back **longer** under `--best-of-round`, which the mechanism forbids: a round kept
open sees every route the cancelled round saw and banks the shortest. Split the levels by whether the
rule could fire at all and the contradiction dissolves — **all nine are levels where the flag was never
consulted**, and on those the arm and the control are *the same configuration run twice*. Where either
rule actually fired, **neither arm came back longer on a single level, 0 of 14 and 0 of 28.**

So **a driver round is not node-governed even though every rung in it is.** The stop bit is polled every
120 ms, and which rungs have crossed the line when a round cancels is a race with the thread scheduler.
The noise floor that puts on stage A is **5-9 levels and ~20 keys a run** — which is larger than the
ratio rule's whole 35-key headline, and is why `round_rules.py` leads with the levels the rule fired on
and prints the rest as the floor. `bash tools/bor_campaign.sh floor a` measures it directly by running
the control a second time; the free version is same-population and in every table already.

#### Stage B agrees with stage A, on a population that is nothing like it

**121 levels, 90 of which the control solved, 2026-09-14.** This is the deep end — a stride over the
short-record failures, to round 3 — and the control spends **5.1G nodes and 2h03m** there, **3.4G of it
on the 31 levels nobody solves**, which is the same bill in every arm and is why the arms run only over
the control's solved set.

The rules fire an order of magnitude more often here than on stage A — **30% and 36% of levels against
2.8% and 5.7%** — which is the population difference doing exactly what the two-stage split was built to
show, and the shot rule stays ahead: **71 keys against 65 where it fired, 57 net against 49.** What it
costs is where the two stages differ, and it is the number that decides how the default reads: on stage A
the round rule is nearly free (1.05x / 1.15x nodes, wall inside the noise), on stage B it is **1.22x the
nodes and 1.34x the wall**. A rule that fires on a third of a collection costs a third of a collection,
which is what the item said to watch for, and it does.

Both arms' biggest wins are the same two levels, and they are worth naming because they are the case the
flag exists for rather than polish: `Sokoban-II` 76 *Day B* **106 → 70 keys**, `push-stop` → `push-ferry`,
2.2x → 1.4x; and `Challenge-I` 1576 *Waterbound* **121 → 93**, `learned` → `push-ferry`, 3.4x → 2.6x. In
both the rung that wins is a different rung, not the same rung polishing — the round stayed open and a
slower specialist arrived with a better plan.

#### What was left after the decision

**Nothing.** All three of the threads the first run left open were closed the same day; they are kept
here because two of them are the instrument, and an instrument defect that is deleted gets rebuilt.

1. **The lost control row — fixed, and the stage re-run.** Stage `acc`'s level-8 control arm had been
   **given up on by a keypress after 0s** — `stop: skipped`, 30,769 nodes — because the driver's
   any-key-gives-up-on-the-level path was armed during an unattended run and one stray byte reached its
   stdin. The campaign now runs every driver invocation with `< /dev/null`, which is what the banner's
   "stdin is not a console, so there is no key to press here" line reports, and `round_rules.py` prints an
   **INSTRUMENT WARNING** naming any level whose row says `skipped` or `stopped`, so a lost arm can never
   again read as an unsolved level. `MAXACC=5 bash tools/bor_campaign.sh acc` re-ran the stage clean, and
   **the control's answer is the same 305 keys the three arms got**, at 380M nodes against their 418M and
   431M — so the round rules buy nothing on level 8 and cost 1.10x and 1.13x to buy it, which is what the
   item predicted from the shot counts and is the prediction landing rather than a new result.
   **All four arms' `00008.lpb` are byte-identical** (`d371e931a857`), which is worth more than any of
   them alone: the 305 is one route the driver derived four independent times, where the 308 is a file.
2. **The candidate recipe does not reproduce the 308 — closed, negative.** The falsifier was re-run
   without the three flags the script had added that are not in the surviving banner (`--no-ida
   --max-keys 5000 --max-keys-record`), leaving exactly what the banner says: 1 worker, 60M nodes,
   5,400,000 ms, beam 600, `--push --push-read --push-beam 2048 --push-eval work`. **Unsolved at budget
   in 3m37s**, against 3m47s with the three flags — so they were never the difference.
   **And the node rate is what actually settles it.** 60M nodes in 3m37s is ~276k/s; at that rate the
   original's 5m51s would be ~97M nodes, which is more than the 60M its own banner caps it at. So the run
   that produced the 308 was searching *slower per node* than this one — a heavier configuration — and
   **the banner does not describe it.** That is the honest end of the question: not "we mis-ran it" but
   **what was recorded was never enough to re-run**, which is the exact hole the `config` and `rung`
   columns were added to close. It costs nothing now, because the level has a better route with a command
   behind it.
3. **The gate passed on an empty directory and said so in green — fixed.** `verify_solutions.py` pointed
   at the falsifier's output printed `0/0 solutions verified / every solution wins` and exited 0, so the
   campaign's `|| say "GATE FAILED"` never fired. It now returns 1 on `total == 0` — *nothing checked is
   not a pass* was already the rule one branch above, for a collection whose `.lvl` could not be found;
   it simply did not cover finding no solutions at all.

Two smaller things fixed in passing, both of which made a table lie rather than a run go wrong:
`bash tools/bor_campaign.sh floor a` had been advertised in this file and in the script's own header
since session 47 and **the function was never written** — the dispatch hit `floor: command not found`,
then ran the gate and printed the table, so it looked like it had worked. And the acceptance table
rendered an unsolved level as `beat   rounds after 6 rounds`, printing the driver's `stop` enum where the
driver's own line says `unsolved in 6 rounds`.

**Stage A and stage B need no re-run**, and neither does stage `acc`'s `bor`, `shots` or `beat` arm. Note
that a *future* control arm must now pass `--no-best-of-round` — the script does — or it is a second
`shots` arm wearing the control's name; `round_rules.py` reads the control's own rows and warns if it is.

#### The run

```bash
bash tools/bor_campaign.sh report          # START HERE -- every stage below is already run
bash tools/bor_campaign.sh                 # everything again, three arms each, ~6-7 h at JOBS=4
bash tools/bor_campaign.sh acc             # the acceptance bars, ~2 h
bash tools/bor_campaign.sh b               # the deep stage, ~5 h
bash tools/bor_campaign.sh floor a         # the null arm, ~10 min -- written in session 49, never run
bash tools/bor_campaign.sh report          # the tables again, free, from the banked reports
tail -f build/reports/item4-run.log        # from any other shell
```

Priced from a 13-level rehearsal at `JOBS=4` beside item 2's pass rather than guessed: **the ten rungs get
through about 1.15M nodes a second between them**, so a level nobody solves costs 31M nodes / ~27 s to
round 2 and 127M / ~110 s to round 3, and a round held open costs the whole of its own budget — 96M at
round 3.

Safe beside item 2's pass — `JOBS=4` against its 16 on 20 cores, node-governed round by round — with the
one caveat this item has that item 19 did not: **wall clock is one of the numbers it wants**, so the
report prints nodes beside every second it quotes and the seconds are the contended ones. **While the pass
holds `build/lasertank-solve.exe` open the new driver can only be built into the project's own `bin/`**
(`dotnet build src/LaserTank.Solver/LaserTank.Solver.csproj -c Release`); the script finds that build
itself and says so, rather than running a third arm that silently repeats the second.

**Three arms, and the third is the new flag.** `ctrl` is the driver as it ships, `bor` is
`--best-of-round` at its 2.0, `shots` is `--best-of-shots` at 3.0.

**The arms after the control run only over the levels the control solved, and that is not a shortcut —
it is what the flags are.** Neither is consulted until a rung has already won, and neither adds budget,
so **a level the control could not solve costs all three arms exactly the same and has no keys to
compare**. `tools/round_rules.py` checks the assumption instead of trusting it: a level an arm solves that
the control did not is printed as an instrument warning, not as a win.

**Two stages, because the flag's cost and its benefit live in different populations.**

| stage | population | rounds | what it answers |
|---|---|---|---|
| **A** | the **494** levels the shipped chain solves at 150k | to round 2 (150k / 600k / 2.4M per rung) | what a *default* costs and buys on the levels nearly every run touches |
| **B** | a stride (`SAMPLE=6`, ~115) over `bench/short-record-failures.txt` | to round 3 (adds 9.6M) | what it buys where the ladder's rungs disagree — **level 9 is a stage-B level**, and it is the whole argument for the flag |

`NODES MAXA MAXB SAMPLE JOBS` are the knobs and the numbers are only comparable at the defaults. Both
stages resume from their own reports: Ctrl-C, reboot, the same command picks up where it stopped.

**What the table has to say for the item to close**, and `round_rules.py` prints all four: keys saved and
the percentage, the ratio column rebased (p50 and how many rows are still over 2.0x), the **shots** column
before and after — a round that comes back with *fewer shots* found a different plan rather than a tidier
keystream, which is the half the ratio cannot see — and the price in nodes and wall, both over the whole
population and over the levels the rule actually fired on. A rule that fires on a third of a collection
and buys nothing on most of them is a rule that costs a third of a collection.

*Two things not to re-derive.* What was tried first and is not the answer: making the gate refuse the
longer write — it fixes the file and hides the run. And the recipe this item replaces: for six sessions
these files said *"this is the command to use for level 9, not the driver"* — a hand-run of
`push-ferry-work` at `--push-beam 2048 --push-restarts 30`, 16m36s, 127 keys / 2.2x — because an
unattended driver run found the beam's 294-key route instead and cancelled the good one 68.5M nodes early.
`--best-of-round` is exactly the removal of that cancel, and with it the driver beats the hand-run by
twelve keys.

**The lost result this item was really for is not lost — session 48 recovered it**, from the *port*
machine's `build/`, which never took part in the machine move that emptied the solver's. Both files are
committed at `bench/recovered/LaserTank/` and **both pass the gate** (`python
tools/verify_solutions.py bench/recovered` → 2/2, both engines, byte-identical traces):

| lvl | the recovered file | keys | ratio | banked when this was written | banked now |
|---:|---|---:|---:|---|---|
| 8 | `bench/recovered/LaserTank/00008.lpb` | **308 (262 + 46)** | **1.4x** | 335 / 1.5x | **305 / 1.37x** |
| 9 | `bench/recovered/LaserTank/00009.lpb` | 114 (81 + 33) | 1.9x | **115 / 1.9x** | 115 / 1.9x |

**The last column is how the item ended.** Stage `acc` re-derived level 8 at **305 keys** from an empty
directory — three keys under the recovered file and thirty under the 335 — in all four arms
byte-identically, and a driver run then banked it. Neither recovered file was copied anywhere: the
reasoning below about re-banking being a decision held, and what settled it was the ladder producing a
better route on its own.

Level 9's 114 stays retired: the acceptance run comes back at 115, one key longer, at the same ratio,
from the driver with no flags aimed at the level. **Level 8's 308 is the one that matters** — 27 keys
and a tenth of a ratio point better than what is banked, never re-derived, and now a *file* rather than
a memory. Neither is banked into `data/solutions/`, deliberately: session 42's move of level 9 was
"re-derived rather than restored", and a shorter route with no recipe behind it is exactly what that
preference is about. **The recovery changes what can be asked, not what is banked.**

**What the recovery makes actionable, cheapest first.** All three were taken up in session 48; **1 and 3
came back changed and 2 is now a scripted stage.** **All three then ran on 2026-09-14** — what they
returned is in [*What is left*](#what-was-left-after-the-decision) and in
[`driver.md`](driver.md#not-settling-for-the-first-win----best-of-round-and---beat-banked); the design
reasoning below is kept because it is what the stage was built from and two of its predictions were
wrong in instructive ways. **The one that was right and matters most: `--beat-banked`'s refusal path
fired for the first time outside unit tests, in both directions, and behaved.**

**Stage `acc` is priced from the driver's own round budgets and it is not a coffee break.** Round *r*
hands each of ten rungs `4^r x --nodes`, so at the default 150k a rung sees 9.6M at round 3 and **153.6M
at round 5** — and the recovered level-8 run spent **60M in a single searcher**. So `MAXACC=5` is the
only setting at which the stage is an acceptance test at all, and driver.md prices that at **44m46s for
one level, one arm**. `MAXACC=3` makes it a ~20-minute smoke that cannot reach the 308 and will report
"refused every round" having measured nothing. **Measured, not assumed: at round 2 (6.4M a rung) neither
level 8 nor level 9 falls at all**, in any arm. The stage therefore runs **cheapest-and-most-decisive
first**, the way `l5_pass.sh` orders its arms: the six-minute recipe falsifier, then the round arms, then
the `--beat-banked` arm that needs round 5.

1. **Level 8's 308 is an acceptance bar for `--beat-banked`, not for `--best-of-round` — and it is not
   free.** Two corrections, both measured:
   * **Neither round rule fires on level 8.** Read the shot counts off the two files and the recovered
     308 and the banked 335 spend **exactly the record's 46 shots**, at 1.39x and 1.51x. So the ratio
     rule closes the round (under 2.0) and the shot rule closes it too — a route matching the record's
     shot count is, by that rule's own definition, the right plan. **Level 8's 27 keys are execution
     slack, not a different strategy**, which is closed item 13's distinction doing exactly what it was
     built for, and it means running level 8 under the round rules measures nothing.
   * **The campaign does not pass through it.** `chain.jsonl` is a `STRIDE=5` sample — levels 1, 6, 11,
     16 — so `LaserTank` 8 and 9 are in neither stage A nor stage B, and stage B's list is derived from
     chain failures so they are not there either.

   What the 308 *does* test is the other flag. **`--beat-banked` refuses a round that comes back longer
   than the `.lpb` on disk and holds every later round open chasing it** — and
   [`driver.md`](driver.md#not-settling-for-the-first-win----best-of-round-and---beat-banked) says that
   refusal path "is unit tests and nothing more", because in the level-9 acceptance run it never had to
   fire. Seed a scratch output directory with the recovered 308 and the driver must beat it or report
   `unsolved in N rounds`. **That is the first real exercise of a shipped default**, and it is what
   stage `acc` does. `data/solutions/` is never written, so re-banking the 308 stays Michal's call as
   `bench/recovered/README.md` asks.

   **Level 9 is the better acceptance level and it is the opposite case**: both its routes spend 33
   shots against the record's 22, at 1.93x and 1.95x — *inside* the ratio rule's 2.0 and *outside* the
   shot rule's test. It is the two rules disagreeing in one level, which is why stage `acc` runs it
   under all three arms.
2. **The candidate recipe is stage `acc`'s last step.** The run logs came back with the solutions and
   they carry the budget, which is more than the directory names item 4 was written around:
   `w8-2048.log` is *1 worker, 5,400,000 ms + 60,000,000 nodes, beam 600*, **solved in 5m51s**, and the
   directory name says width 2048. The banner is the *batch* harness's, so the scripted falsifier is a
   batch run rather than a driver one: `--level 8 --jobs 1 --nodes 60000000 --push --push-read
   --push-beam 2048 --push-eval work`. If 308 comes back, "not reproducible as a recipe" closes and the
   level-8 route has a command; if it does not, that is the more interesting answer, because it says the
   308 came from a flag set nobody wrote down and the banked 335 is the best *reproducible* route.
3. **Done — and it paid the same session it shipped.** Every report row now carries **`config`**, the
   run's own flags with the paths elided (`--nodes 60000000 --push-beam 2048 --levels <path>`), and the
   driver's rows also carry **`rung`**, which is what the winning rung's `Tune` set on top —
   `RunPush=True PushRead=True PushBeamWidth=48 PushRestarts=18`. **The second field is the one that
   closes the actual hole**: `--push-beam 2048` never appears in a driver's argv at all, it comes from
   `Auto.Ladder`, so the run that produced level 8's 308 could not have recorded it even if the row had
   carried the command line. It is read off the options object by reflection rather than written out
   beside each rung, so a rung that gains a knob says so without anybody remembering to update a string.
   The driver's banner prints the flags too. **It paid within the hour**: stage `acc` runs its three
   round arms into one report and tells them apart by reading `--best-of-shots` out of their own
   `config` — a thing no report in this tree could do the day before.

One loose end closed while checking, worth having because it re-attributes a banked file:
**`build/w/d8-w512w/…/00008.lpb` was byte-identical to the banked `data/solutions/LaserTank/00008.lpb`**,
so the banked level-8 route came from that run — 400M nodes, beam 600, width 512 — and not from an
unknown one. The 335 and the 308 are the same searcher at two widths.

### 5. Level 6's decomposition — built, and refused by the falsifier it set itself.

**The item asked for layer 2's decomposition one level out: commit to a phase, search only for that,
re-derive.** It is built — `--push-phases`, `src/LaserTank.Solver/Phase.cs` — with the two instruments
that size it and falsify it, and the answer is **no on `LaserTank.lvl` 6 and the reason is not the one
the item was written against**. The line is short enough. The commitment is right. The search still
cannot walk the middle of it, *from the human's own boards*.

```bash
python tools/phases.py                 # where a hand line's phases are, no search at all
python tools/phase_reach.py 6          # can the beam walk one?  the falsifier
python tools/phase_reach.py 6 status   # the table, free, from the report
```

**What a phase is, and it needed no constant.** Item 18 refuted a global phase *size* (horizons run 2 to
50, a 25x spread) and item 14 carries the negative that nothing free predicts one — so a phase here is
not sized, it is **terminated**. Every board change is one of two kinds and the distinction is a census:
a push *moves* an object and the count of it is unchanged; a fill, a shot brick, a destroyed mirror or
anti-tank *consumes* one and the count drops. A **milestone** is a successor whose board holds strictly
fewer consumable objects than the phase's root — one integer, a strict comparison, the shape
`--push-fire-tier`'s test has. `tools/phases.py` applies it to the 20 hand recordings for free, off
`--push-line`'s replay:

| level | changes | phases | segments | longest | horizon (item 18) | longest / horizon |
|---:|---:|---:|---|---:|---:|---:|
| **6** | **168** | **6** | **18, 26, 28, 30, 32, 34** | **34** | **50** | **0.68** |
| 1 | 50 | 16 | 3,1,7,1,2,2,1,… | 10 | 38 | 0.26 |
| 8 | 52 | 36 | mostly 1 | 9 | 52+ | 0.17 |
| 28 | 105 | 82 | mostly 1 | 7 | 27 | 0.26 |
| 23 | 27 | 6 | 1,3,2,1,5,5 | 10 | 21 | 0.48 |
| 18 | 53 | 1 | 44 | 44 | 28 | 1.57 |

**The sizing question came back positive and that is why this was built.** Level 6's 168-change line is
**six phases, one per hole filled, no tail, the longest 0.68 of the horizon already measured on that
level**. The rule cuts 17 of the 20 recordings and 12 of the 17 have every phase inside their own
horizon. The three it is empty on are 2, 10 and 11 — and `LaserTank.lvl` 10 being one of them is not a
defect but Layer 9's finding from a third side: a GAUNTLET consumes nothing on its way to the flag, so
it has no milestone, exactly as its barrier set is empty by construction. **This item was always for the
half of the corpus that is a ferry.**

**A phase-chained win is a real recording, and that is checked rather than argued.** The commitment is
a live `EngineSnapshot`, which carries its own key prefix (layer 0's second bug and the fix that made a
breadth-first search's answers replayable), so a win in phase *n* reports the whole keystream from the
level start. `LaserTank.lvl` 7 solved after a commit at 66 keys and went through
`tools/verify_solutions.py` — **both engines agree on every tick**. Nothing here is hint-assisted: the
boards committed to are ones the search found.

**The chain commits to the right board.** At width 32 from the root, phase 1 commits at 715,840 nodes
with `census 12 -> 10` — a block and a hole gone together, i.e. a fill — and `--push-trace` prints the
board: the water at **(9,14)**, which is *exactly* the cell the hand line fills first at its change 18.
Six blocks and six holes make level 6 a census of 12, so a solve is six phases to 0.

**And then phase 2 never lands, at any width tried.** `tools/phase_reach.py` is the falsifier and it is a
different question from item 18's: `horizon.py` seeds at K and asks for a **win**, so every board it
measures is near the end of the line where a Sokoban has the fewest blocks and holes left; this seeds at
K and asks only for the **next milestone**, which is the middle of the line and the only place a
decomposition lives. Level 6, width 32, 8M nodes a phase:

| phase | seed K | target | length | w=32 | w=128 | w=512 |
|---:|---:|---:|---:|---|---|---|
| 1 | 0 | 18 | 18 | **YES** 715,840 | YES 2,452,200 | no |
| 2 | 18 | 44 | 26 | **no** | no | no |
| 3 | 44 | 72 | 28 | **no** | no | no |
| 4 | 72 | 102 | 30 | YES 5,982,193 | no | no |
| 5 | 102 | 134 | 32 | YES 3,427,451 *(solved)* | no | no |
| 6 | 134 | 168 | 34 | YES 4,360,647 *(solved)* | YES 6,251,230 *(solved)* | YES 6,251,488 *(solved)* |
| | | | **reached** | **4 of 6** | 2 of 6 | 1 of 6 |

**Four of six at the best width, and the two that fail are the middle.** Phase 2 is 26 board changes —
*shorter* than phases 4, 5 and 6, which all land. So it is not length, and the table refutes the item's
own premise directly: **the phases this level needs are not uniformly hard, and the hard ones are not
the long ones.** Width is not the answer either: at a fixed 8M nodes a phase, **32 reaches 4, 128
reaches 2 and 512 reaches 1** — narrow-and-deep for the fourth time in this project, and phases 2 and 3
fail at all three.

**The reading that closes the item: the commitment is not what fails.** Seed at K = 18, i.e. hand the
chain the *human's own board* after the human's own first fill, and it still does not reach fill 2 (4M
nodes, 37 depths, width 32). So the solver's committed board is not a worse board than the human's; the
phase itself is out of reach. **A decomposition cannot rescue a level whose phases the search cannot
walk**, and level 6 is that level. What defeats it is therefore not the length of its line, which is
what this item was written to attack.

**A finding that falls out of the same table and is worth more than the item was.** Phases 5 and 6
*solved level 6* from their seeds, and **K = 102 is 66 board changes from the end** against item 18's
measured horizon of **50** — reached at width 32 on **3.4M** nodes where item 18 measured its 50 at width
512 on 40M. So the narrow arm goes **deeper on a twelfth of the budget**, and **the horizon is
width-dependent**: 50 is level 6's reach at width 512, not the searcher's best. Item 18's number stands
exactly as measured and its 25x spread is untouched — a horizon is only comparable at one budget and one
width, which is what that file says in its own header. What it does not support is "50 is the reach",
and item 14 inherits the consequence: **the per-level lever it is looking for has a second dimension it
had not measured.**

**Three commit laws, and the two that were refused are the design.** A phase has to end somewhere and
that turned out to be the whole of the engineering:

1. *Let the beam stop on its own.* **Inert** — on every level this item is for the beam stops on
   *budget*, so the first phase spent the whole run and nothing was ever committed.
2. *End the phase at the depth that produced a milestone.* Needs no constant, which is why it was tried,
   and it **cost a level the plain beam solves**: `LaserTank.lvl` 20 went from a win at 2.75M nodes to
   twelve committed phases and `push-dead-end` at 6M. `tools/phases.py` had already shown why from the
   other side — a demolition level consumes something on nearly every change (level 3's census starts at
   **77**; level 28's line is 82 milestones in 105 changes), so committing at the first milestone turns
   the beam into a greedy one-step hill-climb with no backtracking. **Layer 1's structural finding
   restated: a specialist that bets on every level loses in a portfolio.**
3. *Commit where the search would otherwise give up* — which is what ships. PushSearch already returns
   on a dead-end that has exhausted the restart ladder, so the chain only ever spends a commitment on a
   frontier that was finished anyway and a descending beam is never interrupted.

Under law 3 the flag is **inert where it does not apply**, which is the property that makes it safe,
and the control is stronger than "same outcome": over `LaserTank.lvl` 3, 4, 7, 11 and 20 at 6M nodes the
two arms are **node-identical and keystream-identical** — 20 back to its win at 2,751,227 nodes and 108
keys, the same figures the flag off produces. It is also inert on level 6, whose beam neither wins nor
dies but wanders to budget — `--push-phase-nodes N` is the opt-in for that shape and is off by default
because it is a global constant of exactly the kind item 18 refuted.

**The GAUNTLET-tail population this item defined outlives it, and Layer 9 and item 14 both read it.**
`bench/gauntlet-tail.txt` is the 138 unsolved GAUNTLETs whose `.ghs` record is <= 60 moves+shots — the
tail of the 537 the sizing argument named, at the only record length a 40M-node budget reaches. It is
regenerated from the two banked reports, so it needs no search:

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
`build/` has been cleared. **Two things that run said which the sizing argument did not predict**: the
tail is not as hard as its median record suggests (the control arm alone solves 76 of 138 at 40M,
against the 23.9% the best fourth-pass arm scores on the general failure population), and the fire tier
is worth +9 solo and +15 exclusive on top of it —
[Layer 9](layers.md#layer-9--exposure-as-a-tier---own-rung-the-population-pays-the-example-does-not).

**What ships and what it is worth.** `--push-phases` and `--push-phase-nodes`, off by default; two
instruments that cost no search worth speaking of; and a negative on the level the item existed for. It
is **not** proposed as an arm — nothing here measured it on a population, and rule 1 forbids reading a
one-level result as one. The residue is a redirection rather than a to-do: phase 2's trace is `best=137`
flat for 237 depths at width 32, which is the *same signature* as level 10's GAUNTLET — a beam ranking
distinct boards by a key that has stopped discriminating. **What is binding in the middle of a Sokoban is
the ranking, not the depth**, and that is layers 4 and 6's territory, not a decomposition's.

---

---
### 6. The blogspot goal-board harvester, and the goal board as a ranking key.

**Closed in session 36.** Three sessions: 34 ran the feasibility spike and shipped
`tools/harvest.py` + `tools/png.py`; 35 added `tools/sprites.py` and deleted the whole labelling phase by
deriving it; 36 built `--goal-board` and measured it. The framing is below, then every number each
session produced, then what the key is worth.

**The one-line result.** On the 141 levels the fetched bank covers, at 4M nodes on the push rung,
`--goal-weight 0` solves **15** and `--goal-weight 1` solves **21** — +9, -3, union 24, all 21 through
the two-engine gate. Every one of them is *hint-assisted* and none of them is part of the solver's rate.

Michal raised it; what one post actually contains was verified rather than assumed (`Challenge-II-100`):
a **start screenshot**, one **screenshot per flag showing the board at the moment of reaching it**, and —
on some posts — the game's own **Moves and Shots counters visible in the panel**. No move list, no
keystream, no prose.

**What it buys is not solutions, it is a goal.** A final board says which blocks were moved where and
which bricks were destroyed, so "reach the flag" becomes "reach *this* board" — a progress measure that
decreases with every push in the right direction, which is exactly the gradient `RouteFerry` and its four
successors are hand-rolled approximations of. *(That last clause is the item's own framing and it is
right about the goal and wrong about the measure: a **count** of cells still differing does not decrease
with a push at all. See [session 36](#session-36--the-key-the-item-asked-for-is-not-the-key-that-works).)*

**The honesty condition, and it is not optional.** A level solved with a scraped goal board is
*hint-assisted* and must never enter the solver's headline rate. Its value is as a bootstrap:
hint-assisted solutions are real recordings, and real recordings are what `--profile` / `basin.py`
measure and what layer 4 is fit on — the off-distribution long-level sample that layer 4's
self-reinforcement trap needs, obtained without anyone playing twenty levels by hand.

It is no longer a prerequisite for measuring anything: the ferry population was n=2 when this was agreed,
and it is n=20 hand-recorded now. The harvester is a way to make that 200.

#### Session 34 — the spike, and every number it produced

Run in this order, each step cheap enough that the next one was only reached because the previous one
came back clean. `python tools/harvest.py {index,map,fetch,codebook,decode}`.

**1. The index is one minute and needs no HTML at all.** The Blogger feed
(`/feeds/posts/default?alt=json`) serves the *post body*, so the image URLs come with the titles: **6,218
posts in 42 requests, 46 s.** That retires "a scraping project" as this item's phase 1. The count also
corrects the estimate above: 6,218, not 2,000-3,000.

**2. The mapping is exact at scale.** Session 33 checked 6 posts and got 6/6; over all of them it is
**6,188 of 6,197 solution posts exact** on `(collection, level) → name` (99.85%), plus 21 posts that are
not solutions at all (celebrations, tutorials). `harvest.py map` prints the nine residuals and every one
is a **blog-side title typo, not a bad index** — a difficulty appended to the name (`Loopy, Easy`), a
`Shake` for `Shaker`, one `KaserTank` for `LaserTank`, one level name the `.lvl` itself stores with
control bytes in it. The number indexes the `.lvl` directly and the name is a free checksum on top.

**Coverage: 6,043 distinct `(collection, level)` pairs, 28.9% of the 20,914-level corpus** — and it is
concentrated exactly where the chain fails. Against `build/reports/chain.jsonl`, **1,148 of the 3,691
levels the shipped chain fails have a post (31.1%)**, and within the collections the blog actually works
on the rate is 81-90%: `Sokoban-I` 358 of 397, `LaserTank` 315 of 362, `Challenge-I` 284 of 349,
`Special-I` 61 of 84. Per tier: Easy 716 of 2,008, **Medium 349 of 775, Hard 68 of 257** — the tier the
chain is 0-for-257 on. Nothing at all for `Beginner-I/II`, `Gary-I/II` and `Challenge-III`, which is why
Kids is 0 of 586.

**3. One thing session 33 over-generalised.** The `Coll_NNN_G4.png` cell-name convention — the one that
makes a multi-flag subgoal sequence free — is **later-era only**. The 2016-era posts, which are exactly
`LaserTank.lvl` 1-19, name their images `10a.png` / `10b.png` and carry **no cell names**. Both eras are
keyed by the level number and `pick_images` handles both; the point is that *level 10 is in the era
without the free text*, so for the one open level this item can reach at all, the pixel decode was the
whole question. A post can also carry no goal image at all — `LaserTank` 1 has only a start.

**4. Geometry: two window sizes over nine years, and the frame finds both.** 609x463 and 619x473,
8-bit truecolour, served at Blogger's `/s1600/` as the untouched originals. The board is 16x16 at
**24 px per cell inside a two-pixel `(128,128,128)` frame**, at origin (20,62) and (25,67) respectively —
so the detector is `bytes.find` of a 384-pixel grey run over each row, a millisecond rather than a pixel
loop. **Zero failures over the 640 start and goal images of the two samples** (150 + 176 and 141 + 173),
both eras, seven of the nine collections the blog covers — `Challenge-III` and `Challenge-IV` have 3 and
8 posts between them and came up in neither draw. The first 26 were also cross-checked against a slower
whole-image pixel loop and agreed on every one. No third-party dependency: this machine has neither PIL
nor numpy, so `tools/png.py` is a stdlib PNG reader in the house style of `atlas_check.py`'s
`bmp_decode`.

**5. The codebook bootstraps itself and saturates.** For every post the collection and level are known
from the title, so a start screenshot is **256 labelled tiles for free**. Decoding each start board
against the codebook built from the boards *before* it — so the curve is honest and a disagreement is a
hard error rather than a self-fulfilling one — over 150 random posts:

| | sample A (150) | sample B (141) |
|---|---|---|
| codebook entries | **55**, from 38,400 labelled tiles | **55** |
| conflicts (a tile hash claiming two different `PF` values) | **0** | **0** |
| boards decoded exactly against earlier boards only | **135 of 150** | **133 of 141** |
| held-out check: `LaserTank` 10's start board | **256 of 256**, zero unknown tiles | 256 of 256 |

**Two independent random samples, and both land on 55.** They agree on **54 of the 55** and disagree on
**none** — each learned one animation frame the other's draw missed (a tank frame; an anti-tank-up
frame), so the true universe is ~56 and either sample is one rare frame short of it. *What is not stable
is which board teaches the last tile* — 37, 97 and 10 of ~140 across three runs, because it depends
entirely on which rare frame happens to come last in the shuffle. Quote the size, not the position.

**And there is only one graphics pack in play**, which was the third open unknown: dirt, solid, block,
bricks, ice, every mirror and every tunnel have **exactly one** tile image each over those 150 boards.
Only the animated sprites have several — anti-tank 3-4, water 3, flag 3, each conveyor 3 — so the
multiplicity is animation frames, not `.ltg` packs, and the tile universe is small.

**6. The goal side is where the cost actually is, and it does not saturate.** Reproducible as
`harvest.py codebook --goals`. Decoding ~175 goal boards against the 55-entry codebook leaves **93
(sample A) / 117 (sample B) further distinct sprites**, 3.79% / 4.52% of tiles, and at the last board of
either the set was still growing by one every ten boards or so. Unknown tiles per goal board, sample B:

| unknown cells | 0 | 1 | 2 | 3-5 | 6-10 | >10 |
|---|---:|---:|---:|---:|---:|---:|
| goal boards | 3 | 10 | 12 | 28 | 57 | **63** |

That distribution is what session 34 carried forward as the item's remaining cost, and it is **kept
because it is the number that turned out to be the wrong question** — see
[*Session 35*](#session-35--the-residual-was-not-hand-input), where the residue is derived instead of
labelled and the figure is 0.00%, not 1.37%. What follows in this section is session 34's reasoning as
it stood.

**Why, and why it is a labelling job rather than an ambiguity.** A start board only ever shows *authored*
states: the tank faces up, no laser is in flight, and no block has been pushed anywhere. The residual is
the states only play produces — the tank in the other three directions, an anti-tank over ice or water, a
roto-mirror flipped by a laser, a beam mid-flight, and above all **the block pushed into water**, which
is **68.5% / 69.7% of all instances on the two samples** on its own. That one is `Engine.cs:731`: pushing
a block (`obt == 5`) onto water sets **`PF = 0` and `BMF = 19`** — functionally dirt, drawn as a sunken
block. Which is the general shape of the whole residual: **`BMF → PF` is many-to-one, and `PF` is the
only half the solver wants.** So each of them has one right answer, and 26 were labelled by eye off a
contact sheet in one look during the spike.

*This paragraph is right about the mechanism and wrong about who has to supply the answer: because each
sprite has one right answer, and because the sprite that draws it and the rule that assigns its `PF` are
both committed in this repo, the answer is derivable and no eye is needed. Session 35, below.*

**7. `LaserTank.lvl` 10's goal board is in hand.** `/2016/06/10-valley-of-death.html`, decoded with
**one** undecoded cell — and that cell is the tank at (6,0), identified from its crop, one move short of
the flag at (7,0). The post *does* carry counters (the item's warning that they are not universal still
stands, `Challenge-I 1901` has none): **179 moves / 52 shots** against the `.ghs` record's 124/55.

The shape of that solution is the finding:

* **Zero of the ten anti-tanks are destroyed.** Six of them are somewhere else.
* Minimum total push distance over both direction groups, by exhaustive assignment, is **30** — against
  52 shots spent. So level 10 is won by *rearranging* anti-tanks, not by clearing them, and the goal
  board names the cells they have to end up in. (A push preserves an anti-tank's facing — `MoveObj`
  moves it and never rotates it — so `>` can only be matched to `>`, and the two direction groups are
  matched independently. Which anti-tank became which within a group is still a choice, and the numbers
  here take the minimum-cost one.)
* This is not news about the mechanic — `Heuristic.cs:957` already counts anti-tanks as pushable, and the
  comment beneath it is about level 10 by name. It is news about the *quantity*, and about the gradient.
  `--analyze` on level 10 offers **five** board changes at the root, three of which "open somewhere new
  to stand", and it cannot rank them. The goal board can: pushing (1,13) **up** closes the distance to
  its goal cell (0,12) from 2 to 1, while both pushes of (13,14) — left to (12,14), up to (13,13) — open
  the distance to (15,14) from 2 to 3. **A key that distinguishes those three is exactly what this level
  has never had.**
* **A hand-arithmetic result was wrong before the code checked it.** Matching goal anti-tanks to start
  anti-tanks by eye gave a total push distance of exactly 52, equal to the shot count, which read as a
  beautiful confirmation. The exhaustive assignment says 30. Same failure mode as session 33's 896-byte
  record: an arithmetic coincidence is the most convincing kind of wrong answer, and the fix is the same
  — reconcile against a number you did not derive.

**8. And level 6 "Cascade" still has no post.** Checked against the full index rather than by search now:
of `LaserTank.lvl` 1-20, levels **3, 4, 6, 7, 11, 13 and 20 have no post at all**. So this item can reach
one of the two open levels and not the other, and item 5 is unchanged by all of the above.

#### Session 35 — the residual was not hand input

**The labelling job does not exist.** Session 34 costed the goal-only residue at half a session of
eyeballing a contact sheet, on the reasoning that a start board only ever shows authored states and
nothing labels the states play produces. That reasoning was right and the conclusion was wrong: the
2010 binary's own graphics are **committed in this repo**, at `original/src/Game.BMP` and
`original/src/Mask.BMP`, and the tables that choose a sprite are in `LTANK2.C`. Compositing the two
the way the game composites them reproduces the blog's pixels *exactly*, so every state play produces
labels itself. It ships as `tools/sprites.py`, gated by `tools/harvest.py tiles`.

Three things had to be right, and all three are in the original source rather than guessed:

* **Size.** `LTANK2.C:1742` sets `SpBm_Width = SpBm_Height = 24` and `GFXInit` (`:766`) `StretchBlt`s
  the whole 320x192 sheet down to 240x144 — so every 32x32 sprite is drawn at 24x24, which is why the
  blog's cells are 24 px. `BMA[i]` is filled row-major *from i = 1* (`:784`).
* **The shrink, which is the part that made this look impossible.** `GFXInit` never calls
  `SetStretchBltMode`, so the mode is GDI's default `BLACKONWHITE` = `STRETCH_ANDSCANS`: the rows and
  columns a shrink eliminates are **ANDed** into the ones that survive, per RGB channel, with the
  grouping `dst = (src * 24 + 12) // 32`. A plain nearest-neighbour shrink gets the palette exactly
  right and the pixels wrong, which reads as "different artwork" — and that is the whole reason the
  sheet was never suspected. Both the mode and the grouping were **solved from one real dirt tile**
  rather than assumed: of seven candidate groupings, exactly one reproduces it, and it does so
  pixel-exactly. The AND is also self-evidencing — a real dirt tile carries a third colour,
  `0x108010`, that is `0x949410 & 0x108310` and appears in neither source sprite.
* **The composite.** `UpDateSprite` (`:487`) draws a cell as the `BMF2` background — an opaque sprite,
  or a `ColorList` rectangle for a tunnel — then, for a transparent foreground (`BMSTA[bmn] == 1`), the
  mask `SRCAND` and the sprite `SRCPAINT` on top. `UpDateTank` (`:537`) is a further mask+OR, and
  `UpDateLaser` (`:549`) a plain `Rectangle` inset by `LaserOffset = 10`.

**`BMF → PF` needed three engine rules on top of `GetOBMArray`, and each one is a bitmap the residual
actually contained:**

* a shot anti-tank is **`PF = 4`**, not dirt, with junk bitmap 54/52/12/53 for the way it was facing —
  `KillAtank`, `Engine.cs:868`, "the wreck keeps blocking the square". These four were the residual's
  **largest family**: 55 + 34 + 32 + 20 = 141 of 606 instances;
* a block pushed into water is **`PF = 0`** with `BMF = BMF2 = 19` (`Engine.cs:731`) — the one sprite
  session 34 had already hand-labelled, and the derivation agrees with it;
* the tank is **not in `PF` at all**. `BuildBMField` clears `PF` at the tank's cell on load
  (`Engine.cs:348`), so a cell the tank stands on carries the terrain's `PF` and the tank is separate
  output. This is the only place the blog's pixels are genuinely ambiguous — `T` as a foreground
  bitmap and the tank overlay facing up are the same pixels — and the rule decides it rather than a
  coin toss. `decode` now returns `(x, y, facing)` beside the board, which is the shape `--goal-board`
  wants anyway.

**The gate, and it is a gate because none of these tiles were fitted to.** `python tools/harvest.py tiles`:

| check | result |
|---|---|
| the **start-bootstrapped codebook**, whose labels come from the `.lvl` files and no sprite | **53 of 55 agree, 0 clash**; 1 is the tank cell, reconciled as (terrain, facing); 1 not derived |
| the **goal residual** — the sprites a start board can never label | **116 of 116 sprites, 606 of 606 instances** |
| every **goal board**, decoded end to end | **0 unknown tiles of 44,288 over 173 boards** (100% of boards clean, against session 34's 10) |
| the tank located on each goal board | **173 of 173** |
| `decode --check` over every **start** board, against the `.lvl` | **141 of 141, 0 mismatches** |

**Four independent reconciliations, because a pixel match to a table you built is not evidence.**
The check that mattered was computed *before* the sheet was opened: for each unknown sprite, the
distribution of the **start board's `PF` at the cells where it appears**, read from the `.lvl` files.
It is nearly pure — almost every sprite sits over exactly one terrain — and it agreed with the
derivation on **all 95** sprites the first (OBM-only) enumeration matched. It then independently
confirmed the `KillAtank` family: bitmaps 54/52/12/53 appear over start cells `^`/`>`/`v`/`<`, matching
the source's four `case` arms one for one. Third, the derivation reproduces `LaserTank.lvl` 10's tank
at **(6,0) facing right** — the cell session 34 identified by eye from its crop. Fourth, it puts 10
anti-tanks on level 10's goal board with **no wrecks**, reproducing that session's "zero of the ten are
destroyed" from pixels instead of arithmetic.

**The two tiles that are not game states, kept rather than papered over.** One start tile
(`Challenge-I 944` at (15,15)) is the tank drawn as a **black silhouette** — a screenshot caught
between the mask `SRCAND` and the sprite `SRCPAINT`, so it is a capture artifact and no composite can
produce it; the start bootstrap already labels it and only the *facing* is lost, which is why "boards
with no tank found" is a printed number. And `UpDateLaserBounce` (`:565`) draws half-cell rectangles
that are **not** enumerated, along with any explosion frame: **0 of 44,288 tiles in the sample needed
them**, so they are a named gap rather than a silent one, and `sheet` still reports anything the sheet
cannot draw.

#### Session 36 — the key the item asked for is not the key that works

The build is `src/LaserTank.Solver/Goal.cs` plus `tools/harvest.py bank`, and its acceptance test was
written in advance: level 10's three root pushes, which `--analyze` offers and cannot rank.

**The item's own wording for the key is flat on exactly that test.** It read *rank by
cells-still-differing*, and Hamming distance to the goal board does not move under a push at all — the
push vacates one cell and fills another, so a count of misplaced objects is unchanged while an object is
in transit. That is RouteFerry's failure one level up, and it is the first thing the build measured:

| | cells differing | goal distance |
|---|---:|---:|
| root | 12 | **30** |
| shoot up from (1,15): anti-tank (1,13) → (1,12) | 12 | **29** |
| shoot left from (14,14): anti-tank (13,14) → (12,14) | 12 | **31** |
| shoot up from (13,15): anti-tank (13,14) → (13,13) | 12 | **31** |

So the key is the **assignment**: match each misplaced object to a goal cell wanting one of its kind and
sum how far each still has to travel; price an object that has to be created or destroyed instead
(`--goal-miss`, default 16) as a cliff, because which block fills which hole is RouteFerry's question and
not this one. Cells that already agree are dropped from both sides first, which is exact rather than an
approximation — the costs are a metric, so a cell already holding the wanted object can always be matched
to itself — and it is what keeps the assignment over a handful of cells instead of over a twenty-block
Sokoban. Hungarian, O(n²m), with a nearest-first fallback above 32 misplaced objects of one kind.

The 30 is a **reconciliation and not a fresh number**: session 34 computed the same minimum by exhaustive
assignment, by hand, and got 30. `Differing` is kept beside `Distance` because it is the number to
*report* — "this run got the board to within four cells" — and not the number to rank by.

**The acceptance test, run:**

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 10 --analyze \
  --goal-board bench/goal-boards.json
```

```
  goal      the scraped goal board: 12 cells still differ, distance 30  (flag b, the blogger's
            179 moves / 52 shots, .../2016/06/10-valley-of-death.html)
        shoot up from (0,15)   -> anti-tank> (0,10) -> (0,9)                      [goal 29, -1]
        shoot up from (1,15)   -> anti-tank> (1,13) -> (1,12)  [+33 to stand in]  [goal 29, -1]
        shoot left from (14,14)-> anti-tank< (13,14) -> (12,14) [+1 to stand in]  [goal 31, +1]
        shoot up from (13,15)  -> anti-tank< (13,14) -> (13,13) [+17 to stand in] [goal 31, +1]
        shoot up from (15,15)  -> anti-tank< (15,12) -> (15,11)                   [goal 31, +1]
```

Two of the five root changes descend and three ascend, and the two that descend include one the read
never named at all — (0,10) up, which opens nowhere to stand and lands on no barrier. **A key that
distinguishes those three is what this level has never had**, and it now has it. It does not follow that
level 10 falls: the level is unsolved at 399M nodes and nothing here was run at that budget.

**What the key is worth, over the population rather than over the example.** The 141 levels the fetched
bank covers, one collection at a time, `--no-ida --no-beam --push --push-read`, `--nodes 4000000
--budget-ms 120000 --jobs 12`, the only difference between the arms being `--goal-weight`:

| arm | solved of 141 |
|---|---:|
| `--goal-weight 0` (the control — same run, key off) | **15** |
| `--goal-weight 1` | **21** |
| union | 24 |

+9 and −3, and the three it loses are the reminder that this is a ranking key and not an oracle: a goal
board says where the objects end up, not in which order, so a level whose blogger route disagrees with
the machine's is now being steered away from the machine's. All 21 passed `verify_solutions.py` — both
engines, WIN on each, byte-identical traces. The weight is **untuned**: 1 is what `--push-ferry` uses,
`--goal-miss 16` was picked and not swept, and neither has been measured against any other value,
because everything this produces is outside the rate and the population is 141 levels rather than 3,709.

**The honesty condition is code, not a convention.** Three enforcements, because a note in a file is not
one: `--goal-board` moves the default output (`solutions` → `solutions-hint`; `data/solutions` →
`data/solutions-hint` in the driver, which is the one that writes into git), every report row it produces
carries `"hint": "goal-board"`, and the run prints a banner saying so. An explicit `--out` is still
honoured — the user has said where — and the row carries the stamp either way, which is the half a
directory name cannot do.

**What the bank holds.** `tools/harvest.py bank` writes `(collection, level, goal PF, tank, moves,
shots)` per level, ordered so the board the level *ends* on is last — by flags still on the board, which
is derived from the pixels, because the later-era image tags are cell names and carry no order. It
**refuses** a board with an undecoded cell rather than banking a key with a hole: a `?` prices that cell
as already-right wherever the search happens to be, which is worse than having no key. Over the fetched
sample that refusal never fires — 173 of 173 boards bank clean, which is session 35's number arriving
through a second path.

**One field in that tuple is not derived, and it is the only one.** The panel's Moves and Shots are
`TextOut` with the system font (`LTANK.C:563`), not sprites, so no composite of `Game.BMP` can draw them
and nothing in this tree says what they say. That is the *other* side of session 35's rule: a human's
answer that the repo cannot re-derive is exactly what `bench/` is for, so session 34's reading of level
10's panel is committed as `bench/goal-counters.json` rather than left in a session log, and a level
absent from it banks `moves`/`shots` as null.

**Where the artefacts live.** The bank is derived, so it lands in gitignored `build/harvest/goals.json`
(`bank` is one command over already-fetched images, half a second). The exception is
**`bench/goal-boards.json`** — level 10 alone, ~1 KB — because the numbers above are quoted against it
and a number that needs a network fetch to reproduce is not banked. *(Session 40 turned that exception
into the rule: `complete` writes the whole 5,975-level bank there, for the same reason at corpus scale.
A scoped `bank --levels X` still defaults to `build/harvest/goals.json`.)*

**Rebuilding the whole thing from nothing**, all of it under gitignored `build/harvest/` (`index.jsonl`,
`img/`, `fetched.json`, `codebook.json`, `stale.json`, `residual.{png,json}`, `goals.json`):

```bash
python tools/harvest.py index                    # 42 requests, 46 s
python tools/harvest.py fetch --limit 150 --goals    # ~7 min
python tools/harvest.py codebook --goals         # ~45 s
python tools/harvest.py tiles                    # the derivation's gate; no fetch, half a second
python tools/harvest.py bank                     # -> build/harvest/goals.json
```

`tiles` is not in the fetch chain on purpose: it builds the derived table from `original/src/` alone, so
the half of this item that used to be costed in human hours is the half that needs no network at all.

#### Session 38 — the corpus-scale run, and what its two complaints were

The item closed on a **150-post sample**. Session 37 ran `fetch` over the whole index, and this is the
first time any of it was measured at corpus scale: **6,218 posts → 5,779 start boards and 6,708 goal
boards** — 12,487 images — in 34 minutes. Then `codebook --goals` stopped the chain on two complaints, and
neither was what it looked like. Both are worth the space because **both were the instrument reporting
honestly about something outside itself** — one about the corpus, one about the source.

| the run | 150-post sample (session 35) | whole index (session 37) |
|---|---|---|
| start boards | 141 | **5,779** |
| goal boards | 173 | **6,708** |
| codebook entries | 55 | **57** |
| decoded exactly against earlier boards only | 141 of 141 | **5,742 of 5,769** |
| last board to teach the codebook a new tile | — | **4,111 of 5,769** |
| goal-only sprites the start boards never label | 116 | **597** (20,335 of 1,716,992 tiles, 1.18%) |
| conflicts | 0 | **46 cells on 11 boards** |
| `NOFRAME` | 0 | **2** |

**The 46 conflicts are re-authored levels, and the sprite sheet is what says so.** A cell where the
codebook and the `.lvl` disagree has exactly two causes and they point opposite ways: either the
codebook's label is wrong, or the *screenshot* is of a level the `.lvl` no longer matches — the blog is
nine years old and levels have been re-authored under it. `tools/sprites.py` decides which, because it
derives the hash from the game's own graphics and owes nothing to either side, and it backs the
**codebook** at all 46: **0 clashes over the whole 57-entry table**. So the pictures are right and the
levels moved. `Challenge-V 372` is the clearest — the picture shows the same `C` at (12,11), (13,11) and
(14,11) where the `.lvl` has a tunnel, a mirror and a mirror, and no amount of *play* turns three
different terrains into three identical tiles.

**What rules out play, though, is the tank and not the sheet**, and it is worth being exact about it: a
mid-solution screenshot also has honest pixels and a wrong `.lvl` label, so the sheet blames the `.lvl`
there too. The separator is session 37's own admission test — on these 11 boards the tank is on the
`.lvl`'s own `T` cell facing up, so the picture is a genuine *start* position of a board the `.lvl` does
not match. Run the same decode-vs-`.lvl` diff over all 5,779 start boards with the full codebook and it
finds **14**, which is the whole population and it splits three ways:

* **11 re-authored levels** — the conflict list, admitted as start positions;
* **2 play states**, `LaserTank 521` and `596`, already rejected as *not a start position* (tank facing
  right, tank off the start cell) and confirmed independently by the diff: anti-tanks slid along a row,
  blocks gone, water filled;
* **1 capture artifact**, `Challenge-I 944` at (15,15) — the black-silhouette tank session 35 named.

**A stale board is now not allowed to teach.** This is the part that was luck rather than a check:
`setdefault` makes the first board to show a tile own it for good, so a stale board arriving *early*
would poison a label with no conflict to show for it — the honest boards would conflict, not the one that
lied. That the 57-entry table came out clean is ordering, not a guarantee. `codebook` now excludes a
disagreeing board from the labelling pass altogether, exactly as it excludes a play state, and prints
`STALE` with the sheet's per-cell verdict instead of an unexplained `CONFLICT`. The exit code narrowed to
match: a disagreement the sheet blames on the `.lvl` is a finding, one it blames on the codebook or
cannot arbitrate is still a failure.

**The consequence is on the goal boards, not the start one, and it is `bank`'s.** A goal board from a
superseded revision is a target the current level **cannot reach**: the key bottoms out above zero and
the search chases it to the time limit. That is strictly worse than the undecoded cell `bank` already
refuses — a hole misprices one cell, this misprices the whole board — so `codebook` writes the levels to
`build/harvest/stale.json` and `bank` refuses them, `--allow-stale` to look. A *missing* `stale.json`
is a hard stop there rather than a note, for the same reason a missing codebook is: both come out of
`codebook`, so a codebook without a stale list beside it is one from before the check existed.

**Both `NOFRAME`s were Blogger serving a downscale, and neither post is unusable.** `origin` reported no
384-pixel frame because there was none: both files were **512 px wide**, a resample of a real window with
no 24-pixel grid left in it. Not authors resizing screenshots — the originals are there, and asking for
them is the whole fix:

| image | as the post links it | as `s16000` serves it |
|---|---|---|
| `SpecialI_491.png` (start) | **512x389** at `/s1600/` | **609x463** |
| `SpecialI_343a.png` (goal) | **512x391** at `/s619/` | **619x473** |

`fetch_one` tried the post's own URL **first** and only rewrote the size directory as a fallback — while
its own docstring said the opposite, which is how the bug survived reading. Blogger answers a small-size
path with a downscale even when the number is larger than the image (491's own `s1600` is bigger than
609), so the post's URL is the *last* thing to ask. Rewrites first now, plus a width check from the IHDR
so a rendition narrower than a LaserTank window is not accepted while a candidate is untried, plus
`fetch --refetch` to replace what an earlier run got as a downscale. Both recover and both pass their
gate: `decode --check` on `Special-I 491`'s start board is **0 mismatches vs the `.lvl`**, and
`Special-I 343`'s goal frame decodes with **0 unknown tiles**, tank at (10,2) facing right. Its
`.ghs` record of **44,975 moves / 1,086 shots** — the most extreme in the corpus, and the reason the
post looked like a special case — had nothing to do with it.

**Two findings the run turned up that are nobody's bug.** The codebook-vs-sheet comparison `tiles`
runs gives **3 codebook entries the sheet cannot derive**, `>`/`<`/`^` — 5 cells on 5 boards, and all five are **496 of 576 pixels pure
black**, in the shape of an anti-tank's own mask with the barrel notch in the right place. They are the
artifact session 35 named on `Challenge-I 944`'s tank: a screenshot caught between the mask `SRCAND` blit
and the sprite `SRCPAINT` blit. Harmless, because the `.lvl` labels them and the mask belongs to the
sprite that was about to be drawn, so only the facing is lost. **Session 39 retired that verdict twice
over**: the silhouette is derivable after all (`Cells.torn`), and the tank-over-anti-tank cells this
paragraph waves through are information-free rather than merely unlabelled — see
[session 39](#session-39--the-logs-drops-and-the-two-that-were-the-instruments-fault). And **265 levels contributed no start
image at all**: 242 of them because their posts use Blogger's newer `/img/a/<blob>=s609` URL shape, which
carries **no filename** — and `pick_images` keys start-vs-goal off the `<coll>_<n>[a-z]` filename, so it
cannot tell them apart. 20 more are named but unmatched (including the `KaserTank - 826` title typo
`map` already reports) and 3 are mixed. That is a **4% hole in the corpus with a known cause** and no fix
attempted here; post order is the obvious candidate and it is a separate piece of work. **It was also an
undercount**: those posts lose their *goal* frames to the same missing filename, so 246 levels contributed
nothing at all. Session 39 took the post-order fix and measured it.

**Rerunning the chain costs no network.** The 12,487 images are cached and `fetch` skips what is on disk,
so only the two downscales needed `--refetch`:

```bash
python tools/harvest.py fetch --levels Special-I:343,491 --goals --refetch   # done
python tools/harvest.py codebook --goals    # ~36 min: 5,779 boards at 0.37 s
python tools/harvest.py tiles               # the derivation's gate
python tools/harvest.py bank                # -> goals.json, minus the stale levels
```

#### Session 39 — the log's "drops", and the two that were the instrument's fault

Session 38 handed over a chain that ran to completion and a log full of numbers that *looked* like
losses. Working through them one at a time: **most cost nothing, two cost a great deal, and two of the
"nobody's bug" verdicts above were wrong.** The pattern worth keeping is that every one of the five real
findings came from treating a number as a measurement rather than as a threshold.

| | session 38's run | after this session's fixes |
|---|---|---|
| levels with a start frame | 5,779 | **6,023** |
| goal frames | 6,708 | **7,484** |
| levels contributing *no image at all* | **246** | **1** |
| goal boards with an undecodable cell | 5 | **0** (3 hand-stated, 2 derived) |
| derived tiles, internal sheet at 24 px | 4,108 | **7,238** |

**The 265 "no start image" was really 246 levels contributing nothing.** The same filename decides
start-from-goal *and* which-flag, so a post whose URLs carry no filename loses its goal frames too — not
just its start. `pick_images` now falls back to the post's own document order when *no* image is named,
all-or-nothing per post (a post that names some of its pictures has made a claim about those, and mixing
a claim with an order is how a wrong answer looks confident). It is the one guess in the file and it is
arbitrated where the pixels are: `codebook` admits a start board only if the tank is on the `.lvl`'s own
`T` cell facing up, so a post listed goal-first is rejected there and *named* — `fetch` records which
rule picked the frame. Falsified on a 15-level sample before shipping: **13 decode to the `.lvl` exactly**
— 0 unknown, 0 differing cells, tank on its own start cell — and no goal frame of any of them is itself a
start position. The other 2 failed for the zoom reason below, not for order. Also `fetch_one`'s size
rewrite never worked on that URL shape at all (the size is a `=s609` suffix, not a path segment), so
every one of those levels fell through to the smallest rendition — the thing that docstring exists to say
should be asked for last.

**518 goal frames on 83 levels were never fetched, and the Blogger feed's order is why.** `fetch` read
`by_level[(coll, n)][0]` and the feed is newest-first, so an 11-part post contributed **part 11's two
frames and dropped the other 38**. `Challenge-I` 306 is that level. The final board survived — `bank`
orders by flags still on the board rather than by the post's layout — so what was lost was never the
target but the **per-flag subgoal sequence** that ordering exists to carry, which is *Further out*'s
chaining note. Parts ascending now, first part wins a tag collision (17 of 535 collide, every one a
reshoot of the same flag), and each frame keeps its own post's url so a refusal names the right one.

**The board frame's grey run is a measurement, and reading it as "at least 384" decoded three boards into
garbage.** It is 16 tiles of board plus 2 pixels of frame, so it *says what the sprite pitch is* — and
386/514/642 are exactly `SetGameSize`'s three zooms (`LTANK2.C:1729`), which **`PROGRESS.md` has
documented all along**. Three posts are the 32-pixel zoom, where GFXInit's shrink degenerates to the
identity and the sheet's own cells are what is on screen; `find` had been matching *inside* their longer
run and returning a plausible corner. Measured over every image in the corpus: **12,498 at 386, 5 at 514,
none at 642.** `origin` returns the pitch now, and 642 is in the table anyway because recognising a zoom
costs one derived table and beats decoding a fourth surprise.

**A screenshot is not necessarily of the internal sheet either.** `LaserTank` 1619's post is
*EyeSaver+Grid* — a `.ltg` pack this repo already ships under `data/graphics/`, with its format in
`PROGRESS.md` and its reader in `GraphicsFile.cs` — and against `Game.BMP` all 256 of its tiles came back
unknown, teal where dirt is olive. `sprites.py`'s own BMP reader was the obstacle: the packs are 24 bpp
with a 1 bpp mask and it did 4/8 only, while `PROGRESS.md` says both readers handle 1/4/8/24. With those
depths and an `ltg()` splitter (the container is literally two BMPs at `MaskOffset`, so splitting it is a
byte copy), **1619 decodes with 0 mismatches vs the `.lvl`.** `decode_board` tries the shipped packs only
when the internal table leaves unknowns, which is measured-safe rather than hopeful: across all eight
`(pack, pitch)` tables **no hash carries two different `PF` values**, so a match is a match and the
fallback cannot invent an agreement. The bank records which pack a picture was of.

**The mid-blit capture is part of the compositing model, not an exception to it.** Two sessions wrote the
black silhouette off as "no composite can produce it" — but it is the same composite with one GDI call
missing, and `Cells.torn()` derives it like everything else: **+3,130 unambiguous tiles, 1,109 dropped as
ambiguous, and zero collisions with a tile the fully-drawn pass already owns** (measured before it went
in). It recovers a whole start board — `Challenge-I` 944's `NOT A START` was its tank at P16 torn, and it
now decodes 0 unknown / 0 differing — and `Sokoban-I` 1930's goal cell, tank facing right, which Michal
confirmed independently by playing it.

**`PF` and the tank facing are separately ambiguous, and only one of them ever actually is.** Every
facing has its own sprite and its own mask, so it survives whatever the cell is standing on: of the 1,398
hashes `table()` drops as PF-ambiguous, the facing is unambiguous on **1,398 of 1,398**, 578 of them
showing a real tank. Dropping the whole tile threw that away. `sprites.facings()` keeps it, so a cell
nothing can label still says where the tank is and which way it points — which is what shrinks the hand
input below to a single symbol per cell.

**Session 38's "289 dropped as PF-ambiguous, every one unreachable" was wrong, and so was calling the
three underivable codebook entries harmless.** The 289 are not unreachable, they are **information-free**:
the tank drawn on an anti-tank occludes the cell so completely that `PF` 10 and `PF` 4 give identical
pixels and so does *any* background under them. Two of them turn up in the corpus (`LaserTank` 899 at I2,
901 at D15). A cell like that cannot be hand-labelled either — there is nothing in the picture to read —
and it cannot go in a hash table, because the same pixels mean different things on different boards.

**So it goes in `bench/post-fixups.json`, keyed per post and per frame, and a recording is not the
answer.** The tempting fix was to replay a committed `.lpb` through the oracle's `--field` trace and read
the true `PF` off the engine; it was built, it worked, and it agreed with Michal exactly (`A7 = '>'`,
`I2 = '<'`). He rejected it and he was right: the recordings were scratch (`0727_temp.lpb`) and were
deleted within the hour, so the derived artefact would have gone stale pointing at files that no longer
existed. **A durable fact stated by a human beats a derivation from a throwaway input** — and the `.lvl`
corroborates the stated values anyway (899's anti-tank started at J2 and 901's at E15, both `<`, both
pushed one west). The same file carries the three blog-side level-naming errors and `Challenge-I` 1598's
tank, whose frame decodes with 0 unknown cells and no tank anywhere: that source image is wrong, not the
decode.

**Two process notes.** `tiles` was silently skipping the middle of its own three gates for the whole
corpus-scale run — the goal-residual check needs a `residual.json` that only `sheet` writes, and the run
never called `sheet`; it says so now. And a percentage is not a finding: 0.02% of tiles unknown was **one
image with no sprite grid in it plus four capture artifacts**, which the histogram could not say, so
`tiles` names every board that is not clean, worst first, with the post to go and look at.

**Rerunning still costs almost no network** — the fixes recover ~1,000 images that were never fetched and
the other 12,487 are cached:

```bash
python tools/harvest.py map                 # free; expect 1 retarget from post-fixups.json
python tools/harvest.py fetch --goals       # ~1,000 new images, no --refetch needed
python tools/harvest.py codebook --goals    # ~40 min; watch for NOT A START ... [order-picked]
python tools/harvest.py tiles               # both pitches reported; names every unclean board
python tools/harvest.py bank                # -> goals.json
```

#### Session 40 — the whole chain ran clean, and its two complaints were one repair and one instrument bug

Michal ran the rerun above end to end (`build/harvest-full.log`, gitignored). **It came out where session
39 predicted**: 6,218 posts indexed, 6,044 (collection, level) pairs covered, 6,012 start boards admitted,
**7,484 goal boards decoded with 4 unknown tiles in 1,915,904** (0.00%, 4 distinct), and `bank` wrote
5,975 levels / 7,462 boards to `bench/goal-boards.json`. Twelve refusals: 11 stale levels — the same 11
`stale.json` has always held — and **one board refused for an undecoded cell**. Both of the run's
complaints turned out to be about the *report* rather than the decode, except for one genuinely new cell.

**"4 unknown tiles" was 3 already answered plus 1 new, and `tiles` could not say which.** `LaserTank`
899 at I2, `LaserTank` 901 at D15 and `Challenge-V` 727 at A7 are session 39's three occluded cells, each
already stated in `bench/post-fixups.json`; `bank` fills all three and banks the boards, and the same run
proves it (`cells filled from a post fixup: 3`). Reading the two phases side by side to work that out is
exactly the cross-reference a report should do for the reader, so `tiles` now looks the fixup up per
frame and says `1 of 1 from a post fixup` on the row — and `boards with no tank found: 1` likewise names
`Challenge-I` 1598's fixup. **A row with no note is the row to go and look at**, and there was one:
`Sokoban-I` 1060 `b` at **O2**, refused by `bank`. Same family as 899 and 901 — the tank drawn over an
anti-tank, `PF` 10 against `PF` 4, which occludes the cell so completely that the background is
unrecoverable too — so it needs a line in `post-fixups.json` and only Michal can supply it. What the
pixels do give: the tank is at O2 facing right with the flag at P2 (the last frame, one move short), the
ambiguity is `^` against `#` (an anti-tank facing up or its wreck), and the background narrows to dirt or
*some* tunnel. The `.lvl` has tunnel 1 at O2 and its only other tunnel-1 cell at O4, and anti-tanks
facing up demonstrably ride this level's tunnels — the same frame shows `^` at P4 and H7 where the `.lvl`
has tunnels 6 and 7. So both candidates have a story and the pixels cannot choose: `^` if the anti-tank
is real and the tank sprite is the artifact (which is how 899 and 901 resolved), `1` if the anti-tank is
stale residue and the tank arrived through the tunnel from O4.

**And the chain is one command now, because reading five phase reports side by side was the wrong job for
a human.** Both complaints above are the same complaint: the question the reader has is *which posts are
not in the bank, and is that on purpose*, and answering it meant cross-referencing `map`'s counters,
`fetch`'s counters, `codebook`'s `NOT A START` list, `tiles`' unclean-board list and `bank`'s refusals by
hand — five reports, several hundred lines, and the one row that needed a human was in none of them by
itself. `harvest.py complete` runs the same phase functions, sends every phase's own output to
`build/harvest/complete.log`, and prints one funnel and one table: a row per post the decode did not
finish on its own, what was found, and **who can resolve it** — `intentional: ...` or `CLARIFY: ...` —
with the count of the latter as the last line. `--report` re-prints it from `complete.json` for free,
`--all` expands the two categories that are the blog's shape rather than findings (no start screenshot,
no goal screenshot: 79 of the 80 rows, which is exactly why they collapse). It banks into the committed
**`bench/goal-boards.json`**, which is where this project's persistent artefacts live and the only
sensible home for a 5,975-level file that needs nine years of blog to re-derive — and it *overwrites*
it, reporting the bank it replaced (`it replaced a bank of 5,975 levels and 7,462 boards`, and loudly
when the new one is smaller), because guarding that write would hide a regression in the chain where the
diff shows it.

**It is also the measurement that came out of writing it: the chain was decoding every goal board three
times.** `codebook --goals` (the residual sizing), `tiles` (the gate) and `bank` all walk the same 7,484
boards at **37 minutes each**, and the only difference is what they report. The full run above cost 2h40
for that reason. `complete` keeps `tiles`' two cheap gates (`--gate-only`, seconds) and takes the
per-board findings from the bank pass — which is the pass that has the fixups and the refusals in it
anyway — so the same artefacts come out in **~65 minutes** with strictly more reporting. The residual
sizing is not in the chain at all any more: it sized an item that is closed, and `codebook --goals` still
runs it on demand. What `complete` does *not* do is decide anything the phases did not: it is their
findings, assembled.

**Six of the eleven `NOT A START` boards were start boards, and the check's own short-circuit hid it.**
The admission test read *facing up* before it read the cell, so a player who turned in place before taking
the screenshot lost the board — and a turn in place changes no `PF` whatever. Measured on all six
(`Challenge-V` 743, `LaserTank` 19, 223, 283, 385, 498): each decodes to its `.lvl` with **0 unknown and
0 differing cells**, tank on its own `T` cell, turned. The cell is the test now and the facing is free,
which admits them (6,018 start boards, 5 rejections) and leaves every real play state rejected —
`LaserTank` 521 was reported as "tank facing right" when the tank is off its start cell and 20 cells into
a solve, and the reason line says that now. **What the rejection cost was not labelling**: their one new
tile is their own turned tank cell, which `sprites.py` derives anyway, so the codebook gains 6 entries and
0 coverage. It cost the *checks a rejected board skips* — those six levels were never conflict-checked
against the `.lvl`, so a re-authored one among them would have handed `bank` a target the level cannot
reach, which is the one failure mode `stale.json` exists to stop.

#### Session 41 — the filenames the harvester could not read, and the four readers that assumed the graphics

Michal read session 40's table and rejected two of its rows wholesale: the 58 levels "carrying no goal
screenshot" and the 21 "carrying no start screenshot" are mostly wrong, **the screenshots are there, under
names `pick_images` did not read**. He was right on both counts, and the reading was wrong in four
separate ways. Fixing them costs no new guess about which picture is which and gains **+160 goal frames
and +6 start frames** — 7,484 → 7,644 and 6,023 → 6,029 — with the levels that contribute *no goal frame
at all* going 58 → 3 and *no start frame* 21 → 15.

**A filename is not a picture, and keying on one threw pictures away.** Blogger serves each screenshot at
several sizes and the identity of the picture is the blob in the URL, not the basename: the old shape
varies a path segment (`.../<blob>/s1600/LaserTank_801.png`) and the new one a suffix (`<blob>=s609`).
`posts_by_level` deduplicated on the basename, so a post where the author saved *both* screenshots under
one name collapsed to one picture and the second was gone before anything could look at it. **37 posts do
that** — 30 of the 58 "no goal screenshot" rows and 3 of the 21 "no start" ones — and `LaserTank` 801 is
the shape of all of them: two blobs, one name, a start and a goal. `pictures()` keys on the blob and keeps
the post's document order.

**The frame tag is now whatever the filename has left.** Three patterns read the remainder before — bare,
`[b-z]`, and `_<cell>` — and **113 basenames in the corpus matched none of them**, of which 73 are genuine
frames of the level they name: `502_1.png`, `36b1.png`, `173b_4.png`, `SokobanI_620_A1_2.png`,
`SpecialI_431_percent_25.png`, `SpecialI_431_end.png`, `LaserTank_1230bb.png`, `1978_Finish.PNG`, and the
2016 era's own `476_P3.png` — a flag cell, dropped only because that spelling required a collection prefix
which the prefixless era does not write. The tag is an ordering key and a piece of a fetched file's name,
interpreted nowhere, so the rule is to keep the author's own word rather than to understand it: the two
spellings the bank is already keyed on are pinned (`b`, `A15`), everything else is the remainder minus the
characters a filename should not carry. The one guard that earns its place is that the remainder may not
open with a digit — `LaserTank_45.png` in a level-4 post is level 45's picture, not level 4's frame `5`.

**Pictures the filenames do not account for fill the roles the filenames left open**, in the post's own
document order, which generalises session 39's fallback rather than replacing it. That fallback was
all-or-nothing per post, on the reasoning that a post which names *some* of its pictures has made a claim
about those and mixing a claim with an order is how a wrong answer looks confident. True, and it is still
true — but it also refused to place pictures no claim covers at all. Three shapes need it and they are one
rule: a post whose pictures carry no filename (242 levels, session 39's case); a post that mixes named and
filename-less pictures (`SokobanI_942b.png` is the goal and the unnamed one is the start); and a post where
two *different* pictures make the same claim, which is a claim that cannot be honoured for either, so both
fall through and the first is the start. What it never does is overrule a filename. It stays the one guess
in the file and it stays arbitrated where the pixels are — `codebook` admits a start board only if the tank
is on the `.lvl`'s own `T` cell — and the arbitration promptly earned its keep: of the 6 starts it
recovered, **4 decode to their `.lvl` with 0 differing cells** (`Challenge-V` 576, `Sokoban-I` 942, 943 and
1051) and 2 are play states — `Challenge-II` 65 at 29 cells out, `Special-I` 151 turned the wrong way —
which the gate rejects by name as `[order-picked]`.

**And 40 pictures over 21 posts name another level or another collection outright.** `LaserTank_452.png`
in the Sokoban-I 452 post, `SokobanI_1081.png` in the Sokoban-I 1080 one, `SokobanI_234_B7_C6.png` in the
Sokoban-I 236 one. Reading the filename cannot tell a typo from a screenshot of a genuinely different
level — that is `pick_images`' whole premise, that a filename is a *claim* — so they are dropped, and the
change is that `map` now **reports** them: before, 21 posts silently lost a frame apiece. **Decoding each
picture against both candidate `.lvl`s does settle it**, and it splits them almost evenly:

| | posts | pictures | what the decode says |
|---|---|---|---|
| the number or prefix is a typo | 12 | 30 | 11–99 cells from the title's board, 141–240 from the filename's |
| the picture really is another level's | 9 | 10 | **0** cells from the filename's own board, in 7 of the 9 |

Both populations are committed to `bench/post-fixups.json`, the first as `image_level`/`image_coll` and
the second as `confirmed`. The method is not a new instrument, it is `decode --check` run twice, and it
reproduces both cases that were already decided by hand: `Sokoban-I` 344, which Michal played in session
39 and retargeted to `image_level: 343`, comes back 49 cells from 344 against 240 from 343; and
`LaserTank_452.png`, which `pick_images` has dropped as another level's screenshot ever since it
started reading the prefix, comes back **0** cells from LaserTank 452 — the same 201 conflicts it once
taught the codebook, now on the other side of the ledger. `image_level` and `image_coll` also had to start *widening* rather than replacing: the Sokoban-I
236 post carries `SokobanI_236.png` spelled right next to four `SokobanI_234_*` frames, and a retarget
that renamed the level out from under the start would have traded the goal frames for it.

**`Sokoban-I` 1060 O2 is `^`, and session 40's two candidates were both right.** Michal: the tank is
standing on a north-facing anti-tank, moved there by mirrors, which is itself standing on the green tunnel
the `.lvl` already has at O2. So it is 899's and 901's cell shape exactly — the object is what `PF` holds
and the tunnel is what it is standing on — and the last row waiting on a human is answered.

**Two rows that were correct, and a way to say so.** `LaserTank` 521 and 596 are reported as start frames
that are not start positions, and Michal confirms both: the posts really do lead with a play state.
Nothing to fix, and until now nothing to record either, so every run asked again. `confirmed` in
`post-fixups.json` is that word — it corrects nothing, it closes the question, and the row it annotates
stays in the table with the note attached, because the tool still cannot see what was checked and will
find the same thing next run.

**Every row prints its post URL now**, not only the ones waiting on a human: the rows that are *not*
questions are the ones a reader most often wants to open, to confirm for himself that a refusal really is
the blog's shape, and a row without its URL made that a search.

**Michal then opened all 40 rows of that run's table, and two of them were the tool's fault** — which is
the URL change paying for itself on the first run it shipped. `LaserTank` 1126 and 1619 are reported as
`no tank in the picture [order-picked]`, and the tank is plainly in both pictures: 1126's on N1, 1619's on
F13, `Moves 0` and `Shots 0` on the panel beside them. **The start-position gate was the one reader in the
chain still assuming what graphics a screenshot is of.** `origin` has measured the sprite pitch off the
board frame since session 39 and `decode_board` has tried the shipped `.ltg` packs since the same session
— that is how *both* of these posts' goal frames were banked, 1619's recorded as `EyeSaver+Grid` — but
`codebook` built one table from `sprites.Cells()`, the internal sheet at the module's default 24 px, and
asked all 6,022 frames about it. A picture of anything else has no tank in it by construction. 1126 is the
32-px zoom; 1619 is 32-px *and* EyeSaver+Grid. `frame_sheet` now reads both off the picture — the pitch
from the frame, the pack as whichever derivation the tiles are actually in, packs tried only once the
internal sheet has left something unread, on `decode_board`'s own measured safety that no hash carries two
different `PF` values across the eight `(pack, pitch)` tables. Both boards are then admitted with the tank
on the `.lvl`'s own `T` cell facing up and **0 unknown, 0 differing cells**, and the gate does not get
looser in the process: `LaserTank` 521, the play state in the same sample, is still rejected by name.

**The seat comes from `sprites.facings()` rather than from the table**, which is the same fix as session
39's for the same reason: the gate asks the pixels *which cell the tank is on* and never asks what is
under it, so a tank standing on an anti-tank — PF-ambiguous, dropped from `table()` — should still seat.
Measured before it went in, at every pitch and every pack: `facings()` is a **strict superset** of the
table's own facings, 0 missing and 0 disagreeing across all twelve tables, and 578 tiles wider at 24 px
internal. So it can only turn a `no tank` into a seat, never a seat into a wrong one.

**The other 38 rows Michal confirms as the tool had them** — the five occluded cells, the nine posts whose
filenames name another level, the eleven re-authored `.lvl`s, the eight posts with no start screenshot,
and the five genuine play states. The table's job on those was to be readable enough to check, and 40 rows
checked in one pass is the answer to whether it is.

**It was not two readers making that assumption, it was four, and the other two asked for nine tile
labels that were all already answered.** `sheet` — the residual pass, the one that says what still needs a
human — reported **9 unlabelled sprites over 36 instances**, and every one of the nine was accounted for
before it was asked about. They split perfectly in two:

| | sprites | instances | what accounts for it |
|---|---|---|---|
| a `.ltg` pack at 32 px | 5 | 32 | `LaserTank` 726 `c` and 1619 `b`, both EyeSaver+Grid, both **banked with `unknown: 0`** |
| stated in `bench/post-fixups.json` | 4 | 4 | `Challenge-V` 727 A7, `LaserTank` 899 I2, 901 D15, `Sokoban-I` 1060 O2 |

The first five are `codebook`'s bug over again, one layer along: `load_codebook` merges the internal sheet
at every pitch the frame gate accepts **and nothing else**, so a tile whose answer lives in a pack was
outside the codebook and therefore "unlabelled" — 28 of the 36 instances are a single tile of 726's. The
other four can never be in *any* hash table: the tank drawn on an anti-tank occludes the cell so
completely that two different `PF` values give identical pixels, which is the whole reason they are stated
per post rather than labelled. **A tile outside the codebook is not the same thing as a tile nobody can
read**, and conflating them is what turned nine answered questions back into a request for work. `sheet`
now accounts for each residual tile before drawing it, writes the accounting into the sidecar, and draws
only what is left — which is nothing, so it writes no `residual.png` at all and says so.

**And `tiles`' own two gates were checking against the wrong table, one of them loudly.** The goal-residual
gate compared each residual hash against the internal 24-px table, so the five pack sprites came back `not
derived` — a foregone answer to a question about a 32-px EyeSaver tile. The start-bootstrapped-codebook
gate had it worse, and admitting 1126 and 1619 is what exposed it: those two boards now *teach*, so 45 of
the codebook's 106 entries are 32-px or EyeSaver hashes, and the gate reported **48 of 106 "not derived"**
in 48 lines of output that said nothing except that it was looking in the wrong place. Checked against the
derivation each tile is actually of (`derived_cell`, a lookup across every `(pack, pitch)` table — safe
for the measured reason that no hash carries two different `PF` values across them), the same run reads
**96 of 106 agree, 7 tank cells reconciled, 0 clash, 3 not derived** — and those 3 are the three
information-free cells that have been known underivable since session 38. The gate costs 17 s instead of
3 s, which is what building the pack tables costs, and the report now carries an open residual sprite as a
`note:` line rather than leaving it in the log.

**The pattern is worth naming, because four readers had it independently.** *Which graphics a screenshot is
of is a property of the screenshot* — the zoom is in the board frame and the pack is in the tiles — and
every reader in the chain has to ask. `origin` and `decode_board` have asked since session 39; `codebook`,
`sheet` and both `tiles` gates were still assuming, each in its own way, and every one of the four
symptoms looked like a *finding about the blog* rather than a bug in the instrument. That is the same
lesson session 40 wrote down about the residual gate that never ran, one level up: a gate that asks the
wrong question is worse than one that does not run, because its answer looks like data.

```bash
python tools/harvest.py complete            # ~65 min, and it rewrites bench/goal-boards.json
python tools/harvest.py complete --report --all
```

### 8. Level 10, one traced run — and it answered a question the item did not ask.

The item was *is level 10 budget-limited or ranking-limited?* The trace settled it and then found the
actual cause in one column: **a barrier on 0 of 63,454 expansions.** The six-column reading, the `best=`
regression, and what each row retires are in
[layer 8's *What has not fallen*](layers.md#what-has-not-fallen-stated-plainly). The design it prescribed
is [Layer 9](layers.md#layer-9--exposure-as-a-tier---own-rung-the-population-pays-the-example-does-not),
which is built, is worth +9 on the GAUNTLET tail, and does not solve level 10.

**Two things about the item rather than the level.** Its own decision rule named the wrong follow-up —
*"if it climbs past 50 and does not win, item 11 applies"* — and item 11 is retired two ways over. **A
decision rule that names the wrong follow-up is still a good rule if the run reports enough to notice**,
which is the argument for tracing rather than sweeping. And `--analyze` had the whole diagnosis for free,
in one second, before the 27-minute run: that is where *run instrument 2 before instrument 4* comes from.

The traced command is banked with its output as **`bench/trace10.err`** — ~1 KB a depth, the only
artefact of a multi-hour run:

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 10 \
  --no-ida --no-beam --push --push-read --push-eval work --read-antitank-wall \
  --push-reach --push-ferry-match --push-ferry-maze \
  --push-dead 20 --push-fire 8 --push-shot-run 16 \
  --push-beam 1024 --push-restarts 0 --max-keys 5000 \
  --push-trace --nodes 400000000 --budget-ms 14400000 --jobs 1 \
  --out build/trace10 2> bench/trace10.err
```

Three deliberate choices in it, all of them the point of the run: `--push-beam 1024` because that is the
width the disputed arithmetic was quoted at, `--push-restarts 0` so the depth column is one search rather
than thirty stitched together, and `--jobs 1` because a trace from sixteen interleaved workers is
unreadable. Level 10's record is 124 + 55 = **179**, so `--max-keys-record` would compute 995 and the
raise-only rule keeps 5,000; the flag is pointless here and the run needed 5,000 regardless — nothing it
emitted came near either number, because it never won.

### 9. `--max-round N` for the driver.

Done; the flag and what it touches are in [`driver.md`](driver.md#--max-round-and---lanes). *(The
unattended overnight run it enables has still not been made.)*

### 10. Wall clock on the push rungs — the memo shipped, and the layer underneath it is declined.

**Closed on 2026-09-15 in session 53 — positive on its build, and negative on what was left over.** The
item asked why the shipped push rung runs at 166k nodes/s against layer 0's 1.4M. The answer is `PushH`
at **41-48% of the expansion**, and the fix is `--push-memo`, a pose-keyed memo worth **1.31x** on the
shipped rung, **1.56x** on the arm the fourth pass runs and **1.33x** on `layer7` — `IDENTICAL` on all
three, reports field for field and every `.lpb` byte for byte, and **on by default since session 51**.
What the item did not get is the rest of the 8.4x: it capped itself below 2x on its own arithmetic and it
was right to. The one piece never built — a second, board-keyed layer underneath the pose memo — is
**declined rather than deferred**, and the reasons are in
[*the second layer, declined*](#the-second-layer-declined--priced-off-the-code-and-not-built) at the
bottom. Everything between here and that section is the record as the three working sessions left it.

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

#### Instrumented in session 49 — and the profiler the item asked for was the wrong one ☑

**Where this sits: in the working tree, unstaged.** `--push-time` is `Push.cs`, `Heuristic.cs`,
`Search.cs` and `Program.cs`, built and checked but not committed, and `build/lasertank-solve.exe` did
*not* have it — item 2's pass held that file open for six days, so the only build of the flag was the
project's own `src/LaserTank.Solver/bin/Release/net8.0/lasertank-solve.exe` (`LT_SOLVE`, the same route
`bor_campaign.sh` takes), rebuilt with
`dotnet build src/LaserTank.Solver/LaserTank.Solver.csproj -c Release`. **That is no longer the situation:
the pass finished on 2026-09-15, `build/` was republished, and both flags are in the published binary.**
The republish was gated against the pass's own banked output rather than against a second binary — five
levels from five collections at the `l8fire` arm's flags and 40M budget, **node-, key-, depth- and
`.lpb`-byte-identical** — so the flags are inert when not asked for and every report in `build/reports/`
still controls the current binary. **Nothing here is blocked on a build any more.** Nothing below is banked in a report: these are single runs kept in
this file, and every one of them was taken beside the pass.

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

#### Then memoise — built and gated in session 50, and the key is not the one this item named ☑

**`--push-memo` is on by default as of session 51, and on the arm the fourth pass runs it is 1.67x.**
Three rungs, the same level list and the same node budget under both arms, `bash tools/push_memo.sh`:

| rung | speedup | memo hit rate |
|---|---:|---:|
| `rung8` — the shipped rung, width 8 | **1.40x** | 98.3% |
| `l8fire` — the pass's arm, width 128 | **1.67x** | 84.8% |
| `layer7` — the one arm with `--push-stop` | **1.42x** | 84.6% |

**This table had `job time` and `nodes/s` columns and they have been struck rather than corrected** —
they reconciled against nothing and their run's reports no longer exist. See
[*the two struck columns*](#the-two-struck-columns-and-why-they-were-not-re-derived) below; the
speedups and hit rates are unaffected, being ratios and counters within a pair, and the seconds this
section needs are in the re-gate table that follows it.

50 levels of `bench/deep-levels.txt` at 400k nodes, four jobs beside item 2's pass. On `LaserTank.lvl`
10 at 6M nodes and one thread the expansion goes
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

**Two reasons that ~1.12x is an upper bound rather than an estimate, both read off the code in session 52
and neither measured.** The 4.2x is 207,139 misses over 50,029 distinct boards — **4.14 misses a board** —
and that ratio is only collectable where the term has no early exit. `BuildFire` and `BuildAlive` have
none and would collect all of it. **The Dijkstra does**: `WorkDistance` returns the moment it settles the
tank's cell, or the first cell of the safe flood under `--push-reach` (`Heuristic.cs:447`), so a table
shared across a board's 4.14 hats has to run to completion. At only 2x for completion the route term's
reuse falls to ~2.1x, and the saving falls from ~2.8 s to ~2.4 s — the rung lands nearer **1.08x** than
1.12x. **And the entry is not a scalar.** The pose cell is 56 bytes; a board entry is `_fire`, `_alive`,
`_cost[256]` and `_pred[256]`, about **2.5 KB** — 45x — and this file's own sizing note records that
16,384 pose slots at 918 KB a worker already cost more in shared cache across sixteen workers than the
1.8 points of hit rate they bought. **So the first thing this build wants is not the build**: one run with
the early exit removed and `--push-time` on, one level, one thread, prices the completion penalty for a
minute of one core, and the slot count has to be swept against the cache the way the pose table's was.

**Two things not to read into the numbers above, as this section stood at the end of session 50.** They
were taken beside item 2's pass, so the seconds are a loaded machine's; the ratios are the point and both
arms carried the same load. And the memo was then **off by default** — flipping it on is a one-line change
and the gate above is the evidence for it, but nothing in these files had been re-measured with it on, and
the seconds in every table above it are the searcher without it. **The next section is that flip**, so the
second caveat is spent; the first is not, and it is why session 51 re-gated on an idle machine.

#### The default flip — done in session 51, and the gate had to be fixed to stay honest ☑

**`PushMemo = true` in `Search.cs`, and `--no-push-memo` is the way off**, following the `--no-ida` /
`--no-beam` convention. `--push-memo` is still accepted and is now a no-op, so every recipe in these
files keeps working unchanged. **`build/lasertank-solve.exe` is republished** and carries the new
default; nothing else in `tools/` mentions the flag, so no other recipe changes meaning.

**The gate needed a one-line fix before it meant anything, and this is the third instrument defect these
files have caught the same way.** `push_memo.sh` built its control arm as "pass no flag" — which after
the flip *is* the memo — so it would have compared the memo against itself and printed `IDENTICAL` while
measuring nothing, exactly like the gate that once passed on an empty directory. The control now passes
`--no-push-memo`, and the `--push-time` lines are the evidence it bites: the off arm prints no memo line
and `pushH` back at **42-47%** of the expansion, the on arm prints the hit rate and **4-17%**.

**Re-gated after the flip, on an idle machine** — the first reading of this bench not taken beside item
2's pass:

| rung | job time, `--no-push-memo` | default | speedup | nodes | nodes/s off → on | verdict |
|---|---:|---:|---:|---:|---|---|
| `rung8` — the shipped rung, width 8 | 48.4 s | 36.9 s | **1.31x** | 16.7M | 345k → 452k | `IDENTICAL` |
| `l8fire` — the pass's arm, width 128 | 93.0 s | 59.5 s | **1.56x** | 18.4M | 198k → 309k | `IDENTICAL` |
| `layer7` — the one arm with `--push-stop` | 60.9 s | 45.9 s | **1.33x** | 17.0M | 280k → 371k | `IDENTICAL` |

Same 50 levels, 13 / 6 / 10 solved, every `.lpb` byte-identical, and the node count identical across each
pair. **Every column of this table is re-derivable from `build/reports/memo-{rung}-{off,on}-best.jsonl`**
by summing `ms` and `nodes` over the 50 rows — checked in session 52, and it is the only table in this
item of which that is true. **The ratios are 0.06 to 0.11 below session 50's** (1.40 / 1.67 / 1.42) and
the ordering is the same, which is the shape to expect when the control is the arm that suffers most from
a loaded machine: session 50 measured beside sixteen jobs, this one measured alone.

**Both alternative explanations for the 166k are already ruled out**, which is why this item is now the
whole of the wall-clock story rather than one of three guesses at it: `--push-eval none` runs at 171k
against `coarse`'s 163k (but see *the two struck columns* below); and `sterile=` is 0.05%, so wasted
expansions are not the cost either. **The 8.4x gap to layer 0 is `PushH` itself** — measured at 41-48% of
the expansion, which is most of what separates the two rungs but not all of it.

#### The two struck columns, and why they were not re-derived

**Session 50's bench table carried `job time` and `nodes/s` columns that reconciled against nothing, and
session 52 struck them instead of recovering them, because they cannot be recovered.** The row said
`rung8` off was **16.8 s at 166,627 nodes/s**, which implies **2.8M nodes**; the same bench re-gated is
**16.7M nodes at 345k nodes/s** over the same 50 levels at the same 400k cap, and
`bench/deep-levels.txt` has not changed since 2026-09-07.

**The explanation this file offered for them is itself wrong, and that is why the columns are struck
rather than relabelled.** It supposed "most likely the single-level `time` split read into a bench row" —
but that split is **34.18 → 24.20 s** at width 8, **53.50 → 31.31** on `l8fire` and **41.46 → 27.16** on
`layer7`, and the struck rows are 16.8 → 12.0, 177.4 → 106.5 and 111.4 → 78.5. Neither the seconds nor
the ratios (1.41 / 1.71 / 1.53 against 1.40 / 1.67 / 1.42) match. The `166,627` *does* reproduce this
item's opening single-level number (6M nodes / 36.2 s = 165,746) to three figures — but `16.8 s` matches
no run in these files, so the two struck columns are not even from the same run **as each other**.

**And the reports that would settle it are gone**: `build/reports/memo-*.jsonl` are overwritten by every
`push_memo.sh` run, and the session 51 re-gate overwrote them on 2026-09-15. So session 50's absolute
seconds have no evidence behind them and are not retrievable by re-reading anything; the only way back to
a number is to re-run the bench, which measures today's binary and not that one.

**Nothing that rests on that table moves.** The speedups and hit rates are ratios and counters within a
pair, the gate verdict that licensed the flip is untouched, and the seconds the section needs are in the
re-gate table above, where they now carry their own node counts. **The rule this is the fourth instance
of: a table of absolute seconds whose reports have been overwritten is a claim, not a measurement** — the
other three are the unattended run's stdin, the gate that passed on an empty directory, and the control
arm that became the memo after the flip.


#### The second layer, declined — priced off the code, and not built

**This is the one part of item 10 decided by reading rather than by running, and it is labelled that way
on purpose.** The sections above leave the board-keyed layer at "~1.12x by the census's arithmetic,
likely nearer 1.08x once the Dijkstra's early exit is paid for", with a build they call the expensive
half. Three things read off the code in session 53 settle it against building, and **not one of them is a
measurement**:

**1. The board key would be free — which is the only argument in its favour, and it is new.**
`MemoProbe` (`Push.cs:203`) already runs both FNV-1a chains over the 256 bytes of `Game.PF`, and the 256
of `PF2` under `--push-stop`, and mixes the tank cell in only at the last step: `k1 = (a ^ t) * prime`.
The pair `(a, b)` *before* that mix is a board key, already computed, on every call. A second layer needs
no second hash pass over the board. Nothing in these files had noticed that, and it is the cheapest part
of the build.

**2. The early exit costs more than the estimate above charges it.** `WorkDistance` returns at
`Heuristic.cs:447` the moment it settles the tank's cell — or the first cell of the safe flood under
`--push-reach` — so a table shared across a board's 4.14 hats has to run to completion, which the
estimate does say. What it misses is the line after it: `RouteObstacles = CountOnRoute(e, c)`
(`Heuristic.cs:449`) is keyed on the cell the search *stopped at*, so it stays per-pose even with a
completed table in hand. The shared table buys the relaxations and not the route walk, which is strictly
less than the 2x-for-completion the ~1.08x already assumed.

**3. The entry is 45x the pose cell's, in the one dimension already measured and already found not to
pay.** A pose `MemoCell` (`Push.cs:175`) is 56 bytes. A board entry is `_fire[256]`, `_alive[256]`,
`_cost[256]` and `_pred[256]` — about **2.5 KB**. The sizing note at `Push.cs:166` is the measurement
that applies, and it was taken for the pose table: 16,384 slots at **918 KB a worker** cost more in the
cache sixteen workers share than the **1.8 points** of hit rate they bought. At 2.5 KB an entry, **256
board slots is already 640 KB a worker** — inside that same band before the layer has returned its first
hit — and 256 slots against 50,029 distinct boards in an expansion is a thin table.

**So the sum is: ~1.08x, for the expensive half of the build, in the footprint that was already shown not
to pay.** `ApplyKey`'s 24%, the read's 20% and the expansion's own 41% are untouched by any of it and are
what cap this item below 2x, exactly as it said of itself from the first table. **The 8.4x gap to layer 0
does not close here**, and this item is not where the tree's next levels come from —
[item 7](next-actions.md#7-1st--the-solved-vs-budget-curve) is.

**The measurement this section chose not to take is on the record too**, because declining one is worth
as much as taking one: one run with the early exit at `Heuristic.cs:447` removed and `--push-time` on,
one level, one thread, about a minute of one core, prices the completion penalty that point 2 argues
about. It was not taken because points 1 and 3 decide the item without it — the layer is cheap to key and
expensive to hold, and **the holding is the half that was already measured**.

**What would reopen this**, so that it is not re-derived from scratch: `PushH` back above ~40% of the
expansion on an arm that ships, which would mean the pose memo's hit rate had fallen off a configuration
it has not been gated on. `bash tools/push_memo.sh time` prints that split for one level a rung and is
the cheap check; `push_memo.sh gate` is the equality half and takes about twelve minutes for all three.

### 11. The closure-dominance prune — retired on its own acceptance test.

The item set its own bar: add `sterile=` to `--push-trace` (expansions that emitted zero fresh
successors), and *if it is tens of percent on the ferry bench*, add `seen.UnionWith(local)` after every
untruncated expansion. The instrument is in (`_pxSterile` in `Push.cs`, counted where `ReadCount`'s
comment says to count — after every derivation has run, so `first` to `next.Count` is the whole of what
an expansion contributed), and the reading is:

| population, 8 levels each at width 128 / 4M / `--jobs 1` | depths | sterile expansions |
|---|---:|---|
| `bench/ferry-levels.txt` | 241 | **8 of 17,273 — 0.05%** |
| `bench/deep-levels.txt` | 193 | **9 of 13,316 — 0.07%** |

**Tens of percent was the bar; 0.05% is the answer, and both lists agree.** The counter is not broken —
it fires 6 times on `Beginner-I` 41 and 2 on 471. **The reasoning is sound and the prune really is
lossless** (a dominated state offers nothing the dominator did not); it simply has almost nothing to
prune, because a closure of ~200-1,000 poses driven and shot from almost always finds *some* board change
that is neither in `seen` nor already in the layer. One fully-closed expansion is rare.

*The reasoning, kept because it is subtle enough to be re-derived otherwise.* Every pose in an
*untruncated* PF-preserving closure is dominated by the node it was expanded from: movement closure is
transitive, so whatever board changes are reachable from pose *p* are reachable from the parent, and the
parent has already emitted all of them. Yet `ExpandPush` adds those poses only to `local`, never to
`seen`. So a later successor whose state is *exactly* one of those poses — same board, tank somewhere
else in the same component — is fresh to the closed set, takes a slot in the width, and pays a whole
closure to emit successors that are all already closed. Push a block right and then, from another side,
push it back; turn a roto-mirror through a cycle while the tank moves between shots; any reversible pair
on a Sokoban level. Two caveats if it is ever revisited: it must be skipped when the closure truncated
(the parent did not finish emitting), and it changes `G` tie-breaks, since the dominated state may have
arrived with a shorter keystream.

**The item's second clause goes too, for a different reason.** Keying the per-board cap on `(BoardKey,
TankRegion)` presumes the cap binds, and on the level it was proposed for it does not: level 10 runs
`front=1024 boards=1024` at every depth past d=2, i.e. **every frontier node is already a distinct
playfield**. Session 18 built `--push-per-board` because width 48 was holding 1 to 9 distinct boards; at
width 1024 with the layer-8 flags that failure mode is gone.

**The instrument stays.** It cost ~20 minutes of machine and it retired a code change on two populations,
which is the cheapest thing in this project's history per line of code not written. It is off by default
and inside `--push-trace`, so no measured number moves.

### 12. Per-level `MaxKeys` from the record — `--max-keys-record`.

`clamp(5 × (ghs_moves + ghs_shots) + 100, --max-keys, 8000)`, the global kept where the record is 0 or
`RecMax`'s 65500. **Raise-only by construction** — the floor is whatever `--max-keys` asked for, so a run
carrying the flag can reach every keystream the same run without it could — and off by default, so no
number moves. It applies in `SolveOne`, so the driver's rungs get it on top of whatever they `Tune` (the
push rungs already hard-code 5,000 for this exact reason, `Auto.cs:296`).

**The cap is real and it is the floor, not the multiplier.** Over the 757 solved rows with a record in
the five rehearsal arms plus `chain.jsonl`, `clamp(5x + 100, 1200, 8000)` covers **755** of the solutions
actually found — and the two it misses are the two that matter: `Challenge-IV` 641, banked by both
`--max-keys 5000` arms at **1,764 and 1,876 keys against a record of 143**, which is 13x its record and
which *no multiplier of it reaches* (12x + 100 = 1,816, still short). A floor of 2,000 covers all 757. So
the record does not predict solution length in the tail, and what the flag is worth is that no level is
capped *below* what its record implies — 1,367 of the chain's 3,691 failures have a record long enough to
lift them past 1,200, and 302 past 5,000, which is where the 8,000 ceiling starts to bind rather than the
formula.

**And it is a confound in item 2's rehearsal table.** `plain`, `enables` and `layer7` ran at the default
1,200 and `l8learned`/`l8work` at 5,000, so two of the five arms **could not have emitted** the only two
solutions in the population longer than 1,200 keys, at any budget. The full run carries the flag on
*every* arm.

### 13. The shot test — a report column, and the rule it leaves to item 4.

**Closed in session 43, and the falsifier had already passed before the build**, which is why this was a
column rather than a question. `report_stats.py` now prints the solution's shot count against the
record's, over any report, and it reproduces [`human-strategy.md`](human-strategy.md)'s measurement 3 to
the digit on `fix2-chain.jsonl` + `gt-fire.jsonl`:

| shots against the record | n | median keys/record | p90 |
|---|---:|---:|---:|
| **above** the record | 67 | **1.85** | **3.20** |
| **exactly** the record | 298 (65.9%) | 1.41 | 1.80 |
| **below** the record | 87 | 1.46 | 2.10 |

**One filter is load-bearing and it is in the docstring**: a record that spends *no* shot is left out
rather than counted as matched, because firing none where the record fires none is not evidence about
the strategy. Including those 127 rows moves the matched bucket 298 → 425 and its median 1.41x → 1.50x,
i.e. it dilutes the statistic into the corpus average — the same shape as a tier that promotes
everything. The column names a population nothing else here reported: *the levels the solver solves with
the wrong strategy*, which is where the ratio tail lives and the one population `Replan.Improve`
provably cannot help.

**What it does not close is the rule inside `--best-of-round`**, and that belongs to
[item 4](#4-the-campaign-that-decided-that---best-of-round-is-a-default--and-the-shot-rule-won-it) rather
than to a fifth item: the shot test and the ratio test disagree on 58 of the 452 rows, and whether
keeping those rounds open pays is a campaign question.

*(Item 4 ran it on 2026-09-14 and **this rule won**, on both populations — 95 keys against the ratio
rule's 53 over the 494 levels the chain solves, 71 against 65 over the 90 deep ones. It is the driver's
default now, as `--best-of-shots`. A column that cost minutes decided a shipped default.)*

### 14. Per-level width from the record — built, swept, and beaten by the global width.

Layer 8's framing arithmetic is `closure x width x board changes` against the node budget, and the
record supplies the third factor for the whole corpus where a hand recording supplies it for twenty
levels. `--push-width-record F` (`Push.cs` `RecordWidth`) sets the push beam to
`remaining budget / (poses x .ghs shots x F)` — **raise-only**, floored at `--push-beam`, capped at
9,600, and reported as `width` on the row. The decision run swept F over the calibration's own
p25 / p50 / p75, because the free half had already measured that the factor spreads over two and a half
orders of magnitude (p10 1.9 / p25 4.6 / p50 14.2 / p75 59.9 / p90 446 over `l8fire`'s 66 solved levels
at a known width of 128) and no single F is therefore *the* calibration.

    LT_SOLVE=$PWD/build/wr/lasertank-solve.exe JOBS=4 bash tools/width_record.sh

138 unsolved short-record GAUNTLETs at 40M nodes, against `build/reports/gt-fire.jsonl` — the same
population, the same arm, the flag off — as a banked control. **232 of 232 solutions through the
two-engine gate, zero divergences.**

| arm | solo | rate | only it | width raised | p10 | p50 | p90 |
|---|---:|---:|---:|---:|---:|---:|---:|
| **fire** (control, global 128) | **85** | **61.6%** | **8** | — | — | — | — |
| f4p6 | 76 | 55.1% | 4 | 136/138 | 566 | **6,820** | 9,600 |
| f14 | 78 | 56.5% | 1 | 129/138 | 381 | **2,667** | 9,600 |
| f60 | 78 | 56.5% | 1 | 108/138 | 203 | **854** | 8,333 |

**No F beats the global width and the loss is not marginal** — **-9, -7, -7** — which is the answer to
the item as written. The greedy union does climb, 85 -> 95 -> 96 -> **97 (70.3%)**, so each arm holds
exclusive levels and by the fourth rule they are portfolio members rather than the same searcher
wearing a flag; but that union costs **three extra 40M passes to buy 12 levels**, which is a worse
trade than the same nodes spent on item 2's arms. The pairwise read is the same story from both ends:
against the control, f4p6 **misses 19 and finds 10**.

**The direction is the finding, and it is the opposite of the one the project keeps measuring.** The
estimate ladders *up* — median chosen width 854 to 6,820 against the global 128 — and it loses. Closed
item 5 had already solved `LaserTank.lvl` 6 from **K = 102 at width 32 on 3.4M nodes** where item 18
measured its horizon of 50 at width 512 on 40M, and its phase table reaches 4 phases at width 32, 2 at
128 and 1 at 512 for the same nodes a phase. **Narrow-and-deep, for the fifth time.** The raise-only
rule that keeps the flag safe is exactly what forbade this run from testing it, so what closed here is
*this direction of* the per-level lever, not the lever. That was item 19, which closed on a tie in
session 47 and took the other direction with it (below).

One level tells the whole story on its own: **`Challenge-IV` 176 is solved by the control in 781,566
nodes**, and the two widest arms still solve it — at **35.7M and 31.6M**, forty times the cost.

**Two artefacts in the reports, and neither changes the reading.** First, **the flag is not
node-identical when it declines to raise**, which `width_record.sh`'s own comment claims: 18 of f60's 30
unraised rows differ from the control in nodes, median **+1,488**, the root pose closure charged to the
budget as the item said it would be. Keys and solved status are identical on every unraised row in
every arm, so the control reading stands and the comment is a shade too strong. Second, **four rows of
the f60 arm are time- rather than node-truncated**: `Challenge-IV` 176, 201, 586 and 1141 all record
`ms ~ 197,600,000` — 55 hours — because the machine slept mid-run and `BUDGET_MS=1800000` tripped, so
they got 4.3M to 36M nodes instead of 40M. All four are unsolved in that arm and three are unsolved in
the control too, so **f60's 78 is a floor short by at most one level** and no arm ordering moves.

**What it leaves behind.** The flag ships off by default and costs nothing when it is off. The free
calibration stands as the reason not to try solving for a width exactly. And the three reports are a
banked population for item 19 to be read against without re-running the control — which is what it
did.

### 15. `--push-seed` — the editor trick as an instrument, and level 6's horizon.

**Closed in session 43. It was built to be item 5's missing cheap falsifier and it is now item 5's
target.** `--push-seed FILE.lpb:K` replays a recording as far as its K-th board change and searches from
there; the recipe and the four things to know about reading it are in
[instruments 3b](instruments.md#3b-ask-how-far-from-the-end-the-search-can-finish----push-seed).

**Level 6 "Cascade", 168 board changes, at 40M nodes and width 512** — the flags the fourth pass's ferry
arm carries:

| K | board changes left | solved | nodes | wall |
|---:|---:|---|---:|---:|
| 144 | 24 | **yes** | 3.8M | 22 s |
| **120** | **48** | **yes** | **36.1M** | 220 s |
| 117 | 51 | no | 40M (budget) | 225 s |
| 114 | 54 | no | 40M | 234 s |
| 108 | 60 | no | 40M | 249 s |
| 96 | 72 | no | 40M | 255 s |
| 72, 48, 24, 0 | 96 … 168 | no | 40M each | ~230-265 s |

- **The horizon is 48 board changes and the boundary is one change wide.** 48 left finishes on 90% of the
  budget; 51 left does not finish on all of it. That is the first *direct* measurement of the quantity
  every layer since 5 has attacked — `basin.py` measures how far uphill a line goes and `--push-line`
  measures where a line is lost, and neither says how long a suffix the searcher can actually close.
- **Monotone at every K, so there is no ranking hole to chase.** The item said a non-monotone reading
  would itself be the finding (a longer suffix solving where a shorter one does not means the line
  crosses a board the key hates); it did not happen on this level.
- **The cost of a suffix is exponential in its length, and now it has a rate.** 24 changes cost 3.8M
  nodes and 48 cost 36.1M — 9.5x for twice the suffix, i.e. roughly a doubling every 7 board changes, on
  two points. That is the arithmetic behind *depth is the binding constraint*: the gap between 48 and
  168 is not a budget anyone buys.
- **A seeded win is hint-assisted and the code enforces it**, exactly as `--goal-board` does: the default
  output moves to `<out>-hint`, every report row carries `hint=push-seed:K`, and the number is never in
  the solver's rate. The `.lpb` is an ordinary file — `EngineSnapshot.Keys` carries the replayed prefix
  through `Restore`, so it replays from the level start and **all three seeded wins went through the
  two-engine gate**.
- **K = 0 is node-identical to the same run without the flag.** Checked rather than assumed, because a
  seeded root that is not the ordinary root at K = 0 would make every other row unreadable — layer 4's
  equivalence test used the way session 25 wished it had been.
- **What it leaves open is one question, and it is now [item 18](history.md#18-the-horizon-per-level--a-level-property-and-not-a-searchers-reach):**
  is 48 the *searcher's* reach or *this level's*? The item ran one level because that is the level in
  front of the project; the same bisection over the other 19 hand recordings decides which quantity was
  measured, and item 5's design depends on the answer either way.

### 16. Four derivations the read did not have — all four measured.

**Closed across sessions 42 and 43, and the split is two shipped, one refused, one inverted, plus a
fifth derivation the run turned up and refused.** It was one item because all four were `--read-dump` or
`--analyze-tsv` questions before they were tiers, which is *a distribution before a solved count* obeyed
at the cheapest place on the list. Session 42's two — rarity **3.15x**, spend **0.04x** with the
anti-tank kill exempted, and `clears` at **0.88x**, refused — are in
[session 42](#session-log); the other two are here.

**Third: FMO mobility as a quantity — it predicts, and the sign is the opposite of the human claim.**
The series says *"it is generally easy to win if you have enough FMOs (except they are too crowded)"*.
`_alive[c]` was the one-push boolean; `Heuristic.Mobility` is its transitive closure — a flood over block
*positions* using `BuildAlive`'s own test — reported as `--analyze-tsv`'s `alive` / `mob_max` / `mob_sum`
and cheap enough that the 4,185-level stride sample still takes 63 s. Joined against `chain.jsonl`:

| `mob_max`, the freest block's area | levels | solved | rate |
|---|---:|---:|---:|
| 0 (frozen) | 656 | 105 | **16.0%** |
| 1-4 cells | 1,178 | 135 | 11.5% |
| 5-8 | 269 | 11 | 4.1% |
| 9+ | 1,573 | 70 | 4.5% |

- **Freer blocks are *harder*, not easier**, and the reason is that the claim and the measurement are
  about different players: a free object is a resource to a human and a **branching factor** to a beam.
  Layer 5 searches board changes, so a block with a sixty-cell area is sixty successors at every depth.
- **It is not board openness in disguise, and that is the measurement that decides it.** Holding the
  record's own length, the water count and the block count fixed and splitting each stratum at its
  median, `mob_max` separates **9.5% / 5.3% = 1.78x**, while `poses` — the openness column, which
  predicts just as well unstratified — collapses to **1.17x**. The control those strata are built on is
  the one quantity here nobody derived: `.ghs`'s own move+shot count, which alone runs 82.0% at ≤20 down
  to 0.2% above 250.
- **The shape is a cliff at four cells, not a gradient.** Above 8 the column is flat (4.1%, 4.5%), so
  what predicts is *whether the blocks are penned in* rather than how far they can go. Crowding
  (`mob_sum / blocks`, the series' own caveat) adds nothing beyond it (1.56x), and `alive` alone is
  weaker than the area (1.69x against 2.09x under the same control) — which is what makes the flood
  worth its scan over the boolean it extends.
- **The flood is a lower bound and says so**: water consumes a block, a tunnel teleports it, and ice and
  a belt move it on, so none of the four is expanded from. Expanding through them instead was measured
  and thrown away — it reports **235 cells of 256** for one block on `LaserTank.lvl` 2, so the column
  stops discriminating exactly where the belts *are* the level. Reconciled by hand on `LaserTank.lvl` 1:
  five blocks, two movable, one cell each, and all five agree with the board and with `_rayOk`'s rule
  that water is not passable — which is what freezes three of them.

**Fourth: a per-carry constant in `MatchFerry` — refused.** The series prices a ferry in *units* of six
moves; `MatchFerry` sums push distance, so two carries of five cells score the same as one of ten. The
falsifier was an offline sweep: `Heuristic.RouteHoles` (the carries the term priced) is now a
`--profile` column, and `basin.py --carry-cost K` adds K per carry to the ranking key without re-running
the solver. Swept K = 0, 1, 2, 3, 4, 6, 8, 12, 20, 40 over the 20 hand recordings, profiled under the
ferry arm's own flags (`--push --push-read --push-reach --push-ferry-match --push-ferry-maze
--push-dead 20`, which is what the instrument bug below was hiding):

- **No level's ascent moves at any K** — all 20 identical, p50 12 / p90 32 / max 34 board changes, level
  6's 20 unchanged. **A per-carry constant is a constant inside a carry**, and the ascent that defeats
  the beam lives inside a carry: the drop at a fill is *already* in the term, because filling a hole
  removes its whole matched distance. That is *a penalty every board pays is not a penalty* in its
  per-phase form, and the fourth time this project has paid for that rule.
- **Three levels get measurably worse, and they say why.** The deepest rise grows with K on
  `LaserTank.lvl` 13 (40 → 80 at K = 40), 20 (32 → 72) and 24 (6 → 29), because the hole count is **not
  monotone along a human line** — the settled route re-crosses water and the count goes 1 → 2 or 2 → 3,
  which a constant amplifies into a rise. The same signature `--push-shield`'s constant had at 40: no
  ascent bought anywhere and a deeper rise somewhere.
- **The sweep found an instrument bug on the way in, and that is the reusable part.** `--profile` built
  its `ferry` column from whatever the heuristic's `Want*` flags happened to be — never set on that path,
  so always the *per-hole* estimate, whatever `--push-ferry-match` said on the command line. `PushH`'s
  configuration is now `Solver.WantsFromOptions` and both callers go through it. **A profile is only the
  beam's view of a line if it asks for the beam's derivations**, and this is the fourth outing of *the
  instrument measured the wrong quantity*.

### 17. The rarity tier — built, and refused on the bench it set itself.

`--push-rare`, off by default: item 16's first derivation promoted to `TierRare`, in front of all three
of the read's own. The item's whole case was that `rare` is **the most selective and the most accurate**
of the four — 5.1% of the successors offered, 16.0% of what the human does, **3.15x** against 1.34-1.43x
— which is exactly the condition `Push.cs:798` states for where a tier belongs. It was the only open item
whose falsifier was already run, so what was left was a build and four benches. The build is ~40 lines
and the benches are the answer:

| bench, 4M nodes on `--no-ida --no-beam --push --push-read` | control | `--push-rare` | exclusive to the tier | lost |
|---|---:|---:|---|---|
| `bench/ferry-levels.txt`, `--read-rare 2` (default) | **18/50** | **16/50** | none | 1026, 1726 |
| `bench/deep-levels.txt`, `--read-rare 2` | **25/50** | **24/50** | 616, 903, 1214 | 1026, 1575, 1726, 1803 |
| `bench/ferry-levels.txt`, `--read-rare 1` | **18/50** | **18/50** | none | none |
| `bench/deep-levels.txt`, `--read-rare 1` | **25/50** | **25/50** | 616 | 1803 |

**The control reproduced 18/50 and 25/50 exactly**, which is what makes the rest of the table mean
something: the gate was ordered control-first for this reason and it paid. At the default threshold the
tier is **−2 on ferry with zero exclusive levels** — dominated outright — and **−1 on deep**. At `1` it
is inert on ferry (the same eighteen levels, not merely the same count) and on deep it swaps one level
for one. The item's own stop rule was *"if ferry/deep do not move this item ends there and says so"*, so
step 4 — the driver ladder and a stride campaign — is **not earned** and was not run.

**The 3.15x is not wrong, and that is the finding.** The derivation predicts the human's move; it does
not order a beam. This is layer 4's result arriving a second time from the other side — the winner's
state is in the expansion's output 97.6% of the time and *the sort loses it* — and item 17's prior said
so in as many words (*"a tier is a different instrument from a ranking"*). What the table adds is the
mechanism, and `--push-trace`'s new `rare` column shows it: the tier's selectivity on the *bench* is
nothing like its selectivity on the twenty hand recordings. On `Beginner-I` 191 it names **2%** of
successors; on 41, 326, 366 and 431 it names **0%** at the default threshold, because the census is of
the authored board and a ferry level is authored with three or more of everything a ferry push touches. A
derivation measured on twenty hand-picked `LaserTank.lvl` recordings was promoted over a population whose
boards do not have rare elements on the route. **The 3.15x was a property of the recordings, not of the
levels the chain fails** — and no amount of correctness in the promotion rule fixes a partition that is
empty.

**One number in the table looks like an opportunity and is the one rule this project has already paid
for.** `ctl-deep ∪ rare-deep` is **28 against the control's 25**: three exclusive levels, i.e. the
signature of a complementary arm. It is not one. SOLVER.md's first rule names this exact reading —
*"a bench over-reports **complementarity** as readily as strength"* — and the precedent is `--push-eval
none`, which measured +6 on the ferry union and +4 on the deep one, was on that basis the best arm of the
fourth pass, and came back over 255 corpus levels as **the weakest of six with one exclusive level**.
Three exclusive levels on fifty is below what that one scored. Proposing an arm on it would be spending
item 2's machine time on the one measurement the rules say not to trust.

**The code stays, off by default**, for the reason the heading of this file gives: a negative result that
is deleted gets re-run. `--push-rare` and `--read-rare N` together reproduce the whole table, and
`RarePush` calls the same `RareOfDelta` that `--read-dump` scores the human's move with — the tier and
its measurement cannot drift apart, which is checked rather than asserted (`--read-dump` over
`build/demos.txt` still prints 411/8,099 offered, 128/800 named, 3.15x).

**What it costs, and it is the one price worth knowing.** `TierRare` at 0 shifts `TierAdvance`..`TierLost`
up by one, so **the number `--push-line` prints in its tier column moved** — `advance` is now 1 and a pose
is 6. Nothing sorts on the absolute value (`Cut`, `PushCut`, `Macro`, `Restart` and `Line` all compare
tiers), and `--push-read off` still emits every board change at one tier with poses behind it, so no
measured number moves. This is the second time a tier insertion has cost that column; the first was
`TierFire` at 3 in session 31.

### 18. The horizon per level — a level property, and not a searcher's reach.

Closed item 15 measured one number on one level: `LaserTank.lvl` 6 finishes from **48** board changes out
and not from 51, at 40M nodes and width 512. This ran the same measurement over all 20 hand recordings,
`tools/horizon.py`, K = 0 first on every level and a bisection only on the levels that fail from the
root. **96 probes, 43 of them wins, 45 of 45 output directories through the two-engine gate.** Every row
is hint-assisted and stamped `hint=push-seed:K`, so none of it touches the solver's rate.

    python tools/horizon.py            # the sweep, resumable from its own reports
    python tools/horizon.py status     # the table, free, no solver

| level | board changes | horizon | Kmin | horizon / changes |
|---:|---:|---:|---:|---:|
| 6 | 168 | **50** | 118 | 0.30 |
| 1 | 50 | 38 | 12 | 0.76 |
| 18 | 53 | 28 | 25 | 0.53 |
| 28 | 105 | 27 | 78 | 0.26 |
| 23 | 27 | 21 | 6 | 0.78 |
| 5 | 22 | 16 | 6 | 0.73 |
| 14 | 31 | 16 | 15 | 0.52 |
| 10 | 53 | 13 | 40 | 0.25 |
| 2 | 32 | 9 | 23 | 0.28 |
| 17 | 33 | 4 | 29 | 0.12 |
| 19 | 15 | 4 | 11 | 0.27 |
| 9 | 27 | **2** | 25 | 0.07 |
| 3, 4, 7, 8, 11, 13, 20, 24 | 10-52 | *whole recording* | 0 | 1.00 |

**The answer is the item's second branch and it is not close.** The 12 horizons the search bounds run
**2 to 50 — 25x**, where the item's own criterion for *searcher constant* was "inside 2x". Item 18's
third candidate answer goes too: it is not a constant *fraction* of the line either, 0.07 to 0.78, an
11x spread. And it is not a function of the recording's length, which is the cleanest single refutation
because two levels supply it directly: **`LaserTank.lvl` 9 and 23 both have 27 board changes and their
horizons are 2 and 21**, 10.5x apart; 10 and 18 both have 53 and give 13 and 28. So the constant is
level 6's own, the lever for item 5 is not a fixed phase size, and item 14 — width per level — is the
mechanism the spread points at.

**The finding that reframes item 5, and it inverts what the number looked like.** Level 6's 50 is the
**deepest** horizon measured anywhere on these levels, and level 8 solves its whole 52-change recording
from the root. The search's reach on level 6 is therefore the *best* it achieves, not the worst; what
defeats the level is that its line is **168 changes, 3.4x that reach**. Item 15's 48 read like a short
leash and it is the opposite. What that kills is any decomposition sized from a global constant: the
phases a level would need — its changes over its own horizon — run from **1.3 on level 23 to 13.5 on
level 9**, so a fixed phase *count* is as wrong as a fixed phase *length*.

**Item 15's number is refined rather than corrected: 48 → 50.** Item 15 bracketed the boundary with two
probes, K = 120 (solves, 36.1M nodes) and K = 117 (fails at 40M), and called it one board change wide on
the strength of the monotonicity. The bisection settles it at exactly one: **K = 118 solves on 36.75M and
K = 117 fails on 40M**. The re-run reproduced item 15's K = 120 at **36,073,877 nodes** against its
recorded 36.1M, which is the cross-check that the two sweeps are the same measurement.

**And the sizing input item 14 wanted is not there, which is the useful negative.** The horizon is the
quantity that would justify a per-level width, and it costs ~7 probes at 40M nodes to measure — 20,914
levels of that is not a thing anyone runs, so item 14 needs a *derived* predictor. Joined against every
column `--analyze-tsv` produces (n = 12, so read these as leads and not as fits): the two positives are
`changes` +0.64 and `water` +0.62, and `changes` is already refuted above by the two same-length pairs;
`poses`, `region`, `mob_max` and `mob_sum` are flat (+0.06 to +0.08), which is worth knowing because
`poses × width × changes` is exactly the arithmetic item 14's calibration used. **The one lead with a
mechanism is negative**: `effects` and `shots` — board changes on offer at the root — come in at
**−0.41**, i.e. the more the root offers, the shorter the reach. That is a branching-factor reading and
it agrees in sign with closed item 16's mobility column, where freer blocks are *harder*. Two independent
derivations pointing the same way is the only thing on this list worth following.

**The harness cost two defects and they are the same defect twice: in-memory bookkeeping believed over
the banked state.** First, four probes were run into one `--report` file; `StreamWriter(append: true)` is
not atomic across processes on Windows, two solvers wrote at the same offset, and **a lost row read as a
failed probe** — the worst possible direction, because the horizon is the smallest K that *solves*, so a
lost row shortens the answer. Probes now never share a file and a probe that leaves no new row raises
instead of counting as a failure. Second, phase 2's work list was built from what the running process had
classified rather than from the reports, and an interrupted invocation silently dropped **three levels
that were already measured and failing** (17, 18, 19). Both were caught by the same habit — the table did
not add up to 20 — and both are the file's own rule arriving from a new direction: *the report is the
state, so ask the report*.


### 19. The narrow beam — run on a population at last, and it ties.

**Closed in session 47, and it is the first of these items to close neither positive nor negative.**
Item 14 swept the beam *wider* because `--push-width-record` is raise-only and lost every arm; this is
the one arm in the other direction, `--push-beam 32`, over the same 138 short-record GAUNTLETs at 40M
against the same banked `gt-fire` control. `tools/gtw_b32.sh` is the recipe — the arm, the gate, the
five-arm union and the long-record split in one script, logging to `build/reports/gtw-b32-run.log`.

    bash tools/gtw_b32.sh

**It ran beside item 2's pass at `JOBS=4` against the pass's 16 on 20 cores, and the contention
prediction held exactly**: 100 min wall, 4.8 h of job time, **84 of 84 solutions through the two-engine
gate**, all 54 failures stopping on `budget` and the worst level taking **501 s against `BUDGET_MS`'s
1,800 s**. No row is time-truncated, which is what closed item 14's sleeping-machine artefact had made
worth checking rather than assuming.

| arm | width | solo | rate | only it | greedy union |
|---|---:|---:|---:|---:|---|
| **fire** (control) | 128 | **85** | 61.6% | 1 | 85 |
| **b32** | **32** | **84** | **60.9%** | **4** | +4 → 99 |
| f4p6 | 6,820 median | 76 | 55.1% | 2 | +10 → 95 |
| f60 | 854 median | 78 | 56.5% | 1 | +1 → 100 |
| f14 | 2,667 median | 78 | 56.5% | 1 | +1 → **101 (73.2%)** |

**Of the three outcomes the item wrote down in advance, the middle one landed — the tie.** 84 against
85, where the three widening arms lost by 7 to 9, so `--push-beam 128` is **not** mistuned and the cheap
follow-up at 64 that a win would have licensed is not licensed. Head to head the two widths solve **76 in
common, 9 only to the control and 8 only to `b32`**, which is two searchers rather than one setting: the
five-arm union is **101 of 138 (73.2%)** against item 14's four-arm 97, and `b32` is the only arm besides
the control to hold more than two exclusive levels. **Width is a portfolio axis in both directions.**

**The prediction the item was actually written on failed, and that is the load-bearing half.** Four
independent measurements had licensed something specific — a narrow beam should hold the **deep** levels,
the ones whose record is long, and drop the shallow ones. It does not. `b32`'s 8 exclusive levels split
**4 long / 4 short** about the population's median `ghs_shots` of 13, at a median of 13.5 against the
control's exclusives at 13.0. So whatever decides a level between width 32 and 128, **it is not the
length of the record** — and `budget / (poses x ghs_shots x F)`, the free per-level estimate item 14
built and the one thing that survived its negative, is therefore not the way to size width in the
narrowing direction either. Dropping the raise-only rule in `Push.cs` `RecordWidth` has nothing pointing
at it any more. That is the second time this population has refused a per-level width, now from both
sides.

**What is real is the cost, and it reproduces level 6's result on a population for the first time.**
On the 76 levels both solve, `b32` is cheaper on **46** and its median is **1,032,239 nodes against the
control's 1,614,910**. Four of its eight exclusive wins land in **1.8M to 3.4M nodes on levels the
control burned the whole 40M and failed** — `LaserTank` 751 *Around The Maze* at 1,785,103, `Beginner-I`
686 *NO IMPOSSIBLE IV* at 2,612,594, `Challenge-I` 1216 *Spin Cycle* at 2,681,446, `Challenge-III` 1221
*subtropicale* at 3,399,185. That is the same shape as closed item 5's **K = 102 at width 32 on 3.4M
nodes**: when narrow works it works at roughly a twelfth of the budget. Routes are a shade worse for it,
`ratio` p50 **1.71 against 1.63**.

**So the follow-up is a budget question, not a width question.** Two arms that tie at 40M and separate
by 1.6x in nodes on the levels they share are two arms that do **not** tie at 4M or 10M, and that curve
is [item 7](next-actions.md#7-1st--the-solved-vs-budget-curve) rather than a new item. The five reports
are banked, so any budget rung can be read against them without re-running a control.

**What it leaves.** `--push-beam 32` is not a new default — 84 is not 85. It is a **second arm** on a
population where the union says arms are worth more than settings, and it costs a twelfth of the budget
on the levels it wins, which is the only reason to prefer it to `f4p6` as the union's second member
despite `f4p6` contributing +10 to it. **Narrow-and-deep has now been measured six times and this is the
first time it was measured on more than one level: it is a real axis and it is not a better global
width.**

## The corpus through the read

`--analyze-tsv` over all **20,914** levels, ~3 minutes and no search (`build/reports/analyze-corpus.tsv`;
the loop is in [`instruments.md`](instruments.md)). The last column joins against the stride sample's
solved set.

| verdict | levels | share | `barrier == 0` | solved, stride sample |
|---|---:|---:|---:|---:|
| FERRY | 10,466 | 50.0% | 0% | 5.3% |
| **GAUNTLET** | **3,318** | **15.9%** | **100%** | **19.1%** |
| DEMOLITION | 2,309 | 11.0% | 0% | 12.3% |
| SETUP | 1,906 | 9.1% | 0% | 10.2% |
| RIDE | 1,616 | 7.7% | 100% | 20.2% |
| SOKOBAN | 742 | 3.5% | 0% | 6.2% |
| OPEN | 379 | 1.8% | 95.5% | 96.6% |
| WALLED | 178 | 0.9% | 100% | 12.5% |

It confirms *half the corpus is a Sokoban* independently (FERRY 50.0% + SOKOBAN 3.5%). And **26.2% of the
corpus — 5,474 levels — has an empty barrier set at the root**, so level 10's condition is shared by a
quarter of everything.

**But read the last column before concluding anything from that: the `barrier == 0` levels are solved at
25.2% against 7.0%.** Taken at face value that says an absent read is an *advantage*, and it would sink
the diagnosis behind Layer 9.

**It is a record-length artifact, and the split says so.** Of the 664 GAUNTLETs in the stride sample, 127
are solved and 537 are not — and the **median `.ghs` record of the solved ones is 12 against 138 for the
unsolved.** The barrier == 0 population is bimodal: a large easy half the beam solves before ranking
matters at all (OPEN is 96.6% and is almost the definition of that), and a tail whose records are an
order of magnitude longer. So the higher rate is *which levels are in the bucket*, not evidence that the
read's absence is harmless — and **537 unsolved GAUNTLETs in a 1-in-5 sample, 138 of them with a record ≤
60**, is the population a fire tier would be for.

**Where level 10 sits in its own shape, since one level is not a population:** 10 threats, the **87th
percentile** of GAUNTLET threat counts, at a median pose count. A heavily-covered GAUNTLET rather than a
typical one — consistent with exposure being the binding quantity, and also the reason not to over-read
it.

*Worth the paragraph because the naive reading of 25.2%-vs-7.0% would have killed a live hypothesis and
the naive reading of 26.2% would have oversold it.*

### The original per-tier attribution, kept as history

The 150k per-pass curve as first measured. Its per-pass columns are the pre-`4765ae9` searchers and two of
them no longer exist under those names, so it does not rebase onto the shipped chain — the current table
is in [`SOLVER.md`](../../SOLVER.md#status).

| tier | levels | layer 0 | + L1 pass | + L2 | + L3 | + L4 | median ratio |
|---|---:|---:|---:|---:|---:|---:|---:|
| Kids | 960 | 303 (31.6%) | 319 | 339 | 341 | **359 (37.4%)** | 1.6× |
| Easy | 2,118 | 84 (4.0%) | 89 | 94 | 95 | **104 (4.9%)** | 1.7× |
| Medium | 784 | 7 | 7 | 7 | 7 | **8 (1.0%)** | 1.6× |
| Hard | 257 | 0 | 0 | 0 | 0 | **0** | — |
| Deadly | 56 | 1 | 1 | 1 | 1 | **1** | — |
| **all** | **4,185** | 395 (9.4%) | 416 | 441 | 444 | **472 (11.3%)** | **1.6×** |

---

## The state of the tree and of `build/`

**`build/lasertank-solve.exe` is current again as of session 51, and the republish was gated against the
fourth pass's own output.** Item 2's pass held the published binary from 2026-09-10 to 2026-09-15 and
four solver commits landed behind it while it ran (`ea48d3a`, `0bfc16a`, `cd8a065`, `d3fab4a` — the last
two are item 10's `--push-time` and `--push-memo`, in `Push.cs`, `Heuristic.cs` and `Search.cs`), plus one
`LaserTank.Core` commit (`70a4782`, the i18n move of the rank names out of `GameState.cs`). **So the
binary that produced the 1,087 no longer exists**, and the check is not against it but against what it
wrote: five levels from five collections re-run on the rebuilt binary at the `l8fire` arm's own flags and
40M budget are **node-identical, key-identical, depth-identical and `.lpb`-byte-identical** to the pass's
banked rows —

| level | pass: nodes / keys / depth | rebuilt | `.lpb` |
|---|---|---|---|
| `Beginner-I` 1341 | 219,779 / 93 / 93 | same | identical |
| `Beginner-II` 511 | 250,506 / 24 / 24 | same | identical |
| `Challenge-II` 1591 | 214,003 / 57 / 57 | same | identical |
| `Challenge-IV` 371 | 206,438 / 64 / 64 | same | identical |
| `Challenge-V` 406 | 245,551 / 18 / 18 | same | identical |

— so **`chain5.jsonl` and every report in `build/reports/` still stand as controls against the current
binary**, and both of item 10's flags really are inert when not asked for. Evidence in
`build/gatecheck/`. **This is the check to run every time `build/` is republished behind a pass**, and it
is better than the old one: the pass's banked output is a stronger control than a second binary, because
it is the thing the numbers are actually quoted from.

**`build/lasertank-solve.exe` was also current as of session 46**, and the four sessions it spent behind
the tree are why the side-build habit exists. Item 2's pass held the published binary open from
2026-09-10 — `dotnet publish -o build` cannot replace a running `.exe` — so sessions 42-45 built to
`build/item16/` and `build/wr/` instead and drove them through `$LT_SOLVE`, which is what the three
runner scripts take that variable for. With the pass stopped in session 46 the tree republished cleanly
and **every flag added since session 42 is now in the default binary**: `--read-rare` and the five
`--read-dump` columns, `--analyze-tsv`'s `alive` / `mob_max` / `mob_sum`, `--profile`'s `holes` column,
`--push-seed FILE.lpb:K`, `--push-phases` and `--push-width-record`.

**That rebuild was the first one that could have moved a measured number, and it was checked.** Every
earlier side build touched solver code only; `a4eac84` (session 46, the game's undo fix) changed
**`LaserTank.Core`**, which the solver shares with the engine — a death now clears the pending key
buffer (`RB_TOS = Game.RecP`, `Engine.cs`), the line the port was missing. Over `LaserTank.lvl` 3, 4, 7,
11 and 20 at 4M on the `l8fire` arm the rebuilt binary is **node-identical and keystream-identical** to
the pre-fix `build/wr/`, so **every report in `build/reports/` still stands as a control against it**.
The reason is structural — the search keeps no path through the death handler — but a shared-engine
change is the one thing that could invalidate the whole bank at once, so it gets the five-level gate
every time `LaserTank.Core` moves, not an argument.

**Nothing in the shipped search moved in sessions 42 or 43 either, and that was checked rather than
asserted.** `--from 1 --to 12 --nodes 400000` was identical from both binaries; `Enumerate`, `AnalyzeAt`
and `Mobility` are reached only from `--analyze` / `--read-dump`, `RouteHoles` is written by
`CountOnRoute` and read only by `--profile`, `WantsFromOptions` is `PushH`'s own assignment block moved
verbatim, and `_seed` is null unless `--push-seed` is given (`--push-seed FILE:0` is node-identical to no
flag).

**Sessions 34 and 35 added four files between them and touched no engine and no solver code.** Session 34:
`tools/harvest.py` (the blogspot harvester: `index`/`map`/`fetch`/`codebook`/`sheet`/`label`/`decode`),
`tools/png.py` (a stdlib PNG reader and writer, needed because this machine has neither PIL nor numpy)
and **`bench/goal-tiles.json`**, committed, holding one entry. Session 35: **`tools/sprites.py`** (the
game's own sheet and every cell it can draw) plus a `tiles` subcommand and a tank-aware `decode`.
Nothing under `src/` changed in either, so **the standing engine claim and every solver number are
untouched** — no gate was re-run because nothing a gate covers moved.

* **The codebook now has three halves, and the third one retired the other two's split.** A blogspot
  *start* screenshot labels its own 256 tiles, because the corpus already knows that board — so that
  codebook is derivable and lives in gitignored `build/harvest/codebook.json` (55 entries). Session 34
  reasoned that nothing labels the states only *play* produces, making those hand input for
  **`bench/goal-tiles.json`**. Session 35 found that the **game** labels them: `tools/sprites.py`
  composites `original/src/Game.BMP` + `Mask.BMP` the way `UpDateSprite` does and derives 4,108 cells,
  which covers all 116 residual sprites and needs no `bench/` file at all. `load_codebook()` merges all
  three on every read, lowest precedence first, and prints which is which.
* **What is left in `bench/goal-tiles.json` is one now-redundant entry**, kept as the place anything the
  sheet genuinely cannot draw goes — so far one tile where a screenshot caught the tank between the mask
  blit and the sprite blit. Session 34's worked example, `4776b082c1cedd55` → `.` (a block pushed into
  water, `Engine.cs:731`), is now derived independently and agrees.
* **The numbers the derivation is gated on**, all from `harvest.py tiles`: **0 unknown tiles of 44,288
  over 173 goal boards** (session 34's figure for the same boards was 1.37%), the tank located on
  **173 of 173**, the start-bootstrapped codebook reproduced **53 of 55 with 0 clashes** (one tank cell
  reconciled, one capture artifact not derivable), and `decode --check` clean on **141 of 141** start
  boards.
* **Artefacts in `build/harvest/`** (gitignored, all re-derivable): `index.jsonl` (6,218 posts), `img/`
  (~314 screenshots), `fetched.json`, `codebook.json`, `residual.{png,json}`. Rebuilt from nothing by
  `index` (46 s) + `fetch --limit 150 --goals` (~7 min) + `codebook --goals` (~45 s). The derived tile
  table is not in that list because it needs no fetch: `tiles` builds it from the repo in half a second,
  which is why it is not cached anywhere.
* **`data/` is unchanged** — a harvested goal board is hint-assisted and no decision has been taken
  about where such boards are allowed to live, so neither session banked one there.

**Everything below is as session 31 left it.** `build/` is current (`bash src/build.sh`) and carries
`--push-fire-tier` and the tenth rung.

* **What the code is:** `Heuristic.FireSwept`/`FireCells`, `Push.FireTier` + `TierFire` + `Node.Swept` +
  the `fire:` trace line, the flag in `Program.cs`, and a `push-fire` rung in `Auto.cs`'s ladder — **so
  the driver runs ten searchers per level, not nine.** ~190 lines, ~30 of them not comment.
* **Everything is off unless asked for, and that is checked rather than argued.** With
  `--push-fire-tier` absent, a traced `LaserTank.lvl` 1 run is **byte-identical** to the previous build,
  depth line for depth line. The tier numbering shifted (a `TierFire` was inserted at 3 and
  `TierOther`/`TierPose`/`TierLost` moved to 4/5/6), which reorders nothing — `Cut()` sorts on the value —
  but it *does* change the number `--push-line` prints in its tier column.
* **Artefacts.** `bench/gauntlet-tail.txt` (the 138-level population, rule in its header) and
  `tools/gauntlet_tail.sh` are in git; `build/reports/gauntlet-tail.jsonl`,
  `build/reports/gt-{base,fire}.jsonl` and `build/gt/{base,fire}` (161 gated solutions) are in `build/`.
  `build/trace10-fire.err` is the 37m47s level-10 trace with the tier on — it is *not* in `bench/` beside
  `trace10.err`, because unlike that one it settles nothing a re-run would not settle again.
* **`data/solutions/LaserTank/00009.lpb` moved, and it moved the right way** — 127 keys → **115**,
  re-derived rather than restored.
* **Gates re-run after the change:** `verify_solutions.py data/solutions` → 8/8, both engines agreeing on
  every tick; `replay_all.py` → 187 replayed / 181 win / 6 documented non-win, 0 unexpected;
  `test_difftrace.py` → 29 passed; `sweep.py` → 2,347/2,347 identical. `test_fuzz.py` was not re-run — it
  rebuilds the core and nothing in the session is in the core. **`Engine.cs` and `Engine.Search.cs` are
  untouched**, so the standing claim holds at ten layers.
* **Nothing is committed.** Michal writes the history.

**Two traps in this tree that have each bitten twice.**

- **A side publish dir.** A running solve holds `build/lasertank-solve.exe` open, so
  `dotnet publish -o build` cannot replace it. Use `LT_SOLVE=<exe>` with the three runner scripts where it
  fits, and a scratch `build-*/` where it does not. `/build-*/` is now in `.gitignore`; `/build/` always
  was.
- **`core.autocrlf` is `true` on this machine**, against the README's instruction to set it false, so a
  tool that rewrites a whole file through Python's text mode converts it to CRLF while `git diff` goes on
  looking clean — and a file that is CRLF on disk against an LF blob produces a whole-file diff from an
  edit that preserved its bytes. **Check `git diff --stat` after any scripted edit, not just the
  content.**

**Report and solution names that changed meaning mid-history**, because a number quoted against them
rebases:

- `build/reports/` is built under the recipe's own names: `l0.jsonl`, `l3c.jsonl` (the `--sg-eval coarse`
  pass, +73), `l34.jsonl` (`--sg-eval learned`, +20), `l34pass4.jsonl` (macro, +3), and **`chain.jsonl`
  = 494**. Session 25's four are kept beside them as `*-s25.jsonl` — **`l34.jsonl` and `chain.jsonl` are
  not the files of that name any session before 27 wrote.** `build/solutions/l34` holds the 96 the three
  passes added.
- **`chain-s25.jsonl` is the 476 chain**, and it is the report all six fourth-pass arms were pointed at.
  The full run points at `chain.jsonl` (494) instead, which is why its numbers will not be arm-by-arm
  comparable with the rehearsal's.
- **`chain5.jsonl` = 1,581**, the fourth pass folded in: `tools/chain_union.py` over `chain.jsonl` and the
  three arm reports, 494 + 805 + 191 + 91. **It, not `chain.jsonl`, is what a fifth pass points at.** The
  arms themselves are `build/reports/l5-{l8fire,layer7,enables}.jsonl` (3,691 rows each) with
  `build/l5/{arm}/` holding their **805 / 726 / 718 gated solutions**; `l5-run.log` is everything the run
  printed over six days and `l5-run.stamps` is the per-arm wall clock the resume and the `status` table
  are computed from. All of it is in gitignored `build/` and all of it is re-derivable — for 67 h 30 m.
- The push benches live in `build/bench/{ferry,deep}-{coarse,learned,work,none,hs128,hs157}.jsonl`.

---

## Session log

Kept short on purpose; where a finding is still load-bearing it lives in the layer that measured it. The
engine port's own log is in [`PROGRESS.md`](../../PROGRESS.md).

**session 9 — layer 0.** The search API, the batch harness, `verify_solutions.py`. The harness caught two
bugs in itself (the unbounded macro-step; `Restore` rewinding `RecP` while `RecBuffer` is one shared
array).

**session 10 — the campaign, and layer 1.** Threw away a wall-clock-budgeted campaign and re-ran it
node-governed, which is where that rule comes from. Layer 1 wins on levels layer 0 fails and *loses* over
the corpus in both orderings, so it ships as a second pass — the finding that shaped every layer after
it. 395 → 416.

**session 11 — layer 2, subgoal decomposition.** Derive what is in the way from the *executed* movement
closure rather than from the price list; accept on a board test, rank on a position test. The modelled
first version found no obstacle on 62% of expansions and is kept as the thing `--sg-trace` killed.
416 → 441.

**session 12 — layer 3, restarts.** Priced the dead-end failure mode before designing for it (717 levels,
84% of budget unspent), then found that what recovers a dead-end is *width bought after narrow has
failed*, not randomness. The negative half is the larger half: dead-ends 717 → 9 bought four levels.
441 → 444.

**session 13 — layer 4, a learned evaluation.** Built the instrument first, and it authorised the layer
rather than redesigning it: the winner's successor is in the expansion **97.6%** of the time and
`WorkDistance` ranks it **100th of 395**, so the loss was entirely in the sort. Fit as a ranking problem
within a group. **444 → 472, none lost.** Two results kept because they are the useful kind: a re-ranking
is not additive the way a restart is, and feeding the newly solved levels back in **halves what the model
discovers**.

**session 15 — why level 1 is unsolved, and the instrument that says so.** Started from a complaint and
refused to answer it from the level number. Three measurements, each of which changed the answer: the
record is the 66th percentile of its own collection; layer 0 ranks the level by Manhattan distance for its
entire life; and the winning line spends **68 keypresses above its own best `WorkDistance`** against a p90
of 21. Built `--profile` and `tools/basin.py`. Reverted the `WorkDistance`-ranked beam — 12/50 against
13/50, exactly the kind of result the benches exist to catch.

**session 16 — layer 5, push macros.** Structural claims held (closures never truncate, level 1 runs at
depth 50 instead of 264) and the cost claim decided it for then: one expansion is ~4,500 `ApplyKey` calls,
so 3/50 at 400k against layer 0's 13/50. Did not ship in the chain. The keeper is `RouteFerry`. Also: the
second hand recording (level 2, a conveyor level with no water, so the ferry term is provably inert) fails
the same way — **two levels, two structures, one failure mode.**

**session 17 — layer 6 (the read), the polisher, and a trimmer bug worth the session.** Michal
hand-recorded `LaserTank.lvl` 1-19 and asked for the thing a player does before searching. `Analyze.cs`
is layer 2's discipline one step further out. Inside layer 5 it turned into a width experiment and both
halves are keepers: **layer 5's width was simply wrong** (300 → 48 takes the deep bench 7 → 13) and **the
read is worth 4/50 → 11/50 on ferries at width 48**. The polisher landed with the reused-`Engine` bug
that made it look like a no-op — quirk hazard #12.

**session 18 — the beam was ranking tank poses, and the instrument that said so.** Built `--push-line`,
which answered in one row: level 1's line dies at the *first* board change, because 156 successors were
four boards wearing thirty-nine hats each. `--push-per-board` plus the learned key plus width 8 took
layer 5 from 11/50 and 14/50 to **20/50 and 21/50**. Also found the driver's push rung was getting
*weaker* every round.

**session 19 — level 1 is solved.** The fourth derivation, *"after this change the tank can make a board
change it could not make before"*. Measured as an instrument first (coverage 83.2% → 97.1%), which is
what decided it goes in as a tier of its own rather than a promotion. Two false starts, both session 17's
lesson. Then `--push-beam 48 --push-read --push-enables 8` solves level 1 in **6.19M nodes and 17.9 s**
against four 800M-node runs that did not touch it. Michal's objection — *"the user won't know to fine tune
random parameters"* — turned it into a rung, and that is where the
**solo-score-is-the-wrong-statistic** rule comes from.

**session 20 — level 2 is solved, and one old bug was scoring a destroyed board as perfect.** A laser
ferry cost a depth per cell (`--push-shot-run`); no key moved when a block got nearer the cell that would
stop a *ride* (`--push-stop`, five wrong versions, every one found by the beam); `--push-line` was
depth-indexed. And `WorkDistance` returned 0 for a board with no flag — the buried-flag bug. Level 2 falls
in 2.03M nodes; its own rung; ferry bench rebased to 19/50.

**session 21 — a post-solve pass that re-derives the route instead of deleting from it.**
`Replan.Improve`: the playfields a solution stood on are a ladder of positions already proved to win, so
find the shortest keystream that climbs it, free to skip rungs. Levels 3 and 7: 80 → 57 and 81 → 65. Cheap
because the ladder is a DAG. Also `--lanes N` for the driver.

**session 22 — six derivations for four levels, and two of the four fell.** `LaserTank.lvl` **8 and 9 are
solved**; 6 and 10 are not. Every one of the six is a *derivation* rather than a model — the fire map is
`AntiTank()`'s own scan asked of all 256 cells at once, the frozen block is `CheckLoc` on one side and
layer 7's `_rayOk` on the other. Three things worth more than the flags: the framing measurement (all four
levels were ranking-limited by two orders of magnitude, and one `--push-trace` column would have said so
before the eighty-million-node grid that did not); the number `--push-line` prints is not a distance; and
a penalty every board pays is not a penalty. Level 9 turned out to fall to layer 0's beam alone at round 5
— **let the driver finish a round before deriving anything.**

**session 23 — the file split, and one claim that did not survive it.** `SOLVER.md` and `PROGRESS.md`
separated; no code. Two things came out of re-reading the claims against the tree: **a missing `.lpb`
under `data/solutions/` means nothing** (Michal deletes a banked solution to re-run the solver by hand and
re-banks it after — never infer status from a directory listing), and **level 10's "wants about a billion
nodes" is wrong** — two 900M-node runs came back unsolved, read at the time as a frontier at board-change
depth 2, which sessions 26 and 29 then refuted.

**session 24 — three regressions found by re-reading, and one derivation demoted to a flag.** The
shorter level-8 and level-9 files in `build/w/` are gone with the rest of that gitignored directory, so
the 114-key level-9 route is lost unless re-derived. **The anti-tank-on-the-route rule cost level 1
272 keys in 22 s → 289 in 60 s** and moved the driver from round 3 to round 4, because the barrier set
decides the tier and the tier decides the beam's order; reverting that hunk alone restores 272, and it is
now `--read-antitank-wall`, off by default. **The rule it pays for: a derivation shared by every rung is a
flag, not an improvement.** Also: the driver was choosing between two winners by ladder index, i.e. by
accident; it now keeps the shortest. And level 9's 5.0x is the route, not missing polish — 73 board
changes against the hand recording's 26, and `Replan.Improve` moves it by nothing.

**session 25 — the chain rebuilt on a bare machine, and layer 4 found inert.** The layer-0 campaign and
all three passes re-run at 150k from an empty `build/`, `chain.jsonl` rebuilt (by `tools/chain_union.py`
now, because the recipe was a sentence), and the three level lists regenerated and **committed to
`bench/`** with their rules in their headers. **The composite reproduces: 472 → 476 of 4,185**, all gated.
Three findings: the documented chain omitted `NODES` on its three passes, so a copy-paste ran them at
1M — the accidental run is kept as `l3n-1m.jsonl`, **147 of 3,787 against 44**; **layer 4's learned key
does not act at the shipped weights**, identical *to the node* to layer 3, because `Eval.Score` divides
the fixed-point score by 1024 and the model's whole range is smaller than one unit of its own output; and
the attribution inside the chain moved even though the total did not, left unattributed deliberately.
**The session's lesson: a measurement that lives only in `build/` is not banked either.**

**session 26 — a second reader, and the fourth pass rehearsed.** No code. A different model read the file
against the source and the machine and wrote twelve pointers, every one of which has since been actioned
or closed (see below). In parallel, all five arms of the fourth pass at `SAMPLE=15`: **the union is 82 of
250 (32.8%)** where the pass's argument rested on 5.9%, 269 of 269 gated. **The pass is a chain of arms,
not a configuration** — best solo 61, union of three 82, and it took all five arms to know which two were
dead weight. The full run was priced at ~18 h an arm and not started. `tools/arms_union.py` is new,
because the instruction to compare unions had no tool behind it. The session's lesson is about
instruments: **the pricing probe was measured at the wrong concurrency and was 2.4x optimistic**, because
`SAMPLE=100` left each collection 1-4 levels and the timings came out near-solo.

And a citation worth having: **LaserTank is NP-complete** — Alexandersson and Restadh,
[arXiv:1908.05966](https://arxiv.org/abs/1908.05966), by reduction from 3-SAT, and the hardness survives a
board of only mirrors and solid blocks with the tank confined to a single column. It offers the solver
nothing algorithmically, and its **NP-membership half does not transfer**: that holds for the restricted
element set, and the paper conjectures PSPACE-completeness with a richer one — which is the corpus. Worth
knowing mainly because it says there is no polynomial trick being missed.

**session 27 — the learned key acts, and the reason it did not was a flag that gated nothing.** Two
defects between `Weights.cs` and the beam, neither in the model. The composite is **476 → 494 of 4,185
(11.8%)**, a strict superset, 96 of 96 gated. Session 25's unattributed +73 is attributed and it was not
`Heuristic.cs`. **The accident was an improvement, so it is now a key** (`coarse`), and both `--sg-eval`
and `--push-eval` take `work|learned|coarse|none`. `--push-eval none` came out the most complementary push
key on both benches while never winning solo. And the driver was running a duplicate rung. The session's
lesson is the sixth rule: **a flag that gates nothing looks exactly like a feature that does nothing.**

**session 28 — the fourth arm, refused by the corpus.** `--push-eval none` as a sixth arm at `SAMPLE=15`
on the same 255 levels, 2 h 15 m, 37 of 37 gated. **The answer is no** — 37 solo is the weakest of the six
(`plain` scores 38) and it adds +2 to the three-arm union with one exclusive level. **A bench over-reports
complementarity as readily as strength**, which is rule 1 wearing a face it had not worn. Two more: the
five rehearsal arms were not run at the same keystream cap, so two of them could not have emitted the
pass's longest solutions; and item 2's table was off by one or two throughout, recomputed from the banked
reports (population 255, not 250; union 83). Items 9 and 12 shipped alongside — `--max-round N` and
`--max-keys-record`, both off by default.

**session 29 — three cheap items closed, two negative, and level 10 finally has a cause.** No new
searcher and no new solved level; the machine cost was one 27-minute trace and about twenty minutes of
bench. **Level 10 is a GAUNTLET and that is the whole explanation** — d=63 on 399M nodes with `trunc=0`
and `front=1024 boards=1024` throughout, and **a barrier on 0 of 63,454 expansions** against 74.7% and
72.1% on the two benches. `best=` regresses to 28 for the final 28 depths: **read `best=` to the end of
the budget or do not quote it.** `--analyze` had the answer for free in one second, which is where *run
instrument 2 before instrument 4* comes from. The population that shares the condition is a quarter of
the corpus and sizing it nearly refuted the design (25.2% against 7.0%, a record-length artifact). Item
11's prune retired on its own acceptance test on two populations, which left item 10 as the whole of the
wall-clock story — and item 10 has since closed it, positive on `--push-memo` and negative on the layer
underneath. Item 3 a no-op because its premise expired. **The lesson as a ratio: two planned code
changes and one planned batch job retired for ~20 minutes of machine.**

**session 30 — a regression, and the file it happened to.** A `--force` re-solve **banked the beam's
294-key level-9 route over the 127-key one** and said nothing about it. **The first fix was the wrong one
and is worth recording as such, twice over:** first making the gate keep the shorter file, then treating a
`git checkout` of the good file as the repair. Michal's two corrections are the shape of the real one —
**"git takes care of keeping the better one — the solver should always replace it with `--force`, that way
we actually catch the regressions"**, and **"the fix isn't to replace the long solution with another one,
that is like me solving it manually and posting that as a solution"**. So the fix is a property the driver
either has or does not: **one run should find the best route the project has ever found for a level**, and
the answer is `--best-of-round` and `--beat-banked` (ON by default, Michal's call). All four paths tested
on level 3. **The level-9 acceptance run was not done**, leaving an implemented property rather than a
demonstrated one.

**The file itself was the third finding.** `Next actions` had grown to **846 lines** holding four open
items, eight closed ones and four session-state blocks under one heading, which is what made the level-9
warning unfindable at the moment it mattered. Split, numbers preserved, zero non-blank lines lost. **The
rule: this file is read under time pressure, and a warning it contains but cannot surface is a warning it
does not have.**

**session 31 — the top two items of the list, both run to the end, and they came back opposite ways.**
**Item 4's acceptance run passed better than its own bar:** unattended, 44m46s, `3 rungs solved this
round; kept the shortest at 115 keys against 294`, banked at **115 keys / 1.9x** against the 127 the
project had ever managed. The winning rung is `push-ferry`, not the `push-ferry-work` the bar named —
what was broken was the cancel, not the ranking key. **Item 5's fire tier is positive on its population
and negative on its example:** `--push-fire-tier` is Layer 9, **85 solo against the control's 76 with 15
exclusive levels against 6** over the 138 unsolved short-record GAUNTLETs, 161 of 161 gated, 9% wall
clock, shipping as the tenth rung — **and `LaserTank.lvl` 10 is still unsolved at 400M nodes.**

Four things worth more than either result:

* **A derivation can be right about a population and wrong about its own example.** Every layer before
  this one was named after the level that motivated it and shipped when that level fell. This one was
  named after a *measurement* — `on the barrier: 0` on 63,454 expansions — and the measurement generalised
  where the level did not.
* **`best=` is the wrong column for a tier that trades distance for exposure, and it says so loudly.**
  The traced run with the tier bottoms at `best=29` against the untiered run's 22 — the layer looks like a
  regression in the only column the trace had. `--push-trace` gained `frontier sweeps N at best, M at
  least` for that reason, and it shows exposure falling 131 → 115 → 99 on the same run. **Third time an
  instrument here has measured the wrong thing and nearly retired a working layer.**
* **A cheap falsifier that does not falsify is not the same as a cheap falsifier that is cheap.** Item 5
  was ordered first partly because `--analyze` could contradict it in a second. It did not contradict it,
  and the actual cost was a build plus two 40M-node arms over 138 levels — six hours. The estimate was of
  the *best* case and the list read it as the expected one.
* **The two open levels no longer have a costed candidate between them.** 10 has now had budget, closure,
  width, depth and the fire tier cleared; 6 never had a cheap falsifier at all.

**session 32 — the file split again, and this time by kind rather than by heading.** No code, no
measurement. `SOLVER.md` had grown to 3,653 lines, about a third of it per-session archaeology (a session
log, superseded tree-state blocks, twelve pointers whose actionable distillate was already elsewhere) and
another chunk of it the file narrating its own reorganisations. Split into a hub plus five files by what a
reader wants — commands and status; the layers; the driver; the instruments; what is open; and this
archive — with every measurement and every negative result kept. **The rule this pays for is session 30's
one level out: splitting a file by heading keeps everything findable only until the headings themselves
are the problem.**

**session 33 — the fourth arm, and it retired the first.** Rehearsed `--push-fire-tier` as a fourth arm
of the fourth pass on the same 255-level stride the other six were run on, per item 2's own recipe. It is
**66 of 255 solo** — the best solo arm ever measured on that population — **66 of 66 gated**, and the
seven-arm union is **87 (34.1%)** against six arms' 84. But the shape of the win is not the one the
GAUNTLET-tail bench predicted: the tier solves 60 of `l8work`'s 61 *plus six more*, so `l8work` falls to
**+0 and 0 exclusive levels** and is retired as an arm. **The fourth arm is a replacement for the first,
not an addition to it** — the pass stays three arms and ~54 h and gets 84 instead of 81, for +7.3% wall
clock. `LaserTank.lvl` 10 is still unsolved and 166 of the 168 levels no arm solves still stop on
`budget`.

* **A filtered bench can over-report the *kind* of a win as well as its size.** The tail said 85/76 with
  15 exclusive against 6 — a strongly complementary arm. The corpus said 66/61 with 6 exclusive against
  1 — the same direction, a third of the margin, and *less* complementary rather than more. Rule 1 fired
  for the fifth time and for once in the project's favour: what the arm lost was its claim to be a
  fourth arm, and the result got cheaper rather than dearer.
* **Michal's challenge to the ordering, and it was well founded.** He asked whether decoding the
  blogspot goal boards, or simply running the corpus at a production budget, would beat committing 54
  hours. Both halves check out against the record: the headline 11.8% is measured at 150k nodes — about
  a tenth of a second a level — and the one accidental 1M run returned 3.3x the levels (item 7); while
  layer 4 already established that training on the solver's *own* solutions halves discovery (28 → 14),
  which is precisely why the off-distribution recordings item 6 would harvest are what layer 4 says it
  needs. Item 2's own second ordering key — *prefer work that produces a property or a level over work
  that produces a number* — ranks a harvester spike above the 54-hour run, and the list had it last.
* **Four read-only fetches closed two of item 6's three unknowns** and found a third thing nobody had
  looked for: the ordered goal cells are plain text in the image filenames, so the multi-flag subgoal
  sequence needs no image decoding at all. Mapping is 6/6 exact. The Moves/Shots counters, which the
  item leaned on, turn out not to be universal. Details in item 6.
* **A byte-offset error nearly became a published number.** Reverse-engineering the `.lvl` record as 896
  bytes gave 13,437 levels and a plausible-looking 97.8%-unique table, and its level names for
  `LaserTank.lvl` 1-10 were *sequentially correct*, which is what made it convincing. The real record is
  576 bytes and is documented in `PROGRESS.md`; at 576 the corpus totals **20,914**, which is the number
  `SOLVER.md` has always quoted. **The cross-check that caught it was a total the project already knew
  — read the format, and reconcile against a number you did not derive.**

**session 34 — item 6's spike, and it produced a level rather than a percentage.** Ran the ~2 h
feasibility spike session 33 had costed, and it came back positive at every step, so it shipped as
`tools/harvest.py` + `tools/png.py` rather than as a scratch script. The numbers are in
[closed item 6](#session-34--the-spike-and-every-number-it-produced); the four things worth more
than the numbers:

* **A "scraping project" was two orders of magnitude cheaper than costed, and the reason is that nobody
  had looked at the feed.** The Blogger JSON feed serves the post *body*, so the whole index — 6,218
  posts, titles and image URLs — is 42 requests and 46 seconds, with no HTML parsing anywhere. The item
  had been carrying a scraping-project estimate since it was raised. **Before costing a crawl, check
  whether the site has an API.**
* **Session 33's best find was over-generalised from one post, and the correction matters.** The
  `Coll_NNN_G4.png` convention that makes a multi-flag subgoal sequence free text is **later-era only**;
  the 2016-era posts, which are exactly the ones covering `LaserTank.lvl` 1-19, use `10a`/`10b` and carry
  no cell names. So the two levels this project actually cares about were in the era where the pixel
  decode *was* the whole question. One post is a sample of one, even when what it shows is real.
* **A bootstrap that grades itself.** Because every post's collection and level come from its title, a
  start screenshot is 256 *labelled* tiles for free — so the codebook builds itself, and decoding each
  board against the codebook built from only the boards before it makes the curve honest and a
  disagreement a hard error. Two independent samples: **55 entries each, 0 conflicts over ~38,000
  labelled tiles, agreeing on 54 of 55 and disagreeing on none.** *What is not stable is which board
  teaches the last tile* — 10, 37 and 97 across three runs. Quote the size, not the position.
* **An arithmetic coincidence is the most convincing kind of wrong answer, and this is the second session
  in a row it has happened.** Matching level 10's goal anti-tanks to its start anti-tanks by eye gave a
  total push distance of exactly **52**, equal to the post's shot count, which read as a beautiful
  confirmation of the whole decode. The exhaustive assignment says **30**. Same shape as session 33's
  896-byte record, same fix: reconcile against a number you did not derive.
* **The spike's last hour went on the *next* step's tooling, and it changed that step's size.** The
  residual was going to be labelled off a contact sheet built by a throwaway script, which would have
  left the next session to rebuild it — so `sheet` and `label` are subcommands, and labelling one sprite
  proved the loop closes and measured what it buys (4.52% → 1.37%). **A phase costed as "half a session
  of eyeballing" was worth ten minutes of making the eyeballing resumable**, because the artefact a
  spike leaves behind is what decides whether its successor starts or restarts.

**session 35 — item 6's phase 1, closed by deleting it.** The half-session of hand labelling was not
needed: the 2010 binary's graphics are committed at `original/src/Game.BMP` + `Mask.BMP`, and
compositing them the way `UpDateSprite` does labels every state play produces. `tools/sprites.py` and
`harvest.py tiles`. **116 of 116 residual sprites, 606 of 606 instances, 0 unknown tiles of 44,288 over
173 goal boards** (session 34's number for the same boards was 1.37%), the tank located on 173 of 173,
and `decode --check` still clean on **141 of 141** start boards. `LaserTank.lvl` 10's goal board now has
no undecoded cell at all. What is left of item 6 is `--goal-board` alone.

* **The rule this inverts, and it is now the seventh in `SOLVER.md`.** `bench/` exists because nothing
  re-derives a human's answer; the corollary is that nothing should *ask* a human for an answer the tree
  already contains. Both end in a committed file, so they are easy to confuse — and the distinguishing
  question is one sentence long: *what committed input could produce this?* Session 34's reasoning about
  why the residue was hand input was correct in every clause except that one, and it cost the right
  answer a session.
* **The derivation was one wrong transform away from looking impossible, which is the part worth
  remembering.** A plain nearest-neighbour shrink of the sheet reproduces the palette *exactly* and the
  pixels not at all — the first dirt tile came back the right two colours in the wrong proportions
  (78% olive against the real 65%), which reads as "the blog uses a different graphics pack" rather than
  "the scaler is wrong". The real answer is that `GFXInit` never calls `SetStretchBltMode`, so the
  shrink runs under GDI's default `STRETCH_ANDSCANS` and **ANDs** the eliminated rows and columns.
  **When a derivation is nearly right, the residue is a transform, not a different input.** The AND is
  self-evidencing once suspected: a real dirt tile carries a third colour, `0x108010`, which is
  `0x949410 & 0x108310` and appears in neither source sprite.
* **The grouping was solved from the data rather than guessed.** Of seven candidate 32→24 mappings,
  exactly one — `dst = (src * 24 + 12) // 32` — reproduces a real dirt tile pixel-for-pixel. Fitting
  one tile and then reproducing 4,108 others is the only reason that is evidence and not a curve fit.
* **Four reconciliations against quantities not derived from the sheet**, which is what the two previous
  sessions' arithmetic coincidences bought. The one that mattered was computed *before* the contact
  sheet was opened: the distribution of the **start board's `PF`** at the cells where each unknown
  sprite appears, read from the `.lvl` files. It is nearly pure, and it agreed with the sheet on **all
  95** sprites the first enumeration matched. It then confirmed `KillAtank` independently — junk bitmaps
  54/52/12/53 appear over start cells `^`/`>`/`v`/`<`, matching the source's four `case` arms one for
  one. Third, the derivation puts level 10's tank at **(6,0) facing right**, the cell session 34
  identified by eye. Fourth, it finds 10 anti-tanks and no wrecks on that goal board, reproducing
  "zero of the ten are destroyed" from pixels rather than from assignment arithmetic.
* **Three engine rules had to go in on top of `GetOBMArray`, and each was a bitmap the residual actually
  contained** — which is how you know the enumeration is the engine's and not a plausible model of it.
  A shot anti-tank is `PF = 4`, not dirt (`KillAtank`, `Engine.cs:868`), and those four wrecks are the
  residual's largest family at **141 of 606 instances**. A block pushed into water is `PF = 0` with
  `BMF = 19` (`Engine.cs:731`) — the one sprite session 34 had hand-labelled, and the derivation agrees
  with it. And the tank is not in `PF` at all (`Engine.cs:348`), which is the only genuine pixel
  ambiguity in the whole set: `T` as a foreground bitmap and the tank overlay facing up are the same
  pixels. `decode` now returns the tank as `(x, y, facing)` beside the board, and `--check` reconciles
  the tank cell rather than excusing it — a `T` in the `.lvl` must come back as *dirt plus a tank there*,
  so anything else at that cell is still a real disagreement.
* **Two things the sheet cannot draw, named rather than swept up.** One start tile is the tank as a
  **black silhouette** — a screenshot caught between the mask `SRCAND` and the sprite `SRCPAINT`, so it
  is a capture artifact and no composite produces it; only the facing is lost, which is why "boards with
  no tank found" is a printed number. And `UpDateLaserBounce`'s half-cell rectangles and any explosion
  frame are not enumerated: **0 of 44,288 tiles needed them**, so it is a known gap with a named fix.

**session 36 — item 6 closed: `--goal-board`, and an acceptance test that falsified the specification.**
The build is `Goal.cs` plus `harvest.py bank`; the measurements are in
[closed item 6](#session-36--the-key-the-item-asked-for-is-not-the-key-that-works).
Four things worth more than the numbers:

* **The item specified its own ranking key, and the key was flat on the item's own test.** *Rank by
  cells-still-differing* was written down a session before the build, and so were the three root pushes
  on `LaserTank.lvl` 10 it had to separate. They are 12 cells differing before and 12 after all three: a
  push vacates one cell and fills another, so a count of misplaced objects cannot move while an object
  is in transit. **This is the third time the same shape has been paid for** — `WorkDistance` flat over
  a ferry, `--push-fire` flat over a gauntlet, and now this — and it is the reason there is now an
  eighth rule in `SOLVER.md`. What works is the *assignment*: not how many objects are wrong, but how
  far each still has to travel to a cell that wants it. 30 → 29 for the push that helps, 31 for the two
  that do not.
* **The test cost nothing to run because it had been written as arithmetic first.** Session 34 computed
  level 10's minimum total push distance by hand and got 30; the implementation reproduces 30 from the
  pixels. That is the fourth consecutive session in which the useful check was *a quantity derived some
  other way*, and it is now cheap enough to be automatic: two sessions ago it caught an arithmetic
  coincidence, this time it confirmed a build.
* **The honesty condition became code.** "Hint-assisted solutions must never enter the headline rate"
  had been a sentence in three files for three sessions. It is now the flag's own behaviour — the
  default output directory moves, every report row is stamped, the run says so — because the driver's
  default output is `data/solutions/`, which is *committed*, and one hint-assisted `.lpb` landing there
  silently raises a number the whole project is measured by. **A condition that depends on the operator
  remembering it is not a condition.**
* **The population number is small and it is the honest one.** 15 → 21 of the 141 levels the fetched
  bank covers, +9 and **−3**: a goal board says where the objects end up and not in what order, so a
  level whose blogger route disagrees with the machine's is now steered away from the route the machine
  had. All 21 through the two-engine gate. Nothing here is tuned — one weight, one price, neither swept
  — because the population is 141 levels rather than 3,709 and everything it produces is outside the
  rate.

**session 42 — item 16's shared falsifier, and the derivation it turned up by failing.** Sessions 37-41
were item 6's, and are logged under
[closed item 6](#6-the-blogspot-goal-board-harvester-and-the-goal-board-as-a-ranking-key). This one is
item 2's machine time being spent by the machine while the list moves on beside it: item 2 has been
running since 2026-09-10, its arms are node-governed, so a two-second replay costs it wall clock and
nothing else. Item 16's first two derivations were specified with one shared falsifier — two
`--read-dump` columns and one replay — and it is run. Four things came out of it, in ascending order of
how much they cost to learn:

* **Both derivations survived, and the author-intent one is the largest lift the read has ever
  measured.** `rare` — a board change that touches an element the *authored* board has two or fewer
  cells of, families collapsed — is named on 5.1% of the successors on offer and is what the human did
  16.0% of the time: **3.15x**, against 1.43x for `advance`, 1.40x for `opens` and 1.34x for `enables`,
  the three derivations the read already ships. It costs a 256-byte census per level: no closure, no
  second enumeration. **The read's three existing derivations are all about terrain and all sit between
  1.3x and 1.5x; the first one about the author sits at 3.2x.**
* **The spend tier is right-signed, and its exceptions were one thing rather than noise.** `spend` — a
  change that consumes something while the read names no barrier, no `opens` and no `enables` — is
  offered on 7.6% of successors and is what the human did **1.9%** of the time (15 of 795): **0.25x**,
  so the tier demotes what the human avoids, which is the direction the item said it had to be. The 15
  exceptions are where the session actually went. **13 of them are `kill`** — a gun shot in the face —
  and the reason is structural rather than statistical: `KillAtank` leaves a *solid* wreck, so the cell
  stays impassable and `opens` is 0; the gun was covering a free cell of the route rather than standing
  on one, so it is a `Threats` entry and no derivation reads that list; and a dead gun makes no new
  board change possible. Exempting the kill leaves **2 of 795** human changes demoted (0.04x) and still
  names 507 of the 7,772 successors, so the exemption costs the tier 14% of its reach and buys back
  almost all of its error. That number exists because the spend column was built as a **mask of what
  was consumed** rather than as a bool — `fill / brick / ice / kill / face / roto` — which is the whole
  reason the 15 could be attributed without a second replay.
* **The fifth derivation was built, measured, and refused.** If the read has nothing for *fire*, add the
  free one: `clears`, a change that leaves an anti-tank the route named as a threat off the cell it
  named it on. It is **0.88x over all 800 changes, 1.12x over the 543 where the route names a gun at
  all, 1.31x over the 287 where a clearing shot is on offer**, and it explains only 9 of the 15. The
  other 6 say why: `LaserTank.lvl` 20 is *"Destroy most of the guns"*, and the human kills three guns
  the route does not yet name — the route is re-derived every board change, so a gun that will matter
  in twelve moves is not a threat now. **A threat model that outlives one board change is item 5's
  shape, not a free column's.** Kept in the dump as a measured negative, because a negative result that
  is deleted gets re-run.
* **The threshold was swept, and the sweep is the caveat.** `--read-rare N`: 1 → 3.52x, 2 → 3.15x,
  3 → 2.37x, 4 → 2.00x, 5 → 1.98x, **6 → 2.59x**, 8 → 2.50x, 12 → 2.04x. The overall shape is the decay
  into the base rate that "few" losing its meaning should produce, but it is **not monotone**, and the
  bump at 6-8 is one class count on a twenty-level sample — `LaserTank.lvl` 20 has exactly six guns.
  So the lift is real at every threshold and its *magnitude* is a 20-recording number, which is
  precisely what item 6's bank was banked to widen. The default is 2 and the flag exists so the sweep
  can be re-run on a wider sample without a rebuild.

**One discipline note, because it was nearly got wrong.** `spend` reads `Opens` and `Enables`, and both
are only computed under `--read-enables` and inside the `--read-opens` cap. Without the flag the phrase
"for no derived reason" silently collapses into "not on the barrier" and the column would have reported
a *number* — so the three spend columns report **-1** when the derivation was not asked, and
`Read.SpendAsked` is what decides. A missing label, never a wrong one, is the same rule `Effect.Opens`
was given when its cap was introduced.

**Nothing in the shipped search changed.** `Enumerate` and `AnalyzeAt` are reached only from `--analyze`
and `--read-dump`; the tier path is `ReadDerive`/`Advances` and it was not touched. Checked rather than
asserted: `--from 1 --to 12 --nodes 400000` is byte-identical before and after (1 solved, budget 9,
beam-dead-end 2).

**session 43 — three items closed beside the running pass, and one of them came back with the sign
reversed.** Item 2 has held the machine since 2026-09-10 and its arms are node-governed, so this session
did what the list said to do beside it: items 16, 15 and 13, in that order. All three closed. The
measurements are in [closed items 13, 15 and 16](#closed-items--the-measurements-including-the-negative-ones);
what is worth carrying forward is which of them changed a belief.

* **FMO mobility predicts the solve rate, and freer blocks are *harder*.** The human claim was *"it is
  generally easy to win if you have enough FMOs"*; the corpus says the opposite — frozen blocks 16.0%,
  1-4 cells 11.5%, 9+ cells 4.5%. The reading that makes both true is that the claim is about a player
  and the measurement is about a beam: a free object is a resource to one and a **branching factor** to
  the other. **The measurement that made it worth keeping was the control, not the column.** Raw, it
  looks exactly like *big open board*, which `poses` already reports; stratified on the record's own
  length plus water and block count, `mob_max` holds **1.78x** and `poses` collapses to **1.17x**. So
  the column ships, and the rule it re-proves is the one from item 6: *reconcile against a quantity you
  did not derive* — here `.ghs`'s own move+shot count, which nothing in the read computes.
* **The per-carry constant is refused, and the refusal was already written down as a rule.** A constant
  per carry cannot shorten an ascent that happens *inside* a carry, and the drop at a fill is already in
  `MatchFerry`. Ten values of K, twenty recordings, not one ascent moved — while three levels' deepest
  rise got worse, because the hole count is not monotone along a human line. *A penalty every board pays
  is not a penalty*, in its per-phase form, for the fourth time.
* **Level 6 now has a number instead of an ambition.** `--push-seed` searches from a recording's K-th
  board change; the level finishes from **48** board changes out and not from **51**, monotone below
  that, and 24 changes cost 3.8M nodes against 48's 36.1M. Item 5 was open for six sessions as *the one
  item with no cheap falsifier*; it now has a target, a boundary one board change wide, and a growth
  rate to size a phase against.
* **The instrument bug, and it is the fourth of its family.** `--profile` built its `ferry` column from
  whatever `Want*` flags happened to be set — never set on that path — so it reported the *per-hole*
  estimate however the run was flagged, `--push-ferry-match` included. Found only because the carry
  sweep needed `MatchFerry`'s own numbers. `PushH`'s configuration is now `WantsFromOptions` and both
  callers share it. **A profile is only the beam's view of a line if it asks for the beam's
  derivations.**

**Nothing in the shipped search moved, and it was checked rather than asserted.** `Mobility` is reached
only from `AnalyzeAt`, `RouteHoles` is a field `CountOnRoute` assigns and only `--profile` reads,
`WantsFromOptions` is `PushH`'s own assignments moved verbatim and still immediately before the
`WorkDistance` that reads them, and `_seed` is null unless `--push-seed` is given. The equivalence test
is `--push-seed FILE:0` against no flag: same nodes, same stop, same depth.

**session 44 — two items closed beside the running pass, and the second one inverted the number the
first list ordering was built around.** Items 17 and 18, in the list's order, both beside item 2's
machine time. Item 18 is the one that mattered: **the search horizon is a level property, 2 to 50 board
changes over the 12 recordings the arm cannot solve unseeded**, so item 5 cannot size a phase from a
constant — and **level 6's 50 is the *deepest* reach measured anywhere**, which is the opposite of what
item 15's 48 read like. What defeats level 6 is a line 3.4x its own reach, not a short leash. Item 15's
number is refined 48 → 50 and its own probe reproduced to the node. `tools/horizon.py` is the harness,
96 probes, 45 of 45 gated, and it cost two defects that were the same defect twice — in-memory
bookkeeping believed over the banked reports, once through a shared `--report` file where a lost row read
as a *failed* probe, once through a work list built from what the process had classified rather than from
the reports. Full record in
[closed item 18](#18-the-horizon-per-level--a-level-property-and-not-a-searchers-reach).

**session 44, continued — item 17 built and refused, and the refusal names the confound in every
`--read-dump` ratio.** The list's rule had item 17 second and its case was unusually strong: session 42 had already run
the falsifier, `rare` scored **3.15x** — the largest lift the read has measured, and by `Push.cs:798`'s
own rule the most selective *and* most accurate of four derivations — so what was left was ~40 lines and
four benches. Built as `--push-rare` (`TierRare` at 0, the census once per level off `Level.PF`, the
per-successor test being the same `RareOfDelta` `--read-dump` scores the human with), gated in the order
the item prescribed, and refused. The table is in
[closed item 17](#17-the-rarity-tier--built-and-refused-on-the-bench-it-set-itself); three things are
worth carrying forward.

* **A `--read-dump` ratio is measured on twenty `LaserTank.lvl` recordings and a tier runs on the levels
  the chain fails, and those are not the same population.** `rare` names 5.1% of successors over the
  recordings; over the ferry bench `--push-trace`'s new `rare` column reads **2%** on `Beginner-I` 191
  and **0%** on 41, 326, 366 and 431. A ferry level is authored with three or more of everything a ferry
  push touches, so the partition the tier promotes is *empty* exactly where the search spends its budget.
  The promotion rule was correct and there was nothing to promote. **The pair to read from here is the
  `--read-dump` ratio against the tier's own selectivity on the bench** — either alone is silent about
  this, and the second half is now free for any tier that has a trace column.
* **This is layer 4's result arriving from the other side.** The winner's state is in the expansion's
  output 97.6% of the time and the sort loses it; item 17's own prior said *a tier is a different
  instrument from a ranking* and the honest expectation was *a few levels, not a layer*. What it actually
  cost was **two ferry levels and one deep one**, which is the same finding with the sign a stronger
  claim would have hidden.
* **The one number that looks like an opportunity is the one the first rule forbids.** `ctl-deep ∪
  rare-deep` is **28 against 25** — three exclusive levels, the signature of a complementary arm. It is
  not one: `--push-eval none` scored a *larger* union on these two lists (+6 ferry, +4 deep), was the
  best arm of the rehearsed fourth pass on that basis, and came back over 255 corpus levels as the
  weakest of six with one exclusive level. Proposing an arm here would spend item 2's machine time on the
  reading that rule exists to refuse.

**The control was run first and it is why any of the above is readable.** `ctl-ferry` reproduced
**18/50** and `ctl-deep` **25/50** exactly, against the rebased layer-5 baselines, before the tier ran —
so the −2 and −1 are attributable to the flag and to nothing else. Five benches at four jobs beside item
2's sixteen, ~3.5 min each, node-governed; the running pass saw it only in wall clock. The code ships off
by default. **Its one unconditional cost is that `--push-line`'s tier column renumbered** (`TierRare` at
0 pushed `advance` to 1 and a pose to 6), the second time a tier insertion has done that.

---

**session 45 — item 5, built and refused; and the horizon turns out to be width-dependent.** Two
instruments and a flag. `tools/phases.py` cuts a hand line at its **milestones** — a board change after
which some consumable object is strictly rarer — for free off `--push-line`'s replay, and it sized the
item positively: `LaserTank.lvl` 6's 168-change line is **six phases of 18 to 34**, every one inside the
horizon of 50 measured there. `--push-phases` (`Phase.cs`) chains a push search per phase and commits to
the **right** first board — the fill at (9,14), the cell the human fills first. Then
`tools/phase_reach.py` refused it: **4 of 6 phases reachable at width 32, and the two that fail are the
middle**, at every width tried, **and from the human's own board too**. So the phases are short, the
commitment is sound, and what defeats level 6 is not the length of its line.

Three things it leaves. **The horizon is width-dependent** — the chain solves level 6 from K = 102, 66
changes from the end, at width 32 on 3.4M nodes, where item 18 measured 50 at width 512 on 40M; item
14's per-level lever therefore has a second dimension. **The commit law was the whole engineering**:
ending a phase at the first milestone needs no constant and *cost `LaserTank.lvl` 20*, a level the plain
beam solves, because a demolition consumes something on nearly every change — layer 1's founding finding
again — so what ships commits only where the search would give up, and is node-identical to the flag off
on all five control levels. And **phase 2's trace is `best=137` flat for 237 depths**, level 10's
GAUNTLET signature on a ferry level: what binds in a Sokoban's middle game is the ranking, not the depth.
Two harness defects, both item 18's arriving again in a new file: a solver predating the flag left no
commit line and that read as *"not reached"* (the worst direction), and a per-probe report keyed without
the budget let a cheap re-probe report an old run's `solved`. Both now raise.

**session 46 — item 14 swept and refused, and the engine port's death fix cleared the solver's
equivalence gate.** The three-arm sweep finished on an idle machine (the pass was stopped for it, the
only wall clock in these files measured that way) and came back **76 / 78 / 78 against the banked
control's 85 of 138**, at the calibration's own p25 / p50 / p75. Raise-only meant the sweep went *wider*
— median chosen width **854 to 6,820** against the global 128 — so what closed is that direction of the
lever and not the lever; **item 19** is the same run at `--push-beam 32`, which is the direction level 6
and item 5's phase table have both pointed at. 232 of 232 solutions gated. Two artefacts worth keeping:
the flag is **not** node-identical when it declines to raise (the root closure is charged to the budget,
median +1,488 nodes; keys identical everywhere), and four `Challenge-IV` rows of the f60 arm are
time-truncated at `ms ~ 197.6M` because the machine slept and `BUDGET_MS` tripped — a floor short by at
most one level, and the first time a *sleeping machine* has shown up as a measurement artefact here.

The solver was also rebuilt for the first time since 2026-09-09, which mattered because `a4eac84` put a
real change into `LaserTank.Core`: a death now clears the pending key buffer (`RB_TOS = Game.RecP`), the
line the port was missing. Over `LaserTank.lvl` 3, 4, 7, 11 and 20 at 4M on the `l8fire` arm the rebuilt
binary is **node-identical and keystream-identical** to the pre-fix one, so **every banked report still
stands as a control**. That is expected — the search never keeps a path through the death handler — but
it was checked rather than assumed, because a shared-engine change that silently moved the solver would
have invalidated the whole `build/reports/` bank at once.

**session 47 — item 19 run beside the restarted pass, and it closes on a tie.** One arm at
`--push-beam 32` over item 14's 138 levels against the same banked control: **84 against 85**, 84/84
gated, 100 min wall at `JOBS=4` while item 2's pass held 16 — and the wall-clock hazard that made the
pairing worth checking (`BUDGET_MS` is wall, not nodes) never came close, worst level 501 s of 1,800 s.
**The count is the least interesting part.** The item's own prediction — narrow holds the *deep* levels
— failed on a 4/4 split of its exclusives about the population's median record length, which retires
per-level width from the narrowing side as well; what it found instead is **cost**, 1.03M nodes median
against 1.61M on the 76 levels both solve and four exclusive wins at 1.8M-3.4M on levels the control
failed at 40M. Two arms that tie at 40M and differ 1.6x in nodes do not tie at 4M, so the follow-up is
item 7's budget curve and not a width item. Five-arm union **101 of 138 (73.2%)**.

**session 51 — item 2 finished, and the pass that took five days is the project's largest single
result.** The fourth pass completed its third arm at 2026-09-15 20:29, six calendar days and seven
restarts after it began: **1,087 of 3,691 (29.5%)**, composite **494 → 1,581 of 4,185
(11.8% → 37.8%)**, **2,249 of 2,249 gated** with zero divergences, 67 h 30 m wall and 879 h of job time.
`build/reports/chain5.jsonl` is the new *everything that is still open* report. **The finding is not the
count, it is what the rehearsal got wrong.** A 1-in-15 stride priced the union to within 7.8% of its own
published prediction and the composite to within 5.6% — good instrument — and then **put arms 2 and 3 in
the wrong order and 3.5x apart when they are within 1%.** `enables` was written up at *+4* and nearly
dropped; dropping it would have cost **101 levels**, more than dropping the `layer7` the stride ranked
above it (91), and their exclusive counts invert (**15 / 11 / 4** on the stride, **173 / 91 / 101** on the
corpus). So *compare unions* survives and gets a limit clause: **a stride sizes a union; it cannot order
the arms inside it, and it must not be used to drop one.**
Two firsts came out of the run — **`Hard` falls, 5 of 257**, a tier that was 0 of 257 in the chain and
untouched by all seven rehearsal arms, and **8 solutions come in under the `.ghs` record**, which is
the first population big enough to say how often a recorded score is beatable (~0.7%). The ceiling
has not moved: of the 2,604
levels nothing solved, 99.6% still stop on `budget` and **five** are structural in all three arms.
**The tree was then republished for the first time since session 46 and gated against the pass itself.**
Four solver commits and one `LaserTank.Core` commit had landed behind the running binary, so the binary
that produced the 1,087 is gone; five levels from five collections re-run at the `l8fire` arm's flags and
budget come back **node-, key-, depth- and `.lpb`-byte-identical** to the banked rows, so the whole
`build/reports/` bank still stands as a control and item 10's two flags are inert off by default. The
control is the pass's own output rather than a second binary, which is the better form of this check.

**session 53 — item 10 closed, and the last thing it wanted was declined rather than built.** No solver
run: the whole session is a read of `Push.cs` and `Heuristic.cs` against the item's own arithmetic. What
was left of item 10 was a second, **board**-keyed memo underneath the shipped pose memo, written up at
"~1.12x, likely nearer 1.08x". Three things in the code settle it. **Its key would be free** —
`MemoProbe` (`Push.cs:203`) already runs both FNV chains over the board and mixes the tank cell in only
at the last step, so `(a, b)` before that mix *is* a board key, on every call; nothing in these files had
noticed. **Its early-exit penalty is worse than charged** — `RouteObstacles = CountOnRoute(e, c)`
(`Heuristic.cs:449`) keys on the cell the Dijkstra stopped at, so it stays per-pose even with a completed
shared table, and the shared table buys the relaxations only. **And its entry is 2.5 KB against the pose
cell's 56** — 45x — in the exact dimension the pose table's own sizing measurement already resolved
against: 918 KB a worker across sixteen workers costs more in shared cache than the 1.8 points of hit
rate it buys. ~1.08x for the expensive half of the build, in the footprint shown not to pay, on an item
capped below 2x by its own first table. **The rule this session adds is about declining a measurement**:
the run that would have priced the early exit (one level, one thread, a minute of one core) is written
down in the closed item *as not taken and why*, because a declined measurement that leaves no record
gets proposed again. `push_memo.sh` also stopped overwriting its own reports, which is what made session
50's two columns unrecoverable. **The open list is one item: 7.**

## Where session 26's twelve pointers went

The pointers section was a pass over the file and the source by a different model, tagged **measured** /
**in the code** / **hypothesis**. All twelve have been actioned or closed, so the section itself is gone
and this is the map:

| # | what it was | where it is now |
|---|---|---|
| 1 | spend the budget where the record says the level is short | [next-actions](next-actions.md#7-1st--the-solved-vs-budget-curve) item 7 |
| 2 | the node budget hides most of a push rung's wall clock | closed item 10 above — **half-negative**, 1.31-1.56x |
| 3 | a lossless prune the push beam does not take | closed item 11 above — **negative**, 0.05% |
| 4 | what `--push-eval learned` ranks by at the shipped weights | closed item 1 above; the four keys are in [layer 4](layers.md#the-two-defects-that-kept-this-layer-inert-and-the-four-ranking-keys-that-came-out-of-them) |
| 5 | level 10: the file's arithmetic and the machine disagree | closed item 8 above — **the pointer was right about the depth and wrong about the key** |
| 6 | the driver cannot be run unattended | closed item 9 above — `--max-round` |
| 7 | half the corpus is a Sokoban, and Sokoban has a solved literature (FESS) | [next-actions](next-actions.md#further-out-and-only-after-the-numbers-above-have-moved), further out |
| 8 | subgoal chaining over board changes | [next-actions](next-actions.md#further-out-and-only-after-the-numbers-above-have-moved), further out |
| 9 | `MaxKeys` from the record, not from a global | closed item 12 above — `--max-keys-record` |
| 10 | the width ceiling is memory, and the memory is keystreams | [next-actions](next-actions.md#further-out-and-only-after-the-numbers-above-have-moved), further out |
| 11-12 | the three *checked, and not opportunities* items | [next-actions](next-actions.md#checked-and-not-opportunities) |

**One pointer is worth re-reading as a lesson rather than as an item.** Pointer 5 read level 10's first
nine depths and concluded *"the descent 62 → 28 says the key is not the problem"*. Over 64 depths the key
bottoms at 22 by d=18 and then **regresses to 28 at d=36 and holds it for the final 28 depths**. **Nine
depths of descent is what a working key and a stalling key look like identically.**
