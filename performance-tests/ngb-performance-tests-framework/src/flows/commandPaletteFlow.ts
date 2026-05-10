import { operationSucceeded } from '../core/checks.ts';
import { thinkTime } from '../core/sleep.ts';
import type { NgbScenarioContext } from '../scenarios/scenarioTypes.ts';

export function commandPaletteFlow(context: NgbScenarioContext, query = 'lease'): void {
  const response = context.commandPalette.search({
    query,
    limit: 8,
  });

  operationSucceeded(response, [200]);
  thinkTime(0.2, 0.8);
}
