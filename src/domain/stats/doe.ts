/**
 * Diseño de experimentos: generación de diseños factoriales, Plackett-Burman,
 * superficies de respuesta y análisis de efectos por mínimos cuadrados.
 */
import { leastSquares, type Matrix } from './linalg'
import { mean } from './numeric'
import { Rng } from './random'
import { err, ok, type Result } from '../model/result'

export interface FactorDef {
  readonly name: string
  readonly low: number
  readonly high: number
}

export interface DesignRun {
  readonly standardOrder: number
  readonly runOrder: number
  readonly coded: number[]
  readonly actual: number[]
}

export interface DoeDesign {
  readonly type: 'factorial' | 'plackett-burman' | 'ccd' | 'box-behnken'
  readonly factors: FactorDef[]
  readonly runs: DesignRun[]
  readonly replicateCount: number
  readonly centerPoints: number
  readonly seed: number
}

function applyFactors(factors: readonly FactorDef[], coded: readonly number[]): number[] {
  return coded.map((value, index) => {
    const factor = factors[index] as FactorDef
    const center = (factor.low + factor.high) / 2
    const half = (factor.high - factor.low) / 2
    return center + half * value
  })
}

function randomize(runs: DesignRun[], seed: number): DesignRun[] {
  const rng = new Rng(seed)
  const shuffled = [...runs]
  for (let i = shuffled.length - 1; i > 0; i -= 1) {
    const j = Math.floor(rng.next() * (i + 1))
    const tmp = shuffled[i] as DesignRun
    shuffled[i] = shuffled[j] as DesignRun
    shuffled[j] = tmp
  }
  return shuffled.map((run, index) => ({ ...run, runOrder: index + 1 }))
}

export function createFactorialDesign(
  factors: readonly FactorDef[],
  options: { replicates?: number; centerPoints?: number; seed?: number; randomize?: boolean } = {}
): Result<DoeDesign> {
  const k = factors.length
  if (k < 1 || k > 8) return err('INVALID_PARAMETER', 'Se admiten entre 1 y 8 factores.')
  const replicates = Math.max(1, Math.floor(options.replicates ?? 1))
  const centerPoints = Math.max(0, Math.floor(options.centerPoints ?? 0))
  const seed = options.seed ?? 20261023
  const base: DesignRun[] = []
  const total = 1 << k
  for (let index = 0; index < total; index += 1) {
    const coded = Array.from({ length: k }, (_, bit) => ((index >> (k - 1 - bit)) & 1 ? 1 : -1))
    base.push({ standardOrder: index + 1, runOrder: index + 1, coded, actual: applyFactors(factors, coded) })
  }
  const runs: DesignRun[] = []
  for (let rep = 0; rep < replicates; rep += 1) runs.push(...base.map((run) => ({ ...run, coded: [...run.coded], actual: [...run.actual] })))
  for (let center = 0; center < centerPoints; center += 1) {
    const coded = new Array<number>(k).fill(0)
    runs.push({ standardOrder: runs.length + 1, runOrder: runs.length + 1, coded, actual: applyFactors(factors, coded) })
  }
  return ok({
    type: 'factorial',
    factors: [...factors],
    runs: options.randomize === false ? runs : randomize(runs, seed),
    replicateCount: replicates,
    centerPoints,
    seed
  })
}

