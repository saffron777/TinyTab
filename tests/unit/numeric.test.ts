import { describe, expect, it } from 'vitest'
import {
  betai,
  correlation,
  erf,
  gammaFn,
  logGamma,
  mean,
  normalCdf,
  normalInv,
  quantile,
  sd,
  skewness,
  variance
} from '@domain/stats/numeric'

describe('numeric', () => {
  it('calcula media, varianza y desviación', () => {
    const values = [2, 4, 4, 4, 5, 5, 7, 9]
    expect(mean(values)).toBe(5)
    expect(variance(values, 0)).toBe(4)
    expect(sd(values, 0)).toBe(2)
  })

  it('calcula cuantiles tipo 7', () => {
    expect(quantile([1, 2, 3, 4, 5], 0.5)).toBe(3)
    expect(quantile([1, 2, 3, 4], 0.25)).toBeCloseTo(1.75, 12)
    expect(quantile([1, 2, 3, 4], 0.9)).toBeCloseTo(3.7, 12)
  })

  it('calcula la normal estándar con precisión', () => {
    expect(normalCdf(0)).toBeCloseTo(0.5, 15)
    expect(normalCdf(1.959963984540054)).toBeCloseTo(0.975, 10)
    expect(normalCdf(-1)).toBeCloseTo(0.15865525393145707, 10)
    expect(normalInv(0.975)).toBeCloseTo(1.959963984540054, 8)
    expect(normalInv(0.5)).toBe(0)
  })

  it('calcula erf/erfc y gamma', () => {
    expect(erf(0)).toBe(0)
    expect(erf(1)).toBeCloseTo(0.8427007929497149, 10)
    expect(gammaFn(5)).toBeCloseTo(24, 10)
    expect(logGamma(0.5)).toBeCloseTo(Math.log(Math.sqrt(Math.PI)), 12)
  })

  it('calcula la beta incompleta regularizada', () => {
    expect(betai(2, 3, 0.5)).toBeCloseTo(0.6875, 12)
    expect(betai(1, 1, 0.3)).toBeCloseTo(0.3, 12)
  })

  it('calcula correlación y asimetría', () => {
    const x = [1, 2, 3, 4, 5]
    const y = [2, 4, 6, 8, 11]
    expect(correlation(x, y)).toBeGreaterThan(0.99)
    expect(skewness([1, 1, 1, 1])).toBe(0)
  })
})
