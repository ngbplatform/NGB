import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, markRaw, ref, watch } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute, useRouter } from 'vue-router'

import {
  StubBadge,
  StubDocumentListFiltersDrawer,
  StubDocumentPeriodFilter,
  StubEditorDiscardDialog,
  StubRecycleBinFilter,
  StubRegisterPageLayout,
} from './stubs'

const documentMocks = vi.hoisted(() => ({
  registerData: {
    loading: false,
    error: null as string | null,
    metadata: {
      displayName: 'Invoices',
      parts: [{ key: 'lines' }],
    },
    page: {
      items: [{ id: 'doc-1' }],
      total: 1,
    },
    listFilters: [
      {
        key: 'status',
        label: 'Status',
        dataType: 'String',
      },
    ],
    hasListFilters: true,
    columns: [{ key: 'number', title: 'Number' }],
    rows: [{ key: 'doc-1', number: 'INV-001' }],
    load: vi.fn().mockResolvedValue(true),
  },
  filterState: {
    filterDraft: {
      status: {
        raw: 'open',
        items: [],
      },
    },
    lookupItemsByFilterKey: {
      property_id: [{ id: 'property-1', label: 'Riverfront Tower' }],
    },
    activeFilterBadges: [{ key: 'status', text: 'Status: Open' }],
    hasActiveFilters: true,
    canUndoFilters: true,
    handleLookupQuery: vi.fn().mockResolvedValue(undefined),
    handleItemsUpdate: vi.fn(),
    handleValueUpdate: vi.fn(),
    undo: vi.fn().mockResolvedValue(undefined),
  },
  metadataStore: {
    ensureDocumentType: vi.fn(),
  },
  lookupStore: {},
  navigateBack: vi.fn(),
  preferFullPage: true,
}))

vi.mock('../../../../src/ngb/metadata/store', () => ({
  useMetadataStore: () => documentMocks.metadataStore,
}))

vi.mock('../../../../src/ngb/lookup/store', () => ({
  useLookupStore: () => documentMocks.lookupStore,
}))

vi.mock('../../../../src/ngb/metadata/useMetadataRegisterPageData', async () => {
  const { computed, watch } = await vi.importActual<typeof import('vue')>('vue')

  function normalizeSingleQueryValue(value: unknown): string {
    if (Array.isArray(value)) return String(value[0] ?? '').trim()
    return String(value ?? '').trim()
  }

  return {
    useMetadataPageReloadKey: (args: {
      route: { path: string; query: Record<string, unknown> }
      entityTypeCode: { value: string }
      ignoreQueryKey?: (key: string) => boolean
    }) => computed(() => JSON.stringify({
      path: args.route.path,
      entityTypeCode: args.entityTypeCode.value,
      query: Object.entries(args.route.query)
        .filter(([key]) => !args.ignoreQueryKey?.(key))
        .map(([key, value]) => [key, normalizeSingleQueryValue(value)] as const)
        .sort(([left], [right]) => left.localeCompare(right)),
    })),
    useMetadataRegisterPageData: (args: {
      entityTypeCode: { value: string }
      reloadKey: { value: string }
      loadPage: (args: { entityTypeCode: string; metadata: unknown }) => Promise<unknown>
    }) => {
      async function runAutoLoad() {
        await args.loadPage({
          entityTypeCode: args.entityTypeCode.value,
          metadata: documentMocks.registerData.metadata,
        })
      }

      const manualLoad = vi.fn(async () => {
        await runAutoLoad()
        return true
      })
      documentMocks.registerData.load = manualLoad

      watch(
        () => args.reloadKey.value,
        () => {
          void runAutoLoad()
        },
        { immediate: true },
      )

      return {
        loading: computed(() => documentMocks.registerData.loading),
        error: computed(() => documentMocks.registerData.error),
        metadata: computed(() => documentMocks.registerData.metadata),
        page: computed(() => documentMocks.registerData.page),
        listFilters: computed(() => documentMocks.registerData.listFilters),
        hasListFilters: computed(() => documentMocks.registerData.hasListFilters),
        columns: computed(() => documentMocks.registerData.columns),
        rows: computed(() => documentMocks.registerData.rows),
        load: manualLoad,
      }
    },
  }
})

