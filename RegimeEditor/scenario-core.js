// 剧本编辑器的文件读写(纯 Node，不依赖 Electron)：桌面版走 main.js 的 IPC，浏览器版走 scenario-server.js 的 HTTP，
// 两边都调这里。只读模组目录下的 WorldMaps(预设剧本与公共数据)，只写自定剧本文件夹正下方一层的 scenario.json 与 preview.png。
// 自定剧本的位置同游戏(WorldMapData.CustomFolder)：加载器的 mods_config/EmpireCraft/scenarios/<剧本名>/scenario.json
const fs = require("node:fs/promises");
const syncFs = require("node:fs");
const path = require("node:path");
const os = require("node:os");

const CUSTOM_FILE = "scenario.json";
const PREVIEW_FILE = "preview.png";

// 自定剧本文件夹可能在哪儿：指定的 → 环境变量 → 游戏存档目录下的 mods_config(各系统) → 游戏目录下的 mods_config
function scenarioDirCandidates(modRoot) {
  const home = os.homedir();
  const tail = ["mods_config", "EmpireCraft", "scenarios"];
  // 顺序：Windows、Linux、macOS 的游戏存档目录，再是游戏目录(模组目录上两层)
  const list = [
    path.join(home, "AppData", "LocalLow", "mkarpenko", "WorldBox", ...tail),
    path.join(home, ".config", "unity3d", "mkarpenko", "WorldBox", ...tail),
    path.join(home, "Library", "Application Support", "mkarpenko", "WorldBox", ...tail)
  ];
  if (modRoot) list.push(path.join(modRoot, "..", "..", ...tail));
  return list.map(item => path.resolve(item));
}

// 用哪个：指定了就用指定的，再是环境变量 EMPIRECRAFT_SCENARIOS；否则取头一个已经存在的；
// 都没有时取这个系统上游戏会用的那个(保存时再建)
function resolveScenarioDir(modRoot, override) {
  if (override) return path.resolve(override);
  if (process.env.EMPIRECRAFT_SCENARIOS) return path.resolve(process.env.EMPIRECRAFT_SCENARIOS);
  const candidates = scenarioDirCandidates(modRoot);
  const found = candidates.find(dir => syncFs.existsSync(dir));
  if (found) return found;
  return process.platform === "win32" ? candidates[0] : process.platform === "darwin" ? candidates[2] : candidates[1];
}

