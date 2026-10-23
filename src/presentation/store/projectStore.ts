import { create } from 'zustand'
import { activeWorksheet, addGraph, addWorksheet, setActiveWorksheet, updateWorksheet, type ProjectDoc } from '@domain/model/project'
import { addRows, addColumn, createColumn, removeColumn, removeRows, renameColumn, setCell, setColumnType, type Worksheet } from '@domain/model/worksheet'
import { createProject } from '@domain/model/project'
import { offlineAnalysisRunner } from '@application/usecases/analysis-runner'
import { runAnalysis, type ColumnSelection } from '@application/usecases/run-analysis'
import { executeCommands } from '@application/commands/execute'
import { deserializeProject, exportWorksheetCsv, importCsv, newProject, serializeProject, sessionToHtml } from '@application/usecases/project'
import { createElectronFileGateway } from '@infrastructure/electron-file-gateway'
import type { FileGateway } from '@application/ports'
import type { AnalysisDefinition } from '@domain/analysis/types'

export type DialogState =
  | { kind: 'none' }
  | { kind: 'analysis'; analysisId: string }
  | { kind: 'command' }
  | { kind: 'about' }
  | { kind: 'help' }

export interface ProjectStore {
  project: ProjectDoc
  filePath: string | null
  status: string
  dialog: DialogState
  selectedGraphId: string | null
  past: ProjectDoc[]
  future: ProjectDoc[]
  setDialog: (dialog: DialogState) => void
  setStatus: (status: string) => void
  newProject: () => void
  loadProject: (text: string, path: string | null) => boolean
  openProject: () => Promise<void>
  saveProject: (saveAs?: boolean) => Promise<void>
  importCsvFile: () => Promise<void>
  exportCsvFile: () => Promise<void>
  exportSession: () => Promise<void>
  updateWorksheetEdit: (mutator: (worksheet: Worksheet) => Worksheet) => void
  editCell: (columnId: string, row: number, raw: string) => void
  insertRow: () => void
  deleteRow: (row: number) => void
  insertColumn: () => void
  deleteColumn: (columnId: string) => void
  renameColumnName: (columnId: string, name: string) => void
  changeColumnType: (columnId: string, type: 'numeric' | 'text' | 'datetime') => void
  selectWorksheet: (worksheetId: string) => void
  addWorksheet: () => void
  executeAnalysis: (
    analysis: AnalysisDefinition,
    selections: { numeric: ColumnSelection[]; text: ColumnSelection[] },
    params: Record<string, number | string>
  ) => Promise<void>
  executeCommandText: (text: string) => Promise<void>
  selectGraph: (graphId: string | null) => void
  undo: () => void
  redo: () => void
  showHelp: () => void
}

function apply(project: ProjectDoc, mutator: (worksheet: Worksheet) => Worksheet): ProjectDoc {
  return updateWorksheet(project, mutator(activeWorksheet(project)))
}

let gateway: FileGateway | null = null
function fileGateway(): FileGateway {
  if (!gateway) gateway = createElectronFileGateway()
  return gateway
}

function initialProject(): ProjectDoc {
  return createProject('Proyecto sin título')
}

