# 外交里标出玩家：两位玩家各领一国，彼此在外交、舆图(战事有就看)里看得到对面是哪位玩家、在不在线。
#   python tools/smoke_players.py [端口]
import base64, io, json, os, shutil, subprocess, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 18998
BASE = "http://127.0.0.1:%d" % PORT
STATE = os.path.join(HERE, "obj", "state-smoke-players")
SECRET = base64.b16encode(os.urandom(6)).decode().lower()
failed = 0


def check(ok, what):
    global failed
    print(("  ok   " if ok else "  FAIL ") + what)
    if not ok:
        failed += 1


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


def state_of(token, want, seconds=40):
    state = {}
    for _ in range(seconds):
        state = call("GET", "/api/state", token=token)[1]
        if state.get("loaded") and want(state):
            break
        time.sleep(1)
    return state


def diplomacy(state):
    groups = ((state.get("decrees") or {}).get("groups")) or []
    return next((group for group in groups if group.get("id") == "diplomacy"), {}).get("realms") or []


shutil.rmtree(STATE, ignore_errors=True)
os.makedirs(STATE)
json.dump({"port": PORT, "admin_user": "keeper", "admin_password": "smoke-" + SECRET, "size": 0, "speed": 20, "autosave_minutes": 0},
          io.open(os.path.join(STATE, "server.json"), "w", encoding="utf-8"))
log = io.open(os.path.join(HERE, "obj", "smoke-players.log"), "w", encoding="utf-8")
server = subprocess.Popen(["dotnet", os.path.join(HERE, "obj", "out", "EmpireCraftServer.dll"), "--quiet", "--state", STATE, "--new", "--scenario", "ming_qing",
                           "--frame", "eastasia", "--size", "0"], stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, cwd=HERE)
try:
    for _ in range(90):
        if call("GET", "/api/server")[0] == 200:
            break
        time.sleep(1)
    alice = call("POST", "/api/account/register", {"user": "alice", "password": "alice-" + SECRET})[1].get("token")
    bob = call("POST", "/api/account/register", {"user": "bob", "password": "bob-" + SECRET})[1].get("token")
    state = state_of(alice, lambda s: len(s.get("rulers") or []) > 3, 90)
    rulers = state.get("rulers") or []
    pick = max(rulers, key=lambda ruler: ruler.get("cities") or 0)
    call("POST", "/api/choose", {"id": str(pick["id"])}, token=alice)
    state = state_of(alice, lambda s: s.get("ruler") and diplomacy(s))
    known = {realm["id"]: realm for realm in diplomacy(state)}
    other = next((ruler for ruler in rulers if ruler.get("kid") in known and ruler["id"] != pick["id"]), None)
    check(bool(known) and other is not None, "alice 领了 %s，外交里有 %d 国，挑一国给 bob" % (pick.get("realm", "").replace(" ", ""), len(known)))
    call("POST", "/api/choose", {"id": str(other["id"])}, token=bob)
    state_of(bob, lambda s: s.get("ruler"))

    print("alice 看 bob")
    state = state_of(alice, lambda s: any(realm.get("player") for realm in diplomacy(s)))
    row = next((realm for realm in diplomacy(state) if realm["id"] == other["kid"]), {})
    check((row.get("player") or {}).get("name") == "bob" and (row.get("player") or {}).get("online") is True, "外交里 bob 那一国标着：玩家 bob，在线")
    check(all(not realm.get("player") for realm in diplomacy(state) if realm["id"] != other["kid"]), "别的国(电脑在管)不标")
    world_map = {}
    for _ in range(30):
        world_map = call("GET", "/api/map", token=alice)[1]
        if any(realm.get("player") for realm in world_map.get("realms") or []):
            break
        time.sleep(1)
    tagged = [realm for realm in world_map.get("realms") or [] if realm.get("player")]
    check(len(tagged) == 1 and tagged[0]["id"] == other["kid"], "舆图上点 bob 那一国也看得到是 bob")
    mine = next((realm for realm in diplomacy(state_of(bob, lambda s: diplomacy(s))) if realm["id"] == pick["kid"]), None)
    check(mine is None or (mine.get("player") or {}).get("name") == "alice", "bob 那边看 alice 的国也标着 alice")

    print("bob 离线")
    for _ in range(11):
        call("GET", "/api/state", token=alice)
        time.sleep(5)
    state = state_of(alice, lambda s: True, 1)
    row = next((realm for realm in diplomacy(state) if realm["id"] == other["kid"]), {})
    print("   ", row.get("player"), "row", bool(row), "rows", len(diplomacy(state)), "bob ruler", bool(call("GET", "/api/state", token=bob)[1].get("ruler")))
    check((row.get("player") or {}).get("name") == "bob" and (row.get("player") or {}).get("online") is False, "bob 一分钟没来：还标着 bob，改成离线(政令十秒一算)")
finally:
    server.kill()
    log.close()
print("ALL PASSED" if failed == 0 else "%d FAILED" % failed)
sys.exit(1 if failed else 0)
