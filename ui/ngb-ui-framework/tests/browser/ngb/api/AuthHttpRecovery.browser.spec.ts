import { page } from 'vitest/browser'
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import { render } from 'vitest-browser-vue'
import { defineComponent, h, ref } from 'vue'

const authHttpMocks = vi.hoisted(() => ({
  forceRefreshAccessToken: vi.fn(),
  getAccessToken: vi.fn(),
}))

vi.mock('../../../../src/ngb/auth/keycloak', () => ({
  forceRefreshAccessToken: authHttpMocks.forceRefreshAccessToken,
  getAccessToken: authHttpMocks.getAccessToken,
}))

import { httpGet, httpPostFile } from '../../../../src/ngb/api/http'

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })

  return { promise, resolve, reject }
}

const JsonRequestHarness = defineComponent({
  setup() {
    const state = ref('idle')
    const payload = ref('none')

    async function loadFrameworkState() {
      state.value = 'loading'
      payload.value = 'none'

      try {
        const response = await httpGet<{ label: string }>('/api/framework/state')
        state.value = 'loaded'
        payload.value = response.label
      } catch (error) {
        state.value = 'error'
        payload.value = error instanceof Error ? error.message : String(error)
      }
    }

    return () => h('div', { class: 'space-y-4 p-6' }, [
      h('button', {
        type: 'button',
        disabled: state.value === 'loading',
        onClick: () => {
          void loadFrameworkState()
        },
      }, 'Load framework state'),
      h('div', { 'data-testid': 'json-state' }, state.value),
      h('div', { 'data-testid': 'json-payload' }, payload.value),
    ])
  },
})

const FileExportHarness = defineComponent({
  setup() {
    const state = ref('idle')
    const fileName = ref('none')

    async function exportFrameworkWorkbook() {
      state.value = 'loading'
      fileName.value = 'none'

      try {
        const file = await httpPostFile('/api/framework/report/export', { month: '2026-04' })
        const url = URL.createObjectURL(file.blob)
        const link = document.createElement('a')
        link.href = url
        link.download = file.fileName ?? 'framework-export.xlsx'
        document.body.appendChild(link)
        link.click()
        link.remove()
        URL.revokeObjectURL(url)

        state.value = 'done'
        fileName.value = link.download
      } catch (error) {
        state.value = 'error'
        fileName.value = error instanceof Error ? error.message : String(error)
      }
    }

    return () => h('div', { class: 'space-y-4 p-6' }, [
      h('button', {
        type: 'button',
        disabled: state.value === 'loading',
        onClick: () => {
          void exportFrameworkWorkbook()
        },
      }, 'Export framework workbook'),
      h('div', { 'data-testid': 'export-state' }, state.value),
      h('div', { 'data-testid': 'export-file-name' }, fileName.value),
    ])
  },
})

describe('auth http recovery', () => {
  const fetchMock = vi.fn()

  beforeEach(() => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://api.example')
    vi.stubGlobal('fetch', fetchMock)
    authHttpMocks.getAccessToken.mockReset()
    authHttpMocks.forceRefreshAccessToken.mockReset()
    fetchMock.mockReset()
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  test('keeps the UI loading while a 401 refreshes the token and retries a json request', async () => {
    await page.viewport(1280, 900)

    const retryResponse = createDeferred<Response>()

    authHttpMocks.getAccessToken
      .mockResolvedValueOnce('expired-token')
      .mockResolvedValueOnce('fresh-token')
    authHttpMocks.forceRefreshAccessToken.mockResolvedValueOnce('fresh-token')

    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Unauthorized' }), {
        status: 401,
        headers: {
          'content-type': 'application/json',
        },
      }))
      .mockImplementationOnce(async () => await retryResponse.promise)

    const view = await render(JsonRequestHarness)

    const loadButton = view.getByRole('button', { name: 'Load framework state' })
    await loadButton.click()

    await expect.element(view.getByTestId('json-state')).toHaveTextContent('loading')
    await expect.poll(() => fetchMock.mock.calls.length).toBe(2)

    expect(authHttpMocks.forceRefreshAccessToken).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[1]?.[1]).toEqual(expect.objectContaining({
      method: 'GET',
      headers: expect.objectContaining({
        Authorization: 'Bearer fresh-token',
      }),
    }))

    retryResponse.resolve(new Response(JSON.stringify({ label: 'Recovered framework state' }), {
      status: 200,
      headers: {
        'content-type': 'application/json',
      },
    }))

    await expect.element(view.getByTestId('json-state')).toHaveTextContent('loaded')
    await expect.element(view.getByTestId('json-payload')).toHaveTextContent('Recovered framework state')
  })

  test('disables export ui while a 401 refreshes the token and retries the file download', async () => {
    await page.viewport(1280, 900)

    const retryResponse = createDeferred<Response>()
    const createObjectUrlSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:framework-export')
    const revokeObjectUrlSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    const anchorClickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    authHttpMocks.getAccessToken
      .mockResolvedValueOnce('expired-token')
      .mockResolvedValueOnce('fresh-token')
    authHttpMocks.forceRefreshAccessToken.mockResolvedValueOnce('fresh-token')

    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'Unauthorized' }), {
        status: 401,
        headers: {
          'content-type': 'application/json',
        },
      }))
      .mockImplementationOnce(async () => await retryResponse.promise)

    const view = await render(FileExportHarness)
    const exportButton = view.getByRole('button', { name: 'Export framework workbook' })

    await exportButton.click()

    await expect.element(view.getByTestId('export-state')).toHaveTextContent('loading')
    expect((exportButton.element() as HTMLButtonElement).disabled).toBe(true)
    await expect.poll(() => fetchMock.mock.calls.length).toBe(2)

    expect(authHttpMocks.forceRefreshAccessToken).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[1]?.[1]).toEqual(expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({
        Authorization: 'Bearer fresh-token',
      }),
    }))

    retryResponse.resolve(new Response('xlsx-bytes', {
      status: 200,
      headers: {
        'content-type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        'content-disposition': "attachment; filename*=UTF-8''framework%20snapshot.xlsx",
      },
    }))

    await expect.element(view.getByTestId('export-state')).toHaveTextContent('done')
    await expect.element(view.getByTestId('export-file-name')).toHaveTextContent('framework snapshot.xlsx')
    expect((exportButton.element() as HTMLButtonElement).disabled).toBe(false)
    expect(createObjectUrlSpy).toHaveBeenCalledTimes(1)
    expect(anchorClickSpy).toHaveBeenCalledTimes(1)
    expect(revokeObjectUrlSpy).toHaveBeenCalledWith('blob:framework-export')
  })
})
