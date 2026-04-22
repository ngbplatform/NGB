import type {
  AuditLogPageDto,
  CatalogItemDto,
  CatalogTypeMetadataDto,
  DocumentDto,
  DocumentEffectsDto,
  DocumentTypeMetadataDto,
  PageResponseDto,
  RelationshipGraphDto,
} from '../../../ngb-ui-framework/src/ngb/api/contracts'
import { PM_TEST_IDS } from '../support/routes'

export const partyCatalogMetadataFixture: CatalogTypeMetadataDto = {
  catalogType: 'pm.party',
  displayName: 'Parties',
  kind: 2,
  list: {
    columns: [
      { key: 'display', label: 'Display', dataType: 'String', isSortable: true, widthPx: 260, align: 1 },
      { key: 'party_type', label: 'Party type', dataType: 'String', isSortable: true, widthPx: 140, align: 1 },
      { key: 'email', label: 'Email', dataType: 'String', isSortable: true, widthPx: 240, align: 1 },
    ],
  },
  form: {
    sections: [
      {
        title: 'Main',
        rows: [
          {
            fields: [
              {
                key: 'display',
                label: 'Display',
                dataType: 'String',
                uiControl: 1,
                isRequired: true,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'party_type',
                label: 'Party type',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'email',
                label: 'Email',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
        ],
      },
    ],
  },
}

export const partyCatalogBaseItemsFixture: CatalogItemDto[] = [
  {
    id: PM_TEST_IDS.partyCatalogPrimaryId,
    display: 'Harbor State Bank',
    payload: {
      fields: {
        display: 'Harbor State Bank',
        party_type: 'Vendor',
        email: 'ap@harbor.example',
      },
    },
    isMarkedForDeletion: false,
    isDeleted: false,
  },
  {
    id: PM_TEST_IDS.partyCatalogSecondaryId,
    display: 'Maple Tenant LLC',
    payload: {
      fields: {
        display: 'Maple Tenant LLC',
        party_type: 'Tenant',
        email: 'billing@maple.example',
      },
    },
    isMarkedForDeletion: false,
    isDeleted: false,
  },
]

export const createdPartyCatalogFixture: CatalogItemDto = {
  id: PM_TEST_IDS.partyCatalogCreatedId,
  display: 'Northwind Services',
  payload: {
    fields: {
      display: 'Northwind Services',
      party_type: 'Vendor',
      email: 'ap@northwind.example',
    },
  },
  isMarkedForDeletion: false,
  isDeleted: false,
}

export function createPartyCatalogPageFixture(items: CatalogItemDto[] = partyCatalogBaseItemsFixture): PageResponseDto<CatalogItemDto> {
  return {
    items,
    offset: 0,
    limit: 50,
    total: items.length,
  }
}

export const receivablePaymentMetadataFixture: DocumentTypeMetadataDto = {
  documentType: 'pm.receivable_payment',
  displayName: 'Receivable Payments',
  kind: 1,
  list: {
    columns: [
      { key: 'display', label: 'Display', dataType: 'String', isSortable: true, widthPx: 260, align: 1 },
      { key: 'payment_reference', label: 'Reference', dataType: 'String', isSortable: true, widthPx: 180, align: 1 },
      { key: 'memo', label: 'Memo', dataType: 'String', isSortable: true, widthPx: 260, align: 1 },
    ],
  },
  form: {
    sections: [
      {
        title: 'Main',
        rows: [
          {
            fields: [
              {
                key: 'display',
                label: 'Display',
                dataType: 'String',
                uiControl: 1,
                isRequired: true,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'payment_reference',
                label: 'Payment reference',
                dataType: 'String',
                uiControl: 1,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
          {
            fields: [
              {
                key: 'memo',
                label: 'Memo',
                dataType: 'String',
                uiControl: 2,
                isRequired: false,
                isReadOnly: false,
              },
            ],
          },
        ],
      },
    ],
  },
}

export const receivablePaymentBaseDocumentsFixture: DocumentDto[] = [
  {
    id: PM_TEST_IDS.receivablePaymentDocumentId,
    number: 'RP-2026-0007',
    display: 'Receivable Payment RP-2026-0007',
    payload: {
      fields: {
        display: 'Receivable Payment RP-2026-0007',
        payment_reference: 'LOCKBOX-2048',
        memo: 'April rent payment',
      },
    },
    status: 1,
    isMarkedForDeletion: false,
  },
  {
    id: PM_TEST_IDS.receivablePaymentSecondaryDocumentId,
    number: 'RP-2026-0006',
    display: 'Receivable Payment RP-2026-0006',
    payload: {
      fields: {
        display: 'Receivable Payment RP-2026-0006',
        payment_reference: 'PORTAL-991',
        memo: 'Late fee recovery',
      },
    },
    status: 1,
    isMarkedForDeletion: false,
  },
]

export const createdReceivablePaymentFixture: DocumentDto = {
  id: PM_TEST_IDS.receivablePaymentCreatedDocumentId,
  number: 'RP-2026-0099',
  display: 'Receivable Payment draft',
  payload: {
    fields: {
      display: 'Receivable Payment draft',
      payment_reference: 'LOCKBOX-NEW',
      memo: 'New payment draft',
    },
  },
  status: 1,
  isMarkedForDeletion: false,
}

export function createReceivablePaymentPageFixture(items: DocumentDto[] = receivablePaymentBaseDocumentsFixture): PageResponseDto<DocumentDto> {
  return {
    items,
    offset: 0,
    limit: 50,
    total: items.length,
  }
}

export const receivablePaymentEffectsFixture: DocumentEffectsDto = {
  accountingEntries: [
    {
      entryId: 'acct-entry-1',
      occurredAtUtc: '2026-04-07T14:18:00Z',
      debitAccount: {
        accountId: '1000',
        code: '1000',
        name: 'Cash',
      },
      creditAccount: {
        accountId: '1200',
        code: '1200',
        name: 'Tenant A/R',
      },
      amount: 1084,
      debitDimensions: [],
      creditDimensions: [],
    },
  ],
  operationalRegisterMovements: [
    {
      registerCode: 'tenant_balances',
      registerName: 'Tenant Balances',
      movementId: 'op-entry-1',
      occurredAtUtc: '2026-04-07T14:18:00Z',
      dimensions: [],
      resources: [{ code: 'amount', value: 1084 }],
    },
  ],
  referenceRegisterWrites: [
    {
      registerCode: 'receivables_open_items',
      registerName: 'Receivables Open Items',
      recordId: 'rr-entry-1',
      recordedAtUtc: '2026-04-07T14:18:00Z',
      dimensions: [],
      fields: {
        state: 'Open',
        amount: 1084,
      },
      isTombstone: false,
    },
  ],
  ui: {
    isPosted: false,
    canEdit: true,
    canPost: true,
    canUnpost: false,
    canRepost: false,
    canApply: false,
  },
}

export const receivablePaymentGraphFixture: RelationshipGraphDto = {
  nodes: [
    {
      nodeId: 'payment-root',
      kind: 1,
      typeCode: 'pm.receivable_payment',
      entityId: PM_TEST_IDS.receivablePaymentDocumentId,
      title: 'Receivable Payment RP-2026-0007',
      subtitle: '2026-04-07T14:18:00Z',
      documentStatus: 1,
      amount: 1084,
    },
    {
      nodeId: 'charge-source',
      kind: 1,
      typeCode: 'pm.rent_charge',
      entityId: '12121212-1212-4212-8212-121212121212',
      title: 'Rent Charge RC-2026-0208',
      subtitle: '2026-04-01T12:00:00Z',
      documentStatus: 2,
      amount: 1084,
    },
    {
      nodeId: 'apply-related',
      kind: 1,
      typeCode: 'pm.receivable_apply',
      entityId: '34343434-3434-4434-8434-343434343434',
      title: 'Receivable Apply RA-2026-0091',
      subtitle: '2026-04-07T14:19:00Z',
      documentStatus: 2,
      amount: 1084,
    },
  ],
  edges: [
    {
      fromNodeId: 'charge-source',
      toNodeId: 'payment-root',
      relationshipType: 'created_from',
      label: 'Created from',
    },
    {
      fromNodeId: 'payment-root',
      toNodeId: 'apply-related',
      relationshipType: 'related_to',
      label: 'Related',
    },
  ],
}

export const receivablePaymentAuditFixture: AuditLogPageDto = {
  items: [
    {
      auditEventId: 'audit-payment-1',
      entityKind: 1,
      entityId: PM_TEST_IDS.receivablePaymentDocumentId,
      actionCode: 'Updated',
      actor: {
        userId: 'ui-tester',
        displayName: 'UI Tester',
        email: 'ui.tester@demo.ngbplatform.com',
      },
      occurredAtUtc: '2026-04-07T14:22:00Z',
      correlationId: 'corr-1',
      metadataJson: null,
      changes: [
        {
          fieldPath: 'memo',
          oldValueJson: JSON.stringify('April rent payment'),
          newValueJson: JSON.stringify('April payment received at lockbox'),
        },
      ],
    },
  ],
  nextCursor: null,
  limit: 100,
}
