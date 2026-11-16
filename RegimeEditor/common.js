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
    ["claims.html", "派系决议规则", "iconPlot"],
    ["military.html", "军制与官阶", "iconArmy"],
    ["misc.html", "杂项配置", "iconOptions"],
    ["scenario.html", "剧本编辑器", "iconBooksWritten"]
  ];
  bar.innerHTML = `<div class="title">${ico("iconWorldInfo")}EmpireCraft 编辑器</div>` +
    pages.map(([href, label, icon]) => `<a href="${href}" class="${href === active ? "active" : ""}">${ico(icon)}${label}</a>`).join("") +
    `<div class="spacer"></div><span class="root" id="modRootLabel"></span>` +
    `<button class="small" id="coreBtn" title="不开游戏，直接打开服务器核心">${ico("iconKingdom", "sm")}服务器核心</button>` +
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
  bar.querySelector("#coreBtn").addEventListener("click", openCoreDialog);
  refreshRootLabel();
}

// ---------- 服务器核心：不开游戏直接打开它(见 core-launcher.js) ----------
function coreWhen(iso) {
  const d = new Date(iso);
  if (isNaN(d)) return String(iso || "");
  const p = n => String(n).padStart(2, "0");
  return `${d.getMonth() + 1}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

async function openCoreDialog() {
  if (!api || !api.core) return toast("只有桌面版才能打开服务器核心", true);
  let box = document.getElementById("coreDialog");
  if (!box) {
    box = document.createElement("dialog");
    box.id = "coreDialog";
    box.style.cssText = "min-width:520px;max-width:640px";
    document.body.appendChild(box);
    box.addEventListener("click", e => { if (e.target === box) box.close(); });
  }
  const row = (icon, label, value, muted) =>
    `<div style="display:flex;gap:8px;align-items:flex-start;padding:4px 0">${ico(icon)}<span class="muted" style="flex:none;width:64px">${label}</span>` +
    `<span style="flex:1;word-break:break-all;${muted ? "opacity:.7" : ""}">${value}</span></div>`;
  const draw = async () => {
    const s = await api.core.status();
    const save = s.save
      ? `第 ${s.save.year} 年 ${s.save.month} 月 · 存于 ${esc(coreWhen(s.save.saved))}`
      : "还没有存档";
    box.innerHTML =
      `<h3 style="margin:0 0 8px;display:flex;align-items:center;gap:8px">${ico("iconKingdom")}服务器核心</h3>` +
      row("iconOptions", "程序", s.exe ? esc(s.exe) : "没找到。更新到带核心的模组版本，或在游戏的模组设置里打开一次「用服务器核心」。", !s.exe) +
      row("iconSaveLocal", "存档", save, !s.save) +
      row("iconWorldInfo", "眼下", s.running ? `已经开着(端口 ${s.port})，窗口收在托盘里` : "没在运行", !s.running) +
      `<p class="muted" style="margin:10px 0">不用开游戏。核心接着上面那份存档跑，手机照常连；没有存档时，在核心窗口里点「打开网页管理页」挑一个剧本开局。` +
      `这样跑出来的进度不会自己回到游戏：在游戏里打开「用服务器核心」时，会问带回还是丢弃。</p>` +
      `<div style="display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end">` +
      `<button id="coreState">${ico("iconBooks", "sm")}打开存档文件夹</button>` +
      `<button id="coreLink" ${s.exe ? "" : "disabled"}>${ico("iconEdit", "sm")}把核心图标放到桌面</button>` +
      `<button id="coreGo" class="primary" ${s.exe && !s.running ? "" : "disabled"}>${ico("iconKingdom", "sm")}${s.running ? "已经开着" : "打开核心"}</button>` +
      `<button id="coreClose">关闭</button></div>`;
    box.querySelector("#coreClose").onclick = () => box.close();
    box.querySelector("#coreState").onclick = async () => {
      const r = await api.core.openState();
      if (!r.ok) toast(r.error, true);
    };
    box.querySelector("#coreLink").onclick = async () => {
      const r = await api.core.shortcut();
      toast(r.ok ? `已放到桌面：${r.file}` : r.error, !r.ok);
    };
    box.querySelector("#coreGo").onclick = async e => {
      e.target.disabled = true;
      const r = await api.core.launch();
      if (!r.ok) toast(r.error, true);
      else toast(r.running ? "核心已经开着，窗口收在托盘里" : "核心开起来了：它有自己的窗口和托盘图标");
      // 端口要过一会儿才通，等一等再看
      setTimeout(draw, 2500);
    };
  };
  try {
    await draw();
    if (!box.open) box.showModal();
  } catch (e) { toast(e.message, true); }
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
