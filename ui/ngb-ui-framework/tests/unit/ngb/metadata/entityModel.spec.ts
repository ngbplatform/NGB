import { describe, expect, it } from 'vitest'

import {
  asTrimmedString,
  isReferenceValue,
  tryExtractReferenceDisplay,
  tryExtractReferenceId,
} from '../../../../src/ngb/metadata/entityModel'

describe('metadata entityModel', () => {
  it('recognizes reference values and extracts ids from strings or references', () => {
    const reference = {
      id: 'property-1',
      display: 'Riverfront Tower',
    }

    expect(isReferenceValue(reference)).toBe(true)
    expect(isReferenceValue({ id: 'property-1' })).toBe(false)
    expect(tryExtractReferenceId('  property-1  ')).toBe('property-1')
    expect(tryExtractReferenceId(reference)).toBe('property-1')
    expect(tryExtractReferenceId({ bad: true })).toBeNull()
  })

  it('extracts display labels and trims loose string values', () => {
    expect(tryExtractReferenceDisplay({ id: 'property-1', display: ' Riverfront Tower ' })).toBe('Riverfront Tower')
    expect(tryExtractReferenceDisplay('property-1')).toBeNull()
    expect(asTrimmedString('  hello  ')).toBe('hello')
    expect(asTrimmedString(null)).toBe('')
    expect(asTrimmedString(42)).toBe('42')
  })
})
