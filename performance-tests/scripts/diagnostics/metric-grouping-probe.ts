import { check } from 'k6';
import { NgbHttpClient } from '../../ngb-performance-tests-framework/src/core/httpClient.ts';
import type { NgbPerfEnv } from '../../ngb-performance-tests-framework/src/core/env.ts';

export const options = {
  vus: 1,
  iterations: 1,
  thresholds: {
    http_reqs: ['count==202'],
    'checks{probe:routing}': ['rate==1'],
  },
};

const client = new NgbHttpClient({
  env: { apiBaseUrl: __ENV.NGB_PROBE_API, vertical: 'property-management' } as NgbPerfEnv,
  tokenProvider: { getAccessToken: () => 'synthetic-local-probe' },
  defaultTags: { area: 'documents' },
});

export default function (): void {
  for (let id = 0; id < 200; id += 1) {
    const response = client.get(`/documents/${id}`, {
      query: { version: id },
      tags: { operation: 'platform.documents.open', documentType: 'probe.document' },
    });
    check(response, { 'request URL preserved': r => r.json('path') === `/documents/${id}?version=${id}` }, { probe: 'routing' });
  }
  for (const label of ['first', 'second']) {
    const response = client.get(`/untagged/${label}`);
    check(response, { 'request URL preserved': r => r.json('path') === `/untagged/${label}` }, { probe: 'routing' });
  }
}

export function handleSummary(data: unknown): Record<string, string> {
  return { [__ENV.NGB_PROBE_SUMMARY]: JSON.stringify(data, null, 2) };
}