vi.mock('../../../../src/ngb/metadata/useMetadataListFilters', async () => {
  const { computed } = await vi.importActual<typeof import('vue')>('vue')

  return {
    useMetadataListFilters: () => ({
      filterDraft: computed(() => documentMocks.filterState.filterDraft),
      lookupItemsByFilterKey: computed(() => documentMocks.filterState.lookupItemsByFilterKey),
      activeFilterBadges: computed(() => documentMocks.filterState.activeFilterBadges),
      hasActiveFilters: computed(() => documentMocks.filterState.hasActiveFilters),
      canUndoFilters: computed(() => documentMocks.filterState.canUndoFilters),
      handleLookupQuery: documentMocks.filterState.handleLookupQuery,
      handleItemsUpdate: documentMocks.filterState.handleItemsUpdate,
      handleValueUpdate: documentMocks.filterState.handleValueUpdate,
      undo: documentMocks.filterState.undo,
    }),
  }
})

vi.mock('../../../../src/ngb/metadata/NgbRegisterPageLayout.vue', () => ({
  default: StubRegisterPageLayout,
}))

vi.mock('../../../../src/ngb/metadata/NgbDocumentListFiltersDrawer.vue', () => ({
  default: StubDocumentListFiltersDrawer,
}))

vi.mock('../../../../src/ngb/metadata/NgbDocumentPeriodFilter.vue', () => ({
  default: StubDocumentPeriodFilter,
}))

vi.mock('../../../../src/ngb/metadata/NgbRecycleBinFilter.vue', () => ({
  default: StubRecycleBinFilter,
}))

vi.mock('../../../../src/ngb/primitives/NgbBadge.vue', () => ({
  default: StubBadge,
}))

vi.mock('../../../../src/ngb/editor/NgbEditorDiscardDialog.vue', () => ({
  default: StubEditorDiscardDialog,
}))

vi.mock('../../../../src/ngb/router/backNavigation', async () => {
  const actual = await vi.importActual<typeof import('../../../../src/ngb/router/backNavigation')>(
    '../../../../src/ngb/router/backNavigation',
  )

  return {
    ...actual,
    navigateBack: documentMocks.navigateBack,
  }
})

vi.mock('../../../../src/ngb/editor/documentNavigation', async () => {
  const actual = await vi.importActual<typeof import('../../../../src/ngb/editor/documentNavigation')>(
    '../../../../src/ngb/editor/documentNavigation'
  )

  return {
    ...actual,
    shouldOpenDocumentInFullPageByDefault: () => documentMocks.preferFullPage,
    buildDocumentFullPageUrl: (documentType: string, id?: string | null) =>
      id ? `/full-doc/${documentType}/${id}` : `/full-doc/${documentType}/new`,
  }
})

import NgbMetadataDocumentListPage from '../../../../src/ngb/metadata/NgbMetadataDocumentListPage.vue'
import { decodeBackTarget, withBackTarget } from '../../../../src/ngb/router/backNavigation'

function createEditorFlags(isDirty = false) {
  return {
    canSave: true,
    isDirty,
    loading: false,
    saving: false,
    canExpand: true,
    canDelete: false,
    canMarkForDeletion: true,
    canUnmarkForDeletion: false,
    canPost: false,
    canUnpost: false,
    canShowAudit: true,
    canShareLink: true,
  }
}

