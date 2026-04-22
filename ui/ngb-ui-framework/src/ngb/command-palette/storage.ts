import { readStorageJson, writeStorageJson } from '../utils/storage'
import type { CommandPaletteRecentEntry } from './types'

export function loadCommandPaletteRecent(storageKey: string): CommandPaletteRecentEntry[] {
  const parsed = readStorageJson<unknown[]>('local', storageKey, [])
  if (!Array.isArray(parsed)) return []

  return parsed
    .map((entry) => normalizeRecentEntry(entry))
    .filter((entry): entry is CommandPaletteRecentEntry => !!entry)
    .sort((a, b) => String(b.timestamp).localeCompare(String(a.timestamp)))
    .slice(0, 10)
}

export function saveCommandPaletteRecent(storageKey: string, entries: CommandPaletteRecentEntry[]): void {
  void writeStorageJson('local', storageKey, entries.slice(0, 10))
}

function normalizeRecentEntry(value: unknown): CommandPaletteRecentEntry | null {
  if (!value || typeof value !== 'object') return null
  const entry = value as Record<string, unknown>

  const key = String(entry.key ?? '').trim()
  const kind = String(entry.kind ?? '').trim()
  const scope = String(entry.scope ?? '').trim()
  const title = String(entry.title ?? '').trim()
  const timestamp = String(entry.timestamp ?? '').trim()

  if (!key || !kind || !scope || !title || !timestamp) return null

  return {
    key,
    kind: kind as CommandPaletteRecentEntry['kind'],
    scope: scope as CommandPaletteRecentEntry['scope'],
    title,
    subtitle: typeof entry.subtitle === 'string' ? entry.subtitle : null,
    icon: typeof entry.icon === 'string' ? entry.icon : null,
    badge: typeof entry.badge === 'string' ? entry.badge : null,
    route: typeof entry.route === 'string' ? entry.route : null,
    status: typeof entry.status === 'string' ? entry.status : null,
    openInNewTabSupported: Boolean(entry.openInNewTabSupported),
    timestamp,
  }
}

