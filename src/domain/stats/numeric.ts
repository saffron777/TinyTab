/**
 * Utilidades numéricas del dominio. Implementaciones propias y deterministas,
 * sin dependencias externas: series, fracciones continuas y búsqueda de raíces.
 */

export const EPS = 2.220446049250313e-16
export const FPMIN = 1e-300
const SQRT2 = Math.SQRT2
const SQRT2PI = Math.sqrt(2 * Math.PI)

export function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

export function sum(values: readonly number[]): number {
  let total = 0
  for (const value of values) total += value
  return total
}

export function mean(values: readonly number[]): number {
  return values.length === 0 ? Number.NaN : sum(values) / values.length
}

export function variance(values: readonly number[], ddof = 1): number {
  if (values.length - ddof <= 0) return Number.NaN
  const center = mean(values)
  let acc = 0
  for (const value of values) {
    const delta = value - center
    acc += delta * delta
  }
  return acc / (values.length - ddof)
}

export function sd(values: readonly number[], ddof = 1): number {
  return Math.sqrt(variance(values, ddof))
}

export function seMean(values: readonly number[]): number {
  return sd(values) / Math.sqrt(values.length)
}

export function sorted(values: readonly number[]): number[] {
  return [...values].sort((a, b) => a - b)
}

export function minOf(values: readonly number[]): number {
  return Math.min(...values)
}

export function maxOf(values: readonly number[]): number {
  return Math.max(...values)
}

/** Cuantil tipo 7 (R, Excel, Minitab). */
export function quantile(values: readonly number[], probability: number): number {
  if (values.length === 0) return Number.NaN
  const p = clamp(probability, 0, 1)
  const ordered = sorted(values)
  if (ordered.length === 1) return ordered[0] as number
  const h = (ordered.length - 1) * p
  const lower = Math.floor(h)
  const upper = Math.ceil(h)
  const a = ordered[lower] as number
  const b = ordered[upper] as number
  return a + (h - lower) * (b - a)
}

export function median(values: readonly number[]): number {
  return quantile(values, 0.5)
}

export function iqr(values: readonly number[]): number {
  return quantile(values, 0.75) - quantile(values, 0.25)
}

export function skewness(values: readonly number[]): number {
  const n = values.length
  if (n < 3) return Number.NaN
  const m = mean(values)
  const s = sd(values)
  if (s === 0) return 0
  let acc = 0
  for (const value of values) acc += Math.pow((value - m) / s, 3)
  return (n / ((n - 1) * (n - 2))) * acc
}

export function kurtosis(values: readonly number[]): number {
  const n = values.length
  if (n < 4) return Number.NaN
  const m = mean(values)
  const s = sd(values)
  if (s === 0) return 0
  let acc = 0
  for (const value of values) acc += Math.pow((value - m) / s, 4)
  const term1 = (n * (n + 1)) / ((n - 1) * (n - 2) * (n - 3))
  const term2 = (3 * (n - 1) * (n - 1)) / ((n - 2) * (n - 3))
  return term1 * acc - term2
}

export function covariance(x: readonly number[], y: readonly number[]): number {
  const n = Math.min(x.length, y.length)
  if (n < 2) return Number.NaN
  const mx = mean(x.slice(0, n))
  const my = mean(y.slice(0, n))
  let acc = 0
  for (let i = 0; i < n; i += 1) acc += ((x[i] as number) - mx) * ((y[i] as number) - my)
  return acc / (n - 1)
}

export function correlation(x: readonly number[], y: readonly number[]): number {
  const n = Math.min(x.length, y.length)
  if (n < 2) return Number.NaN
  const cov = covariance(x, y)
  const sx = sd(x.slice(0, n))
  const sy = sd(y.slice(0, n))
  if (sx === 0 || sy === 0) return Number.NaN
  return cov / (sx * sy)
}

/** Rangos con corrección de empates (promedio). */
export function ranks(values: readonly number[]): number[] {
  const indexed = values.map((value, index) => ({ value, index }))
  indexed.sort((a, b) => a.value - b.value)
  const output = new Array<number>(values.length).fill(0)
  let i = 0
  while (i < indexed.length) {
    let j = i
    while (j + 1 < indexed.length && indexed[j + 1]?.value === indexed[i]?.value) j += 1
    const average = (i + j) / 2 + 1
    for (let k = i; k <= j; k += 1) {
      const entry = indexed[k]
      if (entry) output[entry.index] = average
    }
    i = j + 1
  }
  return output
}

