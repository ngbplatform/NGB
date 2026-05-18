import { check, fail } from 'k6';
import exec from 'k6/execution';

import { defaultHandleSummary } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { readNgbPerfEnv } from '../../../ngb-performance-tests-framework/src/core/env.ts';
import type { DiagnosticBreakdownSelector } from '../../../ngb-performance-tests-framework/src/profiles/thresholds.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import type { NgbAuthSetupData, NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import {
  buildMultiScenarioWorkload,
  type WorkloadScenario,
} from '../../../ngb-performance-tests-framework/src/scenarios/workloadModels.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';
import { PM_REPORT_BREAKDOWN_IDS } from '../clients/pmReportIds.ts';
import { pmDocumentLifecycleFlow } from '../flows/pmDocumentLifecycleFlow.ts';
import { readBooleanEnv } from '../flows/pmFlowSupport.ts';
import { pmPlatformAuditFlow } from '../flows/pmPlatformAuditFlow.ts';
import { pmPlatformMaintenanceFlow } from '../flows/pmPlatformMaintenanceFlow.ts';
import { pmPlatformReadFlow } from '../flows/pmPlatformReadFlow.ts';
import { pmRentChargePostingFlow } from '../flows/pmRentChargePostingFlow.ts';
import { pmReportsFlow } from '../flows/pmReportsFlow.ts';

type WriteHeavyScenarioKey = 'lifecycle' | 'posting' | 'readback' | 'reporting';

interface WriteHeavyArrivalDefaults {
  readonly rate: number;
  readonly timeUnit: string;
  readonly duration: string;
  readonly preAllocatedVUs: number;
  readonly maxVUs: number;
  readonly exec: string;
  readonly scenarioTag: string;
}

const WRITE_HEAVY_PROFILE = 'write-heavy';
const WRITE_HEAVY_VERTICAL = 'property-management';

const WRITE_HEAVY_DIAGNOSTIC_BREAKDOWNS: readonly DiagnosticBreakdownSelector[] = [
  ...[
    'platform.documents.create',
    'platform.documents.update',
    'platform.documents.open',
    'platform.documents.derive_actions',
    'platform.documents.post',
    'platform.documents.delete_draft',
  ].map((operation) => ({
    area: 'documents',
    operation,
    documentType: PM_DOCUMENT_TYPES.maintenanceRequest,
  })),
  ...[
    'platform.documents.list',
    'platform.documents.open',
    'platform.documents.post',
  ].map((operation) => ({
    area: 'documents',
    operation,
    documentType: PM_DOCUMENT_TYPES.rentCharge,
  })),
  { area: 'accounting', operation: 'platform.accounting_effects.read', documentType: PM_DOCUMENT_TYPES.rentCharge },
  { area: 'document-flow', operation: 'platform.document_flow.read', documentType: PM_DOCUMENT_TYPES.rentCharge },
  { area: 'document-flow', operation: 'platform.document_flow.read', documentType: PM_DOCUMENT_TYPES.maintenanceRequest },
  { area: 'audit', operation: 'platform.audit.entity_log', entityKind: 'Document' },
];

export const options = buildMultiScenarioWorkload(
  {
    lifecycle: writeHeavyArrivalScenario('lifecycle', {
      rate: 4,
      timeUnit: '1s',
      duration: '15m',
      preAllocatedVUs: 64,
      maxVUs: 160,
      exec: 'lifecycleWrites',
      scenarioTag: 'pm.write_heavy.lifecycle',
    }),
    posting: writeHeavyArrivalScenario('posting', {
      rate: 1,
      timeUnit: '2s',
      duration: '15m',
      preAllocatedVUs: 16,
      maxVUs: 64,
      exec: 'postingPath',
      scenarioTag: 'pm.write_heavy.posting',
    }),
    readback: writeHeavyArrivalScenario('readback', {
      rate: 2,
      timeUnit: '1s',
      duration: '15m',
      preAllocatedVUs: 48,
      maxVUs: 128,
      exec: 'readAfterWrite',
      scenarioTag: 'pm.write_heavy.readback',
    }),
    reporting: writeHeavyArrivalScenario('reporting', {
      rate: 1,
      timeUnit: '10s',
      duration: '15m',
      preAllocatedVUs: 8,
      maxVUs: 32,
      exec: 'reportingDuringWrites',
      scenarioTag: 'pm.write_heavy.reporting',
    }),
  },
  {
    profileName: WRITE_HEAVY_PROFILE,
    reportBreakdownIds: PM_REPORT_BREAKDOWN_IDS,
    diagnosticBreakdowns: WRITE_HEAVY_DIAGNOSTIC_BREAKDOWNS,
  },
);

export function setup(): NgbAuthSetupData {
  assertWriteHeavyEnabled();
  return setupNgbAccessToken();
}

export function lifecycleWrites(data: NgbAuthSetupData): void {
  const wrote = pmDocumentLifecycleFlow(context(data));
  check(null, {
    'write-heavy lifecycle executed a write': () => wrote,
  }, writeCheckTags('pm.write_heavy.lifecycle'));
}

export function postingPath(data: NgbAuthSetupData): void {
  const posted = pmRentChargePostingFlow(context(data));
  if (readBooleanEnv('NGB_PERF_ENABLE_POSTING', false)) {
    check(null, {
      'write-heavy posting path executed post': () => posted,
    }, writeCheckTags('pm.write_heavy.posting'));
  }
}

export function readAfterWrite(data: NgbAuthSetupData): void {
  const scenarioContext = context(data);
  pmPlatformReadFlow(scenarioContext, {
    includeMetadata: false,
    includeLookup: true,
    includeDeepPages: false,
  });
  pmPlatformAuditFlow(scenarioContext);
  pmPlatformMaintenanceFlow(scenarioContext);
}

export function reportingDuringWrites(data: NgbAuthSetupData): void {
  pmReportsFlow(context(data), {
    periodProfiles: ['open'],
    includeAccountScopedReports: true,
    includeLedgerAnalysisVariants: true,
  });
}

export function handleSummary(data: unknown): Record<string, string> {
  return defaultHandleSummary(data);
}

function context(data: NgbAuthSetupData): NgbScenarioContext {
  return getNgbScenarioContext(data);
}

function writeHeavyArrivalScenario(
  key: WriteHeavyScenarioKey,
  defaults: WriteHeavyArrivalDefaults,
): WorkloadScenario {
  const prefix = `NGB_PM_WRITE_HEAVY_${key.toUpperCase()}`;
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
    duration: readDuration(`${prefix}_DURATION`, readDuration('NGB_PM_WRITE_HEAVY_DURATION', defaults.duration)),
    preAllocatedVUs,
    maxVUs,
    exec: defaults.exec,
    tags: {
      profile: WRITE_HEAVY_PROFILE,
      vertical: WRITE_HEAVY_VERTICAL,
      scenario: defaults.scenarioTag,
    },
  };
}

function assertWriteHeavyEnabled(): void {
  const env = readNgbPerfEnv();
  if (env.enableWrites) {
    return;
  }

  abortTest('[ngb-perf] pm:write-heavy requires NGB_PERF_ENABLE_WRITES=true. Use ngb-property-management-perf/.env.write.local.');
}

function writeCheckTags(scenario: string): Record<string, string> {
  return {
    profile: WRITE_HEAVY_PROFILE,
    vertical: WRITE_HEAVY_VERTICAL,
    scenario,
    area: 'documents',
    operation: 'pm.write_heavy.write_executed',
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

function abortTest(message: string): never {
  exec.test.abort(message);
  fail(message);
}
