import { describe, expect, it, vi } from 'vitest'

import { hydrateEntityReferenceFieldsForEditing } from '../../../../src/ngb/metadata/referenceHydration'

describe('metadata referenceHydration', () => {
  it('hydrates guid-backed references through grouped lookup batches', async () => {
    const lookupStore = {
      searchCatalog: vi.fn(),
      searchCoa: vi.fn(),
      searchDocuments: vi.fn(),
      ensureCatalogLabels: vi.fn().mockResolvedValue(undefined),
      ensureCoaLabels: vi.fn().mockResolvedValue(undefined),
      ensureAnyDocumentLabels: vi.fn().mockResolvedValue(undefined),
      labelForCatalog: vi.fn((_catalogType: string, id: unknown) => `Catalog ${String(id)}`),
      labelForCoa: vi.fn((id: unknown) => `Account ${String(id)}`),
      labelForAnyDocument: vi.fn((_documentTypes: string[], id: unknown) => `Document ${String(id)}`),
    }

    const model = {
      property_id: '11111111-1111-1111-1111-111111111111',
      account_id: '22222222-2222-2222-2222-222222222222',
      invoice_id: '33333333-3333-3333-3333-333333333333',
      existing_ref: {
        id: '44444444-4444-4444-4444-444444444444',
        display: 'Already loaded',
      },
      invalid_ref: 'not-a-guid',
    }

    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: 'pm.invoice',
      form: {
        sections: [
          {
            title: 'Main',
            rows: [
              {
                fields: [
                  {
                    key: 'property_id',
                    label: 'Property',
                    dataType: 'Guid',
                    uiControl: 0,
                    isRequired: false,
                    isReadOnly: false,
                    lookup: { kind: 'catalog', catalogType: 'pm.property' },
                  },
                  {
                    key: 'account_id',
                    label: 'Account',
                    dataType: 'Guid',
                    uiControl: 0,
                    isRequired: false,
                    isReadOnly: false,
                    lookup: { kind: 'coa' },
                  },
                  {
                    key: 'invoice_id',
                    label: 'Invoice',
                    dataType: 'Guid',
                    uiControl: 0,
                    isRequired: false,
                    isReadOnly: false,
                    lookup: { kind: 'document', documentTypes: ['pm.invoice'] },
                  },
                  {
                    key: 'existing_ref',
                    label: 'Existing ref',
                    dataType: 'Guid',
                    uiControl: 0,
                    isRequired: false,
                    isReadOnly: false,
                    lookup: { kind: 'catalog', catalogType: 'pm.property' },
                  },
                  {
                    key: 'invalid_ref',
                    label: 'Invalid ref',
                    dataType: 'Guid',
                    uiControl: 0,
                    isRequired: false,
                    isReadOnly: false,
                    lookup: { kind: 'catalog', catalogType: 'pm.property' },
                  },
                ],
              },
            ],
          },
        ],
      },
      model,
      lookupStore: lookupStore as never,
    })

    expect(lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('pm.property', ['11111111-1111-1111-1111-111111111111'])
    expect(lookupStore.ensureCoaLabels).toHaveBeenCalledWith(['22222222-2222-2222-2222-222222222222'])
    expect(lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledWith(['pm.invoice'], ['33333333-3333-3333-3333-333333333333'])
    expect(model.property_id).toEqual({
      id: '11111111-1111-1111-1111-111111111111',
      display: 'Catalog 11111111-1111-1111-1111-111111111111',
    })
    expect(model.account_id).toEqual({
      id: '22222222-2222-2222-2222-222222222222',
      display: 'Account 22222222-2222-2222-2222-222222222222',
    })
    expect(model.invoice_id).toEqual({
      id: '33333333-3333-3333-3333-333333333333',
      display: 'Document 33333333-3333-3333-3333-333333333333',
    })
    expect(model.existing_ref).toEqual({
      id: '44444444-4444-4444-4444-444444444444',
      display: 'Already loaded',
    })
    expect(model.invalid_ref).toBe('not-a-guid')
  })

  it('uses behavior overrides and falls back to the raw id when a label is blank', async () => {
    const lookupStore = {
      searchCatalog: vi.fn(),
      searchCoa: vi.fn(),
      searchDocuments: vi.fn(),
      ensureCatalogLabels: vi.fn().mockResolvedValue(undefined),
      ensureCoaLabels: vi.fn().mockResolvedValue(undefined),
      ensureAnyDocumentLabels: vi.fn().mockResolvedValue(undefined),
      labelForCatalog: vi.fn(() => ''),
      labelForCoa: vi.fn(() => ''),
      labelForAnyDocument: vi.fn(() => ''),
    }

    const model = {
      counterparty_id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    }

    await hydrateEntityReferenceFieldsForEditing({
      entityTypeCode: 'pm.invoice',
      form: {
        sections: [
          {
            title: 'Main',
            rows: [
              {
                fields: [
                  {
                    key: 'counterparty_id',
                    label: 'Counterparty',
                    dataType: 'Guid',
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
      model,
      lookupStore: lookupStore as never,
      behavior: {
        resolveLookupHint: () => ({ kind: 'catalog', catalogType: 'crm.counterparty' }),
      },
    })

    expect(lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('crm.counterparty', ['aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'])
    expect(model.counterparty_id).toEqual({
      id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      display: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    })
  })
})
