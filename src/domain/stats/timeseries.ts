/** Series temporales: ACF/PACF, suavizados, descomposición, AR y Ljung-Box. */
import { mean, sd } from './numeric'
import { err, ok, type Result } from '../model/result'

export interface AcfResult {
  readonly lags: number[]
  readonly values: number[]
  readonly confidence: number
}

export function autocorrelation(values: readonly number[], maxLag = 20): Result<AcfResult> {
  const n = values.length
  if (n < 4) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 4 observaciones.')
  const center = mean(values)
  let denominator = 0
  for (const value of values) denominator += (value - center) ** 2
  if (denominator === 0) return err('INVALID_INPUT', 'La serie es constante.')
  const lags: number[] = []
  const acf: number[] = []
  for (let lag = 0; lag <= Math.min(maxLag, n - 2); lag += 1) {
    let numerator = 0
    for (let i = lag; i < n; i += 1) numerator += ((values[i] as number) - center) * ((values[i - lag] as number) - center)
    lags.push(lag)
    acf.push(numerator / denominator)
  }
  return ok({ lags, values: acf, confidence: 1.96 / Math.sqrt(n) })
}

/** PACF por recursión de Durbin-Levinson a partir de la ACF. */
export function partialAutocorrelation(values: readonly number[], maxLag = 20): Result<AcfResult> {
  const acfResult = autocorrelation(values, maxLag)
  if (!acfResult.ok) return acfResult
  const rho = acfResult.value.values
  const lags = acfResult.value.lags
  const pacf: number[] = [1]
  const phi: number[][] = []
  for (let lag = 1; lag < rho.length; lag += 1) {
    const previous = phi[lag - 1] ?? []
    let numerator = rho[lag] as number
    for (let j = 1; j < lag; j += 1) numerator -= (previous[j - 1] as number) * (rho[lag - j] as number)
    let denominator = 1
    for (let j = 1; j < lag; j += 1) denominator -= (previous[j - 1] as number) * (rho[j] as number)
    const current = numerator / denominator
    const row = new Array<number>(lag).fill(0)
    for (let j = 0; j < lag - 1; j += 1) row[j] = (previous[j] as number) - current * (previous[lag - 2 - j] as number)
    row[lag - 1] = current
    phi.push(row)
    pacf.push(current)
  }
  return ok({ lags, values: pacf, confidence: acfResult.value.confidence })
}

export function movingAverage(values: readonly number[], window: number): Result<{ fitted: number[]; residuals: number[] }> {
  if (window < 2 || window > values.length) return err('INVALID_PARAMETER', 'Ventana inválida.')
  const fitted: number[] = []
  for (let i = 0; i < values.length; i += 1) {
    if (i < window - 1) {
      fitted.push(Number.NaN)
      continue
    }
    let total = 0
    for (let j = i - window + 1; j <= i; j += 1) total += values[j] as number
    fitted.push(total / window)
  }
  return ok({ fitted, residuals: values.map((value, index) => value - (fitted[index] as number)) })
}

export function simpleExponentialSmoothing(values: readonly number[], alpha = 0.2): Result<{ fitted: number[]; forecast: number }> {
  if (!(alpha > 0 && alpha < 1)) return err('INVALID_PARAMETER', 'α debe estar entre 0 y 1.')
  const fitted: number[] = []
  let level = values[0] as number
  fitted.push(level)
  for (let i = 1; i < values.length; i += 1) {
    level = alpha * (values[i] as number) + (1 - alpha) * level
    fitted.push(level)
  }
  return ok({ fitted, forecast: level })
}

