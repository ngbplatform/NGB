import { httpGet, httpPost, type HttpRequestOptions } from '../api/http';
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

export async function getMonthCloseStatus(
  period: string,
  options?: HttpRequestOptions,
): Promise<PeriodCloseStatusDto> {
  const query = { period };
  return options
    ? await httpGet<PeriodCloseStatusDto>('/api/accounting/period-closing/month', query, options)
    : await httpGet<PeriodCloseStatusDto>('/api/accounting/period-closing/month', query);
}

export async function closeMonth(request: CloseMonthRequestDto): Promise<PeriodCloseStatusDto> {
  return await httpPost<PeriodCloseStatusDto>('/api/accounting/period-closing/month/close', request);
}

export async function reopenMonth(request: ReopenMonthRequestDto): Promise<PeriodCloseStatusDto> {
  return await httpPost<PeriodCloseStatusDto>('/api/accounting/period-closing/month/reopen', request);
}

export async function getPeriodClosingCalendar(
  year: number,
  options?: HttpRequestOptions,
): Promise<PeriodClosingCalendarDto> {
  const query = { year };
  return options
    ? await httpGet<PeriodClosingCalendarDto>('/api/accounting/period-closing/calendar', query, options)
    : await httpGet<PeriodClosingCalendarDto>('/api/accounting/period-closing/calendar', query);
}

export async function getFiscalYearCloseStatus(
  fiscalYearEndPeriod: string,
  options?: HttpRequestOptions,
): Promise<FiscalYearCloseStatusDto> {
  const query = { fiscalYearEndPeriod };
  return options
    ? await httpGet<FiscalYearCloseStatusDto>('/api/accounting/period-closing/fiscal-year', query, options)
    : await httpGet<FiscalYearCloseStatusDto>('/api/accounting/period-closing/fiscal-year', query);
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
}, options?: HttpRequestOptions): Promise<RetainedEarningsAccountOptionDto[]> {
  const query = {
    q: args?.query?.trim() || undefined,
    limit: args?.limit ?? 20,
  };
  return options
    ? await httpGet<RetainedEarningsAccountOptionDto[]>('/api/accounting/period-closing/retained-earnings-accounts', query, options)
    : await httpGet<RetainedEarningsAccountOptionDto[]>('/api/accounting/period-closing/retained-earnings-accounts', query);
}
