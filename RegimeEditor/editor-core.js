// 编辑器的文件读写逻辑(纯 Node，不依赖 Electron，方便单独测试)
const fs = require("node:fs/promises");
const path = require("node:path");
const syncFs = require("node:fs");

// ---------- 文化 / 通用文件 ----------

function culturesRoot(modRoot) {
  return path.join(modRoot, "Locales", "Cultures");
}

// 只允许读写模组目录里面的文件
function inModRoot(modRoot, relPath) {
  const target = path.resolve(modRoot, String(relPath || "."));
  const rel = path.relative(modRoot, target);
  if (rel.startsWith("..") || path.isAbsolute(rel)) throw new Error(`路径不在模组目录内：${relPath}`);
  return target;
}

function parseJsonLoose(text) {
  return JSON.parse(stripJsonCommentsSafe(text));
}

// 去注释时跳过字符串，避免把 "http://" 之类当成注释
function stripJsonCommentsSafe(text) {
  let out = "";
  let i = 0;
  const s = String(text || "").replace(/^﻿/, "");
  while (i < s.length) {
    const c = s[i];
    if (c === '"') {
      let j = i + 1;
      while (j < s.length && s[j] !== '"') j += s[j] === "\\" ? 2 : 1;
      out += s.slice(i, j + 1);
      i = j + 1;
    } else if (s.startsWith("//", i)) {
      const end = s.indexOf("\n", i);
      i = end < 0 ? s.length : end;
    } else if (s.startsWith("/*", i)) {
      const end = s.indexOf("*/", i + 2);
      i = end < 0 ? s.length : end + 2;
    } else {
      out += c;
      i++;
    }
  }
  return out.replace(/,(\s*[}\]])/g, "$1");
}

async function listCultures(modRoot) {
  const root = culturesRoot(modRoot);
  if (!syncFs.existsSync(root)) return [];
  const dirs = (await fs.readdir(root, { withFileTypes: true }))
    .filter(e => e.isDirectory() && e.name.startsWith("Culture_"))
    .map(e => e.name)
    .sort();
  const result = [];
  for (const folder of dirs) {
    const name = folder.slice("Culture_".length);
    const dir = path.join(root, folder);
    const file = path.join(dir, "CultureRule.jsonc");
    let text = "";
    let data = null;
    let error = null;
    if (syncFs.existsSync(file)) {
      text = (await fs.readFile(file, "utf8")).replace(/^﻿/, "");
      try { data = parseJsonLoose(text); } catch (e) { error = e.message; }
    }
    const csvs = (await fs.readdir(dir)).filter(f => f.toLowerCase().endsWith(".csv")).sort();
    result.push({
      name,
      folder,
      rel: path.relative(modRoot, dir).replace(/\\/g, "/"),
      file,
      text,
      data,
      error,
      csvs
    });
  }
  return result;
}

// 把 "regime": "xxx" 改成新值；没有就插到 "setting": { 后面
function setRegimeInRuleText(text, regime) {
  const re = /("regime"\s*:\s*)"[^"]*"/;
  if (re.test(text)) return text.replace(re, `$1${JSON.stringify(regime)}`);
  return text.replace(/("setting"\s*:\s*\{)/, `$1\n        "regime": ${JSON.stringify(regime)},`);
}

