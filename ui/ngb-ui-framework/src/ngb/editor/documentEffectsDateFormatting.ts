export function formatOccurredAtUtcValue(value: string, locales?: Intl.LocalesArgument): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  const isUtcMidnight =
    date.getUTCHours() === 0 &&
    date.getUTCMinutes() === 0 &&
    date.getUTCSeconds() === 0 &&
    date.getUTCMilliseconds() === 0

  if (isUtcMidnight) {
    return date.toLocaleDateString(locales, {
      timeZone: 'UTC',
    })
  }

  return date.toLocaleString(locales)
}
