import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-vue'

import { StubIcon } from './stubs'

const mocks = vi.hoisted(() => ({
  route: {
    params: {
      documentType: 'pm.invoice',
      id: 'doc-1',
    },
    query: {
      autoprint: '1',
    },
    fullPath: '/documents/pm.invoice/doc-1/print?autoprint=1',
  },
  router: {
    push: vi.fn(),
    replace: vi.fn(),
    back: vi.fn(),
  },
  metadataStore: {
    ensureDocumentType: vi.fn(),
  },
  editorConfig: {
    lookupStore: {
      searchCatalog: vi.fn(),
      searchCoa: vi.fn(),
      searchDocuments: vi.fn(),
      ensureCatalogLabels: vi.fn(),
      ensureCoaLabels: vi.fn(),
      ensureAnyDocumentLabels: vi.fn(),
      labelForCatalog: vi.fn(),
      labelForCoa: vi.fn(),
      labelForAnyDocument: vi.fn(),
    },
    loadDocumentById: vi.fn(),
  },
  printBehavior: {},
}))

vi.mock('vue-router', () => ({
  useRoute: () => mocks.route,
  useRouter: () => mocks.router,
}))

vi.mock('../../../../src/ngb/metadata/store', () => ({
  useMetadataStore: () => mocks.metadataStore,
}))

vi.mock('../../../../src/ngb/editor/config', async () => {
  const actual = await vi.importActual('../../../../src/ngb/editor/config')
  return {
    ...actual,
    getConfiguredNgbEditor: () => mocks.editorConfig,
    resolveNgbEditorPrintBehavior: () => mocks.printBehavior,
  }
})

vi.mock('../../../../src/ngb/primitives/NgbIcon.vue', () => ({
  default: StubIcon,
}))

import NgbDocumentPrintPage from '../../../../src/ngb/editor/NgbDocumentPrintPage.vue'
import { encodeBackTarget, withBackTarget } from '../../../../src/ngb/router/backNavigation'
import { shortGuid } from '../../../../src/ngb/utils/guid'

