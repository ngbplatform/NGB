import { describe, expect, it, vi } from 'vitest'

const readStorageJsonMock = vi.hoisted(() => vi.fn())
const writeStorageJsonMock = vi.hoisted(() => vi.fn())

vi.mock('../../../../src/ngb/utils/storage', () => ({
  readStorageJson: readStorageJsonMock,
  writeStorageJson: writeStorageJsonMock,
}))

import {
  loadCommandPaletteRecent,
  saveCommandPaletteRecent,
} from '../../../../src/ngb/command-palette/storage'

describe('command palette recent storage', () => {
  it('loads, filters, sorts, and caps recent entries', () => {
    readStorageJsonMock.mockReturnValue([
      null,
      {
        key: 'invoice:2',
        kind: 'document',
        scope: 'documents',
        title: 'Invoice INV-002',
        timestamp: '2026-04-08T12:00:00.000Z',
      },
      {
        key: 'invoice:1',
        kind: 'document',
        scope: 'documents',
        title: 'Invoice INV-001',
        timestamp: '2026-04-08T13:00:00.000Z',
        route: '/documents/pm.invoice/doc-1',
      },
      ...Array.from({ length: 12 }, (_, index) => ({
        key: `page:${index}`,
        kind: 'page',
        scope: 'pages',
        title: `Page ${index}`,
        timestamp: `2026-04-${String((index % 9) + 1).padStart(2, '0')}T00:00:00.000Z`,
      })),
      {
        key: '',
        kind: 'page',
        scope: 'pages',
        title: 'Broken',
        timestamp: '2026-04-08T12:00:00.000Z',
      },
    ])

    const recent = loadCommandPaletteRecent('ngb:test:recent')

    expect(readStorageJsonMock).toHaveBeenCalledWith('local', 'ngb:test:recent', [])
    expect(recent).toHaveLength(10)
    expect(recent[0]?.timestamp >= recent[1]?.timestamp).toBe(true)
    expect(recent).toContainEqual(expect.objectContaining({
      key: 'invoice:1',
      title: 'Invoice INV-001',
      route: '/documents/pm.invoice/doc-1',
    }))
    expect(recent.some((entry) => entry.key === '')).toBe(false)
  })

  it('saves only the first ten entries', () => {
    const entries = Array.from({ length: 12 }, (_, index) => ({
      key: `entry:${index}`,
      kind: 'page' as const,
      scope: 'pages' as const,
      title: `Entry ${index}`,
      subtitle: null,
      icon: null,
      badge: null,
      route: `/pages/${index}`,
      status: null,
      openInNewTabSupported: true,
      timestamp: `2026-04-${String((index % 9) + 1).padStart(2, '0')}T00:00:00.000Z`,
    }))

    saveCommandPaletteRecent('ngb:test:recent', entries)

    expect(writeStorageJsonMock).toHaveBeenCalledWith('local', 'ngb:test:recent', entries.slice(0, 10))
  })
})