const DocumentEditorStub = defineComponent({
  props: {
    typeCode: {
      type: String,
      default: '',
    },
    id: {
      type: String,
      default: null,
    },
    expandTo: {
      type: String,
      default: null,
    },
  },
  emits: ['changed', 'created', 'saved', 'deleted', 'flags'],
  setup(props, { emit }) {
    const dirty = ref(false)

    watch(
      () => props.id,
      () => {
        dirty.value = false
      },
      { immediate: true },
    )

    watch(
      () => dirty.value,
      (value) => {
        emit('flags', createEditorFlags(value))
      },
      { immediate: true },
    )

    return () => h('div', { 'data-testid': 'document-editor' }, [
      h('div', `editor-type:${props.typeCode}`),
      h('div', `editor-id:${props.id ?? 'new'}`),
      h('div', `editor-expand:${props.expandTo ?? 'none'}`),
      h('button', {
        type: 'button',
        onClick: () => {
          dirty.value = true
        },
      }, 'Editor dirty'),
      h('button', {
        type: 'button',
        onClick: () => {
          dirty.value = false
        },
      }, 'Editor clean'),
      h('button', {
        type: 'button',
        onClick: () => emit('changed', 'draftUpdated'),
      }, 'Editor changed:draft'),
      h('button', {
        type: 'button',
        onClick: () => emit('changed', 'markForDeletion'),
      }, 'Editor changed:mark'),
      h('button', {
        type: 'button',
        onClick: () => emit('changed', 'unmarkForDeletion'),
      }, 'Editor changed:unmark'),
      h('button', {
        type: 'button',
        onClick: () => emit('created', 'doc-created'),
      }, 'Editor emit created'),
      h('button', {
        type: 'button',
        onClick: () => emit('saved'),
      }, 'Editor emit saved'),
      h('button', {
        type: 'button',
        onClick: () => emit('deleted'),
      }, 'Editor emit deleted'),
    ])
  },
})

const DocumentFullPageRouteStub = defineComponent({
  setup() {
    const route = useRoute()
    const router = useRouter()

    return () => h('div', { 'data-testid': 'document-full-page-route' }, [
      h('div', { 'data-testid': 'document-full-page-path' }, route.fullPath),
      h('button', {
        type: 'button',
        onClick: () => {
          const target = decodeBackTarget(route.query.back) ?? `/documents/${String(route.params.documentType ?? '')}`
          void router.push(target)
        },
      }, 'Route back'),
    ])
  },
})

async function renderDocumentPage() {
  return await renderDocumentPageWithProps()
}

async function renderDocumentPageWithProps(
  props: Record<string, unknown> = {},
  initialPath: string | { path: string; query?: Record<string, unknown> } = '/documents/pm.invoice',
) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/documents/:documentType',
        component: {
          template: '<div />',
        },
      },
      {
        path: '/full-doc/:documentType/:id',
        component: {
          template: '<div />',
        },
      },
    ],
  })

  await router.push(initialPath)
  await router.isReady()

  const view = await render(NgbMetadataDocumentListPage, {
    props: {
      editorComponent: markRaw(DocumentEditorStub),
      loadPage: async () => ({ items: [], total: 0 }),
      resolveTitle: (_documentType: string, displayName: string) => `Documents :: ${displayName}`,
      resolveStorageKey: (documentType: string) => `document-storage:${documentType}`,
      resolveWarning: () => 'Review the current open period.',
      backTarget: '/home',
      ...props,
    },
    global: {
      plugins: [router],
    },
  })

  return {
    router,
    view,
  }
}

async function renderDocumentRoundTripHarness(
  initialPath: string,
  props: Record<string, unknown> = {},
) {
  const DocumentListRoute = defineComponent({
    setup() {
      return () => h(NgbMetadataDocumentListPage, {
        editorComponent: markRaw(DocumentEditorStub),
        loadPage: async () => ({ items: [], total: 0 }),
        resolveTitle: (_documentType: string, displayName: string) => `Documents :: ${displayName}`,
        resolveStorageKey: (documentType: string) => `document-storage:${documentType}`,
        resolveWarning: () => 'Review the current open period.',
        backTarget: '/home',
        ...props,
      })
    },
  })

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/documents/:documentType',
        component: DocumentListRoute,
      },
      {
        path: '/full-doc/:documentType/:id',
        component: DocumentFullPageRouteStub,
      },
    ],
  })

  await router.push(initialPath)
  await router.isReady()

  const view = await render(defineComponent({
    setup() {
      return () => h(RouterView)
    },
  }), {
    global: {
      plugins: [router],
    },
  })

  return {
    router,
    view,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  documentMocks.preferFullPage = true
  documentMocks.registerData.page = {
    items: [{ id: 'doc-1' }],
    total: 1,
  }
  documentMocks.registerData.rows = [{ key: 'doc-1', number: 'INV-001' }]
  documentMocks.registerData.metadata = {
    displayName: 'Invoices',
    parts: [{ key: 'lines' }],
  }
})

