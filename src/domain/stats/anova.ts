/** ANOVA de uno y dos factores, pruebas de igualdad de varianzas y comparaciones múltiples. */
import { betai, gammp, gammq, mean, sd } from './numeric'
import { tCdf, tQuantile } from './tests'
import { err, ok, type Result } from '../model/result'

export interface GroupSummary {
  readonly label: string
  readonly n: number
  readonly mean: number
  readonly sd: number
}

export interface AnovaTableRow {
  readonly source: string
  readonly df: number
  readonly ss: number
  readonly ms: number
  readonly f: number
  readonly p: number
}

export interface OneWayAnovaResult {
  readonly groups: GroupSummary[]
  readonly table: AnovaTableRow[]
  readonly totalSS: number
  readonly rSquared: number
  readonly pooledSD: number
}

export function oneWayAnova(groups: readonly (readonly number[])[], labels: readonly string[]): Result<OneWayAnovaResult> {
  const k = groups.length
  if (k < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos dos grupos.')
  const summaries: GroupSummary[] = groups.map((values, index) => ({
    label: labels[index] ?? `Grupo ${index + 1}`,
    n: values.length,
    mean: mean(values),
    sd: sd(values)
  }))
  if (summaries.some((summary) => summary.n < 2)) return err('INSUFFICIENT_DATA', 'Cada grupo necesita al menos 2 observaciones.')
  const all = groups.flatMap((values) => [...values])
  const grandMean = mean(all)
  let ssBetween = 0
  let ssWithin = 0
  for (let i = 0; i < k; i += 1) {
    const values = groups[i] as number[]
    const summary = summaries[i] as GroupSummary
    ssBetween += summary.n * Math.pow(summary.mean - grandMean, 2)
    for (const value of values) ssWithin += Math.pow(value - summary.mean, 2)
  }
  const totalSS = ssBetween + ssWithin
  const dfBetween = k - 1
  const dfWithin = all.length - k
  const msBetween = ssBetween / dfBetween
  const msWithin = dfWithin > 0 ? ssWithin / dfWithin : Number.NaN
  const f = msBetween / msWithin
  const p = fCdfUpper(f, dfBetween, dfWithin)
  return ok({
    groups: summaries,
    table: [
      { source: 'Factor', df: dfBetween, ss: ssBetween, ms: msBetween, f, p },
      { source: 'Error', df: dfWithin, ss: ssWithin, ms: msWithin, f: Number.NaN, p: Number.NaN },
      { source: 'Total', df: dfBetween + dfWithin, ss: totalSS, ms: Number.NaN, f: Number.NaN, p: Number.NaN }
    ],
    totalSS,
    rSquared: totalSS > 0 ? ssBetween / totalSS : Number.NaN,
    pooledSD: Math.sqrt(msWithin)
  })
}

export function fCdfUpper(f: number, df1: number, df2: number): number {
  if (!(f >= 0)) return Number.NaN
  if (f === 0) return 1
  return 1 - betai(df1 / 2, df2 / 2, (df1 * f) / (df1 * f + df2))
}

export interface TwoWayAnovaResult {
  readonly table: AnovaTableRow[]
  readonly rSquared: number
}

/** ANOVA de dos factores aditiva (sin interacción) con celdas balanceadas. */
export function twoWayAnova(
  response: readonly number[],
  factorA: readonly (string | number)[],
  factorB: readonly (string | number)[]
): Result<TwoWayAnovaResult> {
  const n = Math.min(response.length, factorA.length, factorB.length)
  if (n < 4) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 4 observaciones.')
  const levelsA = [...new Set(factorA.slice(0, n).map(String))]
  const levelsB = [...new Set(factorB.slice(0, n).map(String))]
  if (levelsA.length < 2 || levelsB.length < 2) return err('INVALID_INPUT', 'Cada factor necesita al menos dos niveles.')
  const grand = mean(response.slice(0, n))
  const cellMeans = new Map<string, { sum: number; count: number }>()
  const rowMeans = new Map<string, { sum: number; count: number }>()
  const colMeans = new Map<string, { sum: number; count: number }>()
  for (let i = 0; i < n; i += 1) {
    const key = `${String(factorA[i])}|${String(factorB[i])}`
    const cell = cellMeans.get(key) ?? { sum: 0, count: 0 }
    cell.sum += response[i] as number
    cell.count += 1
    cellMeans.set(key, cell)
    const row = rowMeans.get(String(factorA[i])) ?? { sum: 0, count: 0 }
    row.sum += response[i] as number
    row.count += 1
    rowMeans.set(String(factorA[i]), row)
    const col = colMeans.get(String(factorB[i])) ?? { sum: 0, count: 0 }
    col.sum += response[i] as number
    col.count += 1
    colMeans.set(String(factorB[i]), col)
  }
  let ssA = 0
  for (const { sum, count } of rowMeans.values()) ssA += count * Math.pow(sum / count - grand, 2)
  let ssB = 0
  for (const { sum, count } of colMeans.values()) ssB += count * Math.pow(sum / count - grand, 2)
  let ssTotal = 0
  for (let i = 0; i < n; i += 1) ssTotal += Math.pow((response[i] as number) - grand, 2)
  const ssError = Math.max(0, ssTotal - ssA - ssB)
  const dfA = levelsA.length - 1
  const dfB = levelsB.length - 1
  const dfError = n - levelsA.length - levelsB.length + 1
  const msA = ssA / dfA
  const msB = ssB / dfB
  const msError = dfError > 0 ? ssError / dfError : Number.NaN
  const table: AnovaTableRow[] = [
    { source: 'Factor A', df: dfA, ss: ssA, ms: msA, f: msA / msError, p: fCdfUpper(msA / msError, dfA, dfError) },
    { source: 'Factor B', df: dfB, ss: ssB, ms: msB, f: msB / msError, p: fCdfUpper(msB / msError, dfB, dfError) },
    { source: 'Error', df: dfError, ss: ssError, ms: msError, f: Number.NaN, p: Number.NaN },
    { source: 'Total', df: n - 1, ss: ssTotal, ms: Number.NaN, f: Number.NaN, p: Number.NaN }
  ]
  return ok({ table, rSquared: ssTotal > 0 ? (ssA + ssB) / ssTotal : Number.NaN })
}

export interface VarianceEqualityResult {
  readonly test: 'Levene' | 'Bartlett'
  readonly statistic: number
  readonly df: number
  readonly pValue: number
}

export function leveneTest(groups: readonly (readonly number[])[]): Result<VarianceEqualityResult> {
  if (groups.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos dos grupos.')
  const medians = groups.map((values) => {
    const ordered = [...values].sort((a, b) => a - b)
    const middle = Math.floor(ordered.length / 2)
    return ordered.length % 2 === 0 ? ((ordered[middle - 1] as number) + (ordered[middle] as number)) / 2 : (ordered[middle] as number)
  })
  const transformed = groups.map((values, index) => values.map((value) => Math.abs(value - (medians[index] as number))))
  const anova = oneWayAnova(transformed, groups.map((_, index) => `G${index + 1}`))
  if (!anova.ok) return anova
  const row = anova.value.table[0] as AnovaTableRow
  return ok({ test: 'Levene', statistic: row.f, df: row.df, pValue: row.p })
}

export function bartlettTest(groups: readonly (readonly number[])[]): Result<VarianceEqualityResult> {
  if (groups.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos dos grupos.')
  const variances = groups.map((values) => sd(values) ** 2)
  const sizes = groups.map((values) => values.length)
  if (variances.some((value) => !(value > 0))) return err('INVALID_INPUT', 'Bartlett requiere varianzas positivas en todos los grupos.')
  const total = sizes.reduce((a, b) => a + b, 0)
  const k = groups.length
  const pooled = groups.reduce((acc, _values, index) => acc + (sizes[index] as number - 1) * (variances[index] as number), 0) / (total - k)
  const numerator = (total - k) * Math.log(pooled) - groups.reduce((acc, _values, index) => acc + (sizes[index] as number - 1) * Math.log(variances[index] as number), 0)
  const denominator = 1 + (1 / (3 * (k - 1))) * (groups.reduce((acc, _values, index) => acc + 1 / (sizes[index] as number - 1), 0) - 1 / (total - k))
  const statistic = numerator / denominator
  return ok({ test: 'Bartlett', statistic, df: k - 1, pValue: gammq((k - 1) / 2, statistic / 2) })
}

export interface PairwiseComparison {
  readonly first: string
  readonly second: string
  readonly difference: number
  readonly standardError: number
  readonly t: number
  readonly pValue: number
  readonly significant: boolean
}

/** Comparaciones por pares con ajuste de Holm (método conservador y verificable). */
export function pairwiseComparisons(
  groups: readonly (readonly number[])[],
  labels: readonly string[],
  alpha = 0.05,
  method: 'holm' | 'fisher' = 'holm'
): Result<{ comparisons: PairwiseComparison[]; method: string }> {
  if (groups.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos dos grupos.')
  const anova = oneWayAnova(groups, labels)
  if (!anova.ok) return anova
  const mse = anova.value.table[1]?.ms as number
  const dfError = anova.value.table[1]?.df as number
  const raw: PairwiseComparison[] = []
  for (let i = 0; i < groups.length - 1; i += 1) {
    for (let j = i + 1; j < groups.length; j += 1) {
      const first = groups[i] as number[]
      const second = groups[j] as number[]
      const difference = mean(first) - mean(second)
      const se = Math.sqrt(mse * (1 / first.length + 1 / second.length))
      const t = difference / se
      raw.push({
        first: labels[i] ?? `G${i + 1}`,
        second: labels[j] ?? `G${j + 1}`,
        difference,
        standardError: se,
        t,
        pValue: 2 * Math.min(tCdf(t, dfError), 1 - tCdf(t, dfError)),
        significant: false
      })
    }
  }
  const ordered = [...raw].sort((a, b) => a.pValue - b.pValue)
  const m = ordered.length
  const adjusted = new Map<PairwiseComparison, number>()
  let previous = 0
  ordered.forEach((comparison, index) => {
    const factor = method === 'holm' ? m - index : m
    const value = Math.min(1, Math.max(previous, comparison.pValue * factor))
    previous = value
    adjusted.set(comparison, value)
  })
  const comparisons = raw.map((comparison) => {
    const pValue = adjusted.get(comparison) ?? comparison.pValue
    return { ...comparison, pValue, significant: pValue < alpha }
  })
  return ok({ comparisons, method: method === 'holm' ? 'Holm' : 'Fisher (LSD)' })
}

export interface LeveneBounds {
  readonly label: string
  readonly lower: number
  readonly upper: number
}

/** Intervalos de confianza para la media por grupo. */
export function groupConfidenceIntervals(
  groups: readonly (readonly number[])[],
  labels: readonly string[],
  confidence = 0.95
): LeveneBounds[] {
  return groups.map((values, index) => {
    const n = values.length
    const center = mean(values)
    const se = sd(values) / Math.sqrt(n)
    const critical = tQuantile(1 - (1 - confidence) / 2, Math.max(1, n - 1))
    return { label: labels[index] ?? `G${index + 1}`, lower: center - critical * se, upper: center + critical * se }
  })
}

/** CDF de F para reutilizar en otros módulos. */
export function fCdf(f: number, df1: number, df2: number): number {
  return 1 - fCdfUpper(f, df1, df2)
}

export { gammp }
