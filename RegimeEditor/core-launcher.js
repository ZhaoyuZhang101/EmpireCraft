// 服务器核心：不开游戏，直接从编辑器把核心开起来(用户 2026-11-16 "如何在不打开游戏的情况下直接打开核心ui"
// "然后通过核心载入存档""我的建议是用政体编辑器打开""然后政体编辑器可以加一个将核心图标加入到桌面的选项")。
// 核心是游戏里「用服务器核心」用的同一个程序、同一个存放目录(见 Scripts/Terminal/CoreHost.cs)：
//   · 程序：打包好的编辑器(便携版)自带一份，连同核心要读的模组数据(见 locate、packagedData)——没装游戏、没有模组的电脑
//     凭编辑器自己就能开核心；其次是模组包里自带的那份(<模组目录>/RegimeEditor/core/win-x64，
//     一键发布时打进去的)。自带的都先拷到 <游戏数据目录>/EmpireCraftCore/bin/ 下再跑(不占着原处的文件)；
//   · 存放目录：<游戏数据目录>/EmpireCraftCore/state，里头 save/ 是核心的存档(游戏交给核心的那一局)，有就接着跑；
// 这里不带 --quiet 拉起，所以核心自己出窗口和托盘图标(见 .server/Server/CoreShell.cs)；带 --no-console，核心是控制台程序，
// 不带的话双击快捷方式会多出一个黑色命令行窗(带上它输出改写进 <存放目录>/core.log)。只在 Windows 上有用
const fs = require("node:fs");
const os = require("node:os");
const net = require("node:net");
const path = require("node:path");
const { spawn, execFile } = require("node:child_process");

const EXE = "EmpireCraftServer.exe";
const DEFAULT_PORT = 8766;

function coreRoot() {
  return path.join(os.homedir(), "AppData", "LocalLow", "mkarpenko", "WorldBox", "EmpireCraftCore");
}

function stateDir() {
  return path.join(coreRoot(), "state");
}

function exeIn(dir) {
  try {
    if (!dir || !fs.existsSync(dir)) return null;
    const direct = path.join(dir, EXE);
    if (fs.existsSync(direct)) return direct;
    // 解包时多套了一层目录的
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (!entry.isDirectory()) continue;
      const nested = path.join(dir, entry.name, EXE);
      if (fs.existsSync(nested)) return nested;
    }
  } catch {
    // 读不了就当没有
  }
  return null;
}

// 自带的核心(一整个目录)：拷到 bin/<name>(已经拷过、主程序大小和修改时间都对得上的不再拷)。返回拷好的那份主程序；
// 目录里没有核心的返回 null；拷不成(旧的那份正跑着)就接着用旧的。install 为假时只看不拷(界面上显示用)。
// 目录名不带版本：桌面快捷方式指着它，更新后还是这个位置
function bundledExe(bundled, name, install) {
  if (!bundled) return null;
  const source = path.join(bundled, EXE);
  if (!fs.existsSync(source)) return null;
  const target = path.join(coreRoot(), "bin", name);
  const have = path.join(target, EXE);
  try {
    const a = fs.statSync(source);
    const b = fs.existsSync(have) ? fs.statSync(have) : null;
    if (b && a.size === b.size && Math.abs(a.mtimeMs - b.mtimeMs) < 2000) return have;
    // 只看不拷：说的是它会装到的那个位置(便携版自己解在临时目录里，那个路径每回都不一样，给人看没用)
    if (!install) return have;
    const stage = target + ".copy";
    // 没装过游戏的电脑上连 EmpireCraftCore 这一层都没有
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.rmSync(stage, { recursive: true, force: true });
    fs.cpSync(bundled, stage, { recursive: true, preserveTimestamps: true });
    fs.rmSync(target, { recursive: true, force: true });
    fs.renameSync(stage, target);
    return have;
  } catch {
    return fs.existsSync(have) ? have : source;
  }
}

