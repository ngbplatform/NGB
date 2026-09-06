import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({
  buildTarget: vi.fn(async ({ value }: { value: unknown }) => `/target/${String(value)}`),
  getCatalog: vi.fn(),
  getDocument: vi.fn(),
  normalize: vi.fn((value: unknown) => `normalized:${String(value)}`),
  search: vi.fn(async () => [{ id: 'one', label: 'One' }]),
  store: { kind: 'lookup-store' },
  getHint: vi.fn(() => ({ kind: 'catalog' })),
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
vi.mock('../../../src/lookup/hints', () => ({ getCRMLookupHint: mocks.getHint }))
vi.mock('../../../src/metadata/formBehavior', () => ({
  findDisplayField: mocks.displayField,
  isFieldHidden: mocks.hidden,
  isFieldReadonly: mocks.readonly,
}))

import { createCRMMetadataConfig, crmMetadataFormBehavior } from '../../../src/metadata/framework'

const column = (key: string) => ({
  key,
  label: key,
  dataType: 'String',
  isSortable: true,
  align: 1,
})

describe('CRM metadata framework', () => {
  beforeEach(() => vi.clearAllMocks())

  it.each([
    ['crm.account', ['display', 'account_number', 'account_type', 'industry', 'email', 'phone', 'is_active']],
    ['crm.contact', ['display', 'account_id', 'title', 'email', 'phone', 'is_primary', 'is_active']],
    ['crm.product', ['display', 'sku', 'family', 'list_price', 'currency', 'is_active']],
    ['crm.opportunity_stage', ['display', 'stage_code', 'ordinal', 'default_probability', 'is_closed', 'is_won', 'is_active']],
  ])('orders and filters %s list columns', async (catalogType, expectedKeys) => {
    mocks.getCatalog.mockResolvedValue({
      catalogType,
      displayName: catalogType,
      kind: 1,
      list: { columns: [...expectedKeys].reverse().map(column).concat(column('internal')) },
    })

    const metadata = await createCRMMetadataConfig().loadCatalogTypeMetadata(catalogType)

    expect(metadata.list?.columns.map((item) => item.key)).toEqual(expectedKeys)
  })

  it('keeps unknown and empty-list catalog metadata unchanged', async () => {
    const unknown = {
      catalogType: 'crm.custom',
      displayName: 'Custom',
      kind: 1,
      list: { columns: [column('display')] },
    }
    const empty = {
      catalogType: 'crm.account',
      displayName: 'Account',
      kind: 1,
      list: { columns: [] },
    }
    mocks.getCatalog.mockResolvedValueOnce(unknown).mockResolvedValueOnce(empty)

    await expect(createCRMMetadataConfig().loadCatalogTypeMetadata('crm.custom')).resolves.toBe(unknown)
    await expect(createCRMMetadataConfig().loadCatalogTypeMetadata('crm.account')).resolves.toBe(empty)
  })

  it('drops unavailable opinionated columns without creating placeholders', async () => {
    mocks.getCatalog.mockResolvedValue({
      catalogType: 'crm.product',
      displayName: 'Product',
      kind: 1,
      list: { columns: [column('display'), column('currency')] },
    })

    const metadata = await createCRMMetadataConfig().loadCatalogTypeMetadata('crm.product')
    expect(metadata.list?.columns.map((item) => item.key)).toEqual(['display', 'currency'])
  })

  it('wires metadata loaders and synchronous behavior adapters', async () => {
    const config = createCRMMetadataConfig()
    expect(config.loadDocumentTypeMetadata).toBe(mocks.getDocument)
    expect(config.formBehavior).toBe(crmMetadataFormBehavior)

    const context = { entityTypeCode: 'crm.contact', field: { key: 'account_id', lookup: { kind: 'catalog' } } } as never
    expect(crmMetadataFormBehavior.resolveLookupHint?.(context)).toEqual({ kind: 'catalog' })
    expect(mocks.getHint).toHaveBeenCalledWith('crm.contact', 'account_id', { kind: 'catalog' })
    expect(crmMetadataFormBehavior.isFieldReadonly).toBe(mocks.readonly)
    expect(crmMetadataFormBehavior.isFieldHidden).toBe(mocks.hidden)
    expect(crmMetadataFormBehavior.findDisplayField).toBe(mocks.displayField)
  })

  it('searches through the shared store and normalizes lookup target values', async () => {
    await expect(crmMetadataFormBehavior.searchLookup?.({ hint: { kind: 'catalog' }, query: 'one' } as never))
      .resolves.toEqual([{ id: 'one', label: 'One' }])
    expect(mocks.search).toHaveBeenCalledWith(mocks.store, { kind: 'catalog' }, 'one')
    const signal = new AbortController().signal
    await crmMetadataFormBehavior.searchLookup?.({ hint: { kind: 'catalog' }, query: 'one', signal } as never)
    expect(mocks.search).toHaveBeenCalledWith(mocks.store, { kind: 'catalog' }, 'one', { signal })

    await expect(crmMetadataFormBehavior.buildLookupTargetUrl?.({
      hint: { kind: 'catalog' }, value: 'raw', routeFullPath: '/catalogs/crm.account',
    } as never)).resolves.toBe('/target/normalized:raw')
    expect(mocks.buildTarget).toHaveBeenCalledWith({
      hint: { kind: 'catalog' }, value: 'normalized:raw', route: { fullPath: '/catalogs/crm.account' },
    })
  })
})
