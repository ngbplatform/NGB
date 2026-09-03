import { computed, nextTick, reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const prefetchLookupsForPageMock = vi.hoisted(() => vi.fn().mockResolvedValue(undefined))

vi.mock('../../../../src/ngb/lookup/prefetch', () => ({
  prefetchLookupsForPage: prefetchLookupsForPageMock,
}))

import { useMetadataRegisterPageData } from '../../../../src/ngb/metadata/useMetadataRegisterPageData'

async function flushAsync() {
  await nextTick()
  await Promise.resolve()
  await Promise.resolve()
}

function createLookupStore() {
  return {
    labelForCatalog: vi.fn((catalogType: string, id: unknown) =>
      String(id) === '11111111-1111-1111-1111-111111111111'
        ? 'Riverfront Tower'
        : `${catalogType}:${String(id)}`,
    ),
    labelForCoa: vi.fn((id: unknown) => `COA:${String(id)}`),
    labelForAnyDocument: vi.fn((documentTypes: string[], id: unknown) => `${documentTypes.join('|')}:${String(id)}`),
  }
}

function createMetadata() {
  return {
    displayName: 'Invoices',
    list: {
      columns: [
        {
          key: 'property_id',
          label: 'Property Id',
          dataType: 'Guid',
          align: 1,
          isSortable: true,
          lookup: {
            kind: 'catalog' as const,
            catalogType: 'pm.property',
          },
        },
        {
          key: 'status',
          label: 'Status',
          dataType: 'String',
          align: 1,
          isSortable: true,
        },
        {
          key: 'amount',
          label: 'Amount',
          dataType: 'Decimal',
          align: 3,
          isSortable: true,
        },
      ],
      filters: [
        {
          key: 'status',
          label: 'Status',
          dataType: 'String',
          options: [
            { value: 'open', label: 'Open' },
            { value: 'posted', label: 'Posted' },
          ],
        },
      ],
    },
  }
}

function createPage() {
  return {
    total: 1,
    items: [
      {
        id: 'doc-1',
        status: 1,
        payload: {
          fields: {
            property_id: '11111111-1111-1111-1111-111111111111',
            status: 'open',
            amount: 1250,
          },
        },
      },
    ],
  }
}

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (cause: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })

  return { promise, resolve, reject }
}

function createHarness(options?: {
  entityTypeCode?: string
  loadMetadata?: (entityTypeCode: string) => Promise<ReturnType<typeof createMetadata>>
  loadPage?: (args: { entityTypeCode: string; metadata: ReturnType<typeof createMetadata> }) => Promise<ReturnType<typeof createPage>>
}) {
  const route = reactive({
    path: '/documents/pm.invoice',
    query: {},
  })
  const entityTypeCode = ref(options?.entityTypeCode ?? 'pm.invoice')
  const reloadKey = ref('initial')
  const lookupStore = createLookupStore()

  const register = useMetadataRegisterPageData({
    route: route as never,
    entityTypeCode: computed(() => entityTypeCode.value),
    reloadKey: computed(() => reloadKey.value),
    loadMetadata: options?.loadMetadata ?? vi.fn().mockResolvedValue(createMetadata()),
    loadPage: options?.loadPage ?? vi.fn().mockResolvedValue(createPage()),
    lookupStore: lookupStore as never,
    resolveLookupHint: ({ lookup }) => lookup ?? null,
    mapFieldValue: ({ column, defaultValue }) =>
      column.key === 'amount' ? `USD ${String(defaultValue)}` : defaultValue,
  })

  return {
    route,
    entityTypeCode,
    reloadKey,
    lookupStore,
    register,
  }
}

