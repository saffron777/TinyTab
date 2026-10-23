/**
 * Distribuciones de probabilidad de TinyTab (implementación propia).
 * Cada distribución valida sus parámetros y ofrece pdf/pmf, cdf, invCdf,
 * muestreo y momentos. La parametrización se documenta en `params`.
 */
import {
  betai,
  bisectInverse,
  clamp,
  gammp,
  gammq,
  logCombination,
  logGamma,
  normalCdf,
  normalInv,
  normalPdf
} from './numeric'
import type { Rng } from './random'
import { err, ok, type Result } from '../model/result'

export type Params = Readonly<Record<string, number>>

export interface DistParamDef {
  readonly key: string
  readonly label: string
  readonly defaultValue: number
  readonly integer?: boolean
  readonly minExclusive?: number
  readonly minInclusive?: number
  readonly maxInclusive?: number
}

export type DistributionKind = 'continuous' | 'discrete'

export interface Distribution {
  readonly id: DistId
  readonly label: string
  readonly kind: DistributionKind
  readonly params: readonly DistParamDef[]
  readonly pdf: (x: number, p: Params) => number
  readonly cdf: (x: number, p: Params) => number
  readonly invCdf: (q: number, p: Params) => number
  readonly random: (rng: Rng, p: Params) => number
  readonly validate: (p: Params) => Result<void>
  readonly mean: (p: Params) => number
  readonly sd: (p: Params) => number
  readonly support: (p: Params) => { lower: number; upper: number }
}

export type DistId =
  | 'normal'
  | 't'
  | 'chi2'
  | 'f'
  | 'gamma'
  | 'beta'
  | 'exponential'
  | 'weibull'
  | 'lognormal'
  | 'uniform'
  | 'logistic'
  | 'cauchy'
  | 'laplace'
  | 'triangular'
  | 'smallestExtreme'
  | 'largestExtreme'
  | 'foldedNormal'
  | 'binomial'
  | 'poisson'
  | 'hypergeometric'
  | 'negativeBinomial'

const INF = Number.POSITIVE_INFINITY

function value(p: Params, key: string): number {
  return p[key] ?? Number.NaN
}

function finite(p: Params, keys: readonly string[]): Result<void> {
  for (const key of keys) {
    if (!Number.isFinite(value(p, key))) return err('INVALID_PARAMETER', `El parámetro "${key}" no es válido.`)
  }
  return ok(undefined)
}

function positive(p: Params, keys: readonly string[]): Result<void> {
  for (const key of keys) {
    if (!(value(p, key) > 0)) return err('INVALID_PARAMETER', `El parámetro "${key}" debe ser mayor que cero.`)
  }
  return ok(undefined)
}

/** Inversión genérica de una CDF monótona: expande el límite y bisecciona. */
function invert(cdf: (x: number) => number, q: number, lower: number, upper: number): number {
  if (!(q > 0)) return lower
  if (q >= 1) return upper
  let lo = lower
  let hi = upper
  if (!Number.isFinite(hi)) {
    hi = Math.max(1, Number.isFinite(lo) ? lo + 1 : 1)
    let guard = 0
    while (cdf(hi) < q && guard < 200) {
      hi = hi * 2 + 1
      guard += 1
    }
  }
  if (!Number.isFinite(lo)) {
    lo = -1
    let guard = 0
    while (cdf(lo) > q && guard < 200) {
      lo = lo * 2 - 1
      guard += 1
    }
  }
  return bisectInverse(cdf, q, lo, hi)
}

/** Inversión para variables discretas por búsqueda binaria sobre enteros. */
function discreteInvert(cdf: (k: number) => number, q: number, lower: number, upper: number): number {
  const lo = Math.max(0, Math.floor(lower))
  const bounded = Number.isFinite(upper)
  if (!(q > 0)) return lo
  if (q >= 1) return bounded ? Math.floor(upper) : lo
  const target = Math.min(q, 1 - 1e-12)
  let hi = bounded ? Math.floor(upper) : Math.max(lo + 10, 1)
  let guard = 0
  while (cdf(hi) < target && guard < 200) {
    hi = Math.ceil(hi * 2 + 1)
    guard += 1
  }
  let a = lo
  let b = hi
  while (a < b) {
    const mid = Math.floor((a + b) / 2)
    if (cdf(mid) >= target) b = mid
    else a = mid + 1
  }
  return a
}

