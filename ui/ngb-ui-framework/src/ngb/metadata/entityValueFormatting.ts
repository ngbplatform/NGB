import { dataTypeKind } from './dataTypes'
import { isReferenceValue } from './entityModel'

const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/

export function formatDateOnlyValue(value: string): string {
  if (!DATE_ONLY_RE.test(value)) return value

  const [year, month, day] = value.split('-').map((part) => Number(part))
  const date = new Date(year, month - 1, day, 12, 0, 0)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleDateString()
}

export function formatDateTimeValue(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString()
}

export function formatNumberValue(kind: string, value: unknown): string {
  const numeric = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(numeric)) return String(value ?? '—')

  if (kind === 'Int32') {
    return Math.trunc(numeric).toLocaleString()
  }

  if (kind === 'Money') {
    return numeric.toLocaleString(undefined, {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })
  }

  return numeric.toLocaleString(undefined, {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  })
}

function formatObjectValue(value: Record<string, unknown>): string {
  const entries = Object.entries(value)
    .filter(([, item]) => item !== undefined)
    .map(([key, item]) => `${humanizeEntityKey(key)}: ${formatLooseEntityValue(item)}`)
    .filter((item) => item.length > 0)

  return entries.length > 0 ? entries.join(' · ') : '—'
}

export function formatLooseEntityValue(value: unknown): string {
  if (value == null) return '—'
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  if (typeof value === 'number') return Number.isFinite(value) ? value.toLocaleString() : String(value)
  if (typeof value === 'string') return value.trim() || '—'
  if (Array.isArray(value)) return value.map((item) => formatLooseEntityValue(item)).join(' · ')
  if (isReferenceValue(value)) return value.display.trim() || value.id
  if (typeof value === 'object') return formatObjectValue(value as Record<string, unknown>)
  return String(value)
}

export function formatTypedEntityValue(dataType: unknown, value: unknown): string {
  if (value == null) return '—'

  const kind = dataTypeKind(dataType)
  if (kind === 'Boolean') return !!value ? 'Yes' : 'No'
  if (kind === 'Date' && typeof value === 'string') return formatDateOnlyValue(value)
  if (kind === 'DateTime' && typeof value === 'string') return formatDateTimeValue(value)
  if (kind === 'Money' || kind === 'Decimal' || kind === 'Int32') return formatNumberValue(kind, value)

  return formatLooseEntityValue(value)
}

export function humanizeEntityKey(value: string): string {
  const normalized = String(value ?? '').trim().replace(/[._]/g, ' ')
  if (!normalized) return 'Field'

  return normalized
    .split(/\s+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ')
}
