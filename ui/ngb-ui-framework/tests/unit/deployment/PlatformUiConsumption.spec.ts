import { readFileSync } from 'node:fs'

import { describe, expect, it } from 'vitest'

const uiRoot = new URL('../../../../', import.meta.url)
const platformPackage = '@ngbplatform/ui'
const platformVersion = '2.0.0'
const verticals = [
  'ngb-agency-billing-web',
  'ngb-crm-web',
  'ngb-property-management-web',
  'ngb-trade-web',
] as const

type PackageManifest = {
  name?: string
  version?: string
  workspaces?: string[]
  dependencies?: Record<string, string>
}

describe('platform UI consumption', () => {
  it('keeps every vertical on the same workspace package contract', () => {
    const workspace = readJson(new URL('package.json', uiRoot))
    const framework = readJson(new URL('ngb-ui-framework/package.json', uiRoot))

    expect(framework.name).toBe(platformPackage)
    expect(framework.version).toBe(platformVersion)
    expect(workspace.workspaces).toEqual(expect.arrayContaining(verticals))

    for (const vertical of verticals) {
      const manifest = readJson(new URL(`${vertical}/package.json`, uiRoot))
      expect(manifest.dependencies?.[platformPackage], vertical).toBe(platformVersion)
      expect(manifest.dependencies?.['ngb-ui-framework'], vertical).toBeUndefined()
    }
  })
})

function readJson(url: URL): PackageManifest {
  return JSON.parse(readFileSync(url, 'utf8')) as PackageManifest
}
