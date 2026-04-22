import {
  buildFieldsPayload,
  dataTypeKind,
  isNonEmptyGuid,
  isReferenceValue,
  resolveLookupHint,
  type EntityFormModel,
  type FieldMetadata,
  type FormMetadata,
  type LookupHint,
  type LookupStoreApi,
  type MetadataFormBehavior,
  type PartMetadata,
  type RecordPartRow,
  type RecordParts,
} from 'ngb-ui-framework'

type PendingPartLookupHydration = {
  row: RecordPartRow
  fieldKey: string
  id: string
  hint: LookupHint
}

const TRADE_DOCUMENT_AMOUNT_FIELD_KEYS = ['line_amount', 'amount'] as const

export type TradeDocumentPartErrors = Record<string, Record<number, Record<string, string>>>

const localRowKeys = new WeakMap<RecordPartRow, string>()

function buildTradeDocumentPartRowKey(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') return globalThis.crypto.randomUUID()
  return `row_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 10)}`
}

function resolvePartFieldUiControl(dataType: unknown): number {
  switch (dataTypeKind(dataType)) {
    case 'Boolean':
      return 5
    case 'Int32':
    case 'Decimal':
      return 3
    case 'Money':
      return 4
    case 'Date':
      return 6
    case 'DateTime':
      return 7
    default:
      return 1
  }
}

function createPartField(column: PartMetadata['list']['columns'][number]): FieldMetadata {
  return {
    key: column.key,
    label: column.label,
    dataType: column.dataType,
    uiControl: resolvePartFieldUiControl(column.dataType),
    isRequired: true,
    isReadOnly: false,
    lookup: column.lookup ?? null,
    validation: null,
    helpText: null,
  }
}

function createPartForm(fields: readonly FieldMetadata[]): FormMetadata {
  return {
    sections: [
      {
        title: 'Lines',
        rows: fields.map((field) => ({ fields: [field] })),
      },
    ],
  }
}

export function listTradeDocumentPartFields(part: PartMetadata): FieldMetadata[] {
  return (part.list?.columns ?? [])
    .filter((column) => column.key !== 'ordinal')
    .map(createPartField)
}

export function ensureTradeDocumentPartRowKey(row: RecordPartRow): string {
  const existing = typeof row.__row_key === 'string' ? row.__row_key.trim() : ''
  if (existing) return existing

  const cached = localRowKeys.get(row)
  if (cached) return cached

  const created = buildTradeDocumentPartRowKey()
  localRowKeys.set(row, created)
  return created
}

export function normalizeTradeDocumentPartRows(rows: readonly RecordPartRow[] | null | undefined): RecordPartRow[] {
  return (rows ?? []).map((row, index) => ({
    ...row,
    __row_key: ensureTradeDocumentPartRowKey(row),
    ordinal: index + 1,
  }))
}

function parseDecimal(value: unknown): number | null {
  if (value == null) return null
  if (typeof value === 'number') return Number.isFinite(value) ? value : null

  const normalized = String(value).trim().replace(/,/g, '')
  if (!normalized) return null

  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : null
}

function roundTo4(value: number): number {
  return Math.round(value * 10000) / 10000
}

export function resolveTradeDocumentAmountSourceField(part: PartMetadata): string | null {
  const keys = new Set((part.list?.columns ?? []).map((column) => column.key))
  return TRADE_DOCUMENT_AMOUNT_FIELD_KEYS.find((key) => keys.has(key)) ?? null
}

export function calculateTradeDocumentPartAmount(
  part: PartMetadata,
  rows: readonly RecordPartRow[] | null | undefined,
): number | null {
  const amountField = resolveTradeDocumentAmountSourceField(part)
  if (!amountField) return null

  let total = 0
  for (const row of normalizeTradeDocumentPartRows(rows)) {
    const amount = parseDecimal(row[amountField])
    if (amount != null) total += amount
  }

  return roundTo4(total)
}

export function calculateTradeDocumentAmount(
  partsMeta: readonly PartMetadata[] | null | undefined,
  partsModel: RecordParts | null | undefined,
): number | null {
  if (!(partsMeta?.length)) return null

  let total = 0
  let hasAmountPart = false

  for (const part of partsMeta) {
    const partAmount = calculateTradeDocumentPartAmount(part, partsModel?.[part.partCode]?.rows)
    if (partAmount == null) continue
    total += partAmount
    hasAmountPart = true
  }

  return hasAmountPart ? roundTo4(total) : null
}

