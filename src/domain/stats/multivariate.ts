/** Análisis multivariante: PCA, análisis factorial, k-means y LDA. */
import { correlation, mean, sd } from './numeric'
import { eigenSymmetric, type Matrix } from './linalg'
import { Rng } from './random'
import { err, ok, type Result } from '../model/result'

export function standardize(rows: readonly (readonly number[])[]): Matrix {
  const cols = rows[0]?.length ?? 0
  const output = rows.map((row) => [...row])
  for (let j = 0; j < cols; j += 1) {
    const column = rows.map((row) => row[j] as number)
    const center = mean(column)
    const scale = sd(column) || 1
    for (let i = 0; i < output.length; i += 1) (output[i] as number[])[j] = ((rows[i]?.[j] as number) - center) / scale
  }
  return output
}

export interface PcaResult {
  readonly labels: string[]
  readonly eigenvalues: number[]
  readonly explained: number[]
  readonly cumulative: number[]
  readonly loadings: number[][]
  readonly scores: number[][]
  readonly n: number
}

export function pca(rows: readonly (readonly number[])[], labels: readonly string[]): Result<PcaResult> {
  const n = rows.length
  const p = rows[0]?.length ?? 0
  if (n < 3 || p < 2) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 3 filas y 2 variables.')
  const standardized = standardize(rows)
  const corr: Matrix = Array.from({ length: p }, (_, i) =>
    Array.from({ length: p }, (_, j) => {
      if (i === j) return 1
      const xi = standardized.map((row) => row[i] as number)
      const xj = standardized.map((row) => row[j] as number)
      return correlation(xi, xj)
    })
  )
  const { values, vectors } = eigenSymmetric(corr)
  const order = values.map((_, index) => index).sort((a, b) => (values[b] as number) - (values[a] as number))
  const eigenvalues = order.map((index) => values[index] as number)
  const total = eigenvalues.reduce((a, b) => a + b, 0) || 1
  const explained = eigenvalues.map((value) => (value / total) * 100)
  const cumulative: number[] = []
  let running = 0
  for (const value of explained) {
    running += value
    cumulative.push(running)
  }
  const loadings = order.map((index) => vectors.map((row) => (row[index] as number) * Math.sqrt(Math.max(0, values[index] as number))))
  const scores = standardized.map((row) =>
    order.map((index) => row.reduce((acc, value, j) => acc + value * (vectors[j]?.[index] as number), 0))
  )
  return ok({
    labels: [...labels],
    eigenvalues,
    explained,
    cumulative,
    loadings,
    scores,
    n
  })
}

/** Rotación varimax sobre una matriz de cargas (filas = variables, columnas = factores). */
export function varimax(loadings: readonly number[][]): number[][] {
  const rows = loadings.length
  const cols = loadings[0]?.length ?? 0
  const result = loadings.map((row) => [...row])
  for (let iteration = 0; iteration < 100; iteration += 1) {
    const h = result.map((row) => row.reduce((acc, value) => acc + value * value, 0))
    const b: number[][] = Array.from({ length: rows }, () => new Array<number>(cols).fill(0))
    for (let i = 0; i < rows; i += 1) {
      for (let j = 0; j < cols; j += 1) {
        const value = result[i]?.[j] as number
        ;(b[i] as number[])[j] = Math.pow(value / (h[i] as number || 1), 3)
      }
    }
    let changed = 0
    for (let a = 0; a < cols - 1; a += 1) {
      for (let c = a + 1; c < cols; c += 1) {
        let num = 0
        let den = 0
        for (let i = 0; i < rows; i += 1) {
          const ba = b[i]?.[a] as number
          const bc = b[i]?.[c] as number
          const ia = result[i]?.[a] as number
          const ic = result[i]?.[c] as number
          const diff = ia * ia - ic * ic
          num += 2 * ba * bc - diff * 2 * ia * ic
          den += ba * ba - bc * bc + diff
        }
        const angle = Math.atan2(num, den) / 4
        if (Math.abs(angle) < 1e-8) continue
        changed += Math.abs(angle)
        const cos = Math.cos(angle)
        const sin = Math.sin(angle)
        for (let i = 0; i < rows; i += 1) {
          const ia = result[i]?.[a] as number
          const ic = result[i]?.[c] as number
          ;(result[i] as number[])[a] = cos * ia - sin * ic
          ;(result[i] as number[])[c] = sin * ia + cos * ic
        }
      }
    }
    if (changed < 1e-8) break
  }
  return result
}

export interface FactorAnalysisResult {
  readonly labels: string[]
  readonly loadings: number[][]
  readonly communalities: number[]
  readonly varianceExplained: number[]
  readonly eigenvalues: number[]
}

export function factorAnalysis(
  rows: readonly (readonly number[])[],
  labels: readonly string[],
  factors = 2
): Result<FactorAnalysisResult> {
  const pcaResult = pca(rows, labels)
  if (!pcaResult.ok) return pcaResult
  if (factors < 1 || factors > Math.min(pcaResult.value.loadings[0]?.length ?? 1, 5)) {
    return err('INVALID_PARAMETER', 'Número de factores inválido.')
  }
  const loadings = pcaResult.value.loadings.map((row) => row.slice(0, factors))
  const rotated = varimax(loadings)
  const communalities = rotated.map((row) => row.reduce((acc, value) => acc + value * value, 0))
  const varianceExplained = Array.from({ length: factors }, (_, j) =>
    rotated.reduce((acc, row) => acc + Math.pow(row[j] as number, 2), 0) / rotated.length
  )
  return ok({
    labels: [...labels],
    loadings: rotated,
    communalities,
    varianceExplained,
    eigenvalues: pcaResult.value.eigenvalues.slice(0, factors)
  })
}

