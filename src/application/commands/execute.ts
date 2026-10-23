/**
 * Lenguaje de comandos estilo Minitab (subconjunto): NOTE, NAME, PRINT,
 * DESCRIBE, TTEST, ANOVA, REGRESS, LET y SET.
 */
import { activeWorksheet, appendSession, type ProjectDoc } from '../../domain/model/project'
import { addColumn, columnByName, columnValues, createColumn, renameColumn, setCell, setColumnType } from '../../domain/model/worksheet'
import { toNumericValues, type Cell } from '../../domain/model/value'
import { err, ok, type Result } from '../../domain/model/result'
import { formatAnalysisResult, findAnalysis } from '../../domain/analysis'
import type { AnalysisInput } from '../../domain/analysis/types'
import type { AnalysisRunner } from '../ports'

export type CommandKind = 'command' | 'output' | 'error' | 'note'

export interface CommandBlock {
  readonly kind: CommandKind
  readonly text: string
}

export interface CommandOutcome {
  readonly project: ProjectDoc
  readonly blocks: CommandBlock[]
}

type Token = { type: 'number'; value: number } | { type: 'word'; value: string } | { type: 'string'; value: string } | { type: 'op'; value: string }

export function tokenize(source: string): Result<Token[]> {
  const tokens: Token[] = []
  let index = 0
  while (index < source.length) {
    const char = source[index] as string
    if (/\s/.test(char)) {
      index += 1
      continue
    }
    if (char === '"' || char === "'") {
      const quote = char
      let value = ''
      index += 1
      while (index < source.length && source[index] !== quote) {
        value += source[index]
        index += 1
      }
      if (index >= source.length) return err('INVALID_INPUT', 'Cadena sin cerrar.')
      index += 1
      tokens.push({ type: 'string', value })
      continue
    }
    if (/[0-9.]/.test(char)) {
      let value = ''
      while (index < source.length && /[0-9.eE+-]/.test(source[index] as string)) {
        const current = source[index] as string
        if ((current === '+' || current === '-') && !/[eE]/.test(value.slice(-1))) break
        value += current
        index += 1
      }
      const parsed = Number(value)
      if (!Number.isFinite(parsed)) return err('INVALID_INPUT', `Número inválido: ${value}`)
      tokens.push({ type: 'number', value: parsed })
      continue
    }
    if (/[A-Za-z_$]/.test(char)) {
      let value = ''
      while (index < source.length && /[A-Za-z0-9_$.]/.test(source[index] as string)) {
        value += source[index]
        index += 1
      }
      tokens.push({ type: 'word', value })
      continue
    }
    if ('+-*/^(),='.includes(char)) {
      tokens.push({ type: 'op', value: char })
      index += 1
      continue
    }
    return err('INVALID_INPUT', `Carácter no permitido: ${char}`)
  }
  return ok(tokens)
}

function columnRefs(tokens: readonly Token[]): string[] {
  return tokens.filter((token): token is { type: 'word'; value: string } => token.type === 'word').map((token) => token.value)
}

function numericColumn(project: ProjectDoc, name: string): Result<number[]> {
  const worksheet = activeWorksheet(project)
  const column = columnByName(worksheet, name)
  if (!column) return err('INVALID_INPUT', `No existe la columna "${name}".`)
  return toNumericValues(columnValues(worksheet, column.id))
}

function zipValues(left: readonly number[], right: readonly number[], fn: (a: number, b: number) => number): number[] {
  const length = Math.max(left.length, right.length)
  return Array.from({ length }, (_, index) => {
    const a = left.length === 1 ? (left[0] as number) : (left[index] ?? Number.NaN)
    const b = right.length === 1 ? (right[0] as number) : (right[index] ?? Number.NaN)
    return fn(a, b)
  })
}

class ExpressionParser {
  private position = 0
  constructor(private readonly tokens: readonly Token[], private readonly project: ProjectDoc) {}

  parse(): Result<number[]> {
    const result = this.parseSum()
    if (!result.ok) return result
    if (this.position < this.tokens.length) return err('INVALID_INPUT', 'Expresión no válida.')
    return result
  }

  private peek(): Token | undefined {
    return this.tokens[this.position]
  }

  private next(): Token | undefined {
    const token = this.tokens[this.position]
    this.position += 1
    return token
  }

