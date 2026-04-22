import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildGeneralJournalEntriesPath: vi.fn((id?: string | null) => `/accounting/general-journal-entries/${id ?? 'new'}`),
  getDocumentById: vi.fn(),
  getDocumentEffects: vi.fn(),
  getDocumentGraph: vi.fn(),
  getEntityAuditLog: vi.fn(),
  isEmptyGuid: vi.fn((value: string) => value === '00000000-0000-0000-0000-000000000000'),
  isGeneralJournalEntryDocumentType: vi.fn((documentType: string) => documentType === 'accounting.general_journal_entry'),
  isNonEmptyGuid: vi.fn((value: string) => /^[0-9a-f-]{36}$/i.test(String(value).trim())),
  shortGuid: vi.fn((value: string) => `short:${value.slice(0, 8)}`),
  lookupStore: {
    ensureAnyDocumentLabels: vi.fn(async () => undefined),
    labelForAnyDocument: vi.fn((_: string[], id: string) => {
      if (id === '11111111-1111-4111-8111-111111111111') return 'Sales Invoice SI-100'
      if (id === '22222222-2222-4222-8222-222222222222') return '33333333-3333-4333-8333-333333333333'
      return ''
    }),
  },
  resolveTradeEditorEntityProfile: vi.fn(() => ({ title: 'Resolved Profile' })),
}))

vi.mock('ngb-ui-framework', () => ({
  buildGeneralJournalEntriesPath: mocks.buildGeneralJournalEntriesPath,
  getDocumentById: mocks.getDocumentById,
  getDocumentEffects: mocks.getDocumentEffects,
  getDocumentGraph: mocks.getDocumentGraph,
  getEntityAuditLog: mocks.getEntityAuditLog,
  isEmptyGuid: mocks.isEmptyGuid,
  isGeneralJournalEntryDocumentType: mocks.isGeneralJournalEntryDocumentType,
  isNonEmptyGuid: mocks.isNonEmptyGuid,
  shortGuid: mocks.shortGuid,
  useLookupStore: () => mocks.lookupStore,
  lookupHintFromSource: (lookup?: unknown | null) => lookup ?? null,
}))

vi.mock('../../../src/editor/entityProfile', () => ({
  resolveTradeEditorEntityProfile: mocks.resolveTradeEditorEntityProfile,
}))

import { createTradeEditorConfig } from '../../../src/editor/framework'

