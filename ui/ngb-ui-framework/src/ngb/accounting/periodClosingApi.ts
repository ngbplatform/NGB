import { httpGet, httpPost } from '../api/http';
import type {
  CloseFiscalYearRequestDto,
  CloseMonthRequestDto,
  FiscalYearCloseStatusDto,
  PeriodClosingCalendarDto,
  PeriodCloseStatusDto,
  ReopenFiscalYearRequestDto,
  ReopenMonthRequestDto,
  RetainedEarningsAccountOptionDto,
} from './periodClosingTypes';

export async function getMonthCloseStatus(period: string): Promise<PeriodCloseStatusDto> {
  return await httpGet<PeriodCloseStatusDto>('/api/accounting/period-closing/month', { period });
}

export async function closeMonth(request: CloseMonthRequestDto): Promise<PeriodCloseStatusDto> {
  return await httpPost<PeriodCloseStatusDto>('/api/accounting/period-closing/month/close', request);
}

export async function reopenMonth(request: ReopenMonthRequestDto): Promise<PeriodCloseStatusDto> {
  return await httpPost<PeriodCloseStatusDto>('/api/accounting/period-closing/month/reopen', request);
}

export async function getPeriodClosingCalendar(year: number): Promise<PeriodClosingCalendarDto> {
  return await httpGet<PeriodClosingCalendarDto>('/api/accounting/period-closing/calendar', { year });
}

export async function getFiscalYearCloseStatus(
  fiscalYearEndPeriod: string,
): Promise<FiscalYearCloseStatusDto> {
  return await httpGet<FiscalYearCloseStatusDto>('/api/accounting/period-closing/fiscal-year', {
    fiscalYearEndPeriod,
  });
}

export async function closeFiscalYear(
  request: CloseFiscalYearRequestDto,
): Promise<FiscalYearCloseStatusDto> {
  return await httpPost<FiscalYearCloseStatusDto>('/api/accounting/period-closing/fiscal-year/close', request);
}

export async function reopenFiscalYear(
  request: ReopenFiscalYearRequestDto,
): Promise<FiscalYearCloseStatusDto> {
  return await httpPost<FiscalYearCloseStatusDto>('/api/accounting/period-closing/fiscal-year/reopen', request);
}

export async function searchRetainedEarningsAccounts(args?: {
  query?: string | null;
  limit?: number;
}): Promise<RetainedEarningsAccountOptionDto[]> {
  return await httpGet<RetainedEarningsAccountOptionDto[]>(
    '/api/accounting/period-closing/retained-earnings-accounts',
    {
      q: args?.query?.trim() || undefined,
      limit: args?.limit ?? 20,
    },
  );
}
