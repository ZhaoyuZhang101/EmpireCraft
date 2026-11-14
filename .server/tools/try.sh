#!/usr/bin/env bash
# 跑一遍服务器(参数原样传下去)，把头一个异常和它对应的源码行打出来(迭代补替身时用)。
HERE="$(cd "$(dirname "$0")/.." && pwd)"
cd "$HERE"
dotnet obj/out/EmpireCraftServer.dll --quiet --state obj/state "$@" > obj/try.log 2>&1
grep -vE "^\[替身\] 还没实现: 资产|^\s+[0-9]+  资产 " obj/try.log | grep -E "error|warn|Exception|^   at |模组初始化|世界|剧本" | cut -c1-250 | head -${LINES_MAX:-24}
LOC=$(grep -oE " in [A-Z]:[^:]+\.cs:line [0-9]+" obj/try.log | head -1)
if [ -n "$LOC" ]; then
  F=$(echo "$LOC" | sed -E 's/^ in (.*):line [0-9]+$/\1/'); L=$(echo "$LOC" | sed -E 's/^.*:line ([0-9]+)$/\1/')
  echo "--- $(basename "$F"):$L ---"; sed -n "$((L-4)),$((L+1))p" "$F" | cut -c1-220
fi
