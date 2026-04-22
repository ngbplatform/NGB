import { readFileSync } from 'node:fs'

function normalizePublicFileName(value, fallback) {
  const normalized = String(value ?? '').trim().replace(/^\/+/, '')
  return normalized || fallback
}

function readUiFrameworkPublicAsset(relativePath, encoding) {
  const url = new URL(`./public/${relativePath}`, import.meta.url)
  return encoding ? readFileSync(url, encoding) : readFileSync(url)
}

function buildPublicAssets(options) {
  return [
    {
      requestPath: '/favicon.svg',
      fileName: normalizePublicFileName(options.faviconFileName, 'favicon.svg'),
      contentType: 'image/svg+xml; charset=utf-8',
      source: readUiFrameworkPublicAsset('favicon.svg'),
    },
    {
      requestPath: '/silent-check-sso.html',
      fileName: normalizePublicFileName(options.silentCheckSsoFileName, 'silent-check-sso.html'),
      contentType: 'text/html; charset=utf-8',
      source: readUiFrameworkPublicAsset('silent-check-sso.html', 'utf8'),
    },
  ]
}

function requestPathname(url) {
  try {
    return new URL(url ?? '/', 'http://localhost').pathname
  } catch {
    return '/'
  }
}

export function ngbUiFrameworkPublicAssetsPlugin(options = {}) {
  const assets = buildPublicAssets(options)

  return {
    name: 'ngb-ui-framework-public-assets',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const pathname = requestPathname(req.url)
        const asset = assets.find((entry) => entry.requestPath === pathname)

        if (!asset) {
          next()
          return
        }

        res.statusCode = 200
        res.setHeader('Content-Type', asset.contentType)
        res.end(asset.source)
      })
    },
    generateBundle() {
      for (const asset of assets) {
        this.emitFile({
          type: 'asset',
          fileName: asset.fileName,
          source: asset.source,
        })
      }
    },
  }
}
