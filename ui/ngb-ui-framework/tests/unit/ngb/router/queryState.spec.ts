import { effectScope, nextTick, reactive, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import {
  useAllowedQueryValue,
  useBooleanQueryFlag,
  useGuidQueryParam,
  useRouteLookupSelection,
  useRouteQueryMigration,
} from '../../../../src/ngb/router/queryState'

const EXISTING_ID = '11111111-1111-1111-1111-111111111111'
const SELECTED_ID = '22222222-2222-2222-2222-222222222222'

function createRoute(query: Record<string, unknown>, path = '/catalogs/pm.property') {
  return reactive({
    path,
    fullPath: path,
    query,
  }) as never
}

function createRouter() {
  return {
    push: vi.fn().mockResolvedValue(undefined),
    replace: vi.fn().mockResolvedValue(undefined),
  } as never
}

describe('query state helpers', () => {
  it('derives reactive guid, boolean, and allowed query values from the route', async () => {
    const route = createRoute({
      id: [EXISTING_ID],
      compact: 'yes',
      mode: 'deleted',
    })

    const routeId = useGuidQueryParam(route, 'id')
    const compact = useBooleanQueryFlag(route, 'compact')
    const mode = useAllowedQueryValue(route, 'mode', ['active', 'deleted'] as const)

    expect(routeId.value).toBe(EXISTING_ID)
    expect(compact.value).toBe(true)
    expect(mode.value).toBe('deleted')

    route.query = {
      id: 'not-a-guid',
      compact: '0',
      mode: 'unexpected',
    }
    await nextTick()

    expect(routeId.value).toBeNull()
    expect(compact.value).toBe(false)
    expect(mode.value).toBeNull()
  })

  it('migrates watched values into a clean replace query patch', async () => {
    const route = createRoute({ search: 'lease' }, '/documents/pm.invoice')
    const router = createRouter()
    const trashMode = ref<'active' | 'deleted'>('deleted')

    useRouteQueryMigration({
      route,
      router,
      sources: () => [trashMode.value] as const,
      migrate: ([value]) => value === 'deleted' ? { trash: value } : null,
    })

    await nextTick()
    expect(router.replace).toHaveBeenCalledWith({
      path: '/documents/pm.invoice',
      query: { search: 'lease', trash: 'deleted' },
    })

    router.replace.mockClear()
    trashMode.value = 'active'
    await nextTick()

    expect(router.replace).not.toHaveBeenCalled()
  })

  it('hydrates, searches, selects, and opens lookup values from the route query', async () => {
    const route = createRoute({ accountId: EXISTING_ID }, '/admin/chart-of-accounts')
    const router = createRouter()
    const lookupById = vi.fn().mockResolvedValue('Cash')
    const search = vi.fn().mockResolvedValue([
      { id: SELECTED_ID, label: 'Revenue' },
    ])
    const openTarget = vi.fn().mockResolvedValue({ path: '/catalogs/accounting.account/revenue' })

    const selection = useRouteLookupSelection({
      route,
      router,
      queryKey: 'accountId',
      lookupById,
      search,
      openTarget,
    })

    await selection.hydrateSelected()
    expect(selection.routeId.value).toBe(EXISTING_ID)
    expect(lookupById).toHaveBeenCalledWith(EXISTING_ID, { signal: expect.any(AbortSignal) })
    expect(selection.selected.value).toEqual({ id: EXISTING_ID, label: 'Cash' })

    await selection.onQuery('  revenue  ')
    expect(search).toHaveBeenCalledWith('revenue', { signal: expect.any(AbortSignal) })
    expect(selection.items.value).toEqual([{ id: SELECTED_ID, label: 'Revenue' }])

    selection.onSelect({ id: SELECTED_ID, label: 'Revenue' })
    expect(selection.selected.value).toEqual({ id: SELECTED_ID, label: 'Revenue' })
    expect(router.replace).toHaveBeenCalledWith({
      path: '/admin/chart-of-accounts',
      query: { accountId: SELECTED_ID },
    })

    await selection.openSelected()
    expect(openTarget).toHaveBeenCalledWith({ id: SELECTED_ID, label: 'Revenue' })
    expect(router.push).toHaveBeenCalledWith({ path: '/catalogs/accounting.account/revenue' })
  })

  it('falls back to the raw id on hydrate failures and clears search items for blank queries', async () => {
    const route = createRoute({ dimensionId: EXISTING_ID }, '/documents/pm.invoice')
    const router = createRouter()
    const search = vi.fn()

    const selection = useRouteLookupSelection({
      route,
      router,
      queryKey: 'dimensionId',
      lookupById: vi.fn().mockRejectedValue(new Error('offline')),
      search,
      openTarget: vi.fn().mockResolvedValue(null),
    })

    await selection.hydrateSelected()
    expect(selection.selected.value).toEqual({ id: EXISTING_ID, label: EXISTING_ID })

    selection.items.value = [{ id: SELECTED_ID, label: 'Revenue' }]
    await selection.onQuery('   ')

    expect(search).not.toHaveBeenCalled()
    expect(selection.items.value).toEqual([])

    await selection.openSelected()
    expect(router.push).not.toHaveBeenCalled()

    selection.onSelect(null)
    expect(selection.selected.value).toBeNull()
  })

  it('clears selection without looking up when the route id is absent', async () => {
    const route = createRoute({}, '/catalogs/pm.property')
    const lookupById = vi.fn()
    const selection = useRouteLookupSelection({
      route,
      router: createRouter(),
      queryKey: 'propertyId',
      lookupById,
      search: vi.fn(),
      openTarget: vi.fn(),
    })
    selection.selected.value = { id: SELECTED_ID, label: 'Stale' }

    await selection.hydrateSelected()

    expect(selection.selected.value).toBeNull()
    expect(lookupById).not.toHaveBeenCalled()
  })

  it('uses the raw guid when lookup succeeds without a label', async () => {
    const selection = useRouteLookupSelection({
      route: createRoute({ propertyId: EXISTING_ID }),
      router: createRouter(),
      queryKey: 'propertyId',
      lookupById: vi.fn().mockResolvedValue(null),
      search: vi.fn(),
      openTarget: vi.fn(),
    })

    await selection.hydrateSelected()
    expect(selection.selected.value).toEqual({ id: EXISTING_ID, label: EXISTING_ID })
  })

  it('aborts and ignores stale route hydration and search responses', async () => {
    const route = createRoute({ propertyId: EXISTING_ID })
    let resolveHydration!: (value: string) => void
    let resolveSearch!: (value: Array<{ id: string; label: string }>) => void
    const lookupById = vi.fn()
      .mockImplementationOnce(() => new Promise<string>((resolve) => { resolveHydration = resolve }))
      .mockResolvedValueOnce('Selected property')
    const search = vi.fn()
      .mockImplementationOnce(() => new Promise<Array<{ id: string; label: string }>>((resolve) => { resolveSearch = resolve }))
      .mockResolvedValueOnce([{ id: SELECTED_ID, label: 'Fresh result' }])
    const selection = useRouteLookupSelection({
      route,
      router: createRouter(),
      queryKey: 'propertyId',
      lookupById,
      search,
      openTarget: vi.fn(),
    })

    const staleHydration = selection.hydrateSelected()
    route.query.propertyId = SELECTED_ID
    const freshHydration = selection.hydrateSelected()
    await freshHydration
    expect(lookupById.mock.calls[0]?.[1]?.signal.aborted).toBe(true)
    resolveHydration('Stale property')
    await staleHydration
    expect(selection.selected.value).toEqual({ id: SELECTED_ID, label: 'Selected property' })

    const staleSearch = selection.onQuery('stale')
    const freshSearch = selection.onQuery('fresh')
    await freshSearch
    expect(search.mock.calls[0]?.[1]?.signal.aborted).toBe(true)
    resolveSearch([{ id: EXISTING_ID, label: 'Stale result' }])
    await staleSearch
    expect(selection.items.value).toEqual([{ id: SELECTED_ID, label: 'Fresh result' }])
  })

  it('ignores stale rejected work and rethrows only the current search failure', async () => {
    const route = createRoute({ propertyId: EXISTING_ID })
    let rejectHydration!: (cause: Error) => void
    let rejectSearch!: (cause: Error) => void
    const selection = useRouteLookupSelection({
      route,
      router: createRouter(),
      queryKey: 'propertyId',
      lookupById: vi.fn()
        .mockImplementationOnce(() => new Promise<string>((_resolve, reject) => { rejectHydration = reject }))
        .mockResolvedValueOnce('Fresh'),
      search: vi.fn()
        .mockImplementationOnce(() => new Promise((_resolve, reject) => { rejectSearch = reject }))
        .mockResolvedValueOnce([])
        .mockRejectedValueOnce(new Error('current failure')),
      openTarget: vi.fn(),
    })

    const staleHydration = selection.hydrateSelected()
    route.query.propertyId = SELECTED_ID
    await selection.hydrateSelected()
    rejectHydration(new Error('stale hydration'))
    await expect(staleHydration).resolves.toBeUndefined()

    const staleSearch = selection.onQuery('stale')
    await selection.onQuery('fresh')
    rejectSearch(new Error('stale search'))
    await expect(staleSearch).resolves.toBeUndefined()
    await expect(selection.onQuery('current')).rejects.toThrow('current failure')
  })

  it('aborts pending lookup work when its Vue effect scope is disposed', async () => {
    const route = createRoute({ propertyId: EXISTING_ID })
    const scope = effectScope()
    let selection!: ReturnType<typeof useRouteLookupSelection>
    scope.run(() => {
      selection = useRouteLookupSelection({
        route,
        router: createRouter(),
        queryKey: 'propertyId',
        lookupById: vi.fn(() => new Promise(() => undefined)),
        search: vi.fn(() => new Promise(() => undefined)),
        openTarget: vi.fn(),
      })
    })
    void selection.hydrateSelected()
    void selection.onQuery('pending')
    scope.stop()
  })
})
