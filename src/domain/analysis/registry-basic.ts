/** Registro de análisis: estadística básica, distribuciones, pruebas, ANOVA y regresión. */
import { andersonDarling, chiSquareGoodnessOfFit, mannWhitney, oneProportion, oneSampleT, oneSampleZ, oneVariance, pairedT, runsTest, signTest, spearmanCorrelation, tCdf, tostTwoSample, twoProportions, twoSampleT, twoVariances } from '../stats/tests'
import { describe, meanConfidenceInterval } from '../stats/descriptive'
import { covarianceMatrix, correlationMatrix } from '../stats/descriptive'
import { crossTabulation, goodnessOfFit, tally } from '../stats/tables'
import { DISTRIBUTIONS, defaultParams, getDistribution, type DistId } from '../stats/distributions'
import { fmt, iqr, maxOf, mean, median, minOf, sd } from '../stats/numeric'
import { bestSubsets, logisticRegression, multipleRegression, poissonRegression, stepwiseRegression } from '../stats/regression'
import { bartlettTest, leveneTest, oneWayAnova, pairwiseComparisons, twoWayAnova } from '../stats/anova'
import { err, ok } from '../model/result'
import type { AnalysisDefinition, AnalysisInput } from './types'
import { colText, n, num, p, pbool, pnum, pstr, sections, table, text } from './helpers'

const describeDef: AnalysisDefinition = {
  id: 'describe',
  label: 'Estadísticas descriptivas',
  category: 'Estadísticas básicas',
  description: 'Media, desviación estándar, cuartiles, intervalo de confianza y pruebas de normalidad.',
  numericColumns: [{ key: 'x', label: 'Variables', required: true, multiple: true }],
  params: [{ key: 'confidence', label: 'Nivel de confianza', kind: 'number', defaultValue: 0.95 }],
  run: (input) => {
    const keys = Object.keys(input.numeric)
    const confidence = pnum(input, 'confidence', 0.95)
    const rows = keys.map((key) => {
      const values = num(input, key)
      const stats = describe(values)
      const ci = meanConfidenceInterval(values, confidence)
      return [
        key,
        format(stats.n),
        format(stats.mean),
        format(stats.seMean),
        format(stats.sd),
        format(stats.min),
        format(stats.q1),
        format(stats.median),
        format(stats.q3),
        format(stats.max),
        format(stats.iqr),
        `${format(ci.lower)} – ${format(ci.upper)}`
      ]
    })
    return ok({
      id: 'describe',
      title: 'Estadísticas descriptivas',
      subtitle: `Intervalo de confianza del ${(confidence * 100).toFixed(1)} %`,
      sections: [
        table(
          'Resumen',
          ['Variable', 'N', 'Media', 'EE media', 'Desv.Est.', 'Mínimo', 'Q1', 'Mediana', 'Q3', 'Máximo', 'IQR', 'IC media'],
          rows
        )
      ]
    })
  }
}

const graphicalSummaryDef: AnalysisDefinition = {
  id: 'graphical-summary',
  label: 'Resumen gráfico',
  category: 'Estadísticas básicas',
  description: 'Resumen descriptivo con histograma, prueba de normalidad e intervalo de confianza.',
  numericColumns: [{ key: 'x', label: 'Variable', required: true }],
  params: [{ key: 'confidence', label: 'Nivel de confianza', kind: 'number', defaultValue: 0.95 }],
  run: (input) => {
    const values = num(input, 'x')
    if (values.length < 3) return err('INSUFFICIENT_DATA', 'Se necesitan al menos 3 observaciones.')
    const stats = describe(values)
    const ci = meanConfidenceInterval(values, pnum(input, 'confidence', 0.95))
    const normality = andersonDarling(values)
    return ok({
      id: 'graphical-summary',
      title: 'Resumen gráfico',
      sections: sections(
        table(
          'Estadísticas',
          ['N', 'Media', 'Mediana', 'Desv.Est.', 'Mínimo', 'Máximo', 'IQR'],
          [[stats.n, n(stats.mean), n(stats.median), n(stats.sd), n(stats.min), n(stats.max), n(stats.iqr)]]
        ),
        table('Intervalo de confianza para la media', ['Inferior', 'Media', 'Superior'], [[n(ci.lower), n(stats.mean), n(ci.upper)]]),
        normality.ok
          ? table('Prueba de normalidad (Anderson-Darling)', ['Estadístico', 'Valor p'], [[n(normality.value.statistic, 3), p(normality.value.pValue)]])
          : text('Normalidad', normality.error.message)
      ),
      charts: [
        { kind: 'histogram', title: 'Histograma', values },
        { kind: 'boxplot', title: 'Diagrama de caja', groups: [{ label: 'Datos', values }] }
      ]
    })
  }
}

