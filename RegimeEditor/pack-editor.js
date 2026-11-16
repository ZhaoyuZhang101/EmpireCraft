// 打包编辑器(打包安装程序.bat、打包便携版.bat 调它)：编服务器核心 → 收拾核心要读的模组数据 → electron-builder 打包。
//   node pack-editor.js nsis      安装程序：装到自己挑的位置，加开始菜单和桌面图标，之后打开就快(用户 2026-11-16
//                                 "便携版打开的太慢了有没有安装程序""让我安装在指定的地方并且添加到菜单或者桌面去")
//   node pack-editor.js portable  便携版：一个 exe 不用装，但每回打开都要先把自己解到临时目录，慢
// 打出来的编辑器自带核心和数据(见 package.json 的 extraResources、core-launcher.js)：发给别人，不装游戏、不装模组也能把核心开起来。
// 三步合成一条总进度，一行实时刷新(用户 2026-11-16 "我需要打包的进度条")：
//   · 拷数据按文件数算，是实打实的进度；
//   · 编核心、打便携版这两步工具自己不报百分比，按上一回的耗时估(记在 dist/pack-times.json)，到九成五停住等它真做完；
//   · 工具的输出不往屏幕上刷(会把进度条冲乱)，都记在 dist/pack.log，出错时把最后几十行打出来。
const fs = require("node:fs");
const path = require("node:path");
const { spawn } = require("node:child_process");

const here = __dirname;
const modRoot = path.resolve(here, "..");
const coreOut = path.join(here, "core", "win-x64");
const dataOut = path.join(here, "core", "data");
const distDir = path.join(here, "dist");
const logFile = path.join(distDir, "pack.log");
const timesFile = path.join(distDir, "pack-times.json");
const version = JSON.parse(fs.readFileSync(path.join(here, "package.json"), "utf8")).version;

// 打哪一种：安装程序(nsis)还是便携版(portable)
const kinds = {
  nsis: { label: "安装程序", file: `EmpireCraft Editor Setup ${version}.exe`, last: "(压成安装程序)" },
  portable: { label: "便携版", file: `EmpireCraft Editor ${version}.exe`, last: "(压成一个 exe)" }
};
const kindName = (process.argv[2] || "nsis").toLowerCase();
const kind = kinds[kindName];
if (!kind) {
  console.log(`不认得的打包种类「${process.argv[2]}」：用 nsis(安装程序) 或 portable(便携版)。`);
  process.exit(1);
}

const stages = [
  { key: "core", name: "编译服务器核心", weight: 30, estimate: 45 },
  { key: "data", name: "收拾核心数据", weight: 5, estimate: 3 },
  { key: "pack-" + kindName, name: "打成" + kind.label, weight: 65, estimate: 120 }
];

// ---------- 进度条 ----------
const tty = !!process.stdout.isTTY;
const began = Date.now();
let current = 0, fraction = 0, detail = "", lastWidth = 0, timer = null, stageBegan = Date.now(), estimated = false;

function clock(ms) {
  const s = Math.floor(ms / 1000);
  return String(Math.floor(s / 60)).padStart(2, "0") + ":" + String(s % 60).padStart(2, "0");
}

function draw() {
  const stage = stages[current];
  // 按耗时估的那两步：照上一回的耗时走，九成五封顶
  if (estimated) fraction = Math.max(fraction, Math.min(0.95, (Date.now() - stageBegan) / 1000 / stage.estimate));
  let total = fraction * stage.weight;
  for (let i = 0; i < current; i++) total += stages[i].weight;
  if (!tty) return;
  const width = 28, filled = Math.round(width * total / 100);
  const line = `[${"#".repeat(filled)}${"-".repeat(width - filled)}] ${String(Math.floor(total)).padStart(3)}%  ` +
    `${current + 1}/${stages.length} ${stage.name}${detail ? " " + detail : ""}  ${clock(Date.now() - began)}`;
  // 中文一个字占两格：按显示宽度补空格，把上一回留下的尾巴盖掉
  const shown = [...line].reduce((n, ch) => n + (ch.charCodeAt(0) > 0x2e7f ? 2 : 1), 0);
  process.stdout.write("\r" + line + " ".repeat(Math.max(0, lastWidth - shown)));
  lastWidth = shown;
}

function enter(index, byTime) {
  current = index;
  fraction = 0;
  detail = "";
  estimated = byTime;
  stageBegan = Date.now();
  if (!tty) console.log(`${index + 1}/${stages.length} ${stages[index].name}...`);
  draw();
}

function leave(times) {
  fraction = 1;
  estimated = false;
  times[stages[current].key] = Math.max(1, Math.round((Date.now() - stageBegan) / 1000));
  draw();
}

// ---------- 跑外面的工具 ----------
let log = null;

function run(command, args, cwd, onLine) {
  return new Promise((resolve, reject) => {
    log.write(`\n> ${command} ${args.join(" ")}\n`);
    const child = spawn(command, args, { cwd, windowsHide: true });
    let rest = "";
    const feed = chunk => {
      log.write(chunk);
      rest += chunk.toString("utf8");
      const lines = rest.split(/\r?\n/);
      rest = lines.pop();
      if (onLine) for (const line of lines) onLine(line);
    };
    child.stdout.on("data", feed);
    child.stderr.on("data", feed);
    child.once("error", reject);
    child.once("exit", code => code === 0 ? resolve() : reject(new Error(`${path.basename(command)} 退出码 ${code}`)));
  });
}

