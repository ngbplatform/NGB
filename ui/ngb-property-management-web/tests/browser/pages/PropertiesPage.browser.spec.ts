import { computed, defineComponent, h, nextTick, reactive, ref, type PropType } from 'vue'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  buildFullPageUrl: vi.fn(),
  catalogPage: vi.fn(),
  buildingSummary: vi.fn(),
  normalizeTrashMode: vi.fn(),
  replaceQuery: vi.fn(),
  routerBack: vi.fn(),
  legacyCompat: vi.fn(),
  resetDrawerHeading: vi.fn(),
  requestDiscard: vi.fn(),
  discardConfirm: vi.fn(),
  discardCancel: vi.fn(),
  beforeCloseDrawer: vi.fn(),
  handleEditorFlags: vi.fn(),
  handleEditorState: vi.fn(),
  openCreateDrawer: vi.fn(),
  openEditDrawer: vi.fn(),
  closeRouteDrawer: vi.fn(),
  handleCreated: vi.fn(),
  handleSaved: vi.fn(),
  handleChanged: vi.fn(),
  handleDeleted: vi.fn(),
  route: null as any,
  router: null as any,
  drawerState: null as any,
  routeDrawerState: null as any,
  routeDrawerConfig: null as any,
  commitConfig: null as any,
  grids: {} as Record<string, any>,
  bulkProps: null as any,
  editorProps: null as any,
  editorMethods: null as any,
}))

vi.mock('vue-router', () => ({
  useRoute: () => mocks.route,
  useRouter: () => mocks.router,
}))

vi.mock('../../../src/reporting/queries', () => ({
  getPmBuildingSummary: mocks.buildingSummary,
}))

vi.mock('../../../src/features/properties/usePropertiesLegacyQueryCompat', () => ({
  usePropertiesLegacyQueryCompat: mocks.legacyCompat,
}))

vi.mock('../../../src/editor/pm/PmEntityEditor.vue', () => ({
  default: defineComponent({
    props: {
      id: { type: String, default: null },
      initialFields: { type: Object, default: null },
      expandTo: { type: String, default: null },
    },
    emits: ['state', 'flags', 'created', 'saved', 'changed', 'deleted', 'close'],
    setup(props, { emit, expose }) {
      const methods = {
        openFullPage: vi.fn(),
        copyShareLink: vi.fn(async () => undefined),
        openAuditLog: vi.fn(),
        toggleMarkForDeletion: vi.fn(),
        openBulkCreateUnitsWizard: vi.fn(),
        save: vi.fn(async () => undefined),
      }
      mocks.editorMethods = methods
      expose(methods)
      return () => {
        mocks.editorProps = props
        return h('div', { 'data-testid': 'pm-editor' }, [
          h('span', `editor-id:${props.id ?? 'new'}`),
          h('span', `editor-initial:${JSON.stringify(props.initialFields)}`),
          h('span', `editor-expand:${props.expandTo ?? 'none'}`),
          h('button', { type: 'button', onClick: () => emit('state', { title: 'State title' }) }, 'Emit state'),
          h('button', { type: 'button', onClick: () => emit('flags', { isDirty: true }) }, 'Emit flags'),
          h('button', { type: 'button', onClick: () => emit('created', { id: 'created-building' }) }, 'Emit created'),
          h('button', { type: 'button', onClick: () => emit('saved', { id: 'saved' }) }, 'Emit saved'),
          h('button', { type: 'button', onClick: () => emit('changed', { id: 'changed' }) }, 'Emit changed'),
          h('button', { type: 'button', onClick: () => emit('deleted', { id: 'deleted' }) }, 'Emit deleted'),
          h('button', { type: 'button', onClick: () => emit('close') }, 'Editor close'),
        ])
      }
    },
  }),
}))

