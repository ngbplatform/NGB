import { documentOpenFlow } from '../../../ngb-performance-tests-framework/src/flows/documentOpenFlow.ts';
import { reportExecutionFlow } from '../../../ngb-performance-tests-framework/src/flows/reportExecutionFlow.ts';
import type { NgbHttpResponse } from '../../../ngb-performance-tests-framework/src/core/httpClient.ts';
import type { ReportExecutionRequest } from '../../../ngb-performance-tests-framework/src/ngb/reportsClient.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';

export type PmPeriodProfile = 'open' | 'closed' | 'long';

export interface PmResolvedPeriodProfile {
  readonly profile: PmPeriodProfile;
  readonly fromUtc: string;
  readonly toUtc: string;
  readonly asOfUtc: string;
  readonly periodUtc: string;
}

export function currentDateOnly(): string {
  return new Date().toISOString().slice(0, 10);
}

export function currentMonthStart(): string {
  const today = currentDateOnly();
  return `${today.slice(0, 7)}-01`;
}

export function accountingDateRangeRequest(profile: PmPeriodProfile = 'open', limit = 200): ReportExecutionRequest {
  const period = resolvePeriodProfile(profile);
  return {
    parameters: {
      from_utc: period.fromUtc,
      to_utc: period.toUtc,
    },
    limit,
  };
}

export function asOfDateRequest(profile: PmPeriodProfile = 'open', limit = 200): ReportExecutionRequest {
  const period = resolvePeriodProfile(profile);
  return {
    parameters: {
      as_of_utc: period.asOfUtc,
    },
    limit,
  };
}

export function accountingPeriodRequest(profile: PmPeriodProfile = 'open', limit = 200): ReportExecutionRequest {
  const period = resolvePeriodProfile(profile);
  return {
    parameters: {
      period_utc: period.periodUtc,
    },
    limit,
  };
}

export function accountFilteredAccountingDateRangeRequest(
  accountId: string,
  profile: PmPeriodProfile = 'open',
  limit = 200,
): ReportExecutionRequest {
  return {
    ...accountingDateRangeRequest(profile, limit),
    filters: {
      account_id: {
        value: accountId,
        includeDescendants: false,
      },
    },
  };
}

export function resolveAccountId(): string | null {
  return __ENV.NGB_ACCOUNT_FIXTURE_ACCOUNT_ID?.trim()
    || __ENV.NGB_PM_FIXTURE_ACCOUNT_ID?.trim()
    || null;
}

export function resolveFixtureId(...names: string[]): string | null {
  for (const name of names) {
    const value = __ENV[name]?.trim();
    if (value) {
      return value;
    }
  }

  return null;
}

export function postingEnabled(context: NgbScenarioContext): boolean {
  return context.env.enableWrites && readBooleanEnv('NGB_PERF_ENABLE_POSTING', false);
}

export function cleanupCreatedDraftsEnabled(): boolean {
  return readBooleanEnv('NGB_PERF_DELETE_CREATED_DRAFTS', true);
}

export function readBooleanEnv(name: string, defaultValue = false): boolean {
  const value = __ENV[name]?.trim();
  if (!value) {
    return defaultValue;
  }

  return ['1', 'true', 'yes', 'on'].includes(value.toLowerCase());
}

export function resolveLeaseId(context: NgbScenarioContext): string | null {
  const explicit = __ENV.NGB_PM_FIXTURE_LEASE_ID?.trim();
  if (explicit) {
    return explicit;
  }

  return documentOpenFlow(context, PM_DOCUMENT_TYPES.lease);
}

export function leaseFilteredReportRequest(leaseId: string): ReportExecutionRequest {
  const period = resolvePeriodProfile('open');
  return {
    filters: {
      lease_id: {
        value: leaseId,
        includeDescendants: false,
      },
    },
    parameters: {
      as_of_utc: period.asOfUtc,
      from_utc: period.fromUtc,
      to_utc: period.toUtc,
    },
    limit: 200,
  };
}

export function executeLeaseOptionalReport(
  context: NgbScenarioContext,
  reportId: string,
  fallback: ReportExecutionRequest = {},
): void {
  const leaseId = resolveLeaseId(context);
  if (leaseId) {
    reportExecutionFlow(context, reportId, leaseFilteredReportRequest(leaseId));
    return;
  }

  context.reports.getReportDefinition(reportId);
  if (Object.keys(fallback).length > 0) {
    reportExecutionFlow(context, reportId, fallback);
  }
}

