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
} from '@ngbplatform/ui'

type PendingPartLookupHydration = {
  row: RecordPartRow
  fieldKey: string
  id: string
  hint: LookupHint
}

const AGENCY_BILLING_DOCUMENT_AMOUNT_FIELD_KEYS = ['line_amount', 'applied_amount', 'amount'] as const
const AGENCY_BILLING_DOCUMENT_COST_FIELD_KEYS = ['line_cost_amount', 'cost_amount'] as const

export type AgencyBillingDocumentPartErrors = Record<string, Record<number, Record<string, string>>>
export type AgencyBillingDocumentComputedFields = {
  amount: number | null
  totalHours: number | null
  costAmount: number | null
}

const localRowKeys = new WeakMap<RecordPartRow, string>()

function buildAgencyBillingDocumentPartRowKey(): string {
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

export function listAgencyBillingDocumentPartFields(part: PartMetadata): FieldMetadata[] {
  return (part.list?.columns ?? [])
    .filter((column) => column.key !== 'ordinal')
    .map(createPartField)
}

export function ensureAgencyBillingDocumentPartRowKey(row: RecordPartRow): string {
  const existing = typeof row.__row_key === 'string' ? row.__row_key.trim() : ''
  if (existing) return existing

  const cached = localRowKeys.get(row)
  if (cached) return cached

  const created = buildAgencyBillingDocumentPartRowKey()
  localRowKeys.set(row, created)
  return created
}

export function normalizeAgencyBillingDocumentPartRows(rows: readonly RecordPartRow[] | null | undefined): RecordPartRow[] {
  return (rows ?? []).map((row, index) => ({
    ...row,
    __row_key: ensureAgencyBillingDocumentPartRowKey(row),
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

function resolveAmountSourceField(
  part: PartMetadata,
  candidates: readonly string[],
): string | null {
  const keys = new Set((part.list?.columns ?? []).map((column) => column.key))
  return candidates.find((key) => keys.has(key)) ?? null
}

export function resolveAgencyBillingDocumentAmountSourceField(part: PartMetadata): string | null {
  return resolveAmountSourceField(part, AGENCY_BILLING_DOCUMENT_AMOUNT_FIELD_KEYS)
}

export function resolveAgencyBillingDocumentCostSourceField(part: PartMetadata): string | null {
  return resolveAmountSourceField(part, AGENCY_BILLING_DOCUMENT_COST_FIELD_KEYS)
}

export function calculateAgencyBillingDocumentPartAmount(
  part: PartMetadata,
  rows: readonly RecordPartRow[] | null | undefined,
): number | null {
  const amountField = resolveAgencyBillingDocumentAmountSourceField(part)
  if (!amountField) return null

  let total = 0
  for (const row of rows ?? []) {
    const amount = parseDecimal(row[amountField])
    if (amount != null) total += amount
  }

  return roundTo4(total)
}

export function recomputeAgencyBillingDocumentPartRow(documentType: string, row: RecordPartRow): RecordPartRow {
  if (documentType === 'ab.timesheet') {
    const hours = parseDecimal(row.hours)
    const billable = !!row.billable
    const billingRate = parseDecimal(row.billing_rate)
    const costRate = parseDecimal(row.cost_rate)

    return {
      ...row,
      line_amount: billable
        ? (hours != null && billingRate != null ? roundTo4(hours * billingRate) : null)
        : 0,
      line_cost_amount: hours != null && costRate != null ? roundTo4(hours * costRate) : null,
    }
  }

  if (documentType === 'ab.sales_invoice') {
    const quantityHours = parseDecimal(row.quantity_hours)
    const rate = parseDecimal(row.rate)

    return {
      ...row,
      line_amount: quantityHours != null && rate != null ? roundTo4(quantityHours * rate) : null,
    }
  }

  return row
}

export function syncAgencyBillingDocumentComputedFields(args: {
  documentType: string
  partsMeta: readonly PartMetadata[] | null | undefined
  partsModel: RecordParts | null | undefined
  model: EntityFormModel | null | undefined
}, calculated?: AgencyBillingDocumentComputedFields): void {
  if (!args.model) return

  const fields = calculated ?? calculateAgencyBillingDocumentComputedFields(
    args.documentType,
    args.partsMeta,
    args.partsModel,
  )

  if (Object.prototype.hasOwnProperty.call(args.model, 'amount')) {
    if (fields.amount != null) args.model.amount = fields.amount
  }

  if (args.documentType === 'ab.timesheet' && Object.prototype.hasOwnProperty.call(args.model, 'total_hours')) {
    if (fields.totalHours != null) args.model.total_hours = fields.totalHours
  }

  if (args.documentType === 'ab.timesheet' && Object.prototype.hasOwnProperty.call(args.model, 'cost_amount')) {
    if (fields.costAmount != null) args.model.cost_amount = fields.costAmount
  }
}

export function calculateAgencyBillingDocumentComputedFields(
  documentType: string,
  partsMeta: readonly PartMetadata[] | null | undefined,
  partsModel: RecordParts | null | undefined,
): AgencyBillingDocumentComputedFields {
  if (!(partsMeta?.length)) {
    return { amount: null, totalHours: null, costAmount: null }
  }

  const fieldsByPart = new Map<string, { amount: string | null; cost: string | null }>()
  let hasAmountPart = false
  let hasCostPart = false
  for (const part of partsMeta) {
    const amount = resolveAgencyBillingDocumentAmountSourceField(part)
    const cost = resolveAgencyBillingDocumentCostSourceField(part)
    fieldsByPart.set(part.partCode, { amount, cost })
    hasAmountPart ||= amount != null
    hasCostPart ||= cost != null
  }

  let amountTotal = 0
  let costTotal = 0
  let hoursTotal = 0
  let hasHours = false
  for (const [partCode, modelPart] of Object.entries(partsModel ?? {})) {
    const fields = fieldsByPart.get(partCode)
    for (const row of modelPart?.rows ?? []) {
      if (fields?.amount) {
        const amount = parseDecimal(row[fields.amount])
        if (amount != null) amountTotal += amount
      }
      if (documentType === 'ab.timesheet' && fields?.cost) {
        const cost = parseDecimal(row[fields.cost])
        if (cost != null) costTotal += cost
      }
      if (documentType === 'ab.timesheet') {
        const hours = parseDecimal(row.hours)
        if (hours != null) {
          hoursTotal += hours
          hasHours = true
        }
      }
    }
  }

  return {
    amount: hasAmountPart ? roundTo4(amountTotal) : null,
    totalHours: documentType === 'ab.timesheet' && hasHours ? roundTo4(hoursTotal) : null,
    costAmount: documentType === 'ab.timesheet' && hasCostPart ? roundTo4(costTotal) : null,
  }
}

export function buildAgencyBillingDocumentPartsPayload(
  documentType: string,
  partsMeta: readonly PartMetadata[] | null | undefined,
  partsModel: RecordParts | null | undefined,
): RecordParts | null {
  if (!(partsMeta?.length)) return partsModel ?? null

  const payload: RecordParts = {}

  for (const part of partsMeta) {
    const fields = listAgencyBillingDocumentPartFields(part)
    const form = createPartForm(fields)
    const rows = normalizeAgencyBillingDocumentPartRows(partsModel?.[part.partCode]?.rows)

    payload[part.partCode] = {
      rows: rows.map((row) => ({
        ...buildFieldsPayload(form, recomputeAgencyBillingDocumentPartRow(documentType, row)),
        ordinal: Number(row.ordinal),
      })),
    }
  }

  return payload
}

export async function hydrateAgencyBillingDocumentPartLookupRows(args: {
  entityTypeCode: string
  partsMeta: readonly PartMetadata[] | null | undefined
  partsModel: RecordParts | null | undefined
  lookupStore: LookupStoreApi
  behavior?: MetadataFormBehavior
}): Promise<void> {
  if (!(args.partsMeta?.length) || !args.partsModel) return

  const pending: PendingPartLookupHydration[] = []

  for (const part of args.partsMeta) {
    const fields = listAgencyBillingDocumentPartFields(part)
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
