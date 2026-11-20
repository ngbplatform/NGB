import assert from 'node:assert/strict'
import test from 'node:test'
import { creditTax } from '../../ai-devkit/references/partial-adjustment.mjs'

const line = { lineBase: 2010n, lineTax: 151n, numerator: 75n, denominator: 1000n }

test('partial tax postings sum to the frozen original total at cent precision', () => {
  const first = creditTax({ ...line, priorBase: 0n, priorTax: 0n, creditBase: 1005n })
  const last = creditTax({ ...line, priorBase: 1005n, priorTax: first, creditBase: 1005n })
  assert.equal(first, 75n)
  assert.equal(last, 76n)
  for (let split = 1n; split < line.lineBase; split++) {
    const partial = creditTax({ ...line, priorBase: 0n, priorTax: 0n, creditBase: split })
    const residual = creditTax({ ...line, priorBase: split, priorTax: partial, creditBase: line.lineBase - split })
    assert.equal(partial + residual, line.lineTax)
  }
})

test('half units, zero-rated lines and over-crediting follow the specified policy', () => {
  const initial = { ...line, priorBase: 0n, priorTax: 0n, creditBase: 20n }
  assert.equal(creditTax(initial), 2n)
  assert.equal(creditTax({ ...initial, numerator: 0n, lineTax: 0n }), 0n)
  assert.throws(() => creditTax({ ...initial, creditBase: 2011n }), RangeError)
  assert.throws(() => creditTax({ ...initial, denominator: 0n }), RangeError)
  assert.throws(() => creditTax({ ...initial, priorTax: 152n }), RangeError)
})
