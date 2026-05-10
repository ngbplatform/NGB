import { documentOpenFlow } from '../../../ngb-performance-tests-framework/src/flows/documentOpenFlow.ts';
import { reportExecutionFlow } from '../../../ngb-performance-tests-framework/src/flows/reportExecutionFlow.ts';
import type { ReportExecutionRequest } from '../../../ngb-performance-tests-framework/src/ngb/reportsClient.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { PM_DOCUMENT_TYPES } from '../clients/pmDocumentTypes.ts';

export function currentDateOnly(): string {
  return new Date().toISOString().slice(0, 10);
}

export function currentMonthStart(): string {
  const today = currentDateOnly();
  return `${today.slice(0, 7)}-01`;
}

export function accountingDateRangeRequest(): ReportExecutionRequest {
  return {
    parameters: {
      from_utc: currentMonthStart(),
      to_utc: currentDateOnly(),
    },
    limit: 200,
  };
}

export function asOfDateRequest(): ReportExecutionRequest {
  return {
    parameters: {
      as_of_utc: currentDateOnly(),
    },
    limit: 200,
  };
}

export function resolveLeaseId(context: NgbScenarioContext): string | null {
  const explicit = __ENV.NGB_PM_FIXTURE_LEASE_ID?.trim();
  if (explicit) {
    return explicit;
  }

  return documentOpenFlow(context, PM_DOCUMENT_TYPES.lease);
}

export function leaseFilteredReportRequest(leaseId: string): ReportExecutionRequest {
  return {
    filters: {
      lease_id: {
        value: leaseId,
        includeDescendants: false,
      },
    },
    parameters: {
      as_of_utc: currentDateOnly(),
      from_utc: currentMonthStart(),
      to_utc: currentDateOnly(),
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
