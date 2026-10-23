import type { AnalysisInput, AnalysisResult } from '../../domain/analysis/types'
import { findAnalysis, formatAnalysisResult } from '../../domain/analysis'
import { appendSession, type ProjectDoc } from '../../domain/model/project'
import { activeWorksheet } from '../../domain/model/project'
import { columnValues, columnByName } from '../../domain/model/worksheet'
import { toNumericValues } from '../../domain/model/value'
import { err, ok, type Result } from '../../domain/model/result'

export interface ColumnSelection {
  readonly key: string
  readonly columnName?: string
  readonly columnNames?: string[]
}

export interface RunAnalysisRequest {
  readonly analysisId: string
  /** Columnas numéricas: una por rol; los roles `multiple` llevan lista. */
  readonly numeric: readonly ColumnSelection[]
  /** Columnas de texto por rol. */
  readonly text?: readonly ColumnSelection[]
  readonly params: Readonly<Record<string, number | string>>
}

export interface RunAnalysisOutcome {
  readonly project: ProjectDoc
  readonly result: AnalysisResult
  readonly sessionText: string
}

/** Ejecuta un análisis del registro sobre la hoja activa y registra la salida en la sesión. */
export async function runAnalysis(
  project: ProjectDoc,
  request: RunAnalysisRequest,
  runner: { run(analysisId: string, input: AnalysisInput): Promise<Result<AnalysisResult>> }
): Promise<Result<RunAnalysisOutcome>> {
  const definition = findAnalysis(request.analysisId)
  if (!definition) return err('INVALID_INPUT', `Análisis desconocido: ${request.analysisId}`)
  const worksheet = activeWorksheet(project)
  const numeric: Record<string, readonly number[]> = {}
  for (const requirement of definition.numericColumns) {
    const selection = request.numeric.find((item) => item.key === requirement.key)
    const names = selection?.columnNames ?? (selection?.columnName ? [selection.columnName] : [])
    if (requirement.required && names.length === 0) return err('INVALID_INPUT', `Falta la columna para "${requirement.label}".`)
    if (names.length === 0) continue
    if (!requirement.multiple && names.length > 1) return err('INVALID_INPUT', `"${requirement.label}" admite una sola columna.`)
    for (const name of names) {
      const column = columnByName(worksheet, name)
      if (!column) return err('INVALID_INPUT', `No existe la columna "${name}".`)
      const values = toNumericValues(columnValues(worksheet, column.id))
      if (!values.ok) return values
      numeric[requirement.multiple ? name : requirement.key] = values.value
    }
  }
  const text: Record<string, readonly (string | number)[]> = {}
  for (const requirement of definition.textColumns ?? []) {
    const selection = request.text?.find((item) => item.key === requirement.key)
    const names = selection?.columnNames ?? (selection?.columnName ? [selection.columnName] : [])
    if (requirement.required && names.length === 0) return err('INVALID_INPUT', `Falta la columna para "${requirement.label}".`)
    if (names.length === 0) continue
    const name = names[0] as string
    const column = columnByName(worksheet, name)
    if (!column) return err('INVALID_INPUT', `No existe la columna "${name}".`)
    text[requirement.key] = columnValues(worksheet, column.id).map((value) => (value === null ? '' : value))
  }
  const result = await runner.run(request.analysisId, { numeric, text, params: request.params })
  if (!result.ok) return result
  const sessionText = formatAnalysisResult(result.value)
  const nextProject = appendSession(project, {
    kind: 'output',
    title: result.value.title,
    text: sessionText
  })
  return ok({ project: nextProject, result: result.value, sessionText })
}
