/**
 * Regresión: mínimos cuadrados (QR), selección de variables, regresión
 * logística binaria y Poisson por IRLS.
 */
import { correlation, mean, normalCdf, normalInv, normalPdf } from './numeric'
import { leastSquares, type Matrix } from './linalg'
import { tCdf } from './tests'
import { err, ok, type Result } from '../model/result'

export interface Coefficient {
  readonly term: string
  readonly estimate: number
  readonly standardError: number
  readonly t: number
  readonly pValue: number
}

export interface RegressionResult {
  readonly terms: string[]
  readonly coefficients: Coefficient[]
  readonly rSquared: number
  readonly adjustedRSquared: number
  readonly standardError: number
  readonly observations: number
  readonly dfError: number
  readonly ssRegression: number
  readonly ssError: number
  readonly ssTotal: number
  readonly f: number
  readonly pValue: number
  readonly residuals: number[]
  readonly fitted: number[]
  readonly vif: number[]
  readonly durbinWatson: number
}

export interface RegressionInput {
  readonly response: readonly number[]
  readonly predictors: readonly (readonly number[])[]
  readonly names: readonly string[]
}

function buildDesign(predictors: readonly (readonly number[])[], rows: number): Matrix {
  return Array.from({ length: rows }, (_, i) => predictors.map((column) => column[i] as number))
}

export function multipleRegression(input: RegressionInput): Result<RegressionResult> {
  const rows = input.response.length
  if (input.predictors.length === 0) return err('INVALID_INPUT', 'Se necesita al menos un predictor.')
  if (rows < input.predictors.length + 2) return err('INSUFFICIENT_DATA', 'Pocas observaciones para el número de predictores.')
  const design = buildDesign(input.predictors, rows)
  const fit = leastSquares(design, [...input.response])
  if (!fit.ok) return fit
  const p = input.predictors.length + 1
  const n = rows
  const dfError = n - p
  const mse = fit.value.standardError * fit.value.standardError
  const xt = design.map((row) => [1, ...row])
  const xtx: Matrix = Array.from({ length: p }, (_, i) =>
    Array.from({ length: p }, (_, j) => xt.reduce((acc, row) => acc + (row[i] as number) * (row[j] as number), 0))
  )
  const xtxInv = invertSmall(xtx)
  if (!xtxInv) return err('SINGULAR_MATRIX', 'Predictores colineales: no se puede estimar el modelo.')
  const terms = ['Constante', ...input.names]
  const coefficients: Coefficient[] = fit.value.coefficients.map((estimate, index) => {
    const variance = mse * (xtxInv[index]?.[index] ?? Number.NaN)
    const se = Math.sqrt(variance)
    const t = estimate / se
    return { term: terms[index] ?? `b${index}`, estimate, standardError: se, t, pValue: 2 * Math.min(tCdf(t, dfError), 1 - tCdf(t, dfError)) }
  })
  const sst = input.response.reduce((acc, value) => acc + Math.pow(value - mean(input.response), 2), 0)
  const sse = fit.value.residuals.reduce((acc, value) => acc + value * value, 0)
  const ssr = sst - sse
  const vif = input.predictors.map((column, index) => {
    if (input.predictors.length === 1) return 1
    const others = input.predictors.filter((_, j) => j !== index)
    const sub = buildDesign(others, rows)
    const subFit = leastSquares(sub, [...column])
    if (!subFit.ok) return Number.NaN
    return 1 / Math.max(1e-12, 1 - subFit.value.rSquared)
  })
  const dw = durbinWatson(fit.value.residuals)
  return ok({
    terms,
    coefficients,
    rSquared: fit.value.rSquared,
    adjustedRSquared: fit.value.adjustedRSquared,
    standardError: fit.value.standardError,
    observations: n,
    dfError,
    ssRegression: ssr,
    ssError: sse,
    ssTotal: sst,
    f: ssr / (p - 1) / mse,
    pValue: fUpper(ssr / (p - 1) / mse, p - 1, dfError),
    residuals: fit.value.residuals,
    fitted: fit.value.fitted,
    vif,
    durbinWatson: dw
  })
}

