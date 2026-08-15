import { beforeEach, describe, expect, it, vi } from 'vitest'

const GUID = '11111111-2222-3333-4444-555555555555'
const GUID_2 = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'

const mocks = vi.hoisted(() => ({
  getDocumentById: vi.fn(),
  getDocumentEffects: vi.fn(),
  getDocumentGraph: vi.fn(),
  getDocumentEditorState: vi.fn(),
  executeDocumentAction: vi.fn(),
  getEntityAuditLog: vi.fn(),
  getCRMLookupHint: vi.fn(),
  resolveCRMEditorEntityProfile: vi.fn(),
  lookupStore: {
    ensureCatalogLabels: vi.fn().mockResolvedValue(undefined),
    ensureCoaLabels: vi.fn().mockResolvedValue(undefined),
    ensureAnyDocumentLabels: vi.fn().mockResolvedValue(undefined),
    labelForCatalog: vi.fn(() => ''),
    labelForCoa: vi.fn(() => ''),
    labelForAnyDocument: vi.fn(() => ''),
  },
}))

vi.mock('@ngbplatform/ui', () => ({
  getDocumentById: mocks.getDocumentById,
  getDocumentEffects: mocks.getDocumentEffects,
  getDocumentGraph: mocks.getDocumentGraph,
  getDocumentEditorState: mocks.getDocumentEditorState,
  executeDocumentAction: mocks.executeDocumentAction,
  getEntityAuditLog: mocks.getEntityAuditLog,
  isNonEmptyGuid: (value: string) => /^[0-9a-f]{8}-[0-9a-f-]{27}$/i.test(value),
  isReferenceValue: (value: unknown) => !!value && typeof value === 'object' && 'id' in value,
  shortGuid: (value: string) => value.slice(0, 8),
  useLookupStore: () => mocks.lookupStore,
}))

vi.mock('../../../src/lookup/hints', () => ({
  getCRMLookupHint: mocks.getCRMLookupHint,
}))

vi.mock('../../../src/editor/entityProfile', () => ({
  resolveCRMEditorEntityProfile: mocks.resolveCRMEditorEntityProfile,
}))

import { createCRMEditorConfig } from '../../../src/editor/framework'