export function clamp(value: number, lower: number, upper: number): number {
  return Math.min(Math.max(value, lower), upper)
}

export function logFactorial(n: number): number {
  if (n < 0) return Number.NaN
  return logGamma(n + 1)
}

export function logCombination(n: number, k: number): number {
  if (k < 0 || k > n) return Number.NEGATIVE_INFINITY
  return logGamma(n + 1) - logGamma(k + 1) - logGamma(n - k + 1)
}

/** lgamma por aproximación de Lanczos (g=7, n=9), error < 1e-14. */
export function logGamma(x: number): number {
  const coefficients = [
    0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
    -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
    1.5056327351493116e-7
  ]
  if (x < 0.5) {
    return Math.log(Math.PI / Math.sin(Math.PI * x)) - logGamma(1 - x)
  }
  const z = x - 1
  let a = coefficients[0] as number
  const t = z + 7.5
  for (let i = 1; i < coefficients.length; i += 1) {
    a += (coefficients[i] as number) / (z + i)
  }
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(a)
}

export function gammaFn(x: number): number {
  return Math.exp(logGamma(x))
}

export function betaFn(a: number, b: number): number {
  return Math.exp(logGamma(a) + logGamma(b) - logGamma(a + b))
}

/** Serie de potencias para erf (|x| <= 0.5). */
function erfSeries(x: number): number {
  let term = x
  let total = x
  const x2 = x * x
  for (let n = 1; n < 60; n += 1) {
    term *= (-x2) / n
    const contribution = term / (2 * n + 1)
    total += contribution
    if (Math.abs(contribution) < Math.abs(total) * 1e-17) break
  }
  return (2 / Math.sqrt(Math.PI)) * total
}

/** erfc(x) para x >= 0 mediante la gamma incompleta Q(1/2, x^2). */
function erfcPositive(x: number): number {
  if (x === 0) return 1
  return gammq(0.5, x * x)
}

export function erf(x: number): number {
  if (x < 0) return -erf(-x)
  if (x <= 0.5) return erfSeries(x)
  return 1 - erfcPositive(x)
}

export function erfc(x: number): number {
  if (x < 0) return 2 - erfc(-x)
  if (x <= 0.5) return 1 - erfSeries(x)
  return erfcPositive(x)
}

export function normalPdf(z: number): number {
  return Math.exp(-0.5 * z * z) / SQRT2PI
}

export function normalCdf(z: number): number {
  return 0.5 * erfc(-z / SQRT2)
}

/** Inversa de la normal estándar: aproximación de Acklam-like + Newton. */
export function normalInv(p: number): number {
  if (!(p > 0 && p < 1)) return Number.NaN
  if (p === 0.5) return 0
  const lowerTail = p < 0.5
  const q = lowerTail ? p : 1 - p
  const t = Math.sqrt(-2 * Math.log(q))
  const coefficients = [2.515517, 0.802853, 0.010328]
  const denominators = [1.432788, 0.189269, 0.001308]
  const c0 = coefficients[0] as number
  const c1 = coefficients[1] as number
  const c2 = coefficients[2] as number
  const d1 = denominators[0] as number
  const d2 = denominators[1] as number
  const d3 = denominators[2] as number
  let z = t - (c0 + c1 * t + c2 * t * t) / (1 + d1 * t + d2 * t * t + d3 * t * t * t)
  if (lowerTail) z = -z
  for (let i = 0; i < 6; i += 1) {
    const cdf = normalCdf(z)
    if (cdf === 0 || cdf === 1) break
    const step = (cdf - p) / normalPdf(z)
    z -= step
    if (Math.abs(step) < 1e-14 * Math.max(1, Math.abs(z))) break
  }
  return z
}

/** Serie para la gamma incompleta P(a,x) cuando x < a+1. */
function gser(a: number, x: number): number {
  let ap = a
  let deliver = 1 / a
  let total = deliver
  for (let n = 0; n < 2000; n += 1) {
    ap += 1
    deliver *= x / ap
    total += deliver
    if (Math.abs(deliver) < Math.abs(total) * 1e-16) break
  }
  return total * Math.exp(-x + a * Math.log(x) - logGamma(a))
}