const normalDistribution: Distribution = {
  id: 'normal',
  label: 'Normal',
  kind: 'continuous',
  params: [
    { key: 'mu', label: 'Media (μ)', defaultValue: 0 },
    { key: 'sigma', label: 'Desviación estándar (σ)', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => (finite(p, ['mu']).ok ? positive(p, ['sigma']) : finite(p, ['mu'])),
  pdf: (x, p) => normalPdf((x - value(p, 'mu')) / value(p, 'sigma')) / value(p, 'sigma'),
  cdf: (x, p) => normalCdf((x - value(p, 'mu')) / value(p, 'sigma')),
  invCdf: (q, p) => value(p, 'mu') + value(p, 'sigma') * normalInv(q),
  random: (rng, p) => value(p, 'mu') + value(p, 'sigma') * rng.normal(),
  mean: (p) => value(p, 'mu'),
  sd: (p) => value(p, 'sigma'),
  support: () => ({ lower: -INF, upper: INF })
}

const tDistribution: Distribution = {
  id: 't',
  label: 't de Student',
  kind: 'continuous',
  params: [{ key: 'df', label: 'Grados de libertad', defaultValue: 10, minExclusive: 0 }],
  validate: (p) => positive(p, ['df']),
  pdf: (x, p) => {
    const df = value(p, 'df')
    return (
      (Math.exp(logGamma((df + 1) / 2) - logGamma(df / 2)) / Math.sqrt(df * Math.PI)) *
      Math.pow(1 + (x * x) / df, -(df + 1) / 2)
    )
  },
  cdf: (x, p) => {
    const df = value(p, 'df')
    const tail = 0.5 * betai(df / 2, 0.5, df / (df + x * x))
    return x >= 0 ? 1 - tail : tail
  },
  invCdf: (q, p) => {
    const df = value(p, 'df')
    const target = 1 - 2 * Math.min(q, 1 - q)
    const u = invert((v) => betai(df / 2, 0.5, v), target, 0, 1)
    const x = Math.sqrt((df * (1 - u)) / u)
    return q < 0.5 ? -x : x
  },
  random: (rng, p) => {
    const df = value(p, 'df')
    return rng.normal() / Math.sqrt(rng.gamma(df / 2) / (df / 2))
  },
  mean: (p) => (value(p, 'df') > 1 ? 0 : Number.NaN),
  sd: (p) => (value(p, 'df') > 2 ? Math.sqrt(value(p, 'df') / (value(p, 'df') - 2)) : Number.NaN),
  support: () => ({ lower: -INF, upper: INF })
}

const chiSquareDistribution: Distribution = {
  id: 'chi2',
  label: 'Chi-cuadrada',
  kind: 'continuous',
  params: [{ key: 'df', label: 'Grados de libertad', defaultValue: 5, minExclusive: 0 }],
  validate: (p) => positive(p, ['df']),
  pdf: (x, p) => {
    const df = value(p, 'df')
    if (x <= 0) return 0
    return Math.exp((df / 2 - 1) * Math.log(x) - x / 2 - (df / 2) * Math.log(2) - logGamma(df / 2))
  },
  cdf: (x, p) => (x <= 0 ? 0 : gammp(value(p, 'df') / 2, x / 2)),
  invCdf: (q, p) => invert((x) => gammp(value(p, 'df') / 2, x / 2), q, 0, INF),
  random: (rng, p) => 2 * rng.gamma(value(p, 'df') / 2),
  mean: (p) => value(p, 'df'),
  sd: (p) => Math.sqrt(2 * value(p, 'df')),
  support: () => ({ lower: 0, upper: INF })
}

function fCdf(x: number, p: Params): number {
  const d1 = value(p, 'df1')
  const d2 = value(p, 'df2')
  if (x <= 0) return 0
  return betai(d1 / 2, d2 / 2, (d1 * x) / (d1 * x + d2))
}

const fDistribution: Distribution = {
  id: 'f',
  label: 'F de Fisher',
  kind: 'continuous',
  params: [
    { key: 'df1', label: 'Grados de libertad numerador', defaultValue: 5, minExclusive: 0 },
    { key: 'df2', label: 'Grados de libertad denominador', defaultValue: 10, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['df1', 'df2']),
  pdf: (x, p) => {
    const d1 = value(p, 'df1')
    const d2 = value(p, 'df2')
    if (x <= 0) return 0
    return Math.exp(
      0.5 * d1 * Math.log(d1) +
        0.5 * d2 * Math.log(d2) +
        (d1 / 2 - 1) * Math.log(x) -
        ((d1 + d2) / 2) * Math.log(d2 + d1 * x) -
        (logGamma(d1 / 2) + logGamma(d2 / 2) - logGamma((d1 + d2) / 2))
    )
  },
  cdf: (x, p) => fCdf(x, p),
  invCdf: (q, p) => invert((x) => fCdf(x, p), q, 0, INF),
  random: (rng, p) => {
    const d1 = value(p, 'df1')
    const d2 = value(p, 'df2')
    const numerator = 2 * rng.gamma(d1 / 2)
    const denominator = 2 * rng.gamma(d2 / 2)
    return (numerator / d1) / (denominator / d2)
  },
  mean: (p) => (value(p, 'df2') > 2 ? value(p, 'df2') / (value(p, 'df2') - 2) : Number.NaN),
  sd: (p) => {
    const d2 = value(p, 'df2')
    if (d2 <= 4) return Number.NaN
    return Math.sqrt((2 * d2 * d2 * (d2 - 2)) / (Math.pow(d2 - 2, 2) * (d2 - 4)))
  },
  support: () => ({ lower: 0, upper: INF })
}

const gammaDistribution: Distribution = {
  id: 'gamma',
  label: 'Gamma',
  kind: 'continuous',
  params: [
    { key: 'shape', label: 'Forma', defaultValue: 2, minExclusive: 0 },
    { key: 'scale', label: 'Escala', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['shape', 'scale']),
  pdf: (x, p) => {
    const k = value(p, 'shape')
    const s = value(p, 'scale')
    if (x <= 0) return 0
    return Math.exp((k - 1) * Math.log(x) - x / s - k * Math.log(s) - logGamma(k))
  },
  cdf: (x, p) => (x <= 0 ? 0 : gammp(value(p, 'shape'), x / value(p, 'scale'))),
  invCdf: (q, p) => invert((x) => gammp(value(p, 'shape'), x / value(p, 'scale')), q, 0, INF),
  random: (rng, p) => rng.gamma(value(p, 'shape'), value(p, 'scale')),
  mean: (p) => value(p, 'shape') * value(p, 'scale'),
  sd: (p) => Math.sqrt(value(p, 'shape')) * value(p, 'scale'),
  support: () => ({ lower: 0, upper: INF })
}

const betaDistribution: Distribution = {
  id: 'beta',
  label: 'Beta',
  kind: 'continuous',
  params: [
    { key: 'a', label: 'Alfa (α)', defaultValue: 2, minExclusive: 0 },
    { key: 'b', label: 'Beta (β)', defaultValue: 2, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['a', 'b']),
  pdf: (x, p) => {
    const a = value(p, 'a')
    const b = value(p, 'b')
    if (x <= 0 || x >= 1) return 0
    return Math.exp(
      (a - 1) * Math.log(x) + (b - 1) * Math.log(1 - x) - (logGamma(a) + logGamma(b) - logGamma(a + b))
    )
  },
  cdf: (x, p) => betai(value(p, 'a'), value(p, 'b'), x),
  invCdf: (q, p) => invert((x) => betai(value(p, 'a'), value(p, 'b'), x), q, 0, 1),
  random: (rng, p) => rng.beta(value(p, 'a'), value(p, 'b')),
  mean: (p) => value(p, 'a') / (value(p, 'a') + value(p, 'b')),
  sd: (p) => {
    const a = value(p, 'a')
    const b = value(p, 'b')
    return Math.sqrt((a * b) / ((a + b) * (a + b) * (a + b + 1)))
  },
  support: () => ({ lower: 0, upper: 1 })
}

const exponentialDistribution: Distribution = {
  id: 'exponential',
  label: 'Exponencial',
  kind: 'continuous',
  params: [{ key: 'rate', label: 'Tasa (λ)', defaultValue: 1, minExclusive: 0 }],
  validate: (p) => positive(p, ['rate']),
  pdf: (x, p) => (x < 0 ? 0 : value(p, 'rate') * Math.exp(-value(p, 'rate') * x)),
  cdf: (x, p) => (x < 0 ? 0 : 1 - Math.exp(-value(p, 'rate') * x)),
  invCdf: (q, p) => -Math.log(1 - q) / value(p, 'rate'),
  random: (rng, p) => rng.exponential(value(p, 'rate')),
  mean: (p) => 1 / value(p, 'rate'),
  sd: (p) => 1 / value(p, 'rate'),
  support: () => ({ lower: 0, upper: INF })
}

const weibullDistribution: Distribution = {
  id: 'weibull',
  label: 'Weibull',
  kind: 'continuous',
  params: [
    { key: 'shape', label: 'Forma', defaultValue: 1.5, minExclusive: 0 },
    { key: 'scale', label: 'Escala', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['shape', 'scale']),
  pdf: (x, p) => {
    const k = value(p, 'shape')
    const s = value(p, 'scale')
    if (x < 0) return 0
    return (k / s) * Math.pow(x / s, k - 1) * Math.exp(-Math.pow(x / s, k))
  },
  cdf: (x, p) => (x < 0 ? 0 : 1 - Math.exp(-Math.pow(x / value(p, 'scale'), value(p, 'shape')))),
  invCdf: (q, p) => value(p, 'scale') * Math.pow(-Math.log(1 - q), 1 / value(p, 'shape')),
  random: (rng, p) => rng.weibull(value(p, 'shape'), value(p, 'scale')),
  mean: (p) => value(p, 'scale') * Math.exp(logGamma(1 + 1 / value(p, 'shape'))),
  sd: (p) => {
    const k = value(p, 'shape')
    const s = value(p, 'scale')
    const m1 = Math.exp(logGamma(1 + 1 / k))
    const m2 = Math.exp(logGamma(1 + 2 / k))
    return s * Math.sqrt(m2 - m1 * m1)
  },
  support: () => ({ lower: 0, upper: INF })
}

const lognormalDistribution: Distribution = {
  id: 'lognormal',
  label: 'Log-normal',
  kind: 'continuous',
  params: [
    { key: 'meanLog', label: 'Media de log (μ)', defaultValue: 0 },
    { key: 'sdLog', label: 'Desviación de log (σ)', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['sdLog']),
  pdf: (x, p) => {
    const mu = value(p, 'meanLog')
    const sigma = value(p, 'sdLog')
    if (x <= 0) return 0
    return normalPdf((Math.log(x) - mu) / sigma) / (x * sigma)
  },
  cdf: (x, p) => (x <= 0 ? 0 : normalCdf((Math.log(x) - value(p, 'meanLog')) / value(p, 'sdLog'))),
  invCdf: (q, p) => Math.exp(value(p, 'meanLog') + value(p, 'sdLog') * normalInv(q)),
  random: (rng, p) => rng.lognormal(value(p, 'meanLog'), value(p, 'sdLog')),
  mean: (p) => Math.exp(value(p, 'meanLog') + (value(p, 'sdLog') ** 2) / 2),
  sd: (p) => {
    const mu = value(p, 'meanLog')
    const sigma = value(p, 'sdLog')
    return Math.sqrt((Math.exp(sigma * sigma) - 1) * Math.exp(2 * mu + sigma * sigma))
  },
  support: () => ({ lower: 0, upper: INF })
}

const uniformDistribution: Distribution = {
  id: 'uniform',
  label: 'Uniforme continua',
  kind: 'continuous',
  params: [
    { key: 'a', label: 'Mínimo', defaultValue: 0 },
    { key: 'b', label: 'Máximo', defaultValue: 1 }
  ],
  validate: (p) =>
    value(p, 'a') < value(p, 'b') ? ok(undefined) : err('INVALID_PARAMETER', 'El mínimo debe ser menor que el máximo.'),
  pdf: (x, p) => (x < value(p, 'a') || x > value(p, 'b') ? 0 : 1 / (value(p, 'b') - value(p, 'a'))),
  cdf: (x, p) => clamp((x - value(p, 'a')) / (value(p, 'b') - value(p, 'a')), 0, 1),
  invCdf: (q, p) => value(p, 'a') + q * (value(p, 'b') - value(p, 'a')),
  random: (rng, p) => rng.uniform(value(p, 'a'), value(p, 'b')),
  mean: (p) => (value(p, 'a') + value(p, 'b')) / 2,
  sd: (p) => Math.abs(value(p, 'b') - value(p, 'a')) / Math.sqrt(12),
  support: (p) => ({ lower: value(p, 'a'), upper: value(p, 'b') })
}

const logisticDistribution: Distribution = {
  id: 'logistic',
  label: 'Logística',
  kind: 'continuous',
  params: [
    { key: 'mu', label: 'Ubicación', defaultValue: 0 },
    { key: 's', label: 'Escala', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['s']),
  pdf: (x, p) => {
    const s = value(p, 's')
    const z = (x - value(p, 'mu')) / s
    const e = Math.exp(-z)
    return e / (s * (1 + e) * (1 + e))
  },
  cdf: (x, p) => 1 / (1 + Math.exp(-(x - value(p, 'mu')) / value(p, 's'))),
  invCdf: (q, p) => value(p, 'mu') + value(p, 's') * Math.log(q / (1 - q)),
  random: (rng, p) => value(p, 'mu') + value(p, 's') * Math.log(rng.next() / (1 - rng.next())),
  mean: (p) => value(p, 'mu'),
  sd: (p) => (Math.PI * value(p, 's')) / Math.sqrt(3),
  support: () => ({ lower: -INF, upper: INF })
}

const cauchyDistribution: Distribution = {
  id: 'cauchy',
  label: 'Cauchy',
  kind: 'continuous',
  params: [
    { key: 'mu', label: 'Ubicación', defaultValue: 0 },
    { key: 's', label: 'Escala', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['s']),
  pdf: (x, p) => {
    const s = value(p, 's')
    const z = (x - value(p, 'mu')) / s
    return 1 / (Math.PI * s * (1 + z * z))
  },
  cdf: (x, p) => 0.5 + Math.atan((x - value(p, 'mu')) / value(p, 's')) / Math.PI,
  invCdf: (q, p) => value(p, 'mu') + value(p, 's') * Math.tan(Math.PI * (q - 0.5)),
  random: (rng, p) => value(p, 'mu') + value(p, 's') * Math.tan(Math.PI * (rng.next() - 0.5)),
  mean: () => Number.NaN,
  sd: () => Number.NaN,
  support: () => ({ lower: -INF, upper: INF })
}

const laplaceDistribution: Distribution = {
  id: 'laplace',
  label: 'Laplace',
  kind: 'continuous',
  params: [
    { key: 'mu', label: 'Ubicación', defaultValue: 0 },
    { key: 'b', label: 'Escala', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['b']),
  pdf: (x, p) => (1 / (2 * value(p, 'b'))) * Math.exp(-Math.abs(x - value(p, 'mu')) / value(p, 'b')),
  cdf: (x, p) => {
    const z = (x - value(p, 'mu')) / value(p, 'b')
    return z < 0 ? 0.5 * Math.exp(z) : 1 - 0.5 * Math.exp(-z)
  },
  invCdf: (q, p) => {
    const b = value(p, 'b')
    const mu = value(p, 'mu')
    return q < 0.5 ? mu + b * Math.log(2 * q) : mu - b * Math.log(2 * (1 - q))
  },
  random: (rng, p) => {
    const b = value(p, 'b')
    const mu = value(p, 'mu')
    const u = rng.next() - 0.5
    return mu - b * Math.sign(u) * Math.log(1 - 2 * Math.abs(u))
  },
  mean: (p) => value(p, 'mu'),
  sd: (p) => Math.SQRT2 * value(p, 'b'),
  support: () => ({ lower: -INF, upper: INF })
}

const triangularDistribution: Distribution = {
  id: 'triangular',
  label: 'Triangular',
  kind: 'continuous',
  params: [
    { key: 'a', label: 'Mínimo', defaultValue: 0 },
    { key: 'c', label: 'Moda', defaultValue: 0.5 },
    { key: 'b', label: 'Máximo', defaultValue: 1 }
  ],
  validate: (p) =>
    value(p, 'a') < value(p, 'b') && value(p, 'c') >= value(p, 'a') && value(p, 'c') <= value(p, 'b')
      ? ok(undefined)
      : err('INVALID_PARAMETER', 'Se requiere a ≤ c ≤ b con a < b.'),
  pdf: (x, p) => {
    const a = value(p, 'a')
    const b = value(p, 'b')
    const c = value(p, 'c')
    if (x < a || x > b) return 0
    if (x < c) return (2 * (x - a)) / ((b - a) * (c - a))
    if (x === c) return 2 / (b - a)
    return (2 * (b - x)) / ((b - a) * (b - c))
  },
  cdf: (x, p) => {
    const a = value(p, 'a')
    const b = value(p, 'b')
    const c = value(p, 'c')
    if (x <= a) return 0
    if (x >= b) return 1
    if (x <= c) return ((x - a) * (x - a)) / ((b - a) * (c - a))
    return 1 - ((b - x) * (b - x)) / ((b - a) * (b - c))
  },
  invCdf: (q, p) => {
    const a = value(p, 'a')
    const b = value(p, 'b')
    const c = value(p, 'c')
    const split = (c - a) / (b - a)
    return q <= split ? a + Math.sqrt(q * (b - a) * (c - a)) : b - Math.sqrt((1 - q) * (b - a) * (b - c))
  },
  random: (rng, p) => {
    const a = value(p, 'a')
    const b = value(p, 'b')
    const c = value(p, 'c')
    const u = rng.next()
    const split = (c - a) / (b - a)
    return u <= split ? a + Math.sqrt(u * (b - a) * (c - a)) : b - Math.sqrt((1 - u) * (b - a) * (b - c))
  },
  mean: (p) => (value(p, 'a') + value(p, 'b') + value(p, 'c')) / 3,
  sd: (p) =>
    Math.sqrt(
      (value(p, 'a') ** 2 +
        value(p, 'b') ** 2 +
        value(p, 'c') ** 2 -
        value(p, 'a') * value(p, 'b') -
        value(p, 'a') * value(p, 'c') -
        value(p, 'b') * value(p, 'c')) /
        18
    ),
  support: (p) => ({ lower: value(p, 'a'), upper: value(p, 'b') })
}

const smallestExtremeDistribution: Distribution = {
  id: 'smallestExtreme',
  label: 'Valor extremo menor (Gumbel)',
  kind: 'continuous',
  params: [
    { key: 'mu', label: 'Ubicación', defaultValue: 0 },
    { key: 's', label: 'Escala', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['s']),
  pdf: (x, p) => {
    const s = value(p, 's')
    const z = Math.exp((x - value(p, 'mu')) / s)
    return (z * Math.exp(-z)) / s
  },
  cdf: (x, p) => 1 - Math.exp(-Math.exp((x - value(p, 'mu')) / value(p, 's'))),
  invCdf: (q, p) => value(p, 'mu') + value(p, 's') * Math.log(-Math.log(1 - q)),
  random: (rng, p) => value(p, 'mu') + value(p, 's') * Math.log(-Math.log(1 - rng.next())),
  mean: (p) => value(p, 'mu') - 0.5772156649015329 * value(p, 's'),
  sd: (p) => (Math.PI * value(p, 's')) / Math.sqrt(6),
  support: () => ({ lower: -INF, upper: INF })
}

const largestExtremeDistribution: Distribution = {
  id: 'largestExtreme',
  label: 'Valor extremo mayor (Gumbel)',
  kind: 'continuous',
  params: [
    { key: 'mu', label: 'Ubicación', defaultValue: 0 },
    { key: 's', label: 'Escala', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['s']),
  pdf: (x, p) => {
    const s = value(p, 's')
    const z = Math.exp(-(x - value(p, 'mu')) / s)
    return (z * Math.exp(-z)) / s
  },
  cdf: (x, p) => Math.exp(-Math.exp(-(x - value(p, 'mu')) / value(p, 's'))),
  invCdf: (q, p) => value(p, 'mu') - value(p, 's') * Math.log(-Math.log(q)),
  random: (rng, p) => value(p, 'mu') - value(p, 's') * Math.log(-Math.log(rng.next())),
  mean: (p) => value(p, 'mu') + 0.5772156649015329 * value(p, 's'),
  sd: (p) => (Math.PI * value(p, 's')) / Math.sqrt(6),
  support: () => ({ lower: -INF, upper: INF })
}

function foldedNormalCdf(x: number, p: Params): number {
  if (x <= 0) return 0
  const mu = value(p, 'mu')
  const s = value(p, 'sigma')
  return normalCdf((x - mu) / s) - normalCdf((-x - mu) / s)
}

function foldedNormalMean(p: Params): number {
  const mu = value(p, 'mu')
  const s = value(p, 'sigma')
  return s * Math.sqrt(2 / Math.PI) * Math.exp(-(mu * mu) / (2 * s * s)) + mu * (1 - 2 * normalCdf(-mu / s))
}

const foldedNormalDistribution: Distribution = {
  id: 'foldedNormal',
  label: 'Normal plegada',
  kind: 'continuous',
  params: [
    { key: 'mu', label: 'Media (μ)', defaultValue: 0 },
    { key: 'sigma', label: 'Desviación (σ)', defaultValue: 1, minExclusive: 0 }
  ],
  validate: (p) => positive(p, ['sigma']),
  pdf: (x, p) => {
    const mu = value(p, 'mu')
    const s = value(p, 'sigma')
    if (x < 0) return 0
    return (normalPdf((x - mu) / s) + normalPdf((x + mu) / s)) / s
  },
  cdf: (x, p) => foldedNormalCdf(x, p),
  invCdf: (q, p) => invert((x) => foldedNormalCdf(x, p), q, 0, INF),
  random: (rng, p) => Math.abs(value(p, 'mu') + value(p, 'sigma') * rng.normal()),
  mean: (p) => foldedNormalMean(p),
  sd: (p) => {
    const meanValue = foldedNormalMean(p)
    return Math.sqrt(value(p, 'mu') * value(p, 'mu') + value(p, 'sigma') * value(p, 'sigma') - meanValue * meanValue)
  },
  support: () => ({ lower: 0, upper: INF })
}

function binomialCdf(k: number, p: Params): number {
  const n = value(p, 'n')
  if (k < 0) return 0
  if (k >= n) return 1
  return betai(n - Math.floor(k), Math.floor(k) + 1, 1 - value(p, 'p'))
}

const binomialDistribution: Distribution = {
  id: 'binomial',
  label: 'Binomial',
  kind: 'discrete',
  params: [
    { key: 'n', label: 'Número de ensayos', defaultValue: 10, integer: true, minInclusive: 1 },
    { key: 'p', label: 'Probabilidad de éxito', defaultValue: 0.5, minExclusive: 0, maxInclusive: 1 }
  ],
  validate: (p) => {
    if (!Number.isInteger(value(p, 'n')) || value(p, 'n') < 1) return err('INVALID_PARAMETER', 'n debe ser un entero positivo.')
    if (!(value(p, 'p') > 0 && value(p, 'p') <= 1)) return err('INVALID_PARAMETER', 'p debe estar en (0, 1].')
    return ok(undefined)
  },
  pdf: (k, p) =>
    k < 0 || k > value(p, 'n') || !Number.isInteger(k) ? 0 : Math.exp(logPmfBinomial(value(p, 'n'), k, value(p, 'p'))),
  cdf: (k, p) => binomialCdf(k, p),
  invCdf: (q, p) => discreteInvert((k) => binomialCdf(k, p), q, 0, value(p, 'n')),
  random: (rng, p) => rng.binomial(value(p, 'n'), value(p, 'p')),
  mean: (p) => value(p, 'n') * value(p, 'p'),
  sd: (p) => Math.sqrt(value(p, 'n') * value(p, 'p') * (1 - value(p, 'p'))),
  support: (p) => ({ lower: 0, upper: value(p, 'n') })
}

function poissonCdf(k: number, p: Params): number {
  return k < 0 ? 0 : gammq(Math.floor(k) + 1, value(p, 'lambda'))
}

const poissonDistribution: Distribution = {
  id: 'poisson',
  label: 'Poisson',
  kind: 'discrete',
  params: [{ key: 'lambda', label: 'Media (λ)', defaultValue: 3, minExclusive: 0 }],
  validate: (p) => positive(p, ['lambda']),
  pdf: (k, p) => (k < 0 || !Number.isInteger(k) ? 0 : Math.exp(logPmfPoisson(value(p, 'lambda'), k))),
  cdf: (k, p) => poissonCdf(k, p),
  invCdf: (q, p) => discreteInvert((k) => poissonCdf(k, p), q, 0, INF),
  random: (rng, p) => rng.poisson(value(p, 'lambda')),
  mean: (p) => value(p, 'lambda'),
  sd: (p) => Math.sqrt(value(p, 'lambda')),
  support: () => ({ lower: 0, upper: INF })
}

function hypergeometricPdf(k: number, p: Params): number {
  const N = value(p, 'population')
  const K = value(p, 'successes')
  const n = value(p, 'sample')
  if (k < Math.max(0, n - (N - K)) || k > Math.min(K, n)) return 0
  return Math.exp(logCombination(K, k) + logCombination(N - K, n - k) - logCombination(N, n))
}

function hypergeometricCdf(k: number, p: Params): number {
  let total = 0
  for (let i = 0; i <= Math.floor(k); i += 1) total += hypergeometricPdf(i, p)
  return Math.min(1, total)
}

const hypergeometricDistribution: Distribution = {
  id: 'hypergeometric',
  label: 'Hipergeométrica',
  kind: 'discrete',
  params: [
    { key: 'population', label: 'Tamaño de población (N)', defaultValue: 50, integer: true, minInclusive: 1 },
    { key: 'successes', label: 'Éxitos en la población (K)', defaultValue: 10, integer: true, minInclusive: 0 },
    { key: 'sample', label: 'Tamaño de muestra (n)', defaultValue: 5, integer: true, minInclusive: 0 }
  ],
  validate: (p) =>
    value(p, 'successes') <= value(p, 'population') && value(p, 'sample') <= value(p, 'population')
      ? ok(undefined)
      : err('INVALID_PARAMETER', 'Se requiere K ≤ N y n ≤ N.'),
  pdf: (k, p) => hypergeometricPdf(k, p),
  cdf: (k, p) => hypergeometricCdf(k, p),
  invCdf: (q, p) =>
    discreteInvert(
      (k) => hypergeometricCdf(k, p),
      q,
      0,
      Math.min(value(p, 'successes'), value(p, 'sample'))
    ),
  random: (rng, p) => {
    let remainingSuccesses = value(p, 'successes')
    let remainingPopulation = value(p, 'population')
    let drawn = 0
    for (let i = 0; i < value(p, 'sample'); i += 1) {
      if (rng.next() < remainingSuccesses / remainingPopulation) {
        drawn += 1
        remainingSuccesses -= 1
      }
      remainingPopulation -= 1
    }
    return drawn
  },
  mean: (p) => (value(p, 'sample') * value(p, 'successes')) / value(p, 'population'),
  sd: (p) => {
    const N = value(p, 'population')
    const K = value(p, 'successes')
    const n = value(p, 'sample')
    return Math.sqrt((n * K * (N - K) * (N - n)) / (N * N * (N - 1)))
  },
  support: (p) => ({
    lower: Math.max(0, value(p, 'sample') - (value(p, 'population') - value(p, 'successes'))),
    upper: Math.min(value(p, 'successes'), value(p, 'sample'))
  })
}

function negativeBinomialPdf(k: number, p: Params): number {
  if (k < 0 || !Number.isInteger(k)) return 0
  const r = value(p, 'r')
  return Math.exp(logCombination(k + r - 1, k) + r * Math.log(value(p, 'p')) + k * Math.log(1 - value(p, 'p')))
}

function negativeBinomialCdf(k: number, p: Params): number {
  let total = 0
  for (let i = 0; i <= Math.floor(k); i += 1) total += negativeBinomialPdf(i, p)
  return Math.min(1, total)
}

const negativeBinomialDistribution: Distribution = {
  id: 'negativeBinomial',
  label: 'Binomial negativa',
  kind: 'discrete',
  params: [
    { key: 'r', label: 'Número de éxitos (r)', defaultValue: 5, integer: true, minInclusive: 1 },
    { key: 'p', label: 'Probabilidad de éxito', defaultValue: 0.5, minExclusive: 0, maxInclusive: 1 }
  ],
  validate: (p) => {
    if (!Number.isInteger(value(p, 'r')) || value(p, 'r') < 1) return err('INVALID_PARAMETER', 'r debe ser un entero positivo.')
    if (!(value(p, 'p') > 0 && value(p, 'p') <= 1)) return err('INVALID_PARAMETER', 'p debe estar en (0, 1].')
    return ok(undefined)
  },
  pdf: (k, p) => negativeBinomialPdf(k, p),
  cdf: (k, p) => negativeBinomialCdf(k, p),
  invCdf: (q, p) => discreteInvert((k) => negativeBinomialCdf(k, p), q, 0, INF),
  random: (rng, p) => {
    let failures = 0
    let successes = 0
    while (successes < value(p, 'r')) {
      if (rng.next() < value(p, 'p')) successes += 1
      else failures += 1
    }
    return failures
  },
  mean: (p) => (value(p, 'r') * (1 - value(p, 'p'))) / value(p, 'p'),
  sd: (p) => Math.sqrt((value(p, 'r') * (1 - value(p, 'p'))) / (value(p, 'p') * value(p, 'p'))),
  support: () => ({ lower: 0, upper: INF })
}

function logPmfBinomial(n: number, k: number, p: number): number {
  return logCombination(n, k) + k * Math.log(p) + (n - k) * Math.log(1 - p)
}

function logPmfPoisson(lambda: number, k: number): number {
  return -lambda + k * Math.log(lambda) - logGamma(k + 1)
}

export const DISTRIBUTIONS: readonly Distribution[] = [
  normalDistribution,
  tDistribution,
  chiSquareDistribution,
  fDistribution,
  gammaDistribution,
  betaDistribution,
  exponentialDistribution,
  weibullDistribution,
  lognormalDistribution,
  uniformDistribution,
  logisticDistribution,
  cauchyDistribution,
  laplaceDistribution,
  triangularDistribution,
  smallestExtremeDistribution,
  largestExtremeDistribution,
  foldedNormalDistribution,
  binomialDistribution,
  poissonDistribution,
  hypergeometricDistribution,
  negativeBinomialDistribution
]

export function getDistribution(id: DistId): Distribution {
  const distribution = DISTRIBUTIONS.find((item) => item.id === id)
  if (!distribution) throw new Error(`Distribución desconocida: ${id}`)
  return distribution
}

export function defaultParams(distribution: Distribution): Params {
  const params: Record<string, number> = {}
  for (const param of distribution.params) params[param.key] = param.defaultValue
  return params
}

export function validateDistribution(distribution: Distribution, params: Params): Result<void> {
  return distribution.validate(params)
}
