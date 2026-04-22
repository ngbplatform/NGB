import type { FieldMetadata, FormMetadata } from 'ngb-ui-framework'

const TRADE_DOCUMENT_TYPES_WITH_COMPUTED_AMOUNT = new Set<string>([
  'trd.purchase_receipt',
  'trd.sales_invoice',
  'trd.inventory_adjustment',
  'trd.customer_return',
  'trd.vendor_return',
])

function isHiddenTradeNameField(entityTypeCode: string, fieldKey: string): boolean {
  return (entityTypeCode === 'trd.item' || entityTypeCode === 'trd.unit_of_measure' || entityTypeCode === 'trd.party') && fieldKey === 'name'
}

function isReadonlyTradeDisplayField(entityTypeCode: string, fieldKey: string): boolean {
  return entityTypeCode === 'trd.warehouse' && fieldKey === 'display'
}

function isReadonlyTradeComputedAmountField(entityTypeCode: string, fieldKey: string): boolean {
  return fieldKey === 'amount' && TRADE_DOCUMENT_TYPES_WITH_COMPUTED_AMOUNT.has(entityTypeCode)
}

export function isFieldReadonly(args: {
  entityTypeCode: string
  field: FieldMetadata
  status?: number
  forceReadonly?: boolean
}): boolean {
  const { entityTypeCode, field, status, forceReadonly } = args

  if (forceReadonly) return true
  if (field.isReadOnly) return true
  if (isReadonlyTradeDisplayField(entityTypeCode, field.key)) return true
  if (isReadonlyTradeComputedAmountField(entityTypeCode, field.key)) return true
  if (status !== undefined && field.readOnlyWhenStatusIn?.includes(status)) return true
  return false
}

export function isFieldHidden(args: {
  entityTypeCode: string
  field: FieldMetadata
  isDocumentEntity: boolean
}): boolean {
  const { entityTypeCode, field, isDocumentEntity } = args

  if (isDocumentEntity && (field.key === 'display' || field.key === 'number')) return true
  if (isDocumentEntity && isReadonlyTradeComputedAmountField(entityTypeCode, field.key)) return true
  if (isHiddenTradeNameField(entityTypeCode, field.key)) return true
  return false
}

export function findDisplayField(form: FormMetadata): FieldMetadata | null {
  for (const section of form.sections ?? []) {
    for (const row of section.rows ?? []) {
      for (const field of row.fields ?? []) {
        if (field?.key === 'display') return field
      }
    }
  }

  return null
}
