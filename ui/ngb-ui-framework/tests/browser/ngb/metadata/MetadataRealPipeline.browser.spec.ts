import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h, ref, watch } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute } from 'vue-router'

import { configureNgbLookup } from '../../../../src/ngb/lookup/config'
import { configureNgbMetadata } from '../../../../src/ngb/metadata/config'
import { saveDocumentCopyDraft } from '../../../../src/ngb/editor/documentCopyDraft'
import NgbMetadataDocumentListPage from '../../../../src/ngb/metadata/NgbMetadataDocumentListPage.vue'
import type {
  DocumentTypeMetadata,
  LookupHint,
  LookupSource,
} from '../../../../src/ngb/metadata/types'
import type { MetadataDocumentListPageLoadArgs } from '../../../../src/ngb/metadata/routePages'
import type { MetadataRegisterPageResponse } from '../../../../src/ngb/metadata/useMetadataRegisterPageData'

const PROPERTY_RIVER = '11111111-1111-1111-1111-111111111111'
const PROPERTY_HARBOR = '22222222-2222-2222-2222-222222222222'
const COPY_DRAFT_STORAGE_KEY_PREFIX = 'ngb:document-copy-draft:'

type TestPageItem = NonNullable<MetadataRegisterPageResponse['items']>[number]
type GlobalWithCopyDraftStore = typeof globalThis & {
  __ngbDocumentCopyDraftMemoryStore?: Map<string, string>
}

