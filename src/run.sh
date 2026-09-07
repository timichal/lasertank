#!/usr/bin/env bash
# Build and run the playable game -- src/LaserTank.Game, Phase 5's presentation
# layer over LaserTank.Core.
#
#   bash src/run.sh                                   # play
#   bash src/run.sh --levels data/levels/Beginner-I.lvl --level 12
#   bash src/run.sh --shot out/menu.png --menu --pack 3 --zoom 40
#   bash src/run.sh --check-sheets                    # implies --headless
#
# Everything this script does that a bare `godot --path` does not, it does
# because of a trap PROGRESS.md's *Environment notes* records:
#
#   * **`godot --path` does not compile C#** and says nothing about it -- it
#     loads whatever assembly is in .godot/mono/temp/bin/, so a run after an
#     edit silently plays the *previous* build.  Only the editor builds on run,
#     so this builds first (into .godot/ and src/LaserTank.Core/bin/, never
#     build/ -- safe beside a live solve).
#   * **`godot --path` makes the project the working directory**, so a relative
#     path handed to the game resolves against src/LaserTank.Game/.  Worse than
#     a wrong answer: _Ready throws, Godot logs it and keeps the window open, so
#     the run hangs.  Every path-shaped argument is absolutised below, against
#     the directory you invoked this from.
#   * **A fresh checkout needs one `--import`** before --path will run at all.
#
# Neither dotnet nor Godot needs to be on PATH; both are looked up the way
# src/build.sh and tools/atlas_check.py look them up.  $LT_GODOT overrides the
# editor binary; $LT_DATA and $LT_INI are read by the game itself.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(dirname "$here")
game="$here/LaserTank.Game"
invoked=$PWD

# --- the two toolchains ------------------------------------------------------

if ! command -v dotnet >/dev/null; then
  for d in "/c/Program Files/dotnet" "${LOCALAPPDATA:-}/Microsoft/dotnet" "$HOME/.dotnet"; do
    [ -x "$d/dotnet.exe" ] && export PATH="$d:$PATH" && break
  done
fi
command -v dotnet >/dev/null || { echo "dotnet not found; install Microsoft.DotNet.SDK.10" >&2; exit 1; }

# The winget install cannot create the `godot` alias without admin, so the .exe
# is called by path.  Prefer the _console build: it is the one with a stdout.
godot=${LT_GODOT:-}
if [ -z "$godot" ]; then
  for pat in "$HOME/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono"*/*/Godot_*_console.exe \
             "$HOME/AppData/Local/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono"*/*/Godot_*.exe; do
    [ -x "$pat" ] && godot=$pat && break
  done
fi
[ -n "$godot" ] && [ -x "$godot" ] || {
  echo "Godot not found; install GodotEngine.GodotEngine.Mono (the .NET build)" >&2
  echo "or point \$LT_GODOT at the editor binary." >&2
  exit 1
}

# --- arguments ---------------------------------------------------------------
# Engine flags go before `--`, the game's own after it, and a path-shaped
# argument is made absolute on the way past.

engine=(--path "$game")
gargs=()
headless=0

abspath() {   # against $invoked, not against the project
  case "$1" in
    /*|[A-Za-z]:[/\]*|[A-Za-z]:) printf '%s' "$1" ;;                # already absolute
    *) ( cd "$invoked" && printf '%s' "$(pwd -W 2>/dev/null || pwd)/$1" ) ;;
  esac
}

# A value is path-shaped if it exists, or if its parent directory does -- which
# is what makes `--shot out/menu.png` work before out/menu.png is written.
pathish() {
  [ -e "$invoked/$1" ] && return 0
  case "$1" in */*|*\*) d=$(dirname "$invoked/$1"); [ -d "$d" ] && return 0 ;; esac
  return 1
}

while [ $# -gt 0 ]; do
  case "$1" in
    --headless) headless=1 ;;
    --editor|-e) engine+=(--editor) ;;
    --import) engine+=(--import) ;;
    -h|--help)
      sed -n '2,27p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      echo
      echo "Game arguments (BoardView._Ready): --levels FILE --level N --lpb FILE"
      echo "  --pack N|internal|external|NAME.ltg  --zoom 24|32|40  --sound yes|no"
      echo "  --gfx-dir DIR  --ini FILE  --save-options  --menu  --shot PNG"
      echo "  --play  --replay FILE  --tick-rate N"
      echo "  --check-sheets  --check-sounds  --check-options"
      echo "  --check-lists FILE.lvl  --check-scores DIR"
      exit 0 ;;
    --check-*) headless=1; gargs+=("$1") ;;
    *)
      if pathish "$1"; then gargs+=("$(abspath "$1")"); else gargs+=("$1"); fi ;;
  esac
  shift
done

[ "$headless" = 1 ] && engine=(--headless "${engine[@]}")

# --- build, then run ---------------------------------------------------------

dotnet build "$game/LaserTank.Game.csproj" --nologo -v quiet

# A checkout that has never been imported cannot be run by --path; one import
# is enough, and it is cheap enough to leave to the missing-marker check.
if [ ! -f "$game/.godot/global_script_class_cache.cfg" ]; then
  echo "first run: importing the project (once)"
  "$godot" --headless --path "$game" --import >/dev/null 2>&1 || true
fi

cd "$root"          # so LT_DATA-less Paths.Root and any relative arg agree
if [ ${#gargs[@]} -gt 0 ]; then
  exec "$godot" "${engine[@]}" -- "${gargs[@]}"
else
  exec "$godot" "${engine[@]}"
fi
