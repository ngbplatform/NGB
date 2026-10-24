import { NgbSourceKinds, type NgbNavigationConfig, type NgbNavigationTarget } from '@ngbplatform/ui'

import { buildPmOpenItemsPath } from '../router/pmRoutePaths'

function queryFromTarget(target: NgbNavigationTarget): string {
  const parameters = new URLSearchParams()
  for (const [key, value] of Object.entries(target.parameters ?? {})) {
    const normalized = String(value ?? '').trim()
    if (normalized) parameters.set(key, normalized)
  }
  return parameters.toString()
}

export function createPmNavigationConfig(): NgbNavigationConfig {
  return {
    resolvePageContext(location) {
      if (location.path !== '/catalogs/pm.property' || location.query.panel) return undefined
      const value = location.query.buildingId
      const id = Array.isArray(value) ? value[0] : value
      return id ? { kind: NgbSourceKinds.Catalog, typeCode: 'pm.property', id } : undefined
    },
    resolveTarget(target) {
      if (target.code === 'pm.receivables.reconciliation') {
        const paymentId = String(target.parameters?.paymentId ?? '').trim()
        return paymentId
          ? `/receivables/reconciliation?paymentId=${encodeURIComponent(paymentId)}`
          : '/receivables/reconciliation'
      }
      if (target.code === 'pm.receivables.apply' || target.code === 'pm.payables.apply') {
        const side = target.code === 'pm.receivables.apply' ? 'receivables' : 'payables'
        const query = queryFromTarget(target)
        return `${buildPmOpenItemsPath(side)}${query ? `?${query}` : ''}`
      }
      return null
    },
  }
}
