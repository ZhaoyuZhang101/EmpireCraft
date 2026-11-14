# 身后：几位玩家各领一位年老的世袭君主，世界快跑，等君主驾崩，看选人页最上头给的"接着领"人选(子嗣即位、新君、占故都……)。
#   python tools/smoke_heir.py [端口]
import base64, io, json, os, shutil, subprocess, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 18999
BASE = "http://127.0.0.1:%d" % PORT
STATE = os.path.join(HERE, "obj", "state-smoke-heir")
SECRET = base64.b16encode(os.urandom(6)).decode().lower()
PLAYERS = 6


def call(method, path, body=None, token=None):
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(BASE + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("X-Terminal-Token", token)
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            raw = response.read()
            return response.status, (json.loads(raw) if raw else {})
    except urllib.error.HTTPError as error:
        return error.code, {}
    except Exception as error:
        return 0, {"_error": str(error)}


def clean(text):
    return (text or "").replace(" ", "")


shutil.rmtree(STATE, ignore_errors=True)
os.makedirs(STATE)
json.dump({"port": PORT, "admin_user": "keeper", "admin_password": "smoke-" + SECRET, "size": 0, "speed": 60, "autosave_minutes": 0},
          io.open(os.path.join(STATE, "server.json"), "w", encoding="utf-8"))
log = io.open(os.path.join(HERE, "obj", "smoke-heir.log"), "w", encoding="utf-8")
server = subprocess.Popen(["dotnet", os.path.join(HERE, "obj", "out", "EmpireCraftServer.dll"), "--quiet", "--state", STATE, "--new", "--scenario", "ming_qing",
                           "--frame", "eastasia", "--size", "0"], stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, cwd=HERE)
ok = 0
try:
    for _ in range(90):
        if call("GET", "/api/server")[0] == 200:
            break
        time.sleep(1)
    tokens = [call("POST", "/api/account/register", {"user": "heir%d" % i, "password": "pass-%d-%s" % (i, SECRET)})[1].get("token") for i in range(PLAYERS)]
    rulers = []
    for _ in range(90):
        rulers = call("GET", "/api/state", token=tokens[0])[1].get("rulers") or []
        if len(rulers) > 10:
            break
        time.sleep(1)
    oldest = sorted((r for r in rulers if r.get("kind") in ("king", "emperor") and not r.get("taken")), key=lambda r: -(r.get("age") or 0))[:PLAYERS]
    seats = []
    for token, ruler in zip(tokens, oldest):
        call("POST", "/api/choose", {"id": str(ruler["id"])}, token=token)
        seats.append((token, ruler))
        print("领 %s(%s，%s 岁)" % (clean(ruler.get("ruler")), clean(ruler.get("realm")), ruler.get("age")))
    done = {}
    started = time.time()
    while time.time() - started < 420 and len(done) < len(seats):
        for token, ruler in seats:
            if token in done:
                continue
            state = call("GET", "/api/state", token=token)[1]
            if state.get("vacant") and state.get("reason") not in (None, "none"):
                done[token] = (ruler, state)
        time.sleep(3)
    # 刚驾崩那一下原国常还没立新君(人选只有 interregnum)：等它立了再看，至多一分钟
    for token, (ruler, state) in list(done.items()):
        waited = time.time()
        while time.time() - waited < 60 and all(o.get("why") == "interregnum" for o in (state.get("next") or [])):
            time.sleep(3)
            fresh = call("GET", "/api/state", token=token)[1]
            if not fresh.get("vacant"):
                break
            state = fresh
        done[token] = (ruler, state)
    for token, (ruler, state) in done.items():
        offers = state.get("next") or []
        print("%s 年：%s(%s)%s → 人选：%s" % (state.get("year"), clean(ruler.get("ruler")), clean(ruler.get("realm")), {"dead": "驾崩", "deposed": "失位", "fallen": "亡国"}.get(state.get("reason"), state.get("reason")),
              "；".join("%s %s%s" % (o.get("why"), clean(o.get("ruler", "")), ("(" + o["kin"] + ")") if o.get("kin") else "") for o in offers) or "无"))
        if any(o.get("why") == "heir" for o in offers):
            ok += 1
    print("驾崩/失位 %d 位，其中人选里有子嗣即位的 %d 位；还在位的 %d 位" % (len(done), ok, len(seats) - len(done)))
finally:
    server.kill()
    log.close()
