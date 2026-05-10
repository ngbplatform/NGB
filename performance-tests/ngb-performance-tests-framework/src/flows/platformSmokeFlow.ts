import { thinkTime } from '../core/sleep.ts';
import type { NgbScenarioContext } from '../scenarios/scenarioTypes.ts';

export function platformSmokeFlow(context: NgbScenarioContext): void {
  context.health.check();
  thinkTime(0.1, 0.4);
  context.metadata.loadAll();
}
