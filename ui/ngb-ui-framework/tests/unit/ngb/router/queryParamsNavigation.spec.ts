import { describe, expect, it, vi } from 'vitest'

import {
  navigateCleanRouteQuery,
  omitRouteQueryKeys,
  pushCleanRouteQuery,
  replaceCleanRouteQuery,
  setCleanRouteQuery,
} from '../../../../src/ngb/router/queryParams'

function createRoute() {
  return {
    path: '/documents/pm.invoice',
    query: {
      panel: 'edit',
      id: 'doc-1',
      empty: '',
    },
  } as never
}

function createRouter() {
  return {
    push: vi.fn().mockResolvedValue(undefined),
    replace: vi.fn().mockResolvedValue(undefined),
  } as never
}

describe('query params navigation helpers', () => {
  it('sets a clean query object with replace mode by default', async () => {
    const route = createRoute()
    const router = createRouter()

    await setCleanRouteQuery(route, router, {
      panel: null,
      id: 'doc-1',
      search: ' invoices ',
      archived: false,
    })

    expect(router.replace).toHaveBeenCalledWith({
      path: '/documents/pm.invoice',
      query: {
        id: 'doc-1',
        search: ' invoices ',
        archived: false,
      },
    })
    expect(router.push).not.toHaveBeenCalled()
  })

  it('navigates with push mode when requested and merges patches into the current route query', async () => {
    const route = createRoute()
    const router = createRouter()

    await navigateCleanRouteQuery(route, router, {
      panel: null,
      page: 2,
      id: 'doc-2',
    }, 'push')

    expect(router.push).toHaveBeenCalledWith({
      path: '/documents/pm.invoice',
      query: {
        id: 'doc-2',
        page: 2,
      },
    })
    expect(router.replace).not.toHaveBeenCalled()
  })

  it('offers replace and push wrappers for clean route query updates', async () => {
    const route = createRoute()
    const router = createRouter()

    await replaceCleanRouteQuery(route, router, {
      panel: 'new',
      search: 'open items',
    })
    await pushCleanRouteQuery(route, router, {
      id: null,
      month: '2026-04',
    })

    expect(router.replace).toHaveBeenCalledWith({
      path: '/documents/pm.invoice',
      query: {
        panel: 'new',
        id: 'doc-1',
        search: 'open items',
      },
    })
    expect(router.push).toHaveBeenCalledWith({
      path: '/documents/pm.invoice',
      query: {
        panel: 'edit',
        month: '2026-04',
      },
    })
  })

  it('omits selected query keys and keeps the rest intact', async () => {
    const route = createRoute()
    const router = createRouter()

    await omitRouteQueryKeys(route, router, ['panel', 'missing'])

    expect(router.replace).toHaveBeenCalledWith({
      path: '/documents/pm.invoice',
      query: {
        id: 'doc-1',
      },
    })
  })
})