const correlationDef: AnalysisDefinition = {
  id: 'correlation',
  label: 'Correlación',
  category: 'Estadísticas básicas',
  description: 'Matriz de correlaciones de Pearson entre dos o más variables.',
  numericColumns: [{ key: 'x', label: 'Variables', required: true, multiple: true }],
  params: [],
  run: (input) => {
    const keys = Object.keys(input.numeric)
    if (keys.length < 2) return err('INSUFFICIENT_DATA', 'Seleccione al menos dos variables.')
    const columns = keys.map((key) => num(input, key))
    const { matrix } = correlationMatrix(keys, columns)
    const rows = matrix.map((row, index) => [keys[index] ?? '', ...row.map((value) => n(value, 4))])
    return ok({
      id: 'correlation',
      title: 'Correlación',
      sections: sections(table('Matriz de correlaciones', ['Variable', ...keys], rows))
    })
  }
}

const covarianceDef: AnalysisDefinition = {
  id: 'covariance',
  label: 'Covarianza',
  category: 'Estadísticas básicas',
  description: 'Matriz de covarianzas entre dos o más variables.',
  numericColumns: [{ key: 'x', label: 'Variables', required: true, multiple: true }],
  params: [],
  run: (input) => {
    const keys = Object.keys(input.numeric)
    if (keys.length < 2) return err('INSUFFICIENT_DATA', 'Seleccione al menos dos variables.')
    const matrix = covarianceMatrix(keys.map((key) => num(input, key)))
    const rows = matrix.map((row, index) => [keys[index] ?? '', ...row.map((value) => n(value, 4))])
    return ok({ id: 'covariance', title: 'Covarianza', sections: sections(table('Matriz de covarianzas', ['Variable', ...keys], rows)) })
  }
}

const tallyDef: AnalysisDefinition = {
  id: 'tally',
  label: 'Tally individual variables',
  category: 'Tablas',
  description: 'Conteo, porcentaje y porcentaje acumulado por categoría.',
  numericColumns: [{ key: 'x', label: 'Variable numérica (opcional)', required: false }],
  textColumns: [{ key: 't', label: 'Variable de texto (opcional)', required: false }],
  params: [],
  run: (input) => {
    const textValues = colText(input, 't')
    const values = textValues.length > 0 ? textValues : num(input, 'x').map(String)
    if (values.length === 0) return err('INSUFFICIENT_DATA', 'Seleccione una columna.')
    const rows = tally(values).map((row) => [row.value, String(row.count), `${row.percent.toFixed(2)} %`, `${row.cumulativePercent.toFixed(2)} %`])
    return ok({ id: 'tally', title: 'Tally individual variables', sections: sections(table('Conteo', ['Valor', 'Conteo', 'Porcentaje', 'Acumulado'], rows)) })
  }
}

const crossTabDef: AnalysisDefinition = {
  id: 'cross-tab',
  label: 'Tabla cruzada y chi-cuadrado',
  category: 'Tablas',
  description: 'Tabla de contingencia con prueba de independencia chi-cuadrado.',
  numericColumns: [],
  textColumns: [
    { key: 'row', label: 'Variable de fila', required: true },
    { key: 'column', label: 'Variable de columna', required: true }
  ],
  params: [],
  run: (input) => {
    const rows = colText(input, 'row')
    const columns = colText(input, 'column')
    const result = crossTabulation(rows, columns)
    if (!result.ok) return result
    const value = result.value
    const tableRows = value.counts.map((row, index) => [`${value.rowLabels[index]} (${value.rowTotals[index]})`, ...row.map(String), String(value.rowTotals[index])])
    return ok({
      id: 'cross-tab',
      title: 'Tabla cruzada',
      sections: [
        table('Conteos', ['Fila \\ Columna', ...value.columnLabels, 'Total'], [...tableRows, ['Total', ...value.columnTotals.map(String), String(value.total)]]),
        table('Prueba chi-cuadrado', ['Estadístico', 'GL', 'Valor p'], [[n(value.statistic, 3), String(value.df), p(value.pValue)]])
      ]
    })
  }
}

const normalityDef: AnalysisDefinition = {
  id: 'normality',
  label: 'Prueba de normalidad',
  category: 'Estadísticas básicas',
  description: 'Prueba de Anderson-Darling para la normalidad de los datos.',
  numericColumns: [{ key: 'x', label: 'Variable', required: true }],
  params: [],
  run: (input) => {
    const result = andersonDarling(num(input, 'x'))
    if (!result.ok) return result
    return ok({
      id: 'normality',
      title: 'Prueba de normalidad',
      sections: [table('Anderson-Darling', ['Estadístico', 'Valor p', 'N'], [[n(result.value.statistic, 3), p(result.value.pValue), String(result.value.n)]])]
    })
  }
}

