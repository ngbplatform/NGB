import { defineComponent, h, nextTick, ref } from 'vue'
import { render } from 'vitest-browser-vue'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { EntityEditorHandle } from '@ngbplatform/ui'

const state = vi.hoisted(() => ({
  push: vi.fn(), logout: vi.fn(), save: vi.fn(), guard: vi.fn(), beforeEach: vi.fn(),
  openPalette: vi.fn(),
  lookup: {}, searchLookup: vi.fn(), lookupTarget: vi.fn(), normalizeLookup: vi.fn(),
  createRouter: vi.fn(), catalog: vi.fn(), document: vi.fn(), documentPersistence: vi.fn(),
  access: { isActive: true, current: { isBootstrapAdmin: true } as { isBootstrapAdmin: boolean } | null,
    hasPermission: vi.fn(() => false) },
  catalogProps: null as any,
  documentProps: null as any,
  shellProps: null as any,
  editorProps: null as any,
}))

vi.mock('@ngbplatform/ui/lazy', () => ({
  loadNgbAccountingPeriodClosingPage: vi.fn(),
  loadNgbDocumentEffectsPage: vi.fn(), loadNgbDocumentFlowPage: vi.fn(), loadNgbDocumentPrintPage: vi.fn(),
  loadNgbMetadataCatalogEditPage: vi.fn(), loadNgbMetadataDocumentEditPage: vi.fn(),
  loadNgbNotificationPreferencesPage: vi.fn(), loadNgbReportPage: vi.fn(), loadNgbWorkCenterPage: vi.fn(),
}))

vi.mock('vue-router', () => ({
  createWebHistory: vi.fn(),
  createRouter: state.createRouter.mockReturnValue({ beforeEach: state.beforeEach }),
  useRouter: () => ({ push: state.push }),
  useRoute: () => ({ path: '/catalogs/example.equipment' }),
}))
vi.mock('@ngbplatform/ui', () => ({
  useAuthStore: () => ({ userName: 'Example operator', logout: state.logout }),
  useAccessStore: () => state.access,
  useCommandPaletteStore: () => ({ open: state.openPalette }),
  useCommandPaletteHotkeys: vi.fn(),
  NgbCommandPaletteDialog: defineComponent({ render: () => null }),
  createAuthGuard: state.guard,
  getCatalogPage: state.catalog,
  getDocumentPage: state.document,
  createConfiguredCatalogEntityEditorPersistence: vi.fn(),
  createConfiguredDocumentEntityEditorPersistence: state.documentPersistence,
  NgbMetadataCatalogEditPage: {},
  NgbMetadataDocumentEditPage: {},
  NgbDocumentEffectsPage: {}, NgbDocumentFlowPage: {}, NgbDocumentPrintPage: {},
  NgbReportPage: {},
  NgbAccountingPeriodClosingPage: {},
  useLookupStore: () => state.lookup,
  searchResolvedLookupItems: state.searchLookup,
  buildLookupFieldTargetUrl: state.lookupTarget,
  normalizeLookupValue: state.normalizeLookup,
  NgbSiteShell: defineComponent({
    props: ['nodes'],
    emits: ['navigate', 'select', 'sign-out', 'open-palette'],
    setup(props, { emit, slots }) {
      state.shellProps = props
      return () => h('div', [
        h('button', { onClick: () => emit('navigate', '/documents/example.inspection') }, 'Navigate'),
        h('button', { onClick: () => emit('select', 'equipment', '/catalogs/example.equipment') }, 'Select'),
        h('button', { onClick: () => emit('sign-out') }, 'Sign out'),
        h('button', { onClick: () => emit('open-palette') }, 'Search'),
        slots.default?.(),
      ])
    },
  }),
  NgbConfiguredEntityEditor: defineComponent({
    props: ['kind', 'typeCode', 'configuration', 'navigateOnCreate', 'canBack'],
    setup(props, { expose }) {
      state.editorProps = props
      expose({ save: state.save })
      return () => h('span', `${props.kind}:${props.typeCode}`)
    },
  }),
  NgbMetadataCatalogListPage: defineComponent({
    props: ['editorComponent', 'loadPage'],
    setup(props) {
      state.catalogProps = props
      return () => h('div', 'Equipment list')
    },
  }),
  NgbMetadataDocumentListPage: defineComponent({
    props: ['editorComponent', 'loadPage'],
    setup(props) {
      state.documentProps = props
      return () => h('div', 'Inspection list')
    },
  }),
}))

