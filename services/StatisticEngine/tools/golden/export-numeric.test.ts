import { describe, expect, it } from 'vitest'
import {
  betai,
  betaFn,
  clamp,
  correlation,
  covariance,
  erf,
  erfc,
  fmt,
  fmtP,
  gammaFn,
  gammp,
  gammq,
  iqr,
  kurtosis,
  logCombination,
  logFactorial,
  logGamma,
  maxOf,
  mean,
  median,
  minOf,
  normalCdf,
  normalInv,
  normalPdf,
  quantile,
  ranks,
  sd,
  seMean,
  skewness,
  sorted,
  sum,
  variance
} from '@domain/stats/numeric'
import { Rng } from '@domain/stats/random'
import {
  conditionEstimate,
  determinant,
  eigenSymmetric,
  identity,
  inverse,
  leastSquares,
  matVec,
  multiply,
  solveLinear,
  transpose,
  zeros
} from '@domain/stats/linalg'
import { Recorder, grid } from './encode'
import { SMALL, XS, YS } from './cases'

const numeric = new Recorder()

numeric.add('sum', [SMALL], () => sum(SMALL))
numeric.add('sum', [[]], () => sum([]))
numeric.add('mean', [SMALL], () => mean(SMALL))
numeric.add('mean', [[]], () => mean([]))
numeric.add('variance', [SMALL, 1], () => variance(SMALL, 1))
numeric.add('variance', [SMALL, 0], () => variance(SMALL, 0))
numeric.add('variance', [[1], 1], () => variance([1], 1))
numeric.add('sd', [SMALL, 1], () => sd(SMALL, 1))
numeric.add('sd', [SMALL, 0], () => sd(SMALL, 0))
numeric.add('seMean', [SMALL], () => seMean(SMALL))
numeric.add('sorted', [[3, 1, 2]], () => sorted([3, 1, 2]))
numeric.add('minOf', [SMALL], () => minOf(SMALL))
numeric.add('maxOf', [SMALL], () => maxOf(SMALL))
for (const probability of [0, 0.1, 0.25, 0.5, 0.75, 0.9, 1]) {
  numeric.add('quantile', [SMALL, probability], () => quantile(SMALL, probability))
}
numeric.add('quantile', [[1, 2, 3, 4], 0.25], () => quantile([1, 2, 3, 4], 0.25))
numeric.add('quantile', [[1, 2, 3, 4], 0.9], () => quantile([1, 2, 3, 4], 0.9))
numeric.add('quantile', [[7], 0.5], () => quantile([7], 0.5))
numeric.add('quantile', [[], 0.5], () => quantile([], 0.5))
numeric.add('median', [SMALL], () => median(SMALL))
numeric.add('iqr', [SMALL], () => iqr(SMALL))
numeric.add('skewness', [SMALL], () => skewness(SMALL))
numeric.add('skewness', [[1, 1, 1, 1]], () => skewness([1, 1, 1, 1]))
numeric.add('kurtosis', [SMALL], () => kurtosis(SMALL))
numeric.add('kurtosis', [[1, 2, 3]], () => kurtosis([1, 2, 3]))
numeric.add('covariance', [XS, YS], () => covariance(XS, YS))
numeric.add('correlation', [XS, YS], () => correlation(XS, YS))
numeric.add(
  'correlation',
  [
    [1, 1, 1],
    [2, 3, 4]
  ],
  () => correlation([1, 1, 1], [2, 3, 4])
)
numeric.add('ranks', [[10, 20, 20, 30]], () => ranks([10, 20, 20, 30]))
numeric.add('ranks', [[3, 1, 2, 2, 3]], () => ranks([3, 1, 2, 2, 3]))
numeric.add('clamp', [5, 0, 1], () => clamp(5, 0, 1))
numeric.add('clamp', [-5, 0, 1], () => clamp(-5, 0, 1))
numeric.add('logFactorial', [0], () => logFactorial(0))
numeric.add('logFactorial', [10], () => logFactorial(10))
numeric.add('logFactorial', [-1], () => logFactorial(-1))
for (const [n, k] of [
  [10, 3],
  [5, 0],
  [5, 5],
  [5, 6],
  [170, 85]
] as [number, number][]) {
  numeric.add('logCombination', [n, k], () => logCombination(n, k))
}
for (const x of [0.5, 1, 2.5, 10, 171, 1e-3, 0.1, 100]) {
  numeric.add('logGamma', [x], () => logGamma(x))
}
numeric.add('gammaFn', [5], () => gammaFn(5))
numeric.add('gammaFn', [0.5], () => gammaFn(0.5))
numeric.add('betaFn', [2, 3], () => betaFn(2, 3))
numeric.add('betaFn', [0.5, 0.5], () => betaFn(0.5, 0.5))
for (const x of [-2, -1, -0.5, 0, 0.25, 0.5, 1, 2, 3, 5]) {
  numeric.add('erf', [x], () => erf(x))
  numeric.add('erfc', [x], () => erfc(x))
}
for (const z of [-3, -1.96, -1, 0, 1, 1.96, 3]) {
  numeric.add('normalPdf', [z], () => normalPdf(z))
  numeric.add('normalCdf', [z], () => normalCdf(z))
}
for (const p of [0, 1e-12, 0.001, 0.025, 0.5, 0.975, 0.999, 1]) {
  numeric.add('normalInv', [p], () => normalInv(p))
}
for (const [a, x] of [
  [0.5, 0.25],
  [1, 1],
  [2, 3],
  [5, 2],
  [10, 12],
  [0.5, 5],
  [3, 0]
] as [number, number][]) {
  numeric.add('gammp', [a, x], () => gammp(a, x))
  numeric.add('gammq', [a, x], () => gammq(a, x))
}
for (const [a, b, x] of [
  [2, 3, 0.5],
  [1, 1, 0.3],
  [0.5, 0.5, 0.25],
  [5, 10, 0.75],
  [2, 2, 0],
  [2, 2, 1]
] as [number, number, number][]) {
  numeric.add('betai', [a, b, x], () => betai(a, b, x))
}

