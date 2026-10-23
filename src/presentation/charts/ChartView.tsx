import type { ChartSpec } from '@domain/model/chart'
import type { GraphDoc } from '@domain/model/project'

const WIDTH = 460
const HEIGHT = 260
const PADDING = { left: 46, right: 14, top: 26, bottom: 30 }

function range(values: readonly number[]): { min: number; max: number } {
  const finite = values.filter((value) => Number.isFinite(value))
  if (finite.length === 0) return { min: 0, max: 1 }
  const min = Math.min(...finite)
  const max = Math.max(...finite)
  if (min === max) return { min: min - 1, max: max + 1 }
  return { min, max }
}

export function ChartView({ graph }: { graph?: GraphDoc }): React.JSX.Element {
  if (!graph) {
    return (
      <section className="chart-pane" aria-label="Gráficos">
        <header className="panel-title">Gráfico</header>
        <div className="chart-empty">Ejecute un análisis gráfico para visualizarlo aquí.</div>
      </section>
    )
  }
  const spec = graph.payload as unknown as ChartSpec
  return (
    <section className="chart-pane" aria-label="Gráficos">
      <header className="panel-title">
        Gráfico <span className="panel-subtitle">{graph.title}</span>
      </header>
      <div className="chart-content">
        <Chart spec={spec} />
      </div>
    </section>
  )
}

function Chart({ spec }: { spec: ChartSpec }): React.JSX.Element {
  switch (spec.kind) {
    case 'histogram':
      return <Histogram spec={spec} />
    case 'boxplot':
      return <Boxplot spec={spec} />
    case 'scatter':
      return <Scatter spec={spec} />
    case 'control':
      return <Control spec={spec} />
    case 'probability':
      return <Probability spec={spec} />
    case 'residuals':
      return <Residuals spec={spec} />
    case 'timeseries':
      return <TimeSeries spec={spec} />
    case 'line':
      return <LineChart spec={spec} />
    case 'bar':
    case 'pareto':
      return <BarChart spec={spec} />
    case 'pca':
      return <PcaChart spec={spec} />
    default:
      return <div className="chart-empty">Tipo de gráfico no soportado.</div>
  }
}

function Frame({ title, children }: { title: string; children: React.ReactNode }): React.JSX.Element {
  return (
    <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="chart-svg" role="img" aria-label={title}>
      <text x={WIDTH / 2} y={16} textAnchor="middle" className="chart-title">
        {title}
      </text>
      <rect
        x={PADDING.left}
        y={PADDING.top}
        width={WIDTH - PADDING.left - PADDING.right}
        height={HEIGHT - PADDING.top - PADDING.bottom}
        fill="#ffffff"
        stroke="#b0b0b0"
      />
      {children}
    </svg>
  )
}

function axes(min: number, max: number, ticks = 5): { y: (value: number) => number; ticks: number[] } {
  const plotTop = PADDING.top
  const plotBottom = HEIGHT - PADDING.bottom
  const scale = (value: number): number => plotBottom - ((value - min) / (max - min || 1)) * (plotBottom - plotTop)
  const values = Array.from({ length: ticks + 1 }, (_, index) => min + ((max - min) * index) / ticks)
  return { y: scale, ticks: values }
}

function XAxis({ ticks, labels }: { ticks: number[]; labels: string[] }): React.JSX.Element {
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  const plotBottom = HEIGHT - PADDING.bottom
  return (
    <g>
      <line x1={plotLeft} y1={plotBottom} x2={plotRight} y2={plotBottom} stroke="#808080" />
      {ticks.map((_value, index) => {
        const x = plotLeft + ((plotRight - plotLeft) * index) / Math.max(1, ticks.length - 1)
        return (
          <g key={index}>
            <line x1={x} y1={plotBottom} x2={x} y2={plotBottom + 3} stroke="#808080" />
            {labels[index] !== undefined && (
              <text x={x} y={plotBottom + 14} textAnchor="middle" className="chart-tick">
                {labels[index]}
              </text>
            )}
          </g>
        )
      })}
    </g>
  )
}

function YAxis({ min, max }: { min: number; max: number }): React.JSX.Element {
  const { y, ticks } = axes(min, max)
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  return (
    <g>
      {ticks.map((value, index) => (
        <g key={index}>
          <line x1={plotLeft - 3} y1={y(value)} x2={plotLeft} y2={y(value)} stroke="#808080" />
          <line x1={plotLeft} y1={y(value)} x2={plotRight} y2={y(value)} stroke="#e6e6e6" />
          <text x={plotLeft - 6} y={y(value) + 3} textAnchor="end" className="chart-tick">
            {formatTick(value)}
          </text>
        </g>
      ))}
    </g>
  )
}

