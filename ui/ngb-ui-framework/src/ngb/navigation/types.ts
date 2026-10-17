import type { RouteLocationNormalizedLoaded } from 'vue-router'
import type { NgbSourceIdentity } from '../api/sourceKinds'

export type NgbNavigationLocation = Pick<
  RouteLocationNormalizedLoaded,
  'path' | 'fullPath' | 'name' | 'params' | 'query' | 'meta'
>

export type NgbNavigationTarget = {
  code: string
  parameters: Record<string, string | null>
}

export type NgbNavigationContext = {
  resourceKind?: string | null
  resourceCode?: string | null
  entityId?: string | null
}

export type NgbNavigationRoutes = {
  workCenter: string
  workCenterPreferences: string
}

export type NgbNavigationConfig = {
  routes?: Partial<NgbNavigationRoutes>
  /** Return undefined for standard routes, or null to explicitly disable page context. */
  resolvePageContext?: (location: NgbNavigationLocation) => NgbSourceIdentity | null | undefined
  resolveTarget?: (
    target: NgbNavigationTarget,
    context: NgbNavigationContext,
  ) => string | null
}