  private parseSum(): Result<number[]> {
    const first = this.parseProduct()
    if (!first.ok) return first
    let left: Result<number[]> = first
    for (;;) {
      const token = this.peek()
      if (!token || token.type !== 'op' || (token.value !== '+' && token.value !== '-')) break
      this.next()
      const right = this.parseProduct()
      if (!right.ok) return right
      const current: Result<number[]> = left
      if (!current.ok) return current
      const operator = token.value
      const currentValues: number[] = current.value
      const rightValues: number[] = right.value
      left = ok(zipValues(currentValues, rightValues, (a, b) => (operator === '+' ? a + b : a - b)))
    }
    return left
  }

  private parseProduct(): Result<number[]> {
    const first = this.parsePower()
    if (!first.ok) return first
    let left: Result<number[]> = first
    for (;;) {
      const token = this.peek()
      if (!token || token.type !== 'op' || (token.value !== '*' && token.value !== '/')) break
      this.next()
      const right = this.parsePower()
      if (!right.ok) return right
      const current: Result<number[]> = left
      if (!current.ok) return current
      const operator = token.value
      const currentValues: number[] = current.value
      const rightValues: number[] = right.value
      left = ok(zipValues(currentValues, rightValues, (a, b) => (operator === '*' ? a * b : a / b)))
    }
    return left
  }

  private parsePower(): Result<number[]> {
    const base = this.parseUnary()
    if (!base.ok) return base
    const token = this.peek()
    if (token && token.type === 'op' && token.value === '^') {
      this.next()
      const exponent = this.parsePower()
      if (!exponent.ok) return exponent
      return ok(zipValues(base.value, exponent.value, (a, b) => Math.pow(a, b)))
    }
    return base
  }

  private parseUnary(): Result<number[]> {
    const token = this.peek()
    if (token && token.type === 'op' && token.value === '-') {
      this.next()
      const inner = this.parseUnary()
      if (!inner.ok) return inner
      return ok(inner.value.map((value) => -value))
    }
    return this.parseAtom()
  }

  private parseAtom(): Result<number[]> {
    const token = this.next()
    if (!token) return err('INVALID_INPUT', 'Expresión incompleta.')
    if (token.type === 'number') return ok([token.value])
    if (token.type === 'word') {
      const name = token.value.toUpperCase()
      const functions: Record<string, (value: number) => number> = {
        SQRT: Math.sqrt,
        LOG: Math.log,
        EXP: Math.exp,
        ABS: Math.abs,
        SIN: Math.sin,
        COS: Math.cos,
        TAN: Math.tan
      }
      const fn = functions[name]
      if (fn) {
        const open = this.next()
        if (!open || open.type !== 'op' || open.value !== '(') return err('INVALID_INPUT', 'Se esperaba "(" tras la función.')
        const argument = this.parseSum()
        if (!argument.ok) return argument
        const close = this.next()
        if (!close || close.type !== 'op' || close.value !== ')') return err('INVALID_INPUT', 'Se esperaba ")".')
        return ok(argument.value.map(fn))
      }
      return numericColumn(this.project, token.value)
    }
    return err('INVALID_INPUT', 'Expresión no válida.')
  }
}

async function runAnalysisCommand(project: ProjectDoc, analysisId: string, numeric: Record<string, number[]>, text: Record<string, (string | number)[]>, params: Record<string, number>, runner: AnalysisRunner): Promise<Result<ProjectDoc>> {
  const input: AnalysisInput = { numeric, text, params }
  const result = await runner.run(analysisId, input)
  if (!result.ok) return result
  return ok(appendSession(project, { kind: 'output', title: result.value.title, text: formatAnalysisResult(result.value) }))
}

