"use strict";
// 把政体配置里新出现的键名追加进 Scripts/Regimes/RegimeSystem.cs 的枚举。编辑器自带(Electron 里就有 Node)，不用装 Python。
//
// 枚举的数值进存档，所以这里只在末尾追加，不排序、不删除、不改已有成员和注释：
//   RegimeType        ← 各 SystemConfig.json 最外层的政体键名
//   KingdomType       ← bureau_config.kingdoms 的键
//   CityType          ← bureau_config.cities 的键
//   ArmyOfficialType  ← bureau_config.armies 的键
// default_kingdom、city_type、secede_member_types、direct_member_types 只检查不追加：它们指向的类别
// 应当在某个政体的 kingdoms / cities 里定义，指错了多半是笔误，追加进枚举只会把错误藏起来。
//
// 命令行：node regime-enum-sync.js [模组根目录]
const fs = require("node:fs");
const path = require("node:path");

const ENUMS = ["RegimeType", "KingdomType", "CityType", "ArmyOfficialType"];
const IDENT = /^[A-Za-z_][A-Za-z0-9_]*$/;

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isBlank(text) {
  return text.trim() === "";
}

function findModRoot(start) {
  let current = path.resolve(start);
  while (true) {
    if (fs.existsSync(path.join(current, "Scripts", "Regimes", "RegimeSystem.cs"))) return current;
    const parent = path.dirname(current);
    if (parent === current) return path.resolve(start);
    current = parent;
  }
}

// 去掉 // 与 /* */ 注释和尾逗号；跳过字符串，免得把 "http://" 当成注释
function stripJsonComments(text) {
  const out = [];
  const n = text.length;
  let i = 0;
  while (i < n) {
    const c = text[i];
    if (c === '"') {
      let j = i + 1;
      while (j < n && text[j] !== '"') j += text[j] === "\\" ? 2 : 1;
      out.push(text.slice(i, j + 1));
      i = j + 1;
    } else if (text.startsWith("//", i)) {
      const e = text.indexOf("\n", i);
      i = e < 0 ? n : e;
    } else if (text.startsWith("/*", i)) {
      const e = text.indexOf("*/", i + 2);
      i = e < 0 ? n : e + 2;
    } else {
      out.push(c);
      i += 1;
    }
  }
  return out.join("").replace(/,(\s*[}\]])/g, "$1");
}

// 报错里给键名加引号，转义看不见的字符
function quote(value) {
  if (typeof value !== "string") return JSON.stringify(value);
  const q = value.includes("'") && !value.includes('"') ? '"' : "'";
  let out = q;
  for (const ch of value) {
    if (ch === "\\" || ch === q) out += "\\" + ch;
    else if (ch === "\n") out += "\\n";
    else if (ch === "\r") out += "\\r";
    else if (ch === "\t") out += "\\t";
    else if (ch < " " || ch === "\x7f") out += "\\x" + ch.charCodeAt(0).toString(16).padStart(2, "0");
    else out += ch;
  }
  return out + q;
}

// 配置目录下所有 SystemConfig.json，按路径逐段排序(不分大小写)，追加顺序每次都一样
function findConfigs(configsDir) {
  const files = [];
  const walk = (dir, parts) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (entry.isDirectory()) walk(path.join(dir, entry.name), [...parts, entry.name]);
      else if (entry.name.toLowerCase() === "systemconfig.json") files.push([...parts, entry.name]);
    }
  };
  walk(configsDir, []);
  const lower = (parts) => parts.map((p) => p.toLowerCase());
  files.sort((a, b) => {
    const x = lower(a), y = lower(b);
    for (let k = 0; k < Math.min(x.length, y.length); k++) {
      if (x[k] !== y[k]) return x[k] < y[k] ? -1 : 1;
    }
    return x.length - y.length;
  });
  return files;
}

function collect(configsDir) {
  const found = Object.fromEntries(ENUMS.map((name) => [name, []]));
  const refs = { KingdomType: [], CityType: [] };
  const problems = [];

  const add = (name, key, where) => {
    if (typeof key !== "string" || !IDENT.test(key)) problems.push(`${where}: ${quote(key)} 不是合法的枚举名，跳过`);
    else if (!found[name].includes(key)) found[name].push(key);
  };
  const values = (value) => (Array.isArray(value) ? value : value ? [value] : []);

  for (const parts of findConfigs(configsDir)) {
    const rel = parts.join("/");
    let data;
    try {
      data = JSON.parse(stripJsonComments(fs.readFileSync(path.join(configsDir, ...parts), "utf8").replace(/^﻿/, "")));
    } catch (error) {
      throw new Error(`${rel} 读取失败：${error.message}`);
    }
    if (!isObject(data)) throw new Error(`${rel} 最外层不是对象`);
    for (const [regimeKey, regime] of Object.entries(data)) {
      add("RegimeType", regimeKey, rel);
      if (!isObject(regime)) continue;
      const bureau = isObject(regime.bureau_config) ? regime.bureau_config : {};
      for (const [scope, enumName] of [["kingdoms", "KingdomType"], ["cities", "CityType"], ["armies", "ArmyOfficialType"]]) {
        if (!isObject(bureau[scope])) continue;
        for (const [key, setting] of Object.entries(bureau[scope])) {
          add(enumName, key, `${rel} ${regimeKey}.${scope}`);
          if (scope === "kingdoms" && isObject(setting) && setting.city_type) {
            refs.CityType.push([setting.city_type, `${rel} ${regimeKey}.kingdoms.${key}.city_type`]);
          }
        }
      }
      if (regime.default_kingdom) refs.KingdomType.push([regime.default_kingdom, `${rel} ${regimeKey}.default_kingdom`]);
      for (const field of ["secede_member_types", "direct_member_types"]) {
        for (const value of values(regime[field])) refs.KingdomType.push([value, `${rel} ${regimeKey}.${field}`]);
      }
    }
  }
  return { found, refs, problems };
}