export function reportTags(profile: PmPeriodProfile): { readonly periodProfile: string } {
  return { periodProfile: profile };
}

export function firstItemId(response: { json(): unknown }): string | null {
  return pageItemIds(response, 1)[0] ?? null;
}

export function pageItemIds(response: { json(): unknown }, max = 10): string[] {
  try {
    const json = response.json();
    const items = typeof json === 'object' && json !== null
      ? (json as { items?: Array<{ id?: unknown }> }).items
      : undefined;
    return (items ?? [])
      .map((item) => item.id)
      .filter((id): id is string => typeof id === 'string' && id.length > 0)
      .slice(0, max);
  } catch {
    return [];
  }
}

export function responseDocumentId(response: NgbHttpResponse): string | null {
  try {
    const json = response.json();
    const id = typeof json === 'object' && json !== null
      ? (json as { id?: unknown }).id
      : undefined;
    return typeof id === 'string' && id.length > 0 ? id : null;
  } catch {
    return null;
  }
}

export function resolvePeriodProfile(profile: PmPeriodProfile = 'open'): PmResolvedPeriodProfile {
  const defaults = defaultPeriodProfile(profile);
  const prefix = profile === 'open' ? '' : `${profile.toUpperCase()}_`;

  const fromUtc = readDateEnv(`NGB_PERF_${prefix}FROM_UTC`) ?? defaults.fromUtc;
  const toUtc = readDateEnv(`NGB_PERF_${prefix}TO_UTC`) ?? defaults.toUtc;
  const asOfUtc = readDateEnv(`NGB_PERF_${prefix}AS_OF_UTC`) ?? defaults.asOfUtc;
  const periodUtc = readDateEnv(`NGB_PERF_${prefix}PERIOD_UTC`) ?? defaults.periodUtc;

  return {
    profile,
    fromUtc,
    toUtc,
    asOfUtc,
    periodUtc,
  };
}

function defaultPeriodProfile(profile: PmPeriodProfile): PmResolvedPeriodProfile {
  const today = currentDateOnly();
  const currentStart = monthStart(today);

  if (profile === 'closed') {
    const previousStart = addMonths(currentStart, -1);
    const previousEnd = addDays(currentStart, -1);
    return {
      profile,
      fromUtc: previousStart,
      toUtc: previousEnd,
      asOfUtc: previousEnd,
      periodUtc: previousStart,
    };
  }

  if (profile === 'long') {
    const fromUtc = addMonths(currentStart, -11);
    return {
      profile,
      fromUtc,
      toUtc: today,
      asOfUtc: today,
      periodUtc: currentStart,
    };
  }

  return {
    profile,
    fromUtc: currentStart,
    toUtc: today,
    asOfUtc: today,
    periodUtc: currentStart,
  };
}

function readDateEnv(name: string): string | null {
  const value = __ENV[name]?.trim();
  if (!value) {
    return null;
  }

  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    throw new Error(`${name} must be a date in YYYY-MM-DD format. Received: ${value}`);
  }

  return value;
}

function monthStart(dateOnly: string): string {
  return `${dateOnly.slice(0, 7)}-01`;
}

function addMonths(dateOnly: string, months: number): string {
  const parts = parseDateParts(dateOnly);
  const shifted = new Date(Date.UTC(parts.year, parts.month - 1 + months, 1));
  return shifted.toISOString().slice(0, 10);
}

function addDays(dateOnly: string, days: number): string {
  const parts = parseDateParts(dateOnly);
  const shifted = new Date(Date.UTC(parts.year, parts.month - 1, parts.day + days));
  return shifted.toISOString().slice(0, 10);
}

function parseDateParts(dateOnly: string): { readonly year: number; readonly month: number; readonly day: number } {
  const match = dateOnly.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!match) {
    throw new Error(`Expected a date in YYYY-MM-DD format. Received: ${dateOnly}`);
  }

  return {
    year: Number.parseInt(match[1]!, 10),
    month: Number.parseInt(match[2]!, 10),
    day: Number.parseInt(match[3]!, 10),
  };
}
