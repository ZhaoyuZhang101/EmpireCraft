#!/usr/bin/env bash
# Linux 上测存档(在 WSL 里跑)：按剧本开局跑一阵、存档退出，再起一回接着存档跑。
#   先在 Windows 上发布：dotnet publish EmpireCraft.Server.csproj -c Release -r linux-x64 --self-contained true -p:DebugType=none -o obj/publish-linux
#   再：wsl -d Ubuntu-22.04 -- bash /mnt/d/…/.server/tools/wsl_save.sh
HERE="$(cd "$(dirname "$0")/.." && pwd)"
MOD="$(cd "$HERE/.." && pwd)"
STATE=/tmp/ec-save
cd "$HERE/obj/publish-linux" || exit 2
chmod +x EmpireCraftServer
rm -rf "$STATE"
echo "--- 开局、跑、存"
./EmpireCraftServer --quiet --data "$MOD" --state "$STATE" --new --scenario ming_qing --frame eastasia --size 0 --speed 20 --run 20 2>&1 |
  grep -E "补丁|就绪|存前|已存档|跑了|error" | cut -c1-220
echo "--- 重启、读、接着跑"
./EmpireCraftServer --quiet --data "$MOD" --state "$STATE" --run 10 --speed 20 2>&1 |
  grep -E "补丁|读档|就绪|读回|存前|已存档|跑了|error" | cut -c1-220
ls -la "$STATE/save"
