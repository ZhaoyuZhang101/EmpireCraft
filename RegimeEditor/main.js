const { app, BrowserWindow, ipcMain, shell, dialog } = require("electron");
const fs = require("node:fs/promises");
const path = require("node:path");
const { spawn } = require("node:child_process");
const syncFs = require("node:fs");
const {
  culturesRoot, inModRoot, listCultures, setRegimeInRuleText, createCulture, readLocales, writeLocales
} = require("./editor-core");

function stripJsonComments(text) {
  return text
    .replace(/^\uFEFF/, "")
    .replace(/\/\/.*$/gm, "")
    .replace(/,\s*([}\]])/g, "$1");
}

function hasRegimeSystem(dir) {
  return dir && syncFs.existsSync(path.join(dir, "Scripts", "Regimes", "RegimeSystem.cs"));
}

function climbToModRoot(startDir) {
  if (!startDir) return null;
  let current = path.resolve(startDir);
  while (true) {
    if (hasRegimeSystem(current)) return current;
    const parent = path.dirname(current);
    if (parent === current) return null;
    current = parent;
  }
}

function resolveModRoot() {
  const saved = getSavedModRoot();
  if (saved && hasRegimeSystem(saved)) return saved;
  const candidates = [
    process.cwd(),
    __dirname,
    path.join(__dirname, ".."),
    path.dirname(app.getPath("exe")),
    path.join(path.dirname(app.getPath("exe")), ".."),
    path.join(path.dirname(app.getPath("exe")), "..", "..")
  ];
  for (const candidate of candidates) {
    const found = climbToModRoot(candidate);
    if (found) return found;
  }
  throw new Error("未找到模组根目录，请把 RegimeEditor 放在 EmpireCraft 模组根目录下运行。");
}

function getSettingsPath() {
  return path.join(app.getPath("userData"), "regime-editor-settings.json");
}

function getSavedModRoot() {
  try {
    const raw = syncFs.readFileSync(getSettingsPath(), "utf8");
    const data = JSON.parse(raw);
    return typeof data.modRoot === "string" ? data.modRoot : null;
  } catch {
    return null;
  }
}

async function saveModRoot(modRoot) {
  await fs.mkdir(path.dirname(getSettingsPath()), { recursive: true });
  await fs.writeFile(getSettingsPath(), JSON.stringify({ modRoot }, null, 2), "utf8");
}

async function chooseModRoot() {
  const result = await dialog.showOpenDialog({
    title: "选择 EmpireCraft 模组根目录",
    properties: ["openDirectory"]
  });
  if (result.canceled || !result.filePaths?.[0]) {
    return { canceled: true };
  }

  const modRoot = result.filePaths[0];
  if (!hasRegimeSystem(modRoot)) {
    throw new Error("所选目录不是有效的 EmpireCraft 模组根目录，缺少 Scripts/Regimes/RegimeSystem.cs。");
  }

  await saveModRoot(modRoot);
  return { canceled: false, path: modRoot };
}

function sanitizeFolderName(name) {
  const safe = String(name || "").trim().replace(/[<>:"/\\|?*]/g, "_");
  if (!safe) throw new Error("政体文件夹名不能为空。");
  return safe;
}

async function runCommand(command, args, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd,
      windowsHide: true,
      shell: false
    });

    let stdout = "";
    let stderr = "";

    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString("utf8");
    });
    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString("utf8");
    });
    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) {
        resolve({ stdout: stdout.trim(), stderr: stderr.trim() });
        return;
      }
      reject(new Error((stderr || stdout || `进程退出码 ${code}`).trim()));
    });
  });
}

async function runEnumSync(modRoot) {
  const editorDir = path.join(modRoot, "RegimeEditor");
  const scriptPath = path.join(editorDir, "regime_enum_sync.py");
  const exeCandidates = [
    path.join(editorDir, "regime_enum_sync.exe"),
    path.join(editorDir, "EnumSyncPublish", "RegimeEnumSync.exe")
  ];

  const attempts = [];
  attempts.push(() => runCommand("python", [scriptPath, modRoot], modRoot));
  attempts.push(() => runCommand("py", ["-3", scriptPath, modRoot], modRoot));
  attempts.push(() => runCommand("python3", [scriptPath, modRoot], modRoot));
  for (const exePath of exeCandidates) {
    attempts.push(() => runCommand(exePath, [modRoot], modRoot));
  }

  let lastError = null;
  for (const attempt of attempts) {
    try {
      return await attempt();
    } catch (error) {
      lastError = error;
    }
  }
  throw lastError || new Error("同步脚本运行失败。");
}

function createWindow() {
  const win = new BrowserWindow({
    width: 1560,
    height: 980,
    minWidth: 1180,
    minHeight: 760,
    autoHideMenuBar: true,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      preload: path.join(__dirname, "preload.js")
    }
  });

  win.loadFile(path.join(__dirname, "index.html"));
}

