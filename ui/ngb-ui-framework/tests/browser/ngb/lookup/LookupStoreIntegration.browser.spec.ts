import { page } from 'vitest/browser'
import { beforeEach, expect, test } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { computed, defineComponent, h, ref } from 'vue'

import NgbRegisterGrid from '../../../../src/ngb/components/register/NgbRegisterGrid.vue'
import { configureNgbLookup } from '../../../../src/ngb/lookup/config'
import { useLookupStore } from '../../../../src/ngb/lookup/store'
import type { DocumentTypeMetadata, LookupHint, LookupSource } from '../../../../src/ngb/metadata/types'
import {
  type MetadataRegisterPageResponse,
  useMetadataRegisterPageData,
} from '../../../../src/ngb/metadata/useMetadataRegisterPageData'
import { shortGuid } from '../../../../src/ngb/utils/guid'

const PROPERTY_RIVER = '11111111-1111-1111-1111-111111111111'
const SHARED_DOCUMENT_ID = '22222222-2222-2222-2222-222222222222'
const UNIQUE_DOCUMENT_ID = '33333333-3333-3333-3333-333333333333'
const MISSING_COA_ID = '44444444-4444-4444-4444-444444444444'

const lookupIntegrationState = {
  delayedDocumentLoadMs: 0,
  sharedSearchFailuresRemaining: 0,
}

async function wait(ms: number) {
  await new Promise((resolve) => window.setTimeout(resolve, ms))
}

function makeLookupMetadata(): DocumentTypeMetadata {
  return {
    documentType: 'pm.invoice',
    displayName: 'Invoices',
    kind: 2,
    list: {
      columns: [
        {
          key: 'property_id',
          label: 'Property',
          dataType: 'Guid',
          isSortable: true,
          align: 1,
          lookup: {
            kind: 'catalog',
            catalogType: 'pm.property',
          },
        },
        {
          key: 'source_document_id',
          label: 'Source Document',
          dataType: 'Guid',
          isSortable: true,
          align: 1,
          lookup: {
            kind: 'document',
            documentTypes: ['pm.invoice', 'pm.credit_note'],
          },
        },
      ],
    },
    form: null,
    parts: null,
  }
}

function resolveLookupHintFromSource(lookup?: LookupSource | null): LookupHint | null {
  if (!lookup) return null
  if (lookup.kind === 'catalog') return { kind: 'catalog', catalogType: lookup.catalogType }
  if (lookup.kind === 'coa') return { kind: 'coa' }
  return { kind: 'document', documentTypes: lookup.documentTypes }
}

const LookupStoreHarness = defineComponent({
  setup() {
    const lookupStore = useLookupStore()
    const sharedResults = ref('none')
    const coaLabel = ref('none')

    const metadata = computed(() => makeLookupMetadata())
    const { columns, rows } = useMetadataRegisterPageData({
      route: {
        path: '/documents/pm.invoice',
        query: {},
      } as never,
      entityTypeCode: computed(() => 'pm.invoice'),
      reloadKey: computed(() => 'lookup-store-browser'),
      loadMetadata: async () => metadata.value,
      loadPage: async () => ({
        items: [
          {
            id: 'row-1',
            payload: {
              fields: {
                property_id: PROPERTY_RIVER,
                source_document_id: SHARED_DOCUMENT_ID,
              },
            },
          },
        ],
        total: 1,
      } satisfies MetadataRegisterPageResponse),
      lookupStore,
      resolveLookupHint: ({ lookup }) => resolveLookupHintFromSource(lookup),
    })

    async function searchSharedDocuments() {
      const results = await lookupStore.searchDocuments(['pm.invoice', 'pm.credit_note'], 'shared')
      sharedResults.value = results.map((item) => item.label).join('|') || 'none'
    }

    async function ensureMissingCoaLabel() {
      await lookupStore.ensureCoaLabels([MISSING_COA_ID])
      coaLabel.value = lookupStore.labelForCoa(MISSING_COA_ID)
    }

    return () => h('div', { style: 'padding: 24px;' }, [
      h('div', { 'data-testid': 'lookup-grid' }, [
        h(NgbRegisterGrid, {
          columns: columns.value,
          rows: rows.value,
          showPanel: false,
          showTotals: false,
          heightPx: 220,
          storageKey: 'ngb:test:lookup-store-browser',
        }),
      ]),
      h('div', { 'data-testid': 'lookup-direct-property' }, lookupStore.labelForCatalog('pm.property', PROPERTY_RIVER)),
      h('div', { 'data-testid': 'lookup-direct-document' }, lookupStore.labelForAnyDocument(['pm.invoice', 'pm.credit_note'], SHARED_DOCUMENT_ID)),
      h('button', {
        type: 'button',
        onClick: () => {
          void searchSharedDocuments()
        },
      }, 'Search shared documents'),
      h('div', { 'data-testid': 'lookup-search-results' }, sharedResults.value),
      h('button', {
        type: 'button',
        onClick: () => {
          void ensureMissingCoaLabel()
        },
      }, 'Ensure COA fallback'),
      h('div', { 'data-testid': 'lookup-coa-label' }, coaLabel.value),
    ])
  },
})

