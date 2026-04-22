import { describe, expect, it } from 'vitest'

import type {
  AuditLogPage,
  DocumentEffects,
  DocumentRecord,
  EditorPrintBehavior,
  EntityHeaderIconAction,
} from '../../../../src/ngb/editor/types'

describe('editor types', () => {
  it('supports document records, effects, audit pages, and print behaviors', async () => {
    const action: EntityHeaderIconAction = {
      key: 'copyShareLink',
      title: 'Copy share link',
      icon: 'share',
    }
    const record: DocumentRecord = {
      id: 'doc-1',
      number: 'INV-001',
      display: 'Invoice INV-001',
      payload: {
        fields: {
          total: 1250,
        },
      },
      status: 1,
      isMarkedForDeletion: false,
    }
    const effects: DocumentEffects = {
      accountingEntries: [
        {
          entryId: 1,
          occurredAtUtc: '2026-04-08T10:00:00Z',
          amount: 1250,
        },
      ],
      operationalRegisterMovements: [],
      referenceRegisterWrites: [],
      ui: {
        isPosted: false,
        canEdit: true,
        canPost: true,
        canUnpost: false,
        canRepost: false,
        canApply: true,
      },
    }
    const auditPage: AuditLogPage = {
      items: [
        {
          auditEventId: 'audit-1',
          entityKind: 2,
          entityId: 'doc-1',
          actionCode: 'update',
          occurredAtUtc: '2026-04-08T10:05:00Z',
          changes: [
            {
              fieldPath: 'payload.total',
              oldValueJson: '1200',
              newValueJson: '1250',
            },
          ],
        },
      ],
      nextCursor: {
        occurredAtUtc: '2026-04-08T10:05:00Z',
        auditEventId: 'audit-1',
      },
      limit: 20,
    }
    const printBehavior: EditorPrintBehavior = {
      resolveLookupHint: ({ documentType, fieldKey }) => ({
        kind: 'catalog',
        catalogType: `${documentType}.${fieldKey}`,
      }),
    }

    expect(action.icon).toBe('share')
    expect(record.payload.fields?.total).toBe(1250)
    expect(effects.ui?.canPost).toBe(true)
    expect(auditPage.items[0]?.changes[0]?.fieldPath).toBe('payload.total')
    expect(printBehavior.resolveLookupHint?.({
      documentType: 'pm.invoice',
      fieldKey: 'customer',
      lookup: null,
    })).toEqual({
      kind: 'catalog',
      catalogType: 'pm.invoice.customer',
    })
  })
})
