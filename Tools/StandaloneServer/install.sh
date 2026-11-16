#!/bin/bash
# EmpireCraft 独立服务器(终端核心)在 Linux 上的安装/更新：由 update_server.bat 把本地编译好的程序和数据传到
# <根目录>/.incoming 后调用。做的事：换上新程序、换上新数据、装成开机自启并且崩了自动重启的服务、确认它起来了。
# 用法：bash install.sh [根目录，默认 /opt/empirecraft] [keep|clear-saves]，要用 root 运行。
# 装完以后：systemctl status empirecraft-core 看状态，journalctl -u empirecraft-core -f 看日志(管理员码也在日志里)。
#
# 目录：<根目录>/app 是程序(自包含，不用另装 dotnet)；<根目录> 本身是数据目录(Terminal、Locales、WorldMaps……)；
# 默认保留配置、账号、剧本和存档。沿用服务现在用的存放目录，没有服务时取 core-state、server-state、state
# 里有 server.json 的头一个，都没有就新建 core-state。
set -e
ROOT="${1:-/opt/empirecraft}"
SAVE_MODE="${2:-keep}"
IN="$ROOT/.incoming"
SERVICE=empirecraft-core
UNIT="/etc/systemd/system/$SERVICE.service"

if [ "$(id -u)" != "0" ]; then echo "请用 root 运行：sudo bash $0"; exit 1; fi
case "$ROOT" in ""|"/") echo "根目录不对：$ROOT"; exit 1;; esac
case "$SAVE_MODE" in keep|clear-saves) ;; *) echo "未知的存档模式：$SAVE_MODE"; exit 1;; esac
if [ ! -f "$IN/linux-x64/EmpireCraftServer" ]; then echo "没找到传上来的程序：$IN/linux-x64/EmpireCraftServer"; exit 1; fi

# ---- 存放目录
STATE=""
if [ -f "$UNIT" ]; then STATE="$(sed -n 's/^ExecStart=.*--state \([^ ]*\).*/\1/p' "$UNIT" | head -1)"; fi
if [ -z "$STATE" ]; then
    for name in core-state server-state state; do
        if [ -f "$ROOT/$name/server.json" ]; then STATE="$ROOT/$name"; break; fi
    done
fi
[ -z "$STATE" ] && STATE="$ROOT/core-state"
mkdir -p "$STATE"
echo "存放目录：$STATE"
# 核心的端口照 server.json(没写是核心的默认 8766)
PORT="$(grep -o '"port"[^0-9]*[0-9]\+' "$STATE/server.json" 2>/dev/null | grep -o '[0-9]\+$' | head -1)"
[ -z "$PORT" ] && PORT=8766
SAVE_DIRS=(save save.old save.new save.verify saves-archive)

# ---- 停旧的。没装成服务以前手动起着的那个也停掉(占着端口，新服务起不来)；这台机器上只跑这一个核心。
if [ "$SAVE_MODE" = clear-saves ]; then
    command -v python3 >/dev/null || { echo "清空存档需要 python3 来更新开局配置。"; exit 1; }
    if [ "$(realpath -e "$STATE")" != "$(realpath -e "$ROOT")/$(basename "$STATE")" ] || [ -L "$STATE" ]; then
        echo "存放目录不在服务器根目录下，取消清档：$STATE"
        exit 1
    fi
    for name in "${SAVE_DIRS[@]}"; do
        if [ -L "$STATE/$name" ]; then echo "存档路径是链接，取消清档：$STATE/$name"; exit 1; fi
    done
    if [ -L "$STATE/server.json" ] || [ -d "$STATE/pending_scenario.json" ]; then
        echo "世界配置路径不符合预期，取消清档。"
        exit 1
    fi
    echo "停下旧核心，跳过即将清除的存档……"
    if [ "$(systemctl show --value -p LoadState "$SERVICE")" = loaded ]; then
        # 先让 systemd 接受停止请求，再结束进程，避免 Restart=always 马上拉起旧核心。
        systemctl stop --no-block "$SERVICE"
        for attempt in {1..50}; do
            status="$(systemctl show --value -p ActiveState "$SERVICE")"
            case "$status" in
                inactive|failed) break;;
                deactivating) systemctl kill --signal=SIGKILL "$SERVICE" || true; break;;
            esac
            sleep 0.2
        done
        case "$status" in
            inactive|failed|deactivating) ;;
            *) echo "旧核心没有进入停止状态，取消清档。"; exit 1;;
        esac
        systemctl stop "$SERVICE"
        [ "$(systemctl show --value -p MainPID "$SERVICE")" = 0 ] || exit 1
    fi
    # 同一个安装目录可能还有手动启动的核心。等它退出，避免边写档边清理。
    executable="$(realpath -m "$ROOT/app/EmpireCraftServer")"
    for proc in /proc/[0-9]*/exe; do
        if [ "$(readlink "$proc" 2>/dev/null)" = "$executable" ]; then
            echo "发现手动启动的核心，请先停下再清档。"
            exit 1
        fi
    done
