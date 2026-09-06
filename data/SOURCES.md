# Data provenance

Everything under `data/` came from laser-tank.com **except two directories that were produced
here** — `demos/` (playthroughs recorded by hand) and `solutions/` (what the solver banked). Those
two are the corpus we made; the rest is the corpus we downloaded, recorded here so it can be
refreshed or re-verified without guesswork.

> laser-tank.com sits behind Cloudflare. `WebFetch` gets a 403 — use `curl` with a browser
> User-Agent. The site is a frameset; real content lives in `menu.html`, `help.html`, `levels.html`.

## `levels/` — 13 collections, 20,914 levels

| Collection | Levels | Collection | Levels |
|---|---:|---|---:|
| Beginner-I | 2000 | Gary-I | 2000 |
| Beginner-II | 1376 | Gary-II | 322 |
| Challenge-I | 2000 | LaserTank (flagship) | 2030 |
| Challenge-II | 2000 | Sokoban-I | 2000 |
| Challenge-III | 2000 | Sokoban-II | 1740 |
| Challenge-IV | 2000 | Special-I | 521 |
| Challenge-V | 925 | | |

Every collection has a matching `.ghs` with an entry for **every** level, so all 20,914 are
known-solvable with a best-known (moves, shots) target.

Distributed as `Updates.zip` ("All Levels, GHS & Reports in one zip"), fetched by
`original/bin/LTUpdate.exe` (LTUDU 6.2.1, actively maintained — a third-party tool by Donald Drouin,
the site's data administrator, not part of the game). This snapshot: **2026-04-11**.

The zip was verified byte-identical to these extracted files before being discarded; its three
index text files are in `meta/`.

## `quirks/` — 10 packs, 317 levels, 187 `.lpb` playbacks

From the **Help** section, `https://laser-tank.com/_help/<name>.zip` — re-downloadable with a
browser User-Agent (see the Cloudflare note above). The pristine zips were verified byte-identical
to these extracted files and then discarded.

| Directory | Zip | Levels | LPB | Why it matters |
|---|---|---:|---:|---|
| `tutor` | `Tutor.zip` | 92 | 0 | **The quirk specification.** Each level's hint documents its trick. Readme: *"Some of the tricks are bugs that have been intentionally left in the software because they make the game more interesting."* |
| `tutor-with-playbacks` | `Tutor-with-Playbacks.zip` | 112 | 112 | Full solutions **plus a bundled `.ghs`** — the only pack where recorded move/shot counts can be checked against a target |
| `rotary-mirrors` | `Rotary Mirrors-Challenge.zip` | 39 | 39 | Rotating-mirror edge cases (38 shipped as `.lpb`, one as packed text — see below) |
| `tricks` | `Tricks.zip` | 26 | 0 | Tricks used in the main collections |
| `pono-trick` | `Pono_trick.zip` | 18 | 20 | (more LPBs than levels — alternate solutions) |
| `game-objects` | `Game-Objects-in-LT.zip` | 16 | 16 | One level per game object; best first target for the oracle |
| `4triang` | `4triang.zip` | 5 | 0 | |
| `telek-1` | `telek-1.zip` | 5 | 0 | |
| `l40` | `l40.zip` | 3 | 0 | |
| `inchworm` | `inchworm.zip` | 1 | 0 | |

### The one derived file: `rotary-mirrors/Rotary Mirrors-Challenge_0021.lpb`

The only *downloaded* file that did not arrive as bytes from upstream — `demos/` and `solutions/`
below were not downloaded at all. The pack ships level 21's playback as
`Rotary Mirrors-Challenge_0021.txt` — a base64 wrapper produced by LaserTank's
`Text-Converter.exe`, presumably to survive mail or a forum post. Regenerate it with:

```
python tools/unpack_lpb_txt.py "data/quirks/rotary-mirrors/Rotary Mirrors-Challenge_0021.txt"
```

594 bytes, matching the `Size : 594` the wrapper declares; level 21, 528 keys, author `Ihab`. The
`.txt` is kept alongside it, so this is reproducible and reversible. Note the recording does *not*
win — its last two keys turn the tank around into water one cell from the flag. It earns its place
anyway: level 21's hint documents a solution at *"148/257 or better"*, and the replay reaches
exactly 148 moves / 257 shots, which makes it one of the few independent checks on the oracle's
absolute scoring. See `PROGRESS.md`.

Upstream deliberately withholds `.lpb` files for the main collections — *"The real goal of LaserTank
is not to watch playback files."* These help-section packs are the exception, and are the only
recorded human solutions *available for download*. The `.ghs` targets stand in for the rest — which
is why `demos/` exists.

## `demos/` — 21 `.lpb`, recorded here by hand

**Not from upstream.** These are hand playthroughs recorded in this project: 20 in `LaserTank/`
(levels 1-11, 13, 14, 17-20, 23, 24, 28) and 1 in `Challenge-I/`, all added in one commit, *"some
manual playthroughs"*. They exist because upstream ships no `.lpb` for the main collections, so a
*human* line through a hard level was otherwise unobtainable — and a human line is what says whether
a level is reachable by search at all (`--profile`, `tools/basin.py`, `--push-line`; see
`PROGRESS.md`).

Deliberately **not** under `solutions/`, so a hand solution is never mistaken for a solver one, and
deliberately not regenerable: playing a level by hand is the one input to this repo that no script
can reproduce. Treat them as source data, not as output.

They are held to the same gate as solver output, and re-checking them costs seconds:

```
python tools/verify_solutions.py data/demos
```

**21/21 verified** — both engines win, byte-identical traces, median 1.7x the `.ghs` record, 4 of
the 21 matching the record exactly.

## `solutions/` — what the solver banked

**Not from upstream either — this one is output.** `build/lasertank-solve.exe FILE.lvl` (the
interactive driver) writes `solutions/<collection>/NNNNN.lpb` here, and only after
`tools/verify_solutions.py` has replayed the candidate through both engines.

It is committed while campaign output is not, because the two are different things: a campaign is
thousands of disposable files regenerated by `tools/campaign.sh` (gitignored `build/`), whereas the
driver produces one hand-supervised level at a time, on levels the batch solver could not do.
Verify the same way:

```
python tools/verify_solutions.py data/solutions
```

## `graphics/`

`Eye_Saver+Grid.ltg`, `Lasertank_Comix.ltg`, `Warcraft_II.ltg` — user graphics packs, verified
byte-identical to the copies frozen in `original/src/Setups/Files/`. Format at `LTANK2.C:688`.

## `meta/`

- `*_Report.html` — per-collection changelogs (which levels changed in each update)
- `LVL_Name.txt` (1.1 MB) — every level name across all collections
- `All_Names.txt`, `GHS_Authors.txt` — author indexes
- `ReadAllUp.txt` — the update archive's readme

## `levels/LaserTank-2016-snapshot.zip`

A 2016 snapshot of `LaserTank.lvl` + `.ghs`, made by LTUDU before an update. **Not a duplicate** —
it is a third distinct vintage of the flagship collection: 59 of its 2030 levels differ from the
current 2026 copy, and it differs from the 2007 copy in `original/src/Setups/Files/` as well.

Kept because level content drifts. A `.lpb` recorded against one vintage may not replay against
another, so if a recorded solution fails to reach the flag in Phase 1, "the level changed underneath
the recording" is a real candidate — and three datable versions are what let you test that instead
of hunting a phantom engine bug.