// 剧本文件夹名：只认正下方一层，不许带路径
function safeFolder(name) {
  const text = String(name || "").trim();
  if (!text || text === "." || text === ".." || /[<>:"/\\|?*\x00-\x1f]/.test(text)) throw new Error(`剧本文件夹名不能用：${name}`);
  return text;
}

function folderPath(dir, folder) {
  const target = path.join(dir, safeFolder(folder));
  if (path.dirname(path.resolve(target)) !== path.resolve(dir)) throw new Error(`路径不在剧本文件夹内：${folder}`);
  return target;
}

function stripBom(text) {
  return String(text || "").replace(/^﻿/, "");
}

function worldDir(modRoot) {
  return path.join(modRoot, "WorldMaps");
}

// 列表只给名、年份这些要紧的，不把整份剧本读给页面
function summarize(text) {
  const era = JSON.parse(stripBom(text));
  return {
    zh: era.zh || era.en || "", year: era.year || 0, focus: era.focus || "east", order: era.order || 0, author: era.author || "",
    base: era.base || "", polities: (era.polities || []).length, cities: Array.isArray(era.cities) ? era.cities.length : -1,
    imported: !!era.map, desc: era.dzh || era.den || ""
  };
}

async function listScenarios(modRoot, dir) {
  const custom = [];
  if (syncFs.existsSync(dir)) {
    const entries = (await fs.readdir(dir, { withFileTypes: true })).filter(e => e.isDirectory()).map(e => e.name).sort();
    for (const folder of entries) {
      const file = path.join(dir, folder, CUSTOM_FILE);
      if (!syncFs.existsSync(file)) continue;
      const item = { folder, mtime: (await fs.stat(file)).mtimeMs };
      try { Object.assign(item, summarize(await fs.readFile(file, "utf8"))); }
      catch (error) { item.error = error.message; }
      if (!item.zh) item.zh = folder;
      custom.push(item);
    }
  }
  const presets = [];
  const eras = path.join(worldDir(modRoot), "eras");
  if (syncFs.existsSync(eras)) {
    for (const name of (await fs.readdir(eras)).filter(n => n.toLowerCase().endsWith(".json")).sort()) {
      const item = { id: name.slice(0, -5) };
      try { Object.assign(item, summarize(await fs.readFile(path.join(eras, name), "utf8"))); }
      catch (error) { item.error = error.message; }
      presets.push(item);
    }
    presets.sort((a, b) => (a.order - b.order) || (a.year - b.year));
  }
  return { dir, exists: syncFs.existsSync(dir), custom, presets };
}

async function readScenario(dir, folder) {
  const file = path.join(folderPath(dir, folder), CUSTOM_FILE);
  return { folder, path: file, text: stripBom(await fs.readFile(file, "utf8")) };
}

async function readPreset(modRoot, id) {
  if (!/^[\w-]+$/.test(String(id || ""))) throw new Error(`预设编号不对：${id}`);
  const file = path.join(worldDir(modRoot), "eras", id + ".json");
  return { id, path: file, text: stripBom(await fs.readFile(file, "utf8")) };
}

// 占一个没用过的文件夹名(重名加 _2、_3，同游戏的 CopyAsCustom、CreateBlank)，先把空文件夹建出来
async function reserveFolder(dir, base) {
  const stem = safeFolder(base);
  await fs.mkdir(dir, { recursive: true });
  let name = stem;
  for (let i = 2; syncFs.existsSync(path.join(dir, name)); i++) name = `${stem}_${i}`;
  await fs.mkdir(path.join(dir, name));
  return { folder: name, path: path.join(dir, name) };
}

// 写回 scenario.json：先确认是合法 JSON(读不出来的剧本游戏里整个不显示)，写临时文件再改名，免得写一半断了把原文件弄坏
async function writeScenario(dir, folder, text) {
  const target = folderPath(dir, folder);
  JSON.parse(String(text));
  await fs.mkdir(target, { recursive: true });
  const file = path.join(target, CUSTOM_FILE);
  const temp = file + ".tmp";
  await fs.writeFile(temp, String(text), "utf8");
  await fs.rename(temp, file);
  return { folder, path: file };
}

// 缩略图 preview.png(世界地图窗口的剧本卡用)
async function writePreview(dir, folder, bytes) {
  const target = folderPath(dir, folder);
  const data = Buffer.from(bytes);
  if (data.length < 8 || data.readUInt32BE(0) !== 0x89504e47) throw new Error("缩略图不是 PNG");
  await fs.mkdir(target, { recursive: true });
  const file = path.join(target, PREVIEW_FILE);
  await fs.writeFile(file, data);
  return { path: file };
}

// 整个剧本文件夹里的文件(发到独立服务器用)：scenario.json、导入地图的 map.png 与颜色对照表 map_colors.txt、缩略图 preview.png，
// 连同放在里面的别的文件；文本(json、txt、md、csv)给 text，其余给 base64。路径用 / 分隔、相对剧本文件夹。
// 服务器一次只收 16MB(base64 要胀三分之一)，文件夹超过 12MB 的不发
const TEXT_FILES = new Set([".json", ".txt", ".md", ".csv"]);
const FOLDER_LIMIT = 12 * 1024 * 1024;

async function readFolderFiles(dir, folder) {
  const root = folderPath(dir, folder);
  if (!syncFs.existsSync(path.join(root, CUSTOM_FILE))) throw new Error(`剧本文件夹里没有 ${CUSTOM_FILE}：${folder}`);
  const files = [];
  let total = 0;
  const walk = async (current, prefix, depth) => {
    for (const entry of (await fs.readdir(current, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name))) {
      if (entry.name.startsWith(".") || entry.name.endsWith(".tmp")) continue;
      const full = path.join(current, entry.name), rel = prefix ? `${prefix}/${entry.name}` : entry.name;
      if (entry.isDirectory()) { if (depth < 3) await walk(full, rel, depth + 1); continue; }
      if (!entry.isFile()) continue;
      const data = await fs.readFile(full);
      total += data.length;
      if (total > FOLDER_LIMIT) throw new Error(`剧本文件夹太大了(超过 ${FOLDER_LIMIT / 1024 / 1024}MB)，发不过去：把用不上的文件挪出去`);
      if (TEXT_FILES.has(path.extname(entry.name).toLowerCase())) files.push({ path: rel, text: stripBom(data.toString("utf8")), size: data.length });
      else files.push({ path: rel, base64: data.toString("base64"), size: data.length });
    }
  };
  await walk(root, "", 0);
  // scenario.json 排最前
  files.sort((a, b) => (a.path === CUSTOM_FILE ? -1 : b.path === CUSTOM_FILE ? 1 : 0));
  return files;
}

// 剧本文件夹里的导入地图 map.png 与颜色对照表 map_colors.txt(见 WorldMapImporter)：只许读写这两个名字
const FOLDER_FILES = new Set(["map.png", "map_colors.txt"]);

async function readFolderFile(dir, folder, name) {
  if (!FOLDER_FILES.has(name)) throw new Error(`这个文件不让读：${name}`);
  const file = path.join(folderPath(dir, folder), name);
  if (!syncFs.existsSync(file)) return { exists: false };
  return { exists: true, base64: (await fs.readFile(file)).toString("base64") };
}

async function writeFolderFile(dir, folder, name, bytes) {
  if (!FOLDER_FILES.has(name)) throw new Error(`这个文件不让写：${name}`);
  const target = folderPath(dir, folder);
  await fs.mkdir(target, { recursive: true });
  const file = path.join(target, name);
  await fs.writeFile(file, Buffer.from(bytes));
  return { path: file };
}

// 删除自定剧本：整个剧本文件夹(连同预览图、导入的地图)。同游戏的 WorldMapData.CanDeleteFolder：
// 只认剧本文件夹正下方一层、里面确有 scenario.json 的目录，路径出错也删不到别处
async function deleteScenario(dir, folder) {
  const target = folderPath(dir, folder);
  if (!syncFs.existsSync(path.join(target, CUSTOM_FILE))) throw new Error(`这里没有自定剧本：${folder}`);
  await fs.rm(target, { recursive: true });
  return { folder };
}

// 各文化的城名规则与词库(放城、放关隘时按所在地区的文化起名，同游戏的 ScenarioNaming)：
// CultureRule.jsonc 的 setting.City(groups、rule)，词库 Locales/Cultures/Culture_<文化>/<文化><词库>.csv(一行 key,cz,en,ch)。
// 返回 {文化: {rule: [组名…], groups: {组名: [[简体, 英文, 繁体]…]}}}
function stripJsonComments(text) {
  let out = "";
  const s = stripBom(text);
  for (let i = 0; i < s.length;) {
    if (s[i] === '"') {
      let j = i + 1;
      while (j < s.length && s[j] !== '"') j += s[j] === "\\" ? 2 : 1;
      out += s.slice(i, j + 1);
      i = j + 1;
    } else if (s.startsWith("//", i)) { const end = s.indexOf("\n", i); i = end < 0 ? s.length : end; }
    else if (s.startsWith("/*", i)) { const end = s.indexOf("*/", i + 2); i = end < 0 ? s.length : end + 2; }
    else out += s[i++];
  }
  return out.replace(/,(\s*[}\]])/g, "$1");
}

