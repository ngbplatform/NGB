import { group } from 'k6';

import { KeycloakPasswordGrantAuth } from '../auth/keycloakPasswordGrantAuth.ts';
import { StaticAccessTokenProvider } from '../auth/staticAccessTokenProvider.ts';
import { readNgbPerfEnv } from '../core/env.ts';
import { NgbHttpClient } from '../core/httpClient.ts';
import { AccountingClient } from '../ngb/accountingClient.ts';
import { CatalogsClient } from '../ngb/catalogsClient.ts';
import { DocumentsClient } from '../ngb/documentsClient.ts';
import { HealthClient } from '../ngb/healthClient.ts';
import { MetadataClient } from '../ngb/metadataClient.ts';
import { ReportsClient } from '../ngb/reportsClient.ts';
import type { NgbAuthSetupData, NgbScenarioContext, ScenarioDescriptor } from './scenarioTypes.ts';

let cachedScenarioContext: NgbScenarioContext | undefined;

export function setupNgbAccessToken(): NgbAuthSetupData {
  const env = readNgbPerfEnv();
  const auth = new KeycloakPasswordGrantAuth(env);
  return {
    accessToken: auth.getAccessToken(),
  };
}

export function getNgbScenarioContext(setupData?: NgbAuthSetupData): NgbScenarioContext {
  cachedScenarioContext ??= createNgbScenarioContext(setupData);
  return cachedScenarioContext;
}

export function createNgbScenarioContext(setupData?: NgbAuthSetupData): NgbScenarioContext {
  const env = readNgbPerfEnv();
  const auth = setupData?.accessToken
    ? new StaticAccessTokenProvider(setupData.accessToken)
    : new KeycloakPasswordGrantAuth(env);
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
  };
}

export function runScenario(context: NgbScenarioContext, descriptor: ScenarioDescriptor): void {
  group(descriptor.name, () => {
    descriptor.flow(context);
  });
}
