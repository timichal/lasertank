# The instruments, the source map and the tools

Everything that reports rather than searches, in the order you should reach for it on a level that will
not fall. **The instruments have a cost order and it is worth respecting** — instrument 2 runs no search
and takes a second; instrument 3 costs a whole run. A 27-minute traced run once arrived at the diagnosis
`--analyze` prints in one word.

---

## 1. Ask why the level is hard, before asking the solver to try harder

`--profile` measures the *level*: replay a winning recording — a solver's or a human's — and print what
the ranking keys do along it.

```bash
ls data/demos/LaserTank/*.lpb > build/demos.txt
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl \
    --lpb-list build/demos.txt --profile build/prof.tsv
python tools/basin.py build/prof.tsv --per-level            # in keypresses
python tools/basin.py build/prof.tsv --per-level --events   # in board changes
```

The number to read is the **longest stretch that stays at or above the best heuristic value seen so far**
— see [*Why the search fails on long levels*](layers.md#why-the-search-fails-on-long-levels--the-measurement-the-rest-of-the-layers-answer).
`--ferry-weight` sweeps layer 5's ferry weight offline against a recording instead of by re-running the
solver.

## 2. Ask what the board is

`--analyze` prints the read: what is in the way, every board change the tank can make right now, which of
those advance, and a verdict naming the level's shape. `--analyze-tsv FILE` is one row per level for
joining against a campaign report; `--read-dump FILE` (with `--lpb-list`) scores the read against what
humans actually did next.

It costs no search and takes about a second. Example, and it is the whole diagnosis of `LaserTank.lvl` 10:

```
LaserTank 10  "The Valley of Death"  by Jim Kindley  (Easy)
  board     10 anti-tank
  reach     the tank stands in 18 cells / 208 poses; the flag (7,0) is not among them
  route     WorkDistance 16; in the way: nothing priced (no route settled)
  effects   5 distinct board changes reachable right now -- 5 by shooting, 0 by driving
     on the barrier: 0
     open somewhere new to stand: 3
  verdict   GAUNTLET: the route to the flag crosses nothing that has to be cleared and
            10 anti-tanks cover it -- what is in the way is fire, not terrain
```

`--analyze-tsv`'s columns are `collection level diff verdict work route_obst poses region barrier water
blocks threats effects shots indirect on_barrier toward opens flag_reachable`, joinable against any
campaign report on `(collection, level)`. The whole corpus is one loop of ~3 minutes:

```bash
for c in Beginner-I Beginner-II Challenge-I Challenge-II Challenge-III Challenge-IV Challenge-V \
         Gary-I Gary-II LaserTank Sokoban-I Sokoban-II Special-I; do
  build/lasertank-solve.exe --levels "data/levels/$c.lvl" --analyze-tsv "build/reports/an-$c.tsv"
done
# one table; the header is a '# '-prefixed comment line, so keep one and drop the rest
head -1 build/reports/an-Beginner-I.tsv > build/reports/analyze-corpus.tsv
for f in build/reports/an-*.tsv; do grep -v '^#' "$f" >> build/reports/analyze-corpus.tsv; done
```

That output stays in `build/` rather than `bench/` on the cheapness test the two directories are split
by: **27 minutes is worth 16 KB in git, 3 minutes is not.**

## 3. Ask where the *searcher* loses it, which is a different question

`--push-line` replays a recording, keeps its state at every board change, and runs the real beam with
those in hand, reporting per depth whether the line was generated, how the ranking key placed it, and
whether the width trim kept it. Read the last line (*followed to depth N of M, lost at K*), then the row
at K. If the row at K is a *setup* move — one that neither opens anywhere new to stand nor touches the
barrier, across which every key is flat — the flag to add is `--push-enables 8`.

**Three traps in reading its output:**

- **Each row carries two numbers**, `d=` (beam depth) and `at=` (how far along the *line* the frontier
  is), and they are not the same — a run emits several board changes at once. The report was
  depth-indexed until session 20, which meant that under `--push-shot-run` it called a line it was
  following perfectly `STALE`.
- **The `line-h` column is not a distance** unless you say `--push-eval work`. The default key is layer
  4's model, so the number is a seventeen-feature score in which `work` is one term and everything
  layers 5-8 add is added outside it. (It is divided back to work units before printing.) The tell: a
  line that ends on the flag does not end at 0 — level 9's winning line ends at 70.
- **`--budget-ms` matters here** and its default of 4 s will bite: without it the instrument stops after
  a quarter of a million nodes and reports the line lost at depth 2 when nothing of the sort happened.

## 4. Look at the board the beam settled on, not only at its score

`--push-trace-board` prints the best node's playfield each depth under `--push-trace`. `best=10` says the
ranking key has gone flat and says nothing about *what* the beam is looking at — and on a flat key the
answer is usually that it has found something the key likes and a player would not. It is what caught a
beam that had buried the flag under a block and scored it better than a win.

`--push-trace`'s own columns matter too: `trunc=` (closures truncating), `boards=` (distinct playfields
in the frontier — the column that found the pose duplicates), `closure~` (for the framing arithmetic in
layer 8), and `sterile=N/M` (expansions of the depth's M that emitted no fresh successor at all). **Read
`sterile=` as a falsifier, not as a dial**: it is 0.05% on the ferry bench and 0.07% on the deep one, so
a reading in tens of percent means something is wrong with the run rather than that a
closure-dominance prune has become worth writing.

### 4b. Ask whether the read has anything to say about this level *before* reading its ranking

`--push-trace` with `--push-read` prints `expansions with a barrier B, without W`, and the ratio is the
one column that says whether layers 6-8 are participating at all. It is **74.7% B on the ferry bench and
72.1% on the deep one — and 0% on `LaserTank.lvl` 10**, across 63,454 expansions, which is the whole
diagnosis of that level. A level with no barrier gets no tiering, so the beam falls back to ranking every
successor by the eval key alone; `2/526164 successors advanced (0%)` in a trace is not a weak read, it is
an absent one. **Check this before `--push-line`**, because a read that names nothing makes the line
report unattributable.

### 4c. Read a tier by the quantity it steers, not by `best=`

`--push-fire-tier` adds `fire: X/Y successors sweep fewer cells (Z%), frontier sweeps N at best, M at
least` to `--push-trace`, and the two halves answer different questions. **X/Y is selectivity** and is
read the way every tier here is read — promote almost everything and it is a no-op that costs a scan,
almost nothing and the beam cannot use it; on `LaserTank.lvl` 10 it falls from 33% at d=0 to 0.6% by
d=48, which is still 9,869 successors against a width of 1,024 and so still binding. **`frontier sweeps`
is the quantity itself**, and it exists because `best=` is a *work distance*: a tier that trades distance
for exposure makes `best=` worse by construction, and reading the layer by that column would report it
doing harm while it did exactly what it says. Level 10's least-exposed frontier board goes 131 → 115 → 99
over three depths while `best=` goes 62 → 53 → 72.

**This is the third time an instrument here has measured the wrong thing and nearly retired a working
layer** — the other two are `ReadCount` before the `opens` pass, and `--push-line`'s depth index.

## 5. Ask whether ranking or budget is binding, before spending either

`closure~` × width × board changes is what a *perfect* beam would cost. On all four of layer 8's levels
it was two orders of magnitude under the budget already spent, which is why sixteen configurations at 80M
nodes were the wrong way to spend a session. The corollary does not follow, though: fixing the ranking
moved the binding constraint to width and put it straight back into the budget.

---

## Solver source map

```
src/LaserTank.Solver/
  Search.cs      the beam and IDA*; Cut(), tiers, the closed-set policies
  Heuristic.cs   FlagDistance, WorkDistance, FrontierObstacles, RouteFerry,
                 RouteStop, RouteDead, BuildFire/FireCells, BuildRays,
                 TankRegion
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
                 change reachable from it; PushRun / ShotRun.  Layer 9's
                 FireTier is here too -- it is a tier over the same
                 successors, not a heuristic term
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

## Solver tools

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
gauntlet_tail.sh  the two-arm A/B over bench/gauntlet-tail.txt -- the 138 unsolved
                    short-record GAUNTLETs -- at 40M nodes, gating each arm and printing
                    the union.  It exists because bench.sh's --levels-list is one
                    collection's level numbers and this population spans thirteen, so a
                    multi-collection list has to go through second_pass.sh and a
                    synthesised report.  NODES/BUDGET_MS/JOBS/LT_SOLVE all honoured
report_stats.py   read a campaign .jsonl: per-tier and per-collection rates, stop
                    reasons.  --diff compares two layers
chain_union.py    union the chain's four per-pass reports into chain.jsonl -- the
                    shipped chain's per-level state, and what the fourth pass has
                    to be pointed at.  Was a sentence in SOLVER.md until session 25
arms_union.py     compare several arms of one pass by what each *adds*: solo count,
                    levels only it solves, the cumulative union in greedy order, the
                    pairwise overlap matrix.  Built because the instruction to
                    compare unions had no tool behind it
rankdump.py       layer 4's instrument: replay every winning .lpb and dump the group
                    of successors the shipped expansion offered at each shot boundary
fit_eval.py       read that dump.  Bare: the distribution.  --fit: fit and regenerate
                    Weights.cs (rebuild after — the vector is compiled in).
                    The one tool here that is not stdlib-only: needs numpy
basin.py          read a --profile dump: how far uphill a winning line goes, per level,
                    in keypresses and in board changes
harvest.py        closed item 6: the blogspot goal-board harvester.  index / map /
                    fetch / codebook / tiles / sheet / label / decode / bank, cheapest
                    first.  `map`, `codebook` and `tiles` report rather than fetch --
                    `map` checks (collection, level) -> name against every .lvl and
                    needs no images at all, `codebook --goals` prints the saturation
                    curve of the start-bootstrapped half, and `tiles` is the gate on
                    sprites.py: it re-derives every goal-only tile and prints what it
                    covers against both the start codebook and the goal boards.  It
                    retired `sheet`/`label` as this item's phase 1 -- that loop
                    (contact sheet, sidecar, merge into bench/goal-tiles.json) now
                    reports nothing left, and is kept for tiles no composite can
                    draw, which so far is one caught mid-blit.  `decode` returns the
                    board *and* where the tank is, and `decode --check` on an 'a'
                    image is the gate: a start board must come back identical to the
                    corpus board, its tank cell reconciled rather than excused.
                    `bank` is the last step and the only one the solver reads:
                    (collection, level, goal PF, tank, moves, shots) per level, the
                    final board last, and it *refuses* a board with an undecoded cell
                    rather than banking a ranking key with a hole in it.
                    **`codebook` learns only from a picture that is a start
                    position** -- tank on the cell the .lvl stores as T, facing up,
                    both read off the pixels.  The labels come from the .lvl, so a
                    mid-solution screenshot labels every state play produced with
                    what was there *before* play, and setdefault makes the first such
                    board win for good; the check is what stops that, and it names
                    every board it rejects.  Every loop that runs for minutes prints
                    a progress line on stderr -- rewritten in place on a tty, one new
                    line every ten seconds with an ETA when redirected -- and every
                    line reporting a board the tool will not use carries the post's
                    own URL, because the next question is always what it looks like
sprites.py        not an instrument: the game's own sheet, and every board cell it
                    can draw.  Decodes original/src/Game.BMP + Mask.BMP (RLE8/RLE4),
                    shrinks them to 24 px the way GFXInit's StretchBlt does -- GDI's
                    default STRETCH_ANDSCANS, which *ANDs* the eliminated rows and
                    columns -- and composites background + mask + sprite + tank +
                    laser the way UpDateSprite does, then reads PF off the bitmap.
                    Why it exists: it makes the goal-only tiles derived rather than
                    hand-labelled, so item 6's phase 1 cost nothing.  Every constant
                    in it cites the line it came from
png.py            not an instrument: a stdlib PNG reader and writer, because this
                    machine has neither PIL nor numpy and harvest.py needs pixels.
                    Same reason atlas_check.py and sprites.py hand-roll BMP readers
verify_solutions.py  the gate.  Both engines, WIN on each, byte-identical traces
```

**A level solved with a harvested goal board is hint-assisted and never enters the headline rate**, and
that is enforced by `--goal-board` rather than left to whoever runs it: the flag moves the default output
directory (`solutions` → `solutions-hint`, `data/solutions` → `data/solutions-hint`), stamps
`"hint": "goal-board"` on every report row, and says so on stdout. The condition and what the
hint-assisted recordings are *for* are in
[closed item 6](history.md#6-the-blogspot-goal-board-harvester-and-the-goal-board-as-a-ranking-key).

`--goal-board FILE` is also the cheapest way to read one: `--analyze --goal-board` adds a `goal` block
to the read — how far the start board is from the banked one, and what each available board change does
to that distance — and runs no search.

**Three things a screenshot can be that its filename does not say.** All three first appeared when the
harvester was run over the whole 6,044-post sample instead of a 150-post one, and all three are hazards
to `bank` for the same reason: none of them produces an *undecoded* cell, so the refusal that guards the
bank never fires. They decode to a clean, plausible, wrong board.

* **The picture is not the position the filename claims.** 35 posts carry both `N.png` and `Na.png`, and
  the bare one is the start; a handful name an image for another collection outright
  (`LaserTank_452.png` in the Sokoban-I 452 post). `pick_images` decides both, and `codebook` no longer
  takes any board on trust: it checks the tank is on the `.lvl`'s own `T` cell facing up. This matters
  out of proportion to the count, because `codebook` labels from the `.lvl` — one mid-solution board
  teaches a block sunk in water as `~` and a destroyed anti-tank as `v`, `setdefault` freezes it, and
  every later board that shows the tile honestly then reports a conflict. `tiles` is the gate that
  catches it after the fact, by disagreeing with `sprites.py`; the start-position check is what stops it
  happening.
* **The blog's level pack is not always this corpus's.** A few start boards decode cleanly, tank on its
  start cell facing up, and still disagree with `data/levels/*.lvl` — water where the corpus has tunnels,
  two tile types swapped — *in every frame of the post*, which is what separates drift from a capture
  artifact. A goal board banked for such a level is a ranking key for **a different puzzle**. `codebook`
  now names each one with its post URL; nothing yet keeps them out of the bank.
* **A screenshot can catch `UpDateSprite` between its blits**, and it has two shapes, not one. Caught
  after the mask blit the cell is black — that hashes to nothing and comes back undecoded, which is the
  safe half and the one `sheet`/`label` was kept for. Caught *before* it, the cell is the bare terrain:
  an anti-tank or a water tile silently missing from an otherwise perfect board. Only animated sprites
  can do this, since only they are redrawn; the tell is the same tile present in a sibling frame of the
  same post.

## The bench lists, and what belongs in `bench/`

The banked level lists live in **`bench/`**, committed — see the README there for why, and for what
belongs beside them. **The rule that directory pays for: a list that only lives in a gitignored
directory is not banked.** Nor is a measurement — layer 4's +30 was measured through a weights file in
`build/` and does not reproduce through the path that ships.

The reports the lists are compared through stay in `build/reports/`, which is gitignored and
machine-local. The lists, all `Beginner-I` unless noted, all carrying their generating rule in their own
header:

| list | what it is |
|---|---|
| `bench-levels.txt` | 60 levels layer 0 failed. Described as GAUNTLET-heavy when first cut; FERRY 30 of 60 through today's read, which is partly the population and partly layer 8's barrier fix |
| `deep-levels.txt` | 50 levels with a `.ghs` total of 40-150 |
| `ferry-levels.txt` | 50 the chain fails that the read calls FERRY or SOKOBAN — banked because the two older lists contain almost no ferry. Contains 1581, the one ferry-bench level named in these files |
| `short-record-failures.txt` | 687 chain failures with a record ≤ 60. The population for the solved-vs-budget curve |
| `gauntlet-tail.txt` | the 138 unsolved GAUNTLETs with a record ≤ 60. Layer 9's population |
| `seed-weights.txt` | not a level list: layer 4's equivalence check, the vector that *is* `WorkDistance` written in the features, in `Eval.Scale` fixed point. It was prose until session 27, and prose is not a check |
| `trace10.err` | the 27-minute traced level-10 run, with its command and its five load-bearing columns in the header. In `bench/` because losing it would cost a result |

**The three level lists are reconstructions, not recoveries** — same rules, different levels — so every
bench number ever quoted against the pre-session-25 lists rebases.

`build/reports/chain.jsonl` is *not* one of these: it is the shipped chain's final per-level state, so
`second_pass.sh` can be pointed at everything it still fails, and it is an output tied to one code
version rather than a curated input. `tools/chain_union.py` rebuilds it from the four chain reports.

**Report schema**, because a wrong key name reads as `'?': 3691` rather than as an error:
`collection depth difficulty ghs_moves ghs_shots keys level method moves ms name nodes polished ratio
raw_keys replanned restarts shots solved stop trimmed`. The stop field is `stop`, not `reason`.
