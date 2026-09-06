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
import type { ColumnMetadata, FieldMetadata, LookupHint } from '../../../../src/ngb/metadata/types'
import { shortGuid } from '../../../../src/ngb/utils/guid'

const defaultLookupStore = mocks.editorConfig.lookupStore

function field(key: string, label: string, lookup?: FieldMetadata['lookup']): FieldMetadata {
  return {
    key,
    label,
    dataType: 'String',
    uiControl: 0,
    isRequired: false,
    isReadOnly: false,
    lookup,
  }
}

function column(key: string, label: string, dataType = 'String', lookup?: ColumnMetadata['lookup']): ColumnMetadata {
  return {
    key,
    label,
    dataType,
    isSortable: false,
    align: 0,
    lookup,
  }
}

describe('NgbDocumentPrintPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.editorConfig.lookupStore = defaultLookupStore
    for (const key of Object.keys(mocks.printBehavior))
      delete (mocks.printBehavior as Record<string, unknown>)[key]

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

  it('reports missing route parameters without calling backend services', async () => {
    mocks.route.params.documentType = undefined as unknown as string
    mocks.route.params.id = undefined as unknown as string
    mocks.route.query = { autoprint: ['0', '1'] } as unknown as { autoprint: string }

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByText('Document type or id is missing.')).toBeVisible()
    await view.getByRole('button', { name: 'Back' }).click()
    expect(mocks.metadataStore.ensureDocumentType).not.toHaveBeenCalled()
    expect(mocks.editorConfig.loadDocumentById).not.toHaveBeenCalled()
  })

  it('unwraps a compact source from a nested print back trail', async () => {
    const compactSource = '/documents/pm.invoice?panel=edit&id=doc-1&search=late'
    const nestedDocumentRoute = withBackTarget('/documents/pm.invoice/doc-1', compactSource)
    mocks.route.query = { back: encodeBackTarget(nestedDocumentRoute) }

    const view = await render(NgbDocumentPrintPage)
    await expect.element(view.getByText('Customer Invoice INV-001')).toBeVisible()
    await view.getByRole('button', { name: 'Back' }).click()

    expect(mocks.router.replace).toHaveBeenCalledWith(compactSource)
  })

  it('renders an empty printable document when optional form, parts, and payload collections are absent', async () => {
    mocks.route.query = {}
    mocks.metadataStore.ensureDocumentType.mockResolvedValueOnce({
      documentType: 'pm.invoice',
      displayName: 'Empty invoice',
      kind: 2,
      form: null,
      parts: null,
    })
    mocks.editorConfig.loadDocumentById.mockResolvedValueOnce({
      id: 'doc-1',
      display: null,
      number: null,
      status: 1,
      payload: {
        fields: null,
        parts: null,
      },
    })

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByRole('heading', { name: 'Empty invoice' })).toBeVisible()
    expect(document.querySelectorAll('.document-print-section')).toHaveLength(0)
  })

  it('prints boundary metadata safely without a lookup store', async () => {
    mocks.route.query = { autoprint: ['0', '1'] } as unknown as { autoprint: string }
    mocks.editorConfig.lookupStore = null as unknown as typeof defaultLookupStore
    mocks.metadataStore.ensureDocumentType.mockResolvedValueOnce({
      documentType: 'pm.invoice',
      displayName: 'Invoice',
      kind: 2,
      form: {
        sections: [
          {
            title: '',
            rows: [
              {
                fields: [
                  field('display', 'Hidden display'),
                  field('number', 'Hidden number'),
                  field('customer_id', 'Customer', { kind: 'catalog', catalogType: 'crm.counterparty' }),
                  field('shown_reference', 'Shown reference', { kind: 'catalog', catalogType: 'crm.counterparty' }),
                ],
              },
              { fields: [] },
            ],
          },
        ],
      },
      parts: [
        {
          partCode: 'lines',
          title: '',
          list: {
            columns: [
              column('money', 'Money', 'Money'),
              column('decimal', 'Decimal', 'Decimal'),
              column('quantity', 'Quantity', 'Int32'),
              column('description', 'Description'),
            ],
          },
        },
        {
          partCode: 'empty-columns',
          title: 'Empty columns',
          list: { columns: [] },
        },
        {
          partCode: 'missing-rows',
          title: 'Missing rows',
          list: { columns: [column('value', 'Value')] },
        },
      ],
    })
    mocks.editorConfig.loadDocumentById.mockResolvedValueOnce({
      id: 'doc-1',
      display: 'Boundary printable invoice',
      number: '',
      status: 3,
      payload: {
        fields: {
          display: 'must stay hidden',
          number: 'must stay hidden',
          customer_id: '22222222-2222-2222-2222-222222222222',
          shown_reference: { id: '33333333-3333-3333-3333-333333333333', display: 'Embedded customer' },
        },
        parts: {
          lines: {
            rows: [
              { money: 12.5, decimal: 2.25, quantity: 3, description: 'Boundary line' },
            ],
          },
          'empty-columns': { rows: [{}] },
        },
      },
    })

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByText('Boundary printable invoice')).toBeVisible()
    await expect.element(view.getByText('22222222-2222-2222-2222-222222222222')).toBeVisible()
    await expect.element(view.getByText('Embedded customer')).toBeVisible()
    await expect.element(view.getByText('Boundary line')).toBeVisible()
    expect(document.querySelector('.print-status.deleted')).not.toBeNull()
    expect(mocks.editorConfig.lookupStore).toBeNull()
  })

  it('prefetches and formats catalog, chart-of-accounts, and document lookups including duplicate and invalid values', async () => {
    const catalogId = '44444444-4444-4444-4444-444444444444'
    const coaId = '55555555-5555-5555-5555-555555555555'
    const documentId = '66666666-6666-6666-6666-666666666666'
    const overrideHint: LookupHint = { kind: 'coa' }
    ;(mocks.printBehavior as { resolveLookupHint?: (context: { fieldKey: string }) => LookupHint | null }).resolveLookupHint =
      ({ fieldKey }) => fieldKey === 'override_id' ? overrideHint : null

    mocks.editorConfig.lookupStore.labelForCatalog.mockReturnValue('Catalog label')
    mocks.editorConfig.lookupStore.labelForCoa.mockReturnValue('Account label')
    mocks.editorConfig.lookupStore.labelForAnyDocument.mockReturnValue('Document label')
    mocks.metadataStore.ensureDocumentType.mockResolvedValueOnce({
      documentType: 'pm.invoice',
      displayName: '',
      kind: 2,
      form: {
        sections: [
          {
            title: 'Lookups',
            rows: [
              {
                fields: [
                  field('catalog_a', 'Catalog A', { kind: 'catalog', catalogType: 'crm.counterparty' }),
                  field('catalog_b', 'Catalog B', { kind: 'catalog', catalogType: 'crm.counterparty' }),
                  field('coa_id', 'Account', { kind: 'coa' }),
                  field('document_a', 'Document A', { kind: 'document', documentTypes: ['pm.invoice'] }),
                  field('document_b', 'Document B', { kind: 'document', documentTypes: ['pm.invoice'] }),
                  field('override_id', 'Overridden lookup'),
                  field('invalid_id', 'Invalid lookup', { kind: 'catalog', catalogType: 'crm.counterparty' }),
                  field('empty_reference', 'Empty reference', { kind: 'catalog', catalogType: 'crm.counterparty' }),
                  field('shown_reference', 'Shown reference', { kind: 'catalog', catalogType: 'crm.counterparty' }),
                ],
              },
            ],
          },
        ],
      },
      parts: [
        {
          partCode: 'lookup_lines',
          title: 'Lookup lines',
          list: {
            columns: [
              column('account_id', 'Line account', 'String', { kind: 'coa' }),
              column('document_id', 'Line document', 'String', { kind: 'document', documentTypes: ['pm.invoice'] }),
            ],
          },
        },
        {
          partCode: 'missing_lookup_lines',
          title: 'Missing lookup lines',
          list: { columns: [column('account_id', 'Account', 'String', { kind: 'coa' })] },
        },
      ],
    })
    mocks.editorConfig.loadDocumentById.mockResolvedValueOnce({
      id: 'doc-1',
      display: null,
      number: null,
      status: 0,
      payload: {
        fields: {
          catalog_a: catalogId,
          catalog_b: catalogId,
          coa_id: coaId,
          document_a: documentId,
          document_b: documentId,
          override_id: coaId,
          invalid_id: 'not-a-guid',
          empty_reference: { id: catalogId, display: '' },
          shown_reference: { id: catalogId, display: 'Embedded lookup label' },
        },
        parts: {
          lookup_lines: {
            rows: [
              { account_id: coaId, document_id: documentId },
            ],
          },
        },
      },
    })

    const view = await render(NgbDocumentPrintPage)

    await expect.element(view.getByRole('heading', { name: 'Document', exact: true })).toBeVisible()
    await expect.element(view.getByText('Catalog label').first()).toBeVisible()
    await expect.element(view.getByText('Account label').first()).toBeVisible()
    await expect.element(view.getByText('Document label').first()).toBeVisible()
    await expect.element(view.getByText('not-a-guid')).toBeVisible()
    await expect.element(view.getByText('Embedded lookup label')).toBeVisible()
    expect(document.querySelector('.print-status.draft')).not.toBeNull()
    expect(mocks.editorConfig.lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('crm.counterparty', [catalogId])
    expect(mocks.editorConfig.lookupStore.ensureCoaLabels).toHaveBeenCalledWith([coaId])
    expect(mocks.editorConfig.lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledWith(['pm.invoice'], [documentId])
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

  it('ignores successful and failed document loads that settle after unmount', async () => {
    let resolveDocument!: (value: unknown) => void
    mocks.editorConfig.loadDocumentById.mockReturnValueOnce(new Promise((resolve) => {
      resolveDocument = resolve
    }))
    const successful = await render(NgbDocumentPrintPage)
    await vi.waitFor(() => expect(mocks.editorConfig.loadDocumentById).toHaveBeenCalledOnce())
    successful.unmount()
    resolveDocument({ id: 'late-doc', payload: null })
    await Promise.resolve()

    mocks.editorConfig.loadDocumentById.mockReset()
    let rejectDocument!: (cause: unknown) => void
    mocks.editorConfig.loadDocumentById.mockReturnValueOnce(new Promise((_resolve, reject) => {
      rejectDocument = reject
    }))
    const failed = await render(NgbDocumentPrintPage)
    await vi.waitFor(() => expect(mocks.editorConfig.loadDocumentById).toHaveBeenCalledOnce())
    failed.unmount()
    rejectDocument(new Error('late document failure'))
    await Promise.resolve()
  })
})