// 打包好的编辑器(便携版)自带的核心：打包便携版.bat 先把核心编进 core/win-x64，electron-builder 再把它放进 resources
// (见 package.json 的 extraResources)。用源码直接跑编辑器时 resources 是 Electron 自己的，里头没有核心
function packagedCoreDir() {
  const dir = process.resourcesPath ? path.join(process.resourcesPath, "core", "win-x64") : null;
  return dir && fs.existsSync(path.join(dir, EXE)) ? dir : null;
}

// 便携版(一个 exe，每回打开把自己解到临时目录，关掉就删)：它的启动器会设这个环境变量。便携版自带的核心和数据
// 得先拷到用户数据目录里才能用；装好的编辑器在固定的安装目录里，核心、数据、图标都直接用安装目录里的
// (用户 2026-11-16 "桌面快捷方式图标为什么不应用政体编辑器的安装目录")
function isPortable() {
  return !!process.env.PORTABLE_EXECUTABLE_DIR;
}

// 找核心，连同它是哪儿来的(界面上标出来)。先后：
//   editor   打包好的编辑器自带的(用户 2026-11-16 "服务器核心应该内置到政体编辑器里面去")：装好的直接用安装目录里的，
//            便携版拷到 bin/editor 再跑；
//   mod      模组包里自带的(<模组目录>/RegimeEditor/core/win-x64，一键发布时打进去的)：拷到 bin/current，
//            游戏每回启动也把它对一遍(见 CoreHost.InstallBundled)；
//   download 以前下载到 bin/<版本> 的；
//   dev      从源码发布到游戏目录旁边的
function locate(modRoot, install = false) {
  const root = coreRoot();
  const own = packagedCoreDir();
  const packaged = own && !isPortable() ? path.join(own, EXE) : bundledExe(own, "editor", install);
  if (packaged) return { exe: packaged, source: "editor" };
  // core.path(游戏里联调用的指针，见 CoreHost.cs)这里不认：编辑器自带核心以后用不着它，
  // 还会把人引到别的目录去(用户 2026-11-16 "我不需要这个目录, 因为政体编辑器不是自带核心了吗")
  const bundled = bundledExe(modRoot ? path.join(modRoot, "RegimeEditor", "core", "win-x64") : null, "current", install);
  if (bundled) return { exe: bundled, source: "mod" };
  // 下载下来的各版本里挑最新的
  try {
    const bin = path.join(root, "bin");
    const versions = fs.readdirSync(bin, { withFileTypes: true }).filter(e => e.isDirectory())
      .map(e => ({ dir: path.join(bin, e.name), time: fs.statSync(path.join(bin, e.name)).mtimeMs }))
      .sort((a, b) => b.time - a.time);
    for (const version of versions) {
      const found = exeIn(version.dir);
      if (found) return { exe: found, source: "download" };
    }
  } catch {
    // 没下载过
  }
  // 从源码发布到游戏目录旁边的(开发时)
  const dev = modRoot ? exeIn(path.resolve(modRoot, "..", "..", "EmpireCraftCore", "win-x64")) : null;
  return { exe: dev, source: dev ? "dev" : "" };
}

function findExe(modRoot, install = false) {
  return locate(modRoot, install).exe;
}

