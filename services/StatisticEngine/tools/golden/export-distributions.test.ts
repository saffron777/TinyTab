import { describe, expect, it } from 'vitest'
import { DISTRIBUTIONS, defaultParams, getDistribution } from '@domain/stats/distributions'
import { Rng } from '@domain/stats/random'
import { Recorder, grid } from './encode'

const distributions = new Recorder()

for (const distribution of DISTRIBUTIONS) {
  const params = defaultParams(distribution)

  distributions.add(`${distribution.id}.metadata`, [], () => ({
    id: distribution.id,
    label: distribution.label,
    kind: distribution.kind,
    params: distribution.params.map((param) => ({
      key: param.key,
      label: param.label,
      defaultValue: param.defaultValue,
      integer: param.integer ?? false,
      minExclusive: param.minExclusive ?? null,
      minInclusive: param.minInclusive ?? null,
      maxInclusive: param.maxInclusive ?? null
    })),
    defaults: params
  }))

  distributions.add(`${distribution.id}.validate`, [params], () => distribution.validate(params))

  for (const param of distribution.params) {
    const invalid = { ...params, [param.key]: 0 }
    distributions.add(`${distribution.id}.validate`, [invalid], () => distribution.validate(invalid))
  }
  const invalidNegative = { ...params, [distribution.params[0]?.key ?? 'x']: -1 }
  distributions.add(`${distribution.id}.validate`, [invalidNegative], () =>
    distribution.validate(invalidNegative)
  )

  const support = distribution.support(params)
  const isDiscrete = distribution.kind === 'discrete'
  const xs = isDiscrete ? [0, 1, 2, 3, 4, 5, 10] : grid(-4, 8, 24)
  for (const x of xs) {
    distributions.add(`${distribution.id}.pdf`, [x, params], () => distribution.pdf(x, params))
    distributions.add(`${distribution.id}.cdf`, [x, params], () => distribution.cdf(x, params))
  }
  for (const q of [0.001, 0.01, 0.25, 0.5, 0.75, 0.99, 0.999]) {
    distributions.add(`${distribution.id}.invCdf`, [q, params], () => distribution.invCdf(q, params))
  }
  distributions.add(`${distribution.id}.invCdf`, [0, params], () => distribution.invCdf(0, params))
  distributions.add(`${distribution.id}.invCdf`, [1, params], () => distribution.invCdf(1, params))
  distributions.add(`${distribution.id}.mean`, [params], () => distribution.mean(params))
  distributions.add(`${distribution.id}.sd`, [params], () => distribution.sd(params))
  distributions.add(`${distribution.id}.support`, [params], () => ({ ...support }))
  distributions.add(`${distribution.id}.random`, [20261023, params], () => {
    const rng = new Rng(20261023)
    return Array.from({ length: 5 }, () => distribution.random(rng, params))
  })
}

describe('vectores dorados de las 21 distribuciones', () => {
  it('cubre todas las distribuciones del catálogo', () => {
    expect(DISTRIBUTIONS).toHaveLength(21)
    expect(getDistribution('normal').id).toBe('normal')
    expect(distributions.write('distributions')).toBeGreaterThan(900)
  })
})