vi.mock('../../../src/components/property/PmPropertyBulkCreateUnitsDialog.vue', () => ({
  default: defineComponent({
    props: {
      open: { type: Boolean, default: false },
      buildingId: { type: String, required: true },
      buildingDisplay: { type: String, default: null },
    },
    emits: ['update:open', 'created'],
    setup(props, { emit }) {
      return () => {
        mocks.bulkProps = props
        return h('section', { 'data-testid': 'bulk-dialog' }, [
          h('span', `bulk:${String(props.open)}:${props.buildingId}:${props.buildingDisplay ?? 'none'}`),
          h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Bulk close'),
          h('button', { type: 'button', onClick: () => emit('created') }, 'Bulk created'),
        ])
      }
    },
  }),
}))

vi.mock('@ngbplatform/ui', () => {
  const PageHeader = defineComponent({
    props: { title: { type: String, required: true }, canBack: { type: Boolean, default: false } },
    emits: ['back'],
    setup(props, { emit, slots }) {
      return () => h('header', [
        h('h1', props.title),
        h('span', `can-back:${String(props.canBack)}`),
        h('button', { type: 'button', onClick: () => emit('back') }, 'Header back'),
        slots.secondary?.(),
        slots.actions?.(),
      ])
    },
  })
  const Icon = defineComponent({
    props: { name: { type: String, required: true } },
    setup: (props) => () => h('span', { 'data-testid': `icon-${props.name}` }),
  })
  const Grid = defineComponent({
    props: {
      title: { type: String, required: true },
      subtitle: { type: String, default: '' },
      storageKey: { type: String, required: true },
      columns: { type: Array as PropType<any[]>, default: () => [] },
      rows: { type: Array as PropType<any[]>, default: () => [] },
      selectedKeys: { type: Array as PropType<string[]>, default: () => [] },
    },
    emits: ['update:selectedKeys', 'rowActivate'],
    setup(props, { emit, slots }) {
      return () => {
        mocks.grids[props.storageKey] = props
        return h('section', { 'data-testid': `grid-${props.storageKey}` }, [
          h('h2', props.title),
          h('span', `subtitle:${props.subtitle}`),
          h('span', `rows:${props.rows.length}`),
          h('button', { type: 'button', onClick: () => emit('update:selectedKeys', []) }, `Clear ${props.title}`),
          h('button', { type: 'button', onClick: () => emit('update:selectedKeys', [props.rows[0]?.key ?? 'missing']) }, `Select ${props.title}`),
          h('button', { type: 'button', onClick: () => emit('update:selectedKeys', ['missing']) }, `Select missing ${props.title}`),
          h('button', { type: 'button', onClick: () => emit('rowActivate', props.rows[0]?.key ?? '') }, `Activate ${props.title}`),
          slots.toolbar?.(),
        ])
      }
    },
  })
  const RecycleFilter = defineComponent({
    props: { modelValue: { type: String, required: true }, disabled: { type: Boolean, default: false } },
    emits: ['update:modelValue'],
    setup(props, { emit }) {
      return () => h('button', {
        type: 'button',
        'data-testid': `trash-${props.modelValue}`,
        onClick: () => emit('update:modelValue', 'only'),
      }, `Trash ${props.modelValue}`)
    },
  })
  const Drawer = defineComponent({
    props: {
      open: { type: Boolean, default: false },
      title: { type: String, default: '' },
      subtitle: { type: String, default: '' },
      beforeClose: { type: Function, default: null },
    },
    emits: ['update:open'],
    setup(props, { emit, slots }) {
      return () => h('aside', { 'data-testid': 'drawer' }, [
        h('span', `drawer:${String(props.open)}:${props.title}:${props.subtitle}`),
        h('button', { type: 'button', onClick: () => emit('update:open', false) }, 'Drawer close'),
        h('button', { type: 'button', onClick: () => emit('update:open', true) }, 'Drawer keep open'),
        slots.actions?.(),
        slots.default?.(),
      ])
    },
  })
  const DrawerActions = defineComponent({
    props: { extraActions: { type: Array as PropType<any[]>, default: () => [] } },
    emits: ['action'],
    setup(props, { emit }) {
      return () => h('div', [
        ...['expand', 'share', 'audit', 'mark', 'bulkCreateUnits', 'save', 'unknown'].map((action) =>
          h('button', { type: 'button', onClick: () => emit('action', action) }, `Action ${action}`),
        ),
        h('span', `extras:${props.extraActions.length}`),
      ])
    },
  })
  const DiscardDialog = defineComponent({
    props: { open: { type: Boolean, default: false } },
    emits: ['cancel', 'confirm'],
    setup(props, { emit }) {
      return () => h('div', [
        h('span', `discard:${String(props.open)}`),
        h('button', { type: 'button', onClick: () => emit('cancel') }, 'Discard cancel'),
        h('button', { type: 'button', onClick: () => emit('confirm') }, 'Discard confirm'),
      ])
    },
  })

  return {
    NgbDrawer: Drawer,
    NgbEditorDiscardDialog: DiscardDialog,
    NgbEntityEditorDrawerActions: DrawerActions,
    NgbIcon: Icon,
    NgbPageHeader: PageHeader,
    NgbRecycleBinFilter: RecycleFilter,
    NgbRegisterGrid: Grid,
    buildCatalogFullPageUrl: mocks.buildFullPageUrl,
    formatLooseEntityValue: (value: unknown) => `formatted:${String(value)}`,
    getCatalogPage: mocks.catalogPage,
    normalizeTrashMode: mocks.normalizeTrashMode,
    replaceCleanRouteQuery: mocks.replaceQuery,
    toErrorMessage: (cause: unknown, fallback: string) => cause instanceof Error ? cause.message : fallback,
    useEditorDrawerState: () => mocks.drawerState,
    useEntityEditorCommitHandlers: (config: any) => {
      mocks.commitConfig = config
      return {
        handleCreated: mocks.handleCreated,
        handleSaved: mocks.handleSaved,
        handleChanged: mocks.handleChanged,
        handleDeleted: mocks.handleDeleted,
      }
    },
    useRouteQueryEditorDrawer: (config: any) => {
      mocks.routeDrawerConfig = config
      return mocks.routeDrawerState
    },
  }
})

