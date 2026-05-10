import { commandPaletteFlow } from '../../../ngb-performance-tests-framework/src/flows/commandPaletteFlow.ts';
import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';

export function pmCommandPaletteFlow(context: NgbScenarioContext): void {
  commandPaletteFlow(context, 'lease');
  commandPaletteFlow(context, 'rent');
  commandPaletteFlow(context, 'report aging');
}
