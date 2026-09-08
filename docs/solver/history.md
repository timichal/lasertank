# Closed items, the corpus tables, and the session log

Cold storage for the solver. **Nothing here is open** — it is kept because a measurement that is deleted
gets re-measured and a negative result that is deleted gets re-run, which is `bench/`'s own lesson. What
is open is in [`next-actions.md`](next-actions.md); the design record is in [`layers.md`](layers.md).

---

## Closed items — the measurements, including the negative ones

Numbered as [`next-actions.md`](next-actions.md) refers to them. **Items 1, 3, 8, 9, 11 and 12 are
done, and three of those closed *negative*** — which is the ordering rule paying off rather than
failing.

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

---

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

**Session 34 added three files and touched no engine and no solver code.** `tools/harvest.py` (814 lines,
the blogspot harvester: `index`/`map`/`fetch`/`codebook`/`sheet`/`label`/`decode`), `tools/png.py`
(a stdlib PNG reader and writer, needed because this machine has neither PIL nor numpy) and
**`bench/goal-tiles.json`, which is committed and holds one entry.** Nothing under `src/` changed, so
**the standing engine claim and every solver number are untouched** — no gate was re-run because nothing
a gate covers moved.

* **The two-halves split is the part to understand before touching it.** A blogspot *start* screenshot
  labels its own 256 tiles, because the corpus already knows that board — so that codebook is derivable
  and lives in gitignored `build/harvest/codebook.json` (55 entries). Nothing labels the states only
  *play* produces, so those are hand input and live in **`bench/goal-tiles.json`**, committed, per the
  rule that directory exists for. `harvest.py` merges the two on every read and says which is which.
* **The one entry in it is a worked example, not a stub.** `4776b082c1cedd55` → `.` is the block pushed
  into water (`Engine.cs:731`: `PF = 0`, `BMF = 19`), and it alone takes the goal-side residual from
  4.52% of tiles to **1.37%**, and goal boards with more than ten unknown cells from 63 to 11 of 173.
  116 sprites remain and they are a flat tail — the commonest is 9.1% of what is left.
* **Artefacts in `build/harvest/`** (gitignored, all re-derivable): `index.jsonl` (6,218 posts), `img/`
  (~250 screenshots), `fetched.json`, `codebook.json`, `residual.{png,json}`. Rebuilt from nothing by
  `index` (46 s) + `fetch --limit 150 --goals` (~7 min) + `codebook --goals` (~45 s).
* **`data/` is unchanged** — a harvested goal board is hint-assisted and no decision has been taken
  about where such boards are allowed to live, so the spike deliberately banked none of them there.

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
11's prune retired on its own acceptance test on two populations, which leaves item 10 as the whole of the
wall-clock story. Item 3 a no-op because its premise expired. **The lesson as a ratio: two planned code
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
[item 6](next-actions.md#session-34--the-spike-and-every-number-it-produced); the four things worth more
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

---

## Where session 26's twelve pointers went

The pointers section was a pass over the file and the source by a different model, tagged **measured** /
**in the code** / **hypothesis**. All twelve have been actioned or closed, so the section itself is gone
and this is the map:

| # | what it was | where it is now |
|---|---|---|
| 1 | spend the budget where the record says the level is short | [next-actions](next-actions.md#7-4th--the-solved-vs-budget-curve) item 7 |
| 2 | the node budget hides most of a push rung's wall clock | [next-actions](next-actions.md#10-5th--wall-clock-on-the-push-rungs) item 10 |
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
