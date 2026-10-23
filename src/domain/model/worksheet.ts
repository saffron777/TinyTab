import type { Cell, DataType } from './value'

export interface Column {
  readonly id: string
  readonly name: string
  readonly type: DataType
  readonly values: Cell[]
  /** Fórmula declarativa de columna calculada (pendiente en v0.1). */
  readonly formula?: string
}

export interface Worksheet {
  readonly id: string
  readonly name: string
  readonly columns: Column[]
  readonly rowCount: number
}

export function generateId(prefix: string): string {
  const random = Math.random().toString(36).slice(2, 10)
  return `${prefix}_${Date.now().toString(36)}_${random}`
}

export function createColumn(name: string, type: DataType = 'numeric', length = 0): Column {
  return { id: generateId('col'), name, type, values: Array.from({ length }, () => null) }
}

export function createWorksheet(name = 'Hoja de trabajo 1', columnCount = 3, rowCount = 20): Worksheet {
  const columns = Array.from({ length: columnCount }, (_, index) => createColumn(`C${index + 1}`, 'numeric', rowCount))
  return { id: generateId('ws'), name, columns, rowCount }
}

export function columnByName(worksheet: Worksheet, name: string): Column | undefined {
  return worksheet.columns.find((column) => column.name === name)
}

export function addColumn(worksheet: Worksheet, column: Column): Worksheet {
  const values = column.values.slice()
  while (values.length < worksheet.rowCount) values.push(null)
  const next: Column = { ...column, values }
  return { ...worksheet, columns: [...worksheet.columns, next] }
}

export function removeColumn(worksheet: Worksheet, columnId: string): Worksheet {
  return { ...worksheet, columns: worksheet.columns.filter((column) => column.id !== columnId) }
}

export function renameColumn(worksheet: Worksheet, columnId: string, name: string): Worksheet {
  return {
    ...worksheet,
    columns: worksheet.columns.map((column) => (column.id === columnId ? { ...column, name } : column))
  }
}

export function setColumnType(worksheet: Worksheet, columnId: string, type: DataType): Worksheet {
  return {
    ...worksheet,
    columns: worksheet.columns.map((column) => {
      if (column.id !== columnId) return column
      const values = column.values.map((value) => {
        if (value === null) return null
        if (type === 'text') return String(value)
        if (type === 'numeric') {
          const numeric = typeof value === 'number' ? value : Number(String(value).replace(',', '.'))
          return Number.isFinite(numeric) ? numeric : null
        }
        return value
      })
      return { ...column, type, values }
    })
  }
}

export function addRows(worksheet: Worksheet, count = 1): Worksheet {
  const nextRowCount = worksheet.rowCount + count
  return {
    ...worksheet,
    rowCount: nextRowCount,
    columns: worksheet.columns.map((column) => {
      const values = column.values.slice()
      while (values.length < nextRowCount) values.push(null)
      return { ...column, values }
    })
  }
}

export function removeRows(worksheet: Worksheet, startRow: number, count = 1): Worksheet {
  const nextRowCount = Math.max(0, worksheet.rowCount - count)
  return {
    ...worksheet,
    rowCount: nextRowCount,
    columns: worksheet.columns.map((column) => {
      const values = column.values.slice(0, Math.max(startRow, 0)).concat(column.values.slice(startRow + count))
      while (values.length < nextRowCount) values.push(null)
      return { ...column, values: values.slice(0, nextRowCount) }
    })
  }
}

export function setCell(worksheet: Worksheet, columnId: string, row: number, value: Cell): Worksheet {
  return {
    ...worksheet,
    columns: worksheet.columns.map((column) => {
      if (column.id !== columnId) return column
      const values = column.values.slice()
      while (values.length <= row) values.push(null)
      values[row] = value
      return { ...column, values }
    }),
    rowCount: Math.max(worksheet.rowCount, row + 1)
  }
}

export function columnValues(worksheet: Worksheet, columnId: string): Cell[] {
  const column = worksheet.columns.find((item) => item.id === columnId)
  if (!column) return []
  const values = column.values.slice(0, worksheet.rowCount)
  while (values.length < worksheet.rowCount) values.push(null)
  return values
}
