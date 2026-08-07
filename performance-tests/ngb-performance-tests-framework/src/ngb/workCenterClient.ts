import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse } from '../core/httpClient.ts';

export interface WorkCenterItemsQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly tab?: 'attention' | 'tasks' | 'notifications' | 'completed';
  readonly vertical?: string | null;
  readonly priority?: 'Low' | 'Normal' | 'High' | 'Critical' | null;
  readonly severity?: 'Information' | 'Success' | 'Warning' | 'Critical' | null;
  readonly overdue?: boolean | null;
  readonly unread?: boolean | null;
}

export class WorkCenterClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  getSummary(): NgbHttpResponse {
    return this.http.get('/api/work-center/summary', {
      tags: {
        vertical: this.env.vertical,
        area: 'work-center',
        operation: 'platform.work_center.summary',
      },
    });
  }

  getItems(query: WorkCenterItemsQuery = {}): NgbHttpResponse {
    return this.http.get('/api/work-center/items', {
      query: {
        cursor: query.cursor ?? undefined,
        limit: query.limit ?? 30,
        tab: query.tab ?? 'attention',
        vertical: query.vertical ?? undefined,
        priority: query.priority ?? undefined,
        severity: query.severity ?? undefined,
        overdue: query.overdue ?? undefined,
        unread: query.unread ?? undefined,
      },
      tags: {
        vertical: this.env.vertical,
        area: 'work-center',
        operation: 'platform.work_center.items',
      },
    });
  }

  getNotificationPreferences(): NgbHttpResponse {
    return this.http.get('/api/me/notification-preferences', {
      tags: {
        vertical: this.env.vertical,
        area: 'work-center',
        operation: 'platform.work_center.preferences',
      },
    });
  }
}
