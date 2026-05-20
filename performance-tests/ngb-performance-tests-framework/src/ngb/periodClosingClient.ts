import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';

export class PeriodClosingClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  getMonthStatus(period: string): NgbHttpResponse {
    return this.http.get('/api/accounting/period-closing/month', {
      query: { period },
      tags: {
        vertical: this.env.vertical,
        area: 'period-closing',
        operation: 'platform.period_closing.month_status',
      },
    });
  }

  getCalendar(year: number): NgbHttpResponse {
    return this.http.get('/api/accounting/period-closing/calendar', {
      query: { year },
      tags: {
        vertical: this.env.vertical,
        area: 'period-closing',
        operation: 'platform.period_closing.calendar',
      },
    });
  }

  getFiscalYearStatus(fiscalYearEndPeriod: string): NgbHttpResponse {
    return this.http.get('/api/accounting/period-closing/fiscal-year', {
      query: { fiscalYearEndPeriod },
      tags: {
        vertical: this.env.vertical,
        area: 'period-closing',
        operation: 'platform.period_closing.fiscal_year_status',
      },
    });
  }

  searchRetainedEarningsAccounts(query = '', limit = 20): NgbHttpResponse {
    return this.http.get('/api/accounting/period-closing/retained-earnings-accounts', {
      query: { q: query, limit },
      tags: {
        vertical: this.env.vertical,
        area: 'period-closing',
        operation: 'platform.period_closing.retained_earnings_accounts',
      },
    });
  }

  closeMonth(period: string): NgbHttpResponse {
    return this.http.post('/api/accounting/period-closing/month/close', { period }, {
      tags: {
        vertical: this.env.vertical,
        area: 'period-closing',
        operation: 'platform.period_closing.close_month',
      },
    });
  }

  reopenMonth(period: string, reason: string): NgbHttpResponse {
    return this.http.post('/api/accounting/period-closing/month/reopen', { period, reason }, {
      tags: {
        vertical: this.env.vertical,
        area: 'period-closing',
        operation: 'platform.period_closing.reopen_month',
      },
    });
  }
}
