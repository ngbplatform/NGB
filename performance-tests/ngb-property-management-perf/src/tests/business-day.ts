import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData, NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import {
  buildBusinessDayWorkload,
  type WorkloadScenario,
} from '../../../ngb-performance-tests-framework/src/scenarios/workloadModels.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import {
  pmPlatformBusinessDayHeavyReadFlow,
  pmPlatformBusinessDayReadFlow,
  pmPlatformBusinessDayWriteFlow,
} from '../flows/pmPlatformMixedFlow.ts';
import { pmPlatformMaintenanceFlow } from '../flows/pmPlatformMaintenanceFlow.ts';
import { PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS } from '../flows/pmPlatformReadFlow.ts';
import { pmReportsFlow } from '../flows/pmReportsFlow.ts';

type BusinessDayScenarioKey = 'browsing' | 'reports' | 'posting' | 'payment_apply' | 'heavy_read';

interface BusinessDayArrivalDefaults {
  readonly rate: number;
  readonly timeUnit: string;
  readonly duration: string;
  readonly preAllocatedVUs: number;
  readonly maxVUs: number;
  readonly exec: string;
  readonly scenarioTag: string;
}

const BUSINESS_DAY_PROFILE = 'business-day';
const BUSINESS_DAY_VERTICAL = 'property-management';

export const options = buildBusinessDayWorkload(
  {
    browsing: businessDayArrivalScenario('browsing', {
      rate: 3,
      timeUnit: '1s',
      duration: '10m',
      preAllocatedVUs: 48,
      maxVUs: 96,
      exec: 'browsing',
      scenarioTag: 'pm.business_day.browsing',
    }),
    reports: businessDayArrivalScenario('reports', {
      rate: 1,
      timeUnit: '10s',
      duration: '10m',
      preAllocatedVUs: 8,
      maxVUs: 30,
      exec: 'reports',
      scenarioTag: 'pm.business_day.reports',
    }),
    posting: businessDayArrivalScenario('posting', {
      rate: 1,
      timeUnit: '30s',
      duration: '10m',
      preAllocatedVUs: 4,
      maxVUs: 20,
      exec: 'posting',
      scenarioTag: 'pm.business_day.posting',
    }),
    payment_apply: businessDayArrivalScenario('payment_apply', {
      rate: 1,
      timeUnit: '30s',
      duration: '10m',
      preAllocatedVUs: 4,
      maxVUs: 20,
      exec: 'paymentApply',
      scenarioTag: 'pm.business_day.payment_apply',
    }),
    heavy_read: businessDayArrivalScenario('heavy_read', {
      rate: 1,
      timeUnit: '20s',
      duration: '10m',
      preAllocatedVUs: 4,
      maxVUs: 20,
      exec: 'heavyRead',
      scenarioTag: 'pm.business_day.heavy_read',
    }),
  },
  {
    reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
    diagnosticBreakdowns: PM_PLATFORM_READ_DIAGNOSTIC_BREAKDOWNS,
  },
);

export function setup(): NgbAuthSetupData {
  return setupNgbAccessToken();
}

export function browsing(data: NgbAuthSetupData): void {
  pmPlatformBusinessDayReadFlow(context(data));
}

export function reports(data: NgbAuthSetupData): void {
  pmReportsFlow(context(data), {
    periodProfiles: ['open', 'closed'],
    includeAccountScopedReports: true,
    includeLedgerAnalysisVariants: true,
  });
}

export function posting(data: NgbAuthSetupData): void {
  pmPlatformBusinessDayWriteFlow(context(data));
}

export function paymentApply(data: NgbAuthSetupData): void {
  pmPlatformBusinessDayWriteFlow(context(data));
}

export function heavyRead(data: NgbAuthSetupData): void {
  pmPlatformBusinessDayHeavyReadFlow(context(data));
  pmPlatformMaintenanceFlow(context(data));
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}

function context(data: NgbAuthSetupData): NgbScenarioContext {
  return getNgbScenarioContext(data);
}

function businessDayArrivalScenario(
  key: BusinessDayScenarioKey,
  defaults: BusinessDayArrivalDefaults,
): WorkloadScenario {
  const prefix = `NGB_PM_BUSINESS_DAY_${key.toUpperCase()}`;
  const preAllocatedVUs = readPositiveInteger(`${prefix}_PRE_ALLOCATED_VUS`, defaults.preAllocatedVUs);
  const maxVUs = readPositiveInteger(`${prefix}_MAX_VUS`, defaults.maxVUs);

  if (maxVUs < preAllocatedVUs) {
    throw new Error(
      `${prefix}_MAX_VUS (${maxVUs}) must be greater than or equal to ${prefix}_PRE_ALLOCATED_VUS (${preAllocatedVUs})`,
    );
  }

  return {
    executor: 'constant-arrival-rate',
    rate: readPositiveNumber(`${prefix}_RATE`, defaults.rate),
    timeUnit: readDuration(`${prefix}_TIME_UNIT`, defaults.timeUnit),
    duration: readDuration(`${prefix}_DURATION`, readDuration('NGB_PM_BUSINESS_DAY_DURATION', defaults.duration)),
    preAllocatedVUs,
    maxVUs,
    exec: defaults.exec,
    tags: {
      profile: BUSINESS_DAY_PROFILE,
      vertical: BUSINESS_DAY_VERTICAL,
      scenario: defaults.scenarioTag,
    },
  };
}

function readPositiveNumber(name: string, fallback: number): number {
  const raw = __ENV[name];
  if (raw === undefined || raw.trim() === '') {
    return fallback;
  }

  const value = Number(raw);
  if (!Number.isFinite(value) || value <= 0) {
    throw new Error(`${name} must be a positive number, got ${JSON.stringify(raw)}`);
  }

  return value;
}

function readPositiveInteger(name: string, fallback: number): number {
  const value = readPositiveNumber(name, fallback);
  if (!Number.isInteger(value)) {
    throw new Error(`${name} must be a positive integer, got ${JSON.stringify(__ENV[name])}`);
  }

  return value;
}

function readDuration(name: string, fallback: string): string {
  const raw = __ENV[name];
  if (raw === undefined || raw.trim() === '') {
    return fallback;
  }

  return raw.trim();
}
