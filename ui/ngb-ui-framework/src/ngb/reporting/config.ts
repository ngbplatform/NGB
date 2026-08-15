import type { Awaitable, LookupSource } from '../metadata/types'
import type { ReportLookupStoreApi } from './lookupFilters'
import type { ReportRouteContext, ReportSourceTrail } from './navigation'
import type { ReportCellActionDto } from './types'

export type ReportLookupTargetArgs = {
  hint: LookupSource | null
  value: unknown
  routeFullPath: string
}

export type ReportCellActionNavigationOptions = {
  currentReportContext?: ReportRouteContext | null
  sourceTrail?: ReportSourceTrail | null
  backTarget?: string | null
}

export type ReportingFrameworkConfig = {
  useLookupStore: () => ReportLookupStoreApi
  resolveLookupTarget?: (args: ReportLookupTargetArgs) => Awaitable<string | null>
  resolveCellActionUrl?: (
    action: ReportCellActionDto | null | undefined,
    options?: ReportCellActionNavigationOptions,
  ) => string | null
}

let configuredReporting: ReportingFrameworkConfig | null = null

export function configureNgbReporting(config: ReportingFrameworkConfig) {
  configuredReporting = config
}

export function getConfiguredNgbReporting(): ReportingFrameworkConfig {
  if (!configuredReporting) {
    throw new Error('NGB reporting framework is not configured. Call configureNgbReporting(...) during app bootstrap.')
  }

  return configuredReporting
}

export function maybeGetConfiguredNgbReporting(): ReportingFrameworkConfig | null {
  return configuredReporting
}

export async function resolveReportLookupTarget(args: ReportLookupTargetArgs): Promise<string | null> {
  const resolver = getConfiguredNgbReporting().resolveLookupTarget
  if (!resolver) return null
  return await resolver(args)
}

export function resolveReportCellActionUrl(
  action: ReportCellActionDto | null | undefined,
  options?: ReportCellActionNavigationOptions,
): string | null {
  const configured = maybeGetConfiguredNgbReporting()
  if (!configured?.resolveCellActionUrl) return null
  try {
    return configured.resolveCellActionUrl(action, options)
  } catch {
    return null
  }
}