import PropertiesPage from '../../../src/pages/PropertiesPage.vue'

function building(id = 'building-1', display: unknown = 'Building One') {
  return {
    id,
    display,
    isDeleted: false,
    isMarkedForDeletion: false,
    payload: { fields: { display: 'Payload building', address_line1: '1 Main', city: 'Boston', state: 'MA', zip: '02101' } },
  }
}

function unit(id = 'unit-1') {
  return {
    id,
    display: null,
    payload: { fields: { display: 'Unit One', unit_no: '101', parent_property_id: 'building-1' } },
  }
}

async function flushUi() {
  await Promise.resolve()
  await nextTick()
  await new Promise((resolvePromise) => window.setTimeout(resolvePromise, 50))
}

async function renderPage() {
  const view = await render(PropertiesPage)
  await flushUi()
  return view
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.route = reactive({ path: '/properties', query: {} as Record<string, unknown> })
  mocks.router = { back: mocks.routerBack }
  mocks.normalizeTrashMode.mockImplementation((value: unknown) => value === 'only' ? 'only' : 'active')
  mocks.replaceQuery.mockImplementation(async (_route: any, _router: any, patch: Record<string, unknown>) => {
    for (const [key, value] of Object.entries(patch)) {
      if (value == null) delete mocks.route.query[key]
      else mocks.route.query[key] = String(value)
    }
  })
  mocks.buildFullPageUrl.mockImplementation((_type: string, id?: string) => id ? `/catalogs/pm.property/${id}` : '/catalogs/pm.property')
  mocks.catalogPage.mockImplementation(async (_type: string, request: any) => {
    if (request.filters.kind === 'Building') {
      const items = request.offset === 7
        ? Array.from({ length: 50 }, (_, index) => building(`building-${index + 1}`))
        : [building(), building('payload-building', null), { id: 'empty-building', display: null, payload: null }]
      return { items, offset: request.offset, limit: request.limit, total: request.offset === 7 ? null : items.length }
    }
    const items = request.offset === 7
      ? Array.from({ length: 50 }, (_, index) => unit(`unit-${index + 1}`))
      : [unit()]
    return { items, offset: request.offset, limit: request.limit, total: request.offset === 7 ? null : items.length }
  })
  mocks.buildingSummary.mockResolvedValue({ buildingDisplay: 'Summary Building', totalUnits: 10, occupiedUnits: 7, vacantUnits: 3, vacancyPercent: 30 })

  mocks.drawerState = {
    drawerTitle: ref(''),
    drawerSubtitle: ref('Drawer subtitle'),
    editorFlags: ref({ isDirty: false, loading: false, saving: false, extras: null }),
    discardOpen: ref(false),
    handleEditorFlags: mocks.handleEditorFlags,
    handleEditorState: mocks.handleEditorState,
    resetDrawerHeading: mocks.resetDrawerHeading,
    requestDiscard: mocks.requestDiscard,
    discardConfirm: mocks.discardConfirm,
    discardCancel: mocks.discardCancel,
    beforeCloseDrawer: mocks.beforeCloseDrawer,
  }
  mocks.requestDiscard.mockResolvedValue(true)
  mocks.beforeCloseDrawer.mockResolvedValue(true)
  mocks.routeDrawerState = {
    panelMode: ref<'new' | 'edit' | null>(null),
    currentId: ref<string | null>(null),
    isPanelOpen: ref(false),
    openCreateDrawer: mocks.openCreateDrawer,
    openEditDrawer: mocks.openEditDrawer,
    closeDrawer: mocks.closeRouteDrawer,
  }
  mocks.openCreateDrawer.mockImplementation(async (options: any) => {
    Object.assign(mocks.route.query, Object.fromEntries(Object.entries(options.patch).map(([key, value]) => [key, String(value)])))
    mocks.routeDrawerState.panelMode.value = 'new'
    mocks.routeDrawerState.currentId.value = null
    mocks.routeDrawerState.isPanelOpen.value = true
    options.onCommit?.()
  })
  mocks.openEditDrawer.mockImplementation(async (id: string, options: any) => {
    mocks.routeDrawerState.panelMode.value = 'edit'
    mocks.routeDrawerState.currentId.value = id
    mocks.routeDrawerState.isPanelOpen.value = true
    options.onCommit?.()
  })
  mocks.closeRouteDrawer.mockImplementation(async () => {
    mocks.routeDrawerState.isPanelOpen.value = false
  })
  mocks.handleCreated.mockImplementation(async (payload: any) => mocks.commitConfig.onCreated?.(payload))
})

