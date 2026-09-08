# Next actions — the open items in full

Six items are open. The order and the reasoning behind it are in
[`SOLVER.md`](../../SOLVER.md#what-is-open); this file carries the recipes, the costs and the evidence.
Items keep their numbers because these files refer to them by number — the finished ones are in
[*Closed items*](history.md#closed-items--the-measurements-including-the-negative-ones), including the
negative results, because a negative result that is deleted gets re-run.

Order: **6, 2, 5, 4, 7, 10** — set in session 33 and unchanged, but item 6 is a different item again.
Its spike came back positive in session 34, and **session 35 closed its whole first phase**: the
goal-only tile residue turned out to be *derivable* from the 2010 binary's own graphics, which this repo
already commits, so the half-session of hand labelling it was costed at does not exist. Every goal board
in the sample now decodes with **0 unknown cells**, and `LaserTank.lvl` 10's goal board — the one thing
that could hand that level a candidate — is complete, tank included. What is left of item 6 is
**only `--goal-board`**, a layer-sized build. It stays first on the second ordering key rather than
the first — *prefer work that produces a property or a level over work that produces a number* — because
it has an acceptance test written in advance and item 2 still produces a percentage. Item 2's
rehearsal is done and its 54-hour run is fully specified, so it is ready to launch whenever the machine is
free, and **the two do not compete**: node-governed results are unaffected by extra load; only wall-clock
readings are.

---

## 6 (1st) — the blogspot goal-board harvester

**The feasibility spike is done, positive, and it shipped as `tools/harvest.py` + `tools/png.py`
(session 34); session 35 added `tools/sprites.py` and closed phase 1.** What is left of this item is one
build, not a question — the framing is below, then every number the spike produced, then how the
labelling job turned out not to be one, then the re-costed remainder.

Michal raised it; what one post actually contains was verified rather than assumed (`Challenge-II-100`):
a **start screenshot**, one **screenshot per flag showing the board at the moment of reaching it**, and —
on some posts — the game's own **Moves and Shots counters visible in the panel**. No move list, no
keystream, no prose.

**What it buys is not solutions, it is a goal.** A final board says which blocks were moved where and
which bricks were destroyed, so "reach the flag" becomes "reach *this* board" — a progress measure that
decreases with every push in the right direction, which is exactly the gradient `RouteFerry` and its four
successors are hand-rolled approximations of.

**The honesty condition, and it is not optional.** A level solved with a scraped goal board is
*hint-assisted* and must never enter the solver's headline rate. Its value is as a bootstrap:
hint-assisted solutions are real recordings, and real recordings are what `--profile` / `basin.py`
measure and what layer 4 is fit on — the off-distribution long-level sample that layer 4's
self-reinforcement trap needs, obtained without anyone playing twenty levels by hand.

It is no longer a prerequisite for measuring anything: the ferry population was n=2 when this was agreed,
and it is n=20 hand-recorded now. The harvester is a way to make that 200.

### Session 34 — the spike, and every number it produced

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

### Session 35 — the residual was not hand input

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

### What is left, re-costed

One piece of work, and it is the build:

**`--goal-board`** — bank `(collection, level, goal PF, tank, moves, shots)` and rank by
cells-still-differing. **A layer-sized build**, and for once with its acceptance test written in
advance: level 10, whose goal board is decoded to **0 unknown cells** and whose three root pushes it
must separate. The gradient it supplies is the one `--analyze` cannot rank — pushing (1,13) up closes
the distance to its goal cell (0,12) from 2 to 1, while both pushes of (13,14) open theirs from 2 to 3.

Phase 1 is closed, and the reason to record how is that it inverts a rule these files apply often.
`bench/goal-tiles.json` exists because **nothing re-derives a human's answer** — but the corollary is
that nothing should *ask* a human for an answer the repo can derive, and the check for that is cheap:
the inputs were already committed and the composite rule was already in the ported source. The file
stays, holding one now-redundant label, as the place anything genuinely undrawable goes.

The derivable artefacts land under `build/harvest/` (gitignored, re-fetchable): `index.jsonl`, `img/`,
`fetched.json`, `codebook.json`, `residual.{png,json}`. Rebuilding all of it from nothing is
`index` (46 s) + `fetch --limit 150 --goals` (~7 min) + `codebook --goals` (~45 s); the derived table
is not in that list because it needs no fetch at all — `tiles` builds it from the repo in half a second.

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
  of that; the backward half is what level 6's strip of six holes wants.
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
## 2 (2nd) — the fourth pass, and its fourth arm is rehearsed

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

## 5 (3rd) — level 6's decomposition

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
blocked on it, and **it is the one open item with no cheap falsifier.**

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

## 4 (4th) — the campaign that decides whether `--best-of-round` is a default

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

## 7 (5th) — the solved-vs-budget curve

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

