import { describe, expect, it, vi } from 'vitest'

import { hydrateReportLookupItemsFromFilters, searchReportLookupItems } from '../../../../src/ngb/reporting/lookupFilters'
import type { ReportComposerDraft, ReportDefinitionDto } from '../../../../src/ngb/reporting/types'

const catalogId = '11111111-1111-1111-1111-111111111111'
const documentId = '22222222-2222-2222-2222-222222222222'
const accountId = '33333333-3333-3333-3333-333333333333'

function createLookupStore() {
  return {
    searchCatalog: vi.fn(async () => [{ id: catalogId, label: 'Riverfront Tower' }]),
    searchCoa: vi.fn(async () => [{ id: accountId, label: '3200 · Retained Earnings' }]),
    searchDocuments: vi.fn(async () => [{ id: documentId, label: 'Invoice INV-001' }]),
    ensureCatalogLabels: vi.fn(async () => undefined),
    ensureCoaLabels: vi.fn(async () => undefined),
    ensureAnyDocumentLabels: vi.fn(async () => undefined),
    labelForCatalog: vi.fn((_catalogType: string, id: unknown) => id === catalogId ? 'Riverfront Tower' : String(id)),
    labelForCoa: vi.fn((id: unknown) => id === accountId ? '3200 · Retained Earnings' : String(id)),
    labelForAnyDocument: vi.fn((_documentTypes: string[], id: unknown) => id === documentId ? 'Invoice INV-001' : String(id)),
  }
}

const definition: Pick<ReportDefinitionDto, 'filters'> = {
  filters: [
    {
      fieldCode: 'property',
      label: 'Property',
      dataType: 'Guid',
      lookup: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
    },
    {
      fieldCode: 'source_document',
      label: 'Source document',
      dataType: 'Guid',
      lookup: {
        kind: 'document',
        documentTypes: ['pm.invoice'],
      },
    },
    {
      fieldCode: 'retained_earnings_account',
      label: 'Retained earnings',
      dataType: 'Guid',
      lookup: {
        kind: 'coa',
      },
    },
  ],
}

function createDraft(): ReportComposerDraft {
  return {
    parameters: {},
    filters: {
      property: {
        raw: 'manual property',
        items: [],
        includeDescendants: false,
      },
      source_document: {
        raw: 'manual doc',
        items: [],
        includeDescendants: false,
      },
      retained_earnings_account: {
        raw: 'manual account',
        items: [],
        includeDescendants: false,
      },
      ignored: {
        raw: 'keep me',
        items: [],
        includeDescendants: false,
      },
    },
    rowGroups: [],
    columnGroups: [],
    measures: [],
    detailFields: [],
    sorts: [],
    showDetails: true,
    showSubtotals: true,
    showSubtotalsOnSeparateRows: false,
    showGrandTotals: true,
  }
}

describe('reporting lookup filter helpers', () => {
  it('delegates lookup searches through the resolved metadata lookup store', async () => {
    const lookupStore = createLookupStore()

    await expect(searchReportLookupItems(lookupStore, {
      kind: 'catalog',
      catalogType: 'pm.property',
      filters: { active: '1' },
    }, 'tower')).resolves.toEqual([
      { id: catalogId, label: 'Riverfront Tower' },
    ])

    expect(lookupStore.searchCatalog).toHaveBeenCalledWith('pm.property', 'tower', {
      filters: { active: '1' },
    })
  })

  it('hydrates catalog, document, and coa lookup items from report filters and clears raw values', async () => {
    const lookupStore = createLookupStore()
    const draft = createDraft()

    await hydrateReportLookupItemsFromFilters(lookupStore, definition, draft, {
      property: {
        value: catalogId,
      },
      source_document: {
        value: [documentId],
      },
      retained_earnings_account: {
        value: accountId,
      },
    })

    expect(lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('pm.property', [catalogId])
    expect(lookupStore.ensureAnyDocumentLabels).toHaveBeenCalledWith(['pm.invoice'], [documentId])
    expect(lookupStore.ensureCoaLabels).toHaveBeenCalledWith([accountId])

    expect(draft.filters.property).toEqual({
      raw: '',
      items: [{ id: catalogId, label: 'Riverfront Tower' }],
      includeDescendants: false,
    })
    expect(draft.filters.source_document).toEqual({
      raw: '',
      items: [{ id: documentId, label: 'Invoice INV-001' }],
      includeDescendants: false,
    })
    expect(draft.filters.retained_earnings_account).toEqual({
      raw: '',
      items: [{ id: accountId, label: '3200 · Retained Earnings' }],
      includeDescendants: false,
    })
  })

  it('ignores invalid lookup ids and unrelated filters without clobbering draft state', async () => {
    const lookupStore = createLookupStore()
    const draft = createDraft()

    await hydrateReportLookupItemsFromFilters(lookupStore, definition, draft, {
      property: {
        value: 'not-a-guid',
      },
      ignored: {
        value: catalogId,
      },
    })

    expect(lookupStore.ensureCatalogLabels).not.toHaveBeenCalled()
    expect(draft.filters.property).toEqual({
      raw: 'manual property',
      items: [],
      includeDescendants: false,
    })
    expect(draft.filters.ignored).toEqual({
      raw: 'keep me',
      items: [],
      includeDescendants: false,
    })
  })
})