function wait(ms: number) {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

function dispatchEscape() {
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
}

async function flushUi() {
  await Promise.resolve()
  await Promise.resolve()
}

async function waitForLookupDebounce() {
  await wait(240)
}

function visibleButtonByTitle(title: string): HTMLButtonElement {
  const button = Array.from(document.querySelectorAll(`button[title="${title}"]`))
    .find((entry) => window.getComputedStyle(entry).display !== 'none')

  if (!(button instanceof HTMLButtonElement)) throw new Error(`Button not found: ${title}`)
  return button
}

function lookupHintFromSource(entityTypeCode: string, fieldKey: string, lookup?: LookupSource | null): LookupHint | null {
  if (!lookup) return null
  if (lookup.kind === 'catalog') {
    return {
      kind: 'catalog',
      catalogType: lookup.catalogType,
      filters: entityTypeCode === 'pm.invoice' && fieldKey === 'property_id'
        ? { entity: 'invoice' }
        : undefined,
    }
  }
  if (lookup.kind === 'coa') return { kind: 'coa' }
  return { kind: 'document', documentTypes: lookup.documentTypes }
}

function makeDocumentMetadata(displayName: string): DocumentTypeMetadata {
  return {
    documentType: 'pm.invoice',
    displayName,
    kind: 2,
    list: {
      columns: [
        {
          key: 'number',
          label: 'Number',
          dataType: 'String',
          isSortable: true,
          align: 1,
        },
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
      ],
      filters: [
        {
          key: 'status',
          label: 'Status',
          dataType: 'String',
          options: [
            { value: 'open', label: 'Open' },
            { value: 'posted', label: 'Posted' },
          ],
        },
        {
          key: 'memo',
          label: 'Memo',
          dataType: 'String',
        },
        {
          key: 'property_id',
          label: 'Property',
          dataType: 'Guid',
          lookup: {
            kind: 'catalog',
            catalogType: 'pm.property',
          },
        },
      ],
    },
    form: null,
    parts: null,
  }
}

function makeItem(id: string, number: string, propertyId: string): TestPageItem {
  return {
    id,
    status: 1,
    payload: {
      fields: {
        number,
        property_id: propertyId,
      },
    },
  }
}

const MinimalDocumentEditor = defineComponent({
  props: {
    typeCode: {
      type: String,
      default: '',
    },
    id: {
      type: String,
      default: null,
    },
  },
  setup(props) {
    return () => h('div', { 'data-testid': 'metadata-minimal-editor' }, `editor:${props.typeCode}:${props.id ?? 'new'}`)
  },
})

const InteractiveDocumentEditor = defineComponent({
  props: {
    typeCode: {
      type: String,
      default: '',
    },
    id: {
      type: String,
      default: null,
    },
    initialFields: {
      type: Object,
      default: null,
    },
    expandTo: {
      type: String,
      default: null,
    },
  },
  emits: ['flags', 'created', 'saved', 'changed', 'deleted', 'close'],
  setup(props, { emit }) {
    const isDirty = ref(false)

    watch(
      () => [props.id, JSON.stringify(props.initialFields ?? null)],
      () => {
        isDirty.value = false
      },
      { immediate: true },
    )

    watch(
      () => [isDirty.value, props.id, props.expandTo],
      () => {
        emit('flags', {
          canSave: true,
          isDirty: isDirty.value,
          loading: false,
          saving: false,
          canExpand: !!props.expandTo,
          canDelete: false,
          canMarkForDeletion: true,
          canUnmarkForDeletion: false,
          canPost: false,
          canUnpost: false,
          canShowAudit: true,
          canShareLink: true,
        })
      },
      { immediate: true },
    )

    return () => h('div', { class: 'space-y-3 p-4' }, [
      h(
        'div',
        { 'data-testid': 'interactive-editor-state' },
        [
          `type:${props.typeCode}`,
          `id:${props.id ?? 'new'}`,
          `display:${String((props.initialFields as { display?: string } | null)?.display ?? '')}`,
          `expand:${String(props.expandTo ?? '')}`,
        ].join(';'),
      ),
      h('div', { 'data-testid': 'interactive-editor-dirty' }, String(isDirty.value)),
      h('button', {
        type: 'button',
        onClick: () => {
          isDirty.value = true
        },
      }, 'Mark editor dirty'),
      h('button', {
        type: 'button',
        onClick: () => {
          isDirty.value = false
          emit('saved')
        },
      }, 'Emit saved'),
      h('button', {
        type: 'button',
        onClick: () => {
          isDirty.value = false
          emit('created', 'invoice-created')
        },
      }, 'Emit created'),
      h('button', {
        type: 'button',
        onClick: () => {
          isDirty.value = false
          emit('changed', 'markForDeletion')
        },
      }, 'Emit mark-for-deletion change'),
      h('button', {
        type: 'button',
        onClick: () => emit('close'),
      }, 'Emit close'),
    ])
  },
})

const MetadataRouteHarness = defineComponent({
  props: {
    loadPageImpl: {
      type: Function,
      required: true,
    },
    editorComponent: {
      type: [Object, Function],
      default: MinimalDocumentEditor,
    },
  },
  setup(props) {
    return () => h(NgbMetadataDocumentListPage, {
      editorComponent: props.editorComponent,
      loadPage: props.loadPageImpl,
      resolveLookupHint: ({ entityTypeCode, fieldKey, lookup }: {
        entityTypeCode: string
        fieldKey: string
        lookup?: LookupSource | null
      }) => lookupHintFromSource(entityTypeCode, fieldKey, lookup),
      backTarget: '/home',
    })
  },
})

const AppRoot = defineComponent({
  setup() {
    const route = useRoute()

    return () => h('div', [
      h('div', { 'data-testid': 'metadata-current-route' }, route.fullPath),
      h(RouterView),
    ])
  },
})

async function renderMetadataPipelinePage(
  initialRoute: string,
  options: {
    loadPageImpl?: (args: MetadataDocumentListPageLoadArgs<DocumentTypeMetadata>) => Promise<MetadataRegisterPageResponse>
    editorComponent?: object
  } = {},
) {
  const metadataLoadCalls: string[] = []
  const catalogLabelCalls: Array<{ catalogType: string; ids: string[] }> = []
  const catalogSearchCalls: Array<{ catalogType: string; query: string; filters?: Record<string, string> }> = []
  const loadPageCalls: MetadataDocumentListPageLoadArgs<DocumentTypeMetadata>[] = []

  configureNgbMetadata({
    loadCatalogTypeMetadata: async (catalogType) => ({
      catalogType,
      displayName: 'Properties',
      kind: 1,
      list: {
        columns: [],
      },
      form: null,
      parts: null,
    }),
    loadDocumentTypeMetadata: async (documentType) => {
      metadataLoadCalls.push(documentType)
      return makeDocumentMetadata(documentType === 'pm.credit_note' ? 'Credit Notes' : 'Invoices')
    },
  })

  configureNgbLookup({
    loadCatalogItemsByIds: async (catalogType, ids) => {
      catalogLabelCalls.push({ catalogType, ids: [...ids] })
      return ids
        .map((id) => {
          if (id === PROPERTY_RIVER) return { id, label: 'Riverfront Tower' }
          if (id === PROPERTY_HARBOR) return { id, label: 'Harbor Point' }
          return null
        })
        .filter((item): item is { id: string; label: string } => item !== null)
    },
    searchCatalog: async (catalogType, query, searchOptions) => {
      catalogSearchCalls.push({
        catalogType,
        query,
        filters: searchOptions?.filters,
      })

      const normalized = query.trim().toLowerCase()
      if (normalized.includes('river')) return [{ id: PROPERTY_RIVER, label: 'Riverfront Tower' }]
      if (normalized.includes('harbor')) return [{ id: PROPERTY_HARBOR, label: 'Harbor Point' }]
      return []
    },
    loadCoaItemsByIds: async () => [],
    loadCoaItem: async () => null,
    searchCoa: async () => [],
    loadDocumentItemsByIds: async () => [],
    loadDocumentItem: async () => null,
    searchDocument: async () => [],
    searchDocumentsAcrossTypes: async () => [],
    buildCatalogUrl: (catalogType, id) => `/catalogs/${catalogType}/${id}`,
    buildCoaUrl: (id) => `/chart-of-accounts/${id}`,
    buildDocumentUrl: (documentType, id) => `/documents/${documentType}/${id}`,
  })

  const defaultLoadPage = async (args: MetadataDocumentListPageLoadArgs<DocumentTypeMetadata>) => {
    const search = String(args.search ?? '').trim().toLowerCase()
    const filteredPropertyId = String(args.listFilters.property_id ?? '').trim()
    const allItems = [
      makeItem('invoice-river', 'INV-001', PROPERTY_RIVER),
      makeItem('invoice-harbor', 'INV-002', PROPERTY_HARBOR),
    ]

    let items = allItems
    if (filteredPropertyId) {
      items = items.filter((item) => String(item.payload?.fields?.property_id) === filteredPropertyId)
    }

    if (search === 'alpha') {
      items = [makeItem('invoice-alpha', 'INV-ALPHA', PROPERTY_RIVER)]
    } else if (search === 'beta') {
      items = [makeItem('invoice-beta', 'INV-BETA', PROPERTY_HARBOR)]
    }

    return {
      items,
      total: items.length,
    }
  }

  const loadPageImpl = options.loadPageImpl ?? defaultLoadPage

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/documents/:documentType',
        component: MetadataRouteHarness,
        props: {
          editorComponent: options.editorComponent ?? MinimalDocumentEditor,
          loadPageImpl: async (args: MetadataDocumentListPageLoadArgs<DocumentTypeMetadata>) => {
            loadPageCalls.push({
              ...args,
              listFilters: { ...args.listFilters },
            })
            return await loadPageImpl(args)
          },
        },
      },
    ],
  })

  await router.push(initialRoute)
  await router.isReady()

  const view = await render(AppRoot, {
    global: {
      plugins: [createPinia(), router],
    },
  })

  return {
    view,
    router,
    metadataLoadCalls,
    catalogLabelCalls,
    catalogSearchCalls,
    loadPageCalls,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  window.localStorage.removeItem('ngb:document:pm.invoice')
  for (const store of [window.localStorage, window.sessionStorage]) {
    for (const key of Object.keys(store)) {
      if (key.startsWith(COPY_DRAFT_STORAGE_KEY_PREFIX)) store.removeItem(key)
    }
  }
  ;(globalThis as GlobalWithCopyDraftStore).__ngbDocumentCopyDraftMemoryStore?.clear()
})

