import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h, ref, watch } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute } from 'vue-router'

import { configureNgbMetadata } from '../../../../src/ngb/metadata/config'
import NgbMetadataCatalogListPage from '../../../../src/ngb/metadata/NgbMetadataCatalogListPage.vue'
import type { MetadataCatalogListPageLoadArgs } from '../../../../src/ngb/metadata/routePages'
import type { CatalogTypeMetadata } from '../../../../src/ngb/metadata/types'
import type { MetadataRegisterPageResponse } from '../../../../src/ngb/metadata/useMetadataRegisterPageData'

type TestPageItem = NonNullable<MetadataRegisterPageResponse['items']>[number]

function dispatchEscape() {
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
}

function headerButtonByTitle(title: string): HTMLButtonElement {
  const button = document.querySelector(`[data-testid="drawer-header"] button[title="${title}"]`)
  if (!(button instanceof HTMLButtonElement)) throw new Error(`Drawer header button not found: ${title}`)
  return button
}

function makeCatalogMetadata(): CatalogTypeMetadata {
  return {
    catalogType: 'pm.property',
    displayName: 'Properties',
    kind: 1,
    list: {
      columns: [
        {
          key: 'name',
          label: 'Name',
          dataType: 'String',
          isSortable: true,
          align: 1,
        },
      ],
    },
    form: null,
    parts: null,
  }
}

function makeItem(id: string, name: string): TestPageItem {
  return {
    id,
    status: 1,
    payload: {
      fields: {
        name,
      },
    },
  }
}

const InteractiveCatalogEditor = defineComponent({
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
  emits: ['flags', 'created', 'saved', 'changed', 'deleted', 'close'],
  setup(props, { emit }) {
    const isDirty = ref(false)

    watch(
      () => props.id,
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
          canDelete: true,
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
        { 'data-testid': 'interactive-catalog-editor-state' },
        [
          `type:${props.typeCode}`,
          `id:${props.id ?? 'new'}`,
          `expand:${String(props.expandTo ?? '')}`,
        ].join(';'),
      ),
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
          emit('created', 'prop-created')
        },
      }, 'Emit created'),
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
          emit('changed', 'markForDeletion')
        },
      }, 'Emit mark-for-deletion change'),
      h('button', {
        type: 'button',
        onClick: () => {
          isDirty.value = false
          emit('deleted')
        },
      }, 'Emit deleted'),
      h('button', {
        type: 'button',
        onClick: () => emit('close'),
      }, 'Emit close'),
    ])
  },
})

const ActionAwareCatalogEditor = defineComponent({
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
  emits: ['flags'],
  setup(props, { emit, expose }) {
    const isMarkedForDeletion = ref(false)
    const isDirty = ref(false)
    const isLoading = ref(false)
    const isSaving = ref(false)
    const canSave = ref(true)
    const actionLog = ref<string[]>([])

    function push(entry: string) {
      actionLog.value = [...actionLog.value, entry]
    }

    watch(
      () => [
        props.id,
        props.expandTo,
        isMarkedForDeletion.value,
        isDirty.value,
        isLoading.value,
        isSaving.value,
        canSave.value,
      ],
      () => {
        emit('flags', {
          canSave: canSave.value,
          isDirty: isDirty.value,
          loading: isLoading.value,
          saving: isSaving.value,
          canExpand: !!props.expandTo,
          canDelete: false,
          canMarkForDeletion: !isMarkedForDeletion.value,
          canUnmarkForDeletion: isMarkedForDeletion.value,
          canPost: false,
          canUnpost: false,
          canShowAudit: true,
          canShareLink: true,
        })
      },
      { immediate: true },
    )

    expose({
      async save() {
        push('save')
      },
      openFullPage() {
        push(`expand:${String(props.expandTo ?? '')}`)
      },
      async copyShareLink() {
        push(`share:${String(props.id ?? 'new')}`)
      },
      openAuditLog() {
        push(`audit:${String(props.id ?? 'new')}`)
      },
      toggleMarkForDeletion() {
        isMarkedForDeletion.value = !isMarkedForDeletion.value
        push(isMarkedForDeletion.value ? 'mark' : 'unmark')
      },
    })

    return () => h('div', { class: 'space-y-3 p-4' }, [
      h('div', { 'data-testid': 'interactive-catalog-action-state' }, [
        `type:${props.typeCode}`,
        `id:${props.id ?? 'new'}`,
        `expand:${String(props.expandTo ?? '')}`,
        `dirty:${String(isDirty.value)}`,
        `loading:${String(isLoading.value)}`,
        `saving:${String(isSaving.value)}`,
        `canSave:${String(canSave.value)}`,
        `marked:${String(isMarkedForDeletion.value)}`,
      ].join(';')),
      h('div', { 'data-testid': 'interactive-catalog-action-log' }, actionLog.value.join('|')),
      h('button', {
        type: 'button',
        onClick: () => {
          isDirty.value = true
        },
      }, 'Set dirty true'),
      h('button', {
        type: 'button',
        onClick: () => {
          isDirty.value = false
        },
      }, 'Set dirty false'),
      h('button', {
        type: 'button',
        onClick: () => {
          isLoading.value = true
        },
      }, 'Set loading true'),
      h('button', {
        type: 'button',
        onClick: () => {
          isLoading.value = false
        },
      }, 'Set loading false'),
      h('button', {
        type: 'button',
        onClick: () => {
          isSaving.value = true
        },
      }, 'Set saving true'),
      h('button', {
        type: 'button',
        onClick: () => {
          isSaving.value = false
        },
      }, 'Set saving false'),
      h('button', {
        type: 'button',
        onClick: () => {
          canSave.value = false
        },
      }, 'Set can save false'),
      h('button', {
        type: 'button',
        onClick: () => {
          canSave.value = true
        },
      }, 'Set can save true'),
    ])
  },
})

