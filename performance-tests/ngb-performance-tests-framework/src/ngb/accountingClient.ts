import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';

export class AccountingClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  getChartOfAccountsMetadata(): NgbHttpResponse {
    return this.http.get('/api/chart-of-accounts/metadata', {
      tags: {
        vertical: this.env.vertical,
        area: 'accounting',
        operation: 'platform.accounting.chart.metadata',
      },
    });
  }

  listChartOfAccounts(limit = 50): NgbHttpResponse {
    return this.http.get('/api/chart-of-accounts', {
      query: { offset: 0, limit },
      tags: {
        vertical: this.env.vertical,
        area: 'accounting',
        operation: 'platform.accounting.chart.list',
      },
    });
  }
}
