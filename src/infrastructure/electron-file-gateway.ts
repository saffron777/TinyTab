import type { TinyTabApi } from '@shared/ipc-contract'
import type { FileGateway } from '@application/ports'

declare global {
  interface Window {
    tinytab?: TinyTabApi
  }
}

/** Adaptador del puerto FileGateway sobre el API expuesto por preload. */
export function createElectronFileGateway(): FileGateway {
  const api = window.tinytab
  if (!api) {
    throw new Error('El puente de Electron no está disponible (¿se abrió la app fuera de Electron?).')
  }
  return {
    async openFile(kind) {
      const result = kind === 'csv' ? await api.files.importCsv() : await api.files.openProject()
      return result
    },
    async saveFile(kind, suggestedName, text) {
      if (kind === 'project') return api.files.saveProjectAs({ suggestedName, text })
      if (kind === 'session') return api.files.exportSession({ suggestedName, text })
      return api.files.exportCsv({ suggestedName, text })
    },
    async saveProjectToPath(path, text) {
      return api.files.saveProject({ path, text })
    },
    async message(kind, text) {
      await api.dialog.message({ kind, text })
    },
    async confirm(text) {
      return api.dialog.confirm(text)
    }
  }
}
