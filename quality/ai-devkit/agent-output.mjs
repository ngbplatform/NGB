import assert from 'node:assert/strict'
import { StringDecoder } from 'node:string_decoder'

function redactText(value) {
  return value.replace(/((?:https?|socks5h?):\/\/)[^\s/@]+@/gi, '$1[REDACTED]@')
    .replace(/\b(Bearer\s+)[^\s"']+/gi, '$1[REDACTED]')
    .replace(/\b((?:Proxy-)?Authorization\s*[:=]\s*Basic\s+)[^\s"']+/gi, '$1[REDACTED]')
    .replace(/((?:password|token|secret|api[_-]?key|connectionstring|[a-z0-9_]*proxy[_-]?(?:user(?:name)?|pass(?:word)?))["']?\s*[=:]\s*["']?)[^\r\n,"']+/gi,
      '$1[REDACTED]')
}

function redactValue(value) {
  if (typeof value === 'string') return redactText(value)
  if (Array.isArray(value)) return value.map(redactValue)
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([key, entry]) => [key,
      /^(?:password|token|secret|api[_-]?key|connectionstring|[a-z0-9_]*proxy[_-]?(?:user(?:name)?|pass(?:word)?))$/i.test(key)
        ? '[REDACTED]' : redactValue(entry)]))
  }
  return value
}

function redactLine(line, complete) {
  try {
    return JSON.stringify(redactValue(JSON.parse(line)))
  } catch {
    // A cancelled record can end midway through URL credentials, before '@'.
    // Withhold its final URL fragment rather than persisting an ambiguous secret.
    const text = complete ? line : line.replace(/(?:https?|socks5h?):\/\/[^\s"']*$/gi, '[REDACTED INCOMPLETE URL]')
    return redactText(text)
  }
}

// Buffer complete UTF-8 lines before redaction. Process runners enforce their
// total byte limit before feeding this decoder, including an unterminated line.
export function createAgentOutputRedactor() {
  const decoder = new StringDecoder('utf8')
  let pending = ''
  return {
    write(chunk) {
      pending += decoder.write(chunk)
      const lines = pending.split('\n')
      pending = lines.pop()
      return lines.map(line => redactLine(line, true) + '\n').join('')
    },
    end() {
      pending += decoder.end()
      const result = pending ? redactLine(pending, false) : ''
      pending = ''
      return result
    },
  }
}

export function redactAgentOutput(value) {
  const redactor = createAgentOutputRedactor()
  return redactor.write(Buffer.from(value)) + redactor.end()
}

export function codexFinalAnswer(events) {
  const answer = events.findLast(event => event.type === 'item.completed'
    && event.item?.type === 'agent_message')?.item.text
  assert.ok(typeof answer === 'string' && answer.length > 0, 'Missing final Codex answer in the redacted transcript.')
  return answer
}
