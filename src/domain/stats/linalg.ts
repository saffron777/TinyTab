/**
 * Álgebra lineal numérica del dominio (implementación propia).
 * Matrices como arreglos de filas. Los errores se comunican con `Result`.
 */
import { err, ok, type Result } from '../model/result'

export type Matrix = number[][]

export type Vector = number[]

export function zeros(rows: number, cols: number): Matrix {
  return Array.from({ length: rows }, () => Array.from({ length: cols }, () => 0))
}

export function identity(n: number): Matrix {
  return Array.from({ length: n }, (_, i) => Array.from({ length: n }, (_, j) => (i === j ? 1 : 0)))
}

export function transpose(a: Matrix): Matrix {
  const rows = a.length
  const cols = a[0]?.length ?? 0
  return Array.from({ length: cols }, (_, j) => Array.from({ length: rows }, (_, i) => a[i]?.[j] ?? 0))
}

export function multiply(a: Matrix, b: Matrix): Matrix {
  const n = a.length
  const m = b[0]?.length ?? 0
  const inner = b.length
  const output = zeros(n, m)
  for (let i = 0; i < n; i += 1) {
    const rowA = a[i] as number[]
    const rowOut = output[i] as number[]
    for (let k = 0; k < inner; k += 1) {
      const aik = rowA[k] as number
      if (aik === 0) continue
      const rowB = b[k] as number[]
      for (let j = 0; j < m; j += 1) rowOut[j] = (rowOut[j] as number) + aik * (rowB[j] as number)
    }
  }
  return output
}

export function matVec(a: Matrix, v: Vector): Vector {
  return a.map((row) => row.reduce((acc, value, j) => acc + value * (v[j] as number), 0))
}

/** Resolución de A x = b por eliminación gaussiana con pivoteo parcial. */
export function solveLinear(a: Matrix, b: Vector): Result<Vector> {
  const n = a.length
  if (n === 0 || a.some((row) => row.length !== n) || b.length !== n) {
    return err('INVALID_INPUT', 'La matriz debe ser cuadrada y coincidir con el vector.')
  }
  const m = a.map((row) => row.slice())
  const x = b.slice()
  for (let col = 0; col < n; col += 1) {
    let pivot = col
    for (let row = col + 1; row < n; row += 1) {
      if (Math.abs((m[row]?.[col] as number) ?? 0) > Math.abs((m[pivot]?.[col] as number) ?? 0)) pivot = row
    }
    const pivotValue = m[pivot]?.[col] as number
    if (Math.abs(pivotValue) < 1e-14) return err('SINGULAR_MATRIX', 'La matriz es singular o casi singular.')
    if (pivot !== col) {
      const tmp = m[pivot] as number[]
      m[pivot] = m[col] as number[]
      m[col] = tmp
      const tmpX = x[pivot] as number
      x[pivot] = x[col] as number
      x[col] = tmpX
    }
    const rowCol = m[col] as number[]
    for (let row = col + 1; row < n; row += 1) {
      const factor = (m[row]?.[col] as number) / (rowCol[col] as number)
      if (factor === 0) continue
      const target = m[row] as number[]
      for (let j = col; j < n; j += 1) target[j] = (target[j] as number) - factor * (rowCol[j] as number)
      x[row] = (x[row] as number) - factor * (x[col] as number)
    }
  }
  const solution = Array.from({ length: n }, () => 0)
  for (let row = n - 1; row >= 0; row -= 1) {
    let acc = x[row] as number
    const rowValues = m[row] as number[]
    for (let j = row + 1; j < n; j += 1) acc -= (rowValues[j] as number) * (solution[j] as number)
    solution[row] = acc / (rowValues[row] as number)
    if (!Number.isFinite(solution[row])) return err('SINGULAR_MATRIX', 'La matriz es singular.')
  }
  return ok(solution)
}

