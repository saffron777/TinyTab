/** Registro de análisis (parte 2): SPC, capacidad, MSA, DOE, series, fiabilidad, multivariante, potencia. */
import { cChart, cusumChart, ewmaChart, individualsChart, pChart, processCapability, uChart, xbarRChart } from '../stats/spc'
import { gageRR } from '../stats/msa'
import { analyzeFactorial } from '../stats/doe'
import { autocorrelation, decompose, doubleExponentialSmoothing, fitAr, movingAverage, partialAutocorrelation, simpleExponentialSmoothing, wintersSmoothing } from '../stats/timeseries'
import { kaplanMeier, lognormalFit, probabilityPlotPoints, weibullFit } from '../stats/reliability'
import { factorAnalysis, kmeans, lda, pca } from '../stats/multivariate'
import { ocCurve, powerOneProportion, powerOneSampleT, powerOneWayAnova, sampleSizeOneProportion, sampleSizeOneSampleT, sampleSizeTwoSampleT } from '../stats/power'
import { kruskalWallis, mannWhitney, runsTest, signTest, spearmanCorrelation, wilcoxonSignedRank } from '../stats/tests'
import { DISTRIBUTIONS } from '../stats/distributions'
import { err, ok } from '../model/result'
import type { AnalysisDefinition } from './types'
import { colText, n, num, p, pnum, pstr, sections, table, text } from './helpers'

const chartXbarR: AnalysisDefinition = {
  id: 'chart-xbar-r',
  label: 'Carta X̄-R',
  category: 'Control de calidad',
  description: 'Cartas de medias y rangos para subgrupos de tamaño constante.',
  numericColumns: [{ key: 'x', label: 'Mediciones (por subgrupos consecutivos)', required: true }],
  params: [{ key: 'size', label: 'Tamaño del subgrupo', kind: 'integer', defaultValue: 5 }],
  run: (input) => {
    const values = num(input, 'x')
    const size = Math.max(2, Math.floor(pnum(input, 'size', 5)))
    const groups = [] as { values: number[] }[]
    for (let i = 0; i + size <= values.length; i += size) groups.push({ values: values.slice(i, i + size) })
    const result = xbarRChart(groups)
    if (!result.ok) return result
    return ok({
      id: 'chart-xbar-r',
      title: 'Carta X̄-R',
      sections: sections(
        table('Límites X̄', ['Línea central', 'LSC', 'LIC'], [[n(result.value.xbar.centerLine), n(result.value.xbar.upperLimit), n(result.value.xbar.lowerLimit)]]),
        table('Límites R', ['Línea central', 'LSC', 'LIC'], [[n(result.value.range.centerLine), n(result.value.range.upperLimit), n(result.value.range.lowerLimit)]]),
        table('Detección de causas especiales', ['Carta', 'Puntos', 'Violaciones X̄', 'Violaciones R'], [['X̄-R', String(groups.length), String(result.value.xbar.violations), String(result.value.range.violations)]])
      ),
      charts: [
        { kind: 'control', title: 'Carta X̄', points: result.value.xbar.points.map((point) => ({ index: point.index, value: point.value })), center: result.value.xbar.centerLine, lower: result.value.xbar.lowerLimit, upper: result.value.xbar.upperLimit, violations: result.value.xbar.points.filter((point) => point.outOfControl).map((point) => point.index), yLabel: 'Media' },
        { kind: 'control', title: 'Carta R', points: result.value.range.points.map((point) => ({ index: point.index, value: point.value })), center: result.value.range.centerLine, lower: result.value.range.lowerLimit, upper: result.value.range.upperLimit, violations: result.value.range.points.filter((point) => point.outOfControl).map((point) => point.index), yLabel: 'Rango' }
      ]
    })
  }
}

const chartImr: AnalysisDefinition = {
  id: 'chart-imr',
  label: 'Carta I-MR',
  category: 'Control de calidad',
  description: 'Carta de individuos y rango móvil.',
  numericColumns: [{ key: 'x', label: 'Observaciones', required: true }],
  params: [],
  run: (input) => {
    const result = individualsChart(num(input, 'x'))
    if (!result.ok) return result
    return ok({
      id: 'chart-imr',
      title: 'Carta I-MR',
      sections: sections(
        table('Carta I', ['Línea central', 'LSC', 'LIC'], [[n(result.value.individuals.centerLine), n(result.value.individuals.upperLimit), n(result.value.individuals.lowerLimit)]]),
        table('Carta MR', ['Línea central', 'LSC', 'LIC'], [[n(result.value.movingRange.centerLine), n(result.value.movingRange.upperLimit), n(result.value.movingRange.lowerLimit)]])
      ),
      charts: [
        { kind: 'control', title: 'Carta I', points: result.value.individuals.points.map((point) => ({ index: point.index, value: point.value })), center: result.value.individuals.centerLine, lower: result.value.individuals.lowerLimit, upper: result.value.individuals.upperLimit, violations: result.value.individuals.points.filter((point) => point.outOfControl).map((point) => point.index), yLabel: 'Valor' },
        { kind: 'control', title: 'Carta MR', points: result.value.movingRange.points.map((point) => ({ index: point.index, value: point.value })), center: result.value.movingRange.centerLine, lower: result.value.movingRange.lowerLimit, upper: result.value.movingRange.upperLimit, violations: result.value.movingRange.points.filter((point) => point.outOfControl).map((point) => point.index), yLabel: 'Rango móvil' }
      ]
    })
  }
}