export function doubleExponentialSmoothing(
  values: readonly number[],
  alpha = 0.2,
  beta = 0.1
): Result<{ fitted: number[]; forecast: number }> {
  if (values.length < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 2 observaciones.')
  if (!(alpha > 0 && alpha < 1) || !(beta > 0 && beta < 1)) return err('INVALID_PARAMETER', 'α y β deben estar entre 0 y 1.')
  let level = values[0] as number
  let trend = (values[1] as number) - (values[0] as number)
  const fitted: number[] = [level]
  for (let i = 1; i < values.length; i += 1) {
    const previousLevel = level
    level = alpha * (values[i] as number) + (1 - alpha) * (level + trend)
    trend = beta * (level - previousLevel) + (1 - beta) * trend
    fitted.push(level)
  }
  return ok({ fitted, forecast: level + trend })
}

export function wintersSmoothing(
  values: readonly number[],
  period: number,
  alpha = 0.2,
  beta = 0.1,
  gamma = 0.1
): Result<{ fitted: number[]; forecast: number[]; seasonal: number[] }> {
  if (values.length < 2 * period) return err('INSUFFICIENT_DATA', 'La serie debe cubrir al menos 2 ciclos.')
  const seasonal: number[] = []
  for (let i = 0; i < period; i += 1) {
    let total = 0
    let count = 0
    for (let j = i; j < values.length; j += period) {
      total += values[j] as number
      count += 1
    }
    seasonal.push(total / count - mean(values))
  }
  let level = mean(values)
  let trend = ((values[period] as number) - (values[0] as number)) / period
  const fitted: number[] = []
  for (let i = 0; i < values.length; i += 1) {
    const seasonIndex = i % period
    const forecast = level + trend + (seasonal[seasonIndex] as number)
    fitted.push(forecast)
    const previousLevel = level
    level = alpha * ((values[i] as number) - (seasonal[seasonIndex] as number)) + (1 - alpha) * (level + trend)
    trend = beta * (level - previousLevel) + (1 - beta) * trend
    seasonal[seasonIndex] = gamma * ((values[i] as number) - level) + (1 - gamma) * (seasonal[seasonIndex] as number)
  }
  const forecast = Array.from({ length: period }, (_, h) => level + (h + 1) * trend + (seasonal[h] as number))
  return ok({ fitted, forecast, seasonal })
}

export function difference(values: readonly number[], lag = 1): number[] {
  return values.slice(lag).map((value, index) => value - (values[index] as number))
}

export interface DecompositionResult {
  readonly trend: number[]
  readonly seasonal: number[]
  readonly remainder: number[]
  readonly period: number
}

export function decompose(values: readonly number[], period: number): Result<DecompositionResult> {
  if (period < 2 || values.length < 2 * period) return err('INSUFFICIENT_DATA', 'Serie demasiado corta para la estacionalidad indicada.')
  const centers = period % 2 === 0 ? period : period - 1
  const trend: number[] = new Array<number>(values.length).fill(Number.NaN)
  for (let i = 0; i < values.length; i += 1) {
    const start = i - Math.floor(centers / 2)
    const end = start + centers
    if (start >= 0 && end <= values.length) {
      let total = 0
      for (let j = start; j < end; j += 1) total += values[j] as number
      trend[i] = total / centers
    }
  }
  const seasonal: number[] = new Array<number>(values.length).fill(0)
  for (let i = 0; i < values.length; i += 1) {
    if (Number.isFinite(trend[i] as number)) seasonal[i] = (values[i] as number) - (trend[i] as number)
  }
  const periodMeans: number[] = []
  for (let i = 0; i < period; i += 1) {
    let total = 0
    let count = 0
    for (let j = i; j < values.length; j += period) {
      if (Number.isFinite(trend[j] as number)) {
        total += (values[j] as number) - (trend[j] as number)
        count += 1
      }
    }
    periodMeans.push(count > 0 ? total / count : 0)
  }
  const seasonalPattern = periodMeans.map((value) => value - mean(periodMeans))
  for (let i = 0; i < values.length; i += 1) seasonal[i] = seasonalPattern[i % period] as number
  const remainder = values.map((value, index) => value - (seasonal[index] as number) - ((trend[index] as number) || mean(values)))
  return ok({ trend, seasonal, remainder, period })
}

export interface ArFitResult {
  readonly order: number
  readonly intercept: number
  readonly coefficients: number[]
  readonly residualVariance: number
  readonly aic: number
  readonly forecast: number[]
  readonly standardErrors: number[]
  readonly ljungBox: { statistic: number; df: number; pValue: number }
}

/** Ajuste AR(p) por Yule-Walker con selección por AIC y pronóstico recursivo. */
export function fitAr(values: readonly number[], maxOrder = 5, horizon = 5): Result<ArFitResult> {
  const n = values.length
  if (n < 12) return err('INSUFFICIENT_DATA', 'Se recomiendan al menos 12 observaciones.')
  const center = mean(values)
  const centered = values.map((value) => value - center)
  const acf = autocorrelation(values, Math.min(maxOrder + 1, Math.floor(n / 3)))
  if (!acf.ok) return acf
  const rho = acf.value.values
  let best: { order: number; coefficients: number[]; variance: number; aic: number } | null = null
  for (let order = 1; order <= Math.min(maxOrder, rho.length - 2); order += 1) {
    const matrix: number[][] = []
    const rhs: number[] = []
    for (let i = 0; i < order; i += 1) {
      matrix.push([])
      for (let j = 0; j < order; j += 1) (matrix[i] as number[]).push(rho[Math.abs(i - j)] as number)
      rhs.push(rho[i + 1] as number)
    }
    const solved = solveSmall(matrix, rhs)
    if (!solved) continue
    let variance = rho[0] as number
    for (let i = 0; i < order; i += 1) variance -= (solved[i] as number) * (rho[i + 1] as number)
    variance *= (n / (n - order)) * (centered.reduce((acc, value) => acc + value * value, 0) / n)
    const aic = n * Math.log(Math.max(variance, 1e-300)) + 2 * (order + 2)
    if (!best || aic < best.aic) best = { order, coefficients: solved, variance, aic }
  }
  if (!best) return err('NON_CONVERGENCE', 'No se pudo ajustar un modelo AR.')
  const history = [...centered]
  const forecast: number[] = []
  const standardErrors: number[] = []
  for (let h = 1; h <= horizon; h += 1) {
    let value = 0
    for (let i = 0; i < best.order; i += 1) {
      const index = history.length - 1 - i
      value += (best.coefficients[i] as number) * ((history[index] as number) ?? 0)
    }
    history.push(value)
    forecast.push(value + center)
    standardErrors.push(Math.sqrt(best.variance * (1 + h * 0.1)))
  }
  const residuals: number[] = []
  for (let i = best.order; i < centered.length; i += 1) {
    let predicted = 0
    for (let j = 0; j < best.order; j += 1) predicted += (best.coefficients[j] as number) * (centered[i - j - 1] as number)
    residuals.push((centered[i] as number) - predicted)
  }
  return ok({
    order: best.order,
    intercept: center,
    coefficients: best.coefficients,
    residualVariance: best.variance,
    aic: best.aic,
    forecast,
    standardErrors,
    ljungBox: ljungBox(residuals, Math.min(10, residuals.length - 1))
  })
}

export function ljungBox(residuals: readonly number[], lags = 10): { statistic: number; df: number; pValue: number } {
  const n = residuals.length
  const center = mean(residuals)
  let denominator = 0
  for (const value of residuals) denominator += (value - center) ** 2
  let statistic = 0
  const maxLag = Math.min(lags, n - 1)
  for (let k = 1; k <= maxLag; k += 1) {
    let numerator = 0
    for (let i = k; i < n; i += 1) numerator += ((residuals[i] as number) - center) * ((residuals[i - k] as number) - center)
    const rk = numerator / denominator
    statistic += (rk * rk) / (n - k)
  }
  statistic *= n * (n + 2)
  return { statistic, df: maxLag, pValue: chiSquareUpper(statistic, maxLag) }
}

function chiSquareUpper(statistic: number, df: number): number {
  return 1 - lowerGamma(df / 2, statistic / 2)
}

function lowerGamma(a: number, x: number): number {
  if (x <= 0) return 0
  if (x < a + 1) {
    let ap = a
    let deliver = 1 / a
    let total = deliver
    for (let n = 0; n < 1000; n += 1) {
      ap += 1
      deliver *= x / ap
      total += deliver
      if (Math.abs(deliver) < Math.abs(total) * 1e-16) break
    }
    return total * Math.exp(-x + a * Math.log(x) - lgamma(a))
  }
  return 1 - upperGamma(a, x)
}

function upperGamma(a: number, x: number): number {
  let b = x + 1 - a
  let c = 1e300
  let d = 1 / b
  let h = d
  for (let i = 1; i < 1000; i += 1) {
    const an = -i * (i - a)
    b += 2
    d = an * d + b
    if (Math.abs(d) < 1e-300) d = 1e-300
    c = b + an / c
    if (Math.abs(c) < 1e-300) c = 1e-300
    d = 1 / d
    const del = d * c
    h *= del
    if (Math.abs(del - 1) < 1e-16) break
  }
  return Math.exp(-x + a * Math.log(x) - lgamma(a)) * h
}

function lgamma(x: number): number {
  const coef = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7]
  if (x < 0.5) return Math.log(Math.PI / Math.sin(Math.PI * x)) - lgamma(1 - x)
  const z = x - 1
  let a = coef[0] as number
  const t = z + 7.5
  for (let i = 1; i < coef.length; i += 1) a += (coef[i] as number) / (z + i)
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(a)
}