test('loads the real metadata pipeline, reuses lookup cache across rows and badges, and updates filters through the drawer', async () => {
  await page.viewport(1440, 900)

  const { view, catalogLabelCalls, catalogSearchCalls, loadPageCalls, metadataLoadCalls } = await renderMetadataPipelinePage(
    `/documents/pm.invoice?status=open&property_id=${PROPERTY_RIVER}&periodFrom=2026-03&periodTo=2026-04`,
  )

  await expect.element(view.getByText('Invoices', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Riverfront Tower', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Status: Open', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Property: Riverfront Tower', { exact: true })).toBeVisible()

  expect(metadataLoadCalls).toEqual(['pm.invoice'])
  expect(catalogLabelCalls).toEqual([
    {
      catalogType: 'pm.property',
      ids: [PROPERTY_RIVER],
    },
  ])
  expect(loadPageCalls[0]).toMatchObject({
    documentType: 'pm.invoice',
    periodFrom: '2026-03-01',
    periodTo: '2026-04-30',
    listFilters: {
      property_id: PROPERTY_RIVER,
      status: 'open',
    },
  })

  visibleButtonByTitle('Filter').click()
  await expect.element(view.getByText('Filter', { exact: true })).toBeVisible()

  const propertyLookup = view.getByRole('combobox')
  await propertyLookup.click()
  await propertyLookup.fill('harbor')
  await waitForLookupDebounce()

  await expect.element(view.getByRole('option', { name: /Harbor Point/i })).toBeVisible()
  await view.getByRole('option', { name: /Harbor Point/i }).click()
  await flushUi()

  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toContain(PROPERTY_HARBOR)
  await expect.element(view.getByText('Property: Harbor Point', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Harbor Point', { exact: true })).toBeVisible()

  expect(catalogSearchCalls).toEqual([
    {
      catalogType: 'pm.property',
      query: 'harbor',
      filters: { entity: 'invoice' },
    },
  ])
  expect(catalogLabelCalls).toEqual([
    {
      catalogType: 'pm.property',
      ids: [PROPERTY_RIVER],
    },
  ])
  expect(metadataLoadCalls).toEqual(['pm.invoice'])
})

test('drops stale real page results when overlapping route-query reloads finish out of order', async () => {
  await page.viewport(1440, 900)

  const { router, view, metadataLoadCalls } = await renderMetadataPipelinePage('/documents/pm.invoice', {
    loadPageImpl: async (args) => {
      const search = String(args.search ?? '').trim().toLowerCase()
      if (search === 'alpha') await wait(80)
      if (search === 'beta') await wait(10)

      if (search === 'alpha') {
        return {
          items: [makeItem('invoice-alpha', 'INV-ALPHA', PROPERTY_RIVER)],
          total: 1,
        }
      }

      if (search === 'beta') {
        return {
          items: [makeItem('invoice-beta', 'INV-BETA', PROPERTY_HARBOR)],
          total: 1,
        }
      }

      return {
        items: [],
        total: 0,
      }
    },
  })

  await expect.element(view.getByText('0 / 0', { exact: true })).toBeVisible()

  const firstNavigation = router.push('/documents/pm.invoice?search=alpha')
  const secondNavigation = router.push('/documents/pm.invoice?search=beta')
  await Promise.all([firstNavigation, secondNavigation])

  await expect.element(view.getByText('INV-BETA', { exact: true })).toBeVisible()
  await expect.element(view.getByText('Harbor Point', { exact: true })).toBeVisible()
  expect(document.body.textContent).not.toContain('INV-ALPHA')
  expect(document.body.textContent).not.toContain('Riverfront Tower')
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toContain('search=beta')

  expect(metadataLoadCalls).toEqual(['pm.invoice'])
})

test('opens route-driven drawers from copy drafts, reopens created documents, keeps save commits open, and closes after deletion-state commits', async () => {
  await page.viewport(1440, 900)

  const copyDraftToken = saveDocumentCopyDraft({
    documentType: 'pm.invoice',
    fields: {
      display: 'Copied invoice draft',
    },
    parts: null,
  })

  expect(copyDraftToken).toEqual(expect.any(String))

  const items = [
    makeItem('invoice-river', 'INV-001', PROPERTY_RIVER),
    makeItem('invoice-harbor', 'INV-002', PROPERTY_HARBOR),
    makeItem('invoice-created', 'INV-003', PROPERTY_RIVER),
  ]

  const { view } = await renderMetadataPipelinePage(
    `/documents/pm.invoice?panel=new&copyDraft=${copyDraftToken}`,
    {
      editorComponent: InteractiveDocumentEditor,
      loadPageImpl: async () => ({
        items,
        total: items.length,
      }),
    },
  )

  await expect.element(view.getByTestId('interactive-editor-state')).toHaveTextContent('id:new')
  await expect.element(view.getByTestId('interactive-editor-state')).toHaveTextContent('display:Copied invoice draft')
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/documents/pm.invoice')

  await view.getByRole('button', { name: 'Emit created' }).click()
  await expect.element(view.getByTestId('interactive-editor-state')).toHaveTextContent('id:invoice-created')
  await expect.element(view.getByTestId('interactive-editor-state')).toHaveTextContent('expand:/documents/pm.invoice/invoice-created')

  await view.getByRole('button', { name: 'Emit saved' }).click()
  await expect.element(view.getByTestId('interactive-editor-state')).toHaveTextContent('id:invoice-created')

  await view.getByRole('button', { name: 'Emit mark-for-deletion change' }).click()
  await expect.poll(() => document.querySelector('[data-testid="interactive-editor-state"]')).toBeNull()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/documents/pm.invoice')

  await view.getByText('INV-002', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-editor-state')).toHaveTextContent('id:invoice-harbor')
})

test('confirms discard before closing dirty real document drawers', async () => {
  await page.viewport(1440, 900)

  const items = [
    makeItem('invoice-river', 'INV-001', PROPERTY_RIVER),
    makeItem('invoice-harbor', 'INV-002', PROPERTY_HARBOR),
  ]

  const { view } = await renderMetadataPipelinePage('/documents/pm.invoice', {
    editorComponent: InteractiveDocumentEditor,
    loadPageImpl: async () => ({
      items,
      total: items.length,
    }),
  })

  await view.getByText('INV-001', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-editor-state')).toHaveTextContent('id:invoice-river')

  await view.getByRole('button', { name: 'Mark editor dirty' }).click()
  dispatchEscape()
  await expect.element(view.getByText('Discard changes?', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Keep editing' }).click()
  await expect.element(view.getByTestId('interactive-editor-state')).toHaveTextContent('id:invoice-river')
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/documents/pm.invoice')

  dispatchEscape()
  await view.getByRole('button', { name: 'Discard' }).click()
  await expect.poll(() => document.querySelector('[data-testid="interactive-editor-state"]')).toBeNull()
})

test('debounces free-text filter commits, reloads with applied values, and clears them through undo', async () => {
  await page.viewport(1440, 900)

  const { view, loadPageCalls } = await renderMetadataPipelinePage('/documents/pm.invoice')

  await expect.element(view.getByText('INV-001', { exact: true })).toBeVisible()

  visibleButtonByTitle('Filter').click()
  await expect.element(view.getByText('Filter', { exact: true })).toBeVisible()

  const memoInput = view.getByPlaceholder('Memo')
  await memoInput.fill('alpha')

  await wait(120)
  expect(view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/documents/pm.invoice')

  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toContain('memo=alpha')
  await expect.element(view.getByText('Memo: alpha', { exact: true })).toBeVisible()
  expect(loadPageCalls.at(-1)).toMatchObject({
    documentType: 'pm.invoice',
    listFilters: {
      memo: 'alpha',
    },
  })

  visibleButtonByTitle('Undo').click()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').not.toContain('memo=alpha')
  await expect.poll(() => document.body.textContent?.includes('Memo: alpha') ?? false).toBe(false)
  expect(loadPageCalls.at(-1)).toMatchObject({
    documentType: 'pm.invoice',
    listFilters: {},
  })
})

test('clears pending debounced filter commits when the document type changes', async () => {
  await page.viewport(1440, 900)

  const { router, view, loadPageCalls, metadataLoadCalls } = await renderMetadataPipelinePage('/documents/pm.invoice')

  await expect.element(view.getByText('Invoices', { exact: true })).toBeVisible()

  visibleButtonByTitle('Filter').click()
  await expect.element(view.getByText('Filter', { exact: true })).toBeVisible()

  const memoInput = view.getByPlaceholder('Memo')
  await memoInput.fill('stale')
  await wait(120)

  await router.push('/documents/pm.credit_note')

  await expect.element(view.getByText('Credit Notes', { exact: true })).toBeVisible()
  await wait(320)
  await flushUi()

  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/documents/pm.credit_note')
  expect(metadataLoadCalls).toEqual(['pm.invoice', 'pm.credit_note'])
  expect(
    loadPageCalls.some((call) =>
      call.documentType === 'pm.credit_note'
      && call.listFilters.memo === 'stale',
    ),
  ).toBe(false)
  expect(loadPageCalls.at(-1)).toMatchObject({
    documentType: 'pm.credit_note',
    listFilters: {},
  })
})