const chartP: AnalysisDefinition = {
  id: 'chart-p',
  label: 'Carta P',
  category: 'Control de calidad',
  description: 'Proporción de defectuosos con subgrupos de distinto tamaño.',
  numericColumns: [
    { key: 'defective', label: 'Defectuosos', required: true },
    { key: 'sample', label: 'Tamaño de muestra', required: true }
  ],
  params: [],
  run: (input) => {
    const result = pChart(num(input, 'defective'), num(input, 'sample'))
    if (!result.ok) return result
    return ok({
      id: 'chart-p',
      title: 'Carta P',
      sections: sections(table('Límites', ['Línea central', 'LSC', 'LIC'], [[n(result.value.centerLine), n(result.value.upperLimit), n(result.value.lowerLimit)]]), table('Violaciones', ['Puntos fuera', 'Total'], [[String(result.value.violations), String(result.value.points.length)]])),
      charts: [{ kind: 'control', title: 'Carta P', points: result.value.points.map((point) => ({ index: point.index, value: point.value })), center: result.value.centerLine, lower: result.value.lowerLimit, upper: result.value.upperLimit, violations: result.value.points.filter((point) => point.outOfControl).map((point) => point.index), yLabel: 'Proporción' }]
    })
  }
}

const chartC: AnalysisDefinition = {
  id: 'chart-c',
  label: 'Carta C',
  category: 'Control de calidad',
  description: 'Número de defectos por unidad constante.',
  numericColumns: [{ key: 'defects', label: 'Defectos', required: true }],
  params: [],
  run: (input) => {
    const result = cChart(num(input, 'defects'))
    if (!result.ok) return result
    return ok({
      id: 'chart-c',
      title: 'Carta C',
      sections: sections(table('Límites', ['Línea central', 'LSC', 'LIC'], [[n(result.value.centerLine), n(result.value.upperLimit), n(result.value.lowerLimit)]])),
      charts: [{ kind: 'control', title: 'Carta C', points: result.value.points.map((point) => ({ index: point.index, value: point.value })), center: result.value.centerLine, lower: result.value.lowerLimit, upper: result.value.upperLimit, violations: result.value.points.filter((point) => point.outOfControl).map((point) => point.index), yLabel: 'Defectos' }]
    })
  }
}

const chartU: AnalysisDefinition = {
  id: 'chart-u',
  label: 'Carta U',
  category: 'Control de calidad',
  description: 'Defectos por unidad cuando el tamaño de la unidad varía.',
  numericColumns: [
    { key: 'defects', label: 'Defectos', required: true },
    { key: 'units', label: 'Unidades inspeccionadas', required: true }
  ],
  params: [],
  run: (input) => {
    const result = uChart(num(input, 'defects'), num(input, 'units'))
    if (!result.ok) return result
    return ok({
      id: 'chart-u',
      title: 'Carta U',
      sections: sections(table('Límites', ['Línea central', 'LSC', 'LIC'], [[n(result.value.centerLine), n(result.value.upperLimit), n(result.value.lowerLimit)]])),
      charts: [{ kind: 'control', title: 'Carta U', points: result.value.points.map((point) => ({ index: point.index, value: point.value })), center: result.value.centerLine, lower: result.value.lowerLimit, upper: result.value.upperLimit, violations: result.value.points.filter((point) => point.outOfControl).map((point) => point.index), yLabel: 'Defectos/unidad' }]
    })
  }
}

const chartEwma: AnalysisDefinition = {
  id: 'chart-ewma',
  label: 'Carta EWMA',
  category: 'Control de calidad',
  description: 'Media móvil exponencialmente ponderada.',
  numericColumns: [{ key: 'x', label: 'Observaciones', required: true }],
  params: [{ key: 'lambda', label: 'Peso λ', kind: 'number', defaultValue: 0.2 }],
  run: (input) => {
    const result = ewmaChart(num(input, 'x'), pnum(input, 'lambda', 0.2))
    if (!result.ok) return result
    return ok({
      id: 'chart-ewma',
      title: 'Carta EWMA',
      sections: sections(table('Límites', ['Línea central', 'LSC', 'LIC'], [[n(result.value.centerLine), n(result.value.upperLimit), n(result.value.lowerLimit)]])),
      charts: [{ kind: 'control', title: 'Carta EWMA', points: result.value.points.map((point) => ({ index: point.index, value: point.value })), center: result.value.centerLine, lower: result.value.lowerLimit, upper: result.value.upperLimit, violations: result.value.points.filter((point) => point.outOfControl).map((point) => point.index), yLabel: 'EWMA' }]
    })
  }
}

const chartCusum: AnalysisDefinition = {
  id: 'chart-cusum',
  label: 'Carta CUSUM',
  category: 'Control de calidad',
  description: 'Sumas acumuladas tabulares (k = 0,5).',
  numericColumns: [{ key: 'x', label: 'Observaciones', required: true }],
  params: [{ key: 'k', label: 'Valor de referencia k', kind: 'number', defaultValue: 0.5 }],
  run: (input) => {
    const result = cusumChart(num(input, 'x'), undefined, pnum(input, 'k', 0.5))
    if (!result.ok) return result
    return ok({
      id: 'chart-cusum',
      title: 'Carta CUSUM',
      sections: sections(table('Resumen', ['Puntos', 'Violaciones'], [[String(result.value.points.length), String(result.value.violations)]]))
    })
  }
}

