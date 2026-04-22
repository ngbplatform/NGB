import { describe, expect, it } from 'vitest'

import { stableEquals, stableStringify } from '../../../../src/ngb/utils/stableValue'

describe('stable value helpers', () => {
  it('stringifies object keys in sorted order and marks circular references safely', () => {
    const circular: Record<string, unknown> = {
      zebra: 1,
      alpha: {
        bravo: true,
      },
    }
    circular.self = circular

    expect(stableStringify(circular)).toBe('{"alpha":{"bravo":true},"self":"[Circular]","zebra":1}')
  })

  it('compares values using the stable string representation', () => {
    expect(stableEquals(
      { b: 2, a: [3, 1] },
      { a: [3, 1], b: 2 },
    )).toBe(true)

    expect(stableEquals(
      { a: 1, b: 2 },
      { a: 1, b: 3 },
    )).toBe(false)
  })
})