test('loads both panels, maps rows, formats columns, selects a building, and pages safely', async () => {
  const view = await renderPage()
  expect(mocks.legacyCompat).toHaveBeenCalledWith(mocks.route, mocks.router)
  await expect.element(view.getByText('can-back:true')).toBeVisible()
  await expect.element(view.getByText('3 / 3', { exact: true }).first()).toBeVisible()
  await view.getByRole('button', { name: 'Header back' }).click()
  expect(mocks.routerBack).toHaveBeenCalledOnce()

  const buildings = mocks.grids['pm:properties:buildings']
  expect(buildings.rows[0]).toMatchObject({ key: 'building-1', display: 'Building One', address_line1: '1 Main', city: 'Boston' })
  expect(buildings.rows[1]).toMatchObject({ key: 'payload-building', display: 'Payload building' })
  expect(buildings.rows[2]).toMatchObject({ key: 'empty-building', display: null })
  expect(buildings.columns.map((column: any) => column.format('x'))).toEqual(Array(5).fill('formatted:x'))
  const unitsBefore = mocks.grids['pm:properties:units']
  expect(unitsBefore.rows).toEqual([])
  expect(unitsBefore.columns.map((column: any) => column.format('x'))).toEqual(Array(2).fill('formatted:x'))

  await view.getByRole('button', { name: 'Select Buildings' }).click()
  await flushUi()
  expect(mocks.route.query.buildingId).toBe('building-1')
  expect(mocks.catalogPage).toHaveBeenCalledWith(
    'pm.property',
    expect.objectContaining({ filters: expect.objectContaining({ kind: 'Unit', parent_property_id: 'building-1' }) }),
    { signal: expect.any(AbortSignal) },
  )
  expect(mocks.buildingSummary).toHaveBeenCalledWith('building-1', {
    signal: expect.any(AbortSignal),
  })
  await expect.element(view.getByText('subtitle:Summary Building', { exact: true })).toBeVisible()
  expect(view.getByText('30%', { exact: true }).element()).toHaveTextContent('30%')

  const buildingsPanel = view.getByTestId('properties-buildings-panel')
  const unitsPanel = view.getByTestId('properties-units-panel')
  mocks.route.query.bLimit = '0'
  mocks.route.query.uLimit = '0'
  mocks.route.query.bOffset = '7'
  mocks.route.query.uOffset = '7'
  await flushUi()
  expect(mocks.grids['pm:properties:buildings'].rows).toHaveLength(50)
  expect(mocks.grids['pm:properties:units'].rows).toHaveLength(50)
  mocks.route.query.bOffset = '0'
  mocks.route.query.uOffset = '0'
  await flushUi()
  await buildingsPanel.getByTitle('Next').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await buildingsPanel.getByTitle('Previous').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await unitsPanel.getByTitle('Next').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await unitsPanel.getByTitle('Previous').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await flushUi()
  expect(mocks.replaceQuery).toHaveBeenCalledWith(mocks.route, mocks.router, { bOffset: 50 })
  expect(mocks.replaceQuery).toHaveBeenCalledWith(mocks.route, mocks.router, { bOffset: 0 })
  expect(mocks.replaceQuery).toHaveBeenCalledWith(mocks.route, mocks.router, { uOffset: 50 })
  expect(mocks.replaceQuery).toHaveBeenCalledWith(mocks.route, mocks.router, { uOffset: 0 })

  await buildingsPanel.getByText('Trash active').click()
  await unitsPanel.getByText('Trash active').click()
  await flushUi()
  expect(mocks.replaceQuery).toHaveBeenCalledWith(mocks.route, mocks.router, { bTrash: 'only', bOffset: 0 })
  expect(mocks.replaceQuery).toHaveBeenCalledWith(mocks.route, mocks.router, { uTrash: 'only', uOffset: 0 })
})