describe('CRM editor framework', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.lookupStore.labelForCatalog.mockReturnValue('')
    mocks.lookupStore.labelForAnyDocument.mockReturnValue('')
  })

  it('wires routing, loaders, audit labels, lookup store, print, and profiles', () => {
    const config = createCRMEditorConfig()
    expect(config.routing!.buildDocumentFullPageUrl(' crm.lead/intake ', null))
      .toBe('/documents/crm.lead%2Fintake/new')
    expect(config.routing!.buildDocumentFullPageUrl('crm.lead_intake', ' id/1 '))
      .toBe('/documents/crm.lead_intake/id%2F1')
    expect(config.loadDocumentById).toBe(mocks.getDocumentById)
    expect(config.loadDocumentEffects).toBe(mocks.getDocumentEffects)
    expect(config.loadDocumentGraph).toBe(mocks.getDocumentGraph)
    expect(config.loadEntityAuditLog).toBe(mocks.getEntityAuditLog)
    expect(config.lookupStore).toBe(mocks.lookupStore)
    expect(config.audit?.hiddenFieldNames).toEqual([])
    expect(config.audit?.explicitFieldLabels?.opportunity_id).toBe('Opportunity')
    expect(config.resolveEntityProfile).toBe(mocks.resolveCRMEditorEntityProfile)
    mocks.getCRMLookupHint.mockReturnValueOnce({ kind: 'catalog', catalogType: 'crm.account' })
    expect(config.print?.resolveLookupHint?.({ documentType: 'crm.quote', fieldKey: 'account_id', lookup: {} } as never))
      .toEqual({ kind: 'catalog', catalogType: 'crm.account' })
    mocks.getCRMLookupHint.mockReturnValueOnce(undefined)
    expect(config.print?.resolveLookupHint?.({ documentType: 'crm.quote', fieldKey: 'x', lookup: {} } as never)).toBeNull()
  })

  it('skips prefetch without a lookup store or usable reference fields', async () => {
    const prefetch = createCRMEditorConfig().effects!.prefetchRelatedLabels!
    await prefetch({ effects: {}, lookupStore: null } as never)
    await prefetch({ effects: {}, lookupStore: mocks.lookupStore } as never)
    await prefetch({
      effects: {
        referenceRegisterWrites: [
          { fields: null },
          { fields: { unknown_id: GUID, account_id: 'invalid', contact_id: { id: 'invalid' } } },
        ],
      },
      lookupStore: mocks.lookupStore,
    } as never)
    expect(mocks.lookupStore.ensureCatalogLabels).not.toHaveBeenCalled()
    expect(mocks.lookupStore.ensureAnyDocumentLabels).not.toHaveBeenCalled()
  })

  it('prefetches deduplicated catalog and document labels for every CRM effect hint', async () => {
    const prefetch = createCRMEditorConfig().effects!.prefetchRelatedLabels!
    await prefetch({
      effects: {
        referenceRegisterWrites: [{
          fields: {
            source_document_id: GUID,
            lead_intake_id: { id: GUID },
            opportunity_id: GUID_2,
            quote_id: GUID,
            activity_id: GUID_2,
            account_id: GUID,
            converted_account_id: GUID_2,
            contact_id: GUID,
            converted_contact_id: GUID_2,
            stage_id: GUID,
            product_id: GUID_2,
            ignored_id: GUID,
          },
        }],
      },
      lookupStore: mocks.lookupStore,
    } as never)

    expect(mocks.lookupStore.ensureCatalogLabels).toHaveBeenCalledTimes(4)
    expect(mocks.lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('crm.account', [GUID, GUID_2])
    expect(mocks.lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('crm.contact', [GUID, GUID_2])
    expect(mocks.lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('crm.opportunity_stage', [GUID])
    expect(mocks.lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('crm.product', [GUID_2])
    expect(mocks.lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledTimes(5)
    expect(mocks.lookupStore.ensureCoaLabels).not.toHaveBeenCalled()
  })

  it('returns null for missing store, invalid ids, and unsupported fields', () => {
    const resolve = createCRMEditorConfig().effects!.resolveFieldValue!
    expect(resolve({ fieldKey: 'account_id', value: GUID, lookupStore: null } as never)).toBeNull()
    expect(resolve({ fieldKey: 'account_id', value: 'invalid', lookupStore: mocks.lookupStore } as never)).toBeNull()
    expect(resolve({ fieldKey: 'account_id', value: null, lookupStore: mocks.lookupStore } as never)).toBeNull()
    expect(resolve({ fieldKey: 'unknown_id', value: GUID, lookupStore: mocks.lookupStore } as never)).toBeNull()
  })

  it('resolves catalog and document labels and shortens absent or id-only labels', () => {
    const resolve = createCRMEditorConfig().effects!.resolveFieldValue!
    mocks.lookupStore.labelForCatalog.mockReturnValueOnce(' Acme ')
    expect(resolve({ fieldKey: 'ACCOUNT_ID', value: GUID, lookupStore: mocks.lookupStore } as never)).toBe('Acme')
    mocks.lookupStore.labelForCatalog.mockReturnValueOnce(GUID)
    expect(resolve({ fieldKey: 'converted_account_id', value: { id: GUID }, lookupStore: mocks.lookupStore } as never))
      .toBe(GUID.slice(0, 8))
    mocks.lookupStore.labelForAnyDocument.mockReturnValueOnce(' Lead LI-1 ')
    expect(resolve({ fieldKey: 'lead_intake_id', value: GUID, lookupStore: mocks.lookupStore } as never)).toBe('Lead LI-1')
    mocks.lookupStore.labelForAnyDocument.mockReturnValueOnce('')
    expect(resolve({ fieldKey: 'source_document_id', value: GUID, lookupStore: mocks.lookupStore } as never))
      .toBe(GUID.slice(0, 8))
  })
})
