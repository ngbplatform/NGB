import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';

export class MetadataClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  loadAll(): NgbHttpResponse[] {
    return [
      this.http.get('/api/catalogs/metadata', {
        tags: {
          vertical: this.env.vertical,
          area: 'metadata',
          operation: 'platform.metadata.catalogs',
        },
      }),
      this.http.get('/api/documents/metadata', {
        tags: {
          vertical: this.env.vertical,
          area: 'metadata',
          operation: 'platform.metadata.documents',
        },
      }),
      this.http.get('/api/report-definitions', {
        tags: {
          vertical: this.env.vertical,
          area: 'metadata',
          operation: 'platform.metadata.reports',
        },
      }),
    ];
  }
}