const FORMAT_VALUES = [
  0,
  -0,
  1,
  -1,
  0.5,
  5,
  100,
  1.23456789,
  1234.56789,
  9999999,
  10000000,
  0.0001,
  0.00009,
  1e-7,
  123456789,
  1 / 3,
  Math.PI,
  1.005,
  2.675,
  999999.9,
  Number.POSITIVE_INFINITY,
  Number.NEGATIVE_INFINITY,
  Number.NaN
]
const FORMAT_DIGITS = [0, 1, 2, 3, 4, 6]
for (const value of FORMAT_VALUES) {
  for (const digits of FORMAT_DIGITS) {
    numeric.add('fmt', [value, digits], () => fmt(value, digits))
  }
}
for (const value of [0, 0.0005, 0.0009, 0.001, 0.05, 0.5, 1, Number.NaN]) {
  numeric.add('fmtP', [value], () => fmtP(value))
}
numeric.add('fmt', [1234.5], () => fmt(1234.5))

// ---------- RNG ----------

const rng = new Recorder()

function sequence(count: number, next: () => number): number[] {
  return Array.from({ length: count }, () => next())
}

for (const seed of [20261023, 0, 1, 4294967295]) {
  rng.add('nextUint32', [seed, 10], () => {
    const generator = new Rng(seed)
    return sequence(10, () => generator.nextUint32())
  })
  rng.add('next', [seed, 10], () => {
    const generator = new Rng(seed)
    return sequence(10, () => generator.next())
  })
  rng.add('normal', [seed, 6], () => {
    const generator = new Rng(seed)
    return sequence(6, () => generator.normal())
  })
  rng.add('exponential', [seed, 1, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.exponential(1))
  })
  rng.add('gamma', [seed, 2.5, 1, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.gamma(2.5, 1))
  })
  rng.add('gamma', [seed, 0.5, 2, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.gamma(0.5, 2))
  })
  rng.add('beta', [seed, 2, 3, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.beta(2, 3))
  })
  rng.add('weibull', [seed, 1.5, 2, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.weibull(1.5, 2))
  })
  rng.add('lognormal', [seed, 0, 1, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.lognormal(0, 1))
  })
  rng.add('binomial', [seed, 10, 0.3, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.binomial(10, 0.3))
  })
  rng.add('binomial', [seed, 500, 0.3, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.binomial(500, 0.3))
  })
  rng.add('poisson', [seed, 4.5, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.poisson(4.5))
  })
  rng.add('poisson', [seed, 50, 5], () => {
    const generator = new Rng(seed)
    return sequence(5, () => generator.poisson(50))
  })
}
rng.add('uniform', [20261023, -1, 1, 5], () => {
  const generator = new Rng(20261023)
  return sequence(5, () => generator.uniform(-1, 1))
})
rng.add('sample', [20261023, ['a', 'b', 'c', 'd', 'e'], 10], () => {
  const generator = new Rng(20261023)
  return sequence(10, () => generator.sample(['a', 'b', 'c', 'd', 'e']))
})

