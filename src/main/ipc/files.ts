import { dialog, type IpcMain } from 'electron'
import { readFile, writeFile } from 'node:fs/promises'
import { basename } from 'node:path'
import {
  IpcChannels,
  type OpenTextResult,
  type SaveTextArgs,
  type WriteTextArgs
} from '@shared/ipc-contract'

const projectFilters = [{ name: 'Proyecto TinyTab', extensions: ['ttab'] }]
const worksheetFilters = [{ name: 'Hoja TinyTab', extensions: ['ttw'] }]
const csvFilters = [{ name: 'Valores separados por comas', extensions: ['csv', 'txt'] }]
const htmlFilters = [{ name: 'Documento HTML', extensions: ['html', 'htm'] }]

async function openText(filters: { name: string; extensions: string[] }[]): Promise<OpenTextResult | null> {
  const result = await dialog.showOpenDialog({ properties: ['openFile'], filters })
  const path = result.filePaths[0]
  if (result.canceled || !path) return null
  const text = await readFile(path, 'utf8')
  return { path, name: basename(path), text }
}

async function saveAs(args: SaveTextArgs, filters: { name: string; extensions: string[] }[]): Promise<string | null> {
  const result = await dialog.showSaveDialog({ defaultPath: args.suggestedName, filters })
  if (result.canceled || !result.filePath) return null
  await writeFile(result.filePath, args.text, 'utf8')
  return result.filePath
}

export function registerFileIpc(ipc: IpcMain): void {
  ipc.handle(IpcChannels.projectOpen, async () => openText(projectFilters))

  ipc.handle(IpcChannels.projectSave, async (_event, args: WriteTextArgs) => {
    if (!args.path) return false
    await writeFile(args.path, args.text, 'utf8')
    return true
  })

  ipc.handle(IpcChannels.projectSaveAs, async (_event, args: SaveTextArgs) => saveAs(args, projectFilters))

  ipc.handle(IpcChannels.csvImport, async () => openText(csvFilters))

  ipc.handle(IpcChannels.csvExport, async (_event, args: SaveTextArgs) => saveAs(args, csvFilters))

  ipc.handle(IpcChannels.sessionExport, async (_event, args: SaveTextArgs) => saveAs(args, htmlFilters))

  ipc.handle(IpcChannels.textExport, async (_event, args: SaveTextArgs) => saveAs(args, worksheetFilters))
}
