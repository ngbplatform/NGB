import { afterEach, beforeEach, expect, it, vi } from 'vitest'
const http = vi.hoisted(() => ({ httpGet: vi.fn(), httpPost: vi.fn(), httpDelete: vi.fn(), httpPostFile: vi.fn() }))
vi.mock('../../../../src/ngb/api/http', () => http)
import { executeSavedReport, readSavedReport, exportSavedReport } from '../../../../src/ngb/reporting/savedRuns'

beforeEach(() => { vi.useFakeTimers(); Object.values(http).forEach(mock => mock.mockReset()); http.httpDelete.mockResolvedValue(undefined) })
afterEach(() => vi.useRealTimers())

it('waits for durable completion, then reads a bounded page', async () => {
  http.httpPost.mockResolvedValue({ id: 'run', status: 'Queued' })
  http.httpGet.mockResolvedValueOnce({ status: 'Running' }).mockResolvedValueOnce({ status: 'Ready' }).mockResolvedValueOnce({ total: 15000 })
  const pending = executeSavedReport('accounting/trial', { limit: 200 })
  await vi.runAllTimersAsync()
  await expect(pending).resolves.toEqual({ total: 15000 })
  expect(http.httpGet).toHaveBeenLastCalledWith('/api/reports/accounting%2Ftrial/runs/run?offset=0&limit=200', undefined, undefined)
})

it('cancels a running job when the user stops waiting', async () => {
  const controller = new AbortController()
  http.httpPost.mockResolvedValue({ id: 'run', status: 'Running' })
  const pending = executeSavedReport('tb', {}, { signal: controller.signal })
  const assertion = expect(pending).rejects.toMatchObject({ name: 'AbortError' })
  await vi.advanceTimersByTimeAsync(1)
  controller.abort()
  await assertion
  expect(http.httpDelete).toHaveBeenCalledWith('/api/reports/tb/runs/run')
})

it.each(['Failed', 'Cancelled'])('does not display a partial %s result', async status => {
  http.httpPost.mockResolvedValue({ id: 'run', status })
  await expect(executeSavedReport('tb', {})).rejects.toThrow()
  if (status === 'Cancelled') expect(http.httpGet).not.toHaveBeenCalled()
})

it('preserves a useful validation error returned by the background worker', async () => {
  http.httpPost.mockResolvedValue({ id: 'run', status: 'Failed' })
  http.httpGet.mockRejectedValue(new Error('Select a building, not a unit.'))
  await expect(executeSavedReport('pm.occupancy.summary', {})).rejects.toThrow('Select a building, not a unit.')
})

it('supports pages beyond 10000 and exports the same result ID', async () => {
  http.httpGet.mockResolvedValue({ offset: 12000 })
  http.httpPostFile.mockResolvedValue({ blob: new Blob(['xlsx']), fileName: 'tb.xlsx' })
  await expect(readSavedReport('tb', 'run', 12000, 200)).resolves.toEqual({ offset: 12000 })
  await expect(exportSavedReport('tb', 'run')).resolves.toMatchObject({ fileName: 'tb.xlsx' })
  expect(http.httpPostFile).toHaveBeenCalledWith('/api/reports/tb/runs/run/export/xlsx', {})
})
