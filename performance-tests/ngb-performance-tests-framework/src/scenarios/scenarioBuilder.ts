import { group } from 'k6';

import { KeycloakPasswordGrantAuth } from '../auth/keycloakPasswordGrantAuth.ts';
import { SeededAccessTokenProvider } from '../auth/seededAccessTokenProvider.ts';
import { readNgbPerfEnv } from '../core/env.ts';
import { NgbHttpClient } from '../core/httpClient.ts';
import { AdminClient } from '../ngb/adminClient.ts';
import { AccountingClient } from '../ngb/accountingClient.ts';
import { AuditClient } from '../ngb/auditClient.ts';
import { CatalogsClient } from '../ngb/catalogsClient.ts';
import { DocumentsClient } from '../ngb/documentsClient.ts';
import { HealthClient } from '../ngb/healthClient.ts';
import { MetadataClient } from '../ngb/metadataClient.ts';
import { PeriodClosingClient } from '../ngb/periodClosingClient.ts';
import { ReportsClient } from '../ngb/reportsClient.ts';
import type { NgbAuthSetupData, NgbScenarioContext, ScenarioDescriptor } from './scenarioTypes.ts';

let cachedScenarioContext: NgbScenarioContext | undefined;

export function setupNgbAccessToken(): NgbAuthSetupData {
  const env = readNgbPerfEnv();
  const auth = new KeycloakPasswordGrantAuth(env);
  return auth.getAccessTokenGrant();
}

export function getNgbScenarioContext(setupData?: NgbAuthSetupData): NgbScenarioContext {
  cachedScenarioContext ??= createNgbScenarioContext(setupData);
  return cachedScenarioContext;
}

export function createNgbScenarioContext(setupData?: NgbAuthSetupData): NgbScenarioContext {
  const env = readNgbPerfEnv();
  const refreshAuth = new KeycloakPasswordGrantAuth(env);
  const auth = setupData?.accessToken
    ? new SeededAccessTokenProvider(setupData, refreshAuth, env.authSeedRefreshJitterSeconds)
    : refreshAuth;
  const client = new NgbHttpClient({
    env,
    tokenProvider: auth,
  });

  return {
    env,
    http: client,
    health: new HealthClient(client, env),
    catalogs: new CatalogsClient(client, env),
    documents: new DocumentsClient(client, env),
    reports: new ReportsClient(client, env),
    accounting: new AccountingClient(client, env),
    metadata: new MetadataClient(client, env),
    admin: new AdminClient(client, env),
    audit: new AuditClient(client, env),
    periodClosing: new PeriodClosingClient(client, env),
  };
}

export function runScenario(context: NgbScenarioContext, descriptor: ScenarioDescriptor): void {
  group(descriptor.name, () => {
    descriptor.flow(context);
  });
}
