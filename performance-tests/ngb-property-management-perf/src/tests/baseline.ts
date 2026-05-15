import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { buildBaselineProfile } from '../../../ngb-performance-tests-framework/src/profiles/baseline.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import { pmAccountingEffectsFlow } from '../flows/pmAccountingEffectsFlow.ts';
import { pmDocumentFlowReadFlow } from '../flows/pmDocumentFlowReadFlow.ts';
import { pmPlatformAuditFlow } from '../flows/pmPlatformAuditFlow.ts';
import { pmPlatformMaintenanceFlow } from '../flows/pmPlatformMaintenanceFlow.ts';
import { PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS, pmPlatformReadFlow } from '../flows/pmPlatformReadFlow.ts';
import { pmReportsFlow } from '../flows/pmReportsFlow.ts';

export const options = buildBaselineProfile({
  exec: 'pmBaseline',
  tags: { vertical: 'property-management', scenario: 'pm.baseline' },
  reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
  diagnosticBreakdowns: PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS,
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function pmBaseline(data: NgbAuthSetupData): void {
  const context = getNgbScenarioContext(data);
  pmPlatformReadFlow(context, { includeMetadata: true, includeLookup: true, includeDeepPages: false });
  pmReportsFlow(context, {
    periodProfiles: ['open'],
    includeAccountScopedReports: true,
    includeLedgerAnalysisVariants: true,
  });
  pmAccountingEffectsFlow(context);
  pmDocumentFlowReadFlow(context);
  pmPlatformAuditFlow(context);
  pmPlatformMaintenanceFlow(context);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}
