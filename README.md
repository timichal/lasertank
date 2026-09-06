# LaserTank

A port of **LaserTank 4.1.2** (Jim Kindley, 1995–2001; released to the public domain at 3.1)
to Godot, preserving the original game logic exactly — **including its bugs**, because upstream
level packs deliberately exploit them.

**Start here: [`PROGRESS.md`](PROGRESS.md)** — the living plan, phase status, decoded file formats
and quirk catalog.

## Layout

```
original/          The 25-year-old artifact. Frozen — treat as read-only.
  src/               2007 source distribution, verbatim (C, assets, Setups/, lcc/)
  bin/               shipped 2010 lasertank.exe, help file, and the LTUDU data updater
data/              Game content — this is also the test corpus.
  levels/            13 collections: *.lvl + *.ghs  (20,914 levels, all with global high scores)
                     plus LaserTank-2016-snapshot.zip, an older vintage of the flagship set
  quirks/            10 tutorial/trick packs, extracted (317 levels, 187 .lpb playbacks)
  graphics/          .ltg user graphics packs
  meta/              per-collection changelogs, level-name and author indexes
oracle/            Headless C reference build + per-tick tracing  (see oracle/README.md)
src/               The C# port.  LaserTank.Core is the transliterated engine —
                   pure C#, no Godot; LaserTank.Cli is a headless driver that
                   speaks the oracle's command line and writes the oracle's trace
tools/             Replay gate, trace differ, level dumper, .lpb decoder, bump analysis
```

## Building

```
bash oracle/build.sh                 # the C reference oracle (needs MinGW-w64)
bash src/build.sh                    # the C# core           (needs .NET SDK 10)
python tools/replay_all.py           # replay the recorded corpus through the oracle
python tools/test_difftrace.py       # self-test the trace differ
```

## Solving levels

```
build/lasertank-solve.exe data/levels/Beginner-I.lvl --from 12 --to 40
```

Walks the collection in level order and stays on each level until it solves it or you press a
key (`q` quits). Every searcher runs at once, one per core, and the budget quadruples each round,
so there is no timeout to pick. A solution is written only after `tools/verify_solutions.py` has
replayed it through *both* engines — winners land in `data/solutions/<collection>/`, committed
next to the rest of the game content.

For measuring the solver rather than using it — whole-corpus campaigns, per-layer comparisons, the
JSONL reports — see `tools/campaign.sh` and Phase 4 in [`PROGRESS.md`](PROGRESS.md);
`build/lasertank-solve.exe` with no arguments prints every flag.

## Why so much binary data is committed

Deliberate. The `.lvl`/`.ghs` files *are* the regression corpus — 20,914 levels, every one with a
best-known move/shot count, and 187 recorded keystroke playbacks. `original/` is a historical
artifact that must not drift. Total repo ≈ 24 MB, and it barely changes. See the note at the top of
`.gitignore` before adding rules.

## Running the reference game

`original/bin/lasertank.exe` runs standalone on Windows. It looks for level files next to itself,
so point it at `data/levels/` through *Game → Open Data File*; it remembers the path in a
`LaserTank.ini` it writes beside the exe (gitignored, along with the `.hs` score files it creates).

## Provenance

See [`data/SOURCES.md`](data/SOURCES.md) for where every data file came from and how to refresh it.

## Licence

The original game and source were placed in the public domain by Jim Kindley.
Level content is by the LaserTank community, collected via laser-tank.com.
