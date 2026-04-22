import {
  currentMonthValue,
  dateOnlyToMonthValue,
  formatMonthValue,
  monthValueToDateOnly,
  monthValueYear,
  normalizeMonthValue,
} from '../utils/dateValues';
import {
  normalizeMonthQueryValue,
  normalizeYearQueryValue,
} from '../router/queryParams';

type PeriodClosingQueryLike = {
  year?: unknown;
  month?: unknown;
  fy?: unknown;
};

export function currentCalendarYear(now = new Date()): number {
  return now.getFullYear();
}

export function defaultMonthValueForYear(year: number, now = new Date()): string {
  const current = currentMonthValue(now);
  return monthValueYear(current) === year ? current : `${year}-01`;
}

export function alignMonthValueToYear(
  monthValue: string | null | undefined,
  year: number,
  now = new Date(),
): string {
  const normalized = normalizeMonthValue(monthValue) ?? defaultMonthValueForYear(year, now);
  return `${year}-${normalized.slice(5, 7)}`;
}

export function resolveSelectedYear(query: PeriodClosingQueryLike, now = new Date()): number {
  const explicitYear = normalizeYearQueryValue(query.year);
  if (explicitYear) return explicitYear;

  const monthYear = monthValueYear(normalizeMonthQueryValue(query.month));
  if (monthYear) return monthYear;

  const fiscalYear = monthValueYear(normalizeMonthQueryValue(query.fy));
  if (fiscalYear) return fiscalYear;

  return currentCalendarYear(now);
}

export function resolveSelectedMonthValue(
  queryValue: unknown,
  selectedYear: number,
  now = new Date(),
): string {
  const fromQuery = normalizeMonthQueryValue(queryValue);
  if (fromQuery && monthValueYear(fromQuery) === selectedYear) return fromQuery;
  return defaultMonthValueForYear(selectedYear, now);
}

export function selectMonthValue(value: string | null | undefined): string | null {
  return normalizeMonthValue(value) ?? dateOnlyToMonthValue(value);
}

export function toPeriodDateOnly(monthValue: string): string {
  return monthValueToDateOnly(monthValue) ?? `${monthValue}-01`;
}

export function formatPeriodMonthValue(value: string | null | undefined): string {
  return formatMonthValue(value) ?? '—';
}

export function formatPeriodDateOnly(value: string | null | undefined): string {
  return formatPeriodMonthValue(dateOnlyToMonthValue(value));
}