// ---------- 核心要读的数据 ----------
// 清单同 update_server.bat 顶上的 DATA_DIRS(服务器部署用的那份)，从那里读，免得两边各记一份对不上
function dataFolders() {
  try {
    const text = fs.readFileSync(path.join(modRoot, "update_server.bat"), "utf8");
    const found = /set "DATA_DIRS=([^"]+)"/.exec(text);
    if (found) return found[1].trim().split(/\s+/);
  } catch {
    // 读不到就用下面这份
  }
  return ["Terminal", "WorldMaps", "Locales", "Technology", "InstitutionTrees", "Scripts"];
}

function listData() {
  const files = [];
  const skip = new Set([".cs", ".meta", ".pdb"]);
  const walk = relative => {
    for (const entry of fs.readdirSync(path.join(modRoot, relative), { withFileTypes: true })) {
      const next = path.join(relative, entry.name);
      if (entry.isDirectory()) walk(next);
      else if (entry.isFile() && !skip.has(path.extname(entry.name).toLowerCase())) files.push(next);
    }
  };
  for (const folder of dataFolders()) if (fs.existsSync(path.join(modRoot, folder))) walk(folder);
  for (const entry of fs.readdirSync(modRoot, { withFileTypes: true }))
    if (entry.isFile() && entry.name.toLowerCase().endsWith(".json") && entry.name !== "debug_probe.local.json") files.push(entry.name);
  return files;
}

async function copyData() {
  fs.rmSync(dataOut, { recursive: true, force: true });
  const files = listData();
  let bytes = 0;
  for (let i = 0; i < files.length; i++) {
    const target = path.join(dataOut, files[i]);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.copyFileSync(path.join(modRoot, files[i]), target);
    bytes += fs.statSync(target).size;
    if (i % 50 === 0 || i === files.length - 1) {
      fraction = (i + 1) / files.length;
      detail = `${i + 1}/${files.length}`;
      draw();
      // 让出一下，进度条才画得出来
      await new Promise(resolve => setImmediate(resolve));
    }
  }
  // 编辑器照这个记号认数据换没换过(见 core-launcher.js 的 packagedData)
  const modVersion = JSON.parse(fs.readFileSync(path.join(modRoot, "mod.json"), "utf8").replace(/^﻿/, "")).version;
  fs.writeFileSync(path.join(dataOut, ".stamp"), `${modVersion} ${new Date().toISOString()}`, "utf8");
  if (!fs.existsSync(path.join(dataOut, "mod.json"))) throw new Error("数据没拷全：core/data 里没有 mod.json。");
  return { count: files.length, bytes };
}

// ---------- 主流程 ----------
async function main() {
  fs.mkdirSync(distDir, { recursive: true });
  log = fs.createWriteStream(logFile);
  const times = {};
  try {
    const last = JSON.parse(fs.readFileSync(timesFile, "utf8"));
    Object.assign(times, last);
    for (const stage of stages) if (last[stage.key] > 0) stage.estimate = last[stage.key];
  } catch {
    // 头一回打包，用默认的估计
  }
  console.log(`帝国盒子编辑器 ${version} · ${kind.label}(自带服务器核心)`);
  if (tty) timer = setInterval(draw, 200);

  enter(0, true);
  fs.rmSync(coreOut, { recursive: true, force: true });
  await run("dotnet", ["publish", path.join(modRoot, ".server", "EmpireCraft.Server.csproj"), "-c", "Release", "-r", "win-x64",
    "--self-contained", "true", "-p:DebugType=none", "-o", coreOut, "-v", "q", "-nologo"], here);
  if (!fs.existsSync(path.join(coreOut, "EmpireCraftServer.exe"))) throw new Error("核心没编出来：core/win-x64 里没有 EmpireCraftServer.exe。");
  leave(times);

  enter(1, false);
  const data = await copyData();
  leave(times);

  enter(2, true);
  // electron-builder 报到哪一步就至少走到哪儿；最费时间的是最后把所有东西压进一个 exe
  await run(process.execPath, [path.join(here, "node_modules", "electron-builder", "cli.js"), "--win", kindName, "--x64"], here, line => {
    if (/packaging/.test(line)) { fraction = Math.max(fraction, 0.1); detail = "(整理文件)"; }
    else if (/building\s+target=/.test(line)) { fraction = Math.max(fraction, 0.3); detail = kind.last; }
  });
  const output = path.join(distDir, kind.file);
  if (!fs.existsSync(output)) throw new Error(`没找到打出来的${kind.label}：${output}`);
  leave(times);

  clearInterval(timer);
  if (tty) process.stdout.write("\n");
  fs.writeFileSync(timesFile, JSON.stringify(times), "utf8");
  const mb = bytes => (bytes / 1048576).toFixed(1) + " MB";
  console.log(`打好了，用时 ${clock(Date.now() - began)}`);
  console.log(`  ${kind.label}：${output} (${mb(fs.statSync(output).size)})`);
  console.log(`  里头带着：服务器核心 + 核心数据 ${data.count} 个文件(${mb(data.bytes)})`);
}

main().then(() => log.end(), error => {
  clearInterval(timer);
  if (tty) process.stdout.write("\n");
  console.log(`没打成(停在「${stages[current].name}」)：${error.message}`);
  log.end(() => {
    try {
      const tail = fs.readFileSync(logFile, "utf8").split(/\r?\n/).filter(line => line.trim()).slice(-25);
      if (tail.length) console.log("---- 最后的输出 ----\n" + tail.join("\n"));
    } catch {
      // 日志读不了就算了
    }
    console.log(`完整输出在 ${logFile}`);
    process.exit(1);
  });
});
