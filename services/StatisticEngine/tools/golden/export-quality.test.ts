import { describe, expect, it } from 'vitest'
import {
  cChart,
  cusumChart,
  evaluateRules,
  ewmaChart,
  histogram,
  individualsChart,
  nonnormalCapability,
  npChart,
  pChart,
  processCapability,
  uChart,
  xbarRChart,
  xbarSChart
} from '@domain/stats/spc'
import {
  autocorrelation,
  decompose,
  difference,
  doubleExponentialSmoothing,
  fitAr,
  ljungBox,
  movingAverage,
  partialAutocorrelation,
  simpleExponentialSmoothing,
  wintersSmoothing
} from '@domain/stats/timeseries'
import {
  kaplanMeier,
  lognormalFit,
  probabilityPlotPoints,
  reliabilityAt,
  weibullFit,
  weibullQuantile
} from '@domain/stats/reliability'
import {
  ocCurve,
  powerOneProportion,
  powerOneSampleT,
  powerOneWayAnova,
  powerTwoSampleT,
  sampleSizeOneProportion,
  sampleSizeOneSampleT,
  sampleSizeTwoSampleT
} from '@domain/stats/power'
import { factorAnalysis, kmeans, lda, pca, standardize, varimax } from '@domain/stats/multivariate'
import { Recorder } from './encode'
import {
  CAPABILITY,
  CONTROL_SERIES,
  KMEANS_ROWS,
  KM_EVENTS,
  KM_TIMES,
  LDA_GROUPS,
  LDA_ROWS,
  PCA_ROWS_2,
  PCA_ROWS_3,
  REL_TIMES,
  SUBGROUPS_3,
  TS_SEASONAL,
  TS_SERIES
} from './cases'

const spc = new Recorder()
const SUBGROUPS = SUBGROUPS_3.map((values) => ({ values }))

spc.add('evaluateRules', [CONTROL_SERIES, 10, 0.05], () => evaluateRules(CONTROL_SERIES, 10, 0.05))
spc.add('evaluateRules', [[1, 2, 3, 4, 5, 6, 7, 8], 5, 0], () =>
  evaluateRules([1, 2, 3, 4, 5, 6, 7, 8], 5, 0)
)
spc.add('xbarRChart', [SUBGROUPS], () => xbarRChart(SUBGROUPS))
spc.add('xbarRChart', [[]], () => xbarRChart([]))
spc.add('xbarRChart', [[{ values: [1] }, { values: [1, 2] }]], () =>
  xbarRChart([{ values: [1] }, { values: [1, 2] }])
)
spc.add('xbarSChart', [SUBGROUPS], () => xbarSChart(SUBGROUPS))
spc.add('xbarSChart', [[]], () => xbarSChart([]))
spc.add('individualsChart', [CONTROL_SERIES], () => individualsChart(CONTROL_SERIES))
spc.add('individualsChart', [[1]], () => individualsChart([1]))
spc.add(
  'pChart',
  [
    [1, 2, 0, 3, 1],
    [10, 10, 10, 10, 10]
  ],
  () => pChart([1, 2, 0, 3, 1], [10, 10, 10, 10, 10])
)
spc.add(
  'pChart',
  [
    [1, 2, 0],
    [10, 10]
  ],
  () => pChart([1, 2, 0], [10, 10])
)
spc.add('npChart', [[1, 2, 0, 3, 1], 10], () => npChart([1, 2, 0, 3, 1], 10))
spc.add('npChart', [[1], 10], () => npChart([1], 10))
spc.add('cChart', [[3, 2, 4, 1, 5]], () => cChart([3, 2, 4, 1, 5]))
spc.add('cChart', [[3]], () => cChart([3]))
spc.add(
  'uChart',
  [
    [3, 2, 4, 1, 5],
    [10, 12, 11, 9, 10]
  ],
  () => uChart([3, 2, 4, 1, 5], [10, 12, 11, 9, 10])
)
spc.add('uChart', [[3, 2], [10]], () => uChart([3, 2], [10]))
spc.add('ewmaChart', [CONTROL_SERIES], () => ewmaChart(CONTROL_SERIES))
spc.add('ewmaChart', [CONTROL_SERIES, 0.5, 1], () => ewmaChart(CONTROL_SERIES, 0.5, 1))
spc.add('ewmaChart', [CONTROL_SERIES, 0], () => ewmaChart(CONTROL_SERIES, 0))
spc.add('ewmaChart', [[1]], () => ewmaChart([1]))
spc.add('cusumChart', [CONTROL_SERIES], () => cusumChart(CONTROL_SERIES))
spc.add('cusumChart', [CONTROL_SERIES, 10, 0.5], () => cusumChart(CONTROL_SERIES, 10, 0.5))
spc.add('cusumChart', [[1, 2]], () => cusumChart([1, 2]))
spc.add('processCapability', [CAPABILITY, 9.5, 10.5], () => processCapability(CAPABILITY, 9.5, 10.5))
spc.add('processCapability', [CAPABILITY, 9.5, undefined], () =>
  processCapability(CAPABILITY, 9.5, undefined)
)
spc.add('processCapability', [CAPABILITY, undefined, 10.5], () =>
  processCapability(CAPABILITY, undefined, 10.5)
)
spc.add('processCapability', [CAPABILITY, 9.5, 10.5, { method: 'nonnormal' }], () =>
  processCapability(CAPABILITY, 9.5, 10.5, { method: 'nonnormal' })
)
spc.add('processCapability', [[1]], () => processCapability([1], 0, 1))
spc.add('nonnormalCapability', [CAPABILITY, 9.5, 10.5], () => nonnormalCapability(CAPABILITY, 9.5, 10.5))
spc.add('histogram', [CAPABILITY], () => histogram(CAPABILITY))
spc.add('histogram', [CAPABILITY, 3], () => histogram(CAPABILITY, 3))
spc.add('histogram', [[5, 5, 5]], () => histogram([5, 5, 5]))

