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
`Engine.cs` still differs from a literal transliteration by the word `partial` -- on the class, and
(since Phase 5 step 3) on `SoundPlay`, whose empty body moved to `Engine.Sound.cs` -- and
`Engine.Search.cs` has not changed since layer 0. If a solver change seems to need an engine change,
stop and re-read.

Step 3 also restored three `SoundPlay` calls Phase 2 had read as paint (`S_Move`, `S_EndLev`,
`S_Die`). **Nothing the search touches changed:** `SoundPlay`'s body is `SoundLog?.Add(sn)` and
`SoundLog` is null unless a driver opts in, which the solver never does, so a search pays one null
test per call and allocates nothing. The corpus was re-verified with the change in -- 187/181/112,
2,347/2,347, 208/208 -- and the sound ids themselves are now a trace field the oracle emits too
(`--sound`), gated by `tools/sound_check.py`.

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
half. `STRIDE=1` is the whole 20,914-level corpus and is hours.

**The passes' `NODES=150000` is not optional, though it reads like it.** `second_pass.sh`
defaults to **1M**, 6.7x the campaign budget, and every pass number in this file is measured at
150k -- the 441/444/472 composites, the per-tier curve below, the +44/+30/+3 in
`second_pass.sh`'s own header. Session 25 ran the first pass at the default over the whole
population by accident and it came back **147 of 3,787** against the table's 44 -- 3.3x the
levels for 6.7x the nodes, which is not a better chain, only a different budget, and is not
comparable with anything here. (That run is kept as `build/reports/l3n-1m.jsonl`; it is the
cheapest evidence on file that the shipped chain is budget-bound rather than structure-bound,
and it is an argument for *Next actions* item 1.) The variable now lives in the command:

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

**`--sg-eval coarse` on the middle pass is session 27's, and it is not a tuning knob — it is the
key that pass has been using since `4765ae9` without saying so.** `Rank()` read `_eval != null`
rather than its caller's flag, so a bare `--subgoal` ranked by the learned evaluation rounded to
work units. That is now a key with a name, and writing it here makes the command mean what it ran.
Naming it also made the *third* pass a different searcher for the first time — `learned` used to be
the same one — which is where **476 -> 494** comes from. *Next actions* item 1.

`report_stats.py` reads any of those reports (`--diff` compares two layers); the composite is the
*union* of the four, which is why the two verify runs are separate. A level solved by an earlier
pass is skipped by a later one, so no `.lpb` is ever written twice.

---

## Six rules the sessions each paid to learn

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
- **A flag that gates nothing looks exactly like a feature that does nothing.** `--sg-eval` gated
  nothing for nine commits: `Rank()` asked `_eval != null` and `_eval` is built when *either* beam
  wants the model, so plain layer 3 and `--sg-eval learned` were one searcher. Session 25 measured
  them identical *to the node* — the right observation — and attributed it to the divide in
  `Eval.Score`, which was also real and also a defect, and stopped there. **When a searcher and its
  control come out node-identical, at least one of them is not what its name says; check both ends
  before believing the feature is inert.** The cheap version of that check is the equivalence test
  layer 4 already documents — run it, and run it against a control you have separately proved is a
  different searcher.
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
| Layers 0-4, the chain | **494 of the 4,185-level stride sample (11.8%)** -- session 27, with layer 4's two ranking defects fixed and the chain's middle pass carrying `--sg-eval coarse`. A **strict superset** of session 25's 476 and of every intermediate configuration measured, 96 of 96 new solutions through the two-engine gate. See *Next actions* item 1 for the three chains that were compared and why this one |
| Layer 5 — push macros | board-change search. Ferry bench **18/50**, deep **25/50** on the session-25 lists (9 and 17 in the session-17 configuration); 20/50 and 21/50 on the lost originals |
| Layer 6 — the read | derives what is in the way and what can change it. Ships inside layer 5's rung (15/50 against 9/50 without it). Its anti-tank-on-the-route rule is `--read-antitank-wall`, **off by default** — see *Layer 8* |
| Layer 6's fourth derivation | *what does this change make possible?* — `--push-enables`; own rung, adds 4 ferry / 5 deep, **solves `LaserTank.lvl` 1 in 67 s with no flags** |
| Layer 7 — the stop cell | *what must be blocked before the tank can stand next to the flag?* — own rung with `--push-shot-run`, adds 3 ferry / 5 deep, **solves level 2 in 65 s with no flags** |
| Layer 8 — reading the board | six derivations (fire map, safe flood, frozen block, ferry assignment, ferry maze, shield). **Two rungs**, adding 3+1 ferry / 6+2 deep; **solves level 8** (57.5M nodes, width 512) |
| Layer 5 over the corpus | **15 of 255 (5.9%)** of the levels the whole chain fails, at 27x the campaign budget — an argument for a fourth pass, not for changing the chain |
| The fourth pass, rehearsed | **82 of 250 (32.8%)** of the levels the chain fails, as the **union of five arms at 40M nodes** on a 1-in-15 stride of the whole failure population — session 26, 269 of 269 solutions through the two-engine gate. No single arm scores above **61 (24.4%)**, so the pass is a chain of arms rather than a configuration. *Next actions* item 2 |
| `LaserTank.lvl` 1-10 | **1-5 and 7-9 solved**, banked in `data/solutions/` (any one of them may be temporarily deleted for a manual re-run — see *Next actions*, item 3, and the shorter files for 8 and 9 it lists); **6 and 10 open**, and 10 is no longer a budget case — two 900M-node runs came back unsolved at depth 2 |

*One number moved for a reason worth knowing before trusting the rest: the ferry bench is **19/50**
where earlier sessions banked 20/50. That is session 20's buried-flag fix, attributed rather than
assumed — reverting it alone reproduces 20/50 exactly, so everything else since is inert at default
flags, and the level it costs (`Beginner-I` 1581) never buries a flag on its own line. A correct
ranking that costs one level is still the correct ranking; compare future runs against 19.*
*(Session 25: that list is gone and the regenerated one gives **18**, so 18 is the number to compare
against from here — the two are not the same fifty levels and the difference is not a code change.)*

The per-tier and per-collection curve at 150k nodes:

| tier | levels | layer 0 | + L1 pass | + L2 | + L3 | + L4 | median ratio |
|---|---:|---:|---:|---:|---:|---:|---:|
| Kids | 960 | 303 (31.6%) | 319 | 339 | 341 | **359 (37.4%)** | 1.6× |
| Easy | 2,118 | 84 (4.0%) | 89 | 94 | 95 | **104 (4.9%)** | 1.7× |
| Medium | 784 | 7 | 7 | 7 | 7 | **8 (1.0%)** | 1.6× |
| Hard | 257 | 0 | 0 | 0 | 0 | **0** | — |
| Deadly | 56 | 1 | 1 | 1 | 1 | **1** | — |
| **all** | **4,185** | 395 (9.4%) | 416 | 441 | 444 | **472 (11.3%)** | **1.6×** |

That curve is the original attribution and is kept as history: its per-pass columns are the
pre-`4765ae9` searchers, and two of them no longer exist under those names. The shipped chain's
per-tier state today (session 27, `coarse` -> `learned`, `build/reports/fix2-chain.jsonl`):

| tier | levels | solved | rate | median ratio |
|---|---:|---:|---:|---:|
| Kids | 960 | **374** | 39.0% | 1.5× |
| Easy | 2,118 | **110** | 5.2% | 1.6× |
| Medium | 784 | **9** | 1.1% | 1.5× |
| Hard | 257 | 0 | — | — |
| Deadly | 56 | 1 | 1.8% | — |
| **all** | **4,185** | **494** | **11.8%** | **1.5×** |

