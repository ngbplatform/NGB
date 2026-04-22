import { describe, expect, it } from 'vitest'

import {
  formatDateOnlyValue,
  formatDateTimeValue,
  formatLooseEntityValue,
  formatNumberValue,
  formatTypedEntityValue,
  humanizeEntityKey,
} from '../../../../src/ngb/metadata/entityValueFormatting'

describe('metadata entityValueFormatting', () => {
  it('formats date-only and date-time strings while preserving invalid raw values', () => {
    expect(formatDateOnlyValue('2026-04-08')).not.toBe('2026-04-08')
    expect(formatDateOnlyValue('not-a-date')).toBe('not-a-date')
    expect(formatDateTimeValue('2026-04-08T13:45:00Z')).not.toBe('2026-04-08T13:45:00Z')
    expect(formatDateTimeValue('still-not-a-date')).toBe('still-not-a-date')
  })

  it('formats numbers by metadata kind', () => {
    expect(formatNumberValue('Int32', 1250.9)).toBe('1,250')
    expect(formatNumberValue('Money', 1250)).toBe('1,250.00')
    expect(formatNumberValue('Decimal', 12.3456)).toBe('12.3456')
    expect(formatNumberValue('Money', 'oops')).toBe('oops')
  })

  it('formats loose entity values across booleans, arrays, references, and objects', () => {
    expect(formatLooseEntityValue(null)).toBe('—')
    expect(formatLooseEntityValue(true)).toBe('Yes')
    expect(formatLooseEntityValue(['A', 2, false])).toBe('A · 2 · No')
    expect(formatLooseEntityValue({ id: 'property-1', display: 'Riverfront Tower' })).toBe('Riverfront Tower')
    expect(formatLooseEntityValue({
      status_code: 'open',
      count: 3,
      ignored: undefined,
    })).toBe('Status Code: open · Count: 3')
  })

  it('formats typed entity values and humanizes field keys', () => {
    expect(formatTypedEntityValue('Boolean', 1)).toBe('Yes')
    expect(formatTypedEntityValue('Date', '2026-04-08')).not.toBe('2026-04-08')
    expect(formatTypedEntityValue('DateTime', '2026-04-08T13:45:00Z')).not.toBe('2026-04-08T13:45:00Z')
    expect(formatTypedEntityValue('Money', 1250)).toBe('1,250.00')
    expect(formatTypedEntityValue('String', { id: 'property-1', display: 'Riverfront Tower' })).toBe('Riverfront Tower')
    expect(humanizeEntityKey('property_id')).toBe('Property Id')
    expect(humanizeEntityKey('posted.at')).toBe('Posted At')
    expect(humanizeEntityKey('')).toBe('Field')
  })
})
