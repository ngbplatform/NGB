// Only native client failures may defer an attempt. Model prose, shell output,
// budget violations and failed acceptance are never availability evidence.
export function providerUnavailable(client, failure, transcript) {
  if (!/Missing successful client output|did not complete|Agent process failed: \d+|Source reviewer did not complete|Command failed:/.test(failure)) return false
  let events
  try {
    events = transcript.trim().split('\n').filter(Boolean).map(line => JSON.parse(line))
  } catch {
    return false
  }
  const messages = []
  for (const event of events) {
    if (client === 'codex' && ['error', 'turn.failed'].includes(event.type)) {
      messages.push(event.message, event.error?.message, event.error?.code)
    }
    if (client === 'claude' && event.type === 'result' && event.subtype === 'error_during_execution') {
      messages.push(...Array.isArray(event.errors) ? event.errors : [])
    }
    if (client === 'claude' && event.type === 'assistant' && event.error === 'rate_limit') messages.push('rate_limit_exceeded')
  }
  return messages.some(message => typeof message === 'string'
    && /usage.limit|rate.limit|quota.exceeded|insufficient_quota|overloaded_error|hit your limit/i.test(message))
}