else
    # 旧服务只等 90 秒，大档可能存不完。
    if [ -f "$UNIT" ] && ! grep -q '^TimeoutStopSec=' "$UNIT"; then
        sed -i 's/^\[Service\]$/[Service]\nTimeoutStopSec=600/' "$UNIT"
        systemctl daemon-reload
    fi
    echo "停下旧核心(先存档，大档要一两分钟)……"
    systemctl stop "$SERVICE" 2>/dev/null || true
    pkill -f "EmpireCraftServer" 2>/dev/null || true
    sleep 1
fi

# ---- 换程序(上一版留在 app.old，新版起不来时换回去)
rm -rf "$ROOT/app.old"
[ -d "$ROOT/app" ] && mv "$ROOT/app" "$ROOT/app.old"
mv "$IN/linux-x64" "$ROOT/app"
chmod +x "$ROOT/app/EmpireCraftServer"

# ---- 换数据：传上来的每个目录、文件整个替换同名的(不留旧文件)；程序、存放目录、服务器上原有的别的东西不碰
for item in "$IN"/*; do
    name="$(basename "$item")"
    case "$name" in install.sh|app|app.old|core-state|server-state|state) continue;; esac
    rm -rf "$ROOT/$name"
    mv "$item" "$ROOT/$name"
    chmod -R u=rwX,go=rX "$ROOT/$name"
done
chmod -R u=rwX,go=rX "$ROOT/app"
chmod +x "$ROOT/app/EmpireCraftServer"

# ---- 单独一个没有登录权限的用户来跑，只让它写存放目录
id eccore >/dev/null 2>&1 || useradd --system --no-create-home --shell /sbin/nologin eccore
chown -R eccore: "$STATE"

# 自包含的 .NET 程序仍要系统里有 ICU 库
if ! ldconfig -p 2>/dev/null | grep -q libicu; then
    (command -v dnf >/dev/null 2>&1 && dnf install -y libicu) || (command -v yum >/dev/null 2>&1 && yum install -y libicu) ||
        (command -v apt-get >/dev/null 2>&1 && apt-get install -y libicu-dev) || echo "注意：没装上 ICU 库，程序可能起不来"
fi

# ---- 小内存机器(不到 4G)：.NET 改用工作站 GC 并尽量省内存。默认的多核 GC 每个核预留一大块堆，2G 的机器上
#      核心挤进交换区，存档、开局一次十几分钟，大厅也连不上
MEM_MB="$(awk '/MemTotal/ {print int($2/1024)}' /proc/meminfo 2>/dev/null)"
GC_ENV=""
if [ "${MEM_MB:-0}" -gt 0 ] && [ "${MEM_MB:-0}" -lt 4096 ]; then
    GC_ENV="Environment=DOTNET_gcServer=0
Environment=DOTNET_GCConserveMemory=7"
    echo "本机内存 ${MEM_MB} MB：核心用省内存的 GC"
fi

# ---- 服务：开机自启，退出(崩溃、被杀)五秒后自动重启
AFTER="network-online.target"
systemctl list-unit-files 2>/dev/null | grep -q '^empirecraft-lobby\.service' && AFTER="$AFTER empirecraft-lobby.service"
cat > "$UNIT" <<EOF
[Unit]
Description=EmpireCraft standalone core
After=$AFTER
Wants=network-online.target

[Service]
User=eccore
WorkingDirectory=$ROOT/app
Environment=HOME=$STATE
$GC_ENV
ExecStart=$ROOT/app/EmpireCraftServer --quiet --data $ROOT --state $STATE
StandardInput=null
Restart=always
RestartSec=5
# 退出码 4 = 端口开不了(被别的程序占着)：重启也没用，停着等人改 server.json
RestartPreventExitStatus=4
# 停下时核心要先存档：几百年的大档一次要存一到四分钟，系统默认只等 90 秒就强杀，档就存不上(2026-11-16 实际发生过)
TimeoutStopSec=600
NoNewPrivileges=true
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable "$SERVICE" >/dev/null 2>&1

# ---- 端口被别的程序占着(最常见：和联机大厅同机，两边都是 8765)：核心绑不上，起来了也不应答。直说，不往下试
HOLDER="$(ss -ltnp 2>/dev/null | grep -E "[:.]$PORT[[:space:]]" | grep -v EmpireCraftServ | head -1)"
if [ -n "$HOLDER" ]; then
    systemctl stop "$SERVICE" 2>/dev/null || true
    rm -rf "$IN"
    echo "程序和数据已换成新版，但核心没法起来：端口 $PORT 被别的程序占着——"
    echo "  $HOLDER" | sed 's/  */ /g' | cut -c1-160
    echo "核心的端口写在 $STATE/server.json 的 \"port\"，换一个没人用的(联机大厅占着 8765)，再重跑一次更新。"
    exit 2
