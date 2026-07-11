import {
  buildLookupFieldTargetUrl,
  type CatalogTypeMetadata,
  type ColumnMetadata,
  getCatalogTypeMetadata,
  getDocumentTypeMetadata,
  searchResolvedLookupItems,
  useLookupStore,
  type MetadataFrameworkConfig,
  type MetadataFormBehavior,
} from '@ngbplatform/ui'

import { getCRMLookupHint } from '../lookup/hints'
import { findDisplayField, isFieldHidden, isFieldReadonly } from './formBehavior'

const CRM_CATALOG_LIST_COLUMNS: Record<string, string[]> = {
  'crm.account': ['display', 'account_number', 'account_type', 'industry', 'email', 'phone', 'is_active'],
  'crm.contact': ['display', 'account_id', 'title', 'email', 'phone', 'is_primary', 'is_active'],
  'crm.product': ['display', 'sku', 'family', 'list_price', 'currency', 'is_active'],
  'crm.opportunity_stage': ['display', 'stage_code', 'ordinal', 'default_probability', 'is_closed', 'is_won', 'is_active'],
}

function pickColumns(columns: readonly ColumnMetadata[] | null | undefined, keys: readonly string[]): ColumnMetadata[] {
  const available = new Map((columns ?? []).map((column) => [column.key, column] as const))
  return keys
    .map((key) => available.get(key) ?? null)
    .filter((column): column is ColumnMetadata => column !== null)
}

function normalizeCRMCatalogMetadata(metadata: CatalogTypeMetadata): CatalogTypeMetadata {
  const keys = CRM_CATALOG_LIST_COLUMNS[metadata.catalogType]
  if (!keys || !metadata.list?.columns?.length) return metadata

  return {
    ...metadata,
    list: {
      ...metadata.list,
      columns: pickColumns(metadata.list.columns, keys),
    },
  }
}

export const crmMetadataFormBehavior: MetadataFormBehavior = {
  findDisplayField,
  isFieldHidden,
  isFieldReadonly,
  resolveLookupHint: ({ entityTypeCode, field }) => getCRMLookupHint(entityTypeCode, field.key, field.lookup),
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

export function createCRMMetadataConfig(): MetadataFrameworkConfig {
  return {
    loadCatalogTypeMetadata: async (catalogType) => normalizeCRMCatalogMetadata(await getCatalogTypeMetadata(catalogType)),
    loadDocumentTypeMetadata: getDocumentTypeMetadata,
    formBehavior: crmMetadataFormBehavior,
  }
}
