import { describe, expect, it } from 'vitest'
import { defaultParams, DISTRIBUTIONS, getDistribution, type DistId } from '@domain/stats/distributions'
import { Rng } from '@domain/stats/random'

function withParams(id: DistId, overrides: Record<string, number> = {}): Record<string, number> {
  const distribution = getDistribution(id)
  return { ...defaultParams(distribution), ...overrides } as Record<string, number>
}

describe('distribuciones', () => {
  it('expone 21 distribuciones', () => {
    expect(DISTRIBUTIONS).toHaveLength(21)
    expect(new Set(DISTRIBUTIONS.map((item) => item.id)).size).toBe(21)
  })

  it('normal: pdf, cdf e inversa', () => {
    const params = withParams('normal', { mu: 0, sigma: 1 })
    const normal = getDistribution('normal')
    expect(normal.pdf(0, params)).toBeCloseTo(0.3989422804014327, 10)
    expect(normal.cdf(1.96, params)).toBeCloseTo(0.9750021048517795, 7)
    expect(normal.invCdf(0.975, params)).toBeCloseTo(1.959963984540054, 7)
    expect(normal.mean(params)).toBe(0)
  })

  it('t, chi-cuadrada y F coinciden con valores de referencia', () => {
    const t = getDistribution('t')
    expect(t.cdf(2.228138851986, withParams('t', { df: 10 }))).toBeCloseTo(0.975, 6)
    const chi = getDistribution('chi2')
    expect(chi.cdf(3.841458820694124, withParams('chi2', { df: 1 }))).toBeCloseTo(0.95, 6)
    const f = getDistribution('f')
    expect(f.cdf(3.7083, withParams('f', { df1: 3, df2: 10 }))).toBeCloseTo(0.95, 3)
  })

  it('distribuciones discretas: binomial, Poisson e hipergeométrica', () => {
    const binomial = getDistribution('binomial')
    expect(binomial.pdf(5, withParams('binomial', { n: 10, p: 0.5 }))).toBeCloseTo(0.24609375, 10)
    expect(binomial.cdf(5, withParams('binomial', { n: 10, p: 0.5 }))).toBeCloseTo(0.623046875, 10)
    const poisson = getDistribution('poisson')
    expect(poisson.pdf(2, withParams('poisson', { lambda: 3 }))).toBeCloseTo(0.22404180765538775, 10)
    expect(poisson.cdf(2, withParams('poisson', { lambda: 3 }))).toBeCloseTo(0.42319008112684353, 8)
    const hyper = getDistribution('hypergeometric')
    expect(hyper.pdf(2, withParams('hypergeometric', { population: 50, successes: 10, sample: 5 }))).toBeGreaterThan(0)
  })

  it('Weibull, exponencial y lognormal tienen inversas consistentes', () => {
    const weibull = getDistribution('weibull')
    const params = withParams('weibull', { shape: 2, scale: 3 })
    const value = weibull.invCdf(0.8, params)
    expect(weibull.cdf(value, params)).toBeCloseTo(0.8, 8)
    const exponential = getDistribution('exponential')
    expect(exponential.pdf(1, withParams('exponential', { rate: 2 }))).toBeCloseTo(2 * Math.exp(-2), 12)
    const lognormal = getDistribution('lognormal')
    const logParams = withParams('lognormal', { meanLog: 0, sdLog: 1 })
    expect(lognormal.invCdf(lognormal.cdf(2, logParams), logParams)).toBeCloseTo(2, 7)
  })

  it('valida parámetros inválidos', () => {
    const normal = getDistribution('normal')
    expect(normal.validate(withParams('normal', { sigma: 0 })).ok).toBe(false)
    const binomial = getDistribution('binomial')
    expect(binomial.validate(withParams('binomial', { n: 2.5, p: 0.4 })).ok).toBe(false)
  })

  it('el generador xoshiro es determinista y produce medias razonables', () => {
    const first = new Rng(123)
    const second = new Rng(123)
    expect(first.next()).toBe(second.next())
    const rng = new Rng(2026)
    let total = 0
    for (let i = 0; i < 20000; i += 1) total += rng.normal()
    expect(Math.abs(total / 20000)).toBeLessThan(0.05)
  })
})
