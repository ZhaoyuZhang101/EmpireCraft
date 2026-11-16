# 管理员「史」的测试：开局跑一阵 → 管理员取编年(全部/只看大事/翻页)、历朝 → 存档退出 → 重启接着存档跑，编年还在。
#   python tools/smoke_history.py [端口]
import base64, io, json, os, shutil, subprocess, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 18996
BASE = "http://127.0.0.1:%d" % PORT
STATE = os.path.join(HERE, "obj", "state-smoke-history")
LOG = os.path.join(HERE, "obj", "smoke-history.log")
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
        try:
            return error.code, json.loads(error.read())
        except Exception:
            return error.code, {}
    except Exception as error:
        return 0, {"_error": str(error)}


def log_text():
    return io.open(LOG, encoding="utf-8", errors="replace").read() if os.path.exists(LOG) else ""


class Server:
    def __init__(self, *extra):
        self.mark = len(log_text())
        self.log = io.open(LOG, "a", encoding="utf-8")
        self.process = subprocess.Popen(["dotnet", os.path.join(HERE, "obj", "out", "EmpireCraftServer.dll"), "--quiet", "--state", STATE] + list(extra),
                                        stdout=self.log, stderr=subprocess.STDOUT, stdin=subprocess.PIPE, cwd=HERE)

    def output(self):
        self.log.flush()
        return log_text()[self.mark:]

    def wait_for(self, text, seconds=180):
        for _ in range(seconds * 2):
            if text in self.output():
                return True
            if self.process.poll() is not None:
                return text in self.output()
            time.sleep(0.5)
        return False

    def quit(self):
        try:
            self.process.stdin.write(b"quit\n")
            self.process.stdin.flush()
            self.process.wait(timeout=90)
        except Exception:
            self.process.kill()
        self.log.close()
        return log_text()[self.mark:]


def history(token, body, tries=40):
    reply = {}
    for _ in range(tries):
        status, reply = call("POST", "/api/admin/history", body, token=token)
        if status == 200 and reply.get("ready"):
            return status, reply
        # 有管理员看着主线程才备：顺手取一下状态，算"看着"
        call("GET", "/api/state", token=token)
        time.sleep(1)
    return 0, reply


shutil.rmtree(STATE, ignore_errors=True)
os.makedirs(STATE)
if os.path.exists(LOG):
    os.remove(LOG)
json.dump({"port": PORT, "admin_user": ADMIN_USER, "admin_password": ADMIN_PASSWORD, "frame": "eastasia", "size": 0, "speed": 30, "autosave_minutes": 0},
          io.open(os.path.join(STATE, "server.json"), "w", encoding="utf-8"))