app.whenReady().then(() => {
  ipcMain.handle("get-mod-root", async () => {
    try {
      const modRoot = resolveModRoot();
      if (getSavedModRoot() !== modRoot) {
        await saveModRoot(modRoot);
      }
      return { path: modRoot };
    } catch (error) {
      return { path: null, error: error.message };
    }
  });

  ipcMain.handle("choose-mod-root", async () => {
    return chooseModRoot();
  });

  // 文化规则现在是每个文化一个文件：Locales/Cultures/Culture_<文化>/CultureRule.jsonc
  ipcMain.handle("load-culture-rules", async () => {
    const modRoot = resolveModRoot();
    const cultures = await listCultures(modRoot);
    return {
      path: culturesRoot(modRoot),
      data: cultures.filter(c => c.data).map(c => ({ ...c.data, __file: c.file }))
    };
  });

  ipcMain.handle("list-cultures", async () => listCultures(resolveModRoot()));

  ipcMain.handle("create-culture", async (_event, payload) => createCulture(resolveModRoot(), payload || {}));

  ipcMain.handle("read-text", async (_event, relPath) => {
    const file = inModRoot(resolveModRoot(), relPath);
    if (!syncFs.existsSync(file)) return { path: file, exists: false, text: "" };
    // 去掉 BOM 再给页面，另报 bom 让页面写回时照原样带上(帝国律法等文件带 BOM)
    const raw = await fs.readFile(file, "utf8");
    const bom = raw.startsWith("\ufeff");
    return { path: file, exists: true, text: bom ? raw.slice(1) : raw, bom };
  });

  ipcMain.handle("write-text", async (_event, relPath, text) => {
    const file = inModRoot(resolveModRoot(), relPath);
    await fs.mkdir(path.dirname(file), { recursive: true });
    let out = String(text ?? "");
    // 原文件带 BOM、页面给的文本没带(读的时候去掉了)，写回时补上，免得改一次就丢
    if (!out.startsWith("\ufeff") && syncFs.existsSync(file)) {
      const head = Buffer.alloc(3);
      const fd = syncFs.openSync(file, "r");
      try { syncFs.readSync(fd, head, 0, 3, 0); } finally { syncFs.closeSync(fd); }
      if (head[0] === 0xef && head[1] === 0xbb && head[2] === 0xbf) out = "\ufeff" + out;
    }
    await fs.writeFile(file, out, "utf8");
    return { path: file };
  });

  ipcMain.handle("list-dir", async (_event, relPath) => {
    const dir = inModRoot(resolveModRoot(), relPath);
    if (!syncFs.existsSync(dir)) return [];
    const entries = await fs.readdir(dir, { withFileTypes: true });
    return entries.map(e => ({ name: e.name, dir: e.isDirectory() }));
  });

  ipcMain.handle("open-path", async (_event, relPath) => {
    const target = inModRoot(resolveModRoot(), relPath || ".");
    const error = await shell.openPath(target);
    if (error) throw new Error(error);
    return { path: target };
  });

  ipcMain.handle("read-locales", async (_event, keys) => readLocales(resolveModRoot(), keys || []));
  ipcMain.handle("write-locales", async (_event, entries) => writeLocales(resolveModRoot(), entries || {}));

  ipcMain.handle("open-configs-dir", async () => {
    const modRoot = resolveModRoot();
    const configsDir = path.join(modRoot, "Scripts", "Regimes", "Configs");
    await fs.mkdir(configsDir, { recursive: true });
    const error = await shell.openPath(configsDir);
    if (error) throw new Error(error);
    return { path: configsDir };
  });

  // 政体编辑器写回一个政体目录：只写页面给出的文件(内容没变的不写)，目录里的其他文件(如 Naturalization.json、
  // Senators.csv)不动，也不会凭空建出 SystemConfig.json(八旗目录只有 OfficialType.csv)
  ipcMain.handle("write-configs", async (_event, payload) => {
    const modRoot = resolveModRoot();
    const configsDir = path.join(modRoot, "Scripts", "Regimes", "Configs");
    const folderName = sanitizeFolderName(payload?.folderName);
    const outputDir = path.join(configsDir, folderName);
    const outputs = payload?.outputs || {};
    const allowed = ["SystemConfig.json", "OfficialType.csv", "RegimeEditorState.json"];

    const written = [];
    for (const fileName of allowed) {
      if (typeof outputs[fileName] !== "string") continue;
      const file = path.join(outputDir, fileName);
      if (syncFs.existsSync(file) && (await fs.readFile(file, "utf8")).replace(/^\uFEFF/, "") === outputs[fileName]) continue;
      await fs.mkdir(outputDir, { recursive: true });
      await fs.writeFile(file, outputs[fileName], "utf8");
      written.push(fileName);
    }
    // 文化绑定：只改各文化 CultureRule.jsonc 里的 setting.regime，原文(含注释)其余部分不动
    const changed = [];
    for (const binding of outputs.cultureRegimes || []) {
      if (!binding?.file) continue;
      const file = inModRoot(modRoot, path.relative(modRoot, binding.file));
      const text = await fs.readFile(file, "utf8");
      const next = setRegimeInRuleText(text, String(binding.regime || ""));
      if (next !== text) {
        await fs.writeFile(file, next, "utf8");
        changed.push(file);
      }
    }

    return {
      directory: outputDir,
      files: written,
      culturePath: changed.length ? `${changed.length} 个文化` : "无改动"
    };
  });

  // 政体编辑器的“常用原型”：编辑器自带的 RegimeTemplates/*.json(打包后在程序里，不在模组目录)
  ipcMain.handle("list-regime-templates", async () => {
    const dir = path.join(__dirname, "RegimeTemplates");
    if (!syncFs.existsSync(dir)) return [];
    const result = [];
    for (const name of (await fs.readdir(dir)).filter(n => n.toLowerCase().endsWith(".json")).sort()) {
      result.push({ file: name, text: (await fs.readFile(path.join(dir, name), "utf8")).replace(/^\uFEFF/, "") });
    }
    return result;
  });

  ipcMain.handle("sync-enums", async () => {
    const modRoot = resolveModRoot();
    const result = await runEnumSync(modRoot);
    return result;
  });

  createWindow();

  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});
