// 服务器核心：不开游戏，直接从编辑器把核心开起来(用户 2026-11-16 "如何在不打开游戏的情况下直接打开核心ui"
// "然后通过核心载入存档""我的建议是用政体编辑器打开""然后政体编辑器可以加一个将核心图标加入到桌面的选项")。
// 核心是游戏里「用服务器核心」用的同一个程序、同一个存放目录(见 Scripts/Terminal/CoreHost.cs)：
//   · 程序：模组包里自带一份(<模组目录>/RegimeEditor/core/win-x64，一键发布时打进去的)，用之前拷到
//     <游戏数据目录>/EmpireCraftCore/bin/current 再跑(核心跑着时不占着模组目录里的文件，游戏更新模组照常能换)；
//     同目录 core.path 写着别的目录时先用它；
//   · 存放目录：<游戏数据目录>/EmpireCraftCore/state，里头 save/ 是核心的存档(游戏交给核心的那一局)，有就接着跑；
// 这里不带 --quiet 拉起，所以核心自己出窗口和托盘图标(见 .server/Server/CoreShell.cs)；带 --no-console，核心是控制台程序，
// 不带的话双击快捷方式会多出一个黑色命令行窗(带上它输出改写进 <存放目录>/core.log)。只在 Windows 上有用
const fs = require("node:fs");
const os = require("node:os");
const net = require("node:net");
const path = require("node:path");
const { spawn } = require("node:child_process");

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

// 模组包里自带的核心：拷到 bin/current(已经拷过、主程序大小和修改时间都对得上的不再拷)。返回拷好的那份主程序；
// 包里没有自带的返回 null；拷不成(旧的那份正跑着)就接着用旧的。install 为假时只看不拷(界面上显示用)
function bundledExe(modRoot, install) {
  if (!modRoot) return null;
  const bundled = path.join(modRoot, "RegimeEditor", "core", "win-x64");
  const source = path.join(bundled, EXE);
  if (!fs.existsSync(source)) return null;
  // 目录名不带版本：桌面快捷方式指着它，模组更新后还是这个位置(游戏每回启动也会把它对一遍，见 CoreHost.InstallBundled)
  const target = path.join(coreRoot(), "bin", "current");
  const have = path.join(target, EXE);
  try {
    const a = fs.statSync(source);
    const b = fs.existsSync(have) ? fs.statSync(have) : null;
    if (b && a.size === b.size && Math.abs(a.mtimeMs - b.mtimeMs) < 2000) return have;
    if (!install) return b ? have : source;
    const stage = target + ".copy";
    fs.rmSync(stage, { recursive: true, force: true });
    fs.cpSync(bundled, stage, { recursive: true, preserveTimestamps: true });
    fs.rmSync(target, { recursive: true, force: true });
    fs.renameSync(stage, target);
    return have;
  } catch {
    return fs.existsSync(have) ? have : source;
  }
}

function findExe(modRoot, install = false) {
  const root = coreRoot();
  try {
    const pointer = fs.readFileSync(path.join(root, "core.path"), "utf8").replace(/^﻿/, "").trim();
    const found = exeIn(pointer);
    if (found) return found;
  } catch {
    // 没有 core.path
  }
  const bundled = bundledExe(modRoot, install);
  if (bundled) return bundled;
  // 下载下来的各版本里挑最新的
  try {
    const bin = path.join(root, "bin");
    const versions = fs.readdirSync(bin, { withFileTypes: true }).filter(e => e.isDirectory())
      .map(e => ({ dir: path.join(bin, e.name), time: fs.statSync(path.join(bin, e.name)).mtimeMs }))
      .sort((a, b) => b.time - a.time);
    for (const version of versions) {
      const found = exeIn(version.dir);
      if (found) return found;
    }
  } catch {
    // 没下载过
  }
  // 从源码发布到游戏目录旁边的(开发时)
  return modRoot ? exeIn(path.resolve(modRoot, "..", "..", "EmpireCraftCore", "win-x64")) : null;
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

async function status(modRoot) {
  const exe = findExe(modRoot);
  const port = readPort();
  return {
    supported: process.platform === "win32",
    exe, state: stateDir(), port, save: readSave(),
    running: await portOpen(port)
  };
}

// 拉起核心。已经开着(端口通)的不再开第二个。起来后两秒内就退了的算没起来(4 = 端口被占)
async function launch(modRoot) {
  if (process.platform !== "win32") return { ok: false, error: "只在 Windows 上能直接打开核心。" };
  const exe = findExe(modRoot, true);
  if (!exe) return { ok: false, error: "没找到服务器核心。更新到带核心的模组版本，或在游戏的模组设置里打开一次「用服务器核心」。" };
  if (!modRoot) return { ok: false, error: "没找到模组目录。" };
  const port = readPort();
  if (await portOpen(port)) return { ok: true, running: true, port };
  const state = stateDir();
  fs.mkdirSync(state, { recursive: true });
  const env = { ...process.env };
  delete env.EMPIRECRAFT_RELAUNCH;
  return new Promise(resolve => {
    let settled = false;
    const finish = result => { if (!settled) { settled = true; resolve(result); } };
    let child;
    try {
      child = spawn(exe, ["--data", modRoot, "--state", state, "--no-console"], {
        cwd: path.dirname(exe), env, detached: true, stdio: "ignore", windowsHide: true
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
  const exe = findExe(modRoot, true);
  if (!exe || !modRoot) return null;
  return {
    target: exe,
    cwd: path.dirname(exe),
    args: `--data "${modRoot}" --state "${stateDir()}" --no-console`,
    description: "帝国盒子 · 服务器核心"
  };
}

module.exports = { status, launch, shortcut, stateDir };
