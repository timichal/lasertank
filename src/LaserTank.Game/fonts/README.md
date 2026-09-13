# The two faces the chrome is set in

Shipped rather than named, which is a reversal: until the second pass of the redesign this
interface asked for `Segoe UI` / `Inter` / `SF Pro` by name and let `SystemFont` fall through.
That is the single most recognisable tell of a generated interface -- *system defaults only, no
display pairing* -- and it also made the look non-deterministic: the same window drew differently
on Windows, macOS and a browser export, where `SystemFont` falls through to Godot's own face and
the level list's fixed pitch (which the original's `sprintf` padding depends on) was not fixed at
all.

Two files fix both.

| file | what it sets | licence |
|---|---|---|
| `Archivo.ttf` | the display face: the wordmark and the level name, and nothing else | OFL 1.1, `Archivo-OFL.txt` |
| `PlexMono-Regular.ttf`, `PlexMono-SemiBold.ttf` | **everything else in the interface** | OFL 1.1, `PlexMono-OFL.txt` |

`Archivo.ttf` is the *variable* font -- `wght` 100..900 and `wdth` 62..125 on one file -- so the
weights and the condensed cut the chrome uses come out of `FontVariation` rather than out of eight
more files.

**Why mono for the body.** It is not a stylistic tic: the content *is* fixed-pitch. `LevelList`
draws the original's own `%4d %-30.30s %5d %4s` output and its column rules are placed in glyph
units (see `docs/game/ui.md` -- the two layout facts that cost a debugging session each); the
board is a 16x16 grid labelled A1-P16; the score is two counters against a posted par. An
interface whose every number is a measurement is an instrument, and setting it in a proportional
UI sans was the part that made it read as a dashboard with a game in the middle.
