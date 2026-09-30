import { check } from 'k6';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import { defaultHandleSummary, withSummaryTrendStats } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { diagnosticBreakdownThresholds } from '../../../ngb-performance-tests-framework/src/profiles/thresholds.ts';
import { ledgerAnalysisGroupedRequest } from '../flows/pmReportsFlow.ts';

const vus = Number(__ENV.NGB_PM_REPORTING_PROBE_VUS || '1');
if (!Number.isInteger(vus) || vus < 1 || vus > 5) throw new Error('Reporting probe VUs must be 1..5.');
const periods = ['open', 'closed', 'long'] as const;
const selectors = periods.flatMap(periodProfile => [
 { area: 'reports', operation: 'platform.reports.execute', reportId: 'accounting.ledger.analysis', periodProfile },
 { area: 'report-export', operation: 'platform.reports.export_xlsx', reportId: 'accounting.ledger.analysis', periodProfile },
]);
export const options = withSummaryTrendStats({
 scenarios: { reporting_probe: { executor: 'per-vu-iterations', vus, iterations: 3, maxDuration: '3m' } },
 thresholds: { checks: ['rate==1'], http_req_failed: ['rate==0'],
  ...diagnosticBreakdownThresholds(selectors.flatMap(s => [s, ...['0','200','429','500','503','504'].map(status => ({ ...s,status }))])),
 },
});
export function setup(): NgbAuthSetupData { return setupNgbAccessToken(); }
export default function(data: NgbAuthSetupData): void {
 const c = getNgbScenarioContext(data);
 for(const periodProfile of periods) {
  const request = ledgerAnalysisGroupedRequest(periodProfile,500);
  for (const operation of ['execute', 'export'] as const) {
   const r = operation === 'execute'
    ? c.reports.executeReport('accounting.ledger.analysis',request,{periodProfile})
    : c.reports.exportXlsx('accounting.ledger.analysis',request,{periodProfile});
   check(r, { 'report probe returned 200': r => r.status === 200 });
   console.log(JSON.stringify({ operation, periodProfile, status:r.status, durationMs:r.timings.duration,
    bodySize:typeof r.body === 'string' ? r.body.length : r.body?.byteLength, contentType:r.headers['Content-Type'], retryAfter:r.headers['Retry-After'] }));
  }
 }
}
export function handleSummary(data: unknown): Record<string,string> { return defaultHandleSummary(data); }
