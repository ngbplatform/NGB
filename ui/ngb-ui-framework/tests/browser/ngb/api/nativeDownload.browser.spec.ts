import { afterEach, expect, test, vi } from 'vitest'
const auth = vi.hoisted(() => ({ getAccessToken: vi.fn().mockResolvedValue('test-token') }))
vi.mock('../../../../src/ngb/auth/keycloak', () => ({ ...auth, forceRefreshAccessToken: vi.fn() }))
import { httpPostNativeDownload } from '../../../../src/ngb/api/http'

afterEach(() => { vi.restoreAllMocks(); vi.useRealTimers(); document.querySelectorAll('iframe[title="Report download"]').forEach(e => e.remove()) })

test('submits only a small authenticated POST and keeps at most one frame with no token left in the DOM', async () => {
  let submissions = 0
  const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(function () {
    const form = this as HTMLFormElement
    expect(form.method).toBe('post')
    expect(form.action).toBe(`${window.location.origin}/api/reports/test/export/xlsx/form`)
    expect(form.action).not.toContain('test-token')
    const fields = new FormData(form)
    expect(fields.get('access_token')).toBe('test-token')
    expect(JSON.parse(String(fields.get('request')))).toEqual({ parameters: { from_utc: '2026-01-01' } })
    expect(document.querySelectorAll('iframe[title="Report download"]')).toHaveLength(1)
    submissions++
  })
  const blob = vi.spyOn(Response.prototype, 'blob')
  const payload = { parameters: { from_utc: '2026-01-01' } }
  await httpPostNativeDownload('/api/reports/test/export/xlsx/form', payload)
  await httpPostNativeDownload('/api/reports/test/export/xlsx/form', payload)
  expect(submissions).toBe(2)
  expect(submit).toHaveBeenCalledTimes(2)
  expect(document.querySelector('input[name="access_token"]')).toBeNull()
  expect(blob).not.toHaveBeenCalled()
})

test('rejects cancellation and cross-origin token delivery before submitting', async () => {
  const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
  const controller = new AbortController()
  controller.abort()
  await expect(httpPostNativeDownload('/api/reports/test/export/xlsx/form', {}, { signal: controller.signal })).rejects.toThrow()
  await expect(httpPostNativeDownload('https://other.example/export', {})).rejects.toThrow('same-origin')
  expect(submit).not.toHaveBeenCalled()
})

test('surfaces a server problem response and releases its frame', async () => {
  vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
  const onError = vi.fn()
  await httpPostNativeDownload('/api/reports/test/export/xlsx/form', {}, { onError })
  const frame = document.querySelector('iframe[title="Report download"]') as HTMLIFrameElement
  frame.contentDocument!.body.textContent = JSON.stringify({ status: 403, detail: 'Export permission is required.' })
  frame.dispatchEvent(new Event('load'))
  expect(onError).toHaveBeenCalledWith(expect.objectContaining({ message: 'Export permission is required.' }))
  expect(frame.isConnected).toBe(false)
})
