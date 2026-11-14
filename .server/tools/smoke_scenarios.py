# 管理员在房间里挑剧本的测试：不带剧本起服务器 → 列出服务器上的剧本 → 按名字开局 → 重置地图 → 只上传不开局、再从列表里挑它 →
# 重启后还记得眼下是哪个剧本、还能重置 → 端口被占时明说并退出。
#   python tools/smoke_scenarios.py [端口]
import base64, io, json, os, shutil, subprocess, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(HERE)
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 18995
BASE = "http://127.0.0.1:%d" % PORT
STATE = os.path.join(HERE, "obj", "state-smoke-scenarios")
LOG = os.path.join(HERE, "obj", "smoke-scenarios.log")
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


def start(*extra):
    log = io.open(LOG, "a", encoding="utf-8")
    process = subprocess.Popen(["dotnet", os.path.join(HERE, "obj", "out", "EmpireCraftServer.dll"), "--quiet", "--trace", "--state", STATE] + list(extra),
                               stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.PIPE, cwd=HERE)
    for _ in range(90):
        if call("GET", "/api/server")[0] == 200 or process.poll() is not None:
            break
        time.sleep(1)
    return process, log


def stop(process, log):
    try:
        process.stdin.write(b"quit\n")
        process.stdin.flush()
        process.wait(timeout=60)
    except Exception:
        process.kill()
    log.close()


def wait_ready(token, seconds=150):
    info = {}
    for _ in range(seconds):
        info = call("GET", "/api/admin/scenarios", token=token)[1]
        if info.get("state") in ("ready", "failed"):
            break
        time.sleep(1)
    return info


shutil.rmtree(STATE, ignore_errors=True)
os.makedirs(STATE)
if os.path.exists(LOG):
    os.remove(LOG)
json.dump({"port": PORT, "admin_user": ADMIN_USER, "admin_password": ADMIN_PASSWORD, "size": 0, "speed": 20, "autosave_minutes": 0},
          io.open(os.path.join(STATE, "server.json"), "w", encoding="utf-8"))