export function inverse(a: Matrix): Result<Matrix> {
  const n = a.length
  const result = zeros(n, n)
  for (let col = 0; col < n; col += 1) {
    const unit = Array.from({ length: n }, (_, i) => (i === col ? 1 : 0))
    const solved = solveLinear(a, unit)
    if (!solved.ok) return solved
    for (let row = 0; row < n; row += 1) (result[row] as number[])[col] = solved.value[row] as number
  }
  return ok(result)
}

export function determinant(a: Matrix): number {
  const n = a.length
  const m = a.map((row) => row.slice())
  let det = 1
  for (let col = 0; col < n; col += 1) {
    let pivot = col
    for (let row = col + 1; row < n; row += 1) {
      if (Math.abs((m[row]?.[col] as number) ?? 0) > Math.abs((m[pivot]?.[col] as number) ?? 0)) pivot = row
    }
    const pivotValue = m[pivot]?.[col] as number
    if (Math.abs(pivotValue) < 1e-14) return 0
    if (pivot !== col) {
      const tmp = m[pivot] as number[]
      m[pivot] = m[col] as number[]
      m[col] = tmp
      det = -det
    }
    det *= pivotValue
    const rowCol = m[col] as number[]
    for (let row = col + 1; row < n; row += 1) {
      const factor = (m[row]?.[col] as number) / pivotValue
      const target = m[row] as number[]
      for (let j = col; j < n; j += 1) target[j] = (target[j] as number) - factor * (rowCol[j] as number)
    }
  }
  return det
}

export interface LeastSquaresResult {
  readonly coefficients: number[]
  readonly fitted: number[]
  readonly residuals: number[]
  readonly rSquared: number
  readonly adjustedRSquared: number
  readonly standardError: number
  readonly degreesOfFreedom: number
}

/**
 * Mínimos cuadrados por QR con transformaciones de Householder.
 * X es n×p (sin columna de unos: se añade automáticamente).
 */
export function leastSquares(x: Matrix, y: Vector): Result<LeastSquaresResult> {
  const n = x.length
  const p = (x[0]?.length ?? 0) + 1
  if (n < p) return err('INSUFFICIENT_DATA', 'Se necesitan más observaciones que predictores.')
  const design = x.map((row) => [1, ...row])
  const r = design.map((row) => row.slice())
  const qtY = y.slice()
  for (let k = 0; k < p; k += 1) {
    let norm = 0
    for (let i = k; i < n; i += 1) norm += Math.pow((r[i]?.[k] as number) ?? 0, 2)
    norm = Math.sqrt(norm)
    if (norm < 1e-14) return err('SINGULAR_MATRIX', 'Hay colinealidad perfecta entre las variables.')
    const alpha = ((r[k]?.[k] as number) ?? 0) > 0 ? -norm : norm
    const v = Array.from({ length: n }, (_, i) => (i < k ? 0 : (r[i]?.[k] as number) ?? 0))
    v[k] = (v[k] as number) - alpha
    let vNorm = 0
    for (let i = k; i < n; i += 1) vNorm += (v[i] as number) ** 2
    vNorm = Math.sqrt(vNorm)
    if (vNorm < 1e-14) continue
    for (let i = k; i < n; i += 1) v[i] = (v[i] as number) / vNorm
    for (let j = k; j < p; j += 1) {
      let dot = 0
      for (let i = k; i < n; i += 1) dot += (v[i] as number) * ((r[i]?.[j] as number) ?? 0)
      for (let i = k; i < n; i += 1) {
        const row = r[i] as number[]
        row[j] = (row[j] as number) - 2 * dot * (v[i] as number)
      }
    }
    let dotY = 0
    for (let i = k; i < n; i += 1) dotY += (v[i] as number) * (qtY[i] as number)
    for (let i = k; i < n; i += 1) qtY[i] = (qtY[i] as number) - 2 * dotY * (v[i] as number)
  }
  const coefficients = Array.from({ length: p }, () => 0)
  for (let i = p - 1; i >= 0; i -= 1) {
    let acc = qtY[i] as number
    for (let j = i + 1; j < p; j += 1) acc -= (r[i]?.[j] as number) * (coefficients[j] as number)
    const diagonal = r[i]?.[i] as number
    if (Math.abs(diagonal) < 1e-14) return err('SINGULAR_MATRIX', 'El sistema es singular.')
    coefficients[i] = acc / diagonal
  }
  const fitted = design.map((row) => row.reduce((acc, value, j) => acc + value * (coefficients[j] as number), 0))
  const residuals = y.map((value, i) => value - (fitted[i] as number))
  const meanY = y.reduce((a, b) => a + b, 0) / n
  const sst = y.reduce((acc, value) => acc + (value - meanY) ** 2, 0)
  const sse = residuals.reduce((acc, value) => acc + value * value, 0)
  const df = n - p
  const mse = df > 0 ? sse / df : Number.NaN
  const rSquared = sst > 0 ? 1 - sse / sst : 1
  return ok({
    coefficients,
    fitted,
    residuals,
    rSquared,
    adjustedRSquared: 1 - (1 - rSquared) * ((n - 1) / Math.max(1, df)),
    standardError: Math.sqrt(mse),
    degreesOfFreedom: df
  })
}

