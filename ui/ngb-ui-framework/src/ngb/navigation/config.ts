import type {
  NgbNavigationConfig,
  NgbNavigationContext,
  NgbNavigationLocation,
  NgbNavigationRoutes,
  NgbNavigationTarget,
  NgbPageContext,
} from './types'
import { buildGeneralJournalEntriesPath } from '../accounting/navigation'
import { isNgbSourceIdentity, NgbSourceKinds } from '../api/sourceKinds'
import { resolveDefaultPageContext } from './pageContext'
import { resolveNgbRecordRouting } from './recordRouting'

const defaultRoutes: NgbNavigationRoutes = {
  workCenter: '/work-center',
  workCenterPreferences: '/settings/notifications',
}

let navigationConfig: NgbNavigationConfig = {}

function documentPath(
  buildPath: (typeCode: string, id: string) => string,
  target: NgbNavigationTarget,
  context: NgbNavigationContext,
): string | null {
  const parameters = target.parameters ?? {}
  const documentType = String(parameters.documentType ?? '').trim()
    || String(context.resourceCode ?? '').trim()
  const documentId = String(parameters.documentId ?? '').trim()
    || String(context.entityId ?? '').trim()
  if (!documentType || !documentId) return null
  return buildPath(documentType, documentId)
}

export function configureNgbNavigation(config: NgbNavigationConfig = {}): void {
  navigationConfig = config
}

export function resolveNgbNavigationRoutes(): NgbNavigationRoutes {
  return {
    ...defaultRoutes,
    ...(navigationConfig.routes ?? {}),
  }
}

export function resolveNgbPageContext(location: NgbNavigationLocation): NgbPageContext | null {
  const configured = navigationConfig.resolvePageContext?.(location)
  const context = configured === undefined ? resolveDefaultPageContext(location) : configured
  if (!isNgbSourceIdentity(context)) return null

  // Query criteria help reproduce the selected report; loaded results never cross this boundary.
  return {
    kind: context.kind,
    typeCode: context.typeCode,
    ...(context.id ? { id: context.id } : {}),
    ...(context.kind === NgbSourceKinds.Report && context.parameters ? { parameters: context.parameters } : {}),
    ...(context.kind === NgbSourceKinds.Report && context.filters ? { filters: context.filters } : {}),
  }
}

export function resolveNgbNavigationTarget(
  target: NgbNavigationTarget,
  context: NgbNavigationContext = {},
): string | null {
  const configured = navigationConfig.resolveTarget?.(target, context)
  if (configured) return configured

  const explicitPath = String(target.parameters?.path ?? '').trim()
  if (explicitPath.startsWith('/')) return explicitPath

  const routing = resolveNgbRecordRouting()
  switch (target.code) {
    case 'catalog.editor': {
      const type = context.resourceCode
      const id = context.entityId
      return type && id ? routing.buildCatalogFullPageUrl(type, id) : null
    }
    case 'report.view':
      return context.resourceCode ? `/reports/${encodeURIComponent(context.resourceCode)}` : null
    case 'generalJournalEntry.editor':
      return context.entityId ? buildGeneralJournalEntriesPath(context.entityId) : null
    case 'document.editor':
      return documentPath(routing.buildDocumentFullPageUrl, target, context)
    case 'document.effects':
      return documentPath(routing.buildDocumentEffectsPageUrl, target, context)
    case 'document.flow':
      return documentPath(routing.buildDocumentFlowPageUrl, target, context)
    case 'document.print':
      return documentPath(routing.buildDocumentPrintPageUrl, target, context)
    default:
      return null
  }
}
