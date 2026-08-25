import assert from 'node:assert/strict'
import test from 'node:test'

import {
  classifyCoverageExclusion,
  evaluateCoverage,
  parseVitestSummary,
} from './verify-frontend-full-coverage.mjs'

test('declaration-only TypeScript is excluded until executable logic appears', () => {
  assert.equal(
    classifyCoverageExclusion('ui/app/src/contracts.ts', 'export interface Clock { now(): Date }; export type Id = string'),
    'declaration-only-types-or-reexports',
  )
  assert.equal(
    classifyCoverageExclusion('ui/app/src/status.ts', 'export enum Status { Open, Closed }'),
    'declaration-only-types-or-reexports',
  )
  assert.equal(
    classifyCoverageExclusion('ui/app/src/contracts.ts', 'export interface Clock { now(): Date }; export function now() { return new Date() }'),
    null,
  )
  assert.equal(
    classifyCoverageExclusion('ui/app/src/index.ts', "export type { Clock } from './clock'; export { runtime } from './runtime'"),
    null,
  )
})

test('only static const containers are excluded', () => {
  assert.equal(
    classifyCoverageExclusion('ui/app/src/constants.ts', "export const limits = { minimum: 0, maximum: 100, labels: ['min', 'max'] } as const"),
    'declaration-only-constants',
  )
  assert.equal(
    classifyCoverageExclusion('ui/app/src/constants.ts', 'export const normalize = (value: string) => value.trim()'),
    null,
  )
  assert.equal(
    classifyCoverageExclusion('ui/app/src/constants.ts', 'export const runtime = createRuntime()'),
    null,
  )
})

test('declarations and generated files are excluded but Vue components are required', () => {
  assert.equal(classifyCoverageExclusion('ui/app/src/vite-env.d.ts', '/// <reference types="vite/client" />'), 'declaration-only-types')
  assert.equal(classifyCoverageExclusion('ui/app/src/client.generated.ts', 'export function generated() {}'), 'generated-source')
  assert.equal(classifyCoverageExclusion('ui/app/src/App.vue', '<template><main /></template>'), null)
})

test('Vitest summary is evaluated per file for all four metrics and completeness', () => {
  const report = parseVitestSummary(JSON.stringify({
    total: {},
    '/repo/ui/app/src/calculator.ts': {
      lines: { total: 2, covered: 1, pct: 50 },
      branches: { total: 4, covered: 3, pct: 75 },
      functions: { total: 2, covered: 2, pct: 100 },
      statements: { total: 3, covered: 2, pct: 66.66 },
    },
  }), '/repo')
  const result = evaluateCoverage({
    requiredFiles: ['ui/app/src/calculator.ts', 'ui/app/src/missing.ts'],
    excludedFiles: [],
    reportFiles: report,
    thresholds: { lines: 100, branches: 100, functions: 100, statements: 100 },
  })

  assert.equal(result.passed, false)
  assert.deepEqual(result.missingFiles, ['ui/app/src/missing.ts'])
  assert.deepEqual(result.failingFiles[0].failures, [
    'lines 50.00%',
    'branches 75.00%',
    'statements 66.67%',
  ])
  assert.equal(result.overall.functions.percentage, 100)
})

test('zero-total metrics are treated as fully covered', () => {
  const report = parseVitestSummary(JSON.stringify({
    '/repo/ui/app/src/empty.ts': {
      lines: { total: 0, covered: 0 },
      branches: { total: 0, covered: 0 },
      functions: { total: 0, covered: 0 },
      statements: { total: 0, covered: 0 },
    },
  }), '/repo')
  const result = evaluateCoverage({
    requiredFiles: ['ui/app/src/empty.ts'],
    excludedFiles: [],
    reportFiles: report,
    thresholds: { lines: 100, branches: 100, functions: 100, statements: 100 },
  })

  assert.equal(result.passed, true)
})
