import {
  buildLookupFieldTargetUrl,
  normalizeLookupValue,
  searchResolvedLookupItems,
  useLookupStore,
  type MetadataFormBehavior,
} from '@ngbplatform/ui'

export const metadataFormBehavior: MetadataFormBehavior = {
  searchLookup: ({ hint, query, signal }) => searchResolvedLookupItems(useLookupStore(), hint, query, { signal }),
  buildLookupTargetUrl: ({ hint, value, routeFullPath }) => buildLookupFieldTargetUrl({
    hint,
    value: normalizeLookupValue(value),
    route: { fullPath: routeFullPath },
  }),
}
