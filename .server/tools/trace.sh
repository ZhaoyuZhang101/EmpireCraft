#!/usr/bin/env bash
# 带排查开关跑一遍(参数原样传下去)，把各处异常头一回出现时的调用栈打出来。
HERE="$(cd "$(dirname "$0")/.." && pwd)"
cd "$HERE"
dotnet obj/out/EmpireCraftServer.dll --boot-only --trace --quiet --state obj/state "$@" > obj/try.log 2>&1
grep -E "世界已就绪|可能被内联" obj/try.log | cut -c1-200
grep -A${DEPTH:-8} "^\[trace\]" obj/try.log | grep -vE "HarmonyLib|ThrowHelper|FirstChance|Program\.|System\.Reflection|RuntimeMethodHandle|Dictionary.2\.(FindValue|TryGetValue|get_Item)|^--$|LightweightHost|ZonePaintBudget|SmoothBorders\.Set|ConfigActions|ModClass\.OnLoad" | sed -E 's/ in [A-Z]:.*Scripts.(.*)$/  @\1/' | cut -c1-210 | head -${LINES_MAX:-70}
