export type YearMonthParts = {
  year: number
  month: number
}

const YEAR_MONTH_RE = /^(\d{4})-(\d{2})$/
const DATE_ONLY_RE = /^(\d{4})-(\d{2})-(\d{2})$/

export function parseMonthValue(value: string | null | undefined): YearMonthParts | null {
  const normalized = String(value ?? '').trim()
  const match = YEAR_MONTH_RE.exec(normalized)
  if (!match) return null

  const year = Number(match[1])
  const month = Number(match[2])
  if (!Number.isInteger(year) || month < 1 || month > 12) return null

  return { year, month }
}

export function normalizeMonthValue(value: unknown): string | null {
  const parsed = parseMonthValue(String(value ?? '').trim())
  return parsed ? toMonthValue(parsed.year, parsed.month) : null
}

export function toMonthValue(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`
}

export function currentMonthValue(now = new Date()): string {
  return toMonthValue(now.getFullYear(), now.getMonth() + 1)
}

export function monthValueYear(value: string | null | undefined): number | null {
  return parseMonthValue(value)?.year ?? null
}

export function shiftMonthValue(value: string | null | undefined, offset: number): string | null {
  const parsed = parseMonthValue(value)
  if (!parsed || !Number.isFinite(offset)) return null

  const candidate = new Date(parsed.year, parsed.month - 1 + Math.trunc(offset), 1)
  return toMonthValue(candidate.getFullYear(), candidate.getMonth() + 1)
}

export function relativeMonthValue(offset = 0, now = new Date()): string {
  const candidate = new Date(now.getFullYear(), now.getMonth() + Math.trunc(offset), 1)
  return toMonthValue(candidate.getFullYear(), candidate.getMonth() + 1)
}

export function monthValueToDateOnly(value: string | null | undefined): string | null {
  const normalized = normalizeMonthValue(value)
  return normalized ? `${normalized}-01` : null
}

export function dateOnlyToMonthValue(value: string | null | undefined): string | null {
  const normalized = normalizeDateOnlyValue(value)
  return normalized ? normalized.slice(0, 7) : null
}

export function formatMonthValue(
  value: string | null | undefined,
  options: Intl.DateTimeFormatOptions = { month: 'long', year: 'numeric' },
): string | null {
  const parsed = parseMonthValue(value)
  if (!parsed) return null

  return new Date(parsed.year, parsed.month - 1, 1).toLocaleString(undefined, options)
}

export function normalizeDateOnlyValue(value: unknown): string | null {
  const normalized = String(value ?? '').trim()
  const match = DATE_ONLY_RE.exec(normalized)
  if (!match) return null

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (!Number.isInteger(year) || month < 1 || month > 12 || day < 1 || day > 31) return null

  const candidate = new Date(year, month - 1, day)
  if (
    candidate.getFullYear() !== year
    || candidate.getMonth() !== month - 1
    || candidate.getDate() !== day
  ) {
    return null
  }

  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

export function parseDateOnlyValue(value: string | null | undefined): Date | null {
  const normalized = normalizeDateOnlyValue(value)
  if (!normalized) return null

  const [year, month, day] = normalized.split('-').map(Number)
  return new Date(year, month - 1, day)
}

export function toDateOnlyValue(date: Date): string {
  return [
    String(date.getFullYear()).padStart(4, '0'),
    String(date.getMonth() + 1).padStart(2, '0'),
    String(date.getDate()).padStart(2, '0'),
  ].join('-')
}