describe('trade editor framework', () => {
  beforeEach(() => {
    mocks.buildGeneralJournalEntriesPath.mockClear()
    mocks.lookupStore.ensureAnyDocumentLabels.mockClear()
    mocks.lookupStore.labelForAnyDocument.mockClear()
    mocks.resolveTradeEditorEntityProfile.mockClear()
  })

  it('routes general journal entries through accounting urls', () => {
    const config = createTradeEditorConfig()

    expect(config.routing.buildDocumentFullPageUrl('accounting.general_journal_entry', 'gje-1')).toBe('/accounting/general-journal-entries/gje-1')
    expect(mocks.buildGeneralJournalEntriesPath).toHaveBeenCalledWith('gje-1')
  })

  it('routes ordinary trade documents to metadata document pages', () => {
    const config = createTradeEditorConfig()

    expect(config.routing.buildDocumentFullPageUrl('trd.sales_invoice', 'si-1')).toBe('/documents/trd.sales_invoice/si-1')
    expect(config.routing.buildDocumentFullPageUrl('trd.sales_invoice', null)).toBe('/documents/trd.sales_invoice/new')
  })

  it('prefetches unique related document ids from effect writes', async () => {
    const config = createTradeEditorConfig()

    await config.effects.prefetchRelatedLabels?.({
      effects: {
        referenceRegisterWrites: [
          {
            fields: {
              sales_invoice_document_id: '11111111-1111-4111-8111-111111111111',
              ignored_field: 'not-a-guid',
              vendor_payment_document_id: '11111111-1111-4111-8111-111111111111',
            },
          },
        ],
      },
    } as never)

    expect(mocks.lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledWith(
      [
        'trd.purchase_receipt',
        'trd.sales_invoice',
        'trd.customer_payment',
        'trd.vendor_payment',
        'trd.inventory_transfer',
        'trd.inventory_adjustment',
        'trd.customer_return',
        'trd.vendor_return',
        'trd.item_price_update',
      ],
      ['11111111-1111-4111-8111-111111111111'],
    )
  })

  it('skips prefetch when no effect document ids are present', async () => {
    const config = createTradeEditorConfig()

    await config.effects.prefetchRelatedLabels?.({
      effects: {
        referenceRegisterWrites: [{ fields: { notes: 'hello', customer_id: 'party-1' } }],
      },
    } as never)

    expect(mocks.lookupStore.ensureAnyDocumentLabels).not.toHaveBeenCalled()
  })

  it('resolves effect document field to the current document display for self-references', () => {
    const config = createTradeEditorConfig()

    expect(config.effects.resolveFieldValue?.({
      documentId: '11111111-1111-4111-8111-111111111111',
      document: { display: 'Sales Invoice SI-100' },
      fieldKey: 'sales_invoice_document_id',
      value: '11111111-1111-4111-8111-111111111111',
    } as never)).toBe('Sales Invoice SI-100')
  })

  it('resolves referenced effect document fields through lookup labels', () => {
    const config = createTradeEditorConfig()

    expect(config.effects.resolveFieldValue?.({
      documentId: '99999999-9999-4999-8999-999999999999',
      document: { display: 'Current' },
      fieldKey: 'sales_invoice_document_id',
      value: '11111111-1111-4111-8111-111111111111',
    } as never)).toBe('Sales Invoice SI-100')
  })

  it('falls back to short guid when lookup label is missing or still guid-like', () => {
    const config = createTradeEditorConfig()

    expect(config.effects.resolveFieldValue?.({
      documentId: '99999999-9999-4999-8999-999999999999',
      document: { display: 'Current' },
      fieldKey: 'sales_invoice_document_id',
      value: '22222222-2222-4222-8222-222222222222',
    } as never)).toBe('short:22222222')

    expect(config.effects.resolveFieldValue?.({
      documentId: '99999999-9999-4999-8999-999999999999',
      document: { display: 'Current' },
      fieldKey: 'sales_invoice_document_id',
      value: '44444444-4444-4444-8444-444444444444',
    } as never)).toBe('short:44444444')
  })

  it('returns null for non-reference fields and invalid ids', () => {
    const config = createTradeEditorConfig()

    expect(config.effects.resolveFieldValue?.({
      documentId: '99999999-9999-4999-8999-999999999999',
      document: { display: 'Current' },
      fieldKey: 'notes',
      value: '11111111-1111-4111-8111-111111111111',
    } as never)).toBeNull()

    expect(config.effects.resolveFieldValue?.({
      documentId: '99999999-9999-4999-8999-999999999999',
      document: { display: 'Current' },
      fieldKey: 'sales_invoice_document_id',
      value: 'not-a-guid',
    } as never)).toBeNull()
  })

  it('exposes audit labels and hidden infrastructure fields', () => {
    const config = createTradeEditorConfig()

    expect(config.audit.hiddenFieldNames).toEqual([
      'inventory_movements_register_id',
      'item_prices_register_id',
    ])
    expect(config.audit.explicitFieldLabels.cash_account_id).toBe('Cash / Bank Account')
    expect(config.audit.explicitFieldLabels.inventory_adjustment_account_id).toBe('Inventory Adjustment Account')
  })

  it('uses trade lookup hints in print mode and delegates entity profiles', () => {
    const config = createTradeEditorConfig()

    expect(config.print.resolveLookupHint?.({
      documentType: 'trd.customer_payment',
      fieldKey: 'cash_account_id',
      lookup: null,
    } as never)).toEqual({ kind: 'coa' })

    expect(config.resolveEntityProfile({ entityTypeCode: 'trd.sales_invoice' } as never)).toEqual({ title: 'Resolved Profile' })
    expect(mocks.resolveTradeEditorEntityProfile).toHaveBeenCalled()
  })
})
