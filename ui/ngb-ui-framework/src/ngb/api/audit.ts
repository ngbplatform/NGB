import { httpGet, type HttpRequestOptions } from './http'
import type { AuditLogPageDto } from './contracts'

export type GetEntityAuditLogOptions = {
  afterOccurredAtUtc?: string | null
  afterAuditEventId?: string | null
  limit?: number
  signal?: AbortSignal
}

export async function getEntityAuditLog(
  entityKind: number,
  entityId: string,
  opts?: GetEntityAuditLogOptions,
): Promise<AuditLogPageDto> {
  const url = `/api/audit/entities/${encodeURIComponent(String(entityKind))}/${encodeURIComponent(entityId)}`
  const query = {
    afterOccurredAtUtc: opts?.afterOccurredAtUtc,
    afterAuditEventId: opts?.afterAuditEventId,
    limit: opts?.limit,
  }
  return opts?.signal
    ? await httpGet<AuditLogPageDto>(url, query, { signal: opts.signal } satisfies HttpRequestOptions)
    : await httpGet<AuditLogPageDto>(url, query)
}
