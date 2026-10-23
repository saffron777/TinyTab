/** Canales IPC compartidos entre main, preload y renderer. */
export const IpcChannels = {
  appVersion: 'app:version',
  dialogMessage: 'dialog:message',
  dialogConfirm: 'dialog:confirm',
  projectOpen: 'files:project-open',
  projectSave: 'files:project-save',
  projectSaveAs: 'files:project-save-as',
  csvImport: 'files:csv-import',
  csvExport: 'files:csv-export',
  sessionExport: 'files:session-export',
  textExport: 'files:text-export'
} as const

export type DialogKind = 'info' | 'warning' | 'error'

export interface OpenTextResult {
  path: string
  name: string
  text: string
}

export interface SaveTextArgs {
  suggestedName: string
  filters?: { name: string; extensions: string[] }[]
  text: string
}

export interface WriteTextArgs {
  path: string
  text: string
}

export interface DialogMessageArgs {
  kind: DialogKind
  text: string
  title?: string
}

/** API expuesta al renderer mediante contextBridge (sin acceso a Node). */
export interface TinyTabApi {
  app: {
    version(): Promise<string>
  }
  dialog: {
    message(args: DialogMessageArgs): Promise<void>
    confirm(text: string, title?: string): Promise<boolean>
  }
  files: {
    openProject(): Promise<OpenTextResult | null>
    saveProject(args: WriteTextArgs): Promise<boolean>
    saveProjectAs(args: SaveTextArgs): Promise<string | null>
    importCsv(): Promise<OpenTextResult | null>
    exportCsv(args: SaveTextArgs): Promise<string | null>
    exportSession(args: SaveTextArgs): Promise<string | null>
    exportText(args: SaveTextArgs): Promise<string | null>
  }
  onMenuCommand(callback: (command: string) => void): () => void
}
