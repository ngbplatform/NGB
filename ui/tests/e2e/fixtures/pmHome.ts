import type { ReceivablesReconciliationReportDto } from '../../../ngb-property-management-web/src/api/types/pmContracts'
import type { DocumentDto, PageResponseDto, PeriodClosingCalendarDto, ReferenceValue } from '@ngbplatform/ui/contracts'
import { ReportRowKind, type ReportCellDto, type ReportExecutionResponseDto, type ReportSheetRowDto } from '@ngbplatform/ui/contracts'
import { PM_TEST_IDS } from '../support/routes'

function reference(id: string, display: string): ReferenceValue {
  return { id, display }
}

function postedDocument(
  id: string,
  display: string,
  fields: Record<string, unknown>,
): DocumentDto {
  return {
    id,
    display,
    payload: { fields },
    status: 'posted',
    isMarkedForDeletion: false,
  }
}

function page<T>(items: T[], total = items.length): PageResponseDto<T> {
  return {
    items,
    offset: 0,
    limit: 500,
    total,
  }
}

function cell(value: unknown, display?: string, action?: ReportCellDto['action']): ReportCellDto {
  return {
    value,
    display: display ?? String(value ?? ''),
    action: action ?? null,
  }
}

function row(rowKind: ReportRowKind, cells: ReportCellDto[]): ReportSheetRowDto {
  return {
    rowKind,
    cells,
  }
}

export const homeDashboardFixture = {
  asOfUtc: '2026-04-07',
  warnings: [],
  portfolio: {
    buildingCount: 3,
    totalUnits: 120,
    occupiedUnits: 108,
    vacantUnits: 12,
    occupancyPercent: 90,
    futureOccupiedUnits: 108,
    futureOccupancyPercent: 90,
  },
  leases: {
    expiring30Count: 2,
    upcomingMoveInCount: 1,
    upcomingMoveOutCount: 1,
    events: [
      {
        kind: 'Move-in',
        date: '2026-04-10',
        leaseId: '11111111-aaaa-4aaa-8aaa-111111111111',
        leaseDisplay: 'Lease A-101',
        propertyDisplay: 'Riverfront Flats',
      },
      {
        kind: 'Move-out',
        date: '2026-04-12',
        leaseId: PM_TEST_IDS.receivablesLeaseId,
        leaseDisplay: 'Lease B-204',
        propertyDisplay: 'Harbor Point',
      },
    ],
  },
  receivables: {
    totalOpenItemsNet: 1475,
    totalDiff: -200,
    rowCount: 2,
    mismatchRowCount: 1,
    currentMonthBilled: 2625,
    currentMonthCollected: 2000,
    mismatches: [
      {
        partyId: PM_TEST_IDS.receivablesPartyId,
        propertyId: PM_TEST_IDS.receivablesPropertyId,
        leaseId: PM_TEST_IDS.receivablesLeaseId,
        leaseDisplay: 'Lease B-204',
        propertyDisplay: 'Riverfront Flats',
        rowKind: 'Mismatch',
        diff: -200,
      },
    ],
  },
  maintenance: {
    openItemCount: 3,
    overdueCount: 1,
    items: [
      {
        requestId: 'maintenance-request-1001',
        workOrderId: null,
        queueState: 'Overdue',
        subject: 'HVAC follow-up',
        requestDisplay: 'REQ-1001',
        propertyDisplay: 'Riverfront Flats',
        requestedAtUtc: '2026-04-01',
        dueByUtc: '2026-04-05',
        agingDays: 6,
        assignedTo: 'Jamie Lee',
      },
      {
        requestId: 'maintenance-request-1002',
        workOrderId: 'maintenance-work-order-1002',
        queueState: 'WorkOrdered',
        subject: 'Lobby lighting',
        requestDisplay: 'REQ-1002',
        propertyDisplay: 'Harbor Point',
        requestedAtUtc: '2026-04-03',
        dueByUtc: '2026-04-08',
        agingDays: 4,
        assignedTo: 'Alex Carter',
      },
      {
        requestId: 'maintenance-request-1003',
        workOrderId: null,
        queueState: 'Requested',
        subject: 'Unit turnover paint',
        requestDisplay: 'REQ-1003',
        propertyDisplay: 'Oak Terrace',
        requestedAtUtc: '2026-04-07',
        dueByUtc: '2026-04-12',
        agingDays: 1,
        assignedTo: 'Unassigned',
      },
    ],
    aging: {
      days0To3: 1,
      days4To7: 2,
      days8To14: 0,
      days15Plus: 0,
    },
  },
  periods: {
    pendingCloseCount: 2,
    lastClosedPeriod: '2026-02',
    nextClosablePeriod: '2026-03',
    firstGapPeriod: '2026-03',
  },
  occupancyTrend: Array.from({ length: 12 }, (_, index) => ({
    month: new Date(Date.UTC(2025, 4 + index, 1)).toISOString().slice(0, 10),
    occupiedUnits: 108,
    vacantUnits: 12,
  })),
  collectionsTrend: [
    { month: '2025-05-01', billed: 0, collected: 0 },
    { month: '2025-06-01', billed: 0, collected: 0 },
    { month: '2025-07-01', billed: 0, collected: 0 },
    { month: '2025-08-01', billed: 0, collected: 0 },
    { month: '2025-09-01', billed: 0, collected: 0 },
    { month: '2025-10-01', billed: 0, collected: 0 },
    { month: '2025-11-01', billed: 0, collected: 0 },
    { month: '2025-12-01', billed: 0, collected: 0 },
    { month: '2026-01-01', billed: 0, collected: 0 },
    { month: '2026-02-01', billed: 0, collected: 0 },
    { month: '2026-03-01', billed: 2325, collected: 2100 },
    { month: '2026-04-01', billed: 2625, collected: 2000 },
  ],
}

