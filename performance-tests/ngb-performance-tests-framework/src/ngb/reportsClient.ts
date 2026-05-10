import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';

export interface ReportExecutionRequest {
  readonly layout?: Record<string, unknown> | null;
  readonly filters?: Record<string, unknown> | null;
  readonly parameters?: Record<string, string> | null;
  readonly variantCode?: string | null;
  readonly offset?: number;
  readonly limit?: number;
  readonly cursor?: string | null;
  readonly disablePaging?: boolean;
}

export class ReportsClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  listReports(): NgbHttpResponse {
    return this.http.get('/api/report-definitions', {
      tags: {
        vertical: this.env.vertical,
        area: 'reports',
        operation: 'platform.reports.list',
      },
    });
  }

  getReportDefinition(reportId: string): NgbHttpResponse {
    return this.http.get(`/api/report-definitions/${encodeURIComponent(reportId)}`, {
      tags: {
        vertical: this.env.vertical,
        area: 'reports',
        operation: 'platform.reports.definition',
        reportId,
      },
    });
  }

  executeReport(reportId: string, request: ReportExecutionRequest = {}): NgbHttpResponse {
    return this.http.post(`/api/reports/${encodeURIComponent(reportId)}/execute`, normalizeReportRequest(request), {
      tags: {
        vertical: this.env.vertical,
        area: 'reports',
        operation: 'platform.reports.execute',
        reportId,
      },
    });
  }

  executeReportPage(reportId: string, request: ReportExecutionRequest, cursor?: string | null): NgbHttpResponse {
    return this.executeReport(reportId, { ...request, cursor: cursor ?? request.cursor ?? null });
  }
}

export function normalizeReportRequest(request: ReportExecutionRequest): ReportExecutionRequest {
  return {
    layout: request.layout ?? null,
    filters: request.filters ?? null,
    parameters: request.parameters ?? null,
    variantCode: request.variantCode ?? null,
    offset: request.offset ?? 0,
    limit: request.limit ?? 200,
    cursor: request.cursor ?? null,
    disablePaging: request.disablePaging ?? false,
  };
}
