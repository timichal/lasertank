# What the human experts do, read against the solver

The blog we harvest goal boards from ([closed item
6](history.md#6-the-blogspot-goal-board-harvester-and-the-goal-board-as-a-ranking-key)) also carries a
ten-post strategy series under the label **Lyf Series**, written by Li YiFeng — a player with ~70 `.ghs`
records in `LaserTank.lvl` and 120 solved Deadly levels. Seven parts on *how to reach or beat a GHS* and
two on *how to solve hard/deadly levels*.

**It is the only prose in this project's reach that describes how a strong human actually plays**, so it
is worth what `bench/` is worth: something nothing in the tree re-derives. This file is the comparison —
what it says that the solver already does, what it says that the solver does not, and the three
measurements that reading it produced.

**Re-fetch it.** The harvester keeps titles and image URLs, not post bodies; the Blogger feed serves the
body, and the series is one label query:

```bash
python - <<'PYEOF'
import json, urllib.request, urllib.parse
url = ("https://lasertanksolutions.blogspot.com/feeds/posts/default"
       "?alt=json&max-results=50&category=" + urllib.parse.quote("Lyf Series"))
j = json.loads(urllib.request.urlopen(url, timeout=60).read())
for e in sorted(j["feed"]["entry"], key=lambda e: e["published"]["$t"]):
    print(e["published"]["$t"][:10], e["title"]["$t"], len(e["content"]["$t"]))
PYEOF
```

---

## Three measurements, because the series' central claim is checkable and it holds

Lyf's first and most repeated rule is **watch the shot count, not the move count**: *"The more shots the
more tasks… If GHS's shots are remarkably less than yours, it probably means you need to find another
strategy."* Shots are a property of the **strategy**; moves are a property of the **execution**. Every
instrument needed to test that on this corpus already exists, and it costs a replay.

**1. The record's shot count is this project's search depth.** `--profile` plus `basin.py --per-level
--events` counts board changes along the 20 hand recordings — layer 5's depth unit — against each level's
`.ghs` shots:

| board changes / record shots, 20 hand recordings | |
|---|---:|
| p25 / **p50** / p75 | 0.96 / **1.00** / 1.09 |
| exactly equal | 6 of 20 (levels 3, 4, **6**, 14, 19, 20) |
| within ±10% | 12 of 20 |
| the two outliers | lvl 11 **0.50**, lvl 7 **2.60** — both hand routes far off the record's |

Level 6 is 168 board changes against a record of 425 moves / **168** shots, exactly. **That matters
because layer 8's framing arithmetic — `closure × width × board changes` against the node budget — has
until now needed a hand recording to supply its third factor, and there are 20 of those against 20,914
levels with a record.**

**2. Two thirds of the solver's own solutions already use the record's exact shot count.** Over the 452
solved rows carrying a record in `fix2-chain.jsonl` + `gt-fire.jsonl`:

| | |
|---|---:|
| solution shots **==** record shots | **298 (66%)** |
| within ±10% | 314 (69%) |
| more shots than the record | 67 |
| fewer | 87 |
| median keystream against the record | **1.47x** |

So the solver finds the human's *strategy* far more often than its 1.47x keystream suggests — the slack
is nearly all execution. That re-reads the whole `ratio` column: **1.5x is not half a solution, it is the
right plan driven badly.**

**3. Lyf's diagnostic works on our data, and it disagrees usefully with the test the driver uses.**
Splitting the same 452 rows by the shot comparison:

| | n | median keys/record | p90 |
|---|---:|---:|---:|
| shots **>** record | 67 | **1.85** | **3.20** |
| shots **==** record | 298 | 1.41 | 1.80 |
| shots **<** record | 87 | 1.46 | 2.11 |

A win that spends more shots than the record is a *worse route*, measurably — which is what
`Replan.Improve` cannot fix and what level 9 taught the hard way (*"a 5.0x solution is a bad route, and no
post-processing is a substitute for finding a better one"*). `--best-of-round RATIO` judges a win by
`keys / record ≤ 2.0`; the two tests **disagree on 58 of the 452 rows** — 41 wins the ratio test closes
the round on although their shot count says the strategy is wrong, and 17 it keeps the round open on
although the shot count says the strategy is already right, so those rounds can only buy polish. The
column is shipped (closed item 13) and the rule inside `--best-of-round` is
[item 4](next-actions.md#4-6th--the-campaign-that-decides-whether---best-of-round-is-a-default)'s.

---

## The ideas, and where each one already lives

| Lyf's idea | in the tree? |
|---|---|
| **Shots are the strategy, moves the execution** (pt 1) | **new as a signal.** The solver never reads `.ghs`; only `tools/` do, for populations, plus `--max-keys-record`. **Items 14 and 13** |
| **"Data never tell lies"** — the record's numbers bound what must happen (pt 2) | **new**, same two items. Record shots = 0 as a licence to drop the space bar was checked and is worth nothing (17 levels); *shots = depth* is the useful half |
| **Freely Moveable Objects; don't sink one unless you must; crowding beats count** (hard pt 1 §1) | **built and measured, and the sign came back inverted.** `_alive[c]` was the binary form and `RouteDead = holes − live blocks` the budget form; `Heuristic.Mobility` is now the gradient — the transitive closure of `_alive`, reported as `--analyze-tsv`'s `alive`/`mob_max`/`mob_sum`. Over the 4,185-level stride sample it predicts the solve rate **the other way round from the series**: a level whose freest block has ≤4 cells to move in solves at 9.5% against 5.3% for one with more, *with the record's own length held fixed*, where the openness control (`poses`) gives 1.17x. Freer objects are a human's resource and a beam's branching factor. Crowding (`mob_sum / blocks`) adds nothing beyond it. Item 16, closed |
| **Reversibility as a resource** — bricks shot, thin ice walked, an anti-tank killed or pushed up, a mirror that cannot turn 360° (hard pt 1 §3) | **mostly missing.** The frozen block and the hole/block count are the only irreversibility the search knows. Nothing prices *spending* a brick, a thin-ice tile or an anti-tank for no derived reason — but the distribution is measured (session 42): a change that consumes something the read has no other reason for is 7.6% of what is offered and **1.9%** of what the human does, **0.04%** with the anti-tank kill exempted, so a spend *tier* is right-signed. Item 16 closed on that; the tier itself waits behind item 17, because it needs `Opens` and `Enables` per successor and the search rations both |
| **Phases: a saving in one must not cost another** (pt 7; hard pt 1 §2) | **the definition is new, and item 5 needs it.** [Item 5](next-actions.md#5-4th--level-6s-decomposition) is *commit to one (block, hole) pair, re-derive*, and until item 15 it was "the one open item with no cheap falsifier". Lyf defines a phase as an **independence** property, which is testable — and item 15's horizon now sizes one: 48 board changes on level 6 |
| **"Two by two", not one by one** (pt 4, pt 1) | **a warning about item 5.** Carries are batched *because* a lone carry wastes the round trip, and level 6's hand line moves four blocks in its first five board changes. `--push-ferry-stage` bets the other way and *"fills one hole and stalls"* — so a phase has to be allowed to be a **group** of carries |
| **"Units": six moves is one go-and-return** (pt 5) | **measured and refused.** `MatchFerry` sums push distance with no per-carry constant, so it cannot see that two carries cost more than one carry of twice the length — and adding one (`basin.py --carry-cost K`, swept 0-40 against the 20 hand recordings) changes **no level's ascent at all** and makes the deepest rise *worse* on three, because the hole count is not monotone along a human line. The drop at a fill is already in the term; the ascent lives inside a carry, where a per-carry constant is a constant. Item 16, closed |
| **The editor: build the pivotal situation and play from it** (hard pt 2 §4) | **built, and it answered.** `--push-seed FILE.lpb:K` searches from a recording's K-th board change; on level 6 the horizon is **48 of 168 board changes** and the boundary is one change wide. That is item 5's falsifier and the first direct measurement of the quantity every layer since 5 attacks. Item 15, closed |
| **Work backwards from the ending position** (hard pt 2 §7, quoting Sahan) | **already named**: FESS's packing order, [next-actions *Further out*](next-actions.md#further-out-and-only-after-the-numbers-above-have-moved). The series is independent confirmation that the backward half is the one a Sokoban wants |
| **Thinking like an author** — *"why is that object there?"* (hard pt 2 §6) | **measured, and it is the largest lift the read has ever shown.** `--read-rare N` counts each element class on the board *as authored* and asks whether a change touches one the level has N or fewer of: 5.1% of the successors on offer, **16.0%** of what the human does, **3.15x**, against 1.3-1.5x for the three derivations the read already ships. Item 16 closed on the distribution; the rung is [item 17](next-actions.md#17-2nd--the-rarity-tier-the-one-open-item-whose-falsifier-is-already-run) |
| **Going and thinking** — play to learn the map (hard pt 1 §2) | already: every derivation in layers 6-8 is the engine executing candidates rather than a model of them |
| **Mirrors chained so one shot does everything** (pt 4) | already: `enables`, which is why `LaserTank.lvl` 1 falls |
| **Counting: cancel the shared prefix of two plans** (pt 6) | not applicable — that is how a *human* compares two plans by hand |
| **Even and odd** — chessboard parity fixes the move count's parity (pt 3) | **not applicable, and worth saying why.** Parity is an *optimality* tool: it says your move count cannot equal the record's, therefore your strategy differs. It prunes nothing in a satisficing search, and the shot test above is the same diagnostic without parity's exceptions (ice, tunnels, tank movers, several flags) |
| **Shortcuts: GHS shots ≪ the author's shots means one exists** (hard pt 2 §5) | **not available.** `TLEVEL` is `PF` plus name, hint, author *name* and `SDiff`; the author's own score is not in the file. Only the record is |

---

## What came out of it — items 13-16

Four actionable things, and they are now [`next-actions.md`](next-actions.md) items 13-16 with their
recipes, their costs and their falsifiers. The list's own ordering rule (*cheapest falsifier first,
whatever kind of work it is*) puts three of them ahead of item 5, and gives item 5 the cheap falsifier it
had been open without:

| item | order | what | falsifier | what it came back |
|---|---|---|---|---|
| **16** | 2nd | four derivations the read does not have — author intent, spending an irreversible resource, FMO mobility as a quantity, a per-carry constant in `MatchFerry` | two `--read-dump` columns and one replay decide the first two; an `--analyze-tsv` column the third; `--ferry-weight` the fourth | **all four measured, and split 3-1.** `rare` **3.15x**, `spend` **0.04x** (session 42); FMO mobility predicts the rate and **with the sign the series does not have** — freer blocks are *harder*, 1.78x with the record held fixed — and the per-carry constant is **refused**, it changes no level's ascent (session 43). Closed; the tier it leaves is item 17 |
| **15** | 3rd | **`--push-seed K`** — the editor trick as an instrument: start the search from the K-th board change of a recording, sweep K over level 6's 168 | eight short runs, and it *is* item 5's falsifier | **built and run.** Level 6's horizon is **48 board changes of 168**, and the boundary is one board change wide: 48 left solves on 36.1M nodes, 51 left does not on 40M. Monotone at every K below it |
| **13** | 4th | **the shot test** — a `report_stats.py` column now, a `--best-of-round` rule with item 4 | already run: measurements 2 and 3 above | the column ships and reproduces measurement 3 from any report; the rule is item 4's |
| **14** | 6th | **per-level width from the record** — the driver ladders width globally today | one run on the 138-level GAUNTLET tail; the free calibration is done, below | open |

**Item 14's free half is done and it tempers the item rather than supporting it**, which is why it sits
6th rather than 2nd. Layer 8's arithmetic is `closure × width × board changes` against the node budget;
measurement 1 supplies the board changes and `--analyze-tsv`'s `poses` column the closure, so the estimate
can be checked against what a run actually spent. Over `l8fire`'s 66 solved levels at a known width of
128, `nodes / (poses × width × ghs_shots)` reads **p10 1.9 / p25 4.6 / p50 14.2 / p75 59.9 / p90 446**.
Levels 8 and 9 gave 22x and 47x and looked like a constant; over 66 levels the spread is two and a half
orders of magnitude. **So the arithmetic sizes an order of magnitude, not a width** — which kills solving
for the width exactly and leaves scaling the driver's *round-1* width per level, with the ladder still
doubling from there. Doing that measurement cost one join and it moved the item four places.

---

*Written after reading all ten posts. The prose is worth reading whole rather than trusting this summary,
particularly part 6, which is a human doing by hand what a heuristic is for.*
