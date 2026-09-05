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
  quirks/            10 tutorial/trick packs, extracted (317 levels, 186 with .lpb solutions)
  graphics/          .ltg user graphics packs
  meta/              per-collection changelogs, level-name and author indexes
oracle/            Phase 1: headless C reference build + trace tooling  (empty)
tools/             Format parsers, trace differ, fuzzer, solver              (empty)
```

## Why so much binary data is committed

Deliberate. The `.lvl`/`.ghs` files *are* the regression corpus — 20,914 levels, every one with a
best-known move/shot count, and 186 with recorded keystroke solutions. `original/` is a historical
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
