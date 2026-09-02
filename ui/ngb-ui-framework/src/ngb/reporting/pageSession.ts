import {
  listStorageKeys,
  readStorageJsonOrNull,
  readStorageString,
  removeStorageItem,
  writeStorageJson,
  writeStorageString,
} from '../utils/storage'

import type { ReportExecutionResponseDto } from './types'

const EXECUTION_PREFIX = 'ngb.report.page.execution:'
const SCROLL_PREFIX = 'ngb.report.page.scroll:'
const MAX_PERSISTED_REPORT_ROWS = 500
const MAX_PERSISTED_REPORT_BYTES = 512 * 1024
const MAX_PERSISTED_REPORT_SNAPSHOTS = 8

export type ReportPageExecutionSnapshot = {
  response: ReportExecutionResponseDto
  consumedCursors: string[]
  savedAtMs?: number
}

function removeOldestExecutionSnapshots(currentStorageKey: string) {
  const candidates = listStorageKeys('session')
    .filter((key) => key.startsWith(EXECUTION_PREFIX) && key !== currentStorageKey)
    .map((key) => ({
      key,
      savedAtMs: readStorageJsonOrNull<ReportPageExecutionSnapshot>('session', key)?.savedAtMs ?? 0,
    }))
    .sort((left, right) => left.savedAtMs - right.savedAtMs)

  const excess = Math.max(0, candidates.length - (MAX_PERSISTED_REPORT_SNAPSHOTS - 1))
  for (const candidate of candidates.slice(0, excess)) removeStorageItem('session', candidate.key)
}

function normalizeKey(key: string | null | undefined): string | null {
  const normalized = String(key ?? '').trim()
  return normalized.length > 0 ? normalized : null
}

function executionStorageKey(routeStateKey: string | null | undefined): string | null {
  const normalized = normalizeKey(routeStateKey)
  return normalized ? `${EXECUTION_PREFIX}${normalized}` : null
}

function scrollStorageKey(routeStateKey: string | null | undefined): string | null {
  const normalized = normalizeKey(routeStateKey)
  return normalized ? `${SCROLL_PREFIX}${normalized}` : null
}

export function saveReportPageExecutionSnapshot(
  routeStateKey: string | null | undefined,
  response: ReportExecutionResponseDto,
  consumedCursors: string[],
) {
  const storageKey = executionStorageKey(routeStateKey)
  if (!storageKey) return

  // Large report pages are intentionally not mirrored into synchronous browser
  // storage. Re-running on navigation is cheaper and safer than repeatedly
  // serializing multi-megabyte sheets on the UI thread.
  if ((response.sheet.rows?.length ?? 0) > MAX_PERSISTED_REPORT_ROWS) {
    removeStorageItem('session', storageKey)
    return
  }

  const snapshot = {
    response,
    consumedCursors: Array.from(new Set(consumedCursors.map((entry) => entry.trim()).filter((entry) => entry.length > 0))),
    savedAtMs: Date.now(),
  } satisfies ReportPageExecutionSnapshot

  // Row count alone does not protect wide pivot reports. Keep synchronous
  // sessionStorage payloads bounded by their serialized size as well.
  if (JSON.stringify(snapshot).length * 2 > MAX_PERSISTED_REPORT_BYTES) {
    removeStorageItem('session', storageKey)
    return
  }

  removeOldestExecutionSnapshots(storageKey)
  void writeStorageJson('session', storageKey, snapshot)
}

export function loadReportPageExecutionSnapshot(routeStateKey: string | null | undefined): ReportPageExecutionSnapshot | null {
  const storageKey = executionStorageKey(routeStateKey)
  if (!storageKey) return null

  const snapshot = readStorageJsonOrNull<ReportPageExecutionSnapshot>('session', storageKey)
  if (!snapshot?.response?.sheet) return null

  return {
    response: snapshot.response,
    consumedCursors: Array.from(new Set((snapshot.consumedCursors ?? []).map((entry) => String(entry ?? '').trim()).filter((entry) => entry.length > 0))),
  }
}

export function clearReportPageExecutionSnapshot(routeStateKey: string | null | undefined) {
  const storageKey = executionStorageKey(routeStateKey)
  if (!storageKey) return
  removeStorageItem('session', storageKey)
}

export function saveReportPageScrollTop(routeStateKey: string | null | undefined, scrollTop: number) {
  const storageKey = scrollStorageKey(routeStateKey)
  if (!storageKey) return

  const normalized = Number.isFinite(scrollTop) && scrollTop > 0 ? Math.floor(scrollTop) : 0
  if (normalized <= 0) {
    removeStorageItem('session', storageKey)
    return
  }

  void writeStorageString('session', storageKey, String(normalized))
}

export function loadReportPageScrollTop(routeStateKey: string | null | undefined): number {
  const storageKey = scrollStorageKey(routeStateKey)
  if (!storageKey) return 0

  const parsed = Number(readStorageString('session', storageKey) ?? '')
  return Number.isFinite(parsed) && parsed > 0 ? Math.floor(parsed) : 0
}

export function clearReportPageScrollTop(routeStateKey: string | null | undefined) {
  const storageKey = scrollStorageKey(routeStateKey)
  if (!storageKey) return
  removeStorageItem('session', storageKey)
}
