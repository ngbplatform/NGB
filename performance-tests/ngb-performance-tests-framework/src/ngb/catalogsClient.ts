import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse, QueryValue } from '../core/httpClient.ts';

export interface PageQuery {
  readonly offset?: number;
  readonly limit?: number;
  readonly search?: string;
  readonly filters?: Record<string, QueryValue>;
}

export class CatalogsClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  listMetadata(): NgbHttpResponse {
    return this.http.get('/api/catalogs/metadata', {
      tags: {
        vertical: this.env.vertical,
        area: 'metadata',
        operation: 'platform.catalogs.metadata.list',
      },
    });
  }

  getCatalogMetadata(catalogType: string): NgbHttpResponse {
    return this.http.get(`/api/catalogs/${encodeURIComponent(catalogType)}/metadata`, {
      tags: {
        vertical: this.env.vertical,
        area: 'metadata',
        operation: 'platform.catalogs.metadata.get',
        catalogType,
      },
    });
  }

  listCatalogItems(catalogType: string, query: PageQuery = {}): NgbHttpResponse {
    return this.http.get(`/api/catalogs/${encodeURIComponent(catalogType)}`, {
      query: toPageQuery(query),
      tags: {
        vertical: this.env.vertical,
        area: 'catalogs',
        operation: 'platform.catalogs.list',
        catalogType,
      },
    });
  }

  openCatalogItem(catalogType: string, id: string): NgbHttpResponse {
    return this.http.get(`/api/catalogs/${encodeURIComponent(catalogType)}/${encodeURIComponent(id)}`, {
      tags: {
        vertical: this.env.vertical,
        area: 'catalogs',
        operation: 'platform.catalogs.open',
        catalogType,
      },
    });
  }
}

export function toPageQuery(query: PageQuery): Record<string, QueryValue> {
  return {
    offset: query.offset ?? 0,
    limit: query.limit ?? 50,
    search: query.search,
    ...(query.filters ?? {}),
  };
}