**Re-measured from scratch in session 25**, then re-attributed in session 27 once `--sg-eval` was
found to gate nothing. Same 150k budget, same `build/reports/l0.jsonl` throughout -- layer 0 never
touches `Rank()`, so only the two middle passes move:

| chain | layer 0 | + pass 2 | + pass 3 | + L1 pass | composite |
|---|---:|---:|---:|---:|---:|
| the four passes as banked (pre-`4765ae9`) | 395 | +44 | +30 | +3 | **472 (11.3%)** |
| session 25, this tree | **398** | `--subgoal` **+73** | `--sg-eval learned` **+0** | +5 | **476 (11.4%)** |
| session 27, `work` -> `learned` | 398 | **+44** | **+39** | +3 | **484 (11.6%)** |
| **session 27, `coarse` -> `learned` -- shipped** | 398 | **+73** | **+20** | +3 | **494 (11.8%)** |

Kids 374 (39.0%), Easy 110 (5.2%), Medium 9, Hard 0, Deadly 1; 96 of 96 new solutions through the
two-engine gate, zero divergences, and the 494 is a **strict superset** of both 484 and 476.

**Session 25's open question is closed, and the answer was not in `Heuristic.cs`.** Its layer-3 pass
scored 73 where the attribution says 44 because `Rank()` tested `_eval != null` instead of its
caller's flag: a bare `--subgoal` was already ranking by the learned evaluation, quantised to work
units by the divide in `Eval.Score`. Restore true `WorkDistance` and the pass is **44 again, exactly**
-- the third row above. So all three numbers in this table are real and they are three different
searchers, not one searcher measured three times.

Two things that fall out of it and are worth more than the composite:

- **The accidental key is the best of the three.** 73 against `WorkDistance`'s 44 over the same
  3,787 failures, 33 levels only it solves against 4. It ships as `--sg-eval coarse`.
- **`WorkDistance` is now dominated outright.** A `--sg-eval work` pass appended after
  `coarse` -> `learned` adds **0** -- every level it can reach is already in the union. Layer 2's
  own key survives in the code as the acceptance test's `work` input and as `coarse`'s largest
  term; as a *ranking* for this pass it is retired.

`build/reports/chain.jsonl` is rebuilt — by `tools/chain_union.py` now, so the recipe is a script
rather than a sentence — and the three `bench/` lists are back, regenerated and committed.

**The unsolved are unsolved on budget, not on structure:** 3,636 of layer 0's 3,790 failures stopped
at `budget` (95.9%), only 154 at a beam dead end. No errors, no `NOTPORTED`, no crashes in 8,370
level-solves. `Hard` at 0/257 is not the search failing to find a route, it is the search never
getting near the end of one. **Depth is the binding constraint** and every layer since has been an
attack on it.

---

## Next actions

> **The state of the tree and of `build/`, as session 27 left it.** Read this before running
> anything, because two of the names below changed meaning.
>
> * **Session 27's code changes are in the working tree and not committed.** Michal writes the git
>   history; the solver is built and published (`build/lasertank-solve.exe`), and `bench/seed-weights.txt`
>   is a new untracked file that belongs in the next commit with the rest.
> * **`build/reports/` is rebuilt under the recipe's own names**: `l0.jsonl`, `l3c.jsonl` (the
>   `--sg-eval coarse` pass, 73), `l34.jsonl` (`--sg-eval learned`, 20), `l34pass4.jsonl` (macro, 3),
>   and **`chain.jsonl` = 494**. Session 25's four are kept beside them as `*-s25.jsonl` — note that
>   `l34.jsonl` and `chain.jsonl` are *not* the files of that name any session before 27 wrote, so a
>   number quoted against them rebases. `build/solutions/l34` holds the 96 the three passes added,
>   all through the gate.
> * **The push benches live in `build/bench/{ferry,deep}-{coarse,learned,work,none,hs128,hs157}.jsonl`**,
>   which is what item 1's two tables are computed from via `tools/arms_union.py`.
> * **`LT_SOLVE=<exe>`** overrides the binary in `bench.sh` / `campaign.sh` / `second_pass.sh`, which
>   is how a change gets benched while a long solve holds `build/` open.

> **Session 24's item 0 is done — session 25.** The layer-0 campaign and all three passes have been
> re-run on this machine, `build/reports/{l0,l3n,l34,l34pass4,chain}.jsonl` and
> `build/solutions/{l0,l34}` are rebuilt, and the three level lists are regenerated and **committed
> to `bench/`** with the rule that made each one in its own header. Items 1-4 below are runnable
> again. Two caveats that travel with them:
>
> * **The lists are reconstructions, not recoveries.** Same rules, different levels, so every bench
>   number ever quoted against them rebases — the *19/50 and 21/50* check in item 2 has nothing to
>   be equal to any more, and the honest version of it is to re-measure and write the new pair down.
>   `ferry-levels.txt` contains 1581, which this file names, so the rule is at least the right shape.
> * **`bench-levels.txt` does not reproduce its old character.** It was GAUNTLET-heavy with almost
>   no ferry; every candidate rule over this campaign's `Beginner-I` failures comes back FERRY 30 of
>   60. Part is the population and part is layer 8's barrier fix moving 164 rows out of GAUNTLET, so
>   the old label was a pre-fix read. Levels were not hand-picked to match the old description.

**1. Layer 4's learned evaluation did not act. Two defects, both fixed — session 27.**
*(Done, session 27. The chain question is settled — `coarse` -> `learned`, 476 -> **494** — and
what is still open is the push side, which the benches below split by population and item 2's arms
are the place to decide.)*

**Defect one was the divide, which session 25 found.** `Eval.Score` ended with `s /= Scale` to hand
the beam a number in work units. Integer, and the model's whole dynamic range is smaller than one
unit of its own output — the `work` weight is 157, i.e. 0.15 of a key per unit of `WorkDistance` —
so the ranking rounded away and the minimum tied in 786 of 815 instrument groups. `Eval.Score` now
leaves the score in fixed point and every caller works there: `Rank()`'s non-learned branch is
`work * Eval.Scale`, layer 3's jitter is `Eval.Scale * Jitter()`, and `PushH`'s hand-built addends
are lifted by `--push-hand-scale` (default: the fitted `work` weight — see the sweep below). `--push-trace`'s
`best=` column and `--push-line`'s `line-h` divide back before printing, so every reading of them
in this file is still in work units.

**Defect two is why the divide looked like the whole story, and it is the larger one.** `Rank()`
asked `_eval != null`, not its caller's flag, and `Search.cs` builds `_eval` when *either* beam
wants the model while `PushLearned` defaulted to true. **So from `4765ae9` on, the subgoal beam
ranked by the learned key whatever `--sg-eval` said.** Session 25's "`--sg-eval learned` is
identical *to the node* to plain layer 3" was correct and had a second cause it did not look for:
they were one searcher. Two consequences beyond the pass:

* **`--sg-eval` gated nothing for nine commits**, so every "layer 2", "layer 3" and "layer 4"
  number measured on this tree since is a number for the same ranking.
* **The driver ran a duplicate rung.** `Auto.cs`'s `layer 3` and `learned` rungs were the same
  search, so one lane of the portfolio was spent twice. Fixed.

**The accidental key is not a degenerate one, so it is now a key of its own.** Rounding a learned
score back to work units is *learned score, ties broken by fewest keypresses* — which is exactly
what *Pointers* item 4 guessed the push rungs were really sorting by. It is `coarse`, and both
`--sg-eval` and `--push-eval` now take `work|learned|coarse|none`:

