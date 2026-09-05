#!/usr/bin/env bash
# Build the C# core and its headless driver.
#
#   src/LaserTank.Core/   the transliteration -- pure C#, no Godot
#   src/LaserTank.Cli/    the oracle-compatible driver -> build/lasertank-core.exe
#
# The .NET SDK is installed machine-wide but is not on this shell's PATH (the
# winget install updated the machine PATH after the shell started), so find it
# the same way oracle/build.sh finds MinGW.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(dirname "$here")
out="$root/build"

if ! command -v dotnet >/dev/null; then
  for d in "/c/Program Files/dotnet" "$LOCALAPPDATA/Microsoft/dotnet" "$HOME/.dotnet"; do
    [ -x "$d/dotnet.exe" ] && export PATH="$d:$PATH" && break
  done
fi
command -v dotnet >/dev/null || { echo "dotnet not found; install Microsoft.DotNet.SDK.10" >&2; exit 1; }

dotnet publish "$here/LaserTank.Cli/LaserTank.Cli.csproj" \
  -c Release -o "$out" --nologo "$@"

echo "built $out/lasertank-core.exe"
