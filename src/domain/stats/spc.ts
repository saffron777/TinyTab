/**
 * Control estadístico de procesos: cartas de control, reglas de detección y
 * estudios de capacidad.
 */
import { mean, quantile, sd } from './numeric'
import { normalCdf, normalInv } from './numeric'
import { err, ok, type Result } from '../model/result'

export interface ControlPoint {
  readonly index: number
  readonly value: number
  readonly centerLine: number
  readonly lowerLimit: number
  readonly upperLimit: number
  readonly outOfControl: boolean
  readonly rules: string[]
}

export interface ControlChartResult {
  readonly chart: string
  readonly centerLine: number
  readonly lowerLimit: number
  readonly upperLimit: number
  readonly sigma: number
  readonly points: ControlPoint[]
  readonly violations: number
}

export interface SubgroupPoint {
  readonly values: readonly number[]
}

const CHART_CONSTANTS: Record<number, { d2: number; d3: number; A2: number; D3: number; D4: number; B3: number; B4: number }> = {
  2: { d2: 1.128, d3: 0.853, A2: 1.88, D3: 0, D4: 3.267, B3: 0, B4: 3.267 },
  3: { d2: 1.693, d3: 0.888, A2: 1.023, D3: 0, D4: 2.574, B3: 0, B4: 2.568 },
  4: { d2: 2.059, d3: 0.88, A2: 0.729, D3: 0, D4: 2.282, B3: 0, B4: 2.266 },
  5: { d2: 2.326, d3: 0.864, A2: 0.577, D3: 0, D4: 2.114, B3: 0, B4: 2.089 },
  6: { d2: 2.534, d3: 0.848, A2: 0.483, D3: 0, D4: 2.004, B3: 0.03, B4: 1.97 },
  7: { d2: 2.704, d3: 0.833, A2: 0.419, D3: 0.076, D4: 1.924, B3: 0.118, B4: 1.882 },
  8: { d2: 2.847, d3: 0.82, A2: 0.373, D3: 0.136, D4: 1.864, B3: 0.185, B4: 1.815 },
  9: { d2: 2.97, d3: 0.808, A2: 0.337, D3: 0.184, D4: 1.816, B3: 0.239, B4: 1.761 },
  10: { d2: 3.078, d3: 0.797, A2: 0.308, D3: 0.223, D4: 1.777, B3: 0.284, B4: 1.716 },
  11: { d2: 3.173, d3: 0.787, A2: 0.285, D3: 0.256, D4: 1.744, B3: 0.321, B4: 1.679 },
  12: { d2: 3.258, d3: 0.778, A2: 0.266, D3: 0.283, D4: 1.717, B3: 0.354, B4: 1.646 },
  15: { d2: 3.472, d3: 0.755, A2: 0.223, D3: 0.347, D4: 1.653, B3: 0.428, B4: 1.572 },
  20: { d2: 3.735, d3: 0.729, A2: 0.18, D3: 0.411, D4: 1.585, B3: 0.51, B4: 1.49 },
  25: { d2: 3.931, d3: 0.708, A2: 0.153, D3: 0.459, D4: 1.541, B3: 0.565, B4: 1.435 }
}

function constantsFor(n: number): { d2: number; d3: number; A2: number; D3: number; D4: number; B3: number; B4: number } {
  const exact = CHART_CONSTANTS[n]
  if (exact) return exact
  const keys = Object.keys(CHART_CONSTANTS).map(Number).sort((a, b) => a - b)
  const lower = keys.filter((key) => key <= n).pop() ?? 2
  return CHART_CONSTANTS[lower] as { d2: number; d3: number; A2: number; D3: number; D4: number; B3: number; B4: number }
}

