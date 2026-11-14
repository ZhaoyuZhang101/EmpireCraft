# 存档的整套测试：开局 → 玩家领一位君主 → 管理员存档 → quit(退出时再存) → 重启接着存档跑、席位还领着原来的君主 →
# 上传新剧本(旧存档归档) → 存档坏了退回上一份。
#   python tools/smoke_save.py [端口]
# 配置写在 obj/state-smoke-save/server.json(测试用的管理员账号密码是这里现编的)。
import base64, io, json, os, shutil, subprocess, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(HERE)
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 18994
BASE = "http://127.0.0.1:%d" % PORT
STATE = os.path.join(HERE, "obj", "state-smoke-save")
LOG = os.path.join(HERE, "obj", "smoke-save.log")
ADMIN_USER, ADMIN_PASSWORD = "keeper", "smoke-" + base64.b16encode(os.urandom(6)).decode().lower()
PLAYER_PASSWORD = "smoke-" + base64.b16encode(os.urandom(6)).decode().lower()
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


def log_text():
    return io.open(LOG, encoding="utf-8", errors="replace").read()


class Server:
    def __init__(self, *extra):
        self.mark = len(log_text()) if os.path.exists(LOG) else 0
        self.log = io.open(LOG, "a", encoding="utf-8")
        self.process = subprocess.Popen(["dotnet", os.path.join(HERE, "obj", "out", "EmpireCraftServer.dll"), "--quiet", "--trace", "--state", STATE] + list(extra),
                                        stdout=self.log, stderr=subprocess.STDOUT, stdin=subprocess.PIPE, cwd=HERE)

    # 这一趟启动以来的输出
    def output(self):
        self.log.flush()
        return log_text()[self.mark:]

    def wait_for(self, text, seconds=120):
        for _ in range(seconds * 2):
            if text in self.output():
                return True
            if self.process.poll() is not None:
                return text in self.output()
            time.sleep(0.5)
        return False

    def command(self, line):
        self.process.stdin.write((line + "\n").encode("utf-8"))
        self.process.stdin.flush()

    def quit(self):
        try:
            self.command("quit")
            self.process.wait(timeout=60)
        except Exception:
            self.process.kill()
        self.log.close()
        return log_text()[self.mark:]


def seat_state(token, want_ruler, seconds=30):
    state = {}
    for _ in range(seconds):
        state = call("GET", "/api/state", token=token)[1]
        if bool(state.get("ruler")) == want_ruler and state.get("loaded"):
            break
        time.sleep(1)
    return state


shutil.rmtree(STATE, ignore_errors=True)
os.makedirs(STATE)
if os.path.exists(LOG):
    os.remove(LOG)
json.dump({"port": PORT, "admin_user": ADMIN_USER, "admin_password": ADMIN_PASSWORD, "frame": "eastasia", "size": 0, "speed": 20, "autosave_minutes": 0},
          io.open(os.path.join(STATE, "server.json"), "w", encoding="utf-8"))
