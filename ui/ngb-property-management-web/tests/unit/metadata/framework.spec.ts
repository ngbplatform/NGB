import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildTarget: vi.fn(async ({ value }: { value: unknown }) => `/target/${String(value)}`),
  getCatalog: vi.fn(),
  getDocument: vi.fn(),
  normalize: vi.fn((value: unknown) => `normalized:${String(value)}`),
  search: vi.fn(async () => [{ id: 'one', label: 'One' }]),
  store: { kind: 'lookup-store' },
  getHint: vi.fn(() => ({ kind: 'catalog' })),
  options: vi.fn(() => [{ value: 'one', label: 'One' }]),
  readonly: vi.fn(() => false),
  hidden: vi.fn(() => false),
  displayField: vi.fn(() => ({ key: 'display' })),
}))

vi.mock('@ngbplatform/ui', () => ({
  buildLookupFieldTargetUrl: mocks.buildTarget,
  getCatalogTypeMetadata: mocks.getCatalog,
  getDocumentTypeMetadata: mocks.getDocument,
  normalizeLookupValue: mocks.normalize,
  searchResolvedLookupItems: mocks.search,
  useLookupStore: () => mocks.store,
}))
vi.mock('../../../src/lookup/hints', () => ({ getLookupHint: mocks.getHint }))
vi.mock('../../../src/metadata/formBehavior', () => ({
  findDisplayField: mocks.displayField,
  isFieldHidden: mocks.hidden,
  isFieldReadonly: mocks.readonly,
  resolveFieldOptions: mocks.options,
}))

import { createPmMetadataConfig, pmMetadataFormBehavior } from '../../../src/metadata/framework'

describe('property-management metadata framework', () => {
  beforeEach(() => vi.clearAllMocks())

  it('wires metadata loaders and all synchronous behavior adapters', () => {
    const config = createPmMetadataConfig()
    expect(config.loadCatalogTypeMetadata).toBe(mocks.getCatalog)
    expect(config.loadDocumentTypeMetadata).toBe(mocks.getDocument)
    expect(config.formBehavior).toBe(pmMetadataFormBehavior)

    const context = { entityTypeCode: 'pm.property', field: { key: 'kind', lookup: { kind: 'catalog' } } } as never
    expect(pmMetadataFormBehavior.resolveFieldOptions?.(context)).toEqual([{ value: 'one', label: 'One' }])
    expect(mocks.options).toHaveBeenCalledWith('pm.property', 'kind')
    expect(pmMetadataFormBehavior.resolveLookupHint?.(context)).toEqual({ kind: 'catalog' })
    expect(mocks.getHint).toHaveBeenCalledWith('pm.property', 'kind', { kind: 'catalog' })
    expect(pmMetadataFormBehavior.isFieldReadonly).toBe(mocks.readonly)
    expect(pmMetadataFormBehavior.isFieldHidden).toBe(mocks.hidden)
    expect(pmMetadataFormBehavior.findDisplayField).toBe(mocks.displayField)
  })

  it('searches through the shared store and normalizes lookup navigation values', async () => {
    await expect(pmMetadataFormBehavior.searchLookup?.({ hint: { kind: 'catalog' }, query: 'one' } as never))
      .resolves.toEqual([{ id: 'one', label: 'One' }])
    expect(mocks.search).toHaveBeenCalledWith(mocks.store, { kind: 'catalog' }, 'one')

    await expect(pmMetadataFormBehavior.buildLookupTargetUrl?.({
      hint: { kind: 'catalog' }, value: 'raw', routeFullPath: '/catalogs/pm.property',
    } as never)).resolves.toBe('/target/normalized:raw')
    expect(mocks.buildTarget).toHaveBeenCalledWith({
      hint: { kind: 'catalog' }, value: 'normalized:raw', route: { fullPath: '/catalogs/pm.property' },
    })
  })
})
