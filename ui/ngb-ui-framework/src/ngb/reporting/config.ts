import { buildChartOfAccountsPath } from '../accounting/navigation'
import { buildCatalogFullPageUrl } from '../editor/catalogNavigation'
import { buildDocumentFullPageUrl } from '../editor/documentNavigation'
import type { Awaitable, LookupSource } from '../metadata/types'
import { withBackTarget } from '../router/backNavigation'
import { isGuidString } from '../utils/guid'
import type { ReportLookupStoreApi } from './lookupFilters'
import { appendSourceTrail, buildReportPageUrl, type ReportRouteContext, type ReportSourceTrail } from './navigation'
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

function buildOpenReportActionUrl(
  action: ReportCellActionDto,
  options?: ReportCellActionNavigationOptions,
): string | null {
  const reportCode = String(action.report?.reportCode ?? '').trim()
  if (!reportCode) return null

  return buildReportPageUrl(reportCode, {
    context: {
      reportCode,
      request: {
        parameters: action.report?.parameters ?? null,
        filters: action.report?.filters ?? null,
        layout: null,
        offset: 0,
        limit: 500,
        cursor: null,
      },
    },
    sourceTrail: appendSourceTrail(options?.sourceTrail ?? null, options?.currentReportContext ?? null),
    backTarget: options?.backTarget ?? null,
  })
}

function withOptionalBackTarget(url: string, backTarget?: string | null): string {
  return withBackTarget(url, backTarget ?? null)
}

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
  const configuredTarget = configured?.resolveCellActionUrl?.(action, options)
  if (configuredTarget) return configuredTarget
  if (!action || !action.kind) return null

  try {
    if (action.kind === 'open_document') {
      const documentType = String(action.documentType ?? '').trim()
      const id = String(action.documentId ?? '').trim()
      if (!documentType || !id || !isGuidString(id)) return null
      return withOptionalBackTarget(buildDocumentFullPageUrl(documentType, id), options?.backTarget)
    }

    if (action.kind === 'open_account') {
      const accountId = String(action.accountId ?? '').trim()
      if (!accountId || !isGuidString(accountId)) return null
      return withOptionalBackTarget(buildChartOfAccountsPath({ panel: 'edit', id: accountId }), options?.backTarget)
    }

    if (action.kind === 'open_catalog') {
      const catalogType = String(action.catalogType ?? '').trim()
      const id = String(action.catalogId ?? '').trim()
      if (!catalogType || !id || !isGuidString(id)) return null
      return withOptionalBackTarget(buildCatalogFullPageUrl(catalogType, id), options?.backTarget)
    }

    if (action.kind === 'open_report') return buildOpenReportActionUrl(action, options)
  } catch {
    return null
  }

  return null
}