export const homeLeaseDocumentsFixture = page<DocumentDto>([
  postedDocument('11111111-aaaa-4aaa-8aaa-111111111111', 'Lease A-101', {
    start_on_utc: '2026-04-10',
    end_on_utc: '2027-04-09',
    property_id: reference('prop-riverfront', 'Riverfront Flats'),
  }),
  postedDocument(PM_TEST_IDS.receivablesLeaseId, 'Lease B-204', {
    start_on_utc: '2025-05-01',
    end_on_utc: '2026-04-12',
    property_id: reference(PM_TEST_IDS.receivablesPropertyId, 'Harbor Point'),
  }),
  postedDocument('33333333-aaaa-4aaa-8aaa-333333333333', 'Lease C-310', {
    start_on_utc: '2024-08-01',
    end_on_utc: '2026-04-30',
    property_id: reference('prop-oak', 'Oak Terrace'),
  }),
], 3)

export const homeRentChargeDocumentsFixture = page<DocumentDto>([
  postedDocument('rent-2026-03', 'March Rent', {
    due_on_utc: '2026-03-01',
    amount: 2325,
  }),
  postedDocument('rent-2026-04', 'April Rent', {
    due_on_utc: '2026-04-01',
    amount: 2400,
  }),
])

export const homeReceivableChargeDocumentsFixture = page<DocumentDto>([
  postedDocument('charge-2026-04', 'Parking Charge', {
    due_on_utc: '2026-04-03',
    amount: 150,
  }),
])

export const homeLateFeeChargeDocumentsFixture = page<DocumentDto>([
  postedDocument('late-fee-2026-04', 'Late Fee', {
    due_on_utc: '2026-04-05',
    amount: 75,
  }),
])

export const homeReceivablePaymentDocumentsFixture = page<DocumentDto>([
  postedDocument('payment-2026-03', 'March Payment', {
    received_on_utc: '2026-03-15',
    amount: 2100,
  }),
  postedDocument('payment-2026-04', 'April Payment', {
    received_on_utc: '2026-04-06',
    amount: 2100,
  }),
])

export const homeReturnedPaymentDocumentsFixture = page<DocumentDto>([
  postedDocument('returned-2026-04', 'Returned Payment', {
    returned_on_utc: '2026-04-07',
    amount: 100,
  }),
])

export const homeOccupancySummaryFixture: ReportExecutionResponseDto = {
  sheet: {
    columns: [
      { code: 'total_units', title: 'Total Units', dataType: 'number' },
      { code: 'occupied_units', title: 'Occupied Units', dataType: 'number' },
      { code: 'vacant_units', title: 'Vacant Units', dataType: 'number' },
      { code: 'occupancy_percent', title: 'Occupancy %', dataType: 'number' },
    ],
    rows: [
      row(ReportRowKind.Detail, [
        cell(120, '120'),
        cell(108, '108'),
        cell(12, '12'),
        cell(90, '90%'),
      ]),
      row(ReportRowKind.Total, [
        cell(120, '120'),
        cell(108, '108'),
        cell(12, '12'),
        cell(90, '90%'),
      ]),
    ],
  },
  offset: 0,
  limit: 500,
  total: 3,
  hasMore: false,
  nextCursor: null,
}

