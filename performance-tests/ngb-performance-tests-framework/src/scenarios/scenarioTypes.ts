import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient } from '../core/httpClient.ts';
import type { AccountingClient } from '../ngb/accountingClient.ts';
import type { CatalogsClient } from '../ngb/catalogsClient.ts';
import type { CommandPaletteClient } from '../ngb/commandPaletteClient.ts';
import type { DocumentsClient } from '../ngb/documentsClient.ts';
import type { HealthClient } from '../ngb/healthClient.ts';
import type { MetadataClient } from '../ngb/metadataClient.ts';
import type { ReportsClient } from '../ngb/reportsClient.ts';

export interface NgbScenarioContext {
  readonly env: NgbPerfEnv;
  readonly http: NgbHttpClient;
  readonly health: HealthClient;
  readonly catalogs: CatalogsClient;
  readonly documents: DocumentsClient;
  readonly reports: ReportsClient;
  readonly accounting: AccountingClient;
  readonly metadata: MetadataClient;
  readonly commandPalette: CommandPaletteClient;
}

export interface NgbAuthSetupData {
  readonly accessToken: string;
}

export type NgbScenarioFlow = (context: NgbScenarioContext) => void;

export interface ScenarioDescriptor {
  readonly name: string;
  readonly description: string;
  readonly area: string;
  readonly operation: string;
  readonly flow: NgbScenarioFlow;
}
