import type {
  FiscalYearCloseStatusDto,
  PeriodClosingCalendarDto,
  PeriodCloseStatusDto,
  RetainedEarningsAccountOptionDto,
} from '../../../ngb-ui-framework/src/ngb/accounting/periodClosingTypes'
import type {
  GeneralJournalEntryDetailsDto,
  GeneralJournalEntryDocumentDto,
  GeneralJournalEntryHeaderDto,
  GeneralJournalEntryPageDto,
} from '../../../ngb-ui-framework/src/ngb/accounting/generalJournalEntryTypes'
import type {
  ChartOfAccountsAccountDto,
  ChartOfAccountsMetadataDto,
  ChartOfAccountsPageDto,
} from '../../../ngb-ui-framework/src/ngb/accounting/types'

export const retainedEarningsAccountFixture: RetainedEarningsAccountOptionDto = {
  accountId: '11111111-aaaa-4aaa-8aaa-111111111111',
  code: '3100',
  name: 'Retained Earnings',
  display: '3100 Retained Earnings',
}

export const retainedEarningsAccountsFixture: RetainedEarningsAccountOptionDto[] = [
  retainedEarningsAccountFixture,
  {
    accountId: '22222222-bbbb-4bbb-8bbb-222222222222',
    code: '3200',
    name: 'Current Earnings Clearing',
    display: '3200 Current Earnings Clearing',
  },
]

function buildMonth(period: string, overrides: Partial<PeriodCloseStatusDto> = {}): PeriodCloseStatusDto {
  return {
    period,
    state: 'Open',
    isClosed: false,
    hasActivity: false,
    closedBy: null,
    closedAtUtc: null,
    canClose: false,
    canReopen: false,
    blockingPeriod: null,
    blockingReason: null,
    ...overrides,
  }
}

export function createPeriodClosingCalendarFixture(options?: {
  aprilClosed?: boolean
}): PeriodClosingCalendarDto {
  const aprilClosed = !!options?.aprilClosed

  return {
    year: 2026,
    yearStartPeriod: '2026-01-01',
    yearEndPeriod: '2026-12-01',
    earliestActivityPeriod: '2026-01-01',
    latestContiguousClosedPeriod: aprilClosed ? '2026-04-01' : '2026-03-01',
    latestClosedPeriod: aprilClosed ? '2026-04-01' : '2026-03-01',
    nextClosablePeriod: aprilClosed ? '2026-05-01' : '2026-04-01',
    canCloseAnyMonth: true,
    hasBrokenChain: false,
    firstGapPeriod: null,
    months: [
      buildMonth('2026-01-01', {
        state: 'Closed',
        isClosed: true,
        hasActivity: true,
        closedBy: 'UI Tester',
        closedAtUtc: '2026-02-02T14:10:00Z',
      }),
      buildMonth('2026-02-01', {
        state: 'Closed',
        isClosed: true,
        hasActivity: true,
        closedBy: 'UI Tester',
        closedAtUtc: '2026-03-02T14:10:00Z',
      }),
      buildMonth('2026-03-01', {
        state: 'Closed',
        isClosed: true,
        hasActivity: true,
        closedBy: 'UI Tester',
        closedAtUtc: '2026-04-02T14:10:00Z',
        canReopen: !aprilClosed,
      }),
      buildMonth('2026-04-01', aprilClosed
        ? {
            state: 'Closed',
            isClosed: true,
            hasActivity: true,
            closedBy: 'UI Tester',
            closedAtUtc: '2026-05-02T14:10:00Z',
            canReopen: true,
          }
        : {
            state: 'ReadyToClose',
            hasActivity: true,
            canClose: true,
          }),
      buildMonth('2026-05-01', aprilClosed
        ? {
            state: 'ReadyToClose',
            hasActivity: true,
            canClose: true,
          }
        : {
            state: 'BlockedByEarlierOpenMonth',
            hasActivity: true,
            blockingPeriod: '2026-04-01',
            blockingReason: 'EarlierOpenMonth',
          }),
      buildMonth('2026-06-01'),
      buildMonth('2026-07-01'),
      buildMonth('2026-08-01'),
      buildMonth('2026-09-01'),
      buildMonth('2026-10-01'),
      buildMonth('2026-11-01'),
      buildMonth('2026-12-01'),
    ],
  }
}