| key | what it is |
|---|---|
| `work` | `WorkDistance` (x `Eval.Scale`). What layers 2-3 are documented to use |
| `coarse` | `Eval.Score` rounded to work units. What every run from `4765ae9` to now actually used |
| `learned` | `Eval.Score` at full resolution. What layer 4 was fit to be |
| `none` | H = 0, so `Cut()` orders by `Tier` then `G`. The control that had never been run |

**`coarse` is the push default and reproduces the old binary to the node** — 0 of 50 differing on
the ferry bench, 0 of 50 on the deep bench, and `ferry 18/50 / deep 25/50` at the shipped flags,
which are this file's rebased baselines. **Nothing layers 5-8 measured has moved.** The subgoal
default is `work`, which restores what layers 2-3 say they do; the chain carries the flag it wants.

**The push side, benched as this file asked — and the answer is not the one item 1 predicted.**
Four keys, both lists, 4M nodes, 16 jobs, on top of `--no-ida --no-beam --push --push-read`:

| push key, 4M | ferry solo | only it | deep solo | only it |
|---|---:|---:|---:|---:|
| `coarse` — the shipped key | **18** | 0 | 25 | 1 |
| `learned` — the fix | 15 | **0** | **28** | 2 |
| `work` | 15 | 1 | 19 | 0 |
| `none` — H = 0 | **18** | **6** | 19 | **3** |
| greedy union of the four | **25** | | **33** | |

Three things in that table, in the order they change what to run:

* **`none` is the most complementary key on both lists and never wins solo.** It adds **+6** to the
  ferry union and **+4** to the deep one in greedy order, where the shipped single arm is 18 and 25.
  *Pointers* item 4 asked whether `none` would *reproduce* `learned`; it does better than that — it
  is a different searcher of the same strength, and the pair is worth a quarter more than either.
  **The three-arm fourth pass in item 2 should be benched with a `none` arm before it is started**;
  it is the cheapest arm in the set to add and the only one with evidence of complementarity from
  two independent lists.
* **`none` does not buy back the wall clock, and that kills half of item 10's motivation.** 171k
  nodes/s against `work`'s 173k and `coarse`'s 163k. The tiers still need everything `PushH`
  derives — the flag Dijkstra, the fire map, the matching, `_lastDead` — so H = 0 turns off the
  *ranking*, not the cost. Item 10's memoisation is still the way to that.
* **`learned` splits by population and is dominated on one of them.** Ferry: 15 solo and **0
  exclusive** against `coarse`'s 18 — strictly worse. Deep: 28 against 25, best solo in the set.
  Two benches, opposite orders, which is this file's first rule verbatim. The push side is a
  corpus question and item 2's arms are where it gets answered.

**The one scalar, swept — and `Eval.Scale` is the wrong value for it.** `--push-hand-scale` prices
one work unit of the ferry/stop/dead/shield terms against the learned score. `Eval.Scale` = 1024 is
their historic relation, but the learned key prices a work unit at its own `work` weight of **157**,
so at 1024 the hand terms are 6.5x heavier than the key they are added to — which is the same
units error as the divide, one level out.

| `--push-hand-scale` under `--push-eval learned` | 128 | **157 — now the default** | 1024 (`Eval.Scale`) | 8192 |
|---|---:|---:|---:|---:|
| ferry-levels | **18** | **18** | 15 | 16 |
| deep-levels | **28** | 27 | **28** | — |

**157 is not a fitted number, it is `Weights.cs`'s `work` weight**, i.e. the value that makes a work
unit of the hand terms cost what the learned key itself charges for one. At it, `learned` scores
**18 / 27** against `coarse`'s **18 / 25** — the first configuration in this table that is not worse
than the shipped key on either list. `Eval.Scale` costs three ferry levels for nothing, so if the
push side ships a learned key at all it ships with this scalar set — so `--push-hand-scale`
defaults to it, derived as `Weights.Default[work]` rather than written down, so that a refit moves
it. Verified node-identical to `--push-hand-scale 157` on all 50 ferry levels.

Two cautions before that reads as a result. It is **two benches of fifty**, and the swept arms carry
**0 exclusive levels** on either list — 157 and 128 are inside `coarse`'s and `learned`'s unions, so
what the scalar buys is a better *single* arm and not a better union. The union on both lists is
still driven by `none`: ferry 18 -> 24 with `coarse`, deep 28 -> 32 with `none` then 33 with
`coarse`. **The scalar is worth setting; it is not worth a campaign on its own.**

**What the chain does with the two keys — three configurations, one population, one budget.**
`build/reports/l0.jsonl` is reused throughout: layer 0 never calls `Rank()`, so only the middle
passes move. All at 150k, all through the two-engine gate.

| chain | pass 2 | pass 3 | + macro | composite |
|---|---|---|---:|---:|
| session 25, as it shipped | `--subgoal` +73 | `--sg-eval learned` +0 | +5 | 476 |
| `work` -> `learned` | +44 | +39 | +3 | 484 |
| **`coarse` -> `learned`** | **+73** | **+20** | +3 | **494 (11.8%)** |

**494 is a strict superset of 484 and of 476**, which is unusual for a re-ranking and is worth the
sentence: it happens because pass 2 is unchanged from session 25's and pass 3 only ever attacks what
pass 2 failed, so nothing is re-ranked out of the result. The `work` chain is *not* a superset of
476 — it loses 10 and gains 18 — which is layer 4's founding finding again.

**And `WorkDistance` is retired as a ranking for this pass.** A `--sg-eval work` pass appended after
`coarse` -> `learned` adds **0**: every level it can reach is already in the union. This is
derivable from the three reports rather than run, because a second pass attacks exactly the previous
one's failures, so the levels it would add are exactly the ones it solves and `coarse` does not.

**What this cost, stated plainly.** Nothing in `Weights.cs` changed and nothing was refit; the model
was correct all along and two lines of arithmetic between it and the beam were not. The +30 this
file credited layer 4 with was never measured through the path that ships (`Weights.cs` and the
divide arrived in the same commit, `8752317`), and it is still not recovered — **+20 is what the
learned key is worth on this tree**, on top of a pass 2 that is itself a learned key.

**2. The fourth pass — rehearsed in session 26, and the rehearsal changed the shape of it.** The
decision pass came out positive — 15 of 255 (5.9%) of the levels the shipped chain fails — so the
open question was never *whether* layer 5 pays but *how much of the corpus is worth spending on it*.
One push expansion is a whole closure, so this is the one pass budgeted in tens of millions of nodes
rather than hundreds of thousands.

**All five arms have now been run at `SAMPLE=15`** — a 1-in-15 stride over the whole 3,709-level
failure population, 250 levels every arm was given, `NODES=40000000 BUDGET_MS=1800000 JOBS=16`,
each arm on the whole machine in turn. **269 of 269 solutions through the two-engine gate, zero
divergences.**

| arm | flags on top of `--no-ida --no-beam --push --push-read` | solo | only it solves | greedy union |
|---|---|---:|---:|---|
| layer 8, work key | the layer-8 set plus `--push-eval work` | **61** (24.4%) | 5 | 61 |
| layer 7 | `--push-stop 1 --push-shot-run 16 --push-beam 128` | 54 | **8** | +14 → 75 |
| layer 6's fourth derivation | `--push-enables 8` | 52 | 2 | +5 → 80 |
| layer 8, learned key | `--push-reach --push-ferry-match --push-ferry-maze --push-dead 20 --push-fire 8 --push-shot-run 16 --push-beam 128 --max-keys 5000` | 57 | 2 | +2 → 82 |
| plain | — | 38 | **0** | **+0 → 82 (32.8%)** |

*(Session 27: every arm here that does not say `--push-eval work` ran on the default key, which is
now spelled `coarse` — the same searcher, so the table stands. What has changed is that there are
now two more keys to put in it: `learned` at full resolution, and `none`, which on both benches is
the most complementary of the four. See item 1.)*

