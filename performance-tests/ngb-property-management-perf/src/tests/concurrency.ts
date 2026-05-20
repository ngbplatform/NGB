import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData, NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { buildBusinessDayWorkload } from '../../../ngb-performance-tests-framework/src/scenarios/workloadModels.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import { pmDocumentLifecycleFlow } from '../flows/pmDocumentLifecycleFlow.ts';
import { pmPlatformAuditFlow } from '../flows/pmPlatformAuditFlow.ts';
import { pmPlatformReadFlow } from '../flows/pmPlatformReadFlow.ts';
import { pmRentChargePostingFlow } from '../flows/pmRentChargePostingFlow.ts';
import { pmReportsFlow } from '../flows/pmReportsFlow.ts';

export const options = buildBusinessDayWorkload(
  {
    concurrent_reads: {
      executor: 'constant-vus',
      vus: 20,
      duration: '10m',
      exec: 'concurrentReads',
      tags: { profile: 'concurrency', vertical: 'property-management', scenario: 'pm.concurrency.reads' },
    },
    concurrent_reports: {
      executor: 'constant-arrival-rate',
      rate: 1,
      timeUnit: '5s',
      duration: '10m',
      preAllocatedVUs: 5,
      maxVUs: 20,
      exec: 'concurrentReports',
      tags: { profile: 'concurrency', vertical: 'property-management', scenario: 'pm.concurrency.reports' },
    },
    concurrent_writes: {
      executor: 'constant-arrival-rate',
      rate: 1,
      timeUnit: '15s',
      duration: '10m',
      preAllocatedVUs: 3,
      maxVUs: 10,
      exec: 'concurrentWrites',
      tags: { profile: 'concurrency', vertical: 'property-management', scenario: 'pm.concurrency.writes' },
    },
  },
  {
    reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
    profileName: 'concurrency',
  },
);

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function concurrentReads(data: NgbAuthSetupData): void {
  pmPlatformReadFlow(context(data), { includeMetadata: false, includeLookup: true, includeDeepPages: true });
  pmPlatformAuditFlow(context(data));
}

export function concurrentReports(data: NgbAuthSetupData): void {
  pmReportsFlow(context(data), {
    periodProfiles: ['open'],
    includeAccountScopedReports: false,
    includeLedgerAnalysisVariants: true,
  });
}

export function concurrentWrites(data: NgbAuthSetupData): void {
  const scenarioContext = context(data);
  pmDocumentLifecycleFlow(scenarioContext);
  pmRentChargePostingFlow(scenarioContext);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}

function context(data: NgbAuthSetupData): NgbScenarioContext {
  return getNgbScenarioContext(data);
}
