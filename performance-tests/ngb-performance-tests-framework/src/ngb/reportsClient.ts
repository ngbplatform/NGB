import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';
import type { NgbRequestTags } from '../core/requestTags.ts';

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

  executeReport(reportId: string, request: ReportExecutionRequest = {}, tags: NgbRequestTags = {}): NgbHttpResponse {
    return this.http.post(`/api/reports/${encodeURIComponent(reportId)}/execute`, normalizeReportRequest(request), {
      tags: {
        vertical: this.env.vertical,
        area: 'reports',
        operation: 'platform.reports.execute',
        reportId,
        ...tags,
      },
    });
  }

  executeReportPage(reportId: string, request: ReportExecutionRequest, cursor?: string | null, tags: NgbRequestTags = {}): NgbHttpResponse {
    return this.executeReport(reportId, { ...request, cursor: cursor ?? request.cursor ?? null }, tags);
  }

  exportXlsx(reportId: string, request: ReportExecutionRequest = {}, tags: NgbRequestTags = {}): NgbHttpResponse {
    return this.http.post(`/api/reports/${encodeURIComponent(reportId)}/export/xlsx`, normalizeReportRequest(request), {
      tags: {
        vertical: this.env.vertical,
        area: 'report-export',
        operation: 'platform.reports.export_xlsx',
        reportId,
        ...tags,
      },
      expectedStatuses: [200],
    });
  }

  listVariants(reportId: string): NgbHttpResponse {
    return this.http.get(`/api/reports/${encodeURIComponent(reportId)}/variants`, {
      tags: {
        vertical: this.env.vertical,
        area: 'reports',
        operation: 'platform.reports.variants.list',
        reportId,
      },
    });
  }

  getVariant(reportId: string, variantCode: string): NgbHttpResponse {
    return this.http.get(`/api/reports/${encodeURIComponent(reportId)}/variants/${encodeURIComponent(variantCode)}`, {
      tags: {
        vertical: this.env.vertical,
        area: 'reports',
        operation: 'platform.reports.variants.get',
        reportId,
      },
    });
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
