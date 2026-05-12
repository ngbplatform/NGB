import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData, NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { buildBusinessDayWorkload } from '../../../ngb-performance-tests-framework/src/scenarios/workloadModels.ts';
import { pmAccountingEffectsFlow } from '../flows/pmAccountingEffectsFlow.ts';
import { pmDocumentFlowReadFlow } from '../flows/pmDocumentFlowReadFlow.ts';
import { pmLeaseBrowseFlow } from '../flows/pmLeaseBrowseFlow.ts';
import { pmReceivablePaymentApplyFlow } from '../flows/pmReceivablePaymentApplyFlow.ts';
import { pmRentChargePostingFlow } from '../flows/pmRentChargePostingFlow.ts';
import { pmReportsFlow } from '../flows/pmReportsFlow.ts';

export const options = buildBusinessDayWorkload({
  browsing: {
    executor: 'constant-arrival-rate',
    rate: 3,
    timeUnit: '1s',
    duration: '10m',
    preAllocatedVUs: 10,
    maxVUs: 40,
    exec: 'browsing',
    tags: { profile: 'business-day', vertical: 'property-management', scenario: 'pm.business_day.browsing' },
  },
  reports: {
    executor: 'constant-arrival-rate',
    rate: 1,
    timeUnit: '10s',
    duration: '10m',
    preAllocatedVUs: 5,
    maxVUs: 15,
    exec: 'reports',
    tags: { profile: 'business-day', vertical: 'property-management', scenario: 'pm.business_day.reports' },
  },
  posting: {
    executor: 'constant-arrival-rate',
    rate: 1,
    timeUnit: '30s',
    duration: '10m',
    preAllocatedVUs: 2,
    maxVUs: 8,
    exec: 'posting',
    tags: { profile: 'business-day', vertical: 'property-management', scenario: 'pm.business_day.posting' },
  },
  payment_apply: {
    executor: 'constant-arrival-rate',
    rate: 1,
    timeUnit: '30s',
    duration: '10m',
    preAllocatedVUs: 2,
    maxVUs: 8,
    exec: 'paymentApply',
    tags: { profile: 'business-day', vertical: 'property-management', scenario: 'pm.business_day.payment_apply' },
  },
  heavy_read: {
    executor: 'constant-arrival-rate',
    rate: 1,
    timeUnit: '20s',
    duration: '10m',
    preAllocatedVUs: 2,
    maxVUs: 8,
    exec: 'heavyRead',
    tags: { profile: 'business-day', vertical: 'property-management', scenario: 'pm.business_day.heavy_read' },
  },
});

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function browsing(data: NgbAuthSetupData): void {
  pmLeaseBrowseFlow(context(data));
}

export function reports(data: NgbAuthSetupData): void {
  pmReportsFlow(context(data));
}

export function posting(data: NgbAuthSetupData): void {
  pmRentChargePostingFlow(context(data));
}

export function paymentApply(data: NgbAuthSetupData): void {
  pmReceivablePaymentApplyFlow(context(data));
}

export function heavyRead(data: NgbAuthSetupData): void {
  const scenarioContext = context(data);
  pmAccountingEffectsFlow(scenarioContext);
  pmDocumentFlowReadFlow(scenarioContext);
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}

function context(data: NgbAuthSetupData): NgbScenarioContext {
  return getNgbScenarioContext(data);
}
