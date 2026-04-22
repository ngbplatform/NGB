import { asTrimmedString } from 'ngb-ui-framework'
import type { DocumentStatus, EntityFormModel, FieldMetadata, FormMetadata } from 'ngb-ui-framework'

export type EntityFieldOption = {
  value: string
  label: string
}

const fieldOptionsByKey: Record<string, EntityFieldOption[]> = {
  'pm.property.kind': [
    { value: 'Building', label: 'Building' },
    { value: 'Unit', label: 'Unit' },
  ],
  'pm.maintenance_request.priority': [
    { value: 'Emergency', label: 'Emergency' },
    { value: 'High', label: 'High' },
    { value: 'Normal', label: 'Normal' },
    { value: 'Low', label: 'Low' },
  ],
  'pm.work_order.cost_responsibility': [
    { value: 'Owner', label: 'Owner' },
    { value: 'Tenant', label: 'Tenant' },
    { value: 'Company', label: 'Company' },
    { value: 'Unknown', label: 'Unknown' },
  ],
  'pm.work_order_completion.outcome': [
    { value: 'Completed', label: 'Completed' },
    { value: 'Cancelled', label: 'Cancelled' },
    { value: 'UnableToComplete', label: 'Unable to complete' },
  ],
}

export function resolveFieldOptions(entityTypeCode: string, fieldKey: string): EntityFieldOption[] | null {
  return fieldOptionsByKey[`${entityTypeCode}.${fieldKey}`] ?? null
}

export function getPmPropertyKind(entityTypeCode: string, model: EntityFormModel): 'Building' | 'Unit' | null {
  if (entityTypeCode !== 'pm.property') return null

  const value = asTrimmedString(model.kind)
  if (value === 'Building') return 'Building'
  if (value === 'Unit') return 'Unit'
  return null
}

export function isFieldReadonly(args: {
  entityTypeCode: string
  model: EntityFormModel
  field: FieldMetadata
  status?: DocumentStatus
  forceReadonly?: boolean
}): boolean {
  const { entityTypeCode, model, field, status, forceReadonly } = args

  if (forceReadonly) return true
  if (field.isReadOnly) return true
  if (status !== undefined && (field.key === 'display' || field.key === 'number')) return true

  if ((entityTypeCode === 'pm.property' || entityTypeCode === 'pm.bank_account') && field.key === 'display') {
    return true
  }

  if (entityTypeCode === 'pm.property' && field.key === 'kind') {
    const kind = asTrimmedString(model.kind)
    if (kind === 'Building' || kind === 'Unit') return true
  }

  if (status && field.readOnlyWhenStatusIn?.includes(status)) return true
  return false
}

export function isFieldHidden(args: {
  entityTypeCode: string
  model: EntityFormModel
  field: FieldMetadata
  isDocumentEntity: boolean
}): boolean {
  const { entityTypeCode, model, field, isDocumentEntity } = args

  if (isDocumentEntity && (field.key === 'display' || field.key === 'number')) return true
  if (entityTypeCode !== 'pm.property') return false

  const kind = getPmPropertyKind(entityTypeCode, model)
  const buildingOnly = new Set(['address_line1', 'address_line2', 'city', 'state', 'zip'])
  const unitOnly = new Set(['parent_property_id', 'unit_no'])

  if (field.key === 'kind') return kind !== null
  if (!kind) return buildingOnly.has(field.key) || unitOnly.has(field.key)
  if (kind === 'Building') return unitOnly.has(field.key)
  if (kind === 'Unit') return buildingOnly.has(field.key)
  return false
}

export function findDisplayField(form: FormMetadata): FieldMetadata | null {
  const sections = form.sections ?? []
  for (const section of sections) {
    for (const row of section.rows ?? []) {
      for (const field of row.fields ?? []) {
        if (field?.key === 'display') return field
      }
    }
  }
  return null
}