Note the raised `--max-keys`: 1,200 is a silent cap on any level whose solution runs long, which is
exactly the population this pass is.

**The union is 82 of 250 (32.8%) where the file's fourth-pass argument rests on 5.9%.** Read the
comparison honestly: the 15/255 was `Beginner-I` alone at ~4M nodes, this is all thirteen
collections at 40M, so both the population and the budget differ and what it licenses is *40M buys
much more than 4M*, not a 5.5x improvement in the searcher.

Four things the arms say that their solo counts do not:

* **The best single arm is not the pass.** `l8work` solves 61; the chain of three solves 82.
  Shipping the winner alone costs 21 levels, a quarter of the result. This is layer 1's founding
  finding a fourth time, and it is why the instruction was *compare unions*.
* **Two arms are dead weight — and it took all five to know which.** `plain` contributes **0**
  exclusive levels and **+0** to the union, and `l8learned`, second-best solo at 57, adds **+2**
  because it overlaps `l8work` in 50 of 68. After two arms `plain` still held 4 exclusive levels
  and after three it held 1; only the fifth arm retired it. **The full run wants three arms —
  `l8work`, `layer7`, `enables` — for 80 of the 82.**
* **`--push-eval work` beats the learned key on the push side, which corroborates item 1 over the
  corpus rather than over a bench.** 61 against 57 solo, 11 exclusive against 7 head-to-head. Item
  1's reason for not shipping the scale fix is that `Push.cs:167` mixes `Rank()`'s output with
  work-unit addends; this says that mixing is already costing the learned key *today, at the
  shipped weights*. Item 1's "the push side needs the two benches first" now has a population
  behind it.
* **Layers 7 and 8 are validated over the corpus for the first time.** They were carrying two
  benches and an ablation. Layer 7 is the most complementary arm in the set — 8 exclusive and +14
  in greedy order on top of the best arm.

What the rehearsal does **not** show, stated plainly: **`Hard` and `Deadly` are 0 of 21 in every
arm**, so none of this touches the two tiers that have never fallen; and **98.8% of the 168 the
union misses still stop on `budget`**, so even the best arm is nowhere near a structural ceiling at
40M. Extrapolated to all 3,709 the union is ~1,216 levels and a composite near **1,690 of 4,185
(~40%)** against 476 today — a stride-sample estimate, not a promise, and the sample is 250 levels.

**The full run has not been started; it is a multi-day machine commitment.** Priced from the
rehearsal rather than guessed: an unsolved level at 40M nodes costs a median **275 s** at 16 jobs
(117 s near-solo — the search is bandwidth-bound, and effective parallelism is only ~6.4x at 16
jobs, so buying more jobs does not recover it). That is **~18 h per arm** over the full population,
so the three-arm pass is **~54 h**.

**Before starting it, add a fourth arm and re-price.** Session 27's push benches put `--push-eval
none` at +6 on the ferry union and +4 on the deep one, from a solo score that never wins — the same
signature layer 7 has, and layer 7 is the most complementary arm in the table above. An arm is
~18 h, so the question is worth a `SAMPLE=15` rehearsal of `none` against the existing three
before committing 54 hours to a set chosen without it.

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
      --no-ida --no-beam --push --push-read "$@"
  python tools/verify_solutions.py "build/l5/$arm"
}
run l8work  $L8 --push-eval work
run layer7  --push-stop 1 --push-shot-run 16 --push-beam 128
run enables --push-enables 8

python tools/arms_union.py l8work=build/reports/l5-l8work.jsonl \
    layer7=build/reports/l5-layer7.jsonl enables=build/reports/l5-enables.jsonl
```

Run `l8work` first — it is the largest single result, so it lands earliest if the run is
interrupted. `build/reports/chain.jsonl` is what all three are pointed at; if it is gone,
`tools/chain_union.py` rebuilds it from the four chain reports rather than a hand-retyped union.
**Session 27 rebuilt it and it now reads 494, not 476** — the rehearsal's 82 was measured against
the old one, so the full run attacks 18 fewer levels than the rehearsal did and the extrapolation
below is very slightly optimistic. Session 25's reports are kept beside it as `*-s25.jsonl`.
**Interaction with item 7:** that item draws the solved-vs-budget curve for the *chain's* four
searchers and notes the push rungs become affordable as a fifth pass at 50M. These arms are that
fifth pass, already measured at 40M — run item 7 first if the question is the production curve,
this if the question is what layer 5 adds.

**And the benches as a check, not as a decision — rebased in session 25.** The lists are
reconstructions, so the old pair has nothing to be equal to; these four numbers are the new ones,
measured on the committed lists at 4M nodes and 16 jobs:

| `bench.sh` at 4M | ferry-levels | deep-levels |
|---|---:|---:|
| `--no-ida --no-beam --push --push-read` | **18/50** (was 19 on the old list) | **25/50** (was 21) |
| ...plus `--push-beam 48 --push-per-board 0 --push-eval work --push-depth 400` | **9/50** (was 11) | **17/50** (was 14) |

The shape survives the rebase, which is the only thing the old pair was being used for: the shipped
width beats the session-17 configuration on both lists, by 9 on ferry and 8 on deep.

**3. Refresh the banked solutions.** *(Unblocked: `build/solutions/l0` and `l34` are rebuilt, 476
solutions.)*
`Trim.Polish` removes 47% of a subgoal solution's keypresses and
`Replan.Improve` another slice on top, so **every banked `.lpb` under `build/solutions/` is longer
than it needs to be** — they all predate the replan pass. `--polish DIR` runs both over each of them.
This is not cosmetic: shorter trajectories change the ascent statistics the whole layer-5 argument
rests on, and they are what layer 4 is fit on. Measured on the 416 solutions in
`build/solutions/l0`: 11,060 → 10,249 keypresses in about 150 s.

**4. Levels 8 and 9 — Michal re-banks these himself, and a missing `.lpb` is not a missing solution.**
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

**5. Levels 6 and 10** — see *What has not fallen, stated plainly*. Both now want the same thing, and
it is not budget. 6 wants layer 2's decomposition one level out: commit to one block-and-hole pair,
search only for that, re-derive. **10 was the "just buy the nodes" case until session 23, when the two
900M-node runs session 22 left going came back unsolved at board-change depth 2** — so its ~1,000-pose
closure, not its width, is what needs attacking. Do not spend another overnight run on it as-is.
**Session 26 disputes the premise for 10:** at width 1024 with the layer-8 flags the beam costs
~5.7M nodes a depth and reached depth 9 in 39M, so 900M is ~150 depths, not 2. Item 8 below is
the one traced run that settles it, and it comes before any decomposition.

**6. Still worth doing, no longer blocking: the `lasertanksolutions.blogspot.com` goal-board
harvester.** See *Open question* at the end.

Items 7-12 are session 26's, distilled from *Pointers from a second reader* (next section), which
carries the evidence and the caveats for each. They are ordered by expected value per hour; 7 and 8
are runs, not code.

**7. Draw the solved-vs-budget curve over the short-record failures, and bank what it solves.**
338 unsolved levels in the sample have a `.ghs` record of ≤40 moves+shots, 715 of ≤60, and 95.9% of
failures stop on `budget`. This is the production number the whole project is measured by, and it
has never been run above 150k except by accident (`l3n-1m.jsonl`, 3.3x the levels). One list, three
budgets, every solution through the gate:

