import { resolveNgbMetadataFormBehavior } from './config'
import { dataTypeKind } from './dataTypes'
import { isReferenceValue } from './entityModel'
import { resolveLookupHint } from './lookup'
import type { EntityFormModel, FieldMetadata, FieldOption, LookupHint, MetadataFormBehavior } from './types'

export type FieldRenderMode =
  | 'select'
  | 'lookup'
  | 'checkbox'
  | 'textarea'
  | 'reference-display'
  | 'date'
  | 'input'

export type FieldInputType = 'text' | 'number' | 'datetime-local'

export type FieldRendererState = {
  mode: FieldRenderMode
  inputType: FieldInputType
  fieldOptions: FieldOption[] | null
  hint: LookupHint | null
}

export function resolveFieldRendererState(args: {
  entityTypeCode: string
  model: EntityFormModel
  field: FieldMetadata
  modelValue: unknown
  behavior?: MetadataFormBehavior
}): FieldRendererState {
  const behavior = resolveNgbMetadataFormBehavior(args.behavior)
  const fieldOptions = behavior.resolveFieldOptions?.({
    entityTypeCode: args.entityTypeCode,
    model: args.model,
    field: args.field,
  }) ?? args.field.options ?? null
  const hint = resolveLookupHint({
    entityTypeCode: args.entityTypeCode,
    model: args.model,
    field: args.field,
    behavior,
  })
  const dataType = dataTypeKind(args.field.dataType)

  const isLookup = !!hint || dataType === 'Lookup'
  const isCheckbox = args.field.uiControl === 5 || dataType === 'Boolean'
  const isTextArea = args.field.uiControl === 2
  const isDate = args.field.uiControl === 6 || dataType === 'Date'
  const isDateTime = args.field.uiControl === 7 || dataType === 'DateTime'
  const isNumber = args.field.uiControl === 3 || dataType === 'Int32' || dataType === 'Decimal'
  const isMoney = args.field.uiControl === 4 || dataType === 'Money'

  if (fieldOptions) {
    return { mode: 'select', inputType: 'text', fieldOptions, hint }
  }

  if (isLookup && hint) {
    return { mode: 'lookup', inputType: 'text', fieldOptions, hint }
  }

  if (isCheckbox) {
    return { mode: 'checkbox', inputType: 'text', fieldOptions, hint }
  }

  if (isTextArea) {
    return { mode: 'textarea', inputType: 'text', fieldOptions, hint }
  }

  if (isReferenceValue(args.modelValue) && !hint) {
    return { mode: 'reference-display', inputType: 'text', fieldOptions, hint }
  }

  if (isDate) {
    return { mode: 'date', inputType: 'text', fieldOptions, hint }
  }

  return {
    mode: 'input',
    inputType: isDateTime ? 'datetime-local' : (isNumber || isMoney ? 'number' : 'text'),
    fieldOptions,
    hint,
  }
}