export const homeMaintenanceQueueFixture: ReportExecutionResponseDto = {
  sheet: {
    columns: [
      { code: 'queue_state', title: 'Queue State', dataType: 'string' },
      { code: 'subject', title: 'Subject', dataType: 'string' },
      { code: 'request', title: 'Request', dataType: 'string' },
      { code: 'property', title: 'Property', dataType: 'string' },
      { code: 'requested_at_utc', title: 'Requested', dataType: 'date' },
      { code: 'due_by_utc', title: 'Due By', dataType: 'date' },
      { code: 'aging_days', title: 'Aging Days', dataType: 'number' },
      { code: 'assigned_to', title: 'Assigned To', dataType: 'string' },
    ],
    rows: [
      row(ReportRowKind.Detail, [
        cell('Overdue', 'Overdue'),
        cell('HVAC follow-up'),
        cell('REQ-1001'),
        cell('Riverfront Flats'),
        cell('2026-04-01'),
        cell('2026-04-05'),
        cell(6, '6'),
        cell('Jamie Lee'),
      ]),
      row(ReportRowKind.Detail, [
        cell('WorkOrdered', 'WorkOrdered'),
        cell('Lobby lighting'),
        cell('REQ-1002'),
        cell('Harbor Point'),
        cell('2026-04-03'),
        cell('2026-04-08'),
        cell(4, '4'),
        cell('Alex Carter'),
      ]),
      row(ReportRowKind.Detail, [
        cell('Requested', 'Requested'),
        cell('Unit turnover paint'),
        cell('REQ-1003'),
        cell('Oak Terrace'),
        cell('2026-04-07'),
        cell('2026-04-12'),
        cell(1, '1'),
        cell('Unassigned'),
      ]),
    ],
  },
  offset: 0,
  limit: 500,
  total: 3,
  hasMore: false,
  nextCursor: null,
}

export const homeMaintenanceQueueOverdueFixture: ReportExecutionResponseDto = {
  ...homeMaintenanceQueueFixture,
  total: 1,
  sheet: {
    ...homeMaintenanceQueueFixture.sheet,
    rows: homeMaintenanceQueueFixture.sheet.rows.slice(0, 1),
  },
}

export const homeReceivablesReconciliationFixture: ReceivablesReconciliationReportDto = {
  fromMonthInclusive: '2026-04-01',
  toMonthInclusive: '2026-04-01',
  mode: 'Balance',
  arAccountId: 'ar-account',
  openItemsRegisterId: 'open-items-register',
  totalArNet: 1275,
  totalOpenItemsNet: 1475,
  totalDiff: -200,
  rowCount: 2,
  mismatchRowCount: 1,
  rows: [
    {
      partyId: PM_TEST_IDS.receivablesPartyId,
      partyDisplay: 'Alex Tenant',
      propertyId: PM_TEST_IDS.receivablesPropertyId,
      propertyDisplay: 'Riverfront Flats',
      leaseId: PM_TEST_IDS.receivablesLeaseId,
      leaseDisplay: 'Lease B-204',
      arNet: 1275,
      openItemsNet: 1475,
      diff: -200,
      rowKind: 'Mismatch',
      hasDiff: true,
    },
    {
      partyId: 'party-harbor',
      partyDisplay: 'Taylor Resident',
      propertyId: 'property-harbor',
      propertyDisplay: 'Harbor Point',
      leaseId: 'lease-harbor',
      leaseDisplay: 'Lease A-101',
      arNet: 2400,
      openItemsNet: 2400,
      diff: 0,
      rowKind: 'Matched',
      hasDiff: false,
    },
  ],
}

export const homePeriodClosingCalendarFixture: PeriodClosingCalendarDto = {
  year: 2026,
  yearStartPeriod: '2026-01',
  yearEndPeriod: '2026-12',
  earliestActivityPeriod: '2026-01',
  latestContiguousClosedPeriod: '2026-02',
  latestClosedPeriod: '2026-02',
  nextClosablePeriod: '2026-03',
  canCloseAnyMonth: true,
  hasBrokenChain: true,
  firstGapPeriod: '2026-03',
  months: [
    { period: '2026-01', state: 'Closed', isClosed: true, hasActivity: true, canClose: false, canReopen: true },
    { period: '2026-02', state: 'Closed', isClosed: true, hasActivity: true, canClose: false, canReopen: true },
    { period: '2026-03', state: 'Open', isClosed: false, hasActivity: true, canClose: true, canReopen: false },
    { period: '2026-04', state: 'Open', isClosed: false, hasActivity: true, canClose: true, canReopen: false },
    { period: '2026-05', state: 'Idle', isClosed: false, hasActivity: false, canClose: false, canReopen: false },
    { period: '2026-06', state: 'Idle', isClosed: false, hasActivity: false, canClose: false, canReopen: false },
    { period: '2026-07', state: 'Idle', isClosed: false, hasActivity: false, canClose: false, canReopen: false },
    { period: '2026-08', state: 'Idle', isClosed: false, hasActivity: false, canClose: false, canReopen: false },
    { period: '2026-09', state: 'Idle', isClosed: false, hasActivity: false, canClose: false, canReopen: false },
    { period: '2026-10', state: 'Idle', isClosed: false, hasActivity: false, canClose: false, canReopen: false },
    { period: '2026-11', state: 'Idle', isClosed: false, hasActivity: false, canClose: false, canReopen: false },
    { period: '2026-12', state: 'Idle', isClosed: false, hasActivity: false, canClose: false, canReopen: false },
  ],
}
