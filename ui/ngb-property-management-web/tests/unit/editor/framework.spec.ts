import { beforeEach, describe, expect, it, vi } from 'vitest'

const GUID = '11111111-2222-3333-4444-555555555555'
const GUID_2 = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

const mocks = vi.hoisted(() => ({
  buildGeneralJournalEntriesPath: vi.fn((id?: string | null) => `/accounting/${id ?? 'new'}`),
  getDocumentById: vi.fn(),
  getDocumentEffects: vi.fn(),
  getDocumentGraph: vi.fn(),
  getEntityAuditLog: vi.fn(),
  getLookupHint: vi.fn(),
  isGeneralJournalEntryDocumentType: vi.fn(() => false),
  resolvePmEditorEntityProfile: vi.fn(),
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
  isGeneralJournalEntryDocumentType: mocks.isGeneralJournalEntryDocumentType,
  isNonEmptyGuid: (value: string) => /^[0-9a-f]{8}-[0-9a-f-]{27}$/i.test(value) && value !== EMPTY_GUID,
  shortGuid: (value: string) => value.slice(0, 8),
  useLookupStore: () => mocks.lookupStore,
}))

vi.mock('../../../src/lookup/hints', () => ({
  getLookupHint: mocks.getLookupHint,
}))

vi.mock('../../../src/editor/entityProfile', () => ({
  resolvePmEditorEntityProfile: mocks.resolvePmEditorEntityProfile,
}))

import { createPmEditorConfig } from '../../../src/editor/framework'

