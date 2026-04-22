import type { EntityFormModel, FieldMetadata, FormMetadata } from 'ngb-ui-framework'

const AGENCY_BILLING_DOCUMENT_TYPES_WITH_COMPUTED_AMOUNT = new Set<string>([
  'ab.timesheet',
  'ab.sales_invoice',
  'ab.customer_payment',
])

const AGENCY_BILLING_DOCUMENT_TYPES_WITH_COMPUTED_COST = new Set<string>([
  'ab.timesheet',
])

const AGENCY_BILLING_CATALOG_TYPES_WITH_COMPUTED_DISPLAY = new Set<string>([
  'ab.client',
  'ab.team_member',
  'ab.project',
  'ab.rate_card',
  'ab.service_item',
  'ab.payment_terms',
])

function isReadonlyAgencyBillingComputedField(entityTypeCode: string, fieldKey: string): boolean {
  if (fieldKey === 'amount' && AGENCY_BILLING_DOCUMENT_TYPES_WITH_COMPUTED_AMOUNT.has(entityTypeCode)) return true
  if (fieldKey === 'cost_amount' && AGENCY_BILLING_DOCUMENT_TYPES_WITH_COMPUTED_COST.has(entityTypeCode)) return true
  return false
}

export function isFieldReadonly(args: {
  entityTypeCode: string
  model: EntityFormModel
  field: FieldMetadata
  status?: number
  forceReadonly?: boolean
}): boolean {
  const { entityTypeCode, field, status, forceReadonly } = args

  if (forceReadonly) return true
  if (field.isReadOnly) return true
  if (field.key === 'display' && AGENCY_BILLING_CATALOG_TYPES_WITH_COMPUTED_DISPLAY.has(entityTypeCode)) return true
  if (isReadonlyAgencyBillingComputedField(entityTypeCode, field.key)) return true
  if (status !== undefined && field.readOnlyWhenStatusIn?.includes(status)) return true
  return false
}

export function isFieldHidden(args: {
  entityTypeCode: string
  model: EntityFormModel
  field: FieldMetadata
  isDocumentEntity: boolean
}): boolean {
  const { entityTypeCode, field, isDocumentEntity } = args

  if (isDocumentEntity && (field.key === 'display' || field.key === 'number')) return true
  if (isDocumentEntity && isReadonlyAgencyBillingComputedField(entityTypeCode, field.key)) return true
  if (!isDocumentEntity && field.key === 'display' && AGENCY_BILLING_CATALOG_TYPES_WITH_COMPUTED_DISPLAY.has(entityTypeCode)) return true
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