save_file = os.path.join(STATE, "save", "world.json")
server = None
try:
    print("开局")
    server = Server("--scenario", "three_kingdoms")
    check(server.wait_for("世界已就绪"), "按剧本开局")
    check(server.wait_for("已存档（开局）", 30) and os.path.isfile(save_file), "新开的局就绪后先存了一份")
    server.wait_for("手机打开", 30)
    admin = call("POST", "/api/admin/pair", {"user": ADMIN_USER, "password": ADMIN_PASSWORD})[1].get("token")
    alice = call("POST", "/api/account/register", {"user": "alice", "password": PLAYER_PASSWORD})[1].get("token")
    check(bool(admin) and bool(alice), "管理员登入、玩家注册")
    state = seat_state(alice, False)
    rulers = state.get("rulers") or []
    pick = max(rulers, key=lambda ruler: ruler.get("cities") or 0) if rulers else {}
    call("POST", "/api/choose", {"id": str(pick.get("id"))}, token=alice)
    state = seat_state(alice, True)
    ruler, realm = (state.get("ruler") or {}).get("name"), (state.get("realm") or {}).get("name")
    check(bool(ruler), "玩家领了一位君主：%s（%s）" % (str(ruler).replace(" ", "·"), str(realm).replace(" ", "·")))

    print("存档")
    check(call("POST", "/api/admin/save", token=alice)[0] == 401, "玩家的令牌不能叫服务器存档")
    status, reply = call("POST", "/api/admin/save", token=admin)
    saved = {}
    for _ in range(30):
        saved = call("GET", "/api/admin/save", token=admin)[1]
        if "管理员" in (saved.get("last") or ""):
            break
        time.sleep(0.5)
    check(status == 200 and (saved.get("last") or "").startswith("已存档（管理员）") and saved.get("exists") is True, "管理员叫存档：" + (saved.get("last") or "没有回音"))
    seats = json.load(io.open(os.path.join(STATE, "save", "seats.json"), encoding="utf-8"))
    check(seats.get(alice, -1) >= 0, "存档里记着这个席位领的君主")
    server.command("save")
    check(server.wait_for("已存档（手动）", 30), "控制台 save 命令")
    year = seat_state(alice, True).get("year")
    output = server.quit()
    check("已存档（退出）" in output, "quit：退出时又存了一次")
    check(os.path.isdir(os.path.join(STATE, "save.old")), "换下来的那份留作 save.old")
    world = json.load(io.open(save_file, encoding="utf-8"))
    check(world.get("scenario") == "three_kingdoms" and len(world.get("kingdoms") or []) > 3, "存档里是三国那个世界(%d 国 %d 城 %d 人)" % (
        len(world.get("kingdoms") or []), len(world.get("cities") or []), len(world.get("units") or [])))

    print("重启")
    server = Server()
    check(server.wait_for("读回:"), "不给剧本重启：接着存档里的世界跑")
    lines = [line.strip() for line in (output + server.output()).splitlines() if "存前" in line or "读回" in line]
    server.wait_for("手机打开", 30)
    check(call("POST", "/api/account/login", {"user": "alice", "password": PLAYER_PASSWORD})[1].get("token") == alice, "玩家再登录：还是原来的席位")
    state = seat_state(alice, True)
    check((state.get("ruler") or {}).get("name") == ruler and (state.get("realm") or {}).get("name") == realm, "席位接着领原来的君主、原来的国")
    check(state.get("year") is not None and year is not None and state.get("year") >= year, "年份接着走(存档前 %s 年，现在 %s 年)" % (year, state.get("year")))
    time.sleep(4)
    later = seat_state(alice, True)
    check((later.get("year"), later.get("month")) != (state.get("year"), state.get("month")) or later.get("year") > year, "读档后世界照常往前走(%s 年 %s 月)" % (later.get("year"), later.get("month")))

    print("换剧本")
    scenario = io.open(os.path.join(ROOT, "WorldMaps", "eras", "ming_qing.json"), encoding="utf-8-sig").read()
    status, sent = call("POST", "/api/admin/scenario", {"name": "smoke_other", "files": [{"path": "scenario.json", "text": scenario}], "start": True}, token=admin)
    progress = {}
    for _ in range(120):
        progress = call("GET", "/api/admin/scenario", token=admin)[1]
        if progress.get("state") in ("ready", "failed"):
            break
        time.sleep(1)
    check(status == 200 and progress.get("state") == "ready", "上传另一个剧本并开局")
    archive = os.path.join(STATE, "saves-archive")
    kept = os.listdir(archive) if os.path.isdir(archive) else []
    check(len(kept) == 1 and os.path.isfile(os.path.join(archive, kept[0], "world.json")), "原来的存档挪进了 saves-archive")
    check(server.wait_for("已存档（开局）", 30) and json.load(io.open(save_file, encoding="utf-8")).get("scenario") == "smoke_other", "新世界就绪后存成了新的存档")
    check(not seat_state(alice, False).get("ruler"), "换了世界：席位空出来，等玩家重新选")
    server.command("save")
    server.wait_for("已存档（手动）", 30)
    output = server.quit()

    print("存档坏了")
    io.open(save_file, "w", encoding="utf-8").write("{\"v\":1,\"kingdoms\":[")
    server = Server()
    check(server.wait_for("读回:"), "最新一份读不出来：退回上一份(save.old)")
    output = server.output()
    check("save.old" in output and "读不出来" in output, "命令行说了哪一份读不出来、读的是哪一份")
    state = seat_state(alice, False)
    check(state.get("loaded") is True and len(state.get("rulers") or []) > 3, "退回来的世界能玩(可选君主 %d 位)" % len(state.get("rulers") or []))
    server.quit()
    server = None
finally:
    if server is not None:
        server.process.kill()
errors = [line for line in log_text().splitlines() if "抛出处" in line or "[error]" in line]
print("运行中的异常 %d 处" % len(errors))
for line in errors[:12]:
    print("   " + line.strip()[:220])
print("ALL PASSED" if failed == 0 else "%d FAILED" % failed)
sys.exit(1 if failed else 0)
