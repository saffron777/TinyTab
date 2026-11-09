import { describe, expect, it } from 'vitest'
import {
  bartlettTest,
  fCdf,
  fCdfUpper,
  groupConfidenceIntervals,
  leveneTest,
  oneWayAnova,
  pairwiseComparisons,
  twoWayAnova
} from '@domain/stats/anova'
import {
  bestSubsets,
  durbinWatson,
  logisticRegression,
  multipleRegression,
  poissonRegression,
  stepwiseRegression
} from '@domain/stats/regression'
import { gageRR, gageStatisticsByOperator } from '@domain/stats/msa'
import {
  analyzeFactorial,
  createCcd,
  createFactorialDesign,
  createPlackettBurman,
  designSummary
} from '@domain/stats/doe'
import { Recorder } from './encode'
import {
  DOE_FACTORS,
  DOE_RESPONSE,
  GAGE_ROWS,
  GROUPS_3,
  GROUPS_VAR,
  LOGIT_X,
  LOGIT_Y,
  POISSON_X,
  POISSON_Y,
  REG_X1,
  REG_X2,
  REG_Y,
  SAMPLE_1,
  SAMPLE_2,
  TWO_WAY_A,
  TWO_WAY_A_UNBAL,
  TWO_WAY_B,
  TWO_WAY_B_UNBAL,
  TWO_WAY_Y,
  TWO_WAY_Y_UNBAL
} from './cases'

const anova = new Recorder()

anova.add('oneWayAnova', [GROUPS_3, ['A', 'B', 'C']], () => oneWayAnova(GROUPS_3, ['A', 'B', 'C']))
anova.add('oneWayAnova', [GROUPS_VAR, ['A', 'B', 'C']], () => oneWayAnova(GROUPS_VAR, ['A', 'B', 'C']))
anova.add('oneWayAnova', [[[1, 2, 3]], ['A']], () => oneWayAnova([[1, 2, 3]], ['A']))
anova.add(
  'oneWayAnova',
  [
    [[1], [2, 3]],
    ['A', 'B']
  ],
  () => oneWayAnova([[1], [2, 3]], ['A', 'B'])
)
for (const [f, df1, df2] of [
  [0, 1, 1],
  [1, 2, 10],
  [3, 2, 10],
  [10, 5, 20],
  [2.5, 3, 12]
] as [number, number, number][]) {
  anova.add('fCdfUpper', [f, df1, df2], () => fCdfUpper(f, df1, df2))
  anova.add('fCdf', [f, df1, df2], () => fCdf(f, df1, df2))
}
anova.add('fCdfUpper', [-1, 2, 3], () => fCdfUpper(-1, 2, 3))
anova.add('twoWayAnova', [TWO_WAY_Y, TWO_WAY_A, TWO_WAY_B], () =>
  twoWayAnova(TWO_WAY_Y, TWO_WAY_A, TWO_WAY_B)
)
anova.add('twoWayAnova', [TWO_WAY_Y_UNBAL, TWO_WAY_A_UNBAL, TWO_WAY_B_UNBAL], () =>
  twoWayAnova(TWO_WAY_Y_UNBAL, TWO_WAY_A_UNBAL, TWO_WAY_B_UNBAL)
)
anova.add(
  'twoWayAnova',
  [
    [1, 2, 3],
    ['A', 'B', 'A'],
    ['X', 'Y', 'X']
  ],
  () => twoWayAnova([1, 2, 3], ['A', 'B', 'A'], ['X', 'Y', 'X'])
)
anova.add(
  'twoWayAnova',
  [
    [1, 2, 3, 4],
    ['A', 'A', 'A', 'A'],
    ['X', 'X', 'Y', 'Y']
  ],
  () => twoWayAnova([1, 2, 3, 4], ['A', 'A', 'A', 'A'], ['X', 'X', 'Y', 'Y'])
)
anova.add('leveneTest', [GROUPS_3], () => leveneTest(GROUPS_3))
anova.add('leveneTest', [GROUPS_VAR], () => leveneTest(GROUPS_VAR))
anova.add('leveneTest', [[[1, 2, 3]]], () => leveneTest([[1, 2, 3]]))
anova.add('bartlettTest', [GROUPS_3], () => bartlettTest(GROUPS_3))
anova.add('bartlettTest', [GROUPS_VAR], () => bartlettTest(GROUPS_VAR))
anova.add('bartlettTest', [[[1], [2, 3]]], () => bartlettTest([[1], [2, 3]]))
anova.add('pairwiseComparisons', [GROUPS_3, ['A', 'B', 'C']], () =>
  pairwiseComparisons(GROUPS_3, ['A', 'B', 'C'])
)
anova.add('pairwiseComparisons', [GROUPS_3, ['A', 'B', 'C'], 0.1, 'fisher'], () =>
  pairwiseComparisons(GROUPS_3, ['A', 'B', 'C'], 0.1, 'fisher')
)
anova.add('pairwiseComparisons', [[[1, 2, 3]], ['A']], () => pairwiseComparisons([[1, 2, 3]], ['A']))
anova.add('groupConfidenceIntervals', [GROUPS_3, ['A', 'B', 'C']], () =>
  groupConfidenceIntervals(GROUPS_3, ['A', 'B', 'C'])
)
anova.add('groupConfidenceIntervals', [GROUPS_3, ['A', 'B', 'C'], 0.9], () =>
  groupConfidenceIntervals(GROUPS_3, ['A', 'B', 'C'], 0.9)
)

