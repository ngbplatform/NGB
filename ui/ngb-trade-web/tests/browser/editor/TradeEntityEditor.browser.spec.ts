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
      props: {
        kind: { type: String, required: true },
        typeCode: { type: String, required: true },
        configuration: { type: Object, required: true },
      },
      setup(props) {
        mocks.configuration = props.configuration as Record<string, unknown>
        return () => h('div', {
          'data-testid': 'configured-editor',
          'data-kind': props.kind,
          'data-type-code': props.typeCode,
        })
      },
    }),
  }
})

vi.mock('../../../src/editor/TradeDocumentPartsEditor.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ name: 'TradeDocumentPartsEditor', setup: () => () => h('div') }) }
})

vi.mock('../../../src/editor/useCatalogEntityEditorPersistence', () => ({
  useCatalogEntityEditorPersistence: mocks.createCatalogPersistence,
}))

vi.mock('../../../src/editor/useDocumentEntityEditorPersistence', () => ({
  useDocumentEntityEditorPersistence: mocks.createDocumentPersistence,
}))

vi.mock('../../../src/metadata/framework', () => ({
  tradeMetadataFormBehavior: { vertical: 'trade' },
}))

import TradeEntityEditor from '../../../src/editor/TradeEntityEditor.vue'
import TradeDocumentPartsEditor from '../../../src/editor/TradeDocumentPartsEditor.vue'

test('is a Trade-only configuration shell over the platform editor host', async () => {
  const view = await render(TradeEntityEditor, {
    props: { kind: 'document', typeCode: 'trd.sales_invoice' },
  })

  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-kind', 'document')
  await expect.element(view.getByTestId('configured-editor')).toHaveAttribute('data-type-code', 'trd.sales_invoice')
  expect(mocks.configuration).toMatchObject({
    documentPartsExtensionKey: 'trade-document-parts',
    documentPartsEditor: TradeDocumentPartsEditor,
    metadataFormBehavior: { vertical: 'trade' },
    createCatalogPersistence: mocks.createCatalogPersistence,
    createDocumentPersistence: mocks.createDocumentPersistence,
  })
})
