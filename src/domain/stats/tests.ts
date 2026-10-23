/**
 * Pruebas de hipótesis clásicas y no paramétricas.
 * Todas devuelven resultados tipados con estadístico, p-valor e intervalos.
 */
import { betai, bisectInverse, gammp, gammq, normalCdf, normalInv, quantile, ranks, sd, mean, sorted } from './numeric'
import { err, ok, type Result } from '../model/result'

export type Alternative = 'two-sided' | 'less' | 'greater'

export interface TTestResult {
  readonly test: 't' | 'z'
  readonly statistic: number
  readonly df: number
  readonly pValue: number
  readonly estimate: number
  readonly nullValue: number
  readonly standardError: number
  readonly lower: number
  readonly upper: number
  readonly confidence: number
  readonly alternative: Alternative
}

export function tCdf(x: number, df: number): number {
  if (!(df > 0)) return Number.NaN
  const tail = 0.5 * betai(df / 2, 0.5, df / (df + x * x))
  return x >= 0 ? 1 - tail : tail
}

export function tQuantile(p: number, df: number): number {
  if (p <= 0) return Number.NEGATIVE_INFINITY
  if (p >= 1) return Number.POSITIVE_INFINITY
  let lo = -1e6
  let hi = 1e6
  for (let i = 0; i < 200; i += 1) {
    const mid = (lo + hi) / 2
    if (tCdf(mid, df) < p) lo = mid
    else hi = mid
  }
  return (lo + hi) / 2
}

function pValueFromStatistic(statistic: number, cdf: (x: number) => number, alternative: Alternative): number {
  if (alternative === 'less') return cdf(statistic)
  if (alternative === 'greater') return 1 - cdf(statistic)
  return 2 * Math.min(cdf(statistic), 1 - cdf(statistic))
}