test('handles load failures, missing selection, summary fallbacks, and percentage boundaries', async () => {
  mocks.route.path = '/'
  mocks.route.query.buildingId = 'building-1'
  mocks.catalogPage
    .mockRejectedValueOnce(new Error('Buildings unavailable'))
    .mockRejectedValueOnce('units unavailable')
  mocks.buildingSummary.mockRejectedValueOnce('summary unavailable')
  const view = await renderPage()
  await expect.element(view.getByText('can-back:false')).toBeVisible()
  await expect.element(view.getByText('Failed to load units.')).toBeVisible()
  expect(view.getByTitle('Failed to load the building summary.').element()).toHaveAttribute('title', 'Failed to load the building summary.')

  await view.getByRole('button', { name: 'Clear Buildings' }).click()
  await flushUi()
  expect(mocks.route.query.buildingId).toBeUndefined()
  await expect.element(view.getByText('Select a building to see units')).toBeVisible()

  mocks.catalogPage.mockResolvedValueOnce({ items: [building('building-2', '   ')], offset: 0, limit: 50, total: null })
  mocks.route.query.buildingId = 'building-2'
  mocks.buildingSummary.mockResolvedValueOnce({ buildingDisplay: null, totalUnits: 4, occupiedUnits: 3, vacantUnits: 1, vacancyPercent: 12.345 })
  await flushUi()
  expect(view.getByText('12.35%', { exact: true }).element()).toHaveTextContent('12.35%')

  mocks.buildingSummary.mockResolvedValueOnce({ buildingDisplay: null, totalUnits: 0, occupiedUnits: 0, vacantUnits: 0, vacancyPercent: Number.POSITIVE_INFINITY })
  mocks.route.query.buildingId = 'building-3'
  await flushUi()
  expect(view.getByText('—', { exact: true }).element()).toHaveTextContent('—')
})

