import assert from 'node:assert/strict'
import test from 'node:test'

import { mergeFrontendCoverage } from './merge-frontend-full-coverage.mjs'

function coverage({ column, statementHits, branchHits }) {
  return {
    path: '/repo/ui/app/src/example.ts',
    statementMap: {
      0: { start: { line: 10, column }, end: { line: 10, column: null } },
    },
    fnMap: {
      0: {
        name: 'run',
        decl: { start: { line: 9, column }, end: { line: 9, column: column + 3 } },
        loc: { start: { line: 9, column }, end: { line: 11, column: null } },
        line: 9,
      },
    },
    branchMap: {
      0: {
        type: 'if', line: 10,
        loc: { start: { line: 10, column }, end: { line: 10, column: null } },
        locations: [
          { start: { line: 10, column }, end: { line: 10, column: column + 1 } },
          { start: { line: 10, column: column + 2 }, end: { line: 10, column: null } },
        ],
      },
    },
    s: { 0: statementHits },
    f: { 0: statementHits },
    b: { 0: branchHits },
  }
}

test('merges equivalent Node and browser source points despite shifted columns', () => {
  const merged = mergeFrontendCoverage([
    { '/repo/ui/app/src/example.ts': coverage({ column: 12, statementHits: 1, branchHits: [1, 0] }) },
    { '/repo/ui/app/src/example.ts': coverage({ column: 18, statementHits: 0, branchHits: [0, 1] }) },
  ])
  const summary = merged.fileCoverageFor('/repo/ui/app/src/example.ts').toSummary().toJSON()

  assert.deepEqual(summary.statements, { total: 1, covered: 1, skipped: 0, pct: 100 })
  assert.deepEqual(summary.functions, { total: 1, covered: 1, skipped: 0, pct: 100 })
  assert.deepEqual(summary.branches, { total: 2, covered: 2, skipped: 0, pct: 100 })
})

test('keeps genuinely different source-line points in the merged report', () => {
  const second = coverage({ column: 18, statementHits: 1, branchHits: [1, 1] })
  second.statementMap[0].start.line = 20
  second.statementMap[0].end.line = 20
  const merged = mergeFrontendCoverage([
    { '/repo/ui/app/src/example.ts': coverage({ column: 12, statementHits: 1, branchHits: [1, 1] }) },
    { '/repo/ui/app/src/example.ts': second },
  ])

  assert.equal(merged.fileCoverageFor('/repo/ui/app/src/example.ts').toSummary().statements.total, 2)
})

test('merges multiline source points mapped to the declaration or body line', () => {
  const declarationMapped = coverage({ column: 12, statementHits: 1, branchHits: [1, 1] })
  declarationMapped.statementMap[0] = { start: { line: 9, column: 12 }, end: { line: 10, column: null } }
  declarationMapped.fnMap[0].loc.start.line = 9
  declarationMapped.fnMap[0].loc.end.line = 10
  declarationMapped.branchMap[0].loc.start.line = 9
  declarationMapped.branchMap[0].loc.end.line = 10

  const bodyMapped = coverage({ column: 18, statementHits: 0, branchHits: [0, 1] })
  bodyMapped.fnMap[0].name = '(anonymous_42)'
  declarationMapped.fnMap[0].name = '(anonymous_7)'

  const merged = mergeFrontendCoverage([
    { '/repo/ui/app/src/example.ts': declarationMapped },
    { '/repo/ui/app/src/example.ts': bodyMapped },
  ])
  const summary = merged.fileCoverageFor('/repo/ui/app/src/example.ts').toSummary().toJSON()

  assert.deepEqual(summary.statements, { total: 1, covered: 1, skipped: 0, pct: 100 })
  assert.deepEqual(summary.functions, { total: 1, covered: 1, skipped: 0, pct: 100 })
  assert.deepEqual(summary.branches, { total: 2, covered: 2, skipped: 0, pct: 100 })
})

test('ignores zero-hit transitive variants when another project executed the source', () => {
  const executed = coverage({ column: 12, statementHits: 1, branchHits: [1, 1] })
  const transitive = coverage({ column: 18, statementHits: 0, branchHits: [0, 0] })
  transitive.statementMap[1] = { start: { line: 30, column: 0 }, end: { line: 30, column: null } }
  transitive.s[1] = 0

  const merged = mergeFrontendCoverage([
    { '/repo/ui/app/src/example.ts': executed },
    { '/repo/ui/app/src/example.ts': transitive },
  ])
  const summary = merged.fileCoverageFor('/repo/ui/app/src/example.ts').toSummary().toJSON()

  assert.deepEqual(summary.statements, { total: 1, covered: 1, skipped: 0, pct: 100 })
})

test('ignores partially executed transitive variants from a different package', () => {
  const file = '/repo/ui/ngb-ui-framework/src/example.ts'
  const owner = coverage({ column: 12, statementHits: 1, branchHits: [1, 1] })
  owner.path = file
  const transitive = coverage({ column: 18, statementHits: 1, branchHits: [0, 0] })
  transitive.path = file
  transitive.statementMap[1] = { start: { line: 30, column: 0 }, end: { line: 30, column: null } }
  transitive.s[1] = 0

  const merged = mergeFrontendCoverage(
    [
      { [file]: owner },
      { [file]: transitive },
    ],
    [
      '/coverage/ngb-ui-framework-browser/coverage-final.json',
      '/coverage/ngb-trade-web-unit/coverage-final.json',
    ],
  )
  const summary = merged.fileCoverageFor(file).toSummary().toJSON()

  assert.deepEqual(summary.statements, { total: 1, covered: 1, skipped: 0, pct: 100 })
  assert.deepEqual(summary.branches, { total: 2, covered: 2, skipped: 0, pct: 100 })
})

test('does not let negative sourcemap counters cancel genuine branch hits', () => {
  const unit = coverage({ column: 12, statementHits: 1, branchHits: [1, 2] })
  const browser = coverage({ column: 18, statementHits: 1, branchHits: [2, -2] })

  const merged = mergeFrontendCoverage([
    { '/repo/ui/app/src/example.ts': unit },
    { '/repo/ui/app/src/example.ts': browser },
  ])
  const fileCoverage = merged.fileCoverageFor('/repo/ui/app/src/example.ts')

  assert.deepEqual(fileCoverage.b[0], [3, 2])
  assert.deepEqual(fileCoverage.toSummary().toJSON().branches, { total: 2, covered: 2, skipped: 0, pct: 100 })
})