server = None
try:
    print("开局")
    server = Server("--scenario", "three_kingdoms")
    check(server.wait_for("世界已就绪"), "按剧本开局")
    server.wait_for("手机打开", 30)
    admin = call("POST", "/api/admin/pair", {"user": ADMIN_USER, "password": ADMIN_PASSWORD})[1].get("token")
    check(bool(admin), "管理员登入")
    check(call("POST", "/api/admin/history", {"part": "chron"})[0] in (401, 403), "没有令牌取不到")
    for _ in range(90):
        call("GET", "/api/state", token=admin)
        time.sleep(1)

    print("编年")
    status, chron = history(admin, {"part": "chron", "from": "0", "major": "0"})
    items = chron.get("items") or []
    check(status == 200 and len(items) > 0, "有编年条目(共 %s 条，这一页 %d 条)" % (chron.get("total"), len(items)))
    years = [item.get("y") for item in items]
    check(years == sorted(years, reverse=True), "新的在前")
    check(all(item.get("x") and "<" not in item.get("x") for item in items), "文字去掉了颜色标记")
    if items:
        print("    最新：%s %s月 %s" % (items[0].get("yl"), items[0].get("m"), items[0].get("x")[:60]))
    total = chron.get("total") or 0
    status, major = history(admin, {"part": "chron", "from": "0", "major": "1"})
    check(status == 200 and 0 < (major.get("total") or 0) <= total, "只看大事(%s 条 / 全部 %s 条)" % (major.get("total"), total))
    if chron.get("next", -1) >= 0:
        status, page2 = history(admin, {"part": "chron", "from": str(chron["next"]), "major": "0"})
        check(status == 200 and len(page2.get("items") or []) > 0 and page2["items"][0] != items[0], "翻到下一页")

    print("分类")
    kinds = ["war", "throne", "realm", "dipl", "court", "law", "faith", "culture", "econ", "disaster", "story", "other"]
    cats = chron.get("cats") or {}
    print("    " + " ".join("%s:%s" % (k, cats.get(k, 0)) for k in kinds))
    check(all(item.get("c") in kinds for item in items), "每条都有类别")
    check(sum(cats.values()) == total, "各类加起来等于全部(%d)" % sum(cats.values()))
    check(len([k for k in cats if k != "other"]) >= 3, "至少分出三类(%d 类)" % len(cats))
    check(cats.get("other", 0) * 3 < max(total, 1), "其他不到三分之一(%s / %s)" % (cats.get("other", 0), total))
    busiest = max((k for k in cats if k != "other"), key=lambda k: cats[k], default=None)
    if busiest:
        status, one = history(admin, {"part": "chron", "from": "0", "major": "0", "cat": busiest})
        check(status == 200 and one.get("total") == cats[busiest] and all(i.get("c") == busiest for i in one.get("items") or []),
              "只看%s(%s 条)" % (busiest, one.get("total")))
        for item in (one.get("items") or [])[:4]:
            print("      [%s] %s" % (busiest, item.get("x")[:50]))
    other_items = [i for i in items if i.get("c") == "other"][:6]
    for item in other_items:
        print("      [other] %s" % item.get("x")[:50])

    print("历朝")
    status, dyn = history(admin, {"part": "dyn"})
    rows = dyn.get("items") or []
    alive = [row for row in rows if row.get("alive")]
    fallen = [row for row in rows if not row.get("alive")]
    check(status == 200 and len(alive) > 0, "现存的帝国 %d 个" % len(alive))
    check(len(fallen) > 0, "已亡的王朝 %d 个(含开局前的史实朝代)" % len(fallen))
    check(any(len(row.get("reigns") or []) > 0 for row in fallen), "已亡的朝代列出了历代君主")
    for row in rows[:3] + alive[:2]:
        print("    %s %s · %s 年 · %s · %d 位" % (row.get("name"), row.get("from"), row.get("years"), row.get("ruler"), len(row.get("reigns") or [])))

    print("一国")
    linked = [item for item in items if item.get("k")]
    check(len(linked) > 0, "编年条目带着说的是哪一国(%d / %d 条)" % (len(linked), len(items)))
    court = next((row for row in alive if row.get("kid")), None)
    if court:
        status, realm = history(admin, {"part": "realm", "id": court["kid"]})
        lineage, annals = realm.get("lineage") or {}, realm.get("history") or {}
        check(status == 200 and not realm.get("gone") and realm.get("empire") is True, "点开帝国 %s" % realm.get("name"))
        check(len(lineage.get("nodes") or []) > 0, "君主世系 %d 格" % len(lineage.get("nodes") or []))
        check("items" in annals, "史册 %d 条" % len(annals.get("items") or []))
        status, own = history(admin, {"part": "chron", "from": "0", "major": "0", "kid": court["kid"]})
        check(status == 200 and all(item.get("k") == court["kid"] for item in own.get("items") or []), "只看这一国的编年(%s 条)" % own.get("total"))
    if linked:
        status, realm = history(admin, {"part": "realm", "id": linked[0]["k"]})
        check(status == 200 and realm.get("ready"), "点开编年里的 %s" % linked[0].get("kn"))
    status, gone = history(admin, {"part": "realm", "id": "999999999"})
    check(status == 200 and gone.get("gone") is True, "不在了的国说不在了")

    print("存档后读回")
    output = server.quit()
    check("已存档（退出）" in output, "退出时存了档")
    mod = json.load(io.open(os.path.join(STATE, "save", "EmpireCraftModData.json"), encoding="utf-8"))
    saved = mod.get("world_chronicle") or []
    check(len(saved) >= total, "存档里有编年 %d 条" % len(saved))
    server = Server()
    check(server.wait_for("读回:"), "接着存档跑")
    server.wait_for("手机打开", 30)
    admin = call("POST", "/api/admin/pair", {"user": ADMIN_USER, "password": ADMIN_PASSWORD})[1].get("token")
    status, back = history(admin, {"part": "chron", "from": "0", "major": "0"})
    check(status == 200 and (back.get("total") or 0) >= len(saved), "重启后编年还在(%s 条)" % back.get("total"))
    server.quit()
    server = None
finally:
    if server is not None:
        server.process.kill()
errors = [line for line in log_text().splitlines() if "[error]" in line or "编年没备成" in line or "历朝没备成" in line]
print("运行中的异常 %d 处" % len(errors))
for line in errors[:12]:
    print("   " + line.strip()[:220])
print("ALL PASSED" if failed == 0 else "%d FAILED" % failed)
sys.exit(1 if failed else 0)
