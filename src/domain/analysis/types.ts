import type { ChartSpec } from '../model/chart'
import type { Result } from '../model/result'

export type AnalysisParamKind = 'number' | 'integer' | 'select'

export interface AnalysisParamDef {
  readonly key: string
  readonly label: string
  readonly kind: AnalysisParamKind
  readonly defaultValue: number | string
  readonly options?: readonly { value: string; label: string }[]
  readonly optional?: boolean
}

export interface ColumnRequirement {
  readonly key: string
  readonly label: string
  readonly required: boolean
  /** Permite seleccionar varias columnas (p. ej. predictores). */
  readonly multiple?: boolean
}

export interface AnalysisInput {
  readonly numeric: Readonly<Record<string, readonly number[]>>
  readonly text: Readonly<Record<string, readonly (string | number)[]>>
  readonly params: Readonly<Record<string, number | string>>
}

export interface TableSection {
  readonly kind: 'table'
  readonly title: string
  readonly headers: string[]
  readonly rows: string[][]
  readonly align?: readonly ('left' | 'right')[]
}

export interface TextSection {
  readonly kind: 'text'
  readonly title: string
  readonly text: string
}

export type AnalysisSection = TableSection | TextSection

export interface AnalysisResult {
  readonly id: string
  readonly title: string
  readonly subtitle?: string
  readonly sections: AnalysisSection[]
  readonly charts?: ChartSpec[]
  readonly summary?: string
}

export interface AnalysisDefinition {
  readonly id: string
  readonly label: string
  readonly category: AnalysisCategory
  readonly description: string
  readonly numericColumns: readonly ColumnRequirement[]
  readonly textColumns?: readonly ColumnRequirement[]
  readonly params: readonly AnalysisParamDef[]
  readonly run: (input: AnalysisInput) => Result<AnalysisResult>
}

export type AnalysisCategory =
  | 'Estadísticas básicas'
  | 'Distribuciones'
  | 'Pruebas de hipótesis'
  | 'ANOVA'
  | 'Regresión'
  | 'Control de calidad'
  | 'Capacidad'
  | 'Sistemas de medida'
  | 'DOE'
  | 'Series temporales'
  | 'Fiabilidad'
  | 'Multivariante'
  | 'No paramétricos'
  | 'Potencia y tamaño de muestra'
  | 'Tablas'
