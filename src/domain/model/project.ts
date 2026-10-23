import type { Worksheet } from './worksheet'
import { createWorksheet, generateId } from './worksheet'

export type SessionKind = 'command' | 'output' | 'error' | 'note'

export interface SessionBlock {
  readonly id: string
  readonly kind: SessionKind
  readonly title?: string
  readonly text: string
  readonly createdAt: string
}

export type ChartType =
  | 'histogram'
  | 'boxplot'
  | 'scatter'
  | 'control'
  | 'probability'
  | 'residuals'
  | 'timeseries'
  | 'line'
  | 'bar'
  | 'pareto'
  | 'matrix'
  | 'pca'

/** Documento serializable de un gráfico: los datos permiten re-renderizarlo. */
export interface GraphDoc {
  readonly id: string
  readonly type: ChartType
  readonly title: string
  readonly payload: Readonly<Record<string, unknown>>
  readonly createdAt: string
}

export interface ProjectDoc {
  readonly format: 'tinytab-project'
  readonly version: 1
  readonly name: string
  readonly worksheets: Worksheet[]
  readonly session: SessionBlock[]
  readonly graphs: GraphDoc[]
  readonly activeWorksheetId: string
}

export function createProject(name = 'Proyecto sin título'): ProjectDoc {
  const worksheet = createWorksheet()
  return {
    format: 'tinytab-project',
    version: 1,
    name,
    worksheets: [worksheet],
    session: [],
    graphs: [],
    activeWorksheetId: worksheet.id
  }
}

export function activeWorksheet(project: ProjectDoc): Worksheet {
  return project.worksheets.find((worksheet) => worksheet.id === project.activeWorksheetId) ?? project.worksheets[0]!
}

export function addWorksheet(project: ProjectDoc, worksheet: Worksheet = createWorksheet()): ProjectDoc {
  return { ...project, worksheets: [...project.worksheets, worksheet], activeWorksheetId: worksheet.id }
}

export function updateWorksheet(project: ProjectDoc, worksheet: Worksheet): ProjectDoc {
  return {
    ...project,
    worksheets: project.worksheets.map((item) => (item.id === worksheet.id ? worksheet : item))
  }
}

export function setActiveWorksheet(project: ProjectDoc, worksheetId: string): ProjectDoc {
  return { ...project, activeWorksheetId: worksheetId }
}

export function appendSession(project: ProjectDoc, block: Omit<SessionBlock, 'id' | 'createdAt'>): ProjectDoc {
  const entry: SessionBlock = {
    ...block,
    id: generateId('ses'),
    createdAt: new Date().toISOString()
  }
  return { ...project, session: [...project.session, entry] }
}

export function addGraph(project: ProjectDoc, graph: Omit<GraphDoc, 'id' | 'createdAt'>): ProjectDoc {
  const entry: GraphDoc = { ...graph, id: generateId('grf'), createdAt: new Date().toISOString() }
  return { ...project, graphs: [...project.graphs, entry] }
}
