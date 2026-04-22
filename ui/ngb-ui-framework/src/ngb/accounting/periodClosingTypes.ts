export type CloseMonthRequestDto = {
  period: string;
};

export type ReopenMonthRequestDto = {
  period: string;
  reason: string;
};

export type PeriodCloseStatusDto = {
  period: string;
  state: string;
  isClosed: boolean;
  hasActivity: boolean;
  closedBy?: string | null;
  closedAtUtc?: string | null;
  canClose: boolean;
  canReopen: boolean;
  blockingPeriod?: string | null;
  blockingReason?: string | null;
};

export type PeriodClosingCalendarDto = {
  year: number;
  yearStartPeriod: string;
  yearEndPeriod: string;
  earliestActivityPeriod?: string | null;
  latestContiguousClosedPeriod?: string | null;
  latestClosedPeriod?: string | null;
  nextClosablePeriod?: string | null;
  canCloseAnyMonth: boolean;
  hasBrokenChain: boolean;
  firstGapPeriod?: string | null;
  months: PeriodCloseStatusDto[];
};

export type CloseFiscalYearRequestDto = {
  fiscalYearEndPeriod: string;
  retainedEarningsAccountId: string;
};

export type ReopenFiscalYearRequestDto = {
  fiscalYearEndPeriod: string;
  reason: string;
};

export type RetainedEarningsAccountOptionDto = {
  accountId: string;
  code: string;
  name: string;
  display: string;
};

export type FiscalYearCloseStatusDto = {
  fiscalYearEndPeriod: string;
  fiscalYearStartPeriod: string;
  state: string;
  documentId: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  endPeriodClosed: boolean;
  endPeriodClosedBy?: string | null;
  endPeriodClosedAtUtc?: string | null;
  canClose: boolean;
  canReopen: boolean;
  reopenWillOpenEndPeriod: boolean;
  closedRetainedEarningsAccount?: RetainedEarningsAccountOptionDto | null;
  blockingPeriod?: string | null;
  blockingReason?: string | null;
  reopenBlockingPeriod?: string | null;
  reopenBlockingReason?: string | null;
  priorMonths: PeriodCloseStatusDto[];
};
