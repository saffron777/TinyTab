import { describe, expect, it } from 'vitest'
import {
  ALL_ANALYSES,
  ANALYSIS_CATEGORIES,
  analysesByCategory,
  findAnalysis,
  formatAnalysisResult,
  sectionToPlainText
} from '@domain/analysis'
import type { AnalysisInput } from '@domain/analysis/types'
import { Recorder } from './encode'

/** Réplica exacta del generador de entradas de `tests/unit/analysis-smoke.test.ts`. */
const VALUES = Array.from(
  { length: 30 },
  (_, index) => Math.round((10 + index * 0.7 + Math.sin(index) * 2) * 100) / 100
)

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

const catalog = new Recorder()

catalog.add('ALL_ANALYSES', [], () =>
  ALL_ANALYSES.map((analysis) => ({
    id: analysis.id,
    label: analysis.label,
    category: analysis.category,
    description: analysis.description,
    numericColumns: analysis.numericColumns.map((column) => ({
      key: column.key,
      label: column.label,
      required: column.required,
      multiple: column.multiple ?? false
    })),
    textColumns: (analysis.textColumns ?? []).map((column) => ({
      key: column.key,
      label: column.label,
      required: column.required,
      multiple: column.multiple ?? false
    })),
    params: analysis.params.map((param) => ({
      key: param.key,
      label: param.label,
      kind: param.kind,
      defaultValue: param.defaultValue,
      options: (param.options ?? []).map((option) => ({ value: option.value, label: option.label })),
      optional: param.optional ?? false
    }))
  }))
)
catalog.add('ANALYSIS_CATEGORIES', [], () => ANALYSIS_CATEGORIES)
catalog.add('analysesByCategory', [], () =>
  analysesByCategory().map((group) => ({
    category: group.category,
    ids: group.analyses.map((analysis) => analysis.id)
  }))
)

const analyses = new Recorder()
for (const analysis of ALL_ANALYSES) {
  analyses.addRaw(analysis.id, [buildInput(analysis)], analysis.run(buildInput(analysis)))
}

const textFormat = new Recorder()

function formatOf(id: string): unknown {
  const definition = findAnalysis(id)
  if (!definition) return { missing: id }
  const result = definition.run(buildInput(definition))
  return result.ok ? formatAnalysisResult(result.value) : result
}

for (const id of ['describe', 'graphical-summary', 'capability', 'one-sample-t', 'anova-one-way']) {
  textFormat.addRaw('formatAnalysisResult', [id], formatOf(id))
}

const describeDefinition = findAnalysis('describe')
if (describeDefinition) {
  const describeResult = describeDefinition.run(buildInput(describeDefinition))
  if (describeResult.ok) {
    const tableSection = describeResult.value.sections[0]
    if (tableSection) {
      textFormat.addRaw('sectionToPlainText', [tableSection], sectionToPlainText(tableSection))
    }
    textFormat.addRaw(
      'sectionToPlainText',
      [{ kind: 'text', title: 'T', text: 'contenido' }],
      sectionToPlainText({ kind: 'text', title: 'T', text: 'contenido' })
    )
  }
}

describe('vectores dorados del catálogo de análisis', () => {
  it('exporta catalog.json con los 60 análisis', () => {
    expect(ALL_ANALYSES).toHaveLength(60)
    expect(catalog.write('catalog')).toBeGreaterThan(2)
  })

  it('exporta analyses.json con una entrada por análisis', () => {
    expect(analyses.write('analyses')).toBe(ALL_ANALYSES.length)
  })

  it('exporta text-format.json', () => {
    expect(textFormat.write('text-format')).toBeGreaterThan(4)
  })
})
