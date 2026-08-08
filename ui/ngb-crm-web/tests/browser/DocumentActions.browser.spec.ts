import { expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

const mocks = vi.hoisted(() => ({
  configuration: null as Record<string, unknown> | null,
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
        configuration: { type: Object, required: true },
      },
      setup(props, { attrs }) {
        mocks.configuration = props.configuration as Record<string, unknown>
        return () => h('div', {
          ...attrs,
          'data-testid': 'configured-editor',
          'data-kind': props.kind,
          'data-type-code': props.typeCode,
          'data-id': props.id ?? '',
        })
      },
    }),
  }
})

vi.mock('../../src/editor/CRMDocumentPartsEditor.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ name: 'CRMDocumentPartsEditor', setup: () => () => h('div') }) }
})

vi.mock('../../src/editor/useCatalogEntityEditorPersistence', () => ({
  useCatalogEntityEditorPersistence: mocks.createCatalogPersistence,
}))

vi.mock('../../src/editor/useDocumentEntityEditorPersistence', () => ({
  useDocumentEntityEditorPersistence: mocks.createDocumentPersistence,
}))

vi.mock('../../src/metadata/framework', () => ({
  crmMetadataFormBehavior: { vertical: 'crm' },
}))

import CRMEntityEditor from '../../src/editor/CRMEntityEditor.vue'
import CRMDocumentPartsEditor from '../../src/editor/CRMDocumentPartsEditor.vue'

test('is a CRM-only configuration shell over the platform editor host', async () => {
  const view = await render(CRMEntityEditor, {
    props: { kind: 'document', typeCode: 'crm.lead_intake', id: 'lead-id' },
  })

  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-kind', 'document')
  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-type-code', 'crm.lead_intake')
  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-id', 'lead-id')
  expect(mocks.configuration).toMatchObject({
    documentPartsExtensionKey: 'crm-document-parts',
    documentPartsEditor: CRMDocumentPartsEditor,
    metadataFormBehavior: { vertical: 'crm' },
    createCatalogPersistence: mocks.createCatalogPersistence,
    createDocumentPersistence: mocks.createDocumentPersistence,
  })
})
