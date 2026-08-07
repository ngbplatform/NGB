import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

const uiRoot = existsSync(resolve(process.cwd(), 'ngb-ui-framework'))
  ? process.cwd()
  : resolve(process.cwd(), 'ui')
const frameworkSource = resolve(uiRoot, 'ngb-ui-framework/src')
const verticalRoots = [
  'ngb-agency-billing-web',
  'ngb-crm-web',
  'ngb-property-management-web',
  'ngb-trade-web',
].map((directory) => resolve(uiRoot, directory))

function sourceFiles(root: string): string[] {
  const result: string[] = []
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    if (entry.name === 'dist' || entry.name === 'node_modules' || entry.name === 'coverage') continue
    const path = resolve(root, entry.name)
    if (entry.isDirectory()) result.push(...sourceFiles(path))
    else if (/\.(?:ts|vue)$/.test(entry.name) && !entry.name.endsWith('.d.ts')) result.push(path)
  }
  return result
}

function imports(source: string): string[] {
  const values: string[] = []
  const pattern = /(?:from\s*|import\s*\()\s*['"]([^'"]+)['"]/g
  for (const match of source.matchAll(pattern)) values.push(match[1]!)
  return values
}

function relative(path: string): string {
  return path.slice(uiRoot.length + 1)
}

describe('frontend architecture boundaries', () => {
  it('keeps every vertical and cross-vertical test on the public platform package surface', () => {
    const consumers = [
      ...verticalRoots.flatMap(sourceFiles),
      ...sourceFiles(resolve(uiRoot, 'tests/e2e')),
    ]
    const violations = consumers.flatMap((file) => imports(readFileSync(file, 'utf8'))
      .filter((specifier) => specifier.includes('ngb-ui-framework/src'))
      .map((specifier) => `${relative(file)} -> ${specifier}`))

    expect(violations, 'Consumers must import @ngbplatform/ui instead of framework internals.').toEqual([])
  })

  it('keeps Work Center presentation and state independent from editor internals', () => {
    const workCenterRoot = resolve(frameworkSource, 'ngb/work-center')
    const violations = sourceFiles(workCenterRoot).flatMap((file) => imports(readFileSync(file, 'utf8'))
      .filter((specifier) => /(?:^|\/)editor(?:\/|$)/.test(specifier))
      .map((specifier) => `${relative(file)} -> ${specifier}`))

    expect(violations).toEqual([])
  })

  it('keeps Work Center core behind transport, authentication, environment, and realtime adapters', () => {
    const workCenterRoot = resolve(frameworkSource, 'ngb/work-center')
    const adapterFiles = new Set(['api.ts', 'defaultConfig.ts', 'signalr.ts'])
    const violations = sourceFiles(workCenterRoot)
      .filter((file) => !adapterFiles.has(file.split('/').at(-1)!))
      .flatMap((file) => imports(readFileSync(file, 'utf8'))
        .filter((specifier) =>
          specifier === '@microsoft/signalr'
          || /(?:^|\/)(?:api|auth|env)(?:\/|$)/.test(specifier))
        .map((specifier) => `${relative(file)} -> ${specifier}`))

    expect(violations).toEqual([])
  })

  it('keeps transport APIs independent from editor, site, and Work Center presentation layers', () => {
    const apiRoot = resolve(frameworkSource, 'ngb/api')
    const violations = sourceFiles(apiRoot).flatMap((file) => imports(readFileSync(file, 'utf8'))
      .filter((specifier) => /(?:^|\/)(?:editor|site|work-center)(?:\/|$)/.test(specifier))
      .map((specifier) => `${relative(file)} -> ${specifier}`))

    expect(violations).toEqual([])
  })

  it('keeps editor workflows behind injected gateways and application-owned confirmation UI', () => {
    const editorRoot = resolve(frameworkSource, 'ngb/editor')
    const editorFiles = sourceFiles(editorRoot)
    const concreteApiImports = editorFiles.flatMap((file) => imports(readFileSync(file, 'utf8'))
      .filter((specifier) => specifier.endsWith('/api/documents') || specifier === '../api/documents')
      .map((specifier) => `${relative(file)} -> ${specifier}`))
    const browserDialogs = editorFiles
      .filter((file) => /\bwindow\.(?:confirm|prompt)\s*\(/.test(readFileSync(file, 'utf8')))
      .map(relative)

    expect(concreteApiImports).toEqual([])
    expect(browserDialogs).toEqual([])
  })

  it('keeps vertical document persistence adapters limited to load and save concerns', () => {
    const persistenceFiles = verticalRoots.flatMap((root) => sourceFiles(resolve(root, 'src/editor')))
      .filter((file) => /(?:Persistence|persistenceContext)\.(?:ts|vue)$/.test(file))
    const forbiddenLifecycleCalls = /\b(?:executeDocumentAction|postDocument|unpostDocument|repostDocument|markDocumentForDeletion|unmarkDocumentForDeletion)\b/
    const violations = persistenceFiles
      .filter((file) => forbiddenLifecycleCalls.test(readFileSync(file, 'utf8')))
      .map(relative)

    expect(violations).toEqual([])
  })

  it('keeps Node-side E2E fixtures on the runtime-safe contracts subpath', () => {
    const e2eRoot = resolve(uiRoot, 'tests/e2e')
    const violations = sourceFiles(e2eRoot).flatMap((file) => imports(readFileSync(file, 'utf8'))
      .filter((specifier) => specifier === '@ngbplatform/ui')
      .map((specifier) => `${relative(file)} -> ${specifier}`))

    expect(violations).toEqual([])
  })

  it('requires every vertical bootstrap to install navigation and Work Center adapters', () => {
    const violations = verticalRoots.flatMap((root) => {
      const main = resolve(root, 'src/main.ts')
      const source = readFileSync(main, 'utf8')
      const missing = [
        source.includes('configureNgbNavigation') ? null : 'configureNgbNavigation',
        source.includes('configureNgbWorkCenter') ? null : 'configureNgbWorkCenter',
      ].filter((value): value is string => value !== null)
      return missing.map((name) => `${relative(main)} is missing ${name}`)
    })

    expect(violations).toEqual([])
  })
})
