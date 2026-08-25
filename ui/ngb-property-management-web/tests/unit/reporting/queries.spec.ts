import { beforeEach, describe, expect, it, vi } from 'vitest'

const reporting = vi.hoisted(() => ({ execute: vi.fn() }))

vi.mock('@ngbplatform/ui', () => ({
  executeReport: reporting.execute,
  ReportRowKind: { Detail: 1, Total: 2, Subtotal: 3 },
}))

import { getPmBuildingSummary } from '../../../src/reporting/queries'

function response(rows: Array<{ rowKind: unknown; values?: unknown[]; displays?: unknown[] }>) {
  return {
    sheet: {
      rows: rows.map((row) => ({
        rowKind: row.rowKind,
        cells: (row.values ?? []).map((value, index) => ({ value, display: row.displays?.[index] })),
      })),
    },
  }
}

describe('PM reporting queries', () => {
  beforeEach(() => reporting.execute.mockReset())

  it('maps detail data and optional as-of parameters', async () => {
    reporting.execute.mockResolvedValue(response([
      { rowKind: 2, values: [0, 0, 0, 0, 0, 0] },
      { rowKind: 1, values: [0, 0, 10, 8, 2, 20], displays: ['Building A', '2026-08-23'] },
    ]))

    await expect(getPmBuildingSummary('building-1', { asOfUtc: '2026-08-23' })).resolves.toEqual({
      buildingDisplay: 'Building A', asOfUtc: '2026-08-23', totalUnits: 10,
      occupiedUnits: 8, vacantUnits: 2, vacancyPercent: 20,
    })
    expect(reporting.execute).toHaveBeenCalledWith('pm.building.summary', {
      filters: { building_id: { value: 'building-1' } },
      parameters: { as_of_utc: '2026-08-23' }, limit: 2, offset: 0,
    })
  })

  it.each([1, 'Detail', 'detail'])('recognizes detail row kind %s', async (rowKind) => {
    reporting.execute.mockResolvedValue(response([
      { rowKind, values: [null, null, 'bad', undefined, null, ''], displays: [null, null] },
    ]))
    await expect(getPmBuildingSummary('building-1')).resolves.toEqual({
      buildingDisplay: '', asOfUtc: '', totalUnits: 0, occupiedUnits: 0, vacantUnits: 0, vacancyPercent: 0,
    })
    expect(reporting.execute.mock.calls[0][1].parameters).toBeUndefined()
  })

  it.each([2, 'Total', 'total', 3, 'Subtotal', 'subtotal'])('skips total-like row kind %s', async (rowKind) => {
    reporting.execute.mockResolvedValue(response([
      { rowKind, values: [0, 0, 0, 0, 0, 0] },
      { rowKind: 'ordinary', values: [0, 0, 1, 1, 0, 0], displays: ['Fallback', 'date'] },
    ]))
    await expect(getPmBuildingSummary('building-1')).resolves.toMatchObject({ buildingDisplay: 'Fallback', totalUnits: 1 })
  })

  it('rejects empty and malformed report responses', async () => {
    reporting.execute.mockResolvedValueOnce({ sheet: { rows: null } })
    await expect(getPmBuildingSummary('building-1')).rejects.toThrow('pm.building.summary: unexpected response')
    reporting.execute.mockResolvedValueOnce(response([]))
    await expect(getPmBuildingSummary('building-1')).rejects.toThrow('pm.building.summary: unexpected response')
    reporting.execute.mockResolvedValueOnce(response([{ rowKind: 'ordinary', values: [1, 2, 3] }]))
    await expect(getPmBuildingSummary('building-1')).rejects.toThrow('pm.building.summary: unexpected response')
  })
})
