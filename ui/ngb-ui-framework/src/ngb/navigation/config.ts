import type {
  NgbNavigationConfig,
  NgbNavigationContext,
  NgbNavigationRoutes,
  NgbNavigationTarget,
} from './types'

const defaultRoutes: NgbNavigationRoutes = {
  workCenter: '/work-center',
  workCenterPreferences: '/settings/notifications',
}

let navigationConfig: NgbNavigationConfig = {}

function segment(value: string | null | undefined): string {
  return encodeURIComponent(String(value ?? '').trim())
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
