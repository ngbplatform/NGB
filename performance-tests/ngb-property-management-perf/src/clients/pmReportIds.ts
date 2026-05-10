export const PM_REPORT_IDS = {
  trialBalance: 'accounting.trial_balance',
  generalJournal: 'accounting.general_journal',
  accountCard: 'accounting.account_card',
  buildingSummary: 'pm.building.summary',
  occupancySummary: 'pm.occupancy.summary',
  maintenanceQueue: 'pm.maintenance.queue',
  tenantStatement: 'pm.tenant.statement',
  receivablesAging: 'pm.receivables.aging',
  receivablesOpenItems: 'pm.receivables.open_items',
  receivablesOpenItemsDetails: 'pm.receivables.open_items.details',
} as const;

export type PmReportId = typeof PM_REPORT_IDS[keyof typeof PM_REPORT_IDS];
