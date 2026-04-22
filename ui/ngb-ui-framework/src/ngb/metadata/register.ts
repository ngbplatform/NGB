import type { ColumnMetadata, JsonValue, RecordFields } from './types'
import type { RegisterColumn, RegisterDataRow } from '../components/register/registerTypes'
import { dataTypeKind } from './dataTypes'
import { isReferenceValue } from './entityModel'

export type RegisterAlign = NonNullable<RegisterColumn['align']>
export type RegisterRow = RegisterDataRow

type RegisterRecordItem = {
  id: string
  payload?: {
    fields?: RecordFields | null
  } | null
  isDeleted?: boolean
  isMarkedForDeletion?: boolean
  status?: unknown
}

type BuildColumnsArgs = {
  columns: readonly ColumnMetadata[]
  optionLabelsByColumnKey?: Record<string, Map<string, string>>
  formatOverride?: (column: ColumnMetadata, value: unknown) => string | null
}

type BuildRowsArgs<TItem extends RegisterRecordItem> = {
  items: readonly TItem[]
  columns: readonly ColumnMetadata[]
  mapFieldValue?: (column: ColumnMetadata, rawValue: JsonValue | undefined, item: TItem) => unknown
  extendRow?: (row: RegisterRow, item: TItem) => void
}

const dateOnlyRe = /^\d{4}-\d{2}-\d{2}$/

export function alignFromDto(value: unknown): RegisterAlign {
  if (value === 2) return 'center'
  if (value === 3) return 'right'
  return 'left'
}

export function prettifyRegisterTitle(label: string, key: string): string {
  const normalized = key.toLowerCase()
  if (normalized.endsWith('_id') || normalized.endsWith('_account_id')) return label.replace(/\s+Id$/i, '')
  return label
}

export function tryFormatDateOnly(value: unknown): string | null {
  if (typeof value !== 'string') return null
  if (!dateOnlyRe.test(value)) return null

  const [year, month, day] = value.split('-').map((part) => Number(part))
  if (!Number.isFinite(year) || !Number.isFinite(month) || !Number.isFinite(day)) return null

  const date = new Date(year, month - 1, day)
  if (Number.isNaN(date.getTime())) return null
  return date.toLocaleDateString()
}

export function formatRegisterValue(dataType: unknown, value: unknown): string {
  if (value == null) return '—'
  if (isReferenceValue(value)) return value.display

  const kind = dataTypeKind(dataType)

  if (kind === 'Boolean') return value ? 'Yes' : 'No'

  if (kind === 'Int32') {
    const parsed = Number(value)
    if (Number.isFinite(parsed)) return parsed.toLocaleString(undefined, { maximumFractionDigits: 0 })
  }

  if (kind === 'Decimal' || kind === 'Money') {
    const parsed = Number(value)
    if (Number.isFinite(parsed)) {
      return parsed.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    }
  }

  if (kind === 'Date') {
    const raw = typeof value === 'string' ? value : String(value)
    const dateOnly = tryFormatDateOnly(raw)
    if (dateOnly) return dateOnly

    const date = new Date(value as string | number | Date)
    if (!Number.isNaN(date.getTime())) return date.toLocaleDateString()
  }

  if (kind === 'DateTime') {
    const date = new Date(value as string | number | Date)
    if (!Number.isNaN(date.getTime())) return date.toLocaleString()
  }

  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}

export function buildMetadataRegisterColumns(args: BuildColumnsArgs): RegisterColumn[] {
  return args.columns.map((column) => ({
    key: column.key,
    title: prettifyRegisterTitle(column.label, column.key),
    width: column.widthPx ?? undefined,
    align: alignFromDto(column.align),
    sortable: column.isSortable,
    format: (value: unknown) => {
      const override = args.formatOverride?.(column, value)
      if (override != null) return override

      const normalizedValue = String(value ?? '').trim()
      const optionLabel = args.optionLabelsByColumnKey?.[column.key]?.get(normalizedValue)
        ?? column.options?.find((entry) => String(entry.value ?? '').trim() === normalizedValue)?.label
      if (optionLabel) return optionLabel

      return formatRegisterValue(column.dataType, value)
    },
  }))
}

export function buildMetadataRegisterRows<TItem extends RegisterRecordItem>(args: BuildRowsArgs<TItem>): RegisterRow[] {
  return args.items.map((item) => {
    const fields = (item.payload?.fields ?? {}) as RecordFields
    const row: RegisterRow = {
      key: item.id,
      isDeleted: item.isDeleted,
      isMarkedForDeletion: item.isMarkedForDeletion,
      status: item.status,
    }

    for (const column of args.columns) {
      const rawValue = fields[column.key]
      row[column.key] = args.mapFieldValue?.(column, rawValue, item) ?? rawValue
    }

    args.extendRow?.(row, item)
    return row
  })
}
