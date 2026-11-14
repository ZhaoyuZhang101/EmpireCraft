# 归附与参战：玩家领一个独立小国，外交里对一个帝国下"归附"，使者到了以后看本国是不是进了那个帝国；
# 另看互换天平上有没有"参战(对某国)"这一条，拿库银换对面参战，看对面是不是卷进了那场仗。
#   python tools/smoke_submit.py [端口]
import base64, io, json, os, shutil, subprocess, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 19001
BASE = "http://127.0.0.1:%d" % PORT
STATE = os.path.join(HERE, "obj", "state-smoke-submit")
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


def clean(text):
    return (text or "").replace(" ", "")


def diplomacy(state):
    groups = ((state.get("decrees") or {}).get("groups")) or []
    return next((group for group in groups if group.get("id") == "diplomacy"), {}).get("realms") or []


def wait_state(token, want, seconds=60):
    state = {}
    for _ in range(seconds):
        state = call("GET", "/api/state", token=token)[1]
        if state.get("loaded") and want(state):
            return state
        time.sleep(1)
    return state


shutil.rmtree(STATE, ignore_errors=True)
os.makedirs(STATE)
json.dump({"port": PORT, "admin_user": "keeper", "admin_password": "smoke-" + SECRET, "size": 0, "speed": 20, "autosave_minutes": 0},
          io.open(os.path.join(STATE, "server.json"), "w", encoding="utf-8"))
log = io.open(os.path.join(HERE, "obj", "smoke-submit.log"), "w", encoding="utf-8")
server = subprocess.Popen(["dotnet", os.path.join(HERE, "obj", "out", "EmpireCraftServer.dll"), "--quiet", "--state", STATE, "--new", "--scenario", "ming_qing",
                           "--frame", "eastasia", "--size", "0"], stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, cwd=HERE)
try:
    for _ in range(90):
        if call("GET", "/api/server")[0] == 200:
            break
        time.sleep(1)
    token = call("POST", "/api/account/register", {"user": "vassal", "password": "pass-" + SECRET})[1].get("token")
    rulers = wait_state(token, lambda s: len(s.get("rulers") or []) > 10, 90).get("rulers") or []
    candidates = sorted((r for r in rulers if not r.get("empire") and not r.get("taken")), key=lambda r: r.get("cities") or 0)
    print("独立的君主 %d 位" % len(candidates))

    print("归附")
    picked = target = None
    for i, ruler in enumerate(candidates[:15]):
        # 每试一位换一个账号(注册不设上限)，免得要管理员收回席位
        token = call("POST", "/api/account/register", {"user": "vassal%d" % i, "password": "pass-%d-%s" % (i, SECRET)})[1].get("token")
        call("POST", "/api/choose", {"id": str(ruler["id"])}, token=token)
        state = wait_state(token, lambda s: s.get("ruler") and diplomacy(s), 30)
        target = next((realm for realm in diplomacy(state) if "submit" in (realm.get("can") or []) and realm.get("empire")), None)
        if target:
            picked = ruler
            break
    check(picked is not None, "找到一个能归附帝国的独立小国：%s → %s(%s)" % (clean(picked and picked.get("realm")), clean(target and target.get("name")), clean(target and target.get("empire"))))
    if picked:
        status, _ = call("POST", "/api/decree", {"id": "diplo:submit", "target": target["id"]}, token=token)
        check(status == 200, "下令遣使归附")
        state = wait_state(token, lambda s: (s.get("realm") or {}).get("empire"), 180)
        check(bool((state.get("realm") or {}).get("empire")), "使者到了：本国进了%s(现在是 %s)" % (clean(target.get("empire")), (state.get("realm") or {}).get("kind")))
        reports = [clean(r.get("text")) for r in (state.get("reports") or [])][:3]
        print("   奏报：", reports)

    print("参战")
    war_terms = []
    for _ in range(40):
        state = call("GET", "/api/state", token=token)[1]
        war_terms = [(realm, term) for realm in diplomacy(state) for term in ((realm.get("scale") or {}).get("terms") or []) if term.get("kind") == "war"]
        if war_terms:
            break
        time.sleep(3)
    print("   本国战事 %d 场；外交里在打仗的国 %d" % (len(state.get("wars") or []), len(state.get("wars") or [])))
    check(len(war_terms) > 0, "互换天平上有参战这一条(%d 条)：%s" % (len(war_terms), "；".join(clean(r["name"]) + "·" + clean(t["label"]) for r, t in war_terms[:4])))
finally:
    server.kill()
    log.close()
print("ALL PASSED" if failed == 0 else "%d FAILED" % failed)
sys.exit(1 if failed else 0)
