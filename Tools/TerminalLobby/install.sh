#!/bin/bash
# EmpireCraft 联机大厅(自架版)一键安装：把本目录里的 lobby_server.py 装成开机自启的服务。
# 用法：bash install.sh [端口，默认 8765]      要用 root 运行；重复运行就是更新后重启。
# 装完以后：systemctl status empirecraft-lobby 看状态，journalctl -u empirecraft-lobby -f 看日志。
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
PORT="${1:-8765}"
DATA=/var/lib/empirecraft-lobby

if [ "$(id -u)" != "0" ]; then echo "请用 root 运行：sudo bash $0"; exit 1; fi
if ! command -v python3 >/dev/null 2>&1; then
    (command -v dnf >/dev/null 2>&1 && dnf install -y python3) || (command -v yum >/dev/null 2>&1 && yum install -y python3) ||
        (command -v apt-get >/dev/null 2>&1 && apt-get install -y python3)
fi
python3 -c 'import sys; sys.exit(0 if sys.version_info >= (3, 6) else 1)' || { echo "python3 版本太旧，要 3.6 以上"; exit 1; }

# 单独一个没有登录权限的用户来跑，只让它写自己的数据目录
id eclobby >/dev/null 2>&1 || useradd --system --no-create-home --shell /sbin/nologin eclobby
mkdir -p "$DATA"
chown eclobby: "$DATA"
chmod -R a+rX "$DIR"

cat > /etc/systemd/system/empirecraft-lobby.service <<EOF
[Unit]
Description=EmpireCraft terminal lobby
After=network-online.target
Wants=network-online.target

[Service]
User=eclobby
ExecStart=$(command -v python3) $DIR/lobby_server.py --port $PORT --data $DATA
Restart=always
RestartSec=3
NoNewPrivileges=true
ProtectSystem=full
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable empirecraft-lobby >/dev/null 2>&1
systemctl restart empirecraft-lobby

# 系统自带的防火墙开着的话放行这个端口(云厂商的安全组要另外在控制台放行)
if systemctl is-active --quiet firewalld 2>/dev/null; then
    firewall-cmd --permanent --add-port="$PORT"/tcp >/dev/null && firewall-cmd --reload >/dev/null
fi

sleep 2
if curl -s -m 5 "http://127.0.0.1:$PORT/healthz" | grep -q ok; then
    echo "大厅已在 $PORT 端口运行。记得在云厂商控制台的安全组里放行 TCP $PORT。"
else
    echo "大厅没起来，看日志：journalctl -u empirecraft-lobby -n 30 --no-pager"
    exit 1
fi
