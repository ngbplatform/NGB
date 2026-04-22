import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import type { RouteLocationNormalizedLoaded, Router } from 'vue-router'
import { monthValueToDateOnly, normalizeMonthValue } from '../utils/dateValues'
import { normalizeTrashMode, pushCleanRouteQuery, type QueryTrashMode } from '../router/queryParams'

type UseMonthPagedListQueryArgs = {
  route: RouteLocationNormalizedLoaded
  router: Router
  defaultLimit?: number
}

function parseNonNegativeInteger(value: unknown, fallback: number): number {
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed >= 0 ? Math.floor(parsed) : fallback
}

function parsePositiveInteger(value: unknown, fallback: number): number {
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? Math.floor(parsed) : fallback
}

export function monthValueToDateOnlyStart(monthValue: string): string | undefined {
  return monthValueToDateOnly(monthValue) ?? undefined
}

export function monthValueToDateOnlyEnd(monthValue: string): string | undefined {
  if (!/^\d{4}-\d{2}$/.test(monthValue)) return undefined

  const [year, month] = monthValue.split('-').map((part) => Number(part))
  if (!Number.isFinite(year) || !Number.isFinite(month)) return undefined

  const lastDay = new Date(year, month, 0).getDate()
  return `${monthValue}-${String(lastDay).padStart(2, '0')}`
}

export function useMonthPagedListQuery(args: UseMonthPagedListQueryArgs) {
  const defaultLimit = args.defaultLimit ?? 50

  const offset = computed(() => parseNonNegativeInteger(args.route.query.offset, 0))
  const limit = computed(() => parsePositiveInteger(args.route.query.limit, defaultLimit))

  function pushQuery(partial: Record<string, unknown>, options?: { preserveOffset?: boolean }) {
    void pushCleanRouteQuery(args.route, args.router, {
      ...partial,
      ...(options?.preserveOffset ? {} : { offset: 0 }),
    })
  }

  const trashMode = computed<QueryTrashMode>({
    get() {
      return normalizeTrashMode(args.route.query.trash)
    },
    set(value) {
      pushQuery({ trash: value })
    },
  })

  const periodFromMonth = computed<string>({
    get() {
      return normalizeMonthValue(args.route.query.periodFrom) ?? ''
    },
    set(value) {
      pushQuery({ periodFrom: value || undefined })
    },
  })

  const periodToMonth = computed<string>({
    get() {
      return normalizeMonthValue(args.route.query.periodTo) ?? ''
    },
    set(value) {
      pushQuery({ periodTo: value || undefined })
    },
  })

  function updateListQuery(partial: Record<string, unknown>, options?: { preserveOffset?: boolean }) {
    pushQuery(partial, options)
  }

  function nextPage() {
    pushQuery({ offset: offset.value + limit.value }, { preserveOffset: true })
  }

  function prevPage() {
    pushQuery({ offset: Math.max(0, offset.value - limit.value) }, { preserveOffset: true })
  }

  return {
    offset,
    limit,
    trashMode,
    periodFromMonth,
    periodToMonth,
    updateListQuery,
    nextPage,
    prevPage,
  } satisfies {
    offset: ComputedRef<number>
    limit: ComputedRef<number>
    trashMode: ComputedRef<QueryTrashMode>
    periodFromMonth: ComputedRef<string>
    periodToMonth: ComputedRef<string>
    updateListQuery: (partial: Record<string, unknown>, options?: { preserveOffset?: boolean }) => void
    nextPage: () => void
    prevPage: () => void
  }
}
