import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

/**
 * Codificación canónica de los vectores dorados.
 *
 * Problema: `JSON.stringify` convierte NaN e Infinity en `null` y pierde `-0`,
 * de modo que la comparación de paridad dejaría de ser exacta. Este módulo
 * codifica los números no finitos de forma explícita y reversible:
 *
 *   NaN        -> { "$num": "NaN" }
 *   Infinity   -> { "$num": "Infinity" }
 *   -Infinity  -> { "$num": "-Infinity" }
 *   -0         -> { "$num": "-0" }
 *
 * Map y Set se codifican como { "$map": [...] } y { "$set": [...] }.
 * Las propiedades `undefined` se omiten (igual que JSON.stringify).
 */
export type Encoded = null | boolean | number | string | Encoded[] | { [key: string]: Encoded }

const here = dirname(fileURLToPath(import.meta.url))

/** Directorio de salida consumido por StatisticEngine.Domain.Tests. */
export const VECTORS_DIR = resolve(here, '../../tests/StatisticEngine.Domain.Tests/Parity/vectors')

export function encode(value: unknown): Encoded {
  if (value === undefined) return { $undefined: true }
  if (value === null) return null
  if (typeof value === 'number') {
    if (Number.isNaN(value)) return { $num: 'NaN' }
    if (value === Number.POSITIVE_INFINITY) return { $num: 'Infinity' }
    if (value === Number.NEGATIVE_INFINITY) return { $num: '-Infinity' }
    if (Object.is(value, -0)) return { $num: '-0' }
    return value
  }
  if (typeof value === 'string' || typeof value === 'boolean') return value
  if (typeof value === 'bigint') return { $bigint: value.toString() }
  if (typeof value === 'function' || typeof value === 'symbol') return { $unsupported: typeof value }
  if (Array.isArray(value)) return value.map((item) => encode(item))
  if (value instanceof Map) {
    return { $map: [...value.entries()].map(([key, item]) => [encode(key), encode(item)]) }
  }
  if (value instanceof Set) {
    return { $set: [...value].map((item) => encode(item)) }
  }
  const output: { [key: string]: Encoded } = {}
  for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
    if (item === undefined) continue
    output[key] = encode(item)
  }
  return output
}

export interface GoldenCase {
  /** Argumentos en el orden de la firma TypeScript. */
  readonly args: Encoded[]
  /** Valor devuelto por el motor, codificado. */
  readonly expected: Encoded
}

export interface GoldenDocument {
  readonly module: string
  readonly engine: { readonly name: string; readonly version: string; readonly parityBaseline: string }
  readonly encoding: string
  readonly functions: Record<string, GoldenCase[]>
}

const ENGINE = {
  name: 'tinytab-statistical-software',
  version: '0.1.0',
  parityBaseline: 'tinytab@0.1.0/src/domain'
} as const

const ENCODING =
  'NaN|Infinity|-Infinity|-0 -> {"$num":"..."}; Map -> {"$map":[[k,v]]}; Set -> {"$set":[...]}; undefined -> {"$undefined":true}'

/** Acumula casos por nombre de función y escribe un JSON por módulo. */
export class Recorder {
  private readonly functions: Record<string, GoldenCase[]> = {}

  /** Registra `args` y ejecuta la función para capturar su salida. */
  add(name: string, args: unknown[], run: () => unknown): void {
    this.push(name, args, run())
  }

  /** Registra una salida ya calculada (para argumentos no serializables, p. ej. Map). */
  addRaw(name: string, args: unknown[], expected: unknown): void {
    this.push(name, args, expected)
  }

  count(): number {
    return Object.values(this.functions).reduce((total, list) => total + list.length, 0)
  }

  write(module: string): number {
    const sorted: Record<string, GoldenCase[]> = {}
    for (const name of Object.keys(this.functions).sort()) sorted[name] = this.functions[name] as GoldenCase[]
    const document: GoldenDocument = {
      module,
      engine: ENGINE,
      encoding: ENCODING,
      functions: sorted
    }
    mkdirSync(VECTORS_DIR, { recursive: true })
    writeFileSync(resolve(VECTORS_DIR, `${module}.json`), `${JSON.stringify(document, null, 2)}\n`, 'utf8')
    return this.count()
  }

  private push(name: string, args: unknown[], expected: unknown): void {
    const list = this.functions[name] ?? (this.functions[name] = [])
    list.push({ args: args.map((arg) => encode(arg)), expected: encode(expected) })
  }
}

/** Rejilla inclusiva de `steps + 1` valores. */
export function grid(from: number, to: number, steps: number): number[] {
  return Array.from({ length: steps + 1 }, (_, index) => from + ((to - from) * index) / steps)
}
