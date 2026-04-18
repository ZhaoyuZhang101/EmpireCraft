const { app, BrowserWindow, ipcMain, shell, dialog } = require("electron");
const fs = require("node:fs/promises");
const path = require("node:path");
const { spawn } = require("node:child_process");
const syncFs = require("node:fs");

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

  ipcMain.handle("load-culture-rules", async () => {
    const modRoot = resolveModRoot();
    const culturePath = path.join(modRoot, "CultureRulesConfig.json");
    const text = await fs.readFile(culturePath, "utf8");
    const data = JSON.parse(stripJsonComments(text));
    return {
      path: culturePath,
      text,
      data
    };
  });

  ipcMain.handle("open-configs-dir", async () => {
    const modRoot = resolveModRoot();
    const configsDir = path.join(modRoot, "Scripts", "Regimes", "Configs");
    await fs.mkdir(configsDir, { recursive: true });
    const error = await shell.openPath(configsDir);
    if (error) throw new Error(error);
    return { path: configsDir };
  });

  ipcMain.handle("write-configs", async (_event, payload) => {
    const modRoot = resolveModRoot();
    const configsDir = path.join(modRoot, "Scripts", "Regimes", "Configs");
    const folderName = sanitizeFolderName(payload?.folderName);
    const outputDir = path.join(configsDir, folderName);
    const outputs = payload?.outputs || {};
    const culturePath = path.join(modRoot, "CultureRulesConfig.json");

    await fs.mkdir(outputDir, { recursive: true });
    const writeTargets = {
      "SystemConfig.json": outputs["SystemConfig.json"] || "",
      "OfficialType.csv": outputs["OfficialType.csv"] || "",
      "RegimeEditorState.json": outputs["RegimeEditorState.json"] || ""
    };

    for (const [fileName, content] of Object.entries(writeTargets)) {
      await fs.writeFile(path.join(outputDir, fileName), String(content), "utf8");
    }
    if (typeof outputs.cultureText === "string" && outputs.cultureText.trim()) {
      await fs.writeFile(culturePath, outputs.cultureText, "utf8");
    }

    return {
      directory: outputDir,
      files: Object.keys(writeTargets),
      culturePath
    };
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
