import { gammq } from './numeric'
import { chiSquareIndependence } from './tests'
import { err, ok, type Result } from '../model/result'

export interface TallyRow {
  readonly value: string
  readonly count: number
  readonly percent: number
  readonly cumulativePercent: number
}

export function tally(values: readonly (string | number)[]): TallyRow[] {
  const counts = new Map<string, number>()
  for (const value of values) {
    const key = String(value)
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }
  const total = values.length
  const rows = [...counts.entries()].sort((a, b) => b[1] - a[1])
  let cumulative = 0
  return rows.map(([value, count]) => {
    cumulative += count
    return {
      value,
      count,
      percent: total > 0 ? (count / total) * 100 : 0,
      cumulativePercent: total > 0 ? (cumulative / total) * 100 : 0
    }
  })
}

export interface CrossTabResult {
  readonly rowLabels: string[]
  readonly columnLabels: string[]
  readonly counts: number[][]
  readonly expected: number[][]
  readonly rowTotals: number[]
  readonly columnTotals: number[]
  readonly total: number
  readonly statistic: number
  readonly df: number
  readonly pValue: number
}

export function crossTabulation(
  rows: readonly (string | number)[],
  columns: readonly (string | number)[]
): Result<CrossTabResult> {
  const n = Math.min(rows.length, columns.length)
  if (n === 0) return err('INSUFFICIENT_DATA', 'No hay datos para tabular.')
  const rowLabels = [...new Set(rows.slice(0, n).map(String))].sort()
  const columnLabels = [...new Set(columns.slice(0, n).map(String))].sort()
  const counts = Array.from({ length: rowLabels.length }, () => Array.from({ length: columnLabels.length }, () => 0))
  for (let i = 0; i < n; i += 1) {
    const rowIndex = rowLabels.indexOf(String(rows[i]))
    const columnIndex = columnLabels.indexOf(String(columns[i]))
    if (rowIndex >= 0 && columnIndex >= 0) {
      const row = counts[rowIndex] as number[]
      row[columnIndex] = (row[columnIndex] as number) + 1
    }
  }
  const chi = chiSquareIndependence(counts)
  if (!chi.ok) return chi
  const rowTotals = counts.map((row) => row.reduce((a, b) => a + b, 0))
  const columnTotals = columnLabels.map((_, j) => counts.reduce((acc, row) => acc + (row[j] as number), 0))
  return ok({
    rowLabels,
    columnLabels,
    counts,
    expected: chi.value.expected,
    rowTotals,
    columnTotals,
    total: n,
    statistic: chi.value.statistic,
    df: chi.value.df,
    pValue: chi.value.pValue
  })
}

export interface GoodnessOfFitResult {
  readonly statistic: number
  readonly df: number
  readonly pValue: number
  readonly rows: { label: string; observed: number; expected: number; contribution: number }[]
}

export function goodnessOfFit(
  labels: readonly string[],
  observed: readonly number[],
  expectedProportions?: readonly number[]
): Result<GoodnessOfFitResult> {
  if (labels.length !== observed.length || labels.length < 2) return err('INVALID_INPUT', 'Categorías incompatibles.')
  const total = observed.reduce((a, b) => a + b, 0)
  if (total <= 0) return err('INSUFFICIENT_DATA', 'Las frecuencias observadas deben sumar más de cero.')
  const proportions = expectedProportions ?? labels.map(() => 1 / labels.length)
  const sumProportions = proportions.reduce((a, b) => a + b, 0)
  if (Math.abs(sumProportions - 1) > 1e-6) return err('INVALID_INPUT', 'Las proporciones esperadas deben sumar 1.')
  let statistic = 0
  const rows = labels.map((label, i) => {
    const expected = total * (proportions[i] as number)
    const contribution = expected > 0 ? Math.pow((observed[i] as number) - expected, 2) / expected : Number.POSITIVE_INFINITY
    statistic += contribution
    return { label, observed: observed[i] as number, expected, contribution }
  })
  const df = labels.length - 1
  return ok({ statistic, df, pValue: gammq(df / 2, statistic / 2), rows })
}
