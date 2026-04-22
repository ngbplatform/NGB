import type {
  ReconciliationLoadRequest,
  ReconciliationPageDefinition,
  ReconciliationReport,
  ReconciliationRow,
} from './types'
import { isEmptyGuid, shortGuid } from 'ngb-ui-framework'

export function displayOrGuid(display: string | null | undefined, id: string | null | undefined): string {
  const normalized = String(display ?? '').trim()
  if (normalized) return normalized
  return isEmptyGuid(id) ? '—' : shortGuid(id)
}

export function formatAbsoluteMoney(value: number): string {
  const normalized = Math.abs(Math.round((value ?? 0) * 100) / 100)
  return normalized.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

type CreateReconciliationPageDefinitionArgs<TRow, TReport> = {
  title: string
  ledgerNetLabel: string
  ledgerEntityName: string
  diffSummaryDescription: string
  groupedByDescription: string
  rowsDescription: string
  noRowsMessage: string
  primaryColumnTitle: string
  secondaryColumnTitle: string
  tertiaryColumnTitle?: string | null
  balanceNotes: string[]
  movementNotes: string[]
  describeBalance: (toMonth: string) => string
  describeMovement: (fromMonth: string, toMonth: string) => string
  matchedExplanation: string
  glOnlyExplanation: string
  openItemsOnlyExplanation: string
  toRow: (row: TRow) => ReconciliationRow
  loadReport: (request: ReconciliationLoadRequest) => Promise<TReport>
  getRows: (report: TReport) => TRow[]
  getTotalLedgerNet: (report: TReport) => number
  getTotalOpenItemsNet: (report: TReport) => number
  getTotalDiff: (report: TReport) => number
  getRowCount: (report: TReport) => number
  getMismatchRowCount: (report: TReport) => number
}

export function createReconciliationPageDefinition<TRow, TReport>(
  args: CreateReconciliationPageDefinitionArgs<TRow, TReport>,
): ReconciliationPageDefinition {
  return {
    title: args.title,
    ledgerNetLabel: args.ledgerNetLabel,
    ledgerNetSummaryDescription: (mode) => `GL ${args.ledgerEntityName} ${mode === 'Balance' ? 'balance' : 'movement'}`,
    diffSummaryDescription: args.diffSummaryDescription,
    groupedByDescription: args.groupedByDescription,
    rowsDescription: args.rowsDescription,
    noRowsMessage: args.noRowsMessage,
    primaryColumnTitle: args.primaryColumnTitle,
    secondaryColumnTitle: args.secondaryColumnTitle,
    tertiaryColumnTitle: args.tertiaryColumnTitle ?? null,
    balanceNotes: args.balanceNotes,
    movementNotes: args.movementNotes,
    describeMode: ({ mode, fromMonth, toMonth }) => {
      if (mode === 'Balance') return args.describeBalance(toMonth)
      return args.describeMovement(fromMonth, toMonth)
    },
    explainRow: (row) => {
      switch (row.rowKind) {
        case 'Matched':
          return args.matchedExplanation
        case 'Mismatch':
          return `Both sides exist. Diff ${formatAbsoluteMoney(row.diff)}.`
        case 'GlOnly':
          return args.glOnlyExplanation
        case 'OpenItemsOnly':
          return args.openItemsOnlyExplanation
        default:
          return 'Investigate the row.'
      }
    },
    load: async (request): Promise<ReconciliationReport> => {
      const report = await args.loadReport(request)
      return {
        totalLedgerNet: args.getTotalLedgerNet(report),
        totalOpenItemsNet: args.getTotalOpenItemsNet(report),
        totalDiff: args.getTotalDiff(report),
        rowCount: args.getRowCount(report),
        mismatchRowCount: args.getMismatchRowCount(report),
        rows: args.getRows(report).map(args.toRow),
      }
    },
  }
}
