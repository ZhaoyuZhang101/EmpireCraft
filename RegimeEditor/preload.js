const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("regimeEditorDesktop", {
  isElectron: true,
  getModRoot: () => ipcRenderer.invoke("get-mod-root"),
  chooseModRoot: () => ipcRenderer.invoke("choose-mod-root"),
  loadCultureRules: () => ipcRenderer.invoke("load-culture-rules"),
  openConfigsDir: () => ipcRenderer.invoke("open-configs-dir"),
  writeConfigs: (payload) => ipcRenderer.invoke("write-configs", payload),
  syncEnums: () => ipcRenderer.invoke("sync-enums"),
  // 文化编辑器 / 节点编辑器
  listCultures: () => ipcRenderer.invoke("list-cultures"),
  createCulture: (payload) => ipcRenderer.invoke("create-culture", payload),
  readText: (relPath) => ipcRenderer.invoke("read-text", relPath),
  writeText: (relPath, text) => ipcRenderer.invoke("write-text", relPath, text),
  listDir: (relPath) => ipcRenderer.invoke("list-dir", relPath),
  openPath: (relPath) => ipcRenderer.invoke("open-path", relPath),
  readLocales: (keys) => ipcRenderer.invoke("read-locales", keys),
  writeLocales: (entries) => ipcRenderer.invoke("write-locales", entries)
});
