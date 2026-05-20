import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';

export class HealthClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  check(): NgbHttpResponse {
    return this.http.get('/health', {
      tags: {
        vertical: this.env.vertical,
        area: 'health',
        operation: 'platform.health.check',
      },
      expectedStatuses: [200],
    });
  }
}