describe('property-management editor framework', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.isGeneralJournalEntryDocumentType.mockReturnValue(false)
    mocks.lookupStore.labelForAnyDocument.mockReturnValue('')
  })

  it('wires platform loaders, lookup store, audit, print, and entity profiles', () => {
    const config = createPmEditorConfig()
    expect(config.loadDocumentById).toBe(mocks.getDocumentById)
    expect(config.loadDocumentEffects).toBe(mocks.getDocumentEffects)
    expect(config.loadDocumentGraph).toBe(mocks.getDocumentGraph)
    expect(config.loadEntityAuditLog).toBe(mocks.getEntityAuditLog)
    expect(config.lookupStore).toBe(mocks.lookupStore)
    expect(config.audit?.hiddenFieldNames).toContain('tenant_balances_register_id')
    expect(config.audit?.explicitFieldLabels?.bank_account_id).toBe('Bank Account')
    expect(config.resolveEntityProfile).toBe(mocks.resolvePmEditorEntityProfile)
    mocks.getLookupHint.mockReturnValueOnce({ kind: 'catalog' })
    expect(config.print?.resolveLookupHint?.({ documentType: 'pm.lease', fieldKey: 'x', lookup: {} } as never))
      .toEqual({ kind: 'catalog' })
  })

  it('builds encoded document and GJE routes for new and existing records', () => {
    const build = createPmEditorConfig().routing!.buildDocumentFullPageUrl
    expect(build(' pm.type/with space ', null)).toBe('/documents/pm.type%2Fwith%20space/new')
    expect(build('pm.lease', ' id/1 ')).toBe('/documents/pm.lease/id%2F1')
    expect(build('pm.lease', '')).toBe('/documents/pm.lease/new')
    mocks.isGeneralJournalEntryDocumentType.mockReturnValueOnce(true)
    expect(build('accounting.general_journal_entry', 'gje-1')).toBe('/accounting/gje-1')
  })

  it('routes reconciliation with and without a payment', () => {
    const resolve = createPmEditorConfig().resolveDocumentActionTarget!
    expect(resolve({ code: 'pm.receivables.reconciliation', parameters: { paymentId: ' payment/1 ' } }, {} as never))
      .toBe('/receivables/reconciliation?paymentId=payment%2F1')
    expect(resolve({ code: 'pm.receivables.reconciliation', parameters: { paymentId: ' ' } }, {} as never))
      .toBe('/receivables/reconciliation')
    expect(resolve({ code: 'pm.receivables.reconciliation', parameters: {} }, {} as never))
      .toBe('/receivables/reconciliation')
  })

  it('routes receivable and payable apply targets with normalized parameters', () => {
    const resolve = createPmEditorConfig().resolveDocumentActionTarget!
    expect(resolve({
      code: 'pm.receivables.apply',
      parameters: { documentId: ' payment/1 ', empty: null, blank: ' ' },
    }, {} as never)).toBe('/receivables/open-items?documentId=payment%2F1')
    expect(resolve({ code: 'pm.payables.apply', parameters: {} }, {} as never)).toBe('/payables/open-items')
    expect(resolve({ code: 'document.editor', parameters: {} }, {} as never)).toBeNull()
  })

  it('skips prefetch when effects contain no unresolved document labels', async () => {
    const prefetch = createPmEditorConfig().effects!.prefetchRelatedLabels!
    await prefetch({ effects: {}, lookupStore: mocks.lookupStore } as never)
    await prefetch({
      effects: {
        accountingEntries: [{
          debitDimensions: null,
          creditDimensions: [
            null,
            { valueId: '', display: '' },
            { valueId: EMPTY_GUID, display: EMPTY_GUID },
            { valueId: GUID, display: 'Friendly label' },
          ],
        }],
        operationalRegisterMovements: [{ dimensions: undefined }],
        referenceRegisterWrites: [{ dimensions: [] }],
      },
      lookupStore: mocks.lookupStore,
    } as never)
    expect(mocks.lookupStore.ensureAnyDocumentLabels).not.toHaveBeenCalled()
  })

  it('prefetches unique ids with empty, GUID, short-synthetic, and suffix-synthetic displays', async () => {
    const prefetch = createPmEditorConfig().effects!.prefetchRelatedLabels!
    await prefetch({
      effects: {
        accountingEntries: [{
          debitDimensions: [
            { valueId: GUID, display: '' },
            { valueId: GUID, display: GUID },
          ],
          creditDimensions: [{ valueId: GUID_2, display: `Document ${GUID_2.slice(0, 8)}` }],
        }],
        operationalRegisterMovements: [{
          dimensions: [{ valueId: GUID_2, display: `Document …${GUID_2.slice(-4)}` }],
        }],
        referenceRegisterWrites: [{ dimensions: [{ valueId: GUID, display: null }] }],
      },
      lookupStore: mocks.lookupStore,
    } as never)
    expect(mocks.lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledOnce()
    expect(new Set(mocks.lookupStore.ensureAnyDocumentLabels.mock.calls[0]![1])).toEqual(new Set([GUID, GUID_2]))
  })

  it('resolves a friendly lookup label for unresolved dimension displays', () => {
    const resolve = createPmEditorConfig().effects!.resolveDimensionDisplay!
    mocks.lookupStore.labelForAnyDocument.mockReturnValue('Friendly document')
    expect(resolve({ item: { valueId: GUID, display: '' } } as never)).toBe('Friendly document')
    expect(resolve({ item: { valueId: GUID, display: GUID } } as never)).toBe('Friendly document')
    expect(resolve({ item: { valueId: GUID, display: `Doc ${GUID.slice(0, 8)}` } } as never)).toBe('Friendly document')
    expect(resolve({ item: { valueId: GUID, display: `Doc …${GUID.slice(-4)}` } } as never)).toBe('Friendly document')
  })

  it('preserves friendly display and falls back to shortened ids or a dash', () => {
    const resolve = createPmEditorConfig().effects!.resolveDimensionDisplay!
    mocks.lookupStore.labelForAnyDocument.mockReturnValue('Different friendly document')
    expect(resolve({ item: { valueId: GUID, display: 'Existing display' } } as never)).toBe('Existing display')
    mocks.lookupStore.labelForAnyDocument.mockReturnValue(GUID)
    expect(resolve({ item: { valueId: GUID, display: 'Existing display' } } as never)).toBe('Existing display')
    expect(resolve({ item: { valueId: GUID, display: GUID } } as never)).toBe(GUID.slice(0, 8))
    mocks.lookupStore.labelForAnyDocument.mockReturnValue(`Doc ${GUID.slice(0, 8)}`)
    expect(resolve({ item: { valueId: GUID, display: '' } } as never)).toBe(GUID.slice(0, 8))
    mocks.lookupStore.labelForAnyDocument.mockReturnValue(`Doc …${GUID.slice(-4)}`)
    expect(resolve({ item: { valueId: GUID, display: '' } } as never)).toBe(GUID.slice(0, 8))
    expect(resolve({ item: { valueId: 'not-a-guid', display: 'Display' } } as never)).toBe('Display')
    expect(resolve({ item: { valueId: 'not-a-guid', display: GUID } } as never)).toBe('not-a-gu')
    expect(resolve({ item: { valueId: '', display: '' } } as never)).toBe('—')
    expect(resolve({ item: null } as never)).toBe('—')
  })
})
