import { describe, expect, it } from 'vitest'

import {
  resolveCommandPaletteBadge,
  resolveCommandPaletteIcon,
  resolveCommandPaletteSubtitle,
} from '../../../../src/ngb/command-palette/presentation'
import type { CommandPaletteItem } from '../../../../src/ngb/command-palette/types'

function createItem(overrides: Partial<CommandPaletteItem> = {}): CommandPaletteItem {
  return {
    key: 'item',
    group: 'go-to',
    kind: 'page',
    scope: 'pages',
    title: 'Home',
    subtitle: null,
    icon: null,
    badge: null,
    hint: null,
    route: '/home',
    commandCode: null,
    status: null,
    openInNewTabSupported: true,
    keywords: [],
    perform: undefined,
    defaultRank: 100,
    score: 100,
    isCurrentContext: false,
    isFavorite: false,
    isRecent: false,
    source: 'local',
    ...overrides,
  }
}

describe('command-palette presentation', () => {
  it('coerces icons and falls back to search for missing values', () => {
    expect(resolveCommandPaletteIcon('settings')).toBe('settings')
    expect(resolveCommandPaletteIcon('')).toBe('search')
    expect(resolveCommandPaletteIcon('not-a-real-icon')).toBe('search')
  })

  it('prefers explicit non-generic badges and derives defaults by item kind', () => {
    expect(resolveCommandPaletteBadge(createItem({ badge: 'Pinned' }))).toBe('Pinned')
    expect(resolveCommandPaletteBadge(createItem({ badge: 'Favorite' }))).toBe('Page')
    expect(resolveCommandPaletteBadge(createItem({ kind: 'document', scope: 'documents' }))).toBe('Document')
    expect(resolveCommandPaletteBadge(createItem({ kind: 'catalog', scope: 'catalogs' }))).toBe('Catalog')
    expect(resolveCommandPaletteBadge(createItem({ kind: 'report', scope: 'reports' }))).toBe('Report')
    expect(resolveCommandPaletteBadge(createItem({ kind: 'command', group: 'actions', scope: 'commands' }))).toBe('Action')
    expect(resolveCommandPaletteBadge(createItem({ kind: 'recent', scope: 'catalogs' }))).toBe('Catalog')
  })

  it('prefers specific subtitles and normalizes generic ones into friendly actions', () => {
    expect(resolveCommandPaletteSubtitle(createItem({ subtitle: 'Portfolio dashboard' }))).toBe('Portfolio dashboard')
    expect(resolveCommandPaletteSubtitle(createItem({ subtitle: 'Favorite' }))).toBe('Open this page')
    expect(resolveCommandPaletteSubtitle(createItem({ kind: 'document', scope: 'documents', subtitle: 'Document' }))).toBe('Open this document')
    expect(resolveCommandPaletteSubtitle(createItem({ kind: 'catalog', scope: 'catalogs', subtitle: 'Catalog' }))).toBe('Open this catalog record')
    expect(resolveCommandPaletteSubtitle(createItem({ kind: 'report', scope: 'reports', subtitle: 'Report' }))).toBe('Run this report')
    expect(resolveCommandPaletteSubtitle(createItem({ kind: 'command', group: 'actions', scope: 'commands', subtitle: 'Favorite' }))).toBe('Run this command')
    expect(resolveCommandPaletteSubtitle(createItem({ kind: 'recent', scope: 'reports', subtitle: 'Recent' }))).toBe('Open this recent report')
  })
})
