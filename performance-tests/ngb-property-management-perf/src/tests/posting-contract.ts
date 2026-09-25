import { check, fail } from 'k6';
import type { NgbAuthSetupData } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { getNgbScenarioContext, setupNgbAccessToken } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioBuilder.ts';
import { defaultHandleSummary, withSummaryTrendStats } from '../../../ngb-performance-tests-framework/src/core/summary.ts';
import { diagnosticBreakdownThresholds } from '../../../ngb-performance-tests-framework/src/profiles/thresholds.ts';
import { postingEnabled } from '../flows/pmFlowSupport.ts';
import { pmRentChargePostingFlow } from '../flows/pmRentChargePostingFlow.ts';

const vus = Number(__ENV.NGB_PM_POSTING_PROBE_VUS || '1');
if (!Number.isInteger(vus) || vus < 1 || vus > 4) throw new Error('Posting probe VUs must be 1..4.');
const replay = __ENV.NGB_PM_POSTING_MODE === 'idempotent-replay';
export const options = withSummaryTrendStats({
  scenarios: { posting_contract: { executor: 'per-vu-iterations', vus, iterations: 3, maxDuration: '3m' } },
  thresholds: {
    checks: ['rate==1'],
    http_req_failed: ['rate==0'],
    ngb_pm_posting_succeeded: [`count==${vus * 3}`],
    'ngb_pm_posting_succeeded{postingMode:fresh}': [`count==${vus * (replay ? 1 : 3)}`],
    ...(replay ? { 'ngb_pm_posting_succeeded{postingMode:replay}': [`count==${vus * 2}`] } : {}),
    ...diagnosticBreakdownThresholds(['fresh', 'replay'].map(postingMode => ({
      area: 'documents', operation: 'platform.documents.post', documentType: 'pm.rent_charge', postingMode,
    }))),
  },
});
export function setup(): NgbAuthSetupData {
  const data = setupNgbAccessToken();
  if (!postingEnabled(getNgbScenarioContext(data)) || !__ENV.NGB_PM_FIXTURE_RENT_CHARGE_ID) {
    fail('Posting probe requires write/post gates and an explicit rent-charge template ID.');
  }
  return data;
}
export default function (data: NgbAuthSetupData): void {
  check(null, { 'posting flow completed': () => pmRentChargePostingFlow(getNgbScenarioContext(data)) });
}
export function handleSummary(data: unknown): Record<string, string> { return defaultHandleSummary(data); }
