import { describe, expect, it } from 'vitest'

import type {
  DocumentTypeMetadata,
  FilterFieldState,
  LookupStoreApi,
  MetadataFormBehavior,
} from '../../../../src/ngb/metadata/types'

describe('metadata types', () => {
  it('supports metadata form contracts, filter state, and lookup store interfaces', async () => {
    const documentType: DocumentTypeMetadata = {
      documentType: 'pm.invoice',
      displayName: 'Invoice',
      kind: 2,
      list: {
        columns: [
          {
            key: 'number',
            label: 'Number',
            dataType: 'string',
            isSortable: true,
            align: 1,
          },
        ],
      },
      form: {
        sections: [
          {
            title: 'General',
            rows: [
              {
                fields: [
                  {
                    key: 'customerId',
                    label: 'Customer',
                    dataType: 'reference',
                    uiControl: 1,
                    isRequired: true,
                    isReadOnly: false,
                  },
                ],
              },
            ],
          },
        ],
      },
      capabilities: {
        canCreate: true,
        canPost: true,
      },
    }
    const filterState: FilterFieldState = {
      raw: 'riverfront',
      items: [
        {
          id: 'cust-1',
          label: 'Riverfront Tower',
        },
      ],
      includeDescendants: true,
    }
    const lookupStore: LookupStoreApi = {
      searchCatalog: async () => [],
      searchCoa: async () => [],
      searchDocuments: async () => [],
      ensureCatalogLabels: async () => undefined,
      ensureCoaLabels: async () => undefined,
      ensureAnyDocumentLabels: async () => undefined,
      labelForCatalog: (_catalogType, id) => `catalog:${String(id)}`,
      labelForCoa: (id) => `coa:${String(id)}`,
      labelForAnyDocument: (_documentTypes, id) => `document:${String(id)}`,
    }
    const behavior: MetadataFormBehavior = {
      buildLookupTargetUrl: async ({ value, routeFullPath }) => `${routeFullPath}?id=${String(value)}`,
      resolveLookupHint: ({ entityTypeCode, field }) => ({
        kind: 'catalog',
        catalogType: `${entityTypeCode}.${field.key}`,
      }),
    }

    expect(documentType.form?.sections[0]?.title).toBe('General')
    expect(filterState.items[0]?.label).toBe('Riverfront Tower')
    expect(lookupStore.labelForCatalog('pm.customer', 'cust-1')).toBe('catalog:cust-1')
    await expect(behavior.buildLookupTargetUrl?.({
      hint: {
        kind: 'catalog',
        catalogType: 'pm.customer',
      },
      value: 'cust-1',
      routeFullPath: '/documents/pm.invoice/doc-1',
    })).resolves.toBe('/documents/pm.invoice/doc-1?id=cust-1')
  })
})
