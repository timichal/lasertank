#!/usr/bin/env bash
# Build the headless LaserTank oracle.
#
#   oracle/stub/     shadows <windows.h> etc. so LTANK2.C compiles with no GUI
#   original/src/    LTANK2.C is compiled VERBATIM -- never edit it
#
# -x c            : gcc treats an uppercase .C as C++ otherwise
# -fpermissive    : LTANK2.C:677 assigns char* to LPBITMAPINFO in a graphics
#                   loader we never call; lcc-win32 allowed it, gcc 14+ does not
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(dirname "$here")
out="$here/build"

: "${CC:=gcc}"
if ! command -v "$CC" >/dev/null; then
  winlibs="$HOME/AppData/Local/Microsoft/Winget/Packages/BrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe/mingw64/bin"
  [ -d "$winlibs" ] && export PATH="$winlibs:$PATH"
fi

mkdir -p "$out"

CFLAGS="-std=gnu99 -O2 -g -fpermissive -Wall
        -Wno-unused-variable -Wno-unused-but-set-variable -Wno-parentheses
        -Wno-missing-braces -Wno-pointer-sign -Wno-int-conversion
        -I$here/stub -I$root/original/src -I$here"

# shellcheck disable=SC2086
"$CC" -x c $CFLAGS -c "$root/original/src/LTANK2.C" -o "$out/ltank2.o"
# shellcheck disable=SC2086
"$CC" -x c $CFLAGS -c "$here/win32_stub.c"          -o "$out/win32_stub.o"
# shellcheck disable=SC2086
"$CC" -x c $CFLAGS -c "$here/driver.c"              -o "$out/driver.o"

"$CC" "$out/ltank2.o" "$out/win32_stub.o" "$out/driver.o" -o "$out/oracle.exe"
echo "built $out/oracle.exe"