export interface ClusterResult {
  readonly k: number
  readonly labels: number[]
  readonly centers: number[][]
  readonly withinSS: number[]
  readonly totalWithinSS: number
}

export function kmeans(rows: readonly (readonly number[])[], k: number, seed = 20261023): Result<ClusterResult> {
  const n = rows.length
  const dims = rows[0]?.length ?? 0
  if (n < k || k < 2 || dims < 1) return err('INVALID_PARAMETER', 'Configuración de k-means inválida.')
  const standardized = standardize(rows)
  const rng = new Rng(seed)
  const centers: number[][] = []
  const first = Math.floor(rng.next() * n)
  centers.push([...(standardized[first] as number[])])
  while (centers.length < k) {
    const distances = standardized.map((row) => Math.min(...centers.map((center) => row.reduce((acc, value, j) => acc + Math.pow(value - (center[j] as number), 2), 0))))
    const total = distances.reduce((a, b) => a + b, 0)
    let target = rng.next() * total
    let index = 0
    for (let i = 0; i < distances.length; i += 1) {
      target -= distances[i] as number
      if (target <= 0) {
        index = i
        break
      }
    }
    centers.push([...(standardized[index] as number[])])
  }
  const labels = new Array<number>(n).fill(0)
  for (let iteration = 0; iteration < 100; iteration += 1) {
    let changed = false
    for (let i = 0; i < n; i += 1) {
      let best = 0
      let bestDistance = Number.POSITIVE_INFINITY
      for (let c = 0; c < k; c += 1) {
        const distance = standardized[i]!.reduce((acc, value, j) => acc + Math.pow(value - (centers[c]?.[j] as number), 2), 0)
        if (distance < bestDistance) {
          bestDistance = distance
          best = c
        }
      }
      if (labels[i] !== best) {
        labels[i] = best
        changed = true
      }
    }
    for (let c = 0; c < k; c += 1) {
      const members = standardized.filter((_, index) => labels[index] === c)
      if (members.length === 0) continue
      centers[c] = Array.from({ length: dims }, (_, j) => mean(members.map((row) => row[j] as number)))
    }
    if (!changed) break
  }
  const withinSS = Array.from({ length: k }, (_, c) =>
    standardized.filter((_, index) => labels[index] === c).reduce((acc, row) => acc + row.reduce((sum, value, j) => sum + Math.pow(value - (centers[c]?.[j] as number), 2), 0), 0)
  )
  return ok({ k, labels, centers, withinSS, totalWithinSS: withinSS.reduce((a, b) => a + b, 0) })
}

export interface LdaResult {
  readonly coefficients: number[]
  readonly constant: number
  readonly groupMeans: { group: string; mean: number }[]
  readonly accuracy: number
  readonly n: number
}

/** Análisis discriminante lineal para dos grupos y una o más variables. */
export function lda(
  rows: readonly (readonly number[])[],
  groups: readonly string[]
): Result<LdaResult> {
  const uniqueGroups = [...new Set(groups)]
  if (uniqueGroups.length !== 2) return err('INVALID_INPUT', 'LDA requiere exactamente dos grupos.')
  const [groupA, groupB] = uniqueGroups as [string, string]
  const rowsA = rows.filter((_, index) => groups[index] === groupA)
  const rowsB = rows.filter((_, index) => groups[index] === groupB)
  if (rowsA.length < 2 || rowsB.length < 2) return err('INSUFFICIENT_DATA', 'Cada grupo necesita al menos 2 observaciones.')
  const dims = rows[0]?.length ?? 0
  const meanA = Array.from({ length: dims }, (_, j) => mean(rowsA.map((row) => row[j] as number)))
  const meanB = Array.from({ length: dims }, (_, j) => mean(rowsB.map((row) => row[j] as number)))
  const pooled = Array.from({ length: dims }, (_, i) =>
    Array.from({ length: dims }, (_, j) => {
      let acc = 0
      for (const row of rowsA) acc += ((row[i] as number) - (meanA[i] as number)) * ((row[j] as number) - (meanA[j] as number))
      for (const row of rowsB) acc += ((row[i] as number) - (meanB[i] as number)) * ((row[j] as number) - (meanB[j] as number))
      return acc / (rowsA.length + rowsB.length - 2)
    })
  )
  const inverse = invertMatrix(pooled)
  if (!inverse) return err('SINGULAR_MATRIX', 'La matriz de covarianza es singular.')
  const difference = meanA.map((value, index) => value - (meanB[index] as number))
  const coefficients = difference.map((_, i) => inverse[i]!.reduce((acc, value, j) => acc + value * (difference[j] as number), 0))
  const constant = -0.5 * coefficients.reduce((acc, value, i) => acc + value * (meanA[i] as number + (meanB[i] as number)), 0)
  const classify = (row: readonly number[]): string => {
    const score = constant + row.reduce((acc, value, j) => acc + value * (coefficients[j] as number), 0)
    return score >= 0 ? groupA : groupB
  }
  let correct = 0
  for (let i = 0; i < rows.length; i += 1) if (classify(rows[i] as number[]) === groups[i]) correct += 1
  return ok({
    coefficients,
    constant,
    groupMeans: [
      { group: groupA, mean: coefficients.reduce((acc, value, j) => acc + value * (meanA[j] as number), 0) + constant },
      { group: groupB, mean: coefficients.reduce((acc, value, j) => acc + value * (meanB[j] as number), 0) + constant }
    ],
    accuracy: correct / rows.length,
    n: rows.length
  })
}

function invertMatrix(matrix: Matrix): Matrix | null {
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