// 新建文化：从已有文化复制规则，可选连同词库 CSV 一起复制(文件名前缀、key 前缀替换成新文化)
async function createCulture(modRoot, { name, copyFrom, copyCsv, translate_cz, translate_en, translate_ch }) {
  const safe = String(name || "").trim();
  if (!/^[A-Za-z][A-Za-z0-9]*$/.test(safe)) throw new Error("文化名只能用英文字母和数字，并以字母开头，比如 Korea。");
  const root = culturesRoot(modRoot);
  const dir = path.join(root, `Culture_${safe}`);
  if (syncFs.existsSync(dir)) throw new Error(`文化 ${safe} 已存在。`);
  await fs.mkdir(dir, { recursive: true });

  let rule = { name: safe, color: "#888888", translate_cz: "", translate_en: "", translate_ch: "", species: [], setting: { regime: "", institution_line: "", traits: [], political_traits: [] } };
  if (copyFrom) {
    const srcDir = path.join(root, `Culture_${copyFrom}`);
    const srcFile = path.join(srcDir, "CultureRule.jsonc");
    if (syncFs.existsSync(srcFile)) {
      rule = parseJsonLoose(await fs.readFile(srcFile, "utf8"));
      rule.name = safe;
      rule.species = [];
    }
    if (copyCsv && syncFs.existsSync(srcDir)) {
      const lowerSrc = copyFrom.toLowerCase();
      const lowerNew = safe.toLowerCase();
      for (const file of await fs.readdir(srcDir)) {
        if (!file.toLowerCase().endsWith(".csv")) continue;
        const text = await fs.readFile(path.join(srcDir, file), "utf8");
        const newFile = file.startsWith(copyFrom) ? safe + file.slice(copyFrom.length) : file;
        // key 列里的文化名换成新文化，避免和原文化的本地化 key 撞车
        const lines = text.split(/\r?\n/).map((line, index) => {
          if (index === 0 || !line) return line;
          const comma = line.indexOf(",");
          if (comma < 0) return line;
          const key = line.slice(0, comma)
            .split(lowerSrc).join(lowerNew)
            .split(copyFrom).join(safe);
          return key + line.slice(comma);
        });
        await fs.writeFile(path.join(dir, newFile), lines.join("\r\n"), "utf8");
      }
    }
  }
  if (translate_cz !== undefined) rule.translate_cz = translate_cz;
  if (translate_en !== undefined) rule.translate_en = translate_en;
  if (translate_ch !== undefined) rule.translate_ch = translate_ch;
  const file = path.join(dir, "CultureRule.jsonc");
  await fs.writeFile(file, JSON.stringify(rule, null, 4) + "\n", "utf8");
  return { name: safe, file };
}

// ---------- 本地化 ----------
// Locales/{cz,en,ch}.json 是扁平的 "key": "value"，按行改，保持原来的排版

const LOCALE_LANGS = ["cz", "en", "ch"];

async function readLocales(modRoot, keys) {
  const result = {};
  for (const lang of LOCALE_LANGS) {
    const file = path.join(modRoot, "Locales", `${lang}.json`);
    let data = {};
    try { data = parseJsonLoose(await fs.readFile(file, "utf8")); } catch { data = {}; }
    for (const key of keys) {
      result[key] = result[key] || {};
      result[key][lang] = typeof data[key] === "string" ? data[key] : "";
    }
  }
  return result;
}

// entries: { key: { cz, en, ch } }；值为 undefined 的语言不动
async function writeLocales(modRoot, entries) {
  for (const lang of LOCALE_LANGS) {
    const file = path.join(modRoot, "Locales", `${lang}.json`);
    let text = (await fs.readFile(file, "utf8")).replace(/^﻿/, "");
    const nl = text.includes("\r\n") ? "\r\n" : "\n";
    const appended = [];
    for (const [key, values] of Object.entries(entries)) {
      const value = values?.[lang];
      if (value === undefined || value === null) continue;
      const escapedKey = key.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      const re = new RegExp(`("${escapedKey}"\\s*:\\s*)"(?:[^"\\\\]|\\\\.)*"`);
      const encoded = JSON.stringify(String(value));
      if (re.test(text)) text = text.replace(re, (_m, head) => head + encoded);
      else appended.push(`  ${JSON.stringify(key)}: ${encoded}`);
    }
    if (appended.length) {
      const end = text.lastIndexOf("}");
      let head = text.slice(0, end).replace(/\s*$/, "");
      if (!head.endsWith("{")) head += ",";
      text = head + nl + appended.join("," + nl) + nl + text.slice(end);
    }
    JSON.parse(text); // 写坏了就别写
    await fs.writeFile(file, text, "utf8");
  }
  return { ok: true };
}


module.exports = {
  culturesRoot, inModRoot, parseJsonLoose, stripJsonCommentsSafe, listCultures,
  setRegimeInRuleText, createCulture, readLocales, writeLocales
};