function invertSmall(matrix: Matrix): Matrix | null {
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
    const rowCol = augmented[col] as number[]
    for (let j = 0; j < 2 * n; j += 1) rowCol[j] = (rowCol[j] as number) / pivotValue
    for (let row = 0; row < n; row += 1) {
      if (row === col) continue
      const factor = augmented[row]?.[col] as number
      if (factor === 0) continue
      const target = augmented[row] as number[]
      for (let j = 0; j < 2 * n; j += 1) target[j] = (target[j] as number) - factor * (rowCol[j] as number)
    }
  }
  return augmented.map((row) => row.slice(n))
}

export function durbinWatson(residuals: readonly number[]): number {
  if (residuals.length < 2) return Number.NaN
  let numerator = 0
  let denominator = 0
  for (let i = 0; i < residuals.length; i += 1) {
    denominator += Math.pow(residuals[i] as number, 2)
    if (i > 0) numerator += Math.pow((residuals[i] as number) - (residuals[i - 1] as number), 2)
  }
  return denominator > 0 ? numerator / denominator : Number.NaN
}

export interface SubsetModel {
  readonly terms: number[]
  readonly names: string[]
  readonly rSquared: number
  readonly adjustedRSquared: number
  readonly aic: number
  readonly mallowCp: number
}

/** Mejores subconjuntos exhaustivos (limitado a 14 predictores). */
export function bestSubsets(input: RegressionInput, maxPredictors = 4): Result<SubsetModel[]> {
  const p = input.predictors.length
  if (p > 14) return err('NOT_SUPPORTED', 'Demasiados predictores para búsqueda exhaustiva (máximo 14).')
  const candidates: SubsetModel[] = []
  const totalMse = (() => {
    const fit = leastSquares(buildDesign(input.predictors, input.response.length), [...input.response])
    return fit.ok ? fit.value.standardError * fit.value.standardError : Number.NaN
  })()
  const n = input.response.length
  for (let mask = 1; mask < 1 << p; mask += 1) {
    const terms = Array.from({ length: p }, (_, i) => i).filter((i) => (mask & (1 << i)) !== 0)
    if (terms.length > maxPredictors) continue
    const predictors = terms.map((i) => input.predictors[i] as readonly number[])
    const names = terms.map((i) => input.names[i] ?? `X${i + 1}`)
    const fit = leastSquares(buildDesign(predictors, n), [...input.response])
    if (!fit.ok) continue
    const sse = fit.value.residuals.reduce((acc, value) => acc + value * value, 0)
    const rSquared = fit.value.rSquared
    const adjusted = fit.value.adjustedRSquared
    const aic = n * Math.log(sse / n) + 2 * (terms.length + 2)
    const mallow = (sse / totalMse) - (n - 2 * (terms.length + 1))
    candidates.push({ terms, names, rSquared, adjustedRSquared: adjusted, aic, mallowCp: mallow })
  }
  candidates.sort((a, b) => b.adjustedRSquared - a.adjustedRSquared)
  return ok(candidates.slice(0, 20))
}

export interface StepwiseResult {
  readonly selected: string[]
  readonly steps: { step: number; action: string; term: string; rSquared: number; aic: number }[]
  readonly model: RegressionResult
}

