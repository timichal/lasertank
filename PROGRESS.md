# LaserTank → Godot port: progress & handoff

**Purpose:** single source of truth for this port. Read this first after a context clear.
Update the *Status* block and *Session log* at the end of every working session.

---

## Start here after a context clear

**Where the project is:** Phases 1-3 complete, Phase 4 (the solver) complete through layer 4 with
layer 5 built but not shipping, Phase 5 (presentation) not started. The rest of this file is the
reasoning behind each of those, kept because the reasoning *is* the handoff. This block is the
ninety-second version.

**What is built.** A C reference oracle (`oracle/`), a C# transliteration of the engine that traces
byte-identically to it (`src/LaserTank.Core/`), a differential fuzzer (`tools/fuzz.py`), and a
solver of five layers, four of which ship (`src/LaserTank.Solver/`). No Godot yet — that is
Phase 5.

**Build, then check nothing rotted** (about three minutes all in):

```bash
bash oracle/build.sh && bash src/build.sh      # -> build/lasertank-{core,solve}.exe
python tools/replay_all.py                     # 187 replayed, 181 win, 6 documented non-win
python tools/test_difftrace.py                 # 29 passed
python tools/sweep.py                          # 2,347/2,347 identical
python tools/test_fuzz.py                      # 25 passed  (slow: injects faults and rebuilds)
```

Those four are the fidelity gates and must be green before anything else is believed. The fifth,
`tools/verify_solutions.py`, needs solver output to check and so runs after a campaign.
`test_fuzz.py` patches `Engine.cs` and restores it — a green run leaves the tree byte-clean, and if
it ever does not, read the line-ending trap in *Environment notes* before anything else.

**Solve one level and watch it:**

```bash
build/lasertank-solve.exe --levels data/levels/Beginner-I.lvl --level 44 --verbose \
  --out build/try --nodes 400000
python tools/verify_solutions.py build/try      # both engines, byte-identical, or it did not happen
```

*(Session 20: **level 2 is solved too**, and by the same route — a new derivation, not a bigger
budget. `--from 2 --to 2` takes 65 s with no flags. What it needed is in* Phase 4 — layer 7, the
stop cell*; the two flags behind it, `--push-stop` and `--push-shot-run`, are off in the batch
solver and on in the `push-stop` rung. Read that section before adding a term to `PushH()`: it
went in wrong five separate ways and the beam found every one, which is the most transferable thing
in this file about writing a relaxation.)*

**Work a whole stretch of a collection at once.** The driver runs one level per *lane* and the
lanes share the `--jobs` slots, so raising `--lanes` fills the machine without changing the core
budget. Press a lane's number to give up on the level it is holding.

```bash
build/lasertank-solve.exe data/levels/Beginner-I.lvl --from 1 --to 40 --lanes 4
```

**Look at the board the beam settled on, not only at its score.** `--push-trace-board` prints the
best node's playfield each depth under `--push-trace`. `best=10` says the ranking key has gone flat
and says nothing about *what* the beam is looking at — and on a flat key the answer is usually that
it has found something the key likes and a player would not. It is what caught a beam that had
buried the flag under a block and scored it better than a win.

**Ask why a level is hard, before asking the solver to try harder.** This is the newest tool and
the one the last two sessions turned on most often: replay a *winning* recording — a solver's or a
human's — and print what the searchers' ranking keys do along it.

```bash
ls data/demos/LaserTank/*.lpb > build/demos.txt
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl \
    --lpb-list build/demos.txt --profile build/prof.tsv
python tools/basin.py build/prof.tsv --per-level            # in keypresses
python tools/basin.py build/prof.tsv --per-level --events   # in board changes
```

The number to read is the **longest stretch that stays at or above the best heuristic value seen so
far**. A beam follows a descent for free and an ascent only while the ascent's whole cross-section
fits in its width, so that number, not the budget, is what says whether a level is reachable at all.
Over the 402 recordings the solver has produced it is p50 6 / p90 21 keypresses. `LaserTank.lvl`
level 1 is **68**. That is the difference between "needs more nodes" and "needs a different move
set", and it is why four rounds of the interactive driver do not touch level 1 and a fifth would
not either. *(Session 18 found a second, duller reason on top of that one: the driver's push rung
doubled its width every round, so by round 5 it was running at the worst width the bench measures.
Fixed — but the ascent argument above stands on its own.)*

*(Session 19: **level 1 is solved**, and the ascent argument is why it took a new derivation rather
than a bigger budget. `--push-enables` names the moves every ranking key is flat across — see*
The fourth derivation, *under layer 6. Nobody has to know that: it is a rung of the interactive
driver, so `lasertank-solve.exe data/levels/LaserTank.lvl --from 1 --to 1` solves level 1 in 67
seconds with no flags. The flag is what to reach for in a **batch** run whose `--push-line` dies on
setup moves.)*

**Then ask where the search loses it, which is a different question and a newer tool.** `--profile`
measures the *level*; `--push-line` measures the *searcher* against a line that is known to win.
It replays one recording, keeps its state at every board change — one per push-beam depth — and
runs the real beam with those in hand, reporting per depth whether the line was generated, how the
ranking key placed it, and whether the width trim kept it:

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 1 \
    --push-line data/demos/LaserTank/00001.lpb --nodes 20000000 --budget-ms 600000 --jobs 1
```

*(`--budget-ms` matters here and its default of 4 s will bite you: without it the instrument stops
after a quarter of a million nodes and reports the line lost at depth 2 when nothing of the sort
happened.)*

Each row now carries **two** numbers, `d=` (beam depth) and `at=` (how far along the *line* the
frontier is), and they are not the same: a run emits several of the line's board changes at once, so
a frontier can be four changes along at depth one. The report was depth-indexed until session 20,
which meant that under `--push-shot-run` it called a line it was following perfectly `STALE` —
the hash it wanted had been generated and closed two depths earlier. Read the last line
(*followed to depth N of M, lost at K*) and then the row at K. This is what found
the layer-5 bug that was costing forty-eight closures a depth — see *The width was being spent on
tank poses*, below — and it is the first thing to reach for when a level with a recording will not
fall. It is also what found session 19's derivation: when the row at K is a *setup* move, one that
neither opens anywhere new to stand nor touches the barrier and across which the heuristic is flat,
the flag to add is `--push-enables 8` — see *The fourth derivation*, under layer 6. That is what
solves `LaserTank.lvl` 1, and it is off by default because it costs a level or two on both benches.

**The whole shipped chain** — a layer-0 campaign, then three passes that each attack only what the
previous one failed. `STRIDE=5` gives the 4,185-level sample every number in Phase 4 is quoted
against; each of the three passes over ~3,700 failures measured 6-8 minutes at 14 jobs, and the
campaign itself is the longer half. `STRIDE=1` is the whole 20,914-level corpus and is hours —
budget it as such rather than trusting an extrapolation:

```bash
STRIDE=5 NODES=150000 tools/campaign.sh solutions/l0 build/reports/l0.jsonl --no-macro
tools/second_pass.sh build/reports/l0.jsonl  solutions/l34 build/reports/l3n.jsonl \
                     --no-ida --no-beam --subgoal
tools/second_pass.sh build/reports/l3n.jsonl solutions/l34 build/reports/l34.jsonl \
                     --no-ida --no-beam --subgoal --sg-eval learned
tools/second_pass.sh build/reports/l34.jsonl solutions/l34 build/reports/l34pass4.jsonl \
                     --no-ida --no-beam --macro --macro-first
python tools/verify_solutions.py build/solutions/l0     # layer 0's own solutions
python tools/verify_solutions.py build/solutions/l34    # everything the three passes added
```

`report_stats.py` reads any of those reports (`--diff` compares two layers); the composite is the
*union* of the four, which is why the two verify runs are separate. A level solved by an earlier
pass is skipped by a later one, so no `.lpb` is ever written twice.

**Three rules the last four sessions each paid to learn**, and they are the reason the numbers in
this file can be trusted:

- **Bench on the corpus, not on a filtered population.** `build/reports/bench-levels.txt` is levels
  layer 0 failed. It flattered layer 1, flattered layer 2, overstated layer 3's budget scaling and
  *understated* layer 4. A bench picks parameters; only a campaign decides what ships.
- **Govern by `--nodes`, never wall clock.** A node is one `Engine.ApplyKey`. Seconds are not
  reproducible on a machine that is also running the gates.
- **Instrument before theorising.** `--sg-trace` killed two of layer 2's designs and one of layer
  3's; layer 4's `tools/rankdump.py` decided whether that layer was worth building at all. A new
  layer should report a distribution before it reports a solved count.

**What is deliberately frozen.** `original/` is a read-only historical artifact.
`src/LaserTank.Core/Engine.cs` is the transliteration and differs from a literal one by the single
word `partial`; `Engine.Search.cs` has not changed since layer 0, and layers 1-4 all kept it that
way. If a solver change seems to need an engine change, that is the signal to stop and re-read.

**Artifacts live under `build/`, which is gitignored** — they survive a context clear but not a
`git clean`. The one thing that does not need regenerating is layer 4's weight vector: it is
compiled into `src/LaserTank.Solver/Weights.cs`, so the solver is fully functional from a fresh
clone. Everything else in `build/reports/` is a measurement that can be re-run.

---

## Status

**Phases 1-3 complete. Phase 4 complete through layer 4. Phase 5 not started.**

*One number in this table moved for a reason worth reading before trusting the rest: the ferry
bench is **19/50** where every earlier session banked 20/50. That is the buried-flag fix (session
20) and it is attributed rather than assumed — reverting it alone reproduces 20/50 exactly, so
every other change since is inert at default flags, and the level it costs (`Beginner-I` 1581)
never buries a flag on its own line. A correct ranking that costs one level is still the correct
ranking; the number to compare future runs against is 19.*

| | state |
|---|---|
| C reference oracle | replays the whole corpus; ground truth, never refactored |
| C# core | **byte-identical to the oracle on all 187 recordings** with `--field --bmf` |
| Differential fuzzer | harness proven by fault injection; 20,626 cases, 0 divergences |
| Solver | four shipped layers; **472 of the 4,185-level stride sample (11.3%)**, all verified |
| Solver, layer 5 | push macros: the width was being spent on tank poses; fixed, **ferry bench 11/50 -> 20/50, deep 14/50 -> 21/50**, all verified |
| Solver, layer 6 | the read: shipped inside layer 5 and still carrying its weight there (15/50 against 9/50 without it) |
| Solver, layer 6's fourth derivation | *what does this change make possible?* — `--push-enables`, off in the batch solver, and its **own rung in the interactive driver**, where it adds 4 ferry / 5 deep levels to the rung beside it and **solves `LaserTank.lvl` 1 in 67 s with no flags** |
| Layer 5 over the corpus | **15 of 255 (5.9%) of the levels the whole chain fails**, at 27x the campaign budget — an argument for a fourth pass, not for changing the chain |
| Solver, layer 7, the stop cell | *what has to be blocked before the tank can stand next to the flag?* — `Heuristic.RouteStop`, the ferry term for a route that crosses a conveyor rather than water. Off in the batch solver, its **own rung in the interactive driver** with `--push-shot-run` and width 128, where it adds 3 ferry / 5 deep levels to the plain push rung and **solves `LaserTank.lvl` 2 in 65 s with no flags** |
| Solver, `--push-shot-run` | PushRun for the laser: a k-cell laser ferry is k successors of one expansion, not k depths. Off by default (ferry 15/50, deep 23/50 solo — a wash), on in the layer-7 rung, where level 2 needs it |
| Presentation (Godot) | not started — see Phase 5 |

**The gates, and what green looks like.** `replay_all.py` 187 replayed / 181 win / 6 documented
non-winners / 0 unexpected, and 112/112 `Tutor-with-Playbacks` matching their bundled `.ghs` on
moves *and* shots. `test_difftrace.py` 29 passed. `test_fuzz.py` 25 passed. `sweep.py` 2,347/2,347
identical. `verify_solutions.py` over `build/solutions/` — every solver `.lpb` wins on both engines
with byte-identical traces.

**What is still not ported: `MouseOperation`, and only that.** The mouse buffer is empty headless
(`MB_TOS == MB_SP` always), so the tick's mouse block never fires and no keystream can reach it —
measured, not assumed: the fuzz campaign reached it zero times. It **throws** rather than no-ops, so
if that premise ever breaks the run stops loudly. It is Phase 5 work: a UI entry point, not game
logic.

**Next action: the fourth pass, at a budget that matches what layer 5 costs.** The decision pass
session 17 left undone has been run and it came out positive — 15 of 255 (5.9%) of the levels the
shipped chain fails, all verified — so the open question is no longer *whether* layer 5 pays but
*how much of the corpus it is worth spending on it*. One push expansion is a whole closure, so this
is the one pass that has to be budgeted in tens of millions of nodes rather than hundreds of
thousands.

*Session 19 adds a second question to that pass and it is cheap to answer at the same time: run it
**twice**, once as written below and once with `--push-enables 8`, and compare. The fourth
derivation is a level or two worse on both banked benches at 4M nodes and it solves `LaserTank.lvl`
1, which nothing else does, so the population it is worth spending on is exactly what this pass
measures and nothing smaller can decide it.*

*Session 20 adds a **third** arm to the same pass, for the same reason and at the same price:
`--push-stop 1 --push-shot-run 16 --push-beam 128`. It is 18/50 on both banked benches against the
plain rung's 19 and 21, adds 3 ferry and 5 deep levels as a union, and solves `LaserTank.lvl` 2
which nothing else does — which is the whole of what is known about it. Layer 7 is the only layer
here that has never seen the corpus, so that arm is not a nice-to-have on this pass, it is the
measurement the layer is missing. Run all three arms and compare unions, not solo counts.*

```bash
# the whole population the chain fails, not a 1-in-15 sample of it, at 40M nodes
NODES=40000000 BUDGET_MS=1800000 JOBS=12 bash tools/second_pass.sh \
    build/reports/chain.jsonl solutions/l5 build/reports/l5.jsonl \
    --no-ida --no-beam --push --push-read
python tools/report_stats.py build/reports/l5.jsonl
python tools/verify_solutions.py build/solutions/l5
```

Budget it as hours, not minutes: 3,713 levels at 40M nodes is 10x the sample pass at 10x the budget.
`SAMPLE=15` first if a rehearsal is wanted — that is the run above, and it took about twenty minutes
at 12 jobs. Rebuild `build/reports/chain.jsonl` first if it is gone:

```bash
python - <<'EOF'
import json
rows={}
for f in ["l0.jsonl","l3n.jsonl","l34.jsonl","l34pass4.jsonl"]:
    for line in open("build/reports/"+f,encoding="utf-8"):
        if line.strip():
            r=json.loads(line); rows[(r["collection"],r["level"])]=r.get("solved",False)
with open("build/reports/chain.jsonl","w",encoding="utf-8") as w:
    for (c,l),sv in sorted(rows.items()):
        w.write(json.dumps({"collection":c,"level":l,"solved":sv})+"\n")
EOF
```

**And the benches as a check, not as a decision**: `bench.sh` on `build/reports/ferry-levels.txt`
and `deep-levels.txt` at 4M nodes with `--no-ida --no-beam --push --push-read` should give **20/50**
and **21/50**, and adding `--push-beam 48 --push-per-board 0 --push-eval work --push-depth 400` — the
configuration session 17 shipped — should still give 11/50 and 14/50.

**One smaller thing that is ready and unmeasured.** `Trim.Polish` removes 47% of a subgoal
solution's keypresses and `Replan.Improve` (session 21) another slice on top, so **every banked
`.lpb` under `build/solutions/` is longer than it needs to be** - `--polish DIR` over each of them
now runs both passes and would refresh the corpus that layer 4 is fit on and `--profile` measures,
which is not nothing: shorter trajectories change the ascent statistics the whole layer-5 argument
rests on. Measured on the 416 solutions in `build/solutions/l0`, one such pass is 11,060 -> 10,249
keypresses in about 150 s.

**Still worth doing, and no longer blocking: the `lasertanksolutions.blogspot.com` goal-board
harvester.** It was session 16's agreed next action because the ferry population was n=2; it is
n=20 now, hand-recorded, so the harvester is a way to make that 200 rather than a prerequisite for
measuring anything. See *Open questions* for what a post contains and the feasibility check on two
downloaded images. Sequence, roughly: scrape post URL + collection + level + image URLs; decode a
16x16 board from a screenshot by template matching, bootstrapping the codebook from the *start*
images, whose true contents are already known from the `.lvl`; bank `(collection, level, goal PF,
moves, shots)`; then a `--goal-board` mode that ranks by cells-still-differing. **Solutions found
that way are hint-assisted and never enter the solver's headline rate.** The blog runs in level
order from `LaserTank.lvl` 1 (`/2016/06/1-boot-camp.html`), which makes the index trivial for the
one collection the demos cover.

Phase 5 (Godot) is untouched and its section below is a plan rather than a wish list; Phase 3's
fuzzer can keep running in parallel on new seeds and the 12 collections its first campaign never
touched.
**Blocked on:** nothing.

---

## Goal & hard constraints

Modernize LaserTank 4.1.2 (public domain, Jim Kindley / Yves Maingoy) into Godot.

1. **Game logic must be preserved exactly, quirks included.** Many "bugs" are load-bearing —
   whole level packs exist *only* to exploit them. Upstream says so explicitly:
   *"Some of the tricks are bugs that have been intentionally left in the software because they
   make the game more interesting."* (`data/quirks/tutor/Tutor-ReadMe.txt`)
2. **Community file formats stay readable and writable**: `.lvl`, `.lpb`, `.hs`, `.ghs`, `.ltg`.
   25 years of community content depends on them.
3. Equivalence with the original must be *demonstrated*, not asserted.

---

## The key insight

**The game is a deterministic 20 Hz tick machine driven by a keystroke stream.**

Live keypresses do not act directly. `WM_KEYDOWN` (`LTANK.C:570`) appends the raw VK code to
`RecBuffer` via `AddKBuff`; the timer tick consumes one key per tick when the world is quiescent
(`LTANK.C:613`). Playback and live play run *the same code path*. No RNG, no floats, no
frame-rate dependence.

Consequences:

- **Validation does not require solutions.** Feed both engines the same keystream, dump a per-tick
  state trace, diff. This is the primary correctness strategy.
- A solver is *corpus extension*, not the validation plan. Do not let it become the critical path.

---

## Architecture decision

Three engines, one truth.

| # | Engine | Role |
|---|---|---|
| 1 | **C reference oracle** | Original `LTANK2.C`, unmodified logic + stub Win32 layer, headless, emits traces. **Ground truth forever. Never refactor.** |
| 2 | **Godot core** | Literal transliteration. Headless, pure, no `Node`/rendering/signals. Steps one tick. |
| 3 | **Presentation** | Godot nodes reading core state, interpolating between ticks. |

**Core language: C#** (Godot .NET). Fast enough for a solver doing millions of state expansions,
readable, one build across desktop targets.
Rejected: GDScript (10–50× too slow for search); C++ GDExtension (max fidelity but keeps us
shipping 25-year-old code and complicates web export).

**Transliterate literally.** Including the ugly parts. Idiomatic rewriting is how quirks die.

---

## Phases

### Phase 1 — Oracle & tooling  ☑
- ☑ Build `LTANK2.C` headless, **preserving logic-carrying side effects**. Key move: stub only the
  Win32 *API*, never LaserTank's own code — see `oracle/README.md`. `UpDateLaserBounce` compiles
  verbatim, so hazard #1 survives for free.
- ☑ Drive from a keystream file; emit per-tick trace.
- ☑ Parsers for `.lvl` / `.ghs` / `.lpb`.
- ☑ Trace format — see `oracle/README.md`.
- ☑ Replay all 187 corpus `.lpb` (186 shipped + level 21 decoded from its packed `.txt`).
- ☑ For `Tutor-with-Playbacks`, assert move/shot counts match its bundled `.ghs` — **112/112 exact**.

**Exit criterion — met, restated.** "186/186 reach the flag" was the wrong bar: the corpus does not
contain 186 solutions. 181 of 187 reach the flag; the other 6 are documented incomplete recordings.
The suite encodes that distinction, so it is a real regression gate rather than a permanently red
run. See "The six non-winning recordings" below.

Toolchain: MinGW-w64 (WinLibs, gcc 16.1, UCRT) installed via
`winget install BrechtSanders.WinLibs.POSIX.UCRT`. Nothing else is needed to build the oracle.

### Phase 2 — Transliterate the core  ☑
- ☑ **step 0:** `tools/difftrace.py` — compare two traces (or two directories of them), report the
  first diverging tick and the first field that moved. Nothing downstream is checkable without it.
  Self-tested by `tools/test_difftrace.py`; see "The Phase 2 harness".
- ☑ **step 1:** the C# projects exist and build; the load path and the tick frame are
  transliterated, and an empty-keystream run traces identically to the oracle. See below.
- ☑ **step 2:** `CheckLoc` and `MoveObj`, plus `MoveObj`'s closure — `TranslateTunnel`,
  `UpDateTankPos`, `UpdateUndo`, `ResetUndoBuffer`.
- ☑ **step 3:** `MoveTank` and `AntiTank`. They go together: `Tick()` runs `AntiTank()` after
  every consumed key, so `MoveTank` on its own advances the trace by nothing.
- ☑ **step 4:** the laser subsystem — `FireLaser`, `MoveLaser`, `CheckLLoc`, plus `KillAtank`,
  `UpDateLaserBounce`, `TestIfConvCanMoveTank` and the `SlideO`/`SlideMem` stack helpers.
- ☑ **step 5:** `ConvMoveTank`, `IceMoveT`, `IceMoveO` — the ice and conveyor code, and with it
  quirk #6 and the first real exercise of hazard #1.

**Exit criterion — MET.** `difftrace.py build/t-oracle build/t-csharp` reports **187/187 identical**
with `--field --bmf`, exit 0. `MouseOperation` is the only unported function and is unreachable
from a keystream.

Run both engines with `--field` (and `--bmf` while porting `Animate`) so the diff has the whole
playfield to bite on, not just the hashes.

**Exit criterion:** identical traces on all 187, `--field` included. `BMF`/`AniLevel` differences
are cosmetic (hazard #2) — worth investigating as a tripwire, but not a correctness failure.
`difftrace.py` encodes exactly that distinction: exit 1 for a logic divergence, exit **3** for a
cosmetic-only one, and `--strict` if you want to hold the cosmetic line too.

#### The Phase 2 harness

```
bash oracle/build.sh && bash src/build.sh
python tools/test_difftrace.py                                  # trust the differ first
python tools/replay_all.py --traces build/t-oracle --field --bmf
python tools/replay_all.py --traces build/t-csharp --field --bmf \
       --engine build/lasertank-core.exe
python tools/difftrace.py build/t-oracle build/t-csharp -q      # -q: failures only
```

Add `--pack game-objects` to both replay lines for the 16-recording fast loop (one level per
object); run the whole 187 before believing anything. The full pair takes a few minutes and about
327 MB per side.

`replay_all.py --engine` is what makes both sides one script, and it works because **the C# CLI
takes the oracle's arguments and emits byte-identical trace lines** — same field order, same
spacing, same `%08lx` hashes, same `#` header and result footer (`oracle/driver.c`, `trace_tick`).
Do not invent a nicer format: the differ is textual on purpose, so any drift shows up as a
divergence rather than as a parser bug.

**When a trace stops early.** `NotPortedException` used to take the process down before the
buffered trace was flushed, so a partly-ported engine produced *no* trace and `difftrace.py`
reported `UNUSABLE` — no signal at all. The CLI now catches it, closes the trace after the last
*complete* tick, writes `# result=NOTPORTED`, prints the function and the tick to stderr and exits
**4**. `difftrace.py` then says `DIVERGE length mismatch after N ticks`, which is the useful
reading: the ported half matched for N ticks and the port has to continue at N+1. Nothing about
this softens the failure — the point of the exception is that it is never mistaken for a result,
and `NOTPORTED` is not a result.

**First milestone, before `CheckLoc` — done.** Run with an empty keystream (`--keys ""`). Both
engines trace exactly two lines, `t=0` and `t=1`: level load, then one idle tick. Matching those
two lines means the `.lvl` parser, the `TGAMEREC` layout, `BuildBMField`, `PutLevel`, `Animate`,
the fnv1a hashes and the trace formatting are already right — a large fraction of the surface
area, verified before a single rule of game logic is written.

#### What the C# side looks like now

```
src/LaserTank.Core/    Objects.cs  GameState.cs  LevelFile.cs  Engine.cs
src/LaserTank.Cli/     Program.cs  TraceWriter.cs      -> build/lasertank-core.exe
```

