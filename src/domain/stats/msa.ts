/** Estudios de sistemas de medida: Gage R&R cruzado por ANOVA. */
import { mean, sd } from './numeric'
import { err, ok, type Result } from '../model/result'

export interface GageRow {
  readonly part: string
  readonly operator: string
  readonly trial: number
  readonly value: number
}

export interface GageRROutput {
  readonly parts: number
  readonly operators: number
  readonly trials: number
  readonly repeatability: number
  readonly reproducibility: number
  readonly interaction: number
  readonly partVariation: number
  readonly totalVariation: number
  readonly grr: number
  readonly percentContribution: number
  readonly percentStudy: number
  readonly percentTolerance: number
  readonly ndc: number
  readonly table: { source: string; df: number; ss: number; ms: number; f: number; p: number }[]
}

function fUpper(f: number, df1: number, df2: number): number {
  if (!(f >= 0) || !(df2 > 0)) return Number.NaN
  let lo = 0
  let hi = 1
  for (let i = 0; i < 200 && hi < 1e6; i += 1) hi *= 2
  for (let i = 0; i < 200; i += 1) {
    const mid = (lo + hi) / 2
    if (fCdfLocal(mid, df1, df2) < 1 - 0.05) lo = mid
    else hi = mid
  }
  return hi
}

function fCdfLocal(f: number, df1: number, df2: number): number {
  const x = (df1 * f) / (df1 * f + df2)
  const a = df1 / 2
  const b = df2 / 2
  if (x <= 0) return 0
  if (x >= 1) return 1
  const logBeta = +lgammaLocal(a + b) + a * Math.log(x) + b * Math.log(1 - x) - lgammaLocal(a) - lgammaLocal(b)
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

function lgammaLocal(x: number): number {
  const coef = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7]
  if (x < 0.5) return Math.log(Math.PI / Math.sin(Math.PI * x)) - lgammaLocal(1 - x)
  const z = x - 1
  let a = coef[0] as number
  const t = z + 7.5
  for (let i = 1; i < coef.length; i += 1) a += (coef[i] as number) / (z + i)
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(a)
}

export function gageRR(rows: readonly GageRow[], tolerance?: number): Result<GageRROutput> {
  if (rows.length < 8) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 8 mediciones.')
  const parts = [...new Set(rows.map((row) => row.part))]
  const operators = [...new Set(rows.map((row) => row.operator))]
  const trials = [...new Set(rows.map((row) => row.trial))]
  const p = parts.length
  const o = operators.length
  const r = trials.length
  if (p < 2 || o < 2 || r < 2) return err('INVALID_INPUT', 'Se requieren al menos 2 piezas, 2 operadores y 2 repeticiones.')
  const expected = p * o * r
  if (rows.length !== expected) return err('INVALID_INPUT', `El diseño debe ser balanceado y completo (${expected} mediciones).`)
  const values = rows.map((row) => row.value)
  const total = mean(values)
  const n = rows.length
  const ssTotal = values.reduce((acc, value) => acc + (value - total) ** 2, 0)
  const sumBy = (key: (row: GageRow) => string): Map<string, { sum: number; count: number }> => {
    const map = new Map<string, { sum: number; count: number }>()
    for (const row of rows) {
      const entry = map.get(key(row)) ?? { sum: 0, count: 0 }
      entry.sum += row.value
      entry.count += 1
      map.set(key(row), entry)
    }
    return map
  }
  const partTotals = sumBy((row) => row.part)
  const operatorTotals = sumBy((row) => row.operator)
  const cellTotals = sumBy((row) => `${row.part}|${row.operator}`)
  const ssParts = [...partTotals.values()].reduce((acc, item) => acc + (item.sum * item.sum) / item.count, 0) - (n * total * total)
  const ssOperators = [...operatorTotals.values()].reduce((acc, item) => acc + (item.sum * item.sum) / item.count, 0) - (n * total * total)
  const ssCells = [...cellTotals.values()].reduce((acc, item) => acc + (item.sum * item.sum) / item.count, 0) - (n * total * total)
  const ssInteraction = Math.max(0, ssCells - ssParts - ssOperators)
  const ssRepeatability = Math.max(0, ssTotal - ssCells)
  const dfParts = p - 1
  const dfOperators = o - 1
  const dfInteraction = dfParts * dfOperators
  const dfRepeatability = p * o * (r - 1)
  const dfTotal = n - 1
  const msParts = ssParts / dfParts
  const msOperators = ssOperators / dfOperators
  const msInteraction = dfInteraction > 0 ? ssInteraction / dfInteraction : Number.NaN
  const msRepeatability = ssRepeatability / dfRepeatability
  const fParts = msParts / msInteraction
  const fOperators = msOperators / msInteraction
  const fInteraction = msInteraction / msRepeatability
  const varRepeat = msRepeatability
  const varInteraction = Math.max(0, (msInteraction - msRepeatability) / r)
  const varOperator = Math.max(0, (msOperators - msInteraction) / (p * r))
  const varPart = Math.max(0, (msParts - msInteraction) / (o * r))
  const grr = Math.sqrt(varRepeat + varInteraction + varOperator)
  const tv = Math.sqrt(grr * grr + varPart)
  const toleranceValue = tolerance ?? tv * 6
  return ok({
    parts: p,
    operators: o,
    trials: r,
    repeatability: Math.sqrt(varRepeat),
    reproducibility: Math.sqrt(varOperator + varInteraction),
    interaction: Math.sqrt(varInteraction),
    partVariation: Math.sqrt(varPart),
    totalVariation: tv,
    grr,
    percentContribution: tv > 0 ? ((grr * grr) / (tv * tv)) * 100 : Number.NaN,
    percentStudy: tv > 0 ? (grr / tv) * 100 : Number.NaN,
    percentTolerance: toleranceValue > 0 ? (6 * grr / toleranceValue) * 100 : Number.NaN,
    ndc: grr > 0 ? (1.41 * Math.sqrt(varPart)) / grr : Number.NaN,
    table: [
      { source: 'Pieza', df: dfParts, ss: ssParts, ms: msParts, f: fParts, p: fUpper(fParts, dfParts, dfInteraction) },
      { source: 'Operador', df: dfOperators, ss: ssOperators, ms: msOperators, f: fOperators, p: fUpper(fOperators, dfOperators, dfInteraction) },
      { source: 'Pieza × Operador', df: dfInteraction, ss: ssInteraction, ms: msInteraction, f: fInteraction, p: fUpper(fInteraction, dfInteraction, dfRepeatability) },
      { source: 'Repetibilidad', df: dfRepeatability, ss: ssRepeatability, ms: msRepeatability, f: Number.NaN, p: Number.NaN },
      { source: 'Total', df: dfTotal, ss: ssTotal, ms: Number.NaN, f: Number.NaN, p: Number.NaN }
    ]
  })
}

export function gageStatisticsByOperator(rows: readonly GageRow[]): { operator: string; mean: number; sd: number; n: number }[] {
  const grouped = new Map<string, number[]>()
  for (const row of rows) {
    const values = grouped.get(row.operator) ?? []
    values.push(row.value)
    grouped.set(row.operator, values)
  }
  return [...grouped.entries()].map(([operator, values]) => ({ operator, mean: mean(values), sd: sd(values), n: values.length }))
}
