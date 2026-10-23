import { ALL_ANALYSES, findAnalysis } from '../../domain/analysis'
import type { AnalysisInput } from '../../domain/analysis/types'
import type { Result } from '../../domain/model/result'
import type { AnalysisRunner } from '../ports'

/** Implementación síncrona del puerto, útil en tests y en el proceso principal. */
export const offlineAnalysisRunner: AnalysisRunner = {
  async run(analysisId, input) {
    const analysis = findAnalysis(analysisId)
    if (!analysis) return { ok: false, error: { code: 'INVALID_INPUT', message: `Análisis desconocido: ${analysisId}` } }
    return analysis.run(input)
  }
}

export interface AnalysisCatalogEntry {
  id: string
  label: string
  description: string
}

export function catalog(): AnalysisCatalogEntry[] {
  return ALL_ANALYSES.map((analysis) => ({ id: analysis.id, label: analysis.label, description: analysis.description }))
}

/** Valida un input contra la definición antes de ejecutarlo (para la UI y la API). */
export function validateInput(analysisId: string, input: AnalysisInput): Result<void> {
  const analysis = findAnalysis(analysisId)
  if (!analysis) return { ok: false, error: { code: 'INVALID_INPUT', message: 'Análisis desconocido.' } }
  for (const requirement of analysis.numericColumns) {
    if (!requirement.required) continue
    if (requirement.multiple) {
      const provided = Object.keys(input.numeric).length
      if (provided === 0) return { ok: false, error: { code: 'INSUFFICIENT_DATA', message: `Falta "${requirement.label}".` } }
    } else if ((input.numeric[requirement.key] ?? []).length === 0) {
      return { ok: false, error: { code: 'INSUFFICIENT_DATA', message: `Falta "${requirement.label}".` } }
    }
  }
  return { ok: true, value: undefined }
}
