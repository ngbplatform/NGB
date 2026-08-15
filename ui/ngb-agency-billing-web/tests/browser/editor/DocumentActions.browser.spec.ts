import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  configuration: null as Record<string, unknown> | null,
  catalogAdapter: { kind: 'catalog-adapter' },
  documentAdapter: { kind: 'document-adapter' },
  createCatalogPersistence: vi.fn(),
  createDocumentPersistence: vi.fn(),
}))

vi.mock('@ngbplatform/ui/editor', async () => {
  const { defineComponent, h } = await import('vue')

  return {
    NgbConfiguredEntityEditor: defineComponent({
      name: 'NgbConfiguredEntityEditor',
      inheritAttrs: false,
      props: {
        kind: { type: String, required: true },
        typeCode: { type: String, required: true },
        id: { type: String, default: null },
        mode: { type: String, default: 'page' },
        canBack: { type: Boolean, default: true },
        configuration: { type: Object, required: true },
      },
      emits: ['created', 'saved'],
      setup(props, { attrs, emit }) {
        mocks.configuration = props.configuration as Record<string, unknown>
        return () => h('div', {
          ...attrs,
          'data-testid': 'configured-editor',
          'data-kind': props.kind,
          'data-type-code': props.typeCode,
          'data-id': props.id ?? '',
          'data-mode': props.mode,
          'data-can-back': String(props.canBack),
        }, [
          h('button', { 'data-testid': 'emit-created', onClick: () => emit('created', 'created-id') }, 'created'),
          h('button', { 'data-testid': 'emit-saved', onClick: () => emit('saved') }, 'saved'),
        ])
      },
    }),
  }
})

vi.mock('../../../src/editor/AgencyBillingDocumentPartsEditor.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ name: 'AgencyBillingDocumentPartsEditor', setup: () => () => h('div') }) }
})

vi.mock('../../../src/editor/useCatalogEntityEditorPersistence', () => ({
  useCatalogEntityEditorPersistence: mocks.createCatalogPersistence,
}))

vi.mock('../../../src/editor/useDocumentEntityEditorPersistence', () => ({
  useDocumentEntityEditorPersistence: mocks.createDocumentPersistence,
}))

vi.mock('../../../src/metadata/framework', () => ({
  agencyBillingMetadataFormBehavior: { vertical: 'agency-billing' },
}))

import AgencyBillingEntityEditor from '../../../src/editor/AgencyBillingEntityEditor.vue'
import AgencyBillingDocumentPartsEditor from '../../../src/editor/AgencyBillingDocumentPartsEditor.vue'

test('is a transparent Agency Billing configuration shell over the platform editor host', async () => {
  mocks.createCatalogPersistence.mockReset().mockReturnValue(mocks.catalogAdapter)
  mocks.createDocumentPersistence.mockReset().mockReturnValue(mocks.documentAdapter)
  const onCreated = vi.fn()
  const onSaved = vi.fn()

  const view = await render(AgencyBillingEntityEditor, {
    props: {
      kind: 'document',
      typeCode: 'ab.sales_invoice',
      id: 'invoice-id',
      mode: 'drawer',
      canBack: false,
      onCreated,
      onSaved,
    },
  })

  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-kind', 'document')
  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-type-code', 'ab.sales_invoice')
  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-id', 'invoice-id')
  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-mode', 'drawer')
  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-can-back', 'false')

  expect(mocks.configuration).toMatchObject({
    documentPartsExtensionKey: 'agency-billing-document-parts',
    documentPartsEditor: AgencyBillingDocumentPartsEditor,
    metadataFormBehavior: { vertical: 'agency-billing' },
    createCatalogPersistence: mocks.createCatalogPersistence,
    createDocumentPersistence: mocks.createDocumentPersistence,
  })

  const catalogContext = { kind: 'catalog-context' }
  const documentContext = { kind: 'document-context' }
  expect((mocks.configuration!.createCatalogPersistence as (context: unknown) => unknown)(catalogContext)).toBe(mocks.catalogAdapter)
  expect((mocks.configuration!.createDocumentPersistence as (context: unknown) => unknown)(documentContext)).toBe(mocks.documentAdapter)
  expect(mocks.createCatalogPersistence).toHaveBeenCalledWith(catalogContext)
  expect(mocks.createDocumentPersistence).toHaveBeenCalledWith(documentContext)

  await view.getByTestId('emit-created').click()
  await view.getByTestId('emit-saved').click()
  expect(onCreated).toHaveBeenCalledWith('created-id')
  expect(onSaved).toHaveBeenCalledOnce()
})