const capabilityDef: AnalysisDefinition = {
  id: 'capability',
  label: 'Análisis de capacidad',
  category: 'Capacidad',
  description: 'Cp, Cpk, Pp, Ppk y PPM esperados.',
  numericColumns: [{ key: 'x', label: 'Mediciones', required: true }],
  params: [
    { key: 'lsl', label: 'Límite inferior (vacío = sin límite)', kind: 'number', defaultValue: '', optional: true },
    { key: 'usl', label: 'Límite superior (vacío = sin límite)', kind: 'number', defaultValue: '', optional: true },
    { key: 'method', label: 'Método', kind: 'select', defaultValue: 'normal', options: [{ value: 'normal', label: 'Normal' }, { value: 'nonnormal', label: 'No normal (empírico)' }] }
  ],
  run: (input) => {
    const lslRaw = input.params['lsl']
    const uslRaw = input.params['usl']
    const lsl = lslRaw === '' || lslRaw === undefined ? undefined : Number(lslRaw)
    const usl = uslRaw === '' || uslRaw === undefined ? undefined : Number(uslRaw)
    if (lsl === undefined && usl === undefined) return err('INVALID_PARAMETER', 'Indique al menos un límite de especificación.')
    const result = processCapability(num(input, 'x'), lsl, usl, { method: pstr(input, 'method', 'normal') as 'normal' | 'nonnormal' })
    if (!result.ok) return result
    const value = result.value
    return ok({
      id: 'capability',
      title: 'Análisis de capacidad',
      sections: sections(
        table('Índices', ['Cp', 'Cpk', 'Pp', 'Ppk'], [[n(value.cp, 3), n(value.cpk, 3), n(value.pp, 3), n(value.ppk, 3)]]),
        table('Especificaciones', ['LIE', 'LSE', 'Media', 'σ'], [[n(value.lowerSpec), n(value.upperSpec), n(value.mean), n(value.sd)]]),
        table('PPM esperados', ['Debajo LIE', 'Sobre LSE', 'Total'], [[n(value.ppmBelow, 1), n(value.ppmAbove, 1), n(value.ppmTotal, 1)]])
      ),
      charts: [{ kind: 'histogram', title: 'Histograma con especificaciones', values: num(input, 'x') as number[] }]
    })
  }
}

const gageDef: AnalysisDefinition = {
  id: 'gage-rr',
  label: 'Gage R&R (cruzado)',
  category: 'Sistemas de medida',
  description: 'Estudio de repetibilidad y reproducibilidad por ANOVA.',
  numericColumns: [{ key: 'value', label: 'Medición', required: true }],
  textColumns: [
    { key: 'part', label: 'Pieza', required: true },
    { key: 'operator', label: 'Operador', required: true }
  ],
  params: [{ key: 'trial', label: 'Columna de repetición (opcional)', kind: 'number', defaultValue: '' , optional: true }],
  run: (input) => {
    const values = num(input, 'value')
    const parts = colText(input, 'part')
    const operators = colText(input, 'operator')
    const rows = values.map((value, index) => ({ part: parts[index] ?? String(index), operator: operators[index] ?? 'A', trial: index, value }))
    const result = gageRR(rows)
    if (!result.ok) return result
    return ok({
      id: 'gage-rr',
      title: 'Gage R&R (cruzado)',
      sections: sections(
        table('Resumen', ['Repetibilidad (EV)', 'Reproducibilidad (AV)', 'GRR', 'Variación total', '% Contribución', '% Estudio', 'nDC'], [[n(result.value.repeatability), n(result.value.reproducibility), n(result.value.grr), n(result.value.totalVariation), n(result.value.percentContribution, 2), n(result.value.percentStudy, 2), n(result.value.ndc, 2)]]),
        table('ANOVA', ['Fuente', 'GL', 'SC', 'MC', 'F', 'Valor p'], result.value.table.map((row) => [row.source, String(row.df), n(row.ss, 3), n(row.ms, 3), n(row.f, 3), p(row.p)]))
      )
    })
  }
}

const doeAnalyze: AnalysisDefinition = {
  id: 'doe-analyze',
  label: 'Analizar diseño factorial (efectos)',
  category: 'DOE',
  description: 'Ajusta un modelo con factores codificados (−1/+1) y sus interacciones de 2 vías.',
  numericColumns: [{ key: 'y', label: 'Respuesta', required: true }, { key: 'x', label: 'Factores codificados', required: true, multiple: true }],
  params: [],
  run: (input) => {
    const keys = Object.keys(input.numeric)
    const response = num(input, keys[0] ?? '')
    const coded = keys.slice(1).map((key) => num(input, key))
    if (coded.length < 1) return err('INVALID_INPUT', 'Seleccione al menos un factor codificado.')
    const factors = keys.slice(1).map((key) => ({ name: key, low: -1, high: 1 }))
    const design = {
      type: 'factorial' as const,
      factors,
      runs: response.map((_, index) => ({ standardOrder: index + 1, runOrder: index + 1, coded: coded.map((column) => column[index] as number), actual: coded.map((column) => column[index] as number) })),
      replicateCount: 1,
      centerPoints: 0,
      seed: 20261023
    }
    const result = analyzeFactorial(design, response)
    if (!result.ok) return result
    return ok({
      id: 'doe-analyze',
      title: 'Análisis factorial (efectos)',
      sections: sections(
        table('Efectos estimados', ['Término', 'Efecto', 'Coef.', 'EE', 't', 'Valor p'], result.value.terms.map((term, index) => [term, index === 0 ? '' : n(result.value.effects[index - 1] as number, 4), n(result.value.coefficients[index] as number, 4), n(result.value.standardErrors[index] as number, 4), n(result.value.t[index] as number, 2), p(result.value.pValues[index] as number)])),
        text('R²', `R² = ${n(result.value.rSquared, 4)}`)
      )
    })
  }
}

const acfDef: AnalysisDefinition = {
  id: 'timeseries-acf',
  label: 'Función de autocorrelación (ACF)',
  category: 'Series temporales',
  description: 'Autocorrelaciones y banda de confianza.',
  numericColumns: [{ key: 'x', label: 'Serie', required: true }],
  params: [{ key: 'lags', label: 'Número de rezagos', kind: 'integer', defaultValue: 20 }],
  run: (input) => {
    const result = autocorrelation(num(input, 'x'), Math.floor(pnum(input, 'lags', 20)))
    if (!result.ok) return result
    return ok({
      id: 'timeseries-acf',
      title: 'Función de autocorrelación',
      sections: sections(table('ACF', ['Rezago', 'Autocorrelación'], result.value.lags.map((lag, index) => [String(lag), n(result.value.values[index] as number, 4)]))),
      charts: [{ kind: 'line', title: 'ACF', series: [{ label: 'ACF', values: result.value.values }], xLabel: 'Rezago' }]
    })
  }
}