fi

if [ "$SAVE_MODE" = clear-saves ]; then
    # 先确认下次不会自动开局，再删旧档。
    if [ -f "$STATE/server.json" ]; then
        python3 - "$STATE/server.json" <<'PY'
import json
import os
import stat
import sys
import tempfile

path = sys.argv[1]
with open(path, encoding="utf-8-sig") as source:
    config = json.load(source)
scenarios = [name for name in config if name.casefold() == "scenario" and config[name]]
if scenarios:
    original = os.stat(path)
    fd, temporary = tempfile.mkstemp(dir=os.path.dirname(path))
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as output:
            for name in scenarios:
                config[name] = ""
            json.dump(config, output, ensure_ascii=False, indent=2)
            output.write("\n")
            os.fchown(output.fileno(), original.st_uid, original.st_gid)
            os.fchmod(output.fileno(), stat.S_IMODE(original.st_mode))
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
PY
    fi
    for name in "${SAVE_DIRS[@]}"; do
        rm -rf --one-file-system -- "$STATE/$name"
    done
    rm -f -- "$STATE/pending_scenario.json"
    echo "线上存档和归档已清空，核心将启动到空房间。"
fi

# ---- 起来没有：问本机的 /api/server
# 核心读完存档才开端口：几百年的大档在小内存的机器上要读好几分钟(2026-11-16 一分钟就判失败、把能用的新版换掉了)。
# 进程还活着、日志里还在读档就接着等，最多十分钟；进程退了、或读完档一分钟还不应答才算没起来
started() {
    systemctl restart "$SERVICE"
    local since waited=0
    since="$(date '+%Y-%m-%d %H:%M:%S')"
    while [ "$waited" -lt 600 ]; do
        sleep 2; waited=$((waited + 2))
        if curl -fsS -m 3 "http://127.0.0.1:$PORT/api/server" 2>/dev/null | grep -q empirecraft; then return 0; fi
        systemctl is-active --quiet "$SERVICE" || return 1
        if [ "$waited" -ge 60 ] && ! journalctl -u "$SERVICE" --since "$since" --no-pager 2>/dev/null | grep -q "读档"; then return 1; fi
        if [ $((waited % 30)) -eq 0 ]; then echo "  还在读档，已等 ${waited} 秒……"; fi
    done
    return 1
}
if started; then
    echo "核心已起来：端口 $PORT"
    rm -rf "$ROOT/app.failed"
else
    echo "新版没应答(端口 $PORT)。进程：$(systemctl is-active "$SERVICE")；它在听的端口：$(ss -ltnp 2>/dev/null | grep -c EmpireCraftServ) 个。最近的日志："
    journalctl -u "$SERVICE" -n 25 --no-pager || true
    if [ -d "$ROOT/app.old" ]; then
        echo "换回上一版程序……"
        systemctl stop "$SERVICE" 2>/dev/null || true
        rm -rf "$ROOT/app.failed"
        mv "$ROOT/app" "$ROOT/app.failed"
        mv "$ROOT/app.old" "$ROOT/app"
        if started; then echo "上一版已恢复运行(没起来的那版留在 $ROOT/app.failed)"; else echo "上一版也没起来，请看上面的日志"; fi
    fi
    rm -rf "$IN"
    exit 1
fi

# 系统自带的防火墙开着的话放行这个端口(云厂商的安全组要另外在控制台放行)
if systemctl is-active --quiet firewalld 2>/dev/null; then
    firewall-cmd --permanent --add-port="$PORT"/tcp >/dev/null && firewall-cmd --reload >/dev/null
fi

rm -rf "$IN"
echo "状态：systemctl status $SERVICE      日志：journalctl -u $SERVICE -f"
journalctl -u "$SERVICE" -n 60 --no-pager 2>/dev/null | grep -m1 "管理员" || true
