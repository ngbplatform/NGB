import { jsonHas, operationSucceeded } from '../../../ngb-performance-tests-framework/src/core/checks.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { readBooleanEnv, resolvePeriodProfile } from './pmFlowSupport.ts';

export function pmPlatformMaintenanceFlow(context: NgbScenarioContext): void {
  const open = resolvePeriodProfile('open');
  const closed = resolvePeriodProfile('closed');

  const openStatus = context.periodClosing.getMonthStatus(open.periodUtc);
  operationSucceeded(openStatus, [200]);
  jsonHas(openStatus, 'period');

  const closedStatus = context.periodClosing.getMonthStatus(closed.periodUtc);
  operationSucceeded(closedStatus, [200]);
  jsonHas(closedStatus, 'period');

  const calendar = context.periodClosing.getCalendar(Number.parseInt(open.periodUtc.slice(0, 4), 10));
  operationSucceeded(calendar, [200]);

  const fiscal = context.periodClosing.getFiscalYearStatus(`${open.periodUtc.slice(0, 4)}-12-01`);
  operationSucceeded(fiscal, [200]);

  operationSucceeded(context.periodClosing.searchRetainedEarningsAccounts('', 20), [200]);

  if (context.env.enableWrites && readBooleanEnv('NGB_PERF_ENABLE_PERIOD_CLOSE', false)) {
    const targetPeriod = __ENV.NGB_PERF_CLOSE_PERIOD_UTC?.trim() || closed.periodUtc;
    operationSucceeded(context.periodClosing.closeMonth(targetPeriod), [200]);
    operationSucceeded(context.periodClosing.reopenMonth(targetPeriod, 'NGB performance test cleanup'), [200]);
  }
}
