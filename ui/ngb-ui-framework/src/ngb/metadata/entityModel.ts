import type { ReferenceValue } from './types'

export type { EntityFormModel, ReferenceValue } from './types'

export function isReferenceValue(value: unknown): value is ReferenceValue {
  return !!value
    && typeof value === 'object'
    && typeof (value as ReferenceValue).id === 'string'
    && typeof (value as ReferenceValue).display === 'string'
}

export function tryExtractReferenceId(value: unknown): string | null {
  if (typeof value === 'string') {
    const normalized = value.trim()
    return normalized || null
  }

  if (isReferenceValue(value)) {
    const normalized = value.id.trim()
    return normalized || null
  }

  return null
}

export function tryExtractReferenceDisplay(value: unknown): string | null {
  if (!isReferenceValue(value)) return null
  const normalized = value.display.trim()
  return normalized || null
}

export function asTrimmedString(value: unknown): string {
  return value == null ? '' : String(value).trim()
}