const distributionDef: AnalysisDefinition = {
  id: 'probability-distribution',
  label: 'Distribución de probabilidad',
  category: 'Distribuciones',
  description: 'Funciones de densidad, acumulada e inversa para 21 distribuciones.',
  numericColumns: [],
  params: [
    { key: 'distribution', label: 'Distribución', kind: 'select', defaultValue: 'normal', options: DISTRIBUTIONS.map((item) => ({ value: item.id, label: item.label })) },
    { key: 'x', label: 'Valor de x', kind: 'number', defaultValue: 1 },
    { key: 'p', label: 'Probabilidad acumulada', kind: 'number', defaultValue: 0.95 }
  ],
  run: (input) => {
    const id = pstr(input, 'distribution', 'normal') as DistId
    const distribution = getDistribution(id)
    const params = { ...defaultParams(distribution) }
    for (const param of distribution.params) {
      const configured = pnum(input, param.key)
      if (Number.isFinite(configured)) params[param.key] = configured
    }
    const validation = distribution.validate(params)
    if (!validation.ok) return err(validation.error.code, validation.error.message)
    const x = pnum(input, 'x', 1)
    const probability = pnum(input, 'p', 0.95)
    const values: number[] = []
    const step = (distribution.kind === 'continuous' ? Math.abs(distribution.support(params).lower) : 0)
    const start = Number.isFinite(step) ? Math.max(step, -20) : -20
    for (let i = 0; i <= 60; i += 1) {
      const candidate = Number.isFinite(distribution.support(params).lower)
        ? (distribution.support(params).lower as number) + (i / 60) * (Number.isFinite(distribution.support(params).upper) ? (distribution.support(params).upper as number) - (distribution.support(params).lower as number) : 20)
        : start + i
      if (Number.isFinite(candidate)) values.push(candidate)
    }
    const rows = [
      ['PDF / PMF', n(distribution.pdf(x, params))],
      ['CDF', n(distribution.cdf(x, params))],
      ['Inversa de CDF', n(distribution.invCdf(probability, params))],
      ['Media teórica', n(distribution.mean(params))],
      ['Desv. estándar teórica', n(distribution.sd(params))]
    ]
    return ok({
      id: 'probability-distribution',
      title: `Distribución ${distribution.label}`,
      sections: sections(
        table('Parámetros', ['Parámetro', 'Valor'], distribution.params.map((param) => [param.label, n(params[param.key] as number)])),
        table('Resultados', ['Función', 'Valor'], rows)
      ),
      charts: [
        {
          kind: 'line',
          title: `Función de densidad — ${distribution.label}`,
          series: [{ label: 'f(x)', values: values.map((value) => distribution.pdf(value, params)) }]
        }
      ]
    })
  }
}

function tResult(id: string, title: string, result: ReturnType<typeof oneSampleT>): ReturnType<AnalysisDefinition['run']> {
  if (!result.ok) return result
  const value = result.value
  const effect = value.estimate - value.nullValue
  return ok({
    id,
    title,
    sections: sections(
      table('Estimación', ['N', 'Media', 'Desv.Est.', 'EE media'], []),
      table('Prueba', ['Hipótesis nula', 'Estadístico t', 'GL', 'Valor p'], [[`μ = ${n(value.nullValue)}`, n(value.statistic, 3), n(value.df, 0), p(value.pValue)]]),
      table(`Intervalo de confianza (${(value.confidence * 100).toFixed(1)} %)`, ['Inferior', 'Media', 'Superior'], [[n(value.lower), n(value.estimate), n(value.upper)]]),
      text('Conclusión', `${value.pValue < 0.05 ? 'Se rechaza H₀ (p < 0,05)' : 'No se rechaza H₀ (p ≥ 0,05)'}. Diferencia estimada: ${n(effect)}`)
    )
  })
}

const oneSampleTDef: AnalysisDefinition = {
  id: 'one-sample-t',
  label: 't de 1 muestra',
  category: 'Pruebas de hipótesis',
  description: 'Contraste de la media de una población contra un valor de referencia.',
  numericColumns: [{ key: 'x', label: 'Muestra', required: true }],
  params: [
    { key: 'mu', label: 'Media hipotética (H₀)', kind: 'number', defaultValue: 0 },
    { key: 'confidence', label: 'Nivel de confianza', kind: 'number', defaultValue: 0.95 },
    {
      key: 'alternative',
      label: 'Hipótesis alternativa',
      kind: 'select',
      defaultValue: 'two-sided',
      options: [
        { value: 'two-sided', label: '≠' },
        { value: 'less', label: '<' },
        { value: 'greater', label: '>' }
      ]
    }
  ],
  run: (input) => tResult('one-sample-t', 't de 1 muestra', oneSampleT(num(input, 'x'), pnum(input, 'mu', 0), pnum(input, 'confidence', 0.95), pstr(input, 'alternative', 'two-sided') as 'two-sided' | 'less' | 'greater'))
}