// 返回 { members, lastSig: 最后一个有效字符的位置, close: 右花括号位置 }。跳过注释
function scanEnum(source, enumName) {
  const m = new RegExp(`\\benum\\s+${enumName}\\b`).exec(source);
  if (!m) throw new Error(`未找到枚举 ${enumName}`);
  const brace = source.indexOf("{", m.index + m[0].length);
  if (brace < 0) throw new Error(`枚举 ${enumName} 缺少起始花括号`);
  let i = brace + 1, depth = 1, lastSig = brace;
  const body = [];
  while (i < source.length) {
    if (source.startsWith("//", i)) {
      const e = source.indexOf("\n", i);
      i = e < 0 ? source.length : e;
      continue;
    }
    if (source.startsWith("/*", i)) {
      const e = source.indexOf("*/", i + 2);
      i = e < 0 ? source.length : e + 2;
      continue;
    }
    const c = source[i];
    if (c === "{") depth += 1;
    else if (c === "}") {
      depth -= 1;
      if (depth === 0) {
        const members = body.join("").split(",").map((part) => part.split("=")[0].trim()).filter(Boolean);
        return { members, lastSig, close: i };
      }
    }
    if (!isBlank(c)) lastSig = i;
    body.push(c);
    i += 1;
  }
  throw new Error(`枚举 ${enumName} 缺少结束花括号`);
}

function escapeRegExp(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function appendMembers(source, enumName, newMembers, nl) {
  if (!newMembers.length) return source;
  const { members, lastSig, close } = scanEnum(source, enumName);
  // 缩进照第一个成员所在行
  let indent = "    ";
  if (members.length) {
    const bodyStart = source.indexOf("{", source.indexOf("enum " + enumName)) + 1;
    const line = new RegExp(`(?:^|(?<=\\n))([ \\t]*)${escapeRegExp(members[0])}\\b`).exec(source.slice(bodyStart, close));
    if (line) indent = line[1];
  }
  const comma = ",{".includes(source[lastSig]) ? "" : ",";
  const lineStart = source.lastIndexOf("\n", close - 1) + 1;
  const added = newMembers.map((name) => `${indent}${name},${nl}`).join("");
  const [pos, text] = isBlank(source.slice(lineStart, close)) ? [lineStart, added] : [close, nl + added];
  return source.slice(0, lastSig + 1) + comma + source.slice(lastSig + 1, pos) + text + source.slice(pos);
}

// 同步一次。返回 { changed, lines: 每个枚举一行, problems: 配置里要留意的地方, text: 拼好的说明 }；读不了配置或枚举时抛错
function syncEnums(modRoot) {
  const baseDir = path.resolve(modRoot);
  const configsDir = path.join(baseDir, "Scripts", "Regimes", "Configs");
  const regimeSystem = path.join(baseDir, "Scripts", "Regimes", "RegimeSystem.cs");
  if (!fs.existsSync(configsDir)) throw new Error(`未找到配置目录: ${configsDir}`);
  if (!fs.existsSync(regimeSystem)) throw new Error(`未找到 RegimeSystem.cs: ${regimeSystem}`);

  const { found, refs, problems } = collect(configsDir);
  const raw = fs.readFileSync(regimeSystem);
  const bom = raw.length >= 3 && raw[0] === 0xef && raw[1] === 0xbb && raw[2] === 0xbf;
  const source = raw.toString("utf8").replace(/^﻿/, "");
  const nl = source.includes("\r\n") ? "\r\n" : "\n";

  const lines = [];
  const existingAll = {};
  let updated = source;
  for (const name of ENUMS) {
    const existing = scanEnum(updated, name).members;
    existingAll[name] = new Set(existing);
    const added = found[name].filter((key) => !existingAll[name].has(key));
    updated = appendMembers(updated, name, added, nl);
    for (const key of added) existingAll[name].add(key);
    const unused = existing.filter((key) => !found[name].includes(key));
    let line = `${name}: 已有 ${existing.length} 项`;
    line += added.length ? `，追加 ${added.length} 项(${added.join("、")})` : "，无新增";
    if (unused.length) line += `；配置里没用到但保留(旧存档要用)：${unused.join("、")}`;
    lines.push(line);
  }

  for (const [enumName, items] of Object.entries(refs)) {
    for (const [value, where] of items) {
      if (!existingAll[enumName].has(value)) {
        problems.push(`${where} 指向的 ${value} 不在 ${enumName} 里(也没有哪个政体定义它)，游戏读这个政体会出错`);
      }
    }
  }

  const changed = updated !== source;
  if (changed) {
    fs.writeFileSync(regimeSystem, Buffer.concat([bom ? Buffer.from([0xef, 0xbb, 0xbf]) : Buffer.alloc(0), Buffer.from(updated, "utf8")]));
  }
  const head = changed ? "同步完成，RegimeSystem.cs 已追加新成员，请重新编译模组:" : "同步完成，代码枚举已是最新，没有改动:";
  const text = [head, ...lines, ...problems.map((problem) => "注意：" + problem)].join("\n");
  return { changed, lines, problems, text };
}

module.exports = { syncEnums, stripJsonComments, scanEnum, appendMembers, findModRoot };

if (require.main === module) {
  try {
    const root = process.argv[2] ? path.resolve(process.argv[2]) : findModRoot(__dirname);
    console.log(syncEnums(root).text);
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
