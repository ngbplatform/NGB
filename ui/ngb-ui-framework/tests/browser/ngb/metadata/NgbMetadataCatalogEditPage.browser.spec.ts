import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, markRaw } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

const catalogEditMocks = vi.hoisted(() => ({
  buildCompactPageUrl: vi.fn((catalogType: string, id?: string | null) =>
    id ? `/catalogs-compact/${catalogType}/${id}` : `/catalogs-compact/${catalogType}/new`),
  buildListUrl: vi.fn((catalogType: string) => `/catalogs/${catalogType}`),
}))

vi.mock('../../../../src/ngb/editor/catalogNavigation', () => ({
  buildCatalogCompactPageUrl: catalogEditMocks.buildCompactPageUrl,
  buildCatalogListUrl: catalogEditMocks.buildListUrl,
}))

import NgbMetadataCatalogEditPage from '../../../../src/ngb/metadata/NgbMetadataCatalogEditPage.vue'

const CatalogEditorPageStub = defineComponent({
  props: {
    kind: {
      type: String,
      default: '',
    },
    typeCode: {
      type: String,
      default: '',
    },
    id: {
      type: String,
      default: undefined,
    },
    mode: {
      type: String,
      default: '',
    },
    canBack: {
      type: Boolean,
      default: false,
    },
    compactTo: {
      type: String,
      default: null,
    },
    closeTo: {
      type: String,
      default: null,
    },
  },
  setup(props) {
    return () => h('div', { 'data-testid': 'catalog-edit-editor' }, [
      h('div', `kind:${props.kind}`),
      h('div', `type:${props.typeCode}`),
      h('div', `id:${props.id ?? 'new'}`),
      h('div', `mode:${props.mode}`),
      h('div', `can-back:${String(props.canBack)}`),
      h('div', `compact:${props.compactTo ?? 'none'}`),
      h('div', `close:${props.closeTo ?? 'none'}`),
    ])
  },
})

async function renderCatalogEditPage(initialPath: string, props?: Record<string, unknown>) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/catalogs/:catalogType/:id?',
        component: {
          template: '<div />',
        },
      },
    ],
  })

  await router.push(initialPath)
  await router.isReady()

  const view = await render(NgbMetadataCatalogEditPage, {
    props: {
      editorComponent: markRaw(CatalogEditorPageStub),
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
})

test('normalizes catalog route params and falls back to default compact and close targets', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderCatalogEditPage('/catalogs/%20pm.property%20/%20prop-1%20')

  await expect.element(view.getByTestId('catalog-edit-editor')).toBeVisible()
  await expect.element(view.getByText('kind:catalog')).toBeVisible()
  await expect.element(view.getByText('type:pm.property')).toBeVisible()
  await expect.element(view.getByText('id:prop-1')).toBeVisible()
  await expect.element(view.getByText('mode:page')).toBeVisible()
  await expect.element(view.getByText('can-back:true')).toBeVisible()
  await expect.element(view.getByText('compact:/catalogs-compact/pm.property/prop-1')).toBeVisible()
  await expect.element(view.getByText('close:/catalogs/pm.property')).toBeVisible()

  expect(catalogEditMocks.buildCompactPageUrl).toHaveBeenCalledWith('pm.property', 'prop-1')
  expect(catalogEditMocks.buildListUrl).toHaveBeenCalledWith('pm.property')
})

test('uses resolver overrides and keeps id undefined for catalog create routes', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderCatalogEditPage('/catalogs/pm.property', {
    canBack: false,
    resolveCompactTo: (catalogType: string, id?: string | null) => `/custom-compact/${catalogType}/${id ?? 'draft'}`,
    resolveCloseTo: (catalogType: string) => `/custom-close/${catalogType}`,
  })

  await expect.element(view.getByText('type:pm.property')).toBeVisible()
  await expect.element(view.getByText('id:new')).toBeVisible()
  await expect.element(view.getByText('can-back:false')).toBeVisible()
  await expect.element(view.getByText('compact:/custom-compact/pm.property/draft')).toBeVisible()
  await expect.element(view.getByText('close:/custom-close/pm.property')).toBeVisible()

  expect(catalogEditMocks.buildCompactPageUrl).not.toHaveBeenCalled()
  expect(catalogEditMocks.buildListUrl).not.toHaveBeenCalled()
})

test('treats the full-page /new catalog route as a create page with an undefined id', async () => {
  await page.viewport(1280, 900)

  const { view } = await renderCatalogEditPage('/catalogs/pm.property/new')

  await expect.element(view.getByTestId('catalog-edit-editor')).toBeVisible()
  await expect.element(view.getByText('type:pm.property')).toBeVisible()
  await expect.element(view.getByText('id:new')).toBeVisible()
  await expect.element(view.getByText('compact:/catalogs-compact/pm.property/new')).toBeVisible()
  await expect.element(view.getByText('close:/catalogs/pm.property')).toBeVisible()

  expect(catalogEditMocks.buildCompactPageUrl).toHaveBeenCalledWith('pm.property', undefined)
  expect(catalogEditMocks.buildListUrl).toHaveBeenCalledWith('pm.property')
})
