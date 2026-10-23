import { err, ok, type Result } from './result'

/** Tipos de datos admitidos por una columna, igual que en Minitab. */
export type DataType = 'numeric' | 'text' | 'datetime'

/** Valor de celda. `null` representa el valor faltante (`*`). */
export type Cell = number | string | null

export const MISSING_DISPLAY = '*'

export function isMissing(value: Cell): boolean {
  return value === null || (typeof value === 'number' && Number.isNaN(value))
}

/** Convierte celdas a números; los faltantes se omiten. Falla si hay texto no numérico. */
export function toNumericValues(values: readonly Cell[]): Result<number[]> {
  const output: number[] = []
  for (const value of values) {
    if (isMissing(value)) continue
    if (typeof value !== 'number') {
      return err('TYPE_MISMATCH', 'La columna contiene valores no numéricos.')
    }
    if (!Number.isFinite(value)) continue
    output.push(value)
  }
  return ok(output)
}

export function toNumericColumn(values: readonly Cell[]): number[] {
  const result = toNumericValues(values)
  if (!result.ok) throw new Error(result.error.message)
  return result.value
}

/** Texto de una celda para la rejilla y las exportaciones. */
export function formatCell(value: Cell, type: DataType): string {
  if (isMissing(value)) return MISSING_DISPLAY
  if (type === 'numeric' && typeof value === 'number') {
    if (Number.isInteger(value)) return String(value)
    return String(Number(value.toPrecision(12)))
  }
  return String(value)
}

/** Convierte una celda escrita por el usuario en valor tipado. */
export function parseCell(input: string, type: DataType): Cell {
  const text = input.trim()
  if (text === '' || text === MISSING_DISPLAY) return null
  if (type === 'numeric') {
    const normalized = text.replace(',', '.')
    const value = Number(normalized)
    if (!Number.isFinite(value)) return text
    return value
  }
  return text
}
