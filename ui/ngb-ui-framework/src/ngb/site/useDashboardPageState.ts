import { computed, ref, watch } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'

import { normalizeDateOnlyQueryValue, replaceCleanRouteQuery } from '../router/queryParams'
import { toDateOnlyValue } from '../utils/dateValues'
import { toErrorMessage } from '../utils/errorMessage'

export type DashboardWarningResolver<TDashboard> = (
  dashboard: TDashboard | null,
) => readonly string[] | null | undefined

export type UseDashboardPageStateArgs<TDashboard> = {
  load: (asOf: string) => Promise<TDashboard>
  queryKey?: string
  route?: RouteLocationNormalizedLoaded
  router?: Router
  fallbackAsOf?: () => string
  resolveWarnings?: DashboardWarningResolver<TDashboard>
}

function defaultResolveWarnings<TDashboard>(
  dashboard: TDashboard | null,
): readonly string[] | null | undefined {
  if (!dashboard || typeof dashboard !== 'object' || !('warnings' in dashboard)) return null
  const value = (dashboard as { warnings?: unknown }).warnings
  return Array.isArray(value) ? value.map((item) => String(item ?? '').trim()) : null
}

export function useDashboardPageState<TDashboard>(
  args: UseDashboardPageStateArgs<TDashboard>,
) {
  const route = args.route ?? useRoute()
  const router = args.router ?? useRouter()
  const queryKey = String(args.queryKey ?? 'asOf').trim() || 'asOf'
  const resolveAsOf = args.fallbackAsOf ?? (() => toDateOnlyValue(new Date()))
  const resolveWarnings = args.resolveWarnings ?? defaultResolveWarnings

  const dashboard = ref<TDashboard | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)
  const refreshTick = ref(0)

  let loadSequence = 0

  const asOf = computed<string>({
    get: () => normalizeDateOnlyQueryValue(route.query[queryKey]) ?? resolveAsOf(),
    set: (value) => {
      void replaceCleanRouteQuery(route, router, { [queryKey]: value })
    },
  })

  const warnings = computed(() => {
    const values = resolveWarnings(dashboard.value) ?? []
    return values
      .map((value) => String(value ?? '').trim())
      .filter((value, index, items) => value.length > 0 && items.indexOf(value) === index)
  })

  async function reload(): Promise<void> {
    const seq = ++loadSequence
    loading.value = true
    error.value = null

    try {
      const next = await args.load(asOf.value)
      if (seq !== loadSequence) return
      dashboard.value = next
    } catch (err: unknown) {
      if (seq !== loadSequence) return
      dashboard.value = null
      error.value = toErrorMessage(err, 'Request failed.')
    } finally {
      if (seq === loadSequence) loading.value = false
    }
  }

  function refresh(): void {
    refreshTick.value += 1
  }

  watch(
    () => [asOf.value, refreshTick.value] as const,
    () => {
      void reload()
    },
    { immediate: true },
  )

  return {
    asOf,
    dashboard,
    error,
    loading,
    refresh,
    reload,
    warnings,
  }
}
