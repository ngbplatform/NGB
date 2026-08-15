import type { NgbNavigationConfig, NgbNavigationTarget } from '@ngbplatform/ui'

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
