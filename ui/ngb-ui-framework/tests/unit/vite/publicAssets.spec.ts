import { describe, expect, it, vi } from 'vitest'

import { ngbUiFrameworkPublicAssetsPlugin } from '../../../src/vite/publicAssets'

function captureMiddleware(plugin: ReturnType<typeof ngbUiFrameworkPublicAssetsPlugin>) {
  let handler: ((req: { url?: string }, res: {
    statusCode: number
    setHeader: (name: string, value: string) => void
    end: (body?: string | Uint8Array) => void
  }, next: () => void) => void) | undefined
  plugin.configureServer({
    middlewares: {
      use: (nextHandler) => {
        handler = nextHandler
      },
    },
  })
  if (!handler) throw new Error('Public assets middleware was not registered.')
  return handler
}

function response() {
  return {
    statusCode: 0,
    setHeader: vi.fn(),
    end: vi.fn(),
  }
}

describe('NGB UI framework public assets plugin', () => {
  it('serves the framework favicon and silent SSO asset in development', () => {
    const plugin = ngbUiFrameworkPublicAssetsPlugin()
    const handler = captureMiddleware(plugin)

    const faviconResponse = response()
    const faviconNext = vi.fn()
    handler({ url: '/favicon.svg?v=1' }, faviconResponse, faviconNext)
    expect(faviconResponse.statusCode).toBe(200)
    expect(faviconResponse.setHeader).toHaveBeenCalledWith('Content-Type', 'image/svg+xml; charset=utf-8')
    expect(faviconResponse.end).toHaveBeenCalledWith(expect.any(Uint8Array))
    expect(faviconNext).not.toHaveBeenCalled()

    const ssoResponse = response()
    handler({ url: '/silent-check-sso.html' }, ssoResponse, vi.fn())
    expect(ssoResponse.statusCode).toBe(200)
    expect(ssoResponse.setHeader).toHaveBeenCalledWith('Content-Type', 'text/html; charset=utf-8')
    expect(ssoResponse.end).toHaveBeenCalledWith(expect.stringContaining('postMessage'))
  })

  it('delegates missing, omitted, and malformed request URLs', () => {
    const handler = captureMiddleware(ngbUiFrameworkPublicAssetsPlugin())

    for (const url of ['/missing.svg', undefined, 'http://[invalid']) {
      const next = vi.fn()
      const res = response()
      handler({ url }, res, next)
      expect(next).toHaveBeenCalledTimes(1)
      expect(res.end).not.toHaveBeenCalled()
    }
  })

  it('emits normalized custom filenames and safe fallbacks for production bundles', () => {
    const custom = ngbUiFrameworkPublicAssetsPlugin({
      faviconFileName: ' /assets/app-icon.svg ',
      silentCheckSsoFileName: ' ',
    })
    const emitFile = vi.fn()

    custom.generateBundle.call({ emitFile })

    expect(emitFile).toHaveBeenCalledTimes(2)
    expect(emitFile).toHaveBeenNthCalledWith(1, {
      type: 'asset',
      fileName: 'assets/app-icon.svg',
      source: expect.any(Uint8Array),
    })
    expect(emitFile).toHaveBeenNthCalledWith(2, {
      type: 'asset',
      fileName: 'silent-check-sso.html',
      source: expect.any(String),
    })
    expect(custom.name).toBe('@ngbplatform/ui-public-assets')
  })
})
