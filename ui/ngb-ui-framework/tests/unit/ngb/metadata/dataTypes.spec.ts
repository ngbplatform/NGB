import { describe, expect, it } from 'vitest'

import {
  dataTypeKind,
  isBooleanType,
  isDateTimeType,
  isDateType,
  isGuidType,
  isNumberType,
} from '../../../../src/ngb/metadata/dataTypes'

describe('metadata dataTypes', () => {
  it('normalizes empty data type values to Unknown', () => {
    expect(dataTypeKind(' DateOnly ')).toBe('DateOnly')
    expect(dataTypeKind('')).toBe('Unknown')
    expect(dataTypeKind(null)).toBe('Unknown')
  })

  it('recognizes primitive framework data type families', () => {
    expect(isGuidType('Guid')).toBe(true)
    expect(isBooleanType('Boolean')).toBe(true)
    expect(isNumberType('Int32')).toBe(true)
    expect(isNumberType('Decimal')).toBe(true)
    expect(isNumberType('Money')).toBe(true)
    expect(isDateType('Date')).toBe(true)
    expect(isDateType('DateOnly')).toBe(true)
    expect(isDateTimeType('DateTime')).toBe(true)
    expect(isDateTimeType('DateTimeOffset')).toBe(true)
    expect(isNumberType('String')).toBe(false)
    expect(isDateTimeType('Date')).toBe(false)
  })
})
