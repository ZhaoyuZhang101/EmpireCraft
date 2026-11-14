# 账号、管理员、上传剧本的接口测试：起一台不带剧本的服务器，照网页的走法逐个接口过一遍。
#   python tools/smoke_admin.py [端口]
# 配置写在 obj/state-admin/server.json(测试用的管理员账号密码是这里现编的)，跑完把服务器停掉。
import base64, io, json, os, shutil, subprocess, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(HERE)
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 18993
BASE = "http://127.0.0.1:%d" % PORT
STATE = os.path.join(HERE, "obj", "state-admin")
ADMIN_USER, ADMIN_PASSWORD = "keeper", "smoke-" + base64.b16encode(os.urandom(6)).decode().lower()
failed = 0


def check(ok, what):
    global failed
    print(("  ok   " if ok else "  FAIL ") + what)
    if not ok:
        failed += 1


def call(method, path, body=None, token=None, headers=None):
    data = None if body is None else (body if isinstance(body, bytes) else json.dumps(body).encode("utf-8"))
    request = urllib.request.Request(BASE + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("X-Terminal-Token", token)
    for key, value in (headers or {}).items():
        request.add_header(key, value)
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            raw = response.read()
            return response.status, (json.loads(raw) if raw else {}), dict(response.headers)
    except urllib.error.HTTPError as error:
        raw = error.read()
        try:
            return error.code, json.loads(raw), dict(error.headers)
        except Exception:
            return error.code, {"_raw": raw[:200].decode("utf-8", "replace")}, dict(error.headers)


shutil.rmtree(STATE, ignore_errors=True)
os.makedirs(STATE)
json.dump({"port": PORT, "admin_user": ADMIN_USER, "admin_password": ADMIN_PASSWORD, "frame": "eastasia", "size": 0, "speed": 20},
          io.open(os.path.join(STATE, "server.json"), "w", encoding="utf-8"))
log = io.open(os.path.join(HERE, "obj", "smoke-admin.log"), "w", encoding="utf-8")
server = subprocess.Popen(["dotnet", os.path.join(HERE, "obj", "out", "EmpireCraftServer.dll"), "--quiet", "--trace", "--state", STATE],
                          stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, cwd=HERE)
try:
    for _ in range(60):
        try:
            if call("GET", "/api/server")[0] == 200:
                break
        except Exception:
            time.sleep(1)
    print("服务器")
    status, info, _ = call("GET", "/api/server")
    check(status == 200 and info.get("server") == "empirecraft" and info.get("adminLogin") == "password" and info.get("accounts") is True,
          "GET /api/server 说明是帝国盒子服务器、管理员凭账号密码、玩家凭账号")
    text = io.open(os.path.join(HERE, "obj", "smoke-admin.log"), encoding="utf-8", errors="replace").read()
    check("管理员码" not in text and ("管理员: 账号 " + ADMIN_USER) in text, "设了管理员账号密码：命令行不显示管理员码，显示账号")
    check(ADMIN_PASSWORD not in text, "命令行不打印管理员密码")

    print("玩家账号")
    check(call("POST", "/api/pair", {"code": "123456"})[1].get("error") == "accounts_only", "配对码进不来(只收账号)")
    check(call("POST", "/api/account/register", {"user": "a", "password": "longenough"})[1].get("error") == "bad_user", "账号名太短：不收")
    check(call("POST", "/api/account/register", {"user": "alice", "password": "123"})[1].get("error") == "bad_password", "密码太短：不收")
    status, made, _ = call("POST", "/api/account/register", {"user": "alice", "password": "alice-pass-1"})
    alice = made.get("token")
    check(status == 200 and alice and len(alice) == 32, "注册 alice：给了席位令牌")
    check(call("POST", "/api/account/register", {"user": "Alice", "password": "another-pass"})[0] == 409, "重名(不分大小写)：409")
    bob = call("POST", "/api/account/register", {"user": "鲍勃", "password": "bob-pass-22"})[1].get("token")
    check(bool(bob), "中文账号名能注册")
    check(call("POST", "/api/account/login", {"user": "alice", "password": "wrong"})[1].get("error") == "wrong_login", "密码不对：403 wrong_login")
    check(call("POST", "/api/account/login", {"user": "alice", "password": "alice-pass-1"})[1].get("token") == alice, "再登录：还是同一个席位")
    check(call("GET", "/api/state", token=alice)[0] == 200, "凭令牌取得到国势")
    stored = io.open(os.path.join(STATE, "accounts.json"), encoding="utf-8").read()
    check("alice-pass-1" not in stored and "bob-pass-22" not in stored, "账号文件里没有密码原文")

    print("管理员")
    check(call("POST", "/api/admin/pair", {"user": ADMIN_USER, "password": "nope"})[1].get("error") == "wrong_admin", "管理员密码不对：403 wrong_admin")
    check(call("GET", "/api/admin/accounts", token=alice)[0] == 401, "玩家的令牌看不了账号列表")
    status, admin, _ = call("POST", "/api/admin/pair", {"user": ADMIN_USER, "password": ADMIN_PASSWORD})
    token = admin.get("token")
    check(status == 200 and token and admin.get("admin") is True, "管理员账号密码对：给管理员令牌")
    check(call("POST", "/api/admin/pair", {"code": ADMIN_USER + ":" + ADMIN_PASSWORD})[0] == 200, "只有一个输入框时写成 账号:密码 也进得来")
    status, listed, _ = call("GET", "/api/admin/accounts", token=token)
    names = [row["user"] for row in listed.get("accounts", [])]
    check(status == 200 and names == ["alice", "鲍勃"] and all(row["seat"] for row in listed["accounts"]), "账号列表：两个账号、都有席位")
    check(call("POST", "/api/admin/account/delete", {"user": "alice"}, token=token)[0] == 200, "销号 alice")
    time.sleep(1.5)
    check(call("POST", "/api/account/login", {"user": "alice", "password": "alice-pass-1"})[1].get("error") == "wrong_login", "销号后登录不了")
    check(call("GET", "/api/state", token=alice)[0] == 401, "销号后旧令牌作废")
    check(call("GET", "/api/state", token=bob)[0] == 200, "别的账号不受影响")

    print("跨域")
    request = urllib.request.Request(BASE + "/api/admin/scenario", method="OPTIONS")
    request.add_header("Origin", "http://localhost:8777")
    request.add_header("Access-Control-Request-Headers", "content-type,x-terminal-token")
    with urllib.request.urlopen(request, timeout=10) as response:
        allowed = response.headers.get("Access-Control-Allow-Origin"), response.headers.get("Access-Control-Allow-Headers") or ""
        check(response.status == 204 and allowed[0] == "*" and "X-Terminal-Token" in allowed[1], "预检请求：204，许任何来源、许带令牌头")

    print("上传剧本")
    check(call("GET", "/api/admin/scenario", token=token)[1].get("state") == "idle", "还没上传：idle")
    check(call("POST", "/api/admin/scenario", {"name": "x", "files": []}, token=token)[1].get("error") == "no_files", "没有文件：不收")
    check(call("POST", "/api/admin/scenario", {"name": "x", "files": [{"path": "../evil.txt", "text": "x"}]}, token=token)[1].get("error") == "bad_path", "往文件夹外头写：不收")
    scenario = io.open(os.path.join(ROOT, "WorldMaps", "eras", "three_kingdoms.json"), encoding="utf-8-sig").read()
    body = {"name": "smoke_three", "files": [{"path": "scenario.json", "text": scenario}, {"path": "note.bin", "base64": base64.b64encode(b"\x00\x01\x02").decode()}], "start": True}
    status, sent, _ = call("POST", "/api/admin/scenario", body, token=token)
    check(status == 200 and sent.get("starting") is True and sent.get("name") == "smoke_three", "上传三国剧本(%d KB)：收下并开局" % (len(scenario) // 1024))
    folder = os.path.join(STATE, "mods_config", "EmpireCraft", "scenarios", "smoke_three")
    check(os.path.isfile(os.path.join(folder, "scenario.json")) and io.open(os.path.join(folder, "note.bin"), "rb").read() == b"\x00\x01\x02", "文件落在服务器的自定剧本文件夹里")
    progress, steps = {}, set()
    for _ in range(120):
        progress = call("GET", "/api/admin/scenario", token=token)[1]
        steps.add(progress.get("step"))
        if progress.get("state") in ("ready", "failed"):
            break
        time.sleep(1)
    check(progress.get("state") == "ready", "开局走完：ready(经过 %d 步；%s)" % (len(steps), progress.get("detail") or "无报错"))
    time.sleep(3)
    state = call("GET", "/api/state", token=bob)[1]
    rulers = state.get("rulers") or []
    check(state.get("loaded") is True and len(rulers) > 3, "玩家这头看得到新世界：可选君主 %d 位" % len(rulers))
    print("   例：" + "、".join("%s(%s)" % (r.get("ruler", "").replace(" ", "·"), r.get("realm", "").replace(" ", "·")) for r in rulers[:5]))

    def upload_and_wait(body):
        status, sent, _ = call("POST", "/api/admin/scenario", body, token=token)
        result = {}
        for _ in range(120):
            result = call("GET", "/api/admin/scenario", token=token)[1]
            if result.get("state") in ("ready", "failed"):
                break
            time.sleep(1)
        return status, result

    def server_log():
        log.flush()
        return io.open(os.path.join(HERE, "obj", "smoke-admin.log"), encoding="utf-8", errors="replace").read()

    print("上传的剧本同预设重名")
    renamed = json.loads(scenario)
    renamed["zh"] = renamed["tw"] = renamed["en"] = "三国·自定"
    status, result = upload_and_wait({"name": "three_kingdoms", "files": [{"path": "scenario.json", "text": json.dumps(renamed, ensure_ascii=False)}], "start": True})
    check(status == 200 and result.get("state") == "ready" and "剧本: custom/three_kingdoms（三国·自定）" in server_log(),
          "文件夹叫 three_kingdoms(同预设重名)：开的是传上去的那份，不是预设")

    print("带导入地图的剧本")
    imported = os.path.join(os.environ.get("TEMP", ""), "claude", "bfe4e83d97e37059", "7992703c-f5da-4b11-96a8-145a00df7ba7", "scratchpad", "scenarios", "blank")
    if os.path.isdir(imported):
        files = []
        for name in sorted(os.listdir(imported)):
            data = io.open(os.path.join(imported, name), "rb").read()
            files.append({"path": name, "text": data.decode("utf-8")} if name.endswith((".json", ".txt")) else {"path": name, "base64": base64.b64encode(data).decode()})
        status, result = upload_and_wait({"name": "blank", "files": files, "start": True})
        check(status == 200 and result.get("state") == "ready", "导入地图的剧本(%d 个文件)：开局 %s%s" % (len(files), result.get("state"), "，" + result["detail"] if result.get("detail") else ""))
        print("   " + [line for line in server_log().splitlines() if "世界已就绪" in line][-1].strip())
    else:
        print("  (没找到那份导入地图的剧本，这一条没测)")
finally:
    server.kill()
    log.close()
errors = [line for line in io.open(os.path.join(HERE, "obj", "smoke-admin.log"), encoding="utf-8", errors="replace") if "抛出处" in line or "[error]" in line]
print("运行中的异常 %d 处" % len(errors))
for line in errors[:12]:
    print("   " + line.strip()[:220])
print("ALL PASSED" if failed == 0 else "%d FAILED" % failed)
sys.exit(1 if failed else 0)