const timeseries = new Recorder()

timeseries.add('autocorrelation', [TS_SERIES, 5], () => autocorrelation(TS_SERIES, 5))
timeseries.add('autocorrelation', [[1, 2, 3]], () => autocorrelation([1, 2, 3]))
timeseries.add('autocorrelation', [[2, 2, 2, 2, 2]], () => autocorrelation([2, 2, 2, 2, 2]))
timeseries.add('partialAutocorrelation', [TS_SERIES, 5], () => partialAutocorrelation(TS_SERIES, 5))
timeseries.add('partialAutocorrelation', [[1, 2, 3]], () => partialAutocorrelation([1, 2, 3]))
timeseries.add('movingAverage', [TS_SERIES, 3], () => movingAverage(TS_SERIES, 3))
timeseries.add('movingAverage', [TS_SERIES, 1], () => movingAverage(TS_SERIES, 1))
timeseries.add('movingAverage', [TS_SERIES, 30], () => movingAverage(TS_SERIES, 30))
timeseries.add('simpleExponentialSmoothing', [TS_SERIES, 0.2], () =>
  simpleExponentialSmoothing(TS_SERIES, 0.2)
)
timeseries.add('simpleExponentialSmoothing', [TS_SERIES, 0], () => simpleExponentialSmoothing(TS_SERIES, 0))
timeseries.add('doubleExponentialSmoothing', [TS_SERIES, 0.2, 0.1], () =>
  doubleExponentialSmoothing(TS_SERIES, 0.2, 0.1)
)
timeseries.add('doubleExponentialSmoothing', [[1]], () => doubleExponentialSmoothing([1]))
timeseries.add('doubleExponentialSmoothing', [TS_SERIES, 0, 0.1], () =>
  doubleExponentialSmoothing(TS_SERIES, 0, 0.1)
)
timeseries.add('wintersSmoothing', [TS_SEASONAL, 4, 0.2, 0.1, 0.1], () =>
  wintersSmoothing(TS_SEASONAL, 4, 0.2, 0.1, 0.1)
)
timeseries.add('wintersSmoothing', [[1, 2, 3], 4], () => wintersSmoothing([1, 2, 3], 4))
timeseries.add('difference', [[1, 4, 9, 16], 2], () => difference([1, 4, 9, 16], 2))
timeseries.add('difference', [[1, 4, 9, 16]], () => difference([1, 4, 9, 16]))
timeseries.add('decompose', [TS_SERIES, 4], () => decompose(TS_SERIES, 4))
timeseries.add('decompose', [TS_SERIES, 3], () => decompose(TS_SERIES, 3))
timeseries.add('decompose', [TS_SEASONAL, 1], () => decompose(TS_SEASONAL, 1))
timeseries.add('fitAr', [TS_SERIES, 2, 3], () => fitAr(TS_SERIES, 2, 3))
timeseries.add('fitAr', [[1, 2, 3], 1, 1], () => fitAr([1, 2, 3], 1, 1))
timeseries.add(
  'ljungBox',
  [[0.1, -0.2, 0.3, -0.1, 0.2, -0.3, 0.15, -0.05, 0.25, -0.15, 0.05, 0.1, -0.1, 0.2, -0.2], 5],
  () => ljungBox([0.1, -0.2, 0.3, -0.1, 0.2, -0.3, 0.15, -0.05, 0.25, -0.15, 0.05, 0.1, -0.1, 0.2, -0.2], 5)
)
timeseries.add('ljungBox', [[0.1, 0.2], 5], () => ljungBox([0.1, 0.2], 5))