/** Reglas de Nelson/Western Electric aplicables a una carta con límites 3σ. */
export function evaluateRules(values: readonly number[], center: number, sigma: number): string[][] {
  const rules: string[][] = values.map(() => [])
  for (let i = 0; i < values.length; i += 1) {
    const value = values[i] as number
    const z = sigma > 0 ? (value - center) / sigma : 0
    if (Math.abs(z) > 3) (rules[i] as string[]).push('Regla 1: punto fuera de los límites de control')
    if (i >= 2) {
      const window = values.slice(i - 2, i + 1)
      if (window.every((item) => Math.abs((item - center) / sigma) > 2) && window.every((item) => item > center)) {
        ;(rules[i] as string[]).push('Regla 5: 2 de 3 puntos a más de 2σ en el mismo lado')
      }
      if (window.every((item) => Math.abs((item - center) / sigma) > 2) && window.every((item) => item < center)) {
        ;(rules[i] as string[]).push('Regla 5: 2 de 3 puntos a más de 2σ en el mismo lado')
      }
    }
    if (i >= 7) {
      const window = values.slice(i - 7, i + 1)
      if (window.every((item) => item > center)) (rules[i] as string[]).push('Regla 2: 8 puntos consecutivos al mismo lado')
      if (window.every((item) => item < center)) (rules[i] as string[]).push('Regla 2: 8 puntos consecutivos al mismo lado')
    }
    if (i >= 5) {
      const window = values.slice(i - 5, i + 1)
      const increasing = window.every((item, index) => index === 0 || item > (window[index - 1] as number))
      const decreasing = window.every((item, index) => index === 0 || item < (window[index - 1] as number))
      if (increasing || decreasing) (rules[i] as string[]).push('Regla 3: 6 puntos consecutivos en tendencia')
    }
  }
  return rules
}

function buildChart(
  chart: string,
  values: readonly number[],
  center: number,
  sigma: number,
  lower: number,
  upper: number
): ControlChartResult {
  const rules = evaluateRules(values, center, sigma)
  const points: ControlPoint[] = values.map((value, index) => ({
    index: index + 1,
    value,
    centerLine: center,
    lowerLimit: lower,
    upperLimit: upper,
    outOfControl: value < lower || value > upper || (rules[index] as string[]).length > 0,
    rules: rules[index] as string[]
  }))
  return {
    chart,
    centerLine: center,
    lowerLimit: lower,
    upperLimit: upper,
    sigma,
    points,
    violations: points.filter((point) => point.outOfControl).length
  }
}

export function xbarRChart(subgroups: readonly SubgroupPoint[]): Result<{ xbar: ControlChartResult; range: ControlChartResult }> {
  if (subgroups.length < 1) return err('INSUFFICIENT_DATA', 'No hay subgrupos.')
  const sizes = new Set(subgroups.map((group) => group.values.length))
  if (sizes.size !== 1) return err('INVALID_INPUT', 'Todos los subgrupos deben tener el mismo tamaño.')
  const n = [...sizes][0] as number
  if (n < 2) return err('INVALID_INPUT', 'El tamaño de subgrupo debe ser al menos 2.')
  const constants = constantsFor(n)
  const means = subgroups.map((group) => mean(group.values))
  const ranges = subgroups.map((group) => Math.max(...group.values) - Math.min(...group.values))
  const xbarbar = mean(means)
  const rbar = mean(ranges)
  const sigma = rbar / constants.d2
  return ok({
    xbar: buildChart(
      'X̄',
      means,
      xbarbar,
      sigma,
      xbarbar - constants.A2 * rbar,
      xbarbar + constants.A2 * rbar
    ),
    range: buildChart('R', ranges, rbar, (constants.d3 * sigma) || Number.NaN, constants.D3 * rbar, constants.D4 * rbar)
  })
}

export function xbarSChart(subgroups: readonly SubgroupPoint[]): Result<{ xbar: ControlChartResult; s: ControlChartResult }> {
  if (subgroups.length < 1) return err('INSUFFICIENT_DATA', 'No hay subgrupos.')
  const sizes = new Set(subgroups.map((group) => group.values.length))
  if (sizes.size !== 1) return err('INVALID_INPUT', 'Todos los subgrupos deben tener el mismo tamaño.')
  const n = [...sizes][0] as number
  if (n < 2) return err('INVALID_INPUT', 'El tamaño de subgrupo debe ser al menos 2.')
  const constants = constantsFor(n)
  const means = subgroups.map((group) => mean(group.values))
  const deviations = subgroups.map((group) => sd(group.values))
  const xbarbar = mean(means)
  const sbar = mean(deviations)
  const sigma = sbar / Math.sqrt(1 - 45 / (97 * n) + 1 / (9 * n * n))
  return ok({
    xbar: buildChart('X̄', means, xbarbar, sigma, xbarbar - constants.A2 * sbar * (3 / (constants.A2 * Math.sqrt(n))), xbarbar + constants.A2 * sbar * (3 / (constants.A2 * Math.sqrt(n)))),
    s: buildChart('S', deviations, sbar, sigma, constants.B3 * sbar, constants.B4 * sbar)
  })
}

