import type { FieldMetadata, FormMetadata } from 'ngb-ui-framework'

const CRM_DOCUMENT_TYPES_WITH_COMPUTED_AMOUNT = new Set<string>([
  'crm.quote',
])

function isReadonlyCRMDisplayField(entityTypeCode: string, fieldKey: string): boolean {
  return fieldKey === 'display'
    && (
      entityTypeCode === 'crm.account'
      || entityTypeCode === 'crm.contact'
      || entityTypeCode === 'crm.product'
      || entityTypeCode === 'crm.opportunity_stage'
    )
}

function isReadonlyCRMComputedAmountField(entityTypeCode: string, fieldKey: string): boolean {
  return fieldKey === 'amount' && CRM_DOCUMENT_TYPES_WITH_COMPUTED_AMOUNT.has(entityTypeCode)
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
  if (isReadonlyCRMDisplayField(entityTypeCode, field.key)) return true
  if (isReadonlyCRMComputedAmountField(entityTypeCode, field.key)) return true
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
  if (isDocumentEntity && isReadonlyCRMComputedAmountField(entityTypeCode, field.key)) return true
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
