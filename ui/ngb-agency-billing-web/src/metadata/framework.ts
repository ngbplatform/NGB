import {
  buildLookupFieldTargetUrl,
  getCatalogTypeMetadata,
  getDocumentTypeMetadata,
  searchResolvedLookupItems,
  useLookupStore,
  type CatalogTypeMetadata,
  type ColumnMetadata,
  type MetadataFormBehavior,
  type MetadataFrameworkConfig,
} from 'ngb-ui-framework'

import { getAgencyBillingLookupHint } from '../lookup/hints'
import { findDisplayField, isFieldHidden, isFieldReadonly } from './formBehavior'

const CLIENT_LIST_COLUMN_KEYS = ['display', 'client_code', 'status', 'billing_contact', 'payment_terms_id', 'is_active']
const TEAM_MEMBER_LIST_COLUMN_KEYS = ['display', 'member_code', 'member_type', 'billable_by_default', 'default_billing_rate', 'is_active']
const PROJECT_LIST_COLUMN_KEYS = ['display', 'client_id', 'project_manager_id', 'status', 'billing_model', 'budget_amount']
const RATE_CARD_LIST_COLUMN_KEYS = ['display', 'client_id', 'project_id', 'team_member_id', 'billing_rate', 'effective_from', 'is_active']
const SERVICE_ITEM_LIST_COLUMN_KEYS = ['display', 'code', 'unit_of_measure', 'default_revenue_account_id', 'is_active']
const PAYMENT_TERMS_LIST_COLUMN_KEYS = ['display', 'code', 'due_days', 'is_active']

function pickColumns(columns: readonly ColumnMetadata[] | null | undefined, keys: readonly string[]): ColumnMetadata[] {
  const available = new Map((columns ?? []).map((column) => [column.key, column] as const))
  return keys
    .map((key) => available.get(key) ?? null)
    .filter((column): column is ColumnMetadata => column !== null)
}

function normalizeAgencyBillingCatalogMetadata(metadata: CatalogTypeMetadata): CatalogTypeMetadata {
  const list = metadata.list
  if (!list?.columns?.length) return metadata

  switch (metadata.catalogType) {
    case 'ab.client':
      return { ...metadata, list: { ...list, columns: pickColumns(list.columns, CLIENT_LIST_COLUMN_KEYS) } }
    case 'ab.team_member':
      return { ...metadata, list: { ...list, columns: pickColumns(list.columns, TEAM_MEMBER_LIST_COLUMN_KEYS) } }
    case 'ab.project':
      return { ...metadata, list: { ...list, columns: pickColumns(list.columns, PROJECT_LIST_COLUMN_KEYS) } }
    case 'ab.rate_card':
      return { ...metadata, list: { ...list, columns: pickColumns(list.columns, RATE_CARD_LIST_COLUMN_KEYS) } }
    case 'ab.service_item':
      return { ...metadata, list: { ...list, columns: pickColumns(list.columns, SERVICE_ITEM_LIST_COLUMN_KEYS) } }
    case 'ab.payment_terms':
      return { ...metadata, list: { ...list, columns: pickColumns(list.columns, PAYMENT_TERMS_LIST_COLUMN_KEYS) } }
    default:
      return metadata
  }
}

export const agencyBillingMetadataFormBehavior: MetadataFormBehavior = {
  findDisplayField,
  isFieldHidden,
  isFieldReadonly,
  resolveLookupHint: ({ entityTypeCode, field }) => getAgencyBillingLookupHint(entityTypeCode, field.key, field.lookup),
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

export function createAgencyBillingMetadataConfig(): MetadataFrameworkConfig {
  return {
    loadCatalogTypeMetadata: async (catalogType) => normalizeAgencyBillingCatalogMetadata(await getCatalogTypeMetadata(catalogType)),
    loadDocumentTypeMetadata: getDocumentTypeMetadata,
    formBehavior: agencyBillingMetadataFormBehavior,
  }
}