const oneSampleZDef: AnalysisDefinition = {
  id: 'one-sample-z',
  label: 'Z de 1 muestra',
  category: 'Pruebas de hipótesis',
  description: 'Contraste de la media con σ conocida.',
  numericColumns: [{ key: 'x', label: 'Muestra', required: true }],
  params: [
    { key: 'sigma', label: 'σ conocida', kind: 'number', defaultValue: 1 },
    { key: 'mu', label: 'Media hipotética', kind: 'number', defaultValue: 0 }
  ],
  run: (input) => {
    const result = oneSampleZ(num(input, 'x'), pnum(input, 'sigma', 1), pnum(input, 'mu', 0))
    if (!result.ok) return result
    return ok({
      id: 'one-sample-z',
      title: 'Z de 1 muestra',
      sections: sections(
        table('Prueba', ['Estadístico Z', 'Valor p'], [[n(result.value.statistic, 3), p(result.value.pValue)]]),
        table('IC', ['Inferior', 'Media', 'Superior'], [[n(result.value.lower), n(result.value.estimate), n(result.value.upper)]])
      )
    })
  }
}

const twoSampleTDef: AnalysisDefinition = {
  id: 'two-sample-t',
  label: 't de 2 muestras',
  category: 'Pruebas de hipótesis',
  description: 'Comparación de medias de dos muestras independientes (Welch o varianzas agrupadas).',
  numericColumns: [
    { key: 'x1', label: 'Muestra 1', required: true },
    { key: 'x2', label: 'Muestra 2', required: true }
  ],
  params: [{ key: 'pooled', label: 'Asumir varianzas iguales', kind: 'select', defaultValue: 'false', options: [{ value: 'false', label: 'No' }, { value: 'true', label: 'Sí' }] }],
  run: (input) => {
    const result = twoSampleT(num(input, 'x1'), num(input, 'x2'), { pooled: pbool(input, 'pooled') })
    if (!result.ok) return result
    const value = result.value
    return ok({
      id: 'two-sample-t',
      title: 't de 2 muestras',
      sections: sections(
        table('Prueba', ['Diferencia', 'Estadístico t', 'GL', 'Valor p'], [[n(value.estimate), n(value.statistic, 3), n(value.df, 1), p(value.pValue)]]),
        table('IC de la diferencia', ['Inferior', 'Superior'], [[n(value.lower), n(value.upper)]])
      )
    })
  }
}

const pairedTDef: AnalysisDefinition = {
  id: 'paired-t',
  label: 't pareada',
  category: 'Pruebas de hipótesis',
  description: 'Contraste de medias en observaciones pareadas.',
  numericColumns: [
    { key: 'x1', label: 'Muestra 1', required: true },
    { key: 'x2', label: 'Muestra 2', required: true }
  ],
  params: [],
  run: (input) => {
    const result = pairedT(num(input, 'x1'), num(input, 'x2'))
    if (!result.ok) return result
    return ok({
      id: 'paired-t',
      title: 't pareada',
      sections: sections(
        table('Diferencia', ['Media', 'Estadístico t', 'GL', 'Valor p'], [[n(result.value.estimate), n(result.value.statistic, 3), n(result.value.df, 0), p(result.value.pValue)]]),
        table('IC', ['Inferior', 'Superior'], [[n(result.value.lower), n(result.value.upper)]])
      )
    })
  }
}

const oneProportionDef: AnalysisDefinition = {
  id: 'one-proportion',
  label: 'Proporción de 1 muestra',
  category: 'Pruebas de hipótesis',
  description: 'Contraste de una proporción con aproximación normal e intervalo de Wilson.',
  numericColumns: [],
  params: [
    { key: 'successes', label: 'Éxitos', kind: 'integer', defaultValue: 10 },
    { key: 'trials', label: 'Ensayos', kind: 'integer', defaultValue: 40 },
    { key: 'p0', label: 'Proporción nula', kind: 'number', defaultValue: 0.5 }
  ],
  run: (input) => {
    const result = oneProportion(pnum(input, 'successes', 10), pnum(input, 'trials', 40), pnum(input, 'p0', 0.5))
    if (!result.ok) return result
    return ok({
      id: 'one-proportion',
      title: 'Proporción de 1 muestra',
      sections: sections(
        table('Estimación', ['Proporción', 'IC inferior', 'IC superior'], [[n(result.value.estimate), n(result.value.lower), n(result.value.upper)]]),
        table('Prueba', ['Z', 'Valor p'], [[n(result.value.z, 3), p(result.value.pValue)]])
      )
    })
  }
}

