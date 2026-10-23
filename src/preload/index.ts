import { contextBridge, ipcRenderer } from 'electron'
import { IpcChannels, type SaveTextArgs, type TinyTabApi } from '@shared/ipc-contract'

const api: TinyTabApi = {
  app: {
    version: () => ipcRenderer.invoke(IpcChannels.appVersion) as Promise<string>
  },
  dialog: {
    message: (args) => ipcRenderer.invoke(IpcChannels.dialogMessage, args) as Promise<void>,
    confirm: (text, title) => ipcRenderer.invoke(IpcChannels.dialogConfirm, text, title) as Promise<boolean>
  },
  files: {
    openProject: () => ipcRenderer.invoke(IpcChannels.projectOpen),
    saveProject: (args) => ipcRenderer.invoke(IpcChannels.projectSave, args) as Promise<boolean>,
    saveProjectAs: (args: SaveTextArgs) => ipcRenderer.invoke(IpcChannels.projectSaveAs, args),
    importCsv: () => ipcRenderer.invoke(IpcChannels.csvImport),
    exportCsv: (args: SaveTextArgs) => ipcRenderer.invoke(IpcChannels.csvExport, args),
    exportSession: (args: SaveTextArgs) => ipcRenderer.invoke(IpcChannels.sessionExport, args),
    exportText: (args: SaveTextArgs) => ipcRenderer.invoke(IpcChannels.textExport, args)
  },
  onMenuCommand: (callback) => {
    const listener = (_event: unknown, command: string): void => callback(command)
    ipcRenderer.on('menu:command', listener)
    return () => ipcRenderer.removeListener('menu:command', listener)
  }
}

contextBridge.exposeInMainWorld('tinytab', api)
