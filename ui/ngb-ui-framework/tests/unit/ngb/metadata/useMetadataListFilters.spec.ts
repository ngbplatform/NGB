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

function createHarness(
  initialQuery: Record<string, unknown> = {},
  options: {
    commitDelayMs?: number
    resolveLookupHint?: (field: ReturnType<typeof defaultFilters>[number]) => ReturnType<typeof defaultResolveLookupHint>
    filters?: ReturnType<typeof defaultFilters>
  } = {},
) {
  const route = reactive({
    path: '/documents/pm.invoice',
    query: { ...initialQuery } as Record<string, unknown>,
  })
  const router = {
    replace: vi.fn(),
    push: vi.fn(),
  } as unknown as Router
  const entityTypeCode = ref('pm.invoice')
  const filters = ref(options.filters ?? defaultFilters())
  const resolveLookupHint = options.resolveLookupHint ?? defaultResolveLookupHint
  const lookupStore = createLookupStore()

  const listFilters = useMetadataListFilters({
    route: route as never,
    router,
    entityTypeCode: computed(() => entityTypeCode.value),
    filters: computed(() => filters.value),
    lookupStore,
    resolveLookupHint: ({ field }) => resolveLookupHint(field),
    commitDelayMs: options.commitDelayMs,
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

function defaultFilters() {
  return [
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
  ]
}

function defaultResolveLookupHint(field: ReturnType<typeof defaultFilters>[number]) {
  return field.lookup ?? null
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
    await vi.waitFor(() => expect(listFilters.filterDraft.value.property_id?.items).toHaveLength(2))

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
    }, { commitDelayMs: 25 })
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

  it('covers missing fields, blank lookup queries, empty commits, and pending timer cleanup', async () => {
    vi.useFakeTimers()
    const { route, router, listFilters } = createHarness({}, { commitDelayMs: undefined })
    await flushAsync()

    await listFilters.handleLookupQuery({ key: 'missing', query: 'anything' })
    await listFilters.handleLookupQuery({ key: 'property_id', query: '   ' })
    expect(listFilters.lookupItemsByFilterKey.value.property_id).toEqual([])

    listFilters.handleValueUpdate({ key: 'missing', value: 'ignored' })
    listFilters.handleValueUpdate({ key: 'status', value: '   ' })
    await flushAsync()
    expect(setCleanRouteQueryMock).toHaveBeenCalledWith(route, router, { status: undefined, offset: 0 }, 'replace')

    listFilters.handleValueUpdate({ key: 'memo', value: 'first' })
    listFilters.handleValueUpdate({ key: 'memo', value: 'second' })
    await listFilters.undo()
    await vi.runAllTimersAsync()
    expect(setCleanRouteQueryMock).toHaveBeenLastCalledWith(route, router, { offset: 0 }, 'replace')

    const unmount = onBeforeUnmountMock.mock.calls.at(-1)?.[0] as (() => void) | undefined
    expect(unmount).toBeTypeOf('function')
    unmount?.()
  })

  it('handles unavailable lookup hints and ignores stale asynchronous searches', async () => {
    const noHint = createHarness({
      property_id: '11111111-1111-1111-1111-111111111111',
    }, { resolveLookupHint: () => null })
    await flushAsync()
    expect(noHint.listFilters.filterDraft.value.property_id?.items).toEqual([])
    await noHint.listFilters.handleLookupQuery({ key: 'property_id', query: 'river' })
    expect(noHint.listFilters.lookupItemsByFilterKey.value.property_id).toEqual([])

    const harness = createHarness()
    let resolveFirst!: (items: Array<{ id: string; label: string }>) => void
    harness.lookupStore.searchCatalog
      .mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve }))
      .mockResolvedValueOnce([{ id: 'second', label: 'Second result' }])

    const first = harness.listFilters.handleLookupQuery({ key: 'property_id', query: 'first' })
    await Promise.resolve()
    const second = harness.listFilters.handleLookupQuery({ key: 'property_id', query: 'second' })
    await second
    expect(harness.lookupStore.searchCatalog.mock.calls[0]?.[2]?.signal.aborted).toBe(true)
    resolveFirst([{ id: 'first', label: 'First result' }])
    await first

    expect(harness.listFilters.lookupItemsByFilterKey.value.property_id).toEqual([
      { id: 'second', label: 'Second result' },
    ])

    const singleLookupFilters = defaultFilters()
    const propertyFilter = singleLookupFilters.find((field) => field.key === 'property_id')!
    propertyFilter.isMulti = false
    const singleLookup = createHarness({
      property_id: '11111111-1111-1111-1111-111111111111',
    }, { filters: singleLookupFilters })
    await flushAsync()
    await vi.waitFor(() => expect(singleLookup.listFilters.filterDraft.value.property_id?.items).toHaveLength(1))
    expect(singleLookup.listFilters.filterDraft.value.property_id?.items).toHaveLength(1)
  })

  it('falls back from stale draft labels and suppresses badges with empty lookup labels', async () => {
    const harness = createHarness({ status: 'open' })
    await flushAsync()
    harness.listFilters.filterDraft.value = {}
    expect(harness.listFilters.activeFilterBadges.value).toEqual([{ key: 'status', text: 'Status: Open' }])

    harness.route.query.property_id = '11111111-1111-1111-1111-111111111111'
    await flushAsync()
    harness.listFilters.filterDraft.value.property_id = {
      raw: '11111111-1111-1111-1111-111111111111',
      items: [
        { id: '11111111-1111-1111-1111-111111111111', label: null } as never,
        { id: null, label: null } as never,
      ],
    }
    expect(harness.listFilters.activeFilterBadges.value).toContainEqual({
      key: 'property_id',
      text: 'Property: 11111111-1111-1111-1111-111111111111',
    })

    harness.listFilters.filterDraft.value.property_id = {
      raw: 'different',
      items: [{ id: null, label: null } as never],
    }
    expect(harness.listFilters.activeFilterBadges.value).toContainEqual({
      key: 'property_id',
      text: 'Property: Riverfront Tower',
    })

    harness.lookupStore.labelForCatalog.mockReturnValue('')
    harness.listFilters.filterDraft.value = {}
    expect(harness.listFilters.activeFilterBadges.value).not.toContainEqual(expect.objectContaining({ key: 'property_id' }))
    expect(harness.listFilters.canUndoFilters.value).toBe(true)

    harness.route.query = {}
    await flushAsync()
    harness.listFilters.filterDraft.value = {
      memo: { raw: 'draft only', items: [] },
    }
    expect(harness.listFilters.hasActiveFilters.value).toBe(false)
    expect(harness.listFilters.canUndoFilters.value).toBe(true)
  })

  it('abandons route hydration when a newer synchronization wins the race', async () => {
    const harness = createHarness()
    await flushAsync()

    let releaseHydration!: () => void
    harness.lookupStore.ensureCatalogLabels.mockImplementationOnce(() => new Promise<void>((resolve) => {
      releaseHydration = resolve
    }))

    harness.route.query.property_id = '11111111-1111-1111-1111-111111111111'
    await nextTick()
    harness.route.query.property_id = undefined
    await flushAsync()
    releaseHydration()
    await flushAsync()

    expect(harness.listFilters.filterDraft.value.property_id).toEqual({ raw: '', items: [] })
  })

  it('propagates current lookup failures and aborts pending searches on context change and unmount', async () => {
    const current = createHarness()
    current.lookupStore.searchCatalog.mockRejectedValueOnce(new Error('lookup failed'))
    await expect(current.listFilters.handleLookupQuery({ key: 'property_id', query: 'fail' }))
      .rejects.toThrow('lookup failed')

    const onAbort = vi.fn()
    const pending = createHarness()
    pending.lookupStore.searchCatalog.mockImplementation((_type, _query, options) => new Promise((_resolve, reject) => {
      options?.signal?.addEventListener('abort', () => {
        onAbort()
        reject(new DOMException('Aborted', 'AbortError'))
      }, { once: true })
    }))

    const contextSearch = pending.listFilters.handleLookupQuery({ key: 'property_id', query: 'context' })
    await Promise.resolve()
    pending.entityTypeCode.value = 'pm.credit_note'
    await nextTick()
    await expect(contextSearch).resolves.toBeUndefined()

    const unmountSearch = pending.listFilters.handleLookupQuery({ key: 'property_id', query: 'unmount' })
    await Promise.resolve()
    const unmount = onBeforeUnmountMock.mock.calls.at(-1)?.[0] as () => void
    unmount()
    await expect(unmountSearch).resolves.toBeUndefined()
    expect(onAbort).toHaveBeenCalledTimes(2)
  })
})
