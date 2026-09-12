import { httpDelete, httpGet, httpPost, httpPostFile, type HttpRequestOptions } from '../api/http'
import type { ReportExecutionRequestDto, ReportExecutionResponseDto } from './types'

type ReportRun = { id: string; status: 'Queued' | 'Running' | 'Ready' | 'Failed' | 'Cancelled'; rowCount: number }
const base = (code: string) => `/api/reports/${encodeURIComponent(code)}/runs`

function delay(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const abort = () => { clearTimeout(timer); signal?.removeEventListener('abort', abort); reject(new DOMException('Cancelled', 'AbortError')) }
    const timer = setTimeout(() => { signal?.removeEventListener('abort', abort); resolve() }, ms)
    signal?.addEventListener('abort', abort, { once: true })
    if (signal?.aborted) abort()
  })
}

export async function executeSavedReport(code: string, request: ReportExecutionRequestDto, options?: HttpRequestOptions): Promise<ReportExecutionResponseDto> {
  const run = await httpPost<ReportRun>(base(code), request, options)
  try {
    let status = run.status
    let interval = 250
    while (status === 'Queued' || status === 'Running') {
      await delay(interval, options?.signal)
      status = (await httpGet<ReportRun>(`${base(code)}/${run.id}/status`, undefined, options)).status
      interval = Math.min(2000, interval * 1.5)
    }
    if (status === 'Cancelled') throw new DOMException('Report generation was cancelled.', 'AbortError')
    // A failed result returns the original safe validation error through the normal HTTP error contract.
    if (status !== 'Ready') {
      await readSavedReport(code, run.id, 0, 1, options)
      throw new Error('The report could not be completed. Please run it again.')
    }
    return await readSavedReport(code, run.id, 0, request.limit ?? 200, options)
  } finally {
    if (options?.signal?.aborted) await httpDelete(`${base(code)}/${run.id}`).catch(() => undefined)
  }
}

export async function readSavedReport(code: string, id: string, offset: number, limit: number, options?: HttpRequestOptions): Promise<ReportExecutionResponseDto> {
  return await httpGet<ReportExecutionResponseDto>(`${base(code)}/${encodeURIComponent(id)}?offset=${offset}&limit=${limit}`, undefined, options)
}

export async function exportSavedReport(code: string, id: string, options?: HttpRequestOptions): Promise<{ blob: Blob; fileName: string | null }> {
  const url = `${base(code)}/${encodeURIComponent(id)}/export/xlsx`
  const response = options ? await httpPostFile(url, {}, options) : await httpPostFile(url, {})
  return { blob: response.blob, fileName: response.fileName }
}
