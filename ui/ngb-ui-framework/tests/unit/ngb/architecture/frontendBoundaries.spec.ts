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
const verticalNames = verticalRoots.map((root) => root.split('/').at(-1)!)

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

  it('prevents every vertical source and test suite from importing sibling vertical internals', () => {
    const violations = verticalRoots.flatMap((root, index) => {
      const siblingNames = verticalNames.filter((_, siblingIndex) => siblingIndex !== index)
      return sourceFiles(root).flatMap((file) => imports(readFileSync(file, 'utf8'))
        .filter((specifier) => siblingNames.some((name) => specifier.includes(name)))
        .map((specifier) => `${relative(file)} -> ${specifier}`))
    })

    expect(violations, 'Verticals may collaborate only through platform contracts and public package entrypoints.').toEqual([])
  })

  it('keeps Work Center presentation and state independent from editor internals', () => {
    const workCenterRoot = resolve(frameworkSource, 'ngb/work-center')
    const violations = sourceFiles(workCenterRoot).flatMap((file) => imports(readFileSync(file, 'utf8'))
      .filter((specifier) => /(?:^|\/)editor(?:\/|$)/.test(specifier))
      .map((specifier) => `${relative(file)} -> ${specifier}`))

    expect(violations).toEqual([])
  })

  it('keeps Work Center independent from the site shell composition layer', () => {
    const workCenterRoot = resolve(frameworkSource, 'ngb/work-center')
    const violations = sourceFiles(workCenterRoot).flatMap((file) => imports(readFileSync(file, 'utf8'))
      .filter((specifier) => /(?:^|\/)site(?:\/|$)/.test(specifier))
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

  it('keeps wire contracts as dependency-free transport data', () => {
    const contracts = resolve(frameworkSource, 'ngb/api/contracts.ts')
    expect(imports(readFileSync(contracts, 'utf8'))).toEqual([])
  })

  it('prevents known directory-level dependency cycles', () => {
    const boundaries = [
      {
        file: resolve(frameworkSource, 'ngb/router/routeAliases.ts'),
        forbidden: /(?:^|\/)(?:accounting|reporting)(?:\/|$)/,
      },
      {
        file: resolve(frameworkSource, 'ngb/reporting/config.ts'),
        forbidden: /(?:^|\/)(?:accounting|editor|router)(?:\/|$)/,
      },
      {
        file: resolve(frameworkSource, 'ngb/command-palette/store.ts'),
        forbidden: /(?:^|\/)site(?:\/|$)/,
      },
    ]
    const violations = boundaries.flatMap(({ file, forbidden }) => imports(readFileSync(file, 'utf8'))
      .filter((specifier) => forbidden.test(specifier))
      .map((specifier) => `${relative(file)} -> ${specifier}`))

    expect(violations).toEqual([])
  })

  it('keeps editor workflows behind injected gateways and application-owned confirmation UI', () => {
    const editorRoot = resolve(frameworkSource, 'ngb/editor')
    const editorFiles = sourceFiles(editorRoot)
    const infrastructureAdapters = new Set(['entityEditorPersistence.ts'])
    const concreteApiImports = editorFiles.flatMap((file) => imports(readFileSync(file, 'utf8'))
      .filter((specifier) =>
        !infrastructureAdapters.has(file.split('/').at(-1)!)
        && (specifier.endsWith('/api/documents') || specifier === '../api/documents'))
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

  it('keeps standard vertical persistence adapters as thin policy composition', () => {
    const standardVerticalRoots = verticalRoots.filter((root) => !root.endsWith('ngb-property-management-web'))
    const wrappers = standardVerticalRoots.flatMap((root) => [
      resolve(root, 'src/editor/useCatalogEntityEditorPersistence.ts'),
      resolve(root, 'src/editor/useDocumentEntityEditorPersistence.ts'),
    ])
    const violations = wrappers.flatMap((file) => {
      const source = readFileSync(file, 'utf8')
      const lines = source.split(/\r?\n/).length
      const isCatalog = file.endsWith('/useCatalogEntityEditorPersistence.ts')
      const factory = isCatalog
        ? 'createConfiguredCatalogEntityEditorPersistence'
        : 'createConfiguredDocumentEntityEditorPersistence'
      return [
        source.includes(factory) ? null : `${relative(file)} must delegate to ${factory}`,
        lines <= 55 ? null : `${relative(file)} contains ${lines} lines; transport orchestration belongs in the platform adapter`,
      ].filter((value): value is string => value !== null)
    })

    expect(violations).toEqual([])
  })

  it('keeps vertical persistence adapters independent from router, UI feedback, and concrete stores', () => {
    const persistenceFiles = verticalRoots.flatMap((root) => sourceFiles(resolve(root, 'src/editor')))
      .filter((file) => /(?:Persistence|persistenceContext)\.(?:ts|vue)$/.test(file))
    const forbiddenImport = /(?:^vue-router$|(?:^|\/)(?:primitives\/toast|metadata\/store|lookup\/store)$)/
    const forbiddenIdentifiers = /\b(?:useToasts|useMetadataStore|useLookupStore)\b/
    const violations = persistenceFiles.flatMap((file) => {
      const source = readFileSync(file, 'utf8')
      const importViolations = imports(source)
        .filter((specifier) => forbiddenImport.test(specifier))
        .map((specifier) => `${relative(file)} -> ${specifier}`)
      if (forbiddenIdentifiers.test(source)) importViolations.push(`${relative(file)} -> concrete UI/store dependency`)
      return importViolations
    })

    expect(violations).toEqual([])
  })

  it('keeps standard vertical editor shells as thin platform configurations', () => {
    const wrappers = [
      resolve(uiRoot, 'ngb-agency-billing-web/src/editor/AgencyBillingEntityEditor.vue'),
      resolve(uiRoot, 'ngb-crm-web/src/editor/CRMEntityEditor.vue'),
      resolve(uiRoot, 'ngb-trade-web/src/editor/TradeEntityEditor.vue'),
    ]
    const violations = wrappers.flatMap((file) => {
      const source = readFileSync(file, 'utf8')
      const lines = source.split(/\r?\n/).length
      return [
        source.includes('NgbConfiguredEntityEditor') ? null : `${relative(file)} must compose NgbConfiguredEntityEditor`,
        source.includes('@ngbplatform/ui/editor') ? null : `${relative(file)} must use the curated editor entrypoint`,
        lines <= 80 ? null : `${relative(file)} contains ${lines} lines; orchestration belongs in the platform host`,
      ].filter((value): value is string => value !== null)
    })

    expect(violations).toEqual([])
  })

  it('keeps shared page layout primitives below site and feature composition layers', () => {
    const oldHeader = resolve(frameworkSource, 'ngb/site/NgbPageHeader.vue')
    const layoutHeader = resolve(frameworkSource, 'ngb/layout/NgbPageHeader.vue')
    expect(existsSync(oldHeader)).toBe(false)
    expect(existsSync(layoutHeader)).toBe(true)
  })

  it('requires strict typecheck and published-package Tailwind scanning in every workspace', () => {
    const violations = verticalRoots.flatMap((root) => {
      const manifest = JSON.parse(readFileSync(resolve(root, 'package.json'), 'utf8')) as {
        scripts?: Record<string, string>
        devDependencies?: Record<string, string>
      }
      const tailwind = readFileSync(resolve(root, 'tailwind.config.js'), 'utf8')
      const dockerfile = readFileSync(resolve(root, 'Dockerfile'), 'utf8')
      const buildsOutsideRootWorkspace = /RUN\s+npm run build\s*(?:\r?\n|$)/.test(dockerfile)
      return [
        manifest.scripts?.typecheck?.includes('vue-tsc --noEmit') ? null : `${relative(root)}/package.json is missing strict typecheck`,
        manifest.scripts?.build?.includes('npm run typecheck') ? null : `${relative(root)}/package.json build bypasses typecheck`,
        !buildsOutsideRootWorkspace || manifest.devDependencies?.['vue-tsc']
          ? null
          : `${relative(root)}/package.json must declare vue-tsc for its standalone Docker build`,
        tailwind.includes('@ngbplatform/ui/src') ? null : `${relative(root)}/tailwind.config.js does not scan the package source`,
      ].filter((value): value is string => value !== null)
    })

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
