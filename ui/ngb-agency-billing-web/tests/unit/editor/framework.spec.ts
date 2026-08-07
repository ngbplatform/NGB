import { beforeEach, describe, expect, it, vi } from 'vitest'

const GUID = '11111111-1111-4111-8111-111111111111'
const GUID_2 = '22222222-2222-4222-8222-222222222222'
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

const mocks = vi.hoisted(() => ({
  buildGeneralJournalEntriesPath: vi.fn((id?: string | null) => `/gje/${id ?? 'new'}`),
  getDocumentById: vi.fn(),
  getDocumentEffects: vi.fn(),
  getDocumentGraph: vi.fn(),
  getEntityAuditLog: vi.fn(),
  getAgencyBillingLookupHint: vi.fn(),
  resolveAgencyBillingEditorEntityProfile: vi.fn(),
  lookupStore: {
    ensureAnyDocumentLabels: vi.fn().mockResolvedValue(undefined),
    labelForAnyDocument: vi.fn(() => ''),
  },
}))

vi.mock('@ngbplatform/ui', () => ({
  buildGeneralJournalEntriesPath: mocks.buildGeneralJournalEntriesPath,
  getDocumentById: mocks.getDocumentById,
  getDocumentEffects: mocks.getDocumentEffects,
  getDocumentGraph: mocks.getDocumentGraph,
  getEntityAuditLog: mocks.getEntityAuditLog,
  isEmptyGuid: (value: string) => value === EMPTY_GUID,
  isGeneralJournalEntryDocumentType: (value: string) => value === 'accounting.general_journal_entry',
  isNonEmptyGuid: (value: string) => /^[0-9a-f-]{36}$/i.test(value),
  shortGuid: (value: string) => `short:${value.slice(0, 8)}`,
  useLookupStore: () => mocks.lookupStore,
}))

vi.mock('../../../src/lookup/hints', () => ({
  getAgencyBillingLookupHint: mocks.getAgencyBillingLookupHint,
}))

vi.mock('../../../src/editor/entityProfile', () => ({
  resolveAgencyBillingEditorEntityProfile: mocks.resolveAgencyBillingEditorEntityProfile,
}))

import { createAgencyBillingEditorConfig } from '../../../src/editor/framework'

describe('agency billing editor framework', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.lookupStore.labelForAnyDocument.mockReturnValue('')
  })

  it('wires routes, loaders, audit, print, lookup store, and profiles', () => {
    const config = createAgencyBillingEditorConfig()
    expect(config.routing!.buildDocumentFullPageUrl('accounting.general_journal_entry', 'gje-1')).toBe('/gje/gje-1')
    expect(config.routing!.buildDocumentFullPageUrl(' ab.sales/invoice ', null)).toBe('/documents/ab.sales%2Finvoice/new')
    expect(config.routing!.buildDocumentFullPageUrl('ab.sales_invoice', ' id/1 ')).toBe('/documents/ab.sales_invoice/id%2F1')
    expect(config.loadDocumentById).toBe(mocks.getDocumentById)
    expect(config.loadDocumentEffects).toBe(mocks.getDocumentEffects)
    expect(config.loadDocumentGraph).toBe(mocks.getDocumentGraph)
    expect(config.loadEntityAuditLog).toBe(mocks.getEntityAuditLog)
    expect(config.lookupStore).toBe(mocks.lookupStore)
    expect(config.audit?.hiddenFieldNames).toHaveLength(3)
    expect(config.audit?.explicitFieldLabels?.ar_account_id).toBe('Accounts Receivable Account')
    expect(config.resolveEntityProfile).toBe(mocks.resolveAgencyBillingEditorEntityProfile)

    mocks.getAgencyBillingLookupHint.mockReturnValueOnce({ kind: 'coa' })
    expect(config.print?.resolveLookupHint?.({ documentType: 'ab.sales_invoice', fieldKey: 'ar_account_id' } as never))
      .toEqual({ kind: 'coa' })
    mocks.getAgencyBillingLookupHint.mockReturnValueOnce(undefined)
    expect(config.print?.resolveLookupHint?.({ documentType: 'ab.sales_invoice', fieldKey: 'x' } as never)).toBeNull()
  })

  it('prefetches unique supported effect references and skips empty batches', async () => {
    const prefetch = createAgencyBillingEditorConfig().effects!.prefetchRelatedLabels!
    await prefetch({ effects: {} } as never)
    await prefetch({ effects: { referenceRegisterWrites: [{ fields: null }] } } as never)
    expect(mocks.lookupStore.ensureAnyDocumentLabels).not.toHaveBeenCalled()

    await prefetch({
      effects: {
        referenceRegisterWrites: [{
          fields: {
            contract_id: GUID,
            source_timesheet_id: GUID,
            sales_invoice_id: GUID_2,
            custom_document_id: GUID_2,
            ignored: GUID,
            invalid_document_id: 'invalid',
            empty_document_id: EMPTY_GUID,
          },
        }],
      },
    } as never)
    expect(mocks.lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledWith(expect.any(Array), [GUID, GUID_2])
  })

  it('rejects unsupported and invalid effect values', () => {
    const resolve = createAgencyBillingEditorConfig().effects!.resolveFieldValue!
    expect(resolve({ fieldKey: 'memo', value: GUID } as never)).toBeNull()
    expect(resolve({ fieldKey: 'contract_id', value: 'invalid' } as never)).toBeNull()
    expect(resolve({ fieldKey: 'contract_id', value: EMPTY_GUID } as never)).toBeNull()
    expect(resolve({ fieldKey: 'contract_id', value: null } as never)).toBeNull()
  })

  it('uses current display, resolved label, and short-guid fallbacks', () => {
    const resolve = createAgencyBillingEditorConfig().effects!.resolveFieldValue!
    expect(resolve({ documentId: GUID, document: { display: ' Current Invoice ' }, fieldKey: 'contract_id', value: GUID } as never))
      .toBe('Current Invoice')
    mocks.lookupStore.labelForAnyDocument.mockReturnValueOnce(' Related Timesheet ')
    expect(resolve({ documentId: GUID_2, fieldKey: 'source_timesheet_id', value: GUID } as never)).toBe('Related Timesheet')
    mocks.lookupStore.labelForAnyDocument.mockReturnValueOnce(GUID)
    expect(resolve({ documentId: GUID_2, fieldKey: 'source_timesheet_id', value: GUID } as never)).toBe('short:11111111')
    mocks.lookupStore.labelForAnyDocument.mockReturnValueOnce('')
    expect(resolve({ documentId: GUID_2, fieldKey: 'source_timesheet_id', value: GUID } as never)).toBe('short:11111111')
    expect(resolve({ documentId: GUID, document: { display: ' ' }, fieldKey: 'contract_id', value: GUID } as never)).toBe('short:11111111')
    expect(resolve({ documentId: GUID, document: { display: null }, fieldKey: 'contract_id', value: GUID } as never)).toBe('short:11111111')
  })
})