beforeEach(() => {
  vi.clearAllMocks()
  vi.stubGlobal('__NGB_RUNTIME_CONFIG__', {
    VITE_KEYCLOAK_URL: 'http://127.0.0.1:18081',
    VITE_KEYCLOAK_REALM: 'component-tests',
    VITE_KEYCLOAK_CLIENT_ID: 'component-tests',
  })
  state.access.isActive = true
  state.access.current = { isBootstrapAdmin: true }
  state.access.hasPermission.mockReturnValue(false)
})
afterEach(() => {
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})

it('shows only authorized navigation and forwards shell actions', async () => {
  const { default: App } = await import('../../src/App.vue')
  const screen = render(App)
  expect(state.shellProps.nodes).toHaveLength(7)
  await screen.getByRole('button', { name: 'Navigate' }).click()
  await screen.getByRole('button', { name: 'Select' }).click()
  await screen.getByRole('button', { name: 'Sign out' }).click()
  await screen.getByRole('button', { name: 'Search' }).click()
  expect(state.push.mock.calls).toEqual([['/documents/example.inspection'], ['/catalogs/example.equipment']])
  expect(state.logout).toHaveBeenCalledOnce()
  expect(state.openPalette).toHaveBeenCalledOnce()
})

it.each([
  [false, null, false, 0],
  [true, null, false, 0],
  [true, { isBootstrapAdmin: false }, true, 7],
])('keeps inactive or denied navigation hidden (%s, %s, %s)', async (active, current, allowed, count) => {
  state.access.isActive = active
  state.access.current = current
  state.access.hasPermission.mockReturnValue(allowed)
  const { default: App } = await import('../../src/App.vue')
  render(App)
  expect(state.shellProps.nodes).toHaveLength(count)
  if (active) {
    expect(state.access.hasPermission.mock.calls).toEqual([
      [{ resourceKind: 'catalog', resourceCode: 'example.equipment', actionCode: 'view' }],
      [{ resourceKind: 'document', resourceCode: 'example.inspection', actionCode: 'view' }],
      [{ resourceKind: 'document', resourceCode: 'example.equipment-usage', actionCode: 'view' }],
      [{ resourceKind: 'document', resourceCode: 'example.equipment-condition', actionCode: 'view' }],
      [{ resourceKind: 'document', resourceCode: 'example.equipment-service', actionCode: 'view' }],
      [{ resourceKind: 'report', resourceCode: 'example.inspection-summary', actionCode: 'view' }],
      [{ resourceKind: 'admin', resourceCode: 'period_closing', actionCode: 'view' }],
    ])
  }
})

it('passes bounded list queries and cancellation to the public API', async () => {
  const { default: CatalogPage } = await import('../../src/pages/CatalogPage.vue')
  const { default: InspectionPage } = await import('../../src/pages/InspectionPage.vue')
  render(CatalogPage)
  render(InspectionPage)
  const signal = new AbortController().signal
  const common = { offset: 25, limit: 25, search: 'bench', trashMode: 'active', signal }
  await state.catalogProps.loadPage({ ...common, catalogType: 'example.equipment' })
  expect(state.catalog).toHaveBeenCalledWith('example.equipment', {
    offset: 25, limit: 25, search: 'bench', filters: { trash: 'active' },
  }, { signal })
  await state.documentProps.loadPage({ ...common, documentType: 'example.inspection',
    listFilters: { result: 'Passed' }, periodFrom: '2026-10-01', periodTo: '2026-10-31' })
  expect(state.document).toHaveBeenLastCalledWith('example.inspection', {
    offset: 25, limit: 25, search: 'bench',
    filters: { trash: 'active', result: 'Passed', periodFrom: '2026-10-01', periodTo: '2026-10-31' },
  }, { signal })
  await state.documentProps.loadPage({ ...common, documentType: 'example.inspection', listFilters: {} })
  expect(state.document.mock.lastCall![1].filters).toEqual({ trash: 'active' })
})