/** Stepwise por AIC (forward/backward) sobre un conjunto de predictores. */
export function stepwiseRegression(input: RegressionInput, direction: 'forward' | 'backward' = 'forward'): Result<StepwiseResult> {
  const p = input.predictors.length
  if (p === 0) return err('INVALID_INPUT', 'Sin predictores.')
  const n = input.response.length
  const fitFor = (indices: number[]): { rSquared: number; aic: number } | null => {
    if (indices.length === 0) {
      const sst = input.response.reduce((acc, value) => acc + Math.pow(value - mean(input.response), 2), 0)
      return { rSquared: 0, aic: n * Math.log(sst / n) + 2 * 2 }
    }
    const fit = leastSquares(buildDesign(indices.map((i) => input.predictors[i] as readonly number[]), n), [...input.response])
    if (!fit.ok) return null
    const sse = fit.value.residuals.reduce((acc, value) => acc + value * value, 0)
    return { rSquared: fit.value.rSquared, aic: Math.max(1e-300, n * Math.log(Math.max(sse / n, 1e-300)) + 2 * (indices.length + 2)) }
  }
  const steps: StepwiseResult['steps'] = []
  let selected: number[] = []
  if (direction === 'forward') {
    let current = fitFor(selected)
    for (let step = 1; step <= p; step += 1) {
      let bestIndex = -1
      let best = current
      for (let i = 0; i < p; i += 1) {
        if (selected.includes(i)) continue
        const candidate = fitFor([...selected, i])
        if (candidate && best && candidate.aic < best.aic - 1e-9) {
          best = candidate
          bestIndex = i
        }
      }
      if (bestIndex < 0 || !best) break
      selected = [...selected, bestIndex]
      current = best
      steps.push({ step, action: 'Añadido', term: input.names[bestIndex] ?? `X${bestIndex + 1}`, rSquared: best.rSquared, aic: best.aic })
    }
  } else {
    selected = Array.from({ length: p }, (_, i) => i)
    let current = fitFor(selected)
    for (let step = 1; step <= p; step += 1) {
      let worstIndex = -1
      let best = current
      for (const index of selected) {
        const candidate = fitFor(selected.filter((i) => i !== index))
        if (candidate && best && candidate.aic < best.aic - 1e-9) {
          best = candidate
          worstIndex = index
        }
      }
      if (worstIndex < 0 || !best) break
      selected = selected.filter((i) => i !== worstIndex)
      current = best
      steps.push({ step, action: 'Eliminado', term: input.names[worstIndex] ?? `X${worstIndex + 1}`, rSquared: best.rSquared, aic: best.aic })
    }
  }
  if (selected.length === 0) return err('NON_CONVERGENCE', 'El stepwise no retuvo predictores.')
  const model = multipleRegression({
    response: input.response,
    predictors: selected.map((i) => input.predictors[i] as readonly number[]),
    names: selected.map((i) => input.names[i] ?? `X${i + 1}`)
  })
  if (!model.ok) return model
  return ok({ selected: selected.map((i) => input.names[i] ?? `X${i + 1}`), steps, model: model.value })
}

export interface LogisticResult {
  readonly terms: string[]
  readonly coefficients: number[]
  readonly standardErrors: number[]
  readonly z: number[]
  readonly pValues: number[]
  readonly deviance: number
  readonly nullDeviance: number
  readonly aic: number
  readonly iterations: number
  readonly predicted: number[]
}

