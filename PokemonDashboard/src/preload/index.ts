import { contextBridge, ipcRenderer } from 'electron'

// Expose custom APIs to renderer
contextBridge.exposeInMainWorld('api', {
  loadConfig: () => ipcRenderer.invoke('load-config')
})
