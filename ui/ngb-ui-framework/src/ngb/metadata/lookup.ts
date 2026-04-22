import { resolveNgbMetadataFormBehavior } from './config'
import type { EntityFormModel, FieldMetadata, LookupHint, LookupSource, MetadataFormBehavior } from './types'

export function lookupHintFromSource(source?: LookupSource | null): LookupHint | null {
  if (!source) return null

  if (source.kind === 'catalog') return { kind: 'catalog', catalogType: source.catalogType }
  if (source.kind === 'document') return { kind: 'document', documentTypes: [...source.documentTypes] }
  return { kind: 'coa' }
}

export function resolveLookupHint(args: {
  entityTypeCode: string
  model: EntityFormModel
  field: FieldMetadata
  behavior?: MetadataFormBehavior
}): LookupHint | null {
  const behavior = resolveNgbMetadataFormBehavior(args.behavior)

  return behavior.resolveLookupHint?.({
    entityTypeCode: args.entityTypeCode,
    model: args.model,
    field: args.field,
  }) ?? lookupHintFromSource(args.field.lookup)
}
