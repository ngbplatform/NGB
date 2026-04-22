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
  const tasks: Promise<void>[] = []

  for (const column of args.columns) {
    const hint = args.resolveLookupHint(args.entityTypeCode, column.key, column.lookup)
    if (!hint) continue

    const ids = args.items
      .map((item) => item.payload?.fields?.[column.key])
      .filter(isNonEmptyGuid)

    if (ids.length === 0) continue
    tasks.push(Promise.resolve().then(() => ensureResolvedLookupLabels(args.lookupStore, hint, ids)))
  }

  if (tasks.length > 0) {
    await Promise.allSettled(tasks)
  }
}
