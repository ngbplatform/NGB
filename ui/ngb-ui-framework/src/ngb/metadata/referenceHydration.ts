import { resolveNgbMetadataFormBehavior } from './config'
import { isNonEmptyGuid } from '../utils/guid'
import { flattenFormFields } from './entityForm'
import { isReferenceValue } from './entityModel'
import { resolveLookupHint } from './lookup'
import type { EntityFormModel, FormMetadata, LookupHint, LookupStoreApi, MetadataFormBehavior, ReferenceValue } from './types'

type PendingReferenceHydration = {
  fieldKey: string
  id: string
  hint: LookupHint
}

function buildReferenceValue(id: string, display: string): ReferenceValue {
  return {
    id,
    display: display.trim() || id,
  }
}

function resolveReferenceLabel(lookupStore: LookupStoreApi, hint: LookupHint, id: string): string {
  if (hint.kind === 'catalog') return lookupStore.labelForCatalog(hint.catalogType, id)
  if (hint.kind === 'coa') return lookupStore.labelForCoa(id)
  return lookupStore.labelForAnyDocument(hint.documentTypes, id)
}

export async function hydrateEntityReferenceFieldsForEditing(args: {
  entityTypeCode: string
  form: FormMetadata | null | undefined
  model: EntityFormModel
  lookupStore: LookupStoreApi
  behavior?: MetadataFormBehavior
}): Promise<void> {
  const pending: PendingReferenceHydration[] = []
  const behavior = resolveNgbMetadataFormBehavior(args.behavior)

  for (const field of flattenFormFields(args.form)) {
    const value = args.model[field.key]
    if (isReferenceValue(value) || !isNonEmptyGuid(value)) continue

    const hint = resolveLookupHint({
      entityTypeCode: args.entityTypeCode,
      model: args.model,
      field,
      behavior,
    })

    if (!hint) continue

    pending.push({
      fieldKey: field.key,
      id: value,
      hint,
    })
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
    const display = resolveReferenceLabel(args.lookupStore, entry.hint, entry.id)
    args.model[entry.fieldKey] = buildReferenceValue(entry.id, display)
  }
}
