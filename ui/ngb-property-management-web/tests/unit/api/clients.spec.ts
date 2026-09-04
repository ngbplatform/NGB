import { beforeEach, describe, expect, it, vi } from 'vitest'

const http = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
}))

vi.mock('@ngbplatform/ui', () => ({
  httpGet: http.get,
  httpPost: http.post,
}))

import {
  applyPayablesBatch,
  getPayablesOpenItemsDetails,
  getPayablesReconciliation,
  suggestPayablesFifoApply,
  unapplyPayablesApply,
} from '../../../src/api/clients/payables'
import {
  applyReceivablesBatch,
  getReceivablesOpenItemsDetails,
  getReceivablesReconciliation,
  suggestLeaseFifoApply,
  unapplyReceivablesApply,
} from '../../../src/api/clients/receivables'
import { bulkCreatePmPropertyUnits, dryRunPmPropertyUnits } from '../../../src/api/clients/pmCatalogs'

describe('property-management API clients', () => {
  beforeEach(() => {
    http.get.mockReset()
    http.post.mockReset()
  })

  it('forwards payables queries and commands without losing optional boundaries', async () => {
    http.get.mockResolvedValue({ kind: 'get' })
    http.post.mockResolvedValue({ kind: 'post' })

    const details = {
      partyId: 'party/1',
      propertyId: 'property-1',
      asOfMonth: null,
      toMonth: '2026-08-01',
    }
    const reconciliation = {
      fromMonthInclusive: '2026-01-01',
      toMonthInclusive: '2026-08-01',
      mode: null,
    }
    const request = { value: 1 } as never

    await expect(getPayablesOpenItemsDetails(details)).resolves.toEqual({ kind: 'get' })
    await expect(suggestPayablesFifoApply(request)).resolves.toEqual({ kind: 'post' })
    await expect(applyPayablesBatch(request)).resolves.toEqual({ kind: 'post' })
    await unapplyPayablesApply('apply/1')
    await expect(getPayablesReconciliation(reconciliation)).resolves.toEqual({ kind: 'get' })

    expect(http.get).toHaveBeenNthCalledWith(1, '/api/payables/open-items/details/page', {
      ...details,
      chargeOffset: undefined,
      creditOffset: undefined,
      allocationOffset: undefined,
      limit: undefined,
    })
    expect(http.post).toHaveBeenNthCalledWith(1, '/api/payables/apply/fifo/suggest', request)
    expect(http.post).toHaveBeenNthCalledWith(2, '/api/payables/apply/batch', request)
    expect(http.post).toHaveBeenNthCalledWith(3, '/api/payables/apply/apply%2F1/unapply', {})
    expect(http.get).toHaveBeenNthCalledWith(2, '/api/payables/reconciliation', {
      ...reconciliation,
      status: undefined,
      offset: undefined,
      limit: undefined,
      cursor: undefined,
    })
  })

  it('forwards receivables queries and commands and propagates transport failures', async () => {
    http.get.mockResolvedValue({ kind: 'get' })
    http.post.mockResolvedValue({ kind: 'post' })

    const details = {
      leaseId: 'lease-1',
      partyId: null,
      propertyId: 'property-1',
      asOfMonth: undefined,
      toMonth: '2026-08-01',
    }
    const reconciliation = {
      fromMonthInclusive: '2026-01-01',
      toMonthInclusive: '2026-08-01',
      mode: 'Movement',
    } as const
    const request = { value: 1 } as never

    await expect(getReceivablesOpenItemsDetails(details)).resolves.toEqual({ kind: 'get' })
    await expect(suggestLeaseFifoApply(request)).resolves.toEqual({ kind: 'post' })
    await expect(applyReceivablesBatch(request)).resolves.toEqual({ kind: 'post' })
    await unapplyReceivablesApply('apply/1')
    await expect(getReceivablesReconciliation(reconciliation)).resolves.toEqual({ kind: 'get' })

    expect(http.get).toHaveBeenNthCalledWith(1, '/api/receivables/open-items/details/page', {
      ...details,
      chargeOffset: undefined,
      creditOffset: undefined,
      allocationOffset: undefined,
      limit: undefined,
    })
    expect(http.post).toHaveBeenNthCalledWith(1, '/api/receivables/apply/fifo/suggest/lease', request)
    expect(http.post).toHaveBeenNthCalledWith(2, '/api/receivables/apply/batch', request)
    expect(http.post).toHaveBeenNthCalledWith(3, '/api/receivables/apply/apply%2F1/unapply', {})
    expect(http.get).toHaveBeenNthCalledWith(2, '/api/receivables/reconciliation', {
      ...reconciliation,
      status: undefined,
      offset: undefined,
      limit: undefined,
      cursor: undefined,
    })

    const failure = new Error('transport failed')
    http.get.mockRejectedValueOnce(failure)
    await expect(getReceivablesOpenItemsDetails(details)).rejects.toBe(failure)
  })

  it('uses regular and dry-run bulk-create endpoints', async () => {
    const request = { buildingId: 'building-1', fromInclusive: 1, toInclusive: 3 }
    http.post.mockResolvedValue({ createdCount: 3 })

    await bulkCreatePmPropertyUnits(request)
    await bulkCreatePmPropertyUnits(request, { dryRun: false })
    await bulkCreatePmPropertyUnits(request, { dryRun: true })
    await dryRunPmPropertyUnits(request)

    expect(http.post.mock.calls).toEqual([
      ['/api/catalogs/pm.property/bulk-create-units', request],
      ['/api/catalogs/pm.property/bulk-create-units', request],
      ['/api/catalogs/pm.property/bulk-create-units?dryRun=true', request],
      ['/api/catalogs/pm.property/bulk-create-units?dryRun=true', request],
    ])
  })
})
