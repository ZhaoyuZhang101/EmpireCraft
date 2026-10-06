// 各编辑器页面共用：顶部导航、状态提示、CSV 读写、小工具
const api = window.regimeEditorDesktop || null;

function ico(name, cls = "") {
  return `<img class="ico ${cls}" src="assets/icons/${name}.png" alt="" onerror="this.style.visibility='hidden'">`;
}

function esc(v) {
  return String(v ?? "").replace(/[&<>"]/g, m => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[m]));
}

function mountAppBar(active) {
  const bar = document.createElement("div");
  bar.className = "appbar";
  const pages = [
    ["index.html", "政体编辑器", "iconKings"],
    ["culture.html", "文化编辑器", "iconCulture"],
    ["nodes.html", "科技 / 制度节点", "iconKnowledge"],
    ["claims.html", "派系决议规则", "iconPlot"]
  ];
  bar.innerHTML = `<div class="title">${ico("iconWorldInfo")}EmpireCraft 编辑器</div>` +
    pages.map(([href, label, icon]) => `<a href="${href}" class="${href === active ? "active" : ""}">${ico(icon)}${label}</a>`).join("") +
    `<div class="spacer"></div><span class="root" id="modRootLabel"></span>` +
    `<button class="small" id="chooseRootBtn">${ico("iconOptions", "sm")}选择模组目录</button>`;
  document.body.prepend(bar);
  const status = document.createElement("div");
  status.className = "status";
  status.id = "statusToast";
  document.body.appendChild(status);
  bar.querySelector("#chooseRootBtn").addEventListener("click", async () => {
    if (!api) return toast("只有桌面版才能选择目录", true);
    try {
      const r = await api.chooseModRoot();
      if (!r.canceled) location.reload();
    } catch (e) { toast(e.message, true); }
  });
  refreshRootLabel();
}

async function refreshRootLabel() {
  const el = document.getElementById("modRootLabel");
  if (!el) return;
  if (!api) { el.textContent = "浏览器模式：无法读写文件，请用 启动桌面版.bat"; return; }
  const r = await api.getModRoot();
  el.textContent = r.path ? `模组目录：${r.path}` : (r.error || "未找到模组目录");
  el.title = el.textContent;
}

let toastTimer = null;
function toast(text, isErr = false) {
  const el = document.getElementById("statusToast");
  if (!el) return;
  el.textContent = text;
  el.className = "status show" + (isErr ? " err" : "");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove("show"), isErr ? 8000 : 3500);
}

// 与游戏 JSON 读取一致：允许 // 和 /* */ 注释、尾逗号
function parseJsonLoose(text) {
  let out = "";
  const s = String(text || "").replace(/^﻿/, "");
  let i = 0;
  while (i < s.length) {
    const c = s[i];
    if (c === '"') {
      let j = i + 1;
      while (j < s.length && s[j] !== '"') j += s[j] === "\\" ? 2 : 1;
      out += s.slice(i, j + 1); i = j + 1;
    } else if (s.startsWith("//", i)) {
      const e = s.indexOf("\n", i); i = e < 0 ? s.length : e;
    } else if (s.startsWith("/*", i)) {
      const e = s.indexOf("*/", i + 2); i = e < 0 ? s.length : e + 2;
    } else { out += c; i++; }
  }
  return JSON.parse(out.replace(/,(\s*[}\]])/g, "$1"));
}

// ---------- CSV (key,cz,en,ch) ----------
function parseCsvLine(line) {
  const cells = [];
  let cell = "", quoted = false;
  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (quoted) {
      if (c === '"' && line[i + 1] === '"') { cell += '"'; i++; }
      else if (c === '"') quoted = false;
      else cell += c;
    } else if (c === '"') quoted = true;
    else if (c === ",") { cells.push(cell); cell = ""; }
    else cell += c;
  }
  cells.push(cell);
  return cells;
}

function parseCsv(text) {
  const lines = String(text || "").replace(/^﻿/, "").split(/\r?\n/);
  const header = lines.length && lines[0] ? parseCsvLine(lines[0]) : ["key", "cz", "en", "ch"];
  const rows = lines.slice(1).filter(l => l.trim()).map(parseCsvLine);
  return { header, rows };
}

function csvCell(v) {
  const s = String(v ?? "");
  return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

function serializeCsv({ header, rows }) {
  return [header, ...rows].map(r => r.map(csvCell).join(",")).join("\r\n") + "\r\n";
}

function uniq(arr) { return [...new Set((arr || []).filter(v => v !== undefined && v !== null && v !== ""))]; }

// 可增删的小标签编辑器；onChange(newValues)
function chipEditor(values, { placeholder = "输入后回车", suggestions = [], known = null, onChange }) {
  const wrap = document.createElement("div");
  wrap.className = "chips";
  const listId = "dl" + Math.random().toString(36).slice(2);
  const render = () => {
    wrap.innerHTML = values.map((v, i) =>
      `<span class="chip ${known && !known.has(v) ? "missing" : ""}" title="${known && !known.has(v) ? "找不到这个 id" : ""}">${esc(v)}<button data-i="${i}">×</button></span>`).join("") +
      `<span class="chip-add"><input list="${listId}" placeholder="${esc(placeholder)}"><datalist id="${listId}">${suggestions.map(s => `<option value="${esc(s)}">`).join("")}</datalist></span>`;
    wrap.querySelectorAll("button[data-i]").forEach(b => b.addEventListener("click", () => {
      values.splice(Number(b.dataset.i), 1); render(); onChange(values);
    }));
    const input = wrap.querySelector("input");
    input.addEventListener("keydown", e => {
      if (e.key !== "Enter") return;
      const v = input.value.trim();
      if (v && !values.includes(v)) { values.push(v); render(); onChange(values); wrap.querySelector("input").focus(); }
    });
  };
  render();
  return wrap;
}
