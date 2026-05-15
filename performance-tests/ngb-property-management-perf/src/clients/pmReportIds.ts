export const PM_REPORT_IDS = {
  trialBalance: 'accounting.trial_balance',
  balanceSheet: 'accounting.balance_sheet',
  incomeStatement: 'accounting.income_statement',
  cashFlowStatementIndirect: 'accounting.cash_flow_statement_indirect',
  statementOfChangesInEquity: 'accounting.statement_of_changes_in_equity',
  generalJournal: 'accounting.general_journal',
  accountCard: 'accounting.account_card',
  generalLedgerAggregated: 'accounting.general_ledger_aggregated',
  ledgerAnalysis: 'accounting.ledger.analysis',
  postingLog: 'accounting.posting_log',
  consistency: 'accounting.consistency',
  buildingSummary: 'pm.building.summary',
  occupancySummary: 'pm.occupancy.summary',
  maintenanceQueue: 'pm.maintenance.queue',
  tenantStatement: 'pm.tenant.statement',
  receivablesOpenItems: 'pm.receivables.open_items',
  receivablesOpenItemsDetails: 'pm.receivables.open_items.details',
} as const;

export type PmReportId = typeof PM_REPORT_IDS[keyof typeof PM_REPORT_IDS];

export const PM_REPORT_BREAKDOWN_IDS: readonly PmReportId[] = [
  PM_REPORT_IDS.trialBalance,
  PM_REPORT_IDS.balanceSheet,
  PM_REPORT_IDS.incomeStatement,
  PM_REPORT_IDS.cashFlowStatementIndirect,
  PM_REPORT_IDS.statementOfChangesInEquity,
  PM_REPORT_IDS.generalJournal,
  PM_REPORT_IDS.generalLedgerAggregated,
  PM_REPORT_IDS.accountCard,
  PM_REPORT_IDS.ledgerAnalysis,
  PM_REPORT_IDS.postingLog,
  PM_REPORT_IDS.consistency,
];

export const PM_SMOKE_REPORT_BREAKDOWN_IDS: readonly PmReportId[] = [
  PM_REPORT_IDS.trialBalance,
  PM_REPORT_IDS.ledgerAnalysis,
];