export function createFiscalYearCloseStatusFixture(): FiscalYearCloseStatusDto {
  return {
    fiscalYearEndPeriod: '2026-03-01',
    fiscalYearStartPeriod: '2026-01-01',
    state: 'Completed',
    documentId: '33333333-cccc-4ccc-8ccc-333333333333',
    startedAtUtc: '2026-04-03T12:00:00Z',
    completedAtUtc: '2026-04-03T12:01:32Z',
    endPeriodClosed: true,
    endPeriodClosedBy: 'UI Tester',
    endPeriodClosedAtUtc: '2026-04-02T14:10:00Z',
    canClose: false,
    canReopen: true,
    reopenWillOpenEndPeriod: true,
    closedRetainedEarningsAccount: retainedEarningsAccountFixture,
    blockingPeriod: null,
    blockingReason: null,
    reopenBlockingPeriod: null,
    reopenBlockingReason: null,
    priorMonths: [
      buildMonth('2026-01-01', {
        state: 'Closed',
        isClosed: true,
        hasActivity: true,
        closedBy: 'UI Tester',
        closedAtUtc: '2026-02-02T14:10:00Z',
      }),
      buildMonth('2026-02-01', {
        state: 'Closed',
        isClosed: true,
        hasActivity: true,
        closedBy: 'UI Tester',
        closedAtUtc: '2026-03-02T14:10:00Z',
      }),
    ],
  }
}

const baseDocument: GeneralJournalEntryDocumentDto = {
  id: '44444444-dddd-4ddd-8ddd-444444444444',
  display: 'GJE-2026-0042',
  status: 1,
  isMarkedForDeletion: false,
  number: 'GJE-2026-0042',
}

const baseHeader: GeneralJournalEntryHeaderDto = {
  journalType: 1,
  source: 1,
  approvalState: 1,
  reasonCode: null,
  memo: null,
  externalReference: null,
  autoReverse: false,
  autoReverseOnUtc: null,
  reversalOfDocumentId: null,
  initiatedBy: 'UI Tester',
  initiatedAtUtc: '2026-04-07T14:15:00Z',
  submittedBy: null,
  submittedAtUtc: null,
  approvedBy: null,
  approvedAtUtc: null,
  rejectedBy: null,
  rejectedAtUtc: null,
  rejectReason: null,
  postedBy: null,
  postedAtUtc: null,
  createdAtUtc: '2026-04-07T14:15:00Z',
  updatedAtUtc: '2026-04-07T14:16:00Z',
}

export const generalJournalEntriesPageFixture: GeneralJournalEntryPageDto = {
  items: [
    {
      id: '44444444-dddd-4ddd-8ddd-444444444444',
      dateUtc: '2026-04-07T12:00:00Z',
      number: 'GJE-2026-0042',
      display: 'GJE-2026-0042',
      documentStatus: 1,
      isMarkedForDeletion: false,
      journalType: 1,
      source: 1,
      approvalState: 1,
      reasonCode: 'ACCRUAL',
      memo: 'Quarter-end accrual shell',
      externalReference: 'Q2-CLOSE',
      autoReverse: false,
      autoReverseOnUtc: null,
      reversalOfDocumentId: null,
      postedBy: null,
      postedAtUtc: null,
    },
    {
      id: '55555555-eeee-4eee-8eee-555555555555',
      dateUtc: '2026-04-03T12:00:00Z',
      number: 'GJE-2026-0041',
      display: 'GJE-2026-0041',
      documentStatus: 2,
      isMarkedForDeletion: false,
      journalType: 3,
      source: 1,
      approvalState: 3,
      reasonCode: 'ADJUST',
      memo: 'Month-end adjustment',
      externalReference: 'APR-END',
      autoReverse: false,
      autoReverseOnUtc: null,
      reversalOfDocumentId: null,
      postedBy: 'UI Tester',
      postedAtUtc: '2026-04-03T12:01:00Z',
    },
  ],
  offset: 0,
  limit: 50,
  total: 2,
}

