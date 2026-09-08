# `bench/` — the solver's measurement artefacts

Small files the solver's tuning numbers depend on — the curated level lists that are *inputs* to
`tools/bench.sh` and `tools/second_pass.sh`, and the few measurement *outputs* that are expensive
enough to reproduce that losing them would cost a result. **Committed on purpose.**

They used to live in `build/reports/`, which is gitignored, and a machine move took all of them
with it — along with `chain.jsonl` and every banked campaign solution. Every tuning number in
`SOLVER.md` is quoted against these lists, so a list that only exists on one machine makes the
whole file unreproducible the moment that machine changes. They are a few kilobytes. They belong
in git.

## What goes in here

The test that matters is the one this directory was created for: **would losing this file make a
number in the solver docs unreproducible?** If yes, and it is a few kilobytes, it belongs here. Two
kinds of file pass it.

**Measurement inputs** — the original charter, and still the common case. A file qualifies when all
three are true:

- **It is an input to a measurement**, not the measurement's output.
- **It was curated rather than computed** — somebody chose these levels, and choosing differently
  would move the numbers. `ferry-levels.txt` exists precisely because the two older lists contain
  almost no ferry.
- **It must not change**, or the numbers before and after stop being comparable.

**Measurement outputs that are expensive to reproduce** — the widening, session 29. An output
qualifies when **the run that produced it costs real time and the file is the only record of it**.
The cheapness test is the whole of it: `analyze-corpus.tsv` is all 20,914 levels' read verdicts and
regenerates in ~3 minutes, so it stays in `build/reports/`; `trace10.err` is one 27-minute run at
`--jobs 1` and is the evidence behind this project's diagnosis of `LaserTank.lvl` 10, so it is here.
An output that lands here carries **the command that produced it in its header**, which is the same
rule the level lists follow and the exact thing whose absence lost the level-8 and level-9 routes
(`docs/solver/next-actions.md`, item 4: the flags survived only as directory names, so neither is
reproducible).

| file | kind | what it is |
|---|---|---|
| `bench-levels.txt` | input | 60 `Beginner-I` levels layer 0 failed; GAUNTLET-heavy |
| `deep-levels.txt` | input | 50 `Beginner-I` levels with a `.ghs` total of 40-150 |
| `ferry-levels.txt` | input | 50 levels the chain fails that the read calls FERRY or SOKOBAN |
| `short-record-failures.txt` | input | 687 levels the 494 chain fails whose `.ghs` record is <= 60 — *Next actions* item 7's population |
| `gauntlet-tail.txt` | input | 138 levels the 494 chain fails that `--analyze` calls GAUNTLET with a `.ghs` record of <= 60 — layer 9's population, and the one the fire tier is measured on |
| `seed-weights.txt` | input | layer 4's equivalence check: `WorkDistance` written in `Feat`, in `Eval.Scale` fixed point |
| `goal-tiles.json` | input | *Next actions* item 6: `tile hash → PF` for anything in a blogspot screenshot the game's own sheet cannot draw. Hand input, so nothing re-derives it — but **almost nothing is left in it**, see below |
| `trace10.err` | output | item 8's traced level-10 run: 64 depths, unsolved at 399M nodes, and a barrier on **0 of 63,454** expansions |

`goal-tiles.json` was here for the plainest version of the first reason, and **session 35 took that
reason away from it** — which makes it the most useful entry in this table, because it shows how the
rule is misapplied. The argument was: a start screenshot labels its own 256 tiles, because the corpus
already knows that board, so that half is derivable and stays in gitignored `build/harvest/`; but
nothing labels the states only *play* produces — the tank facing anywhere but up, a pushed anti-tank, a
block sunk in water — so those are hand input, and a human's answer sitting in a gitignored directory
is an answer the project does not have.

Every clause of that is true except the one that matters. Something does label those states: **the
game**. Its graphics are committed at `original/src/Game.BMP` and `Mask.BMP`, and `UpDateSprite`
composites them in thirty lines of `LTANK2.C`, so `tools/sprites.py` derives all 116 of the sprites
that were going to be labelled by eye — 0 unknown tiles over 173 goal boards. **The corollary of
"nothing re-derives a human's answer" is that nothing should ask a human for an answer the repo can
derive**, and the two look identical from here: both end in a committed file under `bench/`. The
distinguishing question is cheap — *what committed input could produce this?* — and it should be asked
before any item is costed in hours of human attention.

So the file stays, holding one now-redundant label, as the place anything genuinely undrawable goes:
so far one tile where a screenshot caught the tank between the mask blit and the sprite blit, which is
a capture artifact and not a game state. `tools/harvest.py tiles` reports what the derivation covers;
`sheet` then `label` is still the loop for whatever it does not.

`seed-weights.txt` is not a level list, and it is here for the third reason above rather than the
second: it is not curated, it is *derived* — but it must not change, because it is the check that
says the learned ranking hook is still only a hook. `--sg-eval learned --eval-weights
bench/seed-weights.txt` must reproduce plain layer 3 exactly. It was a sentence in `SOLVER.md`
until session 27, and a check written as prose is a check nobody runs.

## What does not

- **Campaign reports** (`*.jsonl`, including `chain.jsonl`) — outputs, and they encode *what the
  chain solved at one code version*. A stale one committed here would be read as current and would
  mislead. They stay in `build/reports/`. `chain.jsonl` is the expensive one, and
  `docs/solver/next-actions.md` has the recipe for rebuilding it by unioning the four chain
  reports. **This is the one exclusion the session-29 widening does not touch**, and the reason
  is the failure mode rather
  than the cost: a level list going stale is visible (the numbers rebase), a report going stale is
  invisible (it still parses, and reads as current).
- **Campaign solutions** (`build/solutions/`) — thousands of files, regenerated by
  `tools/campaign.sh`. Genuinely disposable, which is why the driver writes to `data/solutions/`
  instead.

## The three level lists

Regenerated in session 25, after the machine move took the originals. Each file's header carries
the rule that produced it, so the next machine move costs nothing.

They are **reconstructions, not recoveries**: the reports that named the original levels are gone,
so the levels differ and every bench number ever quoted against the old lists rebases. Two
specifics worth knowing before comparing anything:

- `ferry-levels.txt` contains 1581, the one ferry-bench level the solver docs name -- a check that the
  rule is the right shape, not a level chosen by hand. `deep-levels.txt` contains 1488 for the same
  reason.
- `bench-levels.txt` does **not** reproduce the original's GAUNTLET-heavy, ferry-poor character.
  Every candidate rule over this campaign's `Beginner-I` failures comes back FERRY-dominated (30 of
  60). Part of that is the population and part is the read itself -- layer 8's barrier fix moved 164
  rows out of GAUNTLET corpus-wide -- so the old label was a pre-fix read. The levels were not
  hand-picked to match the old description; the rule was recorded and the actual mix written down.