```bash
# the unsolved with a short record, from the chain's final state
python - <<'EOF'
import json
rows=[json.loads(l) for l in open("build/reports/chain.jsonl",encoding="utf-8")]
with open("bench/short-record-failures.txt","w") as f:
    for r in rows:
        if not r["solved"] and 0 < r["ghs_moves"]+r["ghs_shots"] <= 60:
            f.write(f'{r["collection"]}\t{r["level"]}\n')
EOF
# the chain's own four searchers, in its order, each pass over what the previous one failed
for N in 1000000 10000000 50000000; do
  R=build/reports/curve-$N; S=solutions/curve-$N
  NODES=$N bash tools/second_pass.sh build/reports/chain.jsonl $S $R-l0.jsonl  --no-ida
  NODES=$N bash tools/second_pass.sh $R-l0.jsonl  $S $R-l3.jsonl  --no-ida --no-beam --subgoal
  NODES=$N bash tools/second_pass.sh $R-l3.jsonl  $S $R-l4.jsonl  --no-ida --no-beam --subgoal --sg-eval learned
  NODES=$N bash tools/second_pass.sh $R-l4.jsonl  $S $R-l1.jsonl  --no-ida --no-beam --macro --macro-first
  python tools/verify_solutions.py build/$S
done
```

`second_pass.sh` re-attacks a report's failures, so the whole 3,709 run and `--order ghs` puts the
short records first; `SAMPLE=` or a list through `bench.sh` if only the ≤60 population is wanted.
Report the solved count per budget as a table in *Status*. **Keep 150k for attribution; this is a
different question**, and at 50M the push rungs (`--push --push-read`, item 2's five arms) become
affordable as a fifth pass.

**8. Level 10, one traced run before anything is built for it.** `--push-trace` at width 1024, the
`push-ferry-work` flags (item 4's level-9 command, `--push-beam 1024`, `--push-restarts 0`), 400M
nodes, `--jobs 1`, and read the `d=` column. If it climbs past 50 and does not win, level 10 is a
ranking problem at that width and item 11 applies; if it stalls at a small depth, find the
multiplier (`--push-enables`, `--push-read-opens N>0`) and record the flags in the report this
time. 39M nodes reached depth 9 in session 26 with best 62 → 28.

**9. A `--max-round N` for the driver, then run it unattended.** `Auto.cs:609` has no round cap, so
the portfolio cannot be pointed at a collection and left. With the cap, `--lanes 4 --max-round 3`
over `Beginner-I` is the overnight run that gives every rung its chances at 25.6M and banks to
`data/solutions/` through the gate. This is item 7's counterpart for the rungs the chain does not
contain.

**10. Wall clock on the push rungs: profile, then memoise `PushH` per board.** 166k nodes/s on the
shipped push rung against 1.4M on layer 0, and the difference is heuristic work repeated for every
pose of the same playfield. First `dotnet-trace` one `--push --push-read --push-beam 8 --nodes
6000000 --jobs 1` run on `LaserTank.lvl` 10 to see the split; then cache the flag Dijkstra (and the
fire map, matching, `Feat` board terms) by `BoardKey` within an expansion, by `(BoardKey, tank
cell)` under `--push-reach`. **Measure in seconds, never nodes** -- the node count is identical by
construction, so every bench in this file will report no change.

**11. The closure-dominance prune, instrument first.** Add `sterile=` to `--push-trace` (expansions
that emitted zero fresh successors). If it is tens of percent on the ferry bench, add
`seen.UnionWith(local)` after every *untruncated* expansion in `ExpandPush` -- lossless, one line --
and key the per-board cap on `(BoardKey, TankRegion)` rather than `BoardKey`. Re-bench both lists;
expect the node count per solved level to fall rather than the solved count to rise.

**12. Per-level `MaxKeys` from the record.** `clamp(5 × (ghs_moves + ghs_shots) + 100, 1200, 8000)`,
default kept where the record is 0. Two silent truncations in this file already; the third consumer
of a number `--order ghs` and `--trim-ratio` already read.

Further out, and only after 7-9 have moved the number: a FESS-shaped rung for the Sokoban/ferry
half of the corpus (pointer 7), and subgoal chaining over board changes with the acceptance tests
that already exist (pointer 8) -- for 6 and, if item 8 says ranking, for 10.

---

## Pointers from a second reader — session 26

**What this section is.** A pass over this file and the solver source by a different model, looking
for things the layers above may have walked past. No code was changed. Seven short runs were made
(all `--jobs 1`, all on the tree as of this session) and every claim below is tagged: **measured**
means a number from one of those runs or from `build/reports/chain.jsonl`; **in the code** means a
fact read off the source with a file and line; **hypothesis** means neither, and says what would
settle it. Ordered by expected value per hour, not by how interesting each is.
**The actionable subset is *Next actions* items 7-12;** this section is the evidence behind them.

### 1. Spend the budget where the record says the level is short  (measured)

Every number in this file is quoted at 150k nodes so that layers can be attributed. That is the
right *measurement* budget and the wrong *production* budget, and the two have been conflated:
the goal is a solved count, the chain fails 95.9% of its levels on `budget`, and the one accident
that ran a pass at 1M (`l3n-1m.jsonl`) came back 3.3x the levels. `chain.jsonl` says where the
cheap ones are:

| unsolved in the 4,185-level sample | levels |
|---|---:|
| all | 3,709 |
| `.ghs` moves+shots **≤ 40** | **338** |
| `.ghs` moves+shots ≤ 60 | 715 |
| median record of an unsolved level | 149 |

Three hundred and thirty-eight levels whose best-known solution is under forty moves-plus-shots are
unsolved at 150k nodes. A keypress beam at 10M nodes on that population is 3.4G nodes — at the
layer-0 rate measured below, **a few minutes at 14 jobs** — and the same for the push rungs is
about an hour. This is the *solved-count-vs-budget curve, Kids-first ordered by `.ghs` cost* that
*The bar* says is how progress should be reported; it has not been drawn. Draw it: the whole chain
at 1M, 10M and 50M over the ≤60 population, banked and verified, then over the rest. The chain's
scripts already take `NODES=`; the only missing piece is the per-level ordering by record, which
`--order ghs` (the default) already does inside one collection.

### 2. The node budget hides most of a push rung's wall clock  (measured)

A node is one `ApplyKey`, and on layer 0 that is nearly all of the time. On the push rungs it is
not. `LaserTank.lvl` 10, width 8, 6M nodes, one thread:

| configuration | seconds | nodes / s |
|---|---:|---:|
| layer 0 beam, `Beginner-I` 1581, 4M nodes (process start included) | 2.8 | **~1.4M** |
| `--push --push-eval work` (no read) | 16.6 | 360k |
| `--push --push-eval learned` (no read) | 29.9 | 200k |
| `--push --push-read` (learned, the shipped rung) | 36.2 | **166k** |

*(Session 27: `learned` in these two rows is the key now spelled `coarse`. The rename does not move
the seconds — `Feat.Extract` and `Eval.Score` run either way; only the last arithmetic differs.)*

The same node count costs **2-8x the seconds**, and none of it is the engine: it is `PushH` per
emitted successor — a Dijkstra from the flag, the fire map, the reach flood, the ferry matching and
maze BFS, `Feat.Extract`'s seventeen features — plus a `TankRegion` flood per untiered successor in
the cheap `opens`. (The `work`/`learned` pair is not a clean ablation: the two beams walked different
boards, closure ~466 against ~1,022, so the split between key cost and board cost needs a profiler,
not this table.)

**Where it is recoverable:** the successors of one expansion are, in this file's own words, *four
boards wearing thirty-nine hats*. Everything in that list except the tank's own cell is a function
of the playfield alone — the Dijkstra runs *from the flag* and reads the tank cell off its table
(`Heuristic.cs:379-432`) — and it is recomputed for every hat. Memoise the table by `BoardKey`
within an expansion (by `(BoardKey, tank cell)` under `--push-reach`, whose flood starts from the
tank) and the per-successor cost collapses to a lookup for every duplicate. Node counts do not
move, so **no bench in this file can see this change** — it has to be measured in seconds. Two to
four times on the rungs that solve the hard levels is the plausible size; a profiler run
(`dotnet-trace`) on the width-8 level-10 run above is the instrument, and it comes before the
memo, not after.

### 3. A lossless prune the push beam does not take  (in the code; size is a hypothesis)

Every pose in an *untruncated* PF-preserving closure is dominated by the node it was expanded
from: movement closure is transitive, so whatever board changes are reachable from pose *p* are
reachable from the parent, and the parent has already emitted all of them. Yet `ExpandPush` adds
those poses only to `local` (`Push.cs`, the closure loop) — never to `seen`. So a later successor
whose state is *exactly* one of those poses — same board, tank somewhere else in the same
component — is fresh to the closed set, takes a slot in the width, and pays a whole closure
(~5,000 `ApplyKey` on level 10) to emit successors that are all already closed. Push a block right
and then, from another side, push it back; turn a roto-mirror through a cycle while the tank
moves between shots; any reversible pair on a Sokoban level: each is one of these. The per-board
cap does not catch it, because the parent board is not in *this depth's* frontier.

The fix is one line — after an expansion that did not truncate, `seen.UnionWith(local)` — and it
is lossless for the search (a dominated state offers nothing the dominator did not). Two caveats:
it must be skipped when the closure truncated (the parent did not finish emitting), and it changes
`G` tie-breaks, since the dominated state may have arrived with a shorter keystream — polish and
replan take that back after the fact. **Instrument first**, because how much it buys is the open
question: add `sterile=` to `--push-trace`, the count of expansions this depth that emitted zero
fresh successors. If it is a few percent, leave it; if it is tens of percent on the Sokoban and
roto levels, ship it and re-bench.

The same reasoning sharpens the per-board cap. Poses of one board are interchangeable *within a
movement component* and not across one, which is exactly the objection the cap's comment raises.
`Heuristic.TankRegion` already computes the component cheaply: key the cap on `(BoardKey, region)`
rather than `BoardKey`, and the cap stops being a number that occasionally drops the one pose on
the useful side of a newly cut map.

### 4. What `--push-eval learned` ranks by at the shipped weights  (in the code)

`Rank()` returns `Eval.Score` alone (`Learn.cs:291-296`), and `Score` divides by 1024. The `work`
weight is 157, so one unit of `WorkDistance` is worth **0.15** of the key, and session 25 measured
the minimum tied in 786 of 815 groups. On the push rungs, whose default this is, the beam is
therefore sorting on a coarsely quantised score and settling most of it by the `G` tie-break —
**fewest keypresses first**. Read layer 8's table again with that in mind: *6 and 8 want the
learned key* may mean *6 and 8 want fewest-keys-first among near-ties*, a uniform-cost flavour
over board changes, and no feature at all. That is testable for the price of a flag: `--push-eval
none` (H = 0, so `Cut` orders by `Tier` then `G`), and the two levels' `--push-line` ascent and
the two benches beside `learned` and `work`. If `none` reproduces `learned`, the shipped push key
is paying the wall-clock tax in item 2 for nothing, and *Next actions* item 1 has to be benched
against `none` rather than `work` on the push side.

The push-side fix for item 1 is smaller than the text there suggests: keep `Score` in fixed point
and multiply the work-unit addends in `PushH` by `Eval.Scale` — `Rank(work) + Scale * (ferry +
stop + dead + shield)`. Tiers sort before `H` and are untouched; the addends keep their exact
relation to each other; the one thing that changes is the ratio of learned score to hand terms,
which was never tuned because the learned term was inert. One scalar to sweep, on the two benches.

### 5. Level 10: this file's arithmetic and the machine disagree  (measured)

*What has not fallen* says two 900M-node runs at width ~1024 came back with the frontier at
board-change depth **2**, and concludes the ~1,000-pose closure makes every width unaffordable.
Measured on this tree:

| level 10, `--push-trace` | nodes per depth | reached |
|---|---:|---|
| width 8, plain push | 40,880 (= 8 × 1,022 poses × 5 keys) | depth 168 at 6M |
| **width 1024, the full `push-ferry-work` flag set** | **~5.7M** | **depth 9 at 39.3M**, best 62 → 28 |

At that rate 900M nodes is about **150 depths**, not two, and the level's hand line is 53. So
either those runs carried a multiplier this file does not record — `--push-enables` or
`--push-read-opens N>0` are the candidates, each a pose closure *per asked successor*, i.e. up to
64 closures of ~5,000 nodes on top of one expansion of 5,000 — or the depth was read through the
`d=`/`at=` trap. (Checked while here: the default `--push-read-opens -1` is the cheap flood, and a
run with `-1` and one with `0` are node-identical, so the read is *not* the multiplier at
defaults.) Either way the conclusion drawn from them does not stand on its own, and the level is
back to being worth exactly one properly instrumented run — `--push-trace` at width 1024-2048,
the layer-8 flags, 400M nodes, and read the `d=` column — before anything is built for it. The
descent 62 → 28 in nine depths says the key is not the problem on this configuration.

### 6. The driver cannot be run unattended, and that is the tool item 1 needs  (in the code)

`Auto.cs:609` — `for (round = 0; !won && !lane.Skip && !_quit; round++)` — has no cap. Pointed at
a collection, it stays on the first level it cannot solve until a human presses the lane's number.
A `--max-round N` (or a per-level wall clock) turns the portfolio into the batch tool it already
almost is: `--lanes 4 --max-round 3` over `Beginner-I` overnight, banking to `data/solutions/`
through the gate, is the production run — every rung, rounds to 25.6M, no hand-tuning — and it is
the honest denominator for "how much of this corpus can the ladder do".

### 7. Half the corpus is a Sokoban, and Sokoban has a solved literature  (hypothesis)

FERRY + SOKOBAN is 53% of the sample at 5.1% solved, and level 6's diagnosis — *a greedy
level-synchronous beam in a region where every successor of every held board is worse* — is the
textbook failure of beam search on Sokoban. The textbook answer is **FESS** (Shoham & Schaeffer,
*The FESS Algorithm: A Feature Based Approach to Single-Agent Search*, IEEE CoG 2020), the first
solver to clear all 90 XSokoban levels. The shape: project states into a small **feature space**
(boxes packed, connectivity = number of regions the player is cut into, room connectivity,
boxes out of plan); advance by *cycling through the occupied feature cells* and expanding the
best state in each; weight moves by "advisors" that say which pushes serve which feature. Two
things about it belong here. It is the general form of two devices this file arrived at by
measurement — the per-board cap (diversity across boards) and *commit to one block-and-hole pair*
(a progress cell searched on its own) — so the fit is not speculative. And it respects the
fidelity rule: the engine still generates every state; the features are `Heuristic.cs` quantities
that already exist (holes filled, `TankRegion`, ferry-maze distance, `RouteDead`), and the
advisors are the read's derivations under another name. A rung, measured as a union.

Its second gift is the packing order, derived backwards from the goal: which hole must be filled
before which, from where a block can still be pushed *after* the others are down. The greedy
matching in `--push-ferry-match` is the forward half of that; the backward half is what level 6's
strip of six holes wants.

### 8. Subgoal chaining over board changes, with the acceptance test already written  (arithmetic)

Both open levels ask for layer 2's decomposition one level out, and the pieces exist. The
acceptance test in `Subgoal.Offer` (`Subgoal.cs:344`) is a board test; for a gauntlet the board
test is *the safe flood gained a named route cell* (`--push-reach`'s flood, set inclusion rather
than count), and for a Sokoban it is *block b stands on the next cell of its maze path*
(`--push-ferry-maze`'s BFS already produces the path). A sub-search for one such subgoal on level
10 is width 64 × depth ≤ 6 × ~5,100 nodes per expansion ≈ **2M nodes**; ten shields is 20M, the
budget one round of the driver already spends. The outer search is over the *order* of subgoals,
depth-first with backtracking, re-deriving the read after each — at most 6! orderings on level 6
and mostly pruned by the matching. Cheap enough that the first thing to do is not build it but
run the arithmetic against `--analyze`'s output on the two levels: how many subgoals, and how deep
each is on the hand line.

### 9. `MaxKeys` from the record, not from a global  (in the code)

The 1,200-key default has silently truncated the end of a level twice in this file (level 6's
904-key hand line; the layer-8 rungs raising it to 5,000), and a `.ghs` record exists for every
level in the corpus. `MaxKeys = clamp(5 × (moves + shots) + 100, 1200, 8000)` per level, with the
default kept where the record is 0, removes the hazard without spending anything on the levels
that never approached it. `--order ghs` and `--trim-ratio` already read the record; this is a
third consumer.

### 10. The width ceiling is memory, and the memory is keystreams  (in the code; small)

`Snapshot` copies the whole consumed key prefix (`Engine.Search.cs`, `Array.Copy(RecBuffer,
s.Keys, s.KeyLen)`) and `Restore` copies it back, so on a 900-key line every node moves ~1.8 KB of
keys on top of its 1 KB of boards, and a `Node` holds all of it — which is why 76,800 wide was 1.1
GB. Parent pointers (each node keeps only the keys since its parent; the path is rebuilt on a win)
cut both the copy and the residency several-fold. Only worth doing if width is ever the wall
again; it was for level 9.

### Checked, and not opportunities

- **Duplicate boards across the corpus:** 20,914 levels, 20,914 distinct playfields. No solution
  transfers for free.
- **Record shots = 0 as a licence to drop the space bar:** only 17 of the 3,709 unsolved have a
  zero-shot record (and several of those are 0/0, i.e. no record at all). Not worth an action set.
- **The read's `opens` as level 10's hidden cost:** the default is the cheap flood; with it off
  the trace is node-identical. See item 5 for what the multiplier might be instead.

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

> **Session 27: the check is banked and it runs.** It lived here as prose and as a vector that could
> not be handed to `--eval-weights` as written; it is now **`bench/seed-weights.txt`**, in git beside
> the level lists, and `--sg-eval learned --eval-weights bench/seed-weights.txt` reproduces plain
> layer 3 on **0 of 50** deep-bench levels differing. The weights are written x `Eval.Scale`, which is
> not a workaround: `fit_eval.py` writes `round(w * SCALE)` (line 307), so that is the convention any
> weight file is read under, and `Rank()`'s non-learned branch is `work * Eval.Scale` for the same
> reason. The *unscaled* `{1, 1000, 1}` differs on 2 of 50 — close, because the vector is right and
> only the jitter beside it is then 1,024x too strong. Session 25 read the same file as a flat key
> because the divide was still there; it is not.

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
  term and everything layers 5-8 add is added outside it. (Session 27: that default is now spelled
  `--push-eval coarse` and the column is divided back to work units before printing; `learned` is
  the same model at full resolution.) The tell: a line that ends on the flag does
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

**Seven: the ranking key splits per level.** `--push-eval` defaults to the learned model, in which
`work` is one term at weight 157 and everything layers 5-8 add is added outside it; on `--push-eval
work` the key is `WorkDistance` plus the terms and therefore reaches 0 on a win. *(Session 27: the
default is now named `coarse` — same key, same numbers, byte-identical on both benches — and the
`learned` in this table means it. See Status.)*

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
                    Node-governed, not wall-clock.  STRIDE=N samples every Nth level.
                    LT_SOLVE=<exe> overrides the binary in all three runners below
                    as well -- a live solve holds build/lasertank-solve.exe open, so
                    `dotnet publish -o build` cannot replace it and this is the only
                    way to bench a change during a long run.  Same reason $LT_CORE
                    exists for the engine (PROGRESS)
second_pass.sh    re-attack a campaign's unsolved levels with a different searcher,
                    into the same solutions dir.  SAMPLE=N takes every Nth failure
bench.sh          one labelled configuration over one banked level list.  Its header
                    repeats the warning: a bench picks parameters, a campaign ships
report_stats.py   read a campaign .jsonl: per-tier and per-collection rates, stop
                    reasons.  --diff compares two layers
chain_union.py    union the chain's four per-pass reports into chain.jsonl -- the
                    shipped chain's per-level state, and what the fourth pass has
                    to be pointed at.  Was a sentence in this file until session 25
arms_union.py     compare several arms of one pass by what each *adds*: solo count,
                    levels only it solves, the cumulative union in greedy order, the
                    pairwise overlap matrix.  Session 26, because item 2 says to
                    compare unions and there was no tool that did
rankdump.py       layer 4's instrument: replay every winning .lpb and dump the group
                    of successors the shipped expansion offered at each shot boundary
fit_eval.py       read that dump.  Bare: the distribution.  --fit: fit and regenerate
                    Weights.cs (rebuild after — the vector is compiled in).
                    The one tool here that is not stdlib-only: needs numpy
basin.py          read a --profile dump: how far uphill a winning line goes, per level,
                    in keypresses and in board changes
verify_solutions.py  the gate.  Both engines, WIN on each, byte-identical traces
```

> **Session 25: `build/` has been rebuilt and the three level lists are committed.** Session 24
> found `build/` empty after a machine move -- it is gitignored, so `build/reports/`,
> `build/solutions/`, the three level lists and `chain.jsonl` had all gone with it, and none of the
> tuning tables could be re-checked. The campaign and all three passes have since been re-run
> (see *Status*), `chain.jsonl` is rebuilt by `tools/chain_union.py`, and the lists now live in
> **`bench/`**, in git, each carrying the rule that generated it. They are reconstructions, so
> bench numbers quoted against the old lists rebase.
>
> **The rule it paid for: a list that only lives in a gitignored directory is not banked.** Nor is
> a measurement -- layer 4's +30 was measured through a weights file in `build/` and does not
> reproduce through the path that ships (*Next actions* item 1).

The banked level lists live in **`bench/`**, committed — see the README there for why, and for what
belongs beside them. `bench/seed-weights.txt` lives there too and is not a level list: it is layer
4's equivalence check, the vector that *is* `WorkDistance` written in the features, in `Eval.Scale`
fixed point. It was prose in this file until session 27, and prose is not a check. The reports they are compared through stay in `build/reports/`, which is
gitignored and machine-local. The lists, all `Beginner-I` and all regenerated in session 25 with
their rules in their own headers: `bench-levels.txt` (60 levels layer 0 failed — described as
GAUNTLET-heavy when it was first cut, FERRY 30 of 60 through today's read, which is partly the
population and partly layer 8's barrier fix), `deep-levels.txt` (50 levels with a `.ghs` total of
40-150), and `ferry-levels.txt` (50 the chain fails that the read calls FERRY or SOKOBAN — banked
because the two older lists contain almost no ferry). `build/reports/chain.jsonl` is *not* one of them: it is the
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

**session 25 — the chain rebuilt on a bare machine, and layer 4 found inert.** Session 24's item 0,
start to finish: the layer-0 campaign and all three passes re-run at 150k from an empty `build/`,
`chain.jsonl` rebuilt (now by `tools/chain_union.py`, because the recipe was a sentence), and the
three level lists regenerated and **committed to `bench/`** with their rules in their headers.
**The composite reproduces: 472 —> 476 of 4,185**, 476 of 476 solutions through the two-engine gate.
Three findings, in the order they cost time:

- **The documented chain omitted `NODES` on its three passes**, so a copy-paste runs them at
  `second_pass.sh`'s 1M default — 6.7x the budget every number in this file is quoted at. The
  accidental run is kept (`l3n-1m.jsonl`): **147 of 3,787 against 44**, 3.3x the levels for 6.7x
  the nodes, over the whole failure population rather than a sample. The commands now carry the
  variable.
- **Layer 4's learned key does not act at the shipped weights** — identical *to the node* to layer 3
  on 255 levels at 150k and 50 at 400k, so its pass adds 0 where this file credits +30. `Eval.Score`
  divides the fixed-point score by 1024 and the model's whole range is smaller than one unit of its
  own output, so the ranking rounds away: the minimum ties in 786 of 815 instrument groups. Undone,
  the pass adds 17 and the composite is **491**. Measured and written down rather than shipped,
  because `Push.cs` mixes that score with work-unit addends. *Next actions* item 1.
- **The attribution inside the chain has moved even though the total has not**: layer 3's pass is
  +73 where it was +44, and that is *not* nondeterminism (the same pass twice is identical to the
  node). Unattributed, deliberately — a revert-one-thing run over the same population is what would
  settle it. *(Session 27 ran it: the +73 pass was not layer 3. `Rank()` read `_eval != null`
  rather than `--sg-eval`, so a bare `--subgoal` was already the learned key; restore
  `WorkDistance` and the pass is 44 exactly.)*

The session's own lesson is session 24's with a second example: **a measurement that lives only in
`build/` is not banked either.** Layer 4's +30 was real once and there is now no path in the tree
that reproduces it.

**session 26 — a second reader.** No code. A different model read this file against the source and
the machine and wrote *Pointers from a second reader* (after *Next actions*): the record-length
population the budget should be spent on first (338 unsolved levels with a record <= 40), the 2-8x
wall clock the node budget cannot see on the push rungs (166k against 1.4M nodes/s), a lossless
dominance prune the closure does not take, what the push rungs' `learned` key actually sorts by at
the shipped weights, and one arithmetic disagreement: level 10 at width 1024 runs ~5.7M nodes a
depth and reaches depth 9 in 39M, so the "depth 2 at 900M" reading needs a `--push-trace` before
it decides anything.

**session 26 — the fourth pass, rehearsed.** *Next actions* item 2, in a session parallel to the
second reader's above. All five arms run at `SAMPLE=15` — a 1-in-15 stride over the whole
3,709-level failure population, 250 levels each arm was given, 40M nodes, 16 jobs, each arm on the
whole machine in turn. **The union is 82 of 250 (32.8%)** where the fourth-pass argument rested on
5.9%, and **269 of 269 solutions passed the two-engine gate**. Three findings, in the order they
change what to run:

- **The pass is a chain of arms, not a configuration.** Best solo arm 61, union of three 82;
  shipping the winner alone would cost 21 levels. `plain` contributes **0** exclusive levels and
  `l8learned` **+2**, so the full run wants three arms for 80 of the 82 — but `plain` still held 4
  exclusive levels after two arms and 1 after three, so it took all five to retire it. **An arm's
  solo count does not predict what it adds, and no smaller experiment would have said so.**
- **`--push-eval work` beats the learned key on the push side over the corpus** (61 to 57 solo, 11
  exclusive to 7), which is item 1's `Push.cs:167` warning showing up as levels rather than as a
  bench. Item 1's decision not to ship the scale fix is better supported than when it was written.
- **Layers 7 and 8 have now seen the corpus**, which two benches and an ablation were standing in
  for. Layer 7 is the most complementary arm in the set.

The full run was priced from the rehearsal and **not started**: ~275 s per unsolved level at 16 jobs
(117 s near-solo — bandwidth-bound, ~6.4x effective parallelism), so ~18 h an arm and ~54 h for the
three. `tools/arms_union.py` is new and is what the union numbers come from; the instruction to
compare unions had no tool behind it.

The session's lesson is a small one about instruments: **the pricing probe was measured at the wrong
concurrency and was 2.4x optimistic**, because `SAMPLE=100` left each collection 1-4 levels and the
timings came out near-solo. A cost measured at a parallelism the real run will not use is not a cost.

And a citation worth having, found this session: **LaserTank is NP-complete** — Alexandersson and
Restadh, [arXiv:1908.05966](https://arxiv.org/abs/1908.05966), by reduction from 3-SAT, and the
hardness survives a board of only mirrors and solid blocks with the tank confined to a single
column. It offers the solver nothing algorithmically, and its **NP-membership half does not
transfer**: that holds for the restricted element set, and the paper conjectures PSPACE-completeness
with a richer one — which is the corpus. Worth knowing mainly because it says there is no polynomial
trick being missed, which is what *depth is the binding constraint* already says empirically.

**session 27 — the learned key acts, and the reason it did not was a flag that gated nothing.**
*Next actions* item 1, start to finish. Two defects between `Weights.cs` and the beam, neither in
the model: the integer divide in `Eval.Score` that session 25 found, and — the larger one —
`Rank()` testing `_eval != null` instead of its caller's flag, so that from `4765ae9` **every
subgoal run ranked by the learned evaluation whatever `--sg-eval` said**. The composite is
**476 -> 494 of 4,185 (11.8%)**, a strict superset, 96 of 96 new solutions through the two-engine
gate. Four things, in the order they change what to run:

- **Session 25's unattributed +73 is attributed, and it was not `Heuristic.cs`.** Restore true
  `WorkDistance` to the pass and it scores **44 again, exactly** — the number the original
  attribution claims. The three chain configurations are three different searchers.
- **The accident was an improvement, so it is now a key.** A learned score rounded to work units is
  a learned score with fewest-keypresses-first as its tie-break, and over 3,787 failures it solves
  73 against `WorkDistance`'s 44 with 33 exclusive against 4. It ships as `coarse`; both
  `--sg-eval` and `--push-eval` take `work|learned|coarse|none`. `coarse` reproduces the old binary
  to the node — 0 of 50 on each bench, and the *same 73 levels* over the corpus — so nothing layers
  5-8 measured moves. `WorkDistance` as a ranking for this pass is retired: appended after
  `coarse` -> `learned` it adds **0**.
- **`--push-eval none` is the most complementary push key on both benches and never wins solo**:
  +6 to the ferry union and +4 to the deep one, where the shipped single arm is 18 and 25. It is
  also the cheapest arm in the set to add, and item 2's three-arm pass should bench it before the
  54-hour run starts. It does *not* buy back wall clock — the tiers still need everything `PushH`
  derives — so item 10's memoisation is still the route to that.
- **The driver was running a duplicate rung.** `Auto.cs`'s `layer 3` and `learned` rungs were the
  same search from `4765ae9`, so one lane of the portfolio was spent twice. The `subgoal` rung now
  says `coarse` and the `learned` rung says `learned`; the first is what it was already doing, so
  the portfolio keeps its measured behaviour and gains a lane.

The session's lesson is the sixth rule at the top of this file: **a flag that gates nothing looks
exactly like a feature that does nothing**, and session 25 measured the symptom correctly, found one
real cause, and stopped. Two smaller ones worth keeping: the equivalence check layer 4 documents is
now `bench/seed-weights.txt` in git rather than a sentence, and it passes (0 of 50); and
`LT_SOLVE=<exe>` overrides the binary in the three runners, because the previous way to bench a
solver change during a long solve was to wait for it.