const regression = new Recorder()
const SINGLE_PREDICTOR = { response: REG_Y, predictors: [REG_X1], names: ['x1'] }
const TWO_PREDICTORS = { response: REG_Y, predictors: [REG_X1, REG_X2], names: ['x1', 'x2'] }

regression.add('multipleRegression', [SINGLE_PREDICTOR], () => multipleRegression(SINGLE_PREDICTOR))
regression.add('multipleRegression', [TWO_PREDICTORS], () => multipleRegression(TWO_PREDICTORS))
regression.add('multipleRegression', [{ response: [1, 2, 3], predictors: [], names: [] }], () =>
  multipleRegression({ response: [1, 2, 3], predictors: [], names: [] })
)
regression.add('multipleRegression', [{ response: [1, 2], predictors: [[1, 2]], names: ['x'] }], () =>
  multipleRegression({ response: [1, 2], predictors: [[1, 2]], names: ['x'] })
)
regression.add('multipleRegression', [{ response: [1, 2, 3], predictors: [[1, 1, 1]], names: ['x'] }], () =>
  multipleRegression({ response: [1, 2, 3], predictors: [[1, 1, 1]], names: ['x'] })
)
regression.add('durbinWatson', [[1, -1, 2, -2, 1]], () => durbinWatson([1, -1, 2, -2, 1]))
regression.add('durbinWatson', [[0, 0, 0]], () => durbinWatson([0, 0, 0]))
regression.add('durbinWatson', [[1]], () => durbinWatson([1]))
regression.add('bestSubsets', [TWO_PREDICTORS, 1], () => bestSubsets(TWO_PREDICTORS, 1))
regression.add('bestSubsets', [TWO_PREDICTORS], () => bestSubsets(TWO_PREDICTORS))
regression.add('bestSubsets', [{ response: REG_Y, predictors: [], names: [] }], () =>
  bestSubsets({ response: REG_Y, predictors: [], names: [] })
)
regression.add('stepwiseRegression', [TWO_PREDICTORS], () => stepwiseRegression(TWO_PREDICTORS))
regression.add('stepwiseRegression', [TWO_PREDICTORS, 'backward'], () =>
  stepwiseRegression(TWO_PREDICTORS, 'backward')
)
regression.add(
  'stepwiseRegression',
  [{ response: [5, 5, 5, 5, 5], predictors: [REG_X1], names: ['x1'] }],
  () => stepwiseRegression({ response: [5, 5, 5, 5, 5], predictors: [REG_X1], names: ['x1'] })
)
regression.add('stepwiseRegression', [{ response: REG_Y, predictors: [], names: [] }], () =>
  stepwiseRegression({ response: REG_Y, predictors: [], names: [] })
)
regression.add('logisticRegression', [LOGIT_Y, [LOGIT_X], ['x']], () =>
  logisticRegression(LOGIT_Y, [LOGIT_X], ['x'])
)
regression.add('logisticRegression', [LOGIT_Y, [LOGIT_X, REG_X1.slice(0, 8)], ['x', 'z']], () =>
  logisticRegression(LOGIT_Y, [LOGIT_X, REG_X1.slice(0, 8)], ['x', 'z'])
)
regression.add('logisticRegression', [[0, 2, 1], [[1, 2, 3]], ['x']], () =>
  logisticRegression([0, 2, 1], [[1, 2, 3]], ['x'])
)
regression.add('logisticRegression', [[0, 1, 1], [], []], () => logisticRegression([0, 1, 1], [], []))
regression.add('logisticRegression', [[0, 1, 0, 1], [[1, 1, 1, 1]], ['x']], () =>
  logisticRegression([0, 1, 0, 1], [[1, 1, 1, 1]], ['x'])
)
regression.add('poissonRegression', [POISSON_Y, [POISSON_X], ['x']], () =>
  poissonRegression(POISSON_Y, [POISSON_X], ['x'])
)
regression.add('poissonRegression', [[1.5, 2, 3], [[1, 2, 3]], ['x']], () =>
  poissonRegression([1.5, 2, 3], [[1, 2, 3]], ['x'])
)
regression.add('poissonRegression', [[-1, 2, 3], [[1, 2, 3]], ['x']], () =>
  poissonRegression([-1, 2, 3], [[1, 2, 3]], ['x'])
)

