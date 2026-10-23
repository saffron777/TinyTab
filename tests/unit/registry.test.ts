import { describe, expect, it } from 'vitest'
import { analysesByCategory, ALL_ANALYSES, findAnalysis, formatAnalysisResult } from '@domain/analysis'
import { offlineAnalysisRunner } from '@application/usecases/analysis-runner'

describe('registro de análisis', () => {
  it('contiene al menos 40 análisis con identificadores únicos', () => {
    expect(ALL_ANALYSES.length).toBeGreaterThanOrEqual(40)
    expect(new Set(ALL_ANALYSES.map((analysis) => analysis.id)).size).toBe(ALL_ANALYSES.length)
    for (const analysis of ALL_ANALYSES) {
      expect(analysis.params.length).toBeGreaterThanOrEqual(0)
      expect(analysis.params.length + analysis.numericColumns.length + (analysis.textColumns?.length ?? 0)).toBeGreaterThan(0)
      expect(analysis.label.length).toBeGreaterThan(0)
    }
  })

  it('todos los análisis tienen categoría visible', () => {
    const groups = analysesByCategory()
    const total = groups.reduce((acc, group) => acc + group.analyses.length, 0)
    expect(total).toBe(ALL_ANALYSES.length)
  })

  it('ejecuta una descriptiva y formatea la salida', async () => {
    const definition = findAnalysis('describe')
    expect(definition).toBeDefined()
    if (!definition) return
    const result = await offlineAnalysisRunner.run('describe', { numeric: { x: [1, 2, 3, 4, 5] }, text: {}, params: { confidence: 0.95 } })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const text = formatAnalysisResult(result.value)
    expect(text).toContain('Estadísticas descriptivas')
    expect(text).toContain('Media')
    expect(text).toContain('Resumen')
  })

  it('informa errores de datos en los análisis', async () => {
    const result = await offlineAnalysisRunner.run('one-sample-t', { numeric: { x: [1] }, text: {}, params: { mu: 0, confidence: 0.95, alternative: 'two-sided' } })
    expect(result.ok).toBe(false)
  })
})
