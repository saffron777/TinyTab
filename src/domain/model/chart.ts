/** Especificación serializable de gráficos: el dominio describe datos, la UI dibuja. */
export type ChartSpec =
  | { readonly kind: 'histogram'; readonly title: string; readonly values: number[]; readonly bins?: number }
  | { readonly kind: 'boxplot'; readonly title: string; readonly groups: { label: string; values: number[] }[] }
  | {
      readonly kind: 'scatter'
      readonly title: string
      readonly points: { x: number; y: number }[]
      readonly xLabel: string
      readonly yLabel: string
    }
  | {
      readonly kind: 'control'
      readonly title: string
      readonly points: { index: number; value: number }[]
      readonly center: number
      readonly lower: number
      readonly upper: number
      readonly violations: number[]
      readonly yLabel: string
    }
  | {
      readonly kind: 'probability'
      readonly title: string
      readonly points: { x: number; y: number }[]
      readonly slope: number
      readonly intercept: number
    }
  | {
      readonly kind: 'residuals'
      readonly title: string
      readonly fitted: number[]
      readonly residuals: number[]
      readonly order: number[]
    }
  | {
      readonly kind: 'timeseries'
      readonly title: string
      readonly values: number[]
      readonly forecast?: number[]
    }
  | {
      readonly kind: 'line'
      readonly title: string
      readonly series: { label: string; values: number[] }[]
      readonly xLabel?: string
    }
  | { readonly kind: 'bar'; readonly title: string; readonly labels: string[]; readonly values: number[] }
  | { readonly kind: 'pareto'; readonly title: string; readonly labels: string[]; readonly values: number[] }
  | {
      readonly kind: 'pca'
      readonly title: string
      readonly explained: number[]
      readonly cumulative: number[]
    }
