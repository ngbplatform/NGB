import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

import { createPlatformUiPackageManifest } from '../../../../scripts/platform-ui-package-manifest.mjs'

describe('published @ngbplatform/ui manifest', () => {
  it('publishes every supported package entrypoint with its source file included', () => {
    const packageRoot = resolve(process.cwd(), 'ngb-ui-framework')
    const sourceManifest = JSON.parse(readFileSync(resolve(packageRoot, 'package.json'), 'utf8'))
    const published = createPlatformUiPackageManifest(sourceManifest)

    expect(published.exports['./contracts']).toEqual({
      types: './src/contracts.ts',
      import: './src/contracts.ts',
      default: './src/contracts.ts',
    })
    expect(published.files).toContain('src')
    expect(Object.keys(published.exports).sort()).toEqual(Object.keys(sourceManifest.exports).sort())

    for (const subpath of ['./contracts', './editor', './layout', './lazy', './navigation', './work-center']) {
      const publishedExport = published.exports[subpath]
      expect(publishedExport, `${subpath} must be part of the published public API`).toBeDefined()
      const sourcePath = typeof publishedExport === 'string' ? publishedExport : publishedExport.import
      expect(existsSync(resolve(packageRoot, sourcePath)), `${subpath} must point to a packaged source entrypoint`).toBe(true)
    }
  })
})