/** Matrices Plackett-Burman estándar de 12 y 20 corridas. */
const PB12: number[][] = [
  [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
  [-1, 1, -1, 1, 1, 1, -1, -1, -1, 1, -1],
  [-1, -1, 1, -1, 1, 1, 1, -1, -1, -1, 1],
  [1, -1, -1, 1, -1, 1, 1, 1, -1, -1, -1],
  [-1, 1, -1, -1, 1, -1, 1, 1, 1, -1, -1],
  [-1, -1, 1, -1, -1, 1, -1, 1, 1, 1, -1],
  [-1, -1, -1, 1, -1, -1, 1, -1, 1, 1, 1],
  [1, -1, -1, -1, 1, -1, -1, 1, -1, 1, 1],
  [1, 1, -1, -1, -1, 1, -1, -1, 1, -1, 1],
  [1, 1, 1, -1, -1, -1, 1, -1, -1, 1, -1],
  [-1, 1, 1, 1, -1, -1, -1, 1, -1, -1, 1],
  [1, -1, 1, 1, 1, -1, -1, -1, 1, -1, -1]
]

export function createPlackettBurman(factors: readonly FactorDef[], seed = 20261023): Result<DoeDesign> {
  if (factors.length < 2 || factors.length > 11) return err('INVALID_PARAMETER', 'Plackett-Burman admite entre 2 y 11 factores.')
  const runs: DesignRun[] = PB12.map((row, index) => {
    const coded = row.slice(0, factors.length)
    return { standardOrder: index + 1, runOrder: index + 1, coded, actual: applyFactors(factors, coded) }
  })
  return ok({ type: 'plackett-burman', factors: [...factors], runs: randomize(runs, seed), replicateCount: 1, centerPoints: 0, seed })
}

export function createCcd(
  factors: readonly FactorDef[],
  options: { centerPoints?: number; seed?: number } = {}
): Result<DoeDesign> {
  const k = factors.length
  if (k < 2 || k > 5) return err('INVALID_PARAMETER', 'El diseño compuesto central admite entre 2 y 5 factores.')
  const alpha = Math.pow(1 << k, 1 / 4)
  const runs: DesignRun[] = []
  const total = 1 << k
  for (let index = 0; index < total; index += 1) {
    const coded = Array.from({ length: k }, (_, bit) => ((index >> (k - 1 - bit)) & 1 ? 1 : -1))
    runs.push({ standardOrder: runs.length + 1, runOrder: runs.length + 1, coded, actual: applyFactors(factors, coded) })
  }
  for (let axis = 0; axis < k; axis += 1) {
    for (const sign of [-1, 1]) {
      const coded = new Array<number>(k).fill(0)
      coded[axis] = sign * alpha
      runs.push({ standardOrder: runs.length + 1, runOrder: runs.length + 1, coded, actual: applyFactors(factors, coded) })
    }
  }
  const centerPoints = options.centerPoints ?? 3
  for (let i = 0; i < centerPoints; i += 1) {
    const coded = new Array<number>(k).fill(0)
    runs.push({ standardOrder: runs.length + 1, runOrder: runs.length + 1, coded, actual: applyFactors(factors, coded) })
  }
  return ok({
    type: 'ccd',
    factors: [...factors],
    runs: randomize(runs, options.seed ?? 20261023),
    replicateCount: 1,
    centerPoints,
    seed: options.seed ?? 20261023
  })
}

export interface EffectsAnalysis {
  readonly terms: string[]
  readonly coefficients: number[]
  readonly standardErrors: number[]
  readonly t: number[]
  readonly pValues: number[]
  readonly effects: number[]
  readonly rSquared: number
  readonly fitted: number[]
  readonly residuals: number[]
}

/** Análisis de un diseño factorial 2^k por OLS sobre variables codificadas. */
export function analyzeFactorial(
  design: DoeDesign,
  response: readonly number[]
): Result<EffectsAnalysis> {
  if (response.length !== design.runs.length) return err('INVALID_INPUT', 'La respuesta debe tener una fila por corrida.')
  const k = design.factors.length
  const terms: string[] = []
  const predictors: number[][] = []
  for (let i = 0; i < k; i += 1) {
    terms.push(design.factors[i]?.name ?? `Factor ${i + 1}`)
    predictors.push(design.runs.map((run) => run.coded[i] as number))
  }
  for (let i = 0; i < k - 1; i += 1) {
    for (let j = i + 1; j < k; j += 1) {
      terms.push(`${design.factors[i]?.name}*${design.factors[j]?.name}`)
      predictors.push(design.runs.map((run) => (run.coded[i] as number) * (run.coded[j] as number)))
    }
  }
  const designMatrix: Matrix = design.runs.map((run) => predictors.map((column) => column[run.standardOrder - 1] as number))
  const fit = leastSquares(designMatrix, [...response])
  if (!fit.ok) return fit
  const n = response.length
  const p = terms.length + 1
  const xt = design.runs.map((run) => [1, ...predictors.map((column) => column[run.standardOrder - 1] as number)])
  const xtx: Matrix = Array.from({ length: p }, (_, i) =>
    Array.from({ length: p }, (_, j) => xt.reduce((acc, row) => acc + (row[i] as number) * (row[j] as number), 0))
  )
  const xtxInv = invert(xtx)
  const mse = fit.value.standardError * fit.value.standardError
  const dfError = Math.max(1, n - p)
  const coefficients = fit.value.coefficients
  const standardErrors = coefficients.map((_, index) => Math.sqrt(mse * (xtxInv?.[index]?.[index] ?? Number.NaN)))
  const t = coefficients.map((value, index) => value / (standardErrors[index] as number))
  const pValues = t.map((value) => {
    const cdf = tCdfLocal(value, dfError)
    return 2 * Math.min(cdf, 1 - cdf)
  })
  return ok({
    terms: ['Constante', ...terms],
    coefficients,
    standardErrors,
    t,
    pValues,
    effects: coefficients.slice(1).map((value) => value * 2),
    rSquared: fit.value.rSquared,
    fitted: fit.value.fitted,
    residuals: fit.value.residuals
  })
}

function invert(matrix: Matrix): Matrix | null {
  const n = matrix.length
  const augmented = matrix.map((row, i) => [...row, ...Array.from({ length: n }, (_, j) => (i === j ? 1 : 0))])
  for (let col = 0; col < n; col += 1) {
    let pivot = col
    for (let row = col + 1; row < n; row += 1) {
      if (Math.abs((augmented[row]?.[col] as number) ?? 0) > Math.abs((augmented[pivot]?.[col] as number) ?? 0)) pivot = row
    }
    const pivotValue = augmented[pivot]?.[col] as number
    if (Math.abs(pivotValue) < 1e-12) return null
    if (pivot !== col) {
      const tmp = augmented[pivot] as number[]
      augmented[pivot] = augmented[col] as number[]
      augmented[col] = tmp
    }
    for (let j = 0; j < 2 * n; j += 1) (augmented[col] as number[])[j] = (augmented[col] as number[])[j]! / pivotValue
    for (let row = 0; row < n; row += 1) {
      if (row === col) continue
      const factor = augmented[row]?.[col] as number
      if (factor === 0) continue
      for (let j = 0; j < 2 * n; j += 1) (augmented[row] as number[])[j] = (augmented[row] as number[])[j]! - factor * (augmented[col] as number[])[j]!
    }
  }
  return augmented.map((row) => row.slice(n))
}

function tCdfLocal(x: number, df: number): number {
  if (df <= 0) return Number.NaN
  if (df > 300) return 0.5 * (1 + erfLocal(x / Math.SQRT2))
  const tail = 0.5 * betaiLocal(df / 2, 0.5, df / (df + x * x))
  return x >= 0 ? 1 - tail : tail
}

function betaiLocal(a: number, b: number, x: number): number {
  if (x <= 0) return 0
  if (x >= 1) return 1
  const logBeta = lgamma(a + b) - lgamma(a) - lgamma(b) + a * Math.log(x) + b * Math.log(1 - x)
  const front = Math.exp(logBeta)
  if (x < (a + 1) / (a + b + 2)) return (front * cf(a, b, x)) / a
  return 1 - (front * cf(b, a, 1 - x)) / b
}

function cf(a: number, b: number, x: number): number {
  const qab = a + b
  const qap = a + 1
  const qam = a - 1
  let c = 1
  let d = 1 - (qab * x) / qap
  if (Math.abs(d) < 1e-300) d = 1e-300
  d = 1 / d
  let h = d
  for (let m = 1; m < 300; m += 1) {
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

function lgamma(x: number): number {
  const coef = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7]
  if (x < 0.5) return Math.log(Math.PI / Math.sin(Math.PI * x)) - lgamma(1 - x)
  const z = x - 1
  let a = coef[0] as number
  const t = z + 7.5
  for (let i = 1; i < coef.length; i += 1) a += (coef[i] as number) / (z + i)
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(a)
}

function erfLocal(x: number): number {
  const sign = x < 0 ? -1 : 1
  const ax = Math.abs(x)
  let term = ax
  let total = ax
  for (let n = 1; n < 60; n += 1) {
    term *= (-ax * ax) / n
    total += term / (2 * n + 1)
    if (Math.abs(term) < 1e-17) break
  }
  return (sign * 2 * total) / Math.sqrt(Math.PI)
}

export function designSummary(design: DoeDesign): { runs: number; factors: number; type: string; centerPoints: number } {
  return { runs: design.runs.length, factors: design.factors.length, type: design.type, centerPoints: design.centerPoints }
}

export { mean }
