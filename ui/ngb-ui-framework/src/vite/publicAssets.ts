import { readFileSync } from 'node:fs'

type DevRequestLike = {
  url?: string
}

type DevResponseLike = {
  statusCode: number
  setHeader: (name: string, value: string) => void
  end: (body?: string | Uint8Array) => void
}

type DevServerLike = {
  middlewares: {
    use: (handler: (req: DevRequestLike, res: DevResponseLike, next: () => void) => void) => void
  }
}

type AssetEmitterLike = {
  emitFile: (asset: {
    type: 'asset'
    fileName: string
    source: string | Uint8Array
  }) => void
}

type PublicAssetDefinition = {
  requestPath: string
  fileName: string
  contentType: string
  source: string | Uint8Array
}

export type NgbUiFrameworkPublicAssetsPluginOptions = {
  faviconFileName?: string
  silentCheckSsoFileName?: string
}

function normalizePublicFileName(value: string | null | undefined, fallback: string): string {
  const normalized = String(value ?? '').trim().replace(/^\/+/, '')
  return normalized || fallback
}

function readUiFrameworkPublicAsset(relativePath: string, encoding?: BufferEncoding): string | Uint8Array {
  const url = new URL(`../../public/${relativePath}`, import.meta.url)
  return encoding ? readFileSync(url, encoding) : readFileSync(url)
}

function buildPublicAssets(options: NgbUiFrameworkPublicAssetsPluginOptions): PublicAssetDefinition[] {
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

function requestPathname(url: string | undefined): string {
  try {
    return new URL(url ?? '/', 'http://localhost').pathname
  } catch {
    return '/'
  }
}

export function ngbUiFrameworkPublicAssetsPlugin(options: NgbUiFrameworkPublicAssetsPluginOptions = {}) {
  const assets = buildPublicAssets(options)

  return {
    name: 'ngb-ui-framework-public-assets',
    configureServer(server: DevServerLike) {
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
    generateBundle(this: AssetEmitterLike) {
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
