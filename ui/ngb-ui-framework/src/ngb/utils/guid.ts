const GUID_RE = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

export const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

export function isGuidString(value: unknown): value is string {
  return typeof value === 'string' && GUID_RE.test(value.trim())
}

export function isEmptyGuid(value: unknown): boolean {
  return typeof value === 'string' && value.trim() === EMPTY_GUID
}

export function isNonEmptyGuid(value: unknown): value is string {
  return isGuidString(value) && !isEmptyGuid(value)
}

export function shortGuid(value: unknown): string {
  const normalized = typeof value === 'string' ? value.trim() : ''
  if (!normalized) return '—'
  return normalized.length > 12 ? `${normalized.slice(0, 8)}…${normalized.slice(-4)}` : normalized
}
