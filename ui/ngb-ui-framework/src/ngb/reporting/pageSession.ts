import {
  readStorageJsonOrNull,
  readStorageString,
  removeStorageItem,
  writeStorageJson,
  writeStorageString,
} from '../utils/storage'

import type { ReportExecutionResponseDto } from './types'

const EXECUTION_PREFIX = 'ngb.report.page.execution:'
const SCROLL_PREFIX = 'ngb.report.page.scroll:'

export type ReportPageExecutionSnapshot = {
  response: ReportExecutionResponseDto
  consumedCursors: string[]
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
  const storageKey = executionStorageKey(routeStateKey ?? null)
  if (!storageKey) return

  void writeStorageJson('session', storageKey, {
    response,
    consumedCursors: Array.from(new Set((consumedCursors ?? []).map((entry) => String(entry ?? '').trim()).filter((entry) => entry.length > 0))),
  } satisfies ReportPageExecutionSnapshot)
}

export function loadReportPageExecutionSnapshot(routeStateKey: string | null | undefined): ReportPageExecutionSnapshot | null {
  const storageKey = executionStorageKey(routeStateKey ?? null)
  if (!storageKey) return null

  const snapshot = readStorageJsonOrNull<ReportPageExecutionSnapshot>('session', storageKey)
  if (!snapshot?.response?.sheet) return null

  return {
    response: snapshot.response,
    consumedCursors: Array.from(new Set((snapshot.consumedCursors ?? []).map((entry) => String(entry ?? '').trim()).filter((entry) => entry.length > 0))),
  }
}

export function clearReportPageExecutionSnapshot(routeStateKey: string | null | undefined) {
  const storageKey = executionStorageKey(routeStateKey ?? null)
  if (!storageKey) return
  removeStorageItem('session', storageKey)
}

export function saveReportPageScrollTop(routeStateKey: string | null | undefined, scrollTop: number) {
  const storageKey = scrollStorageKey(routeStateKey ?? null)
  if (!storageKey) return

  const normalized = Number.isFinite(scrollTop) && scrollTop > 0 ? Math.floor(scrollTop) : 0
  if (normalized <= 0) {
    removeStorageItem('session', storageKey)
    return
  }

  void writeStorageString('session', storageKey, String(normalized))
}

export function loadReportPageScrollTop(routeStateKey: string | null | undefined): number {
  const storageKey = scrollStorageKey(routeStateKey ?? null)
  if (!storageKey) return 0

  const parsed = Number(readStorageString('session', storageKey) ?? '')
  return Number.isFinite(parsed) && parsed > 0 ? Math.floor(parsed) : 0
}

export function clearReportPageScrollTop(routeStateKey: string | null | undefined) {
  const storageKey = scrollStorageKey(routeStateKey ?? null)
  if (!storageKey) return
  removeStorageItem('session', storageKey)
}