describe('metadata register page data', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('loads metadata and page data, resolves lookup labels, and builds register rows', async () => {
    const loadMetadata = vi.fn().mockResolvedValue(createMetadata())
    const loadPage = vi.fn().mockResolvedValue(createPage())
    const { register } = createHarness({
      loadMetadata,
      loadPage,
    })

    await flushAsync()

    expect(loadMetadata).toHaveBeenCalledWith('pm.invoice', {
      signal: expect.any(AbortSignal),
    })
    expect(loadPage).toHaveBeenCalledWith({
      entityTypeCode: 'pm.invoice',
      metadata: createMetadata(),
      signal: expect.any(AbortSignal),
    })
    expect(register.metadata.value?.displayName).toBe('Invoices')
    expect(register.hasListFilters.value).toBe(true)
    expect(register.optionLabelsByColumnKey.value.status?.get('open')).toBe('Open')
    expect(register.columns.value.map((column) => column.title)).toEqual(['Property', 'Status', 'Amount'])
    expect(register.rows.value).toEqual([
      {
        key: 'doc-1',
        isDeleted: undefined,
        isMarkedForDeletion: undefined,
        property_id: {
          id: '11111111-1111-1111-1111-111111111111',
          display: 'Riverfront Tower',
        },
        status: 'open',
        amount: 'USD 1250',
      },
    ])
    expect(prefetchLookupsForPageMock).toHaveBeenCalledWith({
      entityTypeCode: 'pm.invoice',
      columns: createMetadata().list.columns,
      items: createPage().items,
      lookupStore: expect.any(Object),
      resolveLookupHint: expect.any(Function),
    })
  })

  it('keeps state empty when the entity type is blank', async () => {
    const loadMetadata = vi.fn()
    const loadPage = vi.fn()
    const { register } = createHarness({
      entityTypeCode: '',
      loadMetadata,
      loadPage,
    })

    await flushAsync()

    expect(loadMetadata).not.toHaveBeenCalled()
    expect(loadPage).not.toHaveBeenCalled()
    expect(register.metadata.value).toBeNull()
    expect(register.page.value).toBeNull()
    expect(register.error.value).toBeNull()
  })

  it('surfaces load failures through the formatted error state', async () => {
    const { register } = createHarness({
      loadMetadata: vi.fn().mockResolvedValue(createMetadata()),
      loadPage: vi.fn().mockRejectedValue(new Error('Service unavailable')),
    })

    await flushAsync()

    expect(register.loading.value).toBe(false)
    expect(register.error.value).toBe('Service unavailable')
    expect(register.metadata.value?.displayName).toBe('Invoices')
    expect(register.page.value).toBeNull()
    expect(register.rows.value).toEqual([])
  })

  it('formats non-Error failures with the default formatter', async () => {
    const { register } = createHarness({
      loadMetadata: vi.fn().mockRejectedValue('metadata offline'),
    })

    await flushAsync()

    expect(register.loading.value).toBe(false)
    expect(register.error.value).toBe('metadata offline')
  })

  it('preserves reference values and unresolved GUID values while building rows', async () => {
    const metadata = createMetadata()
    metadata.list.columns.push({
      key: 'owner_id',
      label: 'Owner Id',
      dataType: 'Guid',
      align: 1,
      isSortable: false,
    })
    const page = createPage()
    const property = {
      id: '11111111-1111-1111-1111-111111111111',
      display: 'Stored property label',
    }
    const ownerId = '22222222-2222-2222-2222-222222222222'
    page.items[0]!.payload!.fields!.property_id = property
    page.items[0]!.payload!.fields!.owner_id = ownerId

    const { register } = createHarness({
      loadMetadata: vi.fn().mockResolvedValue(metadata),
      loadPage: vi.fn().mockResolvedValue(page),
    })

    await flushAsync()

    expect(register.rows.value[0]?.property_id).toEqual(property)
    expect(register.rows.value[0]?.owner_id).toBe(ownerId)
  })

  it('ignores metadata returned by an older overlapping load', async () => {
    const staleMetadata = createDeferred<ReturnType<typeof createMetadata>>()
    const freshMetadata = {
      ...createMetadata(),
      displayName: 'Fresh invoices',
    }
    const loadMetadata = vi.fn()
      .mockReturnValueOnce(staleMetadata.promise)
      .mockResolvedValueOnce(freshMetadata)
    const { entityTypeCode, register } = createHarness({
      entityTypeCode: '',
      loadMetadata,
    })

    await flushAsync()
    entityTypeCode.value = 'pm.invoice'
    const staleLoad = register.load()
    const freshLoad = register.load()

    await expect(freshLoad).resolves.toBe(true)
    staleMetadata.resolve(createMetadata())
    await expect(staleLoad).resolves.toBe(false)
    expect(register.metadata.value?.displayName).toBe('Fresh invoices')
  })

  it('ignores page data returned by an older overlapping load', async () => {
    const stalePage = createDeferred<ReturnType<typeof createPage>>()
    const freshPage = createPage()
    freshPage.items[0]!.id = 'fresh-doc'
    const loadPage = vi.fn()
      .mockReturnValueOnce(stalePage.promise)
      .mockResolvedValueOnce(freshPage)
    const { entityTypeCode, register } = createHarness({
      entityTypeCode: '',
      loadPage,
    })

    await flushAsync()
    entityTypeCode.value = 'pm.invoice'
    const staleLoad = register.load()
    await flushAsync()
    const freshLoad = register.load()

    await expect(freshLoad).resolves.toBe(true)
    stalePage.resolve(createPage())
    await expect(staleLoad).resolves.toBe(false)
    expect(register.page.value?.items[0]?.id).toBe('fresh-doc')
  })

  it('ignores failures from an older overlapping load', async () => {
    const staleMetadata = createDeferred<ReturnType<typeof createMetadata>>()
    const loadMetadata = vi.fn()
      .mockReturnValueOnce(staleMetadata.promise)
      .mockResolvedValueOnce(createMetadata())
    const { entityTypeCode, register } = createHarness({
      entityTypeCode: '',
      loadMetadata,
    })

    await flushAsync()
    entityTypeCode.value = 'pm.invoice'
    const staleLoad = register.load()
    const freshLoad = register.load()

    await expect(freshLoad).resolves.toBe(true)
    staleMetadata.reject(new Error('stale failure'))
    await expect(staleLoad).resolves.toBe(false)
    expect(register.error.value).toBeNull()
    expect(register.loading.value).toBe(false)
  })

  it('publishes metadata with an empty page so lookup prefetch never combines generations', async () => {
    const nextPage = createDeferred<ReturnType<typeof createPage>>()
    const oldMetadata = createMetadata()
    const newMetadata = { ...createMetadata(), displayName: 'Updated invoices' }
    const oldPage = createPage()
    const updatedPage = createPage()
    updatedPage.items[0]!.id = 'updated-doc'
    const loadMetadata = vi.fn()
      .mockResolvedValueOnce(oldMetadata)
      .mockResolvedValueOnce(newMetadata)
    const loadPage = vi.fn()
      .mockResolvedValueOnce(oldPage)
      .mockReturnValueOnce(nextPage.promise)
    const { register } = createHarness({ loadMetadata, loadPage })

    await vi.waitFor(() => expect(register.page.value?.items[0]?.id).toBe('doc-1'))
    prefetchLookupsForPageMock.mockClear()
    const reload = register.load()
    await vi.waitFor(() => expect(loadPage).toHaveBeenCalledTimes(2))

    expect(register.metadata.value?.displayName).toBe(newMetadata.displayName)
    expect(register.page.value).toBeNull()
    expect(prefetchLookupsForPageMock).not.toHaveBeenCalled()

    nextPage.resolve(updatedPage)
    await expect(reload).resolves.toBe(true)
    await flushAsync()
    expect(register.metadata.value?.displayName).toBe(newMetadata.displayName)
    expect(register.page.value?.items[0]?.id).toBe(updatedPage.items[0]?.id)
    expect(prefetchLookupsForPageMock).toHaveBeenCalledTimes(1)
  })
})
