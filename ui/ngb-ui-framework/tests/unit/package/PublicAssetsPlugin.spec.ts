import { readFileSync } from 'node:fs'

import { describe, expect, it, vi } from 'vitest'

import { ngbUiFrameworkPublicAssetsPlugin } from 'ngb-ui-framework/vite-public-assets'

type DevHandler = (req: { url?: string }, res: {
  statusCode: number
  setHeader: (name: string, value: string) => void
  end: (body?: string | Uint8Array) => void
}, next: () => void) => void

function frameworkPublicAsset(relativePath: string, encoding?: BufferEncoding): string | Uint8Array {
  const url = new URL(`../../../public/${relativePath}`, import.meta.url)
  return encoding ? readFileSync(url, encoding) : readFileSync(url)
}

function createDevServerHarness() {
  const handlers: DevHandler[] = []

  return {
    handlers,
    server: {
      middlewares: {
        use(handler: DevHandler) {
          handlers.push(handler)
        },
      },
    },
  }
}

function createDevResponseHarness() {
  let body: string | Uint8Array | undefined
  const headers = new Map<string, string>()

  const response = {
    statusCode: 0,
    setHeader(name: string, value: string) {
      headers.set(name, value)
    },
    end(payload?: string | Uint8Array) {
      body = payload
    },
  }

  return {
    response,
    headers,
    body: () => body,
  }
}

describe('public assets plugin', () => {
  it('serves the framework favicon and silent-check document through the dev middleware', () => {
    const plugin = ngbUiFrameworkPublicAssetsPlugin()
    const server = createDevServerHarness()

    plugin.configureServer(server.server)
    expect(server.handlers).toHaveLength(1)

    const handler = server.handlers[0]

    const faviconResponse = createDevResponseHarness()
    const faviconNext = vi.fn()
    handler({ url: '/favicon.svg?cacheBust=1' }, faviconResponse.response, faviconNext)

    expect(faviconNext).not.toHaveBeenCalled()
    expect(faviconResponse.response.statusCode).toBe(200)
    expect(faviconResponse.headers.get('Content-Type')).toBe('image/svg+xml; charset=utf-8')
    expect(Buffer.from(faviconResponse.body() as Uint8Array)).toEqual(
      Buffer.from(frameworkPublicAsset('favicon.svg') as Uint8Array),
    )

    const silentCheckResponse = createDevResponseHarness()
    const silentCheckNext = vi.fn()
    handler({ url: '/silent-check-sso.html#fragment' }, silentCheckResponse.response, silentCheckNext)

    expect(silentCheckNext).not.toHaveBeenCalled()
    expect(silentCheckResponse.response.statusCode).toBe(200)
    expect(silentCheckResponse.headers.get('Content-Type')).toBe('text/html; charset=utf-8')
    expect(silentCheckResponse.body()).toBe(frameworkPublicAsset('silent-check-sso.html', 'utf8'))

    const unknownResponse = createDevResponseHarness()
    const unknownNext = vi.fn()
    handler({ url: '/other-asset.txt' }, unknownResponse.response, unknownNext)

    expect(unknownNext).toHaveBeenCalledTimes(1)
    expect(unknownResponse.response.statusCode).toBe(0)
    expect(unknownResponse.body()).toBeUndefined()
  })

  it('emits both assets into the build with normalized custom filenames', () => {
    const plugin = ngbUiFrameworkPublicAssetsPlugin({
      faviconFileName: '/branding/favicon.svg',
      silentCheckSsoFileName: ' auth/silent-check-sso.html ',
    })

    const emitted: Array<{
      type: 'asset'
      fileName: string
      source: string | Uint8Array
    }> = []

    plugin.generateBundle.call({
      emitFile(asset: {
        type: 'asset'
        fileName: string
        source: string | Uint8Array
      }) {
        emitted.push(asset)
      },
    })

    expect(emitted).toEqual([
      {
        type: 'asset',
        fileName: 'branding/favicon.svg',
        source: frameworkPublicAsset('favicon.svg'),
      },
      {
        type: 'asset',
        fileName: 'auth/silent-check-sso.html',
        source: frameworkPublicAsset('silent-check-sso.html', 'utf8'),
      },
    ])
  })
})