export function oneSampleT(
  values: readonly number[],
  nullValue = 0,
  confidence = 0.95,
  alternative: Alternative = 'two-sided'
): Result<TTestResult> {
  const n = values.length
  if (n < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 observaciones.')
  const estimate = mean(values)
  const s = sd(values)
  if (s === 0) return err('INVALID_INPUT', 'La desviación estándar es cero.')
  const se = s / Math.sqrt(n)
  const statistic = (estimate - nullValue) / se
  const df = n - 1
  const pValue = pValueFromStatistic(statistic, (x) => tCdf(x, df), alternative)
  const alpha = 1 - confidence
  const critical = tQuantile(alternative === 'two-sided' ? 1 - alpha / 2 : 1 - alpha, df)
  return ok({
    test: 't',
    statistic,
    df,
    pValue,
    estimate,
    nullValue,
    standardError: se,
    lower: estimate - critical * se,
    upper: estimate + critical * se,
    confidence,
    alternative
  })
}

export function oneSampleZ(
  values: readonly number[],
  sigma: number,
  nullValue = 0,
  confidence = 0.95,
  alternative: Alternative = 'two-sided'
): Result<TTestResult> {
  const n = values.length
  if (n < 1) return err('INSUFFICIENT_DATA', 'Se necesita al menos una observación.')
  if (!(sigma > 0)) return err('INVALID_PARAMETER', 'σ debe ser mayor que cero.')
  const estimate = mean(values)
  const se = sigma / Math.sqrt(n)
  const statistic = (estimate - nullValue) / se
  const pValue = pValueFromStatistic(statistic, normalCdf, alternative)
  const alpha = 1 - confidence
  const critical = normalInv(alternative === 'two-sided' ? 1 - alpha / 2 : 1 - alpha)
  return ok({
    test: 'z',
    statistic,
    df: Number.POSITIVE_INFINITY,
    pValue,
    estimate,
    nullValue,
    standardError: se,
    lower: estimate - critical * se,
    upper: estimate + critical * se,
    confidence,
    alternative
  })
}

export function twoSampleT(
  first: readonly number[],
  second: readonly number[],
  options: { pooled?: boolean; confidence?: number; alternative?: Alternative } = {}
): Result<TTestResult> {
  const n1 = first.length
  const n2 = second.length
  if (n1 < 2 || n2 < 2) return err('INSUFFICIENT_DATA', 'Cada muestra necesita al menos 2 observaciones.')
  const mean1 = mean(first)
  const mean2 = mean(second)
  const s1 = sd(first)
  const s2 = sd(second)
  const difference = mean1 - mean2
  const alternative = options.alternative ?? 'two-sided'
  const confidence = options.confidence ?? 0.95
  let se: number
  let df: number
  if (options.pooled) {
    df = n1 + n2 - 2
    const pooledVariance = ((n1 - 1) * s1 * s1 + (n2 - 1) * s2 * s2) / df
    se = Math.sqrt(pooledVariance * (1 / n1 + 1 / n2))
  } else {
    const v1 = (s1 * s1) / n1
    const v2 = (s2 * s2) / n2
    se = Math.sqrt(v1 + v2)
    df = Math.pow(v1 + v2, 2) / ((v1 * v1) / (n1 - 1) + (v2 * v2) / (n2 - 1))
  }
  if (se === 0) return err('INVALID_INPUT', 'La variabilidad es cero en ambas muestras.')
  const statistic = difference / se
  const pValue = pValueFromStatistic(statistic, (x) => tCdf(x, df), alternative)
  const alpha = 1 - confidence
  const critical = tQuantile(alternative === 'two-sided' ? 1 - alpha / 2 : 1 - alpha, df)
  return ok({
    test: 't',
    statistic,
    df,
    pValue,
    estimate: difference,
    nullValue: 0,
    standardError: se,
    lower: difference - critical * se,
    upper: difference + critical * se,
    confidence,
    alternative
  })
}

export function pairedT(
  first: readonly number[],
  second: readonly number[],
  options: { confidence?: number; alternative?: Alternative } = {}
): Result<TTestResult> {
  const n = Math.min(first.length, second.length)
  if (n < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 pares completos.')
  const differences = Array.from({ length: n }, (_, i) => (first[i] as number) - (second[i] as number))
  return oneSampleT(differences, 0, options.confidence ?? 0.95, options.alternative ?? 'two-sided')
}

export interface ProportionResult {
  readonly estimate: number
  readonly nullValue: number
  readonly z: number
  readonly pValue: number
  readonly lower: number
  readonly upper: number
  readonly confidence: number
}

function wilsonInterval(p: number, n: number, confidence: number): { lower: number; upper: number } {
  const z = normalInv(1 - (1 - confidence) / 2)
  const denominator = 1 + (z * z) / n
  const center = (p + (z * z) / (2 * n)) / denominator
  const half = (z * Math.sqrt((p * (1 - p)) / n + (z * z) / (4 * n * n))) / denominator
  return { lower: Math.max(0, center - half), upper: Math.min(1, center + half) }
}

export function oneProportion(
  successes: number,
  trials: number,
  nullValue = 0.5,
  confidence = 0.95,
  alternative: Alternative = 'two-sided'
): Result<ProportionResult> {
  if (trials <= 0 || successes < 0 || successes > trials) return err('INVALID_PARAMETER', 'Datos de proporción inválidos.')
  const p = successes / trials
  const se = Math.sqrt((nullValue * (1 - nullValue)) / trials)
  if (se === 0) return err('INVALID_PARAMETER', 'La proporción nula debe estar entre 0 y 1.')
  const z = (p - nullValue) / se
  const pValue = pValueFromStatistic(z, normalCdf, alternative)
  const interval = wilsonInterval(p, trials, confidence)
  return ok({ estimate: p, nullValue, z, pValue, lower: interval.lower, upper: interval.upper, confidence })
}

export function twoProportions(
  successes1: number,
  trials1: number,
  successes2: number,
  trials2: number,
  confidence = 0.95,
  alternative: Alternative = 'two-sided'
): Result<ProportionResult> {
  if (trials1 <= 0 || trials2 <= 0) return err('INVALID_PARAMETER', 'Los tamaños de muestra deben ser positivos.')
  const p1 = successes1 / trials1
  const p2 = successes2 / trials2
  const pooled = (successes1 + successes2) / (trials1 + trials2)
  const se = Math.sqrt(pooled * (1 - pooled) * (1 / trials1 + 1 / trials2))
  if (se === 0) return err('INVALID_INPUT', 'No hay variabilidad para el contraste.')
  const z = (p1 - p2) / se
  const pValue = pValueFromStatistic(z, normalCdf, alternative)
  const alpha = 1 - confidence
  const critical = normalInv(1 - alpha / 2)
  const seDiff = Math.sqrt((p1 * (1 - p1)) / trials1 + (p2 * (1 - p2)) / trials2)
  return ok({
    estimate: p1 - p2,
    nullValue: 0,
    z,
    pValue,
    lower: p1 - p2 - critical * seDiff,
    upper: p1 - p2 + critical * seDiff,
    confidence
  })
}

export interface VarianceTestResult {
  readonly statistic: number
  readonly df: number
  readonly pValue: number
  readonly ratio: number
  readonly lower: number
  readonly upper: number
  readonly confidence: number
}

export function oneVariance(
  sampleVariance: number,
  n: number,
  nullValue = 1,
  confidence = 0.95,
  alternative: Alternative = 'two-sided'
): Result<VarianceTestResult> {
  if (!(sampleVariance >= 0) || n < 2 || !(nullValue > 0)) return err('INVALID_PARAMETER', 'Parámetros de varianza inválidos.')
  const df = n - 1
  const statistic = (df * sampleVariance) / nullValue
  const pValue = pValueFromStatistic(
    statistic,
    (x) => (x <= 0 ? 0 : gammp(df / 2, x / 2)),
    alternative
  )
  const alpha = 1 - confidence
  const chiLower = alternative === 'two-sided' ? chiSquareInv(alpha / 2, df) : chiSquareInv(alpha, df)
  const chiUpper = alternative === 'two-sided' ? chiSquareInv(1 - alpha / 2, df) : chiSquareInv(1 - alpha, df)
  return ok({
    statistic,
    df,
    pValue,
    ratio: sampleVariance / nullValue,
    lower: (df * sampleVariance) / chiUpper,
    upper: (df * sampleVariance) / chiLower,
    confidence
  })
}

function chiSquareInv(p: number, df: number): number {
  return bisectInverse((x) => gammp(df / 2, x / 2), p, 0, 1e6)
}

export function twoVariances(
  variance1: number,
  n1: number,
  variance2: number,
  n2: number,
  confidence = 0.95,
  alternative: Alternative = 'two-sided'
): Result<VarianceTestResult> {
  if (!(variance1 >= 0) || !(variance2 > 0) || n1 < 2 || n2 < 2) return err('INVALID_PARAMETER', 'Parámetros de varianza inválidos.')
  const df1 = n1 - 1
  const df2 = n2 - 1
  const statistic = variance1 / variance2
  const fCdf = (x: number): number => (x <= 0 ? 0 : betai(df1 / 2, df2 / 2, (df1 * x) / (df1 * x + df2)))
  const pValue = pValueFromStatistic(statistic, fCdf, alternative)
  const alpha = 1 - confidence
  const fLower = fQuantile(alpha / 2, df1, df2)
  const fUpper = fQuantile(1 - alpha / 2, df1, df2)
  return ok({
    statistic,
    df: df1,
    pValue,
    ratio: statistic,
    lower: statistic / fUpper,
    upper: statistic / fLower,
    confidence
  })
}

function fQuantile(p: number, df1: number, df2: number): number {
  return bisectInverse((x) => (x <= 0 ? 0 : betai(df1 / 2, df2 / 2, (df1 * x) / (df1 * x + df2))), p, 0, 1e8)
}

export interface ChiSquareResult {
  readonly statistic: number
  readonly df: number
  readonly pValue: number
}

export function chiSquareGoodnessOfFit(observed: readonly number[], expected: readonly number[]): Result<ChiSquareResult> {
  if (observed.length !== expected.length || observed.length < 2) return err('INVALID_INPUT', 'Categorías incompatibles.')
  if (observed.some((value) => value < 0) || expected.some((value) => value <= 0)) {
    return err('INVALID_INPUT', 'Las frecuencias deben ser no negativas y las esperadas positivas.')
  }
  let statistic = 0
  for (let i = 0; i < observed.length; i += 1) {
    const diff = (observed[i] as number) - (expected[i] as number)
    statistic += (diff * diff) / (expected[i] as number)
  }
  const df = observed.length - 1
  return ok({ statistic, df, pValue: gammq(df / 2, statistic / 2) })
}

export function chiSquareIndependence(table: readonly (readonly number[])[]): Result<ChiSquareResult & { expected: number[][] }> {
  const rows = table.length
  const cols = table[0]?.length ?? 0
  if (rows < 2 || cols < 2) return err('INVALID_INPUT', 'La tabla debe tener al menos 2x2.')
  const rowTotals = table.map((row) => row.reduce((a, b) => a + b, 0))
  const colTotals = Array.from({ length: cols }, (_, j) => table.reduce((acc, row) => acc + (row[j] as number), 0))
  const total = rowTotals.reduce((a, b) => a + b, 0)
  let statistic = 0
  const expected: number[][] = []
  for (let i = 0; i < rows; i += 1) {
    expected.push([])
    for (let j = 0; j < cols; j += 1) {
      const e = ((rowTotals[i] as number) * (colTotals[j] as number)) / total
      ;(expected[i] as number[]).push(e)
      if (e > 0) statistic += Math.pow((table[i]?.[j] as number) - e, 2) / e
    }
  }
  const df = (rows - 1) * (cols - 1)
  return ok({ statistic, df, pValue: gammq(df / 2, statistic / 2), expected })
}

/** Prueba de normalidad de Anderson-Darling (parámetros estimados). */
export function andersonDarling(values: readonly number[]): Result<{ statistic: number; pValue: number; n: number }> {
  const n = values.length
  if (n < 8) return err('INSUFFICIENT_DATA', 'Se recomiendan al menos 8 observaciones.')
  const center = mean(values)
  const s = sd(values)
  if (!(s > 0)) return err('INVALID_INPUT', 'Los datos no tienen variabilidad.')
  const ordered = sorted(values)
  let sumA = 0
  for (let i = 1; i <= n; i += 1) {
    const f1 = Math.min(1 - 1e-15, Math.max(1e-15, normalCdf(((ordered[i - 1] as number) - center) / s)))
    const f2 = Math.min(1 - 1e-15, Math.max(1e-15, normalCdf(((ordered[n - i] as number) - center) / s)))
    sumA += (2 * i - 1) * (Math.log(f1) + Math.log(1 - f2))
  }
  const statistic = -n - sumA / n
  const adjusted = statistic * (1 + 0.75 / n + 2.25 / (n * n))
  let pValue: number
  if (adjusted < 0.2) pValue = 1 - Math.exp(-13.436 + 101.14 * adjusted - 223.73 * adjusted * adjusted)
  else if (adjusted < 0.34) pValue = 1 - Math.exp(-8.318 + 42.796 * adjusted - 59.938 * adjusted * adjusted)
  else if (adjusted < 0.6) pValue = Math.exp(0.9177 - 4.279 * adjusted - 1.38 * adjusted * adjusted)
  else pValue = Math.exp(1.2937 - 5.709 * adjusted + 0.0186 * adjusted * adjusted)
  return ok({ statistic, pValue: Math.min(1, Math.max(0, pValue)), n })
}

/** Prueba de los signos (binomial exacta sobre la mediana). */
export function signTest(
  values: readonly number[],
  nullMedian = 0,
  alternative: Alternative = 'two-sided'
): Result<{ positives: number; negatives: number; pValue: number }> {
  let positives = 0
  let negatives = 0
  for (const value of values) {
    if (value > nullMedian) positives += 1
    else if (value < nullMedian) negatives += 1
  }
  const n = positives + negatives
  if (n === 0) return err('INSUFFICIENT_DATA', 'Todas las observaciones son iguales a la mediana nula.')
  const k = Math.min(positives, negatives)
  let cumulative = 0
  for (let i = 0; i <= k; i += 1) {
    cumulative += Math.exp(logCombinationLocal(n, i) - n * Math.LN2)
  }
  const pValue = alternative === 'two-sided' ? Math.min(1, 2 * cumulative) : alternative === 'less' ? cumulative : 1 - cumulative + Math.exp(logCombinationLocal(n, 0) - n * Math.LN2)
  return ok({ positives, negatives, pValue })
}

function logCombinationLocal(n: number, k: number): number {
  let result = 0
  for (let i = 0; i < k; i += 1) result += Math.log(n - i) - Math.log(i + 1)
  return result
}

export function wilcoxonSignedRank(
  values: readonly number[],
  nullMedian = 0,
  alternative: Alternative = 'two-sided'
): Result<{ statistic: number; pValue: number; n: number }> {
  const differences = values.map((value) => value - nullMedian).filter((value) => value !== 0)
  const n = differences.length
  if (n < 5) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 5 diferencias no nulas.')
  const absolute = differences.map((value) => Math.abs(value))
  const ranksOf = ranks(absolute)
  let wPlus = 0
  for (let i = 0; i < n; i += 1) if ((differences[i] as number) > 0) wPlus += ranksOf[i] as number
  const meanW = (n * (n + 1)) / 4
  const varianceW = (n * (n + 1) * (2 * n + 1)) / 24
  const z = (wPlus - meanW) / Math.sqrt(varianceW)
  const pValue = pValueFromStatistic(z, normalCdf, alternative)
  return ok({ statistic: wPlus, pValue, n })
}

export function mannWhitney(
  first: readonly number[],
  second: readonly number[],
  alternative: Alternative = 'two-sided'
): Result<{ statistic: number; pValue: number; u1: number; u2: number }> {
  const n1 = first.length
  const n2 = second.length
  if (n1 < 1 || n2 < 1) return err('INSUFFICIENT_DATA', 'Ambas muestras deben tener datos.')
  const combined = [...first, ...second]
  const rankOf = ranks(combined)
  let r1 = 0
  for (let i = 0; i < n1; i += 1) r1 += rankOf[i] as number
  const u1 = r1 - (n1 * (n1 + 1)) / 2
  const u2 = n1 * n2 - u1
  const meanU = (n1 * n2) / 2
  const varianceU = (n1 * n2 * (n1 + n2 + 1)) / 12
  const z = (u1 - meanU) / Math.sqrt(varianceU)
  const pValue = pValueFromStatistic(z, normalCdf, alternative)
  return ok({ statistic: Math.min(u1, u2), pValue, u1, u2 })
}

export function kruskalWallis(groups: readonly (readonly number[])[]): Result<{ statistic: number; df: number; pValue: number }> {
  const total = groups.reduce((acc, group) => acc + group.length, 0)
  if (groups.length < 2 || total < 3) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 grupos con datos.')
  const combined = groups.flatMap((group) => [...group])
  const rankOf = ranks(combined)
  let offset = 0
  let h = 0
  for (const group of groups) {
    let rankSum = 0
    for (let i = 0; i < group.length; i += 1) rankSum += rankOf[offset + i] as number
    offset += group.length
    h += (rankSum * rankSum) / group.length
  }
  h = (12 / (total * (total + 1))) * h - 3 * (total + 1)
  const df = groups.length - 1
  return ok({ statistic: h, df, pValue: gammq(df / 2, h / 2) })
}

export function runsTest(values: readonly number[]): Result<{ runs: number; z: number; pValue: number }> {
  const n = values.length
  if (n < 10) return err('INSUFFICIENT_DATA', 'Se recomiendan al menos 10 observaciones.')
  const medianValue = quantile(values, 0.5)
  const above = values.filter((value) => value > medianValue).length
  const below = values.filter((value) => value < medianValue).length
  if (above < 1 || below < 1) return err('INVALID_INPUT', 'Todos los valores están por encima o por debajo de la mediana.')
  let runs = 1
  for (let i = 1; i < n; i += 1) {
    const previous = (values[i - 1] as number) > medianValue
    const current = (values[i] as number) > medianValue
    if (previous !== current) runs += 1
  }
  const expectedRuns = (2 * above * below) / (above + below) + 1
  const varianceRuns =
    (2 * above * below * (2 * above * below - above - below)) / (Math.pow(above + below, 2) * (above + below - 1))
  const z = (runs - expectedRuns) / Math.sqrt(varianceRuns)
  return ok({ runs, z, pValue: 2 * Math.min(normalCdf(z), 1 - normalCdf(z)) })
}

export function spearmanCorrelation(x: readonly number[], y: readonly number[]): Result<{ rho: number; pValue: number }> {
  const n = Math.min(x.length, y.length)
  if (n < 3) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 3 pares.')
  const rx = ranks(x.slice(0, n))
  const ry = ranks(y.slice(0, n))
  const meanRx = mean(rx)
  const meanRy = mean(ry)
  let numerator = 0
  let denomX = 0
  let denomY = 0
  for (let i = 0; i < n; i += 1) {
    numerator += ((rx[i] as number) - meanRx) * ((ry[i] as number) - meanRy)
    denomX += Math.pow((rx[i] as number) - meanRx, 2)
    denomY += Math.pow((ry[i] as number) - meanRy, 2)
  }
  const rho = numerator / Math.sqrt(denomX * denomY)
  const t = rho * Math.sqrt((n - 2) / (1 - rho * rho))
  return ok({ rho, pValue: 2 * Math.min(tCdf(t, n - 2), 1 - tCdf(t, n - 2)) })
}

/** Prueba de equivalencia TOST para dos muestras independientes. */
export function tostTwoSample(
  first: readonly number[],
  second: readonly number[],
  margin: number,
  _confidence = 0.95
): Result<{ lowerP: number; upperP: number; pValue: number; estimate: number }> {
  if (!(margin > 0)) return err('INVALID_PARAMETER', 'El margen de equivalencia debe ser positivo.')
  const n1 = first.length
  const n2 = second.length
  if (n1 < 2 || n2 < 2) return err('INSUFFICIENT_DATA', 'Cada muestra necesita al menos 2 observaciones.')
  const difference = mean(first) - mean(second)
  const se = Math.sqrt(Math.pow(sd(first), 2) / n1 + Math.pow(sd(second), 2) / n2)
  if (se === 0) return err('INVALID_INPUT', 'No hay variabilidad.')
  const df = n1 + n2 - 2
  const lowerP = tCdf((difference + margin) / se, df)
  const upperP = 1 - tCdf((difference - margin) / se, df)
  return ok({ lowerP, upperP, pValue: Math.max(lowerP, upperP), estimate: difference })
}

export { normalCdf, normalInv }