test('opens create and edit drawers, derives initial fields, and dispatches every editor action', async () => {
  const view = await renderPage()
  const buildingsPanel = view.getByTestId('properties-buildings-panel')
  const unitsPanel = view.getByTestId('properties-units-panel')

  await buildingsPanel.getByTitle('Create building').click()
  await flushUi()
  expect(mocks.openCreateDrawer).toHaveBeenCalled()
  expect(mocks.resetDrawerHeading).toHaveBeenCalledOnce()
  await expect.element(view.getByText('editor-initial:{"kind":"Building"}')).toBeVisible()
  await expect.element(view.getByText('editor-expand:/catalogs/pm.property')).toBeVisible()

  mocks.drawerState.editorFlags.value = { isDirty: true, loading: false, saving: false, extras: { bulkCreateUnits: true } }
  await flushUi()
  expect(await mocks.routeDrawerConfig.onBeforeOpen({ mode: 'edit' }, { mode: null })).toBe(true)
  expect(await mocks.routeDrawerConfig.onBeforeOpen({ mode: 'edit' }, { mode: 'edit' })).toBe(true)
  expect(mocks.requestDiscard).toHaveBeenCalledOnce()
  await expect.element(view.getByText('extras:1')).toBeVisible()

  for (const action of ['expand', 'share', 'audit', 'mark', 'bulkCreateUnits', 'save', 'unknown']) {
    await view.getByRole('button', { name: `Action ${action}` }).click()
  }
  expect(mocks.editorMethods.openFullPage).toHaveBeenCalledOnce()
  expect(mocks.editorMethods.copyShareLink).toHaveBeenCalledOnce()
  expect(mocks.editorMethods.openAuditLog).toHaveBeenCalledOnce()
  expect(mocks.editorMethods.toggleMarkForDeletion).toHaveBeenCalledOnce()
  expect(mocks.editorMethods.openBulkCreateUnitsWizard).toHaveBeenCalledOnce()
  expect(mocks.editorMethods.save).toHaveBeenCalledOnce()

  await view.getByRole('button', { name: 'Emit state' }).click()
  await view.getByRole('button', { name: 'Emit flags' }).click()
  await view.getByRole('button', { name: 'Discard cancel' }).click()
  await view.getByRole('button', { name: 'Discard confirm' }).click()
  expect(mocks.handleEditorState).toHaveBeenCalled()
  expect(mocks.handleEditorFlags).toHaveBeenCalled()
  expect(mocks.discardCancel).toHaveBeenCalledOnce()
  expect(mocks.discardConfirm).toHaveBeenCalledOnce()

  await view.getByRole('button', { name: 'Drawer keep open' }).click()
  expect(mocks.closeRouteDrawer).not.toHaveBeenCalled()
  await view.getByRole('button', { name: 'Drawer close' }).click()
  expect(mocks.closeRouteDrawer).toHaveBeenCalledOnce()

  await buildingsPanel.getByRole('button', { name: 'Clear Buildings' }).click()
  mocks.buildingSummary.mockResolvedValueOnce({ buildingDisplay: null, totalUnits: 0, occupiedUnits: 0, vacantUnits: 0, vacancyPercent: 0 })
  mocks.route.query.buildingId = 'orphan-building'
  mocks.route.query.newKind = 'Unit'
  mocks.routeDrawerState.panelMode.value = 'new'
  mocks.routeDrawerState.isPanelOpen.value = true
  await flushUi()
  await expect.element(view.getByText('editor-initial:{"kind":"Unit","parent_property_id":{"id":"orphan-building","display":"Building"}}')).toBeVisible()

  delete mocks.route.query.buildingId
  await flushUi()
  await expect.element(view.getByText('editor-initial:{"kind":"Unit"}')).toBeVisible()
  mocks.route.query.newKind = 'Unknown'
  await flushUi()
  await expect.element(view.getByText('editor-initial:null')).toBeVisible()
  mocks.routeDrawerState.panelMode.value = null
  await flushUi()
  await expect.element(view.getByText('editor-expand:none')).toBeVisible()
  mocks.routeDrawerState.panelMode.value = 'new'
  delete mocks.route.query.newKind
  await flushUi()
  await expect.element(view.getByText('editor-initial:null')).toBeVisible()

  mocks.route.query.buildingId = 'building-1'
  await flushUi()
  await unitsPanel.getByTitle('Create unit').click()
  await flushUi()
  await expect.element(view.getByText(/editor-initial:.*"kind":"Unit".*"parent_property_id"/)).toBeVisible()
  await view.getByRole('button', { name: 'Editor close' }).click()
  expect(mocks.closeRouteDrawer).toHaveBeenCalledTimes(2)
})

