import { describe, expect, it } from 'vitest'
import { ALL_ANALYSES } from '@domain/analysis'
import type { AnalysisInput } from '@domain/analysis/types'
import { DISTRIBUTIONS, defaultParams, getDistribution } from '@domain/stats/distributions'

const VALUES = Array.from({ length: 30 }, (_, index) => Math.round((10 + index * 0.7 + Math.sin(index) * 2) * 100) / 100)

function buildInput(analysis: (typeof ALL_ANALYSES)[number]): AnalysisInput {
  const numeric: Record<string, readonly number[]> = {}
  for (const requirement of analysis.numericColumns) {
    if (requirement.multiple) {
      numeric[`Var1`] = VALUES
      numeric[`Var2`] = VALUES.map((value, index) => value * 1.5 + index)
    } else {
      numeric[requirement.key] = VALUES
    }
  }
  const text: Record<string, readonly (string | number)[]> = {}
  for (const requirement of analysis.textColumns ?? []) {
    text[requirement.key] = VALUES.map((_, index) => (index % 2 === 0 ? 'A' : 'B'))
  }
  const params: Record<string, number | string> = {}
  for (const param of analysis.params) {
    params[param.key] = param.defaultValue
  }
  return { numeric, text, params }
}

describe('humo del catálogo de análisis', () => {
  it('ningún análisis lanza excepciones y la mayoría produce resultados', () => {
    const failures: string[] = []
    const successes: string[] = []
    for (const analysis of ALL_ANALYSES) {
      try {
        const result = analysis.run(buildInput(analysis))
        if (result.ok) successes.push(analysis.id)
        else failures.push(`${analysis.id}: ${result.error.code} — ${result.error.message}`)
      } catch (error) {
        failures.push(`${analysis.id}: EXCEPCIÓN ${String(error)}`)
      }
    }
    console.log(`Análisis OK: ${successes.length}/${ALL_ANALYSES.length}`)
    if (failures.length > 0) console.log('Fallos controlados:\n' + failures.join('\n'))
    expect(successes.length).toBeGreaterThanOrEqual(Math.ceil(ALL_ANALYSES.length * 0.7))
    for (const failure of failures) expect(failure).not.toContain('EXCEPCIÓN')
  })

  it('las 21 distribuciones tienen parámetros por defecto válidos', () => {
    expect(DISTRIBUTIONS).toHaveLength(21)
    for (const distribution of DISTRIBUTIONS) {
      const params = defaultParams(getDistribution(distribution.id))
      expect(getDistribution(distribution.id).validate(params).ok).toBe(true)
    }
  })
})
