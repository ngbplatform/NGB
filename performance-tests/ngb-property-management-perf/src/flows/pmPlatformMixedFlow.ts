import type { NgbScenarioContext } from '../../../ngb-performance-tests-framework/src/scenarios/scenarioTypes.ts';
import { pmAccountingEffectsFlow } from './pmAccountingEffectsFlow.ts';
import { pmDocumentFlowReadFlow } from './pmDocumentFlowReadFlow.ts';
import { pmDocumentLifecycleFlow } from './pmDocumentLifecycleFlow.ts';
import { pmPlatformAuditFlow } from './pmPlatformAuditFlow.ts';
import { pmPlatformMaintenanceFlow } from './pmPlatformMaintenanceFlow.ts';
import { pmPlatformReadFlow } from './pmPlatformReadFlow.ts';
import { pmRentChargePostingFlow } from './pmRentChargePostingFlow.ts';
import { pmReportsFlow } from './pmReportsFlow.ts';

export function pmPlatformLoadFlow(context: NgbScenarioContext): void {
  pmPlatformReadFlow(context, { includeMetadata: false, includeLookup: true, includeDeepPages: false });

  if (Math.random() < 0.25) {
    pmReportsFlow(context, {
      periodProfiles: ['open'],
      includeAccountScopedReports: false,
      includeLedgerAnalysisVariants: false,
    });
  }

  if (Math.random() < 0.25) {
    pmAccountingEffectsFlow(context);
    pmDocumentFlowReadFlow(context);
  }

  if (Math.random() < 0.15) {
    pmPlatformAuditFlow(context);
  }
}

export function pmPlatformStressFlow(context: NgbScenarioContext): void {
  pmPlatformReadFlow(context, { includeMetadata: false, includeLookup: true, includeDeepPages: true });
  pmReportsFlow(context, {
    periodProfiles: ['open', 'closed'],
    includeAccountScopedReports: true,
    includeLedgerAnalysisVariants: true,
  });
  pmAccountingEffectsFlow(context);
  pmDocumentFlowReadFlow(context);
}

export function pmPlatformSpikeFlow(context: NgbScenarioContext): void {
  pmPlatformReadFlow(context, { includeMetadata: false, includeLookup: true, includeDeepPages: false });

  if (Math.random() < 0.10) {
    pmReportsFlow(context, {
      periodProfiles: ['open'],
      includeAccountScopedReports: false,
      includeLedgerAnalysisVariants: false,
    });
  }
}

export function pmPlatformSoakFlow(context: NgbScenarioContext): void {
  pmPlatformReadFlow(context, { includeMetadata: false, includeLookup: true, includeDeepPages: false });

  if (Math.random() < 0.15) {
    pmReportsFlow(context, {
      periodProfiles: ['open'],
      includeAccountScopedReports: false,
      includeLedgerAnalysisVariants: false,
    });
  }

  if (Math.random() < 0.10) {
    pmPlatformAuditFlow(context);
    pmPlatformMaintenanceFlow(context);
  }
}

export function pmPlatformBusinessDayReadFlow(context: NgbScenarioContext): void {
  pmPlatformReadFlow(context, { includeMetadata: false, includeLookup: true, includeDeepPages: false });
}

export function pmPlatformBusinessDayHeavyReadFlow(context: NgbScenarioContext): void {
  pmPlatformReadFlow(context, { includeMetadata: false, includeLookup: true, includeDeepPages: true });
  pmAccountingEffectsFlow(context);
  pmDocumentFlowReadFlow(context);
  pmPlatformAuditFlow(context);
}

export function pmPlatformBusinessDayWriteFlow(context: NgbScenarioContext): void {
  pmDocumentLifecycleFlow(context);
  pmRentChargePostingFlow(context);
}

export function pmPlatformMaxCapabilityFlow(context: NgbScenarioContext): void {
  const roll = Math.random();

  if (roll < 0.50) {
    pmPlatformBusinessDayReadFlow(context);
    return;
  }

  if (roll < 0.70) {
    pmPlatformBusinessDayHeavyReadFlow(context);
    return;
  }

  if (roll < 0.85) {
    pmReportsFlow(context, {
      periodProfiles: ['open', 'closed'],
      includeAccountScopedReports: true,
      includeLedgerAnalysisVariants: true,
    });
    return;
  }

  if (roll < 0.95) {
    pmPlatformAuditFlow(context);
    pmPlatformMaintenanceFlow(context);
    return;
  }

  pmPlatformBusinessDayWriteFlow(context);
}
