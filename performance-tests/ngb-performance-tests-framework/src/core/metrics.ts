import { Counter, Rate, Trend } from 'k6/metrics';

export const businessOperationDuration = new Trend('ngb_business_operation_duration', true);
export const businessOperationFailed = new Rate('ngb_business_operation_failed');
export const businessOperationCount = new Counter('ngb_business_operation_count');

export const authDuration = new Trend('ngb_auth_duration', true);
export const documentPostDuration = new Trend('ngb_document_post_duration', true);
export const reportExecutionDuration = new Trend('ngb_report_execution_duration', true);
export const accountingEffectsDuration = new Trend('ngb_accounting_effects_duration', true);
export const documentFlowDuration = new Trend('ngb_document_flow_duration', true);

export function recordBusinessOperation(
  durationMs: number,
  failed: boolean,
  tags: Record<string, string>,
): void {
  businessOperationCount.add(1, tags);
  businessOperationDuration.add(durationMs, tags);
  businessOperationFailed.add(failed, tags);
}