it('keeps the configured editor transparent to parent toolbar actions', async () => {
  const { default: EntityEditor } = await import('../../src/EntityEditor.vue')
  const editor = ref<EntityEditorHandle | null>(null)
  const wrapper = defineComponent({ setup: () => () => h(EntityEditor, {
    ref: editor, kind: 'document', typeCode: 'example.inspection',
  }) })
  const screen = render(wrapper)
  await nextTick()
  expect(screen.container.textContent).toContain('document:example.inspection')
  expect(state.editorProps.navigateOnCreate).toBeUndefined()
  expect(state.editorProps.canBack).toBeUndefined()
  await editor.value!.save()
  expect(state.save).toHaveBeenCalledOnce()
})

it('composes head-only documents without inventing tabular data', async () => {
  const { editorConfiguration } = await import('../../src/editor')
  editorConfiguration.createDocumentPersistence({} as never)
  const strategy = state.documentPersistence.mock.lastCall![1]
  expect(strategy.buildPayload()).toBeNull()
  strategy.hydrate()
  strategy.synchronize()
  const screen = render(editorConfiguration.documentPartsEditor as never)
  expect(screen.container.textContent).toBe('')
})

it('guards list and direct editor routes with the public authentication guard', async () => {
  await import('../../src/router')
  expect(state.createRouter.mock.lastCall![0].routes.map((route: { path: string }) => route.path)).toEqual([
    '/', '/catalogs/:catalogType', '/catalogs/:catalogType/:id', '/documents/:documentType', '/documents/:documentType/:id',
    '/documents/:documentType/:id/effects', '/documents/:documentType/:id/flow', '/documents/:documentType/:id/print',
    '/reports/:reportCode', '/admin/accounting/period-closing', '/work-center', '/settings/notifications', '/:pathMatch(.*)*',
  ])
  expect(state.guard.mock.calls[0][0]().userName).toBe('Example operator')
  expect(state.beforeEach).toHaveBeenCalledOnce()
})

it('searches and opens metadata references through the public lookup adapter', async () => {
  const { metadataFormBehavior } = await import('../../src/metadata')
  const hint = { kind: 'catalog' as const, catalogType: 'example.equipment' }
  const signal = new AbortController().signal
  const matches = [{ id: 'equipment-id', label: 'Inspection bench' }]
  state.searchLookup.mockResolvedValue(matches)
  expect(await metadataFormBehavior.searchLookup!({ hint, query: 'bench', signal })).toEqual(matches)
  expect(state.searchLookup).toHaveBeenCalledWith(state.lookup, hint, 'bench', { signal })
  state.normalizeLookup.mockReturnValue('equipment-id')
  state.lookupTarget.mockResolvedValue('/catalogs/example.equipment/equipment-id')
  expect(await metadataFormBehavior.buildLookupTargetUrl!({ hint, value: matches[0], routeFullPath: '/inspection' }))
    .toBe('/catalogs/example.equipment/equipment-id')
  expect(state.normalizeLookup).toHaveBeenCalledWith(matches[0])
  expect(state.lookupTarget).toHaveBeenCalledWith({ hint, value: 'equipment-id', route: { fullPath: '/inspection' } })
})

it('returns from an unknown page through the public layout action', async () => {
  const { default: NotFoundPage } = await import('../../src/pages/NotFoundPage.vue')
  const screen = render(NotFoundPage)
  await screen.getByRole('button', { name: 'Back', exact: true }).click()
  expect(state.push).toHaveBeenCalledWith('/catalogs/example.equipment')
})