test('navigates to full-page creation when documents prefer full-page mode', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderDocumentPage()

  await expect.element(view.getByText('title:Documents :: Invoices')).toBeVisible()
  await expect.element(view.getByText('warning:Review the current open period.')).toBeVisible()
  await expect.element(view.getByText('filter-active:true')).toBeVisible()
  await expect.element(view.getByText('Status: Open')).toBeVisible()

  await view.getByRole('button', { name: 'Layout create' }).click()

  expect(router.currentRoute.value.fullPath).toBe(
    withBackTarget('/full-doc/pm.invoice/new', '/documents/pm.invoice'),
  )
})

test('round-trips through a direct full-page document route and restores list query state from the encoded back target', async () => {
  await page.viewport(1280, 900)
  documentMocks.preferFullPage = true
  documentMocks.registerData.metadata = {
    displayName: 'Invoices',
    parts: [{ key: 'lines' }],
    list: {
      filters: [
        {
          key: 'status',
          label: 'Status',
          dataType: 'String',
        },
      ],
    },
  }

  const loadPage = vi.fn().mockResolvedValue({ items: [], total: 0 })
  const initialPath = '/documents/pm.invoice?search=late%20fees&trash=deleted&periodFrom=2026-03&periodTo=2026-04&status=posted&offset=25&limit=25'

  const { router, view } = await renderDocumentRoundTripHarness(initialPath, {
    loadPage,
  })

  await expect.element(view.getByText('title:Documents :: Invoices')).toBeVisible()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledWith({
      documentType: 'pm.invoice',
      metadata: documentMocks.registerData.metadata,
      offset: 25,
      limit: 25,
      search: 'late fees',
      trashMode: 'deleted',
      periodFrom: '2026-03-01',
      periodTo: '2026-04-30',
      listFilters: {
        status: 'posted',
      },
    })
  })

  await view.getByRole('button', { name: 'Row:doc-1' }).click()

  const fullPagePath = withBackTarget('/full-doc/pm.invoice/doc-1', initialPath)
  await vi.waitFor(() => {
    expect(router.currentRoute.value.fullPath).toBe(fullPagePath)
  })
  await expect.element(view.getByTestId('document-full-page-path')).toHaveTextContent(fullPagePath)

  await view.getByRole('button', { name: 'Route back' }).click()

  await vi.waitFor(() => {
    expect(router.currentRoute.value.fullPath).toBe(initialPath)
  })
  await expect.element(view.getByText('title:Documents :: Invoices')).toBeVisible()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenLastCalledWith({
      documentType: 'pm.invoice',
      metadata: documentMocks.registerData.metadata,
      offset: 25,
      limit: 25,
      search: 'late fees',
      trashMode: 'deleted',
      periodFrom: '2026-03-01',
      periodTo: '2026-04-30',
      listFilters: {
        status: 'posted',
      },
    })
  })
})