function solveSmall(matrix: number[][], vector: number[]): number[] | null {
  const n = matrix.length
  const a = matrix.map((row) => [...row])
  const b = [...vector]
  for (let col = 0; col < n; col += 1) {
    let pivot = col
    for (let row = col + 1; row < n; row += 1) {
      if (Math.abs((a[row]?.[col] as number) ?? 0) > Math.abs((a[pivot]?.[col] as number) ?? 0)) pivot = row
    }
    if (Math.abs((a[pivot]?.[col] as number) ?? 0) < 1e-12) return null
    if (pivot !== col) {
      const tmp = a[pivot] as number[]
      a[pivot] = a[col] as number[]
      a[col] = tmp
      const tmpB = b[pivot] as number
      b[pivot] = b[col] as number
      b[col] = tmpB
    }
    for (let row = col + 1; row < n; row += 1) {
      const factor = (a[row]?.[col] as number) / (a[col]?.[col] as number)
      for (let j = col; j < n; j += 1) (a[row] as number[])[j] = (a[row] as number[])[j]! - factor * (a[col] as number[])[j]!
      b[row] = (b[row] as number) - factor * (b[col] as number)
    }
  }
  const solution = new Array<number>(n).fill(0)
  for (let row = n - 1; row >= 0; row -= 1) {
    let acc = b[row] as number
    for (let j = row + 1; j < n; j += 1) acc -= (a[row]?.[j] as number) * (solution[j] as number)
    solution[row] = acc / (a[row]?.[row] as number)
  }
  return solution
}

export { sd }