// ---------- Álgebra lineal ----------

const linalg = new Recorder()
const MATRIX_A = [
  [1, 2],
  [3, 4]
]
const MATRIX_B = [
  [5, 6],
  [7, 8]
]

linalg.add('zeros', [2, 3], () => zeros(2, 3))
linalg.add('identity', [3], () => identity(3))
linalg.add('transpose', [MATRIX_A], () => transpose(MATRIX_A))
linalg.add('multiply', [MATRIX_A, MATRIX_B], () => multiply(MATRIX_A, MATRIX_B))
linalg.add('matVec', [MATRIX_A, [1, 2]], () => matVec(MATRIX_A, [1, 2]))
linalg.add(
  'solveLinear',
  [
    [
      [2, 1],
      [1, 3]
    ],
    [3, 5]
  ],
  () =>
    solveLinear(
      [
        [2, 1],
        [1, 3]
      ],
      [3, 5]
    )
)
linalg.add(
  'solveLinear',
  [
    [
      [1, 2],
      [2, 4]
    ],
    [1, 2]
  ],
  () =>
    solveLinear(
      [
        [1, 2],
        [2, 4]
      ],
      [1, 2]
    )
)
linalg.add('solveLinear', [[[1]], [1, 2]], () => solveLinear([[1]], [1, 2]))
linalg.add('inverse', [MATRIX_A], () => inverse(MATRIX_A))
linalg.add(
  'inverse',
  [
    [
      [1, 2],
      [2, 4]
    ]
  ],
  () =>
    inverse([
      [1, 2],
      [2, 4]
    ])
)
linalg.add('determinant', [MATRIX_A], () => determinant(MATRIX_A))
linalg.add(
  'determinant',
  [
    [
      [1, 2],
      [2, 4]
    ]
  ],
  () =>
    determinant([
      [1, 2],
      [2, 4]
    ])
)
linalg.add(
  'determinant',
  [
    [
      [1, 2, 3],
      [0, 5, 6],
      [7, 8, 9]
    ]
  ],
  () =>
    determinant([
      [1, 2, 3],
      [0, 5, 6],
      [7, 8, 9]
    ])
)
linalg.add(
  'leastSquares',
  [
    [[1], [2], [3], [4]],
    [2, 4, 5, 4]
  ],
  () => leastSquares([[1], [2], [3], [4]], [2, 4, 5, 4])
)
linalg.add(
  'leastSquares',
  [
    [[1], [2], [3]],
    [1, 2, 3]
  ],
  () => leastSquares([[1], [2], [3]], [1, 2, 3])
)
linalg.add('leastSquares', [[[1, 2, 3]], [1]], () => leastSquares([[1, 2, 3]], [1]))
linalg.add(
  'eigenSymmetric',
  [
    [
      [2, 1],
      [1, 2]
    ]
  ],
  () =>
    eigenSymmetric([
      [2, 1],
      [1, 2]
    ])
)
linalg.add(
  'eigenSymmetric',
  [
    [
      [4, 1, 2],
      [1, 3, 0],
      [2, 0, 5]
    ]
  ],
  () =>
    eigenSymmetric([
      [4, 1, 2],
      [1, 3, 0],
      [2, 0, 5]
    ])
)
linalg.add('conditionEstimate', [[1, 1e-6, 1e3]], () => conditionEstimate([1, 1e-6, 1e3]))
linalg.add('conditionEstimate', [[0, 0]], () => conditionEstimate([0, 0]))

describe('vectores dorados del núcleo numérico', () => {
  it('exporta numeric.json', () => {
    expect(numeric.write('numeric')).toBeGreaterThan(100)
  })

  it('exporta rng.json', () => {
    expect(rng.write('rng')).toBeGreaterThan(50)
  })

  it('exporta linalg.json', () => {
    expect(linalg.write('linalg')).toBeGreaterThan(15)
  })

  it('cubre la rejilla de formato', () => {
    expect(grid(0, 1, 4)).toEqual([0, 0.25, 0.5, 0.75, 1])
  })
})