test('covers selection guards, direct edit flows, refreshes, bulk creation, and commit callback', async () => {
  const view = await renderPage()
  const buildingsPanel = view.getByTestId('properties-buildings-panel')
  const unitsPanel = view.getByTestId('properties-units-panel')

  for (const title of ['Edit', 'Bulk create units']) {
    buildingsPanel.getByTitle(title).element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  }
  unitsPanel.getByTitle('Create unit').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  unitsPanel.getByTitle('Edit').element().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await unitsPanel.getByRole('button', { name: 'Activate Units' }).click()

  await view.getByRole('button', { name: 'Select Buildings' }).click()
  await flushUi()
  await buildingsPanel.getByRole('button', { name: 'Select missing Buildings' }).click()
  await flushUi()
  expect(mocks.route.query.buildingId).toBe('missing')
  await buildingsPanel.getByRole('button', { name: 'Select Buildings' }).click()
  await flushUi()
  await buildingsPanel.getByTitle('Edit').click()
  await flushUi()
  expect(mocks.openEditDrawer).toHaveBeenCalledWith('building-1', expect.any(Object))
  await expect.element(view.getByText('editor-expand:/catalogs/pm.property/building-1')).toBeVisible()

  await unitsPanel.getByRole('button', { name: 'Select Units' }).click()
  await unitsPanel.getByTitle('Edit').click()
  await unitsPanel.getByRole('button', { name: 'Activate Units' }).click()
  await buildingsPanel.getByRole('button', { name: 'Activate Buildings' }).click()
  expect(mocks.openEditDrawer).toHaveBeenCalledWith('unit-1', expect.any(Object))

  await buildingsPanel.getByTitle('Bulk create units').click()
  await flushUi()
  await expect.element(view.getByText('bulk:true:building-1:Summary Building')).toBeVisible()
  await view.getByRole('button', { name: 'Bulk close' }).click()
  expect(mocks.bulkProps.open).toBe(false)
  const callsBeforeCreated = mocks.catalogPage.mock.calls.length
  await view.getByRole('button', { name: 'Bulk created' }).click()
  await flushUi()
  expect(mocks.catalogPage.mock.calls.length).toBeGreaterThan(callsBeforeCreated)

  await buildingsPanel.getByTitle('Refresh').click()
  await unitsPanel.getByTitle('Refresh').click()
  await flushUi()
  await mocks.commitConfig.reload()
  mocks.route.query.newKind = 'Building'
  await mocks.commitConfig.onCreated({ id: 'created-building' })
  expect(mocks.route.query.buildingId).toBe('created-building')
  mocks.route.query.newKind = 'Unit'
  await mocks.commitConfig.onCreated({ id: 'created-unit' })
  expect(mocks.route.query.buildingId).toBe('created-building')

  mocks.route.query.buildingId = 'payload-building'
  await buildingsPanel.getByTitle('Refresh').click()
  await flushUi()
  await expect.element(view.getByText('subtitle:Summary Building', { exact: true })).toBeVisible()
  mocks.route.query.buildingId = 'missing-building'
  await buildingsPanel.getByTitle('Refresh').click()
  await flushUi()
  mocks.route.query.buildingId = 'building-1'
  await flushUi()
  await unitsPanel.getByTitle('Clear selection').click()
  expect(mocks.route.query.buildingId).toBeUndefined()
})