const reliability = new Recorder()

reliability.add('weibullFit', [REL_TIMES], () => weibullFit(REL_TIMES))
reliability.add('weibullFit', [[1, 2]], () => weibullFit([1, 2]))
reliability.add('weibullFit', [[5, 5, 5, 5]], () => weibullFit([5, 5, 5, 5]))
reliability.add('lognormalFit', [REL_TIMES], () => lognormalFit(REL_TIMES))
reliability.add('lognormalFit', [[1]], () => lognormalFit([1]))
reliability.add('lognormalFit', [[5, 5, 5]], () => lognormalFit([5, 5, 5]))
reliability.add('kaplanMeier', [KM_TIMES, KM_EVENTS], () => kaplanMeier(KM_TIMES, KM_EVENTS))
reliability.add('kaplanMeier', [[1], [true]], () => kaplanMeier([1], [true]))
reliability.add(
  'kaplanMeier',
  [
    [1, 2, 3],
    [false, false, false]
  ],
  () => kaplanMeier([1, 2, 3], [false, false, false])
)
reliability.add('probabilityPlotPoints', [REL_TIMES], () => probabilityPlotPoints(REL_TIMES))
reliability.add('weibullQuantile', [0.5, 2, 100], () => weibullQuantile(0.5, 2, 100))
reliability.add('reliabilityAt', [150, 2, 100], () => reliabilityAt(150, 2, 100))

const power = new Recorder()

power.add('powerOneSampleT', [0.5, 20, 0.05], () => powerOneSampleT(0.5, 20, 0.05))
power.add('powerOneSampleT', [0.5, 20], () => powerOneSampleT(0.5, 20))
power.add('powerOneSampleT', [0, 20], () => powerOneSampleT(0, 20))
power.add('powerOneSampleT', [0.5, 1], () => powerOneSampleT(0.5, 1))
power.add('powerOneSampleT', [0.5, 20, 1.5], () => powerOneSampleT(0.5, 20, 1.5))
power.add('sampleSizeOneSampleT', [0.5, 0.8, 0.05], () => sampleSizeOneSampleT(0.5, 0.8, 0.05))
power.add('sampleSizeOneSampleT', [0.3, 0.9], () => sampleSizeOneSampleT(0.3, 0.9))
power.add('sampleSizeOneSampleT', [0, 0.8], () => sampleSizeOneSampleT(0, 0.8))
power.add('powerTwoSampleT', [0.5, 20, 0.05], () => powerTwoSampleT(0.5, 20, 0.05))
power.add('powerTwoSampleT', [0.5, 1], () => powerTwoSampleT(0.5, 1))
power.add('sampleSizeTwoSampleT', [0.5, 0.8, 0.05], () => sampleSizeTwoSampleT(0.5, 0.8, 0.05))
power.add('sampleSizeTwoSampleT', [0, 0.8], () => sampleSizeTwoSampleT(0, 0.8))
power.add('powerOneProportion', [0.3, 0.5, 100, 0.05], () => powerOneProportion(0.3, 0.5, 100, 0.05))
power.add('powerOneProportion', [0, 0.5, 100], () => powerOneProportion(0, 0.5, 100))
power.add('powerOneProportion', [0.3, 1.5, 100], () => powerOneProportion(0.3, 1.5, 100))
power.add('sampleSizeOneProportion', [0.3, 0.5, 0.8, 0.05], () =>
  sampleSizeOneProportion(0.3, 0.5, 0.8, 0.05)
)
power.add('sampleSizeOneProportion', [0.3, 0.3, 0.8], () => sampleSizeOneProportion(0.3, 0.3, 0.8))
power.add('powerOneWayAnova', [0.25, 10, 3, 0.05], () => powerOneWayAnova(0.25, 10, 3, 0.05))
power.add('powerOneWayAnova', [0.25, 10, 1], () => powerOneWayAnova(0.25, 10, 1))
power.add('powerOneWayAnova', [0, 10, 3], () => powerOneWayAnova(0, 10, 3))
power.add('ocCurve', [0.5, [5, 10, 20, 40], 0.05], () => ocCurve(0.5, [5, 10, 20, 40], 0.05))
power.add('ocCurve', [0, [5, 10], 0.05], () => ocCurve(0, [5, 10], 0.05))

