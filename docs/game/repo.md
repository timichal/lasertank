# The tree, the tools, and the machine

Where everything lives, what each gate proves in one line, and the environment traps that have each
cost a debugging session. The solver's own tools are in
[`docs/solver/instruments.md`](../solver/instruments.md); what the gates *mean* and how to debug
with them is in [`harnesses.md`](harnesses.md).

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
              deliberately NOT under solutions/ so a hand solution is never
              mistaken for a solver one
  solutions/  what the interactive driver banks — one hand-supervised level at a
              time, each already through the two-engine gate.  A missing .lpb
              means a level has not been re-run, not that it is unsolved
  graphics/   .ltg packs      meta/  changelogs & name indexes
  language/   the ten translations as keyed UTF-8 JSON, named by ISO code
              (en fr de nl pt es sv hr zh-Hans zh-Hant), converted once from
              original/src/Setups/*/Language/Language.dat — whose directory
              names are NOT those codes; convert_language.LANGUAGES pairs them
oracle/     the C reference oracle — see oracle/README.md
  stub/       minimal <windows.h> that shadows the real one
  win32_stub.c  real memory/files/messages, no-op GDI
  driver.c    LTANK.C globals + window proc + the WM_TIMER tick loop + tracing,
              plus the keystream / script / edit-token feeds
  build.sh    gcc -x c -I stub -I original/src
src/        the C# port         build.sh -> build/lasertank-{core,solve}.exe
  LaserTank.Core/   Objects.cs GameState.cs LevelFile.cs Engine.cs  (no Godot here)
                    Engine.Search.cs — snapshot/restore, ApplyKey, StateHash
                    Engine.Sound.cs  — SoundPlay's body: SoundLog?.Add, nothing else
                    LevelFile.cs also carries LevelRecord — the raw 576 bytes and
                      the editor's own write widths (hazards #16-#18)
                    not reachable from Tick(), and that is the test:
                      GraphicsFile.cs  .ltg + BMP readers, BMSTA/ColorList
                      SoundFile.cs     the .wav reader and lt_sfx.c's id->name table
                      Editor.cs        ChangeGO, the Shifts, Clear Field
                      Language.cs      the UI strings + both menu trees.  The one
                                       file here that is NOT a transliteration
  LaserTank.Cli/    Program.cs TraceWriter.cs — the oracle's CLI, the oracle's trace
                    EditDriver.cs — `--edit`;  LangDump.cs — `--lang-dump`/`--lang-list`
  LaserTank.Solver/ the batch solver and the interactive driver — see SOLVER.md
  LaserTank.Game/   the Godot 4.7 project
                    BoardView.cs   draws Game.BMF, routes keys, measures the
                                   layout, owns the chrome
                    Ui.cs          the redesign's palette, type and box styles —
                                   the one file here that answers to nothing in
                                   the original
                    Hits.cs        the chrome's clickable rectangles for one
                                   frame: registered by _Draw, tested by the
                                   mouse, named so a gate can drive them
                    Session.cs     LTANK.C's driver half — WM_TIMER, WM_KEYDOWN,
                                   WM_Dead, ReStart, WM_SaveRec, commands 110/111/
                                   112/114/124
                    PlayMode.cs    that driver with a scripted player
                    Atlas.cs       hands the sheet to the renderer
                    Options.cs     LaserTank.ini;  Packs.cs  GFXInit's three modes
                    GraphicsMenu.cs  GraphBox (226);  LanguageMenu.cs  Ctrl+L (ours)
                    LevelList.cs   LoadBox/HSList/GHSList — one class, three modes
                    CollectionList.cs  command 108's picker;  CollectionCheck.cs
                                   its headless dump
                    HighScores.cs  AssignHSFile + CheckHighScore + the HS global
                    Recorder.cs    command 123 and PBWindow
                    Sfx.cs         lt_sfx.c — one player, monophonic
                    EditMode.cs    commands 201/601/603/604/605/701-705/710-713
                                   and the palette
                    Step4Check.cs, Step6Check.cs  the headless dumps list_check.py
                                   and lang_check.py compare against
                    Paths.cs       finds data/
                    Built by Godot or `dotnet build`, never published into build/
build/      C# output (gitignored)      LaserTank.slnx  the solution
tools/      the fidelity and presentation gates; solver-only tools: docs/solver/instruments.md
docs/       the detail behind the two entry points -- game/ is PROGRESS.md's, one file
            per subject; solver/ is SOLVER.md's
```

| tool | what it proves |
|---|---|
| `difftrace.py` | two traces, or two directories of them: first diverging tick, first field, per-cell playfield diff. **The gate everything else is built on** |
| `test_difftrace.py` | self-test for the differ — run it before trusting a verdict |
| `replay_all.py` | replay every `.lpb`; expected outcomes + `.ghs` targets. `--traces DIR [--field] [--bmf]` writes one trace per recording |
| `engines.py` | both engines on one input + compare → a `Div` or `None`. Shared plumbing; also finds `bash`, Godot and `$LT_CORE`, and **builds the Godot C# first** |
| `sweep.py` | one fixed keystream over every level of a `.lvl`, both engines. Bare, the empty-keystream sweep: 2,347 levels |
| `fuzz.py` / `test_fuzz.py` | random keystreams, both engines, and **shrink** a divergence to level + shortest keystream / the self-test that injects known faults |
| `verify_solutions.py` | replay every `.lpb` through both engines: WIN on each, byte-identical traces, ratio to the `.ghs` record. `--levels` names the `.lvl` instead of finding it by directory name |
| `atlas_check.py` | every `BMF`/`BMF2` value over the corpus lands inside the 10×6 grid; every pack decodes to the same 320×192 pixels in Python and in C# |
| `tick_check.py` | every recorded `.lpb` through Godot's own input path and tick agrees with the oracle on result/ticks/moves/shots, re-records the same keystream byte for byte, and its own `.lpb` replays in the oracle. Plus `--tick-rate`: the real driver, timed, must be 20 ticks/second |
| `options_check.py` | the INI's semantics and round trip; every pack loading with mode 1 == mode 2 pixel for pixel; the three board sizes; and the laser bar measured out of a `--shot` PNG **in the cell the engine names** |
| `sound_check.py` | every recorded `.lpb` through both engines with `--sound`, so the per-tick `SoundPlay` id stream must be identical too; the sixteen WAVs decoding to the same PCM in Python and C#; `[OPT] Sound`'s semantics |
| `undo_check.py` | Undo and Save/Restore Position, which no keystream can reach, through `--script` and trace-diffed against the oracle's own `UndoStep`. Shrinks token-wise. `--replay LEVEL SCRIPT` re-checks one with `--field` |
| `list_check.py` | the three list dialogs' rows and the `.hs` writer's bytes, rebuilt in Python against `--check-lists` / `--check-scores`. Neither output is engine behaviour, so neither can go through the oracle — this is the sprite-sheet pattern instead. Plus `win`: level 1's own recorded solution replayed with a live `--ini`, to check the flag case posts a score |
| `roundtrip_check.py` | per undo-carrying script, six runs — the script through both engines and through Godot's command path, then the `.lpb` it records through both engines and through Godot's *playback* path |
| `mouse_check.py` | `MouseOperation` through `--script`'s click tokens, trace-diffed against the oracle's own copy |
| `editor_check.py` | 3,000 edit scripts against the oracle's own `ChangeGO`; the `.lvl` writer (an untouched level re-saves byte for byte across all 23 collections, the `GetWindowText` widths rebuilt in Python, the gap zero-filled, the saved board tied to the trace, and **the oracle — which *is* the 2010 loader — opening what was written**); and the game's own editor saving the same bytes as the driver |
| `lang_check.py` | the tab policy; 2,293 source lines rebuilt out of the JSON and compared **as bytes in each file's own codepage**; the key set and both menu trees against the frozen header and `.inc`; `code`/`name`/`sourceDir`/`sourceEncoding` against `convert_language.LANGUAGES` and `sourceName` against the `.dat`'s own banner line (the one string the round trip cannot reach — it sits on a `#` line the original's loader skips); `--lang-dump` and the game's `--check-lang` against a Python rebuild; a synthetic partial language for the fallback; 5 INI checks |
| `collections_check.py` | the collection picker's 23 rows, rebuilt in Python from the same two directory trees against `--check-collections`; and the switch behind them — all 23 opened in order through one Session, with the `.hs` / `.ghs` / `.lpb` names checked to have followed each one (`AssignHSFile`), the level checked to be 1, an open playback checked to be closed by the change, and a `.lvl` that is not there checked to restore rather than throw |
| `chrome_check.py` | the third arm of the window proc. Dumps every clickable rectangle of twelve screens (`--dump-hits`) and checks three things a screenshot cannot show: that no target is outside its window or under something drawn later (Hits.Click rewritten in Python), that none is under the finger-sized floor `Ui.Touch` puts them at, and that **clicking a target called `key:U` leaves the game in the same state as pressing U** — `--click` against `--press`, two processes, one comparison. Needs a real window and says so |
| `convert_language.py` | the one-time import behind that. `--check` reports staleness without writing |
| `bump_rate.py` | classify consumed keys; bumps = desync signature |
| `dump_level.py` | print a `.lvl` level as ASCII with its hint |
| `unpack_lpb_txt.py` | decode a Text-Converter `.txt` wrapper back to `.lpb` |

Everything in `tools/` is stdlib-only. See `README.md` and `data/SOURCES.md` for provenance.

---

## Environment notes

- **Two Pythons, both fine:** `python` = 3.12.7 (miniforge), `python3` = 3.14.7. Everything in
  `tools/` is stdlib-only and verified on both — keep it that way. **One exception:
  `tools/fit_eval.py` imports `numpy`**, which is installed for `python` only; it is a solver
  instrument, not a gate. A second carve-out should be argued for rather than assumed.
- **C toolchain:** MinGW-w64 (WinLibs gcc 16.1, UCRT),
  `winget install BrechtSanders.WinLibs.POSIX.UCRT`. Not on `PATH` globally; `oracle/build.sh` finds
  it under `~/AppData/Local/Microsoft/Winget/Packages/`.
- **.NET SDK 10.0.400**, `winget install Microsoft.DotNet.SDK.10`, at `C:\Program Files\dotnet`. Not
  on the shell's `PATH` until it restarts, so `src/build.sh` finds it. Only the .NET 10 runtime is
  present, hence `RollForward=LatestMajor` on the `net8.0` CLI.
- **Godot 4.7.2 (.NET/Mono build)**, `winget install GodotEngine.GodotEngine.Mono`, under
  `~/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_*/`. The `godot` alias
  needs admin, so call the `.exe` by path — `..._console.exe` if you want stdout. `$LT_GODOT`
  overrides. A fresh checkout needs one
  `"$GODOT" --headless --path src/LaserTank.Game --import` before `--path` will run the project, and
  `Godot.NET.Sdk` restores from nuget.org on the first build (the install also ships it under
  `GodotSharp/Tools/nupkgs/` if the machine is offline).
- **`godot --path` makes the *project* the working directory**, so every relative path handed to the
  game resolves against `src/LaserTank.Game/`, not the shell's cwd. Worse than a wrong answer:
  `_Ready` throws, Godot logs the exception and **keeps the window open**, so the run hangs instead
  of failing. Pass absolute paths, and read a hung `--shot` as a path error until proven otherwise.
- **`godot --path` does not build C#, and says nothing about it.** It loads whatever assembly is
  already in `src/LaserTank.Game/.godot/mono/temp/bin/`, so editing `Session.cs` and running the
  project — or a gate — silently exercises the *previous* build. Only the editor builds on run.
  Verified the ugly way: a changed string on disk did not appear in the output while the gate still
  reported a green 208/208. Every Godot-backed gate calls `engines.build_godot_game()` first, and
  anything new that runs the project must do the same. By hand:
  `dotnet build src/LaserTank.Game/LaserTank.Game.csproj`. It builds into `.godot/` and
  `src/LaserTank.Core/bin/`, never `build/`, so it is safe beside a live solve.
- **A running solver blocks `src/build.sh`, but not the compilers.** `dotnet publish -o build` cannot
  replace `build/LaserTank.Core.dll` while a `lasertank-solve.exe` holds it open. Build each project
  into its own `bin/` instead and use **`$LT_CORE`** (overrides `engines.CORE`) or
  `replay_all.py --engine`. `LT_SOLVE=<exe>` is the solver's half, for `tools/bench.sh`,
  `campaign.sh` and `second_pass.sh`. The Godot project never publishes into `build/` at all.
- **Never run `test_fuzz.py` while a solver process is alive.** It rebuilds the core, Windows keeps
  `build/LaserTank.Core.dll` locked open by every running `lasertank-solve.exe`, and the rebuild
  loses its retry ladder — so *both* the "clean core builds" control and the restore check report
  `FAIL`, a red gate that is entirely the machine. The tell is
  `MSB3027 ... The file is locked by: "lasertank-solve (NNNNN)"`. The tree is still left byte-clean,
  so the fix is to wait and re-run.
- **Trap in the oracle's own usage text:** it advertises `--keys` as accepting "raw decimal VK codes
  separated by commas", but `driver.c` only parses the characters `u d l r f` and silently skips
  everything else. `--keys 38,38,32` therefore yields an *empty* keystream and an idle run that looks
  like it worked. The C# CLI matches this deliberately — if you fix one, fix both and re-run the
  corpus.
- **`bash` invoked from Python is WSL's `System32\bash.exe`**, not Git Bash — different filesystem,
  no gcc, no dotnet, and it fails with an unreadable `execvpe` error. `engines.find_bash()` skips
  System32 and falls back to the Git for Windows paths; `$LT_BASH` overrides. Any new tool that
  shells out should use it rather than bare `bash`.
- **Rewriting a source file from Python in text mode rewrites every line ending.** `src/` is LF,
  Python's text mode makes it CRLF, and `core.autocrlf=true` then makes `git diff` show *nothing*
  while every line on disk has changed. Patch and restore in **bytes**, or pass `newline=''`.
- **`text=True` on a subprocess decodes with the *locale* codec, and fails silently.** That was fine
  while every output was ASCII; the language dumps are not. On the first byte cp1252 leaves undefined
  the reader thread raises `UnicodeDecodeError`, `subprocess` swallows it in the thread, and
  `p.stdout` comes back **empty** — so four of the ten languages looked like a crashed game rather
  than a decoding bug. `lang_check.run_godot` captures bytes and decodes UTF-8 explicitly. Any new
  gate whose output can carry non-ASCII must do the same.
- **A backslash does not survive `python - <<'EOF'` in this harness**, and a *doubled* backslash
  arrives as a single one even in `cat > f <<'EOF'` — so escaping for the inner language is exactly
  backwards here. Use the editing tools for anything containing a backslash, or write the
  replacement text to a file first and read it in. What does work for a Python patch script that has
  to match C or C# source containing a `\n` escape: `cat > file <<'EOF'`, single backslashes
  throughout, and **raw** string literals — the triple-quoted form, switching to the single-quoted
  one where the text itself contains a double quote. A patch that fails to match a
  plausible-looking string is usually this.
- **The quirk packs mix `.lvl` and `.LVL`**, and the four that ship uppercase are the four biggest
  (`tutor`, `tutor-with-playbacks`, `rotary-mirrors`, `game-objects`). A `glob("*.lvl")` is
  case-insensitive on Windows and silently drops them on Linux — it already cost one campaign four
  packs with no warning. Match on `suffix.lower()`. Same for `.ltg`.
- **laser-tank.com is behind Cloudflare:** `WebFetch` returns 403. Use `curl` with a browser
  User-Agent. The site is a frameset — real content is in `menu.html`, `help.html`, `levels.html`.
- The original build was lcc-win32 (`original/src/_How to compile LTank.txt`, `LTank.prj`). Its
  dependencies are shallow; MinGW/clang work.

