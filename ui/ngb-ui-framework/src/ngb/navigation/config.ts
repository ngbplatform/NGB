import type {
  NgbNavigationConfig,
  NgbNavigationContext,
  NgbNavigationRoutes,
  NgbNavigationTarget,
} from './types'
import { buildGeneralJournalEntriesPath } from '../accounting/navigation'

const defaultRoutes: NgbNavigationRoutes = {
  workCenter: '/work-center',
  workCenterPreferences: '/settings/notifications',
}

let navigationConfig: NgbNavigationConfig = {}

function segment(value: string): string {
  return encodeURIComponent(value)
}

function documentPath(
  suffix: string,
  target: NgbNavigationTarget,
  context: NgbNavigationContext,
): string | null {
  const parameters = target.parameters ?? {}
  const documentType = String(parameters.documentType ?? '').trim()
    || String(context.resourceCode ?? '').trim()
  const documentId = String(parameters.documentId ?? '').trim()
    || String(context.entityId ?? '').trim()
  if (!documentType || !documentId) return null
  return `/documents/${segment(documentType)}/${segment(documentId)}${suffix}`
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

export function resolveNgbNavigationTarget(
  target: NgbNavigationTarget,
  context: NgbNavigationContext = {},
): string | null {
  const configured = navigationConfig.resolveTarget?.(target, context)
  if (configured) return configured

  const explicitPath = String(target.parameters?.path ?? '').trim()
  if (explicitPath.startsWith('/')) return explicitPath

  switch (target.code) {
    case 'catalog.editor': {
      const type = context.resourceCode
      const id = context.entityId
      return type && id ? `/catalogs/${segment(type)}/${segment(id)}` : null
    }
    case 'report.view':
      return context.resourceCode ? `/reports/${segment(context.resourceCode)}` : null
    case 'generalJournalEntry.editor':
      return context.entityId ? buildGeneralJournalEntriesPath(context.entityId) : null
    case 'document.editor':
      return documentPath('', target, context)
    case 'document.effects':
      return documentPath('/effects', target, context)
    case 'document.flow':
      return documentPath('/flow', target, context)
    case 'document.print':
      return documentPath('/print', target, context)
    default:
      return null
  }
}