const pacfDef: AnalysisDefinition = {
  id: 'timeseries-pacf',
  label: 'Autocorrelación parcial (PACF)',
  category: 'Series temporales',
  description: 'PACF por Durbin-Levinson.',
  numericColumns: [{ key: 'x', label: 'Serie', required: true }],
  params: [{ key: 'lags', label: 'Número de rezagos', kind: 'integer', defaultValue: 20 }],
  run: (input) => {
    const result = partialAutocorrelation(num(input, 'x'), Math.floor(pnum(input, 'lags', 20)))
    if (!result.ok) return result
    return ok({ id: 'timeseries-pacf', title: 'Autocorrelación parcial', sections: sections(table('PACF', ['Rezago', 'PACF'], result.value.lags.map((lag, index) => [String(lag), n(result.value.values[index] as number, 4)]))), charts: [{ kind: 'line', title: 'PACF', series: [{ label: 'PACF', values: result.value.values }], xLabel: 'Rezago' }] })
  }
}

const movingAverageDef: AnalysisDefinition = {
  id: 'timeseries-moving-average',
  label: 'Media móvil',
  category: 'Series temporales',
  description: 'Suavizado por media móvil centrada.',
  numericColumns: [{ key: 'x', label: 'Serie', required: true }],
  params: [{ key: 'window', label: 'Ventana', kind: 'integer', defaultValue: 3 }],
  run: (input) => {
    const result = movingAverage(num(input, 'x'), Math.max(2, Math.floor(pnum(input, 'window', 3))))
    if (!result.ok) return result
    return ok({ id: 'timeseries-moving-average', title: 'Media móvil', sections: sections(table('Ajuste', ['t', 'Valor', 'Media móvil'], num(input, 'x').map((value, index) => [String(index + 1), n(value), n(result.value.fitted[index] as number)]))), charts: [{ kind: 'timeseries', title: 'Serie y media móvil', values: num(input, 'x'), forecast: result.value.fitted.filter((value) => Number.isFinite(value)) }] })
  }
}

const sesDef: AnalysisDefinition = {
  id: 'timeseries-ses',
  label: 'Suavizado exponencial simple',
  category: 'Series temporales',
  description: 'Suavizado exponencial simple con pronóstico a un paso.',
  numericColumns: [{ key: 'x', label: 'Serie', required: true }],
  params: [{ key: 'alpha', label: 'α', kind: 'number', defaultValue: 0.2 }],
  run: (input) => {
    const result = simpleExponentialSmoothing(num(input, 'x'), pnum(input, 'alpha', 0.2))
    if (!result.ok) return result
    return ok({ id: 'timeseries-ses', title: 'Suavizado exponencial simple', sections: sections(table('Resumen', ['α', 'Pronóstico siguiente'], [[n(pnum(input, 'alpha', 0.2), 2), n(result.value.forecast)]])), charts: [{ kind: 'timeseries', title: 'Serie y suavizado', values: num(input, 'x'), forecast: result.value.fitted }] })
  }
}

const desDef: AnalysisDefinition = {
  id: 'timeseries-des',
  label: 'Suavizado exponencial doble',
  category: 'Series temporales',
  description: 'Suavizado de Holt (nivel y tendencia).',
  numericColumns: [{ key: 'x', label: 'Serie', required: true }],
  params: [
    { key: 'alpha', label: 'α', kind: 'number', defaultValue: 0.2 },
    { key: 'beta', label: 'β', kind: 'number', defaultValue: 0.1 }
  ],
  run: (input) => {
    const result = doubleExponentialSmoothing(num(input, 'x'), pnum(input, 'alpha', 0.2), pnum(input, 'beta', 0.1))
    if (!result.ok) return result
    return ok({ id: 'timeseries-des', title: 'Suavizado exponencial doble', sections: sections(table('Pronóstico', ['Siguiente valor'], [[n(result.value.forecast)]])), charts: [{ kind: 'timeseries', title: 'Serie y suavizado', values: num(input, 'x'), forecast: result.value.fitted }] })
  }
}

const wintersDef: AnalysisDefinition = {
  id: 'timeseries-winters',
  label: 'Suavizado de Winters',
  category: 'Series temporales',
  description: 'Suavizado estacional (nivel, tendencia y estacionalidad).',
  numericColumns: [{ key: 'x', label: 'Serie', required: true }],
  params: [
    { key: 'period', label: 'Periodo estacional', kind: 'integer', defaultValue: 12 },
    { key: 'alpha', label: 'α', kind: 'number', defaultValue: 0.2 },
    { key: 'beta', label: 'β', kind: 'number', defaultValue: 0.1 },
    { key: 'gamma', label: 'γ', kind: 'number', defaultValue: 0.1 }
  ],
  run: (input) => {
    const result = wintersSmoothing(num(input, 'x'), Math.max(2, Math.floor(pnum(input, 'period', 12))), pnum(input, 'alpha', 0.2), pnum(input, 'beta', 0.1), pnum(input, 'gamma', 0.1))
    if (!result.ok) return result
    return ok({ id: 'timeseries-winters', title: 'Suavizado de Winters', sections: sections(table('Pronóstico', result.value.forecast.map((_, index) => `t+${index + 1}`), [result.value.forecast.map((value) => n(value))])), charts: [{ kind: 'timeseries', title: 'Serie y suavizado', values: num(input, 'x'), forecast: [...result.value.fitted, ...result.value.forecast] }] })
  }
}

