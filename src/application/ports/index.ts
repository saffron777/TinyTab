import type { AnalysisInput, AnalysisResult } from '../../domain/analysis/types'
import type { ProjectDoc } from '../../domain/model/project'
import type { Result } from '../../domain/model/result'

/** Puerto para ejecutar análisis (implementación síncrona o en worker). */
export interface AnalysisRunner {
  run(analysisId: string, input: AnalysisInput): Promise<Result<AnalysisResult>>
}

/** Puerto de persistencia de proyectos. */
export interface ProjectRepository {
  load(path: string): Promise<Result<ProjectDoc>>
  save(project: ProjectDoc, path: string): Promise<Result<void>>
}

/** Diálogos y acceso a archivos provistos por el sistema operativo. */
export interface FileGateway {
  openFile(kind: 'project' | 'csv'): Promise<{ path: string; name: string; text: string } | null>
  saveFile(kind: 'project' | 'csv' | 'session', suggestedName: string, text: string): Promise<string | null>
  saveProjectToPath(path: string, text: string): Promise<boolean>
  message(kind: 'info' | 'warning' | 'error', text: string): Promise<void>
  confirm(text: string): Promise<boolean>
}

/** Reloj inyectable para pruebas deterministas. */
export type Clock = () => string

export const systemClock: Clock = () => new Date().toISOString()
