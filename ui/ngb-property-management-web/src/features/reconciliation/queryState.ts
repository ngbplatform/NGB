import type { RouteLocationNormalizedLoaded, Router } from 'vue-router'

import { useRouteQueryMigration } from 'ngb-ui-framework'
import type { ReconciliationMode, ReconciliationStatusFilter } from './types'

export function normalizeReconciliationMode(value: unknown): ReconciliationMode {
  const raw = Array.isArray(value) ? value[0] : value
  const normalized = String(raw ?? '').trim().toLowerCase()
  return normalized === 'movement' ? 'Movement' : 'Balance'
}

export function normalizeReconciliationStatusFilter(statusValue: unknown): ReconciliationStatusFilter {
  const rawStatus = Array.isArray(statusValue) ? statusValue[0] : statusValue
  const normalizedStatus = String(rawStatus ?? '').trim().toLowerCase()

  switch (normalizedStatus) {
    case 'matched':
      return 'matched'
    case 'mismatch':
      return 'mismatch'
    case 'gl-only':
    case 'glonly':
      return 'glOnly'
    case 'open-items-only':
    case 'openitemsonly':
      return 'openItemsOnly'
    default:
      return 'all'
  }
}

export function encodeReconciliationStatusFilter(value: ReconciliationStatusFilter): string | undefined {
  switch (value) {
    case 'matched':
      return 'matched'
    case 'mismatch':
      return 'mismatch'
    case 'glOnly':
      return 'gl-only'
    case 'openItemsOnly':
      return 'open-items-only'
    default:
      return undefined
  }
}

function normalizeLegacyRowsFilter(legacyRowsValue: unknown): ReconciliationStatusFilter | null {
  const rawRows = Array.isArray(legacyRowsValue) ? legacyRowsValue[0] : legacyRowsValue
  const normalizedRows = String(rawRows ?? '').trim().toLowerCase()
  return normalizedRows === 'mismatches' ? 'mismatch' : null
}

export function useReconciliationLegacyQueryCompat(
  route: RouteLocationNormalizedLoaded,
  router: Router,
) {
  useRouteQueryMigration({
    route,
    router,
    sources: () => [route.query.status, route.query.rows] as const,
    migrate: ([statusValue, rowsValue]) => {
      if (rowsValue == null) return null

      const migrated = normalizeLegacyRowsFilter(rowsValue)
      const status = normalizeReconciliationStatusFilter(statusValue)
      if (!migrated || status === migrated) {
        return { rows: undefined }
      }

      return {
        status: encodeReconciliationStatusFilter(migrated),
        rows: undefined,
      }
    },
  })
}
