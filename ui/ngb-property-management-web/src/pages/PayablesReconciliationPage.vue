<script setup lang="ts">
import { getPayablesReconciliation } from '../api/clients/payables'
import type { PayablesReconciliationRowDto } from '../api/types/pmContracts'
import ReconciliationPage from '../features/reconciliation/ReconciliationPage.vue'
import { createReconciliationPageDefinition, displayOrGuid } from '../features/reconciliation/definitionFactory'
import type { ReconciliationRow } from '../features/reconciliation/types'

function toRow(row: PayablesReconciliationRowDto): ReconciliationRow {
  return {
    key: `${row.vendorId}:${row.propertyId}`,
    rowKind: row.rowKind,
    hasDiff: row.hasDiff,
    primaryLabel: displayOrGuid(row.vendorDisplay, row.vendorId),
    secondaryLabel: displayOrGuid(row.propertyDisplay, row.propertyId),
    tertiaryLabel: null,
    ledgerNet: row.apNet,
    openItemsNet: row.openItemsNet,
    diff: row.diff,
    openTarget: {
      path: '/payables/open-items',
      query: {
        partyId: row.vendorId,
        propertyId: row.propertyId,
      },
    },
  }
}

const definition = createReconciliationPageDefinition({
  title: 'Payables',
  ledgerNetLabel: 'AP Net',
  ledgerEntityName: 'Accounts Payable',
  diffSummaryDescription: 'AP Net minus Open Items Net',
  groupedByDescription: 'These totals are grouped by vendor / property for the selected range and mode.',
  rowsDescription: 'Grouped by vendor / property. Use the action icon to drill into Open Items.',
  noRowsMessage: 'No rows match the current mode and filter.',
  primaryColumnTitle: 'Vendor',
  secondaryColumnTitle: 'Property',
  tertiaryColumnTitle: null,
  balanceNotes: [
    'AP Net = month-end Accounts Payable position in GL for the selected vendor and property dimensions.',
    'Open Items Net = month-end open payables position for the same dimensions.',
    'Use Balance when you want the closing liability position, not just in-period activity.',
  ],
  movementNotes: [
    'AP Net = in-period GL activity for Accounts Payable. Charges increase AP. Payments and credit memos decrease AP.',
    'Open Items Net = in-period open-items activity for the same vendor and property dimensions.',
    'Apply reallocates charge/payment open items, but does not create GL movement by itself.',
  ],
  describeBalance: (toMonth) => `Month-end balance as of ${toMonth}. Best for payables close checks and vendor balance reconciliation.`,
  describeMovement: (fromMonth, toMonth) => `Net movement across ${fromMonth} → ${toMonth}. Best for investigation and drift analysis.`,
  matchedExplanation: 'AP and Open Items are aligned.',
  glOnlyExplanation: 'AP exists, but Open Items is zero.',
  openItemsOnlyExplanation: 'Open Items exists, but AP is zero.',
  toRow,
  loadReport: getPayablesReconciliation,
  getRows: (report) => report.rows,
  getTotalLedgerNet: (report) => report.totalApNet,
  getTotalOpenItemsNet: (report) => report.totalOpenItemsNet,
  getTotalDiff: (report) => report.totalDiff,
  getRowCount: (report) => report.rowCount,
  getMismatchRowCount: (report) => report.mismatchRowCount,
})
</script>

<template>
  <ReconciliationPage :definition="definition" />
</template>
