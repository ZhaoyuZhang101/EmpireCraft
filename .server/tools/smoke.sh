#!/usr/bin/env bash
# 冒烟测试：按剧本起服务器，照手机的走法过一遍接口(配对 → 选君主 → 国势 → 舆图 → 山川 → 军情)，然后停掉。
#   tools/smoke.sh [端口] [剧本] [范围] [大小]
HERE="$(cd "$(dirname "$0")/.." && pwd)"
cd "$HERE"
PORT=${1:-18992}; SCEN=${2:-ming_qing}; FRAME=${3:-eastasia}; SIZE=${4:-0}
B="http://127.0.0.1:$PORT"
export PYTHONIOENCODING=utf-8
dotnet obj/out/EmpireCraftServer.dll --quiet --trace --state obj/state --port $PORT ${SCEN:+--new --scenario $SCEN} --frame $FRAME --size $SIZE --speed ${SPEED:-20} > obj/smoke.log 2>&1 < /dev/null &
SRV_PID=$!
for i in $(seq 1 90); do grep -aq "世界已就绪" obj/smoke.log && break; sleep 1; done
grep -aE "世界已就绪|手机打开|管理员|玩家注册" obj/smoke.log
USER_NAME="smoke$RANDOM"
T=$(curl -s -X POST -H "Content-Type: application/json" -d "{\"user\":\"$USER_NAME\",\"password\":\"test-pass-1\"}" $B/api/account/register | grep -oE '[0-9a-f]{32}')
echo "token=${T:0:6}…"
get() { curl -s -H "X-Terminal-Token: $T" "$B$1"; }
post() { curl -s -X POST -H "X-Terminal-Token: $T" -H "Content-Type: application/json" -d "$2" "$B$1"; }
sleep 3
get /api/state > obj/smoke_state0.json
python - <<'PY'
import json, io
s = json.load(io.open('obj/smoke_state0.json', encoding='utf-8'))
print('空席位的国势: loaded=%s vacant=%s 年月=%s/%s 可选君主=%d' % (s.get('loaded'), s.get('vacant'), s.get('year'), s.get('month'), len(s.get('rulers') or [])))
for r in (s.get('rulers') or [])[:6]: print('   ', r.get('id'), r.get('ruler'), '|', r.get('realm'), '|', r.get('kind'), '| 城', r.get('cities'), '| 岁', r.get('age'), '|', r.get('tianxia'), r.get('empire'))
big = max(s.get('rulers') or [{'id': '0', 'cities': 0}], key=lambda r: r.get('cities') or 0)
io.open('obj/smoke_pick.txt', 'w').write(str(big.get('id')))
print('选:', big.get('ruler'), big.get('realm'), '城', big.get('cities'))
PY
PICK=$(cat obj/smoke_pick.txt)
post /api/choose "{\"id\":\"$PICK\"}"; echo
sleep 4
get /api/state > obj/smoke_state1.json
get /api/map > obj/smoke_map.json
# 山川是"先要、备好了再给"：头一回多半还没备好，隔一会儿再要
for i in 1 2 3 4 5 6; do post /api/terrain '{"p":0,"have":0}' > obj/smoke_terrain.json; grep -q '"ready":true' obj/smoke_terrain.json && break; sleep 2; done
sleep 2
get /api/armies > obj/smoke_armies.json
python - <<'PY'
import json, io
def load(n):
    try: return json.load(io.open(n, encoding='utf-8'))
    except Exception as e: return {'_error': str(e), '_raw': io.open(n, encoding='utf-8', errors='replace').read()[:300]}
s = load('obj/smoke_state1.json')
print('领君之后: vacant=%s 年月=%s/%s' % (s.get('vacant'), s.get('year'), s.get('month')), '君:', (s.get('ruler') or {}).get('name'), '国:', (s.get('realm') or {}).get('name'), (s.get('realm') or {}).get('role'), '时代', (s.get('realm') or {}).get('era'))
print('   数字:', json.dumps(s.get('figures'), ensure_ascii=False)[:200])
print('   各城:', len(s.get('cities') or []), [c.get('name') for c in (s.get('cities') or [])[:8]])
print('   战事:', json.dumps(s.get('wars'), ensure_ascii=False)[:200])
print('   奏报:', len(s.get('reports') or []), [r.get('text') for r in (s.get('reports') or [])[:4]])
print('   政令组:', [g.get('id') for g in ((s.get('decrees') or {}).get('groups') or [])])
if '_error' in s: print(s)
m = load('obj/smoke_map.json')
print('舆图: ready=%s %sx%s 国=%d 城=%d cells=%d字' % (m.get('ready'), m.get('w'), m.get('h'), len(m.get('realms') or []), len(m.get('cities') or []), len(m.get('cells') or '')))
print('   ', [(r.get('name'), r.get('n')) for r in (m.get('realms') or [])[:8]])
t = load('obj/smoke_terrain.json')
print('山川: ready=%s 片=%s/%s %sx%s 色数=%d d=%d字' % (t.get('ready'), t.get('p'), t.get('n'), t.get('w'), t.get('h'), len(t.get('pal') or []), len(t.get('d') or '')))
a = load('obj/smoke_armies.json')
print('军情: ready=%s 军=%d' % (a.get('ready'), len(a.get('armies') or [])), [(x.get('name'), x.get('n')) for x in (a.get('armies') or [])[:5]])
PY
echo "--- 运行中的异常 ---"
grep -aE "抛出处|备状态出错|命令没办成|\[error\]" obj/smoke.log | sed -E 's/ in [A-Z]:.*Scripts.(.*)$/  @\1/; s/ in [A-Z]:.*\.server.(.*)$/  @\1/' | sort | uniq -c | sort -rn | cut -c1-230 | head -${ERRS:-25}
# 只停自己起的这一台(别的会话可能也开着测试服务器)
kill $SRV_PID > /dev/null 2>&1