async function cityNaming(modRoot) {
  const root = path.join(modRoot, "Locales", "Cultures"), result = {}, cache = new Map();
  // 词库约定："_"开头换成空格，单个"_"是空串
  const clean = cell => { const word = cell.trim(); return word === "_" ? "" : word.startsWith("_") ? " " + word.slice(1) : word; };
  const words = async (culture, group) => {
    let owner = culture, name = String(group || "").trim();
    const split = name.indexOf(":");
    if (split > 0) { owner = name.slice(0, split).trim(); name = name.slice(split + 1).trim(); }
    const file = path.join(root, `Culture_${owner}`, owner + name + ".csv");
    if (cache.has(file)) return cache.get(file);
    const list = [];
    if (syncFs.existsSync(file))
      for (const line of stripBom(await fs.readFile(file, "utf8")).split(/\r?\n/).slice(1)) {
        const cells = line.split(",");
        if (cells.length < 2 || !cells[1].trim()) continue;
        const zh = clean(cells[1]);
        list.push([zh, cells.length > 2 && cells[2].trim() ? clean(cells[2]) : zh, cells.length > 3 && cells[3].trim() ? clean(cells[3]) : zh]);
      }
    cache.set(file, list);
    return list;
  };
  for (const culture of await listCultureKeys(modRoot)) {
    try {
      const file = path.join(root, `Culture_${culture}`, "CultureRule.jsonc");
      if (!syncFs.existsSync(file)) continue;
      const data = JSON.parse(stripJsonComments(await fs.readFile(file, "utf8")));
      const city = data?.setting?.City || data?.City;
      if (!city?.groups || !Array.isArray(city.rule)) continue;
      const groups = {};
      for (const token of city.rule) if (city.groups[token] !== undefined) groups[token] = await words(culture, city.groups[token]);
      result[culture] = { rule: city.rule, groups };
    } catch { /* 这个文化的规则读不出来就不按它起名 */ }
  }
  return result;
}

// 公共数据(WorldMaps 下的 cities.json、earth.bytes、rivers.bytes、wilderness.json ……)：只给这个目录里的文件
async function readWorldFile(modRoot, name) {
  if (!/^[\w.-]+$/.test(String(name || "")) || String(name).includes("..")) throw new Error(`文件名不对：${name}`);
  return fs.readFile(path.join(worldDir(modRoot), name));
}

// 模组里有哪些文化(Locales/Cultures/Culture_<键>)，同游戏的 CultureService.CultureKeys
async function listCultureKeys(modRoot) {
  const root = path.join(modRoot, "Locales", "Cultures");
  if (!syncFs.existsSync(root)) return [];
  return (await fs.readdir(root, { withFileTypes: true }))
    .filter(e => e.isDirectory() && e.name.startsWith("Culture_")).map(e => e.name.slice("Culture_".length)).sort();
}

module.exports = {
  CUSTOM_FILE, scenarioDirCandidates, resolveScenarioDir, folderPath, listScenarios, readScenario, readPreset, reserveFolder,
  writeScenario, writePreview, readWorldFile, listCultureKeys, readFolderFiles, readFolderFile, writeFolderFile, deleteScenario, cityNaming
};