const decomposeDef: AnalysisDefinition = {
  id: 'timeseries-decompose',
  label: 'Descomposición estacional',
  category: 'Series temporales',
  description: 'Tendencia por media móvil centrada y patrón estacional.',
  numericColumns: [{ key: 'x', label: 'Serie', required: true }],
  params: [{ key: 'period', label: 'Periodo', kind: 'integer', defaultValue: 12 }],
  run: (input) => {
    const result = decompose(num(input, 'x'), Math.max(2, Math.floor(pnum(input, 'period', 12))))
    if (!result.ok) return result
    return ok({ id: 'timeseries-decompose', title: 'Descomposición estacional', sections: sections(table('Patrón estacional', ['Posición', 'Efecto'], Array.from({ length: result.value.period }, (_, index) => [String(index + 1), n(result.value.seasonal[index] as number)]))), charts: [{ kind: 'timeseries', title: 'Serie original', values: num(input, 'x') }] })
  }
}

const arimaDef: AnalysisDefinition = {
  id: 'timeseries-ar',
  label: 'Modelo AR y pronóstico',
  category: 'Series temporales',
  description: 'Ajuste AR(p) por Yule-Walker con selección AIC y Ljung-Box.',
  numericColumns: [{ key: 'x', label: 'Serie', required: true }],
  params: [
    { key: 'order', label: 'Orden máximo', kind: 'integer', defaultValue: 3 },
    { key: 'horizon', label: 'Horizonte de pronóstico', kind: 'integer', defaultValue: 5 }
  ],
  run: (input) => {
    const result = fitAr(num(input, 'x'), Math.floor(pnum(input, 'order', 3)), Math.floor(pnum(input, 'horizon', 5)))
    if (!result.ok) return result
    return ok({
      id: 'timeseries-ar',
      title: 'Modelo AR y pronóstico',
      sections: sections(
        table('Modelo', ['Orden', 'Intercepto', 'AIC', 'Varianza residual'], [[String(result.value.order), n(result.value.intercept), n(result.value.aic, 2), n(result.value.residualVariance)]]),
        table('Coeficientes AR', result.value.coefficients.map((_, index) => `φ${index + 1}`), [result.value.coefficients.map((value) => n(value, 4))]),
        table('Ljung-Box', ['Estadístico', 'GL', 'Valor p'], [[n(result.value.ljungBox.statistic, 3), String(result.value.ljungBox.df), p(result.value.ljungBox.pValue)]]),
        table('Pronóstico', result.value.forecast.map((_, index) => `t+${index + 1}`), [result.value.forecast.map((value) => n(value))])
      ),
      charts: [{ kind: 'timeseries', title: 'Serie y pronóstico', values: num(input, 'x'), forecast: result.value.forecast }]
    })
  }
}

const weibullDef: AnalysisDefinition = {
  id: 'reliability-weibull',
  label: 'Ajuste de distribución Weibull',
  category: 'Fiabilidad',
  description: 'Estimación por máxima verosimilitud y gráfico de probabilidad.',
  numericColumns: [{ key: 'x', label: 'Tiempos de falla', required: true }],
  params: [],
  run: (input) => {
    const values = num(input, 'x')
    const result = weibullFit(values)
    if (!result.ok) return result
    const points = probabilityPlotPoints(values)
    const slope = 1 / result.value.shape
    const intercept = -Math.log(result.value.scale) / result.value.shape
    return ok({
      id: 'reliability-weibull',
      title: 'Ajuste de distribución Weibull',
      sections: sections(
        table('Parámetros (MLE)', ['Forma (β)', 'Escala (η)', 'Log-verosimilitud', 'AIC'], [[n(result.value.shape, 4), n(result.value.scale, 4), n(result.value.logLikelihood, 3), n(result.value.aic, 3)]]),
        table('Resumen', ['N', 'Media estimada'], [[String(result.value.n), n(result.value.scale * Math.exp(lgammaApprox(1 + 1 / result.value.shape)))]])
      ),
      charts: [{ kind: 'probability', title: 'Gráfico de probabilidad Weibull', points, slope, intercept }]
    })
  }
}

function lgammaApprox(x: number): number {
  const coef = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7]
  if (x < 0.5) return Math.log(Math.PI / Math.sin(Math.PI * x)) - lgammaApprox(1 - x)
  const z = x - 1
  let a = coef[0] as number
  const t = z + 7.5
  for (let i = 1; i < coef.length; i += 1) a += (coef[i] as number) / (z + i)
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(a)
}

const kmDef: AnalysisDefinition = {
  id: 'reliability-kaplan-meier',
  label: 'Kaplan-Meier',
  category: 'Fiabilidad',
  description: 'Curva de supervivencia con censura.',
  numericColumns: [{ key: 'time', label: 'Tiempo', required: true }],
  textColumns: [],
  params: [{ key: 'event', label: 'Evento observado (columna 0/1 opcional)', kind: 'number', defaultValue: '', optional: true }],
  run: (input) => {
    const times = num(input, 'time')
    const eventColumn = input.numeric['event']
    const events = eventColumn ? eventColumn.map((value) => value === 1) : times.map(() => true)
    const result = kaplanMeier(times, events)
    if (!result.ok) return result
    return ok({
      id: 'reliability-kaplan-meier',
      title: 'Kaplan-Meier',
      sections: sections(
        table('Supervivencia', ['t', 'S(t)', 'EE', 'En riesgo', 'Eventos'], result.value.times.map((time, index) => [n(time), n(result.value.survival[index] as number, 4), n(result.value.standardError[index] as number, 4), String(result.value.atRisk[index]), String(result.value.events[index])])),
        table('Mediana de supervivencia', ['Mediana'], [[n(result.value.median)]])
      ),
      charts: [{ kind: 'line', title: 'Curva de supervivencia', series: [{ label: 'S(t)', values: result.value.survival }], xLabel: 'Tiempo' }]
    })
  }
}

