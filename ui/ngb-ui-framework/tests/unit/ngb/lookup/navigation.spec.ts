import { beforeEach, describe, expect, it, vi } from 'vitest'

import { withBackTarget } from '../../../../src/ngb/router/backNavigation'

const lookupNavigationMocks = vi.hoisted(() => ({
  buildCatalogUrl: vi.fn(),
  buildCoaUrl: vi.fn(),
  buildDocumentUrl: vi.fn(),
  loadDocumentItemsByIds: vi.fn(),
}))

vi.mock('../../../../src/ngb/lookup/config', () => ({
  getConfiguredNgbLookup: () => lookupNavigationMocks,
}))

import {
  buildLookupFieldTargetUrl,
  lookupValueId,
  normalizeLookupValue,
} from '../../../../src/ngb/lookup/navigation'

describe('lookup navigation', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    lookupNavigationMocks.buildCatalogUrl.mockImplementation((catalogType: string, id: string) => `/catalog/${catalogType}/${id}`)
    lookupNavigationMocks.buildCoaUrl.mockImplementation((id: string) => `/coa/${id}`)
    lookupNavigationMocks.buildDocumentUrl.mockImplementation((documentType: string, id: string) => `/documents/${documentType}/${id}`)
    lookupNavigationMocks.loadDocumentItemsByIds.mockResolvedValue([])
  })

  it('normalizes lookup values from strings and lookup objects', () => {
    expect(lookupValueId('  coa-1  ')).toBe('coa-1')
    expect(lookupValueId({ id: ' catalog-1 ' })).toBe('catalog-1')
    expect(lookupValueId('   ')).toBeNull()
    expect(lookupValueId(null)).toBeNull()
    expect(lookupValueId({})).toBeNull()
    expect(lookupValueId({ id: null })).toBeNull()

    expect(normalizeLookupValue('id')).toBe('id')
    expect(normalizeLookupValue(null)).toBeNull()
    expect(normalizeLookupValue(undefined)).toBeUndefined()
    expect(normalizeLookupValue({ id: 'id' })).toEqual({ id: 'id' })
    expect(normalizeLookupValue({ id: 42 })).toEqual({ id: null })
    expect(normalizeLookupValue({ label: 'No id' })).toBeNull()
    expect(normalizeLookupValue(42)).toBeNull()
  })

  it('builds coa and catalog targets while preserving the current page as back target', async () => {
    const route = { fullPath: '/accounting/general-journal-entries/new?panel=lines' }

    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'coa' },
      value: { id: 'coa-1' },
      route,
    })).resolves.toBe(withBackTarget('/coa/coa-1', route.fullPath))

    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'catalog', catalogType: 'pm.property' },
      value: 'property-1',
      route,
    })).resolves.toBe(withBackTarget('/catalog/pm.property/property-1', route.fullPath))
  })

  it('prefers bulk multi-type document resolution before building a target url', async () => {
    lookupNavigationMocks.loadDocumentItemsByIds.mockResolvedValue([
      { id: 'doc-1', label: 'CM-001', documentType: 'pm.credit_note' },
    ])

    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'document', documentTypes: ['pm.invoice', 'pm.credit_note'] },
      value: { id: 'doc-1' },
      route: { fullPath: '/accounting/general-journal-entries/gje-1' },
    })).resolves.toBe(
      withBackTarget('/documents/pm.credit_note/doc-1', '/accounting/general-journal-entries/gje-1'),
    )

    expect(lookupNavigationMocks.loadDocumentItemsByIds).toHaveBeenCalledWith(
      ['pm.invoice', 'pm.credit_note'],
      ['doc-1'],
    )
  })

  it('returns null when bulk multi-type document resolution does not find a match', async () => {
    lookupNavigationMocks.loadDocumentItemsByIds.mockResolvedValue([])

    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'document', documentTypes: ['pm.invoice', 'pm.credit_note'] },
      value: { id: 'doc-1' },
      route: { fullPath: '/accounting/general-journal-entries/gje-1' },
    })).resolves.toBeNull()

    expect(lookupNavigationMocks.loadDocumentItemsByIds).toHaveBeenCalledWith(
      ['pm.invoice', 'pm.credit_note'],
      ['doc-1'],
    )
  })

  it('returns null when value is missing or bulk document resolution fails', async () => {
    lookupNavigationMocks.loadDocumentItemsByIds.mockRejectedValue(new Error('Lookup offline'))

    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'coa' },
      value: null,
      route: { fullPath: '/accounting/general-journal-entries/new' },
    })).resolves.toBeNull()

    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'document', documentTypes: ['pm.invoice', 'pm.credit_note'] },
      value: { id: 'missing-doc' },
      route: { fullPath: '/accounting/general-journal-entries/new' },
    })).resolves.toBeNull()
  })

  it('handles absent, single, duplicate, and malformed document type candidates', async () => {
    const route = { fullPath: '/documents' }

    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'document', documentTypes: [] },
      value: 'doc-1',
      route,
    })).resolves.toBeNull()
    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'document', documentTypes: [null as never, ' '] },
      value: 'doc-1',
      route,
    })).resolves.toBeNull()
    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'document', documentTypes: [' pm.invoice ', 'pm.invoice'] },
      value: 'doc-1',
      route,
    })).resolves.toBe(withBackTarget('/documents/pm.invoice/doc-1', route.fullPath))
    expect(lookupNavigationMocks.loadDocumentItemsByIds).not.toHaveBeenCalled()
  })

  it('skips malformed bulk results before finding an exact id and type match', async () => {
    lookupNavigationMocks.loadDocumentItemsByIds.mockResolvedValue([
      { id: null, documentType: null },
      { id: 'other', documentType: 'pm.invoice' },
      { id: 'doc-1', documentType: 'pm.unknown' },
      { id: 'doc-1', documentType: 'pm.invoice' },
    ])

    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'document', documentTypes: ['pm.invoice', 'pm.credit_note'] },
      value: 'doc-1',
      route: { fullPath: '/documents' },
    })).resolves.toContain('/documents/pm.invoice/doc-1')
  })

  it('returns null for a missing hint, unsupported hint, or empty configured path', async () => {
    await expect(buildLookupFieldTargetUrl({
      hint: null,
      value: 'id',
      route: { fullPath: '/current' },
    })).resolves.toBeNull()
    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'unsupported' } as never,
      value: 'id',
      route: { fullPath: '/current' },
    })).resolves.toBeNull()

    lookupNavigationMocks.buildCatalogUrl.mockReturnValueOnce(null)
    await expect(buildLookupFieldTargetUrl({
      hint: { kind: 'catalog', catalogType: 'pm.property' },
      value: 'id',
      route: { fullPath: '/current' },
    })).resolves.toBeNull()
  })
})
