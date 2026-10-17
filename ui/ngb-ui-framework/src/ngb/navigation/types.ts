import type { RouteLocationNormalizedLoaded } from 'vue-router'
import type { NgbSourceIdentity } from '../api/sourceKinds'
import type { ReportExecutionRequestDto } from '../reporting/types'

/** Page identifiers and query criteria only; never loaded business data. */
export type NgbPageContext = NgbSourceIdentity & Pick<ReportExecutionRequestDto, 'parameters' | 'filters'>

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
  resolvePageContext?: (location: NgbNavigationLocation) => NgbPageContext | null | undefined
  resolveTarget?: (
    target: NgbNavigationTarget,
    context: NgbNavigationContext,
  ) => string | null
}
