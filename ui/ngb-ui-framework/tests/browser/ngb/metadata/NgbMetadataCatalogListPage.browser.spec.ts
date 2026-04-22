import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, markRaw, ref, watch } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import {
  StubEditorDiscardDialog,
  StubEntityEditorDrawerActions,
  StubRecycleBinFilter,
  StubRegisterPageLayout,
} from './stubs'

const catalogMocks = vi.hoisted(() => ({
  registerData: {
    loading: false,
    error: null as string | null,
    metadata: {
      displayName: 'Properties',
    },
    page: {
      items: [{ id: 'prop-1' }],
      total: 1,
    },
    columns: [{ key: 'name', title: 'Name' }],
    rows: [{ key: 'prop-1', name: 'Riverfront Tower' }],
    load: vi.fn().mockResolvedValue(true),
  },
  metadataStore: {
    ensureCatalogType: vi.fn(),
  },
  navigateBack: vi.fn(),
  editorHandle: {
    openFullPage: vi.fn(),
    copyShareLink: vi.fn(),
    openAuditLog: vi.fn(),
    toggleMarkForDeletion: vi.fn(),
    save: vi.fn(),
  },
  extraActionHandler: vi.fn().mockResolvedValue(undefined),
}))

vi.mock('../../../../src/ngb/metadata/store', () => ({
  useMetadataStore: () => catalogMocks.metadataStore,
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
      loadPage: (args: { entityTypeCode: string }) => Promise<unknown>
    }) => {
      async function runAutoLoad() {
        await args.loadPage({
          entityTypeCode: args.entityTypeCode.value,
        })
      }

      const manualLoad = vi.fn(async () => {
        await runAutoLoad()
        return true
      })
      catalogMocks.registerData.load = manualLoad

      watch(
        () => args.reloadKey.value,
        () => {
          void runAutoLoad()
        },
        { immediate: true },
      )

      return {
        loading: computed(() => catalogMocks.registerData.loading),
        error: computed(() => catalogMocks.registerData.error),
        metadata: computed(() => catalogMocks.registerData.metadata),
        page: computed(() => catalogMocks.registerData.page),
        columns: computed(() => catalogMocks.registerData.columns),
        rows: computed(() => catalogMocks.registerData.rows),
        load: manualLoad,
      }
    },
  }
})

vi.mock('../../../../src/ngb/metadata/NgbRegisterPageLayout.vue', () => ({
  default: StubRegisterPageLayout,
}))

vi.mock('../../../../src/ngb/metadata/NgbRecycleBinFilter.vue', () => ({
  default: StubRecycleBinFilter,
}))

vi.mock('../../../../src/ngb/editor/NgbEntityEditorDrawerActions.vue', () => ({
  default: StubEntityEditorDrawerActions,
}))

vi.mock('../../../../src/ngb/editor/NgbEditorDiscardDialog.vue', () => ({
  default: StubEditorDiscardDialog,
}))

vi.mock('../../../../src/ngb/router/backNavigation', () => ({
  navigateBack: catalogMocks.navigateBack,
}))

vi.mock('../../../../src/ngb/editor/catalogNavigation', () => ({
  buildCatalogFullPageUrl: (catalogType: string, id?: string | null) =>
    id ? `/catalogs-full/${catalogType}/${id}` : `/catalogs-full/${catalogType}/new`,
}))

import NgbMetadataCatalogListPage from '../../../../src/ngb/metadata/NgbMetadataCatalogListPage.vue'

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

const CatalogEditorStub = defineComponent({
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
  emits: ['flags', 'created', 'saved', 'changed', 'deleted'],
  setup(props, { expose, emit }) {
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

    expose(catalogMocks.editorHandle)

    return () => h('div', { 'data-testid': 'catalog-editor' }, [
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
        onClick: () => emit('created', 'prop-created'),
      }, 'Editor emit created'),
      h('button', {
        type: 'button',
        onClick: () => emit('saved'),
      }, 'Editor emit saved'),
      h('button', {
        type: 'button',
        onClick: () => emit('changed', 'markForDeletion'),
      }, 'Editor emit changed'),
      h('button', {
        type: 'button',
        onClick: () => emit('deleted'),
      }, 'Editor emit deleted'),
    ])
  },
})

