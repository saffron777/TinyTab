import { describe, expect, it } from 'vitest'
import { pairedT, oneSampleT, twoSampleT, andersonDarling, mannWhitney, oneVariance } from '@domain/stats/tests'
import { oneWayAnova } from '@domain/stats/anova'
import { logisticRegression, multipleRegression } from '@domain/stats/regression'
import { processCapability, individualsChart } from '@domain/stats/spc'
import { kaplanMeier, weibullFit } from '@domain/stats/reliability'
import { pca } from '@domain/stats/multivariate'

describe('pruebas de hipótesis', () => {
  it('t de 1 muestra', () => {
    const result = oneSampleT([2, 4, 6, 8, 10], 0)
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.value.estimate).toBe(6)
    expect(result.value.statistic).toBeCloseTo(4.242640687, 6)
    expect(result.value.pValue).toBeCloseTo(0.0132356, 4)
    expect(result.value.df).toBe(4)
  })

  it('t de 2 muestras y pareada', () => {
    const two = twoSampleT([10, 12, 14, 16], [8, 9, 11, 13])
    expect(two.ok).toBe(true)
    const paired = pairedT([10, 12, 14], [9, 11, 12])
    expect(paired.ok).toBe(true)
    if (paired.ok) expect(paired.value.df).toBe(2)
  })

  it('normalidad, no paramétricas y varianza', () => {
    const data = [2, 3, 3, 4, 4, 4, 5, 5, 6, 7, 8, 9]
    const normality = andersonDarling(data)
    expect(normality.ok).toBe(true)
    const mw = mannWhitney([1, 2, 3, 4], [5, 6, 7, 8])
    expect(mw.ok).toBe(true)
    const variance = oneVariance(4, 10, 4)
    expect(variance.ok).toBe(true)
    if (variance.ok) expect(variance.value.statistic).toBeCloseTo(9, 6)
  })

  it('ANOVA de un factor', () => {
    const result = oneWayAnova(
      [
        [1, 2, 3],
        [4, 5, 6],
        [7, 8, 9]
      ],
      ['A', 'B', 'C']
    )
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.value.table[0]?.f).toBeGreaterThan(10)
    expect(result.value.rSquared).toBeGreaterThan(0.8)
  })
})

describe('regresión', () => {
  it('recupera coeficientes exactos de una relación lineal', () => {
    const x = [1, 2, 3, 4, 5, 6]
    const y = x.map((value) => 2 + 3 * value)
    const result = multipleRegression({ response: y, predictors: [x], names: ['x'] })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.value.coefficients[0]?.estimate).toBeCloseTo(2, 10)
    expect(result.value.coefficients[1]?.estimate).toBeCloseTo(3, 10)
    expect(result.value.rSquared).toBeCloseTo(1, 10)
  })

  it('ajusta una regresión logística separable', () => {
    const x = [-3, -2, -1, 1, 2, 3, 0.5, -0.5]
    const y = [0, 0, 1, 1, 1, 0, 0, 1]
    const result = logisticRegression(y, [x], ['x'])
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.value.coefficients[1] as number).toBeGreaterThan(0)
  })
})

describe('SPC y fiabilidad', () => {
  it('capacidad y carta I-MR', () => {
    const values = [10, 10.2, 9.8, 10.1, 9.9, 10.05, 9.95, 10.15]
    const capability = processCapability(values, 9.5, 10.5)
    expect(capability.ok).toBe(true)
    if (capability.ok) expect(capability.value.cpk).toBeGreaterThan(0.5)
    const chart = individualsChart(values)
    expect(chart.ok).toBe(true)
  })

  it('Weibull y Kaplan-Meier', () => {
    const fit = weibullFit([2, 3, 4, 5, 6, 7, 8, 9, 10, 12])
    expect(fit.ok).toBe(true)
    if (fit.ok) expect(fit.value.shape).toBeGreaterThan(0)
    const km = kaplanMeier([1, 2, 2, 3, 4, 5], [true, false, true, true, false, true])
    expect(km.ok).toBe(true)
    if (km.ok) expect(km.value.survival[0]).toBe(1)
  })
})

describe('multivariante', () => {
  it('PCA ordena componentes por varianza', () => {
    const rows = [
      [1, 2],
      [2, 4.1],
      [3, 5.9],
      [4, 8.2],
      [5, 9.8],
      [6, 12.1]
    ]
    const result = pca(rows, ['x', 'y'])
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.value.eigenvalues[0]).toBeGreaterThan(result.value.eigenvalues[1] as number)
    expect(result.value.explained[0]).toBeGreaterThan(90)
  })
})