const LookupRecoveryHarness = defineComponent({
  setup() {
    const lookupStore = useLookupStore()
    const sharedResults = ref('none')
    const sharedError = ref('none')
    const hydrating = ref(false)

    async function hydrateSharedLabel() {
      hydrating.value = true

      try {
        await lookupStore.ensureAnyDocumentLabels(['pm.invoice', 'pm.credit_note'], [SHARED_DOCUMENT_ID])
      } finally {
        hydrating.value = false
      }
    }

    async function searchSharedDocuments() {
      try {
        const results = await lookupStore.searchDocuments(['pm.invoice', 'pm.credit_note'], 'shared')
        sharedResults.value = results.map((item) => item.label).join('|') || 'none'
        sharedError.value = 'none'
      } catch (cause) {
        sharedResults.value = 'none'
        sharedError.value = cause instanceof Error ? cause.message : String(cause)
      }
    }

    return () => h('div', { style: 'padding: 24px;' }, [
      h('div', { 'data-testid': 'lookup-shared-label' }, lookupStore.labelForAnyDocument(['pm.invoice', 'pm.credit_note'], SHARED_DOCUMENT_ID)),
      h('div', { 'data-testid': 'lookup-hydrating' }, String(hydrating.value)),
      h('button', {
        type: 'button',
        onClick: () => {
          void hydrateSharedLabel()
        },
      }, 'Hydrate shared label'),
      h('button', {
        type: 'button',
        onClick: () => {
          void searchSharedDocuments()
        },
      }, 'Search shared with retry'),
      h('div', { 'data-testid': 'lookup-search-results' }, sharedResults.value),
      h('div', { 'data-testid': 'lookup-search-error' }, sharedError.value),
    ])
  },
})

