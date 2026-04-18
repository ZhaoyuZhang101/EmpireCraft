const { contextBridge, ipcRenderer } = require("electron");

contextBridge.exposeInMainWorld("regimeEditorDesktop", {
  isElectron: true,
  getModRoot: () => ipcRenderer.invoke("get-mod-root"),
  chooseModRoot: () => ipcRenderer.invoke("choose-mod-root"),
  loadCultureRules: () => ipcRenderer.invoke("load-culture-rules"),
  openConfigsDir: () => ipcRenderer.invoke("open-configs-dir"),
  writeConfigs: (payload) => ipcRenderer.invoke("write-configs", payload),
  syncEnums: () => ipcRenderer.invoke("sync-enums")
});
