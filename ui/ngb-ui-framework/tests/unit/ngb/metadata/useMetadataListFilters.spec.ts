import { computed, nextTick, reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Router } from 'vue-router'

const onBeforeUnmountMock = vi.hoisted(() => vi.fn())
const setCleanRouteQueryMock = vi.hoisted(() => vi.fn().mockResolvedValue(undefined))

vi.mock('vue', async () => {
  const actual = await vi.importActual<typeof import('vue')>('vue')
  return {
    ...actual,
    onBeforeUnmount: onBeforeUnmountMock,
  }
})

vi.mock('../../../../src/ngb/router/queryParams', async () => {
  const actual = await vi.importActual('../../../../src/ngb/router/queryParams')
  return {
    ...actual,
    setCleanRouteQuery: setCleanRouteQueryMock,
  }
})

import { useMetadataListFilters } from '../../../../src/ngb/metadata/useMetadataListFilters'

async function flushAsync() {
  await nextTick()
  await Promise.resolve()
  await Promise.resolve()
}

function createLookupStore() {
  return {
    searchCatalog: vi.fn().mockResolvedValue([{ id: '11111111-1111-1111-1111-111111111111', label: 'Riverfront Tower' }]),
    searchCoa: vi.fn().mockResolvedValue([]),
    searchDocuments: vi.fn().mockResolvedValue([]),
    ensureCatalogLabels: vi.fn().mockResolvedValue(undefined),
    ensureCoaLabels: vi.fn().mockResolvedValue(undefined),
    ensureAnyDocumentLabels: vi.fn().mockResolvedValue(undefined),
    labelForCatalog: vi.fn((catalogType: string, id: unknown) => {
      if (String(id) === '11111111-1111-1111-1111-111111111111') return 'Riverfront Tower'
      if (String(id) === '22222222-2222-2222-2222-222222222222') return 'Harbor Point'
      return `${catalogType}:${String(id)}`
    }),
    labelForCoa: vi.fn((id: unknown) => `COA:${String(id)}`),
    labelForAnyDocument: vi.fn((documentTypes: string[], id: unknown) => `${documentTypes.join('|')}:${String(id)}`),
  }
}

function createHarness(initialQuery: Record<string, unknown> = {}) {
  const route = reactive({
    path: '/documents/pm.invoice',
    query: { ...initialQuery } as Record<string, unknown>,
  })
  const router = {
    replace: vi.fn(),
    push: vi.fn(),
  } as unknown as Router
  const entityTypeCode = ref('pm.invoice')
  const filters = ref([
    {
      key: 'status',
      label: 'Status',
      dataType: 'String',
      options: [
        { value: 'open', label: 'Open' },
        { value: 'posted', label: 'Posted' },
      ],
    },
    {
      key: 'property_id',
      label: 'Property',
      dataType: 'Guid',
      isMulti: true,
      lookup: {
        kind: 'catalog' as const,
        catalogType: 'pm.property',
      },
    },
    {
      key: 'memo',
      label: 'Memo',
      dataType: 'String',
    },
  ])
  const lookupStore = createLookupStore()

  const listFilters = useMetadataListFilters({
    route: route as never,
    router,
    entityTypeCode: computed(() => entityTypeCode.value),
    filters: computed(() => filters.value),
    lookupStore,
    resolveLookupHint: ({ field }) => field.lookup ?? null,
    commitDelayMs: 25,
  })

  return {
    route,
    router,
    entityTypeCode,
    filters,
    lookupStore,
    listFilters,
  }
}

describe('metadata list filters', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.useRealTimers()
  })

  it('syncs filter draft from route, hydrates lookup ids, and builds active badges', async () => {
    const { lookupStore, listFilters } = createHarness({
      status: 'open',
      property_id: '11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222',
    })

    await flushAsync()

    expect(lookupStore.ensureCatalogLabels).toHaveBeenCalledWith('pm.property', [
      '11111111-1111-1111-1111-111111111111',
      '22222222-2222-2222-2222-222222222222',
    ])
    expect(listFilters.filterDraft.value.status).toEqual({
      raw: 'open',
      items: [],
    })
    expect(listFilters.filterDraft.value.property_id).toEqual({
      raw: '11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222',
      items: [
        { id: '11111111-1111-1111-1111-111111111111', label: 'Riverfront Tower' },
        { id: '22222222-2222-2222-2222-222222222222', label: 'Harbor Point' },
      ],
    })
    expect(listFilters.optionLabelsByColumnKey.value.status.get('open')).toBe('Open')
    expect(listFilters.activeFilterBadges.value).toEqual([
      { key: 'status', text: 'Status: Open' },
      { key: 'property_id', text: 'Property: Riverfront Tower (+1)' },
    ])
    expect(listFilters.hasActiveFilters.value).toBe(true)
    expect(listFilters.canUndoFilters.value).toBe(true)
  })

  it('updates lookup search results and commits selected lookup items immediately', async () => {
    const { route, router, listFilters } = createHarness()

    await listFilters.handleLookupQuery({
      key: 'property_id',
      query: 'river',
    })

    expect(listFilters.lookupItemsByFilterKey.value.property_id).toEqual([
      { id: '11111111-1111-1111-1111-111111111111', label: 'Riverfront Tower' },
    ])

    listFilters.handleItemsUpdate({
      key: 'property_id',
      items: [
        { id: '11111111-1111-1111-1111-111111111111', label: 'Riverfront Tower' },
        { id: '22222222-2222-2222-2222-222222222222', label: 'Harbor Point' },
      ],
    })

    await flushAsync()

    expect(setCleanRouteQueryMock).toHaveBeenCalledWith(route, router, {
      property_id: '11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222',
      offset: 0,
    }, 'replace')
  })

  it('commits select filters immediately, text filters after debounce, and undo clears applied state', async () => {
    vi.useFakeTimers()

    const { route, router, entityTypeCode, listFilters } = createHarness({
      status: 'posted',
      memo: 'recurring',
    })
    await flushAsync()

    listFilters.handleValueUpdate({ key: 'status', value: 'open' })
    await flushAsync()

    expect(setCleanRouteQueryMock).toHaveBeenNthCalledWith(1, route, router, {
      status: 'open',
      memo: 'recurring',
      offset: 0,
    }, 'replace')

    listFilters.handleValueUpdate({ key: 'memo', value: 'april rent' })
    expect(setCleanRouteQueryMock).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(30)
    expect(setCleanRouteQueryMock).toHaveBeenNthCalledWith(2, route, router, {
      status: 'posted',
      memo: 'april rent',
      offset: 0,
    }, 'replace')

    await listFilters.undo()
    expect(setCleanRouteQueryMock).toHaveBeenNthCalledWith(3, route, router, {
      offset: 0,
    }, 'replace')

    entityTypeCode.value = 'pm.credit_note'
    await flushAsync()
    expect(listFilters.filterDraft.value).toEqual({})
    expect(listFilters.lookupItemsByFilterKey.value).toEqual({})
  })
})
