import { describe, expect, it } from 'vitest'
import {
  correlationMatrix,
  covarianceMatrix,
  describe as describeStats,
  describeByGroup,
  meanConfidenceInterval
} from '@domain/stats/descriptive'
import { crossTabulation, goodnessOfFit, tally } from '@domain/stats/tables'
import {
  andersonDarling,
  chiSquareGoodnessOfFit,
  chiSquareIndependence,
  kruskalWallis,
  mannWhitney,
  oneProportion,
  oneSampleT,
  oneSampleZ,
  oneVariance,
  pairedT,
  runsTest,
  signTest,
  spearmanCorrelation,
  tCdf,
  tQuantile,
  tostTwoSample,
  twoProportions,
  twoSampleT,
  twoVariances,
  wilcoxonSignedRank
} from '@domain/stats/tests'
import { Recorder } from './encode'
import {
  CROSS_COLS,
  CROSS_ROWS,
  GOF_LABELS,
  GOF_OBSERVED,
  GROUPS_3,
  GROUPS_VAR,
  NORMAL12,
  PAIRED_1,
  PAIRED_2,
  SAMPLE_1,
  SAMPLE_2,
  SMALL,
  XS,
  YS
} from './cases'

const descriptive = new Recorder()

descriptive.add('describe', [SMALL], () => describeStats(SMALL))
descriptive.add('describe', [SMALL, 3], () => describeStats(SMALL, 3))
descriptive.add('describe', [[5]], () => describeStats([5]))
descriptive.add('describe', [[1, 1, 1, 1, 1]], () => describeStats([1, 1, 1, 1, 1]))
for (const confidence of [0.9, 0.95, 0.99]) {
  descriptive.add('meanConfidenceInterval', [NORMAL12, confidence], () =>
    meanConfidenceInterval(NORMAL12, confidence)
  )
}
descriptive.add('meanConfidenceInterval', [NORMAL12, 0.95, 2], () =>
  meanConfidenceInterval(NORMAL12, 0.95, 2)
)
descriptive.addRaw(
  'describeByGroup',
  [
    [
      ['A', [1, 2, 3]],
      ['B', [4, 5, 6]]
    ]
  ],
  describeByGroup(
    new Map([
      ['A', [1, 2, 3]],
      ['B', [4, 5, 6]]
    ])
  )
)
descriptive.add(
  'correlationMatrix',
  [
    ['x', 'y'],
    [XS, YS]
  ],
  () => correlationMatrix(['x', 'y'], [XS, YS])
)
descriptive.add('covarianceMatrix', [[XS, YS]], () => covarianceMatrix([XS, YS]))

const tables = new Recorder()

tables.add('tally', [['a', 'b', 'a', 'c', 'a', 'b']], () => tally(['a', 'b', 'a', 'c', 'a', 'b']))
tables.add('tally', [['x', 'y', 'z']], () => tally(['x', 'y', 'z']))
tables.add('tally', [[1, 2, 2, 3]], () => tally([1, 2, 2, 3]))
tables.add('crossTabulation', [CROSS_ROWS, CROSS_COLS], () => crossTabulation(CROSS_ROWS, CROSS_COLS))
tables.add(
  'crossTabulation',
  [
    ['a', 'a'],
    ['b', 'b']
  ],
  () => crossTabulation(['a', 'a'], ['b', 'b'])
)
tables.add('crossTabulation', [[], []], () => crossTabulation([], []))
tables.add('goodnessOfFit', [GOF_LABELS, GOF_OBSERVED], () => goodnessOfFit(GOF_LABELS, GOF_OBSERVED))
tables.add('goodnessOfFit', [GOF_LABELS, GOF_OBSERVED, [0.2, 0.3, 0.5]], () =>
  goodnessOfFit(GOF_LABELS, GOF_OBSERVED, [0.2, 0.3, 0.5])
)
tables.add('goodnessOfFit', [GOF_LABELS, GOF_OBSERVED, [0.2, 0.3, 0.6]], () =>
  goodnessOfFit(GOF_LABELS, GOF_OBSERVED, [0.2, 0.3, 0.6])
)

const tests = new Recorder()
const ALTERNATIVES = ['two-sided', 'less', 'greater'] as const

for (const x of [-3, -1, 0, 1, 2]) {
  for (const df of [1, 2, 5, 10, 30]) {
    tests.add('tCdf', [x, df], () => tCdf(x, df))
  }
}
for (const p of [0.5, 0.9, 0.975, 0.995]) {
  for (const df of [1, 2, 5, 10, 30]) {
    tests.add('tQuantile', [p, df], () => tQuantile(p, df))
  }
}
tests.add('tCdf', [1, 0], () => tCdf(1, 0))
tests.add('tQuantile', [-1, 5], () => tQuantile(-1, 5))
tests.add('tQuantile', [2, 5], () => tQuantile(2, 5))