const multivariate = new Recorder()

multivariate.add('standardize', [PCA_ROWS_2], () => standardize(PCA_ROWS_2))
multivariate.add(
  'standardize',
  [
    [
      [1, 2],
      [1, 3],
      [1, 4]
    ]
  ],
  () =>
    standardize([
      [1, 2],
      [1, 3],
      [1, 4]
    ])
)
multivariate.add('pca', [PCA_ROWS_2, ['x', 'y']], () => pca(PCA_ROWS_2, ['x', 'y']))
multivariate.add('pca', [PCA_ROWS_3, ['x', 'y', 'z']], () => pca(PCA_ROWS_3, ['x', 'y', 'z']))
multivariate.add(
  'pca',
  [
    [
      [1, 2],
      [2, 3]
    ],
    ['x', 'y']
  ],
  () =>
    pca(
      [
        [1, 2],
        [2, 3]
      ],
      ['x', 'y']
    )
)
multivariate.add(
  'varimax',
  [
    [
      [0.8, 0.2],
      [0.7, 0.3],
      [0.25, 0.85],
      [0.3, 0.75]
    ]
  ],
  () =>
    varimax([
      [0.8, 0.2],
      [0.7, 0.3],
      [0.25, 0.85],
      [0.3, 0.75]
    ])
)
multivariate.add('factorAnalysis', [PCA_ROWS_3, ['x', 'y', 'z'], 2], () =>
  factorAnalysis(PCA_ROWS_3, ['x', 'y', 'z'], 2)
)
multivariate.add('factorAnalysis', [PCA_ROWS_3, ['x', 'y', 'z'], 0], () =>
  factorAnalysis(PCA_ROWS_3, ['x', 'y', 'z'], 0)
)
multivariate.add('kmeans', [KMEANS_ROWS, 3], () => kmeans(KMEANS_ROWS, 3))
multivariate.add('kmeans', [KMEANS_ROWS, 3, 7], () => kmeans(KMEANS_ROWS, 3, 7))
multivariate.add('kmeans', [KMEANS_ROWS, 1], () => kmeans(KMEANS_ROWS, 1))
multivariate.add('kmeans', [KMEANS_ROWS, 20], () => kmeans(KMEANS_ROWS, 20))
multivariate.add('lda', [LDA_ROWS, LDA_GROUPS], () => lda(LDA_ROWS, LDA_GROUPS))
multivariate.add('lda', [LDA_ROWS, ['A', 'A', 'A', 'A', 'B', 'B', 'B', 'C']], () =>
  lda(LDA_ROWS, ['A', 'A', 'A', 'A', 'B', 'B', 'B', 'C'])
)
multivariate.add('lda', [LDA_ROWS, ['A', 'B', 'B', 'B', 'B', 'B', 'B', 'B']], () =>
  lda(LDA_ROWS, ['A', 'B', 'B', 'B', 'B', 'B', 'B', 'B'])
)

describe('vectores dorados de calidad, series, fiabilidad, potencia y multivariante', () => {
  it('exporta spc.json', () => {
    expect(spc.write('spc')).toBeGreaterThan(20)
  })

  it('exporta timeseries.json', () => {
    expect(timeseries.write('timeseries')).toBeGreaterThan(15)
  })

  it('exporta reliability.json', () => {
    expect(reliability.write('reliability')).toBeGreaterThan(8)
  })

  it('exporta power.json', () => {
    expect(power.write('power')).toBeGreaterThan(15)
  })

  it('exporta multivariate.json', () => {
    expect(multivariate.write('multivariate')).toBeGreaterThan(10)
  })
})