/** Fracción continua para Q(a,x) cuando x >= a+1 (Lentz modificado). */
function gcf(a: number, x: number): number {
  let b = x + 1 - a
  let c = 1 / FPMIN
  let d = 1 / b
  let h = d
  for (let i = 1; i < 2000; i += 1) {
    const an = -i * (i - a)
    b += 2
    d = an * d + b
    if (Math.abs(d) < FPMIN) d = FPMIN
    c = b + an / c
    if (Math.abs(c) < FPMIN) c = FPMIN
    d = 1 / d
    const deliver = d * c
    h *= deliver
    if (Math.abs(deliver - 1) < 1e-16) break
  }
  return Math.exp(-x + a * Math.log(x) - logGamma(a)) * h
}

/** P(a,x): gamma incompleta regularizada inferior. */
export function gammp(a: number, x: number): number {
  if (!(a > 0) || x < 0) return Number.NaN
  if (x === 0) return 0
  if (x < a + 1) return gser(a, x)
  return 1 - gcf(a, x)
}

/** Q(a,x): gamma incompleta regularizada superior. */
export function gammq(a: number, x: number): number {
  if (!(a > 0) || x < 0) return Number.NaN
  if (x === 0) return 1
  if (x < a + 1) return 1 - gser(a, x)
  return gcf(a, x)
}

/** Fracción continua para la beta incompleta (Lentz). */
function betacf(a: number, b: number, x: number): number {
  const qab = a + b
  const qap = a + 1
  const qam = a - 1
  let c = 1
  let d = 1 - (qab * x) / qap
  if (Math.abs(d) < FPMIN) d = FPMIN
  d = 1 / d
  let h = d
  for (let m = 1; m <= 3000; m += 1) {
    const m2 = 2 * m
    let aa = (m * (b - m) * x) / ((qam + m2) * (a + m2))
    d = 1 + aa * d
    if (Math.abs(d) < FPMIN) d = FPMIN
    c = 1 + aa / c
    if (Math.abs(c) < FPMIN) c = FPMIN
    d = 1 / d
    h *= d * c
    aa = (-(a + m) * (qab + m) * x) / ((a + m2) * (qap + m2))
    d = 1 + aa * d
    if (Math.abs(d) < FPMIN) d = FPMIN
    c = 1 + aa / c
    if (Math.abs(c) < FPMIN) c = FPMIN
    d = 1 / d
    const deliver = d * c
    h *= deliver
    if (Math.abs(deliver - 1) < 1e-16) break
  }
  return h
}

/** I_x(a,b): beta incompleta regularizada. */
export function betai(a: number, b: number, x: number): number {
  if (!(a > 0) || !(b > 0)) return Number.NaN
  if (x <= 0) return 0
  if (x >= 1) return 1
  const logBeta = logGamma(a + b) - logGamma(a) - logGamma(b) + a * Math.log(x) + b * Math.log(1 - x)
  const front = Math.exp(logBeta)
  if (x < (a + 1) / (a + b + 2)) return (front * betacf(a, b, x)) / a
  return 1 - (front * betacf(b, a, 1 - x)) / b
}

/** Inversión numérica monótona por bisección con tolerancia relativa. */
export function bisectInverse(
  cdf: (x: number) => number,
  probability: number,
  lower: number,
  upper: number,
  tolerance = 1e-12
): number {
  let lo = lower
  let hi = upper
  for (let i = 0; i < 400; i += 1) {
    const mid = lo + (hi - lo) / 2
    if (mid === lo || mid === hi) break
    const value = cdf(mid)
    if (Math.abs(value - probability) <= tolerance * Math.max(1, Math.abs(probability))) return mid
    if (value < probability) lo = mid
    else hi = mid
    if (Math.abs(hi - lo) <= tolerance * Math.max(1, Math.abs(mid))) break
  }
  return lo + (hi - lo) / 2
}

export function fmt(value: number, digits = 4): string {
  if (!Number.isFinite(value)) return value === Infinity ? '∞' : value === -Infinity ? '-∞' : '*'
  if (value === 0) return '0'
  const abs = Math.abs(value)
  if (abs >= 1e7 || abs < 1e-4) return value.toExponential(digits)
  const decimals = Math.max(0, digits)
  return value.toFixed(decimals).replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, '')
}

export function fmtP(value: number): string {
  if (!Number.isFinite(value)) return '*'
  if (value < 0.001) return '<0.001'
  return fmt(value, 3)
}