tests.add('oneSampleT', [SMALL, 5], () => oneSampleT(SMALL, 5))
tests.add('oneSampleT', [SAMPLE_1, 0], () => oneSampleT(SAMPLE_1, 0))
tests.add('oneSampleT', [[1]], () => oneSampleT([1]))
tests.add('oneSampleT', [[2, 2, 2], 0], () => oneSampleT([2, 2, 2], 0))
for (const alternative of ALTERNATIVES) {
  tests.add('oneSampleT', [NORMAL12, 5, 0.9, alternative], () => oneSampleT(NORMAL12, 5, 0.9, alternative))
}
tests.add('oneSampleZ', [SMALL, 2, 5], () => oneSampleZ(SMALL, 2, 5))
tests.add('oneSampleZ', [SMALL, 0, 5], () => oneSampleZ(SMALL, 0, 5))
for (const alternative of ALTERNATIVES) {
  tests.add('oneSampleZ', [NORMAL12, 1.5, 5, 0.99, alternative], () =>
    oneSampleZ(NORMAL12, 1.5, 5, 0.99, alternative)
  )
}
tests.add('twoSampleT', [SAMPLE_1, SAMPLE_2], () => twoSampleT(SAMPLE_1, SAMPLE_2))
tests.add('twoSampleT', [SAMPLE_1, SAMPLE_2, { pooled: true }], () =>
  twoSampleT(SAMPLE_1, SAMPLE_2, { pooled: true })
)
tests.add('twoSampleT', [SAMPLE_1, SAMPLE_2, { confidence: 0.9, alternative: 'greater' }], () =>
  twoSampleT(SAMPLE_1, SAMPLE_2, { confidence: 0.9, alternative: 'greater' })
)
tests.add('twoSampleT', [[1], [2]], () => twoSampleT([1], [2]))
tests.add(
  'twoSampleT',
  [
    [1, 1],
    [1, 1]
  ],
  () => twoSampleT([1, 1], [1, 1])
)
tests.add('pairedT', [PAIRED_1, PAIRED_2], () => pairedT(PAIRED_1, PAIRED_2))
tests.add('pairedT', [PAIRED_1, PAIRED_2, { confidence: 0.9, alternative: 'less' }], () =>
  pairedT(PAIRED_1, PAIRED_2, { confidence: 0.9, alternative: 'less' })
)
tests.add('pairedT', [[1], [2]], () => pairedT([1], [2]))

tests.add('oneProportion', [30, 100], () => oneProportion(30, 100))
tests.add('oneProportion', [30, 100, 0.2, 0.9], () => oneProportion(30, 100, 0.2, 0.9))
tests.add('oneProportion', [3, 10, 0.5, 0.95, 'greater'], () => oneProportion(3, 10, 0.5, 0.95, 'greater'))
tests.add('oneProportion', [10, 0], () => oneProportion(10, 0))
tests.add('oneProportion', [11, 10], () => oneProportion(11, 10))
tests.add('twoProportions', [30, 100, 40, 110], () => twoProportions(30, 100, 40, 110))
tests.add('twoProportions', [30, 100, 40, 110, 0.9, 'less'], () =>
  twoProportions(30, 100, 40, 110, 0.9, 'less')
)
tests.add('twoProportions', [0, 100, 0, 100], () => twoProportions(0, 100, 0, 100))
tests.add('twoProportions', [1, 0, 1, 10], () => twoProportions(1, 0, 1, 10))

tests.add('oneVariance', [4, 10, 4], () => oneVariance(4, 10, 4))
tests.add('oneVariance', [4, 10], () => oneVariance(4, 10))
tests.add('oneVariance', [4, 10, 4, 0.9, 'less'], () => oneVariance(4, 10, 4, 0.9, 'less'))
tests.add('oneVariance', [-1, 10], () => oneVariance(-1, 10))
tests.add('twoVariances', [4, 10, 9, 12], () => twoVariances(4, 10, 9, 12))
tests.add('twoVariances', [4, 10, 9, 12, 0.9, 'greater'], () => twoVariances(4, 10, 9, 12, 0.9, 'greater'))
tests.add('twoVariances', [0, 10, 9, 12], () => twoVariances(0, 10, 9, 12))

