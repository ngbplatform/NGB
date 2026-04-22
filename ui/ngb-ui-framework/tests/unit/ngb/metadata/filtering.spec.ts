import { describe, expect, it, vi } from 'vitest'

import {
  buildFilterOptionLabelsByKey,
  ensureResolvedLookupLabels,
  extractLookupIds,
  filterInputType,
  filterPlaceholder,
  filterSelectOptions,
  hydrateResolvedLookupItems,
  joinFilterValues,
  labelForResolvedLookup,
  optionLabelForFilter,
  searchResolvedLookupItems,
  splitFilterValues,
  summarizeFilterValues,
} from '../../../../src/ngb/metadata/filtering'

function createLookupStore() {
  return {
    searchCatalog: vi.fn().mockResolvedValue([{ id: 'catalog-1', label: 'Riverfront Tower' }]),
    searchCoa: vi.fn().mockResolvedValue([{ id: 'coa-1', label: '1100 Cash' }]),
    searchDocuments: vi.fn().mockResolvedValue([{ id: 'document-1', label: 'Invoice INV-001' }]),
    ensureCatalogLabels: vi.fn().mockResolvedValue(undefined),
    ensureCoaLabels: vi.fn().mockResolvedValue(undefined),
    ensureAnyDocumentLabels: vi.fn().mockResolvedValue(undefined),
    labelForCatalog: vi.fn((catalogType: string, id: unknown) => `${catalogType}:${String(id)}`),
    labelForCoa: vi.fn((id: unknown) => `COA:${String(id)}`),
    labelForAnyDocument: vi.fn((documentTypes: string[], id: unknown) => `${documentTypes.join('|')}:${String(id)}`),
  }
}

describe('metadata filtering helpers', () => {
  it('splits, joins, and summarizes multi-value filters', () => {
    expect(splitFilterValues(' open, posted ,, draft ')).toEqual(['open', 'posted', 'draft'])
    expect(joinFilterValues([' open ', '', 'posted'])).toBe('open,posted')
    expect(summarizeFilterValues([])).toBeNull()
    expect(summarizeFilterValues(['Open'])).toBe('Open')
    expect(summarizeFilterValues(['Open', 'Posted', 'Draft'])).toBe('Open (+2)')
  })

  it('builds option label maps and placeholders/select options for filter fields', () => {
    expect(optionLabelForFilter({
      options: [
        { value: 'open', label: 'Open' },
        { value: 'posted', label: 'Posted' },
      ],
    }, 'posted')).toBe('Posted')

    expect(buildFilterOptionLabelsByKey([
      {
        key: 'status',
        options: [
          { value: 'open', label: 'Open' },
          { value: 'posted', label: 'Posted' },
        ],
      },
      {
        key: 'blank',
        options: [],
      },
    ])).toEqual({
      status: new Map([
        ['open', 'Open'],
        ['posted', 'Posted'],
      ]),
    })

    expect(filterInputType('Decimal')).toBe('number')
    expect(filterInputType('String')).toBe('text')
    expect(filterPlaceholder({ label: 'Property', lookup: { kind: 'catalog', catalogType: 'pm.property' }, isMulti: false })).toBe('Type property…')
    expect(filterPlaceholder({ label: 'Status', lookup: null, isMulti: true })).toBe('Comma-separated values…')
    expect(filterPlaceholder({ label: 'Manager', lookup: null, isMulti: false })).toBe('Manager')
    expect(filterSelectOptions({
      options: [{ value: 'open', label: 'Open' }],
    })).toEqual([
      { value: '', label: 'Any' },
      { value: 'open', label: 'Open' },
    ])
  })

  it('hydrates lookup ids, labels, and search results through the resolved lookup kind', async () => {
    const lookupStore = createLookupStore()

    expect(extractLookupIds([
      '11111111-1111-1111-1111-111111111111',
      'invalid',
      '22222222-2222-2222-2222-222222222222',
    ])).toEqual([
      '11111111-1111-1111-1111-111111111111',
      '22222222-2222-2222-2222-222222222222',
    ])

    expect(labelForResolvedLookup(lookupStore, { kind: 'catalog', catalogType: 'pm.property' }, 'catalog-1')).toBe('pm.property:catalog-1')
    expect(labelForResolvedLookup(lookupStore, { kind: 'coa' }, 'coa-1')).toBe('COA:coa-1')
    expect(labelForResolvedLookup(lookupStore, { kind: 'document', documentTypes: ['pm.invoice'] }, 'document-1')).toBe('pm.invoice:document-1')

    await ensureResolvedLookupLabels(lookupStore, { kind: 'catalog', catalogType: 'pm.property' }, ['catalog-1'])
    await ensureResolvedLookupLabels(lookupStore, { kind: 'coa' }, ['coa-1'])
    await ensureResolvedLookupLabels(lookupStore, { kind: 'document', documentTypes: ['pm.invoice'] }, ['document-1'])

    expect(lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('pm.property', ['catalog-1'])
    expect(lookupStore.ensureCoaLabels).toHaveBeenCalledWith(['coa-1'])
    expect(lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledWith(['pm.invoice'], ['document-1'])

    expect(await searchResolvedLookupItems(lookupStore, { kind: 'catalog', catalogType: 'pm.property', filters: { active: '1' } }, 'river')).toEqual([
      { id: 'catalog-1', label: 'Riverfront Tower' },
    ])
    expect(await searchResolvedLookupItems(lookupStore, { kind: 'coa' }, '1100')).toEqual([
      { id: 'coa-1', label: '1100 Cash' },
    ])
    expect(await searchResolvedLookupItems(lookupStore, { kind: 'document', documentTypes: ['pm.invoice'] }, 'INV')).toEqual([
      { id: 'document-1', label: 'Invoice INV-001' },
    ])

    expect(await hydrateResolvedLookupItems(lookupStore, { kind: 'catalog', catalogType: 'pm.property' }, ['catalog-1'])).toEqual([
      { id: 'catalog-1', label: 'pm.property:catalog-1' },
    ])
  })
})