function logisticFit(x: Matrix, y: readonly number[], maxIterations = 50): Result<LogisticResult> {
  const n = y.length
  const p = (x[0]?.length ?? 0) + 1
  const beta = new Array<number>(p).fill(0)
  const design = x.map((row) => [1, ...row])
  const eta = new Array<number>(n).fill(0)
  const mu = new Array<number>(n).fill(0.5)
  let iterations = 0
  let converged = false
  for (; iterations < maxIterations; iterations += 1) {
    for (let i = 0; i < n; i += 1) {
      eta[i] = design[i]!.reduce((acc, value, j) => acc + value * (beta[j] as number), 0)
      mu[i] = 1 / (1 + Math.exp(-(eta[i] as number)))
    }
    const info: Matrix = Array.from({ length: p }, () => new Array<number>(p).fill(0))
    const score = new Array<number>(p).fill(0)
    for (let i = 0; i < n; i += 1) {
      const weight = (mu[i] as number) * (1 - (mu[i] as number))
      const row = design[i] as number[]
      for (let a = 0; a < p; a += 1) {
        score[a] = (score[a] as number) + (row[a] as number) * ((y[i] as number) - (mu[i] as number))
        for (let b = 0; b < p; b += 1) {
          const infoRow = info[a] as number[]
          infoRow[b] = (infoRow[b] as number) + weight * (row[a] as number) * (row[b] as number)
        }
      }
    }
    const inverse = invertSmall(info)
    if (!inverse) return err('SINGULAR_MATRIX', 'Matriz de información singular en la regresión logística.')
    let maxStep = 0
    for (let a = 0; a < p; a += 1) {
      const delta = inverse[a]!.reduce((acc, value, b) => acc + value * (score[b] as number), 0)
      beta[a] = (beta[a] as number) + delta
      maxStep = Math.max(maxStep, Math.abs(delta))
    }
    if (maxStep < 1e-8) {
      converged = true
      break
    }
  }
  if (!converged && iterations >= maxIterations) return err('NON_CONVERGENCE', 'La regresión logística no convergió.')
  for (let i = 0; i < n; i += 1) mu[i] = 1 / (1 + Math.exp(-design[i]!.reduce((acc, value, j) => acc + value * (beta[j] as number), 0)))
  const info: Matrix = Array.from({ length: p }, () => new Array<number>(p).fill(0))
  for (let i = 0; i < n; i += 1) {
    const weight = (mu[i] as number) * (1 - (mu[i] as number))
    const row = design[i] as number[]
    for (let a = 0; a < p; a += 1) for (let b = 0; b < p; b += 1) (info[a] as number[])[b] = (info[a] as number[])[b]! + weight * (row[a] as number) * (row[b] as number)
  }
  const inverse = invertSmall(info)
  if (!inverse) return err('SINGULAR_MATRIX', 'Matriz de información singular.')
  let deviance = 0
  let nullDeviance = 0
  const prevalence = y.reduce((a, b) => a + b, 0) / n
  for (let i = 0; i < n; i += 1) {
    const probability = Math.min(1 - 1e-12, Math.max(1e-12, mu[i] as number))
    deviance += -2 * ((y[i] as number) * Math.log(probability) + (1 - (y[i] as number)) * Math.log(1 - probability))
    nullDeviance += -2 * ((y[i] as number) * Math.log(prevalence) + (1 - (y[i] as number)) * Math.log(1 - prevalence))
  }
  const standardErrors = beta.map((_, index) => Math.sqrt(inverse[index]?.[index] ?? Number.NaN))
  return ok({
    terms: ['Constante'],
    coefficients: beta,
    standardErrors,
    z: beta.map((value, index) => value / (standardErrors[index] as number)),
    pValues: beta.map((value, index) => {
      const statistic = value / (standardErrors[index] as number)
      return 2 * Math.min(normalCdf(statistic), 1 - normalCdf(statistic))
    }),
    deviance,
    nullDeviance,
    aic: deviance + 2 * p,
    iterations,
    predicted: mu.map((value) => value)
  })
}

export function logisticRegression(
  response: readonly number[],
  predictors: readonly (readonly number[])[],
  names: readonly string[]
): Result<LogisticResult> {
  if (response.some((value) => value !== 0 && value !== 1)) return err('INVALID_INPUT', 'La respuesta debe ser 0/1.')
  if (predictors.length === 0) return err('INVALID_INPUT', 'Se necesita al menos un predictor.')
  const fit = logisticFit(buildDesign(predictors, response.length), response)
  if (!fit.ok) return fit
  return ok({ ...fit.value, terms: ['Constante', ...names] })
}

export interface PoissonResult {
  readonly terms: string[]
  readonly coefficients: number[]
  readonly standardErrors: number[]
  readonly z: number[]
  readonly pValues: number[]
  readonly deviance: number
  readonly aic: number
  readonly iterations: number
}