const twoProportionsDef: AnalysisDefinition = {
  id: 'two-proportions',
  label: 'Proporción de 2 muestras',
  category: 'Pruebas de hipótesis',
  description: 'Comparación de dos proporciones independientes.',
  numericColumns: [],
  params: [
    { key: 'x1', label: 'Éxitos muestra 1', kind: 'integer', defaultValue: 12 },
    { key: 'n1', label: 'Ensayos muestra 1', kind: 'integer', defaultValue: 50 },
    { key: 'x2', label: 'Éxitos muestra 2', kind: 'integer', defaultValue: 18 },
    { key: 'n2', label: 'Ensayos muestra 2', kind: 'integer', defaultValue: 50 }
  ],
  run: (input) => {
    const result = twoProportions(pnum(input, 'x1', 12), pnum(input, 'n1', 50), pnum(input, 'x2', 18), pnum(input, 'n2', 50))
    if (!result.ok) return result
    return ok({
      id: 'two-proportions',
      title: 'Proporción de 2 muestras',
      sections: sections(
        table('Diferencia', ['Estimación', 'Z', 'Valor p'], [[n(result.value.estimate), n(result.value.z, 3), p(result.value.pValue)]]),
        table('IC de la diferencia', ['Inferior', 'Superior'], [[n(result.value.lower), n(result.value.upper)]])
      )
    })
  }
}

const oneVarianceDef: AnalysisDefinition = {
  id: 'one-variance',
  label: 'Varianza de 1 muestra',
  category: 'Pruebas de hipótesis',
  description: 'Contraste de una varianza contra un valor de referencia.',
  numericColumns: [{ key: 'x', label: 'Muestra', required: true }],
  params: [{ key: 'sigma0', label: 'Desviación hipotética (σ₀)', kind: 'number', defaultValue: 1 }],
  run: (input) => {
    const values = num(input, 'x')
    const result = oneVariance(sd(values) ** 2, values.length, pnum(input, 'sigma0', 1) ** 2)
    if (!result.ok) return result
    return ok({
      id: 'one-variance',
      title: 'Varianza de 1 muestra',
      sections: sections(table('Prueba', ['Estadístico χ²', 'GL', 'Valor p'], [[n(result.value.statistic, 3), n(result.value.df, 0), p(result.value.pValue)]]), table('IC de σ²', ['Inferior', 'Superior'], [[n(result.value.lower), n(result.value.upper)]]))
    })
  }
}

const twoVariancesDef: AnalysisDefinition = {
  id: 'two-variances',
  label: 'Varianza de 2 muestras',
  category: 'Pruebas de hipótesis',
  description: 'Comparación de dos varianzas mediante la prueba F.',
  numericColumns: [
    { key: 'x1', label: 'Muestra 1', required: true },
    { key: 'x2', label: 'Muestra 2', required: true }
  ],
  params: [],
  run: (input) => {
    const first = num(input, 'x1')
    const second = num(input, 'x2')
    const result = twoVariances(sd(first) ** 2, first.length, sd(second) ** 2, second.length)
    if (!result.ok) return result
    return ok({
      id: 'two-variances',
      title: 'Varianza de 2 muestras',
      sections: sections(table('Prueba F', ['Estadístico F', 'GL', 'Valor p'], [[n(result.value.statistic, 3), n(result.value.df, 0), p(result.value.pValue)]]))
    })
  }
}

const gofDef: AnalysisDefinition = {
  id: 'chi-square-gof',
  label: 'Chi-cuadrado de bondad de ajuste',
  category: 'Tablas',
  description: 'Compara frecuencias observadas con proporciones esperadas iguales.',
  numericColumns: [{ key: 'counts', label: 'Frecuencias observadas', required: true }],
  textColumns: [{ key: 'labels', label: 'Etiquetas (opcional)', required: false }],
  params: [],
  run: (input) => {
    const observed = num(input, 'counts')
    if (observed.length < 2) return err('INSUFFICIENT_DATA', 'Seleccione la columna de frecuencias.')
    const labels = observed.map((_, index) => String(index + 1))
    const result = goodnessOfFit(labels, observed)
    if (!result.ok) return result
    return ok({
      id: 'chi-square-gof',
      title: 'Chi-cuadrado de bondad de ajuste',
      sections: [
        table('Contribuciones', ['Categoría', 'Observado', 'Esperado', 'Contribución'], result.value.rows.map((row) => [row.label, n(row.observed, 1), n(row.expected, 2), n(row.contribution, 3)])),
        table('Prueba', ['χ²', 'GL', 'Valor p'], [[n(result.value.statistic, 3), String(result.value.df), p(result.value.pValue)]])
      ]
    })
  }
}

