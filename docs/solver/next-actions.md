# Next actions — the open items in full

**This file is what is open. What has closed is in [`history.md`](history.md#closed-items--the-measurements-including-the-negative-ones),
including the negative results** — a negative result that is deleted gets re-run, which is why that file
is long and this one is not. Items keep their numbers because these files refer to them by number. The
reasoning behind the list is in [`SOLVER.md`](../../SOLVER.md#what-is-open); this file carries the
recipes, the costs and the evidence.

**The rule the list is ordered by is *cheapest falsifier first, whatever kind of work it is*.** Second
key: **prefer work that produces a property or a level over work that produces a number** — twenty-five
hours of machine time once went into items that report percentages while the two levels in front of the
project did not move and one banked solution got worse. The corollary is what makes an item an item
rather than a bullet: **each one carries a recipe, a cost, and the measurement that would refuse it.**
Of the items this list has held, four turned out to have a falsifier costing minutes that nobody had
run, and all four were refused by it — which is the argument for writing a bullet out in full.

---

## Iteration 1 is running, and the next items come out of it

**The autosolver is on its first pass over the whole 20,914-level corpus, and nothing is proposed here
until it finishes.** The driver's configuration is versioned rather than passed: `Auto.Iteration` and
`Auto.IterationMaxRound` in [`src/LaserTank.Solver/Auto.cs`](../../src/LaserTank.Solver/Auto.cs), declared
together so that changing the settings without changing the number is an edit somebody has to make on
purpose. **Iteration 1 is `--max-round 2`** — three rounds ending at 6.4M nodes, 8.5M cumulative per
rung, which is past the point where
[closed item 7](history.md#7-the-solved-vs-budget-curve--run-and-it-saturates-below-10m)'s curve
flattens: 1M / 10M / 50M bought 12 / 20 / 21 levels of 253, so round 2 buys all but one level of what
round 5 would, at a sixteenth of the nodes. Over 20,914 levels that is the difference between weeks and
a year, and **a first pass that does not finish is not a first pass**.

```bash
bash tools/iteration.sh                 # the pass; rerun the same command to resume
bash tools/iteration.sh status          # the table, free, no solver
python tools/ledger.py data/reports/solutions.jsonl --stops    # why the unsolved fail
python tools/ledger.py data/reports/solutions.jsonl --ladder   # what every rung was set to
```

**The ledger is the state.** `data/reports/solutions.jsonl` is append-only, one row per level with the
last row winning, and it carries the per-rung detail of each level's last round — so an interrupted pass
resumes instead of re-running the unsolved remainder, and the question *how did this level fail* has an
answer without re-running anything. `SOLUTIONS.md` is a view of it, regenerated after every collection
and overwritten in full; the ledger is what to read from, not the document.

**What picks iteration 2 is `--stops`.** It counts the last round's stop reason per rung over the
unsolved levels: a rung ending on `push-dead-end` everywhere is asking for width, one ending on `budget`
everywhere is asking for a better ranking, and item 7 has already priced the third answer. Bumping
`Auto.Iteration` is the act of saying *this is a new approach* — every level still unsolved is attempted
again, every level already solved stays solved. **Until there is a ledger to read, the only open item is
the parked one below.**

---
## 24 (parked, and the only open item) — parent pointers instead of copied keystreams

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
