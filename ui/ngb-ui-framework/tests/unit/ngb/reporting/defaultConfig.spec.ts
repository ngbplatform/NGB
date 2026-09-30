import { beforeEach, describe, expect, it, vi } from 'vitest'

const lookupMocks = vi.hoisted(() => ({
  buildLookupFieldTargetUrl: vi.fn(),
  useLookupStore: vi.fn(),
}))

vi.mock('../../../../src/ngb/lookup/navigation', () => ({
  buildLookupFieldTargetUrl: lookupMocks.buildLookupFieldTargetUrl,
}))

vi.mock('../../../../src/ngb/lookup/store', () => ({
  useLookupStore: lookupMocks.useLookupStore,
}))

import { createDefaultNgbReportingConfig, resolveDefaultReportCellActionUrl } from '../../../../src/ngb/reporting/defaultConfig'

const documentId = '11111111-1111-1111-1111-111111111111'

describe('reporting default config', () => {
  beforeEach(() => {
    lookupMocks.buildLookupFieldTargetUrl.mockReset()
    lookupMocks.useLookupStore.mockReset()
  })

  it('exposes the shared lookup store and resolves lookup targets through the lookup navigation helper', async () => {
    const store = {
      searchCatalog: vi.fn(),
      labelForCatalog: vi.fn(),
    }

    lookupMocks.useLookupStore.mockReturnValue(store)
    lookupMocks.buildLookupFieldTargetUrl.mockResolvedValueOnce('/catalogs/pm.property/riverfront')

    const config = createDefaultNgbReportingConfig()

    expect(config.useLookupStore()).toBe(store)
    await expect(config.resolveLookupTarget?.({
      hint: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
      value: 'riverfront',
      routeFullPath: '/reports/pm.occupancy.summary',
    })).resolves.toBe('/catalogs/pm.property/riverfront')

    expect(lookupMocks.buildLookupFieldTargetUrl).toHaveBeenCalledWith({
      hint: {
        kind: 'catalog',
        catalogType: 'pm.property',
      },
      value: 'riverfront',
      route: {
        fullPath: '/reports/pm.occupancy.summary',
      },
    })

    for (const [value, expected] of [
      [null, null],
      [{ id: documentId }, { id: documentId }],
      [{ id: 42 }, { id: null }],
      [{ label: 'No id' }, null],
      [42, null],
    ] as const) {
      await config.resolveLookupTarget?.({ hint: null, value, routeFullPath: '/reports/current' })
      expect(lookupMocks.buildLookupFieldTargetUrl).toHaveBeenLastCalledWith({
        hint: null,
        value: expected,
        route: { fullPath: '/reports/current' },
      })
    }
  })

  it('rejects incomplete and unsupported actions at every navigation boundary', () => {
    expect(resolveDefaultReportCellActionUrl(null)).toBeNull()
    expect(resolveDefaultReportCellActionUrl({} as never)).toBeNull()
    expect(resolveDefaultReportCellActionUrl({ kind: 'unsupported' } as never)).toBeNull()

    expect(resolveDefaultReportCellActionUrl({ kind: 'open_document', documentId } as never)).toBeNull()
    expect(resolveDefaultReportCellActionUrl({ kind: 'open_document', documentType: 'pm.invoice' } as never)).toBeNull()
    expect(resolveDefaultReportCellActionUrl({ kind: 'open_document', documentType: 'pm.invoice', documentId: 'bad' })).toBeNull()
    expect(resolveDefaultReportCellActionUrl({ kind: 'open_account' } as never)).toBeNull()
    expect(resolveDefaultReportCellActionUrl({ kind: 'open_account', accountId: 'bad' })).toBeNull()
    expect(resolveDefaultReportCellActionUrl({ kind: 'open_catalog', catalogId: documentId } as never)).toBeNull()
    expect(resolveDefaultReportCellActionUrl({ kind: 'open_catalog', catalogType: 'pm.property' } as never)).toBeNull()
    expect(resolveDefaultReportCellActionUrl({ kind: 'open_catalog', catalogType: 'pm.property', catalogId: 'bad' })).toBeNull()
    expect(resolveDefaultReportCellActionUrl({ kind: 'open_report' })).toBeNull()
  })

  it('builds valid actions when optional navigation context is absent', () => {
    expect(resolveDefaultReportCellActionUrl({
      kind: 'open_document',
      documentType: ' pm.invoice ',
      documentId,
    })).toContain(`/documents/pm.invoice/${documentId}`)
    expect(resolveDefaultReportCellActionUrl({ kind: 'open_account', accountId: documentId })).toContain(documentId)
    expect(resolveDefaultReportCellActionUrl({
      kind: 'open_catalog',
      catalogType: ' pm.property ',
      catalogId: documentId,
    })).toContain(`/catalogs/pm.property/${documentId}`)

    const reportUrl = resolveDefaultReportCellActionUrl({
      kind: 'open_report',
      report: { reportCode: ' pm.summary ' },
    })
    expect(reportUrl).toContain('/reports/pm.summary')
  })
})
