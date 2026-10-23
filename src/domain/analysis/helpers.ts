import { fmt, fmtP } from '../stats/numeric'
import type { AnalysisInput, AnalysisSection, TableSection, TextSection } from './types'

export function num(input: AnalysisInput, key: string): number[] {
  return [...(input.numeric[key] ?? [])]
}

export function colText(input: AnalysisInput, key: string): string[] {
  return (input.text[key] ?? []).map(String)
}

export function pnum(input: AnalysisInput, key: string, fallback = Number.NaN): number {
  const value = input.params[key]
  if (value === undefined || value === '') return fallback
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

export function pstr(input: AnalysisInput, key: string, fallback = ''): string {
  const value = input.params[key]
  return value === undefined ? fallback : String(value)
}

export function pbool(input: AnalysisInput, key: string, fallback = false): boolean {
  const value = input.params[key]
  if (value === undefined) return fallback
  return value === 'true' || value === 1 || value === '1'
}

export function n(value: number, digits = 4): string {
  return Number.isFinite(value) ? fmt(value, digits) : '*'
}

export function p(value: number): string {
  return Number.isFinite(value) ? fmtP(value) : '*'
}

export function table(
  title: string,
  headers: readonly string[],
  rows: readonly (readonly (string | number)[])[],
  align?: readonly ('left' | 'right')[]
): TableSection {
  return {
    kind: 'table',
    title,
    headers: [...headers],
    rows: rows.map((row) => row.map((cell) => (typeof cell === 'number' ? n(cell) : String(cell)))),
    align
  }
}

export function text(title: string, content: string): TextSection {
  return { kind: 'text', title, text: content }
}

export function sections(...items: (AnalysisSection | null | undefined | false)[]): AnalysisSection[] {
  return items.filter((item): item is AnalysisSection => Boolean(item))
}
