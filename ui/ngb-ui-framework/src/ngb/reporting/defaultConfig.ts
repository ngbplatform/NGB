import { buildChartOfAccountsPath } from '../accounting/navigation'
import { buildCatalogFullPageUrl } from '../editor/catalogNavigation'
import { buildDocumentFullPageUrl } from '../editor/documentNavigation'
import { buildLookupFieldTargetUrl } from '../lookup/navigation'
import type { LookupValueLike } from '../lookup/navigation'
import { useLookupStore } from '../lookup/store'
import { withBackTarget } from '../router/backNavigation'
import { isGuidString } from '../utils/guid'
import type { ReportCellActionNavigationOptions, ReportingFrameworkConfig } from './config'
import { appendSourceTrail, buildReportPageUrl } from './navigation'
import type { ReportCellActionDto } from './types'

export function createDefaultNgbReportingConfig(): ReportingFrameworkConfig {
  return {
    useLookupStore: () => useLookupStore(),
    resolveLookupTarget: async ({ hint, value, routeFullPath }) =>
      await buildLookupFieldTargetUrl({
        hint,
        value: toLookupValue(value),
        route: { fullPath: routeFullPath },
      }),
    resolveCellActionUrl: resolveDefaultReportCellActionUrl,
  }
}

export function resolveDefaultReportCellActionUrl(
  action: ReportCellActionDto | null | undefined,
  options?: ReportCellActionNavigationOptions,
): string | null {
  if (!action?.kind) return null

  if (action.kind === 'open_document') {
    const documentType = String(action.documentType ?? '').trim()
    const id = String(action.documentId ?? '').trim()
    if (!documentType || !isGuidString(id)) return null
    return withBackTarget(buildDocumentFullPageUrl(documentType, id), options?.backTarget ?? null)
  }

  if (action.kind === 'open_account') {
    const id = String(action.accountId ?? '').trim()
    if (!isGuidString(id)) return null
    return withBackTarget(buildChartOfAccountsPath({ panel: 'edit', id }), options?.backTarget ?? null)
  }

  if (action.kind === 'open_catalog') {
    const catalogType = String(action.catalogType ?? '').trim()
    const id = String(action.catalogId ?? '').trim()
    if (!catalogType || !isGuidString(id)) return null
    return withBackTarget(buildCatalogFullPageUrl(catalogType, id), options?.backTarget ?? null)
  }

  if (action.kind !== 'open_report') return null
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

function toLookupValue(value: unknown): LookupValueLike {
  if (typeof value === 'string' || value == null) return value
  if (typeof value === 'object' && 'id' in value) {
    const id = (value as { id?: unknown }).id
    return { id: typeof id === 'string' ? id : null }
  }
  return null
}
