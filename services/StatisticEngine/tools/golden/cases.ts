/**
 * Entradas deterministas compartidas por los exportadores de vectores dorados.
 * No usan aleatoriedad del sistema: todas las semillas son explícitas.
 */

/** Misma serie que `tests/unit/analysis-smoke.test.ts` (30 valores). */
export const SERIES30: number[] = Array.from(
  { length: 30 },
  (_, index) => Math.round((10 + index * 0.7 + Math.sin(index) * 2) * 100) / 100
)

/** Serie corta con repeticiones (cuantiles, rangos con empates). */
export const SMALL = [2, 4, 4, 4, 5, 5, 7, 9]

/** Serie apta para Anderson-Darling y pruebas no paramétricas. */
export const NORMAL12 = [2, 3, 3, 4, 4, 4, 5, 5, 6, 7, 8, 9]

export const XS = [1, 2, 3, 4, 5]
export const YS = [2, 4, 6, 8, 11]

export const SAMPLE_1 = [10, 12, 14, 16]
export const SAMPLE_2 = [8, 9, 11, 13]
export const PAIRED_1 = [10, 12, 14]
export const PAIRED_2 = [9, 11, 12]

export const GROUPS_3: number[][] = [
  [1, 2, 3],
  [4, 5, 6],
  [7, 8, 9]
]

export const GROUPS_VAR: number[][] = [
  [1, 2, 3, 4],
  [2, 4, 6, 8, 10],
  [10, 11, 9, 10, 10.5, 9.5]
]

/** Serie de capacidad (media 10, σ pequeña). */
export const CAPABILITY = [10, 10.2, 9.8, 10.1, 9.9, 10.05, 9.95, 10.15]

/** Datos de Gage R&R: 3 piezas × 2 operadores × 2 repeticiones = 12 filas. */
export const GAGE_ROWS = (() => {
  const rows: { part: string; operator: string; trial: number; value: number }[] = []
  const values: Record<string, number> = {
    P1A: 10.01,
    P1B: 10.02,
    P2A: 10.51,
    P2B: 10.49,
    P3A: 11.02,
    P3B: 10.98
  }
  const parts = ['P1', 'P2', 'P3']
  for (const part of parts) {
    for (const operator of ['A', 'B']) {
      for (let trial = 1; trial <= 2; trial += 1) {
        const base = values[`${part}${operator}`] as number
        rows.push({ part, operator, trial, value: Number((base + (trial - 1) * 0.01).toFixed(4)) })
      }
    }
  }
  return rows
})()

/** Diseño factorial de 3 factores (códigos −1/+1). */
export const DOE_FACTORS = [
  { name: 'A', low: -1, high: 1 },
  { name: 'B', low: -1, high: 1 },
  { name: 'C', low: -1, high: 1 }
]

/** Respuesta asociada a un diseño factorial 2^3 (8 corridas, orden estándar). */
export const DOE_RESPONSE = [12.1, 13.4, 12.8, 14.2, 12.5, 13.8, 13.1, 14.6]

/** Serie temporal determinista de 24 puntos con tendencia y estacionalidad. */
export const TS_SERIES: number[] = Array.from({ length: 24 }, (_, index) =>
  Number((50 + index * 1.5 + Math.sin((index * Math.PI) / 3) * 4).toFixed(4))
)

/** Serie estacional corta (2 periodos de 4). */
export const TS_SEASONAL = [10, 14, 12, 8, 11, 15, 13, 9]

export const REL_TIMES = [120, 150, 200, 260, 340, 430, 520, 610]
export const KM_TIMES = [1, 2, 2, 3, 4, 5, 6, 8, 9, 11]
export const KM_EVENTS = [true, false, true, true, false, true, true, false, true, true]

/** Observaciones multivariantes 6×2 y 5×3. */
export const PCA_ROWS_2 = [
  [1, 2],
  [2, 4.1],
  [3, 5.9],
  [4, 8.2],
  [5, 9.8],
  [6, 12.1]
]

export const PCA_ROWS_3 = [
  [1, 2, 9],
  [2, 4.1, 8],
  [3, 5.9, 7.1],
  [4, 8.2, 6],
  [5, 9.8, 5.2],
  [6, 12.1, 4]
]

/** Conglomerados sintéticos en 2 dimensiones. */
export const KMEANS_ROWS = [
  [0.1, 0.2],
  [0.3, 0.1],
  [0.2, 0.4],
  [5.1, 5.2],
  [5.3, 5.1],
  [5.2, 5.4],
  [9.9, 0.1],
  [10.1, 0.2],
  [10, 0.4],
  [0.4, 0.3]
]

export const LDA_ROWS = [
  [1, 1],
  [1.2, 0.9],
  [0.8, 1.1],
  [1.1, 1.3],
  [4, 4.2],
  [4.3, 3.9],
  [3.8, 4.1],
  [4.1, 4.3]
]

export const LDA_GROUPS = ['A', 'A', 'A', 'A', 'B', 'B', 'B', 'B']

/** Regresión múltiple: y = 2 + 3·x1 − 1.5·x2. */
export const REG_X1 = [1, 2, 3, 4, 5, 6, 7, 8]
export const REG_X2 = [2, 1, 3, 2, 4, 3, 5, 4]
export const REG_Y = REG_X1.map((x, index) => 2 + 3 * x - 1.5 * (REG_X2[index] as number))

/** Regresión logística (x, y 0/1). */
export const LOGIT_X = [-3, -2, -1, 1, 2, 3, 0.5, -0.5]
export const LOGIT_Y = [0, 0, 1, 1, 1, 0, 0, 1]

/** Regresión de Poisson (conteos). */
export const POISSON_Y = [0, 1, 2, 3, 2, 1, 4, 5, 3, 2]
export const POISSON_X = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]

/** ANOVA de dos factores: 2×2 balanceado y desbalanceado. */
export const TWO_WAY_Y = [10, 11, 9, 12, 14, 13, 15, 16]
export const TWO_WAY_A = ['A1', 'A1', 'A1', 'A1', 'A2', 'A2', 'A2', 'A2']
export const TWO_WAY_B = ['B1', 'B1', 'B2', 'B2', 'B1', 'B1', 'B2', 'B2']

export const TWO_WAY_Y_UNBAL = [10, 11, 9, 12, 14, 13, 15]
export const TWO_WAY_A_UNBAL = ['A1', 'A1', 'A1', 'A1', 'A2', 'A2', 'A2']
export const TWO_WAY_B_UNBAL = ['B1', 'B1', 'B2', 'B2', 'B1', 'B2', 'B2']

/** Tabla de contingencia y bondad de ajuste. */
export const CROSS_ROWS = ['x', 'x', 'x', 'y', 'y', 'y', 'y', 'z']
export const CROSS_COLS = ['1', '2', '1', '2', '1', '2', '1', '1']
export const GOF_OBSERVED = [10, 20, 30]
export const GOF_LABELS = ['A', 'B', 'C']

/** Secuencia para reglas de control (Nelson). */
export const CONTROL_SERIES = [10, 10.1, 9.9, 10.05, 9.95, 10.02, 9.98, 10.01, 10.03, 9.97, 14.2, 10.02]

/** Subgrupos de tamaño 3 para cartas X̄-R / X̄-S. */
export const SUBGROUPS_3 = [0, 1, 2, 3, 4].map((group) =>
  [0, 1, 2].map((index) => Number((10 + group * 0.5 + index * 0.1).toFixed(4)))
)