const tostDef: AnalysisDefinition = {
  id: 'equivalence-tost',
  label: 'Prueba de equivalencia (TOST)',
  category: 'Pruebas de hipótesis',
  description: 'Dos pruebas t de una cola para demostrar equivalencia con un margen.',
  numericColumns: [
    { key: 'x1', label: 'Muestra 1', required: true },
    { key: 'x2', label: 'Muestra 2', required: true }
  ],
  params: [{ key: 'margin', label: 'Margen de equivalencia', kind: 'number', defaultValue: 1 }],
  run: (input) => {
    const result = tostTwoSample(num(input, 'x1'), num(input, 'x2'), pnum(input, 'margin', 1))
    if (!result.ok) return result
    return ok({
      id: 'equivalence-tost',
      title: 'Prueba de equivalencia (TOST)',
      sections: sections(
        table('Resultados', ['Diferencia', 'p (inferior)', 'p (superior)', 'p TOST'], [[n(result.value.estimate), n(result.value.lowerP, 4), n(result.value.upperP, 4), p(result.value.pValue)]])
      )
    })
  }
}

const anovaOneWayDef: AnalysisDefinition = {
  id: 'anova-one-way',
  label: 'ANOVA de un factor',
  category: 'ANOVA',
  description: 'Comparación de medias entre niveles de un factor, con comparaciones múltiples.',
  numericColumns: [{ key: 'y', label: 'Respuesta', required: true }],
  textColumns: [{ key: 'factor', label: 'Factor', required: true }],
  params: [{ key: 'posthoc', label: 'Comparaciones por pares (Holm)', kind: 'select', defaultValue: 'true', options: [{ value: 'true', label: 'Sí' }, { value: 'false', label: 'No' }] }],
  run: (input) => {
    const response = num(input, 'y')
    const factors = colText(input, 'factor')
    const levels = [...new Set(factors)]
    if (levels.length < 2) return err('INVALID_INPUT', 'El factor necesita al menos dos niveles.')
    const groups = levels.map((level) => response.filter((_, index) => factors[index] === level))
    const result = oneWayAnova(groups, levels)
    if (!result.ok) return result
    const value = result.value
    const posthoc = pbool(input, 'posthoc', true) ? pairwiseComparisons(groups, levels) : null
    return ok({
      id: 'anova-one-way',
      title: 'ANOVA de un factor',
      sections: sections(
        table('Estadísticas por grupo', ['Grupo', 'N', 'Media', 'Desv.Est.'], value.groups.map((group) => [group.label, String(group.n), n(group.mean), n(group.sd)])),
        table('Tabla ANOVA', ['Fuente', 'GL', 'SC', 'MC', 'F', 'Valor p'], value.table.map((row) => [row.source, String(row.df), n(row.ss, 3), n(row.ms, 3), n(row.f, 3), p(row.p)])),
        text('R²', `R² = ${n(value.rSquared, 4)} (${(value.rSquared * 100).toFixed(2)} %)`),
        posthoc && posthoc.ok
          ? table(
              'Comparaciones por pares (Holm)',
              ['Grupo A', 'Grupo B', 'Diferencia', 'EE', 't', 'Valor p', 'Sig.'],
              posthoc.value.comparisons.map((comparison) => [comparison.first, comparison.second, n(comparison.difference), n(comparison.standardError), n(comparison.t, 3), p(comparison.pValue), comparison.significant ? 'Sí' : 'No'])
            )
          : null
      ),
      charts: [{ kind: 'boxplot', title: 'Diagrama de caja por grupo', groups: value.groups.map((group) => ({ label: group.label, values: groups[value.groups.indexOf(group)] ?? [] })) }]
    })
  }
}

const anovaTwoWayDef: AnalysisDefinition = {
  id: 'anova-two-way',
  label: 'ANOVA de dos factores',
  category: 'ANOVA',
  description: 'Modelo aditivo de dos factores (sin interacción).',
  numericColumns: [{ key: 'y', label: 'Respuesta', required: true }],
  textColumns: [
    { key: 'a', label: 'Factor A', required: true },
    { key: 'b', label: 'Factor B', required: true }
  ],
  params: [],
  run: (input) => {
    const result = twoWayAnova(num(input, 'y'), colText(input, 'a'), colText(input, 'b'))
    if (!result.ok) return result
    return ok({
      id: 'anova-two-way',
      title: 'ANOVA de dos factores',
      sections: sections(
        table('Tabla ANOVA', ['Fuente', 'GL', 'SC', 'MC', 'F', 'Valor p'], result.value.table.map((row) => [row.source, String(row.df), n(row.ss, 3), n(row.ms, 3), n(row.f, 3), p(row.p)])),
        text('R²', `R² = ${n(result.value.rSquared, 4)}`)
      )
    })
  }
}

