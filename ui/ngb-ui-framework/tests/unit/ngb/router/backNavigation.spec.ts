import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  buildPathWithQuery,
  currentRouteBackTarget,
  decodeBackTarget,
  encodeBackTarget,
  navigateBack,
  resolveBackTargetFromPath,
  resolveBackTarget,
  routeTargetMatches,
  withBackTarget,
} from '../../../../src/ngb/router/backNavigation'

describe('backNavigation', () => {
  const originalWindow = globalThis.window

  afterEach(() => {
    globalThis.window = originalWindow as typeof window
  })

  it('round-trips encoded back targets and appends them to URLs', () => {
    const target = '/reports/occupancy?variant=team&label=North America'
    const encoded = encodeBackTarget(target)

    expect(encoded).not.toBeNull()
    expect(decodeBackTarget(encoded)).toBe(target)
    expect(decodeBackTarget([encoded, 'ignored'])).toBe(target)
    expect(decodeBackTarget('%%%')).toBeNull()

    const path = withBackTarget('/documents/pm.invoice?id=doc-1', target)
    const query = new URLSearchParams(path.split('?')[1] ?? '')

    expect(query.get('id')).toBe('doc-1')
    expect(decodeBackTarget(query.get('back'))).toBe(target)
  })

  it('patches query strings, drops blank values, and resolves current and explicit back targets', () => {
    const path = buildPathWithQuery('/documents/pm.invoice?panel=edit&search=open', {
      panel: null,
      id: 'doc-1',
      search: ' open items ',
    })
    const query = new URLSearchParams(path.split('?')[1] ?? '')

    expect(path.startsWith('/documents/pm.invoice?')).toBe(true)
    expect(query.get('panel')).toBeNull()
    expect(query.get('id')).toBe('doc-1')
    expect(query.get('search')).toBe('open items')
    expect(currentRouteBackTarget({ fullPath: '/documents/pm.invoice?panel=edit' })).toBe('/documents/pm.invoice?panel=edit')
    expect(currentRouteBackTarget({ fullPath: '' })).toBe('/')
    expect(resolveBackTarget({ query: { back: encodeBackTarget('/home') } })).toBe('/home')
  })

  it('matches canonical route targets while allowing extra query state and unwraps nested back targets from urls', () => {
    const compactTarget = '/documents/pm.invoice?panel=edit&id=doc-1'
    const compactWithState = '/documents/pm.invoice?search=late&panel=edit&id=doc-1&trash=deleted'
    const fullPageTarget = withBackTarget('/documents/pm.invoice/doc-1', compactWithState)

    expect(routeTargetMatches(compactWithState, compactTarget)).toBe(true)
    expect(routeTargetMatches('/documents/pm.invoice?panel=edit&id=doc-2', compactTarget)).toBe(false)
    expect(routeTargetMatches('/documents/pm.invoice/doc-1?panel=edit&id=doc-1', compactTarget)).toBe(false)
    expect(resolveBackTargetFromPath(fullPageTarget)).toBe(compactWithState)
    expect(resolveBackTargetFromPath('/documents/pm.invoice/doc-1')).toBeNull()
  })

  it('prefers an explicit encoded back target during navigation', async () => {
    const router = {
      replace: vi.fn().mockResolvedValue(undefined),
      back: vi.fn(),
    }

    await navigateBack(
      router as never,
      { query: { back: encodeBackTarget('/reports/occupancy') } } as never,
      '/home',
    )

    expect(router.replace).toHaveBeenCalledWith('/reports/occupancy')
    expect(router.back).not.toHaveBeenCalled()
  })

  it('uses the fallback target when browser history is shallow', async () => {
    globalThis.window = {
      history: {
        length: 1,
      },
    } as typeof window

    const router = {
      replace: vi.fn().mockResolvedValue(undefined),
      back: vi.fn(),
    }

    await navigateBack(
      router as never,
      { query: {} } as never,
      '/home',
    )

    expect(router.replace).toHaveBeenCalledWith('/home')
    expect(router.back).not.toHaveBeenCalled()
  })

  it('falls back to router.back when there is no explicit or shallow-history target', async () => {
    globalThis.window = {
      history: {
        length: 4,
      },
    } as typeof window

    const router = {
      replace: vi.fn(),
      back: vi.fn(),
    }

    await navigateBack(
      router as never,
      { query: {} } as never,
      '/home',
    )

    expect(router.replace).not.toHaveBeenCalled()
    expect(router.back).toHaveBeenCalledTimes(1)
  })
})