const pcaDef: AnalysisDefinition = {
  id: 'multivariate-pca',
  label: 'Análisis de componentes principales',
  category: 'Multivariante',
  description: 'PCA sobre la matriz de correlaciones con cargas y puntuaciones.',
  numericColumns: [{ key: 'x', label: 'Variables', required: true, multiple: true }],
  params: [],
  run: (input) => {
    const keys = Object.keys(input.numeric)
    if (keys.length < 2) return err('INVALID_INPUT', 'Seleccione al menos dos variables.')
    const columns = keys.map((key) => num(input, key))
    const rows = columns[0]?.map((_, index) => columns.map((column) => column[index] as number)) ?? []
    const result = pca(rows, keys)
    if (!result.ok) return result
    return ok({
      id: 'multivariate-pca',
      title: 'Análisis de componentes principales',
      sections: sections(
        table('Varianza explicada', ['Componente', 'Eigenvalor', '% explicado', '% acumulado'], result.value.eigenvalues.map((value, index) => [`PC${index + 1}`, n(value, 4), n(result.value.explained[index] as number, 2), n(result.value.cumulative[index] as number, 2)])),
        table('Cargas', ['Variable', ...result.value.eigenvalues.map((_, index) => `PC${index + 1}`)], result.value.loadings.map((row, index) => [result.value.labels[index] ?? '', ...row.map((value) => n(value, 3))]))
      ),
      charts: [{ kind: 'pca', title: 'Varianza explicada', explained: result.value.explained, cumulative: result.value.cumulative }]
    })
  }
}

const factorDef: AnalysisDefinition = {
  id: 'multivariate-factor',
  label: 'Análisis factorial',
  category: 'Multivariante',
  description: 'Extracción por componentes principales con rotación varimax.',
  numericColumns: [{ key: 'x', label: 'Variables', required: true, multiple: true }],
  params: [{ key: 'factors', label: 'Número de factores', kind: 'integer', defaultValue: 2 }],
  run: (input) => {
    const keys = Object.keys(input.numeric)
    const columns = keys.map((key) => num(input, key))
    const rows = columns[0]?.map((_, index) => columns.map((column) => column[index] as number)) ?? []
    const result = factorAnalysis(rows, keys, Math.max(1, Math.floor(pnum(input, 'factors', 2))))
    if (!result.ok) return result
    return ok({
      id: 'multivariate-factor',
      title: 'Análisis factorial',
      sections: sections(
        table('Cargas rotadas (varimax)', ['Variable', ...result.value.loadings[0]?.map((_, index) => `Factor ${index + 1}`) ?? []], result.value.loadings.map((row, index) => [result.value.labels[index] ?? '', ...row.map((value) => n(value, 3))])),
        table('Comunalidades', ['Variable', 'Comunalidad'], result.value.communalities.map((value, index) => [result.value.labels[index] ?? '', n(value, 4)]))
      )
    })
  }
}

const kmeansDef: AnalysisDefinition = {
  id: 'multivariate-kmeans',
  label: 'Análisis de conglomerados K-means',
  category: 'Multivariante',
  description: 'Agrupamiento k-means++ sobre variables estandarizadas.',
  numericColumns: [{ key: 'x', label: 'Variables', required: true, multiple: true }],
  params: [{ key: 'k', label: 'Número de grupos', kind: 'integer', defaultValue: 3 }],
  run: (input) => {
    const keys = Object.keys(input.numeric)
    const columns = keys.map((key) => num(input, key))
    const rows = columns[0]?.map((_, index) => columns.map((column) => column[index] as number)) ?? []
    const result = kmeans(rows, Math.max(2, Math.floor(pnum(input, 'k', 3))))
    if (!result.ok) return result
    return ok({
      id: 'multivariate-kmeans',
      title: 'Análisis de conglomerados K-means',
      sections: sections(
        table('Resumen', ['Grupos', 'Suma de cuadrados intra', 'Total intra'], [[String(result.value.k), result.value.withinSS.map((value) => n(value, 3)).join(' / '), n(result.value.totalWithinSS, 3)]]),
        table('Asignación', ['Observación', 'Grupo'], result.value.labels.map((label, index) => [String(index + 1), String(label + 1)]))
      )
    })
  }
}

const ldaDef: AnalysisDefinition = {
  id: 'multivariate-lda',
  label: 'Análisis discriminante (2 grupos)',
  category: 'Multivariante',
  description: 'Función discriminante lineal y exactitud de clasificación.',
  numericColumns: [{ key: 'x', label: 'Variables', required: true, multiple: true }],
  textColumns: [{ key: 'group', label: 'Grupo (2 niveles)', required: true }],
  params: [],
  run: (input) => {
    const keys = Object.keys(input.numeric)
    const columns = keys.map((key) => num(input, key))
    const rows = columns[0]?.map((_, index) => columns.map((column) => column[index] as number)) ?? []
    const result = lda(rows, colText(input, 'group'))
    if (!result.ok) return result
    return ok({
      id: 'multivariate-lda',
      title: 'Análisis discriminante',
      sections: sections(
        table('Coeficientes', ['Variable', 'Coeficiente'], result.value.coefficients.map((value, index) => [keys[index] ?? '', n(value, 4)])),
        table('Clasificación', ['Exactitud', 'N'], [[n(result.value.accuracy * 100, 2) + ' %', String(result.value.n)]])
      )
    })
  }
}

