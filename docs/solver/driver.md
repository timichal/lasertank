# The interactive driver, and the post-solve passes

How a human uses the solver, and what happens to a route after it wins. The layers themselves are in
[`layers.md`](layers.md); the commands and current status are in [`SOLVER.md`](../../SOLVER.md).

---

## The interactive driver

`build/lasertank-solve.exe FILE.lvl [--from N] [--to N] [--lanes N]` — a bare `.lvl` and nothing else
required. It walks the collection in level-number order and stays on each level until it falls or you
press a key.

**It is the portfolio the campaign could not afford.** A round runs every searcher *at once, one per
thread* — layer 0's beam (with IDA* on round 0, where a probe is cheap), layer 3's subgoal beam, layer
4's learned ranking of it, layer 1's macro beam, and layers 5-9's **six** push rungs — and the first
win cancels the rest. In a campaign that trade is a loss, because every node a specialist spends is a
node taken from the raw beam; here a specialist spends a *core*, and one level at a time means the
cores are there. If nobody wins, the node budget quadruples and the round repeats — 400k on round 0,
about a second; 400M on round 5, about an hour — and rounds also widen what only widening helps: the
raw beam doubles its width (a `beam-dead-end` has nothing to do with a bigger budget), the subgoal beam
gets six more restarts, and the push rungs step their widths where a level has paid for the step.

**The ladder is ten rungs.** Nine before `push-fire` (Layer 9) was added.

**The round's winner is the shortest solution, not the first one in ladder order.** More than one rung
crossing the line in the same round is common — the stop bit is polled every 120 ms and a rung already
past its search and inside `Clean()` never sees it — and the routes they bring back are not equally
good. Choosing by ladder index made that a coin toss dressed as a policy; everything compared here has
already been through `Clean()`, so it is two finished solutions being compared on the number the result
line prints. Ties keep the earlier rung, which keeps the choice deterministic.

**The two-engine gate is not optional here, it is the write path.** A win goes to a scratch `.lpb`, then
to `tools/verify_solutions.py` (which grew a `--levels` argument so one candidate can be checked against
a named `.lvl`), and is moved into the output directory only if the frozen C oracle and the C# core both
report WIN with byte-identical traces. A solution that fails is deleted and the search carries on —
loudly, because after Phase 3 that can only mean an engine divergence. Missing engines or no python is a
startup error, not a discovery made six levels in.

