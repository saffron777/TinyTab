import {
  correlation,
  covariance,
  iqr,
  kurtosis,
  maxOf,
  mean,
  median,
  minOf,
  quantile,
  sd,
  seMean,
  skewness,
  sum,
  variance
} from './numeric'

export interface DescriptiveResult {
  readonly n: number
  readonly missing: number
  readonly mean: number
  readonly seMean: number
  readonly sd: number
  readonly variance: number
  readonly cv: number
  readonly min: number
  readonly q1: number
  readonly median: number
  readonly q3: number
  readonly max: number
  readonly iqr: number
  readonly range: number
  readonly skewness: number
  readonly kurtosis: number
  readonly sum: number
  readonly sumSquares: number
}

export function describe(values: readonly number[], missing = 0): DescriptiveResult {
  const n = values.length
  const m = mean(values)
  const s = sd(values)
  return {
    n,
    missing,
    mean: m,
    seMean: n > 1 ? seMean(values) : Number.NaN,
    sd: s,
    variance: variance(values),
    cv: m !== 0 ? (s / Math.abs(m)) * 100 : Number.NaN,
    min: n > 0 ? minOf(values) : Number.NaN,
    q1: quantile(values, 0.25),
    median: median(values),
    q3: quantile(values, 0.75),
    max: n > 0 ? maxOf(values) : Number.NaN,
    iqr: iqr(values),
    range: n > 0 ? maxOf(values) - minOf(values) : Number.NaN,
    skewness: skewness(values),
    kurtosis: kurtosis(values),
    sum: sum(values),
    sumSquares: sum(values.map((value) => value * value))
  }
}

/** Intervalo de confianza para la media basado en t (o z si se indica). */
export function meanConfidenceInterval(
  values: readonly number[],
  confidence = 0.95,
  sigma?: number
): { lower: number; upper: number; halfWidth: number } {
  const n = values.length
  const center = mean(values)
  const standardError = sigma !== undefined ? sigma / Math.sqrt(n) : sd(values) / Math.sqrt(n)
  const critical = sigma !== undefined ? normalQuantile(1 - (1 - confidence) / 2) : tQuantile(1 - (1 - confidence) / 2, n - 1)
  const halfWidth = critical * standardError
  return { lower: center - halfWidth, upper: center + halfWidth, halfWidth }
}

/** Cuantil normal (evita una dependencia circular con el registro de distribuciones). */
function normalQuantile(p: number): number {
  const z = inverseNormalApprox(p)
  return z
}

function inverseNormalApprox(p: number): number {
  /* Aproximación de Beasley-Springer-Moro con refinamiento de Newton. */
  const a = [0, -3.969683028665376e1, 2.209460984245205e2, -2.759285104469687e2, 1.38357751867269e2, -3.066479806614716e1, 2.506628277459239]
  const b = [0, -5.447609879822406e1, 1.615858368580409e2, -1.556989798598866e2, 6.680131188771972e1, -1.328068155288572e1]
  const c = [0, -7.784894002430293e-3, -3.223964580411365e-1, -2.400758277161838, -2.549732539343734, 4.374664141464968, 2.938163982698783]
  const d = [0, 7.784695709041462e-3, 3.224671290700398e-1, 2.445134137142996, 3.754408661907416]
  const plow = 0.02425
  const phigh = 1 - plow
  if (p <= 0) return Number.NEGATIVE_INFINITY
  if (p >= 1) return Number.POSITIVE_INFINITY
  let x: number
  if (p < plow) {
    const q = Math.sqrt(-2 * Math.log(p))
    x = (((((c[1] as number) * q + (c[2] as number)) * q + (c[3] as number)) * q + (c[4] as number)) * q + (c[5] as number)) * q + (c[6] as number)
    x = x / (((((d[1] as number) * q + (d[2] as number)) * q + (d[3] as number)) * q + (d[4] as number)) * q + 1)
  } else if (p <= phigh) {
    const q = p - 0.5
    const r = q * q
    x = ((((((a[1] as number) * r + (a[2] as number)) * r + (a[3] as number)) * r + (a[4] as number)) * r + (a[5] as number)) * r + (a[6] as number)) * q
    x = x / ((((((b[1] as number) * r + (b[2] as number)) * r + (b[3] as number)) * r + (b[4] as number)) * r + (b[5] as number)) * r + 1)
  } else {
    const q = Math.sqrt(-2 * Math.log(1 - p))
    x = -(((((c[1] as number) * q + (c[2] as number)) * q + (c[3] as number)) * q + (c[4] as number)) * q + (c[5] as number)) * q - (c[6] as number)
    x = x / (((((d[1] as number) * q + (d[2] as number)) * q + (d[3] as number)) * q + (d[4] as number)) * q + 1)
  }
  return x
}