const powerTDef: AnalysisDefinition = {
  id: 'power-t1',
  label: 'Potencia y tamaño de muestra (t de 1 muestra)',
  category: 'Potencia y tamaño de muestra',
  description: 'Potencia para un efecto y n dados, y n necesario para una potencia objetivo.',
  numericColumns: [],
  params: [
    { key: 'effect', label: 'Tamaño del efecto (d)', kind: 'number', defaultValue: 0.5 },
    { key: 'n', label: 'Tamaño de muestra', kind: 'integer', defaultValue: 20 },
    { key: 'power', label: 'Potencia objetivo', kind: 'number', defaultValue: 0.8 },
    { key: 'alpha', label: 'α', kind: 'number', defaultValue: 0.05 }
  ],
  run: (input) => {
    const effect = pnum(input, 'effect', 0.5)
    const alpha = pnum(input, 'alpha', 0.05)
    const power = powerOneSampleT(effect, Math.max(2, Math.floor(pnum(input, 'n', 20))), alpha)
    const sample = sampleSizeOneSampleT(effect, pnum(input, 'power', 0.8), alpha)
    if (!power.ok || !sample.ok) return err('INVALID_PARAMETER', 'Parámetros inválidos.')
    return ok({
      id: 'power-t1',
      title: 'Potencia (t de 1 muestra)',
      sections: sections(
        table('Potencia', ['Efecto', 'n', 'α', 'Potencia'], [[n(effect, 3), String(power.value.n), n(alpha, 3), n(power.value.power, 4)]]),
        table('Tamaño de muestra necesario', ['Efecto', 'Potencia objetivo', 'n'], [[n(effect, 3), n(sample.value.power, 2), String(sample.value.n)]])
      ),
      charts: [{ kind: 'line', title: 'Curva de potencia', series: [{ label: 'Potencia', values: ocCurve(effect, [5, 10, 15, 20, 30, 40, 50, 75, 100], alpha).map((point) => point.power) }], xLabel: 'n' }]
    })
  }
}

const powerPropDef: AnalysisDefinition = {
  id: 'power-prop1',
  label: 'Potencia y tamaño de muestra (proporción)',
  category: 'Potencia y tamaño de muestra',
  description: 'Potencia para una prueba de una proporción.',
  numericColumns: [],
  params: [
    { key: 'p0', label: 'Proporción nula', kind: 'number', defaultValue: 0.5 },
    { key: 'p1', label: 'Proporción alternativa', kind: 'number', defaultValue: 0.6 },
    { key: 'n', label: 'Tamaño de muestra', kind: 'integer', defaultValue: 100 },
    { key: 'power', label: 'Potencia objetivo', kind: 'number', defaultValue: 0.8 }
  ],
  run: (input) => {
    const p0 = pnum(input, 'p0', 0.5)
    const p1 = pnum(input, 'p1', 0.6)
    const power = powerOneProportion(p0, p1, Math.max(1, Math.floor(pnum(input, 'n', 100))))
    const sample = sampleSizeOneProportion(p0, p1, pnum(input, 'power', 0.8))
    if (!power.ok || !sample.ok) return err('INVALID_PARAMETER', 'Parámetros inválidos.')
    return ok({
      id: 'power-prop1',
      title: 'Potencia (proporción de 1 muestra)',
      sections: sections(
        table('Potencia', ['p₀', 'p₁', 'n', 'Potencia'], [[n(p0, 3), n(p1, 3), String(power.value.n), n(power.value.power, 4)]]),
        table('Tamaño de muestra necesario', ['p₀', 'p₁', 'Potencia', 'n'], [[n(p0, 3), n(p1, 3), n(sample.value.power, 2), String(sample.value.n)]])
      )
    })
  }
}

const powerAnovaDef: AnalysisDefinition = {
  id: 'power-anova1',
  label: 'Potencia y tamaño de muestra (ANOVA de 1 factor)',
  category: 'Potencia y tamaño de muestra',
  description: 'Aproximación para k grupos con efecto f de Cohen.',
  numericColumns: [],
  params: [
    { key: 'effect', label: 'Tamaño del efecto (f)', kind: 'number', defaultValue: 0.25 },
    { key: 'groups', label: 'Número de grupos', kind: 'integer', defaultValue: 3 },
    { key: 'n', label: 'n por grupo', kind: 'integer', defaultValue: 20 }
  ],
  run: (input) => {
    const result = powerOneWayAnova(pnum(input, 'effect', 0.25), Math.max(2, Math.floor(pnum(input, 'n', 20))), Math.max(2, Math.floor(pnum(input, 'groups', 3))))
    if (!result.ok) return result
    return ok({ id: 'power-anova1', title: 'Potencia (ANOVA de 1 factor)', sections: sections(table('Resultado', ['Efecto', 'Grupos', 'n por grupo', 'Potencia'], [[n(result.value.effect, 3), String(pnum(input, 'groups', 3)), String(result.value.n), n(result.value.power, 4)]])) })
  }
}

const twoSamplePowerDef: AnalysisDefinition = {
  id: 'power-t2',
  label: 'Tamaño de muestra (t de 2 muestras)',
  category: 'Potencia y tamaño de muestra',
  description: 'n por grupo para detectar un efecto con potencia objetivo.',
  numericColumns: [],
  params: [
    { key: 'effect', label: 'Tamaño del efecto (d)', kind: 'number', defaultValue: 0.5 },
    { key: 'power', label: 'Potencia objetivo', kind: 'number', defaultValue: 0.8 }
  ],
  run: (input) => {
    const result = sampleSizeTwoSampleT(pnum(input, 'effect', 0.5), pnum(input, 'power', 0.8))
    if (!result.ok) return result
    return ok({ id: 'power-t2', title: 'Tamaño de muestra (t de 2 muestras)', sections: sections(table('Resultado', ['Efecto', 'Potencia', 'n por grupo'], [[n(result.value.effect, 3), n(result.value.power, 2), String(result.value.n)]])) })
  }
}