Ported: `BuildBMField`, `PutLevel`, `UpDateTank`'s `TankDirty` write, `GameOn`, `Animate`,
the logic-carrying half of `LoadNextLevel`, the whole `Tick()` frame, the
`SendMessage`/`PostMessage` death split (quirk #8: `SendDead` runs inline, `PostDead` queues for
`Pump()` after the tick, exactly as the oracle's stub message pump does), — step 2 —
`CheckLoc`, `TranslateTunnel`, `UpDateTankPos`, `UpdateUndo`, `ResetUndoBuffer`, `MoveObj`, —
step 3 — `MoveTank`, `AntiTank`, and — step 4 — `FireLaser`, `MoveLaser`, `CheckLLoc`,
`KillAtank`, `UpDateLaserBounce`, `TestIfConvCanMoveTank` and the `SlideO`/`SlideMem` helpers.

and — step 5 — `ConvMoveTank`, `IceMoveT`, `IceMoveO`. That is every function a keystream can
reach.

**`MouseOperation` is the only remaining stub, and it still throws rather than no-ops.** A silent
stub would produce a *plausible* wrong trace, which is the one failure mode this whole approach
exists to prevent — and step 4 turned that from an argument into an incident report: the tick frame
had been passing `0` instead of `S_Fire` to `FireLaser` since step 1, and only the exception kept
it from silently corrupting `laser.Good`. Today `replay_all.py --engine build/lasertank-core.exe`
reports 187 replayed / 181 win / 0 unexpected / 112 `.ghs` exact — the oracle's own numbers.

Worth knowing before step 4: `AntiTank` is a `wasIce` writer even though it never names the flag.
Its four scans are `while (CheckLoc(...))` loops, so whichever scan ran last leaves `wasIce`
holding its final probe — and `MoveTank`, `IceMoveT`, `IceMoveO` and `ConvMoveTank` all read it
after their own `CheckLoc`. That is quirk #3 with a longer reach than the three callers the
hazard list names.

Decisions worth not relitigating:

- **`byte[16,16]`, not `sbyte`.** `PF`/`PF2`/`BMF`/`BMF2` are `char[16][16]` in C, and gcc's `char`
  is signed. It cannot matter: `BuildBMField`'s 2003 sanitisation forces every cell to `<= 0x19`
  or to a tunnel (`0x40 | id<<1 | wait`), so nothing above `0x7F` survives a load. The single place
  the original's signedness is visible is `GetOBM(char)`'s `ob > -1` guard, which `Obj.GetOBM`
  keeps verbatim.
- **Original names, not C# conventions.** `Game`, `ScoreMove`, `SlideO`, `wasIce`, `IceMoveO`.
  Renaming is how a quirk stops looking like a quirk.
- **`net8.0` for both projects.** It is the lowest TFM Godot 4.x accepts, so Phase 5 can reference
  `LaserTank.Core` unchanged; the CLI sets `RollForward=LatestMajor` because only the .NET 10
  runtime is installed.
- **The undo buffer is carried even though nothing headless reads it.** `UndoStep` is unreachable
  from a keystream, so the `TGAMEREC` snapshots `UpdateUndo` stores are write-only. `UndoP` is not:
  `MoveObj`'s tunnel path decrements it (quirk #7), so its growth (`UndoBufSize` in steps of 200)
  and its roll-over at `UndoMax` have to be exact, and the cheapest way to be sure of that is to
  keep the buffer that they are the indices into. The two `GlobalReAlloc == NULL` branches in
  `UpdateUndo`/`ResetUndoBuffer` are *not* carried: the oracle's stub is plain `realloc`
  (`oracle/win32_stub.c:74`), so they are unreachable on both sides. They are the only pieces of
  either function left out.
- **Trace line 1 differs by design** (`# lasertank core trace` vs `# lasertank oracle trace`).
  Line 2 and every tick line are byte-identical; `difftrace.py` reads level/name/author/keys off
  line 2 to check both sides ran the same input.

What `difftrace.py` gives you when it does go wrong:

```
=== first divergence: tick 90 (line 91) ===
first field:  T.dir 1 -> 4                 [tank]
also:         T.firing 1 -> 0, L.y 11 -> 12, S.shots 23 -> 22
    PF[x=4,y=9]       0d mirror dr      -> 19 thin ice
```

The field name *is* the localisation — `S.moves` sends you to `ScoreMove`, `SlO.dy` to `IceMoveO`,
`P` to the key-consume test at `LTANK.C:613`. The summary at the end counts how many ticks each
field diverges on, which separates "one wrong cell" from "everything after tick 90".

### Phase 3 — Differential fuzzing  ◐ (harness done and proven; more campaigns welcome)
This is what finds the remaining divergences. Random keystreams (weight toward fire/turn), both
engines, diff traces, shrink any divergence to a minimal repro. Run across all 20,914 levels.
Cheap, unlimited, needs no solutions.

`tools/fuzz.py` does all of that; `tools/test_fuzz.py` is what makes a green run mean something.

#### The Phase 3 harness

```
bash oracle/build.sh && bash src/build.sh
python tools/test_difftrace.py                       # trust the differ      29 cases
python tools/test_fuzz.py                            # trust the fuzzer      25 cases, ~90 s
python tools/sweep.py                                # 2,347 levels, empty keystream, ~50 s
python tools/fuzz.py --each 3 --seed 1               # 6,090 cases over the flagship, ~140 s
```

**Run `test_fuzz.py` before believing any green fuzz run.** It is the one step that is easy to
skip and the one the whole phase rests on: a fuzzer that has never gone red is untested, and
"20,626 cases, no divergence" is then a claim about the fuzzer rather than about the port. It
patches `Engine.cs` with two faults a transliteration would plausibly make, rebuilds, and requires
that each is found, shrunk, and independently reproducible — then restores `Engine.cs` **in
bytes** (an earlier version used `write_text` and silently rewrote all 1,362 lines LF→CRLF, which
`git diff` hides under `core.autocrlf`) and requires green again. If it is ever killed between the
patch and the restore: `git checkout src/LaserTank.Core/Engine.cs`.

**The shrinker is the deliverable, not an extra.** A divergence at key 300 of 400 on level 1,712
is not a bug report; `level 1712, keys "rrfud"` is. `fuzz.reduce_keys` is three passes:

1. **Shortest diverging prefix**, by binary search — O(log n) runs, and where nearly all the length
   goes. Not assumed to be sound: every prefix that survives is one that actually ran and actually
   diverged, and pass 2 cleans up after it.
2. **Delta debugging** (ddmin) over what is left.
3. **Measure 1-minimality rather than claim it** — delete each remaining key and check the
   divergence goes away. ddmin's exit condition guarantees this on paper; a bug report should not
   quote a guarantee it did not check. Nearly free, because the candidates are already cached.

All three hold the divergence **signature** fixed — the name of the first field that moved, which
is `difftrace.py`'s localisation — so what comes out is a reduction of the bug you started with and
not a different one found along the way. `--shrink-any` relaxes that when the same root cause
surfaces through a different field once the noise is gone.

Each finding gets a directory under `--out`: the minimal keystream, both traces re-run with
`--field --bmf`, the full `difftrace.py` report, the level as ASCII, and the exact commands. Plus
`findings.json` for scripts. Findings are deduped by signature, so a systematic bug reports once
rather than 9,000 times.

**Fuzzing runs without `--field`/`--bmf`** — those add 512 hex bytes per tick each, and the default
trace already carries `H=fnv1a(PF),fnv1a(PF2)`, so a playfield divergence still shows up, as a hash
rather than a cell. The minimal repro is then re-run *with* both, and the tool warns if the wider
trace does not reproduce the same signature.

#### What the first campaign covered

Six keystream shapes, because one shape is one bias — varying length and the fire/turn weights is
how you reach code the default under-samples:

| run | cases | shape | keys consumed |
|---|---:|---|---:|
| `--each 3` | 6,090 | every flagship level ×3, 48 keys | 65% |
| `--keys 200` | 3,000 | long streams | 42% |
| `--p-fire 0.60` | 3,000 | shot-heavy | 71% |
| `--p-fire 0.10 --p-repeat 0.80` | 3,000 | movement-heavy, 96 keys | 51% |
| `--p-fire 0.15 --p-repeat 0.05` | 3,000 | almost pure turning | 84% |
| every quirk pack, `--each 8` | 2,536 | 64 keys | 20–100% |

**20,626 cases, 3,751,638 tick-lines, 0 divergences.** Consumed keys, not generated keys, is the
honest coverage number: random play drowns or shoots the tank early, so the two differ by nearly
half, and `fuzz.py` reports both.

**Two things the campaign taught about running it, both worth not rediscovering:**

- **Four of the ten quirk packs ship `.LVL`, six ship `.lvl`** — `tutor`, `tutor-with-playbacks`,
  `rotary-mirrors` and `game-objects` are the uppercase ones, i.e. exactly the four biggest. A
  `*.lvl` shell glob skips them in silence. `replay_all.py` already used `suffix.lower()`;
  `sweep.py` now does too. Anything new that walks `data/quirks/` must.
- **Random keystreams are shallow.** Over half of all runs end `DEAD`, and the flagship's own
  level 1 needs 149 keypresses to win. Random play is wide but not deep, which is the argument for
  Phase 4's solver being the *other* kind of coverage rather than a nice-to-have: a solved level is
  a long, legal, non-random path through the engine.

### Phase 4 — Solver  ☑ (layers 0-4; whole corpus run and every solution verified)

**The bar, measured before building anything.** Best-known solution cost from the 13 `.ghs` files,
by the difficulty rating in each level record:

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
without scoring (`Engine.cs:491`), so "103 moves + 46 shots = 149 keypresses" is a lower bound —
add one key per direction change. A keypress-level exhaustive search reaches the ≤20 bucket and
nothing else, which is why the plan is layered.

**The plan.** Layer 0 is built (below). Above it:

- **Layer 1 — macro-actions.** ☑ Built and measured. `Goto(x,y,dir)` — a movement closure *over
  the engine*, not a grid A* — plus `Shoot`, so depth becomes the shot count. It wins on shallow
  branchy levels and loses on deep ones; the numbers and the reason are below, and the reason is
  the argument for layer 2.
- **Layer 2 — subgoal decomposition.** ☑ Built and measured. Find what blocks the flag, and accept
  a move because it removed one of those things rather than because a heuristic went down. The
  obstacles are derived from the *executed* movement closure, not from a model — the section below
  has the version that used a model instead, and the measurement that killed it. **As a second pass
  it is worth roughly twice layer 1: +40 against +21**, and the two are complementary (441 together).
  As a portfolio member it loses, for the same structural reason layer 1 does.
- **Layer 3 — restarts.** ☑ Built and measured. The subgoal beam is re-run when it dies of an
  *empty frontier* with budget still in hand, each restart doubling its width and slack. It is the
  first layer that is **strictly additive** — attempt 0 is layer 2 exactly, and a restart only ever
  spends budget that was already forfeit — so it needs no portfolio bet. It is also the smallest
  win: **+4 on the pass, 441 -> 444 composite**, and the section below has the negative result that
  is worth more than the number.
- **Layer 4 — learning.** ☑ Built and measured. A learned evaluation over board features,
  fit to 20,148 ranking groups taken from 652 winning trajectories, replacing `WorkDistance` as
  the subgoal beam's *ranking key only*. It is the largest win since layer 2: **+30 on the pass,
  444 -> 472 composite, none lost**. The measurement that authorised it is worth more than the
  number — the successor the winner used is in the expansion's output **97.6%** of the time and
  `WorkDistance` ranks it **100th of 395** — and so is the one that killed the second half of the
  plan: feeding the newly solved levels back in and refitting *halves* what the model discovers.
  Hints as landmarks was not built; the size was already measured and says not to — only
  **175 of 20,914** hints are recipe-grade (>=2 grid references or numbered steps), concentrated
  where search fails (0.4% of Kids, 3.5% of Hard, 7.3% of Deadly), which is a tail tool against a
  3,700-level tail.

**Be realistic.** No public automated LaserTank solver solves the full set. The deliverable is a
solved-count-vs-budget curve, Kids-first ordered by `.ghs` cost, not a promise of 20,914.

#### Layer 0 — the search API and the harness  ☑

`Engine.cs` gained exactly one word (`partial`). Everything else is additive:

- **`src/LaserTank.Core/Engine.Search.cs`** — `Snapshot`/`Restore` of the whole mutable engine,
  `ApplyKey` (one keypress, then tick to quiescence), `StateHash` for a transposition table, and
  `ActionKeys`. The rule is **restore everything, hash a subset**: staleness is load-bearing here
  (`wasIce`, quirk #3; `WaitToTrans`), so the hash keeps them while dropping `BMF`, the counters
  and the path.
- **`src/LaserTank.Solver/`** → `build/lasertank-solve.exe`. Weighted beam + IDA* over macro-steps,
  a flag-distance heuristic, the trimmer, and a parallel batch harness that writes each solution as
  a **`.lpb`** — a real recording, playable in the 2010 binary, not a private format.
- **`tools/verify_solutions.py`** — replays every produced `.lpb` through the *unmodified* oracle
  and the core with `--field --bmf` and requires WIN on both plus byte-identical traces.

Two bugs the harness found on itself, both worth not re-discovering:

- **A macro-step is not bounded by anything cheap.** Level 1491 ("Grand Prix 2", hint: *"get on the
  conveyor and watch"*) takes **3,652 ticks for one keypress** — the tank rides a closed conveyor
  circuit around the whole board. A 512-tick cap called that a hang and threw the level away.
  `ApplyKey` now detects *cycles* (sampled state hashes, started only after 256 ticks so the common
  case pays nothing) and keeps the tick cap only as a backstop. Genuine eternal cycles exist and
  must stay reportable: Tutor 43 is literally "Smallest eternal cycle".
- **`Restore` rewinds `RecP` but the keystream is one shared array.** A breadth-first search then
  silently corrupts its own answers: siblings overwrite each other's keys and the winning node
  reports whichever prefix was written last. A depth-first search never notices — IDA* was green
  while every beam solution failed to replay. The snapshot now carries its key prefix. Caught only
  because the harness replays each solution before writing it; that self-check earns its place.

**First numbers** (the 150 cheapest-by-`.ghs` levels of `LaserTank.lvl`, 3 s and 400k macro-steps
per level, 8 workers, 20 s wall):

| tier | attempted | solved | median keypresses / record |
|---|---:|---:|---:|
| Kids | 81 | 66 (81.5%) | 1.6× |
| Easy | 61 | 37 (60.7%) | 1.6× |
| Medium | 8 | 7 | 1.6× |

**110/110 verified through both engines**, byte-identical traces, and **73 of them match the
`.ghs` record exactly**. The 40 unsolved stopped on budget (30), a beam dead end (5) or the IDA*
depth cap (5) — that breakdown is the argument for layer 1 rather than for a bigger budget.

A second, larger sample on the easiest collection: **`Beginner-I.lvl`, 400 cheapest levels, 2.5 s
each, 12 workers — 384 solved in 12 s wall, 384/384 verified, 332 matching the record exactly**,
median 1.6×, exactly one solution over 10× (10.4×, and the trimmer could not shorten it). Verifying
all 384 through both engines takes 11 s, so the gate is cheap enough to run on every campaign.

#### The campaign — the whole corpus, both layers  ☑

**How it is budgeted, and why that changed.** The first run of this was wall-clock-budgeted, 4 s a
level, and it was thrown away: the test gates and the layer-1 tuning benches were running beside
it on the same 16 cores, so its budget bought a varying amount of work and its per-tier rates were
not reproducible. A campaign is now governed by **`--nodes`, a count of `Engine.ApplyKey` calls** —
equal work, load-independent, and the only way two layers can be compared honestly. `--budget-ms`
survives as a backstop against a level whose ticks are pathologically slow (the Grand Prix case
above). `tools/campaign.sh` runs one campaign over all 13 collections into one report;
`tools/report_stats.py` reads it, and `--diff` compares two.

**And it is a 1-in-5 stride sample, not the whole corpus, which is a scoping decision worth
stating.** All 20,914 levels at a budget worth having is about six hours per layer — the hard
collections spend the whole budget on nearly every level — and this campaign exists to *measure
per-tier rates*, not to bank solutions. Level numbers are not sorted by difficulty, so every fifth
level of every collection is an unbiased sample of each tier; `sweep.py` already used the same
trick. `STRIDE=1` runs the lot when banking solutions is the point.

**The stride matters for more than runtime, and getting it wrong once proved it.** An earlier,
aborted campaign was ordered cheapest-by-`.ghs` and stopped part-way, so its first 1,451
`Beginner-I` levels were *the cheapest 1,451* — a sample that reported a median of 10 shots per
level where the unbiased stride sample reports **16 shots against 27 moves, with 15% needing no
shot at all**. A truncated cheapest-first run is not a sample of the collection, and any number
taken from one is wrong in a direction that flatters the solver.

**150,000 `ApplyKey` calls per level, every 5th level of all 13 collections — 4,185 levels —
ordered cheapest-by-`.ghs` first.** Layer 0 is the raw-keypress portfolio (IDA* + beam); each
"+ pass" is `tools/second_pass.sh` re-attacking the levels the previous column failed, at the same
budget. The final column is **layer 2's subgoal beam followed by layer 1's macro beam**, which is
the composite that ships.

| tier | levels | layer 0 | + layer 1 pass | + layer 2 passes | + layer 3 passes | + layer 4 passes | median ratio |
|---|---:|---:|---:|---:|---:|---:|---:|
| Kids | 960 | 303 (31.6%) | 319 (33.2%) | 339 (35.3%) | 341 (35.5%) | **359 (37.4%)** | 1.6× |
| Easy | 2,118 | 84 (4.0%) | 89 (4.2%) | 94 (4.4%) | 95 (4.5%) | **104 (4.9%)** | 1.7× |
| Medium | 784 | 7 (0.9%) | 7 (0.9%) | 7 (0.9%) | 7 (0.9%) | **8 (1.0%)** | 1.6× |
| Hard | 257 | 0 | 0 | 0 | 0 | **0** | — |
| Deadly | 56 | 1 (1.8%) | 1 (1.8%) | 1 (1.8%) | 1 (1.8%) | **1 (1.8%)** | — |
| unrated | 10 | 0 | 0 | 0 | 0 | **0** | — |
| **all** | **4,185** | 395 (9.4%) | 416 (9.9%) | 441 (10.5%) | 444 (10.6%) | **472 (11.3%)** | **1.6×** |

| collection | levels | layer 0 | + layer 1 pass | + layer 2 passes | + layer 3 passes | + layer 4 passes |
|---|---:|---:|---:|---:|---:|---:|
| `Beginner-I` | 400 | 143 | 150 | 162 | 164 | **172** |
| `Beginner-II` | 276 | 82 | 86 | 90 | 90 | **95** |
| `Challenge-I` | 400 | 39 | 42 | 44 | 44 | **48** |
| `LaserTank` | 406 | 34 | 35 | 38 | 38 | **41** |
| `Challenge-III` | 400 | 31 | 34 | 36 | 36 | **38** |
| `Special-I` | 105 | 19 | 20 | 20 | 20 | **20** |
| `Challenge-II` | 400 | 17 | 18 | 18 | 19 | **22** |
| `Challenge-IV` | 400 | 14 | 15 | 15 | 15 | **16** |
| `Gary-I` | 400 | 7 | 7 | 8 | 8 | **10** |
| `Sokoban-II` | 348 | 4 | 4 | 5 | 5 | **5** |
| `Sokoban-I` | 400 | 3 | 3 | 3 | 3 | **3** |
| `Challenge-V` | 185 | 1 | 1 | 1 | 1 | **1** |
| `Gary-II` | 65 | 1 | 1 | 1 | 1 | **1** |

**The unsolved are unsolved on budget, not on structure: 3,636 of layer 0's 3,790 stopped at
`budget` (95.9%) and only 154 at a beam dead end.** No errors, no `NOTPORTED`, no crashes in
8,370 level-solves. That matters for reading the low tiers: `Hard` at 0/257 is not the search
failing to find a route, it is the search never getting near the end of one.

**Every solution verified through both engines** — `tools/verify_solutions.py` replays each `.lpb`
through the *unmodified* C oracle and the C# core with `--field --bmf` and requires WIN on both
plus byte-identical traces. **416/416 verified** for the layer-1 composite, 280 matching the `.ghs`
record exactly, median 1.6× and worst 5.0×; the separate all-layer-1 run verified 381/381 the same
way; layer 2's two passes verified **46/46**. That is 843 solver-produced recordings replayed
tick-for-tick through the 25-year-old C, with zero divergences — and none of it random: these are
long, legal, *winning* paths, which is the coverage a fuzzer cannot reach.

**Where the artifacts are** (under `build/`, gitignored, so they survive a context clear but not
a `git clean`):

| path | what |
|---|---|
| `build/reports/l0.jsonl` | the layer-0 campaign, 4,185 rows |
| `build/reports/pass2.jsonl` | the second pass over its 3,790 failures |
| `build/reports/l1.jsonl`, `l1b.jsonl` | layer 1 as a portfolio member, macro first / macro last |
| `build/reports/bench-budget.txt` | bench 1 and 2, the three-budget sweep |
| `build/reports/bench-portfolio.txt` | the share sweep both level sets |
| `build/reports/verify-composite.txt` | 416/416 through both engines |
| `build/solutions/l0/<collection>/NNNNN.lpb` | the 416 composite solutions |
| `build/reports/{deep,bench}-levels.txt` | the two bench level lists, so the benches are repeatable |
| `build/reports/l2pass.jsonl` | layer 2's pass over layer 0's 3,790 failures (+40) |
| `build/reports/pass3.jsonl` | layer 1's macro beam over what that left (+6) |
| `build/reports/l2first.jsonl`, `l2last.jsonl` | layer 2 as a portfolio member, both orderings |
| `build/solutions/l2pass/<collection>/NNNNN.lpb` | the 46 those two passes found |
| `build/reports/l3n.jsonl` | layer 3's pass over layer 0's 3,790 failures (+44) — **the shipped one** |
| `build/reports/l3r.jsonl` | the same pass restarting from the reserve instead (+43) |
| `build/reports/l3pass3.jsonl` | layer 1's macro beam over what layer 3 left (+5) |
| `build/reports/b3-{off,on}.jsonl` | the 400k budget control, 1-in-3 of the pass population |
| `build/solutions/l3n/<collection>/NNNNN.lpb` | the 49 layer 3's chain found |
| `build/reports/rank.tsv` | layer 4's instrument, 20,148 groups / 5.9M candidates (484 MB) |
| `build/reports/rank2.tsv` | the same with the 74 fed back in — the round that lost |
| `build/reports/l4-fit.txt`, `l4-fit2.txt` | the two fits, held-out top-k for each |
| `build/reports/eval-weights.txt` | **the shipped vector** (also baked into `Weights.cs`) |
| `build/reports/eval-weights2.txt` | the refit; measured worse at discovering, kept with its numbers |
| `build/reports/w-seed.txt` | the WorkDistance-equivalent control vector |
| `build/reports/l4.jsonl` | layer 4 *replacing* layer 3's pass over the 3,790 (+69, −3) |
| `build/reports/l4b.jsonl` | the same after the refit (+72, and half the discovery) |
| `build/reports/l34.jsonl` | **the shipped one** — layer 4 *after* layer 3, over its 3,746 (+30) |
| `build/reports/l34pass4.jsonl` | layer 1's macro beam over what that left (+3) |
| `build/solutions/l34/<collection>/NNNNN.lpb` | the 33 layer 4's chain adds |
| `build/reports/read-corpus.tsv` | layer 6's read over the same 4,185 stride sample, one row a level |
| `build/reports/read-demos.tsv` | layer 6's `--read-dump`, 391 board changes of the nine hand recordings |
| `build/reports/prof-demos.tsv` | `--profile` over the hand recordings; `tools/basin.py` reads it |
| `build/reports/ferry-levels.txt` | 50 `Beginner-I` FERRY/SOKOBAN levels the shipped chain fails - **the bench the read is measured on**, banked because the two older lists contain almost no ferry |
| `build/reports/chain.jsonl` | the shipped chain's final per-level state (4,185 rows, 472 solved), so `second_pass.sh` can be pointed at everything it still fails |

Regenerate any of it with `tools/campaign.sh` / `tools/second_pass.sh`; read it with
`tools/report_stats.py` (`--diff` for two layers). The tuning benches are `tools/bench.sh`, one
labelled configuration over one of the two banked level lists — and its header repeats the warning
those lists have earned four times over: a bench picks parameters, a campaign decides what ships.
`SAMPLE=N` on `second_pass.sh` takes every Nth failure instead of all of them, which is how the
400k control (`b3-{off,on}.jsonl`, `SAMPLE=3` → 1,268 levels) was measured. Layer 4's dataset is
`tools/rankdump.py` (instrument) and `tools/fit_eval.py` (bare: the distribution; `--fit`: the
weights and a regenerated `Weights.cs`) — rebuild after a fit, the vector is compiled in.

**Where this leaves the deliverable.** The Phase 4 promise was "a solved-count-vs-budget curve,
Kids-first ordered by `.ghs` cost, not a promise of 20,914". At 150k nodes that curve reads:
**Kids 35%, Easy 4%, everything else ≈ 0**, and the binding constraint is depth, not correctness.
Layer 3 moves that to Kids 35.5% / Easy 4.5% and does not change the shape, which is itself the
result: see below. Layer 4 moves it to **Kids 37.4% / Easy 4.9%** and does not change the shape
either — but for the first time the reason it does not is *measured* rather than inferred: the
winning line is inside the beam's width 10% of the time, and the other 90% is where the curve is.

#### Layer 1 — macro-actions  ☑

**The action set.** `Goto(x, y, dir)` — drive the tank somewhere, spending as many keys as
that takes — plus `Shoot`, one space bar. A solution is an alternation of the two, and that is
*complete rather than restrictive*: any keystream is a run of direction keys, a space, a run of
direction keys, a space, …, so searching (Goto, Shoot) pairs expresses everything layer 0's raw
five-key search could. What changes is the depth. **Search depth is now the number of shots.**
In the unbiased 1-in-5 sample of `Beginner-I` the median level needs **16 shots against 27
moves**, and **15% need no shot at all** — those are solved by one Goto with nothing after it.

**The Goto is a sub-search *in* the engine, not a model of it.** The plan said "A* over tank
movement (ice slides, conveyors and tunnels resolved by a deterministic sub-search)". A grid A*
would have to re-derive `MoveTank`'s turn-costs-a-key rule, `IceMoveT`'s slide, `ConvMoveTank`,
`TranslateTunnel`'s pairing, and the fact that `AntiTank()` runs on every key-consuming tick —
i.e. it would be a second implementation of the game, free to drift from the one being ported.
Four phases have gone into there being exactly one. So `Goto` is a **breadth-first closure over
`Engine.ApplyKey` with the four direction keys, deduplicated by `StateHash`**: ice, conveyors,
tunnels, pushed blocks and anti-tank turns are "resolved" by being *executed*. It costs more per
node than a grid A* would and it cannot be wrong. The closure is the expensive part and the cap
on it (`--closure-nodes`, default 1500; `--closure-depth`, default 40 keys) is the one place
layer 1 gives up completeness — a knob with a number on it rather than a hidden constant.

**Two prunes, and the difference matters.**

- *A shot that changes nothing is dropped, and that is lossless.* If the state hash is identical
  after the space bar then nothing happened at all — not even an anti-tank turn, because
  `AntiTank()` runs inside the same key-consuming tick and any move it made would show. So the
  successor **is** the state it was fired from, which is already in this expansion's closure, and
  every continuation through it is some other (Goto, Shoot) pair of the same parent.
- *The closure cap is not lossless.* See above.

**The escape hatch.** Alongside the shot successors, the `--move-only` (default 6) closure states
that end nearest the flag are kept as pure-`Goto` successors. Without them a level needing no
shot at all and a solution longer than one closure has no successors to expand and the beam dies
at depth 1 — and 15% of `Beginner-I`'s sampled levels have `.ghs` shots = 0.

**A new heuristic, because the old one goes flat exactly here.** `FlagDistance` is a BFS over
cells the tank may *currently* enter. After a Goto closure that is guaranteed useless: the closure
only ends on states whose flag is not movement-reachable — if it were, the closure would already
have won there — so every macro successor scores `Unreachable + manhattan` and the beam is left
ranking by tank position. `Heuristic.WorkDistance` keeps a gradient by *charging* for obstacles
instead of refusing to cross them: a Dijkstra from the flag where an empty step costs 1, a brick 4
(one shot and walk through), a block 6, an anti-tank 6, a mirror 7, water 9 (a block has to go in
first), a rotary mirror 12, and only `Solid` and `Crystal` are impassable — crystal because
`CheckLLoc` case 19 returns `true` without touching the cell, so a laser goes straight through one
and never clears it. Tunnel mouths sharing an id are joined by zero-cost edges, which is the other
half of "tunnels resolved": a route in one and out another is a real route rather than the dead
end `FlagDistance` sees. Deliberately not admissible — a beam needs a gradient, not a lower bound,
and the admissible version of this (every price 1) *is* `FlagDistance`'s flat spot. Pushing a
block into water turns the cell to `Dirt` (`Engine.cs`, `MoveObj`'s `obt == 5` arm), so the number
really does drop by 8 when the level's central puzzle is solved.

**Measured three ways, and the first two measurements were misleading.** This is the part of
layer 1 worth reading, because the code was right and the *experiment* was wrong twice.

**Bench 1 — levels layer 0 failed.** 60 `Beginner-I` levels, node-governed, three budgets. Layer 1
wins at every one, and by a lot:

| nodes per level | layer 0 | layer 1 |
|---|---:|---:|
| 150,000 | 18/60 | **28/60** |
| 400,000 | 23/60 | **31/60** |
| 1,000,000 | 33/60 | **38/60** |

**Bench 2 — deep levels.** 50 `Beginner-I` levels with a `.ghs` total of 40–150 — the ones layer 1
was built for. Layer 1 does *not* win: 12 against 13 at 400k, 13 against 13 at 1M. The macro beam
alone scores 8–9 at every setting tried (macro beam 4/8/24/32/48/64, closure cap 150/400/1500/3000,
move-only 0/6/16, both closed-set policies). **Parameters are not the lever.**

**Bench 3 — the campaign, which is the one that counts.** Every 5th level of all 13 collections,
4,185 levels, 150,000 nodes each:

| portfolio | solved | vs layer 0 |
|---|---:|---|
| layer 0 alone (`--no-macro`) | **395** | — |
| macro beam first, a tenth of the budget | 381 | +21, **−35** |
| macro beam last, raw beam capped at 0.6 | 354 | +23, **−64** |

**As a portfolio member layer 1 is a net loss, either way round, and no share or ordering fixes
it.** Running it first taxes every level to help a few; running it last starves the beam of the
budget it was going to win with. Both directions have the same cause: **most solvable levels are
ones the raw beam gets easily, and every node the macro beam spends is a node taken from it.** A
portfolio has to make that bet on every level *in advance*.

**Why bench 1 lied.** It was run on levels layer 0 had already failed — a population where the raw
beam is 0% by construction, so anything the macro beam adds is free. That is a real population,
but it is not the corpus. Holding the level set fixed and sweeping the budget (bench 1's table)
shows the win is stable in budget, so it was never about how much the probe got.

**So layer 1 ships as a second pass, not as a portfolio member** — `tools/second_pass.sh`, which
reads a campaign report, takes the levels it did not solve, and re-attacks only those with
`--macro-first --no-ida --no-beam` into the same solutions directory. The first pass identifies
the population; the second attacks it; neither pays for the other. `RunMacro` is therefore **off
by default** and `--macro` turns it on.

**The composite, measured.** Layer 0's campaign solved 395 of 4,185. The second pass over its
3,790 failures — same 150,000-node budget, macro beam only — solved **21 more**, none of them at
layer 0's expense: **416 of 4,185, 9.4% → 9.9%**, 16 Kids and 5 Easy, median 1.6× the record.
Every one of the 416 is verified through both engines.

Layer 1's honest contribution is therefore *those 21 levels and the method that finds them*, not
a better portfolio. Small — and it is the shape of the result rather than its size that points at
layer 2.

**And the deep-level result is real regardless of ordering, which is the finding that matters
most.** A Goto closure costs `5 × |closure|` `ApplyKey` calls — the four direction keys from every
state it reaches, plus a shot from each — 1,500 to 7,500 where a raw-beam successor costs exactly
1. At an equal node budget the raw beam reaches roughly 300 keypresses of depth; the macro beam
reaches six to twenty shots. Macro-actions cut the *number* of decisions by an order of magnitude
and multiply the *price* of each by two or three.

**The ranking signal is weaker in macro space too, and that is the more interesting half.** Inside
a Goto, movement is *exhausted* rather than searched, so the beam never ranks a movement — it
ranks *board changes*, and `WorkDistance` is a thin signal for those. A keypress beam has a
gradient to walk down (get nearer the flag); a shot beam has to guess which of two hundred
available shots is the useful one. **That is the argument for layer 2 rather than for more tuning
here:** the reason to fire has to be *derived* — this brick is on the only path — not scored.

**A closed-set policy that looks like a bug and is not.** Both beams mark a successor visited the
moment it is *generated*, so a state the width trim discards is closed forever and no later depth
can regenerate it — the search prunes far more than its width suggests. That reads as a defect,
and layer 1 shows the symptom loudly: 33 of 60 bench-1 levels end at `macro-dead-end`, the
frontier having emptied because everything reachable was marked and then binned. The fix was
written and measured, and **it is a regression**: closing only on expansion takes the raw beam
from 33 to 27 and the macro portfolio from 36 to 30. Over-pruning wins, because the budget is
nodes and the greedy policy spends them on depth instead of on re-deriving positions it has
already rejected. Kept as `--closed generate|expand` with the measured default, and the reasoning
sits in `Search.cs` so it does not get "fixed" again.

#### Layer 2 — subgoal decomposition  ☑

**The brief was layer 1's result.** The macro beam made depth the shot count and still lost on deep
levels at every width, closure cap and share tried, because inside a Goto movement is *exhausted*
rather than searched: the beam never ranks a movement, it ranks board changes, and it ranks them
with `WorkDistance` — a number that also moves when the tank merely walks. A 1,500-state closure
offers ~1,500 shots, almost all of which change *something* and almost none of which change the
route, so they score alike and the beam keeps whichever twenty-four sorted first. The conclusion
recorded there was that the reason to fire has to be **derived**, not scored. This layer derives it.

**The derivation — and the first version of it was wrong, which is the part worth reading.**

*Version 1, from a model.* Run the priced Dijkstra `WorkDistance` already runs, keep its
predecessor chain, walk the route from the tank to the flag, and call every cell on it that costs
more than an empty step an obstacle. `--sg-trace` over 384 expansions on the 60 bench-1 levels:
**240 of them — 62% — derived no obstacle at all.** The price list said the flag was five cheap
steps away while the tank plainly could not get there. `Beginner-I` 101 ("BE the RABBIT") is the
clean case: the flag is walled in by bricks and reached through a tunnel, so a model that joins
tunnel mouths at zero cost reports a clear five-step run. A price list knows what a cell costs to
*enter*. It does not know the cell is covered by an anti-tank, that the thin ice on the way has
already been used, or which mouth a tunnel actually pairs with — and those are precisely what stops
a tank on the levels a solver fails.

*Version 2, from the engine.* The movement closure runs **first**, and the cells it stood on are
recorded. That set is an executed answer to "where can the tank get to", with death, spent thin
ice, conveyors and tunnel pairing all resolved by having happened. The Dijkstra then runs from the
flag and stops at the first of those cells it settles; what lies between the two is what is in the
way. Same idiom as layer 1's Goto: **the model proposes the ordering, the engine supplies every
claim about what the tank can do.** Expansions that derive no obstacle at all fell from
62% to 23%.

Two kinds of cell come back and the difference is the interesting half. A cell that costs something
to enter — brick, block, mirror, water — is its own subgoal: make it cheaper. A cell that costs
*nothing* to enter and is still not reached is one the tank died in, so there is nothing at the cell
to shoot; the anti-tanks aligned with it become the targets instead. That is Tutor 75, "Pass the
anti-tanks", derived rather than recognised.

**Acceptance is a board test; ranking is a position test.** A successor survives because a derived
obstacle got cheaper — not because a number went down. `WorkDistance` is still used, but only to
*order* what already survived. That separation is the whole point: clearing a brick usually leaves
the tank somewhere awkward, so the two tests disagree constantly, and layer 1 could not see past
their sum.

**Slack, and why a search that only accepts progress gets stuck.** A derived subgoal is often two
moves away, not one — rotate the mirror so the laser turns, *then* shoot the brick — and the first
of those clears nothing and shortens no route. Accepting only progress therefore dies: measured, 44
of 50 deep levels ended at `subgoal-dead-end` having spent **20,810 of 400,000 nodes**. So each
expansion keeps its best `--sg-slack` (default 4) board-changing successors as **Tier 1** nodes,
which `Cut()` takes only after every successor that actually advanced: slack fills the width that
progress left empty and never displaces it. That one change takes the deep bench from 6 to 9, puts
the whole budget to work (median 400,000 nodes), and drops the dead-ends from 44 to 4.

**Four things measured that did *not* work**, kept as flags with their numbers so they are not
re-invented, all on the 50 deep levels at 400k nodes, subgoal beam alone:

| | solved |
|---|---:|
| shipped defaults | **10** |
| `--sg-aim` (fire only from poses whose ray meets a target, mirror or anti-tank) | 2 |
| `--sg-strict` (accept only on a cleared obstacle, never on a shorter route) | 3 |
| `--sg-closed generate` (layer 0's measured default) | 6 |
| `--sg-width 12` instead of 4 | 8 |

`--sg-aim` is the instructive one: the ray *is* a superset of the shots that hit a target, and it is
not a superset of the shots worth firing. Rearranging a brick or block that is not itself a target
is how the next step becomes possible, and a shot whose only effect is to make an anti-tank turn is
sometimes the whole trick. The closed-set result is the other one: **over-pruning is not a universal
truth about this game, it is a property of the search.** Layer 0's 600-wide beam wants it (33
against 27, recorded above); layer 2's 4-wide beam is killed by it (10 against 6). Hence
`--sg-closed`, defaulting to `expand`, the opposite of `--closed`.

**Benched two ways before the campaign, and neither bench decides anything** — bench 1 is levels
layer 0 already failed, which is the population that flattered layer 1:

| | bench 1 — 60 levels, 150k | deep — 50 levels, 400k |
|---|---:|---:|
| layer 0 portfolio | 18 | 13 |
| layer 1's macro beam alone | 20 | 3 |
| layer 2's subgoal beam alone | **24** | **10** |
| portfolio with layer 1 | 28 | 12 |
| portfolio with layer 2 | **29** | 12 |

**The campaign, which is the one that counts.** Every 5th level of all 13 collections, 4,185
levels, 150,000 nodes each. As a portfolio member layer 2 is a **smaller loss than layer 1 and
still a loss**, in both orderings:

| portfolio | solved | vs layer 0 |
|---|---:|---|
| layer 0 alone (`--no-macro`) | **395** | — |
| layer 1 first, a tenth of the budget | 381 | +21, −35 |
| layer 1 last, raw beam capped at 0.6 | 354 | +23, −64 |
| layer 2 first, a tenth of the budget | 387 | +23, −31 |
| layer 2 last, raw beam capped at 0.6 | 365 | +30, −60 |

That is the same finding as layer 1's, confirmed on a second specialist: **a portfolio has to make
its bet on every level in advance, and most solvable levels are ones the raw beam gets easily.** No
share or ordering wins it, and the reason is arithmetic rather than tuning.

**As a second pass it is worth about twice layer 1.** Same 3,790 failures, same 150,000-node budget:

| pass over layer 0's 3,790 failures | adds | composite |
|---|---:|---|
| layer 1's macro beam (`--macro-first --no-ida --no-beam`) | 21 | 416 (9.9%) |
| layer 2's subgoal beam (`--subgoal --no-ida --no-beam`) | **40** | **435 (10.4%)** |
| layer 2, then layer 1 over what it left | **46** | **441 (10.5%)** |

The two overlap on 15 levels, so they are complementary rather than one superseding the other: the
6 levels layer 1's pass finds that layer 2's does not are *exactly* the 6 a third pass recovers.
**46/46 verified through both engines**, byte-identical traces, 11 matching the `.ghs` record
exactly. Two solutions are over 10× the record and the trimmer could not shorten them.

**Where the budget goes now, which is the brief for layer 3.** Layer 0's unsolved levels stop on
budget 95.9% of the time — the search never gets near the end of a route. Layer 2's stop on budget
80.8% of the time and at **`subgoal-dead-end` 19.1%**: a frontier that emptied, not a clock that ran
out. Those are two different failures and they want two different fixes. The dead-ends are what
restarts are for.

**Layer 2 does not touch the engine.** `Engine.cs` still differs from layer 0 by the single word
`partial`, and `Engine.Search.cs` is unchanged since layer 0. Everything above is in
`src/LaserTank.Solver/`: `Subgoal.cs` (new), `Heuristic.FrontierObstacles` (new), plus `Node.Tier`
and a width argument on `Cut()`.

#### Layer 3 — restarts  ☑

**The brief was layer 2's failure mode, and the first job was to price it.** Layer 0's unsolved
levels stop on budget 95.9% of the time; layer 2's stop on budget 80.8% and at `subgoal-dead-end`
19.1%. Instrumenting that second number before designing anything for it: over layer 2's pass on
layer 0's 3,790 failures, **717 levels dead-ended and they did it with a median of 84% of their node
budget unspent** — 507 of them below a third of it. That is about **90 million `ApplyKey` calls the
pass paid for and threw away**, and it is the resource this layer exists to spend.

**What a dead-end actually looks like, which killed the design that was going to be built.**
`--sg-trace` over six of them (`Beginner-I` 101, 126, 611, 1131, 1206, 1226, 1856):

| | |
|---|---|
| expansions before the frontier emptied | 53–357 |
| closure size, p50 | **8–18 states** (the cap is 400) |
| expansions deriving `added = 0` | **81–99%** |
| expansions offering no slack either | 33–64% |

Three readings, and each one cost a plausible idea:

- **The closure cap is not the problem.** A tank that is boxed in reaches eight states, not the 400
  it is allowed. So randomising *which* states a truncated closure keeps — the obvious diversifier,
  and the one that would matter in layer 1 — buys almost nothing here. It is still done, because it
  is three lines and levels like 101 do truncate at 403, but it is not the lever.
- **The search is running on slack.** With `added = 0` on nine expansions in ten, essentially every
  frontier node is a Tier 1 slack node, picked as the best `--sg-slack` by `WorkDistance`. *That*
  choice is the arbitrary one, so that is where the noise goes: `--sg-noise` jitters the ranking key
  **after `Offer()` has decided the successor advanced**, so a restart keeps a different handful and
  never an inadmissible one. Acceptance stays a board test; only the ordering is randomised.
- **The frontier emptied because the run had closed everything it saw**, which is what suggested
  re-seeding a restart from the nodes the width trim discarded rather than from the root. That is
  the idea the corpus refuted — below.

**Restarts are strictly additive, and that is the structural difference from layers 1 and 2.**
`SubgoalSearch` re-runs the beam only when it stopped at `subgoal-dead-end` *and* budget remains —
never on a `budget` stop, where there is nothing left to spend. Attempt 0 has no jitter, no
re-seed and the canonical key order, so **it is layer 2 exactly**: verified, `--sg-restarts 0`
reproduces layer 2's benches to the level (10/50 deep, 24/60 bench-1, identical stop breakdowns).
Layers 1 and 2 each had to be measured as a portfolio member and each lost, because a portfolio bets
on every level in advance. This one cannot tax the pass it runs inside, so the only question it has
to answer is what the recovered budget buys.

**What recovers a dead-end is width, not randomness — and the two directions of that are the
measurement worth keeping.** Buying width *up front* is a loss and buying the same width *after
narrow has provably failed* is a win:

| subgoal beam | deep (50 @ 400k) | bench 1 (60 @ 150k) |
|---|---:|---:|
| width 4, no restarts (layer 2) | **10** | 24 |
| width 8 from the start | 8 | 25 |
| width 16 from the start | 6 | 25 |
| restarts, no growth | 10 | 25 |
| restarts + `--sg-grow` (4→8→16…) | **11** | **28** |

That is the whole layer in one table. Narrow-and-deep is what buys layer 2 the depth it exists for,
so widening it costs; a restart is the only way to have both, because by then the narrow search has
*already reported* that it failed. `--sg-grow` doubles width and slack per restart, capped at 64/32,
and is on by default.

**The design prior that was wrong: restarting from the reserve.** Re-seeding a restart from the
nodes the width trim discarded is strictly cheaper — it skips re-deriving the shallow part — and it
loses, because it also inherits every commitment the beam had already made, and a *grown* beam wants
to re-take those decisions wider. Kept as `--sg-reuse reserve` with its numbers: **corpus 43 against
root's 44**, bench-1 27 against 28. The default is `root`.

**The campaign, which is the one that counts.** Same 3,790 failures, same 150,000-node budget, so it
is directly comparable with layer 2's pass:

| pass over layer 0's 3,790 failures | solved | `subgoal-dead-end` | lost |
|---|---:|---:|---:|
| layer 2 (`--sg-restarts 0`) | 40 | 717 | — |
| layer 3, restarting from the reserve | 43 | 37 | 0 |
| **layer 3, restarting from the root** | **44** | **9** | **0** |

**717 levels spent at least one restart, the mechanism did what it was built to do — dead-ends fell
from 717 to 9 — and it bought four levels.** That is the result, and the negative half of it is
worth more than the positive half: **converting an emptied frontier into a spent budget mostly does
not convert it into a solution.** The 19.1% dead-end figure marked where budget was being *wasted*,
not where solutions were being *missed*; the levels that dead-end are, with four exceptions, the
same levels that fail on budget. Depth remains the binding constraint, exactly as layer 0's 95.9%
said, and re-running a search that is out of its depth does not change that.

The four include `Beginner-I` 101 "BE the RABBIT" — the level whose walled-in flag and tunnel route
is the worked example that killed layer 2's modelled derivation. It now falls on the first restart.

**The chain that ships, and the composite.** Layer 3's pass replaces layer 2's in the chain, since
it *is* layer 2 plus restarts; layer 1's macro beam still runs third and still finds levels neither
subgoal pass does:

| pass | adds | composite |
|---|---:|---|
| layer 0's campaign | — | 395 (9.4%) |
| layer 3's subgoal beam with restarts | **44** | 439 (10.5%) |
| layer 1's macro beam over what that left | 5 | **444 (10.6%)** |

Against the layer-2 chain's 441 that is **+3 levels and none lost** — `Beginner-I` 101 and 1001,
`Challenge-II` 576. **49/49 verified through both engines**, byte-identical traces, 11 matching the
`.ghs` record exactly.

**Does the win grow with the budget?  Bench 1 says yes, the corpus says no, and the corpus wins.**
This is the third time the project has been caught benching on levels layer 0 already failed, so it
is recorded with both numbers. On the 60 bench-1 levels the gap widens with budget; on an unbiased
1-in-3 sample of the *pass population* (1,268 of the 3,790) at 400k it holds at roughly the relative
size it had at 150k:

| | 150k | 400k | 1M |
|---|---:|---:|---:|
| bench 1, layer 2 | 24 | 25 | 28 |
| bench 1, layer 3 | **28** | **29** | **34** |
| corpus sample, layer 2 | — | 26 | — |
| corpus sample, layer 3 | — | **28** | — |

What *does* grow with the budget, on both, is the waste layer 3 removes: layer 2's dead-ends are
19.1% of its failures at 150k and **26.7% at 400k** (332 of 1,242), because a bigger budget is
mostly a bigger amount left unspent when the frontier empties. The justification for the mechanism
strengthens with budget even where the solved count does not.

**Measured settings that did not win**, kept with their numbers so they are not re-invented:

| | deep | bench 1 |
|---|---:|---:|
| shipped defaults | 10 | **28** |
| `--sg-reuse reserve` | 11 | 27 |
| `--sg-noise 0` (growth alone, no randomisation) | 10 | 26 |
| `--sg-restarts 3` instead of 6 | — | 25 |
| `--sg-width 8` / `16` from the start, no restarts | 8 / 6 | 25 / 25 |

`--sg-noise` is the one to read carefully: worth +2 on bench 1, nothing on the deep bench, and never
separated on the corpus. It ships at 3 because it is free and the bench evidence points one way,
**not** because it is measured at corpus scale — unlike `--sg-grow` and `--sg-reuse`, which are.

**Why not NRPA or nested Monte-Carlo, which is what the plan said.** Those adapt *which action* a
playout picks. The measurement above says the subgoal search does not fail by picking the wrong
successor among many — it fails with `added = 0` on nine expansions in ten, i.e. with almost nothing
to pick from, and giving it strictly more (16× the width, six restarts, 84% more budget actually
spent) moved 4 levels of 3,790. A policy that learns to order an empty list has nothing to learn.
That is an argument from this game's measured shape rather than against the technique, and it is the
argument for layer 4 being about **depth** — a learned evaluation that makes the beam's long lines
pay off — rather than about restart policy.

**Layer 3 does not touch the engine either.** `Engine.cs` still differs from layer 0 by the single
word `partial`, and `Engine.Search.cs` is unchanged since layer 0. Everything above is
`src/LaserTank.Solver/Restart.cs` (new) plus a `seed` argument on `SubgoalBeam`, `_sgWidth` /
`_sgSlack` in place of the two options a restart grows, and a `restarts` field in the report.

#### Layer 4 — a learned evaluation  ☑

**The brief was layer 3's result, and the first job was to find out whether a
better ranking could even matter.** After restarts the subgoal pass fails on
budget 99.7% of the time, so there is one failure mode left and it is depth.
Layer 3 also measured *why* more search does not fix it: `added = 0` on nine
expansions in ten, so almost every frontier node is a Tier 1 slack node, picked
as the best `--sg-slack` by `WorkDistance`. Layer 3 randomised that pick and
bought four levels. This layer asks whether the pick is *wrong*.

**The instrument, and it runs inside the shipped expansion.** A winning `.lpb`
is a keystream, so replaying it one key at a time through `Engine.ApplyKey`
gives the exact sequence of states a perfect search would have visited. From
each of its shot boundaries — the shape of state the subgoal beam actually holds
in its frontier — the *real* `ExpandSubgoal` is run, and every candidate it
offers is recorded with its board features and with whether the winner in fact
went through it. `_collect` is a hook in `Offer()`, not a second copy of the
expansion: the same argument as `--sg-trace`, because a look-alike written to
observe the search would be free to drift from it and the distribution would
then be a fact about the look-alike. Off, it is one null check.

**652 recordings, 20,148 groups, 5.9 million candidates** — 187 human `.lpb`
plus 465 verified solver ones. The instrument found the six documented
non-winners on its own (`RankDump` returns 0 groups when the trajectory does not
win), which is the cheapest possible check that it is replaying what it thinks
it is.

**The distribution, which decided the layer:**

| | all | human recordings | solver solutions |
|---|---:|---:|---:|
| groups | 20,148 | 17,055 | 3,093 |
| candidates per group, p50 | 395 | 399 | 66 |
| **the winner's successor is in the group** | **97.6%** | 97.4% | 98.4% |
| ...and the board test calls it *slack*, not progress | **62.4%** | 68.2% | 30.6% |
| `WorkDistance` rank of it, p50 | **100** | 128 | 6 |
| ...inside the beam's width of 4 | **10.0%** | **4.1%** | 41.8% |

Three readings, and the first is the one that authorised the layer:

- **Coverage is not the constraint.** The state the winner went through is in
  the expansion's output 97.6% of the time. The closure reaches it and the
  acceptance test admits it. Everything that is lost is lost in the sort.
- **The sort loses it.** `WorkDistance` ranks it 100th of 395; the beam keeps
  four. So the shipped search is still on the winner's line one step later 10%
  of the time overall and **4.1% on the human recordings** — which are the hard
  population, and the one a solver that fails 99.7% on budget is up against.
- **The right move is usually one the board test does not call progress.**
  62.4% of the winners' successors are Tier 1 slack nodes. Layer 3 found that
  the search *runs* on slack; this says the slack pick is usually wrong. That is
  what makes it worth learning rather than jittering, and it is why the target is
  the ranking key and nothing else.

**What is fit, and what is deliberately not.** The datum is a *group*, not a
state: one expansion, every successor it offered, and which of them was right.
A cost-to-go regression over trajectory states was the obvious alternative and
is the wrong shape — every state on a winning trajectory is a good state, so a
model fit to those alone has never seen a bad one and cannot be asked to tell
them apart. Here the positives and the negatives come out of the same
expansion, which is exactly the comparison the beam makes. The loss is a
softmax within the group — the probability the evaluation puts *some* state the
winner stood on at the front of the sort — because that is the metric, not a
proxy for it: the beam keeps four of 395, so what is worth fitting is which
four, not the order of the 391 it will throw away.

Seventeen features (`Feat`), all functions of the *board* and never of the path
to it — two routes to the same state must score the same or the closed set and
the ranking disagree about what a state is, which rules out the obvious-looking
keys-spent and shots-fired. They cost one BFS and one 256-cell scan on top of
the Dijkstra the search already ran; measured, the learned pass is *cheaper* per
node than layer 3's (5.8 vs 8.0 µs on bench 1), so the `--budget-ms` backstop is
not at risk and the node budget is untouched by construction.

**Layer 4 is a ranking change and nothing else, and that is enforced rather than
intended.** `Rank()` is consulted only after `Offer()` has settled whether a
successor advanced — the same contract layer 3's jitter has — so acceptance
stays layer 2's board test and a model can reorder the frontier but can never
admit a state the shipped search refused. The check that this is true: the seed
weight vector `{work: 1, work_far: 1000, far_man: 1}` **is** `WorkDistance`
written in these features, and `--sg-eval learned` with it reproduces layer 3
exactly — identical keystreams, node counts and stop reasons on all 50
deep-bench levels. `far_man` exists only so that equivalence can be exact.

**Held out by recording, never by group** — groups from one recording share a
board and most of a position, so a group-wise split reports a training score and
calls it held out. Groups are also weighted by 1/(groups from the same
recording), because two quirk packs supply 16,599 of the 20,148 and without the
weight the fit is a fit to `Tutor-with-Playbacks`.

| held out, top-4 | `WorkDistance` | learned |
|---|---:|---:|
| all (121 recordings) | 13.6% | **18.2%** |
| human (33) | 5.7% | **10.4%** |
| solver (88) | 45.6% | **49.5%** |

**The benches barely move, and this time they are the ones that are wrong.**
Deep 10 → 11 at 400k, bench 1 28 → 29 at 150k. Both banked lists are levels
layer 0 failed — the population that flattered layers 1 and 2 — and here the
same bias runs the other way, because a re-ranking pays off over depth and these
lists are scored one level at a time.

**The campaign, which is the one that counts.** Same 3,790 layer-0 failures,
same 150,000-node budget, so it is directly comparable with layer 3's pass:

| pass over layer 0's 3,790 failures | solved | `subgoal-dead-end` |
|---|---:|---:|
| layer 3 (`--subgoal`) | 44 | 9 |
| **layer 4 (`--subgoal --sg-eval learned`)** | **69** | 6 |

**69 against 44 — and it is not a superset.** Three of layer 3's are lost, which
is the structural difference from layer 3 and worth stating plainly: a *restart*
is additive by construction because it only spends budget already forfeit, and a
*re-ranking* is not additive at all — it is a different search from the first
expansion. So layer 4 does not replace layer 3's pass, it follows it:

| pass | adds | composite |
|---|---:|---|
| layer 0's campaign | — | 395 (9.4%) |
| layer 3's subgoal beam with restarts | 44 | 439 (10.5%) |
| **layer 4's learned ranking over what that left** | **30** | **469 (11.2%)** |
| layer 1's macro beam over what that left | 3 | **472 (11.3%)** |

Against layer 3's chain that is **+28 and none lost**, at the cost of one extra
pass over the failures. Replacing layer 3 instead also reaches 469 in two
passes, so the extra pass buys exactly the three levels a different ranking
gives up.

| tier | levels | layer 3 chain | layer 4 chain |
|---|---:|---:|---:|
| Kids | 960 | 341 (35.5%) | **359 (37.4%)** |
| Easy | 2,118 | 95 (4.5%) | **104 (4.9%)** |
| Medium | 784 | 7 (0.9%) | **8 (1.0%)** |
| Hard | 257 | 0 | **0** |
| Deadly | 56 | 1 | **1** |
| **all** | **4,185** | 444 (10.6%) | **472 (11.3%)** |

**107/107 verified through both engines** — the 74 of the replacement chain and
the 33 of the shipped one — byte-identical traces, WIN on both, median 2.1× the
`.ghs` record. One `Special-I` solution is 0.2× the record, i.e. five times
*shorter* than the recorded best.

**Feeding the newly solved levels back in makes it worse, and the way it is
worse is the interesting part.** That was the other half of the plan, so it was
run: re-dump with the 74 new solutions included (726 trajectories), refit, and
re-measure the pass over the same 3,790.

| | pass | levels whose own solution was in the training set | levels it had never seen |
|---|---:|---:|---:|
| round 1 — fit on 187 human + 465 solver | 69 | 41 of 49 | **28** |
| round 2 — fit on 187 human + 539 solver | 72 | 58 of 77 | **14** |

The pass goes up by 3 and the *discovery* halves. Round 2 recalls more of what
it was shown and generalises less, and both benches move the other way (11 → 10,
29 → 28). **A self-trained ranker learns the levels it was fed rather than the
game**, and the total is a worse summary of it than the split is. Round 1 ships;
the refit is kept as `build/reports/eval-weights2.txt` with its numbers.

Note what that table also says about the leakage in the headline: 41 of layer
4's 69 are levels whose winning keystream was in the training set — they are
mostly the ones layer 3 and layer 1's passes had already banked — so **28 is the
size of the generalisation**, and the composite gains 28 exactly.

**Why linear, and where the ceiling is.** A 17-feature linear model over a
softmax-in-group loss converges in seconds and moves held-out top-4 from 13.6%
to 18.2%. It is still wrong four times in five: even after fitting, the winner's
successor is inside the width of 4 only 10.4% of the time on held-out human
recordings. The instrument says the headroom is not coverage (97.6%) and not the
acceptance test — it is entirely in the sort, and 80% of it is still on the
table. That is the brief for anything above this layer, and it is a measurement
rather than a hope.

**Hints as landmarks was not built**, and the arithmetic that says not to is in
the layer-4 plan above: 175 of 20,914 hints are recipe-grade. That is a tail
tool, and this layer's tail is 3,700 levels wide.

**Layer 4 does not touch the engine either.** `Engine.cs` still differs from
layer 0 by the single word `partial`, and `Engine.Search.cs` is unchanged since
layer 0 — four layers now. Everything above is
`src/LaserTank.Solver/Learn.cs` (new: `Feat`, `Eval`, `RankRow`, `Rank()`,
`RankDump`), `Weights.cs` (generated), three published fields on `Heuristic`,
and a `Rank()` call in place of two `WorkDistance` calls in `Subgoal.cs`.

### Phase 4 addendum — the interactive driver  ☑

Everything above measures the solver; this is how you *use* it on a level you
care about. `build/lasertank-solve.exe FILE.lvl [--from N] [--to N]` — a bare
`.lvl` and nothing else required — walks the collection in level-number order
and stays on each level until it falls or you press a key.

    build/lasertank-solve.exe data/levels/Beginner-I.lvl --from 12 --to 40

**It is the portfolio the campaign could not afford.** A round runs every
searcher *at once, one per thread* — layer 0's beam (with IDA* on round 0,
where a probe is cheap), layer 3's subgoal beam, layer 4's learned ranking of
it, layer 1's macro beam, and layers 5-7's three push rungs — and the first
win cancels the rest. In a campaign
that trade is a loss, because every node a specialist spends is a node taken
from the raw beam (hence `second_pass.sh`); here a specialist spends a *core*,
and one level at a time means the cores are there. If nobody wins, the node
budget quadruples and the round repeats — 400k on round 0, about a second;
400M on round 5, about an hour — and rounds also widen what only widening
helps: the raw beam doubles its width, since a `beam-dead-end` stop has nothing
to do with a bigger budget, and the subgoal beam gets six more restarts.

**The two-engine gate is not optional here, it is the write path.** A win goes
to a scratch `.lpb`, then to `tools/verify_solutions.py` (which grew a
`--levels` argument so one candidate can be checked against a named `.lvl`
rather than by directory name), and is moved into the output directory only if
the frozen C oracle and the C# core both report WIN with byte-identical traces.
A solution that fails is deleted and the search carries on — loudly, because
after Phase 3 that can only mean an engine divergence. Missing engines or no
python is a startup error, not a discovery made six levels in.

**`--lanes N` works N levels at once** (session 21; default 1, so a bare run is
exactly what it always was). The ladder is seven rungs and this machine has
sixteen cores, so one level left nine of them idle — and a second level is a
better thing to spend them on than a wider anything, because the rounds already
widen what widening helps.

The scheduling policy is one sentence: **every lane draws on the same pool of
`--jobs` slots.** Four lanes against sixteen slots is four levels with about
four searchers apiece, not twenty-eight compute-bound threads over sixteen
cores; a rung that cannot get a slot waits, and if the level falls while it
waits it returns without expanding a node, because its stop bit was set while
it queued. That is the same bargain the driver already struck whenever `--jobs`
was below the ladder size, so `--lanes` costs nothing to add and nothing to
raise — the only thing it trades is portfolio breadth per level against levels
in flight, and *which* of those is worth more is a property of the levels, not
of the driver.

The display is one line per lane plus a footer, and **the lane number is the
key that gives up on it** — `1`-`N`, where the single-lane driver's any-key
still means what it always did. A key naming no lane is ignored rather than
guessed at: throwing away an hour of the wrong lane's search is not a thing to
do on a maybe.

Two things had to change under it, and both were latent races rather than new
work. The gate *empties* the directory it stages through, so a lane verifies in
one of its own — two lanes sharing one would delete each other's candidate and,
worse, could hand a lane the other lane's solution to pass off as its own. And
`Sweep` deleted `cand-*.lpb` wholesale, which was fine when a driver was the
only thing running and is not fine now: a second driver started while the first
is still going would delete a candidate between the searcher writing it and the
gate reading it, and the first run would report a level it had actually solved
as *the winning searcher wrote no file*. Candidates now carry the process id
and a run sweeps only its own, plus anything a day old, since that is the
growing-forever the sweep was written for.

`Auto.cs` is the whole of it, plus a `CancelFlag` on `SolveOptions` that
`OutOfBudget` reads — the keypress ends a search at the next node rather than
at the end of a stage, and the searchers publish their node counts back through
it for the live display. `Engine.Search.cs` is still unchanged since layer 0.

*One trap worth keeping, because it is the whole of why the display is written
the way it is:* a lane may not write to `Console`. Two lanes each printing half
a level's story interleave into neither, so a level's lines are built up in the
lane and handed to a queue, and the painter on the main thread is the only
writer there is — it erases the live block, drains the queue, and repaints. The
block is erased with one *relative* escape (`ESC[<rows>A ESC[J`), which is what
keeps it correct after the terminal has scrolled, and every row is cut to the
window before it is coloured: a row that wraps is two lines on screen and one
in the row count, and from there the block walks up the scrollback a line per
repaint.

### Phase 4 addendum — why the search fails, measured on one level  ☑ (instrument), ☐ (layer 5)

**The trigger.** `LaserTank.lvl` level 1, "Boot Camp", survives four rounds of the interactive
driver. That is a fair thing to be annoyed by and a bad thing to reason about from the level
number, so it was measured instead. Three findings, in the order they were found.

**1. "Level 1" is a name, not a difficulty.** Its `.ghs` record is 103 moves + 46 shots = 149,
which is the **66th percentile of `LaserTank.lvl`** and the **75th percentile of the Easy tier
inside it**. Of the 677 winning rows across every campaign report the median record is 19; only 13
are >=100 and 8 are >=149. Level 1 is longer than roughly 99% of everything the solver has ever
solved. Nothing about it being first makes it small.

**2. Layer 0's beam ranks this level by a heuristic that is constant on it.** `FlagDistance`
returns `Unreachable + manhattan` when the flag is not reachable over currently-passable cells
(`Heuristic.cs:125`) — and here the flag at (0,0) sits in a water pocket, so it stays unreachable
until the last three pushes of the level. The whole search therefore ranks by Manhattan distance:
a depth trace of the beam shows the tank walking to (0,8), `bestH` pinned at 1008 for **220
consecutive depths**, and the search dying of an empty frontier at 708k nodes with the tank never
having left that spot. Swapping the beam's ranking key to `WorkDistance` — which prices water at 9
and blocks at 6 and so keeps a gradient across an unreachable flag — changes the picture
completely: `bestH` falls 59 -> 28 and the tank reaches (4,0), one water bridge from the flag.

**It still does not solve the level, and on the bench it is not a win: 12/50 against 13/50 on
`deep-levels.txt` at 400k nodes.** So it is not shipped, and it is written down here because the
*trace* is the finding, not the swap. The experiment is two lines against `Search.Beam` and was
reverted.

**3. The real obstacle is that the winning line goes uphill, and by how much is now measurable.**
Michal recorded `data/demos/LaserTank/00001.lpb` by hand: 264 keypresses, 125 moves + 49 shots,
**verified through both engines**, 1.8x the record. Replaying it through `--profile` (`Profile.cs`)
and reading it with `tools/basin.py`:

| | keypresses | board-changing keypresses |
|---|---:|---:|
| length of the human solution | 264 | 50 |
| longest stretch at-or-above the best `WorkDistance` so far | **68** | **16** |
| deepest rise above it | +16 | +8 |

and the same measurement over the **402 recordings the solver itself produced**:

| | p50 | p90 | max |
|---|---:|---:|---:|
| longest uphill stretch, keypresses | 6 | 21 | 457 |
| longest uphill stretch, board-changing keypresses | 1 | 8 | 227 |

**A greedy beam follows a descending line for free and an ascending one only while the whole
cross-section of the ascent fits in the width.** Level 1's ascent is 68 keypresses where the solved
population's 90th percentile is 21. No width a machine holds covers the states reachable in 68
keypresses, so this is not a budget problem and no number of rounds fixes it — the four rounds were
never close, and a fifth would not be either. That is the honest answer to "why can it not solve
the first level".

**What the level actually asks for** is visible in the trajectory: the tank ferries blocks one at a
time from around (5,4)/(6,5) up the (6,3)->(6,0) corridor and into the water at (3,0), (2,0), (1,0).
Each ferry is 20-50 keypresses of pure setup during which every ranking key the solver has gets
*worse*, and only the final push improves it. It is a Sokoban endgame wearing a boot-camp costume.

**Which is the case for layer 5 — push macros.** The same trajectory measured in board-changing
keypresses is 50 events with a 16-event ascent, against a solved-population p90 of 8. A search
whose move is `Push(block -> cell)` rather than one keypress compresses the level 5.3x and moves it
from *3.2x beyond anything ever solved* to *2x the p90* — which is the range width and restarts
already reach. That is a measured argument for the layer rather than an intuition about it, and it
is the first thing to build. Layer 1's `Goto + Shoot` is the near miss: a push happens only as a
side effect of a `Goto`, and `--closure-depth` caps one `Goto` at 40 movement keys.

**The second demonstration says the shape generalises past the ferry.** `LaserTank.lvl` level 2,
"Easy Level Conveyor" (record 10 moves + 62 shots), recorded by hand as
`data/demos/LaserTank/00002.lpb` — 119 keypresses, verified through both engines, 1.7x the record.
It is a *different* structure: a conveyor ring with blocks to be shot onto it, no water anywhere, so
the ferry term is exactly inert on it and `work` and `work+ferry` are the same column.

| | keys | events | longest ascent (keys) | uphill | longest ascent (events) | uphill |
|---|---:|---:|---:|---:|---:|---:|
| level 1, block ferry | 264 | 50 | 68 | +16 | 16 | +8 |
| level 2, conveyor | 119 | 32 | 35 | +11 | 13 | +9 |
| *solved population* | — | — | *p50 6 / p90 21* | — | *p50 1 / p90 8* | — |

Level 2 is the milder case of the same disease — 1.7x the solved p90 in keypresses, 1.6x in events
— and it is unsolved by everything: the raw beam dies at `beam-dead-end` after 1.6M nodes, and the
learned subgoal beam and layer 5 both spend a full 40M without a win. **Two levels, two structures,
one failure mode.** That was the first evidence that the ascent length is a property of long levels
rather than of level 1, and it was n=2.

**n is now 20, and the shape held.** Michal hand-recorded `LaserTank.lvl` 1-19 across
sessions 15-17 (`data/demos/LaserTank/`), **20/20 verified through both engines**, median 1.7x the
`.ghs` record. Nineteen of the twenty spend longer above their own best `WorkDistance` than the
90th percentile of the 402 levels the solver has actually solved, and fifteen do in board changes:

| all 20 recordings | p50 | p90 | max |
|---|---:|---:|---:|
| longest ascent, keypresses | **50** | 200 | 297 |
| longest ascent, board changes | **15** | 33 | 33 |
| *solved population, keypresses* | *6* | *21* | *457* |
| *solved population, board changes* | *1* | *8* | *227* |

The first nine level by level, which is the table the layer-5 argument was built on:

| | keys | events | ascent (keys) | ascent (events) | verdict (layer 6, below) |
|---|---:|---:|---:|---:|---|
| 1 Boot Camp | 264 | 50 | 68 | 16 | FERRY x4 |
| 2 Easy Level Conveyor | 119 | 32 | 35 | 13 | RIDE |
| 3 Building A Bridge | 75 | 10 | 20 | 4 | FERRY x2 |
| 4 The River Nile | 76 | 17 | 37 | 15 | FERRY x1 |
| 5 Dodge the Gun | 90 | 22 | 63 | 15 | FERRY x2 |
| 6 Cascade | 904 | 168 | 152 | 33 | SOKOBAN x6 |
| 7 Jim's Wild Ride | 132 | 13 | 47 | 8 | FERRY x1 |
| 8 castle siege | 386 | 52 | 297 | 31 | FERRY x3 |
| 9 Grid Lock | 126 | 27 | 38 | 7 | GAUNTLET |
| **those nine** | | | **p50 47** | **p50 15** | |
| *solved population* | | | *p50 6 / p90 21* | *p50 1 / p90 8* | |

**Twenty levels, six structures, one failure mode** — ferry, sokoban, conveyor ring, anti-tank
gauntlet, mirror routing and a mixed siege all fail the same way, and none of them is close to the
band a beam can walk. The median recording here is 2.4x the solved p90 in keypresses and 1.9x
in board changes. The ascent belongs to *long levels*, not to level 1 and not to ferrying.

**And it is the case for more hand recordings.** Twenty demonstrations produced the tables above; the
instrument now exists, so each further one is another row. Two distinct uses, worth separating:

- **As instrument.** ~20 hand playthroughs of *long* levels (record >=100), spread across the
  structures — block-into-water, mirror ferrying, conveyor timing, ice — would say whether 68 is
  typical of the unsolved tail or particular to this level, and whether push macros flatten the
  ascent everywhere or only here. That is what decides layer 5's shape before it is written.
- **As training data.** Layer 4's evaluation was fit on 652 *solver* trajectories, median record
  19, and the refit that fed its own new wins back in **halves what it discovers** — the
  self-reinforcement trap recorded in the layer 4 section. Human recordings of long levels are the
  one available sample that is off that distribution in exactly the direction the solver fails.
  Their value per level is small; their value as a *different* distribution is the whole point.

Short recordings are not wanted: the solver already wins those, and they would push the fit further
toward what it already knows.

### Phase 4 — layer 5, push macros  ☑ (built, measured, and after session 18 it pays)

**Built on the addendum's argument and measured against it.** `Push.cs`, `--push`, off by default.
The action set is: one **PF-preserving movement closure** — everything the tank can do without
changing the playfield, so it is the set of *poses*, at most 16x16x4 of them — and then every board
change reachable from any pose in it, as a first-class successor. Fire from a pose (layer 1's rule
and its lossless prune, kept whole), or drive into something and keep driving while the board keeps
changing, one successor per changed cell. Search depth is therefore the board-change count, which
is the unit `tools/basin.py` measures in.

**What is confirmed.** Closures never truncate — `--push-trace` reports `trunc=0` on every
expansion of every run made so far, which is the layer's structural bet checked rather than
assumed. Layer 1's closure mixes movement with pushes and enumerates block configurations; this one
terminates on the pose count, well inside its cap. On level 1 the search runs at depth 50 where the
raw beam runs at depth 264, and its ranking key descends 92 -> 49 by depth 14 instead of pinning at
1008 for 220 depths. Solutions are real: a 40-level smoke over `Beginner-I` solved 14, **14/14
verified through both engines, 11 matching the `.ghs` record exactly**.

**What it costs, which is the number that decides the layer.** One expansion is a whole closure —
about **4,500 `ApplyKey` calls** against layer 0's five per keypress. At an equal node budget that
is two orders of magnitude fewer steps, and the bench says so plainly, on the 50 banked deep levels:

| budget | layer 0 beam | layer 3 subgoal | layer 5 push |
|---|---:|---:|---:|
| 400k | 13/50 | 10/50 | 3/50 |
| 4M | 13/50 | 11/50 | 7/50 |

It closes as the budget grows and it has not caught up. **So layer 5 does not ship in the chain
yet**, and the honest statement of where it stands is that it buys the right *shape* and has not
yet bought a level.

> **Superseded in session 18, and the reason is worth keeping in place.** That table is real and
> its explanation was wrong: the cost per expansion was not the binding constraint, the *trim* was.
> A successor here is (board change, the pose it was fired from) and the trim was ranking forty
> copies of one board against each other, so a width of 300 was searching a handful of boards and
> paying for three hundred closures. Capping poses per board takes the same code to 20/50 on the
> ferry bench and 21/50 on the deep bench — see *The width was being spent on tank poses*, below.
> Everything between here and there is the reasoning that led to the instrument that found it, and
> the ferry term below is a keeper on its own terms.

**The ferry term, and it is the part worth keeping.** WorkDistance prices a water cell at 9 and
does not move at all while a block is being carried towards it: the whole ferry — fetch, turn,
push, twenty to fifty keypresses — scores the same as standing still, and only the final push that
fills the cell scores anything. `Heuristic.RouteFerry` is the fix: summed over the water cells on
the settled route, how far the nearest movable block still is from each. Manhattan, deliberately,
because "can this block actually be pushed there" is a question about `MoveObj`, ice and where the
tank can stand, i.e. a second implementation of the game that this project does not have.

Measured on the human recording with `--profile` + `tools/basin.py --ferry-weight`, it is a real
but partial win, and the weight is picked from the sweep rather than guessed:

| weight | longest ascent, keypresses | uphill | longest ascent, events | uphill |
|---:|---:|---:|---:|---:|
| 0 (off) | 68 | +16 | 16 | +8 |
| **1** | **49** | **+16** | **12** | **+8** |
| 2 | 45 | +16 | 12 | +10 |
| 3 | 41 | +27 | 11 | +21 |

Past 2 the ascent gets shorter and *deeper*, which is the term overpowering the thing it is
supposed to be correcting; 1 is the default. **And it is inert where it is not needed** — over the
402 recordings the solver has produced it changes the excursion distribution not at all (p50 6,
p90 21, max 457, identical at weight 0, 1 and 2), even though 100 of those 416 trajectories touch
it at some point. A targeted term that cannot cost anything on the population already solved is
the right kind of heuristic to add; it is also why the deep bench cannot measure it — those levels
have no water crossing, and layer 5 scores 7/50 with the term on, off, or replaced by layer 4's
learned evaluation.

**Where that leaves level 1.** Still unsolved, and now for a reason one step further in: the beam
reaches the flag's doorstep and has to cross an ascent that is 12 board-changes long even with the
ferry term, at a width where 300 states per depth all tie on the ranking key and `Cut` breaks the
tie by *cheapest keystream* — which during a ferry favours the state that has not started one. That
tiebreak is the next thing to look at, and it wants a population to measure on: the deep bench
contains no ferry at all, so a change made against level 1 alone would be a change made against
n=1. **This is the point at which more long-level demonstrations stop being nice to have and become
the blocker** — see the addendum above for what to record.

**Restarts, carried over from layer 3 for a failure with the same shape.** Ranked by the learned
evaluation with the ferry term, level 1 reaches board-change depth **66** and a WorkDistance of
**15** — one ferry from the end — and then dies of an empty frontier with **556M of its 600M nodes
unspent**. That is layer 3's measurement again (dead-ends died with a median 84% of their budget in
hand), so it gets layer 3's answer: `--push-restarts`, default 6, re-running the beam with double
the width on `push-dead-end` only, capped at 9,600. Attempt 0 is the plain beam exactly and a
restart spends only budget the dead-end had already forfeit, so it is strictly additive by
construction — the same argument Restart.cs makes, and the reason this one is on by default while
the layer as a whole is not.

**Knobs** (all in `--help`): `--push-beam` 300, `--push-restarts` 6, `--push-depth` 400,
`--push-closure` 4000, `--push-closure-depth` 64, `--push-run` 8, `--push-move-only` 4,
`--push-ferry` 1, `--push-eval learned`, `--push-closed generate|expand`, `--push-trace`,
`--push-share`.

**`--push-closed` defaults to `expand`, the opposite of layer 0, and that one is measured too.**
Closing on generate binned level 1's frontier entirely: `push-dead-end` at 21M nodes at width 300
and 78M at width 1200, everything reachable marked and thrown away. Layer 0 spends five `ApplyKey`
calls on a successor and can afford to; this layer spends a closure.

### Phase 4 — layer 6, the read  ◐ (instrument built and measured; not yet in a search)

**The brief was Michal's, and it is worth quoting because it is the design.** *"Level 4 is a great
example of a very easy level: as a player, I immediately see I have to make a bridge somehow, I see
the block, I know I have to use the mirrors and avoid the ATs — and I see the conveyor belt is there
only for me to save steps. Or level 6: no antitanks, only blocks and water, I instantly know there
will be long sokoban shit. This kind of analysis is what we need over like an application of five
different Knuth algorithms."*

Every layer so far ranks states. Layer 4 measured how far that goes and the answer was *not much
further*: the winner's successor is in the expansion 97.6% of the time, the sort loses it, and a
fitted linear model over seventeen features is still wrong four times in five. What a player does
in two seconds is not a better sort of two hundred successors. It is a derivation of **which
successors exist for a reason**, done before the search starts.

**The two halves, and the discipline is layer 2's.** Layer 2 learned the hard way that a model of
what the tank can do is a second implementation of the game — its first obstacle derivation priced
cells with a Dijkstra and found *no obstacle at all* on 62% of expansions, because a price list does
not know a cell is covered by an anti-tank. Version 2 ran the movement closure first and asked the
engine. This is that split one step further out:

- **What must change** is a model: the priced Dijkstra from the flag, stopped at the cells the tank
  demonstrably stands in — layer 2's `FrontierObstacles`, unchanged.
- **What can change it** is not a model at all. Every board change the tank can make right now is
  enumerated by *making* it: a PF-preserving pose closure, then all five keys from every pose, and
  whatever `Game.PF` comes back different is an **effect**, carrying the pose and key that produced
  it as its witness.

Nothing in `Analyze.cs` knows that a laser bounces off a mirror, that a block sinks in water, that
shooting a movable block shoves it one square, or that a conveyor carries the tank. **Level 4's
mirror route is discovered because firing left from (7,15) was tried and the block at (2,2) moved** —
the beam goes left along row 15 into the mirror at (0,15), up column 0 into the mirror at (0,0),
right along row 0 into the mirror at (2,0), down into the block. Three reflections, no mirror code,
and it cannot drift from the engine because it *is* the engine.

**Three derivations of "this change advances", and the third is the one that generalises.**

| | what it is | derived from |
|---|---|---|
| `on the barrier` | the change lands on a cell the Dijkstra named | cell intersection |
| `toward` | it moves a block nearer a water cell the route crosses | block delta + manhattan |
| `opens` | **after it, the tank can stand somewhere it could not stand before** | a second pose closure |

`opens` needs no theory of the obstacle at all. Stopping a conveyor ride by shooting a block onto
it, killing the anti-tank that owns a corridor, blowing a brick out of a doorway — the read has no
name for any of those and does not need one, because all three come back as *somewhere new to be*.
It costs one closure per effect, which is why it is capped (`--read-opens` 64): the median expansion
offers four effects, so the usual bill is four closures, and past the cap the question is simply not
asked, so a missing label is never a wrong one.

**The verdict is a decision list, not a classifier**, and every fact it tests was derived rather
than pattern-matched — "no shot on this board does anything" is the enumeration coming back with
zero shot effects, not a scan for bricks. On `LaserTank.lvl` 1–9, the nine levels there are hand
recordings for:

| lvl | verdict | the read, in one line |
|---:|---|---|
| 1 | FERRY x4 | 4 water cells in the way, 5 blocks, nothing available brings one nearer yet |
| 2 | RIDE | the route is clear and the tank still cannot get there: it runs over cells the tank cannot stop on |
| 3 | FERRY x2 | 2 water cells, 2 blocks — as many blocks as holes, so every one is needed |
| 4 | FERRY x1 | 1 water cell, 1 block, **moved by shooting and every shot that moves it is mirror-routed**; 2 anti-tanks named by the route |
| 5 | FERRY x2 | 2 water cells, 3 blocks, four shots move one nearer |
| 6 | SOKOBAN x6 | 6 water cells in the way and nothing else, 6 blocks, no anti-tanks |
| 7 | FERRY x1 | 1 water cell, 1 block, nothing available reaches it yet |
| 8 | FERRY x3 | 3 water cells, 19 blocks, and one shot available now **fills one of them** |
| 9 | GAUNTLET | the route crosses nothing that has to be cleared and **28 anti-tanks cover it** |

That is Michal's read of 4 and 6 in the machine's own words, and level 1's is the same conclusion
the addendum above reached by hand two sessions ago — "a Sokoban endgame wearing a boot-camp
costume" — derived in 166 ms, process start included.

**What the whole corpus looks like through it.** `--analyze-tsv` over the same 1-in-5 stride sample
the campaigns use (4,185 levels, 13 collections, **64 seconds** for all of it, serial across the
collections), joined against the
shipped chain's solved set:

| verdict | levels | solved | rate | effects offered, p50 |
|---|---:|---:|---:|---:|
| FERRY | 2,073 | 104 | 5.0% | 5 |
| GAUNTLET | 828 | 138 | 16.7% | 5 |
| DEMOLITION | 360 | 40 | 11.1% | 4 |
| SETUP | 324 | 30 | 9.3% | 4 |
| RIDE | 312 | 61 | 19.6% | 6 |
| SOKOBAN | 161 | 10 | 6.2% | 7 |
| OPEN | 87 | **84** | **96.6%** | 0 |
| WALLED | 40 | 5 | 12.5% | 3 |
| **all** | **4,185** | **472** | **11.3%** | **5** |

Three things fall out of that table:

- **OPEN at 96.6% is the sanity check.** When the flag is already movement-reachable the solver
  essentially always wins, and the read agrees with the solver about which levels those are.
- **Half the corpus is a ferry** — FERRY and SOKOBAN together are 2,234 of 4,185 (53%) and the
  solver gets 114 of them (5.1%). That is where the corpus is and it is the worst-performing
  non-trivial class.
- **The number of fills is the difficulty**, and it is the first quantity that predicts the rate
  monotonically: 1 fill 9.9%, 2 fills 5.7%, 3–4 1.8%, 5–8 1.7%, 9+ **0.7%**. Barrier size says the
  same thing (0 cells 22.6%, 9+ 3.9%). Depth is still the binding constraint — layer 0's 95.9%
  measured that in stop reasons; this measures it in the units a player uses.

**The read, measured against the humans, which is the number that decides whether to search by it.**
`--read-dump` replays each winning recording, stops at every board change, and asks whether the
change the human made next is one the read named. Between two board changes the tank only *moves*,
so the next change is by construction reachable from the pose closure of the state the previous one
left — the question has a yes-or-no answer. 391 board changes over the nine recordings:

| | 9 recordings | **20 recordings** |
|---|---:|---:|
| board changes | 391 | **800** |
| the human's change was in the enumeration at all | 391 | **800/800 = 100%** |
| `on the barrier` or `toward` named it (set size p50 **1**) | 210 | 286 |
| else `opens` named it (set size p50 4) | 142 | 380 |
| neither | 39 | 134 |
| **named by the read** | 90% | **666/800 = 83%** |

Michal kept recording while this was being written, so the right-hand column is the one to quote:
twenty hand playthroughs of `LaserTank.lvl`, all verified through both engines. The rate fell from
90% to 83% as the population grew, which is what an honest n=9 was always going to do.

**100% coverage is a check on the enumeration, not a finding** — the enumeration *is* the engine, so
anything else would have been a bug, and it also says the pose closures never truncated. The finding
is the 90%, and the shape of it: `opens` alone accounts for 86%, and it is the only one of the three
that says anything at all on RIDE, GAUNTLET and SETUP levels, where the other two are empty by
construction (no water on the route, no barrier to land on). Before `opens` the read named 53.7%.

**And the honest half.** Ordering by tier — barrier/ferry first, then `opens`, then the rest,
uniformly at random inside each tier — against the same beam with no ordering at all:

| beam width | tiered by the read | no ordering |
|---:|---:|---:|
| 1 | **36.5%** | 24.2% |
| 2 | **55.2%** | 45.7% |
| 4 | **73.6%** | 65.2% |
| 8 | 88.4% | 83.2% |
| 16 | 96.1% | 93.2% |

The read wins at every width and **the gap closes as the width grows**, because the candidate lists
are short to begin with: the median expansion offers six board changes where layer 2's shot
expansion offers 395. So most of "73.6% inside a width of 4" is bought by the *action set* — layer
5's board-change move — and the read adds eight points on top of it, twelve at width 1. Stated the
other way round, and this is the comparison worth keeping: layer 4's beam holds the winner's
successor inside a width of 4 **4.1% of the time on human recordings**; a board-change beam ordered
by the read holds it **74%** of the time, on human recordings of exactly the levels that beam cannot
solve. Those are different units over different populations and the number is not a like-for-like
improvement — but the two together say plainly where the remaining loss is, and it is no longer in
the sort.

#### Layer 6 inside layer 5 — the read as the beam's first sort key  ◐

The read enters the search as a **tier**, not a term: `Cut()` sorts on `Tier` before `H`, so the
read can say "these successors exist for a reason and the rest are filler" without reordering
anything inside either group and without being able to admit a successor the expansion did not
already offer. Same contract layer 4's `Rank()` has. A tier rather than a number because the read's
answer *is* a set — "this shot lands on the brick that is in the way" is not three points better
than a shot that does not — and layer 5's ferry term is already the version of this that is a
number, with the measurement (above) saying a weight past 2 makes the ascent shorter and deeper.

`--push-read` turns it on, off by default. Turning it off reproduces layer 5 exactly: the tier
renumbering is inert, checked rather than asserted — layer 5's recorded 3/50 at 400k and 7/50 at 4M
on the deep bench both reproduce to the level.

**Two false starts, and the instrument that ended both.** `--push-trace` grew a line reporting what
fraction of a depth's successors the read promoted, and it read **0%** — 0 of 248 at depth 3.

- The first cause was in the code being measured. `opens` inside a beam cannot afford a pose closure
  per successor, so it was given a cheap proxy — and the proxy asked whether the *flag's* passable
  component grew, where the 86% in the table above came from asking whether the *tank* can stand
  somewhere new. During a ferry those are opposites: the flag's component does not move at all until
  the last block goes into the water, while the tank's region moves on almost every push, because a
  block that leaves a square is a square the tank can now stand on. `Heuristic.TankRegion` is the
  corrected version and its doc comment carries the trap.
- The second cause was in the instrument. The counter was incremented before the `opens` pass ran,
  so it was reporting the first two derivations only. **An instrument that measures the wrong moment
  says the layer does nothing**, which is exactly the conclusion it nearly bought — and it is the
  same lesson as layer 2's `--sg-trace`, which is why the counting now lives in one function called
  from every return path.

**What the corrected trace then said, and it is the finding.** The read promotes 5–11% of successors
at shallow depth and under 1% deep — and the frontier at those depths is 20 to 660 nodes against a
beam width of **300**. `Cut(next, 300)` on a frontier of 248 does nothing at all. **The read is a
filter, and layer 5's shipped beam is not filter-limited: it is width-limited at a width that
already admits everything the read would have selected.** So the read cannot help there, and the
Dijkstra it costs makes it fractionally worse — which is exactly what the first benches said.

**Which turns it into a width experiment, and both halves of that are worth keeping.** A population
was banked for it first, because the deep bench cannot decide a ferry question and the read itself
says so: `build/reports/ferry-levels.txt`, 50 `Beginner-I` levels the shipped chain fails that the
read calls FERRY or SOKOBAN — against the deep bench, which the read scores at 3 ferries in the 8 of
its 50 that the stride sample covers, and bench-1, which is GAUNTLET-heavy. All at 4M nodes:

| push beam width | ferry bench, plain | ferry bench, **+read** | deep bench, plain | deep bench, **+read** |
|---:|---:|---:|---:|---:|
| 8 | 5/50 | **11/50** | — | — |
| 16 | 6/50 | 10/50 | — | — |
| 32 | 5/50 | **11/50** | — | — |
| 48 | 4/50 | **11/50** | 13/50 | **14/50** |
| 96 | 3/50 | 9/50 | — | — |
| 300 *(layer 5 as shipped)* | 4/50 | 5/50 | 7/50 | 6/50 |

Two findings, and they are orthogonal:

- **Layer 5's width was wrong, and that has nothing to do with the read.** 300 → 48 takes the deep
  bench from **7/50 to 13/50**, which is the first time layer 5 has matched layer 0 (13/50) on that
  bench. Narrow-and-deep again, exactly as layer 3 measured for the subgoal beam — one push
  expansion is ~4,500 `ApplyKey` calls, so width is the most expensive thing this layer buys.
  *(Session 18: right conclusion, wrong reason. Narrower was buying **fewer duplicate tank poses**,
  not more focus — with the poses capped the whole curve moves and the best width is 8. See below.)*
- **The read is worth roughly 2.75x on the population it was built for and nothing elsewhere.** At
  width 48, 4/50 → **11/50** on ferries and 13/50 → 14/50 on the deep bench. That split is the
  read's own claim about itself holding up: its three derivations are barrier, ferry and opens, and
  the deep bench is mostly not a ferry. **11/11 verified through both engines.**

At width 300 the read is worth nothing (4 → 5, 7 → 6) and at width 48 it is worth seven levels on
ferries. **The filter and the width are the same decision**: a tier that names 5% of successors is
useless at a width that keeps 100% of them, and a width that keeps 5% of them is useless without
something to say *which* 5%.

**Knobs.** `--analyze` (the printed read), `--analyze-tsv FILE` (one row per level, for joining
against a campaign report), `--read-dump FILE` (needs `--lpb-list`), `--read-opens N` (default 64,
the instrument's cap). In the search: `--push-read`, and `--push-read-opens` — `-1` (default) is the
cheap `TankRegion` flood on every successor, `N>0` is the executed pose closure on the best N, `0`
is the two free derivations only. All the instrument flags honour `--from` / `--to` / `--level` /
`--stride` / `--levels-list` / `--jobs`.

**Layer 6 does not touch the engine either.** `Engine.cs` still differs from layer 0 by the single
word `partial` and `Engine.Search.cs` is unchanged since layer 0 — five layers now. Everything above
is `src/LaserTank.Solver/Analyze.cs` (new) plus three flags and one option in `Program.cs`. All four
fidelity gates re-run green after it: 187 replayed / 181 win / 6 documented, 29 difftrace, 2,347
sweep identical, 25 fuzz.

#### The width was being spent on tank poses — `--push-line`, and what it found  ☑

**The instrument first, and this one is new in kind.** Three sessions had explained level 1 from the
outside — the record's percentile, the ascent length, the read's verdict — and every explanation was
true while none of them named *which line of code loses the level*. `--push-line FILE.lpb`
(`Line.cs`) asks that directly: replay a winning recording, keep its state at every board change —
one per push-beam depth, by construction — then run the real beam with those states in hand and
report per depth whether the line's state was generated, what the ranking key made of it, and
whether the width trim kept it. The hashes are read and never given to `Cut`, so a `--push-line` run
*is* the run it is explaining.

Four outcomes, and they call for different fixes: **CUT** (generated, ranked, outside the width —
rank 60 at width 48 is a tiebreak problem, rank 3,000 is a ranking problem), **STALE** (refused by
the closed set, which is a `--push-closed` finding and not a ranking one), **cut-early** (dropped by
the interim trim inside the depth), and **absent** (never generated, which after `--read-dump`'s
800/800 can only mean the parent was already gone). Aliveness is asked of the *playfield*, not of
the state, and that distinction turned out to matter: every pose in a closure offers the same board
changes, so a frontier holding the line's board at some other pose can still play the line's next
move — and the runs show it doing exactly that, the exact state dropping out at one depth and
reappearing two depths later. Losing the *board* is the loss that does not come back.

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 1 \
    --push-line data/demos/LaserTank/00001.lpb --nodes 20000000 --jobs 1
```

**What it said about level 1 was one row long.**

```
line d=  1 CUT    rank=130/156  line-h=99  tier=0  best-h=90  cut-h=93  boards 3->0
line d=  2 absent rank= -1/186  ...                                     boards 0->0
```

The line dies at **the first board change** — not at the 12-event ascent this layer was built for,
but before the search has done anything at all.

**And the cause is that the trim was counting the wrong thing.** A successor of this layer is
(board change, the pose it was fired from), and one board change is reachable from *every* pose in
the closure: level 1's root closure is 158 poses offering **4** distinct changes, so the expansion
emits 156 successors that are four boards wearing thirty-nine hats each. `Cut` ranks them by a
heuristic that depends on where the tank is standing and fills all 48 slots with poses of one or two
of those boards. `--push-trace` grew a `boards=` column and it reads **1 to 9 distinct playfields at
a width of 48**, and exactly one at three separate depths. Every duplicate then costs its own
closure — ~4,500 `ApplyKey` calls — to expand into the successors its twin has already produced.
**That is where this layer's budget was going**, and it re-explains every width experiment before
it: narrower was never buying focus, it was buying fewer duplicates.

**`--push-per-board N`** caps how many poses of one playfield the trim may keep, default 1, and `0`
restores the old trim exactly. The frontier is then allowed to come out *narrower* than the width,
which is the point — a depth that offers six distinct boards should cost six closures and not
forty-eight. Poses are not interchangeable in general (a board change can cut the map in two with
the tank on one side of it), which is why it is a cap rather than a dedupe. On the two banked
benches at 4M nodes with everything else as layer 5 shipped it, width 48 and the read on:

| `--push-per-board` | ferry bench | deep bench | level 1: line survives to |
|---:|---:|---:|---:|
| 0 *(layer 5+6 as shipped)* | 11/50 | 14/50 | board change 1 |
| **1** | **14/50** | **17/50** | **board change 6** |
| 2 | 11/50 | 16/50 | board change 7 |
| 4 | — | — | board change 3 |

The 11 and the 14 are session 17's recorded numbers reproducing to the level, which is the check
that makes the rest of the table worth reading.

**With the trim fixed, the ranking key changes hands.** `--push-line` prints the line's heuristic at
every board change, so session 15's basin measurement is now one command at push granularity — and
it says layer 4's learned evaluation is a different animal on this level than on the population it
was fit on:

| ranking key | longest stretch at-or-above its own best | deepest rise |
|---|---:|---:|
| `work`, ferry 0 | 16 board changes | +8 |
| `work`, ferry 1 *(layer 5's old default)* | 12 | +8 |
| `work`, ferry 2 | 12 | +10 |
| `work`, ferry 3 | 11 | +21 |
| **`learned`** | **6** | +8 |
| *solved population* | *p50 1 / p90 8* | |

Six is **inside the solved population's p90 for the first time on this level**. The four `work` rows
reproduce session 16's 16/12/12/11 exactly, so the learned row is being read on the same scale. The
benches agree: at width 48 with per-board 1, `learned` takes ferry 14 → 15 and deep 17 → 19, and
dropping the read from that configuration costs 15 → 9 and 19 → 15, so the read is still carrying
its weight — more of it than before, because a tier that names 5% of successors is worth something
only at a width that does not already keep everything.

**Then the width, re-measured on a trim that finally spends it on boards.** All at 4M nodes,
per-board 1, read on, ranked by `learned`:

| `--push-beam` | ferry bench | deep bench |
|---:|---:|---:|
| 4 | 17/50 | 20/50 |
| **8** | **19/50** | **21/50** |
| 16 | 18/50 | 20/50 |
| 48 | 15/50 | 19/50 |
| 128 | 13/50 | 17/50 |
| 300 *(layer 5's old default)* | 8/50 | 12/50 |

Narrow and deep for the third time in this project — layer 2's subgoal width, layer 3's
grow-on-restart, now this — and this time the number is **8**. `--push-depth` came with it: at width
4 seven of the ferry bench's fifty stopped at the 400-board-change cap, so it is now `MaxKeys`, a
backstop again rather than something that stops a search which is still descending.

**And one more thing the trace caught, in the restart.** The first version doubled the per-board cap
alongside the width, on the argument that a dead-end can be either too few boards or too few ways
into the one that mattered. `--push-trace` says otherwise: by the fourth restart level 1 was running
at width 128 over **8 distinct boards** — the pose duplicates the cap exists to stop, quietly back.
A restart now buys width only, worth ferry 19 → 20 with the deep bench unchanged at 21.

**Where that leaves layer 5, and it is a different place.** New defaults — width 8, per-board 1,
`learned`, depth 1200, restarts buying width alone — with the read:

| | ferry bench | deep bench |
|---|---:|---:|
| layer 5+6 as session 17 shipped it | 11/50 | 14/50 |
| **the same code, new defaults** | **20/50** | **21/50** |
| *layer 0, for scale* | *—* | *13/50* |

**40/40 verified through both engines** across those two benches, and the old configuration still
reproduces its two numbers from its flags, so the delta is the change and not the machine.

**The corpus pass that decides whether it ships, which is what session 17 left undone.** One pass
over the levels the whole shipped chain fails, `SAMPLE=15` of them at 4M nodes:

```bash
SAMPLE=15 NODES=4000000 BUDGET_MS=900000 JOBS=12 bash tools/second_pass.sh \
    build/reports/chain.jsonl build/solutions/l7 build/reports/l7.jsonl \
    --no-ida --no-beam --push --push-read
python tools/report_stats.py build/reports/l7.jsonl
python tools/verify_solutions.py build/solutions/l7
```

**15 of 255 (5.9%), all fifteen verified through both engines** — Beginner-I 6/16, Challenge-II 2/26,
Challenge-III 1/25, and a scatter elsewhere. Read it knowing what it is: 4M nodes is 27x the
campaign budget, so this is an argument for **a fourth pass at a high budget**, not for changing
what the campaign itself runs. Extrapolated over the 3,713 failures the chain has it is on the order
of 200 levels, which would be 472 → ~690 of the 4,185-level stride sample; that extrapolation is a
motivation to run the pass, not a number to quote.

**And the interactive driver's push rung was getting *weaker* every round.** The ladder doubles a
rung's width per round, which was written when the default was 300 and nodes were assumed to bind
first; against a default of 8 it meant round 5 ran at 256, and the rung never turned the read on.
Benched as the ladder actually ran it — width 256, no read — that is **11/50 on the ferry bench
against the default's 20/50**. So "level 1 survives five rounds" was partly the ladder walking away
from its own best configuration. The rung now grows **restarts** instead (6 and 36 both score
20/50, so it is free, and a restart only ever spends budget a dead-end had already forfeit) and
turns the read on, while the width stays where the sweep put it. `Auto.cs` only.

**Level 1 was still unsolved at the end of this session**, and four 800M-node runs at width 4/8/16
and 10 restarts said so plainly rather than hopefully. The instrument says how much closer, and the
honest version of that sentence needs a width on it: at width 48 the line now survives to board
change **6** where the old trim lost it at **1**, and at the shipped width of 8 it survives to **2**
— a narrower frontier holds fewer boards, so it gives the line back in exchange for depth, and the
benches say to take that trade (19-20/50 against 15/50). What is not width-dependent is the endgame:
at 100M nodes the search reaches a learned score of **14** against the human line's own final 10,
where before the fix it stalled at 37. Depth 6 is the first
of five roto-mirror rotations that set up a mirror-routed shot: pure setup, on which the read is
silent by construction (they open nowhere new to stand and land on no barrier) and across which
every ranking key this project has is flat. A fourth derivation — *"after this change the tank can
make a board change it could not make before"* — is what would name them. It costs an effect
enumeration per successor, which is the same price as expanding it, so it is a measurement to make
before it is a feature to build.

*(Session 19 made that measurement, built the derivation, and **level 1 fell to it**. The rest of
this section is that; everything above stands as written.)*

#### The fourth derivation — *what does this change make possible?*  ☑ (and it solves level 1)

**Measured before it was built, and the measurement is the reason it went in where it did.**
`--read-enables` adds the derivation to the instrument: for each effect, run the pose closure on the
board it leaves behind, enumerate every key from every pose, and count the board *deltas* that were
not available a moment ago. Deltas rather than resulting boards — "shoot the brick at (4,3)" is the
same change whether or not an unrelated roto has since turned, so a delta set can overlap between
two boards where a set of reachable boards never would. Nothing in it knows what a mirror is; it is
the same enumeration layer 6 already runs, asked one ply further out. Over the same 20 recordings,
with the derivation off reproducing the earlier table row for row (**0 of 800** rows differ on the
fifteen columns that existed before it):

| | 20 recordings |
|---|---:|
| board changes | 800 |
| named by barrier / toward / opens | 666 = **83.2%** |
| **+ the fourth derivation** | 777 = **97.1%** |
| of the 134 nothing named, it names | **111** |
| successors it names, p50 / p90 / mean fraction | 4 / 16 / **79%** |
| *successors the other three name, for scale* | *4 / 14 / 70%* |

**And that 79% is why it is a tier of its own rather than a promotion.** A derivation that names
four fifths of an expansion is no filter; folded into `TierAdvance` it would only dilute one that
works. As `TierEnables`, sitting between the other three and the rest, it can reorder nothing except
the group they were already silent about — which is precisely the group level 1's rotations sit in.
A stricter variant was measured alongside it and rejected on its own numbers: *enables a change the
read would itself name* is properly selective (p50 1, 25% of successors) but names only 8 of the 134,
so it buys coverage 83.2% → 84.2% and is not worth a pass.

**Level 1, derivation by derivation, is the finding.** On its first ten board changes:

| derivation | successors it names | times it named the human's move |
|---|---:|---:|
| on the barrier / toward | **0 of 7** | 0 of 10 |
| `opens` | 4 of 7 | 3 of 10 |
| **the fourth** | **3 of 7** | **9 of 10** |

The flag sits in a water pocket, so no effect ever lands on the barrier and the two free derivations
are inert for half the level; `opens` is not inert, it is *wrong* — it names four successors an
expansion and misses the human's move seven times out of ten. So when the fourth derivation is on,
`opens` moves behind it (`TierOpens`), and with `--push-enables 0` the tiering is layer 6's exactly,
opens included. A derivation that is more selective *and* more accurate belongs in front of one that
is neither.

**Two things had to be fixed before any of it could be seen, and both are the same lesson twice.**
The pass was written after `ReadTier`'s two early returns, so only the `--push-read-opens N>0` path
ever reached it — **it benched as completely inert through a whole round of measurements**, which is
session 17's "an instrument that measures the wrong moment says the layer does nothing" wearing a
different hat. `Opens` is now a named method called from one place instead of two returns. And at
full price the pass cost a pose closure and an enumeration per distinct board, about **4x an
expansion**, which took the ferry bench from 20/50 to 12/50 — right shape, wrong price, layer 5's
own story for the third time.

**The ration is session 18's finding re-used, and it is what makes the price survivable.** A tier
can only matter at a width the tiers above it do not already fill, so the pass counts what the depth
has already promoted — across every parent expanded so far, because they all compete in one trim —
and returns immediately if that alone covers the width. On a ferry level the read fills the shipped
width of 8 inside the first parent or two and the pass is mostly skipped; on level 1, where the free
derivations name nothing all the way through, it is asked every time. Ferry bench 12/50 → **19/50**
on the gate alone.

**What it costs, on both banked benches at 4M nodes, and this is the number that keeps it off by
default:**

| | ferry bench | deep bench |
|---|---:|---:|
| layer 5+6 as session 18 shipped it | **20/50** | **21/50** |
| + `--push-enables 8` | 19/50 | 19/50 |
| + `--push-enables 8`, ungated | 12/50 | — |

It is a level worse on one and two worse on the other. That is the whole case for `--push-enables`
being **off by default** — and, as it turned out, the wrong way to read the number for the one place
a user actually meets the solver. See *The rung*, below: measured as a *union* with the rung it runs
beside rather than on its own, the same configuration **adds four ferry levels and five deep ones**.
A solo score is the wrong statistic for a portfolio member.

**And what it buys, which the two benches cannot see because neither contains a level of this
shape.** `--push-line` on level 1 at width 48, 40M nodes — how far the beam follows the human
recording:

| | line survives to board change |
|---|---:|
| before session 18's trim fix | 1 |
| session 18's shipped configuration, width 48 | 5 |
| the same with `opens` off | 6 |
| **+ the fourth derivation** | **11** |

Board change 11 is past all five roto rotations and past the mirror shot they set up. **And then the
level falls.**

```bash
build/lasertank-solve.exe --levels data/levels/LaserTank.lvl --level 1 \
    --no-ida --no-beam --push --push-read --push-enables 8 --push-beam 48 \
    --nodes 600000000 --jobs 1 --out build/l1
python tools/verify_solutions.py build/l1
```

**`LaserTank.lvl` 1 "Boot Camp", solved in 6.19M nodes and 17.9 seconds**, 294 keypresses = 157
moves + 47 shots against the `.ghs` record of 103 + 46, **1.97x**. Verified through both engines and
banked at `data/solutions/LaserTank/00001.lpb`, which is committed rather than left under `build/`. The
shipped default width of 8 solves it too, in 412 s — slower, because a narrower frontier holds fewer
boards, which is the same trade session 18 measured and the same one the benches say to take
everywhere else.

That is the level this file has opened with since session 15, and the honest measure of the distance
covered is that four 800M-node runs did not touch it and this one is eighteen seconds.

**It is not one level, and the control is what says so.** Over `LaserTank.lvl` 1-19 — the levels
there are hand recordings for, and which sessions 15-18 measured at 2.4x the solved population's p90
uphill — at 60M nodes, run twice, with the derivation and without. Only the two levels in bold are
the derivation's:

| level | `--push-enables 8` | without it |
|---:|---:|---:|
| **1 Boot Camp** | **solved, 6.19M at width 48** | **unsolved at 60M** |
| 3 Building A Bridge | 12.4M | 13.7M |
| **4 The River Nile** | **solved, 49.9M** | **unsolved at 60M** |
| 7 Jim's Wild Ride | 0.20M | 4.28M |
| 11 Bumper Cars | 0.25M | 0.06M |
| the other fourteen | unsolved at 60M | — |

**4 of 19 against 3 of 19, and the arithmetic understates it**: 3, 7 and 11 were reachable either
way and only 1 and 4 are new, so the honest claim is *two levels*, one of which is the one this file
has opened with since session 15. **4/4 verified through both engines**, p50 1.7x the record. Level
1 needs more than 60M at the shipped width of 8 — it solves there in 412 s at a larger budget, and
in 6.19M nodes at width 48, which is why it carries a width in its row and the others do not. The
speedups on 3 and 7 are real but are one sample each and should be read as noise until a population
says otherwise; 11 is 4x *slower* with it, which is the same caveat pointing the other way.

#### The rung — and why the solo bench score was the wrong statistic  ☑

**Michal's objection, and it is the right one:** *"we have to somehow incorporate this into the
autosolver — the user won't know to fine tune random parameters for specific levels."* A flag that
solves the flagship level and that nobody will ever type is not a feature. The first attempt was to
switch it on inside the existing push rung from round 2, which is the cheap answer and the wrong
one: it makes a rung that has been tuned to its best measured setting worse, on the argument that a
later round's population is different — an argument with nothing measured behind it.

**The driver's own design says what to do instead.** It is a portfolio: every searcher runs at once,
one per core, and a specialist costs a *core* rather than a share of anybody's budget — which is
exactly why `learned` is a rung beside `subgoal` rather than a change to it. So the fourth
derivation gets a rung, `push-enables`, and the ladder goes from five searchers to six.

**And the moment it is a portfolio member the bench numbers have to be read differently.** A solo
score answers "is this configuration better", which is not the question; the question is "does this
add anything the rung beside it does not". All at 4M nodes, against the plain push rung's own solved
set:

| | solo | union with the plain rung | it adds |
|---|---:|---:|---:|
| plain push rung (session 18's) | 20 / 21 | — | — |
| **+ `--push-enables 8` at the default width 8** | 19 / 19 | **24 / 26** | **+4 / +5** |
| + `--push-enables 8` at width 48 | 14 / 18 | 21 / 26 | +1 / +5 |
| *all three together* | | *24 / 28* | |

*(ferry / deep.)* **The configuration that looks like a one-to-two-level loss on its own is a
four-to-five-level gain next to the rung it runs beside**, because the levels it fails are not the
levels the other rung fails. That is the entire case for the rung, and it is invisible in every
table above this one.

**The width is the one thing the rung changes with the round.** The default of 8 is the better
partner (+4/+5 against +1/+5) and 48 is what level 1 wants — 6.19M nodes and 18 s, against 412 s at
width 8. Both, then: width 8 while the rounds are cheap, and 48 from **round 3**, by which point the
budget is 25.6M and anything either bench solves at 4M has had three chances at it already. One
rung, both widths, no core spent on the disagreement.

**The test that matters is the one a user would run**, with no flags anywhere:

```bash
build/lasertank-solve.exe data/levels/LaserTank.lvl --from 1 --to 1
```

```
  round 3: 25.6M nodes to each of 6 searchers
  SOLVED  294 keys (157 moves, 47 shots), 2.0x the record   push, round 3, 67s, 6.2M nodes
  verified through both engines -> data\solutions\LaserTank\00001.lpb
```

**67 seconds, from the bare command line, banked and verified without being asked.** That is the
difference between a derivation that exists and one that ships.

**What the knobs are, and what the pose cap is really for.** `--push-enables N` is the number of
distinct playfields per expansion that may be asked (0 = off). `--push-enables-poses N` caps the
child closure the question is asked from, default 32: truncating a closure can only *lose* poses, so
it costs promotions and cannot invent them, which is what makes a small default safe. It is not free
of consequence — at 32 the level-1 line is held only to board change 4 where the whole closure holds
it to 11 — but the level solves at either, because the search does not have to follow the human's
line to win. Raise it when reading a `--push-line`, leave it when solving.

**Where this leaves the layer.** The read now has four derivations and the fourth is the first one
that is about *the tank's options* rather than about the route. Everything the other three ask is a
question about the flag: is the barrier smaller, is the block nearer the water, can I stand
somewhere new. This one asks what a player asks while turning a mirror — *what does this let me do
next?* — and it is the only one that says anything at all during pure setup. It is also, measured
honestly, a net loss of one to two levels on both populations that were banked before it existed,
which is the argument for the flag and against the default.

### Phase 4 — layer 7, the stop cell  ☑ (built, measured, its own rung)

**The trigger.** *"The solver is still stuck on level 2. From a human perspective this level is
trivial: I instantly see that I have to shoot the boxes to block the conveyor belt."*

`LaserTank.lvl` 2 "Easy Level Conveyor" is a single conveyor loop with the flag at (14,0) in the
top wall and the tank penned into the bottom two rows — 30 cells, 348 poses, and every way out is a
ride that never stops. `--analyze` calls it **RIDE**, which is 312 of the 4,185-level stride sample
(7.5%) and the second-best-solved shape at 19.6%, so the shape was never the problem. Three things
were, and each was found by an instrument rather than guessed.

**One: a laser ferry cost a depth per cell.** `PushRun` compresses a *drive* push — the tank
travels with the block, so pressing the same key again continues it, and a k-cell ferry is k
successors of **one** expansion. A shot leaves the tank where it is, so nothing compressed it: the
fire pass fired once per pose and the next cell of the same ferry was a whole beam depth away.
Level 2's hand recording is **32 board changes of which 26 are a repeat of the shot before them**,
on a WorkDistance that goes 13 -> 11 across the entire level. That is a 32-deep breadth-first
search on a flat key. `--push-shot-run N` is PushRun for the laser and makes it six.

**Two: `--push-line` was counting the wrong thing.** It asked for line index *d* at beam depth *d*,
which was right while every successor was one board change. `PushRun` already bent that and a shot
run breaks it: the instrument reported a line it was following perfectly as `STALE`, because the
hash it wanted had been generated and closed two depths earlier. It now tracks **how far along the
line the frontier actually is** and prints both numbers (`d=` and `at=`). Nothing about a search
changed; a measurement that lied stopped lying.

**Three, and this is the layer: no ranking key in the project moves when a block gets nearer the
cell that would stop the ride.** A ferry level's route crosses water and `RouteFerry` prices "how
far is the nearest block from the hole". A RIDE level's route crosses a *conveyor*, which the price
list charges 1 for and the Dijkstra walks straight over — and the tank still cannot follow it.

`Heuristic.RouteStop` is that term, and getting it right took **five wrong versions, every one of
which the beam found and sat on**. They are worth listing because each is a different way for a
relaxation to lie:

1. **Price every conveyor on the route.** Junk: ~20 requirements deep, and it *rose* along the
   winning line, 13 -> 36 -> 107, because placing a block reroutes the Dijkstra through fresh
   conveyors faster than it satisfies old ones. Being carried off the priced route is not a
   failure — the ride usually arrives somewhere useful the long way round, which is what a conveyor
   level *is*. What the route cannot dodge is the **last step**: a drive is a key consumed while the
   world is quiescent, so on the cell it drives into the flag from, the tank has to be standing
   still. Scoped to that one cell, the winning line became a descent: 25 -> 12.
2. **Manhattan to the nearest block.** The beam shoved a block up column 15 to (15,1), two cells
   from the target and infinitely far by push, because moving it left again needs the tank at
   x=16. Replaced by a backward BFS over block positions.
3. **A dead-end branch of the chain returning "free".** Stopping next to the flag is not the same
   as *getting* there, so the term regresses: a cell no conveyor feeds has to be driven into, from a
   neighbour the tank must in turn stop on. A branch with nowhere to arrive from returned 0, and the
   beam parked on a board whose two spare blocks were both stuck in row 2 with every way in priced
   at nothing.
4. **Spending one block on two requirements.** With a block on (13,2) the requirement for (13,1)
   costs one push — push *that* block up — and (13,2) is empty again. A block already paying for a
   requirement is now reserved, and is a wall to the BFS rather than a candidate.
5. **`Passable` standing in for "the tank can stop here".** A block on (14,1) is one cell from
   (13,1) and can never get there: the only square behind it is (15,1), a conveyor. The push test
   now needs a cell on the ray behind that the tank can actually **stand** on, precomputed as one
   sweep per line per direction.

**And a bug that was not mine, found because a shot run walks straight into it.** `WorkDistance`
and `FlagDistance` returned **0** — the best score any state can have — when there is no flag on
the board, on the reading "nothing to steer by". A flag leaves PF for exactly one reason: something
has been pushed on top of it. With `--push-shot-run` a block goes up column 14 in **one** laser run
and lands on the flag, and at width 128 *every one of the 128 boards in the frontier was that
board*, scoring 4 against the winning line's 11. It now returns `Unreachable`. See *Status* for what
that costs and how it was attributed.

**What it takes, measured as an ablation at 60M nodes on level 2:**

| configuration | result |
|---|---|
| width 128, `--push-stop 1`, `--push-shot-run 16` | **solved, 2.03M nodes, 44 s, 90 keys** |
| ...without the shot run | unsolved at 60M |
| ...without the stop term | unsolved at 60M |
| ...at width 8 | unsolved at 60M |
| ...at width 64 | unsolved at 60M |
| ...at width 256 | solved, 4.11M nodes |

All three, and the width. Nothing here is a preference.

**It is a specialist and the bench says so.** Solo at 4M nodes it is **18/50 ferry and 18/50 deep**
against the plain push rung's 19 and 21 — a loss, read solo. As a portfolio member, which is the
only honest way to read one, it **adds 3 ferry levels and 5 deep ones** to that rung's own solved
set (union 22 and 26). Same shape and same conclusion as layer 6's fourth derivation: a specialist
costs a *core*, not a share of somebody's budget. So `--push-stop` is off in the batch solver and
has its own rung, `push-stop`, in the interactive driver — and
`build/lasertank-solve.exe data/levels/LaserTank.lvl --from 2 --to 2` solves it at **round 2 in
65 seconds with no flags**, 90 keys against the record's 72, **1.2x**, verified through both
engines. `--from 1 --to 1` is unchanged at 72 s.

`--push-shot-run` is off by default for its own measured reason rather than by association: solo on
the shipped push configuration it is **ferry 15/50 (from 19) and deep 23/50 (from 21)**. That is a
wash, not a win, so it lives in the rung that needs it.

**New instrument: `--push-trace-board`**, which prints the best node's playfield under
`--push-trace`. Three of the five wrong versions above were diagnosed by *looking at the board the
beam had settled on*; `best=10` on its own says a key has gone flat and says nothing about what the
beam is looking at. It is what found the buried flag.

**What is left undone here.** The chain is conveyors only. `--analyze`'s RIDE verdict says "a
conveyor, a slide or a tunnel", and ice is the same shape — you slide until you hit something —
but the direction of travel on ice depends on how the tank entered, which is a second question.
`StopChain` is capped at 3 and the reservation list is not unwound per branch (over-reserving makes
a state look dearer, which is the safe direction). And the layer has never been run over the
corpus: the ablation and the two benches are all that is behind it.

### Phase 4 addendum — polishing a solution so it reads like a person played it  ☑

**The complaint, and it is not about length.** *"Get rid of repeated turns in place (like facing
north, facing west, facing south, moving south) and shooting at nothing. They look very computery
in the replays."* All three artifacts are free to the search and so it emits them freely:
`MoveTank` (`Engine.cs:657`) spends a whole keypress turning when the key does not match the way
the tank already faces and `ScoreMove` only increments in `UpDateTankPos`, so a turn on the spot
costs the *record* nothing; a shot that hits nothing is one keypress for one node; and `Cut` breaks
a tie by cheapest keystream, which makes a state the beam has already left exactly as good as one
it has not — so wandering out and back is free too.

`Trim.Polish` removes all three, and it is separate from `Trim.Shrink` on purpose: Shrink is delta
debugging, costs thousands of replays and runs only past `--trim-ratio` (default 10x), whereas this
runs on **every** solution because a 1.6x solution can be just as ugly as a 12x one.

| the artifact | how it is found |
|---|---|
| a round trip | `StateHash` after key *j* equals the state after key *i* — so keys *i..j-1* left nothing behind. Longest first |
| a turn on the spot | a direction key after which the tank did not move and the board did not change, *followed by a different direction key* — which is what keeps the last turn of a run, the one the move needs |
| a shot at nothing | a space bar after which the whole state hash is unchanged |

**Every deletion is still replayed before it is accepted, and that is not caution.** A wasted turn
is not a no-op: `AntiTank()` runs inside every key-consuming tick, so a turn on the spot gives every
gun on the board a move, and there are levels whose solution *is* burning a tick so a gun fires
early. Measured — on `Beginner-I` 1488 the run `> > < < > > < <` **survives** the polish, because
those round trips really are the anti-tank timing, while `> v FIRE ^ ^ < FIRE ^ v` collapses to
`v FIRE < FIRE`.

**And the first version of it reported almost everything as irreducible, because of a bug that is
the most useful thing in this section.** `Trim` replayed every candidate through *one reused*
`Engine`. `LoadLevel` resets the playfield, the tank and the slide records — and deliberately does
**not** reset `wasIce`, `WaitToTrans`, `ConvMoving` or `BlackHole`, because that staleness is quirk
#3 and the original never reloaded a level into a fresh process either. So the second candidate
replayed through an engine inherits the first one's leftovers, and **a keystream that wins from cold
can be reported as losing**. The polisher was therefore declaring solutions minimal that were not.

Michal caught it from the replay rather than from the code — *"step 2 is a useless turn south; step
3 is visually a noop; step 4 is a useless turn west; steps 5 and 6 are a noop; step 7 is a turn
north; step 8 is a correct shot. Compare to my manual demo: step 1 same, step 2 turn north, step 3
the shot."* An identical deletion search on **fresh** engines took that solution from 71 keys to 51
and 23 shots to 14, and its opening became exactly what he described. Every candidate replay in
`Trim` now constructs its own `Engine`; the doc comment on `Trim.Wins` carries the trap.

Note what the bug could and could not do: it made the trimmer *miss* deletions, and could in
principle have made one look acceptable that was not — but nothing reaches a `.lpb` without a
fresh-engine replay in `Program.SolveOne` and another in `tools/verify_solutions.py`, so no wrong
solution was ever written. The cost was entirely in reductions not found, and it roughly doubled:

| population, 400k nodes | solutions | keys before | after | removed |
|---|---:|---:|---:|---:|
| subgoal beam, 50 deep levels — shared engine | 10 | 1,722 | 1,251 | 27.4% |
| subgoal beam, 50 deep levels — **fresh engine** | 10 | 1,722 | **908** | **47.3%** |
| raw beam, 60 bench-1 levels — shared engine | 23 | 860 | 839 | 2.4% |

The split between the two searchers is the other half. The raw beam barely improves, because its
`Cut` already breaks ties by cheapest keystream and it cannot afford long detours anyway; the
**subgoal beam loses nearly half its keypresses**, because it searches in shot-space and the
movement between shots is whatever the closure happened to execute. `Beginner-I` 1488 goes
**831 → 212 keys**, 1068 goes 171 → 49, 1484 goes 76 → 46. **10/10 verified through both engines**
after polishing.

On by default; `--no-polish` turns it off. Cost is a handful of replays per solution against a
150,000-node search, so it is free at campaign scale. The interactive driver gets it too — it goes
through the same `SolveOne`.

**The interactive driver writes to `data/solutions`, not `build/`.** A campaign's output is
disposable (regenerated by `tools/campaign.sh`, thousands of files, gitignored `build/`); the
driver's output is one hand-supervised level at a time, already through the two-engine gate, on
levels the batch solver could not do. Those are worth committing, so they go where git can see them,
next to `data/demos/` — the same kind of thing: a recording that cannot be regenerated on demand.
The scratch directory the verifier stages through stays under `build/`.

### Phase 4 addendum — replanning a solution through the board changes it already made  ☑

**The complaint, and why the polisher is structurally unable to answer it.** *"In the first part
(first ~40 steps) the tank moves the C4 block to C6 and later back to C4 to move it correctly, which
is a waste - I'm thinking there could be a step like 'now that we know where the blocks are going to
end up, what's the least-move way to get them there?'"* On `LaserTank.lvl` 3 the excursion is four
shots and about seventeen keypresses, and `Trim.Polish` cannot see any of it. It is not a round trip
(`Decycle` needs two *identical* states, and the board is different afterwards - the other block has
sunk into the water in between), not a turn on the spot, not a shot at nothing. Every key in it does
something. They just undo each other. Deletion cannot find that; only re-derivation can.

**The observation the pass is built on, and it is Michal's own from session 20** - *"the solution is
basically a series of state changes with walking in between"*. If that is what a solution *is*, then
the playfields it passes through are a **ladder of positions already proved to lead to a win** - the
solution is the proof. So "what is the least-move way to get the blocks where they end up" has a
cheap and sound answer: search for the shortest keystream that climbs that ladder, **allowed to skip
rungs**. Nothing is modelled. A rung is reached because `Engine.ApplyKey` was called and `Game.PF`
came back equal to a playfield the original run stood on.

`Replan.Improve` (`Replan.cs`) is that search:

| step | what it does |
|---|---|
| the ladder | replay the solution, keep every playfield it stood on, in order. A board it stood on twice maps to its *last* rung, so a plain round trip is skipped by the lookup alone |
| the sweep | one forward pass over the rungs. Every successor is at a strictly higher rung, so the ladder is a DAG and rung order is a topological order: each rung is expanded exactly once |
| one closure per rung | every state at a rung has the same playfield by construction, so they share one PF-preserving movement closure - a multi-source uniform-cost walk over tank poses, each seeded at the keystream length it arrived with |
| the runs | a board change is offered to its rung and then the same key is pressed again while the board keeps moving, so a k-cell ferry and a k-shot mirror push are k rungs for k keys. `Solver.PushRun` and `Solver.ShotRun` inside the search, for the same reason |

Level 3 is one skip. With the first block on B7 the original walks off to fiddle with the second one
and comes back; the replan pushes B7 straight into the water, which lands on the playfield the
original only reached five board changes later, and the whole excursion goes with it. **80 -> 57
keys, 15 -> 11 board changes**, and the replay now reads: shoot the gun through the mirror, three
shots to ferry the first block into B8, walk round to D4, one shot to put the second on B4, walk it
down column B, cross. Level 7 is the same machine on a different complaint - *"walk/ride circle,
shoot mirror half into position, walk/ride circle again, shoot mirror into position; could be just
one circle"* - and the ladder search fires four times from the one square it can be fired from: a
shot run walking four rungs for four keys. **81 -> 65.**

**The one thing that had to be got right, because the first version got it wrong.** The space bar
does not belong in the closure. A shot that hits nothing still moves the laser record, so its
`StateHash` differs from the pose it was fired from, and a walk that treats "new hash" as "new place
to stand" will fire from *that*, and again, and stop being bounded by the pose count. Measured
before the split: level 3 spent its entire pose budget at rung 0, on an island of twenty-four cells,
and found nothing all corpus. The fix is `Solver.ExpandPush`'s own structure - walk on movement
keys, then fire once from each pose the walk found - and with it level 3 finishes in **25,014
`ApplyKey` calls**. What that gives up is a shot kept for its *timing* (burn a tick so a gun fires
early, then walk on); the search layer gives up the same thing in the same place.

**Ordering, and the measurement that fixed it.** The first build ran replan *before* the polish and
was a net win that **lost keys on two collections** - a re-derived route is a different starting
point for delta debugging and the deletions it offers are not a superset of the ones the polisher
had found. So the pipeline (`Program.Clean`, shared by `SolveOne` and `--polish`) is **polish,
replan, polish again, and keep the replan only when what comes out is shorter**. Polishing first is
not only safe, it is better: a shorter input lowers the length the replan has to beat and so prunes
its sweep, and the polish can delete a board change nobody needed, which shortens the ladder itself.

| 416 solutions, `build/solutions/l0` | keys | time |
|---|---:|---:|
| as banked | 11,060 | — |
| polish only | 10,327 | 126 s |
| replan before polish | 10,249 | 143 s |
| **polish, replan, polish** | **10,249** | 152 s |

Same total as the losing order, no collection worse than polish alone, **416/416 verified through
both engines**. The l0 corpus is layer 0's raw beam, whose `Cut` already breaks ties by cheapest
keystream, so it is the *unfavourable* population; the pass earns its keep on the searchers that
plan in shot space, where the movement between board changes is whatever the closure happened to
execute. On the seven hand-supervised `data/solutions/LaserTank/` recordings it is 821 -> 725, with
levels 3, 4 and 7 at **80 -> 57, 92 -> 61 and 81 -> 65**.

On by default; `--no-replan` turns it off, `--replan-width` (8) bounds the states kept per rung and
`--replan-nodes` (1.5M) is the backstop. The sweep is bounded by rungs x poses x 5 `ApplyKey` calls
and normally stops well under the budget - level 5 of `LaserTank.lvl` is the worst in the corpus at
412,882, which is why the default is not the 400,000 the first build shipped.

**A polisher bug this turned up on the way.** `data/solutions/LaserTank/00007.lpb` was written at 81
keys with polishing on, and a second `--polish` over the file took it to 68 - one 13-key round trip,
the second lap of the conveyor circuit, which `Decycle` finds immediately when it is handed the
file. Nothing about it needed a bigger budget. `Decycle` cuts at most one round trip per round and
gives up after sixteen, `Polish` called it once, and the width sweep after it tops out at twelve
keys - so on a raw solution with more than sixteen round trips, every one `Decycle` did not reach
and the sweep could not span stayed in. `Polish` now repeats its three passes while anything comes
out (four times at most, and the second pass only runs when the first removed something). It changes
nothing on the l0 corpus, which had already been polished once, and costs 8% there for the
convergence check.

### Phase 5 — Presentation & features  ☐

**This is the first phase where the deliverable is the game rather than a measurement, and the
discipline that got the project here still applies: the presentation layer must not become a second
implementation of the rules.** Everything in `LaserTank.Core` stays untouched — a Godot node reads
`Game.PF` and `Game.BMF` and draws them; it never decides anything. That is the same contract the
solver kept for four layers (`Engine.Search.cs` unchanged since layer 0), and it is why the fidelity
gates keep working while this phase is built.

**The head start, which is larger than it looks.** The renderer's input is already computed and
already ported. `BuildBMField` maintains `Game.BMF[x][y]` — the *bitmap number* per cell, not the
object id — `Animate()` cycles it, and `Obj.GetOBM()` is the object→bitmap table
(`LTANK2.C:77`). So drawing a frame is: for each cell, look up `BMF`, index the sprite atlas, blit.
Hazard #2 is what makes this safe (no bitmap ever feeds a decision) and hazard #1 is what makes it
dangerous (`UpDateLaserBounce` is a *paint* function that mutates game state): **the core already
calls it inside the tick — the renderer must not call it, skip it, or reimplement it.**

#### The steps, each with an exit criterion

**Step 0 — the Godot project, and the board on screen.** A `LaserTank.Game` Godot project
referencing `LaserTank.Core` as a plain library (it has no Godot dependency, which is what makes
this a reference rather than a rewrite). Load a level and draw the 16×16 board.

**The atlas geometry, decoded and verified against all three packs**, because getting it wrong is a
silent off-by-one: the sheet is **always 320×192 — a 10×6 grid of 32×32 sprites** — and `BMA[]` is
filled row-major from **i = 1**, ten per row (`GFXInit`, `LTANK2.C:782`). So sprite index `i` is
atlas cell `((i-1) % 10, (i-1) / 10)`. `MaxBitMaps` is 58 (`LTANK.H:92`) and the highest index the
object table yields is 57, so the last row is partly unused.

**Read `Game.BMF`, never re-derive it from `PF`.** `BuildBMField` is not simply `GetOBM(PF)`: a
tunnel is 55, the tank's own cell is 1 *and its `PF` is zeroed*, and `Animate()` then cycles `BMF`
for animated objects. Every one of those is a place a re-derivation drifts.
*Exit:* a headless test over all 2,347 corpus levels — every `BMF` value at load is in 1..57 and
maps to a cell inside the 10×6 grid. Cheap, no rendering, runs beside the other gates.

**Step 1 — the tick loop, and the gate that matters.** A fixed 20 Hz tick (`GameDelay = 50` ms,
`LTANK.H:96`) decoupled from rendering, driving `Engine.Tick()`; keyboard input appended to
`RecBuffer` exactly as `AddKBuff` does; visuals interpolated between ticks. **Never drive logic
from `_process`** (hazard #10).
*Exit — and this is Phase 5's real gate:* play a level in Godot, win it, save the keystream with
the existing `LevelFile.WritePlayback`, and **that `.lpb` must replay byte-identically through the
unmodified C oracle** (`tools/verify_solutions.py` already does exactly this, unchanged). A human
playthrough that survives the oracle is the same proof the solver's 472 solutions gave.

**Step 2 — graphics packs and zoom.** `.ltg` is a 324-byte `TLTGREC` header (`Name[40]`,
`Author[30]`, `Info[245]`, `ID[5]` = `"LTG1"`, `MaskOffset` DWORD) followed by two ordinary Windows
BMPs: the game bitmap from the end of the header to `MaskOffset`, the mask from there to EOF
(`LoadLTG`, `LTANK2.C:688`). Verified against the three shipped packs: the game bitmap is 320×192
at 24bpp (`Warcraft_II` is 8bpp) and the mask is 320×192 at **1bpp**. The original blits
mask-`SRCAND` then bitmap-`SRCPAINT` — 1-bit transparency — so the load step is "fold the mask into
an alpha channel" and the draw step is then an ordinary textured quad.

Zoom is 24/32/40 px (`SetGameSize`, `LTANK2.C:1729`), and the original implements it by
`StretchBlt`-ing the whole 320×192 sheet up or down at load. **Do not copy that.** Godot should
keep the atlas at native 32×32 and scale at draw time, which is the same picture without the
resample — the sprite size is a presentation choice and no logic reads it.
*Exit:* all three packs in `data/graphics/` load and render; switching zoom changes nothing but
pixels. Note hazard #11 lives in this function — `if (GFXOn) GFXKill;` is missing its parens and
must stay missing.

**Step 3 — sound.** The 16 WAVs in `original/src/Sounds/`. The tick already computes *which* sound
fires: `FireLaser`'s `sf` argument is the sound id and is load-bearing for logic
(`laser.Good = (sf == 2)`), so sound ids are read from the engine, never re-derived.
*Exit:* the gates stay green — i.e. adding audio changed no trace.

**Step 4 — the game around the game.** Level picker, high scores (`.hs`/`.ghs`, already read by
`LevelFile`), undo, and record/playback UI. Undo needs less than it looks: `UpdateUndo` /
`ResetUndoBuffer` and the whole `UndoBuffer` are already ported and maintained — only `UndoStep`,
the reader, is missing, because nothing headless ever called it.
*Exit:* a recorded game round-trips — record in Godot, replay in Godot, replay in the oracle, all
three agree.

**Step 5 — the level editor.** This is where `MouseOperation`, the one unported function, finally
gets written; it is a UI entry point rather than game logic, which is why it was left throwing.
*Exit:* an edited level saves as a `.lvl` the 2010 binary opens, and constraint 2 (community
formats stay readable *and* writable) is demonstrated rather than asserted.

**Step 6 — i18n.** `language.dat` via `LANGUAGE.C`.

#### What to be careful about

- **Do not "fix" anything on the way past.** Hazards #9 and #11 are both real bugs in the original
  that must survive, and #11 is inside `SetGameSize` — a function this phase has to touch.
- **The 20 Hz tick is not a rendering rate.** Interpolate sprites between ticks; a 144 Hz display
  must not consume 144 keys a second.
- **`.lpb` compatibility is bidirectional.** The 2010 binary must be able to play what Godot
  records. `LevelFile.WritePlayback` already writes the real 66-byte header format, and the
  solver's 472 recordings are the existing evidence that it round-trips.
- **Godot 4.7.2 Mono** is installed but has no `godot` alias (needs admin) — call the `.exe` by
  path; see *Environment notes*.

---

## Source map

| File | Lines | Role |
|---|---|---|
| `original/src/LTANK.C` | 1572 | Win32 window proc. **`WM_TIMER` at `:579` is the real game loop.** |
| `original/src/LTANK2.C` | 1834 | Game logic *and* GDI rendering, interleaved |
| `original/src/LTANK_D.C` | 1319 | Dialogs (level picker, high scores, playback, graphics) |
| `original/src/LANGUAGE.C` | 279 | i18n from `language.dat` |
| `original/src/lt_sfx.c` | 62 | WAV playback |
| `original/src/LTANK.H` | — | Structs, object-ID table, tunnel macros |

### Tick order (`LTANK.C:579`) — this *is* the spec
1. `Animate()` every `ani_delay`(=4) ticks
2. `MoveLaser()` if `Game.Tank.Firing`
3. Playback pacing / `PBHold`
4. Consume **one** key from `RecBuffer` if `!(Firing || ConvMoving || SlideO.s || SlideT.s || PBHold)`
   — **and, inside that same `if`, `AntiTank()`.** Anti-tanks act only on ticks where a key was
   consumed; on a tick with no key they do not play. (Earlier revisions of this list showed
   `AntiTank()` as an unconditional step 5, which is wrong and matters for the solver: it is why a
   "wait" is not free. `Engine.cs:1309` and `oracle/driver.c` both have it right.)
5. `IceMoveO()` then `IceMoveT()`
6. `ConvMoving = FALSE`, then conveyor / flag / water check on tank's cell
7. Mouse buffer
8. Repaint tank

**The wait.** The switch at `LTANK.C:616` has no `default`, and `Game.RecP++` runs regardless — so
any recorded byte outside {32, 37, 38, 39, 40} is a legal one-tick **wait** that still gives the
anti-tanks their turn, and `AddKBuff` (`LTANK2.C:256`) filters nothing, so a human pressing any
other key records one. **No human ever did:** all 54,162 bytes of all 187 `.lpb` are those five
keys, zero exceptions. The "wait" the tutor hints describe (level 4 *"Move up, wait"*, level 14
*"Wait 11 seconds"*) is a different thing — it is *free* time while the world is non-quiescent
(riding a conveyor, sliding on ice), during which no key is consumed and no byte is needed. So the
solver's action set is the five keys, matching the recordings. Note that neither engine's `--keys`
parser can express a wait anyway (both accept only `u d l r f`); if that ever changes, both change
together and the corpus gets re-run.

---

## Data formats (decoded and verified)

**`.lvl`** — flat array of 576-byte records, no header:
```
offset  size  field
0       256   playfield  char[16][16]   (PF[x][y], x = column)
256      31   name
287     256   hint
543      31   author
574       2   difficulty  u16  (1,2,4,8,16 = rated 1-5; 0 = unrated)
```
`data/levels/LaserTank.lvl` = **exactly 2030 levels**; 13 collections total, **20,914 levels**.

**`.ghs` / `.hs`** — flat array of 10-byte records, indexed by `level - 1`:
```
0  2  moves  u16
2  2  shots  u16
4  6  initials
```
**Every entry in all 13 `.ghs` files is non-zero** → every level is known-solvable, with
best-known move/shot targets. Ranking is lexicographic: moves first, then shots.

**`.lpb`** — 66-byte header then raw VK bytes:
```
0   31  level name
31  31  author
62   2  level number  u16
64   2  data size     u16
66   ..  keystream
```
Key codes: `37`=Left `38`=Up `39`=Right `40`=Down `32`=Fire.

**Objects** — IDs 0–25, table at top of `LTANK.H`. Tunnels are encoded out-of-band as
`0x40 | (id << 1) | waitbit`; see the `GetTunnelID` / `ISTunnel` macros.

---

## Quirk hazards — every one is load-bearing

**The rule these generalise to**, learned twice in Phase 2 and worth holding while reading any of
them: *in this program a function's name tells you nothing about whether it mutates state.*
`UpDateTank()` clears `TankDirty` (`LTANK2.C:537`) and `Animate()` ends by setting it
(`LTANK2.C:1161`) — both were nearly missed because they are named like paint calls.

1. **Rendering mutates game state.** `UpDateLaserBounce()` (`LTANK2.C:565`) is a *paint* function
   that sets `LaserBounceOnIce`, making `MoveLaser` `goto LaserMoveJump` and take a second step in
   the same tick (`LTANK2.C:1631`). Stub drawing naively → laser-on-sliding-mirror behaviour changes.
   **Confirmed live in Phase 2:** `Tutor-with-Playbacks` levels 93 and 94 are the only two
   recordings in the corpus that reach it (tick 527 and tick 206). Level 93's own hint names the
   mechanism — *"deflected by three mirrors at K8 (**sliding mirror**), K10, N10, and N8"*. To
   re-check it after any change to the laser or ice code, swap `LaserBounceOnIce = true` in
   `UpDateLaserBounce` for a throw and replay the corpus: it must fire exactly twice.
2. ~~**Animation frame is game state.**~~ **Corrected in Phase 1 — animation is cosmetic.**
   `Animate()` writes `Game.BMF[][]` and `MoveObj:1293` reads `bm = Game.BMF[x][y]` to carry the
   sprite along (the "Tere6 Bug" fix, `original/src/Bugs.txt` 02-25-02), but *every* read of `BMF`
   in the whole program is either a paint call or that sprite carry — **no bitmap ever feeds a
   decision.** Verified by exhaustive grep; see `oracle/README.md`.
   Consequences: `AniLevel`/`AniCount` need not be simulated for logic equivalence, and — more
   importantly — a `.lpb` replays identically regardless of which animation phase the game happened
   to be in when the level loaded, which would otherwise make every replay phase-dependent.
   The tutor readme's "the tank will temporarily disappear" is a rendering artifact.
   Still worth tracing in Phase 2: a BMF divergence is a cheap tripwire for a transliteration slip.
3. **`wasIce` is a hidden return channel** from `CheckLoc()` (`LTANK2.C:1278`), read by three callers.
4. **Tunnel low bit is a flag.** `Game.PF2[x][y] |= 1` marks "waiting to transport";
   `Game.PF[x][y] & 0xFE` strips it.
5. **Anti-tank fire order is right → left → down → up**, and only the *first* match fires per call
   (`LTANK2.C:1655`). Tutor level 42 is literally "Inverse A-T's shooting order."
6. **Slide stack caps at 15**, silently (`if (SlideMem.count < MAX_TICEMEM-1)`), and `IceMoveO`
   mutates the stack while iterating it top-down (`LTANK2.C:1390`).
7. **`MoveObj` decrements `ScoreMove` and `UndoP`** in the tunnel path — the "Bartok Bug"
   workaround (`LTANK2.C:1310`).
8. `SendMessage(WM_Dead)` vs `PostMessage(WM_Dead)` — immediate vs deferred death, deliberately
   changed in 4.0.6. Ordering is observable.
9. `BuildBMField()` (`LTANK2.C:843`) leaves `i` uninitialized on one branch; currently unreachable
   because of the 2003 sanitization above it, but do not "fix" it silently.
10. Godot must run logic on a **fixed 20 Hz tick decoupled from rendering**, interpolating visuals.
    Never drive logic from `_process`.
11. **`LTANK2.C:1738` reads `if (GFXOn) GFXKill;`** — a missing `()`, so the call never happens.
    A real bug in the original, in `SetGameSize`, and cosmetic. Same species as #9: **do not
    "fix" it.** It is Phase 5 territory, which is the phase most likely to want to.

12. **`LoadLevel` does not reset the stale flags, so one `Engine` cannot replay two keystreams.**
    It resets `PF`, the tank, `RecP` and the slide records; it leaves `wasIce`, `WaitToTrans`,
    `ConvMoving` and `BlackHole` exactly where the previous game left them — faithfully, because
    the original never reloaded a level into a fresh process either. The consequence is a rule for
    *our* code rather than a quirk in the original: **anything that replays candidate keystreams
    must build a fresh `Engine` for each one.** `Trim` did not, and silently reported winning
    keystreams as losing — see the polishing addendum in Phase 4 for how it was caught and what it
    cost. The search itself is unaffected: `Restore()` puts all four flags back, which is exactly
    why `EngineSnapshot` carries them.

---

## Repo layout

```
original/   the frozen 25-year-old artifact — read-only
  src/        2007 source distribution, verbatim
  bin/        shipped 2010 lasertank.exe + LTUDU data updater
data/       game content = the regression corpus
  levels/     13 collections, 20,914 levels, all with .ghs targets
  quirks/     10 tutorial/trick packs, 317 levels, 187 .lpb recordings
  demos/      human playthroughs — recorded by hand, cannot be regenerated, and
              deliberately NOT under build/solutions/ so a hand solution is never
              mistaken for a solver one
  solutions/  what the interactive driver banks — one hand-supervised level at a
              time, each already through the two-engine gate
  graphics/   .ltg packs      meta/  changelogs & name indexes
oracle/     the C reference oracle — see oracle/README.md
  stub/       minimal <windows.h> that shadows the real one
  win32_stub.c  real memory/files/messages, no-op GDI
  driver.c    LTANK.C globals + window proc + the WM_TIMER tick loop + tracing
  build.sh    gcc -x c -I stub -I original/src
src/        the C# port         build.sh -> build/lasertank-core.exe + lasertank-solve.exe
  LaserTank.Core/  Objects.cs GameState.cs LevelFile.cs Engine.cs  (no Godot here)
                   Engine.Search.cs — snapshot/restore, ApplyKey, StateHash (Phase 4)
  LaserTank.Cli/   Program.cs TraceWriter.cs — the oracle's CLI, the oracle's trace
  LaserTank.Solver/ Search.cs Heuristic.cs Trim.cs Report.cs Program.cs — the batch
                   solver; writes .lpb, never trusted without verify_solutions.py
                   Auto.cs — the interactive driver: `lasertank-solve FILE.lvl`,
                   every searcher at once, budget x4 per round, until you press
                   a key; keeps only what verify_solutions.py has passed
                   Macro.cs — layer 1: Goto (a movement closure over ApplyKey)
                   + Shoot, so search depth is shots rather than keypresses
                   Subgoal.cs — layer 2: the obstacles between the closure and
                   the flag, derived; a successor is kept because it cleared one
                   Restart.cs — layer 3: re-run the subgoal beam when it dies of
                   an empty frontier with budget in hand, growing width each time
                   Learn.cs, Weights.cs — layer 4: board features, the learned
                   evaluation that orders the beam, and the instrument that
                   dumps one ranking group per shot of a winning recording
                   Profile.cs — `--profile`: replay a winning recording and print
                   FlagDistance/WorkDistance/RouteFerry at every keypress.
                   Measures the *level*, not the search; tools/basin.py reads it
                   Push.cs — layer 5: a PF-preserving movement closure, then every
                   board change reachable from it, so depth is the board-change
                   count rather than the keypress count
                   Analyze.cs — layer 6, the read: `--analyze` / `--analyze-tsv`
                   / `--read-dump`.  What is in the way, every board change the
                   tank can make right now (enumerated by making them), which of
                   those advance, and a verdict naming the level's shape.  Also
                   ReadDerive/ReadAdvances/ReadOpens, the same derivations as a
                   ranking tier for layer 5 (`--push-read`)
                   Trim.cs — Shrink (delta debugging, past --trim-ratio) and
                   Polish (every solution: round trips, turns on the spot,
                   shots at nothing; each deletion replayed before it is taken)
                   Replan.cs — post-solve: the board changes a solution made are
                   a ladder of positions known to win, so re-derive the shortest
                   route up it and let it skip the rungs that cancel out
build/      C# output (gitignored)      LaserTank.slnx  the solution
tools/
  replay_all.py     replay every .lpb; green/red gate (expected outcomes + .ghs targets)
                      --traces DIR [--field] [--bmf] writes one trace per recording
  difftrace.py      compare two traces, or two directories of them: first diverging
                      tick, first field, per-cell playfield diff   <- the Phase 2 gate
  test_difftrace.py self-test for difftrace.py; run it before trusting a verdict
  engines.py        both engines on one input + compare -> a Div or None.  Shared
                      plumbing for the two below; the comparison is difftrace's
  sweep.py          one fixed keystream over every level of a .lvl, both engines.
                      Bare, it is the empty-keystream sweep: 2,347 levels
  fuzz.py           random keystreams, both engines, and **shrink** a divergence
                      to level + shortest keystream   <- the Phase 3 gate
  test_fuzz.py      self-test for fuzz.py: injects known faults into the C# core,
                      rebuilds, and fails unless the fuzzer finds and shrinks them
  verify_solutions.py replay every solver .lpb through BOTH engines: WIN on each,
                      byte-identical traces, and the ratio to the .ghs record.
                      --levels names the .lvl instead of finding it by directory
                      name, which is how Auto.cs gates one candidate at a time
  campaign.sh       one solver campaign over all 13 collections into one report.
                      Node-governed, not wall-clock: two layers have to be
                      comparable and a contended machine makes seconds lie.
                      STRIDE=N samples every Nth level of every collection
  second_pass.sh    re-attack a campaign's unsolved levels with a different
                      searcher, into the same solutions dir  <- where layer 1 ships
  report_stats.py   read a campaign .jsonl: per-tier solved rates, per-collection
                      rates, stop-reason breakdown.  --diff compares two layers
  rankdump.py       layer 4's instrument: replay every winning .lpb and dump the
                      group of successors the shipped subgoal expansion offered
                      at each shot boundary, labelled with what the winner did
  fit_eval.py       read that dump.  Bare, it reports the distribution (is the
                      right successor even in the group, and where does
                      WorkDistance rank it); --fit fits the evaluation and
                      regenerates Weights.cs
  bump_rate.py      classify consumed keys; bumps = desync signature
  basin.py          read a --profile dump: how far *uphill* a winning line goes,
                      per level, in keypresses and in board-changing keypresses.
                      The number that separates "needs more nodes" from "needs a
                      different move set"  <- the layer 5 instrument.
                      --ferry-weight sweeps layer 5's ferry weight offline,
                      against a recording, instead of by re-running the solver
  dump_level.py     print a .lvl level as ASCII with its hint
  unpack_lpb_txt.py decode a Text-Converter .txt wrapper back to .lpb
```

See `README.md` and `data/SOURCES.md` for provenance.

## Test corpus

**A trap for the day a `.lpb` will not replay:** `data/levels/LaserTank-2016-snapshot.zip` is a
third vintage of the flagship collection, **59 of its 2,030 levels differing** from the current
one. A recording made against one vintage cannot replay against another, so before debugging the
engine, check the level bytes.

**Tier 1 — `data/quirks/`, 317 quirk-focused levels, 187 recorded playbacks.**
Upstream deliberately withholds `.lpb` for the main collections; these help-section packs are the
only recorded human solutions in existence.

| Directory | Levels | LPB | Note |
|---|---:|---:|---|
| `tutor-with-playbacks` | 112 | 112 | + bundled `.ghs` — the only pack where recorded counts can be checked against a target |
| `tutor` | 92 | 0 | **the quirk specification** — each hint documents its trick |
| `rotary-mirrors` | 39 | 39 | 6 of these do not reach the flag — see above |
| `tricks` | 26 | 0 | |
| `pono-trick` | 18 | 20 | more LPBs than levels (alternate solutions) |
| `game-objects` | 16 | 16 | one level per object — **best first target for the oracle** |
| `4triang`, `telek-1`, `l40`, `inchworm` | 14 | 0 | |

**Tier 2 — `data/levels/`, 20,914 levels across 13 collections**, every one with a non-zero `.ghs`
entry. No keystreams, but a solvability guarantee and a (moves, shots) target for each. This is the
fuzzing surface for Phase 3.

## The six non-winning recordings

All six are in `rotary-mirrors`, and five of the six are the only files in that pack whose `.lpb`
author field reads `Ihab` rather than `Ihab-Ihab`. They consume their entire keystream and stop
short of the flag. `tools/replay_all.py` pins each one's expected outcome, asserting the exact
numbers wherever a level hint documents them. Evidence that this is the recordings, not the engine:

| file | level | replay | corroboration |
|---|---|---|---|
| `_0036` | 36 `noor II` | 621 keys, 419 shots | hint: *"I invite you to complete the solution … it is stopped at step 621 (after 419 shots)"* — **exact match** |
| `_0009` | 9 `rotary mirrors 4-c1` | 39 moves | hint: *"blocked at 39 steps"* — **exact match**; level 10 is the same puzzle (`4-c2`) and its recording wins |
| `_0021` | 21 `rotary mirrors 5-g` | 148 moves, 257 shots, then dies | hint: *"it has a solution : 148/257 or better"* — **exact match**; see below |
| `_0011`, `_0013`, `_0017` | 11, 13, 17 | — | no hint text; inferred from the zero-bump result below |

**The zero-bump result.** A key that produces neither a move, a turn, nor a shot means the tank
walked into something solid. Across all 187 replays — **54,162 keypresses — not one bump.** A
desynced engine puts the tank in the wrong place and blocked moves pile up immediately; instead
every single recorded keystroke in the corpus does exactly what a keystroke should do, including
throughout all five non-winning recordings. `tools/bump_rate.py` computes this.

**The level-21 confirmation.** `rotary-mirrors` ships level 21's playback as `_0021.txt`, a
Text-Converter base64 wrapper rather than a `.lpb`. Decoded with `tools/unpack_lpb_txt.py` (594 B,
528 keys, author `Ihab`) and added to the corpus — the only derived file under `data/`, documented
in `data/SOURCES.md` and regenerable from the `.txt` beside it.

Level 21's hint says *"it has a solution : 148/257 or better."* Replaying it gives **148 moves /
257 shots** and leaves the tank one cell above the flag, facing it — then its final two keys turn
the tank around and drive it into water. Replace that trailing `uu` with `dd` and the oracle **wins
at exactly 148/257**, the documented optimum. So the engine reproduces a known-good solution
precisely, and the distributed recording's tail is simply wrong. This is one of the few independent
checks on absolute scoring outside the `Tutor-with-Playbacks` `.ghs`, which is why it earns its
place despite not winning.

## Environment notes

- Two Pythons, both fine: `python` = 3.12.7 (miniforge), `python3` = 3.14.7 (installed 2026-09-05,
  which retired the old Microsoft Store shim). Everything in `tools/` is stdlib-only and verified on
  both — keep it that way so either alias works.
- C toolchain: MinGW-w64 (WinLibs gcc 16.1, UCRT), `winget install BrechtSanders.WinLibs.POSIX.UCRT`.
  Not on `PATH` globally; `oracle/build.sh` finds it under `~/AppData/Local/Microsoft/Winget/Packages/`.
- **.NET SDK 10.0.400**, `winget install Microsoft.DotNet.SDK.10`, at
  `C:\Program Files\dotnet`. Same story: not on the shell's `PATH` until it restarts, so
  `src/build.sh` finds it. Only the .NET 10 runtime is present, hence `RollForward=LatestMajor`
  on the `net8.0` CLI.
- **Godot 4.7.2 (.NET/Mono build)**, `winget install GodotEngine.GodotEngine.Mono`, unpacked to
  `~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_*/Godot_v4.7.2-stable_mono_win64/`.
  The `godot` alias needs admin to be created, so call the `.exe` by path. Nothing in Phase 2
  needs it — the core and its CLI are plain .NET — it is here for Phase 5.
- **Trap in the oracle's own usage text:** it advertises `--keys` as accepting "raw decimal VK
  codes separated by commas", but `driver.c` only parses the characters `u d l r f` and silently
  skips everything else. `--keys 38,38,32` therefore yields an *empty* keystream and an idle run
  that looks like it worked. The C# CLI matches this behaviour deliberately (same parser, same
  skipping) — if you fix one, fix both and re-run the corpus.
- **`bash` invoked from Python is WSL's `System32\bash.exe`**, not Git Bash — different
  filesystem, no gcc, no dotnet, and it fails with an unreadable `execvpe` error.
  `tools/engines.py`'s `find_bash()` skips System32 and falls back to the Git for Windows paths;
  `$LT_BASH` overrides. Any new tool that shells out should use it rather than bare `bash`.
- **Rewriting a source file from Python in text mode rewrites every line ending.** `src/` is LF,
  Python's text mode makes it CRLF, and `core.autocrlf=true` then makes `git diff` show *nothing*
  while every line on disk has changed. Patch and restore in **bytes**, or pass `newline=''`.
  `tools/test_fuzz.py` does, and a green self-test leaves the tree byte-clean.
- **Never run `test_fuzz.py` while a solver process is alive.** It rebuilds the core, and Windows
  keeps `build/LaserTank.Core.dll` locked open by every running `lasertank-solve.exe`, so the
  rebuild loses its retry ladder and *both* the "clean core builds" control and the restore check
  report `FAIL` — a red gate that is entirely the machine. The tell is `MSB3027 ... The file is
  locked by: "lasertank-solve (NNNNN)"` in the output. The tree is still left byte-clean, so the
  fix is simply to wait for the search to finish and re-run. Same trap in the other direction:
  `src/build.sh` cannot replace the binary while a search is running, which is what killed session
  17's decision pass twice.
- **A backslash does not survive `python - <<'EOF'` in this harness.** Rewriting `PROGRESS.md` (or
  any file) through a Python heredoc silently turns `\n` into a real newline and eats `\` line
  continuations, which is how the *Next action* block's `chain.jsonl` snippet came to be split
  across two lines and its `second_pass.sh` invocation to lose its continuations. Use the editing
  tools for anything containing a backslash, or write the replacement text to a file first and read
  it in.
- **The quirk packs mix `.lvl` and `.LVL`**, and the four that ship uppercase are the four biggest.
  A `glob("*.lvl")` is case-insensitive on Windows and silently drops them on Linux — it already
  cost one campaign four packs with no warning. Match on `suffix.lower()`, as `replay_all.py`,
  `sweep.py` and `verify_solutions.py` all now do.
- laser-tank.com is behind Cloudflare: `WebFetch` returns 403. Use `curl` with a browser
  User-Agent. The site is a frameset — real content is in `menu.html`, `help.html`, `levels.html`.
- Original build was lcc-win32 (`original/src/_How to compile LTank.txt`, `LTank.prj`). Its dependencies are
  shallow; MinGW/clang should work.

## Open questions

- **Which binary is the behavioural reference?** `original/bin/lasertank.exe` is dated 2010;
  `original/src/Setups/Files/lasertank.exe` is the 2007 build matching this source. Both are
  UPX-packed and 148,512 bytes but differ across ~95% of their bytes, so the version can't be read
  off without unpacking. `Bugs.txt` stops at 4.1.2 (2005), so the 2010 build may contain changes
  we have no source for. Resolve in Phase 1 by trace-diffing the oracle against both.
  The Tutor readme warns: *"made/verified using LaserTank.exe Ver 4.1. The use of earlier versions
  may cause different results."*

- **`lasertanksolutions.blogspot.com` as a source of goal boards — checked far enough to cost, not
  started.** Michal raised it; what one post actually contains was verified rather than assumed
  (`Challenge-II-100`, `.../2024/07/challenge-ii-100-no-problemo.html`): a **start screenshot**, one
  **screenshot per flag showing the board at the moment of reaching it**, and — this is the part
  that makes it interesting — the game's own **Moves and Shots counters visible in the panel**
  (56 / 22 for that level). No move list, no keystream, no prose. Roughly 2,000-3,000 posts across
  2016-2024; the 2024 archive is `Challenge-II`.

  **What it would buy is not solutions, it is a goal.** A final board says which blocks were moved
  where and which bricks were destroyed, so "reach the flag" becomes "reach *this* board" — a
  progress measure that decreases with every push in the right direction, which is exactly the
  gradient layer 5's ferry term is a hand-rolled approximation of. The counters are a second gift:
  an exact cost target to bound a search by and to check a solution against.

  **Feasibility, measured on two downloaded images.** They are full-window PNGs (609x463, 619x473),
  not board crops: the 16x16 board sits at a fixed offset at roughly **24px per cell**, i.e. the
  32x32 sprites scaled down, so this is template matching and not OCR. The codebook does not even
  have to be built by hand — **for every post we already know the collection and level, so the
  start screenshot is 256 labelled tiles for free**, and the goal images decode against a codebook
  bootstrapped from the starts. Unknowns worth checking before committing: whether the window
  geometry is stable across nine years of posts, and which of the three `.ltg` packs is in use.

  **The honesty condition, and it is not optional.** A level solved with a scraped goal board is
  *hint-assisted* and must never enter the solver's headline rate. Its value is as a bootstrap:
  hint-assisted solutions are real recordings, and real recordings are what `--profile` /
  `tools/basin.py` measure and what layer 4 is fit on — the off-distribution long-level sample the
  addendum says is the blocker, obtained without anyone playing 20 levels by hand.

## Cross-references — do not trust for quirk fidelity

- `github.com/tobiasvl/lasertank` — mirror of this same source.
- `github.com/h4tr3d/laser-tank` — SDL2/C++ port, but descends from a KolibriOS *reimplementation*.
- `lasertankpedia.zdobywca.com`, `lasertanksolutions.blogspot.com` — community game-data references.

The oracle is the only authority.

---

## Session log

The reasoning behind each phase lives in that phase's section above; this is the changelog, kept
short on purpose. Where a session's finding is still load-bearing it has been moved to where it
belongs — traps to *Environment notes*, engine hazards to *Quirk hazards*, measured negatives to
the layer that measured them — so that nothing here needs reading to work on the project.

**2026-09-05, sessions 1-2 — Phase 1.** Read the source, decoded `.lvl`/`.ghs`/`.lpb` against the
real files, catalogued the quirk hazards, settled the three-engine architecture, reorganised the
repo. Then built the oracle on the load-bearing decision that made everything after it possible:
**stub only the Win32 API and compile `LTANK2.C` verbatim.** Measuring the external surface first
(`nm -u`, 49 calls and ~12 globals) is what turned it into a bounded job. Result: all 187
recordings replay, 181 win, 54,162 keypresses, 0 bumps, and 112/112 `Tutor-with-Playbacks` match
their bundled `.ghs`. Hazard #2 was *corrected* here rather than confirmed — animation is cosmetic
— which is what makes replays independent of the animation phase at load.

**2026-09-05, sessions 3-7 — Phase 2, the transliteration.** Six steps, each gated on the corpus:
the differ (`difftrace.py`, self-tested 29 cases, fault-injected 7 ways), then the core stood up,
then `CheckLoc`/`MoveObj`, then `MoveTank`/`AntiTank`, then the whole laser subsystem, then the ice
and conveyor movers. Ended **byte-identical to the oracle on all 187 recordings** with
`--field --bmf`. Two decisions earned their keep repeatedly: unported functions **throw** rather
than no-op (a stub would have hidden the `FireLaser(..., S_Fire)` bug for three more steps), and
the exit criterion was always the corpus rather than a spot check.

**2026-09-05, session 8 — Phase 3, the fuzzer.** `engines.py`, `sweep.py`, `fuzz.py` and
`test_fuzz.py`, with both planned faults injected and caught and shrunk to two keys each. Campaign:
**20,626 cases, 3,751,638 tick-lines, 0 divergences** — 24x the recorded corpus. The honest limit
is recorded with it: only 55% of generated keys are ever *consumed*, because random play is wide
and shallow, which is the argument for Phase 4's solver as the complementary coverage.

**2026-09-05, session 9 — Phase 4, layer 0.** The search API (`Engine.Search.cs`: snapshot/restore,
`ApplyKey`, `StateHash`), the batch harness, and `verify_solutions.py`. The harness caught two bugs
in itself, both worth not re-discovering, and both are written up in the layer 0 section: a
macro-step is not bounded by anything cheap (3,652 ticks for one keypress on a conveyor circuit),
and `Restore` rewinds `RecP` while `RecBuffer` is one shared array.

**2026-09-05, session 10 — the campaign, and layer 1.** Threw away a wall-clock-budgeted campaign
and re-ran it node-governed, which is where that rule comes from. Layer 1's macro-actions win on
levels layer 0 fails and *lose* over the corpus in both orderings, so they ship as a second pass
rather than a portfolio member — the finding that shaped every layer after it. 395 -> 416.

**2026-09-05, session 11 — layer 2, subgoal decomposition.** Derive what is in the way from the
*executed* movement closure rather than from the price list; accept on a board test, rank on a
position test. The modelled first version found no obstacle on 62% of expansions and is kept in the
write-up as the thing that `--sg-trace` killed. 416 -> 441.

**2026-09-05, session 12 — layer 3, restarts.** Priced the dead-end failure mode before designing
for it (717 levels, 84% of budget unspent), then found that what recovers a dead-end is *width
bought after narrow has failed*, not randomness. Strictly additive by construction and checked to
be. The negative half is the larger half: dead-ends 717 -> 9 bought four levels. 441 -> 444.

**2026-09-06, session 13 — layer 4, a learned evaluation.** Built the instrument first
(`rankdump.py`), and it authorised the layer rather than redesigning it: the winner's successor is
in the expansion's output **97.6%** of the time and `WorkDistance` ranks it **100th of 395**, so
the loss was entirely in the sort. Fit as a ranking problem within a group, not a cost-to-go
regression. **444 -> 472, none lost.** Two results kept because they are the useful kind: a
re-ranking is *not* additive the way a restart is (so the chain grew a pass instead of swapping
one), and feeding the newly solved levels back in **halves what the model discovers** while
raising its score.

**2026-09-06, session 14 — the handoff itself.** No code. Added the *Start here* block (build,
gates, one level, the whole chain — every command in it run and verified), turned Phase 5 from a
seven-line wish list into a plan with exit criteria, and **pruned the file from 2,342 lines to
~1,650**. What was cut was narrative of completed, verified work whose outcome is stated in its
phase section — the old Status block was 187 lines duplicating Phases 2-4, and the session log was
680. What was *kept* is anything a future session could waste time re-discovering, and four such
facts that lived only in the session log were rehomed first: the WSL-`bash` trap, the
text-mode line-ending trap and the `.lvl`/`.LVL` case trap to *Environment notes*, the
`GFXKill` missing parens to *Quirk hazards* as #11, and the 2016-snapshot warning to *Test corpus*.
Decoded the sprite atlas properly while writing Phase 5 (320×192, a 10×6 grid of 32×32, `BMA`
row-major from i=1) and verified the `.ltg` header against all three packs, which corrected two
claims the plan would otherwise have shipped wrong.

**2026-09-06, session 15 — why level 1 is unsolved, and the instrument that says so.** Started from
a complaint ("the solver cannot do the first level") and refused to answer it from the level number.
Three measurements, each of which changed what the answer was: the record is 149, the 66th
percentile of its own collection; layer 0's beam ranks this level by Manhattan distance for its
entire life, because `FlagDistance` degenerates when the flag is unreachable; and — from Michal's
hand recording, now `data/demos/LaserTank/00001.lpb`, verified through both engines — the winning
line spends **68 keypresses above its own best `WorkDistance`**, against a p90 of **21** over the
402 recordings the solver has produced. So it is not a budget problem, and the layer 5 argument
(push macros: the same ascent is 16 events instead of 68 keypresses) is a measured one. Built
`--profile` (`Profile.cs`) and `tools/basin.py` to make all of it repeatable, and reverted the
`WorkDistance`-ranked beam experiment — it transforms this level's trace and loses on the bench,
12/50 against 13/50, which is exactly the kind of result the benches exist to catch.

**2026-09-06, session 16 — layer 5, push macros.** Built the layer the addendum argued for: a
PF-preserving movement closure, then every board change reachable from it, so search depth is the
board-change count. The structural claims hold — closures never truncate, level 1 runs at depth 50
instead of 264, and a 40-level smoke verified 14/14 through both engines with 11 exact records —
and **the cost claim decides it for now**: one expansion is ~4,500 `ApplyKey` calls against layer
0's five, so on the deep bench it is 3/50 at 400k and 7/50 at 4M where layer 0 is 13/50 at both. It
does not ship in the chain. The keeper is `Heuristic.RouteFerry`, the term that makes *carrying* a
block score better than not carrying it — measured on the human recording, it shortens the ascent
from 68 keypresses to 49 (16 events to 12) at weight 1, gets shorter and deeper past weight 2, and
is exactly inert over the 402 trajectories the solver has already solved. Level 1 is still
unsolved: the beam now reaches the flag's doorstep and stalls on a 12-event ascent where 300 tied
states are broken by cheapest-keystream, which favours the state that has not started the ferry.
Restarts (layer 3's control law, ported) buy the forfeited budget back and change nothing: two
600M-node runs, three restarts each, still unsolved -- width does not fix a tiebreak.  That
tiebreak is next, and it needs a population that contains a ferry, which the deep bench does not.

**2026-09-06, session 16 (cont.) — the second demonstration, and what to do next.** Michal recorded
`LaserTank.lvl` level 2 by hand (`data/demos/LaserTank/00002.lpb`, 119 keypresses, 1.7x the record,
**2/2 demos verified through both engines**) and it is the useful kind of second data point: a
conveyor-and-shooting level with no water at all, so the ferry term is provably inert on it, and it
fails the same way — longest ascent 35 keypresses / 13 board changes against a solved-population p90
of 21 / 8, unsolved by the raw beam (`beam-dead-end` at 1.6M nodes), by the learned subgoal beam and
by layer 5 (both a full 40M). **Two levels, two structures, one failure mode**, which is the first
evidence the ascent length belongs to long levels rather than to level 1 — and it is n=2, which is
the whole problem. Agreed next action is therefore the blog harvester rather than more solver
tuning: the `Cut` tiebreak that layer 5 now points at cannot be measured against a population that
does not exist. *Open questions* carries the feasibility check (full-window PNGs, ~24px cells,
codebook bootstrapped from the start images whose contents we already know) and the honesty
condition (hint-assisted solutions never enter the headline rate).

**2026-09-06, session 17 - layer 6 (the read), the polisher, and a trimmer bug worth the session.**
Michal hand-recorded `LaserTank.lvl` up to 19 (**20/20 verified through both engines**) and asked
for the thing a player does before searching: *"level 4 - I immediately see I have to make a bridge,
I see the block, I know I have to use the mirrors and avoid the ATs; or level 6 - no antitanks, only
blocks and water, I instantly know there will be long sokoban shit. This kind of analysis is what we
need over an application of five different Knuth algorithms."*

**Layer 6, the read** (`Analyze.cs`). Layer 2's discipline one step further out: *what must change*
is the priced Dijkstra stopped at the executed pose closure, *what can change it* is enumerated by
**making** every change. No mirror, conveyor or block-sinking code exists in the file - level 4's
three-mirror bank shot is found because firing left from (7,15) was tried and the block at (2,2)
moved. Three derivations of progress (lands on the barrier / moves a block nearer route water /
`opens`: the tank can stand somewhere new) and a verdict decision list. Measured three ways: over
the 4,185-level stride sample in 64s (OPEN 96.6% solved as a sanity check; FERRY+SOKOBAN 53% of the
corpus at 5.1%; the fill count predicts the rate monotonically, 1 fill 9.9% to 9+ fills 0.7%); over
the 20 recordings, where the human's next board change is in the enumeration **800/800** and named
by the read **83%**; and as a tiered beam ordering, which beats no ordering at every width and by
less than the action set does.

**Layer 6 inside layer 5** (`--push-read`). Two false starts, both ended by `--push-trace` reporting
0% promoted: the cheap `opens` proxy asked whether the *flag's* component grew where the 86% came
from asking whether the *tank* can stand somewhere new (`Heuristic.TankRegion` is the fix), and then
the counter itself was incremented before the `opens` pass ran - **an instrument that measures the
wrong moment says the layer does nothing**. The corrected trace gave the finding: the read promotes
5-11% of successors into a beam of width **300**, so `Cut` never binds and the read cannot help.
That turned it into a width experiment on a banked ferry population (`ferry-levels.txt`, built with
the read itself because the two older benches contain almost no ferry), and both halves are keepers:
**layer 5's width was simply wrong** - 300 -> 48 takes the deep bench 7 -> 13, matching layer 0 there
for the first time - and **the read is worth 4/50 -> 11/50 on ferries at width 48** and one level on
the deep bench, which is its own claim about itself holding up. 11/11 verified. The corpus pass that
decides whether it ships was started twice and killed twice to free the binary; it is written out in
*Next action*.

**The polisher** (`Trim.Polish`, on by default, `--no-polish`, plus `--polish PATH` for existing
`.lpb`). Michal: *"get rid of repeated turns in place and shooting at nothing - they look very
computery."* Round trips found by `StateHash` recurrence, turns on the spot, shots that change
nothing, then an exhaustive contiguous-deletion sweep at every width 12 down to 1 - contiguous
rather than a halving ladder because the run he actually pointed at is *five* keys long, which is
exactly what 1/2/4/8/16 skips.

**And the bug that made all of it look like a no-op.** `Trim` replayed every candidate through one
reused `Engine`, and `LoadLevel` deliberately does not reset `wasIce` / `WaitToTrans` / `ConvMoving`
/ `BlackHole` (quirk #3), so each candidate inherited the previous one's leftovers and **keystreams
that win from cold were reported as losing**. The polisher was declaring solutions irreducible that
were not, and said so twice to Michal's face before he pushed back with the replay: *"it really is
not single-key minimal at 71 - step 2 is a useless turn south, step 3 is visually a noop..."* He was
right. A fresh engine per candidate takes that solution **71 -> 51 keys and 23 -> 14 shots**, opening
exactly as he described, and takes the deep bench from 27.4% removed to **47.3%** (`Beginner-I` 1488:
831 -> 212 keys). Recorded as *Quirk hazards* #12, because the rule generalises past `Trim`: anything
replaying candidate keystreams must build its own `Engine`. Nothing wrong ever shipped - every
`.lpb` passes a fresh-engine replay in `SolveOne` and another in `verify_solutions.py` - the cost was
entirely in reductions not found.

Also: the interactive driver now writes to `./solutions` (committable) rather than gitignored
`build/`. All four fidelity gates green, and `Engine.cs` still differs from layer 0 by one word.

**2026-09-06, session 18 - the beam was ranking tank poses, and the instrument that said so.**
Started from the same complaint as sessions 15 and 17 - *"level 1 is still not solvable even on
round 5"* - and refused, again, to answer it by tuning. The three previous explanations were all
true and none of them named a line of code, so the session built the instrument that does:
**`--push-line`** (`Line.cs`) replays a hand recording, keeps its state at every board change - one
per push-beam depth - and runs the real beam with those in hand, reporting per depth whether the
line was generated, how the ranking key placed it and whether the width trim kept it. It reads the
line's survival off the *playfield* rather than the state, because every pose in a closure offers
the same board changes and the exact state routinely drops out at one depth and comes back two
later.

**It answered in one row.** Level 1's line dies at **the first board change**, ranked 130 of 156 -
nowhere near the 12-event ascent layer 5 was built for. The cause is that a successor here is
(board change, the pose it was fired from), and one change is reachable from every pose in the
closure: the root closure is 158 poses offering **4** distinct changes, so 156 successors are four
boards wearing thirty-nine hats each, and `Cut` - ranking by a heuristic that depends on where the
tank stands - filled all 48 slots with poses of one or two of them. A `boards=` column added to
`--push-trace` reads **1 to 9 distinct playfields at width 48**, and exactly one at three depths.
Each duplicate then bought its own ~4,500-call closure to re-derive what its twin had already
produced. **That is where the layer's budget had been going all along**, and it re-explains every
"narrower is better" result before it: narrow was buying fewer duplicates, never focus.

**`--push-per-board`**, default 1, caps poses per playfield and lets the frontier come out narrower
than the width. Ferry bench 11/50 -> 14/50 and deep 14/50 -> 17/50 on that alone, with per-board 0
reproducing session 17's recorded 11 and 14 exactly. Three more measurements followed from a trim
that finally spends width on boards, and each moved a default: the **learned evaluation** makes
level 1's winning line only **6 board changes uphill** against 12 for the ferry term and 16 for
plain work - inside the solved population's p90 of 8 for the first time - and is worth ferry
14 -> 15, deep 17 -> 19; the **width** wants to be **8** (ferry 19, deep 21) where 300 gives 8 and
12; and the **restart** must buy width *alone*, because doubling the per-board cap with it had level
1 running at width 128 over eight distinct boards by the fourth restart, the duplicates quietly
back. `--push-depth` went to `MaxKeys` after seven of the ferry bench's fifty stopped at the old
400 cap at width 4.

**Layer 5 as it now stands: ferry 20/50 and deep 21/50 against 11/50 and 14/50**, layer 0 being
13/50 on the deep bench; 40/40 verified through both engines, and the old configuration still
reproduces its own two numbers from its flags. **And the corpus pass session 17 left undone was
run**: over the levels the whole shipped chain fails, `SAMPLE=15` at 4M nodes, **15 of 255 (5.9%),
all fifteen verified**. That is 27x the campaign budget, so it argues for a fourth pass at a high
budget rather than for changing the campaign - which is now the next action, written out in
*Status*.

**Also: the interactive driver's push rung was getting weaker every round**, which is half of why
the complaint that started this session kept coming back. The ladder doubles a rung's width per
round, so against the new default of 8 round 5 ran at 256 and never turned the read on - benched as
the ladder actually ran it, **11/50 on the ferry bench against the default's 20/50**. It now grows
restarts instead (6 and 36 both score 20/50, so it costs nothing) and turns the read on.

**Level 1 is still unsolved** - four 800M-node runs across widths 4/8/16 and 10 restarts - and is
closer in a way that is measured rather than felt: at width 48 the line survives to board change 6
where the old trim lost it at 1 (at the shipped width of 8 it is 2, a frontier that holds fewer
boards buying depth with it), and the search reaches a learned score of 14 against the human line's
own final 10 where before it stalled at 37. Depth 6 is the first of five roto-mirror
rotations - pure setup, on which the read is silent by construction and every ranking key is flat.
Naming those wants a fourth derivation, *"after this change the tank can make a board change it
could not make before"*, which costs an effect enumeration per successor; that is a measurement to
make before it is a feature to build.

All four fidelity gates green (187/181/6, 29 difftrace, 2,347 sweep identical, 25 fuzz) - the fuzz
one only on the second attempt, and the first attempt's failures are now *Environment notes*' newest
trap: `test_fuzz.py` rebuilds the core and every live `lasertank-solve.exe` holds the published DLL
open, so running it beside a search reports a red gate that is entirely the machine. `Engine.cs`
still differs from layer 0 by one word and `Engine.Search.cs` is unchanged - six layers now.

**2026-09-06, session 19 - level 1 is solved.** Same opening complaint as sessions 15, 17 and 18 -
*"the solver still doesn't solve level 1"* - and the answer was the thing session 18 wrote down and
left undone: **the read's fourth derivation**, *"after this change the tank can make a board change
it could not make before"*. Level 1 turns three roto-mirrors five times before its first fill; those
five moves open nowhere new to stand, land on no barrier and move no block, and every ranking key
this project has is flat across all of them. What they *do* is put shots on the board that did not
exist a moment ago, and that is a question the engine can be asked without knowing what a mirror is
- the same pose closure plus enumeration layer 6 already runs, asked one ply further out and
compared on board *deltas* rather than on resulting boards.

**Measured as an instrument first** (`--read-enables`), which is what decided where it went. Over
the 20 recordings it takes coverage of the human's next board change from **83.2% to 97.1%**,
naming 111 of the 134 changes nothing else named - but it also names **79% of the successors
offered**, so as a promotion into `TierAdvance` it would be no filter at all. It went in as a tier
of its own between the other three and the rest, where it can reorder only the group they were
silent about. With it off, all 800 rows reproduce byte for byte on the fifteen columns that existed
before it.

**The level-1 detail is the finding, and it is about `opens` as much as about the new one.** On the
first ten board changes barrier/toward name **nothing** (the flag is in a water pocket, so no effect
ever lands on the barrier); `opens` names four of seven successors and misses the human's move seven
times out of ten; the fourth derivation names three of seven and **hits nine of ten**. So when it is
on, `opens` moves behind it - more selective *and* more accurate belongs in front.

**Two failures on the way, and both are session 17's lesson.** The pass was written after
`ReadTier`'s two early returns, so only the `--push-read-opens N>0` path reached it and **it benched
as entirely inert through a whole round of measurements**; `Opens` is now a named method called from
one place. And at full price it cost ~4x an expansion and took the ferry bench 20/50 -> **12/50** -
right shape, wrong price, for the third time in this layer's life. The fix is session 18's own
finding turned round: a tier can only matter at a width the tiers above it have not already filled,
so the pass counts what the depth has promoted so far and returns if that covers the width. On a
ferry level the read fills a width of 8 inside the first parent or two and the pass is skipped; on
level 1, where the free derivations say nothing all level, it is asked every time. 12/50 -> 19/50 on
the gate alone.

**Then the level fell.** `--push-beam 48 --push-read --push-enables 8` solves `LaserTank.lvl` 1
"Boot Camp" in **6.19M nodes and 17.9 seconds**, 294 keys = 157 moves + 47 shots against the record's
103 + 46, **1.97x**, verified through both engines and committed at
`data/solutions/LaserTank/00001.lpb`. The shipped default width of 8 solves it too, in 412 s. Against
four 800M-node runs that did not touch it. `--push-line` says why: the human line now survives to
board change **11** where session 18's configuration lost it at 5 and the pre-session-18 trim lost
it at 1 - past all five rotations and past the mirror shot they set up.

**And a second level, with the control run to prove it is two and not four.** Over `LaserTank.lvl`
1-19 at 60M nodes, with and without: the derivation solves 1 and **4 (The River Nile, 49.9M)** that
the same budget cannot solve without it, while 3, 7 and 11 come out either way. Four of nineteen
against three, **4/4 verified**, and the honest headline is *two levels*.

**What it costs, which is why it is off by default.** Ferry bench **19/50** against 20/50 and deep
**19/50** against 21/50, both at 4M nodes - and with `--push-enables 0` both benches reproduce
session 18's numbers exactly on the final build. `--push-enables-poses` (default 32) caps the child
closure it looks from; truncating costs promotions and cannot invent them, which is what makes a
small default safe - it holds the
level-1 line only to board change 4 where the whole closure holds it to 11, and the level solves at
either, because a search does not have to follow the human's line to win.

**And then Michal made the objection that mattered** - *"we have to somehow incorporate this into
the autosolver - the user won't know to fine tune random parameters for specific levels"* - which is
right, and which the first answer got wrong. Switching the flag on inside the existing push rung
from round 2 makes a rung tuned to its best measured setting worse on an argument with nothing
measured behind it. The driver is a *portfolio*, so the answer is its own rung, `push-enables`, six
searchers instead of five - and the moment it is a portfolio member the bench numbers have to be
read as a **union** rather than solo. Against the plain push rung's own solved set at 4M nodes, the
configuration that scores 19/50 and 19/50 alone **adds four ferry levels and five deep ones**;
at width 48 it adds one and five, and all three together are 24 and 28. **A solo score is the wrong
statistic for a portfolio member**, and every table in this session's notes above that point is
written in it. The rung therefore runs at the default width while the rounds are cheap and widens to
48 from round 3, which is where level 1 lives. The test is the one a user would actually run:
`build/lasertank-solve.exe data/levels/LaserTank.lvl --from 1 --to 1`, no flags, **solved in 67
seconds at round 3**, verified through both engines and banked to `data/solutions/` unasked.

All four fidelity gates green (187 replayed / 181 win / 6 documented, 29 difftrace, 2,347 sweep
identical, 25 fuzz). `Analyze.cs`, `Push.cs`, `Search.cs`, `Program.cs` and `Auto.cs` only -
`Engine.cs` still differs from layer 0 by one word and `Engine.Search.cs` is unchanged.

**2026-09-06, session 20 - level 2 is solved, and one old bug was scoring a destroyed board as
perfect.** Opening complaint, the sequel to four sessions of the same about level 1: *"the solver is
still stuck on level 2 - from a human perspective this level is trivial, I instantly see that I have
to shoot the boxes to block the conveyor belt."* Michal also named the design out loud - *"you can
solve the levels by game state changes ... the solution is basically a series of state changes with
walking in between"* - which is layer 5 exactly, and the useful thing about hearing it back is that
it puts the question where it belongs: not *is the action set right* but *why is this action set
blind here*. Three answers, all in the new **layer 7** section above.

The short version. A **laser** ferry cost the beam a depth per cell where a **drive** ferry costs
one for the whole run, so level 2's 32 board changes - 26 of them a repeat of the shot before - were
a 32-deep breadth-first search on a key that moves 13 -> 11 all level (`--push-shot-run`). No
ranking key in the project moves when a block gets nearer the cell that would stop a *ride*, which
is what `RouteFerry` does for water and `Heuristic.RouteStop` now does for a conveyor
(`--push-stop`). And `--push-line`'s report was depth-indexed, so under run compression it called a
line it was following perfectly `STALE`; it now tracks how far along the line the frontier is.

**The part worth remembering is how the term was got right, because it was wrong five times and the
beam found every one.** Price the whole route -> it *rose* along the winning line, 13 -> 36 -> 107.
Manhattan to the nearest block -> the beam shoved one up column 15 to a cell it could never be
pushed out of. A chain branch with nowhere to arrive from returning "free" -> the beam parked on a
board with two blocks stuck in row 2 and every way in priced at nothing. One block paying for two
requirements -> it parked on the *dead* ordering, (13,2) before (13,1), which walls off column 13.
`Passable` standing in for "the tank can stop here" -> a block on (14,1) priced as one push from
(13,1) when the only square behind it is a conveyor. Each was found by `--push-trace-board`, a new
instrument that prints the board the beam has settled on: `best=10` says a key has gone flat and
says nothing about *what* the beam is looking at.

**And a bug nobody put there this session.** `WorkDistance`/`FlagDistance` returned **0** - the best
score there is - when no flag is on the board, "nothing to steer by". A flag leaves PF only because
something was pushed onto it. With a shot run a block goes up column 14 in one run and lands on the
flag, and at width 128 all 128 boards in the frontier were that board, scoring 4 against the winning
line's 11. Fixed to `Unreachable`. It costs **one ferry-bench level** and that is attributed rather
than assumed: reverting it alone reproduces the banked 20/50 exactly, so everything else added this
session is inert at default flags, and the level lost (`Beginner-I` 1581) never buries a flag on its
own line. The ferry number to compare against from now on is **19/50**; the deep bench is unmoved at
21/50.

**Level 2 falls in 2.03M nodes and 44 s**, and the ablation says all three ingredients are load
bearing - without the shot run, without the stop term, at width 8 or at width 64, it is unsolved at
**60M**. Solo the configuration is 18/50 on both benches against 19 and 21, and as a portfolio
member it adds **3 ferry and 5 deep** levels, so it went in the way session 19's derivation did: its
own rung. `build/lasertank-solve.exe data/levels/LaserTank.lvl --from 2 --to 2` solves it at round 2
in **65 s with no flags**, 90 keys against the record's 72 (**1.2x**, better than level 1's 1.97x),
verified through both engines and banked at `data/solutions/LaserTank/00002.lpb`. Level 1 is
unchanged at 72 s. The driver also stopped lying about which rung won: three rungs now report
`push`, so the result line names the *rung*, and level 2 reads `push-stop`.

All four fidelity gates green (187 replayed / 181 win / 6 documented, 29 difftrace, 2,347 sweep
identical, 25 fuzz). `Heuristic.cs`, `Push.cs`, `Line.cs`, `Search.cs`, `Program.cs` and `Auto.cs`
only - `Engine.cs` still differs from layer 0 by one word and `Engine.Search.cs` is unchanged.
Seven layers now, and the one thing layer 7 has *not* had is a corpus pass: two benches and one
ablation is all that is behind it.

**2026-09-06, session 21 - a post-solve pass that re-derives the route instead of deleting from
it.** Two complaints, one machine. *"In the first part the tank moves the C4 block to C6 and later
back to C4 to move it correctly, which is a waste - I'm thinking there could be a step like 'now
that we know where the blocks are going to end up, what's the least-move way to get them there?'"*
and, on level 7, *"walk/ride circle, shoot mirror half into position, walk/ride circle again, shoot
mirror into position; could be just one circle."* The whole reasoning is in the new **Phase 4
addendum - replanning a solution through the board changes it already made**; this is the short
version.

**The idea is Michal's own from last session** - *"the solution is basically a series of state
changes with walking in between"*. If that is what a solution is, the playfields it stood on are a
ladder of positions **already proved to lead to a win**, and the least-move question has a sound and
cheap answer: find the shortest keystream that climbs the ladder, free to **skip rungs**. Level 3's
excursion disappears because with the first block on B7 the replan pushes it straight into the water
and lands on the playfield the original only reached five board changes later. Level 7's second lap
disappears because a shot run fires four times from the one square it can be fired from. **80 -> 57
and 81 -> 65**, and the level-3 replay now reads the way a person would play it.

Cheap because the ladder is a DAG - one forward sweep, one PF-preserving closure per rung shared by
every state at it, no heuristic anywhere. Level 3 costs 25,014 `ApplyKey` calls.

**The bug worth remembering is that the space bar does not belong in a movement closure.** A shot
that hits nothing still moves the laser record, so its `StateHash` differs from the pose it was
fired from; a walk that treats a new hash as a new place to stand fires from that, and again, and is
no longer bounded by the pose count. The first build spent its whole pose budget at rung 0 of level
3 - an island of twenty-four cells - and found nothing on any level. `Solver.ExpandPush` had the
answer already and had said so in a comment: walk on movement keys, then fire once from each pose
the walk found.

**And an ordering that had to be measured rather than argued.** Replan-before-polish was a net win
over the 416 solutions in `build/solutions/l0` and still **lost keys on two collections**, because a
re-derived route is a different starting point for delta debugging. The shipped pipeline
(`Program.Clean`, shared by `SolveOne` and `--polish`) is polish, replan, polish, keep the replan
only if it is shorter: 11,060 -> 10,327 keys for the polish alone, **-> 10,249** with the replan,
no collection worse, 416/416 verified through both engines. l0 is layer 0's raw beam, which is the
*unfavourable* population - on the seven hand-supervised `data/solutions/LaserTank/` recordings the
same pass is 821 -> 725.

**One old bug fell out of it.** `00007.lpb` was banked at 81 keys with polishing on, and a second
`--polish` over the file took it to 68 by cutting one 13-key round trip. `Decycle` cuts one round
trip per round, gives up after sixteen, `Polish` called it once, and the sweep after it tops out at
twelve keys - so on a raw solution with more than sixteen round trips the long ones nobody reached
stayed in. `Polish` now repeats while anything comes out. No change on l0 (already polished once),
8% cost there.

`Replan.cs` is new; `Trim.cs`, `Program.cs` and `Report.cs` changed. **Nothing in `LaserTank.Core`
was touched** - `Engine.cs` still differs from layer 0 by one word, `Engine.Search.cs` is unchanged
- and the engine gates confirm it (187 replayed / 181 win / 6 documented, 29 difftrace). The gate
that matters for this session is `verify_solutions.py`: **416/416 and 7/7**, both engines, every
tick.

*Left for the next session:* `build/lasertank-solve.exe` was **not** rebuilt - a solver run was
holding the DLL - so the new binary is at `build/rp/lasertank-solve.exe` and `bash src/build.sh`
still has to be run. And the corpus refresh in *Start here* is now worth more than it was: every
banked `.lpb` predates this pass.

**2026-09-07, session 21 — the driver works several levels at once.** `--lanes N`. The premise it
started from was wrong in a useful way: *does a solve use one core?* A searcher does, but the
driver has run its whole ladder in parallel since the interactive addendum, so a level already had
seven. What was serial was the *level loop* — and with seven rungs on sixteen cores that left nine
cores idle, which is where the lanes came from.

The policy is that lanes share one pool of `--jobs` slots rather than each claiming the ladder, so
the core budget does not move when `--lanes` does; the reasoning is in *Phase 4 addendum — the
interactive driver*, along with the two latent races that had to be closed first (the gate's
staging directory, which it empties, and `Sweep` deleting other runs' candidates). Default is 1 and
the single-lane output is unchanged line for line, which was the point of the default.

Measured, not assumed: `Beginner-I` 1-8 at `--lanes 4 --jobs 12` is **8 solved, 8 verified through
both engines, 4 s**. The display was checked by forcing the ANSI path on with a temporary patch to
`Ansi.On` and reading the escapes back with `cat -v` — patch reverted, `Report.cs` byte-clean — and
it caught the one real bug in the session: the footer claimed **14 searchers busy on 9 slots**,
because a rung queued on the semaphore and a rung holding one are both "not completed". Lanes now
report the rungs actually holding a slot and count the rest as `+N` queued, which is also the more
useful line to read, since a lane showing two searchers is the machine being full rather than the
level being nearly done.

`Auto.cs` and `Program.cs` only. **Nothing in `LaserTank.Core` was touched** and the engine gate
confirms it: 187 replayed / 181 win / 6 documented non-winners / 0 unexpected. Last session's
*left for the next session* is also cleared — `bash src/build.sh` ran clean, so `build/` is current
and `build/rp/` is stale and can go.
