import { describe, expect, it } from 'vitest'

import {
  asTrimmedString,
  isReferenceValue,
  normalizeJsonValue,
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
    expect(isReferenceValue(null)).toBe(false)
    expect(isReferenceValue(42)).toBe(false)
    expect(isReferenceValue({ id: 42, display: 'Riverfront Tower' })).toBe(false)
    expect(isReferenceValue({ id: 'property-1' })).toBe(false)
    expect(tryExtractReferenceId('  property-1  ')).toBe('property-1')
    expect(tryExtractReferenceId('   ')).toBeNull()
    expect(tryExtractReferenceId(reference)).toBe('property-1')
    expect(tryExtractReferenceId({ id: '   ', display: 'Empty id' })).toBeNull()
    expect(tryExtractReferenceId({ bad: true })).toBeNull()
  })

  it('extracts display labels and trims loose string values', () => {
    expect(tryExtractReferenceDisplay({ id: 'property-1', display: ' Riverfront Tower ' })).toBe('Riverfront Tower')
    expect(tryExtractReferenceDisplay({ id: 'property-1', display: '   ' })).toBeNull()
    expect(tryExtractReferenceDisplay('property-1')).toBeNull()
    expect(asTrimmedString('  hello  ')).toBe('hello')
    expect(asTrimmedString(null)).toBe('')
    expect(asTrimmedString(42)).toBe('42')
  })

  it('normalizes every JSON boundary without losing nested values', () => {
    expect(normalizeJsonValue(undefined)).toBeNull()
    expect(normalizeJsonValue(null)).toBeNull()
    expect(normalizeJsonValue('value')).toBe('value')
    expect(normalizeJsonValue(true)).toBe(true)
    expect(normalizeJsonValue(42.5)).toBe(42.5)
    expect(normalizeJsonValue(Number.POSITIVE_INFINITY)).toBeNull()
    expect(normalizeJsonValue([undefined, 1, { nested: undefined }])).toEqual([
      null,
      1,
      { nested: null },
    ])
    expect(normalizeJsonValue(Symbol.for('boundary'))).toBe('Symbol(boundary)')
  })
})