function formatTick(value: number): string {
  if (Math.abs(value) >= 1000) return value.toExponential(1)
  return Number(value.toPrecision(3)).toString()
}

function linePath(points: readonly { x: number; y: number }[]): string {
  return points
    .map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x.toFixed(2)},${point.y.toFixed(2)}`)
    .join(' ')
}

function Histogram({ spec }: { spec: Extract<ChartSpec, { kind: 'histogram' }> }): React.JSX.Element {
  const bins = spec.bins ?? Math.max(5, Math.ceil(Math.log2(Math.max(2, spec.values.length)) + 1))
  const { min, max } = range(spec.values)
  const width = (max - min) / bins || 1
  const counts = new Array<number>(bins).fill(0)
  for (const value of spec.values) {
    const index = Math.min(bins - 1, Math.max(0, Math.floor((value - min) / width)))
    counts[index] = (counts[index] as number) + 1
  }
  const maxCount = Math.max(...counts, 1)
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  const plotBottom = HEIGHT - PADDING.bottom
  const plotTop = PADDING.top
  const barWidth = (plotRight - plotLeft) / bins
  return (
    <Frame title={spec.title}>
      <YAxis min={0} max={maxCount} />
      {counts.map((count, index) => {
        const height = ((plotBottom - plotTop) * count) / maxCount
        return <rect key={index} x={plotLeft + index * barWidth + 1} y={plotBottom - height} width={Math.max(1, barWidth - 2)} height={height} fill="#4f93b8" />
      })}
      <XAxis ticks={[min, (min + max) / 2, max]} labels={[formatTick(min), formatTick((min + max) / 2), formatTick(max)]} />
    </Frame>
  )
}

function Boxplot({ spec }: { spec: Extract<ChartSpec, { kind: 'boxplot' }> }): React.JSX.Element {
  const all = spec.groups.flatMap((group) => group.values)
  const { min, max } = range(all)
  const { y } = axes(min, max)
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  const width = (plotRight - plotLeft) / Math.max(1, spec.groups.length)
  return (
    <Frame title={spec.title}>
      <YAxis min={min} max={max} />
      {spec.groups.map((group, index) => {
        const ordered = [...group.values].sort((a, b) => a - b)
        const q = (p: number): number => ordered[Math.min(ordered.length - 1, Math.max(0, Math.floor((ordered.length - 1) * p)))] as number
        const q1 = q(0.25)
        const median = q(0.5)
        const q3 = q(0.75)
        const low = ordered[0] as number
        const high = ordered[ordered.length - 1] as number
        const center = plotLeft + width * index + width / 2
        return (
          <g key={group.label}>
            <line x1={center} y1={y(low)} x2={center} y2={y(high)} stroke="#333" />
            <rect x={center - width * 0.25} y={y(q3)} width={width * 0.5} height={Math.abs(y(q1) - y(q3))} fill="#cfe6f5" stroke="#333" />
            <line x1={center - width * 0.25} y1={y(median)} x2={center + width * 0.25} y2={y(median)} stroke="#0a6e9e" strokeWidth={2} />
            <text x={center} y={HEIGHT - 10} textAnchor="middle" className="chart-tick">
              {group.label}
            </text>
          </g>
        )
      })}
    </Frame>
  )
}

function Scatter({ spec }: { spec: Extract<ChartSpec, { kind: 'scatter' }> }): React.JSX.Element {
  const xRange = range(spec.points.map((point) => point.x))
  const yRange = range(spec.points.map((point) => point.y))
  const xScale = (value: number): number => PADDING.left + ((value - xRange.min) / (xRange.max - xRange.min || 1)) * (WIDTH - PADDING.left - PADDING.right)
  const yScale = (value: number): number => HEIGHT - PADDING.bottom - ((value - yRange.min) / (yRange.max - yRange.min || 1)) * (HEIGHT - PADDING.top - PADDING.bottom)
  return (
    <Frame title={spec.title}>
      <YAxis min={yRange.min} max={yRange.max} />
      {spec.points.map((point, index) => (
        <circle key={index} cx={xScale(point.x)} cy={yScale(point.y)} r={2.4} fill="#0a6e9e" opacity={0.8} />
      ))}
      <XAxis ticks={[xRange.min, (xRange.min + xRange.max) / 2, xRange.max]} labels={[formatTick(xRange.min), formatTick((xRange.min + xRange.max) / 2), formatTick(xRange.max)]} />
    </Frame>
  )
}

function Control({ spec }: { spec: Extract<ChartSpec, { kind: 'control' }> }): React.JSX.Element {
  const values = spec.points.map((point) => point.value)
  const { min, max } = range([...values, spec.lower, spec.upper])
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  const y = (value: number): number => HEIGHT - PADDING.bottom - ((value - min) / (max - min || 1)) * (HEIGHT - PADDING.top - PADDING.bottom)
  const x = (index: number): number => plotLeft + ((index - 1) / Math.max(1, values.length - 1)) * (plotRight - plotLeft)
  return (
    <Frame title={spec.title}>
      <YAxis min={min} max={max} />
      <line x1={plotLeft} y1={y(spec.center)} x2={plotRight} y2={y(spec.center)} stroke="#1a7f37" strokeDasharray="4 3" />
      <line x1={plotLeft} y1={y(spec.upper)} x2={plotRight} y2={y(spec.upper)} stroke="#b42318" strokeDasharray="4 3" />
      <line x1={plotLeft} y1={y(spec.lower)} x2={plotRight} y2={y(spec.lower)} stroke="#b42318" strokeDasharray="4 3" />
      <path d={linePath(spec.points.map((point) => ({ x: x(point.index), y: y(point.value) })))} fill="none" stroke="#0a6e9e" strokeWidth={1.4} />
      {spec.points.map((point) => (
        <circle key={point.index} cx={x(point.index)} cy={y(point.value)} r={spec.violations.includes(point.index) ? 3.6 : 2.2} fill={spec.violations.includes(point.index) ? '#b42318' : '#0a6e9e'} />
      ))}
      <XAxis ticks={[1, Math.ceil(values.length / 2), values.length]} labels={['1', String(Math.ceil(values.length / 2)), String(values.length)]} />
    </Frame>
  )
}

function Probability({ spec }: { spec: Extract<ChartSpec, { kind: 'probability' }> }): React.JSX.Element {
  const xs = spec.points.map((point) => point.x)
  const ys = spec.points.map((point) => point.y)
  const xRange = range(xs)
  const yRange = range([...ys, spec.intercept, spec.intercept + spec.slope * xRange.min, spec.intercept + spec.slope * xRange.max])
  const xScale = (value: number): number => PADDING.left + ((value - xRange.min) / (xRange.max - xRange.min || 1)) * (WIDTH - PADDING.left - PADDING.right)
  const yScale = (value: number): number => HEIGHT - PADDING.bottom - ((value - yRange.min) / (yRange.max - yRange.min || 1)) * (HEIGHT - PADDING.top - PADDING.bottom)
  return (
    <Frame title={spec.title}>
      <YAxis min={yRange.min} max={yRange.max} />
      {spec.points.map((point, index) => (
        <circle key={index} cx={xScale(point.x)} cy={yScale(point.y)} r={2.4} fill="#0a6e9e" />
      ))}
      <line x1={xScale(xRange.min)} y1={yScale(spec.intercept + spec.slope * xRange.min)} x2={xScale(xRange.max)} y2={yScale(spec.intercept + spec.slope * xRange.max)} stroke="#b42318" />
      <XAxis ticks={[xRange.min, (xRange.min + xRange.max) / 2, xRange.max]} labels={[formatTick(xRange.min), '', formatTick(xRange.max)]} />
    </Frame>
  )
}

function Residuals({ spec }: { spec: Extract<ChartSpec, { kind: 'residuals' }> }): React.JSX.Element {
  const { min, max } = range(spec.residuals)
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  const y = (value: number): number => HEIGHT - PADDING.bottom - ((value - min) / (max - min || 1)) * (HEIGHT - PADDING.top - PADDING.bottom)
  const x = (index: number): number => plotLeft + (index / Math.max(1, spec.fitted.length - 1)) * (plotRight - plotLeft)
  return (
    <Frame title={spec.title}>
      <YAxis min={min} max={max} />
      <line x1={plotLeft} y1={y(0)} x2={plotRight} y2={y(0)} stroke="#1a7f37" strokeDasharray="4 3" />
      <path d={linePath(spec.residuals.map((value, index) => ({ x: x(index), y: y(value) })))} fill="none" stroke="#0a6e9e" strokeWidth={1.3} />
      {spec.residuals.map((value, index) => (
        <circle key={index} cx={x(index)} cy={y(value)} r={2} fill="#0a6e9e" />
      ))}
      <XAxis ticks={[0, spec.fitted.length - 1]} labels={['1', String(spec.fitted.length)]} />
    </Frame>
  )
}

function TimeSeries({ spec }: { spec: Extract<ChartSpec, { kind: 'timeseries' }> }): React.JSX.Element {
  const observed = spec.values
  const forecast = spec.forecast ?? []
  const { min, max } = range([...observed, ...forecast])
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  const totalPoints = Math.max(observed.length + forecast.length, 2)
  const x = (index: number): number => plotLeft + (index / (totalPoints - 1)) * (plotRight - plotLeft)
  const y = (value: number): number => HEIGHT - PADDING.bottom - ((value - min) / (max - min || 1)) * (HEIGHT - PADDING.top - PADDING.bottom)
  return (
    <Frame title={spec.title}>
      <YAxis min={min} max={max} />
      <path d={linePath(observed.map((value, index) => ({ x: x(index), y: y(value) })))} fill="none" stroke="#0a6e9e" strokeWidth={1.5} />
      {forecast.length > 0 && (
        <path d={linePath(forecast.map((value, index) => ({ x: x(observed.length + index), y: y(value) })))} fill="none" stroke="#b42318" strokeWidth={1.5} strokeDasharray="4 3" />
      )}
      <XAxis ticks={['1', String(observed.length), String(totalPoints)].map((_, index) => index)} labels={['1', String(observed.length), String(totalPoints)]} />
    </Frame>
  )
}

function LineChart({ spec }: { spec: Extract<ChartSpec, { kind: 'line' }> }): React.JSX.Element {
  const all = spec.series.flatMap((series) => series.values)
  const { min, max } = range(all)
  const colors = ['#0a6e9e', '#b42318', '#1a7f37', '#8a4f9e']
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  const count = Math.max(...spec.series.map((series) => series.values.length), 2)
  const x = (index: number): number => plotLeft + (index / (count - 1)) * (plotRight - plotLeft)
  const y = (value: number): number => HEIGHT - PADDING.bottom - ((value - min) / (max - min || 1)) * (HEIGHT - PADDING.top - PADDING.bottom)
  return (
    <Frame title={spec.title}>
      <YAxis min={min} max={max} />
      {spec.series.map((series, seriesIndex) => (
        <path key={series.label} d={linePath(series.values.map((value, index) => ({ x: x(index), y: y(value) })))} fill="none" stroke={colors[seriesIndex % colors.length]} strokeWidth={1.5} />
      ))}
      {spec.series.map((series, index) => (
        <text key={series.label} x={PADDING.left + 4 + index * 90} y={HEIGHT - 6} className="chart-tick" fill={colors[index % colors.length]}>
          {series.label}
        </text>
      ))}
    </Frame>
  )
}

function BarChart({ spec }: { spec: Extract<ChartSpec, { kind: 'bar' | 'pareto' }> }): React.JSX.Element {
  const maxValue = Math.max(...spec.values, 1)
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  const width = (plotRight - plotLeft) / Math.max(1, spec.values.length)
  const plotBottom = HEIGHT - PADDING.bottom
  const plotTop = PADDING.top
  return (
    <Frame title={spec.title}>
      <YAxis min={0} max={maxValue} />
      {spec.values.map((value, index) => {
        const height = ((plotBottom - plotTop) * value) / maxValue
        return (
          <g key={index}>
            <rect x={plotLeft + index * width + 2} y={plotBottom - height} width={Math.max(1, width - 4)} height={height} fill="#4f93b8" />
            <text x={plotLeft + index * width + width / 2} y={plotBottom + 12} textAnchor="middle" className="chart-tick">
              {spec.labels[index] ?? ''}
            </text>
          </g>
        )
      })}
    </Frame>
  )
}

function PcaChart({ spec }: { spec: Extract<ChartSpec, { kind: 'pca' }> }): React.JSX.Element {
  const cumulative = spec.cumulative
  const { min, max } = range(cumulative)
  const plotLeft = PADDING.left
  const plotRight = WIDTH - PADDING.right
  const x = (index: number): number => plotLeft + (index / Math.max(1, cumulative.length - 1)) * (plotRight - plotLeft)
  const y = (value: number): number => HEIGHT - PADDING.bottom - ((value - min) / (max - min || 1)) * (HEIGHT - PADDING.top - PADDING.bottom)
  return (
    <Frame title={spec.title}>
      <YAxis min={min} max={max} />
      {spec.explained.map((value, index) => (
        <rect key={index} x={x(index) - 6} y={y(value)} width={12} height={Math.max(1, HEIGHT - PADDING.bottom - y(value))} fill="#4f93b8" opacity={0.6} />
      ))}
      <path d={linePath(cumulative.map((value, index) => ({ x: x(index), y: y(value) })))} fill="none" stroke="#b42318" strokeWidth={1.6} />
    </Frame>
  )
}