export const createdGeneralJournalEntryDraftFixture: GeneralJournalEntryDetailsDto = {
  document: {
    id: baseDocument.id,
    display: null,
    status: 1,
    isMarkedForDeletion: false,
    number: null,
  },
  dateUtc: '2026-04-07T12:00:00Z',
  header: {
    ...baseHeader,
    memo: null,
    externalReference: null,
  },
  lines: [],
  allocations: [],
}

export function createSavedGeneralJournalEntryDraftFixture(options?: {
  memo?: string | null
  reasonCode?: string | null
  externalReference?: string | null
}): GeneralJournalEntryDetailsDto {
  return {
    document: {
      ...baseDocument,
    },
    dateUtc: '2026-04-07T12:00:00Z',
    header: {
      ...baseHeader,
      memo: options?.memo ?? null,
      reasonCode: options?.reasonCode ?? null,
      externalReference: options?.externalReference ?? null,
      updatedAtUtc: '2026-04-07T14:18:00Z',
    },
    lines: [],
    allocations: [],
  }
}

export const chartOfAccountsMetadataFixture: ChartOfAccountsMetadataDto = {
  accountTypeOptions: [
    { value: 'Asset', label: 'Asset' },
    { value: 'Liability', label: 'Liability' },
    { value: 'Equity', label: 'Equity' },
    { value: 'Revenue', label: 'Revenue' },
    { value: 'Expense', label: 'Expense' },
  ],
  cashFlowRoleOptions: [
    { value: 'Operating', label: 'Operating activity', supportsLineCode: true, requiresLineCode: false },
    { value: 'Investing', label: 'Investing activity', supportsLineCode: true, requiresLineCode: true },
    { value: 'Financing', label: 'Financing activity', supportsLineCode: true, requiresLineCode: false },
  ],
  cashFlowLineOptions: [
    { value: 'OPERATING_CASH', label: 'Operating cash', section: 'Operating', allowedRoles: ['Operating'] },
    { value: 'INVESTING_CAPEX', label: 'Capital expenditures', section: 'Investing', allowedRoles: ['Investing'] },
    { value: 'FINANCING_DEBT', label: 'Debt service', section: 'Financing', allowedRoles: ['Financing'] },
  ],
}

export const chartOfAccountsBaseItemsFixture: ChartOfAccountsAccountDto[] = [
  {
    accountId: '66666666-aaaa-4aaa-8aaa-666666666666',
    code: '1000',
    name: 'Cash',
    accountType: 'Asset',
    cashFlowRole: 'Operating',
    cashFlowLineCode: 'OPERATING_CASH',
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  },
  {
    accountId: '77777777-bbbb-4bbb-8bbb-777777777777',
    code: '2000',
    name: 'Accounts Payable',
    accountType: 'Liability',
    cashFlowRole: null,
    cashFlowLineCode: null,
    isActive: true,
    isDeleted: false,
    isMarkedForDeletion: false,
  },
]

export const createdChartOfAccountFixture: ChartOfAccountsAccountDto = {
  accountId: '88888888-cccc-4ccc-8ccc-888888888888',
  code: '1250',
  name: 'Security Deposit Clearing',
  accountType: 'Asset',
  cashFlowRole: null,
  cashFlowLineCode: null,
  isActive: true,
  isDeleted: false,
  isMarkedForDeletion: false,
}

export function createChartOfAccountsPageFixture(
  items: ChartOfAccountsAccountDto[] = chartOfAccountsBaseItemsFixture,
): ChartOfAccountsPageDto {
  return {
    items,
    offset: 0,
    limit: 50,
    total: items.length,
  }
}