test('opens a local drawer, forwards filter events, and closes the filter drawer on document type changes', async () => {
  await page.viewport(1280, 900)
  documentMocks.preferFullPage = false
  documentMocks.registerData.metadata = {
    displayName: 'Invoices',
    parts: [],
  }

  const { router, view } = await renderDocumentPage()

  await view.getByRole('button', { name: 'Layout filter' }).click()
  await expect.element(view.getByText('filter-drawer-open:true')).toBeVisible()

  await view.getByRole('button', { name: 'Filter lookup query' }).click()
  await view.getByRole('button', { name: 'Filter set items' }).click()
  await view.getByRole('button', { name: 'Filter set value' }).click()
  await view.getByRole('button', { name: 'Filter undo' }).click()
  await view.getByRole('button', { name: 'Filter close' }).click()

  expect(documentMocks.filterState.handleLookupQuery).toHaveBeenCalledWith({
    key: 'property_id',
    query: 'river',
  })
  expect(documentMocks.filterState.handleItemsUpdate).toHaveBeenCalledWith({
    key: 'property_id',
    items: [{ id: 'property-1', label: 'Riverfront Tower' }],
  })
  expect(documentMocks.filterState.handleValueUpdate).toHaveBeenCalledWith({
    key: 'status',
    value: 'posted',
  })
  expect(documentMocks.filterState.undo).toHaveBeenCalledTimes(1)

  await view.getByRole('button', { name: 'Layout create' }).click()
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()
  await expect.element(view.getByText('editor-type:pm.invoice')).toBeVisible()
  await expect.element(view.getByText('editor-id:new')).toBeVisible()
  await expect.element(view.getByText('editor-expand:/full-doc/pm.invoice/new')).toBeVisible()

  await view.getByRole('button', { name: 'Layout close drawer' }).click()
  await expect.element(view.getByText('drawer-open:false', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Row:doc-1' }).click()
  await expect.element(view.getByText('editor-id:doc-1')).toBeVisible()
  await expect.element(view.getByText('editor-expand:/full-doc/pm.invoice/doc-1')).toBeVisible()

  await view.getByRole('button', { name: 'Layout filter' }).click()
  await expect.element(view.getByText('filter-drawer-open:true')).toBeVisible()

  await router.push('/documents/pm.credit_note')
  await expect.element(view.getByText('filter-drawer-open:false')).toBeVisible()
})

test('confirms discard before closing or switching dirty local document drawers', async () => {
  await page.viewport(1280, 900)
  documentMocks.preferFullPage = false
  documentMocks.registerData.metadata = {
    displayName: 'Invoices',
    parts: [],
  }
  documentMocks.registerData.page = {
    items: [{ id: 'doc-1' }, { id: 'doc-2' }],
    total: 2,
  }
  documentMocks.registerData.rows = [
    { key: 'doc-1', number: 'INV-001' },
    { key: 'doc-2', number: 'INV-002' },
  ]

  const { view } = await renderDocumentPage()

  await view.getByRole('button', { name: 'Layout create' }).click()
  await expect.element(view.getByText('editor-id:new')).toBeVisible()

  await view.getByRole('button', { name: 'Editor dirty' }).click()
  await view.getByRole('button', { name: 'Layout close drawer' }).click()
  await expect.element(view.getByTestId('stub-discard-dialog')).toBeVisible()

  await view.getByRole('button', { name: 'Discard cancel' }).click()
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Layout close drawer' }).click()
  await view.getByRole('button', { name: 'Discard confirm' }).click()
  await expect.element(view.getByText('drawer-open:false', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Row:doc-1' }).click()
  await expect.element(view.getByText('editor-id:doc-1')).toBeVisible()
  await view.getByRole('button', { name: 'Editor dirty' }).click()

  await view.getByRole('button', { name: 'Row:doc-2' }).click()
  await expect.element(view.getByTestId('stub-discard-dialog')).toBeVisible()

  await view.getByRole('button', { name: 'Discard cancel' }).click()
  await expect.element(view.getByText('editor-id:doc-1')).toBeVisible()

  await view.getByRole('button', { name: 'Row:doc-2' }).click()
  await view.getByRole('button', { name: 'Discard confirm' }).click()
  await expect.element(view.getByText('editor-id:doc-2')).toBeVisible()
})

test('keeps document drawers open for ordinary changes but closes them for deletion-state changes', async () => {
  await page.viewport(1280, 900)
  documentMocks.preferFullPage = false
  documentMocks.registerData.metadata = {
    displayName: 'Invoices',
    parts: [],
  }

  const { view } = await renderDocumentPage()

  await view.getByRole('button', { name: 'Layout create' }).click()
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Editor changed:draft' }).click()
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Editor changed:mark' }).click()
  await expect.element(view.getByText('drawer-open:false', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Layout create' }).click()
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()
  await view.getByRole('button', { name: 'Editor changed:unmark' }).click()
  await expect.element(view.getByText('drawer-open:false', { exact: true })).toBeVisible()
})

test('reopens created documents, keeps saved document drawers open, and closes after delete commits', async () => {
  await page.viewport(1280, 900)
  documentMocks.preferFullPage = false
  documentMocks.registerData.metadata = {
    displayName: 'Invoices',
    parts: [],
  }

  const { view } = await renderDocumentPage()
  const initialLoadCalls = documentMocks.registerData.load.mock.calls.length

  await view.getByRole('button', { name: 'Layout create' }).click()
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()
  await expect.element(view.getByText('editor-id:new')).toBeVisible()

  await view.getByRole('button', { name: 'Editor emit created' }).click()
  await vi.waitFor(() => {
    expect(documentMocks.registerData.load).toHaveBeenCalledTimes(initialLoadCalls + 1)
  })
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()
  await expect.element(view.getByText('editor-id:doc-created')).toBeVisible()
  await expect.element(view.getByText('editor-expand:/full-doc/pm.invoice/doc-created')).toBeVisible()

  await view.getByRole('button', { name: 'Editor emit saved' }).click()
  await vi.waitFor(() => {
    expect(documentMocks.registerData.load).toHaveBeenCalledTimes(initialLoadCalls + 2)
  })
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()
  await expect.element(view.getByText('editor-id:doc-created')).toBeVisible()

  await view.getByRole('button', { name: 'Editor emit deleted' }).click()
  await vi.waitFor(() => {
    expect(documentMocks.registerData.load).toHaveBeenCalledTimes(initialLoadCalls + 3)
  })
  await expect.element(view.getByText('drawer-open:false', { exact: true })).toBeVisible()
})

test('delegates create to handleCreateOverride and can force drawer mode even when full-page is preferred', async () => {
  await page.viewport(1280, 900)
  documentMocks.preferFullPage = true

  const handleCreateOverride = vi.fn(async ({
    documentType,
    metadata,
    preferFullPage,
    route,
    openCreateDrawer,
  }: Record<string, unknown>) => {
    expect(documentType).toBe('pm.invoice')
    expect((metadata as { displayName: string }).displayName).toBe('Invoices')
    expect(preferFullPage).toBe(true)
    expect((route as { fullPath: string }).fullPath).toBe('/documents/pm.invoice')
    ;(openCreateDrawer as (copyDraftToken?: string | null) => void)()
    return true
  })

  const { router, view } = await renderDocumentPageWithProps({
    handleCreateOverride,
  })

  await view.getByRole('button', { name: 'Layout create' }).click()

  expect(handleCreateOverride).toHaveBeenCalledTimes(1)
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()
  await expect.element(view.getByText('editor-id:new')).toBeVisible()
  expect(router.currentRoute.value.fullPath).toBe('/documents/pm.invoice')
})

test('delegates create to handleCreateOverride and can force full-page navigation from drawer-preferred documents', async () => {
  await page.viewport(1280, 900)
  documentMocks.preferFullPage = false
  documentMocks.registerData.metadata = {
    displayName: 'Invoices',
    parts: [],
  }

  const handleCreateOverride = vi.fn(async ({
    preferFullPage,
    openFullPage,
  }: Record<string, unknown>) => {
    expect(preferFullPage).toBe(false)
    await (openFullPage as () => Promise<void>)()
    return true
  })

  const { router, view } = await renderDocumentPageWithProps({
    handleCreateOverride,
  })

  await view.getByRole('button', { name: 'Layout create' }).click()

  expect(handleCreateOverride).toHaveBeenCalledTimes(1)
  expect(router.currentRoute.value.fullPath).toBe(
    withBackTarget('/full-doc/pm.invoice/new', '/documents/pm.invoice'),
  )
})

test('passes search, period, trash, paging, and list filter query params into loadPage payload', async () => {
  await page.viewport(1280, 900)
  documentMocks.registerData.metadata = {
    displayName: 'Invoices',
    parts: [{ key: 'lines' }],
    list: {
      filters: [
        {
          key: 'status',
          label: 'Status',
          dataType: 'String',
        },
      ],
    },
  }

  const loadPage = vi.fn().mockResolvedValue({ items: [], total: 0 })

  await renderDocumentPageWithProps({
    loadPage,
  }, {
    path: '/documents/pm.invoice',
    query: {
      search: 'late fees',
      trash: 'all',
      periodFrom: '2026-02',
      periodTo: '2026-04',
      status: 'posted',
      offset: '25',
      limit: '25',
    },
  })

  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledWith({
      documentType: 'pm.invoice',
      metadata: documentMocks.registerData.metadata,
      offset: 25,
      limit: 25,
      search: 'late fees',
      trashMode: 'all',
      periodFrom: '2026-02-01',
      periodTo: '2026-04-30',
      listFilters: {
        status: 'posted',
      },
    })
  })
})

test('recomputes loadPage payload when period, trash, and paging controls update the route query', async () => {
  await page.viewport(1280, 900)

  const loadPage = vi.fn().mockResolvedValue({ items: [], total: 0 })

  const { view } = await renderDocumentPageWithProps({
    loadPage,
  }, '/documents/pm.invoice?limit=25&offset=25')

  await view.getByRole('button', { name: 'Set from month' }).click()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenLastCalledWith(expect.objectContaining({
      offset: 0,
      limit: 25,
      periodFrom: '2026-03-01',
      periodTo: null,
      trashMode: 'active',
    }))
  })

  await view.getByRole('button', { name: 'Set to month' }).click()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenLastCalledWith(expect.objectContaining({
      offset: 0,
      limit: 25,
      periodFrom: '2026-03-01',
      periodTo: '2026-04-30',
    }))
  })

  await view.getByTestId('stub-recycle-bin-filter').click()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenLastCalledWith(expect.objectContaining({
      offset: 0,
      limit: 25,
      trashMode: 'deleted',
      periodFrom: '2026-03-01',
      periodTo: '2026-04-30',
    }))
  })

  await view.getByRole('button', { name: 'Layout next' }).click()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenLastCalledWith(expect.objectContaining({
      offset: 25,
      limit: 25,
      trashMode: 'deleted',
    }))
  })

  await view.getByRole('button', { name: 'Layout prev' }).click()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenLastCalledWith(expect.objectContaining({
      offset: 0,
      limit: 25,
      trashMode: 'deleted',
    }))
  })
})

