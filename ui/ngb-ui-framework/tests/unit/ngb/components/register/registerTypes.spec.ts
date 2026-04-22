import { describe, expect, it } from 'vitest'

import { isDisplayDataRow } from '../../../../../src/ngb/components/register/registerTypes'
import type { DisplayRow } from '../../../../../src/ngb/components/register/registerTypes'

describe('register type guards', () => {
  it('detects display data rows and excludes group header rows', () => {
    const row: DisplayRow = {
      type: 'row',
      key: 'row-1',
      __index: 0,
    }
    const group: DisplayRow = {
      type: 'group',
      key: 'group-1',
      groupId: 'property:Riverfront',
      label: 'Riverfront',
      count: 2,
      totalDebit: 10,
      totalCredit: 5,
    }

    expect(isDisplayDataRow(row)).toBe(true)
    expect(isDisplayDataRow(group)).toBe(false)
  })
})
