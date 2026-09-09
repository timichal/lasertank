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
although the shot count says the strategy is already right, so those rounds can only buy polish. See
item 13.

---

## The ideas, and where each one already lives

| Lyf's idea | in the tree? |
|---|---|
| **Shots are the strategy, moves the execution** (pt 1) | **new as a signal.** The solver never reads `.ghs`; only `tools/` do, for populations, plus `--max-keys-record`. **Items 14 and 13** |
| **"Data never tell lies"** — the record's numbers bound what must happen (pt 2) | **new**, same two items. Record shots = 0 as a licence to drop the space bar was checked and is worth nothing (17 levels); *shots = depth* is the useful half |
| **Freely Moveable Objects; don't sink one unless you must; crowding beats count** (hard pt 1 §1) | **half.** `_alive[c]` is the binary form (*can this block still be moved*) and `RouteDead = holes − live blocks` the budget form. **The gradient — how large a region each object can be pushed over — is not there**, and `MazeFill` already computes exactly that BFS. **Item 16** |
| **Reversibility as a resource** — bricks shot, thin ice walked, an anti-tank killed or pushed up, a mirror that cannot turn 360° (hard pt 1 §3) | **mostly missing.** The frozen block and the hole/block count are the only irreversibility the search knows. Nothing prices *spending* a brick, a thin-ice tile or an anti-tank for no derived reason. **Item 16** |
| **Phases: a saving in one must not cost another** (pt 7; hard pt 1 §2) | **the definition is new, and item 5 needs it.** [Item 5](next-actions.md#5-5th--level-6s-decomposition) is *commit to one (block, hole) pair, re-derive*, and until item 15 it was "the one open item with no cheap falsifier". Lyf defines a phase as an **independence** property, which is testable. **Item 15** |
| **"Two by two", not one by one** (pt 4, pt 1) | **a warning about item 5.** Carries are batched *because* a lone carry wastes the round trip, and level 6's hand line moves four blocks in its first five board changes. `--push-ferry-stage` bets the other way and *"fills one hole and stalls"* — so a phase has to be allowed to be a **group** of carries |
| **"Units": six moves is one go-and-return** (pt 5) | **new, small.** `MatchFerry` sums push distance with no per-carry constant, so it cannot see that two carries cost more than one carry of twice the length. **Item 16** |
| **The editor: build the pivotal situation and play from it** (hard pt 2 §4) | **new as an instrument, and it is item 5's missing falsifier — [item 15](next-actions.md#15-3rd----push-seed-k-the-editor-trick-as-an-instrument).** |
| **Work backwards from the ending position** (hard pt 2 §7, quoting Sahan) | **already named**: FESS's packing order, [next-actions *Further out*](next-actions.md#further-out-and-only-after-the-numbers-above-have-moved). The series is independent confirmation that the backward half is the one a Sokoban wants |
| **Thinking like an author** — *"why is that object there?"* (hard pt 2 §6) | **new.** The read derives what is *in the way*; nothing derives what the author put there *on purpose*. A lone mirror, a single `*`, one crystal on an otherwise plain board is load-bearing far more often than chance. **Item 16** |
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

| item | order | what | falsifier |
|---|---|---|---|
| **16** | 2nd | four derivations the read does not have — author intent, spending an irreversible resource, FMO mobility as a quantity, a per-carry constant in `MatchFerry` | two `--read-dump` columns and one replay decide the first two; an `--analyze-tsv` column the third; `--ferry-weight` the fourth |
| **15** | 3rd | **`--push-seed K`** — the editor trick as an instrument: start the search from the K-th board change of a recording, sweep K over level 6's 168 | eight short runs, and it *is* item 5's falsifier |
| **13** | 4th | **the shot test** — a `report_stats.py` column now, a `--best-of-round` rule with item 4 | already run: measurements 2 and 3 above |
| **14** | 6th | **per-level width from the record** — the driver ladders width globally today | one run on the 138-level GAUNTLET tail; the free calibration is done, below |

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
