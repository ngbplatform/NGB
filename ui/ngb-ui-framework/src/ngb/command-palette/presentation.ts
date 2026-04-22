import { coerceNgbIconName, type NgbIconName } from '../primitives/iconNames'
import type { CommandPaletteItem } from './types'

const GENERIC_BADGES = new Set(['Favorite', 'Recent'])
const GENERIC_SUBTITLES = new Set([
  'Favorite',
  'Favorite report',
  'Recent',
  'Report',
  'Page',
  'Document',
  'Catalog',
])

function cleanMeta(value: string | null | undefined): string | null {
  const next = String(value ?? '').trim()
  return next.length > 0 ? next : null
}

export function resolveCommandPaletteIcon(icon: string | null | undefined): NgbIconName {
  return coerceNgbIconName(icon, 'search')
}

export function resolveCommandPaletteBadge(item: CommandPaletteItem): string | null {
  const badge = cleanMeta(item.badge)
  if (badge && !GENERIC_BADGES.has(badge)) return badge

  switch (item.kind) {
    case 'page':
      return 'Page'
    case 'document':
      return 'Document'
    case 'catalog':
      return 'Catalog'
    case 'report':
      return 'Report'
    case 'command':
      return 'Action'
    case 'recent':
      switch (item.scope) {
        case 'reports':
          return 'Report'
        case 'documents':
          return 'Document'
        case 'catalogs':
          return 'Catalog'
        default:
          return 'Page'
      }
    default:
      return null
  }
}

export function resolveCommandPaletteSubtitle(item: CommandPaletteItem): string {
  const subtitle = cleanMeta(item.subtitle)
  if (subtitle && !GENERIC_SUBTITLES.has(subtitle)) return subtitle

  switch (item.kind) {
    case 'page':
      return 'Open this page'
    case 'document':
      return 'Open this document'
    case 'catalog':
      return 'Open this catalog record'
    case 'report':
      return 'Run this report'
    case 'command':
      return 'Run this command'
    case 'recent':
      switch (item.scope) {
        case 'reports':
          return 'Open this recent report'
        case 'documents':
          return 'Open this recent document'
        case 'catalogs':
          return 'Open this recent catalog record'
        default:
          return 'Open this recent page'
      }
    default:
      return 'Open this item'
  }
}