const equalVariancesDef: AnalysisDefinition = {
  id: 'equal-variances',
  label: 'Prueba de igualdad de varianzas',
  category: 'ANOVA',
  description: 'Pruebas de Levene y Bartlett sobre los grupos de un factor.',
  numericColumns: [{ key: 'y', label: 'Respuesta', required: true }],
  textColumns: [{ key: 'factor', label: 'Factor', required: true }],
  params: [],
  run: (input) => {
    const response = num(input, 'y')
    const factors = colText(input, 'factor')
    const levels = [...new Set(factors)]
    if (levels.length < 2) return err('INVALID_INPUT', 'El factor necesita al menos dos niveles.')
    const groups = levels.map((level) => response.filter((_, index) => factors[index] === level))
    const levene = leveneTest(groups)
    const bartlett = bartlettTest(groups)
    return ok({
      id: 'equal-variances',
      title: 'Prueba de igualdad de varianzas',
      sections: sections(
        levene.ok ? table('Levene', ['Estadístico', 'GL', 'Valor p'], [[n(levene.value.statistic, 3), String(levene.value.df), p(levene.value.pValue)]]) : text('Levene', levene.error.message),
        bartlett.ok ? table('Bartlett', ['Estadístico', 'GL', 'Valor p'], [[n(bartlett.value.statistic, 3), String(bartlett.value.df), p(bartlett.value.pValue)]]) : text('Bartlett', bartlett.error.message)
      )
    })
  }
}

function regressionOutput(id: string, title: string, result: ReturnType<typeof multipleRegression>, extra?: string): ReturnType<AnalysisDefinition['run']> {
  if (!result.ok) return result
  const value = result.value
  return ok({
    id,
    title,
    sections: sections(
      table('Coeficientes', ['Término', 'Coef.', 'EE coef.', 't', 'Valor p', 'VIF'], value.coefficients.map((coefficient, index) => [coefficient.term, n(coefficient.estimate, 5), n(coefficient.standardError, 4), n(coefficient.t, 3), p(coefficient.pValue), index === 0 ? '' : n(value.vif[index - 1] ?? Number.NaN, 3)])),
      table('Resumen del modelo', ['R²', 'R² ajustado', 'Error estándar', 'Observaciones', 'GL error', 'F', 'Valor p', 'Durbin-Watson'], [[n(value.rSquared, 4), n(value.adjustedRSquared, 4), n(value.standardError, 4), String(value.observations), String(value.dfError), n(value.f, 3), p(value.pValue), n(value.durbinWatson, 3)]]),
      extra ? text('Nota', extra) : null
    ),
    charts: [{ kind: 'residuals', title: 'Residuos', fitted: value.fitted, residuals: value.residuals, order: value.residuals.map((_, index) => index + 1) }]
  })
}

function buildRegressionInput(input: AnalysisInput): { response: number[]; predictors: number[][]; names: string[] } {
  const keys = Object.keys(input.numeric)
  return { response: num(input, keys[0] ?? ''), predictors: keys.slice(1).map((key) => num(input, key)), names: keys.slice(1) }
}

const regressionDef: AnalysisDefinition = {
  id: 'regression-multiple',
  label: 'Regresión múltiple',
  category: 'Regresión',
  description: 'Ajuste por mínimos cuadrados (QR) con diagnóstico de residuos y VIF.',
  numericColumns: [{ key: 'y', label: 'Respuesta', required: true }, { key: 'x', label: 'Predictores', required: true, multiple: true }],
  params: [],
  run: (input) => {
    const { response, predictors, names } = buildRegressionInput(input)
    return regressionOutput('regression-multiple', 'Regresión múltiple', multipleRegression({ response, predictors, names }))
  }
}

const stepwiseDef: AnalysisDefinition = {
  id: 'regression-stepwise',
  label: 'Regresión stepwise',
  category: 'Regresión',
  description: 'Selección de predictores por AIC (forward/backward).',
  numericColumns: [{ key: 'y', label: 'Respuesta', required: true }, { key: 'x', label: 'Predictores', required: true, multiple: true }],
  params: [{ key: 'direction', label: 'Dirección', kind: 'select', defaultValue: 'forward', options: [{ value: 'forward', label: 'Forward' }, { value: 'backward', label: 'Backward' }] }],
  run: (input) => {
    const { response, predictors, names } = buildRegressionInput(input)
    const result = stepwiseRegression({ response, predictors, names }, pstr(input, 'direction', 'forward') as 'forward' | 'backward')
    if (!result.ok) return result
    return regressionOutput('regression-stepwise', 'Regresión stepwise', { ok: true, value: result.value.model }) as ReturnType<AnalysisDefinition['run']>
  }
}

