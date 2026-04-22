import { computed, nextTick, reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useDashboardPageState } from '../../../../src/ngb/site/useDashboardPageState'

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })

  return { promise, resolve, reject }
}

async function flushUi() {
  await Promise.resolve()
  await nextTick()
  await Promise.resolve()
}

describe('useDashboardPageState', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('loads immediately from route query, exposes deduped warnings, and refreshes on demand', async () => {
    const route = reactive({
      path: '/dashboard',
      query: {
        asOf: '2026-04-08',
      },
    })
    const router = {
      replace: vi.fn(),
    }
    const load = vi.fn()
      .mockResolvedValueOnce({
        warnings: [' Late data ', 'Stale occupancy cache', 'Late data'],
      })
      .mockResolvedValueOnce({
        warnings: ['Blocked ledger sync'],
      })

    const state = useDashboardPageState({
      load,
      route: route as never,
      router: router as never,
    })

    await flushUi()

    expect(load).toHaveBeenCalledWith('2026-04-08')
    expect(state.asOf.value).toBe('2026-04-08')
    expect(state.warnings.value).toEqual(['Late data', 'Stale occupancy cache'])
    expect(state.error.value).toBeNull()
    expect(state.loading.value).toBe(false)

    state.refresh()
    await flushUi()

    expect(load).toHaveBeenCalledTimes(2)
    expect(state.warnings.value).toEqual(['Blocked ledger sync'])
  })

  it('updates route query through the asOf setter and falls back when query is missing', async () => {
    const route = reactive({
      path: '/dashboard',
      query: {},
    })
    const router = {
      replace: vi.fn(async ({ query }: { query: Record<string, unknown> }) => {
        route.query = query
      }),
    }
    const load = vi.fn().mockResolvedValue({
      warnings: [],
    })

    const state = useDashboardPageState({
      load,
      route: route as never,
      router: router as never,
      fallbackAsOf: () => '2026-04-01',
    })

    await flushUi()
    expect(state.asOf.value).toBe('2026-04-01')
    expect(load).toHaveBeenCalledWith('2026-04-01')

    state.asOf.value = '2026-04-15'
    await flushUi()

    expect(router.replace).toHaveBeenCalledWith({
      path: '/dashboard',
      query: {
        asOf: '2026-04-15',
      },
    })
    expect(state.asOf.value).toBe('2026-04-15')
    expect(load).toHaveBeenLastCalledWith('2026-04-15')
  })

  it('ignores stale responses and normalizes error messages on failure', async () => {
    const route = reactive({
      path: '/dashboard',
      query: {
        asOf: '2026-04-08',
      },
    })
    const router = {
      replace: vi.fn(async ({ query }: { query: Record<string, unknown> }) => {
        route.query = query
      }),
    }

    const first = createDeferred<{ warnings: string[] }>()
    const second = createDeferred<{ warnings: string[] }>()
    const load = vi.fn()
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise)
      .mockRejectedValueOnce(new Error('Dashboard exploded'))

    const state = useDashboardPageState({
      load,
      route: route as never,
      router: router as never,
      resolveWarnings: (dashboard) => (dashboard as { warnings?: string[] } | null)?.warnings ?? [],
    })

    state.asOf.value = '2026-04-09'
    await flushUi()

    second.resolve({ warnings: ['Fresh warning'] })
    await flushUi()

    first.resolve({ warnings: ['Stale warning'] })
    await flushUi()

    expect(state.warnings.value).toEqual(['Fresh warning'])
    expect(state.error.value).toBeNull()

    state.refresh()
    await flushUi()
    await flushUi()

    expect(state.dashboard.value).toBeNull()
    expect(state.error.value).toBe('Dashboard exploded')
    expect(state.loading.value).toBe(false)
  })
})
