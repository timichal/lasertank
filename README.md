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

## Prerequisites

Developed and verified on **Windows 11**. The build scripts are `bash`, the reference game and the
oracle are Windows binaries, and `tools/` shells out to both — so Windows with Git Bash is the
supported combination.

| What | Install | Needed for |
|---|---|---|
| **Git for Windows** | `winget install Git.Git` | the `bash` both build scripts and `tools/` expect |
| **MinGW-w64** (WinLibs, UCRT; gcc 16.1 verified) | `winget install BrechtSanders.WinLibs.POSIX.UCRT` | `oracle/build.sh` — the C reference oracle |
| **.NET SDK 10** (10.0.400 verified) | `winget install Microsoft.DotNet.SDK.10` | `src/build.sh` — the C# core and solver |
| **Python 3.12+** (3.12.7 and 3.14.7 verified) | either alias works, `python` or `python3` | everything in `tools/` |
| Godot 4.7.2 **.NET/Mono** build | `winget install GodotEngine.GodotEngine.Mono` | Phase 5 — the playable game and its four gates.  Skip it if you only care about the engine and the solver |

Notes that save an afternoon:

- **Neither gcc nor dotnet needs to be on `PATH`.** Both build scripts look them up themselves —
  MinGW under `~/AppData/Local/Microsoft/Winget/Packages/`, the SDK at `C:\Program Files\dotnet` —
  because a fresh `winget install` updates the machine `PATH` only for shells started afterwards.
  `CC` overrides the compiler.
- **`tools/` is stdlib-only** and must stay that way so either Python alias works — so nothing
  above needs a virtualenv or a requirements file. The single exception is `tools/fit_eval.py`
  (weight fitting, a solver instrument and not a gate), which needs `pip install numpy` in
  whichever interpreter you run it with.
- **Use Git Bash, not WSL.** A bare `bash` on Windows resolves to WSL's `System32\bash.exe`, which
  has no gcc and no dotnet and fails with an unreadable `execvpe` error. `tools/engines.py`
  skips it automatically; set `$LT_BASH` if neither guess is right on your machine.
- **The projects target `net8.0`** (the lowest TFM Godot 4.x accepts) with `RollForward=LatestMajor`,
  so the .NET 10 runtime alone is enough — no .NET 8 runtime install needed.

## Setup

Git for Windows sets `core.autocrlf=true` system-wide, and this repo commits LF with no
`.gitattributes` — so turn it off first, and the working tree stays byte-identical to what is
committed. That is the assumption `tools/` patches source files under (in bytes, to preserve it)
and the one behind the line-ending trap in PROGRESS's *Environment notes*:

```bash
git config --global core.autocrlf false        # or --local, after cloning
git clone git@github.com:timichal/lasertank.git
cd lasertank
```

Nothing needs extracting or downloading: the levels, scores and recordings are committed
(≈24 MB, see *Why so much binary data is committed*). Only the binaries are missing — `build/`
and `oracle/build/` are gitignored, so build them:

```bash
bash oracle/build.sh                 # -> oracle/build/oracle.exe
bash src/build.sh                    # -> build/lasertank-{core,solve}.exe
```

Then run the four fidelity gates. All four must be green before any measurement here is worth
believing; about three minutes all in:

```bash
python tools/replay_all.py           # 187 replayed, 181 win, 6 documented non-win
python tools/test_difftrace.py       # 29 passed
python tools/sweep.py                # 2,347/2,347 identical
python tools/test_fuzz.py            # 25 passed  (slow: injects faults and rebuilds the core)
```

`PROGRESS.md` holds the canonical expected counts and an *Environment notes* section with the
traps behind each of them — including why `test_fuzz.py` must never run while a solver process is
alive (Windows keeps `build/LaserTank.Core.dll` locked, and the rebuild fails for reasons that
have nothing to do with the code).

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
JSONL reports — see `tools/campaign.sh` and [`SOLVER.md`](SOLVER.md);
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