const subsetsDef: AnalysisDefinition = {
  id: 'regression-best-subsets',
  label: 'Mejores subconjuntos',
  category: 'Regresión',
  description: 'Modelos con mayor R² ajustado hasta 4 predictores.',
  numericColumns: [{ key: 'y', label: 'Respuesta', required: true }, { key: 'x', label: 'Predictores', required: true, multiple: true }],
  params: [],
  run: (input) => {
    const { response, predictors, names } = buildRegressionInput(input)
    const result = bestSubsets({ response, predictors, names }, 4)
    if (!result.ok) return result
    return ok({
      id: 'regression-best-subsets',
      title: 'Mejores subconjuntos',
      sections: [
        table('Modelos (ordenados por R² ajustado)', ['Términos', 'R²', 'R² ajustado', 'AIC', 'Cp Mallows'], result.value.map((model) => [model.names.join(', '), n(model.rSquared, 4), n(model.adjustedRSquared, 4), n(model.aic, 2), n(model.mallowCp, 2)]))
      ]
    })
  }
}

const logisticDef: AnalysisDefinition = {
  id: 'regression-logistic',
  label: 'Regresión logística binaria',
  category: 'Regresión',
  description: 'Modelo logit ajustado por IRLS con desviación y AIC.',
  numericColumns: [{ key: 'y', label: 'Respuesta 0/1', required: true }, { key: 'x', label: 'Predictores', required: true, multiple: true }],
  params: [],
  run: (input) => {
    const { response, predictors, names } = buildRegressionInput(input)
    const result = logisticRegression(response, predictors, names)
    if (!result.ok) return result
    return ok({
      id: 'regression-logistic',
      title: 'Regresión logística binaria',
      sections: sections(
        table('Coeficientes', ['Término', 'Coef.', 'EE', 'Z', 'Valor p', 'Odds ratio'], result.value.terms.map((term, index) => [term, n(result.value.coefficients[index] as number, 5), n(result.value.standardErrors[index] as number, 4), n(result.value.z[index] as number, 3), p(result.value.pValues[index] as number), n(Math.exp(result.value.coefficients[index] as number), 4)])),
        table('Bondad de ajuste', ['Desviación', 'Desviación nula', 'AIC', 'Iteraciones'], [[n(result.value.deviance, 3), n(result.value.nullDeviance, 3), n(result.value.aic, 3), String(result.value.iterations)]])
      )
    })
  }
}

const poissonDef: AnalysisDefinition = {
  id: 'regression-poisson',
  label: 'Regresión de Poisson',
  category: 'Regresión',
  description: 'Modelo log-lineal para respuestas de conteo (IRLS).',
  numericColumns: [{ key: 'y', label: 'Conteo', required: true }, { key: 'x', label: 'Predictores', required: true, multiple: true }],
  params: [],
  run: (input) => {
    const { response, predictors, names } = buildRegressionInput(input)
    const result = poissonRegression(response, predictors, names)
    if (!result.ok) return result
    return ok({
      id: 'regression-poisson',
      title: 'Regresión de Poisson',
      sections: sections(table('Coeficientes', ['Término', 'Coef.', 'EE', 'Z', 'Valor p'], result.value.terms.map((term, index) => [term, n(result.value.coefficients[index] as number, 5), n(result.value.standardErrors[index] as number, 4), n(result.value.z[index] as number, 3), p(result.value.pValues[index] as number)])), table('Ajuste', ['Desviación', 'AIC', 'Iteraciones'], [[n(result.value.deviance, 3), n(result.value.aic, 3), String(result.value.iterations)]]))
    })
  }
}

export const BASIC_ANALYSES: readonly AnalysisDefinition[] = [
  describeDef,
  graphicalSummaryDef,
  correlationDef,
  covarianceDef,
  tallyDef,
  crossTabDef,
  normalityDef,
  distributionDef,
  oneSampleTDef,
  oneSampleZDef,
  twoSampleTDef,
  pairedTDef,
  oneProportionDef,
  twoProportionsDef,
  oneVarianceDef,
  twoVariancesDef,
  gofDef,
  tostDef,
  anovaOneWayDef,
  anovaTwoWayDef,
  equalVariancesDef,
  regressionDef,
  stepwiseDef,
  subsetsDef,
  logisticDef,
  poissonDef
]

function format(value: number): string {
  return Number.isFinite(value) ? fmt(value, 4) : '*'
}

export { mean, median, minOf, maxOf, iqr, fmt, chiSquareGoodnessOfFit, mannWhitney, runsTest, signTest, spearmanCorrelation, tCdf, text }
