#!/usr/bin/env bash
# 重新生成替身层的空壳并编译服务器，把错误归类打出来(迭代补替身时用)。
#   tools/build.sh            只编译
#   tools/build.sh gen        先重跑生成器再编译
set -u
HERE="$(cd "$(dirname "$0")/.." && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
GAME="$(cd "$ROOT/../.." && pwd)"
cd "$HERE"
if [ "${1:-}" = "gen" ]; then
  dotnet build tools/ShimGen/ShimGen.csproj -c Release -v q -nologo 2>&1 | grep -E " error " | head -20
  dotnet tools/ShimGen/bin/Release/net8.0/ShimGen.dll "$ROOT/bin/Debug/net48/EmpireCraft.dll" "$HERE/Shim/Generated" \
    "$HERE/tools/include.txt" "$HERE/tools/handwritten.txt" \
    "$GAME/worldbox_Data/StreamingAssets/mods/NML" "$GAME/worldbox_Data/StreamingAssets/mods" "$GAME/worldbox_Data/Managed" | head -30
fi
dotnet build EmpireCraft.Server.csproj -c Debug -o obj/out -v q -nologo -clp:NoSummary > obj/build.log 2>&1
TOTAL=$(grep -c " error " obj/build.log)
echo "errors: $TOTAL"
[ "$TOTAL" = "0" ] && exit 0
echo "--- by code ---"
grep -oE "error [A-Z]+[0-9]+" obj/build.log | sort | uniq -c | sort -rn | head -12
echo "--- by message (top ${TOP:-60}) ---"
grep " error " obj/build.log | sed -E 's/^.*: error //' | sed -E 's/ \[[A-Z]:.*$//' | sort | uniq -c | sort -rn | head -${TOP:-60} | cut -c1-260
exit 1