const signDef: AnalysisDefinition = {
  id: 'nonparam-sign',
  label: 'Prueba de los signos',
  category: 'No paramétricos',
  description: 'Contraste de la mediana mediante la distribución binomial.',
  numericColumns: [{ key: 'x', label: 'Muestra', required: true }],
  params: [{ key: 'median', label: 'Mediana nula', kind: 'number', defaultValue: 0 }],
  run: (input) => {
    const result = signTest(num(input, 'x'), pnum(input, 'median', 0))
    if (!result.ok) return result
    return ok({ id: 'nonparam-sign', title: 'Prueba de los signos', sections: sections(table('Resultados', ['Positivos', 'Negativos', 'Valor p'], [[String(result.value.positives), String(result.value.negatives), p(result.value.pValue)]])) })
  }
}

const wilcoxonDef: AnalysisDefinition = {
  id: 'nonparam-wilcoxon',
  label: 'Wilcoxon de rangos con signo',
  category: 'No paramétricos',
  description: 'Prueba no paramétrica para muestras pareadas o una muestra.',
  numericColumns: [{ key: 'x', label: 'Muestra', required: true }],
  params: [{ key: 'median', label: 'Mediana nula', kind: 'number', defaultValue: 0 }],
  run: (input) => {
    const result = wilcoxonSignedRank(num(input, 'x'), pnum(input, 'median', 0))
    if (!result.ok) return result
    return ok({ id: 'nonparam-wilcoxon', title: 'Wilcoxon de rangos con signo', sections: sections(table('Resultado', ['W+', 'n', 'Valor p'], [[n(result.value.statistic, 1), String(result.value.n), p(result.value.pValue)]])) })
  }
}

const mannWhitneyDef: AnalysisDefinition = {
  id: 'nonparam-mann-whitney',
  label: 'Mann-Whitney',
  category: 'No paramétricos',
  description: 'Comparación de dos muestras independientes por rangos.',
  numericColumns: [
    { key: 'x1', label: 'Muestra 1', required: true },
    { key: 'x2', label: 'Muestra 2', required: true }
  ],
  params: [],
  run: (input) => {
    const result = mannWhitney(num(input, 'x1'), num(input, 'x2'))
    if (!result.ok) return result
    return ok({ id: 'nonparam-mann-whitney', title: 'Mann-Whitney', sections: sections(table('Resultado', ['U', 'Valor p'], [[n(result.value.statistic, 1), p(result.value.pValue)]])) })
  }
}

const kruskalDef: AnalysisDefinition = {
  id: 'nonparam-kruskal-wallis',
  label: 'Kruskal-Wallis',
  category: 'No paramétricos',
  description: 'Comparación de k muestras independientes por rangos.',
  numericColumns: [{ key: 'y', label: 'Respuesta', required: true }],
  textColumns: [{ key: 'factor', label: 'Factor', required: true }],
  params: [],
  run: (input) => {
    const response = num(input, 'y')
    const factors = colText(input, 'factor')
    const levels = [...new Set(factors)]
    const groups = levels.map((level) => response.filter((_, index) => factors[index] === level))
    const result = kruskalWallis(groups)
    if (!result.ok) return result
    return ok({ id: 'nonparam-kruskal-wallis', title: 'Kruskal-Wallis', sections: sections(table('Resultado', ['Estadístico H', 'GL', 'Valor p'], [[n(result.value.statistic, 3), String(result.value.df), p(result.value.pValue)]])) })
  }
}

const runsDef: AnalysisDefinition = {
  id: 'nonparam-runs',
  label: 'Prueba de rachas',
  category: 'No paramétricos',
  description: 'Aleatoriedad de una secuencia alrededor de la mediana.',
  numericColumns: [{ key: 'x', label: 'Secuencia', required: true }],
  params: [],
  run: (input) => {
    const result = runsTest(num(input, 'x'))
    if (!result.ok) return result
    return ok({ id: 'nonparam-runs', title: 'Prueba de rachas', sections: sections(table('Resultado', ['Rachas', 'Z', 'Valor p'], [[String(result.value.runs), n(result.value.z, 3), p(result.value.pValue)]])) })
  }
}

const spearmanDef: AnalysisDefinition = {
  id: 'nonparam-spearman',
  label: 'Correlación de Spearman',
  category: 'No paramétricos',
  description: 'Correlación de rangos entre dos variables.',
  numericColumns: [
    { key: 'x', label: 'X', required: true },
    { key: 'y', label: 'Y', required: true }
  ],
  params: [],
  run: (input) => {
    const result = spearmanCorrelation(num(input, 'x'), num(input, 'y'))
    if (!result.ok) return result
    return ok({ id: 'nonparam-spearman', title: 'Correlación de Spearman', sections: sections(table('Resultado', ['ρ', 'Valor p'], [[n(result.value.rho, 4), p(result.value.pValue)]])) })
  }
}

export const ADVANCED_ANALYSES: readonly AnalysisDefinition[] = [
  chartXbarR,
  chartImr,
  chartP,
  chartC,
  chartU,
  chartEwma,
  chartCusum,
  capabilityDef,
  gageDef,
  doeAnalyze,
  acfDef,
  pacfDef,
  movingAverageDef,
  sesDef,
  desDef,
  wintersDef,
  decomposeDef,
  arimaDef,
  weibullDef,
  kmDef,
  pcaDef,
  factorDef,
  kmeansDef,
  ldaDef,
  powerTDef,
  powerPropDef,
  powerAnovaDef,
  twoSamplePowerDef,
  signDef,
  wilcoxonDef,
  mannWhitneyDef,
  kruskalDef,
  runsDef,
  spearmanDef
]

export { lognormalFit, DISTRIBUTIONS, text }
