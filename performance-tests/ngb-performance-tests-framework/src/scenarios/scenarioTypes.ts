import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient } from '../core/httpClient.ts';
import type { AdminClient } from '../ngb/adminClient.ts';
import type { AccountingClient } from '../ngb/accountingClient.ts';
import type { AuditClient } from '../ngb/auditClient.ts';
import type { CatalogsClient } from '../ngb/catalogsClient.ts';
import type { DocumentsClient } from '../ngb/documentsClient.ts';
import type { HealthClient } from '../ngb/healthClient.ts';
import type { MetadataClient } from '../ngb/metadataClient.ts';
import type { PeriodClosingClient } from '../ngb/periodClosingClient.ts';
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
  readonly admin: AdminClient;
  readonly audit: AuditClient;
  readonly periodClosing: PeriodClosingClient;
}

export interface NgbAuthSetupData {
  readonly accessToken: string;
  readonly expiresAtUnixMs: number;
}

export type NgbScenarioFlow = (context: NgbScenarioContext) => void;

export interface ScenarioDescriptor {
  readonly name: string;
  readonly description: string;
  readonly area: string;
  readonly operation: string;
  readonly flow: NgbScenarioFlow;
}
