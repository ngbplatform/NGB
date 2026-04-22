import { beforeEach, describe, expect, it, vi } from 'vitest'

const metadataLookupMocks = vi.hoisted(() => ({
  resolveLookupHint: vi.fn(),
}))

vi.mock('../../../../src/ngb/metadata/config', async () => {
  const actual = await vi.importActual<typeof import('../../../../src/ngb/metadata/config')>(
    '../../../../src/ngb/metadata/config',
  )

  return {
    ...actual,
    resolveNgbMetadataFormBehavior: metadataLookupMocks.resolveLookupHint,
  }
})

import {
  lookupHintFromSource,
  resolveLookupHint,
} from '../../../../src/ngb/metadata/lookup'

describe('metadata lookup helpers', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    metadataLookupMocks.resolveLookupHint.mockReturnValue({})
  })

  it('maps lookup sources into normalized hints', () => {
    expect(lookupHintFromSource({ kind: 'catalog', catalogType: 'pm.property' })).toEqual({
      kind: 'catalog',
      catalogType: 'pm.property',
    })
    expect(lookupHintFromSource({ kind: 'document', documentTypes: ['pm.invoice', 'pm.credit_note'] })).toEqual({
      kind: 'document',
      documentTypes: ['pm.invoice', 'pm.credit_note'],
    })
    expect(lookupHintFromSource({ kind: 'coa' })).toEqual({ kind: 'coa' })
    expect(lookupHintFromSource(null)).toBeNull()
  })

  it('prefers behavior-provided lookup hints over field lookup defaults', () => {
    metadataLookupMocks.resolveLookupHint.mockReturnValue({
      resolveLookupHint: vi.fn(() => ({
        kind: 'catalog',
        catalogType: 'pm.override',
        filters: { active: '1' },
      })),
    })

    const result = resolveLookupHint({
      entityTypeCode: 'pm.invoice',
      model: { status: 'draft' },
      field: {
        key: 'property_id',
        label: 'Property',
        dataType: 'Guid',
        uiControl: 1,
        isRequired: false,
        isReadOnly: false,
        lookup: {
          kind: 'catalog',
          catalogType: 'pm.property',
        },
      },
    })

    expect(result).toEqual({
      kind: 'catalog',
      catalogType: 'pm.override',
      filters: { active: '1' },
    })
  })

  it('falls back to the field lookup source when behavior does not override it', () => {
    metadataLookupMocks.resolveLookupHint.mockReturnValue({
      resolveLookupHint: vi.fn(() => null),
    })

    const result = resolveLookupHint({
      entityTypeCode: 'pm.invoice',
      model: {},
      field: {
        key: 'source_document_id',
        label: 'Source document',
        dataType: 'Guid',
        uiControl: 1,
        isRequired: false,
        isReadOnly: false,
        lookup: {
          kind: 'document',
          documentTypes: ['pm.invoice'],
        },
      },
    })

    expect(result).toEqual({
      kind: 'document',
      documentTypes: ['pm.invoice'],
    })
  })
})
