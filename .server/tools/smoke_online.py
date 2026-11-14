# 同时在线上限的测试：注册不设上限，在线的满了后来的等着；管理员照样进得去；有人离线(一分钟没来取东西)后让出位置。
#   python tools/smoke_online.py [端口]
import base64, io, json, os, shutil, subprocess, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 18997
BASE = "http://127.0.0.1:%d" % PORT
STATE = os.path.join(HERE, "obj", "state-smoke-online")
LOG = os.path.join(HERE, "obj", "smoke-online.log")
ADMIN_USER, ADMIN_PASSWORD = "keeper", "smoke-" + base64.b16encode(os.urandom(6)).decode().lower()
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
        raw = error.read()
        try:
            return error.code, json.loads(raw)
        except Exception:
            return error.code, {}
    except Exception as error:
        return 0, {"_error": str(error)}


shutil.rmtree(STATE, ignore_errors=True)
os.makedirs(STATE)
json.dump({"port": PORT, "admin_user": ADMIN_USER, "admin_password": ADMIN_PASSWORD, "max_seats": 2, "autosave_minutes": 0},
          io.open(os.path.join(STATE, "server.json"), "w", encoding="utf-8"))
log = io.open(LOG, "w", encoding="utf-8")
server = subprocess.Popen(["dotnet", os.path.join(HERE, "obj", "out", "EmpireCraftServer.dll"), "--quiet", "--state", STATE],
                          stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, cwd=HERE)
try:
    for _ in range(60):
        if call("GET", "/api/server")[0] == 200:
            break
        time.sleep(1)
    print("注册")
    tokens = []
    for i in range(20):
        tokens.append(call("POST", "/api/account/register", {"user": "p%02d" % i, "password": "pass-%02d-%s" % (i, ADMIN_PASSWORD)})[1].get("token"))
    check(all(tokens), "上限设 2，注册 20 个账号都收下(注册不设上限)")

    print("同时在线")
    a, b, c = tokens[0], tokens[1], tokens[2]
    check(call("GET", "/api/state", token=a)[0] == 200 and call("GET", "/api/state", token=b)[0] == 200, "头两位上线")
    status, reply = call("GET", "/api/state", token=c)
    check(status == 503 and reply.get("error") == "server_full" and reply.get("online") == 2 and reply.get("max") == 2, "第三位：人满，告诉他在线 2/上限 2")
    check(call("GET", "/api/state", token=a)[0] == 200, "已经在线的照常")
    info = call("GET", "/api/server")[1]
    check(info.get("online") == 2 and info.get("max") == 2, "服务器信息里报在线人数")
    status, admin = call("POST", "/api/admin/pair", {"user": ADMIN_USER, "password": ADMIN_PASSWORD})
    token = admin.get("token")
    check(status == 200 and call("GET", "/api/state", token=token)[0] == 200, "人满了管理员照样进得去")
    check(call("POST", "/api/account/login", {"user": "p02", "password": "pass-02-" + ADMIN_PASSWORD})[1].get("token") == c, "人满时也登得进账号(拿得到自己的席位)")

    print("离线让位(等一分钟)")
    for _ in range(14):
        call("GET", "/api/state", token=a)
        time.sleep(5)
    status, _ = call("GET", "/api/state", token=c)
    check(status == 200, "第二位一分钟没来，第三位接上")
    status, reply = call("GET", "/api/state", token=b)
    check(status == 503 and reply.get("error") == "server_full", "第二位回来时人又满了，要等")
finally:
    server.kill()
    log.close()
print("ALL PASSED" if failed == 0 else "%d FAILED" % failed)
sys.exit(1 if failed else 0)
