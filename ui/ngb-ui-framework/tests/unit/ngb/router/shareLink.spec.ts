import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  buildAbsoluteAppUrl,
  copyAppLink,
} from '../../../../src/ngb/router/shareLink'

describe('shareLink', () => {
  const originalWindow = globalThis.window
  const originalNavigator = globalThis.navigator

  afterEach(() => {
    globalThis.window = originalWindow as typeof window
    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: originalNavigator,
    })
  })

  it('builds an absolute app URL from the router href and browser origin', () => {
    globalThis.window = {
      location: {
        origin: 'https://app.ngb.test',
      },
    } as typeof window

    const router = {
      resolve: vi.fn(() => ({
        href: '/documents/pm.invoice?id=doc-1',
      })),
    }

    expect(buildAbsoluteAppUrl(router as never, '/documents/pm.invoice')).toBe(
      'https://app.ngb.test/documents/pm.invoice?id=doc-1',
    )

    globalThis.window = undefined as never
    expect(buildAbsoluteAppUrl(router as never, '/documents/pm.invoice')).toBe('/documents/pm.invoice?id=doc-1')
  })

  it('copies an app link to the clipboard and pushes a success toast', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    const toasts = {
      push: vi.fn(),
    }
    globalThis.window = {
      location: {
        origin: 'https://app.ngb.test',
      },
    } as typeof window
    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: {
        clipboard: {
          writeText,
        },
      },
    })

    const router = {
      resolve: vi.fn(() => ({
        href: '/reports/occupancy?variant=default',
      })),
    }

    await expect(copyAppLink(
      router as never,
      toasts,
      '/reports/occupancy',
      {
        title: 'Copied report link',
        message: 'Ready to share.',
      },
    )).resolves.toBe(true)

    expect(writeText).toHaveBeenCalledWith('https://app.ngb.test/reports/occupancy?variant=default')
    expect(toasts.push).toHaveBeenCalledWith({
      title: 'Copied report link',
      message: 'Ready to share.',
      tone: 'neutral',
    })
  })

  it('reports clipboard failures through a danger toast', async () => {
    const toasts = {
      push: vi.fn(),
    }
    globalThis.window = {
      location: {
        origin: 'https://app.ngb.test',
      },
    } as typeof window
    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: {
        clipboard: {
          writeText: vi.fn().mockRejectedValue(new Error('Clipboard denied')),
        },
      },
    })

    const router = {
      resolve: vi.fn(() => ({
        href: '/documents/pm.invoice/doc-1',
      })),
    }

    await expect(copyAppLink(
      router as never,
      toasts,
      '/documents/pm.invoice/doc-1',
    )).resolves.toBe(false)

    expect(toasts.push).toHaveBeenCalledWith({
      title: 'Could not copy link',
      message: 'Clipboard denied',
      tone: 'danger',
    })
  })

  it('uses default success copy and reports an unavailable clipboard', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    const toasts = { push: vi.fn() }
    globalThis.window = { location: { origin: 'https://app.ngb.test' } } as typeof window
    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: { clipboard: { writeText } },
    })
    const router = { resolve: vi.fn(() => ({ href: '/home' })) }

    await expect(copyAppLink(router as never, toasts, '/home')).resolves.toBe(true)
    expect(toasts.push).toHaveBeenLastCalledWith({
      title: 'Link copied',
      message: 'Shareable link copied to clipboard.',
      tone: 'neutral',
    })

    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: {},
    })
    await expect(copyAppLink(router as never, toasts, '/home')).resolves.toBe(false)
    expect(toasts.push).toHaveBeenLastCalledWith({
      title: 'Could not copy link',
      message: 'Clipboard is not available.',
      tone: 'danger',
    })
  })
})