tests.add(
  'chiSquareGoodnessOfFit',
  [
    [10, 20, 30],
    [20, 20, 20]
  ],
  () => chiSquareGoodnessOfFit([10, 20, 30], [20, 20, 20])
)
tests.add('chiSquareGoodnessOfFit', [[10, 20], [20]], () => chiSquareGoodnessOfFit([10, 20], [20]))
tests.add(
  'chiSquareGoodnessOfFit',
  [
    [-1, 20],
    [20, 20]
  ],
  () => chiSquareGoodnessOfFit([-1, 20], [20, 20])
)
tests.add(
  'chiSquareIndependence',
  [
    [
      [10, 20],
      [30, 40]
    ]
  ],
  () =>
    chiSquareIndependence([
      [10, 20],
      [30, 40]
    ])
)
tests.add('chiSquareIndependence', [[[10, 20]]], () => chiSquareIndependence([[10, 20]]))

tests.add('andersonDarling', [NORMAL12], () => andersonDarling(NORMAL12))
tests.add('andersonDarling', [[1, 2, 3]], () => andersonDarling([1, 2, 3]))
tests.add('andersonDarling', [[2, 2, 2, 2, 2, 2, 2, 2]], () => andersonDarling([2, 2, 2, 2, 2, 2, 2, 2]))

tests.add('signTest', [[1, 2, 3, 4, 5, 6, 7, 8]], () => signTest([1, 2, 3, 4, 5, 6, 7, 8]))
for (const alternative of ALTERNATIVES) {
  tests.add('signTest', [NORMAL12, 4, alternative], () => signTest(NORMAL12, 4, alternative))
}
tests.add('signTest', [[1, 1, 1]], () => signTest([1, 1, 1]))
tests.add('wilcoxonSignedRank', [[1, -2, 3, -4, 5, -6, 7]], () =>
  wilcoxonSignedRank([1, -2, 3, -4, 5, -6, 7])
)
tests.add('wilcoxonSignedRank', [NORMAL12, 4], () => wilcoxonSignedRank(NORMAL12, 4))
for (const alternative of ALTERNATIVES) {
  tests.add('wilcoxonSignedRank', [[1, -2, 3, -4, 5, -6, 7, 8, -9], 0, alternative], () =>
    wilcoxonSignedRank([1, -2, 3, -4, 5, -6, 7, 8, -9], 0, alternative)
  )
}
tests.add('wilcoxonSignedRank', [[1, 2]], () => wilcoxonSignedRank([1, 2]))
tests.add(
  'mannWhitney',
  [
    [1, 2, 3, 4],
    [5, 6, 7, 8]
  ],
  () => mannWhitney([1, 2, 3, 4], [5, 6, 7, 8])
)
for (const alternative of ALTERNATIVES) {
  tests.add('mannWhitney', [[1, 2, 3, 4], [5, 6, 7, 8], alternative], () =>
    mannWhitney([1, 2, 3, 4], [5, 6, 7, 8], alternative)
  )
}
tests.add('mannWhitney', [[], [1]], () => mannWhitney([], [1]))
tests.add('kruskalWallis', [GROUPS_3], () => kruskalWallis(GROUPS_3))
tests.add('kruskalWallis', [GROUPS_VAR], () => kruskalWallis(GROUPS_VAR))
tests.add('kruskalWallis', [[[1, 2, 3]]], () => kruskalWallis([[1, 2, 3]]))
tests.add('runsTest', [NORMAL12], () => runsTest(NORMAL12))
tests.add('runsTest', [[1, 2, 3]], () => runsTest([1, 2, 3]))
tests.add('runsTest', [[1, 1, 1, 1, 1, 1, 1, 1, 1, 1]], () => runsTest([1, 1, 1, 1, 1, 1, 1, 1, 1, 1]))
tests.add('spearmanCorrelation', [XS, YS], () => spearmanCorrelation(XS, YS))
tests.add(
  'spearmanCorrelation',
  [
    [1, 2],
    [3, 4]
  ],
  () => spearmanCorrelation([1, 2], [3, 4])
)
tests.add('tostTwoSample', [SAMPLE_1, SAMPLE_2, 2], () => tostTwoSample(SAMPLE_1, SAMPLE_2, 2))
tests.add('tostTwoSample', [SAMPLE_1, SAMPLE_2, 0], () => tostTwoSample(SAMPLE_1, SAMPLE_2, 0))
tests.add('tostTwoSample', [[1], [2], 1], () => tostTwoSample([1], [2], 1))
tests.add('tostTwoSample', [[1, 1], [1, 1], 1], () => tostTwoSample([1, 1], [1, 1], 1))

describe('vectores dorados de estadística básica e inferencia', () => {
  it('exporta descriptive.json', () => {
    expect(descriptive.write('descriptive')).toBeGreaterThanOrEqual(10)
  })

  it('exporta tables.json', () => {
    expect(tables.write('tables')).toBeGreaterThanOrEqual(9)
  })

  it('exporta tests.json', () => {
    expect(tests.write('tests')).toBeGreaterThan(100)
  })
})
