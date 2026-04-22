import { page } from 'vitest/browser'
import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { computed, defineComponent, h } from 'vue'
import { createMemoryHistory, createRouter, useRoute } from 'vue-router'

import {
  useMetadataPageReloadKey,
  useMetadataRegisterPageData,
} from '../../../../src/ngb/metadata/useMetadataRegisterPageData'
import { shortGuid } from '../../../../src/ngb/utils/guid'

function createMetadata() {
  return {
    displayName: 'Invoices',
    list: {
      columns: [
        {
          key: 'number',
          label: 'Number',
          dataType: 'String',
          align: 1,
          isSortable: true,
        },
      ],
    },
  }
}

function createPageResponse() {
  return {
    total: 1,
    items: [
      {
        id: 'doc-1',
        payload: {
          fields: {
            number: 'INV-001',
          },
        },
      },
    ],
  }
}

async function renderRecoveryHarness() {
  let attempt = 0
  let resolveSecondLoad: ((value: ReturnType<typeof createPageResponse>) => void) | null = null

  const loadMetadata = vi.fn().mockResolvedValue(createMetadata())
  const loadPage = vi.fn(async () => {
    attempt += 1

    if (attempt === 1) {
      throw new Error('Service unavailable')
    }

    return await new Promise<ReturnType<typeof createPageResponse>>((resolve) => {
      resolveSecondLoad = resolve
    })
  })

  const RecoveryHarness = defineComponent({
    setup() {
      const route = useRoute()
      const entityTypeCode = computed(() => String(route.params.documentType ?? ''))
      const reloadKey = useMetadataPageReloadKey({
        route,
        entityTypeCode,
      })
      const register = useMetadataRegisterPageData({
        route,
        entityTypeCode,
        reloadKey,
        loadMetadata,
        loadPage,
      })

      return () => h('div', { class: 'space-y-2' }, [
        h('div', { 'data-testid': 'loading-state' }, String(register.loading.value)),
        h('div', { 'data-testid': 'error-state' }, register.error.value ?? 'none'),
        h('div', { 'data-testid': 'row-count' }, String(register.rows.value.length)),
        h('div', { 'data-testid': 'row-number' }, String(register.rows.value[0]?.number ?? 'none')),
        h('button', {
          type: 'button',
          onClick: () => {
            void register.load()
          },
        }, 'Refresh register'),
      ])
    },
  })

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/documents/:documentType',
        component: {
          template: '<div />',
        },
      },
    ],
  })

  await router.push('/documents/pm.invoice')
  await router.isReady()

  const view = await render(RecoveryHarness, {
    global: {
      plugins: [router],
    },
  })

  return {
    view,
    loadMetadata,
    loadPage,
    resolveSecondLoad: () => {
      resolveSecondLoad?.(createPageResponse())
    },
  }
}

test('recovers from an initial register load failure after refresh without leaving stale error state behind', async () => {
  await page.viewport(1280, 900)

  const { view, loadMetadata, loadPage, resolveSecondLoad } = await renderRecoveryHarness()

  await expect.element(view.getByTestId('error-state')).toHaveTextContent('Service unavailable')
  await expect.element(view.getByTestId('loading-state')).toHaveTextContent('false')
  await expect.element(view.getByTestId('row-count')).toHaveTextContent('0')
  expect(loadMetadata).toHaveBeenCalledTimes(1)
  expect(loadPage).toHaveBeenCalledTimes(1)

  await view.getByRole('button', { name: 'Refresh register' }).click()

  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledTimes(2)
  })
  await expect.element(view.getByTestId('loading-state')).toHaveTextContent('true')
  await expect.element(view.getByTestId('error-state')).toHaveTextContent('none')

  resolveSecondLoad()

  await expect.element(view.getByTestId('loading-state')).toHaveTextContent('false')
  await expect.element(view.getByTestId('error-state')).toHaveTextContent('none')
  await expect.element(view.getByTestId('row-count')).toHaveTextContent('1')
  await expect.element(view.getByTestId('row-number')).toHaveTextContent('INV-001')
})

test('keeps register rows visible when lookup label prefetch fails and falls back to unresolved lookup labels', async () => {
  await page.viewport(1280, 900)

  const customerId = '11111111-1111-1111-1111-111111111111'
  const lookupStore = {
    searchCatalog: vi.fn().mockResolvedValue([]),
    searchCoa: vi.fn().mockResolvedValue([]),
    searchDocuments: vi.fn().mockResolvedValue([]),
    ensureCatalogLabels: vi.fn().mockRejectedValue(new Error('Catalog labels offline')),
    ensureCoaLabels: vi.fn().mockResolvedValue(undefined),
    ensureAnyDocumentLabels: vi.fn().mockResolvedValue(undefined),
    labelForCatalog: vi.fn((_: unknown, id: unknown) => shortGuid(String(id ?? ''))),
    labelForCoa: vi.fn((id: unknown) => String(id ?? '')),
    labelForAnyDocument: vi.fn((_: unknown, id: unknown) => String(id ?? '')),
  }

  const PrefetchFailureHarness = defineComponent({
    setup() {
      const register = useMetadataRegisterPageData({
        route: {
          path: '/documents/pm.invoice',
          query: {},
        } as never,
        entityTypeCode: computed(() => 'pm.invoice'),
        reloadKey: computed(() => 'lookup-prefetch-failure'),
        loadMetadata: async () => ({
          displayName: 'Invoices',
          list: {
            columns: [
              {
                key: 'customer_id',
                label: 'Customer',
                dataType: 'Guid',
                align: 1,
                isSortable: true,
                lookup: {
                  kind: 'catalog',
                  catalogType: 'crm.counterparty',
                },
              },
            ],
          },
        }),
        loadPage: async () => ({
          total: 1,
          items: [
            {
              id: 'doc-1',
              payload: {
                fields: {
                  customer_id: customerId,
                },
              },
            },
          ],
        }),
        lookupStore,
        resolveLookupHint: ({ lookup }) => lookup && lookup.kind === 'catalog'
          ? { kind: 'catalog', catalogType: lookup.catalogType }
          : null,
      })

      return () => {
        const customer = register.rows.value[0]?.customer_id
        const customerLabel = customer && typeof customer === 'object' && 'display' in customer
          ? String((customer as { display?: unknown }).display ?? 'none')
          : String(customer ?? 'none')

        return h('div', { class: 'space-y-2' }, [
          h('div', { 'data-testid': 'error-state' }, register.error.value ?? 'none'),
          h('div', { 'data-testid': 'row-count' }, String(register.rows.value.length)),
          h('div', { 'data-testid': 'row-customer' }, customerLabel),
        ])
      }
    },
  })

  const view = await render(PrefetchFailureHarness)

  await vi.waitFor(() => {
    expect(lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('crm.counterparty', [customerId])
  })
  await expect.element(view.getByTestId('error-state')).toHaveTextContent('none')
  await expect.element(view.getByTestId('row-count')).toHaveTextContent('1')
  await expect.element(view.getByTestId('row-customer')).toHaveTextContent(shortGuid(customerId))
})
