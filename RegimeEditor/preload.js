const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("regimeEditorDesktop", {
  isElectron: true,
  getModRoot: () => ipcRenderer.invoke("get-mod-root"),
  chooseModRoot: () => ipcRenderer.invoke("choose-mod-root"),
  loadCultureRules: () => ipcRenderer.invoke("load-culture-rules"),
  openConfigsDir: () => ipcRenderer.invoke("open-configs-dir"),
  writeConfigs: (payload) => ipcRenderer.invoke("write-configs", payload),
  syncEnums: () => ipcRenderer.invoke("sync-enums"),
  listRegimeTemplates: () => ipcRenderer.invoke("list-regime-templates"),
  // 文化编辑器 / 节点编辑器
  listCultures: () => ipcRenderer.invoke("list-cultures"),
  createCulture: (payload) => ipcRenderer.invoke("create-culture", payload),
  readText: (relPath) => ipcRenderer.invoke("read-text", relPath),
  writeText: (relPath, text) => ipcRenderer.invoke("write-text", relPath, text),
  listDir: (relPath) => ipcRenderer.invoke("list-dir", relPath),
  openPath: (relPath) => ipcRenderer.invoke("open-path", relPath),
  readLocales: (keys) => ipcRenderer.invoke("read-locales", keys),
  writeLocales: (entries) => ipcRenderer.invoke("write-locales", entries),
  // 剧本编辑器
  scenario: {
    info: () => ipcRenderer.invoke("scenario-info"),
    list: () => ipcRenderer.invoke("scenario-list"),
    read: (folder) => ipcRenderer.invoke("scenario-read", folder),
    preset: (id) => ipcRenderer.invoke("scenario-preset", id),
    reserve: (base) => ipcRenderer.invoke("scenario-reserve", base),
    write: (folder, text) => ipcRenderer.invoke("scenario-write", folder, text),
    preview: (folder, bytes) => ipcRenderer.invoke("scenario-preview", folder, bytes),
    world: (name) => ipcRenderer.invoke("scenario-world", name),
    cultures: () => ipcRenderer.invoke("scenario-cultures"),
    naming: () => ipcRenderer.invoke("scenario-naming"),
    files: (folder) => ipcRenderer.invoke("scenario-files", folder),
    file: (folder, name) => ipcRenderer.invoke("scenario-file", folder, name),
    putFile: (folder, name, bytes) => ipcRenderer.invoke("scenario-put-file", folder, name, bytes),
    remove: (folder) => ipcRenderer.invoke("scenario-delete", folder),
    chooseDir: () => ipcRenderer.invoke("scenario-choose-dir"),
    openFolder: (folder) => ipcRenderer.invoke("scenario-open-folder", folder)
  }
});
