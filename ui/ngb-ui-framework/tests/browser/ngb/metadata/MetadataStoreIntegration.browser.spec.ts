import { page } from 'vitest/browser'
import { beforeEach, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { createPinia } from 'pinia'
import { defineComponent, h, ref } from 'vue'

type MetadataModule = typeof import('../../../../src/ngb/metadata/config')
type MetadataStoreModule = typeof import('../../../../src/ngb/metadata/store')

function catalogMetadata(displayName: string) {
  return {
    catalogType: 'pm.property',
    displayName,
    kind: 1,
    list: {
      columns: [
        { key: 'name', label: 'Name', dataType: 1, isSortable: true, align: 1 },
      ],
    },
    form: null,
    parts: null,
  }
}

function documentMetadata(displayName: string) {
  return {
    documentType: 'pm.invoice',
    displayName,
    kind: 2,
    list: {
      columns: [
        { key: 'posted_at', label: 'Posted At', dataType: 6, isSortable: true, align: 1 },
      ],
    },
    form: null,
    parts: null,
  }
}

async function renderMetadataStoreHarness(options?: {
  configure?: boolean
  loadCatalogTypeMetadata?: ReturnType<typeof vi.fn>
  loadDocumentTypeMetadata?: ReturnType<typeof vi.fn>
}) {
  vi.resetModules()

  const config = await import('../../../../src/ngb/metadata/config') as MetadataModule
  const storeModule = await import('../../../../src/ngb/metadata/store') as MetadataStoreModule

  const loadCatalogTypeMetadata = options?.loadCatalogTypeMetadata ?? vi.fn().mockResolvedValue(catalogMetadata('Properties'))
  const loadDocumentTypeMetadata = options?.loadDocumentTypeMetadata ?? vi.fn().mockResolvedValue(documentMetadata('Invoices'))

  if (options?.configure !== false) {
    config.configureNgbMetadata({
      loadCatalogTypeMetadata,
      loadDocumentTypeMetadata,
      formBehavior: {
        findDisplayField: () => ({
          key: 'display_name',
          label: 'Display Name',
          dataType: 'String',
          uiControl: 1,
          isRequired: false,
          isReadOnly: false,
          lookup: null,
          validation: null,
          helpText: null,
        }),
        isFieldReadonly: () => true,
      },
    })
  }

  const Harness = defineComponent({
    setup() {
      const store = storeModule.useMetadataStore()
      const catalogState = ref('idle')
      const documentState = ref('idle')
      const behaviorState = ref('idle')
      const errorState = ref('none')

      async function loadCatalog() {
        try {
          const metadata = await store.ensureCatalogType('pm.property')
          const cached = await store.ensureCatalogType('pm.property')
          catalogState.value = `${metadata.displayName}|${String(metadata.list?.columns[0]?.dataType)}|same:${String(cached === metadata)}`
        } catch (cause) {
          errorState.value = cause instanceof Error ? cause.message : String(cause)
        }
      }

      async function loadDocument() {
        try {
          const metadata = await store.ensureDocumentType('pm.invoice')
          const cached = await store.ensureDocumentType('pm.invoice')
          documentState.value = `${metadata.displayName}|${String(metadata.list?.columns[0]?.dataType)}|same:${String(cached === metadata)}`
        } catch (cause) {
          errorState.value = cause instanceof Error ? cause.message : String(cause)
        }
      }

      function resolveBehavior() {
        const behavior = config.resolveNgbMetadataFormBehavior({
          isFieldReadonly: () => false,
          isFieldHidden: () => true,
        })

        behaviorState.value = [
          `display:${behavior.findDisplayField?.({ sections: [] })?.key ?? 'none'}`,
          `readonly:${String(behavior.isFieldReadonly?.({
            entityTypeCode: 'pm.invoice',
            model: {},
            field: {
              key: 'status',
              label: 'Status',
              dataType: 'String',
              uiControl: 1,
              isRequired: false,
              isReadOnly: false,
              lookup: null,
              validation: null,
              helpText: null,
            },
            status: 1,
            forceReadonly: false,
          }))}`,
          `hidden:${String(behavior.isFieldHidden?.({
            entityTypeCode: 'pm.invoice',
            model: {},
            field: {
              key: 'internal_note',
              label: 'Internal Note',
              dataType: 'String',
              uiControl: 1,
              isRequired: false,
              isReadOnly: false,
              lookup: null,
              validation: null,
              helpText: null,
            },
            isDocumentEntity: true,
          }))}`,
        ].join('|')
      }

      function clearStore() {
        store.clear()
      }

      return () => h('div', { 'data-testid': 'metadata-store-harness' }, [
        h('div', { 'data-testid': 'catalog-state' }, catalogState.value),
        h('div', { 'data-testid': 'document-state' }, documentState.value),
        h('div', { 'data-testid': 'behavior-state' }, behaviorState.value),
        h('div', { 'data-testid': 'error-state' }, errorState.value),
        h('div', { 'data-testid': 'catalog-keys' }, Object.keys(store.catalogs).join('|') || 'none'),
        h('div', { 'data-testid': 'document-keys' }, Object.keys(store.documents).join('|') || 'none'),
        h('button', { type: 'button', onClick: loadCatalog }, 'Load catalog metadata'),
        h('button', { type: 'button', onClick: loadDocument }, 'Load document metadata'),
        h('button', { type: 'button', onClick: resolveBehavior }, 'Resolve form behavior'),
        h('button', { type: 'button', onClick: clearStore }, 'Clear metadata store'),
      ])
    },
  })

  const view = await render(Harness, {
    global: {
      plugins: [createPinia()],
    },
  })

  return {
    view,
    loadCatalogTypeMetadata,
    loadDocumentTypeMetadata,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
})

test('loads configured metadata through the browser store, normalizes values, caches repeated lookups, and merges form behavior overrides', async () => {
  await page.viewport(1280, 900)

  const { view, loadCatalogTypeMetadata, loadDocumentTypeMetadata } = await renderMetadataStoreHarness()

  await view.getByRole('button', { name: 'Resolve form behavior' }).click()
  await view.getByRole('button', { name: 'Load catalog metadata' }).click()
  await view.getByRole('button', { name: 'Load document metadata' }).click()

  await expect.element(view.getByTestId('behavior-state')).toHaveTextContent(
    'display:display_name|readonly:false|hidden:true',
  )
  await expect.element(view.getByTestId('catalog-state')).toHaveTextContent(
    'Properties|String|same:true',
  )
  await expect.element(view.getByTestId('document-state')).toHaveTextContent(
    'Invoices|DateTime|same:true',
  )
  await expect.element(view.getByTestId('catalog-keys')).toHaveTextContent('pm.property')
  await expect.element(view.getByTestId('document-keys')).toHaveTextContent('pm.invoice')

  expect(loadCatalogTypeMetadata).toHaveBeenCalledTimes(1)
  expect(loadDocumentTypeMetadata).toHaveBeenCalledTimes(1)
})

test('clears cached metadata in the browser store and reloads fresh configured values', async () => {
  await page.viewport(1280, 900)

  const loadCatalogTypeMetadata = vi.fn()
    .mockResolvedValueOnce(catalogMetadata('Properties'))
    .mockResolvedValueOnce(catalogMetadata('Properties v2'))

  const { view } = await renderMetadataStoreHarness({
    loadCatalogTypeMetadata,
  })

  await view.getByRole('button', { name: 'Load catalog metadata' }).click()
  await expect.element(view.getByTestId('catalog-state')).toHaveTextContent('Properties|String|same:true')

  await view.getByRole('button', { name: 'Clear metadata store' }).click()
  await expect.element(view.getByTestId('catalog-keys')).toHaveTextContent('none')
  await expect.element(view.getByTestId('document-keys')).toHaveTextContent('none')

  await view.getByRole('button', { name: 'Load catalog metadata' }).click()
  await expect.element(view.getByTestId('catalog-state')).toHaveTextContent('Properties v2|String|same:true')
  expect(loadCatalogTypeMetadata).toHaveBeenCalledTimes(2)
})

test('clears and reloads document metadata independently from the catalog cache', async () => {
  await page.viewport(1280, 900)

  const loadDocumentTypeMetadata = vi.fn()
    .mockResolvedValueOnce(documentMetadata('Invoices'))
    .mockResolvedValueOnce(documentMetadata('Invoices v2'))

  const { view } = await renderMetadataStoreHarness({
    loadDocumentTypeMetadata,
  })

  await view.getByRole('button', { name: 'Load document metadata' }).click()
  await expect.element(view.getByTestId('document-state')).toHaveTextContent('Invoices|DateTime|same:true')
  await expect.element(view.getByTestId('catalog-keys')).toHaveTextContent('none')
  await expect.element(view.getByTestId('document-keys')).toHaveTextContent('pm.invoice')

  await view.getByRole('button', { name: 'Clear metadata store' }).click()
  await expect.element(view.getByTestId('catalog-keys')).toHaveTextContent('none')
  await expect.element(view.getByTestId('document-keys')).toHaveTextContent('none')

  await view.getByRole('button', { name: 'Load document metadata' }).click()
  await expect.element(view.getByTestId('document-state')).toHaveTextContent('Invoices v2|DateTime|same:true')
  expect(loadDocumentTypeMetadata).toHaveBeenCalledTimes(2)
})
