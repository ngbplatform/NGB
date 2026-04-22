import {
  type LocationQueryRaw,
  type RouteLocationNormalizedLoaded,
  type Router,
} from 'vue-router'
import {
  normalizeDateOnlyValue as normalizeSharedDateOnlyValue,
  normalizeMonthValue as normalizeSharedMonthValue,
} from '../utils/dateValues'

export type QueryTrashMode = 'active' | 'deleted' | 'all'
export type QueryPatch = Record<string, unknown>
export type QueryNavigationMode = 'push' | 'replace'

export function normalizeTrashMode(value: unknown): QueryTrashMode {
  const normalized = String(value ?? '').toLowerCase()
  if (normalized === 'deleted') return 'deleted'
  if (normalized === 'all') return 'all'
  return 'active'
}

export function normalizeMonthValue(value: unknown): string {
  return normalizeSharedMonthValue(value) ?? ''
}

export function normalizeMonthQueryValue(value: unknown): string | null {
  const normalized = normalizeMonthValue(value)
  return normalized || null
}

export function normalizeDateOnlyQueryValue(value: unknown): string | null {
  const normalized = normalizeSingleQueryValue(value)
  return normalizeSharedDateOnlyValue(normalized)
}

export function normalizeYearQueryValue(value: unknown): number | null {
  const normalized = normalizeSingleQueryValue(value)
  const parsed = Number.parseInt(normalized, 10)
  return Number.isInteger(parsed) && parsed >= 1900 && parsed <= 9999 ? parsed : null
}

export function normalizeSingleQueryValue(value: unknown): string {
  if (Array.isArray(value)) return String(value[0] ?? '').trim()
  return String(value ?? '').trim()
}

export function firstQueryValue(value: unknown): string | null {
  const normalized = normalizeSingleQueryValue(value)
  return normalized.length > 0 ? normalized : null
}

export function normalizeBooleanQueryFlag(value: unknown): boolean {
  const normalized = normalizeSingleQueryValue(value).toLowerCase()
  return normalized === '1' || normalized === 'true' || normalized === 'yes'
}

export function normalizeAllowedQueryValue<TValue extends string>(
  value: unknown,
  allowedValues: readonly TValue[],
): TValue | null {
  const normalized = normalizeSingleQueryValue(value)
  if (!normalized) return null

  const allowed = new Set<string>(allowedValues)
  return allowed.has(normalized) ? normalized as TValue : null
}

export function cleanQueryObject<T extends Record<string, unknown>>(query: T): T {
  const next = { ...query }
  for (const key of Object.keys(next)) {
    if (next[key] == null || next[key] === '') delete next[key]
  }
  return next
}

export function mergeCleanQuery<TBase extends Record<string, unknown>, TPatch extends Record<string, unknown>>(
  base: TBase,
  patch: TPatch,
): TBase & TPatch {
  return cleanQueryObject({ ...base, ...patch })
}

export function navigateCleanRouteQuery(
  route: RouteLocationNormalizedLoaded,
  router: Router,
  patch: QueryPatch,
  mode: QueryNavigationMode = 'replace',
) {
  return setCleanRouteQuery(route, router, { ...route.query, ...patch }, mode)
}

export function setCleanRouteQuery(
  route: RouteLocationNormalizedLoaded,
  router: Router,
  query: Record<string, unknown>,
  mode: QueryNavigationMode = 'replace',
) {
  const nextQuery = cleanQueryObject({ ...query }) as LocationQueryRaw
  return mode === 'push'
    ? router.push({ path: route.path, query: nextQuery })
    : router.replace({ path: route.path, query: nextQuery })
}

export function replaceCleanRouteQuery(
  route: RouteLocationNormalizedLoaded,
  router: Router,
  patch: QueryPatch,
) {
  return navigateCleanRouteQuery(route, router, patch, 'replace')
}

export function pushCleanRouteQuery(
  route: RouteLocationNormalizedLoaded,
  router: Router,
  patch: QueryPatch,
) {
  return navigateCleanRouteQuery(route, router, patch, 'push')
}

export function omitRouteQueryKeys(
  route: RouteLocationNormalizedLoaded,
  router: Router,
  keys: readonly string[],
  mode: QueryNavigationMode = 'replace',
) {
  const next = { ...route.query } as QueryPatch
  for (const key of keys) delete next[key]
  return setCleanRouteQuery(route, router, next, mode)
}