test('ignores drawer-only query keys for list reloads but reloads on search and filter query changes', async () => {
  await page.viewport(1280, 900)
  documentMocks.registerData.metadata = {
    displayName: 'Invoices',
    parts: [{ key: 'lines' }],
    list: {
      filters: [
        {
          key: 'status',
          label: 'Status',
          dataType: 'String',
        },
      ],
    },
  }

  const loadPage = vi.fn().mockResolvedValue({ items: [], total: 0 })

  const { router, view } = await renderDocumentPageWithProps({
    loadPage,
  }, '/documents/pm.invoice')

  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledTimes(1)
  })

  await router.push('/documents/pm.invoice?panel=new&copyDraft=draft-1')
  await expect.element(view.getByText('drawer-open:true', { exact: true })).toBeVisible()
  await expect.element(view.getByText('editor-id:new')).toBeVisible()

  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledTimes(1)
  })

  await router.push('/documents/pm.invoice?search=rent')
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledTimes(2)
    expect(loadPage).toHaveBeenLastCalledWith(expect.objectContaining({
      search: 'rent',
      listFilters: {},
    }))
  })

  await router.push('/documents/pm.invoice?search=rent&status=posted')
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledTimes(3)
    expect(loadPage).toHaveBeenLastCalledWith(expect.objectContaining({
      search: 'rent',
      listFilters: {
        status: 'posted',
      },
    }))
  })
})
