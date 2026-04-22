<script setup lang="ts">
import { getReceivablesReconciliation } from '../api/clients/receivables'
import type { ReceivablesReconciliationRowDto } from '../api/types/pmContracts'
import ReconciliationPage from '../features/reconciliation/ReconciliationPage.vue'
import { createReconciliationPageDefinition, displayOrGuid } from '../features/reconciliation/definitionFactory'
import type { ReconciliationRow } from '../features/reconciliation/types'

function toRow(row: ReceivablesReconciliationRowDto): ReconciliationRow {
  return {
    key: `${row.partyId}:${row.propertyId}:${row.leaseId}`,
    rowKind: row.rowKind,
    hasDiff: row.hasDiff,
    primaryLabel: displayOrGuid(row.partyDisplay, row.partyId),
    secondaryLabel: displayOrGuid(row.propertyDisplay, row.propertyId),
    tertiaryLabel: displayOrGuid(row.leaseDisplay, row.leaseId),
    ledgerNet: row.arNet,
    openItemsNet: row.openItemsNet,
    diff: row.diff,
    openTarget: {
      path: '/receivables/open-items',
      query: {
        leaseId: row.leaseId,
        partyId: row.partyId,
        propertyId: row.propertyId,
      },
    },
  }
}

const definition = createReconciliationPageDefinition({
  title: 'Receivables',
  ledgerNetLabel: 'AR Net',
  ledgerEntityName: 'Accounts Receivable',
  diffSummaryDescription: 'AR Net minus Open Items Net',
  groupedByDescription: 'These totals are grouped by party / property / lease for the selected range and mode.',
  rowsDescription: 'Grouped by party / property / lease. Use the action icon to drill into Open Items.',
  noRowsMessage: 'No rows match the current mode and filter.',
  primaryColumnTitle: 'Party',
  secondaryColumnTitle: 'Property',
  tertiaryColumnTitle: 'Lease',
  balanceNotes: [
    'AR Net = month-end Accounts Receivable position in GL for the selected lease dimensions.',
    'Open Items Net = month-end open receivables position for the same lease dimensions.',
    'Use Balance when you want the closing position, not just in-period activity.',
  ],
  movementNotes: [
    'AR Net = in-period GL activity for Accounts Receivable. Charges increase AR. Payments decrease AR.',
    'Open Items Net = in-period open-items activity for the same lease dimensions.',
    'Apply reallocates charge/payment open items, but does not create GL movement by itself.',
  ],
  describeBalance: (toMonth) => `Month-end balance as of ${toMonth}. Best for accounting reconciliation and close checks.`,
  describeMovement: (fromMonth, toMonth) => `Net movement across ${fromMonth} → ${toMonth}. Best for investigation and drift analysis.`,
  matchedExplanation: 'AR and Open Items are aligned.',
  glOnlyExplanation: 'AR exists, but Open Items is zero.',
  openItemsOnlyExplanation: 'Open Items exists, but AR is zero.',
  toRow,
  loadReport: getReceivablesReconciliation,
  getRows: (report) => report.rows,
  getTotalLedgerNet: (report) => report.totalArNet,
  getTotalOpenItemsNet: (report) => report.totalOpenItemsNet,
  getTotalDiff: (report) => report.totalDiff,
  getRowCount: (report) => report.rowCount,
  getMismatchRowCount: (report) => report.mismatchRowCount,
})
</script>

<template>
  <ReconciliationPage :definition="definition" />
</template>
