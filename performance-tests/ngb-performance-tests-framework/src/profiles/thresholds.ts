import type { Threshold } from 'k6/options';

export type ThresholdMap = Record<string, Threshold[]>;
export type DiagnosticBreakdownSelector = Partial<Record<
  'area'
  | 'operation'
  | 'documentType'
  | 'catalogType'
  | 'reportId'
  | 'entityKind'
  | 'periodProfile'
  | 'status',
  string
>>;

export const commonThresholds: ThresholdMap = {
  http_req_failed: ['rate<0.01'],
  checks: ['rate>0.99'],
  ngb_business_operation_failed: ['rate<0.01'],
};

export const operationThresholds: ThresholdMap = {
  'ngb_auth_duration{area:auth}': ['p(95)<1000'],
  'http_req_duration{area:health}': ['p(95)<500'],
  'http_req_duration{area:metadata}': ['p(95)<1000'],
  'http_req_duration{area:catalogs}': ['p(95)<1000'],
  'http_req_duration{area:documents}': ['p(95)<1000'],
  'http_req_duration{area:admin}': ['p(95)<1000'],
  'http_req_duration{area:chart-of-accounts}': ['p(95)<1000'],
  'http_req_duration{area:audit}': ['p(95)<1500'],
  'http_req_duration{area:period-closing}': ['p(95)<1500'],
  'http_req_duration{area:report-export}': ['p(95)<10000'],
  'ngb_document_post_duration{area:documents}': ['p(95)<2500'],
  'ngb_accounting_effects_duration{area:accounting}': ['p(95)<1500'],
  'ngb_document_flow_duration{area:document-flow}': ['p(95)<2000'],
  'ngb_report_execution_duration{area:reports}': ['p(95)<3000'],
};

export const diagnosticOperationThresholds: ThresholdMap = {
  'ngb_auth_duration{area:auth}': ['p(99)<30000'],
  'http_req_duration{area:health}': ['p(99)<30000'],
  'http_req_duration{area:metadata}': ['p(99)<30000'],
  'http_req_duration{area:catalogs}': ['p(99)<30000'],
  'http_req_duration{area:documents}': ['p(99)<30000'],
  'http_req_duration{area:admin}': ['p(99)<30000'],
  'http_req_duration{area:chart-of-accounts}': ['p(99)<30000'],
  'http_req_duration{area:audit}': ['p(99)<30000'],
  'http_req_duration{area:period-closing}': ['p(99)<30000'],
  'http_req_duration{area:report-export}': ['p(99)<60000'],
  'ngb_document_post_duration{area:documents}': ['p(99)<30000'],
  'ngb_accounting_effects_duration{area:accounting}': ['p(99)<30000'],
  'ngb_document_flow_duration{area:document-flow}': ['p(99)<30000'],
  'ngb_report_execution_duration{area:reports}': ['p(99)<30000'],
};

export function reportExecutionBreakdownThresholds(reportIds: readonly string[] = []): ThresholdMap {
  const thresholds: ThresholdMap = {};
  const uniqueReportIds = [...new Set(reportIds.map((reportId) => reportId.trim()).filter(Boolean))];

  for (const reportId of uniqueReportIds) {
    thresholds[`ngb_report_execution_duration{area:reports,operation:platform.reports.execute,reportId:${reportId}}`] = [
      'max<600000',
    ];
  }

  return thresholds;
}

export function diagnosticBreakdownThresholds(
  selectors: readonly DiagnosticBreakdownSelector[] = [],
): ThresholdMap {
  const thresholds: ThresholdMap = {};
  const uniqueSelectors = [...new Set(selectors.map(toK6TagSelector).filter(Boolean))];

  for (const selector of uniqueSelectors) {
    thresholds[`http_req_duration${selector}`] = ['max<600000'];
    thresholds[`http_req_failed${selector}`] = ['rate<1.01'];
    thresholds[`ngb_business_operation_duration${selector}`] = ['max<600000'];
    thresholds[`ngb_business_operation_failed${selector}`] = ['rate<1.01'];
  }

  return thresholds;
}

export function mergeThresholds(...items: ThresholdMap[]): ThresholdMap {
  return items.reduce<ThresholdMap>((merged, item) => ({ ...merged, ...item }), {});
}

export function relaxedThresholds(): ThresholdMap {
  return mergeThresholds(commonThresholds, {
    http_req_failed: ['rate<0.05'],
    checks: ['rate>0.95'],
    ngb_business_operation_failed: ['rate<0.05'],
  });
}

function toK6TagSelector(selector: DiagnosticBreakdownSelector): string {
  const priority = [
    'area',
    'operation',
    'documentType',
    'catalogType',
    'reportId',
    'entityKind',
    'periodProfile',
    'status',
  ] as const;
  const parts: string[] = [];

  for (const key of priority) {
    const value = selector[key]?.trim();
    if (value) {
      parts.push(`${key}:${value}`);
    }
  }

  return parts.length > 0 ? `{${parts.join(',')}}` : '';
}