beforeEach(() => {
  window.localStorage.removeItem('ngb:test:lookup-store-browser')
  lookupIntegrationState.delayedDocumentLoadMs = 0
  lookupIntegrationState.sharedSearchFailuresRemaining = 0

  configureNgbLookup({
    loadCatalogItemsByIds: async (_catalogType, ids) =>
      ids.map((id) => ({
        id,
        label: id === PROPERTY_RIVER ? 'Riverfront Tower' : id,
      })),
    searchCatalog: async () => [],
    loadCoaItemsByIds: async () => [],
    loadCoaItem: async () => {
      throw new Error('Missing COA')
    },
    searchCoa: async () => [],
    loadDocumentItemsByIds: async (_documentTypes, ids) => {
      const results = []
      for (const id of ids) {
        if (id !== SHARED_DOCUMENT_ID) continue
        if (lookupIntegrationState.delayedDocumentLoadMs > 0) {
          await wait(lookupIntegrationState.delayedDocumentLoadMs)
        }
        results.push({
          id,
          label: 'Credit Memo CM-001',
          documentType: 'pm.credit_note',
        })
      }
      return results
    },
    searchDocumentsAcrossTypes: async (_documentTypes, query) => {
      if (query === 'shared' && lookupIntegrationState.sharedSearchFailuresRemaining > 0) {
        lookupIntegrationState.sharedSearchFailuresRemaining -= 1
        throw new Error('Document search offline')
      }

      return [
        { id: SHARED_DOCUMENT_ID, label: 'Invoice INV-001', documentType: 'pm.invoice' },
        { id: SHARED_DOCUMENT_ID, label: 'Credit Memo CM-001', documentType: 'pm.credit_note' },
        { id: UNIQUE_DOCUMENT_ID, label: 'Credit Memo CM-002', documentType: 'pm.credit_note' },
      ]
    },
    loadDocumentItem: async (documentType, id) => {
      if (documentType === 'pm.credit_note' && id === SHARED_DOCUMENT_ID) {
        return {
          id,
          label: 'Credit Memo CM-001',
        }
      }

      throw new Error('Not found')
    },
    searchDocument: async () => [],
    buildCatalogUrl: (catalogType, id) => `/catalogs/${catalogType}/${id}`,
    buildCoaUrl: (id) => `/chart-of-accounts/${id}`,
    buildDocumentUrl: (documentType, id) => `/documents/${documentType}/${id}`,
  })
})

test('shares the lookup cache across register prefetch, direct label consumers, deduped document search, and coa fallbacks', async () => {
  await page.viewport(1280, 900)

  const view = await render(LookupStoreHarness, {
    global: {
      plugins: [createPinia()],
    },
  })

  await expect.poll(() => view.getByTestId('lookup-grid').element().textContent ?? '').toContain('Riverfront Tower')
  await expect.poll(() => view.getByTestId('lookup-grid').element().textContent ?? '').toContain('Credit Memo CM-001')
  await expect.element(view.getByTestId('lookup-direct-property')).toHaveTextContent('Riverfront Tower')
  await expect.element(view.getByTestId('lookup-direct-document')).toHaveTextContent('Credit Memo CM-001')

  await view.getByRole('button', { name: 'Search shared documents' }).click()
  await expect.element(view.getByTestId('lookup-search-results')).toHaveTextContent('Invoice INV-001|Credit Memo CM-002')

  await view.getByRole('button', { name: 'Ensure COA fallback' }).click()
  await expect.element(view.getByTestId('lookup-coa-label')).toHaveTextContent(shortGuid(MISSING_COA_ID))
})

test('hydrates mixed document labels in-place and recovers shared searches on retry after a browser-visible failure', async () => {
  await page.viewport(1280, 900)

  lookupIntegrationState.delayedDocumentLoadMs = 60
  lookupIntegrationState.sharedSearchFailuresRemaining = 1

  const view = await render(LookupRecoveryHarness, {
    global: {
      plugins: [createPinia()],
    },
  })

  await expect.element(view.getByTestId('lookup-shared-label')).toHaveTextContent(shortGuid(SHARED_DOCUMENT_ID))

  await view.getByRole('button', { name: 'Hydrate shared label' }).click()
  await expect.element(view.getByTestId('lookup-shared-label')).toHaveTextContent('Credit Memo CM-001')
  await expect.poll(() => view.getByTestId('lookup-hydrating').element().textContent ?? '').toBe('false')

  await view.getByRole('button', { name: 'Search shared with retry' }).click()
  await expect.element(view.getByTestId('lookup-search-error')).toHaveTextContent('Document search offline')
  await expect.element(view.getByTestId('lookup-search-results')).toHaveTextContent('none')

  await view.getByRole('button', { name: 'Search shared with retry' }).click()
  await expect.element(view.getByTestId('lookup-search-results')).toHaveTextContent('Invoice INV-001|Credit Memo CM-002')
  await expect.element(view.getByTestId('lookup-search-error')).toHaveTextContent('none')
})
