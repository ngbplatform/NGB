import type { Threshold } from 'k6/options';

export type ThresholdMap = Record<string, Threshold[]>;

export const commonThresholds: ThresholdMap = {
  http_req_failed: ['rate<0.01'],
  checks: ['rate>0.99'],
  ngb_business_operation_failed: ['rate<0.01'],
};

export const operationThresholds: ThresholdMap = {
  'ngb_auth_duration{area:auth}': ['p(95)<1000'],
  'http_req_duration{area:health}': ['p(95)<500'],
  'http_req_duration{area:documents}': ['p(95)<1000'],
  'ngb_document_post_duration{area:documents}': ['p(95)<2500'],
  'ngb_accounting_effects_duration{area:accounting}': ['p(95)<1500'],
  'ngb_document_flow_duration{area:document-flow}': ['p(95)<2000'],
  'ngb_report_execution_duration{area:reports}': ['p(95)<3000'],
};

export const diagnosticOperationThresholds: ThresholdMap = {
  'ngb_auth_duration{area:auth}': ['p(99)<30000'],
  'http_req_duration{area:health}': ['p(99)<30000'],
  'http_req_duration{area:documents}': ['p(99)<30000'],
  'ngb_document_post_duration{area:documents}': ['p(99)<30000'],
  'ngb_accounting_effects_duration{area:accounting}': ['p(99)<30000'],
  'ngb_document_flow_duration{area:document-flow}': ['p(99)<30000'],
  'ngb_report_execution_duration{area:reports}': ['p(99)<30000'],
};

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