export function syncTradeDocumentAmountField(args: {
  partsMeta: readonly PartMetadata[] | null | undefined
  partsModel: RecordParts | null | undefined
  model: EntityFormModel | null | undefined
}): void {
  if (!args.model || !Object.prototype.hasOwnProperty.call(args.model, 'amount')) return

  const amount = calculateTradeDocumentAmount(args.partsMeta, args.partsModel)
  if (amount == null) return

  args.model.amount = amount
}

export function buildTradeDocumentPartsPayload(
  partsMeta: readonly PartMetadata[] | null | undefined,
  partsModel: RecordParts | null | undefined,
): RecordParts | null {
  if (!(partsMeta?.length)) return partsModel ?? null

  const payload: RecordParts = {}

  for (const part of partsMeta) {
    const fields = listTradeDocumentPartFields(part)
    const form = createPartForm(fields)
    const rows = normalizeTradeDocumentPartRows(partsModel?.[part.partCode]?.rows)

    payload[part.partCode] = {
      rows: rows.map((row) => ({
        ...buildFieldsPayload(form, row),
        ordinal: Number(row.ordinal ?? 0) || 0,
      })),
    }
  }

  return payload
}

export async function hydrateTradeDocumentPartLookupRows(args: {
  entityTypeCode: string
  partsMeta: readonly PartMetadata[] | null | undefined
  partsModel: RecordParts | null | undefined
  lookupStore: LookupStoreApi
  behavior?: MetadataFormBehavior
}): Promise<void> {
  if (!(args.partsMeta?.length) || !args.partsModel) return

  const pending: PendingPartLookupHydration[] = []

  for (const part of args.partsMeta) {
    const fields = listTradeDocumentPartFields(part)
    const rows = args.partsModel[part.partCode]?.rows ?? []

    for (const row of rows) {
      for (const field of fields) {
        const value = row[field.key]
        if (isReferenceValue(value) || !isNonEmptyGuid(value)) continue

        const hint = resolveLookupHint({
          entityTypeCode: args.entityTypeCode,
          model: row,
          field,
          behavior: args.behavior,
        })

        if (!hint) continue

        pending.push({
          row,
          fieldKey: field.key,
          id: value,
          hint,
        })
      }
    }
  }

  if (pending.length === 0) return

  const catalogIdsByType = new Map<string, Set<string>>()
  const documentIdsByTypesKey = new Map<string, { documentTypes: string[]; ids: Set<string> }>()
  const coaIds = new Set<string>()

  for (const entry of pending) {
    if (entry.hint.kind === 'catalog') {
      const ids = catalogIdsByType.get(entry.hint.catalogType) ?? new Set<string>()
      ids.add(entry.id)
      catalogIdsByType.set(entry.hint.catalogType, ids)
      continue
    }

    if (entry.hint.kind === 'coa') {
      coaIds.add(entry.id)
      continue
    }

    const key = entry.hint.documentTypes.join('|')
    const group = documentIdsByTypesKey.get(key) ?? {
      documentTypes: entry.hint.documentTypes,
      ids: new Set<string>(),
    }
    group.ids.add(entry.id)
    documentIdsByTypesKey.set(key, group)
  }

  const tasks: Promise<void>[] = []
  for (const [catalogType, ids] of catalogIdsByType) {
    tasks.push(args.lookupStore.ensureCatalogLabels(catalogType, [...ids]))
  }
  if (coaIds.size > 0) tasks.push(args.lookupStore.ensureCoaLabels([...coaIds]))
  for (const group of documentIdsByTypesKey.values()) {
    tasks.push(args.lookupStore.ensureAnyDocumentLabels(group.documentTypes, [...group.ids]))
  }
  await Promise.all(tasks)

  for (const entry of pending) {
    const display = entry.hint.kind === 'catalog'
      ? args.lookupStore.labelForCatalog(entry.hint.catalogType, entry.id)
      : entry.hint.kind === 'coa'
        ? args.lookupStore.labelForCoa(entry.id)
        : args.lookupStore.labelForAnyDocument(entry.hint.documentTypes, entry.id)

    entry.row[entry.fieldKey] = {
      id: entry.id,
      display: display.trim() || entry.id,
    }
  }
}