export function individualsChart(values: readonly number[]): Result<{ individuals: ControlChartResult; movingRange: ControlChartResult }> {
  if (values.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 observaciones.')
  const ranges = values.slice(1).map((value, index) => Math.abs(value - (values[index] as number)))
  const mrbar = mean(ranges)
  const sigma = mrbar / 1.128
  const center = mean(values)
  return ok({
    individuals: buildChart('I', values, center, sigma, center - 3 * sigma, center + 3 * sigma),
    movingRange: buildChart('MR', ranges, mrbar, mrbar / 1.128, 0, 3.267 * mrbar)
  })
}

export function pChart(defectives: readonly number[], samples: readonly number[]): Result<ControlChartResult> {
  if (defectives.length !== samples.length || defectives.length < 2) return err('INVALID_INPUT', 'Datos incompatibles.')
  const proportions = defectives.map((value, index) => value / (samples[index] as number))
  const pbar = defectives.reduce((a, b) => a + b, 0) / samples.reduce((a, b) => a + b, 0)
  const nbar = mean(samples)
  const sigma = Math.sqrt((pbar * (1 - pbar)) / nbar)
  return ok(buildChart('P', proportions, pbar, sigma, Math.max(0, pbar - 3 * sigma), Math.min(1, pbar + 3 * sigma)))
}

export function npChart(defectives: readonly number[], sampleSize: number): Result<ControlChartResult> {
  if (defectives.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 muestras.')
  const pbar = defectives.reduce((a, b) => a + b, 0) / (sampleSize * defectives.length)
  const npbar = sampleSize * pbar
  const sigma = Math.sqrt(sampleSize * pbar * (1 - pbar))
  return ok(buildChart('NP', defectives, npbar, sigma, Math.max(0, npbar - 3 * sigma), npbar + 3 * sigma))
}

export function cChart(defects: readonly number[]): Result<ControlChartResult> {
  if (defects.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 muestras.')
  const cbar = mean(defects)
  const sigma = Math.sqrt(cbar)
  return ok(buildChart('C', defects, cbar, sigma, Math.max(0, cbar - 3 * sigma), cbar + 3 * sigma))
}

export function uChart(defects: readonly number[], units: readonly number[]): Result<ControlChartResult> {
  if (defects.length !== units.length || defects.length < 2) return err('INVALID_INPUT', 'Datos incompatibles.')
  const rates = defects.map((value, index) => value / (units[index] as number))
  const ubar = defects.reduce((a, b) => a + b, 0) / units.reduce((a, b) => a + b, 0)
  const nbar = mean(units)
  const sigma = Math.sqrt(ubar / nbar)
  return ok(buildChart('U', rates, ubar, sigma, Math.max(0, ubar - 3 * sigma), ubar + 3 * sigma))
}

export function ewmaChart(values: readonly number[], lambda = 0.2, sigmaKnown?: number): Result<ControlChartResult> {
  if (values.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 observaciones.')
  if (!(lambda > 0 && lambda <= 1)) return err('INVALID_PARAMETER', 'λ debe estar en (0, 1].')
  const center = mean(values)
  const sigma = sigmaKnown ?? sd(values)
  const smoothing = Array.from({ length: values.length }, () => 0)
  let previous = center
  for (let i = 0; i < values.length; i += 1) {
    previous = lambda * (values[i] as number) + (1 - lambda) * previous
    smoothing[i] = previous
  }
  const limitWidth = 3 * sigma * Math.sqrt((lambda / (2 - lambda)) * (1 - Math.pow(1 - lambda, 2)))
  return ok(buildChart('EWMA', smoothing, center, sigma, center - limitWidth, center + limitWidth))
}

export function cusumChart(values: readonly number[], target?: number, k = 0.5): Result<ControlChartResult> {
  if (values.length < 3) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 3 observaciones.')
  const center = target ?? mean(values)
  const sigma = sd(values)
  const positive: number[] = []
  const negative: number[] = []
  let cPlus = 0
  let cMinus = 0
  for (const value of values) {
    cPlus = Math.max(0, cPlus + (value - center) / sigma - k)
    cMinus = Math.min(0, cMinus + (value - center) / sigma + k)
    positive.push(cPlus)
    negative.push(cMinus)
  }
  const combined = positive.map((value, index) => value + Math.abs(negative[index] as number))
  return ok(buildChart('CUSUM', combined, 0, 1, 0, 5))
}

export interface CapabilityResult {
  readonly mean: number
  readonly sd: number
  readonly lowerSpec: number
  readonly upperSpec: number
  readonly cp: number
  readonly cpk: number
  readonly cpu: number
  readonly cpl: number
  readonly pp: number
  readonly ppk: number
  readonly ppmBelow: number
  readonly ppmAbove: number
  readonly ppmTotal: number
  readonly method: string
}

export function processCapability(
  values: readonly number[],
  lowerSpec: number | undefined,
  upperSpec: number | undefined,
  options: { method?: 'normal' | 'nonnormal'; confidence?: number } = {}
): Result<CapabilityResult> {
  if (values.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 observaciones.')
  const center = mean(values)
  const sigmaWithin = sd(values)
  const method = options.method ?? 'normal'
  let ppmBelow = 0
  let ppmAbove = 0
  if (method === 'nonnormal') {
    if (lowerSpec !== undefined) ppmBelow = 1e6 * (values.filter((value) => value < lowerSpec).length / values.length)
    if (upperSpec !== undefined) ppmAbove = 1e6 * (values.filter((value) => value > upperSpec).length / values.length)
  } else {
    if (lowerSpec !== undefined) ppmBelow = 1e6 * normalCdf((lowerSpec - center) / sigmaWithin)
    if (upperSpec !== undefined) ppmAbove = 1e6 * (1 - normalCdf((upperSpec - center) / sigmaWithin))
  }
  const cp =
    lowerSpec !== undefined && upperSpec !== undefined && sigmaWithin > 0
      ? (upperSpec - lowerSpec) / (6 * sigmaWithin)
      : Number.NaN
  const cpu = upperSpec !== undefined && sigmaWithin > 0 ? (upperSpec - center) / (3 * sigmaWithin) : Number.NaN
  const cpl = lowerSpec !== undefined && sigmaWithin > 0 ? (center - lowerSpec) / (3 * sigmaWithin) : Number.NaN
  const overallSd = sd(values)
  const pp =
    lowerSpec !== undefined && upperSpec !== undefined && overallSd > 0
      ? (upperSpec - lowerSpec) / (6 * overallSd)
      : Number.NaN
  const ppu = upperSpec !== undefined && overallSd > 0 ? (upperSpec - center) / (3 * overallSd) : Number.NaN
  const ppl = lowerSpec !== undefined && overallSd > 0 ? (center - lowerSpec) / (3 * overallSd) : Number.NaN
  return ok({
    mean: center,
    sd: sigmaWithin,
    lowerSpec: lowerSpec ?? Number.NaN,
    upperSpec: upperSpec ?? Number.NaN,
    cp,
    cpk: Math.min(cpu, cpl),
    cpu,
    cpl,
    pp,
    ppk: Math.min(ppu, ppl),
    ppmBelow,
    ppmAbove,
    ppmTotal: ppmBelow + ppmAbove,
    method: method === 'nonnormal' ? 'No normal (percentiles empíricos)' : 'Normal'
  })
}

/** Capacidad no normal por cuantiles empíricos (método de percentiles). */
export function nonnormalCapability(
  values: readonly number[],
  lowerSpec: number | undefined,
  upperSpec: number | undefined
): Result<CapabilityResult> {
  const result = processCapability(values, lowerSpec, upperSpec, { method: 'nonnormal' })
  if (!result.ok) return result
  const q50 = quantile(values, 0.5)
  const q999865 = quantile(values, 0.99865)
  const q00135 = quantile(values, 0.00135)
  const spread = q999865 - q00135
  const cp = lowerSpec !== undefined && upperSpec !== undefined && spread > 0 ? (upperSpec - lowerSpec) / (6 * (spread / 6)) : Number.NaN
  const cpu = upperSpec !== undefined && spread > 0 ? (upperSpec - q50) / (3 * (spread / 6)) : Number.NaN
  const cpl = lowerSpec !== undefined && spread > 0 ? (q50 - lowerSpec) / (3 * (spread / 6)) : Number.NaN
  return ok({ ...result.value, cp, cpu, cpl, cpk: Math.min(cpu, cpl) })
}

/** Histograma con regla de Sturges. */
export function histogram(values: readonly number[], bins?: number): { lower: number; upper: number; counts: number[]; edges: number[] } {
  const ordered = [...values].sort((a, b) => a - b)
  const min = ordered[0] ?? 0
  const max = ordered[ordered.length - 1] ?? 0
  const count = bins ?? Math.max(5, Math.ceil(Math.log2(Math.max(2, ordered.length)) + 1))
  const width = (max - min) / count || 1
  const edges = Array.from({ length: count + 1 }, (_, i) => min + i * width)
  const counts = new Array<number>(count).fill(0)
  for (const value of ordered) {
    const index = Math.min(count - 1, Math.floor((value - min) / width))
    counts[index] = (counts[index] as number) + 1
  }
  return { lower: min, upper: max, counts, edges }
}

export { normalInv }