// 核心要读的模组数据(地图、语言、科技、制度、手机页面……清单同 update_server.bat 的 DATA_DIRS)。打包好的编辑器自带一份
// (pack-portable.js 拷进 core/data，随 extraResources 进包)：没装游戏、没有模组目录的电脑，光凭一个便携版就能把核心开起来
// (用户 2026-11-16 "我只需要把便携版的exe文件发给玩家他们就能直接打开核心并且玩了""他们也不用下载世界盒子的游戏")。
// 装好的编辑器直接用安装目录里的；便携版自己解在临时目录里、关掉就没了，所以拷到 EmpireCraftCore/data 再用，
// 打包时写的 .stamp 对得上就不再拷
function packagedData(install) {
  const source = process.resourcesPath ? path.join(process.resourcesPath, "core", "data") : null;
  if (!source || !fs.existsSync(path.join(source, "mod.json"))) return null;
  // 装好的编辑器：就用安装目录里的那份，不另拷
  if (!isPortable()) return source;
  const target = path.join(coreRoot(), "data");
  const stamp = dir => {
    try {
      return fs.readFileSync(path.join(dir, ".stamp"), "utf8");
    } catch {
      return null;
    }
  };
  const want = stamp(source);
  if (want && stamp(target) === want) return target;
  if (!install) return target;
  try {
    const stage = target + ".copy";
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.rmSync(stage, { recursive: true, force: true });
    fs.cpSync(source, stage, { recursive: true });
    // 没有模组目录的电脑上，编辑器改的就是这份数据(见 main.js 的 resolveModRoot)：换新版数据时把旧的留一份在 data.old，
    // 改过的政体、文化还找得回来
    if (fs.existsSync(target)) {
      fs.rmSync(target + ".old", { recursive: true, force: true });
      fs.renameSync(target, target + ".old");
    }
    fs.renameSync(stage, target);
    return target;
  } catch {
    return fs.existsSync(path.join(target, "mod.json")) ? target : source;
  }
}

// 核心读哪份数据：用的是编辑器自带的核心就配自带的那份数据(同一回打包出来的，对得上)；别的来源用模组目录，
// 没有模组目录时也退到自带的数据
function dataFor(modRoot, source, install = false) {
  if (source === "editor") {
    const own = packagedData(install);
    if (own) return own;
  }
  return modRoot || packagedData(install);
}

function readPort() {
  try {
    const config = JSON.parse(fs.readFileSync(path.join(stateDir(), "server.json"), "utf8").replace(/^﻿/, ""));
    const port = Number(config.port);
    return port > 0 && port < 65536 ? port : DEFAULT_PORT;
  } catch {
    return DEFAULT_PORT;
  }
}

// 核心的存档：world.json 开头就是这一局的时刻和存的时间，只读开头一小段
function readSave() {
  const file = path.join(stateDir(), "save", "world.json");
  try {
    if (!fs.existsSync(file)) return null;
    const handle = fs.openSync(file, "r");
    const buffer = Buffer.alloc(2048);
    const read = fs.readSync(handle, buffer, 0, buffer.length, 0);
    fs.closeSync(handle);
    const head = buffer.toString("utf8", 0, read);
    const time = /"world_time"\s*:\s*([0-9.]+)/.exec(head);
    const saved = /"saved"\s*:\s*"([^"]+)"/.exec(head);
    const scenario = /"scenario"\s*:\s*"([^"]*)"/.exec(head);
    const worldTime = time ? Number(time[1]) : 0;
    // 一个月 5 秒世界时长、一年 60 秒
    return {
      year: Math.floor(worldTime / 60) + 1,
      month: Math.floor(worldTime / 5) % 12 + 1,
      saved: saved ? saved[1] : new Date(fs.statSync(file).mtimeMs).toISOString(),
      scenario: scenario ? scenario[1] : ""
    };
  } catch {
    return null;
  }
}

function portOpen(port) {
  return new Promise(resolve => {
    const socket = net.connect({ host: "127.0.0.1", port });
    const done = open => { socket.destroy(); resolve(open); };
    socket.setTimeout(400);
    socket.once("connect", () => done(true));
    socket.once("timeout", () => done(false));
    socket.once("error", () => done(false));
  });
}