export const useProjectStore = create<ProjectStore>((set, get) => ({
  project: initialProject(),
  filePath: null,
  status: 'Listo',
  dialog: { kind: 'none' },
  selectedGraphId: null,
  past: [],
  future: [],
  setDialog: (dialog) => set({ dialog }),
  setStatus: (status) => set({ status }),

  newProject: () =>
    set({ project: newProject('Proyecto sin título'), filePath: null, status: 'Proyecto nuevo', dialog: { kind: 'none' }, past: [], future: [] }),

  loadProject: (text, path) => {
    const result = deserializeProject(text)
    if (!result.ok) {
      set({ status: `Error: ${result.error.message}` })
      return false
    }
    set({ project: result.value, filePath: path, status: `Proyecto abierto${path ? `: ${path}` : ''}`, past: [], future: [] })
    return true
  },

  openProject: async () => {
    try {
      const opened = await fileGateway().openFile('project')
      if (!opened) return
      get().loadProject(opened.text, opened.path)
    } catch (error) {
      set({ status: `Error al abrir: ${String(error)}` })
    }
  },

  saveProject: async (saveAs = false) => {
    try {
      const project = get().project
      const text = serializeProject(project)
      const gatewayInstance = fileGateway()
      const path = get().filePath
      if (!saveAs && path) {
        const saved = await gatewayInstance.saveProjectToPath(path, text)
        set({ status: saved ? `Guardado: ${path}` : 'No se pudo guardar' })
        return
      }
      const newPath = await gatewayInstance.saveFile('project', `${project.name}.ttab`, text)
      if (newPath) set({ filePath: newPath, status: `Guardado: ${newPath}` })
    } catch (error) {
      set({ status: `Error al guardar: ${String(error)}` })
    }
  },

  importCsvFile: async () => {
    try {
      const opened = await fileGateway().openFile('csv')
      if (!opened) return
      const result = importCsv(opened.text, opened.name.replace(/\.csv$/i, ''))
      if (!result.ok) {
        set({ status: `Error CSV: ${result.error.message}` })
        return
      }
      const project = get().project
      const next = addWorksheet(project, result.value)
      set({ project: next, status: `CSV importado: ${opened.name}`, past: [...get().past, project], future: [] })
    } catch (error) {
      set({ status: `Error al importar: ${String(error)}` })
    }
  },

  exportCsvFile: async () => {
    try {
      const project = get().project
      const text = exportWorksheetCsv(activeWorksheet(project))
      const path = await fileGateway().saveFile('csv', `${activeWorksheet(project).name}.csv`, text)
      set({ status: path ? `CSV exportado: ${path}` : 'Exportación cancelada' })
    } catch (error) {
      set({ status: `Error al exportar: ${String(error)}` })
    }
  },

  exportSession: async () => {
    try {
      const project = get().project
      const path = await fileGateway().saveFile('session', `${project.name} - sesión.html`, sessionToHtml(project))
      set({ status: path ? `Sesión exportada: ${path}` : 'Exportación cancelada' })
    } catch (error) {
      set({ status: `Error al exportar sesión: ${String(error)}` })
    }
  },

  updateWorksheetEdit: (mutator) => {
    const project = get().project
    set({ project: apply(project, mutator), past: [...get().past, project], future: [], status: 'Hoja modificada' })
  },

  editCell: (columnId, row, raw) => {
    const project = get().project
    const worksheet = activeWorksheet(project)
    const column = worksheet.columns.find((item) => item.id === columnId)
    if (!column) return
    const parsed = raw.trim() === '' || raw.trim() === '*' ? null : column.type === 'numeric' ? Number(raw.replace(',', '.')) : raw
    if (parsed !== null && typeof parsed === 'number' && !Number.isFinite(parsed)) return
    set({ project: apply(project, (item) => setCell(item, columnId, row, parsed)), past: [...get().past, project], future: [] })
  },

  insertRow: () => get().updateWorksheetEdit((worksheet) => addRows(worksheet, 1)),
  deleteRow: (row) => get().updateWorksheetEdit((worksheet) => removeRows(worksheet, Math.max(0, row - 1), 1)),
  insertColumn: () => {
    get().updateWorksheetEdit((worksheet) => addColumn(worksheet, createColumn(`C${worksheet.columns.length + 1}`, 'numeric', worksheet.rowCount)))
  },
  deleteColumn: (columnId) => get().updateWorksheetEdit((worksheet) => removeColumn(worksheet, columnId)),
  renameColumnName: (columnId, name) => get().updateWorksheetEdit((worksheet) => renameColumn(worksheet, columnId, name || 'C')),
  changeColumnType: (columnId, type) => get().updateWorksheetEdit((worksheet) => setColumnType(worksheet, columnId, type)),
  selectWorksheet: (worksheetId) => {
    const project = get().project
    set({ project: setActiveWorksheet(project, worksheetId), status: 'Hoja activa cambiada' })
  },
  addWorksheet: () => {
    const project = get().project
    set({ project: addWorksheet(project), past: [...get().past, project], future: [], status: 'Hoja añadida' })
  },

  executeAnalysis: async (analysis, selections, params) => {
    const project = get().project
    set({ status: `Ejecutando ${analysis.label}…` })
    const outcome = await runAnalysis(project, { analysisId: analysis.id, numeric: selections.numeric, text: selections.text, params }, offlineAnalysisRunner)
    if (!outcome.ok) {
      set({ status: `Error: ${outcome.error.message}` })
      await fileGateway().message('error', outcome.error.message)
      return
    }
    let next = outcome.value.project
    const charts = outcome.value.result.charts ?? []
    for (const chart of charts) {
      next = addGraph(next, { type: chart.kind, title: chart.title, payload: { ...chart } })
    }
    const last = next.graphs[next.graphs.length - 1]
    set({
      project: next,
      dialog: { kind: 'none' },
      status: `${analysis.label}: listo`,
      past: [...get().past, project],
      future: [],
      selectedGraphId: last ? last.id : get().selectedGraphId
    })
  },

  executeCommandText: async (text) => {
    const project = get().project
    const outcome = await executeCommands(project, text, offlineAnalysisRunner)
    set({ project: outcome.project, dialog: { kind: 'none' }, past: [...get().past, project], future: [], status: 'Comandos ejecutados' })
  },

  selectGraph: (graphId) => set({ selectedGraphId: graphId }),

  undo: () => {
    const { past, project, future } = get()
    const previous = past[past.length - 1]
    if (!previous) return
    set({ project: previous, past: past.slice(0, -1), future: [project, ...future], status: 'Deshacer' })
  },

  redo: () => {
    const { future, project, past } = get()
    const next = future[0]
    if (!next) return
    set({ project: next, past: [...past, project], future: future.slice(1), status: 'Rehacer' })
  },

  showHelp: () => set({ dialog: { kind: 'help' } })
}))
