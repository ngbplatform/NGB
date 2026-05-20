import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse, QueryValue } from '../core/httpClient.ts';

export interface ChartOfAccountsQuery {
  readonly offset?: number;
  readonly limit?: number;
  readonly search?: string;
  readonly accountTypes?: readonly string[];
  readonly includeDeleted?: boolean;
  readonly onlyActive?: boolean;
  readonly onlyDeleted?: boolean;
}

export class AdminClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  getMainMenu(): NgbHttpResponse {
    return this.http.get('/api/main-menu', {
      tags: {
        vertical: this.env.vertical,
        area: 'admin',
        operation: 'platform.admin.main_menu',
      },
    });
  }

  getChartOfAccountsMetadata(): NgbHttpResponse {
    return this.http.get('/api/chart-of-accounts/metadata', {
      tags: {
        vertical: this.env.vertical,
        area: 'chart-of-accounts',
        operation: 'platform.chart_of_accounts.metadata',
      },
    });
  }

  listChartOfAccounts(query: ChartOfAccountsQuery = {}): NgbHttpResponse {
    return this.http.get('/api/chart-of-accounts', {
      query: toChartOfAccountsQuery(query),
      tags: {
        vertical: this.env.vertical,
        area: 'chart-of-accounts',
        operation: 'platform.chart_of_accounts.list',
      },
    });
  }

  getChartOfAccount(accountId: string): NgbHttpResponse {
    return this.http.get(`/api/chart-of-accounts/${encodeURIComponent(accountId)}`, {
      tags: {
        vertical: this.env.vertical,
        area: 'chart-of-accounts',
        operation: 'platform.chart_of_accounts.open',
      },
    });
  }

  getChartOfAccountsByIds(accountIds: readonly string[]): NgbHttpResponse {
    return this.http.post('/api/chart-of-accounts/by-ids', { ids: accountIds }, {
      tags: {
        vertical: this.env.vertical,
        area: 'chart-of-accounts',
        operation: 'platform.chart_of_accounts.by_ids',
      },
    });
  }
}

function toChartOfAccountsQuery(query: ChartOfAccountsQuery): Record<string, QueryValue> {
  return {
    offset: query.offset ?? 0,
    limit: query.limit ?? 100,
    search: query.search,
    accountTypes: query.accountTypes?.join(','),
    includeDeleted: query.includeDeleted,
    onlyActive: query.onlyActive,
    onlyDeleted: query.onlyDeleted,
  };
}
