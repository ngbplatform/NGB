import {
  buildLookupFieldTargetUrl,
  getCatalogTypeMetadata,
  getDocumentTypeMetadata,
  searchResolvedLookupItems,
  useLookupStore,
  type MetadataFrameworkConfig,
  type MetadataFormBehavior,
} from 'ngb-ui-framework'

import { getLookupHint } from '../lookup/hints'
import { findDisplayField, isFieldHidden, isFieldReadonly, resolveFieldOptions } from './formBehavior'

export const pmMetadataFormBehavior: MetadataFormBehavior = {
  resolveFieldOptions: ({ entityTypeCode, field }) => resolveFieldOptions(entityTypeCode, field.key),
  resolveLookupHint: ({ entityTypeCode, field }) => getLookupHint(entityTypeCode, field.key, field.lookup),
  isFieldReadonly,
  isFieldHidden,
  findDisplayField,
  searchLookup: async ({ hint, query }) => {
    const lookupStore = useLookupStore()
    return await searchResolvedLookupItems(lookupStore, hint, query)
  },
  buildLookupTargetUrl: async ({ hint, value, routeFullPath }) =>
    await buildLookupFieldTargetUrl({
      hint,
      value,
      route: { fullPath: routeFullPath },
    }),
}

export function createPmMetadataConfig(): MetadataFrameworkConfig {
  return {
    loadCatalogTypeMetadata: getCatalogTypeMetadata,
    loadDocumentTypeMetadata: getDocumentTypeMetadata,
    formBehavior: pmMetadataFormBehavior,
  }
}