// 核心的进程在不在(不管端口通没通)。核心读存档要一阵子，这段时间端口还没开：只看端口会以为它没起来，
// 再点一回就起了第二个，两个核心抢同一份存档、同一个端口(2026-11-16 用户连点两回，后一个报端口只开得了本机退了)
// 只认"就是这份程序"起的进程(按程序路径比)：别处的核心(游戏拉起的那份、另一处装的)各管各的
function coreAlive(exe) {
  if (!exe || process.platform !== "win32") return Promise.resolve(false);
  return new Promise(resolve => {
    execFile("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
      "Get-Process EmpireCraftServer -ErrorAction SilentlyContinue | ForEach-Object { $_.Path }"],
      { encoding: "utf8", windowsHide: true, timeout: 6000 }, (error, stdout) => {
        if (error) return resolve(false);
        const want = path.resolve(exe).toLowerCase();
        resolve(String(stdout).split(String.fromCharCode(10)).some(line => line.trim() && path.resolve(line.trim()).toLowerCase() === want));
      });
  });
}

async function status(modRoot) {
  const { exe, source } = locate(modRoot);
  const port = readPort();
  const running = await portOpen(port);
  return {
    supported: process.platform === "win32",
    exe, source, state: stateDir(), port, save: readSave(),
    running,
    // 进程起来了、端口还没通：正在读存档
    starting: !running && await coreAlive(exe)
  };
}

// 拉起核心。已经开着(端口通)的不再开第二个。起来后两秒内就退了的算没起来(4 = 端口被占)
async function launch(modRoot) {
  if (process.platform !== "win32") return { ok: false, error: "只在 Windows 上能直接打开核心。" };
  const { exe, source } = locate(modRoot, true);
  if (!exe) return { ok: false, error: "没找到服务器核心。用「打包便携版」打出来的编辑器自带核心；也可以更新到带核心的模组版本。" };
  const data = dataFor(modRoot, source, true);
  if (!data) return { ok: false, error: "没找到核心要读的模组数据(这份编辑器没带数据，电脑上也没有模组目录)。" };
  const port = readPort();
  if (await portOpen(port)) return { ok: true, running: true, port };
  if (await coreAlive(exe)) return { ok: true, running: false, starting: true, port };
  const state = stateDir();
  fs.mkdirSync(state, { recursive: true });
  const env = { ...process.env };
  delete env.EMPIRECRAFT_RELAUNCH;
  return new Promise(resolve => {
    let settled = false;
    const finish = result => { if (!settled) { settled = true; resolve(result); } };
    let child;
    try {
      child = spawn(exe, ["--data", data, "--state", state, "--no-console"], {
        // 不能带 windowsHide：那是让系统以"隐藏窗口"拉起它，核心头一回显示自己的窗口会被系统吃掉。
        // detached 的进程本来就不分配控制台，不会多出黑色命令行窗
        cwd: path.dirname(exe), env, detached: true, stdio: "ignore", windowsHide: false
      });
    } catch (error) {
      finish({ ok: false, error: error.message });
      return;
    }
    child.once("error", error => finish({ ok: false, error: error.message }));
    child.once("exit", code => finish({
      ok: false,
      error: code === 4 ? `端口 ${port} 被占着(游戏里的终端还开着？)，核心没起来。` : `核心刚起来就退了(退出码 ${code})。`
    }));
    setTimeout(() => {
      if (settled) return;
      child.removeAllListeners("exit");
      child.unref();
      finish({ ok: true, running: false, port });
    }, 2000);
  });
}

// 桌面快捷方式要用的那几样(真正写 .lnk 的是 main.js，用 Electron 的 shell.writeShortcutLink)
function shortcut(modRoot) {
  const { exe, source } = locate(modRoot, true);
  const data = exe ? dataFor(modRoot, source, true) : null;
  if (!exe || !data) return null;
  return {
    target: exe,
    cwd: path.dirname(exe),
    args: `--data "${data}" --state "${stateDir()}" --no-console`,
    description: "帝国盒子 · 服务器核心"
  };
}

module.exports = {
  status, launch, shortcut, stateDir,
  ownData: () => packagedData(true),
  // 这份程序起的核心还在不在跑(更新编辑器前要先停下它)
  alive: modRoot => coreAlive(locate(modRoot).exe)
};
