export function normalizeRouteParam(value: unknown): string | null {
  const raw = Array.isArray(value) ? value[0] : value
  const normalized = String(raw ?? '').trim()
  return normalized || null
}

export function normalizeRequiredRouteParam(value: unknown): string {
  return normalizeRouteParam(value) ?? ''
}

export function normalizeEntityEditorIdRouteParam(value: unknown): string | undefined {
  const normalized = normalizeRouteParam(value)
  if (!normalized) return undefined
  return normalized.toLowerCase() === 'new' ? undefined : normalized
}