export async function executeCommands(project: ProjectDoc, source: string, runner: AnalysisRunner): Promise<CommandOutcome> {
  const blocks: CommandBlock[] = []
  let current = project
  const lines = source.split(/\r?\n/)
  for (const rawLine of lines) {
    const line = rawLine.trim()
    if (!line || line.startsWith('#')) continue
    blocks.push({ kind: 'command', text: `> ${line}` })
    const tokens = tokenize(line)
    if (!tokens.ok) {
      blocks.push({ kind: 'error', text: tokens.error.message })
      continue
    }
    const [commandToken, ...rest] = tokens.value
    const command = commandToken?.type === 'word' ? commandToken.value.toUpperCase() : ''
    const words = columnRefs(rest)
    try {
      if (command === 'NOTE') {
        const message = rest.find((token) => token.type === 'string')
        if (message && message.type === 'string') blocks.push({ kind: 'note', text: message.value })
        continue
      }
      if (command === 'NAME') {
        const target = words[0]
        const newName = rest.find((token) => token.type === 'string')
        if (!target || !newName || newName.type !== 'string') {
          blocks.push({ kind: 'error', text: 'Uso: NAME C1 "Nuevo nombre"' })
          continue
        }
        const worksheet = activeWorksheet(current)
        const column = columnByName(worksheet, target)
        if (!column) {
          blocks.push({ kind: 'error', text: `No existe la columna "${target}".` })
          continue
        }
        current = { ...current, worksheets: current.worksheets.map((item) => (item.id === worksheet.id ? renameColumn(worksheet, column.id, newName.value) : item)) }
        blocks.push({ kind: 'output', text: `Columna ${target} renombrada a ${newName.value}.` })
        continue
      }
      if (command === 'PRINT') {
        for (const name of words) {
          const values = columnValues(activeWorksheet(current), columnByName(activeWorksheet(current), name)?.id ?? '')
          blocks.push({ kind: 'output', text: `${name}: ${values.map((value) => (value === null ? '*' : String(value))).join('  ')}` })
        }
        continue
      }
      if (command === 'DESCRIBE') {
        if (words.length === 0) {
          blocks.push({ kind: 'error', text: 'Uso: DESCRIBE C1 C2 ...' })
          continue
        }
        const numeric: Record<string, number[]> = {}
        for (const name of words) {
          const values = numericColumn(current, name)
          if (!values.ok) throw new Error(values.error.message)
          numeric[name] = values.value
        }
        const next = await runAnalysisCommand(current, 'describe', numeric, {}, {}, runner)
        if (!next.ok) throw new Error(next.error.message)
        current = next.value
        blocks.push({ kind: 'output', text: 'Estadísticas descriptivas generadas.' })
        continue
      }
      if (command === 'LET') {
        const equalityIndex = rest.findIndex((token) => token.type === 'op' && token.value === '=')
        const target = words[0]
        if (equalityIndex < 0 || !target) {
          blocks.push({ kind: 'error', text: 'Uso: LET C4 = C1 + C2' })
          continue
        }
        const expression = new ExpressionParser(rest.slice(equalityIndex + 1), current).parse()
        if (!expression.ok) {
          blocks.push({ kind: 'error', text: expression.error.message })
          continue
        }
        const worksheet = activeWorksheet(current)
        const existing = columnByName(worksheet, target)
        const values: Cell[] = expression.value.map((value) => (Number.isFinite(value) ? value : null))
        let updated = existing
          ? { ...worksheet, columns: worksheet.columns.map((column) => (column.id === existing.id ? { ...column, values } : column)) }
          : addColumn(worksheet, { ...createColumn(target, 'numeric', values.length), values })
        updated = { ...updated, rowCount: Math.max(updated.rowCount, values.length) }
        current = { ...current, worksheets: current.worksheets.map((item) => (item.id === worksheet.id ? updated : item)) }
        blocks.push({ kind: 'output', text: `Columna ${target} calculada (${values.length} valores).` })
        continue
      }
      if (command === 'SET') {
        const target = words[0]
        const values = rest.filter((token): token is { type: 'number'; value: number } => token.type === 'number').map((token) => token.value)
        if (!target || values.length === 0) {
          blocks.push({ kind: 'error', text: 'Uso: SET C1 1 2 3 ...' })
          continue
        }
        const worksheet = activeWorksheet(current)
        const existing = columnByName(worksheet, target)
        let updated = worksheet
        if (!existing) updated = addColumn(updated, { ...createColumn(target, 'numeric', values.length), values: values as Cell[] })
        else {
          let next = updated
          values.forEach((value, index) => {
            next = setCell(next, existing.id, index, value)
          })
          updated = next
        }
        current = { ...current, worksheets: current.worksheets.map((item) => (item.id === worksheet.id ? updated : item)) }
        blocks.push({ kind: 'output', text: `Columna ${target} cargada con ${values.length} valores.` })
        continue
      }
      if (command === 'TTEST') {
        const column = words[0]
        if (!column) {
          blocks.push({ kind: 'error', text: 'Uso: TTEST C1 [media nula]' })
          continue
        }
        const values = numericColumn(current, column)
        if (!values.ok) throw new Error(values.error.message)
        const mu = rest.find((token) => token.type === 'number')?.type === 'number' ? (rest.find((token) => token.type === 'number') as { type: 'number'; value: number }).value : 0
        const next = await runAnalysisCommand(current, 'one-sample-t', { x: values.value }, {}, { mu }, runner)
        if (!next.ok) throw new Error(next.error.message)
        current = next.value
        blocks.push({ kind: 'output', text: 'Prueba t de 1 muestra generada.' })
        continue
      }
      if (command === 'ANOVA') {
        const [responseName, factorName] = words
        if (!responseName || !factorName) {
          blocks.push({ kind: 'error', text: 'Uso: ANOVA respuesta factor' })
          continue
        }
        const response = numericColumn(current, responseName)
        if (!response.ok) throw new Error(response.error.message)
        const worksheet = activeWorksheet(current)
        const factorColumn = columnByName(worksheet, factorName)
        if (!factorColumn) throw new Error(`No existe la columna "${factorName}".`)
        const text = columnValues(worksheet, factorColumn.id).map((value) => (value === null ? '' : value))
        const next = await runAnalysisCommand(current, 'anova-one-way', { y: response.value }, { factor: text }, {}, runner)
        if (!next.ok) throw new Error(next.error.message)
        current = next.value
        blocks.push({ kind: 'output', text: 'ANOVA de un factor generada.' })
        continue
      }
      if (command === 'REGRESS') {
        const [responseName, ...predictors] = words
        if (!responseName || predictors.length === 0) {
          blocks.push({ kind: 'error', text: 'Uso: REGRESS respuesta predictor1 predictor2 ...' })
          continue
        }
        const numeric: Record<string, number[]> = {}
        const response = numericColumn(current, responseName)
        if (!response.ok) throw new Error(response.error.message)
        numeric[responseName] = response.value
        for (const predictor of predictors) {
          const values = numericColumn(current, predictor)
          if (!values.ok) throw new Error(values.error.message)
          numeric[predictor] = values.value
        }
        const next = await runAnalysisCommand(current, 'regression-multiple', numeric, {}, {}, runner)
        if (!next.ok) throw new Error(next.error.message)
        current = next.value
        blocks.push({ kind: 'output', text: 'Regresión múltiple generada.' })
        continue
      }
      if (command === 'HELP') {
        blocks.push({ kind: 'output', text: 'Comandos: NOTE, NAME, PRINT, DESCRIBE, LET, SET, TTEST, ANOVA, REGRESS, HELP' })
        continue
      }
      blocks.push({ kind: 'error', text: `Comando no reconocido: ${command || line}` })
    } catch (error) {
      blocks.push({ kind: 'error', text: error instanceof Error ? error.message : String(error) })
    }
  }
  const hasError = blocks.some((block) => block.kind === 'error')
  let output = current
  for (const block of blocks) {
    if (block.kind === 'note') output = appendSession(output, { kind: 'note', title: 'Nota', text: block.text })
    else if (block.kind === 'error') output = appendSession(output, { kind: 'error', title: 'Error', text: block.text })
    else if (block.kind === 'command') output = appendSession(output, { kind: 'command', title: 'Comando', text: block.text })
  }
  if (hasError) return { project: output, blocks }
  return { project: output, blocks }
}

export function commandHelp(): string {
  return [
    'NOTE "texto"            Añade una nota a la sesión',
    'NAME C1 "nombre"        Renombra una columna',
    'PRINT C1 C2             Muestra los valores de una columna',
    'DESCRIBE C1 C2          Estadísticas descriptivas',
    'LET C4 = C1 + C2        Calcula una columna',
    'SET C1 1 2 3            Carga valores en una columna',
    'TTEST C1 [mu]           Prueba t de 1 muestra',
    'ANOVA y factor          ANOVA de un factor',
    'REGRESS y x1 x2         Regresión múltiple',
    'HELP                    Esta ayuda'
  ].join('\n')
}

export { setColumnType, findAnalysis }
