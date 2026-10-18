import type { AssistantValue } from './types'

const isoDatePattern = /(?<![\w-])\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?(?:Z|[+-]\d{2}:\d{2}))?(?![\w:-])/g
const utcTextPattern = /\b(\d{2})\/(\d{2})\/(\d{4})(?: at |,? )(\d{2}:\d{2}:\d{2}) UTC\b/g

export function formatAssistantValue(value: AssistantValue, timeZone: string): string {
  if (value.kind === 'text') return value.value
  if (value.kind === 'date' || value.kind === 'timestamp') return formatAssistantDates(value.value, timeZone)
  if (!/^\d+$/.test(value.value)) return 'Size unavailable'
  const bytes = BigInt(value.value)
  return `${bytes.toLocaleString('en-US')} bytes`
}

/** Timestamp offsets describe instants; date-only values describe calendar dates. */
export function formatAssistantDates(text: string, timeZone: string): string {
  const formatter = new Intl.DateTimeFormat('en-US', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: true,
  })

  return text.replace(utcTextPattern, (_, month, day, year, time) => `${year}-${month}-${day}T${time}Z`)
    .replace(isoDatePattern, (value, offset, original) => {
      // Legacy/plain model output can contain dates in filenames. Treat the complete
      // filename as literal; typed display references avoid this ambiguity entirely.
      const suffix = String(original).slice(Number(offset) + value.length)
      if (/^[^\n|<>]*\.[a-zA-Z][a-zA-Z0-9]{0,7}(?:\s|$|["'`)])/u.test(suffix)) return value
      const calendar = value.slice(0, 10)
      const date = new Date(`${calendar}T00:00:00Z`)
      if (!Number.isFinite(date.getTime()) || date.toISOString().slice(0, 10) !== calendar) return value
      if (value.length === 10) return `${calendar.slice(5, 7)}/${calendar.slice(8, 10)}/${calendar.slice(0, 4)}`

      const instant = new Date(value.replace(/(\.\d{3})\d+/, '$1'))
      if (!Number.isFinite(instant.getTime())) return value
      const parts = Object.fromEntries(formatter.formatToParts(instant).map(part => [part.type, part.value]))
      return `${parts.month}/${parts.day}/${parts.year}, ${parts.hour}:${parts.minute}:${parts.second} ${parts.dayPeriod}`
    })
}
