/** Potencia estadística y tamaño de muestra (aproximaciones normales documentadas). */
import { normalCdf, normalInv } from './numeric'
import { fCdfUpper } from './anova'
import { err, ok, type Result } from '../model/result'

export interface PowerResult {
  readonly power: number
  readonly alpha: number
  readonly n: number
  readonly effect: number
  readonly method: string
}

function twoSidedPower(noncentrality: number, alpha: number): number {
  const z = normalInv(1 - alpha / 2)
  return normalCdf(noncentrality - z) + normalCdf(-noncentrality - z)
}

export function powerOneSampleT(effect: number, n: number, alpha = 0.05): Result<PowerResult> {
  if (!(n >= 2) || effect === 0 || !(alpha > 0 && alpha < 1)) return err('INVALID_PARAMETER', 'Parámetros de potencia inválidos.')
  const noncentrality = Math.abs(effect) * Math.sqrt(n)
  return ok({ power: twoSidedPower(noncentrality, alpha), alpha, n, effect, method: 't de 1 muestra (aprox. normal)' })
}

export function sampleSizeOneSampleT(effect: number, power = 0.8, alpha = 0.05): Result<PowerResult> {
  if (effect === 0 || !(alpha > 0 && alpha < 1) || !(power > 0 && power < 1)) return err('INVALID_PARAMETER', 'Parámetros inválidos.')
  let n = 2
  for (; n < 1_000_000; n += 1) {
    const result = powerOneSampleT(effect, n, alpha)
    if (result.ok && result.value.power >= power) break
  }
  return ok({ power, alpha, n, effect, method: 't de 1 muestra (aprox. normal)' })
}

export function powerTwoSampleT(effect: number, nPerGroup: number, alpha = 0.05): Result<PowerResult> {
  if (!(nPerGroup >= 2) || effect === 0) return err('INVALID_PARAMETER', 'Parámetros inválidos.')
  const noncentrality = (Math.abs(effect) * Math.sqrt(nPerGroup)) / Math.SQRT2
  return ok({ power: twoSidedPower(noncentrality, alpha), alpha, n: nPerGroup, effect, method: 't de 2 muestras (aprox. normal)' })
}

export function sampleSizeTwoSampleT(effect: number, power = 0.8, alpha = 0.05): Result<PowerResult> {
  if (effect === 0) return err('INVALID_PARAMETER', 'El efecto debe ser distinto de cero.')
  let n = 2
  for (; n < 1_000_000; n += 1) {
    const result = powerTwoSampleT(effect, n, alpha)
    if (result.ok && result.value.power >= power) break
  }
  return ok({ power, alpha, n, effect, method: 't de 2 muestras (aprox. normal)' })
}

export function powerOneProportion(p0: number, p1: number, n: number, alpha = 0.05): Result<PowerResult> {
  if (!(p0 > 0 && p0 < 1) || p1 < 0 || p1 > 1 || !(n >= 1)) return err('INVALID_PARAMETER', 'Parámetros inválidos.')
  const zAlpha = normalInv(1 - alpha / 2)
  const seNull = Math.sqrt((p0 * (1 - p0)) / n)
  const seAlt = Math.sqrt((p1 * (1 - p1)) / n) || 1e-12
  const difference = Math.abs(p1 - p0)
  const power = normalCdf((difference - zAlpha * seNull) / seAlt)
  return ok({ power, alpha, n, effect: p1 - p0, method: 'proporción de 1 muestra (aprox. normal)' })
}

export function sampleSizeOneProportion(p0: number, p1: number, power = 0.8, alpha = 0.05): Result<PowerResult> {
  if (!(p0 > 0 && p0 < 1) || !(p1 > 0 && p1 < 1) || p0 === p1) return err('INVALID_PARAMETER', 'Proporciones inválidas.')
  let n = 1
  for (; n < 10_000_000; n += 1) {
    const result = powerOneProportion(p0, p1, n, alpha)
    if (result.ok && result.value.power >= power) break
  }
  return ok({ power, alpha, n, effect: p1 - p0, method: 'proporción de 1 muestra (aprox. normal)' })
}

export function powerOneWayAnova(effectSize: number, nPerGroup: number, groups: number, alpha = 0.05): Result<PowerResult> {
  if (!(effectSize > 0) || !(nPerGroup >= 2) || !(groups >= 2)) return err('INVALID_PARAMETER', 'Parámetros inválidos.')
  const df1 = groups - 1
  const df2 = groups * (nPerGroup - 1)
  const critical = fCritical(df1, df2, alpha)
  const latent = Math.max(0, (effectSize * effectSize * nPerGroup * groups) / (1 + effectSize * effectSize))
  const power = 1 - normalCdf((critical - latent) / Math.sqrt(2 * Math.max(1, latent)))
  return ok({ power: Math.min(1, Math.max(0, power)), alpha, n: nPerGroup, effect: effectSize, method: 'ANOVA de 1 factor (aprox. normal)' })
}

function fCritical(df1: number, df2: number, alpha: number): number {
  let low = 0
  let high = 100
  for (let i = 0; i < 100; i += 1) {
    const mid = (low + high) / 2
    if (1 - fCdfUpper(mid, df1, df2) >= 1 - alpha) high = mid
    else low = mid
  }
  return high
}

export function ocCurve(effect: number, sizes: readonly number[], alpha = 0.05): { n: number; power: number }[] {
  return sizes.map((n) => {
    const result = powerOneSampleT(effect, n, alpha)
    return { n, power: result.ok ? result.value.power : Number.NaN }
  })
}
