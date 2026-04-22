import type { RouteLocationRaw } from 'vue-router'

export type ReconciliationMode = 'Movement' | 'Balance'
export type ReconciliationStatusFilter = 'all' | 'matched' | 'mismatch' | 'glOnly' | 'openItemsOnly'
export type ReconciliationRowKind = 'Matched' | 'Mismatch' | 'GlOnly' | 'OpenItemsOnly'

export type ReconciliationRow = {
  key: string
  rowKind: ReconciliationRowKind
  hasDiff: boolean
  primaryLabel: string
  secondaryLabel: string
  tertiaryLabel?: string | null
  ledgerNet: number
  openItemsNet: number
  diff: number
  openTarget?: RouteLocationRaw | null
}

export type ReconciliationReport = {
  totalLedgerNet: number
  totalOpenItemsNet: number
  totalDiff: number
  rowCount: number
  mismatchRowCount: number
  rows: ReconciliationRow[]
}

export type ReconciliationLoadRequest = {
  fromMonthInclusive: string
  toMonthInclusive: string
  mode: ReconciliationMode
}

export type ReconciliationModeDescriptionArgs = {
  mode: ReconciliationMode
  fromMonth: string
  toMonth: string
}

export type ReconciliationPageDefinition = {
  title: string
  ledgerNetLabel: string
  ledgerNetSummaryDescription: (mode: ReconciliationMode) => string
  diffSummaryDescription: string
  groupedByDescription: string
  rowsDescription: string
  noRowsMessage: string
  primaryColumnTitle: string
  secondaryColumnTitle: string
  tertiaryColumnTitle?: string | null
  balanceNotes: string[]
  movementNotes: string[]
  describeMode: (args: ReconciliationModeDescriptionArgs) => string
  explainRow: (row: ReconciliationRow) => string
  load: (request: ReconciliationLoadRequest) => Promise<ReconciliationReport>
}
