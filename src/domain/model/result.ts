/** Resultado explícito para todo el dominio: nunca se lanzan excepciones por errores de usuario. */

export type DomainErrorCode =
  | 'INSUFFICIENT_DATA'
  | 'INVALID_PARAMETER'
  | 'SINGULAR_MATRIX'
  | 'NON_CONVERGENCE'
  | 'TYPE_MISMATCH'
  | 'NOT_SUPPORTED'
  | 'INVALID_INPUT'
  | 'UNKNOWN'

export interface DomainError {
  readonly code: DomainErrorCode
  readonly message: string
  readonly details?: Readonly<Record<string, unknown>>
}

export type Result<T, E = DomainError> = { readonly ok: true; readonly value: T } | { readonly ok: false; readonly error: E }

export function ok<T>(value: T): Result<T> {
  return { ok: true, value }
}

export function err<T = never>(
  code: DomainErrorCode,
  message: string,
  details?: Record<string, unknown>
): Result<T> {
  return { ok: false, error: { code, message, details } }
}

export function isOk<T, E>(result: Result<T, E>): result is { ok: true; value: T } {
  return result.ok
}

export function isErr<T, E>(result: Result<T, E>): result is { ok: false; error: E } {
  return !result.ok
}

/** Sólo para tests y rutas donde el fallo es un error de programación. */
export function unwrap<T>(result: Result<T>): T {
  if (!result.ok) throw new Error(`${result.error.code}: ${result.error.message}`)
  return result.value
}

export function mapResult<T, U>(result: Result<T>, fn: (value: T) => U): Result<U> {
  return result.ok ? ok(fn(result.value)) : result
}