process = log = None
try:
    print("没有世界的服务器")
    process, log = start()
    admin = call("POST", "/api/admin/pair", {"user": ADMIN_USER, "password": ADMIN_PASSWORD})[1].get("token")
    player = call("POST", "/api/account/register", {"user": "alice", "password": "alice-" + ADMIN_PASSWORD})[1].get("token")
    check(call("GET", "/api/admin/scenarios", token=player)[0] == 401, "玩家的令牌看不了剧本表")
    time.sleep(2)
    check(call("GET", "/api/state", token=admin)[1].get("loaded") is False, "管理员看到的是世界尚未载入")
    status, info = call("GET", "/api/admin/scenarios", token=admin)
    keys = [item["key"] for item in info.get("list", [])]
    check(status == 200 and info.get("state") == "idle" and info.get("world") is False and "ming_qing" in keys and "three_kingdoms" in keys,
          "列得出服务器上的剧本(%d 个预设)，眼下没有世界" % len(keys))
    check(len(info.get("frames", [])) >= 3 and [size["id"] for size in info.get("sizes", [])] == [0, 1, 2] and all(item.get("name") for item in info["list"]),
          "带地图范围、大小的选项，各剧本有名字")
    check(call("POST", "/api/admin/scenarios", {"key": "no_such"}, token=admin)[0] == 404, "没有的剧本：404")
    check(call("POST", "/api/admin/scenarios", {"key": "../../etc"}, token=admin)[0] == 404, "乱写的路径：404")
    check(call("POST", "/api/admin/scenarios", {"reset": True}, token=admin)[1].get("error") == "no_scenario", "还没开过局：重置不了")
    check(call("POST", "/api/admin/scenarios", {"key": "three_kingdoms", "frame": "mars"}, token=admin)[1].get("error") == "bad_frame", "没有的地图范围：不收")

    print("按名字开局")
    status, sent = call("POST", "/api/admin/scenarios", {"key": "three_kingdoms", "frame": "eastasia", "size": 0}, token=admin)
    check(status == 200 and sent.get("starting") is True, "挑三国、东亚、小图：收下")
    check(call("POST", "/api/admin/scenarios", {"key": "ming_qing"}, token=admin)[0] == 409, "正在开局时再挑一个：409")
    info = wait_ready(admin)
    check(info.get("state") == "ready" and info.get("world") is True and info.get("key") == "three_kingdoms" and info.get("frame") == "eastasia" and info.get("size") == 0,
          "开局走完：眼下是三国(%s)" % (info.get("detail") or "无报错"))
    state = {}
    for _ in range(20):
        state = call("GET", "/api/state", token=player)[1]
        if state.get("loaded"):
            break
        time.sleep(1)
    first_year = state.get("year")
    check(state.get("loaded") is True and len(state.get("rulers") or []) > 3, "玩家这头接上了新世界")

    print("重置地图")
    time.sleep(6)
    before = call("GET", "/api/state", token=player)[1]
    status, sent = call("POST", "/api/admin/scenarios", {"reset": True}, token=admin)
    info = wait_ready(admin)
    check(status == 200 and info.get("state") == "ready" and info.get("key") == "three_kingdoms", "重置：同一个剧本又开了一遍")
    time.sleep(2)
    after = call("GET", "/api/state", token=player)[1]
    check(after.get("loaded") is True and (after.get("year"), after.get("month")) < (before.get("year"), before.get("month")) or after.get("year") == first_year,
          "世界回到开局的年月(重置前 %s 年 %s 月，重置后 %s 年 %s 月)" % (before.get("year"), before.get("month"), after.get("year"), after.get("month")))
    check(os.path.isdir(os.path.join(STATE, "saves-archive")), "重置前的存档挪去了归档")

    print("上传过的剧本")
    scenario = json.loads(io.open(os.path.join(ROOT, "WorldMaps", "eras", "ming_qing.json"), encoding="utf-8-sig").read())
    scenario["zh"] = scenario["tw"] = scenario["en"] = "自定的一份"
    status, sent = call("POST", "/api/admin/scenario", {"name": "mine", "files": [{"path": "scenario.json", "text": json.dumps(scenario, ensure_ascii=False)}], "start": False}, token=admin)
    info = call("GET", "/api/admin/scenarios", token=admin)[1]
    mine = [item for item in info.get("list", []) if item["key"] == "custom/mine"]
    check(status == 200 and sent.get("starting") is False and info.get("key") == "three_kingdoms" and len(mine) == 1 and mine[0]["custom"] and mine[0]["name"] == "自定的一份",
          "只上传不开局：世界没动，列表里多了这一份")
    call("POST", "/api/admin/scenarios", {"key": "custom/mine", "frame": "eastasia", "size": 0}, token=admin)
    info = wait_ready(admin)
    check(info.get("state") == "ready" and info.get("key") == "custom/mine", "从列表里挑上传的那份开局")
    stop(process, log)

    print("重启")
    process, log = start()
    admin = call("POST", "/api/admin/pair", {"user": ADMIN_USER, "password": ADMIN_PASSWORD})[1].get("token")
    info = wait_ready(admin)
    check(info.get("state") == "ready" and info.get("key") == "custom/mine" and info.get("frame") == "eastasia" and info.get("size") == 0, "读档后还记得眼下是哪个剧本、什么范围大小")
    call("POST", "/api/admin/scenarios", {"reset": True}, token=admin)
    info = wait_ready(admin)
    check(info.get("state") == "ready" and info.get("key") == "custom/mine", "读档后也能重置")

    print("端口被占")
    second = subprocess.run(["dotnet", os.path.join(HERE, "obj", "out", "EmpireCraftServer.dll"), "--quiet", "--state", STATE + "-second", "--port", str(PORT)],
                            capture_output=True, stdin=subprocess.DEVNULL, cwd=HERE, timeout=120)
    text = (second.stdout + second.stderr).decode("utf-8", "replace")
    check(second.returncode == 4 and ("%d 端口开不了" % PORT) in text and "port" in text, "同一个端口再起一台：说明端口开不了、叫改 port，退出码 4")
    shutil.rmtree(STATE + "-second", ignore_errors=True)
    stop(process, log)
    process = None
finally:
    if process is not None:
        process.kill()
errors = [line for line in io.open(LOG, encoding="utf-8", errors="replace").read().splitlines() if "抛出处" in line or "[error]" in line]
print("运行中的异常 %d 处" % len(errors))
for line in errors[:12]:
    print("   " + line.strip()[:220])
print("ALL PASSED" if failed == 0 else "%d FAILED" % failed)
sys.exit(1 if failed else 0)
