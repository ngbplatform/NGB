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

    expect(loadMetadata).toHaveBeenCalledWith('pm.invoice')
    expect(loadPage).toHaveBeenCalledWith({
      entityTypeCode: 'pm.invoice',
      metadata: createMetadata(),
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
    expect(register.page.value).toBeNull()
    expect(register.rows.value).toEqual([])
  })
})