export function poissonRegression(
  response: readonly number[],
  predictors: readonly (readonly number[])[],
  names: readonly string[]
): Result<PoissonResult> {
  if (response.some((value) => value < 0 || !Number.isInteger(value))) return err('INVALID_INPUT', 'La respuesta Poisson debe ser un conteo.')
  const n = response.length
  const p = predictors.length + 1
  const design = buildDesign(predictors, n).map((row) => [1, ...row])
  const beta = new Array<number>(p).fill(0)
  let iterations = 0
  let converged = false
  for (; iterations < 100; iterations += 1) {
    const mu = design.map((row) => Math.exp(Math.min(50, row.reduce((acc, value, j) => acc + value * (beta[j] as number), 0))))
    const info: Matrix = Array.from({ length: p }, () => new Array<number>(p).fill(0))
    const score = new Array<number>(p).fill(0)
    for (let i = 0; i < n; i += 1) {
      const row = design[i] as number[]
      for (let a = 0; a < p; a += 1) {
        score[a] = (score[a] as number) + (row[a] as number) * ((response[i] as number) - (mu[i] as number))
        for (let b = 0; b < p; b += 1) (info[a] as number[])[b] = (info[a] as number[])[b]! + (mu[i] as number) * (row[a] as number) * (row[b] as number)
      }
    }
    const inverse = invertSmall(info)
    if (!inverse) return err('SINGULAR_MATRIX', 'Matriz de información singular en la regresión Poisson.')
    let maxStep = 0
    for (let a = 0; a < p; a += 1) {
      const delta = inverse[a]!.reduce((acc, value, b) => acc + value * (score[b] as number), 0)
      beta[a] = (beta[a] as number) + delta
      maxStep = Math.max(maxStep, Math.abs(delta))
    }
    if (maxStep < 1e-8) {
      converged = true
      break
    }
  }
  if (!converged) return err('NON_CONVERGENCE', 'La regresión Poisson no convergió.')
  const mu = design.map((row) => Math.exp(Math.min(50, row.reduce((acc, value, j) => acc + value * (beta[j] as number), 0))))
  const info: Matrix = Array.from({ length: p }, () => new Array<number>(p).fill(0))
  for (let i = 0; i < n; i += 1) {
    const row = design[i] as number[]
    for (let a = 0; a < p; a += 1) for (let b = 0; b < p; b += 1) (info[a] as number[])[b] = (info[a] as number[])[b]! + (mu[i] as number) * (row[a] as number) * (row[b] as number)
  }
  const inverse = invertSmall(info)
  if (!inverse) return err('SINGULAR_MATRIX', 'Matriz de información singular.')
  let deviance = 0
  for (let i = 0; i < n; i += 1) {
    const observed = response[i] as number
    const expected = Math.max(mu[i] as number, 1e-12)
    deviance += observed > 0 ? 2 * (observed * Math.log(observed / expected) - (observed - expected)) : 2 * expected
  }
  const standardErrors = beta.map((_, index) => Math.sqrt(inverse[index]?.[index] ?? Number.NaN))
  return ok({
    terms: ['Constante', ...names],
    coefficients: beta,
    standardErrors,
    z: beta.map((value, index) => value / (standardErrors[index] as number)),
    pValues: beta.map((value, index) => {
      const statistic = value / (standardErrors[index] as number)
      return 2 * Math.min(normalCdf(statistic), 1 - normalCdf(statistic))
    }),
    deviance,
    aic: deviance + 2 * p,
    iterations
  })
}

/** F superior para tablas ANOVA (evita dependencia circular). */
function fUpper(f: number, df1: number, df2: number): number {
  if (!(f >= 0)) return Number.NaN
  return 1 - betaCdf((df1 * f) / (df1 * f + df2), df1 / 2, df2 / 2)
}

function betaCdf(x: number, a: number, b: number): number {
  const logBeta = gammaLog(a + b) - gammaLog(a) - gammaLog(b) + a * Math.log(Math.max(x, 1e-300)) + b * Math.log(Math.max(1 - x, 1e-300))
  const front = Math.exp(logBeta)
  if (x < (a + 1) / (a + b + 2)) return (front * betaCf(a, b, x)) / a
  return 1 - (front * betaCf(b, a, 1 - x)) / b
}

function betaCf(a: number, b: number, x: number): number {
  const qab = a + b
  const qap = a + 1
  const qam = a - 1
  let c = 1
  let d = 1 - (qab * x) / qap
  if (Math.abs(d) < 1e-300) d = 1e-300
  d = 1 / d
  let h = d
  for (let m = 1; m < 2000; m += 1) {
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

function gammaLog(x: number): number {
  const coef = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7]
  if (x < 0.5) return Math.log(Math.PI / Math.sin(Math.PI * x)) - gammaLog(1 - x)
  const z = x - 1
  let a = coef[0] as number
  const t = z + 7.5
  for (let i = 1; i < coef.length; i += 1) a += (coef[i] as number) / (z + i)
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(a)
}

export { correlation, normalInv, normalPdf }
