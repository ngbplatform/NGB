import { httpGet } from './http'
import type { AuditLogPageDto } from './contracts'

export type GetEntityAuditLogOptions = {
  afterOccurredAtUtc?: string | null
  afterAuditEventId?: string | null
  limit?: number
}

export async function getEntityAuditLog(
  entityKind: number,
  entityId: string,
  opts?: GetEntityAuditLogOptions,
): Promise<AuditLogPageDto> {
  return await httpGet<AuditLogPageDto>(
    `/api/audit/entities/${encodeURIComponent(String(entityKind))}/${encodeURIComponent(entityId)}`,
    {
      afterOccurredAtUtc: opts?.afterOccurredAtUtc,
      afterAuditEventId: opts?.afterAuditEventId,
      limit: opts?.limit,
    },
  )
}
