/** Fiabilidad y análisis de supervivencia. */
import { mean, sd } from './numeric'
import { err, ok, type Result } from '../model/result'

export interface WeibullFit {
  readonly shape: number
  readonly scale: number
  readonly logLikelihood: number
  readonly aic: number
  readonly n: number
}

function weibullLogLikelihood(data: readonly number[], shape: number, scale: number): number {
  if (!(shape > 0) || !(scale > 0)) return Number.NEGATIVE_INFINITY
  let total = 0
  for (const value of data) {
    if (value <= 0) return Number.NEGATIVE_INFINITY
    total += Math.log(shape) - Math.log(scale) + (shape - 1) * (Math.log(value) - Math.log(scale)) - Math.pow(value / scale, shape)
  }
  return total
}

/** Ajuste Weibull(2p) por máxima verosimilitud (búsqueda Newton numérica). */
export function weibullFit(data: readonly number[]): Result<WeibullFit> {
  const positive = data.filter((value) => value > 0)
  if (positive.length < 3) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 3 tiempos positivos.')
  const cv = sd(positive) / mean(positive)
  let shape = Math.max(0.2, Math.min(10, 1.2 / Math.max(cv, 0.05)))
  let scale = mean(positive) / Math.max(0.1, Math.exp(lgammaLocal(1 + 1 / shape)))
  let best = weibullLogLikelihood(positive, shape, scale)
  for (let iteration = 0; iteration < 200; iteration += 1) {
    const hShape = shape * 1e-4
    const hScale = scale * 1e-4
    const dShape = (weibullLogLikelihood(positive, shape + hShape, scale) - weibullLogLikelihood(positive, shape - hShape, scale)) / (2 * hShape)
    const dScale = (weibullLogLikelihood(positive, shape, scale + hScale) - weibullLogLikelihood(positive, shape, scale - hScale)) / (2 * hScale)
    const d2Shape = (weibullLogLikelihood(positive, shape + hShape, scale) - 2 * best + weibullLogLikelihood(positive, shape - hShape, scale)) / (hShape * hShape)
    const d2Scale = (weibullLogLikelihood(positive, shape, scale + hScale) - 2 * best + weibullLogLikelihood(positive, shape, scale - hScale)) / (hScale * hScale)
    const dMixed = (weibullLogLikelihood(positive, shape + hShape, scale + hScale) - weibullLogLikelihood(positive, shape + hShape, scale - hScale) - weibullLogLikelihood(positive, shape - hShape, scale + hScale) + weibullLogLikelihood(positive, shape - hShape, scale - hScale)) / (4 * hShape * hScale)
    const det = d2Shape * d2Scale - dMixed * dMixed
    if (!(Math.abs(det) > 1e-12)) break
    const stepShape = (d2Scale * dShape - dMixed * dScale) / det
    const stepScale = (-dMixed * dShape + d2Shape * dScale) / det
    const nextShape = Math.max(1e-3, shape - Math.max(-0.5, Math.min(0.5, stepShape)))
    const nextScale = Math.max(1e-3, scale - Math.max(-0.5, Math.min(0.5, stepScale)))
    const next = weibullLogLikelihood(positive, nextShape, nextScale)
    if (!(next > best)) break
    shape = nextShape
    scale = nextScale
    const improvement = next - best
    best = next
    if (improvement < 1e-10) break
  }
  return ok({ shape, scale, logLikelihood: best, aic: -2 * best + 4, n: positive.length })
}

export interface LogNormalFit {
  readonly location: number
  readonly scale: number
  readonly logLikelihood: number
  readonly n: number
}

export function lognormalFit(data: readonly number[]): Result<LogNormalFit> {
  const positive = data.filter((value) => value > 0)
  if (positive.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 tiempos positivos.')
  const logs = positive.map(Math.log)
  const location = mean(logs)
  const scale = sd(logs)
  if (!(scale > 0)) return err('INVALID_INPUT', 'Los datos no tienen variabilidad.')
  const logLikelihood = positive.reduce((acc, value) => acc - Math.log(value) - Math.log(scale) - 0.5 * Math.log(2 * Math.PI) - ((Math.log(value) - location) ** 2) / (2 * scale * scale), 0)
  return ok({ location, scale, logLikelihood, n: positive.length })
}

/** Kaplan-Meier con error estándar de Greenwood. */
export interface KaplanMeierResult {
  readonly times: number[]
  readonly survival: number[]
  readonly standardError: number[]
  readonly atRisk: number[]
  readonly events: number[]
  readonly median: number
}

export function kaplanMeier(times: readonly number[], event: readonly boolean[]): Result<KaplanMeierResult> {
  const n = Math.min(times.length, event.length)
  if (n < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 observaciones.')
  const rows = Array.from({ length: n }, (_, i) => ({ time: times[i] as number, event: Boolean(event[i]) })).sort((a, b) => a.time - b.time)
  const uniqueTimes = [...new Set(rows.map((row) => row.time))]
  const outputTimes: number[] = [0]
  const outputSurvival: number[] = [1]
  const outputSe: number[] = [0]
  const outputRisk: number[] = [n]
  const outputEvents: number[] = [0]
  let survival = 1
  let varianceSum = 0
  let median = Number.POSITIVE_INFINITY
  for (const time of uniqueTimes) {
    const atRisk = rows.filter((row) => row.time >= time).length
    const events = rows.filter((row) => row.time === time && row.event).length
    if (events > 0) {
      survival *= (atRisk - events) / atRisk
      varianceSum += events / (atRisk * (atRisk - events || 1))
      outputTimes.push(time)
      outputSurvival.push(survival)
      outputSe.push(survival * Math.sqrt(varianceSum))
      outputRisk.push(atRisk)
      outputEvents.push(events)
      if (median === Number.POSITIVE_INFINITY && survival <= 0.5) median = time
    }
  }
  return ok({ times: outputTimes, survival: outputSurvival, standardError: outputSe, atRisk: outputRisk, events: outputEvents, median })
}

/** Puntos para un gráfico de probabilidad Weibull. */
export function probabilityPlotPoints(data: readonly number[]): { x: number; y: number; rank: number }[] {
  const positive = data.filter((value) => value > 0).sort((a, b) => a - b)
  const n = positive.length
  return positive.map((value, index) => {
    const rank = (index + 1 - 0.3) / (n + 0.4)
    return { x: Math.log(value), y: Math.log(-Math.log(1 - rank)), rank }
  })
}

/** Estimación de percentiles de una distribución Weibull ajustada. */
export function weibullQuantile(probability: number, shape: number, scale: number): number {
  return scale * Math.pow(-Math.log(1 - probability), 1 / shape)
}

export function reliabilityAt(time: number, shape: number, scale: number): number {
  return Math.exp(-Math.pow(time / scale, shape))
}

function lgammaLocal(x: number): number {
  const coef = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7]
  if (x < 0.5) return Math.log(Math.PI / Math.sin(Math.PI * x)) - lgammaLocal(1 - x)
  const z = x - 1
  let a = coef[0] as number
  const t = z + 7.5
  for (let i = 1; i < coef.length; i += 1) a += (coef[i] as number) / (z + i)
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(a)
}
