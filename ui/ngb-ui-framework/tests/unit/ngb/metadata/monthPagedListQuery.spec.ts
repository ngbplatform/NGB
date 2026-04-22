import { reactive } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Router } from 'vue-router'

const pushCleanRouteQueryMock = vi.hoisted(() => vi.fn())

vi.mock('../../../../src/ngb/router/queryParams', async () => {
  const actual = await vi.importActual('../../../../src/ngb/router/queryParams')
  return {
    ...actual,
    pushCleanRouteQuery: pushCleanRouteQueryMock,
  }
})

import {
  monthValueToDateOnlyEnd,
  monthValueToDateOnlyStart,
  useMonthPagedListQuery,
} from '../../../../src/ngb/metadata/monthPagedListQuery'

function createHarness(initialQuery: Record<string, unknown> = {}) {
  const route = reactive({
    path: '/documents/pm.invoice',
    query: { ...initialQuery } as Record<string, unknown>,
  })
  const router = {
    push: vi.fn(),
    replace: vi.fn(),
  } as unknown as Router

  const query = useMonthPagedListQuery({
    route: route as never,
    router,
    defaultLimit: 25,
  })

  return {
    route,
    router,
    query,
  }
}

describe('month paged list query', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('converts month values to inclusive date-only boundaries', () => {
    expect(monthValueToDateOnlyStart('2026-02')).toBe('2026-02-01')
    expect(monthValueToDateOnlyEnd('2026-02')).toBe('2026-02-28')
    expect(monthValueToDateOnlyEnd('2024-02')).toBe('2024-02-29')
    expect(monthValueToDateOnlyStart('invalid')).toBeUndefined()
    expect(monthValueToDateOnlyEnd('invalid')).toBeUndefined()
  })

  it('normalizes route values and pushes clean query updates for filters and paging', () => {
    const { route, router, query } = createHarness({
      offset: '25',
      limit: '25',
      trash: 'all',
      periodFrom: '2026-03',
      periodTo: 'bad',
    })

    expect(query.offset.value).toBe(25)
    expect(query.limit.value).toBe(25)
    expect(query.trashMode.value).toBe('all')
    expect(query.periodFromMonth.value).toBe('2026-03')
    expect(query.periodToMonth.value).toBe('')

    query.trashMode.value = 'deleted'
    query.periodFromMonth.value = '2026-04'
    query.updateListQuery({ search: 'rent' })
    query.updateListQuery({ search: 'lease' }, { preserveOffset: true })
    query.nextPage()
    query.prevPage()

    expect(pushCleanRouteQueryMock).toHaveBeenNthCalledWith(1, route, router, {
      trash: 'deleted',
      offset: 0,
    })
    expect(pushCleanRouteQueryMock).toHaveBeenNthCalledWith(2, route, router, {
      periodFrom: '2026-04',
      offset: 0,
    })
    expect(pushCleanRouteQueryMock).toHaveBeenNthCalledWith(3, route, router, {
      search: 'rent',
      offset: 0,
    })
    expect(pushCleanRouteQueryMock).toHaveBeenNthCalledWith(4, route, router, {
      search: 'lease',
    })
    expect(pushCleanRouteQueryMock).toHaveBeenNthCalledWith(5, route, router, {
      offset: 50,
    })
    expect(pushCleanRouteQueryMock).toHaveBeenNthCalledWith(6, route, router, {
      offset: 0,
    })
  })
})