**A round is not node-governed, even though every rung in it is** — and stage A of closed item 4 is where that
was measured. The stop bit is polled every 120 ms and a rung already inside `Clean()` never sees it, so
**which rungs have crossed the line when a round cancels is a race with the thread scheduler**: two runs
of the *same* configuration over the 494 levels the chain solves disagree on **5-9 levels and about 20
keys**. Nothing is wrong — every rung spends its node budget exactly — but a campaign of driver runs has
a noise floor that a batch campaign does not, and any difference smaller than it is not a result.
[Item 4](history.md#4-the-campaign-that-decided-that---best-of-round-is-a-default--and-the-shot-rule-won-it) reports
against it rather than around it.

**The driver can be measured now, which it could not before session 48.** It takes the batch harness's
`--report FILE.jsonl` — one row per level, the same `Outcome.Json` every other report in the tree is made
of, so `report_stats.py`, its `--diff` and `arms_union.py` all read it — plus five numbers only the driver
has: `rounds`, `wins` (how many rungs solved the round that ended the level), `longest` (the longest of
those routes, which is what a first-win-cancels run could have banked instead), and `total_nodes` /
`total_ms`, **the level's whole bill against the winning rung's own**. A level nobody solved gets a row
too, with `stop` saying which of the three ways it ended.

**And every row says how the search was configured**, which no report in this tree did before: `config`
is the run's own flags with the paths elided, and — driver rows only — `rung` is what the winning rung's
`Tune` set on top of them (`RunPush=True PushRead=True PushBeamWidth=48 PushRestarts=18`). The second is
the one that matters here, because **a driver's argv cannot express a rung**: `--push-beam 2048` comes
from `Ladder`, not from the command line, which is exactly how `LaserTank.lvl` 8's 308-key route came to
survive as the string "2048" in a gitignored directory name and nothing else
(`bench/recovered/README.md`). It is read off the options object by reflection, so a rung that gains a
knob says so without anybody remembering to update a string.

It also takes the selection flags the batch path
has always had — `--stride`, `--levels-list`, `--difficulty`, `--limit` — in level order, which is the one
thing the driver does differently on purpose. Together those are what makes a *campaign of driver runs*
comparable, which is what [item 4](history.md#4-the-campaign-that-decided-that---best-of-round-is-a-default--and-the-shot-rule-won-it)
needs and what nothing in the tree could express before.

**The driver writes to `data/solutions`, not `build/`.** A campaign's output is disposable (regenerated
by `tools/campaign.sh`, thousands of files, gitignored); the driver's output is one hand-supervised
level at a time, already through the gate, on levels the batch solver could not do. Those are worth
committing, so they go where git can see them, next to `data/demos/`.

### Not settling for the first win — `--best-of-round` and `--beat-banked`

**A rung that finishes first is not a rung with a better answer**, and `LaserTank.lvl` 9 is where that
costs something measurable: the raw beam wins at 94.3M nodes with 294 keys and `push-ferry-work` would
have won at 162.8M with 127, inside the same round-5 budget, so cancelling on the first win destroys the
better route before it exists.

The property the driver is supposed to have is that **one run reproduces the best route the project has
ever found for a level.** Two flags:

* **Keeping the round open is now the default**, and the rule it uses is the shot test below —
  `--no-best-of-round` is the opt-out, and a bare **`--best-of-round [RATIO]`** asks for the *ratio* rule
  on its own, which is how a campaign's arms are told apart. Under either: a win no longer ends the
  round. Every rung spends the budget the round already gave it and the **shortest** of however many win
  is banked. **The cost is bounded by a round nobody wins** — the rungs are already sized to spend
  `nodes` each and a failed round spends exactly that — which is the argument for it being affordable at
  all. `RATIO` (default 2.0) spends it only where it can pay: a win already inside 2.0x the record is a
  good route and the round ends on it as before, so a collection of easy levels costs nothing. A level
  with no `.ghs` record always keeps the round open, because an unjudgeable route is the case the flag
  exists for.
* **`--beat-banked`**, **ON by default** (`--no-beat-banked` opts out) — a round whose best route is
  longer than the `.lpb` already on disk is *not accepted*: the candidate is dropped and the budget
  quadruples instead. So a re-solve converges on the best route ever banked rather than on whatever this
  run reached first, and a level the ladder cannot match reports **`unsolved in N rounds`** with the
  banked file untouched — "not yet", not "solved". It needs `--max-round`, or such a level escalates
  until a key is pressed. **It can only bite under `--force`**, since an already-solved level is
  otherwise skipped before a searcher starts, which is what makes a default safe: no run that used to
  terminate stops terminating.
* **A refusal turns the round open by itself, which is what makes the default do anything.**
  `--beat-banked` alone would refuse level 9's 294 keys every round for ever and never bank anything,
  because the same rung keeps winning early with the same long answer. So the first refusal hands the
  lane's later rounds the banked length as a **target**, and a round with a target does not cancel on a
  win at all: **a refusal is positive evidence that a shorter route exists — it is on disk.** The cost
  lands only on levels that actually regressed.
* **A round with a target stops the moment it beats it**, which is what keeps that affordable. The ratio
  path has to run the round out because it never knows whether a shorter route exists; the target path
  knows exactly what it is chasing. Without that, a refused round 5 would hold round 6 open across its
  whole 1.6-billion-node budget long after the win that was the point of opening it.
* **`--best-of-shots [R]`** — **the rule that ships on**; spelling it out only changes `R`. Closed item
  13's test in place of the ratio. Shots are the strategy and moves the execution, so **a win that spends
  more shots than the record is a worse route whatever its keystream ratio** — median 1.85x against 1.41x
  for one that matches the record — and it keeps the round open; otherwise the ratio decides against a
  looser bound, `R`, default 3.0, because the shot test has already said the plan is right and what is
  left to buy is polish. The two tests disagree on **58 of 452 solved rows**: 41 wins the ratio closes the
  round on although the shot count says the strategy is wrong, 17 it holds open although the shot count
  says it is already right. **Item 4's campaign ran on 2026-09-14 and this rule won both its
  populations** — 95 keys against the ratio rule's 53 over the 494 levels the chain solves, 71 against 65
  over the 90 deep ones, and **never longer on a single level where either rule fired, 0 of 60** — which
  is why it is the default rather than merely the better flag. It is not free: 1.15x the nodes on stage
  A and 1.22x on stage B, and 1.34x the wall there.
* **Which rule ran is on every report row**, whether it came from a flag or from the default: `config`
  ends in `[round-rule shots 3 beat-banked]`, `[round-rule ratio 2 …]` or `[round-rule off …]`. A
  default has no token in argv, so without this two rows produced by two different rules would carry the
  same `config` — the same hole `config` was added to close for `--push-beam`.

**The mechanism is demonstrated, not only argued for.** The level-9 acceptance run —
`--from 9 --to 9 --force --best-of-round --max-round 5`, unattended, 44m46s, 258.8M nodes:

```
  round 5: 409.6M nodes to each of 9 searchers
  3 rungs solved this round; kept the shortest at 115 keys against 294
lv 9/2030  Grid Lock  Easy  record 37 moves + 22 shots
  SOLVED  115 keys (49 moves, 33 shots), 1.9x the record   push-ferry, round 5, 44m46s, 258.8M nodes
```

**115 keys / 1.9x against the 127 / 2.2x that was the best the project had ever banked** — the driver did
not merely reproduce the good route, it beat it, unattended, with no flags naming the level. Three things
in that output are the whole of the property, and each was an assertion before the run: `3 rungs solved
this round` (under first-win-cancels there is exactly one, and it is whichever rung is fastest rather
than whichever is best); `kept the shortest at 115 keys against 294` (the 294 is the beam's route, still
found, still first, and now losing); and **`push-ferry`, not `push-ferry-work`** — the acceptance
criterion named the wrong rung, and the winner was the `coarse`-key rung two keys off the 114 that was
lost with `build/w/b9-b/` (recovered in session 48 and gated —
`bench/recovered/LaserTank/00009.lpb`; the reading is unchanged, the file is simply no longer gone). **An acceptance test that names the mechanism can be passed by a different
mechanism, and that is a result rather than a technicality:** what was broken was the cancel, not the
ranking key.

In that run `--beat-banked` never had to fire — the first round that solved anything already beat the
banked file — so its refusal path was unit tests and nothing more. **Item 4's stage `acc` fired it, on
2026-09-14, in both directions, and it behaved.** Seeded with the two recovered routes and run
`--force --max-round 5`:

* **Level 8, seeded with the recovered 308:** the ladder came back with **305 keys at 1.37x**,
  `push-fire`, round 4, gated through both engines. The refusal path let a shorter route through and the
  file was replaced — and the 305 is *re-derived*, which is the distinction session 42 set and the one
  that matters here. It was 30 keys under the 335 then banked. **All four arms of the stage returned it
  byte-identically** (`d371e931a857`), the control included, so it is one route the driver reaches four
  times over rather than a lucky round — and **`data/solutions/LaserTank/00008.lpb` now holds it**,
  written by a driver run of its own rather than copied from the stage's output, which is the only way
  anything gets into that directory.
* **Level 9, seeded with the 114:** every round was refused — `round 5: 294 keys, longer than the banked
  114 -- not accepted, escalating; holding the round open now` — and the level ended **`unsolved in 6
  rounds` with the seeded file untouched.** That is the refusal path saying "not yet" rather than
  "solved", which is exactly what it promises and had never been watched doing.

Note the price this level puts on the other flag: **44m46s for one level** against the 16m36s of the
hand-run it beat, because a round nobody cancels is a round every rung spends in full. Stage `acc`
measured the worst case of that too — on level 9 at round 5 the two round arms spent **1.39G nodes
against the control's 603M, 2.31x, to return a byte-identical 294-key route.** Holding a round open pays
when a slower rung has a better answer *inside that round's budget*; at round 5 `push-ferry-work` needs
162.8M in one searcher and the round hands each rung 153.6M, so it was about 6% short and the spending
bought nothing. See [closed item 4](history.md#4-the-campaign-that-decided-that---best-of-round-is-a-default--and-the-shot-rule-won-it).

### When a round *is* accepted, `--force` overwrites unconditionally

A re-solve of an already-banked level does not necessarily come back with the better route — level 9 is
the case that proves it, and a `--force` pass over the collection duly replaced the good file with the
bad one, saying nothing about it. **The write is still unconditional**, because that is what makes the
regression *visible*: `data/solutions/` is committed, so the diff against git is the detector, and a gate
that kept the shorter file would leave a clean tree and no evidence that the run had gone backwards.

What the gate adds is the sentence nobody should have to infer from a diff — it reads the length it is
about to overwrite, and if the new route is longer it prints `LONGER than the one it replaced (127 keys
-> 294, +167)` in yellow beside the `SOLVED` line, with a count in the run's last line so an unattended
overnight pass cannot bury it in scrollback. **Keys is the comparison** for the same reason it is the
comparison inside a round: it is the number the result line prints and the number `--polish` is measured
in. An absent or unreadable banked file is "no opinion" — nothing is claimed and nothing is blocked,
since the write path never refuses a candidate that has just been through both engines.

*Two generalisations from the regression that produced these flags, both worth more than the bug.*
**When the artefact is under version control, the diff is the alarm and the tool's job is to make sure
somebody reads it** — not to prevent the write. And **repairing the output is not fixing the producer**:
the test of a fix here is whether the solver re-derives the good route, not whether the good route is on
disk. Restoring the file from git repaired the artefact and left the machine still unable to produce it.

### `--max-round` and `--lanes`

**`--max-round N`** gives up on a level after round **N**, numbered the way the driver prints them, so
`--max-round 3` is four rounds ending at 25.6M and `--max-round 5` ends at 409.6M. It only ever raises a
stopping condition the driver did not have: without it the loop condition was
`!won && !lane.Skip && !_quit`, i.e. a lane stays on one level until a key is pressed, which is why the
portfolio could not be pointed at a collection and left. Three things it touches beyond the loop, because
an unattended run reads its own log afterwards: the banner says `budget x4 each round to round N` when
the cap is set and nothing when it is not; a level the cap gives up on prints **`unsolved in N rounds`**
rather than `skipped`, which is reserved for a keypress (`stopped` stays the Ctrl-C case); and those
levels land in the closing `still unsolved:` list while the `skipped` counter stays 0. Verified on
`Beginner-I` with stdin redirected: `--from 26 --to 31 --max-round 0 --nodes 50000` banks 28 and 29
through the gate, reports `unsolved in 1 round` for 30 and 31, and exits by itself.

The overnight run is therefore one command — `--lanes 4 --max-round 3` over `Beginner-I`, every rung its
chances at 25.6M, banked to `data/solutions/` through the gate. It is the rungs' counterpart to the
chain's solved-vs-budget curve.

**`--lanes N` works N levels at once** (default 1, so a bare run is exactly what it always was). The
ladder is ten rungs and this machine has sixteen cores, so one level left several idle — and a second
level is a better thing to spend them on than a wider anything, because the rounds already widen what
widening helps. The scheduling policy is one sentence: **every lane draws on the same pool of `--jobs`
slots.** A rung that cannot get a slot waits, and if the level falls while it waits it returns without
expanding a node. The only thing `--lanes` trades is portfolio breadth per level against levels in
flight, and *which* is worth more is a property of the levels, not of the driver. The lane number is the
key that gives up on it; a key naming no lane is ignored rather than guessed at, because throwing away an
hour of the wrong lane's search is not a thing to do on a maybe.

Two latent races had to be closed for lanes: the gate *empties* the directory it stages through, so each
lane verifies in one of its own (two lanes sharing one would delete each other's candidate and, worse,
could hand a lane the other lane's solution to pass off as its own); and `Sweep` deleted `cand-*.lpb`
wholesale, so a second driver could delete a candidate between the searcher writing it and the gate
reading it. Candidates now carry the process id and a run sweeps only its own, plus anything a day old.

*One trap worth keeping, because it is the whole of why the display is written the way it is:* **a lane
may not write to `Console`.** Two lanes each printing half a level's story interleave into neither, so a
level's lines are built up in the lane and handed to a queue, and the painter on the main thread is the
only writer there is. The block is erased with one *relative* escape (`ESC[<rows>A ESC[J`), which is what
keeps it correct after the terminal has scrolled, and every row is cut to the window before it is coloured
— a row that wraps is two lines on screen and one in the row count, and from there the block walks up the
scrollback a line per repaint.

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
whole keypress turning when the key does not match the way the tank faces and `ScoreMove` only increments
in `UpDateTankPos`, so a turn on the spot costs the *record* nothing; a shot that hits nothing is one
keypress for one node; and `Cut` breaks a tie by cheapest keystream, which makes a state the beam has
already left exactly as good as one it has not — so wandering out and back is free too.

| the artifact | how it is found |
|---|---|
| a round trip | `StateHash` after key *j* equals the state after key *i* — so keys *i..j-1* left nothing behind. Longest first |
| a turn on the spot | a direction key after which the tank did not move and the board did not change, *followed by a different direction key* — which is what keeps the last turn of a run, the one the move needs |
| a shot at nothing | a space bar after which the whole state hash is unchanged |

**Every deletion is replayed before it is accepted, and that is not caution.** A wasted turn is not a
no-op: `AntiTank()` runs inside every key-consuming tick, so a turn on the spot gives every gun on the
board a move, and there are levels whose solution *is* burning a tick so a gun fires early. Measured — on
`Beginner-I` 1488 the run `> > < < > > < <` **survives** the polish, because those round trips really are
the anti-tank timing, while `> v FIRE ^ ^ < FIRE ^ v` collapses to `v FIRE < FIRE`.

It is separate from `Trim.Shrink` on purpose: Shrink is delta debugging, costs thousands of replays and
runs only past `--trim-ratio` (default 10x), whereas this runs on **every** solution because a 1.6x
solution can be just as ugly as a 12x one. An exhaustive contiguous-deletion sweep at every width 12 down
to 1 — contiguous rather than a halving ladder because the run Michal actually pointed at is *five* keys
long, which is exactly what 1/2/4/8/16 skips. `Polish` repeats its passes while anything comes out
(`Decycle` cuts one round trip per round and gives up after sixteen, so on a raw solution with more than
sixteen, the long ones nobody reached used to stay in).

**And the bug that made all of it look like a no-op is the most useful thing in this section.** `Trim`
replayed every candidate through *one reused* `Engine`, and `LoadLevel` deliberately does not reset
`wasIce` / `WaitToTrans` / `ConvMoving` / `BlackHole` (quirk #12), so each candidate inherited the
previous one's leftovers and **keystreams that win from cold were reported as losing**. The polisher was
declaring solutions irreducible that were not, and said so twice before Michal pushed back with the
replay: *"it really is not single-key minimal at 71 — step 2 is a useless turn south, step 3 is visually a
noop…"* He was right. A fresh engine per candidate took that solution **71 → 51 keys and 23 → 14 shots**
and the deep bench from 27.4% removed to **47.3%** (`Beginner-I` 1488: 831 → 212 keys). Nothing wrong ever
shipped — every `.lpb` passes a fresh-engine replay in `SolveOne` and another in `verify_solutions.py` —
the cost was entirely in reductions not found. Recorded as quirk hazard #12 because the rule generalises
past `Trim`: **anything replaying candidate keystreams must build its own `Engine`.**

The split between searchers is the other half: the raw beam barely improves (2.4%), because its `Cut`
already breaks ties by cheapest keystream; the **subgoal beam loses nearly half its keypresses**, because
it searches in shot-space and the movement between shots is whatever the closure happened to execute.

### `Replan.Improve` — re-deriving the route instead of deleting from it

**The complaint the polisher is structurally unable to answer.** *"In the first part the tank moves the C4
block to C6 and later back to C4 to move it correctly, which is a waste — I'm thinking there could be a
step like 'now that we know where the blocks are going to end up, what's the least-move way to get them
there?'"* On `LaserTank.lvl` 3 the excursion is four shots and about seventeen keypresses, and `Polish`
cannot see any of it: it is not a round trip (the board is different afterwards — the other block has sunk
in between), not a turn on the spot, not a shot at nothing. Every key in it does something. They just undo
each other. **Deletion cannot find that; only re-derivation can.**

**The observation it is built on is Michal's own** — *"the solution is basically a series of state changes
with walking in between"*. If that is what a solution *is*, the playfields it passes through are a
**ladder of positions already proved to lead to a win** — the solution is the proof. So the least-move
question has a cheap and sound answer: search for the shortest keystream that climbs that ladder,
**allowed to skip rungs**. Nothing is modelled: a rung is reached because `ApplyKey` was called and
`Game.PF` came back equal to a playfield the original run stood on.

| step | what it does |
|---|---|
| the ladder | replay the solution, keep every playfield it stood on, in order. A board it stood on twice maps to its *last* rung, so a plain round trip is skipped by the lookup alone |
| the sweep | one forward pass over the rungs. Every successor is at a strictly higher rung, so the ladder is a DAG and rung order is a topological order: each rung is expanded exactly once |
| one closure per rung | every state at a rung has the same playfield by construction, so they share one PF-preserving movement closure — a multi-source uniform-cost walk over tank poses, each seeded at the keystream length it arrived with |
| the runs | a board change is offered to its rung and then the same key is pressed again while the board keeps moving, so a k-cell ferry and a k-shot mirror push are k rungs for k keys |

Level 3 is one skip: with the first block on B7 the replan pushes it straight into the water and lands on
the playfield the original only reached five board changes later. **80 → 57 keys**, and the replay now
reads the way a person would play it. Level 7's second lap of the conveyor circuit disappears because a
shot run fires four times from the one square it can be fired from: **81 → 65**.

**The one thing that had to be got right: the space bar does not belong in a movement closure.** A shot
that hits nothing still moves the laser record, so its `StateHash` differs from the pose it was fired
from; a walk that treats a new hash as a new place to stand fires from *that*, and again, and is no longer
bounded by the pose count. The first build spent its whole pose budget at rung 0 of level 3 — an island of
twenty-four cells — and found nothing on any level. `Solver.ExpandPush` had the answer already and had
said so in a comment: walk on movement keys, then fire once from each pose the walk found. With it level 3
finishes in **25,014 `ApplyKey` calls**. What that gives up is a shot kept for its *timing*; the search
layer gives up the same thing in the same place.

| 416 solutions, `build/solutions/l0` | keys | time |
|---|---:|---:|
| as banked | 11,060 | — |
| polish only | 10,327 | 126 s |
| **polish, replan, polish** | **10,249** | 152 s |

No collection worse than polish alone, **416/416 verified through both engines**. l0 is layer 0's raw
beam, which is the *unfavourable* population; on the seven hand-supervised `data/solutions/LaserTank/`
recordings the same pass is 821 → 725, with levels 3, 4 and 7 at 80 → 57, 92 → 61 and 81 → 65.

`--replan-width` (8) bounds the states kept per rung and `--replan-nodes` (1.5M) is the backstop — level 5
of `LaserTank.lvl` is the worst in the corpus at 412,882, which is why the default is not the 400,000 the
first build shipped.

**Both are ON by default during a solve**, which is why a re-polish of the banked corpus is a no-op: all
494 solutions re-polished came back **0 shortened, 14,518 keys → 14,518**, because they were banked
already polished and already replanned. The 11,060 → 10,249 above was measured on solutions banked
*before* that became the default and there is no path in the tree that reproduces it.
