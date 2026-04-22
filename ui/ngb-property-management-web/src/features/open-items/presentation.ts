import type { RegisterColumn, RegisterDataRow } from 'ngb-ui-framework'
import type { OpenItemsApplyResultLine } from './shared'

export type OpenItemsTabKey = 'charges' | 'credits' | 'applied'

export type OpenItemsSummary = {
  totalOutstanding: number
  totalCredit: number
  chargesCount: number
  creditsCount: number
  allocationsCount: number
}

export type OpenItemsGridDefinition = {
  columns: RegisterColumn[]
  rows: RegisterDataRow[]
  storageKey: string
  onActivate: (id: string) => void | Promise<void>
}

export type OpenItemsAppliedAllocationView = {
  applyId: string
  applyDisplay?: string | null
  applyNumber?: string | null
  creditDocumentId: string
  creditDocumentType: string
  creditDocumentDisplay?: string | null
  creditDocumentNumber?: string | null
  chargeDocumentId: string
  chargeDocumentType: string
  chargeDisplay?: string | null
  chargeNumber?: string | null
  appliedOnUtc: string
  amount: number
  isPosted: boolean
}

export type OpenItemsPageResultView = {
  visible: boolean
  title: string
  subtitle: string
  lines: OpenItemsApplyResultLine[]
  outstandingNow: number
  creditNow: number
  inconsistent?: boolean
}

export function buildOpenItemsTabs(summary: OpenItemsSummary): Array<{ key: OpenItemsTabKey; label: string }> {
  return [
    { key: 'charges', label: `Charges (${summary.chargesCount})` },
    { key: 'credits', label: `Credits (${summary.creditsCount})` },
    { key: 'applied', label: `Applied (${summary.allocationsCount})` },
  ]
}

export function buildApplyResultTitle(count: number): string {
  return `Created ${count} ${count === 1 ? 'apply' : 'applies'}`
}

export function buildApplyResultSubtitle(count: number, totalApplied: number, formatMoney: (value: number) => string): string {
  return `${buildApplyResultTitle(count)} totaling ${formatMoney(totalApplied)}.`
}
