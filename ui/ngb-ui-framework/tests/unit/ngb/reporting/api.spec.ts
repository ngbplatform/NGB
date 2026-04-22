import { beforeEach, describe, expect, it, vi } from 'vitest'

const httpMocks = vi.hoisted(() => ({
  httpDelete: vi.fn(),
  httpGet: vi.fn(),
  httpPost: vi.fn(),
  httpPostFile: vi.fn(),
  httpPut: vi.fn(),
}))

vi.mock('../../../../src/ngb/api/http', () => ({
  httpDelete: httpMocks.httpDelete,
  httpGet: httpMocks.httpGet,
  httpPost: httpMocks.httpPost,
  httpPostFile: httpMocks.httpPostFile,
  httpPut: httpMocks.httpPut,
}))

import {
  deleteReportVariant,
  executeReport,
  exportReportXlsx,
  getReportDefinition,
  getReportDefinitions,
  getReportVariant,
  getReportVariants,
  saveReportVariant,
} from '../../../../src/ngb/reporting/api'

describe('reporting api', () => {
  beforeEach(() => {
    httpMocks.httpDelete.mockReset()
    httpMocks.httpGet.mockReset()
    httpMocks.httpPost.mockReset()
    httpMocks.httpPostFile.mockReset()
    httpMocks.httpPut.mockReset()
  })

  it('loads report definitions and executes/export reports with encoded report codes', async () => {
    const executeRequest = {
      parameters: {
        as_of_utc: '2026-04-08',
      },
      filters: null,
      layout: null,
      offset: 0,
      limit: 500,
      cursor: null,
    }
    const exportRequest = {
      parameters: {
        as_of_utc: '2026-04-08',
      },
      filters: null,
      layout: null,
      variantCode: null,
    }

    const blob = new Blob(['xlsx'])

    httpMocks.httpGet
      .mockResolvedValueOnce([{ reportCode: 'pm.occupancy.summary' }])
      .mockResolvedValueOnce({ reportCode: 'pm.occupancy.summary' })
    httpMocks.httpPost.mockResolvedValueOnce({ sheets: [] })
    httpMocks.httpPostFile.mockResolvedValueOnce({
      blob,
      fileName: 'occupancy.xlsx',
      contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    })

    await getReportDefinitions()
    await getReportDefinition('pm/report')
    await executeReport('pm/report', executeRequest as never)
    const exported = await exportReportXlsx('pm/report', exportRequest as never)

    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(1, '/api/report-definitions')
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(2, '/api/report-definitions/pm%2Freport')
    expect(httpMocks.httpPost).toHaveBeenCalledWith('/api/reports/pm%2Freport/execute', executeRequest)
    expect(httpMocks.httpPostFile).toHaveBeenCalledWith('/api/reports/pm%2Freport/export/xlsx', exportRequest)
    expect(exported).toEqual({
      blob,
      fileName: 'occupancy.xlsx',
    })
  })

  it('loads and mutates report variants through encoded variant routes', async () => {
    const variant = {
      reportCode: 'pm/report',
      variantCode: 'default/view',
      name: 'Default View',
      parameters: null,
      filters: null,
      layout: null,
      isDefault: true,
      isShared: false,
    }

    httpMocks.httpGet
      .mockResolvedValueOnce([variant])
      .mockResolvedValueOnce(variant)
    httpMocks.httpPut.mockResolvedValueOnce(variant)
    httpMocks.httpDelete.mockResolvedValueOnce(undefined)

    await getReportVariants('pm/report')
    await getReportVariant('pm/report', 'default/view')
    await saveReportVariant('pm/report', 'default/view', variant as never)
    await deleteReportVariant('pm/report', 'default/view')

    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(1, '/api/reports/pm%2Freport/variants')
    expect(httpMocks.httpGet).toHaveBeenNthCalledWith(2, '/api/reports/pm%2Freport/variants/default%2Fview')
    expect(httpMocks.httpPut).toHaveBeenCalledWith('/api/reports/pm%2Freport/variants/default%2Fview', variant)
    expect(httpMocks.httpDelete).toHaveBeenCalledWith('/api/reports/pm%2Freport/variants/default%2Fview')
  })
})