function tQuantile(p: number, df: number): number {
  if (df <= 0) return Number.NaN
  if (df > 200) return normalQuantile(p)
  let low = -100
  let high = 100
  for (let i = 0; i < 200; i += 1) {
    const mid = (low + high) / 2
    const cdf = tCdf(mid, df)
    if (cdf < p) low = mid
    else high = mid
  }
  return (low + high) / 2
}

function tCdf(x: number, df: number): number {
  const beta = betaiLocal(df / 2, 0.5, df / (df + x * x))
  const tail = 0.5 * beta
  return x >= 0 ? 1 - tail : tail
}

function betaiLocal(a: number, b: number, x: number): number {
  if (x <= 0) return 0
  if (x >= 1) return 1
  const logBeta = logGammaLocal(a + b) - logGammaLocal(a) - logGammaLocal(b) + a * Math.log(x) + b * Math.log(1 - x)
  const front = Math.exp(logBeta)
  if (x < (a + 1) / (a + b + 2)) return (front * betacfLocal(a, b, x)) / a
  return 1 - (front * betacfLocal(b, a, 1 - x)) / b
}

function betacfLocal(a: number, b: number, x: number): number {
  const qab = a + b
  const qap = a + 1
  const qam = a - 1
  let c = 1
  let d = 1 - (qab * x) / qap
  if (Math.abs(d) < 1e-300) d = 1e-300
  d = 1 / d
  let h = d
  for (let m = 1; m <= 2000; m += 1) {
    const m2 = 2 * m
    let aa = (m * (b - m) * x) / ((qam + m2) * (a + m2))
    d = 1 + aa * d
    if (Math.abs(d) < 1e-300) d = 1e-300
    c = 1 + aa / c
    if (Math.abs(c) < 1e-300) c = 1e-300
    d = 1 / d
    h *= d * c
    aa = (-(a + m) * (qab + m) * x) / ((a + m2) * (qap + m2))
    d = 1 + aa * d
    if (Math.abs(d) < 1e-300) d = 1e-300
    c = 1 + aa / c
    if (Math.abs(c) < 1e-300) c = 1e-300
    d = 1 / d
    const del = d * c
    h *= del
    if (Math.abs(del - 1) < 1e-16) break
  }
  return h
}

function logGammaLocal(x: number): number {
  const coef = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7]
  if (x < 0.5) return Math.log(Math.PI / Math.sin(Math.PI * x)) - logGammaLocal(1 - x)
  const z = x - 1
  let a = coef[0] as number
  const t = z + 7.5
  for (let i = 1; i < coef.length; i += 1) a += (coef[i] as number) / (z + i)
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(a)
}

/** Estadísticas por grupo (para describir por niveles de una columna categórica). */
export interface GroupedDescriptive {
  readonly group: string
  readonly values: number[]
  readonly stats: DescriptiveResult
}

export function describeByGroup(groups: ReadonlyMap<string, readonly number[]>, missing = 0): GroupedDescriptive[] {
  return [...groups.entries()].map(([group, values]) => ({ group, values: [...values], stats: describe(values, missing) }))
}

/** Matriz de correlación con etiquetas. */
export function correlationMatrix(
  labels: readonly string[],
  columns: readonly (readonly number[])[]
): { labels: string[]; matrix: number[][] } {
  const n = columns.length
  const matrix = Array.from({ length: n }, (_, i) =>
    Array.from({ length: n }, (_, j) => {
      if (i === j) return 1
      return correlation(columns[i] as number[], columns[j] as number[])
    })
  )
  return { labels: [...labels], matrix }
}

export function covarianceMatrix(columns: readonly (readonly number[])[]): number[][] {
  const n = columns.length
  return Array.from({ length: n }, (_, i) =>
    Array.from({ length: n }, (_, j) => (i === j ? variance(columns[i] as number[]) : covariance(columns[i] as number[], columns[j] as number[])))
  )
}
