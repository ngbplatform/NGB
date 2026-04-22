import { dataTypeKind } from './dataTypes'
import { resolveNgbMetadataFormBehavior } from './config'
import { tryExtractReferenceId } from './entityModel'
import type { EntityFormModel, FieldHiddenArgs, FieldMetadata, FieldReadonlyArgs, FormMetadata, JsonValue, MetadataFormBehavior, RecordFields } from './types'

export function flattenFormFields(form?: FormMetadata | null): FieldMetadata[] {
  if (!form?.sections) return []

  const out: FieldMetadata[] = []
  for (const section of form.sections) {
    for (const row of section.rows ?? []) {
      for (const field of row.fields ?? []) out.push(field)
    }
  }

  return out
}

export function defaultFindDisplayField(form: FormMetadata): FieldMetadata | null {
  for (const field of flattenFormFields(form)) {
    if (field.key === 'display') return field
  }

  return null
}

export function defaultIsFieldReadonly(args: FieldReadonlyArgs): boolean {
  if (args.forceReadonly) return true
  if (args.field.isReadOnly) return true
  if (args.status !== undefined && args.field.readOnlyWhenStatusIn?.includes(args.status)) return true
  return false
}

export function defaultIsFieldHidden(_args: FieldHiddenArgs): boolean {
  return false
}

function tryFlattenRef(value: unknown): unknown {
  const referenceId = tryExtractReferenceId(value)
  return referenceId ?? value
}

export function ensureModelKeys(form: FormMetadata | null | undefined, model: EntityFormModel): void {
  for (const field of flattenFormFields(form)) {
    if (!(field.key in model) || model[field.key] === undefined) {
      model[field.key] = dataTypeKind(field.dataType) === 'Boolean' || field.uiControl === 5 ? false : null
    }
  }
}

function normalizeValue(field: FieldMetadata, value: unknown): JsonValue {
  let normalized = value
  if (normalized === '') normalized = null
  if (normalized === undefined) normalized = null

  normalized = tryFlattenRef(normalized)

  if (normalized === null) return null

  switch (field.uiControl) {
    case 5:
      return !!normalized
    case 3:
    case 4:
      if (typeof normalized === 'number') return normalized
      if (typeof normalized === 'string') {
        const number = Number(normalized)
        return Number.isFinite(number) ? number : normalized
      }
      return normalized as JsonValue
    case 6:
    case 7:
      return normalized as JsonValue
  }

  switch (dataTypeKind(field.dataType)) {
    case 'Int32': {
      if (typeof normalized === 'number') return Math.trunc(normalized)
      if (typeof normalized === 'string') {
        const number = Number.parseInt(normalized, 10)
        return Number.isFinite(number) ? number : normalized
      }
      return normalized as JsonValue
    }
    case 'Decimal':
    case 'Money': {
      if (typeof normalized === 'number') return normalized
      if (typeof normalized === 'string') {
        const number = Number(normalized)
        return Number.isFinite(number) ? number : normalized
      }
      return normalized as JsonValue
    }
    case 'Boolean':
      return !!normalized
    default:
      return normalized as JsonValue
  }
}

export function buildFieldsPayload(form: FormMetadata | null | undefined, model: EntityFormModel): RecordFields {
  const fields = flattenFormFields(form)
  const payload: RecordFields = {}

  for (const field of fields) {
    payload[field.key] = normalizeValue(field, model[field.key])
  }

  return payload
}

export function resolveMetadataFormBehavior(override?: MetadataFormBehavior): MetadataFormBehavior {
  return resolveNgbMetadataFormBehavior(override)
}