const MetadataCatalogRouteHarness = defineComponent({
  props: {
    loadPageImpl: {
      type: Function,
      required: true,
    },
    editorComponent: {
      type: [Object, Function],
      default: InteractiveCatalogEditor,
    },
    pageProps: {
      type: Object,
      default: () => ({}),
    },
  },
  setup(props) {
    return () => h(NgbMetadataCatalogListPage, {
      editorComponent: props.editorComponent,
      loadPage: props.loadPageImpl,
      backTarget: '/home',
      ...(props.pageProps as Record<string, unknown>),
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

async function renderCatalogPipelinePage(
  initialRoute: string,
  options: {
    loadPageImpl?: (args: MetadataCatalogListPageLoadArgs) => Promise<MetadataRegisterPageResponse>
    editorComponent?: object
    pageProps?: Record<string, unknown>
  } = {},
) {
  const metadataLoadCalls: string[] = []
  const loadPageCalls: MetadataCatalogListPageLoadArgs[] = []

  configureNgbMetadata({
    loadCatalogTypeMetadata: async (catalogType) => {
      metadataLoadCalls.push(catalogType)
      return makeCatalogMetadata()
    },
    loadDocumentTypeMetadata: async (documentType) => ({
      documentType,
      displayName: documentType,
      kind: 2,
      list: {
        columns: [],
      },
      form: null,
      parts: null,
    }),
  })

  const defaultLoadPage = async (_args: MetadataCatalogListPageLoadArgs) => {
    const items = [
      makeItem('prop-river', 'Riverfront Tower'),
      makeItem('prop-harbor', 'Harbor Lofts'),
      makeItem('prop-created', 'Newly Created Property'),
    ]

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
        path: '/catalogs/:catalogType',
        component: MetadataCatalogRouteHarness,
        props: {
          editorComponent: options.editorComponent ?? InteractiveCatalogEditor,
          pageProps: options.pageProps ?? {},
          loadPageImpl: async (args: MetadataCatalogListPageLoadArgs) => {
            loadPageCalls.push({ ...args })
            return await loadPageImpl(args)
          },
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
    loadPageCalls,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
})

test('opens route-driven catalog drawers and closes them after create, save, change, and delete commits', async () => {
  await page.viewport(1440, 900)

  const { view, router, metadataLoadCalls, loadPageCalls } = await renderCatalogPipelinePage('/catalogs/pm.property?panel=new')

  await expect.element(view.getByText('Properties', { exact: true })).toBeVisible()
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:new')
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('expand:/catalogs/pm.property/new')

  expect(metadataLoadCalls).toEqual(['pm.property'])
  expect(loadPageCalls[0]).toMatchObject({
    catalogType: 'pm.property',
    offset: 0,
    limit: 50,
    trashMode: 'active',
  })

  await view.getByRole('button', { name: 'Emit created' }).click()
  await expect.poll(() => document.querySelector('[data-testid="interactive-catalog-editor-state"]')).toBeNull()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/catalogs/pm.property')

  await view.getByText('Riverfront Tower', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-river')
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('expand:/catalogs/pm.property/prop-river')
  expect(router.currentRoute.value.query.panel).toBe('edit')
  expect(router.currentRoute.value.query.id).toBe('prop-river')

  await view.getByRole('button', { name: 'Emit saved' }).click()
  await expect.poll(() => document.querySelector('[data-testid="interactive-catalog-editor-state"]')).toBeNull()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/catalogs/pm.property')

  await view.getByText('Harbor Lofts', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-harbor')

  await view.getByRole('button', { name: 'Emit mark-for-deletion change' }).click()
  await expect.poll(() => document.querySelector('[data-testid="interactive-catalog-editor-state"]')).toBeNull()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/catalogs/pm.property')

  await router.push('/catalogs/pm.property?panel=edit&id=prop-river')
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-river')

  await view.getByRole('button', { name: 'Emit deleted' }).click()
  await expect.poll(() => document.querySelector('[data-testid="interactive-catalog-editor-state"]')).toBeNull()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/catalogs/pm.property')

  expect(loadPageCalls).toHaveLength(5)
})

test('confirms discard before closing dirty real catalog drawers and cancels dirty route switches', async () => {
  await page.viewport(1440, 900)

  const { view, router } = await renderCatalogPipelinePage('/catalogs/pm.property')

  await view.getByText('Riverfront Tower', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-river')
  expect(router.currentRoute.value.query).toMatchObject({
    panel: 'edit',
    id: 'prop-river',
  })

  await view.getByRole('button', { name: 'Mark editor dirty' }).click()
  dispatchEscape()
  await expect.element(view.getByText('Discard changes?', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Keep editing' }).click()
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-river')
  expect(router.currentRoute.value.query.id).toBe('prop-river')

  dispatchEscape()
  await view.getByRole('button', { name: 'Discard' }).click()
  await expect.poll(() => document.querySelector('[data-testid="interactive-catalog-editor-state"]')).toBeNull()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/catalogs/pm.property')

  await view.getByText('Riverfront Tower', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-river')
  await view.getByRole('button', { name: 'Mark editor dirty' }).click()

  void router.push('/catalogs/pm.property?panel=edit&id=prop-harbor')
  await expect.element(view.getByText('Discard changes?', { exact: true })).toBeVisible()

  await view.getByRole('button', { name: 'Keep editing' }).click()
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-river')
  await expect.poll(() => String(router.currentRoute.value.query.id ?? '')).toBe('prop-river')
})

test('switches to the requested catalog record after confirming discard on a dirty route-driven drawer', async () => {
  await page.viewport(1440, 900)

  const { view, router } = await renderCatalogPipelinePage('/catalogs/pm.property')

  await view.getByText('Riverfront Tower', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-river')

  await view.getByRole('button', { name: 'Mark editor dirty' }).click()

  void router.push('/catalogs/pm.property?panel=edit&id=prop-created')
  await expect.element(view.getByText('Discard changes?', { exact: true })).toBeVisible()
  await view.getByRole('button', { name: 'Discard' }).click()

  await expect.poll(() => String(router.currentRoute.value.query.id ?? '')).toBe('prop-created')
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-created')
})

test('replays real catalog drawer state through browser back and forward navigation', async () => {
  await page.viewport(1440, 900)

  const { view, router } = await renderCatalogPipelinePage('/catalogs/pm.property')

  await view.getByText('Riverfront Tower', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-river')

  await router.push('/catalogs/pm.property?panel=edit&id=prop-harbor')
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-harbor')

  dispatchEscape()
  await expect.poll(() => document.querySelector('[data-testid="interactive-catalog-editor-state"]')).toBeNull()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/catalogs/pm.property')

  router.back()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toContain('id=prop-river')
  await expect.element(view.getByTestId('interactive-catalog-editor-state')).toHaveTextContent('id:prop-river')

  router.forward()
  await expect.poll(() => view.getByTestId('metadata-current-route').element().textContent ?? '').toBe('/catalogs/pm.property')
  await expect.poll(() => document.querySelector('[data-testid="interactive-catalog-editor-state"]')).toBeNull()
})

test('routes real drawer action buttons to the current editor handle and extra action handler', async () => {
  await page.viewport(1440, 900)

  const extraActionHandler = vi.fn().mockResolvedValue(undefined)

  const { view } = await renderCatalogPipelinePage('/catalogs/pm.property', {
    editorComponent: ActionAwareCatalogEditor,
    pageProps: {
      resolveDrawerExtraActions: () => [
        {
          key: 'archive',
          title: 'Archive',
          icon: 'history',
          disabled: false,
        },
      ],
      handleDrawerExtraAction: extraActionHandler,
    },
  })

  await view.getByText('Riverfront Tower', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-catalog-action-state')).toHaveTextContent('id:prop-river')

  await expect.element(view.getByTitle('Open full page')).toBeVisible()
  await expect.element(view.getByTitle('Share link')).toBeVisible()
  await expect.element(view.getByTitle('Audit log')).toBeVisible()
  await expect.element(view.getByTitle('Mark for deletion')).toBeVisible()
  await expect.element(view.getByTitle('Archive')).toBeVisible()
  await expect.poll(() => headerButtonByTitle('Save').disabled).toBe(false)

  await view.getByTitle('Open full page').click()
  await view.getByTitle('Share link').click()
  await view.getByTitle('Audit log').click()
  await view.getByTitle('Mark for deletion').click()
  await expect.element(view.getByTitle('Unmark for deletion')).toBeVisible()
  await view.getByTitle('Unmark for deletion').click()
  await expect.element(view.getByTitle('Mark for deletion')).toBeVisible()
  headerButtonByTitle('Save').click()
  await view.getByTitle('Archive').click()

  await expect.element(view.getByTestId('interactive-catalog-action-state')).toHaveTextContent('marked:false')
  await expect.element(view.getByTestId('interactive-catalog-action-log')).toHaveTextContent(
    'expand:/catalogs/pm.property/prop-river|share:prop-river|audit:prop-river|mark|unmark|save',
  )

  expect(extraActionHandler).toHaveBeenCalledTimes(1)
  expect(extraActionHandler.mock.calls[0]?.[0]).toMatchObject({
    action: 'archive',
  })
})

test('reactively disables built-in drawer actions and recomputes extra actions from real editor flags', async () => {
  await page.viewport(1440, 900)

  const { view } = await renderCatalogPipelinePage('/catalogs/pm.property', {
    editorComponent: ActionAwareCatalogEditor,
    pageProps: {
      resolveDrawerExtraActions: ({ editorFlags }: { editorFlags: { isDirty: boolean; loading: boolean; saving: boolean } }) => [
        {
          key: 'archive',
          title: editorFlags.isDirty ? 'Archive dirty' : 'Archive clean',
          icon: 'history',
          disabled: editorFlags.loading || editorFlags.saving,
        },
      ],
    },
  })

  await view.getByText('Riverfront Tower', { exact: true }).click()
  await expect.element(view.getByTestId('interactive-catalog-action-state')).toHaveTextContent('id:prop-river')

  await expect.element(view.getByTitle('Archive clean')).toBeVisible()
  expect((view.getByTitle('Open full page').element() as HTMLButtonElement).disabled).toBe(false)
  expect(headerButtonByTitle('Save').disabled).toBe(false)
  expect((view.getByTitle('Archive clean').element() as HTMLButtonElement).disabled).toBe(false)

  await view.getByRole('button', { name: 'Set dirty true' }).click()
  await expect.element(view.getByTitle('Archive dirty')).toBeVisible()

  await view.getByRole('button', { name: 'Set saving true' }).click()
  await expect.element(view.getByTestId('interactive-catalog-action-state')).toHaveTextContent('saving:true')
  expect((view.getByTitle('Open full page').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Share link').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Audit log').element() as HTMLButtonElement).disabled).toBe(true)
  expect((view.getByTitle('Mark for deletion').element() as HTMLButtonElement).disabled).toBe(true)
  expect(headerButtonByTitle('Save').disabled).toBe(true)
  expect((view.getByTitle('Archive dirty').element() as HTMLButtonElement).disabled).toBe(true)

  await view.getByRole('button', { name: 'Set saving false' }).click()
  await view.getByRole('button', { name: 'Set can save false' }).click()
  await expect.element(view.getByTestId('interactive-catalog-action-state')).toHaveTextContent('canSave:false')
  expect((view.getByTitle('Open full page').element() as HTMLButtonElement).disabled).toBe(false)
  expect((view.getByTitle('Share link').element() as HTMLButtonElement).disabled).toBe(false)
  expect((view.getByTitle('Audit log').element() as HTMLButtonElement).disabled).toBe(false)
  expect((view.getByTitle('Mark for deletion').element() as HTMLButtonElement).disabled).toBe(false)
  expect((view.getByTitle('Archive dirty').element() as HTMLButtonElement).disabled).toBe(false)
  expect(headerButtonByTitle('Save').disabled).toBe(true)
})