async function renderCatalogPage(
  initialPath = '/catalogs/pm.property',
  props: Record<string, unknown> = {},
) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/catalogs/:catalogType',
        component: {
          template: '<div />',
        },
      },
      {
        path: '/home',
        component: {
          template: '<div />',
        },
      },
    ],
  })

  await router.push(initialPath)
  await router.isReady()

  const view = await render(NgbMetadataCatalogListPage, {
    props: {
      editorComponent: markRaw(CatalogEditorStub),
      loadPage: async () => ({ items: [], total: 0 }),
      resolveTitle: (_catalogType: string, displayName: string) => `Catalog :: ${displayName}`,
      resolveStorageKey: (catalogType: string) => `catalog-storage:${catalogType}`,
      resolveDrawerExtraActions: () => [{ key: 'archive', title: 'Archive' }],
      handleDrawerExtraAction: catalogMocks.extraActionHandler,
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

beforeEach(() => {
  vi.clearAllMocks()
  catalogMocks.registerData.loading = false
  catalogMocks.registerData.error = null
  catalogMocks.registerData.page = {
    items: [{ id: 'prop-1' }],
    total: 1,
  }
  catalogMocks.registerData.rows = [{ key: 'prop-1', name: 'Riverfront Tower' }]
})

test('refreshes, navigates back, and opens create and edit drawers through route query', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderCatalogPage()

  await expect.element(view.getByText('title:Catalog :: Properties')).toBeVisible()
  await expect.element(view.getByText('storage:catalog-storage:pm.property')).toBeVisible()

  await view.getByRole('button', { name: 'Layout refresh' }).click()
  await view.getByRole('button', { name: 'Layout back' }).click()

  expect(catalogMocks.registerData.load).toHaveBeenCalledTimes(1)
  expect(catalogMocks.navigateBack).toHaveBeenCalledTimes(1)
  expect(catalogMocks.navigateBack.mock.calls[0]?.[0]).toBe(router)
  expect(catalogMocks.navigateBack.mock.calls[0]?.[1]?.path).toBe('/catalogs/pm.property')
  expect(catalogMocks.navigateBack.mock.calls[0]?.[2]).toBe('/home')

  await view.getByRole('button', { name: 'Layout create' }).click()
  await expect.element(view.getByText('drawer-open:true')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBe('new')

  await expect.element(view.getByText('editor-type:pm.property')).toBeVisible()
  await expect.element(view.getByText('editor-id:new')).toBeVisible()
  await expect.element(view.getByText('editor-expand:/catalogs-full/pm.property/new')).toBeVisible()

  await view.getByRole('button', { name: 'Layout close drawer' }).click()
  await expect.element(view.getByText('drawer-open:false')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBeUndefined()

  await view.getByRole('button', { name: 'Row:prop-1' }).click()
  await expect.element(view.getByText('editor-id:prop-1')).toBeVisible()
  await expect.element(view.getByText('editor-expand:/catalogs-full/pm.property/prop-1')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBe('edit')
  expect(router.currentRoute.value.query.id).toBe('prop-1')
})

test('forwards standard and extra drawer actions to the current editor handle', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderCatalogPage()

  await view.getByRole('button', { name: 'Layout create' }).click()
  await expect.element(view.getByTestId('catalog-editor')).toBeVisible()

  await view.getByRole('button', { name: 'Drawer action:expand' }).click()
  await view.getByRole('button', { name: 'Drawer action:share' }).click()
  await view.getByRole('button', { name: 'Drawer action:audit' }).click()
  await view.getByRole('button', { name: 'Drawer action:mark' }).click()
  await view.getByRole('button', { name: 'Drawer action:save' }).click()
  await view.getByRole('button', { name: 'Drawer action:archive' }).click()

  expect(catalogMocks.editorHandle.openFullPage).toHaveBeenCalledTimes(1)
  expect(catalogMocks.editorHandle.copyShareLink).toHaveBeenCalledTimes(1)
  expect(catalogMocks.editorHandle.openAuditLog).toHaveBeenCalledTimes(1)
  expect(catalogMocks.editorHandle.toggleMarkForDeletion).toHaveBeenCalledTimes(1)
  expect(catalogMocks.editorHandle.save).toHaveBeenCalledTimes(1)
  expect(catalogMocks.extraActionHandler).toHaveBeenCalledWith({
    action: 'archive',
    editor: expect.any(Object),
  })
})

test('applies the generic commit policy by closing drawers after create, save, change, and delete commits', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderCatalogPage('/catalogs/pm.property?panel=new')
  const initialLoadCalls = catalogMocks.registerData.load.mock.calls.length

  await expect.element(view.getByText('drawer-open:true')).toBeVisible()
  await expect.element(view.getByText('editor-id:new')).toBeVisible()

  await view.getByRole('button', { name: 'Editor emit created' }).click()
  await vi.waitFor(() => {
    expect(catalogMocks.registerData.load).toHaveBeenCalledTimes(initialLoadCalls + 1)
  })
  await expect.element(view.getByText('drawer-open:false')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBeUndefined()
  expect(router.currentRoute.value.query.id).toBeUndefined()

  await view.getByRole('button', { name: 'Row:prop-1' }).click()
  await expect.element(view.getByText('editor-id:prop-1')).toBeVisible()
  await view.getByRole('button', { name: 'Editor emit saved' }).click()
  await vi.waitFor(() => {
    expect(catalogMocks.registerData.load).toHaveBeenCalledTimes(initialLoadCalls + 2)
  })
  await expect.element(view.getByText('drawer-open:false')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBeUndefined()
  expect(router.currentRoute.value.query.id).toBeUndefined()

  await view.getByRole('button', { name: 'Row:prop-1' }).click()
  await expect.element(view.getByText('editor-id:prop-1')).toBeVisible()
  await view.getByRole('button', { name: 'Editor emit changed' }).click()
  await vi.waitFor(() => {
    expect(catalogMocks.registerData.load).toHaveBeenCalledTimes(initialLoadCalls + 3)
  })
  await expect.element(view.getByText('drawer-open:false')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBeUndefined()
  expect(router.currentRoute.value.query.id).toBeUndefined()

  await view.getByRole('button', { name: 'Row:prop-1' }).click()
  await expect.element(view.getByText('editor-id:prop-1')).toBeVisible()
  await view.getByRole('button', { name: 'Editor emit deleted' }).click()
  await vi.waitFor(() => {
    expect(catalogMocks.registerData.load).toHaveBeenCalledTimes(initialLoadCalls + 4)
  })
  await expect.element(view.getByText('drawer-open:false')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBeUndefined()
  expect(router.currentRoute.value.query.id).toBeUndefined()
})

test('confirms discard before closing or switching dirty catalog drawers', async () => {
  await page.viewport(1280, 900)

  catalogMocks.registerData.page = {
    items: [{ id: 'prop-1' }, { id: 'prop-2' }],
    total: 2,
  }
  catalogMocks.registerData.rows = [
    { key: 'prop-1', name: 'Riverfront Tower' },
    { key: 'prop-2', name: 'Harbor Lofts' },
  ]

  const { router, view } = await renderCatalogPage()

  await view.getByRole('button', { name: 'Row:prop-1' }).click()
  await expect.element(view.getByText('editor-id:prop-1')).toBeVisible()

  await view.getByRole('button', { name: 'Editor dirty' }).click()
  await view.getByRole('button', { name: 'Layout close drawer' }).click()
  await expect.element(view.getByTestId('stub-discard-dialog')).toBeVisible()

  await view.getByRole('button', { name: 'Discard cancel' }).click()
  await expect.element(view.getByText('drawer-open:true')).toBeVisible()
  expect(router.currentRoute.value.query).toMatchObject({
    panel: 'edit',
    id: 'prop-1',
  })

  await view.getByRole('button', { name: 'Layout close drawer' }).click()
  await view.getByRole('button', { name: 'Discard confirm' }).click()
  await expect.element(view.getByText('drawer-open:false')).toBeVisible()
  expect(router.currentRoute.value.query.panel).toBeUndefined()
  expect(router.currentRoute.value.query.id).toBeUndefined()

  await view.getByRole('button', { name: 'Row:prop-1' }).click()
  await expect.element(view.getByText('editor-id:prop-1')).toBeVisible()
  await view.getByRole('button', { name: 'Editor dirty' }).click()

  await view.getByRole('button', { name: 'Row:prop-2' }).click()
  await expect.element(view.getByTestId('stub-discard-dialog')).toBeVisible()

  await view.getByRole('button', { name: 'Discard cancel' }).click()
  await expect.element(view.getByText('editor-id:prop-1')).toBeVisible()
  expect(router.currentRoute.value.query.id).toBe('prop-1')

  await view.getByRole('button', { name: 'Row:prop-2' }).click()
  await view.getByRole('button', { name: 'Discard confirm' }).click()
  await expect.element(view.getByText('editor-id:prop-2')).toBeVisible()
  expect(router.currentRoute.value.query.id).toBe('prop-2')
})

test('updates trash and paging query params through the register controls', async () => {
  await page.viewport(1280, 900)

  const { router, view } = await renderCatalogPage('/catalogs/pm.property?limit=25')

  await expect.element(view.getByText('title:Catalog :: Properties')).toBeVisible()
  await expect.element(view.getByText('trash:active')).toBeVisible()

  await view.getByRole('button', { name: 'Layout next' }).click()
  expect(router.currentRoute.value.query).toMatchObject({
    limit: '25',
    offset: '25',
  })

  await view.getByRole('button', { name: 'Layout prev' }).click()
  expect(router.currentRoute.value.query).toMatchObject({
    limit: '25',
    offset: '0',
  })

  await view.getByTestId('stub-recycle-bin-filter').click()
  expect(router.currentRoute.value.query).toMatchObject({
    limit: '25',
    offset: '0',
    trash: 'deleted',
  })
})

test('passes search, trash, and paging query params into loadPage payload', async () => {
  await page.viewport(1280, 900)

  const loadPage = vi.fn().mockResolvedValue({ items: [], total: 0 })

  await renderCatalogPage('/catalogs/pm.property?search=river&trash=all&offset=25&limit=25', {
    loadPage,
  })

  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledWith({
      catalogType: 'pm.property',
      offset: 25,
      limit: 25,
      search: 'river',
      trashMode: 'all',
    })
  })
})

test('recomputes loadPage payload when search, trash, and paging query state changes', async () => {
  await page.viewport(1280, 900)

  const loadPage = vi.fn().mockResolvedValue({ items: [], total: 0 })

  const { router, view } = await renderCatalogPage('/catalogs/pm.property?limit=25&offset=25', {
    loadPage,
  })

  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledWith({
      catalogType: 'pm.property',
      offset: 25,
      limit: 25,
      search: undefined,
      trashMode: 'active',
    })
  })

  await router.push('/catalogs/pm.property?limit=25&offset=25&search=lease')
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenLastCalledWith({
      catalogType: 'pm.property',
      offset: 25,
      limit: 25,
      search: 'lease',
      trashMode: 'active',
    })
  })

  await view.getByTestId('stub-recycle-bin-filter').click()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenLastCalledWith({
      catalogType: 'pm.property',
      offset: 0,
      limit: 25,
      search: 'lease',
      trashMode: 'deleted',
    })
  })

  await view.getByRole('button', { name: 'Layout next' }).click()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenLastCalledWith({
      catalogType: 'pm.property',
      offset: 25,
      limit: 25,
      search: 'lease',
      trashMode: 'deleted',
    })
  })
})

test('ignores drawer-only query keys for reloads but reloads on search changes', async () => {
  await page.viewport(1280, 900)

  const loadPage = vi.fn().mockResolvedValue({ items: [], total: 0 })

  const { router, view } = await renderCatalogPage('/catalogs/pm.property', {
    loadPage,
  })

  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledTimes(1)
  })

  await router.push('/catalogs/pm.property?panel=new')
  await expect.element(view.getByText('drawer-open:true')).toBeVisible()
  await expect.element(view.getByText('editor-id:new')).toBeVisible()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledTimes(1)
  })

  await router.push('/catalogs/pm.property?panel=edit&id=prop-1')
  await expect.element(view.getByText('editor-id:prop-1')).toBeVisible()
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledTimes(1)
  })

  await router.push('/catalogs/pm.property?search=river')
  await vi.waitFor(() => {
    expect(loadPage).toHaveBeenCalledTimes(2)
    expect(loadPage).toHaveBeenLastCalledWith({
      catalogType: 'pm.property',
      offset: 0,
      limit: 50,
      search: 'river',
      trashMode: 'active',
    })
  })
})
