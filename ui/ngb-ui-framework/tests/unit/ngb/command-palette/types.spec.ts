import { describe, expect, it } from 'vitest'

import type {
  CommandPaletteItem,
  CommandPaletteRecentEntry,
  CommandPaletteSearchRequestDto,
  CommandPaletteSearchResponseDto,
} from '../../../../src/ngb/command-palette/types'

describe('command palette types', () => {
  it('supports item, recent entry, and search dto contracts', () => {
    const item: CommandPaletteItem = {
      key: 'open-invoice',
      group: 'actions',
      kind: 'command',
      scope: 'commands',
      title: 'Open invoice',
      icon: 'file-text',
      defaultRank: 10,
      score: 100,
      source: 'local',
    }
    const recent: CommandPaletteRecentEntry = {
      key: 'recent-invoice',
      kind: 'document',
      scope: 'documents',
      title: 'Invoice INV-001',
      route: '/documents/pm.invoice/doc-1',
      timestamp: '2026-04-08T12:00:00Z',
    }
    const request: CommandPaletteSearchRequestDto = {
      query: 'invoice',
      scope: 'documents',
      currentRoute: '/documents/pm.invoice',
      limit: 5,
    }
    const response: CommandPaletteSearchResponseDto = {
      groups: [
        {
          code: 'documents',
          label: 'Documents',
          items: [
            {
              key: 'pm.invoice:doc-1',
              kind: 'document',
              title: 'Invoice INV-001',
              route: '/documents/pm.invoice/doc-1',
              openInNewTabSupported: true,
              score: 88,
            },
          ],
        },
      ],
    }

    expect(item.group).toBe('actions')
    expect(recent.route).toContain('/documents/')
    expect(request.scope).toBe('documents')
    expect(response.groups[0]?.items[0]?.score).toBe(88)
  })
})
