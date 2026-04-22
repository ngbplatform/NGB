import type { CommandPaletteGroupCode, CommandPaletteScope } from './types'

type SearchField = {
  value?: string | null
  exact: number
  prefix: number
  wordPrefix: number
  contains: number
}

export function normalizeSearchText(value: string | null | undefined): string {
  const source = String(value ?? '').trim().toLowerCase()
  if (!source) return ''

  let out = ''
  let lastWasSpace = false

  for (const char of source) {
    if (/[a-z0-9]/.test(char)) {
      out += char
      lastWasSpace = false
      continue
    }

    if (lastWasSpace) continue
    out += ' '
    lastWasSpace = true
  }

  return out.trim()
}

export function parseCommandPaletteQuery(value: string): { rawQuery: string; query: string; scope: CommandPaletteScope | null } {
  const rawQuery = String(value ?? '')
  const trimmed = rawQuery.trimStart()
  const prefix = trimmed.charAt(0)

  const scope = prefixToScope(prefix)
  if (!scope) {
    return {
      rawQuery,
      query: rawQuery.trim(),
      scope: null,
    }
  }

  return {
    rawQuery,
    query: trimmed.slice(1).trim(),
    scope,
  }
}

export function prefixToScope(prefix: string): CommandPaletteScope | null {
  switch (prefix) {
    case '>':
      return 'commands'
    case '/':
      return 'pages'
    case '#':
      return 'reports'
    case ':':
      return 'documents'
    case '@':
      return 'catalogs'
    default:
      return null
  }
}

export function scoreSearchText(query: string, fields: SearchField[]): number {
  const normalizedQuery = normalizeSearchText(query)
  if (!normalizedQuery) return 0

  let best = 0
  for (const field of fields) {
    best = Math.max(best, scoreField(normalizedQuery, field))
  }

  return best
}

export function groupOrder(code: CommandPaletteGroupCode): number {
  switch (code) {
    case 'actions':
      return 0
    case 'go-to':
      return 1
    case 'documents':
      return 2
    case 'catalogs':
      return 3
    case 'reports':
      return 4
    case 'recent':
      return 5
    default:
      return 99
  }
}

export function defaultSearchFields(...values: Array<string | null | undefined>): SearchField[] {
  return values
    .filter((value) => String(value ?? '').trim().length > 0)
    .map((value, index) => ({
      value,
      exact: index === 0 ? 1 : 0.86,
      prefix: index === 0 ? 0.92 : 0.8,
      wordPrefix: index === 0 ? 0.88 : 0.76,
      contains: index === 0 ? 0.76 : 0.68,
    }))
}

function scoreField(normalizedQuery: string, field: SearchField): number {
  const normalizedValue = normalizeSearchText(field.value)
  if (!normalizedValue) return 0

  if (normalizedValue === normalizedQuery) return field.exact
  if (normalizedValue.startsWith(normalizedQuery)) return field.prefix
  if (containsWordPrefix(normalizedValue, normalizedQuery)) return field.wordPrefix
  if (normalizedValue.includes(normalizedQuery)) return field.contains

  return 0
}

function containsWordPrefix(value: string, query: string): boolean {
  const parts = value.split(' ')
  return parts.some((part) => part.startsWith(query))
}

