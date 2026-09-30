import { ensureResolvedLookupLabels } from '../metadata/filtering'
import type { LookupHint, LookupSource, LookupStoreApi, RecordFields } from '../metadata/types'
import { isNonEmptyGuid } from '../utils/guid'

type LookupPrefetchColumn = {
  key: string
  lookup?: LookupSource | null
}

type LookupPrefetchItem = {
  payload?: {
    fields?: RecordFields | null
  } | null
}

export async function prefetchLookupsForPage(args: {
  entityTypeCode: string
  columns: readonly LookupPrefetchColumn[]
  items: readonly LookupPrefetchItem[]
  lookupStore: LookupStoreApi
  resolveLookupHint: (entityTypeCode: string, fieldKey: string, lookup?: LookupSource | null) => LookupHint | null
}) {
  const groups = new Map<string, { hint: LookupHint; ids: Set<string> }>()

  for (const column of args.columns) {
    const hint = args.resolveLookupHint(args.entityTypeCode, column.key, column.lookup)
    if (!hint) continue

    const ids = args.items
      .map((item) => item.payload?.fields?.[column.key])
      .filter(isNonEmptyGuid)

    if (ids.length === 0) continue
    const key = hint.kind === 'catalog'
      ? `catalog:${hint.catalogType}`
      : hint.kind === 'document'
        ? `document:${hint.documentTypes.map((entry) => entry.trim()).filter(Boolean).join('|')}`
        : 'coa'
    const group = groups.get(key) ?? { hint, ids: new Set<string>() }
    ids.forEach((id) => group.ids.add(id))
    groups.set(key, group)
  }

  await Promise.allSettled(
    Array.from(groups.values(), ({ hint, ids }) =>
      Promise.resolve().then(() => ensureResolvedLookupLabels(args.lookupStore, hint, Array.from(ids)))),
  )
}