const msa = new Recorder()

msa.add('gageRR', [GAGE_ROWS], () => gageRR(GAGE_ROWS))
msa.add('gageRR', [GAGE_ROWS, 0.5], () => gageRR(GAGE_ROWS, 0.5))
msa.add('gageRR', [GAGE_ROWS.slice(0, 7)], () => gageRR(GAGE_ROWS.slice(0, 7)))
msa.add('gageStatisticsByOperator', [GAGE_ROWS], () => gageStatisticsByOperator(GAGE_ROWS))

const doe = new Recorder()

function unwrapDesign<T>(result: { ok: true; value: T } | { ok: false; error: unknown }): T {
  if (!result.ok) throw new Error(`diseño inválido: ${JSON.stringify(result.error)}`)
  return result.value
}

const designFixed = unwrapDesign(
  createFactorialDesign(DOE_FACTORS, {
    replicates: 1,
    centerPoints: 0,
    seed: 20261023,
    randomize: false
  })
)
const designRandom = unwrapDesign(
  createFactorialDesign(DOE_FACTORS, {
    replicates: 1,
    centerPoints: 0,
    seed: 20261023,
    randomize: true
  })
)
const designReplicated = unwrapDesign(
  createFactorialDesign(DOE_FACTORS, {
    replicates: 2,
    centerPoints: 2,
    seed: 20261023
  })
)

doe.add(
  'createFactorialDesign',
  [DOE_FACTORS, { replicates: 1, centerPoints: 0, seed: 20261023, randomize: false }],
  () => createFactorialDesign(DOE_FACTORS, { replicates: 1, centerPoints: 0, seed: 20261023, randomize: false })
)
doe.add('createFactorialDesign', [DOE_FACTORS, { seed: 20261023 }], () =>
  createFactorialDesign(DOE_FACTORS, { seed: 20261023 })
)
doe.add(
  'createFactorialDesign',
  [DOE_FACTORS, { replicates: 2, centerPoints: 2, seed: 7 }],
  () => createFactorialDesign(DOE_FACTORS, { replicates: 2, centerPoints: 2, seed: 7 })
)
const ONE_FACTOR = [{ name: 'A', low: -1, high: 1 }]
doe.add('createFactorialDesign', [ONE_FACTOR, { seed: 1 }], () =>
  createFactorialDesign(ONE_FACTOR, { seed: 1 })
)
doe.add('createFactorialDesign', [[], {}], () => createFactorialDesign([], {}))
doe.add('createPlackettBurman', [DOE_FACTORS, 20261023], () => createPlackettBurman(DOE_FACTORS, 20261023))
doe.add('createPlackettBurman', [ONE_FACTOR, 1], () => createPlackettBurman(ONE_FACTOR, 1))
doe.add('createCcd', [DOE_FACTORS.slice(0, 2)], () => createCcd(DOE_FACTORS.slice(0, 2)))
doe.add('createCcd', [DOE_FACTORS, { centerPoints: 2, seed: 5 }], () =>
  createCcd(DOE_FACTORS, { centerPoints: 2, seed: 5 })
)
doe.add('createCcd', [ONE_FACTOR, {}], () => createCcd(ONE_FACTOR, {}))
doe.add('analyzeFactorial', [designFixed, DOE_RESPONSE], () => analyzeFactorial(designFixed, DOE_RESPONSE))
doe.add('analyzeFactorial', [designRandom, DOE_RESPONSE], () => analyzeFactorial(designRandom, DOE_RESPONSE))
doe.add('analyzeFactorial', [designFixed, DOE_RESPONSE.slice(0, 4)], () =>
  analyzeFactorial(designFixed, DOE_RESPONSE.slice(0, 4))
)
doe.add('designSummary', [designFixed], () => designSummary(designFixed))
doe.add('designSummary', [designReplicated], () => designSummary(designReplicated))

describe('vectores dorados de modelos lineales y diseño de experimentos', () => {
  it('exporta anova.json', () => {
    expect(anova.write('anova')).toBeGreaterThan(20)
  })

  it('exporta regression.json', () => {
    expect(regression.write('regression')).toBeGreaterThan(20)
  })

  it('exporta msa.json', () => {
    expect(msa.write('msa')).toBeGreaterThan(3)
  })

  it('exporta doe.json', () => {
    expect(doe.write('doe')).toBeGreaterThan(10)
  })

  it('mantiene las muestras del repo como referencia', () => {
    expect(SAMPLE_1.length).toBe(4)
    expect(SAMPLE_2.length).toBe(4)
  })
})
