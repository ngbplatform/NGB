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
} from 'ngb-ui-framework'

import { getTradeLookupHint } from '../lookup/hints'
import { findDisplayField, isFieldHidden, isFieldReadonly } from './formBehavior'

const TRADE_PARTY_LIST_COLUMN_KEYS = ['display', 'party_number', 'is_customer', 'is_vendor', 'is_active']
const TRADE_ITEM_LIST_COLUMN_KEYS = ['display', 'sku', 'unit_of_measure_id', 'item_type']
const TRADE_UNIT_OF_MEASURE_LIST_COLUMN_KEYS = ['display', 'is_active', 'code', 'symbol']
const TRADE_WAREHOUSE_LIST_COLUMN_KEYS = ['display', 'name', 'warehouse_code', 'address', 'is_active']

const TRADE_PARTY_FALLBACK_COLUMNS: Record<string, ColumnMetadata> = {
  is_customer: {
    key: 'is_customer',
    label: 'Is Customer',
    dataType: 'Boolean',
    isSortable: true,
    align: 1,
  },
  is_vendor: {
    key: 'is_vendor',
    label: 'Is Vendor',
    dataType: 'Boolean',
    isSortable: true,
    align: 1,
  },
  is_active: {
    key: 'is_active',
    label: 'Is Active',
    dataType: 'Boolean',
    isSortable: true,
    align: 1,
  },
}

function pickColumns(
  columns: readonly ColumnMetadata[] | null | undefined,
  keys: readonly string[],
  fallbacks?: Record<string, ColumnMetadata>,
): ColumnMetadata[] {
  const available = new Map((columns ?? []).map((column) => [column.key, column] as const))
  return keys
    .map((key) => available.get(key) ?? fallbacks?.[key] ?? null)
    .filter((column): column is ColumnMetadata => column !== null)
}

function normalizeTradeCatalogMetadata(metadata: CatalogTypeMetadata): CatalogTypeMetadata {
  const list = metadata.list
  if (!list?.columns?.length) return metadata

  if (metadata.catalogType === 'trd.item') {
    return {
      ...metadata,
      list: {
        ...list,
        columns: pickColumns(list.columns, TRADE_ITEM_LIST_COLUMN_KEYS),
      },
    }
  }

  if (metadata.catalogType === 'trd.party') {
    return {
      ...metadata,
      list: {
        ...list,
        columns: pickColumns(list.columns, TRADE_PARTY_LIST_COLUMN_KEYS, TRADE_PARTY_FALLBACK_COLUMNS),
      },
    }
  }

  if (metadata.catalogType === 'trd.unit_of_measure') {
    return {
      ...metadata,
      list: {
        ...list,
        columns: pickColumns(list.columns, TRADE_UNIT_OF_MEASURE_LIST_COLUMN_KEYS),
      },
    }
  }

  if (metadata.catalogType === 'trd.warehouse') {
    return {
      ...metadata,
      list: {
        ...list,
        columns: pickColumns(list.columns, TRADE_WAREHOUSE_LIST_COLUMN_KEYS),
      },
    }
  }

  return metadata
}

export const tradeMetadataFormBehavior: MetadataFormBehavior = {
  findDisplayField,
  isFieldHidden,
  isFieldReadonly,
  resolveLookupHint: ({ entityTypeCode, field }) => getTradeLookupHint(entityTypeCode, field.key, field.lookup),
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

export function createTradeMetadataConfig(): MetadataFrameworkConfig {
  return {
    loadCatalogTypeMetadata: async (catalogType) => normalizeTradeCatalogMetadata(await getCatalogTypeMetadata(catalogType)),
    loadDocumentTypeMetadata: getDocumentTypeMetadata,
    formBehavior: tradeMetadataFormBehavior,
  }
}