describe('NgbDocumentPrintPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    mocks.route.params.documentType = 'pm.invoice'
    mocks.route.params.id = 'doc-1'
    mocks.route.query = {
      autoprint: '1',
    }
    mocks.route.fullPath = '/documents/pm.invoice/doc-1/print?autoprint=1'

    mocks.metadataStore.ensureDocumentType.mockResolvedValue({
      documentType: 'pm.invoice',
      displayName: 'Customer Invoice',
      kind: 2,
      form: {
        sections: [
          {
            title: 'Main',
            rows: [
              {
                fields: [
                  {
                    key: 'customer_id',
                    label: 'Customer',
                    dataType: 'String',
                    uiControl: 0,
                    isRequired: false,
                    isReadOnly: false,
                    lookup: {
                      kind: 'catalog',
                      catalogType: 'crm.counterparty',
                    },
                  },
                  {
                    key: 'memo',
                    label: 'Memo',
                    dataType: 'String',
                    uiControl: 0,
                    isRequired: false,
                    isReadOnly: false,
                  },
                ],
              },
            ],
          },
        ],
      },
      parts: [
        {
          partCode: 'lines',
          title: 'Lines',
          list: {
            columns: [
              {
                key: 'description',
                label: 'Description',
                dataType: 'String',
                isSortable: false,
                align: 0,
              },
              {
                key: 'amount',
                label: 'Amount',
                dataType: 'Decimal',
                isSortable: false,
                align: 1,
              },
            ],
          },
        },
      ],
    })
    mocks.editorConfig.loadDocumentById.mockResolvedValue({
      id: 'doc-1',
      number: 'INV-001',
      status: 2,
      payload: {
        fields: {
          customer_id: '11111111-1111-1111-1111-111111111111',
          memo: 'April recurring rent',
        },
        parts: {
          lines: {
            rows: [
              {
                description: 'Base rent',
                amount: 1250,
              },
            ],
          },
        },
      },
    })
    mocks.editorConfig.lookupStore.ensureCatalogLabels.mockResolvedValue(undefined)
    mocks.editorConfig.lookupStore.ensureCoaLabels.mockResolvedValue(undefined)
    mocks.editorConfig.lookupStore.ensureAnyDocumentLabels.mockResolvedValue(undefined)
    mocks.editorConfig.lookupStore.labelForCatalog.mockReturnValue('Riverfront Tower')
    mocks.editorConfig.lookupStore.labelForCoa.mockImplementation((id: unknown) => String(id ?? ''))
    mocks.editorConfig.lookupStore.labelForAnyDocument.mockImplementation((_: string[], id: unknown) => String(id ?? ''))
  })

  it('renders printable sections, prefetches lookup labels, and auto-prints once after load', async () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => {})

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByText('Customer Invoice INV-001')).toBeVisible()
    await expect.element(view.getByText('Riverfront Tower')).toBeVisible()
    await expect.element(view.getByText('April recurring rent')).toBeVisible()
    await expect.element(view.getByText('Base rent')).toBeVisible()
    await expect.element(view.getByText('1,250')).toBeVisible()

    await vi.waitFor(() => {
      expect(printSpy).toHaveBeenCalledTimes(1)
    })
    expect(mocks.editorConfig.lookupStore.ensureCatalogLabels).toHaveBeenCalledWith(
      'crm.counterparty',
      ['11111111-1111-1111-1111-111111111111'],
    )

    await view.getByRole('button', { name: 'Print' }).click()
    expect(printSpy).toHaveBeenCalledTimes(2)
  })

  it('shows an error state when the print preview cannot be loaded', async () => {
    mocks.editorConfig.loadDocumentById.mockRejectedValueOnce(new Error('boom'))

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByText('boom')).toBeVisible()
  })

  it('keeps the print preview visible when lookup label prefetch fails and falls back to unresolved lookup labels', async () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => {})
    const customerId = '11111111-1111-1111-1111-111111111111'

    mocks.editorConfig.lookupStore.ensureCatalogLabels.mockRejectedValueOnce(new Error('Catalog labels offline'))
    mocks.editorConfig.lookupStore.labelForCatalog.mockImplementation((_: unknown, id: unknown) => shortGuid(String(id ?? '')))

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByText('Customer Invoice INV-001')).toBeVisible()
    await expect.element(view.getByText(shortGuid(customerId))).toBeVisible()
    await expect.element(view.getByText('April recurring rent')).toBeVisible()
    expect(document.body.textContent).not.toContain('Catalog labels offline')

    await vi.waitFor(() => {
      expect(printSpy).toHaveBeenCalledTimes(1)
    })
    expect(mocks.editorConfig.lookupStore.ensureCatalogLabels).toHaveBeenCalledWith(
      'crm.counterparty',
      [customerId],
    )
  })

  it('updates the document title around print lifecycle events and uses the explicit back target from the toolbar', async () => {
    mocks.route.query = {
      back: encodeBackTarget('/reports/pm.occupancy.summary'),
    }
    mocks.route.fullPath = '/documents/pm.invoice/doc-1/print?back=encoded'

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByText('Customer Invoice INV-001')).toBeVisible()
    expect(document.title).toBe('Customer Invoice INV-001')

    window.dispatchEvent(new Event('beforeprint'))
    expect(document.title).toBe('')

    window.dispatchEvent(new Event('afterprint'))
    expect(document.title).toBe('Customer Invoice INV-001')

    await view.getByRole('button', { name: 'Back' }).click()

    expect(mocks.router.replace).toHaveBeenCalledWith('/reports/pm.occupancy.summary')
    expect(mocks.router.back).not.toHaveBeenCalled()
  })

  it('returns to the source document page while preserving the outer report back trail', async () => {
    const reportBackTarget = '/reports/pm.occupancy.summary?variant=audit-view'
    const nestedDocumentRoute = withBackTarget('/documents/pm.invoice/doc-1', reportBackTarget)

    mocks.route.query = {
      back: encodeBackTarget(nestedDocumentRoute),
    }
    mocks.route.fullPath = '/documents/pm.invoice/doc-1/print?back=encoded'

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByText('Customer Invoice INV-001')).toBeVisible()
    await view.getByRole('button', { name: 'Back' }).click()

    expect(mocks.router.replace).toHaveBeenCalledWith(nestedDocumentRoute)
    expect(mocks.router.back).not.toHaveBeenCalled()
  })

  it('removes print lifecycle listeners when the print page unmounts', async () => {
    mocks.route.query = {}
    mocks.route.fullPath = '/documents/pm.invoice/doc-1/print'

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByText('Customer Invoice INV-001')).toBeVisible()

    view.unmount()
    document.title = 'Stable title'

    window.dispatchEvent(new Event('beforeprint'))
    expect(document.title).toBe('Stable title')

    window.dispatchEvent(new Event('afterprint'))
    expect(document.title).toBe('Stable title')
  })
})
