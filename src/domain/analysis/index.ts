import { BASIC_ANALYSES } from './registry-basic'
import { ADVANCED_ANALYSES } from './registry-advanced'
import type { AnalysisCategory, AnalysisDefinition, AnalysisResult, AnalysisSection } from './types'

export * from './types'
export { BASIC_ANALYSES, ADVANCED_ANALYSES }

export const ALL_ANALYSES: readonly AnalysisDefinition[] = [...BASIC_ANALYSES, ...ADVANCED_ANALYSES]

export function findAnalysis(id: string): AnalysisDefinition | undefined {
  return ALL_ANALYSES.find((analysis) => analysis.id === id)
}

export const ANALYSIS_CATEGORIES: readonly AnalysisCategory[] = [
  'Estadísticas básicas',
  'Distribuciones',
  'Tablas',
  'Pruebas de hipótesis',
  'No paramétricos',
  'ANOVA',
  'Regresión',
  'Control de calidad',
  'Capacidad',
  'Sistemas de medida',
  'DOE',
  'Series temporales',
  'Fiabilidad',
  'Multivariante',
  'Potencia y tamaño de muestra'
]

export function analysesByCategory(): { category: AnalysisCategory; analyses: AnalysisDefinition[] }[] {
  return ANALYSIS_CATEGORIES.map((category) => ({
    category,
    analyses: ALL_ANALYSES.filter((analysis) => analysis.category === category)
  })).filter((group) => group.analyses.length > 0)
}

/** Formatea un resultado como texto para la ventana de sesión. */
export function formatAnalysisResult(result: AnalysisResult): string {
  const lines: string[] = []
  lines.push(result.title)
  if (result.subtitle) lines.push(result.subtitle)
  lines.push('')
  for (const section of result.sections) {
    lines.push(`─ ${section.title}`)
    if (section.kind === 'text') {
      lines.push(section.text)
    } else {
      lines.push(formatTable(section.headers, section.rows))
    }
    lines.push('')
  }
  if (result.charts && result.charts.length > 0) {
    lines.push(`Gráficos generados: ${result.charts.length} (${result.charts.map((chart) => chart.kind).join(', ')})`)
  }
  return lines.join('\n').trimEnd()
}

function formatTable(headers: readonly string[], rows: readonly (readonly string[])[]): string {
  const widths = headers.map((header, index) =>
    Math.max(header.length, ...rows.map((row) => (row[index] ?? '').length))
  )
  const renderRow = (row: readonly string[]): string =>
    row.map((cell, index) => (cell ?? '').padEnd(widths[index] ?? 0)).join('  ').trimEnd()
  const separator = widths.map((width) => '─'.repeat(width)).join('  ')
  return [renderRow(headers), separator, ...rows.map(renderRow)].join('\n')
}

export function sectionToPlainText(section: AnalysisSection): string {
  if (section.kind === 'text') return `${section.title}\n${section.text}`
  return `${section.title}\n${formatTable(section.headers, section.rows)}`
}
