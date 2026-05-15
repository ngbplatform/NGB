import type { NgbPerfEnv } from '../core/env.ts';
import type { NgbHttpClient, NgbHttpResponse, QueryValue } from '../core/httpClient.ts';

export type AuditEntityKind =
  | 'Document'
  | 'Catalog'
  | 'ChartOfAccountsAccount'
  | 'Period'
  | 'OperationalRegister'
  | 'DocumentRelationship'
  | 'ReferenceRegister';

export interface AuditLogQuery {
  readonly afterOccurredAtUtc?: string | null;
  readonly afterAuditEventId?: string | null;
  readonly limit?: number;
}

export class AuditClient {
  constructor(
    private readonly http: NgbHttpClient,
    private readonly env: NgbPerfEnv,
  ) {}

  getEntityAuditLog(entityKind: AuditEntityKind, entityId: string, query: AuditLogQuery = {}): NgbHttpResponse {
    return this.http.get(`/api/audit/entities/${encodeURIComponent(entityKind)}/${encodeURIComponent(entityId)}`, {
      query: toAuditQuery(query),
      tags: {
        vertical: this.env.vertical,
        area: 'audit',
        operation: 'platform.audit.entity_log',
        entityKind,
      },
    });
  }
}

function toAuditQuery(query: AuditLogQuery): Record<string, QueryValue> {
  return {
    afterOccurredAtUtc: query.afterOccurredAtUtc,
    afterAuditEventId: query.afterAuditEventId,
    limit: query.limit ?? 20,
  };
}