/** Eigenvalores/vectores de una matriz simétrica por rotaciones de Jacobi. */
export function eigenSymmetric(a: Matrix, maxSweeps = 100): { values: number[]; vectors: Matrix } {
  const n = a.length
  const m = a.map((row) => row.slice())
  const v = identity(n)
  for (let sweep = 0; sweep < maxSweeps; sweep += 1) {
    let off = 0
    for (let i = 0; i < n; i += 1) for (let j = i + 1; j < n; j += 1) off += Math.pow((m[i]?.[j] as number) ?? 0, 2)
    if (off < 1e-24) break
    for (let p = 0; p < n - 1; p += 1) {
      for (let q = p + 1; q < n; q += 1) {
        const apq = (m[p]?.[q] as number) ?? 0
        if (Math.abs(apq) < 1e-18) continue
        const app = (m[p]?.[p] as number) ?? 0
        const aqq = (m[q]?.[q] as number) ?? 0
        const theta = (aqq - app) / (2 * apq)
        const t = Math.sign(theta || 1) / (Math.abs(theta) + Math.sqrt(theta * theta + 1))
        const c = 1 / Math.sqrt(t * t + 1)
        const s = t * c
        for (let k = 0; k < n; k += 1) {
          const mkp = (m[k]?.[p] as number) ?? 0
          const mkq = (m[k]?.[q] as number) ?? 0
          ;(m[k] as number[])[p] = c * mkp - s * mkq
          ;(m[k] as number[])[q] = s * mkp + c * mkq
        }
        for (let k = 0; k < n; k += 1) {
          const mpk = (m[p]?.[k] as number) ?? 0
          const mqk = (m[q]?.[k] as number) ?? 0
          ;(m[p] as number[])[k] = c * mpk - s * mqk
          ;(m[q] as number[])[k] = s * mpk + c * mqk
        }
        for (let k = 0; k < n; k += 1) {
          const vkp = (v[k]?.[p] as number) ?? 0
          const vkq = (v[k]?.[q] as number) ?? 0
          ;(v[k] as number[])[p] = c * vkp - s * vkq
          ;(v[k] as number[])[q] = s * vkp + c * vkq
        }
      }
    }
  }
  const values = Array.from({ length: n }, (_, i) => (m[i]?.[i] as number) ?? 0)
  return { values, vectors: v }
}

/** Número de condición aproximado usando la diagonal de R si está disponible. */
export function conditionEstimate(values: readonly number[]): number {
  const abs = values.map((value) => Math.abs(value)).filter((value) => value > 0)
  if (abs.length === 0) return Number.POSITIVE_INFINITY
  const max = Math.max(...abs)
  const min = Math.min(...abs)
  return max / min
}
